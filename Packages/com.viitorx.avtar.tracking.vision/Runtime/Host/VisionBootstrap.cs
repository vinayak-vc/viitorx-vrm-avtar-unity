using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Everything a project needs to use the hands, face and cutout streams, in ONE component: put it on a GameObject (once) and every
    /// <see cref="VisionHostClient"/> in the scenes finds the host through <see cref="VisionHostRegistry"/>. It owns the <see cref="VisionSettings"/>
    /// (the Inspector shows all of them: engine, camera source, each stream), creates the <see cref="VisionHost"/> (sockets, engine process, message
    /// hand-over), ticks it every frame, adds the F6 control panel, and disposes everything with the GameObject. Nothing else is required: no
    /// AppBootstrap, no avatar, no log service.
    ///
    /// The host starts the engine for the streams a scene asks for (mode Auto) or always (mode Always); see the sidecar package for where the engine
    /// is looked for. A second VisionBootstrap in another scene is refused and removed, so the one that exists keeps the ports.
    /// </summary>
    [AddComponentMenu("Vision/Vision Bootstrap")]
    [DefaultExecutionOrder(-100)]
    public sealed class VisionBootstrap : MonoBehaviour {
        private static VisionBootstrap instance;

        [Tooltip("Every setting of the hands, face and cutout senders and receivers. The F6 panel edits the same object while the game runs (not saved).")]
        [SerializeField] private VisionSettings settings = new VisionSettings();
        [Tooltip("Keep this object (and the host with its ports and engine process) when another scene is loaded. Only applies to a root object.")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        private VisionHost host;
        private VisionControlPanel panel;

        /// <summary>The running bootstrap, or null.</summary>
        public static VisionBootstrap Instance {
            get {
                return instance;
            }
        }

        /// <summary>The host. Null until Awake has run, and after this component is destroyed.</summary>
        public VisionHost Host {
            get {
                return host;
            }
        }

        public VisionSettings Settings {
            get {
                return settings;
            }
        }

        private void Awake() {
            if (instance != null && instance != this) {
                Debug.LogWarning("[Viitorx] A VisionBootstrap already exists (" + instance.name + "); this one on '" + name + "' was removed so the first keeps the UDP ports.");
                Destroy(this);
                return;
            }
            instance = this;
            if (settings == null) {
                settings = new VisionSettings();
            }
            if (dontDestroyOnLoad && transform.parent == null) {
                DontDestroyOnLoad(gameObject);
            }
            UnityDebugLogService log = new UnityDebugLogService();
            host = new VisionHost(log, settings);
            VisionHostRegistry.Register(host);
            panel = gameObject.AddComponent<VisionControlPanel>();
            panel.Bind(host);
            log.Log(VirtualMirror.Core.LogLevel.Info, "Vision host ready (hands " + settings.HandsMode + " on UDP " + settings.HandsPort + ", face " + settings.FaceMode
                    + " on UDP " + settings.FacePort + ", cutout " + settings.CutoutMode + " on UDP " + settings.CutoutPort + "; control panel: " + settings.PanelKey + ").");
        }

        private void Update() {
            if (host != null) {
                host.Tick(Time.unscaledDeltaTime);
            }
        }

        private void OnDestroy() {
            if (instance != this) {
                return;
            }
            instance = null;
            if (host != null) {
                VisionHostRegistry.Unregister(host);
                host.Dispose();
                host = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForFastEnterPlayMode() {
            instance = null;
        }
    }
}
