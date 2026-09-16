using UnityEngine;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 — the shared look: colours, materials and the one rule that makes three different modes
    /// read as one piece of work rather than three demos.
    ///
    /// THE RULE: colour means SPEED, always, in every mode. Cool at rest, hot in motion. A viewer
    /// learns it once in the first mode and it keeps being true in the other two. If each mode chose
    /// its own mapping the switcher would feel like three unrelated toys.
    ///
    /// EVERY COLOUR IS HDR (values above 1.0). That is what makes Bloom glow instead of merely
    /// brighten: the post-process threshold is above 1, so a colour at 0.9 produces no glow at all and
    /// the same colour at 3.0 blooms. The modes therefore look flat and wrong without the Volume the
    /// bootstrap creates, which is why the bootstrap creates it rather than expecting the scene to.
    ///
    /// Materials are created once here and shared, so switching modes does not churn the renderer's
    /// material cache; each mode instances only where it needs a per-object colour.
    /// </summary>
    public sealed class SkeletonShowPalette {
        /// <summary>At rest. Deep, saturated blue - present but calm.</summary>
        public readonly Color Cool = new Color(0.10f, 0.55f, 1.60f, 1f);

        /// <summary>Moving normally. Cyan.</summary>
        public readonly Color Warm = new Color(0.20f, 1.80f, 1.70f, 1f);

        /// <summary>Moving fast. Magenta - the top of the scale, and rare enough to feel earned.</summary>
        public readonly Color Hot = new Color(2.40f, 0.35f, 1.30f, 1f);

        /// <summary>Behind everything: near-black with a trace of blue, so the glow has somewhere to
        /// sit. Pure black makes bloom look like a bug rather than a light.</summary>
        public readonly Color Background = new Color(0.012f, 0.016f, 0.035f, 1f);

        private Material additive;
        private Material opaque;

        /// <summary>Speed 0..1 -> the shared palette. Two-stage so the middle of the range is cyan
        /// rather than a muddy blend of blue and magenta.</summary>
        public Color Heat(float t) {
            t = Mathf.Clamp01(t);
            if (t < 0.5f) {
                return Color.Lerp(Cool, Warm, t * 2f);
            }
            return Color.Lerp(Warm, Hot, (t - 0.5f) * 2f);
        }

        /// <summary>Additive, unlit, no depth write - for lines, trails and the energy body, which
        /// must build up where they overlap instead of occluding each other.</summary>
        public Material Additive() {
            if (additive == null) {
                additive = new Material(FindShader());
                additive.name = "SkeletonShow_Additive";
                SetTransparentAdditive(additive);
            }
            return additive;
        }

        /// <summary>Unlit and opaque - for the joint spheres, which should read as solid cores.</summary>
        public Material Opaque() {
            if (opaque == null) {
                opaque = new Material(FindShader());
                opaque.name = "SkeletonShow_Opaque";
            }
            return opaque;
        }

        /// <summary>A per-object instance of the additive material, for a colour of its own.</summary>
        public Material NewAdditive(Color colour) {
            Material m = new Material(Additive());
            SetColour(m, colour);
            return m;
        }

        /// <summary>A per-object instance of the opaque material.</summary>
        public Material NewOpaque(Color colour) {
            Material m = new Material(Opaque());
            SetColour(m, colour);
            return m;
        }

        /// <summary>Set whichever colour property this shader actually uses. URP's Unlit uses
        /// _BaseColor; the sprite fallback uses _Color. Setting a property a shader does not have is
        /// silently ignored by Unity, which is how a material ends up stubbornly white.</summary>
        public static void SetColour(Material material, Color colour) {
            if (material == null) {
                return;
            }
            if (material.HasProperty("_BaseColor")) {
                material.SetColor("_BaseColor", colour);
            }
            if (material.HasProperty("_Color")) {
                material.SetColor("_Color", colour);
            }
            if (material.HasProperty("_EmissionColor")) {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colour);
            }
        }

        private static Shader FindShader() {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) {
                // Not a URP project, or the shader was stripped. Sprites/Default is unlit, always
                // present, and is what PoseDebugSkeleton already relies on - so the show degrades to
                // "visible but not glowing" instead of to magenta error material.
                s = Shader.Find("Sprites/Default");
            }
            return s;
        }

        private static void SetTransparentAdditive(Material m) {
            // URP surface setup by hand: these are the same properties the material inspector writes,
            // and they have to be set together - Surface alone does nothing without the blend modes.
            if (m.HasProperty("_Surface")) {
                m.SetFloat("_Surface", 1f);            // transparent
                m.SetFloat("_Blend", 1f);              // additive
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
