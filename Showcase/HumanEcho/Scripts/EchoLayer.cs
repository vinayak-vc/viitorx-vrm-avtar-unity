using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// The ECHO LAYER: the person's mask of a moment ago, redrawn as up to 16 copies (flat, dotted or outline
    /// only), each older one more
    /// faded, shifted, resized, recoloured and optionally waved. It owns the <see cref="EchoRing"/> that
    /// remembers the masks.
    ///
    /// Drawing is ONE full-screen quad. Its shader (<c>HumanEcho/Echo</c>, pass 1) loops over the copies from
    /// the oldest to the newest and
    /// composites them premultiplied, newest on top, reading each copy's mask from its slice of the ring's
    /// Texture2DArray. The per-copy
    /// numbers (slice, colour, opacity, shift, size) are written into three preallocated vector arrays every
    /// frame, so the layer allocates
    /// nothing while running and costs the same whether it draws 1 copy or 16 (the loop length is a uniform).
    ///
    /// The full-screen quad (not a picture-sized one) lets a copy that is shifted sideways or waved leave the
    /// picture's rectangle.
    /// The picture is still "contain" fitted: the shader maps screen uv to picture uv with <c>_PicScale</c>,
    /// and anything outside the
    /// picture has no mask.
    ///
    /// Create it with <c>new EchoLayer(parent, shader)</c>; <see cref="IsReady"/> is false (one error logged)
    /// if the shader is missing.
    /// The controller points <see cref="Settings"/> at the active look's echo settings every frame.
    /// </summary>
    public sealed class EchoLayer : IHumanEchoLayer {
        private const float QuadDepth = 0f;
        private const float MinViewSize = 0.01f;
        private const float TwoPi = 6.2831853f;

        /// <summary>
        /// Width of the line of the Outline look, in screen heights / 1000. The echo settings have no width
        /// of their own: a ghost's
        /// outline is a fixed thin line, the (tunable) neon line is the outline layer's job.
        /// </summary>
        public const float OutlineWidthThousandths = 2.2f;

        private static readonly int EchoRingId = Shader.PropertyToID("_EchoRing");
        private static readonly int PicScaleId = Shader.PropertyToID("_PicScale");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int CopyCountId = Shader.PropertyToID("_CopyCount");
        private static readonly int LookId = Shader.PropertyToID("_Look");
        private static readonly int DotsPerHeightId = Shader.PropertyToID("_DotsPerHeight");
        private static readonly int DotSoftnessId = Shader.PropertyToID("_DotSoftness");
        private static readonly int EdgeContrastId = Shader.PropertyToID("_EdgeContrast");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int WaveAmountId = Shader.PropertyToID("_WaveAmount");
        private static readonly int WaveFrequencyId = Shader.PropertyToID("_WaveFrequency");
        private static readonly int WavePhaseId = Shader.PropertyToID("_WavePhase");
        private static readonly int CopyColorId = Shader.PropertyToID("_CopyColor");
        private static readonly int CopyXformId = Shader.PropertyToID("_CopyXform");
        private static readonly int CopyParamId = Shader.PropertyToID("_CopyParam");

        private readonly Vector4[] copyColor = new Vector4[EchoSchedule.MaxCopies];
        private readonly Vector4[] copyXform = new Vector4[EchoSchedule.MaxCopies];
        private readonly Vector4[] copyParam = new Vector4[EchoSchedule.MaxCopies];

        private Material material;
        private GameObject quad;
        private MeshRenderer quadRenderer;
        private EchoRing ring;
        private bool active = true;
        private bool disposed;

        public EchoLayer(Transform parent, Shader shader) {
            if (!LayerResources.ShaderUsable(shader, "echo", "HumanEcho/Echo")) {
                return;
            }
            material = LayerResources.CreateMaterial(shader, "HumanEcho_Echo (runtime)");
            ring = new EchoRing(shader, EchoSchedule.DefaultCapacity);
            quad = LayerQuad.Create("Human Echo Copies", parent, material, LayerQuad.QueueEcho, out quadRenderer);
            quadRenderer.enabled = false;
            IsReady = true;
        }

        public bool IsReady { get; private set; }

        /// <summary>The settings the layer reads every frame; the controller points it at the active look's
        /// echo settings.</summary>
        public EchoSettings Settings { get; set; }

        /// <summary>The ring of past masks. Hand it to the outline layer so its delayed outline reads the
        /// same history. Null if not ready.</summary>
        public EchoRing Ring {
            get {
                return ring;
            }
        }

        /// <summary>
        /// One frame. Off (or hidden) means no capture and no draw. Otherwise: take a mask into the ring when
        /// the schedule says so,
        /// then draw every copy that already has a mask (right after start-up there are none until the second
        /// capture).
        /// </summary>
        public void Tick(PersonField field, FrameContext frame) {
            if (!IsReady || disposed || field == null || field.Field == null) {
                return;
            }
            EchoSettings s = Settings;
            if (s != null) {
                // Written even while the layer is off, so a delayed outline that borrows the ring keeps the
                // same pace.
                ring.SpacingSeconds = s.spacingSeconds;
            }
            if (!active || s == null || !s.enabled) {
                quadRenderer.enabled = false;
                return;
            }

            ring.Advance(field, frame.Time);

            int wanted = EchoSchedule.ClampCopies(s.copies);
            int available = Mathf.Max(0, ring.ValidCount - EchoSchedule.FirstCopyAge);
            int count = Mathf.Min(wanted, available);
            if (count <= 0) {
                quadRenderer.enabled = false;
                return;
            }

            float viewWidth = Mathf.Max(MinViewSize, frame.ViewWidth);
            float viewHeight = Mathf.Max(MinViewSize, frame.ViewHeight);
            float aspect = viewWidth / viewHeight;
            Vector2 pictureSize = LayerQuad.ContainSize(field.Aspect, viewWidth, viewHeight);

            FillCopies(s, wanted, count, aspect, frame.Time);

            material.SetTexture(EchoRingId, ring.Texture);
            material.SetVector(PicScaleId, new Vector4(viewWidth / pictureSize.x, viewHeight / pictureSize.y, 0f, 0f));
            material.SetFloat(AspectId, aspect);
            material.SetFloat(CopyCountId, (float)count);
            material.SetFloat(LookId, (float)s.look);
            material.SetFloat(DotsPerHeightId, s.dotsPerScreenHeight);
            material.SetFloat(DotSoftnessId, s.dotSoftness);
            material.SetFloat(EdgeContrastId, s.edgeContrast);
            material.SetFloat(OutlineWidthId, OutlineWidthThousandths * 0.001f);
            material.SetFloat(WaveAmountId, s.waveAmount);
            material.SetFloat(WaveFrequencyId, s.waveFrequency);
            // The phase is wrapped on the CPU so a scene that runs for hours keeps a precise sine.
            material.SetFloat(WavePhaseId, Mathf.Repeat(frame.Time * s.waveSpeed, 1f) * TwoPi);
            material.SetVectorArray(CopyColorId, copyColor);
            material.SetVectorArray(CopyXformId, copyXform);
            material.SetVectorArray(CopyParamId, copyParam);

            LayerQuad.Place(quad.transform, new Vector2(viewWidth, viewHeight), QuadDepth);
            quadRenderer.enabled = true;
        }

        /// <summary>Shows or hides the layer without disposing it. A hidden layer draws nothing and does no
        /// capture.</summary>
        public void SetActive(bool value) {
            active = value;
            if (!value && quadRenderer != null) {
                quadRenderer.enabled = false;
            }
        }

        /// <summary>Forgets every captured mask (the copies start again from the next mask frame).</summary>
        public void Clear() {
            if (ring != null) {
                ring.Reset();
            }
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            IsReady = false;
            LayerResources.DestroyObject(quad);
            LayerResources.DestroyObject(material);
            if (ring != null) {
                ring.Dispose();
            }
        }

        // Array index 0 is the OLDEST copy drawn: the shader composites in index order, so the newest ends up
        // on top.
        private void FillCopies(EchoSettings s, int wanted, int count, float aspect, float time) {
            for (int j = 0; j < count; j++) {
                int copy = count - 1 - j;
                int age = EchoSchedule.AgeOfCopy(copy);
                Color linear = EchoSchedule.ColorFor(s, copy, time).linear;
                float opacity = EchoSchedule.OpacityFor(s, copy, wanted);
                copyColor[j] = new Vector4(linear.r * s.gain, linear.g * s.gain, linear.b * s.gain, opacity);

                // The shift is in screen heights; the shader works in screen uv, whose u axis is one aspect
                // wide.
                Vector2 offset = EchoSchedule.OffsetFor(s, copy);
                copyXform[j] = new Vector4(offset.x / aspect, offset.y, EchoSchedule.ScaleFor(s, copy), 0f);

                Vector2 gridShift = EchoSchedule.GridShiftFor(copy);
                copyParam[j] = new Vector4((float)ring.SlotForAge(age), (float)age, gridShift.x, gridShift.y);
            }
        }
    }
}
