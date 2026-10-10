using UnityEngine;
using UnityEngine.Rendering;

namespace ChromaticRain {
    /// <summary>
    /// Renders the body into ONE reusable single-channel mask texture and publishes it as the global
    /// texture <c>_ChromaMaskTex</c>, which the rain, the fluid layer and the dark overlay all sample.
    /// Also owns the overlay quad that draws the Dark Silhouette.
    /// </summary>
    public sealed class SilhouetteMaskRenderer : MonoBehaviour {
        public const string MaskTextureName = "_ChromaMaskTex";

        private static readonly int MaskTextureId = Shader.PropertyToID(MaskTextureName);
        private static readonly int CapsId = Shader.PropertyToID("_Caps");
        private static readonly int CapRadiiId = Shader.PropertyToID("_CapRadii");
        private static readonly int CapCountId = Shader.PropertyToID("_CapCount");
        private static readonly int HeadId = Shader.PropertyToID("_Head");
        private static readonly int HeadDirectionId = Shader.PropertyToID("_HeadDirection");
        private static readonly int ViewId = Shader.PropertyToID("_View");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int BodyColorId = Shader.PropertyToID("_BodyColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");

        [SerializeField] private Material maskMaterial;
        [SerializeField] private MeshRenderer overlayQuad;
        [SerializeField] private Material overlayMaterial;

        private Material runtimeMask;
        private Material runtimeOverlay;
        private RenderTexture mask;
        private int maskWidth;
        private int maskHeight;

        public RenderTexture Mask {
            get {
                return mask;
            }
        }

        public bool IsReady {
            get {
                return runtimeMask != null && mask != null;
            }
        }

        public string FormatName {
            get {
                return mask != null ? mask.format.ToString() : "none";
            }
        }

        public bool Initialize(float aspect, ChromaticRainSettings s) {
            if (maskMaterial == null || overlayQuad == null || overlayMaterial == null) {
                Debug.LogError("[ChromaticRain] SilhouetteMaskRenderer needs the mask material, the "
                               + "overlay quad and the overlay material. Run Showcase > Chromatic Rain "
                               + "> Build Showcase Scene.", this);
                return false;
            }
            runtimeMask = new Material(maskMaterial);
            runtimeMask.name = "ChromaticRain_Mask (runtime)";
            runtimeOverlay = new Material(overlayMaterial);
            runtimeOverlay.name = "ChromaticRain_Overlay (runtime)";
            overlayQuad.sharedMaterial = runtimeOverlay;
            EnsureTexture(aspect, s.maskHeight);
            return true;
        }

        /// <summary>Re-creates the mask when the screen aspect or the requested height changed.</summary>
        public void EnsureTexture(float aspect, int height) {
            int h = Mathf.Max(64, height);
            int w = Mathf.Max(64, Mathf.RoundToInt(h * aspect));
            if (mask != null && w == maskWidth && h == maskHeight) {
                return;
            }
            if (mask != null) {
                mask.Release();
                Destroy(mask);
            }
            // One channel where the platform supports it; a four-channel fallback otherwise. The
            // shaders read only R, so both behave identically.
            RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8
                : RenderTextureFormat.ARGB32;
            mask = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear);
            mask.name = "ChromaticRain_Mask";
            mask.filterMode = FilterMode.Bilinear;
            mask.wrapMode = TextureWrapMode.Clamp;
            mask.useMipMap = false;
            mask.Create();
            maskWidth = w;
            maskHeight = h;
            ClearMask();
            Shader.SetGlobalTexture(MaskTextureId, mask);
        }

        private void ClearMask() {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = mask;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = previous;
        }

        public void Render(SilhouetteBuilder builder, float viewWidth, float viewHeight, float alpha,
                           SilhouetteMode mode, ChromaticRainSettings s) {
            if (!IsReady) {
                return;
            }
            bool draw = alpha > 0.001f && (builder.CapCount > 0 || builder.HeadDirection.z > 0.001f);
            if (draw) {
                runtimeMask.SetVectorArray(CapsId, builder.Caps);
                runtimeMask.SetVectorArray(CapRadiiId, builder.CapRadii);
                runtimeMask.SetInt(CapCountId, builder.CapCount);
                runtimeMask.SetVector(HeadId, builder.Head);
                runtimeMask.SetVector(HeadDirectionId, builder.HeadDirection);
                runtimeMask.SetVector(ViewId, new Vector4(viewWidth, viewHeight, s.edgeSoftness, s.shapeBlend));
                runtimeMask.SetFloat(FadeId, alpha);
                Graphics.Blit(null, mask, runtimeMask, 0);
            } else {
                ClearMask();
            }

            bool dark = mode == SilhouetteMode.DarkSilhouette && draw;
            if (overlayQuad.enabled != dark) {
                overlayQuad.enabled = dark;
            }
            if (dark) {
                runtimeOverlay.SetColor(BodyColorId, s.silhouetteColor);
                runtimeOverlay.SetColor(RimColorId, s.rimColor);
                runtimeOverlay.SetFloat(RimIntensityId, s.rimIntensity);
            }
        }

        private void OnDestroy() {
            if (mask != null) {
                if (RenderTexture.active == mask) {
                    RenderTexture.active = null;
                }
                mask.Release();
                Destroy(mask);
                mask = null;
            }
            if (runtimeMask != null) {
                Destroy(runtimeMask);
            }
            if (runtimeOverlay != null) {
                Destroy(runtimeOverlay);
            }
        }
    }
}
