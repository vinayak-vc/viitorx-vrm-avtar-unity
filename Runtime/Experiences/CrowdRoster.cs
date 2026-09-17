using System.Collections.Generic;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — WHO ARRIVED AND WHO LEFT, between this frame and the last.
    ///
    /// WHY AN EXPERIENCE CANNOT USE THE PRESENCE GATE FOR THIS. <see cref="PresenceGate"/> answers
    /// "is there A person in the room", which is the right question for one visitor and the wrong one
    /// for several: with three people standing there it never changes, and a fourth walking in
    /// changes nothing it reports. Until F-34 that gate was also what made an experience reset
    /// itself, so in a crowd anybody walking up wiped everybody's progress.
    ///
    /// WHAT THIS IS FOR, AND WHAT IT IS NOT FOR. It is for RELEASING what was allocated to a person
    /// who has gone — an audio voice, a renderer, a colour slot. It is NOT a licence to accumulate a
    /// score per person: an id can change hands without any of this firing, because a slow identity
    /// switch keeps the id alive and swaps the human behind it (measured at 0.015 m per frame, with
    /// nothing logged). Departure is observable; substitution is not.
    ///
    /// Pure managed state with no Unity dependency beyond the body list, so it is unit-testable
    /// without an Editor.
    /// </summary>
    public sealed class CrowdRoster {
        private readonly HashSet<int> present = new HashSet<int>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private readonly List<int> arrived = new List<int>();
        private readonly List<int> left = new List<int>();

        /// <summary>Ids that appeared this frame and were not here last frame.</summary>
        public IReadOnlyList<int> Arrived {
            get {
                return arrived;
            }
        }

        /// <summary>Ids that were here last frame and are not here now.</summary>
        public IReadOnlyList<int> Left {
            get {
                return left;
            }
        }

        /// <summary>How many people are on the roster right now.</summary>
        public int Count {
            get {
                return present.Count;
            }
        }

        public bool Contains(int id) {
            return present.Contains(id);
        }

        /// <summary>Advance one frame from the crowd as published by the stage.</summary>
        public void Update(IReadOnlyList<TrackedBody> bodies) {
            arrived.Clear();
            left.Clear();
            seen.Clear();

            int i = 0;
            while (bodies != null && i < bodies.Count) {
                int id = bodies[i].Id;
                seen.Add(id);
                if (!present.Contains(id)) {
                    arrived.Add(id);
                }
                i = i + 1;
            }

            foreach (int id in present) {
                if (!seen.Contains(id)) {
                    left.Add(id);
                }
            }

            present.Clear();
            foreach (int id in seen) {
                present.Add(id);
            }
        }

        /// <summary>Empty the roster. Everybody currently present is reported as having LEFT on the
        /// next update, so whatever was allocated for them is still released.</summary>
        public void Clear() {
            present.Clear();
            arrived.Clear();
            left.Clear();
        }
    }
}
