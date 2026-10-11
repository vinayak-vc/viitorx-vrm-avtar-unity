using System.Text;

using Fluxy;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// Owns the FluXY side of the showcase: one <see cref="FluxySolver"/>, one screen-filling
    /// <see cref="FluxyContainer"/>, the rendering material, the quality level, and the persistent
    /// baseline field.
    ///
    /// Baseline: when the container is cleared FluXY blits the container's clearTexture into the dye
    /// buffer. That texture is a procedurally built field of wide, wavy colour bands, so the very first
    /// frame already shows a populated liquid, and "reset" returns to that same recognisable look. The
    /// baseline sources (<see cref="FluidBaselineSources"/>) then keep topping it up. Nothing here
    /// depends on tracking, and a person entering or leaving never clears the solver.
    ///
    /// Uses only FluXY's own public fields and methods; no FluXY file is modified.
    /// </summary>
    public sealed class FluidDisruptionFluid : MonoBehaviour {
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int CrestId = Shader.PropertyToID("_Crest");
        private static readonly int ReliefId = Shader.PropertyToID("_Relief");
        private static readonly int ChromaId = Shader.PropertyToID("_Chroma");
        private static readonly int StreakId = Shader.PropertyToID("_StreakLength");
        private static readonly int GlowId = Shader.PropertyToID("_MotionGlow");
        private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
        private static readonly int EnhanceId = Shader.PropertyToID("_Enhance");
        private static readonly int BgLowId = Shader.PropertyToID("_BgLow");
        private static readonly int BgHighId = Shader.PropertyToID("_BgHigh");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int DyeTexelId = Shader.PropertyToID("_DyeTexel");
        private static readonly int OverscanId = Shader.PropertyToID("_Overscan");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int DetailId = Shader.PropertyToID("_Detail");
        private static readonly int RampId = Shader.PropertyToID("_Ramp");
        private static readonly int DetailAmountId = Shader.PropertyToID("_DetailAmount");
        private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        private static readonly int DetailAdvectionId = Shader.PropertyToID("_DetailAdvection");
        private static readonly int LayersId = Shader.PropertyToID("_Layers");
        private static readonly int LayerDepthId = Shader.PropertyToID("_LayerDepth");
        private static readonly int RampGlowId = Shader.PropertyToID("_RampGlow");

        private const int SeedWidth = 256;
        private const int SeedHeight = 144;

        [SerializeField] private FluxySolver solver;
        [SerializeField] private FluxyContainer container;
        [SerializeField] private MeshRenderer containerRenderer;
        [SerializeField] private Material fluidMaterial;
        [SerializeField] private Material simulationMaterial;
        [SerializeField] private Texture detailTexture;

        private Material runtimeFluid;
        private Material runtimeSimulation;
        private Texture2D seed;
        private Texture2D ramp;
        private int appliedResolution;
        private int appliedSupersampling;
        private float viewWidth = 4.27f;
        private float viewHeight = 2.4f;
        private bool simulationOn = true;

        public bool IsReady { get; private set; }

        /// <summary>The baseline pattern: the field every clear starts from and every pass relaxes toward.</summary>
        public Texture BaselineField {
            get {
                return seed;
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

        /// <summary>Resolution FluXY actually allocated for the velocity buffer (0 while off).</summary>
        public int VelocityResolution {
            get {
                FluxyStorage.Framebuffer fb = Framebuffer();
                return fb != null && fb.velocityA != null ? fb.velocityA.width : 0;
            }
        }

        /// <summary>Resolution of the dye buffer (0 while off).</summary>
        public int DyeResolution {
            get {
                FluxyStorage.Framebuffer fb = Framebuffer();
                return fb != null && fb.stateA != null ? fb.stateA.width : 0;
            }
        }

        public Texture StateTexture {
            get {
                FluxyStorage.Framebuffer fb = Framebuffer();
                return fb != null ? fb.stateA : null;
            }
        }

        public Texture VelocityTexture {
            get {
                FluxyStorage.Framebuffer fb = Framebuffer();
                return fb != null ? fb.velocityA : null;
            }
        }

        public bool SimulationOn {
            get {
                return simulationOn;
            }
        }

        private FluxyStorage.Framebuffer Framebuffer() {
            if (solver == null || !solver.isActiveAndEnabled) {
                return null;
            }
            return solver.framebuffer;
        }

        /// <summary>Checks the wiring and returns a short report; empty when everything is in place.</summary>
        public string DescribeProblems() {
            StringBuilder sb = new StringBuilder();
            if (solver == null) {
                sb.Append("FluxySolver missing. ");
            } else if (solver.storage == null) {
                sb.Append("Solver has no FluxyStorage. ");
            } else if (solver.simulationMaterial == null) {
                sb.Append("Solver has no simulation material. ");
            }
            if (container == null) {
                sb.Append("FluxyContainer missing. ");
            } else if (container.solver == null) {
                sb.Append("Container has no solver. ");
            }
            if (containerRenderer == null || containerRenderer.sharedMaterial == null) {
                sb.Append("Container renderer or material missing. ");
            }
            if (IsReady && simulationOn && Framebuffer() == null) {
                sb.Append("Solver has no framebuffer (is it enabled?). ");
            }
            return sb.ToString();
        }

        public bool Initialize(FluidDisruptionSettings s, FluidQuality quality, bool enhancements, float width, float height) {
            if (solver == null || container == null || containerRenderer == null
                || fluidMaterial == null || simulationMaterial == null) {
                Debug.LogError("[FluidDisruption] The fluid is missing a FluXY reference or material. Run "
                               + "Showcase > Fluid Disruption > Build Showcase Scene to rebuild it.", this);
                return false;
            }
            if (solver.storage == null) {
                Debug.LogError("[FluidDisruption] The FluxySolver has no FluxyStorage asset assigned.", this);
                return false;
            }

            viewWidth = width;
            viewHeight = height;

            runtimeFluid = new Material(fluidMaterial);
            runtimeFluid.name = "FluidDisruption_Fluid (runtime)";
            containerRenderer.sharedMaterial = runtimeFluid;

            runtimeSimulation = new Material(simulationMaterial);
            runtimeSimulation.name = "FluidDisruption_Simulation (runtime)";

            seed = BuildSeedTexture(s);
            ramp = BuildRampTexture(s);
            runtimeFluid.SetTexture(RampId, ramp);
            if (detailTexture != null) {
                runtimeFluid.SetTexture(DetailId, detailTexture);
            }

            // The scene load enabled the solver with the serialized values; rebuild it once with the
            // runtime copy of the material and the live quality profile.
            QualityProfile profile = s.ProfileFor(quality);
            solver.enabled = false;
            solver.simulationMaterial = runtimeSimulation;
            solver.desiredResolution = profile.resolution;
            solver.densitySupersampling = profile.supersampling;
            solver.maxSteps = profile.maxSteps;
            appliedResolution = profile.resolution;
            appliedSupersampling = profile.supersampling;
            solver.enabled = true;

            ConfigureContainer(s);
            IsReady = true;
            ApplyLive(s, quality, enhancements);
            ResetToBaseline();
            return true;
        }

        private void ConfigureContainer(FluidDisruptionSettings s) {
            container.containerShape = FluxyContainer.ContainerShape.Plane;
            container.subdivisions = Vector2Int.one;
            container.size = new Vector3(viewWidth * s.overscan, viewHeight * s.overscan, 1f);
            container.lookAt = null;
            container.projectFrom = null;
            container.clearTexture = seed;
            container.clearColor = Color.white;
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
            container.buoyancy = 0f;
            container.UpdateContainerShape();
            BindMesh();
        }

        // The MeshFilter must show the container's own procedural mesh, which is built from the
        // container's size. A scene saved in the Editor can carry an older mesh of the default size, so
        // the container's mesh is assigned explicitly rather than trusted to be the one on the filter.
        private void BindMesh() {
            MeshFilter filter = container.GetComponent<MeshFilter>();
            Mesh mesh = container.containerMesh;
            if (filter != null && mesh != null && filter.sharedMesh != mesh) {
                filter.sharedMesh = mesh;
            }
        }

        /// <summary>The settings that may change while running. Cheap: no allocation.</summary>
        public void ApplyLive(FluidDisruptionSettings s, FluidQuality quality, bool enhancements) {
            if (!IsReady) {
                return;
            }
            float d = s.dyeDissipation;
            container.dissipation = new Vector4(d, d, d, d * 4f);
            container.viscosity = s.viscosity;
            container.turbulence = s.turbulence;
            container.pressure = s.pressure;
            container.externalForce = new Vector3(s.drift.x, s.drift.y, 0f);
            FluxyContainer.EdgeFalloff falloff = new FluxyContainer.EdgeFalloff();
            falloff.densityEdgeWidth = s.edgeWidth;
            falloff.densityFalloffRate = s.edgeDrain;
            falloff.velocityEdgeWidth = s.edgeWidth;
            falloff.velocityFalloffRate = s.edgeDrain * 2f;
            container.edgeFalloff = falloff;

            runtimeFluid.SetFloat(GainId, s.dyeGain);
            runtimeFluid.SetFloat(CrestId, s.crestStrength);
            runtimeFluid.SetFloat(ReliefId, s.relief);
            runtimeFluid.SetFloat(ChromaId, s.chromaticSplit);
            runtimeFluid.SetFloat(StreakId, s.streakLength);
            runtimeFluid.SetFloat(GlowId, s.motionGlow);
            runtimeFluid.SetFloat(VignetteId, s.vignette);
            runtimeFluid.SetFloat(EnhanceId, enhancements ? 1f : 0f);
            runtimeFluid.SetColor(BgLowId, s.backgroundLow.linear);
            runtimeFluid.SetColor(BgHighId, s.backgroundHigh.linear);
            runtimeFluid.SetColor(AccentId, s.PaletteColor(s.accentColour).linear);
            int dyeRes = DyeResolution;
            runtimeFluid.SetFloat(DyeTexelId, dyeRes > 0 ? 1f / dyeRes : 0.002f);
            runtimeFluid.SetFloat(OverscanId, s.overscan);
            runtimeFluid.SetFloat(AspectId, viewWidth / Mathf.Max(0.01f, viewHeight));
            runtimeFluid.SetFloat(DetailAmountId, s.detailAmount);
            runtimeFluid.SetFloat(DetailScaleId, s.detailScale);
            runtimeFluid.SetFloat(DetailAdvectionId, s.detailAdvection);
            runtimeFluid.SetFloat(LayersId, s.layers);
            runtimeFluid.SetFloat(LayerDepthId, s.layerDepth);
            runtimeFluid.SetFloat(RampGlowId, s.rampGlow);

            QualityProfile profile = s.ProfileFor(quality);
            if (profile.resolution != appliedResolution || profile.supersampling != appliedSupersampling) {
                ApplyQuality(s, quality);
            }
            solver.maxSteps = profile.maxSteps;
        }

        /// <summary>Applies the active quality profile. FluXY only rebuilds its buffers when the solver
        /// is re-enabled, which empties them, so the baseline field is seeded again afterwards.</summary>
        public void ApplyQuality(FluidDisruptionSettings s, FluidQuality quality) {
            QualityProfile profile = s.ProfileFor(quality);
            solver.enabled = false;
            solver.desiredResolution = profile.resolution;
            solver.densitySupersampling = profile.supersampling;
            solver.maxSteps = profile.maxSteps;
            appliedResolution = profile.resolution;
            appliedSupersampling = profile.supersampling;
            solver.enabled = true;
            ResetToBaseline();
        }

        /// <summary>Back to the seeded baseline field. Not used automatically: the fluid settles by
        /// itself, this exists for diagnosis and for a manual reset during a demonstration.</summary>
        public void ResetToBaseline() {
            if (IsReady && container != null && container.isActiveAndEnabled) {
                container.Clear();
            }
        }

        /// <summary>Switches the simulation off or on (diagnosis). Off removes the solver's buffers, and
        /// the screen then shows only the background.</summary>
        public void SetSimulationOn(bool on) {
            if (simulationOn == on) {
                return;
            }
            simulationOn = on;
            gameObject.SetActive(on);
            if (on) {
                ResetToBaseline();
            }
        }

        public void SetEnhancements(bool on) {
            if (runtimeFluid != null) {
                runtimeFluid.SetFloat(EnhanceId, on ? 1f : 0f);
            }
        }

        /// <summary>Resizes the container when the screen aspect changes.</summary>
        public void Resize(FluidDisruptionSettings s, float width, float height) {
            viewWidth = width;
            viewHeight = height;
            if (!IsReady) {
                return;
            }
            container.size = new Vector3(width * s.overscan, height * s.overscan, 1f);
            container.UpdateContainerShape();
            // Re-register so the solver repacks its tiles.
            container.enabled = false;
            container.enabled = true;
            BindMesh();
        }

        // A field of wide, wavy colour bands in the palette, as dye (premultiplied colour in rgb,
        // thickness in a). It is what the calm screen looks like before any source has contributed.
        private Texture2D BuildSeedTexture(FluidDisruptionSettings s) {
            Texture2D tex = new Texture2D(SeedWidth, SeedHeight, TextureFormat.RGBAHalf, false, true);
            tex.name = "FluidDisruption_Seed";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[SeedWidth * SeedHeight];
            Color c0 = s.PaletteColor(3).linear;
            Color c1 = s.PaletteColor(1).linear;
            Color c2 = s.PaletteColor(2).linear;
            Color c3 = s.PaletteColor(4).linear;
            Color c4 = s.PaletteColor(6).linear;
            for (int y = 0; y < SeedHeight; y++) {
                float v = (y + 0.5f) / SeedHeight;
                for (int x = 0; x < SeedWidth; x++) {
                    float u = (x + 0.5f) / SeedWidth;
                    // Where we are along the colour cycle: horizontal bands bent by two slow waves.
                    float band = v * 4.0f
                                 + 0.42f * Mathf.Sin(u * 6.2832f * 1.1f + v * 4.0f)
                                 + 0.20f * Mathf.Sin(u * 6.2832f * 2.3f - v * 3.0f + 1.3f);
                    float cyc = Mathf.Repeat(band, 4f);
                    Color c;
                    if (cyc < 1f) {
                        c = Color.Lerp(c0, c1, Mathf.SmoothStep(0f, 1f, cyc));
                    } else if (cyc < 2f) {
                        c = Color.Lerp(c1, c2, Mathf.SmoothStep(0f, 1f, cyc - 1f));
                    } else if (cyc < 3f) {
                        c = Color.Lerp(c2, c3, Mathf.SmoothStep(0f, 1f, cyc - 2f));
                    } else {
                        c = Color.Lerp(c3, c0, Mathf.SmoothStep(0f, 1f, cyc - 3f));
                    }
                    // A faint magenta accent drifting through the bands.
                    float accent = Mathf.SmoothStep(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(u * 6.2832f * 0.8f + v * 9f + 2f));
                    c = Color.Lerp(c, c4, accent * 0.22f);
                    // How much dye there is: thicker in the bands' cores, thinner between them.
                    float thick = s.fieldThickness * (1f + 0.55f * Mathf.Sin(band * 3.1416f * 2f + u * 3f));
                    pixels[y * SeedWidth + x] = new Color(c.r * thick, c.g * thick, c.b * thick, thick);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // Dye thickness to colour: midnight, electric blue, cyan, turquoise, lavender. Read by the shader
        // to colour the thick parts of the liquid, so crests are bright and thin edges stay deep blue.
        private Texture2D BuildRampTexture(FluidDisruptionSettings s) {
            const int width = 256;
            Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false, true);
            tex.name = "FluidDisruption_Ramp";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color[] stops = new Color[] {
                Color.black,
                s.PaletteColor(3).linear,
                s.PaletteColor(1).linear,
                s.PaletteColor(2).linear,
                s.PaletteColor(5).linear
            };
            Color32[] pixels = new Color32[width];
            for (int x = 0; x < width; x++) {
                float f = x / (float)(width - 1) * (stops.Length - 1);
                int i0 = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(f));
                Color c = Color.Lerp(stops[i0], stops[i0 + 1], f - i0);
                pixels[x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f),
                                        (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy() {
            if (ramp != null) {
                Destroy(ramp);
            }
            if (runtimeFluid != null) {
                Destroy(runtimeFluid);
            }
            if (runtimeSimulation != null) {
                Destroy(runtimeSimulation);
            }
            if (seed != null) {
                Destroy(seed);
            }
        }
    }
}
