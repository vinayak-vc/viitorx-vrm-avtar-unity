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
        private ShowcaseTrackingSource tracking;
        private bool ready;
        private float lastAspect;
        private bool lastFluidOn;
        private float smoothedFrameMs = 16.7f;
        private bool leaving;

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
                return tracking != null && tracking.SimulationActive;
            }
        }

        public bool StreamFresh {
            get {
                return tracking != null && tracking.StreamFresh;
            }
        }

        public TrackingState ProviderState {
            get {
                return tracking != null ? tracking.ProviderState : TrackingState.NoStream;
            }
        }

        public double SecondsSinceAccepted {
            get {
                return tracking != null ? tracking.SecondsSinceAccepted : -1.0;
            }
        }

        public long PacketsReceived {
            get {
                return tracking != null ? tracking.PacketsReceived : 0L;
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
                return tracking != null ? tracking.LiveSourceLabel : string.Empty;
            }
        }

        public bool PortInUse {
            get {
                return tracking != null && tracking.PortInUse;
            }
        }

        public string OwnershipState {
            get {
                return tracking != null ? tracking.OwnershipState : "-";
            }
        }

        /// <summary>What the status chip may truthfully say.</summary>
        public SignalStatus Signal {
            get {
                return tracking != null ? tracking.Signal : SignalStatus.NoSignal;
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

            tracking = new ShowcaseTrackingSource("ChromaticRain", udpPort, poseInterpolationDelayMs,
                                                  useHumanizedSkeleton, mirrorX, simulated);
            tracking.Mode = source;
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
            if (tracking != null) {
                tracking.Stop();
            }
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
                tracking.Stop();
                leaving = LauncherScene.Load();
                if (leaving) {
                    return;
                }
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            smoothedFrameMs = Mathf.Lerp(smoothedFrameMs, Time.unscaledDeltaTime * 1000f,
                                         1f - Mathf.Exp(-dt / 0.5f));

            ReadKeys();
            tracking.Mode = source;
            HandleAspect();
            SyncFluidLayer();

            // 1. Source.
            SkeletonPose pose;
            HandPose left;
            HandPose right;
            TrackingTelemetry telemetry;
            bool sourceFresh = tracking.Tick(dt, settings, out pose, out left, out right, out telemetry);
            if (tracking.ConsumeResetRequest()) {
                ResetPipeline();
            }

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

        private void ResetPipeline() {
            adapter.Reset(body);
            machine.Reset();
            builder.Reset();
            if (fluid != null) {
                fluid.Clear();
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
            tracking.SetMirror(mirror);
        }

        /// <summary>Forget where everyone stood and restart from nobody.</summary>
        public void Recenter() {
            tracking.Recenter();
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
    }
}
