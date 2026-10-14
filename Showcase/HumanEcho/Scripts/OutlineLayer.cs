using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// The NEON OUTLINE LAYER: a thin bright line along the person mask's edge, with an optional faint halo,
    /// HDR intensity (so Bloom
    /// flares it) and an optional electric zigzag. It is drawn on one full-screen quad
    /// (<c>HumanEcho/Outline</c>); the picture is mapped
    /// into the screen with the same "contain" mapping the echo uses.
    ///
    /// All widths are in screen heights (the settings count thousandths of them), never in mask pixels, so
    /// the line looks the same at
    /// any mask resolution and any screen size. The shader derives the line from the mask's 0.5 iso-line and
    /// its gradient rather than a
    /// hard threshold, because the mask edge is soft and flickers a few percent (see the shader header).
    ///
    /// The mask is the live person field, or, when <see cref="OutlineSettings.delayCopies"/> is above 0 and
    /// an <see cref="EchoRing"/> is
    /// shared, the ring's capture that many copies back: the lagging outline of the reference clip. The ring
    /// is borrowed, never owned:
    /// the echo layer disposes it. The ring moves on in steps of the echo spacing, so the delayed line
    /// follows in steps of that size.
    ///
    /// Create it with <c>new OutlineLayer(parent, shader, ring)</c> (the ring may be null, or set later
    /// through <see cref="Ring"/>).
    /// <see cref="IsReady"/> is false (one error logged) if the shader is missing.
    /// </summary>
    public sealed class OutlineLayer : IHumanEchoLayer {
        private const float QuadDepth = 0f;
        private const float MinViewSize = 0.01f;
        private const float Thousandth = 0.001f;
        private const float HueSaturation = 0.9f;
        private const float HueValue = 1f;

        // The shader hashes the lattice with the time step; wrapping keeps the hash inputs small and precise
        // (the line only
        // repeats its crackle pattern after this many steps).
        private const float JitterStepWrap = 1024f;

        private static readonly int FieldTexId = Shader.PropertyToID("_FieldTex");
        private static readonly int RingTexId = Shader.PropertyToID("_RingTex");
        private static readonly int UseRingId = Shader.PropertyToID("_UseRing");
        private static readonly int RingSliceId = Shader.PropertyToID("_RingSlice");
        private static readonly int PicScaleId = Shader.PropertyToID("_PicScale");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int MaskTexelId = Shader.PropertyToID("_MaskTexel");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        private static readonly int HaloWidthId = Shader.PropertyToID("_HaloWidth");
        private static readonly int HaloStrengthId = Shader.PropertyToID("_HaloStrength");
        private static readonly int JitterAmountId = Shader.PropertyToID("_JitterAmount");
        private static readonly int JitterFrequencyId = Shader.PropertyToID("_JitterFrequency");
        private static readonly int JitterStepId = Shader.PropertyToID("_JitterStep");

        private Material material;
        private GameObject quad;
        private MeshRenderer quadRenderer;
        private bool active = true;
        private bool disposed;

        public OutlineLayer(Transform parent, Shader shader, EchoRing ring) {
            Ring = ring;
            if (!LayerResources.ShaderUsable(shader, "outline", "HumanEcho/Outline")) {
                return;
            }
            material = LayerResources.CreateMaterial(shader, "HumanEcho_Outline (runtime)");
            quad = LayerQuad.Create("Human Echo Outline", parent, material, LayerQuad.QueueOutline, out quadRenderer);
            quadRenderer.enabled = false;
            IsReady = true;
        }

        public bool IsReady { get; private set; }

        /// <summary>The settings the layer reads every frame; the controller points it at the active look's
        /// outline settings.</summary>
        public OutlineSettings Settings { get; set; }

        /// <summary>The echo ring a delayed outline reads (see the class summary). Null = always the live
        /// mask. Not owned.</summary>
        public EchoRing Ring { get; set; }

        public void Tick(PersonField field, FrameContext frame) {
            if (!IsReady || disposed || field == null || field.Field == null) {
                return;
            }
            OutlineSettings s = Settings;
            if (!active || s == null || !s.enabled) {
                quadRenderer.enabled = false;
                return;
            }

            float viewWidth = Mathf.Max(MinViewSize, frame.ViewWidth);
            float viewHeight = Mathf.Max(MinViewSize, frame.ViewHeight);
            Vector2 pictureSize = LayerQuad.ContainSize(field.Aspect, viewWidth, viewHeight);

            SelectMask(field, frame.Time, s.delayCopies);

            material.SetTexture(FieldTexId, field.Field);
            material.SetVector(PicScaleId, new Vector4(viewWidth / pictureSize.x, viewHeight / pictureSize.y, 0f, 0f));
            material.SetFloat(AspectId, viewWidth / viewHeight);
            material.SetVector(MaskTexelId, new Vector4(1f / field.Width, 1f / field.Height, (float)field.Width, (float)field.Height));

            Color lineColor = s.color;
            if (s.colorMode == OutlineColorMode.HueCycle) {
                lineColor = Color.HSVToRGB(Mathf.Repeat(frame.Time * s.hueSpeed, 1f), HueSaturation, HueValue);
            }
            material.SetColor(ColorId, lineColor.linear);
            material.SetFloat(IntensityId, s.intensity);
            material.SetFloat(WidthId, s.widthThousandths * Thousandth);
            material.SetFloat(SoftnessId, s.softness);
            material.SetFloat(HaloWidthId, s.haloThousandths * Thousandth);
            material.SetFloat(HaloStrengthId, s.haloStrength);
            material.SetFloat(JitterAmountId, s.jitterThousandths * Thousandth);
            material.SetFloat(JitterFrequencyId, s.jitterFrequency);
            material.SetFloat(JitterStepId, Mathf.Floor(Mathf.Repeat(frame.Time * s.jitterSpeed, JitterStepWrap)));

            LayerQuad.Place(quad.transform, new Vector2(viewWidth, viewHeight), QuadDepth);
            quadRenderer.enabled = true;
        }

        /// <summary>Shows or hides the layer without disposing it. A hidden layer draws nothing.</summary>
        public void SetActive(bool value) {
            active = value;
            if (!value && quadRenderer != null) {
                quadRenderer.enabled = false;
            }
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            IsReady = false;
            Ring = null;
            LayerResources.DestroyObject(quad);
            LayerResources.DestroyObject(material);
        }

        // Live mask, or the ring's capture <delayCopies> back (the newest one that exists while the ring is
        // still filling up).
        private void SelectMask(PersonField field, float time, int delayCopies) {
            bool useRing = false;
            int slot = 0;
            EchoRing shared = Ring;
            if (shared != null && delayCopies > 0) {
                // Idempotent per mask frame, so it does not matter whether the echo layer ticked first; it
                // also keeps the history
                // moving when the echo layer itself is switched off.
                shared.Advance(field, time);
                if (shared.Texture != null && shared.ValidCount > 0) {
                    int age = Mathf.Min(delayCopies, shared.ValidCount - 1);
                    slot = shared.SlotForAge(age);
                    useRing = slot >= 0;
                }
            }
            material.SetFloat(UseRingId, useRing ? 1f : 0f);
            if (useRing) {
                material.SetTexture(RingTexId, shared.Texture);
                material.SetFloat(RingSliceId, (float)slot);
            }
        }
    }
}
