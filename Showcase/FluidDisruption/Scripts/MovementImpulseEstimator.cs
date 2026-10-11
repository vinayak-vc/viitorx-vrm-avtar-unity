using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// Turns the position of ONE tracked point (a hand or the torso, in view metres) into a filtered
    /// velocity and a smooth 0..1 disturbance strength. Pure logic, no Unity objects.
    ///
    /// Rules that keep the fluid honest:
    /// - Velocity comes from consecutive valid positions and the REAL elapsed time between them.
    /// - An invalid sample (stale, lost, not tracked) resets the estimator: it neither extrapolates
    ///   nor keeps a stale velocity, and the first valid sample after it carries no velocity at all,
    ///   so re-acquiring a point can never produce a jump impulse.
    /// - A step faster than the glitch speed is a tracking jump: it re-seats the point silently.
    /// - Speed is clamped, filtered, and passed through a dead zone and a smooth ramp, so jitter
    ///   gives nothing and there is no hard threshold edge. A newly started track also eases its
    ///   strength in over a short time, so a point that just came back cannot kick the fluid.
    /// </summary>
    public sealed class MovementImpulseEstimator {
        private Vector2 previous;
        private bool hasPrevious;
        private float trackAge;

        /// <summary>The last valid position.</summary>
        public Vector2 Position { get; private set; }

        /// <summary>Filtered velocity in view metres per second.</summary>
        public Vector2 Velocity { get; private set; }

        /// <summary>Length of <see cref="Velocity"/>, clamped to the tuning's maximum.</summary>
        public float Speed { get; private set; }

        /// <summary>0 = still (inside the dead zone), 1 = full-speed gesture. Smooth in between.</summary>
        public float Intensity { get; private set; }

        /// <summary>True while a valid position is being tracked.</summary>
        public bool HasSample {
            get {
                return hasPrevious;
            }
        }

        /// <summary>Forget everything. The next valid position starts a new track.</summary>
        public void Reset() {
            hasPrevious = false;
            trackAge = 0f;
            Velocity = Vector2.zero;
            Speed = 0f;
            Intensity = 0f;
        }

        public void Update(bool valid, Vector2 position, float dt, MovementTuning tuning) {
            if (!valid || dt <= 0f || float.IsNaN(position.x) || float.IsNaN(position.y)
                || float.IsInfinity(position.x) || float.IsInfinity(position.y)) {
                if (!valid) {
                    Reset();
                }
                return;
            }

            if (!hasPrevious) {
                // First valid sample of a track: place the point, carry no motion.
                hasPrevious = true;
                trackAge = 0f;
                previous = position;
                Position = position;
                Velocity = Vector2.zero;
                Speed = 0f;
                Intensity = 0f;
                return;
            }

            trackAge += dt;
            Vector2 raw = (position - previous) / dt;
            previous = position;
            Position = position;

            float rawSpeed = raw.magnitude;
            if (rawSpeed > tuning.glitchSpeed) {
                // A jump, not a gesture: re-seat without injecting anything.
                Velocity = Vector2.zero;
                Speed = 0f;
                Intensity = 0f;
                return;
            }
            if (rawSpeed > tuning.maxSpeed) {
                raw *= tuning.maxSpeed / rawSpeed;
            }

            float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, tuning.smoothingSeconds));
            Velocity = Vector2.Lerp(Velocity, raw, a);
            Speed = Velocity.magnitude;

            float span = Mathf.Max(0.001f, tuning.fullSpeed - tuning.deadZoneSpeed);
            float t = Mathf.Clamp01((Speed - tuning.deadZoneSpeed) / span);
            float response = t * t * (3f - 2f * t);
            float ramp = Mathf.Clamp01(trackAge / Mathf.Max(0.001f, tuning.startRampSeconds));
            Intensity = response * ramp * ramp * (3f - 2f * ramp);
        }
    }
}
