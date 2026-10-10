using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// Owns the full-screen rain quad and its single runtime material. All motion happens in the
    /// shader; this class only advances one wrapping phase and pushes the settings. No per-drop
    /// objects, no per-frame allocation.
    /// </summary>
    public sealed class RainRenderer : MonoBehaviour {
        /// <summary>Must match REF_FALL and WRAP_SECONDS in the rain shader.</summary>
        private const float ReferenceFall = 0.85f;
        private const double WrapSeconds = 1200.0;

        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int StreakLengthId = Shader.PropertyToID("_StreakLength");
        private static readonly int StreakWidthId = Shader.PropertyToID("_StreakWidth");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int WindId = Shader.PropertyToID("_Wind");
        private static readonly int AmberId = Shader.PropertyToID("_AmberChance");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int PaletteId = Shader.PropertyToID("_Palette");
        private static readonly int MaskInfluenceId = Shader.PropertyToID("_MaskInfluence");

        [SerializeField] private MeshRenderer quad;
        [SerializeField] private Material sourceMaterial;

        private Material material;
        private readonly Vector4[] palette = new Vector4[5];
        private double phase;

        public bool IsReady {
            get {
                return material != null;
            }
        }

        public bool Initialize() {
            if (quad == null || sourceMaterial == null) {
                Debug.LogError("[ChromaticRain] RainRenderer needs a quad MeshRenderer and a rain "
                               + "material. Run Showcase > Chromatic Rain > Build Showcase Scene.", this);
                return false;
            }
            material = new Material(sourceMaterial);
            material.name = "ChromaticRain_Rain (runtime)";
            quad.sharedMaterial = material;
            return true;
        }

        public void Apply(ChromaticRainSettings s, float dt, float aspect) {
            if (material == null) {
                return;
            }
            phase += dt * (s.fallSpeed / ReferenceFall) / WrapSeconds;
            phase -= System.Math.Floor(phase);

            for (int i = 0; i < 5; i++) {
                Color c = s.PaletteColor(i);
                palette[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
            material.SetFloat(DensityId, s.rainDensity);
            material.SetFloat(StreakLengthId, s.streakLength);
            material.SetFloat(StreakWidthId, s.streakWidth);
            material.SetFloat(IntensityId, s.colorIntensity);
            material.SetFloat(WindId, s.windStrength);
            material.SetFloat(AmberId, s.amberChance);
            material.SetFloat(PhaseId, (float)phase);
            material.SetFloat(AspectId, aspect);
            material.SetVectorArray(PaletteId, palette);
            material.SetFloat(MaskInfluenceId, 1f);
        }

        private void OnDestroy() {
            if (material != null) {
                Destroy(material);
                material = null;
            }
        }
    }
}
