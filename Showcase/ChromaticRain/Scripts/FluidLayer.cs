using Fluxy;

using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// The FluXY layer: one <see cref="FluxySolver"/>, one <see cref="FluxyContainer"/> covering the
    /// screen, and three <see cref="FluxyTarget"/>s PER PERSON SLOT (left hand, right hand, body). The targets are
    /// driven only from valid tracked positions; a target that has no valid position is DISABLED, so
    /// it injects nothing and keeps no stale state, and a re-enabled target starts from its current
    /// position (FluxyTarget.OnEnable), so there is never a velocity impulse from a jump.
    ///
    /// Everything FluXY-specific lives here; the rest of the showcase works with this layer off.
    /// Uses only FluXY's own public fields and methods.
    /// </summary>
    public sealed class FluidLayer : MonoBehaviour {
        private sealed class TargetState {
            public FluxyTarget Target;
            public Vector2 Last;
            public bool WasValid;
            public float ColorPhase;
            public Material SplatInstance;
        }

        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int FlowTintId = Shader.PropertyToID("_FlowTint");
        private static readonly int GradientId = Shader.PropertyToID("_Gradient");
        private static readonly int StreakLengthId = Shader.PropertyToID("_StreakLength");
        private static readonly int StriationId = Shader.PropertyToID("_Striation");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        [SerializeField] private FluxySolver solver;
        [SerializeField] private FluxyContainer container;
        [SerializeField] private MeshRenderer containerRenderer;
        [SerializeField] private FluxyTarget leftHandTarget;
        [SerializeField] private FluxyTarget rightHandTarget;
        [SerializeField] private FluxyTarget bodyTarget;
        [SerializeField] private Material fluidMaterial;
        [SerializeField] private Material simulationMaterial;
        [SerializeField] private Material splatMaterial;
        [SerializeField] private Texture splatTexture;

        /// <summary>Targets per person: left hand, right hand, body.</summary>
        public const int TargetsPerPerson = 3;

        // Three states per person slot, in order left, right, body. Slot 0 uses the three targets of the scene; the
        // others are created at run time when more than one person is shown.
        private TargetState[] states = new TargetState[0];
        private int slotCount = 1;
        private Material runtimeFluid;
        private Material runtimeSimulation;
        private Texture2D hueRing;
        private int appliedResolution;
        private int appliedSupersampling;
        private float viewAspect = 1.7778f;
        private float time;

        public bool IsReady { get; private set; }

        /// <summary>True while the layer is switched on.</summary>
        public bool IsOn {
            get {
                return gameObject.activeSelf;
            }
        }

        /// <summary>FluXY's dye buffer, for the debug view. Null while the layer is off.</summary>
        public Texture StateTexture {
            get {
                if (solver == null || !gameObject.activeSelf) {
                    return null;
                }
                FluxyStorage.Framebuffer fb = solver.framebuffer;
                return fb != null ? fb.stateA : null;
            }
        }

        /// <summary>FluXY's velocity buffer, for the debug view. Null while the layer is off.</summary>
        public Texture VelocityTexture {
            get {
                if (solver == null || !gameObject.activeSelf) {
                    return null;
                }
                FluxyStorage.Framebuffer fb = solver.framebuffer;
                return fb != null ? fb.velocityA : null;
            }
        }

        /// <summary>How many targets exist (three per person slot), for the debug view.</summary>
        public int TargetCount {
            get {
                return states.Length;
            }
        }

        /// <summary>World position of a target for the debug view; valid only while it is enabled. Index 0-2 are the
        /// first person's left hand, right hand and body, 3-5 the second person's, and so on.</summary>
        public bool TryGetTargetPosition(int index, out Vector2 position) {
            TargetState state = index >= 0 && index < states.Length ? states[index] : null;
            if (state != null && state.Target != null && state.Target.enabled && state.WasValid) {
                position = state.Last;
                return true;
            }
            position = Vector2.zero;
            return false;
        }

        public bool Initialize(ChromaticRainSettings s, float viewWidth, float viewHeight) {
            return Initialize(s, viewWidth, viewHeight, 1);
        }

        /// <summary>Starts the layer with targets for <paramref name="people"/> person slots (1 to 6).</summary>
        public bool Initialize(ChromaticRainSettings s, float viewWidth, float viewHeight, int people) {
            if (solver == null || container == null || containerRenderer == null
                || leftHandTarget == null || rightHandTarget == null || bodyTarget == null
                || fluidMaterial == null || simulationMaterial == null || splatMaterial == null) {
                Debug.LogError("[ChromaticRain] FluidLayer is missing a FluXY reference or material. "
                               + "Run Showcase > Chromatic Rain > Build Showcase Scene to rebuild it.", this);
                return false;
            }

            runtimeFluid = new Material(fluidMaterial);
            runtimeFluid.name = "ChromaticRain_Fluid (runtime)";
            hueRing = BuildHueRing(s);
            runtimeFluid.SetTexture(GradientId, hueRing);
            containerRenderer.sharedMaterial = runtimeFluid;

            runtimeSimulation = new Material(simulationMaterial);
            runtimeSimulation.name = "ChromaticRain_Simulation (runtime)";

            // The solver was already enabled by the scene load, so it holds the serialized material
            // and resolution. Rebuild it once with the runtime copy and the live settings.
            solver.enabled = false;
            solver.simulationMaterial = runtimeSimulation;
            solver.desiredResolution = s.fluidResolution;
            solver.densitySupersampling = s.fluidSupersampling;
            appliedResolution = s.fluidResolution;
            appliedSupersampling = s.fluidSupersampling;
            solver.enabled = true;
            viewAspect = viewWidth / Mathf.Max(0.01f, viewHeight);

            ConfigureContainer(s, viewWidth, viewHeight);

            slotCount = Mathf.Clamp(people, 1, 6);
            states = new TargetState[slotCount * TargetsPerPerson];
            FluxyTarget[] all = new FluxyTarget[states.Length];
            for (int slot = 0; slot < slotCount; slot++) {
                for (int k = 0; k < TargetsPerPerson; k++) {
                    int index = slot * TargetsPerPerson + k;
                    TargetState state = new TargetState();
                    state.Target = slot == 0 ? SceneTarget(k) : CreateTarget(slot, k);
                    // Each person's colour drifts on its own phase, so two people do not paint the same colour.
                    state.ColorPhase = k * 2.1f + slot * 1.3f;
                    ConfigureTarget(state, s, k == 2 ? s.bodySplatSize : s.handSplatSize);
                    states[index] = state;
                    all[index] = state.Target;
                }
            }
            container.targets = all;
            IsReady = true;
            return true;
        }

        private FluxyTarget SceneTarget(int kind) {
            return kind == 0 ? leftHandTarget : (kind == 1 ? rightHandTarget : bodyTarget);
        }

        // A target of another person: its own GameObject, because FluXY splats at the target's transform.
        private FluxyTarget CreateTarget(int slot, int kind) {
            string part = kind == 0 ? "L" : (kind == 1 ? "R" : "body");
            GameObject host = new GameObject("FluidTarget_person" + slot + "_" + part);
            host.transform.SetParent(transform, false);
            FluxyTarget target = host.AddComponent<FluxyTarget>();
            target.enabled = false;
            return target;
        }

        private void ConfigureContainer(ChromaticRainSettings s, float width, float height) {
            container.containerShape = FluxyContainer.ContainerShape.Plane;
            container.subdivisions = Vector2Int.one;
            container.size = new Vector3(width, height, 1f);
            container.lookAt = null;
            container.projectFrom = null;
            container.clearTexture = null;
            container.clearColor = new Color(0f, 0f, 0f, 0f);
            container.surfaceNormals = null;
            container.normalScale = 0f;
            FluxyContainer.BoundaryConditions boundaries = new FluxyContainer.BoundaryConditions();
            boundaries.horizontalBoundary = FluxyContainer.BoundaryConditions.BoundaryType.Open;
            boundaries.verticalBoundary = FluxyContainer.BoundaryConditions.BoundaryType.Open;
            container.boundaries = boundaries;
            FluxyContainer.EdgeFalloff falloff = new FluxyContainer.EdgeFalloff();
            falloff.densityEdgeWidth = 0.12f;
            falloff.densityFalloffRate = 1.5f;
            falloff.velocityEdgeWidth = 0.12f;
            falloff.velocityFalloffRate = 2f;
            container.edgeFalloff = falloff;
            container.velocityScale = 1f;
            container.accelerationScale = 0f;
            container.gravity = Vector3.zero;
            container.adhesion = 0f;
            container.surfaceTension = 0f;
            container.buoyancy = 0f;
            container.pressure = 1f;
            container.UpdateContainerShape();
            ApplyFluidSettings(s);
        }

        /// <summary>The settings that may change while running.</summary>
        public void ApplyFluidSettings(ChromaticRainSettings s) {
            if (container == null) {
                return;
            }
            container.viscosity = s.fluidViscosity;
            container.turbulence = s.fluidTurbulence;
            container.dissipation = new Vector4(s.fluidDissipation, s.fluidDissipation,
                                                s.fluidDissipation, s.fluidDissipation);
            container.externalForce = new Vector3(0f, -s.fluidDrift, 0f);
            if (runtimeFluid != null) {
                runtimeFluid.SetFloat(OpacityId, s.fluidOpacity);
                runtimeFluid.SetFloat(GainId, s.fluidGain);
                runtimeFluid.SetFloat(FlowTintId, s.flowTint);
                runtimeFluid.SetFloat(StreakLengthId, s.fluidStreakLength);
                runtimeFluid.SetFloat(StriationId, s.fluidStriation);
                runtimeFluid.SetFloat(AspectId, viewAspect);
            }
            if (solver != null && IsReady
                && (s.fluidResolution != appliedResolution || s.fluidSupersampling != appliedSupersampling)) {
                // FluXY only rebuilds its buffers when the solver is re-enabled.
                solver.enabled = false;
                solver.desiredResolution = s.fluidResolution;
                solver.densitySupersampling = s.fluidSupersampling;
                solver.enabled = true;
                appliedResolution = s.fluidResolution;
                appliedSupersampling = s.fluidSupersampling;
            }
        }

        private void ConfigureTarget(TargetState state, ChromaticRainSettings s, float size) {
            FluxyTarget t = state.Target;
            state.SplatInstance = new Material(splatMaterial);
            state.SplatInstance.name = "ChromaticRain_Splat (runtime)";
            t.splatMaterial = state.SplatInstance;
            t.densityTexture = splatTexture;
            t.velocityTexture = null;
            t.rateOverSteps = 0;
            t.rateOverTime = 0f;
            t.rateOverDistance = s.splatsPerMetre;
            t.overridePosition = false;
            t.overrideRotation = true;
            t.rotation = 0f;
            t.scaleWithDistance = true;
            t.scaleWithTransform = false;
            t.scale = new Vector2(size, size);
            t.positionRandomness = 0f;
            t.scaleRandomness = 0.1f;
            t.rotationRandomness = 0f;
            t.velocityWeight = s.velocityWeight;
            t.maxRelativeVelocity = 8f;
            t.velocityScale = new Vector3(s.velocityGain, s.velocityGain, 0f);
            t.angularVelocityScale = 0f;
            t.force = Vector3.zero;
            t.torque = 0f;
            t.densityWeight = 0f;
            t.color = Color.white;
            t.densityNoise = 0f;
            t.velocityNoise = 0f;
            t.srcBlend = UnityEngine.Rendering.BlendMode.SrcAlpha;
            t.dstBlend = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
            t.blendOp = UnityEngine.Rendering.BlendOp.Add;
            t.enabled = false;
        }

        /// <summary>Turns the whole layer on or off. Off removes the solver's buffers entirely.</summary>
        public void SetLayerOn(bool on) {
            if (gameObject.activeSelf != on) {
                gameObject.SetActive(on);
            }
        }

        /// <summary>Resizes the container when the screen aspect changes.</summary>
        public void Resize(float width, float height) {
            if (!IsReady) {
                return;
            }
            container.size = new Vector3(width, height, 1f);
            viewAspect = width / Mathf.Max(0.01f, height);
            container.UpdateContainerShape();
            // Re-register so the solver repacks its tiles.
            container.enabled = false;
            container.enabled = true;
        }

        public void Clear() {
            if (IsReady && container != null && container.isActiveAndEnabled) {
                container.Clear();
            }
        }

        /// <summary>
        /// One frame, called BEFORE FluXY's own LateUpdate. <paramref name="activity"/> is the state
        /// machine's fluid gate: 0 means no target may inject.
        /// </summary>
        public void Tick(ShowcaseBody body, float activity, float dt, ChromaticRainSettings s) {
            if (!BeginFrame(dt, s)) {
                return;
            }
            DrivePerson(0, body, activity, dt, s);
        }

        /// <summary>Once per frame, before any <see cref="DrivePerson"/>: advances the clock and applies the live
        /// settings. Returns false while the layer is off.</summary>
        public bool BeginFrame(float dt, ChromaticRainSettings s) {
            if (!IsReady || !gameObject.activeSelf) {
                return false;
            }
            time += dt;
            ApplyFluidSettings(s);
            return true;
        }

        /// <summary>Drives one person slot's three targets. A slot nobody uses passes a cleared body and activity 0,
        /// which switches its targets off.</summary>
        public void DrivePerson(int slot, ShowcaseBody body, float activity, float dt, ChromaticRainSettings s) {
            if (!IsReady || !gameObject.activeSelf || slot < 0 || slot >= slotCount) {
                return;
            }
            int baseIndex = slot * TargetsPerPerson;
            DriveHand(states[baseIndex], body, Joint.LeftWrist, Joint.LeftHandEnd, activity, dt, s, s.handSplatSize, 1f);
            DriveHand(states[baseIndex + 1], body, Joint.RightWrist, Joint.RightHandEnd, activity, dt, s, s.handSplatSize, 1f);
            DriveBody(states[baseIndex + 2], body, activity, dt, s);
        }

        private void DriveHand(TargetState state, ShowcaseBody body, int wrist, int handEnd,
                               float activity, float dt, ChromaticRainSettings s, float size, float strength) {
            bool valid = activity > 0.01f && body.Fresh[wrist] && body.Weight[wrist] > 0.6f;
            Vector2 p = body.Position[wrist];
            if (valid && body.Weight[handEnd] > 0.5f) {
                p = Vector2.Lerp(p, body.Position[handEnd], 0.5f);
            }
            float speed = body.Velocity[wrist].magnitude;
            Drive(state, valid, p, speed, activity, dt, s, size, strength);
        }

        private void DriveBody(TargetState state, ShowcaseBody body, float activity, float dt,
                               ChromaticRainSettings s) {
            bool valid = s.bodyTargetEnabled && activity > 0.01f && body.CoreFresh;
            Vector2 p = body.HipCentre + new Vector2(0f, 0.3f);
            Drive(state, valid, p, body.HipVelocity.magnitude, activity, dt, s, s.bodySplatSize, 0.4f);
        }

        private void Drive(TargetState state, bool valid, Vector2 p, float speed, float activity,
                           float dt, ChromaticRainSettings s, float size, float strength) {
            FluxyTarget t = state.Target;
            if (!valid || float.IsNaN(p.x) || float.IsNaN(p.y)) {
                if (t.enabled) {
                    t.enabled = false;
                }
                state.WasValid = false;
                return;
            }

            float clamped = Mathf.Min(speed, s.maxTargetSpeed);
            float inject = activity * strength
                           * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.minInjectSpeed, s.fullInjectSpeed, clamped));

            float jump = state.WasValid ? Vector2.Distance(p, state.Last) : float.MaxValue;
            bool teleport = !state.WasValid || jump > s.maxTargetSpeed * Mathf.Max(dt, 1f / 120f);

            t.transform.position = new Vector3(p.x, p.y, 0f);
            if (teleport) {
                // Re-seed FluxyTarget's old-position state so this frame carries no velocity.
                t.enabled = false;
                t.enabled = true;
            } else if (!t.enabled) {
                t.enabled = true;
            }
            state.Last = p;
            state.WasValid = true;

            t.rateOverDistance = s.splatsPerMetre;
            t.scale = new Vector2(size, size);
            t.velocityWeight = s.velocityWeight;
            t.velocityScale = new Vector3(s.velocityGain, s.velocityGain, 0f);
            t.densityWeight = inject;
            t.color = TargetColor(state, clamped, s);
        }

        private Color TargetColor(TargetState state, float speed, ChromaticRainSettings s) {
            float wave = 0.5f + 0.5f * Mathf.Sin(time * 0.6f + state.ColorPhase);
            int a = Mathf.FloorToInt(state.ColorPhase) % 4;
            Color c = Color.Lerp(s.PaletteColor(a), s.PaletteColor(a + 1), wave);
            float hot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 4f, speed)) * 0.45f;
            c = Color.Lerp(c, s.PaletteColor(4), hot);
            c.a = 1f;
            return c;
        }

        private Texture2D BuildHueRing(ChromaticRainSettings s) {
            const int width = 256;
            Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false, true);
            tex.name = "ChromaticRain_HueRing";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Color32[] pixels = new Color32[width];
            for (int x = 0; x < width; x++) {
                float f = x / (float)width * 5f;
                int i0 = Mathf.FloorToInt(f);
                Color c = Color.Lerp(s.PaletteColor(i0), s.PaletteColor(i0 + 1), f - i0);
                pixels[x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f),
                                        (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy() {
            if (runtimeFluid != null) {
                Destroy(runtimeFluid);
            }
            if (runtimeSimulation != null) {
                Destroy(runtimeSimulation);
            }
            if (hueRing != null) {
                Destroy(hueRing);
            }
            for (int i = 0; i < states.Length; i++) {
                DestroySplat(states[i]);
            }
        }

        private static void DestroySplat(TargetState state) {
            if (state.SplatInstance != null) {
                Destroy(state.SplatInstance);
            }
        }
    }
}
