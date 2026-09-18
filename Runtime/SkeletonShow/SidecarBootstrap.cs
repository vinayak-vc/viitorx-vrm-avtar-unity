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
    /// IT RUNS THE MULTI-PERSON SENDER, SUPERVISED. `multiperson_udp_sender.py` is backward
    /// compatible — it publishes the most-established person in the ordinary single-person shape as
    /// well — so ONE sender serves the single-person scenes, the crowd scenes and the avatar app
    /// alike.
    ///
    /// THIS USED TO LAUNCH IT DIRECTLY, and that was a mistake worth remembering. The reason was
    /// real: `sidecar_supervisor.py` built its child command from flags only
    /// `wholebody_udp_sender.py` accepts (`--portrait`, `--portrait-dir`, `--subpixel-bits`,
    /// `--seconds`), so it could not launch the multi-person sender at all. The direct route got the
    /// stream at the price of the watchdog — and a sidecar with no watchdog stays dead after
    /// ANYTHING that ends the process, including its own preview window's quit key. The supervisor
    /// now picks its command shape from the sender it is given and both senders print the readiness
    /// markers it waits on, so there is no longer a trade to make.
    ///
    /// IT NEVER STARTS A SECOND PRODUCER, in both directions. Before spawning anything it LISTENS on
    /// the destination port for a moment: if datagrams are already arriving, somebody is producing — a
    /// sidecar started by hand, or another Unity instance — and it attaches instead. In the other
    /// direction the SUPERVISOR's own single-instance lock does the work: it binds the lock port for
    /// as long as it lives, so launching the avatar app from the menu's PRODUCTION column makes
    /// `AppBootstrap` see an owner and attach rather than start a second producer on UDP 8899. This
    /// class used to hold that lock by hand precisely because nothing else did; taking it here now
    /// would make the supervisor abort on its own mutex.
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
        /// External status provider (e.g. from AppBootstrap when booting via Bootstrap.unity).
        /// </summary>
        public static System.Func<string> ExternalStatusProvider;

        /// <summary>
        /// One line about the sidecar, for the launcher's footer. Null when no boot scene ran — the
        /// launcher then says nothing rather than claiming a sidecar it knows nothing about.
        /// </summary>
        public static string Status {
            get {
                if (ExternalStatusProvider != null) {
                    string external = ExternalStatusProvider();
                    if (!string.IsNullOrEmpty(external)) {
                        return external;
                    }
                }
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
                // The supervisor's single-instance lock, read here and HELD below. Read, so that a
                // supervised sidecar already running means we attach instead of adding a second
                // producer; held, so that the avatar app defers to us for the same reason.
                LockPort = SidecarSingleInstanceLock.DefaultPort,
                ModelPathOverride = modelPathOverride,
                // SUPERVISED, like the avatar app. This used to launch the sender directly, because
                // the supervisor could only build a command for the single-person one - which also
                // meant no watchdog, and no single-instance lock to speak of, which is why this
                // class used to take one BY HAND below. The supervisor now handles both senders, so
                // it binds its own lock port and restarts its own child, and the hand-held lock is
                // gone: taking it here would make the supervisor abort on its own mutex.
                SupervisedSenderScript = senderScript,
                MaxPoses = maxPoses,
                ExtraArguments = showPreview ? "--show" : string.Empty
            };
            launcher = new SidecarProcessLauncher(log, options);
            launcher.Start(SidecarLocator.Resolve(modelPathOverride));
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
