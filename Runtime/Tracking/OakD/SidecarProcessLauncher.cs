using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Modules.Utility;
using VirtualMirror.Core;

namespace VirtualMirror.Tracking.OakD {

    /// <summary>Lifecycle of the launcher's relationship to the sidecar process.</summary>
    public enum SidecarLaunchState {
        /// <summary>Auto-start is switched off; the user runs the sidecar themselves.</summary>
        Disabled,
        /// <summary>Process spawned, still waiting for the supervisor to report readiness.</summary>
        Starting,
        /// <summary>Our child process is alive and streaming.</summary>
        Running,
        /// <summary>A sidecar we did NOT spawn already owns the lock port; we left it alone.</summary>
        AttachedExternal,
        /// <summary>Could not launch. LastError explains why.</summary>
        Failed,
        /// <summary>Cleanly torn down.</summary>
        Stopped
    }

    /// <summary>
    /// Starts and stops the Python tracking sidecar so the user does not have to run it by hand.
    ///
    /// This launches sidecar_supervisor.py, NOT wholebody_udp_sender.py: the supervisor is the
    /// tested watchdog (F-20B, 35/35) that restarts a dead sidecar, detects crash loops and
    /// validates the environment. Re-implementing any of that here would duplicate it.
    ///
    /// Uses <see cref="NativeProcess"/> (Modules/Utility/StartExternalProcess.cs) rather than
    /// System.Diagnostics.Process, which is stripped under IL2CPP.
    ///
    /// Orphan prevention (the failure this class exists to avoid): a stranded python holding UDP
    /// 8899 blocks the next run, and during development that happens constantly. Two mechanisms,
    /// because neither alone is sufficient:
    ///   1. A Win32 job object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE. The kernel kills the whole
    ///      tree whenever this process dies, INCLUDING a crash or Task Manager, where no managed
    ///      teardown runs at all.
    ///   2. An explicit `taskkill /F /T` on the normal path. NativeProcess.Kill() calls
    ///      TerminateProcess on the single PID, which is NOT enough here: the live tree is
    ///      supervisor -> sender -> a third process, so terminating only the supervisor strands the
    ///      actual UDP producer while it still holds the camera.
    /// </summary>
    public sealed class SidecarProcessLauncher : IDisposable {

        private const string LogTag = "[SidecarLauncher] ";

        private readonly ILogService log;
        private readonly SidecarLaunchOptions options;

        private NativeProcess process;
        private IntPtr jobHandle = IntPtr.Zero;
        private IntPtr childHandle = IntPtr.Zero;
        private readonly object stateLock = new object();
        private SidecarLaunchState state = SidecarLaunchState.Disabled;
        private string lastError = string.Empty;
        private int childPid = -1;
        private string lastStderrLine = string.Empty;
        private bool disposed;

        /// <summary>Current state. Safe to read from the main thread while stdout arrives on another.</summary>
        public SidecarLaunchState State {
            get { lock (stateLock) { return state; } }
        }

        /// <summary>Human-readable reason for the most recent failure, or empty.</summary>
        public string LastError {
            get { lock (stateLock) { return lastError; } }
        }

        /// <summary>True when this launcher owns a live child process.</summary>
        public bool IsOwnedProcessAlive {
            get {
                NativeProcess p = process;
                try {
                    return p != null && !p.HasExited;
                } catch (Exception) {
                    return false;
                }
            }
        }

        /// <summary>One line for the diagnostics HUD.</summary>
        public string StatusLine {
            get {
                switch (State) {
                    case SidecarLaunchState.Disabled:         return "Sidecar: manual (auto-start off)";
                    case SidecarLaunchState.Starting:         return "Sidecar: starting...";
                    case SidecarLaunchState.Running:          return "Sidecar: running (pid " + childPid + ")";
                    case SidecarLaunchState.AttachedExternal: return "Sidecar: external instance (not managed)";
                    case SidecarLaunchState.Failed:           return "Sidecar: FAILED - " + LastError;
                    default:                                  return "Sidecar: stopped";
                }
            }
        }

        public SidecarProcessLauncher(ILogService log, SidecarLaunchOptions options) {
            this.log = log;
            this.options = options ?? new SidecarLaunchOptions();
        }

        /// <summary>
        /// Starts the sidecar, or attaches to one that is already running.
        /// Returns true when a sidecar is expected to stream after this call - including the
        /// attached-external case, where somebody started it by hand and we must not spawn a second
        /// producer onto the same port.
        /// </summary>
        public bool Start(SidecarPathSet paths) {
            if (disposed) {
                return false;
            }
            if (!options.AutoStart) {
                SetState(SidecarLaunchState.Disabled, string.Empty);
                Info("auto-start disabled; expecting a manually started sidecar.");
                return false;
            }

            // Someone already owns the supervisor's single-instance lock -> attach, do not spawn.
            // Two producers on one UDP port interleave poses from different sessions, which reads as
            // violent jitter rather than as a configuration error, so it is worth avoiding by design.
            if (IsLockPortHeld()) {
                SetState(SidecarLaunchState.AttachedExternal, string.Empty);
                Info("a sidecar supervisor already holds lock port " + options.LockPort
                     + "; attaching to it instead of starting a second one.");
                return true;
            }

            List<string> problems = SidecarPaths.Validate(paths);
            if (!string.IsNullOrEmpty(options.DirectScript)) {
                string direct = ResolveDirectScript(paths);
                if (!System.IO.File.Exists(direct)) {
                    problems.Add("Sender script is missing: " + direct);
                }
            } else if (!string.IsNullOrEmpty(options.SupervisedSenderScript)) {
                // Validate::SenderScript only ever checks the PACKAGED sender, so a typo'd override
                // would otherwise surface as the supervisor dying on a missing file after launch.
                string supervised = ResolveSupervisedSender(paths);
                if (!System.IO.File.Exists(supervised)) {
                    problems.Add("Sender script is missing: " + supervised);
                }
            }
            if (problems.Count > 0) {
                string detail = string.Join(" | ", problems.ToArray());
                SetState(SidecarLaunchState.Failed, detail);
                // Loud on purpose: a silently absent sidecar looks identical to a camera fault.
                log?.Log(LogLevel.Error, LogTag + "cannot start sidecar. " + detail);
                return false;
            }

            try {
                return Spawn(paths);
            } catch (Exception ex) {
                SetState(SidecarLaunchState.Failed, ex.Message);
                log?.LogException(ex, LogTag + "failed to spawn sidecar process");
                return false;
            }
        }

        private bool Spawn(SidecarPathSet paths) {
            string args = BuildArguments(paths);
            NativeProcessStartInfo psi = new NativeProcessStartInfo {
                // SidecarPaths normalises to forward slashes so logs and arguments read consistently,
                // but these two go straight into CreateProcessW, which is only reliably happy with
                // native separators. Arguments keep forward slashes - Python accepts them everywhere.
                FileName = ToNativeSeparators(paths.PythonExe),
                Arguments = args,
                WorkingDirectory = ToNativeSeparators(paths.Root),
                UseShellExecute = false, // required: pipe redirection is unavailable via ShellExecuteEx
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Info("launching: " + Quote(paths.PythonExe) + " " + args);

            process = new NativeProcess { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += OnStdout;
            process.ErrorDataReceived += OnStderr;
            process.Exited += OnExited;

            // NativeProcess starts its reader threads inside Start() when the Redirect* flags are
            // set; BeginOutputReadLine/BeginErrorReadLine are no-ops kept only for API compatibility,
            // so there is deliberately nothing to call here.
            if (!process.Start()) {
                SetState(SidecarLaunchState.Failed, "NativeProcess.Start returned false");
                return false;
            }
            childPid = process.Id;

            // Assign to the kill-on-close job immediately, so that even a crash of the editor a
            // moment from now cannot leave this child behind.
            AssignToJobObject(childPid);

            SetState(SidecarLaunchState.Starting, string.Empty);
            Info((string.IsNullOrEmpty(options.DirectScript)
                  ? "sidecar supervisor started, pid "
                  : "sidecar started UNSUPERVISED (no watchdog), pid ") + childPid);
            return true;
        }

        /// <summary>
        /// Supervisor command line. Interpreter and script are passed explicitly rather than relying
        /// on the supervisor's own defaults, because those resolve relative to its notion of the
        /// project and we already know the answer.
        /// </summary>
        private string BuildArguments(SidecarPathSet paths) {
            if (!string.IsNullOrEmpty(options.DirectScript)) {
                return BuildDirectArguments(paths);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("-u ").Append(Quote(paths.SupervisorScript));
            sb.Append(" --python ").Append(Quote(paths.PythonExe));
            sb.Append(" --script ").Append(Quote(ResolveSupervisedSender(paths)));
            sb.Append(" --model ").Append(Quote(paths.ModelFile));
            sb.Append(" --host ").Append(options.Host);
            sb.Append(" --port ").Append(options.UdpPort);
            sb.Append(" --lock-port ").Append(options.LockPort);
            sb.Append(options.Portrait
                ? " --portrait --portrait-dir " + options.PortraitDirection
                : " --no-portrait");
            sb.Append(" --subpixel-bits ").Append(options.SubpixelBits);
            // F-44. Forwarded to BOTH senders by the supervisor, like --subpixel-bits and for the
            // same reason: these change the depth and the crop the sender measures from, so one
            // sender running native while the other runs half-res would make the two columns
            // disagree about the same room.
            if (!string.IsNullOrEmpty(options.MonoResolution)) {
                sb.Append(" --mono-res ").Append(options.MonoResolution);
            }
            if (!string.IsNullOrEmpty(options.RgbIspScale)) {
                sb.Append(" --rgb-isp ").Append(options.RgbIspScale);
            }
            // Which of these the supervisor actually forwards depends on the sender it was pointed
            // at - the portrait/subpixel pair for the single-person sender, --max-poses for the
            // multi-person one. That decision lives in ONE place (sidecar_supervisor.build_command)
            // rather than being duplicated here, so the two cannot drift into a combination that
            // makes Python exit on an unrecognised flag before the camera is ever opened.
            sb.Append(" --max-poses ").Append(Math.Max(1, Math.Min(8, options.MaxPoses)));
            // Unity is listening on the destination port while in Play mode. The supervisor's port
            // probe binds that port to look for a stale producer, so it reads our healthy listener
            // as "port in use" and refuses to launch. This flag exists for exactly this case.
            sb.Append(" --allow-port-listener");
            if (!string.IsNullOrEmpty(options.ExtraArguments)) {
                sb.Append(" ").Append(options.ExtraArguments);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Command line for the DIRECT path. Deliberately minimal: only the three flags every sender
        /// here accepts, plus whatever the caller named explicitly. The supervisor's portrait and
        /// subpixel flags are absent because the multi-person sender does not take them - it handles
        /// portrait internally - and passing an unrecognised flag makes Python exit before the camera
        /// is ever opened.
        /// </summary>
        private string BuildDirectArguments(SidecarPathSet paths) {
            StringBuilder sb = new StringBuilder();
            sb.Append("-u ").Append(Quote(ResolveDirectScript(paths)));
            sb.Append(" --host ").Append(options.Host);
            sb.Append(" --port ").Append(options.UdpPort);
            sb.Append(" --model ").Append(Quote(paths.ModelFile));
            if (!string.IsNullOrEmpty(options.DirectArguments)) {
                sb.Append(" ").Append(options.DirectArguments);
            }
            return sb.ToString();
        }

        /// <summary>The direct script as an absolute path: taken as-is when rooted, otherwise
        /// resolved against the sidecar root.</summary>
        public string ResolveDirectScript(SidecarPathSet paths) {
            return ResolveAgainstRoot(paths, options.DirectScript);
        }

        /// <summary>
        /// The sender the SUPERVISOR should launch. Falls back to the packaged single-person sender,
        /// so a caller that names nothing gets exactly the behaviour it always had.
        /// </summary>
        public string ResolveSupervisedSender(SidecarPathSet paths) {
            string resolved = ResolveAgainstRoot(paths, options.SupervisedSenderScript);
            return string.IsNullOrEmpty(resolved) ? paths.SenderScript : resolved;
        }

        /// <summary>A sidecar-relative script name as an absolute path; empty stays empty.</summary>
        private static string ResolveAgainstRoot(SidecarPathSet paths, string script) {
            if (string.IsNullOrEmpty(script)) {
                return string.Empty;
            }
            if (System.IO.Path.IsPathRooted(script)) {
                return script.Replace('\\', '/');
            }
            return (paths.Root.TrimEnd('/') + "/" + script).Replace('\\', '/');
        }

        /// <summary>
        /// Stops only the process this launcher spawned. Never kills python.exe by image name - the
        /// user may be running their own, and taking it down would be both surprising and untraceable.
        /// </summary>
        public void Stop() {
            NativeProcess p = process;
            process = null;
            if (p != null) {
                try {
                    p.OutputDataReceived -= OnStdout;
                    p.ErrorDataReceived -= OnStderr;
                    p.Exited -= OnExited;
                } catch (Exception) {
                    // Detaching handlers must never block teardown.
                }
                try {
                    if (!p.HasExited) {
                        Info("stopping sidecar pid " + childPid);
                        KillProcessTree(p, childPid);
                    }
                } catch (Exception ex) {
                    log?.LogException(ex, LogTag + "error while stopping sidecar");
                }
                try {
                    p.Dispose();
                } catch (Exception) {
                    // Already gone.
                }
            }
            CloseJobObject();
            childPid = -1;
            if (State != SidecarLaunchState.Failed) {
                SetState(SidecarLaunchState.Stopped, LastError);
            }
        }

        /// <summary>
        /// taskkill /T kills the whole tree, which NativeProcess.Kill() does not: it calls
        /// TerminateProcess on one PID. The supervisor is our direct child and
        /// wholebody_udp_sender.py is its grandchild, so killing only the supervisor would leave the
        /// actual UDP producer alive and still holding the camera. Measured on a live session: the
        /// tree was three processes deep.
        /// </summary>
        private void KillProcessTree(NativeProcess p, int pid) {
            if (pid > 0) {
                try {
                    NativeProcess killer = NativeProcess.Start(new NativeProcessStartInfo {
                        FileName = "taskkill.exe",
                        Arguments = "/F /T /PID " + pid,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    if (killer != null) {
                        killer.WaitForExit(8000);
                        killer.Dispose();
                    }
                } catch (Exception) {
                    // taskkill missing or refused - fall through to the direct kill below.
                }
            }
            try {
                if (!p.HasExited) {
                    p.Kill();
                }
            } catch (Exception) {
                // Race with a normal exit, or no handle; nothing left to do.
            }
            try {
                p.WaitForExit(8000);
            } catch (Exception) {
                // Best effort.
            }
        }

        /// <summary>
        /// Probes the supervisor's single-instance lock port the same way the supervisor does: by
        /// trying to bind it. A successful bind means nobody owns it, so we are clear to spawn.
        /// </summary>
        private bool IsLockPortHeld() {
            // F-36: the probe moved to SidecarSingleInstanceLock, because the lock is now something
            // that can be HELD as well as tested - SidecarBootstrap owns a sidecar without ever
            // starting a supervisor, and has to be visible to this check. Same bind test, one
            // definition, so both sides agree on what "already owned" means.
            return SidecarSingleInstanceLock.IsHeldByAnyone(options.LockPort);
        }

        // ---- supervisor output -> ILogService --------------------------------------------------

        private void OnStdout(object sender, NativeDataReceivedEventArgs e) {
            if (e == null || string.IsNullOrEmpty(e.Data)) {
                return;
            }
            // The supervisor prints its own state transitions; promote the one that means "streaming"
            // so the HUD stops saying "starting" without needing to parse its whole event stream.
            if (State == SidecarLaunchState.Starting
                && e.Data.IndexOf("RUNNING", StringComparison.Ordinal) >= 0) {
                SetState(SidecarLaunchState.Running, string.Empty);
            }
            log?.Log(LogLevel.Info, LogTag + e.Data);
        }

        private void OnStderr(object sender, NativeDataReceivedEventArgs e) {
            if (e == null || string.IsNullOrEmpty(e.Data)) {
                return;
            }
            lastStderrLine = e.Data;
            log?.Log(LogLevel.Warning, LogTag + e.Data);
        }

        private void OnExited(object sender, EventArgs e) {
            int code = -1;
            try {
                NativeProcess p = sender as NativeProcess;
                if (p != null) {
                    code = p.ExitCode;
                }
            } catch (Exception) {
                // Exit code unavailable after dispose.
            }
            // An exit during teardown is expected; only an unrequested one is a failure.
            if (State == SidecarLaunchState.Starting || State == SidecarLaunchState.Running) {
                string name = string.IsNullOrEmpty(options.DirectScript)
                    ? "sidecar supervisor"
                    : "sidecar (" + options.DirectScript + ")";
                string detail = name + " exited unexpectedly, code " + code;
                if (!string.IsNullOrEmpty(lastStderrLine)) {
                    detail += " | Last error: " + lastStderrLine;
                }
                SetState(SidecarLaunchState.Failed, detail);
                log?.Log(LogLevel.Error, LogTag + detail);
            }
        }

        // ---- Windows Job Object ----------------------------------------------------------------
        // Raw P/Invoke, which IL2CPP supports: it is the managed System.Diagnostics surface that is
        // stripped, not kernel32 interop.

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint PROCESS_TERMINATE = 0x0001;
        private const uint PROCESS_SET_QUOTA = 0x0100;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Ties the child's lifetime to ours at the kernel level. Closing the job handle - which
        /// Windows does automatically when this process dies for ANY reason - terminates every
        /// process in the job. This is the guarantee that a managed teardown path alone cannot give.
        ///
        /// NativeProcess keeps its process handle private, so the handle is re-opened from the PID
        /// with the two rights AssignProcessToJobObject requires.
        /// </summary>
        private void AssignToJobObject(int pid) {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            IntPtr infoPtr = IntPtr.Zero;
            try {
                childHandle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, false, pid);
                if (childHandle == IntPtr.Zero) {
                    Warn("OpenProcess failed for pid " + pid + " (win32 " + Marshal.GetLastWin32Error()
                         + "); falling back to managed teardown only.");
                    return;
                }
                jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (jobHandle == IntPtr.Zero) {
                    Warn("CreateJobObject failed (win32 " + Marshal.GetLastWin32Error()
                         + "); falling back to managed teardown only.");
                    CloseJobObject();
                    return;
                }
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                infoPtr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(info, infoPtr, false);

                if (!SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, infoPtr, (uint)size)) {
                    Warn("SetInformationJobObject failed (win32 " + Marshal.GetLastWin32Error() + ").");
                    CloseJobObject();
                    return;
                }
                if (!AssignProcessToJobObject(jobHandle, childHandle)) {
                    Warn("AssignProcessToJobObject failed (win32 " + Marshal.GetLastWin32Error() + ").");
                    CloseJobObject();
                    return;
                }
                Info("child assigned to kill-on-close job object.");
            } catch (Exception ex) {
                log?.LogException(ex, LogTag + "job object setup failed; managed teardown only");
                CloseJobObject();
            } finally {
                if (infoPtr != IntPtr.Zero) {
                    Marshal.FreeHGlobal(infoPtr);
                }
            }
#else
            // v1 ships Windows-only, so managed teardown is the whole story on other platforms.
#endif
        }

        private void CloseJobObject() {
            if (jobHandle != IntPtr.Zero) {
                try {
                    CloseHandle(jobHandle);
                } catch (Exception) {
                    // Handle already invalid.
                }
                jobHandle = IntPtr.Zero;
            }
            if (childHandle != IntPtr.Zero) {
                try {
                    CloseHandle(childHandle);
                } catch (Exception) {
                    // Handle already invalid.
                }
                childHandle = IntPtr.Zero;
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        private void SetState(SidecarLaunchState next, string error) {
            lock (stateLock) {
                state = next;
                lastError = error ?? string.Empty;
            }
        }

        private static string Quote(string value) {
            return "\"" + value + "\"";
        }

        /// <summary>Forward slashes to backslashes, for paths handed to Win32 directly.</summary>
        private static string ToNativeSeparators(string path) {
            return string.IsNullOrEmpty(path) ? path : path.Replace('/', '\\');
        }

        private void Info(string message) {
            log?.Log(LogLevel.Info, LogTag + message);
        }

        private void Warn(string message) {
            log?.Log(LogLevel.Warning, LogTag + message);
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            Stop();
        }
    }

    /// <summary>Everything the launcher needs that a user might reasonably want to change.</summary>
    public sealed class SidecarLaunchOptions {
        public bool AutoStart = true;
        public string Host = "127.0.0.1";
        public int UdpPort = 8899;
        /// <summary>Must match sidecar_supervisor.py's --lock-port default (8897).</summary>
        public int LockPort = 8897;
        public bool Portrait = true;
        public string PortraitDirection = "ccw";
        public int SubpixelBits = 3;

        /// <summary>
        /// F-44 WORKING VOLUME. Both OV9782 sensors are 1280x800 and the pipeline used to discard
        /// half the linear resolution on each path. `MonoResolution` 800p halves depth error at every
        /// range (mono fx 287 -> 574, so f·B doubles); `RgbIspScale` 1/1 keeps the native frame at
        /// FULL FOV and doubles the pixels on a body, which is what sets the distance at which the
        /// pose model still sees a person. Measured cost of both together: 30.0 -> 29.2 fps.
        /// The pose solve is unaffected — RTMW3D always resizes its crop to 288x384.
        /// </summary>
        public string MonoResolution = "800p";
        public string RgbIspScale = "1/1";

        public string ExtraArguments = string.Empty;

        /// <summary>
        /// Which sender the SUPERVISOR launches, relative to the sidecar root (or absolute). Empty
        /// means the packaged single-person sender, which is what this always did.
        ///
        /// Prefer this over <see cref="DirectScript"/>. Both can reach the multi-person sender; only
        /// this one keeps the watchdog, and an unsupervised sidecar has no way back from anything
        /// that ends the process.
        /// </summary>
        public string SupervisedSenderScript = string.Empty;

        /// <summary>People posed per frame, forwarded to the multi-person sender. The single-person
        /// sender has no such flag and the supervisor does not forward it there.</summary>
        public int MaxPoses = 3;

        /// <summary>
        /// F-36 — run this sender DIRECTLY instead of going through the supervisor. Empty (the
        /// default) keeps the supervised production path exactly as it was.
        ///
        /// PREFER <see cref="SupervisedSenderScript"/>. This existed because the supervisor built its
        /// child command from a fixed set of flags — `--portrait`, `--portrait-dir`,
        /// `--subpixel-bits`, `--seconds` — that only `wholebody_udp_sender.py` accepts, so the
        /// multi-person sender could not be supervised at all. That is no longer true: the supervisor
        /// now picks the command shape from the sender it is given, and both senders print the
        /// readiness markers it waits on.
        ///
        /// What the direct route costs, measured the hard way: a sidecar launched this way has NO
        /// watchdog, so anything that ends the process — a crash, a USB unplug, or the preview
        /// window's own quit key — takes tracking down for the rest of the session with nothing to
        /// bring it back. Keep this only for one-off experiments that genuinely must not restart.
        ///
        /// Everything else this class does still applies — the kill-on-close job object, the
        /// taskkill of the whole tree, path validation, the log tag. Those are what make it worth
        /// reusing rather than writing a second launcher.
        ///
        /// A path relative to the sidecar root, or absolute.
        /// </summary>
        public string DirectScript = string.Empty;

        /// <summary>Extra arguments for <see cref="DirectScript"/>, appended after host/port/model.
        /// Ignored unless DirectScript is set.</summary>
        public string DirectArguments = string.Empty;
        /// <summary>Overrides the model location; empty means use the packaged path.</summary>
        public string ModelPathOverride = string.Empty;
    }
}
