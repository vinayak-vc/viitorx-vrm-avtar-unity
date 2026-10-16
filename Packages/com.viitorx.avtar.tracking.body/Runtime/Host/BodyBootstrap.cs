using System;
using System.IO;
using System.Text;
using System.Threading;

using UnityEngine;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Body {
    /// <summary>
    /// A person in front of a camera drives a VRM avatar, in ONE component: put it on a GameObject (once). It starts the tracking engine's <c>body</c>
    /// sender for the camera or clip in <see cref="BodySettings"/>, listens for the body wire (UDP 8899), loads the VRM with its control rig, and each
    /// frame hands the pose to a <see cref="BodyAvatarDriver"/>. Nothing else is required: no AppBootstrap, no settings store, no scenes.
    ///
    /// The engine is looked for as the sidecar package describes (the compiled engine only: the body path needs its models folder). The avatar is a
    /// <see cref="TextAsset"/> (a .vrm renamed .bytes, which also works in a player build) or a file (absolute, or relative to StreamingAssets); it is
    /// parented under the Avatar Parent, or under this object. A second BodyBootstrap is refused and removed, so the first keeps the UDP port.
    /// A webcam can be opened by one program: the body engine cannot share it with the vision package's senders.
    /// </summary>
    [AddComponentMenu("Body/Body Bootstrap")]
    [DefaultExecutionOrder(-90)]
    public sealed class BodyBootstrap : MonoBehaviour {
        private static BodyBootstrap instance;

        [Tooltip("The engine, the camera or clip, the body wire and how the avatar follows the person. Edited at run time, they apply to the next start; the engine is not restarted by an edit.")]
        [SerializeField] private BodySettings settings = new BodySettings();

        [Header("Avatar")]
        [Tooltip("The avatar as a VRM file renamed to .bytes (a TextAsset is included in a player build). Used before Avatar File.")]
        [SerializeField] private TextAsset avatarAsset;
        [Tooltip("The avatar as a VRM file: an absolute path, or one relative to StreamingAssets. Used when there is no Avatar Asset.")]
        [SerializeField] private string avatarFile = string.Empty;
        [Tooltip("The object the avatar is parented under. Empty: this object.")]
        [SerializeField] private Transform avatarParent;
        [Tooltip("Load the avatar when the component starts. Off: call LoadAvatarAsync yourself.")]
        [SerializeField] private bool loadAvatarOnStart = true;
        [Tooltip("Keep this object (the engine process, the port and the avatar) when another scene is loaded. Only applies to a root object.")]
        [SerializeField] private bool dontDestroyOnLoad = false;

        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private UnityDebugLogService log;
        private OakDUdpPoseProvider provider;
        private BodyAvatarDriver driver;
        private SidecarProcessLauncher launcher;
        private IAvatarInstance avatar;
        private string engineStatus = "engine: not started yet";
        private string avatarStatus = "avatar: not loaded";
        private bool destroyed;

        /// <summary>The running bootstrap, or null.</summary>
        public static BodyBootstrap Instance {
            get {
                return instance;
            }
        }

        public BodySettings Settings {
            get {
                return settings;
            }
        }

        /// <summary>The body wire receiver. Null until Start has run.</summary>
        public OakDUdpPoseProvider Provider {
            get {
                return provider;
            }
        }

        /// <summary>The pose-to-avatar stage. Null until Start has run.</summary>
        public BodyAvatarDriver Driver {
            get {
                return driver;
            }
        }

        /// <summary>The loaded avatar, or null.</summary>
        public IAvatarInstance Avatar {
            get {
                return avatar;
            }
        }

        /// <summary>The engine process the bootstrap started, or null (not started, not wanted, or somebody else's sender is already on the port).</summary>
        public SidecarProcessLauncher Launcher {
            get {
                return launcher;
            }
        }

        /// <summary>Several lines for a status box: the engine, the stream and the avatar.</summary>
        public string StatusText {
            get {
                StringBuilder sb = new StringBuilder();
                sb.Append(engineStatus);
                if (launcher != null) {
                    sb.Append(launcher.IsOwnedProcessAlive ? "  (process running)" : "  (process NOT running)");
                }
                sb.Append('\n');
                if (provider != null) {
                    sb.Append("stream: ").Append(provider.State).Append(", ").Append(provider.ReceivedCount).Append(" datagrams, ").Append(provider.ParseErrorCount).Append(" refused");
                } else {
                    sb.Append("stream: not listening");
                }
                sb.Append('\n').Append(avatarStatus);
                if (driver != null && driver.VolumeGate.Admission == PoseAdmission.Outside) {
                    sb.Append('\n').Append(driver.VolumeGate.Message());
                }
                return sb.ToString();
            }
        }

        private void Awake() {
            if (instance != null && instance != this) {
                Debug.LogWarning("[Viitorx] A BodyBootstrap already exists (" + instance.name + "); this one on '" + name + "' was removed so the first keeps the UDP port.");
                Destroy(this);
                return;
            }
            instance = this;
            if (settings == null) {
                settings = new BodySettings();
            }
            if (dontDestroyOnLoad && transform.parent == null) {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Start() {
            if (instance != this) {
                return;
            }
            log = new UnityDebugLogService();
            // Published before any converter is built: every PoseSpaceConverter in the process reads it at construction.
            CameraMount.TiltDegrees = settings.CameraTiltDegrees;
            PoseSpaceConverter converter = new PoseSpaceConverter(settings.Mirror, true, false, CameraMount.TiltDegrees);
            // The engine first, so its model load overlaps the rest; the provider does not depend on it being up, it waits for datagrams.
            StartEngine();
            provider = new OakDUdpPoseProvider(log, converter, settings.Port);
            provider.SetPoseInterpolation(settings.PoseInterpolationDelayMs);
            provider.StartTracking();
            if (!provider.IsRunning) {
                log.Log(LogLevel.Warning, "Body: could not listen on UDP " + settings.Port + " (is another program, or another BodyBootstrap, using it?).");
            }
            driver = new BodyAvatarDriver(log, settings, provider);
            if (loadAvatarOnStart) {
                RunConfiguredLoad();
            }
        }

        private void LateUpdate() {
            if (driver != null) {
                driver.Update(Time.deltaTime);
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            if (driver != null && Input.GetKeyDown(KeyCode.C)) {
                driver.Recalibrate();
            }
#endif
        }

        /// <summary>Take the neutral again (the person stands upright and still).</summary>
        public void Recalibrate() {
            if (driver != null) {
                driver.Recalibrate();
            }
        }

        /// <summary>
        /// Loads a VRM with its control rig under the avatar parent and drives it, replacing the avatar driven so far (which is destroyed). Returns the
        /// result; on failure the previous avatar stays.
        /// </summary>
        public async Awaitable<AvatarLoadResult> LoadAvatarAsync(AvatarLoadRequest request) {
            if (destroyed) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.Cancelled, "The BodyBootstrap was destroyed.");
            }
            if (driver == null) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.Error, "The BodyBootstrap has not started yet.");
            }
            UniVrmAvatarLoader loader = new UniVrmAvatarLoader(log, 0L);
            // A load-time decision: the Kalidokit whole-body driver needs the VRM normalized control rig.
            loader.GenerateControlRig = true;
            Transform parent = avatarParent != null ? avatarParent : transform;
            avatarStatus = "avatar: loading " + request.DisplayName + "...";
            AvatarLoadResult result = await loader.LoadAsync(request, parent, lifetime.Token);
            if (destroyed) {
                if (result.IsSuccess) {
                    result.Instance.Dispose();
                }
                return result;
            }
            if (!result.IsSuccess) {
                avatarStatus = "avatar: " + result.Status + " - " + result.Message;
                log.Log(LogLevel.Warning, "Body: the avatar did not load (" + result.Status + "): " + result.Message);
                return result;
            }
            // the retargeting works in the avatar's model frame and binds to the avatar as it stands: turn it first (the application does the same on load)
            result.Instance.Root.transform.localRotation = Quaternion.Euler(0f, settings.AvatarYawDegrees, 0f);
            IAvatarInstance previous = avatar;
            if (driver.Bind(result.Instance)) {
                avatar = result.Instance;
                avatarStatus = "avatar: " + request.DisplayName;
            } else {
                avatar = result.Instance;
                avatarStatus = "avatar: " + request.DisplayName + " loaded, but it cannot be driven (see the log)";
            }
            if (previous != null) {
                previous.Dispose();
            }
            return result;
        }

        private async void RunConfiguredLoad() {
            try {
                AvatarLoadRequest request = ConfiguredRequest();
                if (request == null) {
                    avatarStatus = "avatar: none set (Avatar Asset or Avatar File)";
                    log.Log(LogLevel.Warning, "Body: no avatar is set. Give the BodyBootstrap an Avatar Asset or an Avatar File, or call LoadAvatarAsync.");
                    return;
                }
                await LoadAvatarAsync(request);
            } catch (Exception exception) {
                log.LogException(exception, "Body: loading the avatar threw");
            }
        }

        private AvatarLoadRequest ConfiguredRequest() {
            if (avatarAsset != null) {
                return new AvatarLoadRequest(avatarAsset.bytes, avatarAsset.name, true);
            }
            if (string.IsNullOrWhiteSpace(avatarFile)) {
                return null;
            }
            string path = Path.IsPathRooted(avatarFile) ? avatarFile : Path.Combine(Application.streamingAssetsPath, avatarFile);
            return new AvatarLoadRequest(path, Path.GetFileName(path), true);
        }

        private void StartEngine() {
            if (!settings.AutoStartEngine) {
                engineStatus = "engine: not started (Auto Start Engine is off); expecting `viitorx-tracking body` on UDP " + settings.Port;
                return;
            }
            string resolved;
            string error;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!VideoSourceSpec.TryResolve(settings.Source, projectRoot, out resolved, out error)) {
                engineStatus = "SOURCE ERROR: " + error;
                log.Log(LogLevel.Error, "Body: the engine was NOT started. " + engineStatus);
                return;
            }
            if (SidecarGuards.LanCameraNeedsPin(resolved, settings.LanCameraPin)) {
                engineStatus = "SOURCE ERROR: the LAN camera needs its access PIN (Lan Camera Pin, or the CAMERA_PIN environment variable).";
                log.Log(LogLevel.Error, "Body: the engine was NOT started. " + engineStatus);
                return;
            }
            EngineLocation engine = SidecarLocator.ResolveEngine(settings.EnginePath, false, string.Empty, false, string.Empty);
            if (!engine.Found || engine.Kind != EngineKind.Executable) {
                engineStatus = "engine: not found. " + engine.Describe();
                log.Log(LogLevel.Error, "Body: the engine was NOT started. " + engine.Describe() + " Install the engine (viitorx-tracking-setup), or set its path in the settings.");
                return;
            }
            EngineCompatibilityResult compatibility = EngineCompatibility.Check(engine.ExecutablePath);
            if (compatibility.Blocks) {
                engineStatus = "engine: " + compatibility.Message;
                log.Log(LogLevel.Error, "Body: the engine was NOT started. " + compatibility.Message);
                return;
            }
            if (compatibility.Status == EngineCompatibilityStatus.Unknown) {
                log.Log(LogLevel.Warning, "Body: " + compatibility.Message);
            }
            if (SidecarGuards.ProducerAlreadyRunning(settings.Port)) {
                engineStatus = "engine: another sender is already on UDP " + settings.Port + "; using it instead of starting one";
                log.Log(LogLevel.Info, "Body: " + engineStatus + ".");
                return;
            }
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.AutoStart = true;
            options.DirectSenderOnly = true;
            options.EngineExecutable = engine.ExecutablePath;
            options.EngineCommand = BodyEngineArguments.Command;
            options.DirectArguments = BodyEngineArguments.Build(settings, resolved);
            options.VideoSource = resolved;
            options.LanCameraPin = settings.LanCameraPin;
            launcher = new SidecarProcessLauncher(log, options);
            if (launcher.Start(null)) {
                engineStatus = engine.Describe() + " (" + VideoSourceSpec.DisplayName(resolved) + ")";
            } else {
                engineStatus = "engine: did not start: " + launcher.LastError;
                log.Log(LogLevel.Error, "Body: " + engineStatus);
            }
        }

        private void OnDestroy() {
            if (instance != this) {
                return;
            }
            instance = null;
            destroyed = true;
            lifetime.Cancel();
            if (driver != null) {
                driver.Unbind();
                driver = null;
            }
            if (avatar != null) {
                avatar.Dispose();
                avatar = null;
            }
            if (provider != null) {
                provider.Dispose();
                provider = null;
            }
            if (launcher != null) {
                launcher.Dispose();
                launcher = null;
            }
            lifetime.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForFastEnterPlayMode() {
            instance = null;
        }
    }
}
