// Vendored copy of NativeProcess / NativeProcessStartInfo / NativeDataReceivedEventArgs from Assets/Modules/Utility/StartExternalProcess.cs (ViitorCloud's own module),
// so that this package does not depend on a module that exists in one project only. Changes against the original: the namespace, and LogWarning (the only
// helper it took from Modules.Utility.Utility) is a private wrapper over UnityEngine.Debug.LogWarning. The original is untouched.
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using UnityEngine;
using UnityEngine.Scripting;

namespace VirtualMirror.Sidecar.Native {

    internal static class NativeLog {
        internal static void LogWarning(string message) {
            Debug.LogWarning(message);
        }
    }

    // =========================================================================
    // Mirrors System.Diagnostics.ProcessStartInfo
    // =========================================================================
    [Preserve]
    public class NativeProcessStartInfo {
        public string FileName = "";
        public string Arguments = "";
        public string WorkingDirectory = "";
        public bool CreateNoWindow = false;
        /// <summary>
        /// When true, launches via ShellExecuteEx (shell32) instead of CreateProcessW (kernel32).
        /// Required for protected system executables like osk.exe that need UAC elevation.
        /// Pipe redirection is not available when UseShellExecute is true.
        /// </summary>
        public bool UseShellExecute = false;
        public bool RedirectStandardOutput = false;
        public bool RedirectStandardError = false;
        public bool RedirectStandardInput = false;
    }

    // =========================================================================
    // Mirrors System.Diagnostics.DataReceivedEventArgs
    // =========================================================================
    [Preserve]
    public class NativeDataReceivedEventArgs : EventArgs {
        /// <summary>null signals end-of-stream.</summary>
        public string Data {
            get;
        }
        internal NativeDataReceivedEventArgs(string data) {
            Data = data;
        }
    }

    // =========================================================================
    // IL2CPP-safe replacement for System.Diagnostics.Process
    //
    // THREADING: Exited, OutputDataReceived, ErrorDataReceived are raised on
    // background threads. Marshal to the Unity main thread before touching the
    // scene graph or UI (e.g. via a ConcurrentQueue polled in Update).
    // =========================================================================
    [Preserve]
    public class NativeProcess : IDisposable {

        // -- Public surface ---------------------------------------------------

        public NativeProcessStartInfo StartInfo { get; set; } = new NativeProcessStartInfo();

        /// <summary>OS process ID; -1 until Start() succeeds.</summary>
        public int Id { get; private set; } = -1;

        /// <summary>When true, fires Exited when the process terminates.</summary>
        public bool EnableRaisingEvents { get; set; } = true;

        public event EventHandler<NativeDataReceivedEventArgs> OutputDataReceived;

        public event EventHandler<NativeDataReceivedEventArgs> ErrorDataReceived;

        public event EventHandler Exited;

        public bool HasExited {
            get {
                if (_hasExited)
                    return true;
                if (Id < 0)
                    return false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                if (!_watcherRunning)
                    PollExitStatusDirect();
#endif
                return _hasExited;
            }
        }

        public int ExitCode {
            get {
                if (!_hasExited)
                    throw new InvalidOperationException("Process has not exited yet.");
                return _exitCode;
            }
        }

        // -- Private state ----------------------------------------------------

        private bool _hasExited;
        private int _exitCode = -1;
        private bool _disposed;
        private bool _watcherRunning;
        private readonly object _exitLock = new object();

        private Thread _exitWatcherThread;
        private Thread _stdoutReaderThread;
        private Thread _stderrReaderThread;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private IntPtr _hProcess = IntPtr.Zero;
        private IntPtr _stdoutReadPipe = IntPtr.Zero;
        private IntPtr _stderrReadPipe = IntPtr.Zero;
        private IntPtr _stdinWritePipe = IntPtr.Zero;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private int _stdoutFd = -1;
        private int _stderrFd = -1;
        private int _stdinFd = -1;
#endif

        // =====================================================================
        // P/Invoke — Windows (kernel32.dll)
        // =====================================================================
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(
            string lpApplicationName,
            [In] string lpCommandLine,
            IntPtr procSecAttrs,
            IntPtr threadSecAttrs,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            ref PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreatePipe(
            out IntPtr hReadPipe, out IntPtr hWritePipe,
            ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer,
            uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer,
            uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetProcessId(IntPtr hProcess);

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO lpExecInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [Preserve, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [Preserve, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb;
            public string lpFile;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIconOrMonitor;
            public IntPtr hProcess;
        }

        private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;

        [Preserve, StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [Preserve, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO {
            public uint cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize;
            public uint dwXCountChars, dwYCountChars, dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [Preserve, StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }

        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint STARTF_USESTDHANDLES = 0x00000100;
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;
        private const uint PROCESS_TERMINATE = 0x0001;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SYNCHRONIZE = 0x00100000;
        private const uint INFINITE = 0xFFFFFFFF;
        private const uint WAIT_OBJECT_0 = 0x00000000;
        private const uint STILL_ACTIVE = 259;
        private const int STD_INPUT_HANDLE = -10;
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;

#endif

        // =====================================================================
        // P/Invoke — macOS (/usr/lib/libc.dylib)
        // =====================================================================
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("/usr/lib/libc.dylib")] private static extern int  fork();
        [DllImport("/usr/lib/libc.dylib")] private static extern int  execvp(string file, IntPtr argv);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  waitpid(int pid, out int status, int options);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  kill(int pid, int sig);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  pipe(int[] fds);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  dup2(int oldfd, int newfd);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  read(int fd, byte[] buf, int count);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  write(int fd, byte[] buf, int count);
        [DllImport("/usr/lib/libc.dylib")] private static extern int  close(int fd);
        [DllImport("libproc.dylib")] private static extern int proc_listallpids(int[] buffer, int buffersize);
        [DllImport("libproc.dylib")] private static extern int proc_pidpath(int pid, StringBuilder buffer, uint buffersize);

        private const int SIGTERM = 15;
        private const int SIGKILL = 9;
        private const int WNOHANG = 1;

#endif

        // =====================================================================
        // Constructors / static factories
        // =====================================================================

        public NativeProcess() {
        }

        private NativeProcess(int pid) {
            Id = pid;
        }

        /// <summary>
        /// Wraps an already-running process by PID so you can Kill/WaitForExit/Exited on it.
        /// Mirrors Process.GetProcessById(pid).
        /// </summary>
        public static NativeProcess GetProcessById(int pid) {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr h = OpenProcess(
                PROCESS_TERMINATE | PROCESS_QUERY_INFORMATION | PROCESS_SYNCHRONIZE,
                false, (uint)pid);
            if (h == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Cannot open process {pid}.");
            NativeProcess p = new NativeProcess(pid) {
                _hProcess = h
            };
            p.StartExitWatcherWindows();
            return p;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // kill(pid, 0) probes existence without sending a real signal
            if (kill(pid, 0) != 0)
                throw new Exception($"Process {pid} not found or insufficient permissions.");
            NativeProcess p = new NativeProcess(pid);
            p.StartExitWatcherMac();
            return p;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>
        /// Finds a running process by executable name (no path, extension optional)
        /// and wraps it. Mirrors Process.GetProcessesByName(name).FirstOrDefault().
        /// Throws if no matching process is found.
        /// </summary>
        public static NativeProcess GetProcessByName(string processName) {
            if (string.IsNullOrEmpty(processName))
                throw new ArgumentException("processName must not be null or empty.", nameof(processName));

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int pid = FindPidByNameWindows(processName);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            int pid = FindPidByNameMac(processName);
#else
            throw new PlatformNotSupportedException();
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (pid < 0)
                throw new Exception($"Process '{processName}' not found.");
            return GetProcessById(pid);
#endif
        }

        /// <summary>Convenience static factory. Mirrors Process.Start(startInfo).</summary>
        public static NativeProcess Start(NativeProcessStartInfo startInfo) {
            var p = new NativeProcess {
                StartInfo = startInfo
            };
            p.Start();
            return p;
        }

        // =====================================================================
        // Start
        // =====================================================================

        /// <summary>
        /// Starts the process described by StartInfo.
        /// Pipe readers and exit watcher are launched automatically.
        /// </summary>
        public bool Start() {
            if (StartInfo == null)
                throw new InvalidOperationException("StartInfo must be set before calling Start.");

            string workDir = StartInfo.WorkingDirectory;
            if (string.IsNullOrEmpty(workDir))
                workDir = Path.GetDirectoryName(StartInfo.FileName);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return StartWindows(workDir);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return StartMac(workDir);
#else
            throw new PlatformNotSupportedException("NativeProcess supports Windows and macOS only.");
#endif
        }

        // =====================================================================
        // Kill
        // =====================================================================

        /// <summary>Immediately terminates the process.</summary>
        public void Kill() {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_hProcess == IntPtr.Zero)
                throw new InvalidOperationException("No process handle. Was Start() called?");
            if (!TerminateProcess(_hProcess, 1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "TerminateProcess failed.");
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (Id < 0)
                throw new InvalidOperationException("No process ID. Was Start() called?");
            if (kill(Id, SIGTERM) != 0)
                kill(Id, SIGKILL);
#else
            throw new PlatformNotSupportedException();
#endif
        }

        // =====================================================================
        // WaitForExit
        // =====================================================================

        /// <summary>
        /// Blocks until the process exits or the timeout elapses.
        /// Pass -1 (default) to wait indefinitely.
        /// Returns true if the process has exited.
        /// </summary>
        public bool WaitForExit(int milliseconds = -1) {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_hProcess == IntPtr.Zero)
                return _hasExited;
            uint timeout = milliseconds < 0 ? INFINITE : (uint)milliseconds;
            uint result = WaitForSingleObject(_hProcess, timeout);
            if (result == WAIT_OBJECT_0) {
                PollExitStatusDirect();
                return true;
            }
            return false;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return WaitForExitMac(milliseconds);
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Blocks indefinitely until the process exits.</summary>
        public void WaitForExit() => WaitForExit(-1);

        // =====================================================================
        // BeginOutputReadLine / BeginErrorReadLine
        // Reading starts automatically inside Start() when the Redirect* flags
        // are set — these methods exist for API compatibility only.
        // =====================================================================
        public void BeginOutputReadLine() {
        }
        public void BeginErrorReadLine() {
        }

        // =====================================================================
        // WriteToStandardInput
        // =====================================================================

        /// <summary>
        /// Writes UTF-8 text to the process's stdin.
        /// RedirectStandardInput must be true in StartInfo.
        /// </summary>
        public void WriteToStandardInput(string text) {
            if (string.IsNullOrEmpty(text))
                return;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_stdinWritePipe == IntPtr.Zero)
                throw new InvalidOperationException("Standard input is not redirected.");
            WriteFile(_stdinWritePipe, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (_stdinFd < 0)
                throw new InvalidOperationException("Standard input is not redirected.");
            write(_stdinFd, bytes, bytes.Length);
#else
            throw new PlatformNotSupportedException();
#endif
        }

        // =====================================================================
        // Dispose
        // =====================================================================

        public void Dispose() {
            if (_disposed)
                return;
            _disposed = true;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_stdoutReadPipe != IntPtr.Zero) {
                CloseHandle(_stdoutReadPipe);
                _stdoutReadPipe = IntPtr.Zero;
            }
            if (_stderrReadPipe != IntPtr.Zero) {
                CloseHandle(_stderrReadPipe);
                _stderrReadPipe = IntPtr.Zero;
            }
            if (_stdinWritePipe != IntPtr.Zero) {
                CloseHandle(_stdinWritePipe);
                _stdinWritePipe = IntPtr.Zero;
            }
            if (_hProcess != IntPtr.Zero) {
                CloseHandle(_hProcess);
                _hProcess = IntPtr.Zero;
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (_stdoutFd >= 0) { close(_stdoutFd); _stdoutFd = -1; }
            if (_stderrFd >= 0) { close(_stderrFd); _stderrFd = -1; }
            if (_stdinFd  >= 0) { close(_stdinFd);  _stdinFd = -1; }
#endif
        }

        // =====================================================================
        // Windows — implementation
        // =====================================================================
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        private bool StartWindows(string workDir) {
            if (StartInfo.UseShellExecute)
                return StartShellExecuteWindows(workDir);

            var sa = new SECURITY_ATTRIBUTES {
                nLength = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                lpSecurityDescriptor = IntPtr.Zero,
                bInheritHandle = true
            };

            IntPtr stdoutWritePipe = IntPtr.Zero;
            IntPtr stderrWritePipe = IntPtr.Zero;
            IntPtr stdinReadPipe = IntPtr.Zero;

            try {
                var si = new STARTUPINFO();
                si.cb = (uint)Marshal.SizeOf(si);

                bool useHandles = StartInfo.RedirectStandardOutput
                                  || StartInfo.RedirectStandardError
                                  || StartInfo.RedirectStandardInput;

                if (useHandles) {
                    si.dwFlags |= STARTF_USESTDHANDLES;

                    if (StartInfo.RedirectStandardOutput) {
                        if (!CreatePipe(out _stdoutReadPipe, out stdoutWritePipe, ref sa, 0))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe stdout failed.");
                        SetHandleInformation(_stdoutReadPipe, HANDLE_FLAG_INHERIT, 0);
                        si.hStdOutput = stdoutWritePipe;
                    } else {
                        si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
                    }

                    if (StartInfo.RedirectStandardError) {
                        if (!CreatePipe(out _stderrReadPipe, out stderrWritePipe, ref sa, 0))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe stderr failed.");
                        SetHandleInformation(_stderrReadPipe, HANDLE_FLAG_INHERIT, 0);
                        si.hStdError = stderrWritePipe;
                    } else {
                        si.hStdError = GetStdHandle(STD_ERROR_HANDLE);
                    }

                    if (StartInfo.RedirectStandardInput) {
                        IntPtr stdinWritePipe = IntPtr.Zero;
                        if (!CreatePipe(out stdinReadPipe, out stdinWritePipe, ref sa, 0))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe stdin failed.");
                        SetHandleInformation(stdinWritePipe, HANDLE_FLAG_INHERIT, 0);
                        _stdinWritePipe = stdinWritePipe;
                        si.hStdInput = stdinReadPipe;
                    } else {
                        si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
                    }
                }

                string cmdLine = $"\"{StartInfo.FileName}\" {StartInfo.Arguments}".Trim();
                uint flags = StartInfo.CreateNoWindow ? CREATE_NO_WINDOW : 0u;

                var pi = new PROCESS_INFORMATION();
                bool ok = CreateProcessW(null, cmdLine,
                    IntPtr.Zero, IntPtr.Zero,
                    true, flags, IntPtr.Zero, workDir,
                    ref si, ref pi);

                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed.");

                Id = (int)pi.dwProcessId;
                _hProcess = pi.hProcess;
                CloseHandle(pi.hThread);

                // Close the child's write ends in the parent — child inherits them
                if (stdoutWritePipe != IntPtr.Zero) {
                    CloseHandle(stdoutWritePipe);
                    stdoutWritePipe = IntPtr.Zero;
                }
                if (stderrWritePipe != IntPtr.Zero) {
                    CloseHandle(stderrWritePipe);
                    stderrWritePipe = IntPtr.Zero;
                }
                if (stdinReadPipe != IntPtr.Zero) {
                    CloseHandle(stdinReadPipe);
                    stdinReadPipe = IntPtr.Zero;
                }

                if (_stdoutReadPipe != IntPtr.Zero)
                    StartPipeReader(_stdoutReadPipe,
                        e => OutputDataReceived?.Invoke(this, e),
                        ref _stdoutReaderThread, "stdout");

                if (_stderrReadPipe != IntPtr.Zero)
                    StartPipeReader(_stderrReadPipe,
                        e => ErrorDataReceived?.Invoke(this, e),
                        ref _stderrReaderThread, "stderr");

                StartExitWatcherWindows();
                return true;
            } catch {
                if (stdoutWritePipe != IntPtr.Zero)
                    CloseHandle(stdoutWritePipe);
                if (stderrWritePipe != IntPtr.Zero)
                    CloseHandle(stderrWritePipe);
                if (stdinReadPipe != IntPtr.Zero)
                    CloseHandle(stdinReadPipe);
                if (_stdoutReadPipe != IntPtr.Zero) {
                    CloseHandle(_stdoutReadPipe);
                    _stdoutReadPipe = IntPtr.Zero;
                }
                if (_stderrReadPipe != IntPtr.Zero) {
                    CloseHandle(_stderrReadPipe);
                    _stderrReadPipe = IntPtr.Zero;
                }
                if (_stdinWritePipe != IntPtr.Zero) {
                    CloseHandle(_stdinWritePipe);
                    _stdinWritePipe = IntPtr.Zero;
                }
                throw;
            }
        }

        private void StartPipeReader(IntPtr hPipe, Action<NativeDataReceivedEventArgs> callback, ref Thread threadRef, string label) {

            IntPtr pipe = hPipe;
            Thread t = new Thread(() => {
                byte[] buf = new byte[4096];
                StringBuilder sb = new StringBuilder();
                try {
                    while (true) {
                        bool ok = ReadFile(pipe, buf, (uint)buf.Length,
                            out uint n, IntPtr.Zero);
                        if (!ok || n == 0)
                            break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, (int)n));
                        FlushLines(sb, callback);
                    }
                    if (sb.Length > 0)
                        callback(new NativeDataReceivedEventArgs(sb.ToString()));
                    callback(new NativeDataReceivedEventArgs(null)); // EOF
                } catch (Exception ex) {
                    NativeLog.LogWarning($"[NativeProcess] Pipe reader ({label}) error: {ex.Message}");
                }
            }) {
                IsBackground = true,
                Name = $"NativeProcess.{label}Reader"
            };
            t.Start();
            threadRef = t;
        }

        private bool StartShellExecuteWindows(string workDir) {
            if (StartInfo.RedirectStandardOutput || StartInfo.RedirectStandardError || StartInfo.RedirectStandardInput)
                throw new InvalidOperationException("Pipe redirection is not supported when UseShellExecute is true.");

            SHELLEXECUTEINFO info = new SHELLEXECUTEINFO {
                cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
                fMask = SEE_MASK_NOCLOSEPROCESS,
                hwnd = IntPtr.Zero,
                lpVerb = null,
                lpFile = StartInfo.FileName,
                lpParameters = string.IsNullOrEmpty(StartInfo.Arguments) ? null : StartInfo.Arguments,
                lpDirectory = string.IsNullOrEmpty(workDir) ? null : workDir,
                nShow = StartInfo.CreateNoWindow ? SW_HIDE : SW_SHOWNORMAL,
                hProcess = IntPtr.Zero,
            };

            if (!ShellExecuteExW(ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ShellExecuteEx failed.");

            _hProcess = info.hProcess;

            if (_hProcess != IntPtr.Zero) {
                Id = (int)GetProcessId(_hProcess);
                StartExitWatcherWindows();
            } else {
                // Shell launched the process but didn't return a handle (e.g. document association).
                // Treat as fire-and-forget — Id stays -1.
                NativeLog.LogWarning("[NativeProcess] ShellExecuteEx succeeded but returned no process handle.");
            }

            return true;
        }

        private void StartExitWatcherWindows() {
            var h = _hProcess;
            _watcherRunning = true;
            var t = new Thread(() => {
                WaitForSingleObject(h, INFINITE);
                lock (_exitLock) {
                    if (!_hasExited) {
                        GetExitCodeProcess(h, out uint code);
                        _hasExited = true;
                        _exitCode = (int)code;
                    }
                }
                _watcherRunning = false;
                if (EnableRaisingEvents)
                    Exited?.Invoke(this, EventArgs.Empty);
            }) {
                IsBackground = true,
                Name = "NativeProcess.ExitWatcher"
            };
            t.Start();
            _exitWatcherThread = t;
        }

        private void PollExitStatusDirect() {
            if (_hProcess == IntPtr.Zero)
                return;
            if (!GetExitCodeProcess(_hProcess, out uint code))
                return;
            if (code == STILL_ACTIVE)
                return;
            lock (_exitLock) {
                if (_hasExited)
                    return;
                _hasExited = true;
                _exitCode = (int)code;
            }
        }

        /// <summary>Returns PID of first process whose exe name (no extension) matches, or -1.</summary>
        private static int FindPidByNameWindows(string processName) {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot failed.");

            try {
                PROCESSENTRY32W entry = new PROCESSENTRY32W {
                    dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W))
                };

                if (!Process32FirstW(snapshot, ref entry))
                    return -1;

                do {
                    string exeName = Path.GetFileNameWithoutExtension(entry.szExeFile);
                    if (string.Equals(exeName, processName, StringComparison.OrdinalIgnoreCase))
                        return (int)entry.th32ProcessID;
                } while (Process32NextW(snapshot, ref entry));

                return -1;
            } finally {
                CloseHandle(snapshot);
            }
        }

#endif

        // =====================================================================
        // macOS — implementation
        // =====================================================================
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private bool StartMac(string workDir) {
            int[] stdoutPipe = null, stderrPipe = null, stdinPipe = null;

            if (StartInfo.RedirectStandardOutput) {
                stdoutPipe = new int[2];
                if (pipe(stdoutPipe) != 0) throw new Exception("pipe() failed for stdout.");
            }
            if (StartInfo.RedirectStandardError) {
                stderrPipe = new int[2];
                if (pipe(stderrPipe) != 0) throw new Exception("pipe() failed for stderr.");
            }
            if (StartInfo.RedirectStandardInput) {
                stdinPipe = new int[2];
                if (pipe(stdinPipe) != 0) throw new Exception("pipe() failed for stdin.");
            }

            int pid = fork();

            if (pid == 0) {
                // ---- Child process ----
                if (!string.IsNullOrEmpty(workDir)) {
                    try { Directory.SetCurrentDirectory(workDir); } catch { }
                }
                if (stdoutPipe != null) { close(stdoutPipe[0]); dup2(stdoutPipe[1], 1); close(stdoutPipe[1]); }
                if (stderrPipe != null) { close(stderrPipe[0]); dup2(stderrPipe[1], 2); close(stderrPipe[1]); }
                if (stdinPipe  != null) { close(stdinPipe[1]);  dup2(stdinPipe[0],  0); close(stdinPipe[0]);  }

                IntPtr argvPtr = BuildArgvPtr(StartInfo.FileName, StartInfo.Arguments);
                execvp(StartInfo.FileName, argvPtr);
                Environment.Exit(-1); // execvp only returns on failure
            }
            else if (pid > 0) {
                // ---- Parent process ----
                Id = pid;

                if (stdoutPipe != null) { close(stdoutPipe[1]); _stdoutFd = stdoutPipe[0]; }
                if (stderrPipe != null) { close(stderrPipe[1]); _stderrFd = stderrPipe[0]; }
                if (stdinPipe  != null) { close(stdinPipe[0]);  _stdinFd = stdinPipe[1];  }

                if (_stdoutFd >= 0)
                    StartFdReader(_stdoutFd,
                        e => OutputDataReceived?.Invoke(this, e),
                        ref _stdoutReaderThread, "stdout");

                if (_stderrFd >= 0)
                    StartFdReader(_stderrFd,
                        e => ErrorDataReceived?.Invoke(this, e),
                        ref _stderrReaderThread, "stderr");

                StartExitWatcherMac();
                return true;
            }
            else {
                throw new Exception("fork() failed.");
            }

            return false;
        }

        private static IntPtr BuildArgvPtr(string exe, string args) {
            string[] parts;
            if (string.IsNullOrEmpty(args)) {
                parts = new[] { exe };
            } else {
                var split = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                parts = new string[split.Length + 1];
                parts[0] = exe;
                Array.Copy(split, 0, parts, 1, split.Length);
            }
            IntPtr ptr = Marshal.AllocHGlobal(IntPtr.Size * (parts.Length + 1));
            for (int i = 0; i < parts.Length; i++)
                Marshal.WriteIntPtr(ptr, i * IntPtr.Size, Marshal.StringToHGlobalAnsi(parts[i]));
            Marshal.WriteIntPtr(ptr, parts.Length * IntPtr.Size, IntPtr.Zero);
            return ptr;
        }

        private void StartFdReader(int fd,
            Action<NativeDataReceivedEventArgs> callback,
            ref Thread threadRef, string label) {

            int capturedFd = fd;
            var t = new Thread(() => {
                var buf = new byte[4096];
                var sb = new StringBuilder();
                try {
                    while (true) {
                        int n = read(capturedFd, buf, buf.Length);
                        if (n <= 0) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                        FlushLines(sb, callback);
                    }
                    if (sb.Length > 0)
                        callback(new NativeDataReceivedEventArgs(sb.ToString()));
                    callback(new NativeDataReceivedEventArgs(null)); // EOF
                }
                catch (Exception ex) {
                    NativeLog.LogWarning($"[NativeProcess] FD reader ({label}) error: {ex.Message}");
                }
            }) { IsBackground = true, Name = $"NativeProcess.{label}Reader" };
            t.Start();
            threadRef = t;
        }

        private void StartExitWatcherMac() {
            int pid = Id;
            _watcherRunning = true;
            var t = new Thread(() => {
                int status;
                // blocking waitpid — valid only for our own child processes
                int result = waitpid(pid, out status, 0);
                if (result == pid) {
                    lock (_exitLock) {
                        if (!_hasExited) {
                            _hasExited = true;
                            _exitCode = MacExitStatus(status);
                        }
                    }
                } else {
                    // Not our child (GetProcessById path) — poll via kill(pid,0)
                    while (kill(pid, 0) == 0) Thread.Sleep(100);
                    lock (_exitLock) {
                        if (!_hasExited) _hasExited = true;
                    }
                }
                _watcherRunning = false;
                if (EnableRaisingEvents)
                    Exited?.Invoke(this, EventArgs.Empty);
            }) { IsBackground = true, Name = "NativeProcess.ExitWatcher" };
            t.Start();
            _exitWatcherThread = t;
        }

        private void PollExitStatusDirect() {
            if (Id < 0) return;
            int status;
            int result = waitpid(Id, out status, WNOHANG);
            if (result != Id) return;
            lock (_exitLock) {
                if (_hasExited) return;
                _hasExited = true;
                _exitCode = MacExitStatus(status);
            }
        }

        private bool WaitForExitMac(int milliseconds) {
            if (Id < 0) return _hasExited;

            if (milliseconds < 0) {
                // Let the watcher thread do the definitive wait
                _exitWatcherThread?.Join();
                return _hasExited;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline) {
                if (_hasExited) return true;
                Thread.Sleep(20);
            }
            return _hasExited;
        }

        private static int MacExitStatus(int status) => (status >> 8) & 0xff;

        /// <summary>Returns PID of first process whose exe name (no extension) matches, or -1.</summary>
        private static int FindPidByNameMac(string processName) {
            int count = proc_listallpids(null, 0);
            if (count <= 0)
                return -1;

            // Pad the buffer — process count can grow between the sizing call and the fetch call.
            int[] pids = new int[count + 32];
            int actual = proc_listallpids(pids, pids.Length * sizeof(int));
            if (actual <= 0)
                return -1;

            var pathBuf = new StringBuilder(4096);
            for (int i = 0; i < actual && i < pids.Length; i++) {
                int pid = pids[i];
                if (pid <= 0)
                    continue;

                pathBuf.Clear();
                int len = proc_pidpath(pid, pathBuf, (uint)pathBuf.Capacity);
                if (len <= 0)
                    continue;

                string exeName = Path.GetFileNameWithoutExtension(pathBuf.ToString());
                if (string.Equals(exeName, processName, StringComparison.OrdinalIgnoreCase))
                    return pid;
            }

            return -1;
        }

#endif

        // =====================================================================
        // Shared helper — line-buffered flush
        // =====================================================================

        private static void FlushLines(StringBuilder sb, Action<NativeDataReceivedEventArgs> callback) {
            int start = 0;
            for (int i = 0; i < sb.Length; i++) {
                if (sb[i] != '\n')
                    continue;
                callback(new NativeDataReceivedEventArgs(
                    sb.ToString(start, i - start).TrimEnd('\r')));
                start = i + 1;
            }
            if (start > 0)
                sb.Remove(0, start);
        }
    }
}