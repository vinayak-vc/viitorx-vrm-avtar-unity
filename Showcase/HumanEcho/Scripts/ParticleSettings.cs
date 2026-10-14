using System;

using UnityEngine;

namespace HumanEcho {
    /// <summary>Where on the silhouette new particles appear.</summary>
    public enum ParticleEmitFrom {
        Interior = 0,
        Edge = 1,
        Both = 2
    }

    /// <summary>How particles are coloured.</summary>
    public enum ParticleColorMode {
        /// <summary>A hue that cycles over time, so what was emitted a moment ago keeps its colour (clip
        /// 11367).</summary>
        HueOverTime = 0,
        /// <summary>The camera picture's colour at the spot where the particle was born.</summary>
        Picture = 1,
        /// <summary>One colour (the red storms of clip 14008).</summary>
        Solid = 2
    }

    /// <summary>
    /// The GPU PARTICLE layer: dots and shaded spheres born on the person mask, carried by FluXY's velocity
    /// field (so they swirl with the
    /// performer), pulled by gravity and, optionally, piling up on a floor line. Plain serializable data,
    /// read every frame.
    /// </summary>
    [Serializable]
    public sealed class ParticleSettings {
        public bool enabled = false;
        [Tooltip("Size of the particle pool (a ring: the oldest are reused). 200000 is comfortable on the development GPU.")]
        [Range(1000, 400000)] public int maxParticles = 200000;
        [Tooltip("Particles born per second while the silhouette moves; scaled by motion if motionBoost is above 0.")]
        [Min(0f)] public float spawnPerSecond = 40000f;
        [Tooltip("Extra birth rate where the silhouette moves (0 = uniform, 1 = up to double where the motion is strongest).")]
        [Range(0f, 4f)] public float motionBoost = 1f;
        public ParticleEmitFrom emitFrom = ParticleEmitFrom.Both;
        [Range(0f, 1f)] public float edgeShare = 0.4f;
        [Min(0.1f)] public float lifetimeMin = 2.5f;
        [Min(0.1f)] public float lifetimeMax = 5f;
        [Tooltip("Particle diameter range in screen heights / 1000 (4 = a fine dot, 24 = a sphere of clip 14008).")]
        [Range(1f, 60f)] public float sizeMinThousandths = 3f;
        [Range(1f, 60f)] public float sizeMaxThousandths = 14f;
        [Tooltip("Share of the particles drawn as lit spheres; the rest are flat dots.")]
        [Range(0f, 1f)] public float sphereFraction = 0.25f;
        public ParticleColorMode colorMode = ParticleColorMode.HueOverTime;
        public float hueSpeed = 0.08f;
        [Range(0f, 1f)] public float saturation = 0.85f;
        public Color solidColor = new Color(1f, 0.04f, 0.04f, 1f);
        [Range(0.5f, 4f)] public float gain = 1.4f;

        [Header("Motion")]
        [Tooltip("Screen heights per second squared; negative falls (down is -y).")]
        public float gravity = -0.35f;
        [Tooltip("How much of FluXY's velocity the particles take on (0 = ignore the fluid).")]
        [Range(0f, 8f)] public float flowInfluence = 3f;
        [Tooltip("Velocity a particle is born with along the silhouette's own motion.")]
        [Range(0f, 4f)] public float motionKick = 1.5f;
        [Tooltip("Curl-noise swirl, screen heights per second.")]
        [Range(0f, 1f)] public float swirl = 0.12f;
        [Range(0.5f, 20f)] public float swirlScale = 4f;
        [Tooltip("Velocity lost per second (drag).")]
        [Range(0f, 5f)] public float drag = 0.6f;

        [Header("Floor pile")]
        public bool floorEnabled = false;
        [Tooltip("Height of the floor line from the bottom of the screen, 0..1 of the screen height.")]
        [Range(0f, 0.5f)] public float floorHeight = 0.08f;
        [Tooltip("Seconds a settled particle stays in the pile before it fades.")]
        [Min(0.5f)] public float pileSeconds = 8f;
        [Tooltip("How tall one landed particle makes the pile, in screen heights / 1000.")]
        [Range(0f, 4f)] public float pileGrowthThousandths = 0.6f;
        [Range(0f, 0.2f)] public float pileMaxHeight = 0.1f;
    }
}
