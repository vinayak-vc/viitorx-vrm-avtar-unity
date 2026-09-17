using System.Net;
using System.Net.Sockets;

using UnityEngine;
using UnityEngine.SceneManagement;

using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-36 — START THE SIDECAR, THEN SHOW THE MENU. The one scene to press Play on.
    ///
    /// WHAT IT SOLVES. Every experience scene listens on UDP 8899 and none of them starts a producer,
    /// so the whole demonstration set needed a terminal open before it did anything. Only
    /// `Bootstrap.unity` auto-started the sidecar, and that is the avatar application — it loads a
    /// VRM, builds an IK session and binds the port itself, none of which a menu wants.
    ///
    /// SO THIS IS A SEPARATE SCENE, NOT A MODE OF `AppBootstrap`. It survives scene loads
    /// (DontDestroyOnLoad) purely so the sidecar it owns outlives every scene switch the launcher
    /// makes, and it deliberately owns NOTHING else: no camera, no UI, no avatar, and above all **no
    /// UDP socket** — because each scene binds 8899 for itself, and two binds on one port is the
    /// failure this is meant to remove rather than create.
    ///
    /// IT RUNS THE MULTI-PERSON SENDER, and that is the whole reason it does not use the supervisor.
    /// `sidecar_supervisor.py` builds its child command from flags only `wholebody_udp_sender.py`
    /// accepts (`--portrait`, `--portrait-dir`, `--subpixel-bits`, `--seconds`) and waits on a
    /// readiness contract only that sender emits, so it cannot supervise the multi-person one without
    /// changing the Python production path. `multiperson_udp_sender.py` is backward compatible — it
    /// publishes the most-established person in the ordinary single-person shape as well — so ONE
    /// sender serves the single-person scenes, the crowd scenes and the avatar app alike.
    ///
    /// THE COST, stated plainly: the direct route has NO WATCHDOG. The supervised path restarts a
    /// dead sidecar, detects crash loops and validates the environment (F-20B, 35/35); this does not.
    /// If the sidecar dies here, it stays dead until you leave play mode and press Play again. That
    /// is an acceptable trade for a demonstration launcher and an unacceptable one for the product,
    /// which is why `AppBootstrap` is untouched and still supervised.
    ///
    /// IT NEVER STARTS A SECOND PRODUCER. Before spawning anything it LISTENS on the destination port
    /// for a moment: if datagrams are already arriving, somebody is producing — a sidecar started by
    /// hand, or another Unity instance — and it attaches instead. Two producers on one port interleave
    /// poses from different sessions, which reads as violent jitter rather than as a configuration
    /// error, so it is worth the few hundred milliseconds to rule out.
    /// </summary>
    public sealed class SidecarBootstrap : MonoBehaviour {
        [Header("Sidecar")]
        [Tooltip("Start the sidecar. Turn off to use one you started yourself.")]
        [SerializeField] private bool autoStart = true;
        [Tooltip("Sender script, relative to the sidecar root. The multi-person one also publishes "
                 + "the primary person in the single-person shape, so it serves every scene.")]
        [SerializeField] private string senderScript = "multiperson_udp_sender.py";
        [Tooltip("How many people to pose. A REAL budget: 20.4 ms each, so 2 people run at ~24 fps, "
                 + "3 at ~16 and 4 at ~12.")]
        [SerializeField] private int maxPoses = 3;
        [Tooltip("UDP port. Must match every scene's udpPort and the sender's --port.")]
        [SerializeField] private int udpPort = 8899;
        [Tooltip("Open the sender's preview window with the track ids drawn on it.")]
        [SerializeField] private bool showPreview;
        [Tooltip("Absolute path to rtmw3d-x.onnx. Empty resolves it the way the app does.")]
        [SerializeField] private string modelPathOverride = string.Empty;

        [Header("Then")]
        [Tooltip("Scene to open once the sidecar has been asked to start.")]
        [SerializeField] private string nextScene = LauncherScene.SceneName;

        /// <summary>How long to listen for an existing producer before spawning one. Two pose frames
        /// at the slowest rate anything here runs at, so a live stream cannot be missed.</summary>
        private const int ProbeMilliseconds = 400;

        private SidecarProcessLauncher launcher;
        private static SidecarBootstrap instance;

        /// <summary>
        /// One line about the sidecar, for the launcher's footer. Null when no boot scene ran — the
        /// launcher then says nothing rather than claiming a sidecar it knows nothing about.
        /// </summary>
        public static string Status {
            get {
                if (instance == null) {
                    return null;
                }
                if (instance.launcher == null) {
                    return "sidecar: not started by Unity";
                }
                return "sidecar: " + instance.launcher.StatusLine;
            }
        }

        private void Awake() {
            if (instance != null && instance != this) {
                // A second boot scene would spawn a second producer. There can only be one.
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start() {
            ILogService log = new BootLog();
            if (autoStart) {
                StartSidecar(log);
            } else {
                log.Log(LogLevel.Info, "auto-start is off; expecting a sidecar started by hand.");
            }

            // The launcher opens immediately rather than waiting for the model to load, which takes
            // roughly 15 seconds. Its own status line already reports "WAITING for the sidecar on UDP
            // 8899" and then starts drawing bodies, so the wait is visible and explained where the
            // person is already looking.
            if (!string.IsNullOrEmpty(nextScene)) {
                SceneManager.LoadScene(nextScene, LoadSceneMode.Single);
            }
        }

        private void StartSidecar(ILogService log) {
            if (ProducerAlreadyRunning(udpPort)) {
                log.Log(LogLevel.Info, "something is already sending poses on UDP " + udpPort
                        + "; attaching to it instead of starting a second sidecar.");
                return;
            }

            SidecarLaunchOptions options = new SidecarLaunchOptions {
                AutoStart = true,
                Host = "127.0.0.1",
                UdpPort = udpPort,
                // The supervisor's single-instance lock. Nothing here binds it, but leaving it set
                // means that if the SUPERVISED sidecar is already up - somebody ran the production
                // path - this attaches to it rather than adding a second producer.
                LockPort = 8897,
                ModelPathOverride = modelPathOverride,
                DirectScript = senderScript,
                DirectArguments = BuildSenderArguments()
            };
            launcher = new SidecarProcessLauncher(log, options);
            launcher.Start(SidecarLocator.Resolve(modelPathOverride));
        }

        private string BuildSenderArguments() {
            string args = "--max-poses " + Mathf.Clamp(maxPoses, 1, 8);
            if (showPreview) {
                args = args + " --show";
            }
            return args;
        }

        /// <summary>
        /// Listen briefly for an existing producer. Binding the port and waiting for a datagram tests
        /// the thing that actually matters — is somebody already sending — rather than a proxy for it
        /// like a lock file, which a hand-started sender does not create.
        ///
        /// Returns false on ANY failure, including the port being unavailable: the caller then tries
        /// to spawn, and a real conflict surfaces as the sidecar's own error rather than as this
        /// silently deciding not to start.
        /// </summary>
        private static bool ProducerAlreadyRunning(int port) {
            UdpClient probe = null;
            try {
                probe = new UdpClient();
                probe.Client.SetSocketOption(SocketOptionLevel.Socket,
                                             SocketOptionName.ReuseAddress, true);
                probe.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
                probe.Client.ReceiveTimeout = ProbeMilliseconds;
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = probe.Receive(ref from);
                return data != null && data.Length > 0;
            } catch (SocketException) {
                return false;
            } catch (System.Exception) {
                return false;
            } finally {
                if (probe != null) {
                    probe.Close();
                }
            }
        }

        private void OnDestroy() {
            if (instance == this) {
                instance = null;
            }
            if (launcher != null) {
                // Takes the whole process tree with it - see SidecarProcessLauncher's note on orphan
                // prevention. A stranded python holds the camera and blocks the next run.
                launcher.Dispose();
                launcher = null;
            }
        }

        /// <summary>Minimal log, so the boot scene needs no dependency on the IO assembly. Must never
        /// throw, per the interface contract.</summary>
        private sealed class BootLog : ILogService {
            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Debug.LogError("[SidecarBoot] " + message);
                } else if (level == LogLevel.Warning) {
                    Debug.LogWarning("[SidecarBoot] " + message);
                } else {
                    Debug.Log("[SidecarBoot] " + message);
                }
            }

            public void LogException(System.Exception exception, string context) {
                Debug.LogError("[SidecarBoot] " + context + ": "
                               + (exception != null ? exception.Message : "null"));
            }

            public void Flush() {
            }
        }
    }
}
