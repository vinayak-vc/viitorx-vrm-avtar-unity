using System;

using UnityEngine;
using UnityEngine.Rendering.Universal;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking.OakD;

namespace ChromaticRain {
    /// <summary>
    /// The showcase's one controller: lifecycle, source selection and the ORDER of a frame.
    ///
    /// Tracking comes from the existing <see cref="TrackedStage"/> (the project's UDP provider,
    /// space converter, humanized layer and grounding), or from a deterministic simulated pose that
    /// goes through the same SkeletonPose / HandPose code. This class opens no socket of its own and
    /// starts no process. In Live and Auto mode it listens on the same port every experience scene
    /// uses; start the sidecar as for those scenes.
    ///
    /// Frame order, in one place: source -> adapter -> state machine -> silhouette -> mask -> rain ->
    /// fluid targets. FluXY's solver runs afterwards in its own LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ChromaticRainShowcase : MonoBehaviour {
        [Header("Assets")]
        [SerializeField] private ChromaticRainSettings settings;

        [Header("Input")]
        [Tooltip("Live = the Python sidecar. Simulated = built-in test pose. Auto = live, and the "
                 + "simulation takes over (with a visible banner) when no live data arrives.")]
        [SerializeField] private ShowcaseSource source = ShowcaseSource.Auto;
        [SerializeField] private SimulatedPoseSource simulated = new SimulatedPoseSource();
        [Tooltip("UDP port the sidecar sends to. Same default as every experience scene.")]
        [SerializeField] private int udpPort = 8899;
        [Tooltip("Presentation delay in ms, as in the mirror app. 0 = latest wins.")]
        [SerializeField] private float poseInterpolationDelayMs = 40f;
        [Tooltip("Run the existing humanized skeleton layer before the silhouette reads the pose.")]
        [SerializeField] private bool useHumanizedSkeleton = true;

        [Header("Scene")]
        [SerializeField] private Camera showcaseCamera;
        [SerializeField] private RainRenderer rain;
        [SerializeField] private SilhouetteMaskRenderer silhouette;
        [SerializeField] private FluidLayer fluid;
        [SerializeField] private Transform[] fullScreenQuads;
        [SerializeField] private ShowcaseDebugOverlay debugOverlay;

        [Header("Runtime toggles (start values)")]
        [SerializeField] private SilhouetteMode silhouetteMode = SilhouetteMode.DarkSilhouette;
        [SerializeField] private bool fluidOn = true;
        [SerializeField] private bool mirrorX = true;
        [SerializeField] private bool uiVisible = true;
        [SerializeField] private bool debugVisible;

        private readonly ShowcaseBody body = new ShowcaseBody();
        private readonly ShowcasePoseAdapter adapter = new ShowcasePoseAdapter();
        private readonly ScreenSpaceMapper mapper = new ScreenSpaceMapper();
        private readonly ShowcaseStateMachine machine = new ShowcaseStateMachine();
        private readonly SilhouetteBuilder builder = new SilhouetteBuilder();
        private readonly SkeletonPose simPose = new SkeletonPose();
        private readonly HandPose simLeft = new HandPose();
        private readonly HandPose simRight = new HandPose();

        private TrackedStage stage;
        private ILogService log;
        private bool ready;
        private bool simulationActive;
        private float noLiveFor;
        private float lastAspect;
        private bool lastFluidOn;
        private bool streamFresh;
        private double sinceAccepted = -1.0;
        private TrackingState trackingState = TrackingState.NoStream;
        private float smoothedFrameMs = 16.7f;
        private ShowcaseSource lastSource;
        private bool leaving;
        private float labelTimer;
        private string sourceLabel = string.Empty;

        // Provider health, read in one call. Kept as fields so the call allocates nothing.
        private string healthSession;
        private long healthAccepted;
        private long healthReceived;
        private long healthTransitions;
        private long healthNewSession;
        private long healthRejectedOld;
        private long healthOutOfOrder;
        private long healthDuplicate;
        private int healthBuffer;
        private long healthReconnects;

        public ChromaticRainSettings Settings {
            get {
                return settings;
            }
        }

        public ShowcaseBody Body {
            get {
                return body;
            }
        }

        public ScreenSpaceMapper Mapper {
            get {
                return mapper;
            }
        }

        public SilhouetteBuilder Builder {
            get {
                return builder;
            }
        }

        public ShowcaseStateMachine Machine {
            get {
                return machine;
            }
        }

        public SimulatedPoseSource Simulation {
            get {
                return simulated;
            }
        }

        public FluidLayer Fluid {
            get {
                return fluid;
            }
        }

        public SilhouetteMaskRenderer MaskRenderer {
            get {
                return silhouette;
            }
        }

        public ShowcaseSource Source {
            get {
                return source;
            }
        }

        public SilhouetteMode Mode {
            get {
                return silhouetteMode;
            }
        }

        public bool FluidOn {
            get {
                return fluidOn && settings != null && settings.fluidEnabled;
            }
        }

        public bool MirrorX {
            get {
                return mirrorX;
            }
        }

        public bool UiVisible {
            get {
                return uiVisible;
            }
        }

        public bool DebugVisible {
            get {
                return debugVisible;
            }
        }

        public bool SimulationActive {
            get {
                return simulationActive;
            }
        }

        public bool StreamFresh {
            get {
                return streamFresh;
            }
        }

        public TrackingState ProviderState {
            get {
                return trackingState;
            }
        }

        public double SecondsSinceAccepted {
            get {
                return sinceAccepted;
            }
        }

        public long PacketsReceived {
            get {
                return stage != null && stage.Provider != null ? stage.Provider.ReceivedCount : 0L;
            }
        }

        public float FrameMilliseconds {
            get {
                return smoothedFrameMs;
            }
        }

        public bool PersonTracked {
            get {
                return machine.Phase != ShowcasePhase.NoPerson && machine.Phase != ShowcasePhase.Lost
                       && body.CoreFresh;
            }
        }

        /// <summary>What the bootstrap says the live source is, e.g. "VIDEO 147_1080p.mp4" or
        /// "LIVE camera:0"; empty for the OAK-D or when no bootstrap has said anything.</summary>
        public string LiveSourceLabel {
            get {
                return sourceLabel;
            }
        }

        public bool PortInUse {
            get {
                return stage != null && stage.Provider != null && !stage.Provider.IsRunning;
            }
        }

        public string OwnershipState {
            get {
                if (stage == null || stage.Telemetry == null) {
                    return "-";
                }
                return string.IsNullOrEmpty(stage.Telemetry.OwnershipState) ? "-" : stage.Telemetry.OwnershipState;
            }
        }

        /// <summary>What the status chip may truthfully say.</summary>
        public SignalStatus Signal {
            get {
                if (simulationActive) {
                    return source == ShowcaseSource.Simulated ? SignalStatus.Simulated : SignalStatus.SimulatedFallback;
                }
                if (PortInUse) {
                    return SignalStatus.PortInUse;
                }
                return streamFresh ? SignalStatus.Live : SignalStatus.NoSignal;
            }
        }

        private void Awake() {
            if (settings == null) {
                Debug.LogError("[ChromaticRain] No ChromaticRainSettings assigned. Assign "
                               + "Assets/Games/viitorx-vrm-avtar-unity/Showcase/ChromaticRain/Settings/ChromaticRainSettings.asset, or run "
                               + "Showcase > Chromatic Rain > Build Showcase Scene.", this);
                enabled = false;
                return;
            }
            if (showcaseCamera == null) {
                showcaseCamera = Camera.main;
            }
            if (showcaseCamera == null) {
                Debug.LogError("[ChromaticRain] No camera assigned and no MainCamera in the scene.", this);
                enabled = false;
                return;
            }

            log = new ShowcaseLog();
            lastSource = source;
            lastAspect = showcaseCamera.aspect;
            mapper.Configure(settings, lastAspect);
            ConfigureCamera();
            FitQuads();

            bool rainOk = rain != null && rain.Initialize();
            bool maskOk = silhouette != null && silhouette.Initialize(lastAspect, settings);
            if (!rainOk || !maskOk) {
                Debug.LogError("[ChromaticRain] The rain or the silhouette mask could not start; see the "
                               + "errors above. Nothing is drawn.", this);
                enabled = false;
                return;
            }

            if (fluid != null) {
                bool fluidOk = fluid.Initialize(settings, mapper.ViewWidth, mapper.ViewHeight);
                if (!fluidOk) {
                    Debug.LogWarning("[ChromaticRain] The FluXY layer is unavailable; continuing with rain "
                                     + "and silhouette only.", this);
                    fluid = null;
                }
            }
            lastFluidOn = FluidOn;
            if (fluid != null) {
                fluid.SetLayerOn(lastFluidOn);
            }

            adapter.Reset(body);
            machine.Reset();
            builder.Reset();
            ready = true;
        }

        private void OnDestroy() {
            StopStage();
        }

        private void Update() {
            if (!ready) {
                return;
            }
            // ESC goes back to the launcher, like every experience scene. The socket is released
            // first: the next scene binds the same port.
            if (leaving) {
                return;
            }
            if (LauncherScene.ReturnRequested() && LauncherScene.IsAvailable) {
                StopStage();
                leaving = LauncherScene.Load();
                if (leaving) {
                    return;
                }
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            smoothedFrameMs = Mathf.Lerp(smoothedFrameMs, Time.unscaledDeltaTime * 1000f,
                                         1f - Mathf.Exp(-dt / 0.5f));

            RefreshSourceLabel(dt);
            ReadKeys();
            HandleAspect();
            SyncFluidLayer();

            // 1. Source.
            SkeletonPose pose;
            HandPose left;
            HandPose right;
            TrackingTelemetry telemetry;
            bool sourceFresh = TickSource(dt, out pose, out left, out right, out telemetry);

            // 2. Adapter -> view-space body.
            adapter.Update(body, pose, left, right, telemetry, sourceFresh, dt, settings, mapper);

            // 3. State machine, on the same frame's answer.
            bool trackedNow = sourceFresh && body.CoreFresh;
            machine.Tick(trackedNow, body.Energy, dt, settings);
            if (machine.SnapRequested) {
                adapter.RequestSnap();
                adapter.Update(body, pose, left, right, telemetry, sourceFresh, dt, settings, mapper);
            }

            // 4. Silhouette shapes, mask, rain.
            builder.Build(body, settings, dt);
            silhouette.EnsureTexture(mapper.Aspect, settings.maskHeight);
            silhouette.Render(builder, mapper.ViewWidth, mapper.ViewHeight, machine.SilhouetteAlpha,
                              silhouetteMode, settings);
            rain.Apply(settings, dt, mapper.Aspect);
            showcaseCamera.backgroundColor = settings.backgroundColor;

            // 5. Fluid targets, before FluXY's own LateUpdate.
            if (fluid != null && lastFluidOn) {
                fluid.Tick(body, machine.FluidActivity, dt, settings);
            }
        }

        private bool TickSource(float dt, out SkeletonPose pose, out HandPose left, out HandPose right,
                                out TrackingTelemetry telemetry) {
            bool wantLive = source != ShowcaseSource.Simulated;
            if (wantLive && stage == null) {
                StartStage();
            } else if (!wantLive && stage != null) {
                StopStage();
            }

            bool liveFresh = false;
            streamFresh = false;
            if (stage != null && stage.Provider != null && stage.Provider.IsRunning) {
                stage.Tick(dt);
                double since;
                stage.Provider.GetStreamHealth(out trackingState, out healthSession, out healthAccepted,
                                               out healthReceived, out since, out healthTransitions,
                                               out healthNewSession, out healthRejectedOld,
                                               out healthOutOfOrder, out healthDuplicate,
                                               out healthBuffer, out healthReconnects);
                sinceAccepted = since;
                streamFresh = trackingState == TrackingState.Live && since <= settings.maxPoseAgeSeconds;
                // A person, not merely a stream: the stage must call the pose live and not synthetic.
                liveFresh = streamFresh && stage.IsLive && !stage.IsAttract && stage.Pose.Valid;
            } else {
                sinceAccepted = -1.0;
                trackingState = stage != null && stage.Provider != null ? stage.Provider.State : TrackingState.NoStream;
            }

            bool useSimulation;
            switch (source) {
                case ShowcaseSource.Simulated:
                    useSimulation = true;
                    break;
                case ShowcaseSource.Live:
                    useSimulation = false;
                    break;
                default:
                    // Judged on the STREAM, not on whether somebody stands in front of the camera:
                    // a live sidecar with an empty room is "LIVE, no person" and shows plain rain.
                    // Fall back only after a sustained absence of data, and hand control straight
                    // back as soon as live data returns.
                    if (streamFresh) {
                        noLiveFor = 0f;
                    } else {
                        noLiveFor += dt;
                    }
                    useSimulation = simulationActive
                        ? !streamFresh
                        : noLiveFor >= settings.autoFallbackSeconds;
                    break;
            }

            if (useSimulation != simulationActive || source != lastSource) {
                simulationActive = useSimulation;
                lastSource = source;
                ResetPipeline();
            }

            if (simulationActive) {
                if (simulated.StreamActive) {
                    simulated.mirrorX = mirrorX;
                    simulated.Tick(dt);
                    Vector3 origin = new Vector3(simulated.OffsetX, SimulatedPoseSource.HipHeight, 0f);
                    simPose.Fill(simulated.Frame, origin, 1f, dt);
                    simLeft.Fill(simulated.Hands, true, origin, 1f);
                    simRight.Fill(simulated.Hands, false, origin, 1f);
                }
                pose = simPose;
                left = simLeft;
                right = simRight;
                telemetry = null;
                return simulated.StreamActive;
            }

            if (stage != null) {
                pose = stage.Pose;
                left = stage.Left;
                right = stage.Right;
                telemetry = stage.Telemetry;
            } else {
                pose = null;
                left = null;
                right = null;
                telemetry = null;
            }
            return liveFresh;
        }

        // The bootstrap that owns the sidecar publishes one status line (video / webcam / phone, then
        // the sidecar's own state). Only its first part says WHAT is being tracked, and it is rebuilt
        // on every call, so it is read once a second rather than every frame.
        private void RefreshSourceLabel(float dt) {
            labelTimer -= dt;
            if (labelTimer > 0f) {
                return;
            }
            labelTimer = 1f;
            string status = SidecarBootstrap.Status;
            string label = string.Empty;
            if (!string.IsNullOrEmpty(status)) {
                int bar = status.IndexOf("  |  ", StringComparison.Ordinal);
                string head = bar >= 0 ? status.Substring(0, bar) : status;
                if (head.StartsWith("VIDEO", StringComparison.Ordinal) || head.StartsWith("LIVE", StringComparison.Ordinal)) {
                    label = head;
                }
            }
            sourceLabel = label;
        }

        private void ResetPipeline() {
            adapter.Reset(body);
            machine.Reset();
            builder.Reset();
            simPose.Fill(null, Vector3.zero, 1f, 0.016f);
            if (fluid != null) {
                fluid.Clear();
            }
        }

        private void StartStage() {
            stage = new TrackedStage(log, udpPort, poseInterpolationDelayMs);
            stage.BodyOrigin = new Vector3(0f, SimulatedPoseSource.HipHeight, 0f);
            stage.BodyScale = 1f;
            stage.UseHumanized = useHumanizedSkeleton;
            // A synthetic attract figure is NOT a person and must never reach the silhouette.
            stage.AttractEnabled = false;
            stage.GroundToFloor = true;
            stage.FollowPosition = true;
            stage.FollowScale = 1f;
            stage.Start(mirrorX, true, false);
            if (stage.Provider == null || !stage.Provider.IsRunning) {
                Debug.LogWarning("[ChromaticRain] The tracking socket on UDP " + udpPort + " could not be "
                                 + "opened (is the mirror app or another scene already running?). Live "
                                 + "tracking is unavailable; choose Simulated, or free the port.", this);
            }
        }

        private void StopStage() {
            if (stage != null) {
                stage.Stop();
                stage = null;
            }
        }

        private void ConfigureCamera() {
            showcaseCamera.orthographic = true;
            showcaseCamera.orthographicSize = mapper.ViewHeight * 0.5f;
            showcaseCamera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            showcaseCamera.nearClipPlane = 0.1f;
            showcaseCamera.farClipPlane = 30f;
            showcaseCamera.clearFlags = CameraClearFlags.SolidColor;
            showcaseCamera.backgroundColor = settings.backgroundColor;
            UniversalAdditionalCameraData data = showcaseCamera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) {
                data.renderPostProcessing = true;
            }
        }

        private void FitQuads() {
            if (fullScreenQuads == null) {
                return;
            }
            for (int i = 0; i < fullScreenQuads.Length; i++) {
                if (fullScreenQuads[i] != null) {
                    fullScreenQuads[i].localScale = new Vector3(mapper.ViewWidth, mapper.ViewHeight, 1f);
                    fullScreenQuads[i].position = Vector3.zero;
                }
            }
        }

        private void HandleAspect() {
            float aspect = showcaseCamera.aspect;
            if (Mathf.Abs(aspect - lastAspect) < 0.001f) {
                return;
            }
            lastAspect = aspect;
            mapper.Configure(settings, aspect);
            ConfigureCamera();
            FitQuads();
            if (fluid != null) {
                fluid.Resize(mapper.ViewWidth, mapper.ViewHeight);
            }
        }

        private void SyncFluidLayer() {
            bool want = FluidOn;
            if (fluid != null && want != lastFluidOn) {
                fluid.SetLayerOn(want);
                lastFluidOn = want;
            }
        }

        private void ReadKeys() {
            if (Input.GetKeyDown(KeyCode.D)) {
                ToggleDebug();
            }
            if (Input.GetKeyDown(KeyCode.N)) {
                SetSilhouetteMode(silhouetteMode == SilhouetteMode.DarkSilhouette
                                  ? SilhouetteMode.NegativeSpace : SilhouetteMode.DarkSilhouette);
            }
            if (Input.GetKeyDown(KeyCode.F)) {
                fluidOn = !fluidOn;
            }
            if (Input.GetKeyDown(KeyCode.S)) {
                source = source == ShowcaseSource.Live ? ShowcaseSource.Simulated
                         : (source == ShowcaseSource.Simulated ? ShowcaseSource.Auto : ShowcaseSource.Live);
            }
            if (Input.GetKeyDown(KeyCode.M)) {
                SetMirror(!mirrorX);
            }
            if (Input.GetKeyDown(KeyCode.R)) {
                Recenter();
            }
            if (Input.GetKeyDown(KeyCode.H)) {
                uiVisible = !uiVisible;
            }
            if (Input.GetKeyDown(KeyCode.C) && fluid != null) {
                fluid.Clear();
            }
            if (!debugVisible) {
                return;
            }
            // Test hooks, only while the debug view is up.
            if (Input.GetKeyDown(KeyCode.Alpha1)) {
                simulated.armPattern = SimulatedArmPattern.Auto;
            }
            if (Input.GetKeyDown(KeyCode.Alpha2)) {
                simulated.armPattern = SimulatedArmPattern.RaiseBoth;
            }
            if (Input.GetKeyDown(KeyCode.Alpha3)) {
                simulated.armPattern = SimulatedArmPattern.Alternate;
            }
            if (Input.GetKeyDown(KeyCode.Alpha4)) {
                simulated.armPattern = SimulatedArmPattern.Down;
            }
            if (Input.GetKeyDown(KeyCode.P)) {
                simulated.streamActive = !simulated.streamActive;
            }
            if (Input.GetKeyDown(KeyCode.T)) {
                simulated.stationary = !simulated.stationary;
            }
            if (Input.GetKeyDown(KeyCode.K)) {
                simulated.handsActive = !simulated.handsActive;
            }
            if (Input.GetKeyDown(KeyCode.L)) {
                ToggleLostJoints(Joint.LeftElbow, Joint.LeftWrist);
            }
            if (Input.GetKeyDown(KeyCode.J)) {
                ToggleLostJoints(Joint.RightKnee, Joint.RightAnkle);
            }
        }

        private void ToggleLostJoints(int a, int b) {
            long mask = (1L << a) | (1L << b);
            simulated.lostJointMask = (simulated.lostJointMask & mask) == mask
                ? simulated.lostJointMask & ~mask
                : simulated.lostJointMask | mask;
        }

        public void ToggleDebug() {
            debugVisible = !debugVisible;
            if (debugOverlay != null) {
                debugOverlay.enabled = debugVisible;
            }
        }

        public void SetSilhouetteMode(SilhouetteMode mode) {
            silhouetteMode = mode;
        }

        public void SetSource(ShowcaseSource value) {
            source = value;
        }

        public void SetFluid(bool on) {
            fluidOn = on;
        }

        /// <summary>Left/right mirroring, as a real mirror does. Restarts the live listener so the
        /// converter is rebuilt with the new sign; the simulation follows the same convention.</summary>
        public void SetMirror(bool mirror) {
            if (mirror == mirrorX) {
                return;
            }
            mirrorX = mirror;
            if (stage != null) {
                StopStage();
                StartStage();
            }
            ResetPipeline();
        }

        /// <summary>Forget where everyone stood and restart from nobody.</summary>
        public void Recenter() {
            simulated.Restart();
            if (stage != null) {
                stage.ResetFollow();
                stage.ResetGrounding();
            }
            ResetPipeline();
        }

        private void OnValidate() {
            if (debugOverlay != null && Application.isPlaying) {
                debugOverlay.enabled = debugVisible;
            }
        }

        private void Start() {
            if (debugOverlay != null) {
                debugOverlay.enabled = debugVisible;
            }
        }

        private sealed class ShowcaseLog : ILogService {
            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Debug.LogError("[ChromaticRain] " + message);
                } else if (level == LogLevel.Warning) {
                    Debug.LogWarning("[ChromaticRain] " + message);
                } else {
                    Debug.Log("[ChromaticRain] " + message);
                }
            }

            public void LogException(Exception exception, string context) {
                Debug.LogError("[ChromaticRain] " + context + ": " + (exception != null ? exception.Message : "null"));
            }

            public void Flush() {
            }
        }
    }
}
