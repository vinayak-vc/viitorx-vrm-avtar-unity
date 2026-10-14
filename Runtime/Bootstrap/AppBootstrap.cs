using System;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

using UniVRM10;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Camera;
using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Diagnostics;
using VirtualMirror.IK;
using VirtualMirror.IO;
using VirtualMirror.Rendering;
using VirtualMirror.Retargeting;
using VirtualMirror.Settings;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking;
using VirtualMirror.Tracking.Filtering;
using VirtualMirror.Tracking.MediaPipe;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Tracking.Sentis;
using VirtualMirror.UI;
using VirtualMirror.Vision;

namespace VirtualMirror.App {
    /// <summary>
    /// Application composition root. Lives in Bootstrap.unity: wires the core services, loads
    /// settings, additively loads the Mirror scene, then builds the avatar session against the
    /// Mirror scene's avatar root and auto-loads the last used avatar. Survives scene loads.
    ///
    /// The serialized settings are declared in the <c>AppBootstrap.*.cs</c> partial files, one per Inspector
    /// section (<see cref="InspectorGroupAttribute"/>); this file keeps the plumbing and the behaviour.
    /// </summary>
    public sealed partial class AppBootstrap : MonoBehaviour {
        // ---- Scene & services plumbing (set once; hidden from the Inspector to keep it focused) ----
        [InspectorGroup("General", Order = 0, ExpandedByDefault = true)]
        [Tooltip("The scene loaded after boot (the Launcher). The name is historical.")]
        [SerializeField] private string mirrorSceneName = "Launcher";
        [HideInInspector] [SerializeField] private string avatarRootName = "AvatarRoot";
        [HideInInspector] [SerializeField] private bool loadMirrorSceneOnStart = true;
        [HideInInspector] [SerializeField] private float settingsSaveDebounceSeconds = 0.4f;
        [HideInInspector] [SerializeField] private bool mirrorLogToConsole = true;
        [HideInInspector] [SerializeField] private long maxAvatarFileBytes = 268435456;
        [SerializeField] private bool surviveSceneLoads = true;

        // ---- Capture / filter / retarget internals (tuned at runtime via the calibration panel; hidden) ----
        [HideInInspector] [SerializeField] private string poseModelFileName = "pose_landmarker_heavy.bytes";
        [HideInInspector] [SerializeField] private string sampleVideoPath = string.Empty;
        [HideInInspector] [SerializeField] private int cameraWidth = 1280;
        [HideInInspector] [SerializeField] private int cameraHeight = 720;
        [HideInInspector] [SerializeField] private int cameraFps = 30;
        [HideInInspector] [SerializeField] private float filterMinCutoff = 1f;
        [HideInInspector] [SerializeField] private float filterBeta = 0.2f;
        [HideInInspector] [SerializeField] private float filterDerivativeCutoff = 1f;
        [HideInInspector] [SerializeField] private float retargetMinConfidence = 0.5f;
        [HideInInspector] [SerializeField] private float retargetExitConfidence = 0.3f;
        [HideInInspector] [SerializeField] private float poseStaleSeconds = 0.5f;
        [HideInInspector] [SerializeField] private float positionSmoothing = 12f;
        [HideInInspector] [SerializeField] private bool useMediaPipeFace = true;
        [HideInInspector] [SerializeField] private string faceModelFileName = "face_landmarker.bytes";
        [HideInInspector] [SerializeField] private bool useMediaPipeHand = true;
        [HideInInspector] [SerializeField] private string handModelFileName = "hand_landmarker.bytes";

        // ---- Legacy FK/IK path (fully bypassed while Kalidokit Body is on) ----
        [HideInInspector] [SerializeField] private bool useIkDriver = false;

        // ---- Sentis RTMW3D GPU path (optional alternate provider; off by default) ----
        [HideInInspector] [SerializeField] private bool useSentis3dTracking = false;
        [HideInInspector] [SerializeField] private Unity.InferenceEngine.ModelAsset sentisModel;
        [HideInInspector] [SerializeField] private bool sentisImageNetNorm = true;
        [HideInInspector] [SerializeField] private float sentisMetreScale = 1.7f;
        [HideInInspector] [SerializeField] private float sentisDepthScale = 0.8f;
        [HideInInspector] [SerializeField] private bool sentisPersonCrop = true;
        [HideInInspector] [SerializeField] private float sentisFilterBeta = 0.6f;
        [HideInInspector] [SerializeField] private float sentisFilterMinCutoff = 1.5f;


        private ServiceRegistry services;
        private LogService logService;
        private SettingsStore settingsStore;
        private AvatarSessionController avatarSession;
        private ICameraCapture cameraCapture;
        private IBodyTrackingProvider bodyProvider;
        private SidecarProcessLauncher sidecarLauncher; // ADR-064: owns the Python sidecar process
        /// <summary>The sender the running sidecar was started with, so a scene change can tell
        /// whether it already has what it needs. Empty when we own no sidecar.</summary>
        private string activeSenderScript = string.Empty;
        // ---- F-20A stale-stream failsafe --------------------------------------------------------
        // How long the release to the neutral pose takes. Chosen so the move reads as deliberate
        // rather than as a glitch; it is a per-frame slerp factor of deltaTime / this.
        private const float StreamRestReleaseSeconds = 0.5f;
        private bool streamFailsafeActive;
        private PoseSpaceConverter converter;
        private JointFilterPipeline jointFilter;
        private HumanoidPoseRetargeter retargeter;
        private KalidokitControlRigDriver kalidokitControlRig;
        private VirtualMirror.Core.Humanize.HumanizedSkeleton humanizedSkeleton;
        private PoseFrame lastHumanizedFrame;
        private double lastHumanizeTimestamp;
        // F-48: the operating-volume verdict for the avatar path, and the per-joint depth provenance
        // the humanize layer needs to decide what the calibration may learn from.
        private readonly OperatingVolumeGate volumeGate = new OperatingVolumeGate();
        private readonly TrackingTelemetry humanizeTelemetry = new TrackingTelemetry();
        private bool hasHumanizeTelemetry;
        private bool volumeWasOutside;
        private bool volumeWasExpired;

        /// <summary>F-48 diagnostics: why the avatar is or is not following the subject. Read-only.</summary>
        public VirtualMirror.Core.Humanize.OperatingVolumeGate VolumeGate {
            get {
                return volumeGate;
            }
        }

        /// <summary>
        /// DIAG-ONLY (F-27): called once per applied frame with (raw filtered pose, humanized pose, dt).
        /// Null unless a recorder is attached, and invoked only when non-null, so the production path
        /// pays one null check. It exists so BEFORE and AFTER can be measured from the SAME input in
        /// ONE pass — running the session twice would compare two different takes and call the
        /// difference an improvement.
        /// </summary>
        public System.Action<PoseFrame, PoseFrame, float> HumanizeTap;

        /// <summary>DIAG-ONLY (F-27): the live layer, for its stats and calibration readout.</summary>
        public VirtualMirror.Core.Humanize.HumanizedSkeleton HumanizedLayer {
            get {
                return humanizedSkeleton;
            }
        }
        private PoseDebugSkeleton debugSkeleton;
        private System.IO.StreamWriter modelLog;
        private bool modelLogFailed;
        private IIkSolver ikSolver;
        private IFaceTrackingProvider faceProvider;
        private VrmExpressionRetargeter expressionRetargeter;
        private IHandTrackingProvider handProvider;
        private HumanoidHandRetargeter handRetargeter;
        private PerformanceMonitor performanceMonitor;
        private DiagnosticsHudPanel diagnosticsHud;
        private CalibrationSettingsPanel calibrationPanel;
        private Animator boundAnimator;
        private MirrorCameraController cameraController;
        private double lastFrameTimestamp;
        private float lastFreshTime;
        private double lastFilterTimestamp; // M10: timestamp of the last frame actually pushed through jointFilter
        private PoseFrame lastFilteredFrame; // M10: reused on repeat Updates so the filter runs once per NEW frame
        private Transform avatarRootTransform;
        private Vector3 avatarRootInitialPosition;
        private bool hasPositionNeutral;
        private Vector3 positionNeutralHip;
        private Vector3 positionCurrentOffset;

        public ServiceRegistry Services {
            get {
                return services;
            }
        }

        public AvatarSessionController AvatarSession {
            get {
                return avatarSession;
            }
        }

        public ICameraCapture CameraCapture {
            get {
                return cameraCapture;
            }
        }

        /// <summary>The scene that shows the avatar and owns the body-tracking session.</summary>
        private const string MirrorSceneName = "Mirror";

        private static AppBootstrap instance;

        /// <summary>True for a second AppBootstrap that destroyed itself in Awake and must do nothing meanwhile.</summary>
        private bool isDuplicate;

        public static AppBootstrap Instance {
            get {
                return instance;
            }
        }

        public SidecarProcessLauncher SidecarLauncher {
            get {
                return sidecarLauncher;
            }
        }

        private void Awake() {
            if (instance != null && instance != this) {
                isDuplicate = true;
                Destroy(gameObject);
                return;
            }
            instance = this;
            // One shared input: the vision settings exist before anything (the sidecar start included) reads them,
            // and an older scene's body-path copy is carried into them once.
            if (vision == null) {
                vision = new VisionSettings();
            }
            MigrateInputSettings();
            if (surviveSceneLoads) {
                DontDestroyOnLoad(gameObject);
            }
            // F-43: publish the mount FIRST. Every PoseSpaceConverter in the process reads
            // CameraMount at construction, and SkeletonShowBootstrap / TrackedStage build theirs
            // when their own scene loads - which is always after this Awake, but only because this
            // runs before InitializeServices and before the first scene load. Setting it any later
            // would leave a scene-launched converter silently level while this one is tilted.
            CameraMount.TiltDegrees = cameraTiltDegrees;
            // F-48: published for the same reason - every tracked consumer in the process builds its
            // own gate, and they must all read one definition of the working range.
            OperatingVolume.Enabled = useOperatingVolume;
            OperatingVolume.Configure(volumeNearMetres, volumeFarMetres, volumeHoldSeconds,
                                      volumeSettlePoses, volumePartialNearMetres);
            InitializeServices();
            InitializeVision();
        }

        private void MigrateInputSettings() {
            if (inputSettingsVersion >= InputSettingsMigration.CurrentVersion) {
                return;
            }
            bool changed = InputSettingsMigration.Apply(inputSettingsVersion, trackFromVideo, legacyVideoSource,
                    legacyLanCameraPin, legacyVideoLoop, legacyVideoCameraSize, vision);
            inputSettingsVersion = InputSettingsMigration.CurrentVersion;
            if (changed && logService != null) {
                logService.Log(LogLevel.Info, "Input settings: the body path's own video source was moved to Hands and Face > Source.");
            }
        }

#if UNITY_EDITOR
        // Runs when a scene loads in the Editor too, so the migrated values are what gets saved.
        private void OnValidate() {
            if (inputSettingsVersion >= InputSettingsMigration.CurrentVersion) {
                return;
            }
            if (vision == null) {
                vision = new VisionSettings();
            }
            MigrateInputSettings();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private void OnEnable() {
            if (isDuplicate) {
                return;
            }
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable() {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Start() {
            Scene activeScene = SceneManager.GetActiveScene();
            // sceneLoaded never fires for the scene we were already in, so the starting scene has to
            // be asked about here or booting straight into one would keep whatever InitializeServices
            // happened to start.
            EnsureSidecarForScene(activeScene.name);
            if (activeScene.name == MirrorSceneName) {
                StartTracking();
                SetupAvatarSession(activeScene);
                return;
            }

            if (loadMirrorSceneOnStart && !string.IsNullOrEmpty(mirrorSceneName)) {
                if (mirrorSceneName == activeScene.name) {
                    return;
                }
                logService.Log(LogLevel.Info, "Loading initial scene (single mode): " + mirrorSceneName);
                SceneManager.LoadScene(mirrorSceneName, LoadSceneMode.Single);
            }
        }

        private void Update() {
            TickVision();
            WatchVideoSettings(Time.unscaledDeltaTime);
            // O = "I just turned the camera". AppBootstrap survives scene loads, so this works from
            // every scene rather than needing a binding in each one. Guarded on owning a sidecar:
            // there is nothing to restart otherwise, and one we merely attached to is not ours.
            if (Input.GetKeyDown(KeyCode.O) && SidecarManaged
                && sidecarLauncher != null && sidecarLauncher.IsOwnedProcessAlive) {
                ToggleSidecarOrientation();
                return;
            }
            if (SceneManager.GetActiveScene().name == MirrorSceneName) {
                if (VirtualMirror.SkeletonShow.LauncherScene.ReturnRequested() && VirtualMirror.SkeletonShow.LauncherScene.IsAvailable) {
                    logService.Log(LogLevel.Info, "Return to Launcher requested from Mirror.");
                    StopTracking();
                    TeardownAvatarSession();
                    VirtualMirror.SkeletonShow.LauncherScene.Load();
                    return;
                }
            }
            if (settingsStore != null) {
                settingsStore.Tick(Time.unscaledDeltaTime);
            }
            if (performanceMonitor != null) {
                performanceMonitor.Tick(Time.unscaledDeltaTime); // H1: drive the FPS/frame-time HUD
            }
        }

        private void LateUpdate() {
            if (SceneManager.GetActiveScene().name != MirrorSceneName) {
                return;
            }
            UpdateTracking();
        }



        private bool servicesTornDown;
        private System.Func<string> externalStatusProvider;

        private void OnApplicationQuit() {
            TeardownServices();
        }

        // M5: also tear down on OnDestroy so an Editor domain reload / script recompile while playing
        // releases the OAK-D UDP socket + receive thread. Without it the socket leaks and the next Play
        // hits "port 8899 in use" and silently falls back off the OAK path. Idempotent (servicesTornDown).
        private void OnDestroy() {
            if (instance == this) {
                TeardownServices();
            }
        }

        private void StopTracking() {
            if (modelLog != null) {
                CloseModelLog();
            }
            if (faceProvider != null) {
                faceProvider.Dispose();
                faceProvider = null;
            }
            if (handProvider != null) {
                handProvider.Dispose();
                handProvider = null;
            }
            if (ikSolver != null) {
                ikSolver.Dispose();
                ikSolver = null;
            }
            if (bodyProvider != null) {
                bodyProvider.Dispose();
                bodyProvider = null;
            }
            if (cameraCapture != null) {
                cameraCapture.Dispose();
                cameraCapture = null;
            }
            if (debugSkeleton != null) {
                Destroy(debugSkeleton.gameObject);
                debugSkeleton = null;
            }
            boundAnimator = null;
            avatarRootTransform = null;
            hasPositionNeutral = false;
            if (logService != null) {
                logService.Log(LogLevel.Info, "Body tracking stopped.");
            }
        }

        private void TeardownServices() {
            if (servicesTornDown) {
                return;
            }
            servicesTornDown = true;
            // The provider is a static on another class: leave it pointing at a dead instance and it would keep
            // this component alive and answer for a sidecar that is gone.
            if (VirtualMirror.SkeletonShow.SidecarBootstrap.ExternalStatusProvider == externalStatusProvider) {
                VirtualMirror.SkeletonShow.SidecarBootstrap.ExternalStatusProvider = null;
            }
            // ADR-064: stop the producer first, so it is not still streaming into a socket we are
            // about to close. Disposing is safe when the sidecar was never started, and it never
            // touches a supervisor that this launcher did not spawn.
            ShutdownVision();
            StopSidecar();
            StopTracking();
            TeardownAvatarSession();
            if (settingsStore != null) {
                settingsStore.Flush();
            }
            if (logService != null) {
                logService.Log(LogLevel.Info, "Virtual Mirror shutting down.");
                logService.Flush();
                logService.Dispose();
            }
        }

        private string DescribeExternalStatus() {
            string video = string.IsNullOrEmpty(videoStatus) ? string.Empty : videoStatus + "  |  ";
            if (sidecarLauncher != null && !string.IsNullOrEmpty(sidecarLauncher.StatusLine)) {
                return video + "sidecar: " + sidecarLauncher.StatusLine;
            }
            return string.IsNullOrEmpty(video) ? null : video.TrimEnd(' ', '|');
        }

        private void InitializeServices() {
            PathProvider pathProvider = new PathProvider();
            pathProvider.EnsureDirectories();
            logService = new LogService(pathProvider, mirrorLogToConsole);
            logService.Log(LogLevel.Info, "Virtual Mirror bootstrap starting.");
            settingsStore = new SettingsStore(pathProvider, logService, settingsSaveDebounceSeconds);
            settingsStore.Load();
            UniVrmAvatarLoader avatarLoader = new UniVrmAvatarLoader(logService, maxAvatarFileBytes);
            // ADR-022: the whole-body Kalidokit path needs the VRM normalized control rig, which is a
            // load-time option — set it before the avatar auto-loads. Off → raw bones for the FK/IK path.
            avatarLoader.GenerateControlRig = useKalidokitBody;
            services = new ServiceRegistry(pathProvider, logService, settingsStore, avatarLoader);
            performanceMonitor = new PerformanceMonitor(); // H1: was never instantiated → HUD FPS stayed blank
            externalStatusProvider = DescribeExternalStatus;
            VirtualMirror.SkeletonShow.SidecarBootstrap.ExternalStatusProvider = externalStatusProvider;
            if (SidecarManaged) {
                StartSidecar();
            }
            logService.Log(LogLevel.Info, "Core services ready.");
        }

        private void StartCameraCapture() {
            if (cameraCapture != null) {
                return;
            }
            if (useVideoSource) {
                if (string.IsNullOrWhiteSpace(sampleVideoPath) || !File.Exists(sampleVideoPath)) {
                    logService.Log(LogLevel.Warning, "'Use Video Source' is on but the sample video '" + sampleVideoPath
                            + "' does not exist; the MediaPipe path will see no picture.");
                }
                cameraCapture = new VideoFileCaptureService(logService, sampleVideoPath);
            } else {
                cameraCapture = new WebcamCaptureService(logService);
            }
            if (useOakUdpTracking) {
                // LOW-A: the OAK-D path streams its own frames from the Python sidecar over UDP; the local
                // webcam (often a virtual cam that fails to open in this setup) is not needed and only logs
                // a scary "camera failed" warning. Keep the service object (so any fallback still has one)
                // but do not open a device.
                logService.Log(LogLevel.Info, "OAK-D path active; skipping local camera open.");
                return;
            }
            CameraCaptureRequest request = new CameraCaptureRequest(string.Empty, cameraWidth, cameraHeight, cameraFps);
            bool started = cameraCapture.StartCapture(request);
            if (!started) {
                logService.Log(LogLevel.Warning, "Camera capture failed to start.");
            }
        }



        private void StartTracking() {
            if (bodyProvider != null) {
                return;
            }
            StartCameraCapture();
            jointFilter = new JointFilterPipeline(filterMinCutoff, filterBeta, filterDerivativeCutoff);
            retargeter = new HumanoidPoseRetargeter();
            kalidokitControlRig = new KalidokitControlRigDriver(); // ADR-022: whole-body via normalized control rig
            kalidokitControlRig.SetLogger(logService); // P0-1: sparse limb hold/reacquire diagnostics
            // F-27: the humanized skeleton sits between filtering and retargeting. It owns no smoothing
            // and no confidence - it makes the POSE anatomically possible before anything solves from it.
            humanizedSkeleton = new VirtualMirror.Core.Humanize.HumanizedSkeleton();
            if (useIkDriver) {
                EnsureIkSolver();
            }
            // F-43: reads CameraMount rather than the field, so this converter and the ones built by
            // scene components cannot disagree. Awake published it.
            converter = new PoseSpaceConverter(poseFlipX, poseFlipY, poseFlipZ, CameraMount.TiltDegrees);
            // OAK-D via the Python sidecar over UDP (B2, ADR-016) takes top priority. No native DLL in-process
            // → cannot crash Unity. Binds a UDP socket; if the port is free it "starts" and waits for the
            // sidecar's datagrams (avatar rests until the sidecar streams). Run udp_pose_sender.py separately.
            if (useOakUdpTracking) {
                // ADR-064: start the sidecar before the provider binds, so its ~45 s model load
                // overlaps the rest of bootstrap. The provider does not depend on it being up — it
                // simply waits for datagrams, and TrackingStreamHealth + the F-20A stale/failsafe
                // machinery already handle a producer that never arrives or dies mid-session.
                StartSidecar();
                OakDUdpPoseProvider oakUdp = new OakDUdpPoseProvider(logService, converter, oakUdpPort);
                if (pipelineLogging) {
                    oakUdp.SetPipelineLog(ResolvedPipelineLogDir()); // recv_log.jsonl — must be set before StartTracking
                }
                oakUdp.SetPoseInterpolation(poseInterpolationDelayMs); // P1-3 timestamped pose buffer
                oakUdp.StartTracking();
                if (oakUdp.IsRunning) {
                    bodyProvider = oakUdp;
                } else {
                    logService.Log(LogLevel.Warning, "OAK-D UDP provider failed to start; falling back.");
                    oakUdp.Dispose();
                }
            }
            if (bodyProvider == null) {
                if (useSentis3dTracking && sentisModel != null) {
                    bodyProvider = new SentisPoseProvider(logService, cameraCapture, converter, sentisModel, sentisImageNetNorm, sentisMetreScale, sentisDepthScale, sentisPersonCrop);
                } else if (useMediaPipeTracking) {
                    string modelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", poseModelFileName);
                    bodyProvider = new MediaPipePoseProvider(logService, cameraCapture, modelPath, converter);
                } else {
                    bodyProvider = new FakeBodyTrackingProvider();
                }
                bodyProvider.StartTracking();
                if (!bodyProvider.IsRunning && !(bodyProvider is FakeBodyTrackingProvider)) {
                    logService.Log(LogLevel.Warning, "Body tracking provider failed to start; falling back to fake tracking.");
                    bodyProvider.Dispose();
                    bodyProvider = new FakeBodyTrackingProvider();
                    bodyProvider.StartTracking();
                }
            }
            if (bodyProvider is SentisPoseProvider) {
                // The 3D path (fast dance, true depth) needs a snappier filter than the calm-webcam default,
                // or fast motion gets damped into the "under-responsive" look. LOW-A: apply the Sentis
                // profile to the LIVE filter WITHOUT overwriting the serialized filterMinCutoff/Beta fields
                // — those stay the single source for the calibration sliders and the other providers.
                jointFilter.SetParameters(sentisFilterMinCutoff, sentisFilterBeta);
            }
            logService.Log(LogLevel.Info, "Body tracking started.");

            if (useFaceTracking) {
                EnsureFaceProvider();
            }
            if (useHandTracking) {
                EnsureHandProvider();
            }
        }

        // H5: the IK solver used to be constructed only at startup and only when useIkDriver was already
        // true; the scene ships it off, so ticking "IK Arm/Leg Driver" at runtime just flipped a bool and
        // nothing ever bound → the whole IK driver was dead as shipped. Build on demand and bind
        // immediately if an avatar is already loaded (UpdateTracking's animator-changed block only binds a
        // solver that already exists). Idempotent.
        private void EnsureIkSolver() {
            if (ikSolver == null) {
                ikSolver = new AnimationRiggingIkDriver();
            }
            if (!ikSolver.IsBound && boundAnimator != null) {
                Transform avatarRoot = null;
                if (avatarSession != null && avatarSession.Current != null && avatarSession.Current.Root != null) {
                    avatarRoot = avatarSession.Current.Root.transform;
                }
                ikSolver.Bind(boundAnimator, avatarRoot);
                ikSolver.SetLegTracking(trackLegs);
            }
        }

        // M1: extracted from StartTracking so the face toggle can lazily construct the provider when it is
        // enabled at runtime. The old code built providers once behind the start-time bool, so toggling a
        // feature on later did nothing (the provider stayed null). Idempotent.
        private void EnsureFaceProvider() {
            if (faceProvider != null) {
                return;
            }
            if (bodyProvider is OakDUdpPoseProvider) {
                // LOW-A: the OAK-D setup feeds no RGB webcam into Unity (the OAK color sensor stays on the
                // sidecar) and the OAK stream carries no face data, so a MediaPipe face provider here would
                // only poll a dead capture. Skip it; face tracking is unavailable on the OAK path.
                logService.Log(LogLevel.Info, "Face tracking unavailable on the OAK-D path (no RGB webcam); skipping face provider.");
                return;
            }
            if (useMediaPipeFace) {
                string faceModelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", faceModelFileName);
                MediaPipeFaceProvider mediaPipeFace = new MediaPipeFaceProvider(logService, cameraCapture, faceModelPath);
                if (mediaPipeFace.StartTracking()) {
                    faceProvider = mediaPipeFace;
                } else {
                    logService.Log(LogLevel.Warning, "MediaPipe face provider failed to start; falling back to fake face tracking.");
                    mediaPipeFace.Dispose();
                    faceProvider = new FakeFaceTrackingProvider();
                    faceProvider.StartTracking();
                }
            } else {
                faceProvider = new FakeFaceTrackingProvider();
                faceProvider.StartTracking();
            }
            if (expressionRetargeter == null) {
                expressionRetargeter = new VrmExpressionRetargeter();
            }
            logService.Log(LogLevel.Info, "Face tracking started.");
        }

        // M1: same lazy-construct treatment for the hand toggle. Idempotent.
        private void EnsureHandProvider() {
            if (handProvider != null) {
                return;
            }
            if (bodyProvider is OakDUdpPoseProvider) {
                // The OAK-D sidecar streams fingers (lh/rh) alongside the body in one datagram, so drive
                // the hands from that same stream via a facade over the pose provider's socket — no
                // separate MediaPipe RGB webcam needed (which is unavailable in the OAK setup anyway).
                OakDUdpPoseProvider oakBody = (OakDUdpPoseProvider)bodyProvider;
                handProvider = new OakDUdpHandProvider(oakBody);
                handProvider.StartTracking();
                logService.Log(LogLevel.Info, "Hand tracking source: OAK-D UDP (fingers from the whole-body stream).");
            } else if (useMediaPipeHand) {
                string handModelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", handModelFileName);
                // LOW-A: reuse the SINGLE shared converter so a live mirror-flip (SetFlipX) applies to the
                // hands too. The old code built a SECOND converter that never received runtime flip changes,
                // so a live mirror toggle mirrored the body but not the fingers.
                MediaPipeHandProvider mediaPipeHand = new MediaPipeHandProvider(logService, cameraCapture, handModelPath, converter);
                if (mediaPipeHand.StartTracking()) {
                    handProvider = mediaPipeHand;
                } else {
                    logService.Log(LogLevel.Warning, "MediaPipe hand provider failed to start; falling back to fake hand tracking.");
                    mediaPipeHand.Dispose();
                    handProvider = new FakeHandTrackingProvider();
                    handProvider.StartTracking();
                }
            } else {
                handProvider = new FakeHandTrackingProvider();
                handProvider.StartTracking();
            }
            if (handRetargeter == null) {
                handRetargeter = new HumanoidHandRetargeter();
                handRetargeter.SetWristWeight(wristRotationWeight);
            }
            logService.Log(LogLevel.Info, "Hand tracking started.");
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) {
            // BEFORE anything binds the port or starts reading. sceneLoaded runs after the new
            // scene's Awake/OnEnable and before its Start, so a scene's own TrackedStage has not yet
            // opened its socket when this decides whether the producer has to change.
            EnsureSidecarForScene(scene.name);
            if (scene.name == MirrorSceneName) {
                logService.Log(LogLevel.Info, "Mirror scene loaded - starting avatar tracking provider.");
                StartTracking();
                SetupAvatarSession(scene);
            } else {
                if (bodyProvider != null) {
                    logService.Log(LogLevel.Info, "Non-mirror scene loaded (" + scene.name + ") - stopping avatar tracking provider to free UDP port.");
                    StopTracking();
                    TeardownAvatarSession();
                }
            }
        }

        private void TeardownAvatarSession() {
            if (avatarSession != null) {
                avatarSession.AvatarChanged -= FrameCameraOnAvatar;
                avatarSession.Dispose();
                avatarSession = null;
                boundAnimator = null;
            }
            diagnosticsHud = null;
            calibrationPanel = null;
            cameraController = null;
        }

        private void SetupAvatarSession(Scene scene) {
            TeardownAvatarSession();
            Transform avatarRoot = FindAvatarRoot(scene);
            if (avatarRoot == null) {
                logService.Log(LogLevel.Warning, "AvatarRoot '" + avatarRootName + "' not found in scene " + scene.name + ".");
                return;
            }
            avatarSession = new AvatarSessionController(services.AvatarLoader, logService, settingsStore, avatarRoot);
            AvatarMouseRotator mouseRotator = avatarRoot.gameObject.GetComponent<AvatarMouseRotator>();
            if (mouseRotator == null) {
                mouseRotator = avatarRoot.gameObject.AddComponent<AvatarMouseRotator>();
            }
            mouseRotator.SetTarget(avatarRoot);
            InitializeAvatarUi(scene);
            InitializeCameraPreview(scene);
            InitializeDiagnosticsHud(scene);
            InitializeCalibrationPanel(scene);
            cameraController = FindInScene<MirrorCameraController>(scene);
            avatarSession.AvatarChanged += FrameCameraOnAvatar;
            TryAutoLoadLastAvatar();
        }

        private string DescribeActiveTracking() {
            if (bodyProvider is OakDUdpPoseProvider) {
                // Same provider, same wire; only the producer differs. Say which, because a video that
                // looks like a person at a mirror is otherwise indistinguishable from the camera.
                return trackFromVideo && !string.IsNullOrEmpty(videoStatus)
                    ? videoStatus + " (UDP sidecar, no stereo)"
                    : "OAK-D 3D (UDP sidecar)";
            }
            if (bodyProvider is SentisPoseProvider) {
                return "Sentis RTMW3D (GPU)";
            }
            if (bodyProvider is MediaPipePoseProvider) {
                return "MediaPipe Pose (CPU)";
            }
            return "Fake Tracking";
        }

        /// <summary>The first component of type <typeparamref name="T"/> under any root of the scene (inactive ones too), or null.</summary>
        private static T FindInScene<T>(Scene scene) where T : Component {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++) {
                T found = roots[index].GetComponentInChildren<T>(true);
                if (found != null) {
                    return found;
                }
            }
            return null;
        }

        private void InitializeDiagnosticsHud(Scene scene) {
            DiagnosticsHudPanel panel = FindInScene<DiagnosticsHudPanel>(scene);
            if (panel == null) {
                return;
            }
            diagnosticsHud = panel;
            diagnosticsHud.Initialize(performanceMonitor);
            diagnosticsHud.SetTrackingStatus(DescribeActiveTracking());
            // LOW-A: interpolate the real serialized camera fields instead of a hardcoded string
            // (the scene may run 640x400 for OAK / any webcam resolution, not always 1280x720@30).
            diagnosticsHud.SetCameraInfo(useVideoSource
                ? "Sample Video MP4"
                : ("Webcam " + cameraWidth + "x" + cameraHeight + "@" + cameraFps + "fps"));
            logService.Log(LogLevel.Info, "Diagnostics HUD wired.");
        }

        private void InitializeCalibrationPanel(Scene scene) {
            CalibrationSettingsPanel panel = FindInScene<CalibrationSettingsPanel>(scene);
            if (panel == null) {
                return;
            }
            calibrationPanel = panel;
            calibrationPanel.SetInitialValues(poseFlipX, useIkDriver, useFaceTracking, useHandTracking, filterMinCutoff, filterBeta);
            // LOW-A: only show the webcam picker when a webcam is actually the source.
            calibrationPanel.SetCameraDropdownVisible(!useVideoSource && !useOakUdpTracking);
            calibrationPanel.OnCameraDeviceChanged += HandleCameraDeviceChanged;
            calibrationPanel.OnMirrorFlipToggled += (value) => {
                poseFlipX = value;
                if (converter != null) {
                    converter.SetFlipX(value);
                }
            };
            calibrationPanel.OnIkToggled += (value) => {
                useIkDriver = value;
                if (value) {
                    EnsureIkSolver(); // H5: build + bind the solver on enable, not just flip the bool
                }
            };
            calibrationPanel.OnFaceToggled += (value) => {
                useFaceTracking = value;
                if (value) {
                    EnsureFaceProvider(); // M1: construct the provider on enable
                }
            };
            calibrationPanel.OnHandToggled += (value) => {
                useHandTracking = value;
                if (value) {
                    EnsureHandProvider(); // M1: construct the provider on enable
                }
            };
            calibrationPanel.OnFilterMinCutoffChanged += (value) => {
                filterMinCutoff = value;
                if (jointFilter != null) {
                    jointFilter.SetParameters(filterMinCutoff, filterBeta);
                }
            };
            calibrationPanel.OnFilterBetaChanged += (value) => {
                filterBeta = value;
                if (jointFilter != null) {
                    jointFilter.SetParameters(filterMinCutoff, filterBeta);
                }
            };
            logService.Log(LogLevel.Info, "Calibration settings panel wired.");
        }

        private void HandleCameraDeviceChanged(string deviceName) {
            if (cameraCapture == null || string.IsNullOrEmpty(deviceName)) {
                return;
            }
            cameraCapture.StopCapture();
            CameraCaptureRequest request = new CameraCaptureRequest(deviceName, cameraWidth, cameraHeight, cameraFps);
            bool started = cameraCapture.StartCapture(request);
            if (started && diagnosticsHud != null) {
                diagnosticsHud.SetCameraInfo(deviceName + " (" + cameraWidth + "x" + cameraHeight + ")");
            }
            logService.Log(LogLevel.Info, "Camera device changed to: " + deviceName + " (Started: " + started + ")");
        }

        private void FrameCameraOnAvatar() {
            if (avatarSession == null || avatarSession.Current == null) {
                return;
            }
            if (diagnosticsHud != null) {
                diagnosticsHud.SetAvatarName(avatarSession.Current.Root != null ? avatarSession.Current.Root.name : "Avatar");
            }
            if (cameraController != null) {
                GameObject root = avatarSession.Current.Root;
                if (root != null) {
                    cameraController.Frame(root.transform);
                }
            }
        }

        private void InitializeCameraPreview(Scene scene) {
            CameraPreviewController preview = FindInScene<CameraPreviewController>(scene);
            if (preview == null) {
                logService.Log(LogLevel.Info, "No CameraPreviewController found in scene; skipping preview wiring.");
                return;
            }
            preview.Initialize(cameraCapture);
            logService.Log(LogLevel.Info, "Camera preview wired.");
        }

        private void InitializeAvatarUi(Scene scene) {
            AvatarLibraryPanel panel = FindInScene<AvatarLibraryPanel>(scene);
            if (panel == null) {
                logService.Log(LogLevel.Info, "No AvatarLibraryPanel found in scene; skipping avatar UI wiring.");
                return;
            }
            panel.Initialize(avatarSession, logService);
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                Transform btnTransform = roots[index].transform.Find("ToggleUiButton");
                if (btnTransform != null) {
                    UnityEngine.UI.Button btn = btnTransform.GetComponent<UnityEngine.UI.Button>();
                    if (btn != null) {
                        btn.onClick.AddListener(() => {
                            panel.gameObject.SetActive(!panel.gameObject.activeSelf);
                        });
                    }
                    break;
                }
                index = index + 1;
            }
            logService.Log(LogLevel.Info, "Avatar UI wired.");
        }

        private Transform FindAvatarRoot(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                GameObject root = roots[index];
                if (root.name == avatarRootName) {
                    return root.transform;
                }
                index = index + 1;
            }
            return null;
        }

        private void TryAutoLoadLastAvatar() {
            AppSettings settings = settingsStore.Current;
            if (settings == null || settings.Avatar == null) {
                return;
            }
            string lastPath = settings.Avatar.LastPath;
            if (string.IsNullOrEmpty(lastPath)) {
                logService.Log(LogLevel.Info, "No last avatar path set; skipping auto-load.");
                return;
            }
            logService.Log(LogLevel.Info, "Auto-loading last avatar: " + lastPath);
            RunAutoLoad(lastPath);
        }

        private async void RunAutoLoad(string lastPath) {
            try {
                AvatarLoadResult result = await avatarSession.LoadFromPathAsync(lastPath, true);
                if (!result.IsSuccess) {
                    logService.Log(LogLevel.Warning, "Auto-load failed (" + result.Status + "): " + result.Message);
                }
            } catch (Exception exception) {
                logService.LogException(exception, "Auto-load avatar threw");
            }
        }
    }
}
