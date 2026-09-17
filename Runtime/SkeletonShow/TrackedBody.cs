using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-32 — one tracked person, ready to draw: a stable id and their world-space pose.
    ///
    /// THE ID IS THE CONTRACT. It is stable for as long as the sidecar holds this person and is
    /// never reused once retired, so an experience may key state on it - a score, a colour, a
    /// drawing, a trail - and know that state can never leak to a different visitor later. That
    /// guarantee is what separates "several skeletons on screen" from an experience people can
    /// actually play, and it is the reason the tracker exists at all.
    /// </summary>
    public readonly struct TrackedBody {
        /// <summary>Stable sidecar track id. 0 when a single-person sender is upstream, which has
        /// no ids - there is only ever one person, so one constant id is honest.</summary>
        public readonly int Id;

        /// <summary>World-space pose, with this person's OWN speed, velocity and floor history.</summary>
        public readonly SkeletonPose Pose;

        /// <summary>"CONFIRMED" (seen this frame), "LOST" (held through an occlusion on prediction)
        /// or "ATTRACT" (the synthetic demonstration figure). Draw a LOST person - flickering a body
        /// out because somebody walked in front of them looks far worse than a briefly stale pose -
        /// but do not SCORE them, and never score ATTRACT.</summary>
        public readonly string State;

        public TrackedBody(int id, SkeletonPose pose, string state) {
            Id = id;
            Pose = pose;
            State = state;
        }

        /// <summary>True when this person was actually seen this frame, so scoring them is honest.</summary>
        public bool IsScorable {
            get {
                return State == "CONFIRMED";
            }
        }
    }
}
