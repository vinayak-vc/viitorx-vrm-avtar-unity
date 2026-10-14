using System;

using UnityEngine;

namespace HumanEcho {
    /// <summary>How the neon outline is coloured.</summary>
    public enum OutlineColorMode {
        Fixed = 0,
        HueCycle = 1
    }

    /// <summary>
    /// The thin NEON OUTLINE traced along the person mask's edge (white or cyan in the reference clips), with
    /// a glow and an optional electric
    /// jitter. Plain serializable data, read every frame.
    /// </summary>
    [Serializable]
    public sealed class OutlineSettings {
        public bool enabled = true;
        public OutlineColorMode colorMode = OutlineColorMode.Fixed;
        public Color color = new Color(0.85f, 1f, 1f, 1f);
        [Tooltip("Hue cycles per second (HueCycle only).")]
        public float hueSpeed = 0.1f;
        [Tooltip("Line width in screen heights / 1000 (1.5 is a thin line on a 1080p screen).")]
        [Range(0.5f, 8f)] public float widthThousandths = 1.6f;
        [Range(0f, 1f)] public float softness = 0.35f;
        [Tooltip("HDR brightness of the line, so Bloom flares it.")]
        [Range(0.5f, 6f)] public float intensity = 2.2f;
        [Tooltip("A wider faint halo around the line, in screen heights / 1000. 0 = none.")]
        [Range(0f, 30f)] public float haloThousandths = 6f;
        [Range(0f, 1f)] public float haloStrength = 0.35f;
        [Tooltip("Electric jitter: how far the line is shaken, in screen heights / 1000 (the zigzag outline of clip 27845). 0 = a clean line.")]
        [Range(0f, 12f)] public float jitterThousandths = 0f;
        [Range(2f, 80f)] public float jitterFrequency = 24f;
        [Range(0f, 30f)] public float jitterSpeed = 14f;
        [Tooltip("The outline follows the mask of this many echo copies ago (0 = the live mask; needs the echo layer's ring). The lag in clip 11367.")]
        [Range(0, 15)] public int delayCopies = 0;
    }
}
