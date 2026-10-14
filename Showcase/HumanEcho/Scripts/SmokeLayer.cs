using System;

using Fluxy;

using UnityEngine;
using UnityEngine.Rendering;

namespace HumanEcho {
    /// <summary>
    /// The FLUXY SMOKE LAYER: one <see cref="FluxySolver"/>, one screen-filling <see cref="FluxyContainer"/>
    /// and one <see cref="FluxyTarget"/> that
    /// stamps the live person into the fluid every frame, as dye (the silhouette, in the picture's colours
    /// mixed with a drifting hue) and as
    /// velocity (how the silhouette moved). The fluid then carries the dye away from the body: smoke that
    /// follows the performer.
    ///
    /// The FluXY objects are built here at run time (FluXY's own Resources materials are copied, never
    /// edited), so the layer works in any scene.
    /// Follows the existing showcases (Fluid Disruption, Chromatic Rain): its own solver, a container whose
    /// procedural mesh is bound explicitly, the
    /// solver rebuilt when the resolution changes, the target configured neutral and driven every frame.
    /// FluXY's own LateUpdate steps the solver, so
    /// <see cref="Tick"/> must run in Update.
    ///
    /// The layer never reads the camera itself: it is handed the <see cref="PersonField"/>.
    /// </summary>
    public sealed class SmokeLayer : IHumanEchoLayer, IFluidFlow {
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int HueSpreadId = Shader.PropertyToID("_HueSpread");
        private static readonly int PictureMixId = Shader.PropertyToID("_PictureMix");
        private static readonly int MotionTexId = Shader.PropertyToID("_MotionTex");
        private static readonly int PackGainId = Shader.PropertyToID("_PackGain");
        private static readonly int VelocityConfidenceId = Shader.PropertyToID("_VelocityConfidence");
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int LookId = Shader.PropertyToID("_Look");
        private static readonly int DotSizeId = Shader.PropertyToID("_DotSize");
        private static readonly int DotSoftnessId = Shader.PropertyToID("_DotSoftness");
        private static readonly int DotFlowId = Shader.PropertyToID("_DotFlow");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        private const string SimulationResource = "Materials/FluidSimulation";
        private const string SplatResource = "Materials/Splat";
        private const string NoiseResource = "Textures/NoiseTexture";

        private GameObject root;
        private FluxySolver solver;
        private FluxyContainer container;
        private MeshRenderer containerRenderer;
        private FluxyTarget target;
        private FluxyStorage storage;
        private Material simulation;
        private Material splat;
        private Material inject;
        private Material smoke;
        private RenderTexture dye;
        private RenderTexture packed;
        private float viewWidth;
        private float viewHeight;
        private int appliedResolution;
        private int appliedSupersampling;
        private bool disposed;

        public bool IsReady { get; private set; }

        /// <summary>The settings the layer reads every frame; the controller points it at the active look's
        /// smoke settings.</summary>
        public SmokeSettings Settings { get; set; }

        /// <summary>FluXY's dye buffer (an atlas), for debug views. Null while the layer is off.</summary>
        public Texture StateTexture {
            get {
                FluxyStorage.Framebuffer fb = Framebuffer();
                return fb != null ? fb.stateA : null;
            }
        }

        /// <summary>FluXY's velocity buffer (an atlas: x, y velocity in atlas units per second). Null while
        /// the layer is off.</summary>
        public Texture VelocityTexture {
            get {
                FluxyStorage.Framebuffer fb = Framebuffer();
                return fb != null ? fb.velocityA : null;
            }
        }

        /// <summary>Where this container lives inside the atlas textures: x, y offset, z, w size, all atlas
        /// UV. Zero until the solver has run once.</summary>
        public Vector4 TileRect {
            get {
                return solver != null && container != null ? solver.GetContainerUVRect(container) : Vector4.zero;
            }
        }

        /// <summary>The velocity pattern last stamped (FluXY packing: 0.5 = still), for debug
        /// views.</summary>
        public Texture PackedMotion {
            get {
                return packed;
            }
        }

        public Texture DyePattern {
            get {
                return dye;
            }
        }

        public FluxySolver Solver {
            get {
                return solver;
            }
        }

        public FluxyContainer Container {
            get {
                return container;
            }
        }

        /// <summary>Builds the layer under <paramref name="parent"/>. False (and a log line) when something
        /// it needs is missing.</summary>
        public bool Build(Transform parent, SmokeSettings s, Shader injectShader, Shader smokeShader, float width, float height) {
            if (disposed) {
                return false;
            }
            Material simulationSource = Resources.Load<Material>(SimulationResource);
            Material splatSource = Resources.Load<Material>(SplatResource);
            if (simulationSource == null || splatSource == null || injectShader == null || smokeShader == null) {
                Debug.LogError("[HumanEcho] The smoke layer is missing FluXY's Resources materials or one of its shaders; it stays off.");
                return false;
            }
            viewWidth = width;
            viewHeight = height;

            simulation = new Material(simulationSource);
            simulation.name = "HumanEcho_Simulation (runtime)";
            splat = new Material(splatSource);
            splat.name = "HumanEcho_Splat (runtime)";
            inject = new Material(injectShader);
            inject.name = "HumanEcho_SmokeInject (runtime)";
            smoke = new Material(smokeShader);
            smoke.name = "HumanEcho_Smoke (runtime)";

            storage = ScriptableObject.CreateInstance<FluxyStorage>();
            storage.name = "HumanEcho FluxyStorage (runtime)";
            storage.memoryBudget = 96;
            storage.densityPrecision = FluxyStorage.FluidTexturePrecision.Half;
            storage.velocityPrecision = FluxyStorage.FluidTexturePrecision.Half;

            // Built inactive so every field is set before FluXY's OnEnable runs.
            root = new GameObject("HumanEcho Smoke (FluXY)");
            root.SetActive(false);
            root.transform.SetParent(parent, false);

            solver = root.AddComponent<FluxySolver>();
            solver.storage = storage;
            solver.simulationMaterial = simulation;
            solver.desiredResolution = s.resolution;
            solver.densitySupersampling = s.supersampling;
            solver.maxTimestep = 0.008f;
            solver.maxSteps = s.maxSteps;
            solver.pressureSolver = FluxySolver.PressureSolver.Separable;
            solver.readable = FluxySolver.ReadbackMode.None;
            appliedResolution = s.resolution;
            appliedSupersampling = s.supersampling;

            container = root.AddComponent<FluxyContainer>();
            containerRenderer = root.GetComponent<MeshRenderer>();
            containerRenderer.sharedMaterial = smoke;
            containerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            containerRenderer.receiveShadows = false;
            containerRenderer.lightProbeUsage = LightProbeUsage.Off;
            containerRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ConfigureContainer(s);
            container.solver = solver;

            GameObject targetObject = new GameObject("Person Target");
            targetObject.transform.SetParent(root.transform, false);
            target = targetObject.AddComponent<FluxyTarget>();
            target.enabled = false;
            ConfigureTarget(Resources.Load<Texture>(NoiseResource));
            container.targets = new FluxyTarget[] { target };

            root.SetActive(true);
            BindMesh();
            IsReady = true;
            return true;
        }

        /// <summary>The layer's frame: switches the fluid on or off with <see cref="SmokeSettings.enabled"/>
        /// and otherwise ticks it.</summary>
        public void Tick(PersonField field, FrameContext frame) {
            if (!IsReady || disposed || Settings == null) {
                return;
            }
            if (!Settings.enabled) {
                if (root.activeSelf) {
                    SetActive(false);
                }
                return;
            }
            if (!root.activeSelf) {
                SetActive(true);
            }
            Tick(field, frame.DeltaTime, frame.Time, Settings);
        }

        /// <summary>One frame, BEFORE FluXY's own LateUpdate (call it from Update). Stamps the person into
        /// the fluid and applies the live settings.</summary>
        public void Tick(PersonField field, float deltaTime, float time, SmokeSettings s) {
            if (!IsReady || disposed || field == null || field.Field == null || field.Motion == null) {
                return;
            }
            ApplyLive(s);
            EnsurePatterns(field.Width, field.Height);

            inject.SetColor(TintId, s.TintAt(time).linear);
            inject.SetFloat(HueSpreadId, s.hueSpread);
            inject.SetFloat(PictureMixId, s.pictureMix);
            Graphics.Blit(field.Field, dye, inject, 0);

            Vector4 rect = TileRect;
            float rectHeight = rect.w > 0f ? rect.w : 1f / Mathf.Max(1f, viewWidth / Mathf.Max(0.01f, viewHeight));
            inject.SetTexture(MotionTexId, field.Motion);
            // The motion is in heights of the PICTURE per second; the container is the whole screen, so
            // convert (the picture is contain-fitted and
            // is shorter than the screen when it is wider than the window).
            float pictureHeight = Mathf.Min(1f, (viewWidth / Mathf.Max(0.01f, viewHeight)) / Mathf.Max(0.01f, field.Aspect));
            inject.SetFloat(PackGainId, s.motionGain * rectHeight * pictureHeight);
            inject.SetFloat(VelocityConfidenceId, s.motionConfidence);
            Graphics.Blit(null, packed, inject, 1);

            float fieldAspect = field.Aspect;
            float viewAspect = viewWidth / Mathf.Max(0.01f, viewHeight);
            // FluXY's override-position splat size is a FRACTION of the container; this scale fits the
            // picture inside the screen without
            // stretching it (contain). For equal aspects it is the same (1 / aspect) the baseline of Fluid
            // Disruption uses.
            float fit = 1f / Mathf.Max(fieldAspect, viewAspect);
            target.scale = new Vector2(fit, fit);
            target.densityTexture = dye;
            target.velocityTexture = packed;
            target.densityWeight = PerFrame(s.dyeWeight, deltaTime);
            target.velocityWeight = PerFrame(s.velocityWeight, deltaTime);
            if (!target.enabled) {
                target.enabled = true;
            }
        }

        /// <summary>Turns the layer on or off. Off removes the solver's buffers entirely (FluXY disposes them
        /// in OnDisable).</summary>
        public void SetActive(bool active) {
            if (root != null && root.activeSelf != active) {
                root.SetActive(active);
                if (active) {
                    BindMesh();
                    container.Clear();
                }
            }
        }

        /// <summary>Empties the fluid.</summary>
        public void Clear() {
            if (IsReady && container != null && container.isActiveAndEnabled) {
                container.Clear();
            }
        }

        /// <summary>Resizes the container when the screen aspect changes.</summary>
        public void Resize(float width, float height) {
            viewWidth = width;
            viewHeight = height;
            if (!IsReady) {
                return;
            }
            container.size = new Vector3(width, height, 1f);
            container.UpdateContainerShape();
            // Re-register so the solver repacks its tiles.
            container.enabled = false;
            container.enabled = true;
            BindMesh();
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            IsReady = false;
            DestroyObject(root);
            DestroyObject(simulation);
            DestroyObject(splat);
            DestroyObject(inject);
            DestroyObject(smoke);
            DestroyObject(storage);
            Release(ref dye);
            Release(ref packed);
        }

        private FluxyStorage.Framebuffer Framebuffer() {
            if (solver == null || !solver.isActiveAndEnabled) {
                return null;
            }
            return solver.framebuffer;
        }

        private void ConfigureContainer(SmokeSettings s) {
            container.containerShape = FluxyContainer.ContainerShape.Plane;
            container.subdivisions = Vector2Int.one;
            container.size = new Vector3(viewWidth, viewHeight, 1f);
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
            container.velocityScale = 1f;
            container.accelerationScale = 0f;
            container.gravity = Vector3.zero;
            container.adhesion = 0f;
            container.surfaceTension = 0f;
            container.UpdateContainerShape();
            ApplyContainerLive(s);
        }

        private void ApplyContainerLive(SmokeSettings s) {
            container.viscosity = s.viscosity;
            container.turbulence = s.turbulence;
            container.pressure = s.pressure;
            container.buoyancy = s.buoyancy;
            container.dissipation = new Vector4(s.dissipation, s.dissipation, s.dissipation, s.dissipation);
            container.externalForce = new Vector3(s.drift.x, s.drift.y, 0f);
            FluxyContainer.EdgeFalloff falloff = new FluxyContainer.EdgeFalloff();
            falloff.densityEdgeWidth = s.edgeWidth;
            falloff.densityFalloffRate = s.edgeDrain;
            falloff.velocityEdgeWidth = s.edgeWidth;
            falloff.velocityFalloffRate = s.edgeDrain * 2f;
            container.edgeFalloff = falloff;
        }

        private void ApplyLive(SmokeSettings s) {
            ApplyContainerLive(s);
            containerRenderer.enabled = s.drawn;
            smoke.SetFloat(GainId, s.gain);
            smoke.SetFloat(OpacityId, s.opacity);
            smoke.SetFloat(LookId, (float)s.look);
            smoke.SetFloat(DotSizeId, s.dotsPerScreenHeight);
            smoke.SetFloat(DotSoftnessId, s.dotSoftness);
            smoke.SetFloat(DotFlowId, s.dotFlow);
            smoke.SetFloat(AspectId, viewWidth / Mathf.Max(0.01f, viewHeight));
            solver.maxSteps = s.maxSteps;
            if (s.resolution != appliedResolution || s.supersampling != appliedSupersampling) {
                // FluXY only rebuilds its buffers when the solver is re-enabled, which empties them.
                solver.enabled = false;
                solver.desiredResolution = s.resolution;
                solver.densitySupersampling = s.supersampling;
                solver.enabled = true;
                appliedResolution = s.resolution;
                appliedSupersampling = s.supersampling;
                container.Clear();
            }
        }

        // The container's MeshFilter must show the container's own procedural mesh, built from its size.
        private void BindMesh() {
            MeshFilter filter = container.GetComponent<MeshFilter>();
            Mesh mesh = container.containerMesh;
            if (filter != null && mesh != null && filter.sharedMesh != mesh) {
                filter.sharedMesh = mesh;
            }
        }

        // Gives the target its neutral FluXY configuration; the weights and the scale are set every frame.
        private void ConfigureTarget(Texture noise) {
            target.splatMaterial = splat;
            target.noiseTexture = noise;
            target.rateOverSteps = 1;
            target.rateOverTime = 0f;
            target.rateOverDistance = 0f;
            target.overridePosition = true;
            target.position = Vector2.zero;
            target.overrideRotation = true;
            target.rotation = 0f;
            target.scaleWithDistance = false;
            target.scaleWithTransform = false;
            target.positionRandomness = 0f;
            target.scaleRandomness = 0f;
            target.rotationRandomness = 0f;
            target.maxRelativeVelocity = 8f;
            target.velocityScale = Vector3.zero;
            target.angularVelocityScale = 0f;
            target.force = Vector3.zero;
            target.torque = 0f;
            target.densityNoise = 0f;
            target.velocityNoise = 0f;
            target.srcBlend = BlendMode.SrcAlpha;
            target.dstBlend = BlendMode.OneMinusSrcAlpha;
            target.blendOp = BlendOp.Add;
            target.color = Color.white;
            target.densityWeight = 0f;
            target.velocityWeight = 0f;
        }

        // The per-frame share that reproduces a per-frame weight at 60 fps whatever the real frame rate is.
        private static float PerFrame(float weightAt60, float deltaTime) {
            float w = Mathf.Clamp01(weightAt60);
            if (w >= 1f) {
                return 1f;
            }
            return 1f - Mathf.Pow(1f - w, Mathf.Clamp(deltaTime, 0.001f, 0.1f) * 60f);
        }

        private void EnsurePatterns(int width, int height) {
            if (dye != null && dye.width == width && dye.height == height) {
                return;
            }
            Release(ref dye);
            Release(ref packed);
            dye = CreatePattern("HumanEcho Dye Pattern", width, height);
            packed = CreatePattern("HumanEcho Velocity Pattern", width, height);
        }

        private static RenderTexture CreatePattern(string name, int width, int height) {
            RenderTexture texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Create();
            return texture;
        }

        private static void Release(ref RenderTexture texture) {
            if (texture == null) {
                return;
            }
            texture.Release();
            DestroyObject(texture);
            texture = null;
        }

        private static void DestroyObject(UnityEngine.Object value) {
            if (value == null) {
                return;
            }
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(value);
            } else {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
