using UnityEngine;
using UnityEngine.Rendering;

namespace HumanEcho {
    /// <summary>
    /// The GPU PARTICLE layer: dots and lit spheres born on the live silhouette, carried by FluXY's velocity
    /// field, gravity and a swirl, optionally
    /// piling up on a floor line. Everything runs in <c>HumanEcho_Particles.compute</c> on one structured
    /// buffer used as a ring (new particles
    /// overwrite the oldest slots) and is drawn by <c>HumanEcho/ParticleDraw</c> with <see
    /// cref="Graphics.RenderPrimitives"/>: no mesh, no CPU
    /// upload, no readback. A particle is a few floats, so 200 000 of them are 9.6 MB.
    ///
    /// The fluid is optional: without an <see cref="IFluidFlow"/> (the smoke layer off or missing) the
    /// particles simply ignore the flow.
    /// </summary>
    public sealed class ParticleLayer : IHumanEchoLayer {
        /// <summary>Bytes of one particle in the buffer: 12 floats, matching the struct in the compute shader
        /// and the draw shader.</summary>
        public const int ParticleStride = 48;

        /// <summary>Columns of the floor pile across the screen width.</summary>
        public const int PileColumns = 512;

        private const int ThreadGroupSize = 64;
        private const int MinCapacity = 1000;
        private const int MaxCapacity = 400000;
        private const float FlowFollowPerSecond = 4f;

        private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        private static readonly int PileId = Shader.PropertyToID("_Pile");
        private static readonly int FieldTexId = Shader.PropertyToID("_FieldTex");
        private static readonly int MotionTexId = Shader.PropertyToID("_MotionTex");
        private static readonly int FluidTexId = Shader.PropertyToID("_FluidTex");
        private static readonly int CapacityId = Shader.PropertyToID("_Capacity");
        private static readonly int SpawnHeadId = Shader.PropertyToID("_SpawnHead");
        private static readonly int SpawnCountId = Shader.PropertyToID("_SpawnCount");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int PileColumnsId = Shader.PropertyToID("_PileColumns");
        private static readonly int TimeId = Shader.PropertyToID("_Time");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int ViewAspectId = Shader.PropertyToID("_ViewAspect");
        private static readonly int PictureSizeId = Shader.PropertyToID("_PictureSize");
        private static readonly int FluidRectId = Shader.PropertyToID("_FluidRect");
        private static readonly int FluidScaleId = Shader.PropertyToID("_FluidScale");
        private static readonly int FlowInfluenceId = Shader.PropertyToID("_FlowInfluence");
        private static readonly int FlowFollowId = Shader.PropertyToID("_FlowFollow");
        private static readonly int MotionKickId = Shader.PropertyToID("_MotionKick");
        private static readonly int MotionScaleId = Shader.PropertyToID("_MotionScale");
        private static readonly int MotionBoostId = Shader.PropertyToID("_MotionBoost");
        private static readonly int SwirlId = Shader.PropertyToID("_Swirl");
        private static readonly int SwirlScaleId = Shader.PropertyToID("_SwirlScale");
        private static readonly int DragId = Shader.PropertyToID("_Drag");
        private static readonly int GravityId = Shader.PropertyToID("_Gravity");
        private static readonly int LifeMinId = Shader.PropertyToID("_LifeMin");
        private static readonly int LifeMaxId = Shader.PropertyToID("_LifeMax");
        private static readonly int SizeMinId = Shader.PropertyToID("_SizeMin");
        private static readonly int SizeMaxId = Shader.PropertyToID("_SizeMax");
        private static readonly int SphereFractionId = Shader.PropertyToID("_SphereFraction");
        private static readonly int EdgeShareId = Shader.PropertyToID("_EdgeShare");
        private static readonly int EmitFromId = Shader.PropertyToID("_EmitFrom");
        private static readonly int ColorModeId = Shader.PropertyToID("_ColorMode");
        private static readonly int HueId = Shader.PropertyToID("_Hue");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int SolidColorId = Shader.PropertyToID("_SolidColor");
        private static readonly int FloorEnabledId = Shader.PropertyToID("_FloorEnabled");
        private static readonly int FloorYId = Shader.PropertyToID("_FloorY");
        private static readonly int PileSecondsId = Shader.PropertyToID("_PileSeconds");
        private static readonly int PileGrowthId = Shader.PropertyToID("_PileGrowth");
        private static readonly int PileMaxId = Shader.PropertyToID("_PileMax");
        private static readonly int ViewHeightId = Shader.PropertyToID("_ViewHeight");
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int LightDirId = Shader.PropertyToID("_LightDir");

        private ComputeShader compute;
        private Material drawMaterial;
        private IFluidFlow flow;
        private MaterialPropertyBlock block;
        private GraphicsBuffer particles;
        private GraphicsBuffer pile;
        private int kernelClear;
        private int kernelSpawn;
        private int kernelUpdate;
        private int kernelDecay;
        private int capacity;
        private int head;
        private float spawnAccumulator;
        private uint frameSeed = 17u;
        private bool needsClear;
        private bool active = true;
        private bool disposed;

        public bool IsReady { get; private set; }

        /// <summary>The settings the layer reads every frame; the controller points it at the active look's
        /// particle settings.</summary>
        public ParticleSettings Settings { get; set; }

        /// <summary>Slots in the pool (0 until the first tick that needed them).</summary>
        public int Capacity {
            get {
                return capacity;
            }
        }

        /// <summary>The pool, for tests and debug readbacks. Null until the first tick.</summary>
        public GraphicsBuffer ParticleBuffer {
            get {
                return particles;
            }
        }

        public GraphicsBuffer PileBuffer {
            get {
                return pile;
            }
        }

        /// <summary>Particles born by the last tick.</summary>
        public int LastSpawnCount { get; private set; }

        public bool Build(ComputeShader computeShader, Shader drawShader, IFluidFlow fluidFlow) {
            if (disposed) {
                return false;
            }
            if (computeShader == null || drawShader == null) {
                Debug.LogError("[HumanEcho] The particle layer is missing its compute shader or its draw shader; it stays off.");
                return false;
            }
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogError("[HumanEcho] This device does not support compute shaders; the particle layer stays off.");
                return false;
            }
            compute = computeShader;
            flow = fluidFlow;
            kernelClear = compute.FindKernel("Clear");
            kernelSpawn = compute.FindKernel("Spawn");
            kernelUpdate = compute.FindKernel("Update");
            kernelDecay = compute.FindKernel("DecayPile");
            drawMaterial = new Material(drawShader);
            drawMaterial.name = "HumanEcho_ParticleDraw (runtime)";
            drawMaterial.renderQueue = LayerQuad.QueueParticles;
            block = new MaterialPropertyBlock();
            IsReady = true;
            return true;
        }

        public void SetActive(bool value) {
            if (value && !active) {
                // A pool that was frozen while the layer was hidden would reappear for a moment as a stale
                // cloud and pile.
                Clear();
            }
            active = value;
        }

        public void Tick(PersonField field, FrameContext frame) {
            if (Simulate(field, frame)) {
                Draw(frame, Settings);
            }
        }

        /// <summary>
        /// Everything of a frame except the draw: spawn, move, pile. Returns false (and does nothing) when
        /// the layer is off or has no field.
        /// Separate from <see cref="Tick"/> so tests can run the simulation without a render loop.
        /// </summary>
        internal bool Simulate(PersonField field, FrameContext frame) {
            if (!IsReady || disposed || !active || Settings == null || !Settings.enabled
                || field == null || field.Field == null || field.Motion == null) {
                LastSpawnCount = 0;
                return false;
            }
            ParticleSettings s = Settings;
            EnsureBuffers(Mathf.Clamp(s.maxParticles, MinCapacity, MaxCapacity));
            if (needsClear) {
                ClearBuffers();
            }

            float dt = Mathf.Clamp(frame.DeltaTime, 0.0005f, 0.1f);
            frameSeed = frameSeed * 1664525u + 1013904223u;

            Vector2 pictureSize = LayerQuad.ContainSize(field.Aspect, frame.ViewWidth, frame.ViewHeight) / frame.ViewHeight;
            Texture fluidTexture = flow != null ? flow.VelocityTexture : null;
            Vector4 rect = fluidTexture != null && flow != null ? flow.TileRect : Vector4.zero;
            bool hasFluid = fluidTexture != null && rect.w > 0f && rect.z > 0f;
            if (!hasFluid) {
                fluidTexture = Texture2D.blackTexture;
            }

            compute.SetInt(CapacityId, capacity);
            compute.SetInt(SeedId, (int)frameSeed);
            compute.SetInt(PileColumnsId, PileColumns);
            compute.SetFloat(TimeId, frame.Time);
            compute.SetFloat(DeltaTimeId, dt);
            compute.SetFloat(ViewAspectId, frame.ViewAspect);
            compute.SetVector(PictureSizeId, new Vector4(pictureSize.x, pictureSize.y, 0f, 0f));
            compute.SetVector(FluidRectId, hasFluid ? rect : Vector4.zero);
            compute.SetFloat(FluidScaleId, hasFluid ? 1f / rect.w : 0f);
            compute.SetFloat(FlowInfluenceId, s.flowInfluence);
            compute.SetFloat(FlowFollowId, FlowFollowPerSecond);
            compute.SetFloat(MotionKickId, s.motionKick);
            compute.SetFloat(MotionScaleId, pictureSize.y);
            compute.SetFloat(MotionBoostId, s.motionBoost);
            compute.SetFloat(SwirlId, s.swirl);
            compute.SetFloat(SwirlScaleId, s.swirlScale);
            compute.SetFloat(DragId, s.drag);
            compute.SetFloat(GravityId, s.gravity);
            compute.SetFloat(LifeMinId, s.lifetimeMin);
            compute.SetFloat(LifeMaxId, Mathf.Max(s.lifetimeMin, s.lifetimeMax));
            compute.SetFloat(SizeMinId, s.sizeMinThousandths * 0.001f);
            compute.SetFloat(SizeMaxId, Mathf.Max(s.sizeMinThousandths, s.sizeMaxThousandths) * 0.001f);
            compute.SetFloat(SphereFractionId, s.sphereFraction);
            compute.SetFloat(EdgeShareId, s.edgeShare);
            compute.SetInt(EmitFromId, (int)s.emitFrom);
            compute.SetInt(ColorModeId, (int)s.colorMode);
            compute.SetFloat(HueId, Mathf.Repeat(frame.Time * s.hueSpeed, 1f));
            compute.SetFloat(SaturationId, s.saturation);
            compute.SetVector(SolidColorId, new Vector4(s.solidColor.linear.r, s.solidColor.linear.g, s.solidColor.linear.b, 1f));
            compute.SetInt(FloorEnabledId, s.floorEnabled ? 1 : 0);
            compute.SetFloat(FloorYId, s.floorHeight - 0.5f);
            compute.SetFloat(PileSecondsId, s.pileSeconds);
            compute.SetFloat(PileGrowthId, s.pileGrowthThousandths * 0.001f);
            compute.SetFloat(PileMaxId, s.pileMaxHeight);

            int count = NextSpawnCount(s.spawnPerSecond, dt);
            LastSpawnCount = count;
            if (count > 0) {
                compute.SetInt(SpawnHeadId, head);
                compute.SetInt(SpawnCountId, count);
                compute.SetBuffer(kernelSpawn, ParticlesId, particles);
                compute.SetTexture(kernelSpawn, FieldTexId, field.Field);
                compute.SetTexture(kernelSpawn, MotionTexId, field.Motion);
                compute.Dispatch(kernelSpawn, Groups(count), 1, 1);
                head = (head + count) % capacity;
            }

            compute.SetBuffer(kernelUpdate, ParticlesId, particles);
            compute.SetBuffer(kernelUpdate, PileId, pile);
            compute.SetTexture(kernelUpdate, FluidTexId, fluidTexture);
            compute.Dispatch(kernelUpdate, Groups(capacity), 1, 1);

            if (s.floorEnabled) {
                compute.SetBuffer(kernelDecay, PileId, pile);
                compute.Dispatch(kernelDecay, Groups(PileColumns), 1, 1);
            }
            return true;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            IsReady = false;
            ReleaseBuffers();
            if (drawMaterial != null) {
                if (Application.isPlaying) {
                    Object.Destroy(drawMaterial);
                } else {
                    Object.DestroyImmediate(drawMaterial);
                }
            }
        }

        /// <summary>Empties the pool and the pile on the next tick.</summary>
        public void Clear() {
            needsClear = true;
            head = 0;
            spawnAccumulator = 0f;
        }

        // Whole particles owed this frame; the remainder carries over so the rate is exact at any frame rate.
        // Never more than a quarter of the pool
        // in one frame (a long hitch must not wipe every live particle).
        private int NextSpawnCount(float perSecond, float dt) {
            spawnAccumulator += Mathf.Max(0f, perSecond) * dt;
            int count = Mathf.FloorToInt(spawnAccumulator);
            spawnAccumulator -= count;
            return Mathf.Clamp(count, 0, capacity / 4);
        }

        private void Draw(FrameContext frame, ParticleSettings s) {
            block.SetBuffer(ParticlesId, particles);
            block.SetFloat(ViewHeightId, frame.ViewHeight);
            block.SetFloat(GainId, s.gain);
            block.SetVector(LightDirId, new Vector4(-0.45f, 0.6f, 0.65f, 0f));
            RenderParams parameters = new RenderParams(drawMaterial);
            parameters.worldBounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
            parameters.matProps = block;
            parameters.shadowCastingMode = ShadowCastingMode.Off;
            parameters.receiveShadows = false;
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, capacity);
        }

        private void EnsureBuffers(int wanted) {
            if (particles != null && capacity == wanted) {
                return;
            }
            ReleaseBuffers();
            capacity = wanted;
            particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, ParticleStride);
            pile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, PileColumns, sizeof(uint));
            head = 0;
            needsClear = true;
        }

        private void ClearBuffers() {
            compute.SetInt(CapacityId, capacity);
            compute.SetInt(PileColumnsId, PileColumns);
            compute.SetBuffer(kernelClear, ParticlesId, particles);
            compute.SetBuffer(kernelClear, PileId, pile);
            compute.Dispatch(kernelClear, Groups(Mathf.Max(capacity, PileColumns)), 1, 1);
            needsClear = false;
        }

        private void ReleaseBuffers() {
            if (particles != null) {
                particles.Release();
                particles = null;
            }
            if (pile != null) {
                pile.Release();
                pile = null;
            }
            capacity = 0;
        }

        private static int Groups(int threads) {
            return (threads + ThreadGroupSize - 1) / ThreadGroupSize;
        }
    }
}
