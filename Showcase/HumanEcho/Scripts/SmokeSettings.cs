using System;

using UnityEngine;

namespace HumanEcho {
    /// <summary>How the smoke layer draws the dye.</summary>
    public enum SmokeLook {
        Ink = 0,
        Dots = 1
    }

    /// <summary>
    /// Every tunable of the FluXY smoke layer. Plain serializable data: the layer reads it every frame, so it
    /// can be edited while playing. Colours
    /// are the injection tint (the dye is the person's own picture mixed with a hue that drifts over time).
    /// </summary>
    [Serializable]
    public sealed class SmokeSettings {
        public bool enabled = true;
        [Tooltip("False: the fluid still runs (particles ride on its velocity) but nothing of it is drawn.")]
        public bool drawn = true;

        [Header("Fluid (FluXY)")]
        [Tooltip("FluXY rounds this DOWN to a power of two (192 gives 128).")]
        public int resolution = 512;
        [Range(1, 4)] public int supersampling = 2;
        [Tooltip("FluXY steps of 8 ms per frame at most: 4 keeps the fluid in real time down to about 31 fps (3 would slow it below 42).")]
        [Range(1, 6)] public int maxSteps = 4;
        [Range(0f, 1f)] public float viscosity = 0.05f;
        [Min(0f)] public float turbulence = 8f;
        [Range(0f, 1f)] public float pressure = 1f;
        [Tooltip("Upward force per unit of dye (FluXY's buoyancy). 0 = the smoke does not rise by itself.")]
        public float buoyancy = 0.6f;
        [Tooltip("Dye lost per second, subtractive (FluXY). High values tear the thin edges (ADR-091); keep it moderate.")]
        [Min(0f)] public float dissipation = 0.35f;
        [Tooltip("A constant push on the whole fluid, screen heights per second squared (x right, y up).")]
        public Vector2 drift = new Vector2(0f, 0.05f);
        [Range(0f, 0.5f)] public float edgeWidth = 0.08f;
        [Min(0f)] public float edgeDrain = 1.5f;

        [Header("Injection from the person")]
        [Tooltip("How much of the dye under the silhouette is replaced per frame at 60 fps (0 = none, 1 = all).")]
        [Range(0f, 1f)] public float dyeWeight = 0.45f;
        [Tooltip("How much of the fluid's velocity under the silhouette is pulled toward the person's motion per frame at 60 fps.")]
        [Range(0f, 1f)] public float velocityWeight = 0.25f;
        [Tooltip("Scales the person's motion before it enters the fluid. FluXY clamps the result to +-1 of its own units.")]
        [Min(0f)] public float motionGain = 2.5f;
        [Tooltip("Scales the motion confidence, so slow movement still counts.")]
        [Min(0f)] public float motionConfidence = 1.5f;
        [Tooltip("0 = every dye pixel is the tint; 1 = the camera picture's own colours.")]
        [Range(0f, 1f)] public float pictureMix = 0.35f;
        [Tooltip("Hue cycles per second.")]
        public float hueSpeed = 0.07f;
        [Range(0f, 1f)] public float saturation = 0.85f;
        [Range(0f, 1f)] public float value = 1f;
        [Tooltip("How far the hue turns from the bottom to the top of the picture, in full colour cycles (0 = one hue at a time; 0.5 = a rainbow across the body).")]
        [Range(0f, 1f)] public float hueSpread = 0f;

        [Header("Drawing")]
        public SmokeLook look = SmokeLook.Ink;
        [Range(0f, 8f)] public float gain = 2.2f;
        [Range(0f, 1f)] public float opacity = 1f;
        [Range(40f, 400f)] public float dotsPerScreenHeight = 160f;
        [Range(0.02f, 0.5f)] public float dotSoftness = 0.18f;
        [Range(0f, 1f)] public float dotFlow = 0.6f;

        /// <summary>The tint the dye has at <paramref name="time"/> seconds: a hue that cycles.</summary>
        public Color TintAt(float time) {
            float hue = Mathf.Repeat(time * hueSpeed, 1f);
            return Color.HSVToRGB(hue, saturation, value);
        }
    }
}
