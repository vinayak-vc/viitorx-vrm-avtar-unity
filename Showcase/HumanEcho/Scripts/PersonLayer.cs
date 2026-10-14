using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// The PERSON LAYER: the person field drawn as a true-colour cutout (picture where the mask says person,
    /// nothing elsewhere) on ONE
    /// quad fitted inside the screen ("contain"), so the quad's uv is the picture's uv. It is the figure the
    /// echo copies trail behind and
    /// the outline traces. Colour mode (picture, grey, flat), brightness, contrast, tint, opacity, a
    /// white-hot flash (HDR, for Bloom), a
    /// harder mask edge and an optional rim light on the outer edge are all shader parameters read from <see
    /// cref="PersonSettings"/>
    /// every frame; nothing is read back or copied on the CPU.
    ///
    /// <see cref="PersonSettings.rimWidth"/> is counted in screen heights / 1000 (like the outline's widths),
    /// so the rim keeps its
    /// width on screen whatever the mask's resolution is.
    ///
    /// Create it with <c>new PersonLayer(parent, shader)</c>; <see cref="IsReady"/> is false (one error
    /// logged) if the shader is missing.
    /// </summary>
    public sealed class PersonLayer : IHumanEchoLayer {
        private const float QuadDepth = 0f;
        private const float MinViewSize = 0.01f;
        private const float Thousandth = 0.001f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int EdgeContrastId = Shader.PropertyToID("_EdgeContrast");
        private static readonly int RimOnId = Shader.PropertyToID("_RimOn");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimRadiusUvId = Shader.PropertyToID("_RimRadiusUv");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");

        private Material material;
        private GameObject quad;
        private MeshRenderer quadRenderer;
        private bool active = true;
        private bool disposed;

        public PersonLayer(Transform parent, Shader shader) {
            if (!LayerResources.ShaderUsable(shader, "person", "HumanEcho/Person")) {
                return;
            }
            material = LayerResources.CreateMaterial(shader, "HumanEcho_Person (runtime)");
            quad = LayerQuad.Create("Human Echo Person", parent, material, LayerQuad.QueuePerson, out quadRenderer);
            quadRenderer.enabled = false;
            IsReady = true;
        }

        public bool IsReady { get; private set; }

        /// <summary>The settings the layer reads every frame; the controller points it at the active look's
        /// person settings.</summary>
        public PersonSettings Settings { get; set; }

        public void Tick(PersonField field, FrameContext frame) {
            if (!IsReady || disposed || field == null || field.Field == null) {
                return;
            }
            PersonSettings s = Settings;
            if (!active || s == null || !s.enabled) {
                quadRenderer.enabled = false;
                return;
            }

            float viewWidth = Mathf.Max(MinViewSize, frame.ViewWidth);
            float viewHeight = Mathf.Max(MinViewSize, frame.ViewHeight);
            Vector2 pictureSize = LayerQuad.ContainSize(field.Aspect, viewWidth, viewHeight);

            material.SetTexture(MainTexId, field.Field);
            material.SetFloat(ModeId, (float)s.colorMode);
            material.SetColor(TintId, s.tint.linear);
            material.SetFloat(OpacityId, s.opacity);
            material.SetFloat(BrightnessId, s.brightness);
            material.SetFloat(ContrastId, s.contrast);
            material.SetFloat(FlashId, s.flash);
            material.SetFloat(EdgeContrastId, s.edgeContrast);

            bool rimOn = s.rim && s.rimWidth > 0f && s.rimStrength > 0f;
            material.SetFloat(RimOnId, rimOn ? 1f : 0f);
            if (rimOn) {
                // The rim radius is in screen heights; the shader works in picture uv, which spans the
                // picture's world size.
                float radius = s.rimWidth * Thousandth;
                material.SetColor(RimColorId, s.rimColor.linear);
                material.SetVector(RimRadiusUvId,
                                   new Vector4(radius * viewHeight / pictureSize.x, radius * viewHeight / pictureSize.y, 0f, 0f));
                material.SetFloat(RimStrengthId, s.rimStrength);
            }

            LayerQuad.Place(quad.transform, pictureSize, QuadDepth);
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
            LayerResources.DestroyObject(quad);
            LayerResources.DestroyObject(material);
        }
    }
}
