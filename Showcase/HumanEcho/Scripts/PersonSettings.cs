using System;

using UnityEngine;

namespace HumanEcho {
    /// <summary>How the person layer colours the camera picture.</summary>
    public enum PersonColorMode {
        /// <summary>The camera's own colours (clips 27845 and 11367).</summary>
        Picture = 0,
        /// <summary>A grey / white figure, optionally tinted (clip 14008).</summary>
        Grayscale = 1,
        /// <summary>One flat colour wherever the mask says person.</summary>
        Flat = 2
    }

    /// <summary>The true-colour cutout itself, drawn as one quad (fitted inside the screen) from the person
    /// field. Plain serializable data, read every frame.</summary>
    [Serializable]
    public sealed class PersonSettings {
        public bool enabled = true;
        public PersonColorMode colorMode = PersonColorMode.Picture;
        [Tooltip("Tint multiplied into the picture (Picture, Grayscale) or the flat colour (Flat).")]
        public Color tint = Color.white;
        [Range(0f, 1f)] public float opacity = 1f;
        [Range(0f, 2f)] public float brightness = 1f;
        [Range(0.5f, 2f)] public float contrast = 1f;
        [Tooltip("Pushes the figure toward white-hot (HDR, so Bloom flares it). 0 = none.")]
        [Range(0f, 4f)] public float flash = 0f;
        [Tooltip("Mask contrast: 1 = the mask as sent, higher = a harder edge.")]
        [Range(0.5f, 4f)] public float edgeContrast = 1.5f;
        [Tooltip("A thin coloured light on the figure's outer edge (the rim in clip 14008).")]
        public bool rim = false;
        public Color rimColor = new Color(1f, 0.25f, 0.1f, 1f);
        [Range(0f, 8f)] public float rimWidth = 2f;
        [Range(0f, 4f)] public float rimStrength = 1.5f;
    }
}
