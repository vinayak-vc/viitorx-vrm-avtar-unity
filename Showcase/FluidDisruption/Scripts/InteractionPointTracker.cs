using ChromaticRain;

using UnityEngine;

using Joint = ChromaticRain.Joint;

namespace FluidDisruption {
    /// <summary>
    /// The three interaction points of the showcase (left hand, right hand, torso centre) read from the
    /// view-space <see cref="ShowcaseBody"/>, each with its own <see cref="MovementImpulseEstimator"/>.
    /// Pure logic.
    ///
    /// A point is valid only while its landmark was MEASURED this frame (not held from before) and the
    /// caller says a person is being followed. A hand whose wrist is missing, held or low-confidence
    /// therefore stops producing a track at once; it neither moves to the origin nor keeps a stale
    /// velocity, and it restarts without a jump when the landmark comes back.
    /// </summary>
    public sealed class InteractionPointTracker {
        private readonly MovementImpulseEstimator left = new MovementImpulseEstimator();
        private readonly MovementImpulseEstimator right = new MovementImpulseEstimator();
        private readonly MovementImpulseEstimator torso = new MovementImpulseEstimator();

        public MovementImpulseEstimator Left {
            get {
                return left;
            }
        }

        public MovementImpulseEstimator Right {
            get {
                return right;
            }
        }

        public MovementImpulseEstimator Torso {
            get {
                return torso;
            }
        }

        public void Reset() {
            left.Reset();
            right.Reset();
            torso.Reset();
        }

        /// <summary>One frame. <paramref name="following"/> is false while nobody is tracked, or before a
        /// person is confirmed.</summary>
        public void Update(ShowcaseBody body, bool following, float dt, FluidDisruptionSettings s) {
            left.Update(following && HandValid(body, Joint.LeftWrist),
                        HandPosition(body, Joint.LeftWrist, Joint.LeftHandEnd), dt, s.handMovement);
            right.Update(following && HandValid(body, Joint.RightWrist),
                         HandPosition(body, Joint.RightWrist, Joint.RightHandEnd), dt, s.handMovement);
            torso.Update(following && body.CoreFresh, TorsoPosition(body), dt, s.torsoMovement);
        }

        private static bool HandValid(ShowcaseBody body, int wrist) {
            return body.Fresh[wrist] && body.Weight[wrist] > 0.6f;
        }

        // The wrist, pulled halfway toward the end of the hand when hand tracking supplies it.
        private static Vector2 HandPosition(ShowcaseBody body, int wrist, int handEnd) {
            Vector2 p = body.Position[wrist];
            if (body.Weight[handEnd] > 0.5f) {
                p = Vector2.Lerp(p, body.Position[handEnd], 0.5f);
            }
            return p;
        }

        // The middle of the torso: shoulders and hips, weighted by how much each is trusted.
        private static Vector2 TorsoPosition(ShowcaseBody body) {
            Vector2 sum = Vector2.zero;
            float weight = 0f;
            AddJoint(body, Joint.LeftShoulder, ref sum, ref weight);
            AddJoint(body, Joint.RightShoulder, ref sum, ref weight);
            AddJoint(body, Joint.LeftHip, ref sum, ref weight);
            AddJoint(body, Joint.RightHip, ref sum, ref weight);
            return weight > 0.01f ? sum / weight : body.HipCentre;
        }

        private static void AddJoint(ShowcaseBody body, int joint, ref Vector2 sum, ref float weight) {
            float w = body.Weight[joint];
            sum += body.Position[joint] * w;
            weight += w;
        }
    }
}
