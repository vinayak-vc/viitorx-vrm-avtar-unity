using System;

using UnityEngine;

namespace HumanEcho {
    /// <summary>How the echo copies are coloured.</summary>
    public enum EchoColorMode {
        /// <summary>The hue steps from copy to copy (the rainbow ghosts of clip 27845).</summary>
        HueRamp = 0,
        /// <summary>Every copy is <see cref="EchoSettings.solidColor"/> (the red echoes of clip
        /// 14008).</summary>
        Solid = 1
    }

    /// <summary>How one echo copy is drawn.</summary>
    public enum EchoLook {
        /// <summary>A flat silhouette.</summary>
        Solid = 0,
        /// <summary>The silhouette as a field of round dots (a point-cloud ghost).</summary>
        Stipple = 1,
        /// <summary>Only its outline.</summary>
        Outline = 2
    }

    /// <summary>
    /// The ECHO layer: the person mask of N moments ago, redrawn as copies, newest first, each more faded and
    /// shifted than the last. Plain
    /// serializable data, read every frame.
    /// </summary>
    [Serializable]
    public sealed class EchoSettings {
        public bool enabled = false;
        [Range(1, 16)] public int copies = 8;
        [Tooltip("Seconds between two copies: copy k shows the mask from about k * spacing seconds ago.")]
        [Min(0.02f)] public float spacingSeconds = 0.14f;
        public EchoColorMode colorMode = EchoColorMode.HueRamp;
        public Color solidColor = new Color(1f, 0.05f, 0.05f, 1f);
        [Tooltip("Hue (0..1) of the newest copy, and how far the hue moves per copy.")]
        [Range(0f, 1f)] public float hueStart = 0f;
        public float hueStep = 0.09f;
        [Tooltip("Hue cycles per second that move hueStart, so the whole ramp slowly rotates. 0 = fixed.")]
        public float hueDrift = 0.05f;
        [Range(0f, 1f)] public float saturation = 0.9f;
        [Range(0f, 1f)] public float value = 1f;
        [Range(0f, 1f)] public float opacityNewest = 0.85f;
        [Range(0f, 1f)] public float opacityOldest = 0.12f;
        [Tooltip("How far each older copy is shifted, in screen heights (x right, y up). Clip 14008 trails them sideways; 0 stacks them.")]
        public Vector2 offsetPerCopy = Vector2.zero;
        [Tooltip("Size change per older copy (0.02 = 2 % bigger each step, -0.02 = smaller).")]
        [Range(-0.1f, 0.1f)] public float scaleStep = 0f;
        public EchoLook look = EchoLook.Solid;
        [Range(40f, 400f)] public float dotsPerScreenHeight = 140f;
        [Range(0.02f, 0.5f)] public float dotSoftness = 0.2f;
        [Tooltip("A wavy shift of older copies (screen heights): the ribbons of clip 14008. 0 = straight copies.")]
        [Range(0f, 0.1f)] public float waveAmount = 0f;
        [Range(1f, 40f)] public float waveFrequency = 8f;
        [Range(0f, 4f)] public float waveSpeed = 1.2f;
        [Tooltip("HDR multiplier of the copies' colour, so Bloom makes them glow. 1 = no extra glow.")]
        [Range(0.5f, 4f)] public float gain = 1.2f;
        [Tooltip("The copy keeps its own mask edge (soft) or is hardened: 1 = the mask as sent.")]
        [Range(0.5f, 4f)] public float edgeContrast = 1.5f;
    }
}
