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
        [Tooltip("The silhouette skins: body proportions and look. Edit them live, or add new ones. B cycles while it runs.")]
        [SerializeField] private SilhouetteSkinSet skins;

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
        [Tooltip("Inspect the silhouette without the rain: the filled mask, the landmarks over it, or its contour. V cycles.")]
        [SerializeField] private SilhouetteDebugView silhouetteDebugView = SilhouetteDebugView.Off;
        [SerializeField] private bool fluidOn = true;
        [SerializeField] private bool mirrorX = true;
        [SerializeField] private bool uiVisible = true;
        [SerializeField] private bool debugVisible;

        // One slot per person. Slot 0 is also what the single-person code (debug text, tests, the simulation) reads.
        private ShowcasePerson[] people = new ShowcasePerson[] { new ShowcasePerson() };
        private readonly ScreenSpaceMapper mapper = new ScreenSpaceMapper();
        private ShowcaseTrackingSource tracking;
        private int skinIndex;
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

        /// <summary>The first person slot's body.</summary>
        public ShowcaseBody Body {
            get {
                return people[0].Body;
            }
        }

        /// <summary>How many person slots the scene has (from the settings' Max People).</summary>
        public int PersonSlotCount {
            get {
                return people.Length;
            }
        }

        public ShowcasePerson PersonAt(int slot) {
            return people[slot];
        }

        /// <summary>How many people are shown and measured this frame.</summary>
        public int TrackedPeopleCount {
            get {
                int count = 0;
                for (int i = 0; i < people.Length; i++) {
                    if (people[i].Tracked) {
                        count = count + 1;
                    }
                }
                return count;
            }
        }

        public ScreenSpaceMapper Mapper {
            get {
                return mapper;
            }
        }

        public SilhouetteBuilder Builder {
            get {
                return people[0].Builder;
            }
        }

        public ShowcaseStateMachine Machine {
            get {
                return people[0].Machine;
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

        /// <summary>The skin in use: the proportions and look of the silhouette. Never null.</summary>
        public SilhouetteSkin ActiveSkin {
            get {
                return skins.Get(skinIndex);
            }
        }

        public int SkinIndex {
            get {
                return skinIndex;
            }
        }

        public int SkinCount {
            get {
                return skins.Count;
            }
        }

        public SilhouetteDebugView DebugView {
            get {
                return silhouetteDebugView;
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

        /// <summary>Somebody is shown and measured this frame.</summary>
        public bool PersonTracked {
            get {
                for (int i = 0; i < people.Length; i++) {
                    if (people[i].Tracked) {
                        return true;
                    }
                }
                return false;
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
            if (skins == null) {
                Debug.LogWarning("[ChromaticRain] No SilhouetteSkinSet assigned: using the built-in skins. Assign "
                                 + "Assets/Games/viitorx-vrm-avtar-unity/Showcase/ChromaticRain/Settings/ChromaticRainSkins.asset to edit them.", this);
                skins = SilhouetteSkinSet.CreateDefault();
            }
            skinIndex = skins.StartSkin;
            if (showcaseCamera == null) {
                showcaseCamera = Camera.main;
            }
            if (showcaseCamera == null) {
                Debug.LogError("[ChromaticRain] No camera assigned and no MainCamera in the scene.", this);
                enabled = false;
                return;
            }

            int slots = Mathf.Clamp(settings.maxPeople, 1, 6);
            people = new ShowcasePerson[slots];
            for (int i = 0; i < slots; i++) {
                people[i] = new ShowcasePerson();
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
                bool fluidOk = fluid.Initialize(settings, mapper.ViewWidth, mapper.ViewHeight, slots);
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

            for (int i = 0; i < people.Length; i++) {
                people[i].Release();
            }
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

            // 2. Each person: adapter -> view-space body, state machine, silhouette shapes.
            UpdatePeople(sourceFresh, pose, left, right, telemetry, dt);

            // 3. Mask (one texture for everybody), rain.
            silhouette.EnsureTexture(mapper.Aspect, settings.maskHeight);
            silhouette.BeginFrame();
            for (int i = 0; i < people.Length; i++) {
                ShowcasePerson person = people[i];
                if (person.InUse) {
                    silhouette.AddPerson(person.Builder, mapper.ViewWidth, mapper.ViewHeight,
                                         person.Machine.SilhouetteAlpha, ActiveSkin);
                }
            }
            silhouette.EndFrame(silhouetteMode, ActiveSkin, silhouetteDebugView);
            rain.Apply(settings, dt, mapper.Aspect);
            showcaseCamera.backgroundColor = settings.backgroundColor;

            // 4. Fluid targets, before FluXY's own LateUpdate. A free slot passes a cleared body: its targets go off.
            if (fluid != null && lastFluidOn && fluid.BeginFrame(dt, settings)) {
                for (int i = 0; i < people.Length; i++) {
                    fluid.DrivePerson(i, people[i].Body, people[i].Machine.FluidActivity, dt, settings);
                }
            }
        }

        /// <summary>
        /// Who is on screen this frame, and one pipeline step for each of them. With a crowd upstream every tracked
        /// person gets a slot keyed on the sidecar's track id (stable, never reused). With a single-person sender, or
        /// the simulation, there is one person in the slot keyed <see cref="PersonSlots.SingleId"/>, exactly as before.
        /// A person who disappears keeps their slot while their silhouette fades out, then the slot is freed.
        /// </summary>
        private void UpdatePeople(bool sourceFresh, SkeletonPose pose, HandPose left, HandPose right,
                                  TrackingTelemetry telemetry, float dt) {
            for (int i = 0; i < people.Length; i++) {
                people[i].Present = false;
            }

            if (tracking.CrowdActive) {
                for (int b = 0; b < tracking.LiveBodyCount; b++) {
                    ShowcaseLiveBody live = tracking.LiveBody(b);
                    int slot = PersonSlots.Acquire(people, live.Id);
                    if (slot == PersonSlots.NoSlot) {
                        continue;
                    }
                    ShowcasePerson person = people[slot];
                    person.Present = true;
                    // Hands and the trust channel exist for the first person only; the rest are drawn from the body.
                    UpdateLateralShift(person, live);
                    StepPerson(person, live.Pose, live.Primary ? left : null, live.Primary ? right : null,
                               live.Primary ? telemetry : null, true, dt);
                }
            } else {
                int slot = PersonSlots.Acquire(people, PersonSlots.SingleId);
                if (slot != PersonSlots.NoSlot) {
                    ShowcasePerson person = people[slot];
                    person.Present = true;
                    person.Adapter.LateralShift = 0f;
                    StepPerson(person, pose, left, right, telemetry, sourceFresh, dt);
                }
            }

            for (int i = 0; i < people.Length; i++) {
                ShowcasePerson person = people[i];
                if (!person.InUse) {
                    continue;
                }
                if (!person.Present) {
                    StepPerson(person, null, null, null, null, false, dt);
                    if (person.Machine.Phase == ShowcasePhase.NoPerson) {
                        person.Release();
                    }
                }
            }
        }

        // Where the camera sees this person, sideways, against where the stage staged them. Updated only while both hips
        // are measured (the centre is the mid-hip) or the sender says it anchored the root itself (F-60); otherwise the
        // last good value stays, so a person whose hips are briefly lost does not slide across the screen.
        private static void UpdateLateralShift(ShowcasePerson person, ShowcaseLiveBody live) {
            if (!live.HasRoot
                || (!live.Pose.RootEstimated && (!live.Pose.Present[Joint.LeftHip] || !live.Pose.Present[Joint.RightHip]))) {
                return;
            }
            float shift = live.RootX - live.Pose.Centre.x;
            if (float.IsNaN(shift) || float.IsInfinity(shift)) {
                return;
            }
            person.Adapter.LateralShift = shift;
        }

        private void StepPerson(ShowcasePerson person, SkeletonPose pose, HandPose left, HandPose right,
                                TrackingTelemetry telemetry, bool fresh, float dt) {
            person.Adapter.Update(person.Body, pose, left, right, telemetry, fresh, dt, settings, mapper);
            bool trackedNow = fresh && person.Body.CoreFresh;
            person.Machine.Tick(trackedNow, person.Body.Energy, dt, settings);
            if (person.Machine.SnapRequested) {
                person.Adapter.RequestSnap();
                person.Adapter.Update(person.Body, pose, left, right, telemetry, fresh, dt, settings, mapper);
            }
            person.Builder.Build(person.Body, ActiveSkin, dt);
        }

        private void ResetPipeline() {
            for (int i = 0; i < people.Length; i++) {
                people[i].Release();
            }
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
            if (Input.GetKeyDown(KeyCode.B)) {
                SetSkin(skins.Next(skinIndex));
            }
            if (Input.GetKeyDown(KeyCode.V)) {
                SetDebugView((SilhouetteDebugView)(((int)silhouetteDebugView + 1) % 4));
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
                debugOverlay.enabled = debugVisible || silhouetteDebugView == SilhouetteDebugView.Landmarks;
            }
        }

        /// <summary>Switches the silhouette skin by index (wrapped into the list). Does not touch the asset.</summary>
        public void SetSkin(int index) {
            skinIndex = ((index % skins.Count) + skins.Count) % skins.Count;
        }

        public void SetSilhouetteMode(SilhouetteMode mode) {
            silhouetteMode = mode;
        }

        /// <summary>Inspect the silhouette without the rain; the landmarks view needs the debug overlay, which is enabled for it.</summary>
        public void SetDebugView(SilhouetteDebugView view) {
            silhouetteDebugView = view;
            if (debugOverlay != null) {
                debugOverlay.enabled = debugVisible || view == SilhouetteDebugView.Landmarks;
            }
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
                debugOverlay.enabled = debugVisible || silhouetteDebugView == SilhouetteDebugView.Landmarks;
            }
        }

        private void Start() {
            if (debugOverlay != null) {
                debugOverlay.enabled = debugVisible || silhouetteDebugView == SilhouetteDebugView.Landmarks;
            }
        }
    }
}
