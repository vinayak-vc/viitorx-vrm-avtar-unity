using System;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>The interaction states A-F, in one enum with one place that changes it.</summary>
    public enum DisruptionState {
        /// <summary>A - nobody is tracked; only the baseline flow moves.</summary>
        Calm,
        /// <summary>B - a person was just confirmed; one gentle swirl around the body centre.</summary>
        Detected,
        /// <summary>C - a hand or the torso is moving; wakes follow it.</summary>
        Active,
        /// <summary>D - both hands are moving at once; their wakes coexist and merge.</summary>
        Interacting,
        /// <summary>E - the person stands still; no new impulses, the disturbance settles.</summary>
        Settling,
        /// <summary>F - tracking was lost; nothing injects, the fluid dissipates on its own.</summary>
        Lost
    }

    public enum FluidQuality {
        /// <summary>Lower solver resolution, no density supersampling.</summary>
        Performance,
        /// <summary>Higher solver resolution, supersampled dye.</summary>
        Quality
    }

    /// <summary>How movement speed becomes a 0..1 disturbance strength. One per kind of point.</summary>
    [Serializable]
    public sealed class MovementTuning {
        [Tooltip("Speed (view metres per second) below which a point is considered still: jitter lives here.")]
        public float deadZoneSpeed = 0.12f;
        [Tooltip("Speed at which the disturbance reaches full strength.")]
        public float fullSpeed = 1.6f;
        [Tooltip("Speed is clamped to this before it is used, so a tracking spike cannot become a storm.")]
        public float maxSpeed = 5f;
        [Tooltip("A step faster than this is a tracking glitch (a jump), not a gesture, and is ignored.")]
        public float glitchSpeed = 14f;
        [Tooltip("Time constant of the velocity filter in seconds. Larger = smoother, slower to react.")]
        public float smoothingSeconds = 0.08f;
        [Tooltip("A point that has just been (re)acquired eases its strength in over this long, so the "
                 + "catch-up of a hand that dropped out and came back is not read as a gesture.")]
        public float startRampSeconds = 0.2f;

        public MovementTuning() {
        }

        public MovementTuning(float deadZoneSpeed, float fullSpeed, float maxSpeed, float glitchSpeed,
                              float smoothingSeconds) {
            this.deadZoneSpeed = deadZoneSpeed;
            this.fullSpeed = fullSpeed;
            this.maxSpeed = maxSpeed;
            this.glitchSpeed = glitchSpeed;
            this.smoothingSeconds = smoothingSeconds;
        }
    }

    /// <summary>One slow ambient FLOW source of the calm baseline: a wide band that wanders on a small
    /// ellipse and gives the fluid a steady push along itself. It injects no dye; alternating directions
    /// at different heights make slow shear layers that bend the baseline bands into waves.</summary>
    [Serializable]
    public sealed class BaselineSourceConfig {
        [Tooltip("Orbit centre, as a fraction of the view: (0,0) is the middle of the screen, x and y in -0.5..0.5.")]
        public Vector2 centre = Vector2.zero;
        [Tooltip("Orbit half-extent as a fraction of the view.")]
        public Vector2 radius = new Vector2(0.05f, 0.07f);
        [Tooltip("Seconds for one orbit.")]
        public float period = 60f;
        [Tooltip("Start phase of the orbit, 0..1.")]
        [Range(0f, 1f)] public float phase;
        [Tooltip("Direction of the steady push, in degrees (0 = to the right).")]
        public float flowAngle;
        [Tooltip("Strength of the steady push in FluXY velocity units.")]
        public float flowStrength = 0.06f;
        [Tooltip("Width and height of the pushed band in metres.")]
        public Vector2 size = new Vector2(6f, 0.95f);
        [Tooltip("Tilt of the band in degrees.")]
        public float tilt;
        [Tooltip("Pushes per second.")]
        public float rate = 2f;

        public BaselineSourceConfig() {
        }

        public BaselineSourceConfig(Vector2 centre, Vector2 radius, float period, float phase, float flowAngle,
                                    float flowStrength, Vector2 size, float tilt, float rate) {
            this.centre = centre;
            this.radius = radius;
            this.period = period;
            this.phase = phase;
            this.flowAngle = flowAngle;
            this.flowStrength = flowStrength;
            this.size = size;
            this.tilt = tilt;
            this.rate = rate;
        }
    }
}
