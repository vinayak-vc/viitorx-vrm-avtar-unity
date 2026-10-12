using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// The tracked body in VIEW space: metres from the centre of the screen, X right, Y up.
    /// Smoothed, with a per-joint weight that already includes the grace period and fade-out for
    /// joints that dropped out. Everything downstream (mask, fluid, debug) reads only this.
    /// Reused in place every frame.
    /// </summary>
    public sealed class ShowcaseBody {
        public readonly Vector2[] Position = new Vector2[Joint.SlotCount];
        public readonly Vector2[] Velocity = new Vector2[Joint.SlotCount];

        /// <summary>0 = not drawn, 1 = fully trusted. Includes grace and fade.</summary>
        public readonly float[] Weight = new float[Joint.SlotCount];

        /// <summary>True for slots that were measured THIS frame (not held).</summary>
        public readonly bool[] Fresh = new bool[Joint.SlotCount];

        /// <summary>Both shoulders were measured this frame, and the hip was either measured or - F-60, a body the frame
        /// cuts off - ESTIMATED by the sender from the shoulders (<see cref="RootEstimated"/>).</summary>
        public bool CoreFresh;

        /// <summary>The hips are not in the picture: <see cref="HipCentre"/> is the sender's estimate, there are no
        /// hip joints to draw, and the legs are absent. The torso above is measured as ever.</summary>
        public bool RootEstimated;

        /// <summary>Mean of the hips, in view metres.</summary>
        public Vector2 HipCentre;

        /// <summary>Hip speed in view metres per second.</summary>
        public Vector2 HipVelocity;

        /// <summary>Measured shoulder width in view metres; 0 until both shoulders were seen.</summary>
        public float ShoulderWidth;

        /// <summary>Whole-body movement 0..1 from the shared SkeletonPose.</summary>
        public float Energy;

        public void Clear() {
            for (int i = 0; i < Joint.SlotCount; i++) {
                Position[i] = Vector2.zero;
                Velocity[i] = Vector2.zero;
                Weight[i] = 0f;
                Fresh[i] = false;
            }
            CoreFresh = false;
            RootEstimated = false;
            HipCentre = Vector2.zero;
            HipVelocity = Vector2.zero;
            ShoulderWidth = 0f;
            Energy = 0f;
        }
    }
}
