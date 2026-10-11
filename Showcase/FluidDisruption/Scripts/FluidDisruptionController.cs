using ChromaticRain;

using Joint = ChromaticRain.Joint;

using UnityEngine;
using UnityEngine.Rendering.Universal;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking.OakD;

namespace FluidDisruption {
    /// <summary>
    /// The showcase's one controller: lifecycle, input selection and the ORDER of a frame. It coordinates;
    /// each step is a small class of its own.
    ///
    /// Frame order: demo programme -> tracking source (shared <see cref="ShowcaseTrackingSource"/>: the
    /// existing TrackedStage, or the simulated pose) -> pose adapter (view-space body) -> state
    /// controller -> movement estimators -> FluXY targets and baseline sources. FluXY's own solver runs
    /// afterwards, in its LateUpdate.
    ///
    /// Tracking itself belongs to the scene's AppBootstrap (it owns the sidecar and the video, webcam,
    /// phone or OAK-D source); this class only listens, and opens no socket of its own.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class FluidDisruptionController : MonoBehaviour {
        [Header("Assets")]
        [SerializeField] private FluidDisruptionSettings settings;

        [Header("Input")]
        [Tooltip("Live = the Python sidecar. Simulated = the built-in demo programme. Auto = live, and the "
                 + "simulation takes over (with a visible banner) when no live data arrives.")]
        [SerializeField] private ShowcaseSource source = ShowcaseSource.Auto;
        [SerializeField] private SimulatedPoseSource simulated = new SimulatedPoseSource();
        [SerializeField] private FluidDisruptionDemoPoseSource demo = new FluidDisruptionDemoPoseSource();
        [Tooltip("UDP port the sidecar sends to. Same default as every experience scene.")]
        [SerializeField] private int udpPort = 8899;
        [Tooltip("Presentation delay in ms, as in the mirror app. 0 = latest wins.")]
        [SerializeField] private float poseInterpolationDelayMs = 40f;
        [SerializeField] private bool useHumanizedSkeleton = true;

        [Header("Scene")]
        [SerializeField] private Camera showcaseCamera;
        [SerializeField] private FluidDisruptionFluid fluid;
        [SerializeField] private FluidBaselineSources baseline;
        [SerializeField] private FluidDisruptionTargetDriver targetDriver;
        [SerializeField] private FluidDisruptionDebugView debugView;

        [Header("Runtime switches (start values)")]
        [SerializeField] private FluidQuality quality = FluidQuality.Quality;
        [Tooltip("Off = no FluXY simulation at all (diagnosis). Keep it ON for the showcase.")]
        [SerializeField] private bool simulationOn = true;
        [Tooltip("Off = plain FluXY dye with none of the shader extras.")]
        [SerializeField] private bool shaderEnhancements = true;
        [SerializeField] private bool mirrorX = true;
        [SerializeField] private bool uiVisible = true;
        [SerializeField] private bool debugVisible;

        private readonly ShowcaseBody body = new ShowcaseBody();
        private readonly ShowcasePoseAdapter adapter = new ShowcasePoseAdapter();
        private readonly ScreenSpaceMapper mapper = new ScreenSpaceMapper();
        private readonly FluidDisruptionStateController machine = new FluidDisruptionStateController();
        private readonly InteractionPointTracker points = new InteractionPointTracker();

        private ShowcaseTrackingSource tracking;
        private bool ready;
        private bool leaving;
        private float lastAspect;
        private float smoothedFrameMs = 16.7f;
        private bool streamCut;
        private FluidQuality lastQuality;
        private bool lastSimulationOn;
        private bool lastEnhancements;
        private Texture2D softSplat;

        public FluidDisruptionSettings Settings {
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

        public FluidDisruptionStateController Machine {
            get {
                return machine;
            }
        }

        public MovementImpulseEstimator LeftHand {
            get {
                return points.Left;
            }
        }

        public MovementImpulseEstimator RightHand {
            get {
                return points.Right;
            }
        }

        public MovementImpulseEstimator Torso {
            get {
                return points.Torso;
            }
        }

        public SimulatedPoseSource Simulation {
            get {
                return simulated;
            }
        }

        public FluidDisruptionDemoPoseSource Demo {
            get {
                return demo;
            }
        }

        public FluidDisruptionFluid Fluid {
            get {
                return fluid;
            }
        }

        public FluidDisruptionTargetDriver Targets {
            get {
                return targetDriver;
            }
        }

        public FluidBaselineSources Baseline {
            get {
                return baseline;
            }
        }

        public ShowcaseSource Source {
            get {
                return source;
            }
        }

        public FluidQuality Quality {
            get {
                return quality;
            }
        }

        public bool SimulationOn {
            get {
                return simulationOn;
            }
        }

        public bool ShaderEnhancements {
            get {
                return shaderEnhancements;
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

        public float FrameMilliseconds {
            get {
                return smoothedFrameMs;
            }
        }

        public bool StreamCut {
            get {
                return streamCut;
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

        public string LiveSourceLabel {
            get {
                return tracking != null ? tracking.LiveSourceLabel : string.Empty;
            }
        }

        /// <summary>What the status chip may truthfully say. LIVE only with fresh, accepted data.</summary>
        public SignalStatus Signal {
            get {
                return tracking != null ? tracking.Signal : SignalStatus.NoSignal;
            }
        }

        /// <summary>A person is being followed right now: confirmed, and a torso is measured this frame.</summary>
        public bool PersonTracked {
            get {
                return machine.PersonPresent && body.CoreFresh;
            }
        }

        private void Awake() {
            if (settings == null) {
                Debug.LogError("[FluidDisruption] No FluidDisruptionSettings assigned. Assign "
                               + "Assets/Games/viitorx-vrm-avtar-unity/Showcase/FluidDisruption/Settings/FluidDisruptionSettings.asset, "
                               + "or run Showcase > Fluid Disruption > Build Showcase Scene.", this);
                enabled = false;
                return;
            }
            if (showcaseCamera == null) {
                showcaseCamera = Camera.main;
            }
            if (showcaseCamera == null) {
                Debug.LogError("[FluidDisruption] No camera assigned and no MainCamera in the scene.", this);
                enabled = false;
                return;
            }

            tracking = new ShowcaseTrackingSource("FluidDisruption", udpPort, poseInterpolationDelayMs,
                                                  useHumanizedSkeleton, mirrorX, simulated);
            tracking.Mode = source;
            lastAspect = showcaseCamera.aspect;
            mapper.Configure(settings, lastAspect);
            ConfigureCamera();

            softSplat = FluidTargets.BuildSoftSplat(128);
            lastQuality = quality;
            lastEnhancements = shaderEnhancements;
            lastSimulationOn = simulationOn;
            bool fluidOk = fluid != null && fluid.Initialize(settings, quality, shaderEnhancements,
                                                             mapper.ViewWidth, mapper.ViewHeight);
            bool baselineOk = fluidOk && baseline != null && baseline.Initialize(settings, mapper.ViewWidth, mapper.ViewHeight, softSplat, fluid.BaselineField);
            bool driverOk = fluidOk && targetDriver != null && targetDriver.Initialize(settings, softSplat);
            if (!fluidOk || !baselineOk || !driverOk) {
                Debug.LogError("[FluidDisruption] The FluXY fluid could not start; see the errors above. "
                               + "Nothing is drawn.", this);
                enabled = false;
                return;
            }
            if (fluid != null) {
                string problems = fluid.DescribeProblems();
                if (!string.IsNullOrEmpty(problems)) {
                    Debug.LogWarning("[FluidDisruption] FluXY configuration: " + problems, this);
                }
            }
            if (!simulationOn) {
                fluid.SetSimulationOn(false);
            }

            adapter.Reset(body);
            machine.Reset();
            points.Reset();
            demo.Restart();
            ready = true;
        }

        private void Start() {
            if (debugView != null) {
                debugView.enabled = debugVisible;
            }
        }

        private void OnDestroy() {
            if (tracking != null) {
                tracking.Stop();
            }
            if (softSplat != null) {
                Destroy(softSplat);
            }
        }

        private void Update() {
            if (!ready || leaving) {
                return;
            }
            // ESC goes back to the launcher, like every experience scene. The socket is released first:
            // the next scene binds the same port.
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
            SyncSwitches();

            // 1. The demo programme steers the simulated figure; the shared source reads it.
            if (source != ShowcaseSource.Live) {
                demo.Tick(dt, simulated);
                if (streamCut) {
                    simulated.streamActive = false;
                }
            }

            // 2. Source.
            SkeletonPose pose;
            HandPose left;
            HandPose right;
            TrackingTelemetry telemetry;
            bool sourceFresh = tracking.Tick(dt, settings, out pose, out left, out right, out telemetry);
            if (tracking.ConsumeResetRequest()) {
                ResetPipeline();
            }

            // 3. Adapter -> view-space body.
            adapter.Update(body, pose, left, right, telemetry, sourceFresh, dt, settings, mapper);

            // 4. State, on the same frame's answer (the movement strengths are last frame's).
            bool trackedNow = sourceFresh && body.CoreFresh;
            machine.Tick(trackedNow, points.Left.Intensity, points.Right.Intensity, points.Torso.Intensity, dt, settings);
            if (machine.PersonAppeared) {
                // A new person: place every joint exactly and start every movement track afresh, so
                // nothing of whoever stood here before can become a velocity.
                adapter.RequestSnap();
                adapter.Update(body, pose, left, right, telemetry, sourceFresh, dt, settings, mapper);
                points.Reset();
            }

            // 5. Movement strength of each interaction point, from valid samples only.
            points.Update(body, trackedNow && machine.PersonPresent, dt, settings);

            // 6. FluXY targets and baseline sources, before FluXY's own LateUpdate.
            if (simulationOn) {
                baseline.Tick(dt, settings);
                targetDriver.Tick(machine, points.Left, points.Right, points.Torso, settings);
                fluid.ApplyLive(settings, quality, shaderEnhancements);
            }
        }

        // Forget the previous person. The FLUID is deliberately not touched: a person entering or
        // leaving, or the source changing, never clears the solver.
        private void ResetPipeline() {
            adapter.Reset(body);
            machine.Reset();
            points.Reset();
            if (targetDriver != null) {
                targetDriver.Release();
            }
        }

        private void SyncSwitches() {
            if (simulationOn != lastSimulationOn) {
                lastSimulationOn = simulationOn;
                fluid.SetSimulationOn(simulationOn);
                if (!simulationOn) {
                    targetDriver.Release();
                }
            }
            if (!simulationOn) {
                return;
            }
            if (quality != lastQuality) {
                lastQuality = quality;
                fluid.ApplyQuality(settings, quality);
            }
            if (shaderEnhancements != lastEnhancements) {
                lastEnhancements = shaderEnhancements;
                fluid.SetEnhancements(shaderEnhancements);
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
            fluid.Resize(settings, mapper.ViewWidth, mapper.ViewHeight);
            baseline.Resize(mapper.ViewWidth, mapper.ViewHeight);
        }

        private void ConfigureCamera() {
            showcaseCamera.orthographic = true;
            showcaseCamera.orthographicSize = mapper.ViewHeight * 0.5f;
            showcaseCamera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            showcaseCamera.nearClipPlane = 0.1f;
            showcaseCamera.farClipPlane = 30f;
            showcaseCamera.clearFlags = CameraClearFlags.SolidColor;
            showcaseCamera.backgroundColor = settings.backgroundLow;
            UniversalAdditionalCameraData data = showcaseCamera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) {
                data.renderPostProcessing = true;
            }
        }

        private void ReadKeys() {
            if (Input.GetKeyDown(KeyCode.D)) {
                ToggleDebug();
            }
            if (Input.GetKeyDown(KeyCode.H)) {
                uiVisible = !uiVisible;
            }
            if (Input.GetKeyDown(KeyCode.S)) {
                source = source == ShowcaseSource.Live ? ShowcaseSource.Simulated
                         : (source == ShowcaseSource.Simulated ? ShowcaseSource.Auto : ShowcaseSource.Live);
            }
            if (Input.GetKeyDown(KeyCode.M)) {
                SetMirror(!mirrorX);
            }
            if (Input.GetKeyDown(KeyCode.Q)) {
                quality = quality == FluidQuality.Quality ? FluidQuality.Performance : FluidQuality.Quality;
            }
            if (Input.GetKeyDown(KeyCode.E)) {
                shaderEnhancements = !shaderEnhancements;
            }
            if (Input.GetKeyDown(KeyCode.F)) {
                simulationOn = !simulationOn;
            }
            if (Input.GetKeyDown(KeyCode.C)) {
                ResetFluid();
            }
            if (Input.GetKeyDown(KeyCode.R)) {
                Recenter();
            }
            if (!debugVisible) {
                return;
            }
            // Test hooks, only while the debug view is up.
            if (Input.GetKeyDown(KeyCode.Space)) {
                demo.playProgramme = !demo.playProgramme;
            }
            if (Input.GetKeyDown(KeyCode.Period)) {
                demo.Hold(demo.SegmentIndex + 1);
            }
            if (Input.GetKeyDown(KeyCode.Comma)) {
                demo.Hold(demo.SegmentIndex - 1);
            }
            if (Input.GetKeyDown(KeyCode.Slash)) {
                demo.Hold(-1);
            }
            if (Input.GetKeyDown(KeyCode.P)) {
                streamCut = !streamCut;
            }
            if (Input.GetKeyDown(KeyCode.L)) {
                ToggleLostJoints(Joint.LeftElbow, Joint.LeftWrist);
            }
            if (Input.GetKeyDown(KeyCode.K)) {
                ToggleLostJoints(Joint.RightElbow, Joint.RightWrist);
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
            if (debugView != null) {
                debugView.enabled = debugVisible;
            }
        }

        public void SetSource(ShowcaseSource value) {
            source = value;
        }

        public void SetQuality(FluidQuality value) {
            quality = value;
        }

        public void SetShaderEnhancements(bool on) {
            shaderEnhancements = on;
        }

        public void SetSimulation(bool on) {
            simulationOn = on;
        }

        /// <summary>Left/right mirroring, as a real mirror does.</summary>
        public void SetMirror(bool mirror) {
            if (mirror == mirrorX) {
                return;
            }
            mirrorX = mirror;
            tracking.SetMirror(mirror);
        }

        /// <summary>Manual reset for debugging or a demonstration: back to the seeded baseline field.
        /// Never called automatically.</summary>
        public void ResetFluid() {
            if (fluid != null && simulationOn) {
                fluid.ResetToBaseline();
            }
        }

        /// <summary>Forget where everyone stood and restart the demo from nobody. The fluid keeps going.</summary>
        public void Recenter() {
            demo.Restart();
            tracking.Recenter();
        }

        private void OnValidate() {
            if (debugView != null && Application.isPlaying) {
                debugView.enabled = debugVisible;
            }
        }
    }
}
