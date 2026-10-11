using Fluxy;

using UnityEngine;
using UnityEngine.Rendering;

namespace FluidDisruption {
    /// <summary>
    /// The one place a <see cref="FluxyTarget"/> gets its neutral configuration, shared by the baseline
    /// sources and the interaction targets. A target configured here injects nothing until its owner
    /// gives it a weight, a rate or a motion: no rate over steps, none over time, none over distance.
    /// </summary>
    public static class FluidTargets {
        /// <summary>Gives the target its own copy of the splat material and puts it in the neutral state.
        /// The returned material is owned by the caller and must be destroyed with it.</summary>
        public static Material Configure(FluxyTarget target, Material splatMaterial, Texture splatTexture,
                                         Texture noiseTexture, string materialName) {
            Material instance = new Material(splatMaterial);
            instance.name = materialName;
            target.splatMaterial = instance;
            target.densityTexture = splatTexture;
            target.velocityTexture = null;
            target.noiseTexture = noiseTexture;
            target.rateOverSteps = 0;
            target.rateOverTime = 0f;
            target.rateOverDistance = 0f;
            target.overridePosition = false;
            target.overrideRotation = true;
            target.rotation = 0f;
            target.scaleWithDistance = true;
            target.scaleWithTransform = false;
            target.positionRandomness = 0f;
            target.scaleRandomness = 0f;
            target.rotationRandomness = 0f;
            target.velocityWeight = 0f;
            target.maxRelativeVelocity = 8f;
            target.velocityScale = Vector3.zero;
            target.angularVelocityScale = 0f;
            target.force = Vector3.zero;
            target.torque = 0f;
            target.densityWeight = 0f;
            target.densityNoise = 0f;
            target.velocityNoise = 0f;
            target.velocityNoiseOffset = 0f;
            target.velocityNoiseTiling = 1f;
            target.srcBlend = BlendMode.SrcAlpha;
            target.dstBlend = BlendMode.OneMinusSrcAlpha;
            target.blendOp = BlendOp.Add;
            target.color = Color.white;
            target.enabled = false;
            return instance;
        }

        /// <summary>Opacity of the soft splat at a squared distance from its middle (0 = centre, 1 = rim):
        /// 1 in the middle, easing smoothly to 0 at the rim.</summary>
        public static float SoftSplatFalloff(float squaredRadius) {
            float r2 = Mathf.Clamp01(squaredRadius);
            return (1f - r2) * (1f - r2);
        }

        /// <summary>A soft, round splat shape: opaque white in the middle, falling smoothly to nothing at the
        /// rim, so a splat never leaves an edge. The caller owns the texture and must destroy it.</summary>
        public static Texture2D BuildSoftSplat(int size) {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.name = "FluidDisruption_SoftSplat";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color32[] pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    byte a = (byte)Mathf.RoundToInt(SoftSplatFalloff(dx * dx + dy * dy) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        public static void Destroy(Material material) {
            if (material != null) {
                Object.Destroy(material);
            }
        }
    }
}
