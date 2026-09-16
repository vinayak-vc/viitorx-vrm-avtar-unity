using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-31 — a fixed ring of recorded poses, so an experience can ask "where was the body N seconds
    /// ago". Allocated once and never grown: this runs unattended for hours, and a history that grows
    /// with session length is a leak with a slow fuse.
    ///
    /// A TOP-LEVEL CLASS RATHER THAN A DETAIL OF <see cref="TimeEchoExperience"/>, because the ring
    /// indexing and the search are the only parts of that experience that can be subtly wrong in a
    /// way nobody would see — a mis-indexed ring shows a plausible body at a slightly wrong time, and
    /// no viewer can tell. Out here it is unit-testable with an injected clock, which is how the
    /// off-by-one cases below are actually pinned rather than argued about.
    ///
    /// Clock-injected: every method takes the time rather than reading <c>Time.time</c>, matching the
    /// shape of <c>target_ownership.py</c> and <c>TrackingStreamHealth</c>.
    /// </summary>
    public sealed class EchoBuffer {
        private readonly Vector3[][] positions;
        private readonly float[][] confidence;
        private readonly float[] times;
        private readonly int capacity;
        private int head = -1;
        private int filled;

        public EchoBuffer(int frames) {
            capacity = Mathf.Max(2, frames);
            positions = new Vector3[capacity][];
            confidence = new float[capacity][];
            times = new float[capacity];
            int i = 0;
            while (i < capacity) {
                positions[i] = new Vector3[PoseFrame.LandmarkCount];
                confidence[i] = new float[PoseFrame.LandmarkCount];
                i = i + 1;
            }
        }

        public int Capacity {
            get {
                return capacity;
            }
        }

        public int Count {
            get {
                return filled;
            }
        }

        /// <summary>How far back the buffer currently reaches, in seconds. Zero when empty. Used to
        /// tell a visitor the trail is still filling rather than broken.</summary>
        public float OldestAge(float now) {
            if (filled == 0) {
                return 0f;
            }
            return now - times[Oldest()];
        }

        private int Oldest() {
            return filled < capacity ? 0 : (head + 1) % capacity;
        }

        /// <summary>Record one frame. World positions, because an echo is a record of where the body
        /// actually WAS and must stay there while the person walks away from it.</summary>
        public void Push(SkeletonPose pose, float now) {
            head = (head + 1) % capacity;
            Vector3[] p = positions[head];
            float[] c = confidence[head];
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                p[i] = pose.World[i];
                // Presence folded into the stored confidence, so a replayed frame needs no second
                // array and a consumer has exactly one thing to test.
                c[i] = pose.Present[i] ? pose.Confidence[i] : 0f;
                i = i + 1;
            }
            times[head] = now;
            if (filled < capacity) {
                filled = filled + 1;
            }
        }

        /// <summary>
        /// The recorded frame nearest <paramref name="target"/>, or false when the buffer does not
        /// reach that far back yet.
        ///
        /// NEAREST, NOT INTERPOLATED, on purpose. At the 60 Hz record rate the worst error is 8 ms of
        /// motion, which is invisible; and interpolating across two frames would blend a real pose
        /// with a dropped one whenever tracking flickered, producing a body that was never in that
        /// position. P1-3 interpolates the LIVE pose because latency matters there. Here it does not.
        ///
        /// The returned arrays are the buffer's own. A caller that needs to modify them — to offset
        /// an echo in space — must copy first, or it corrupts the history for every other reader.
        /// </summary>
        public bool Sample(float target, out Vector3[] p, out float[] c) {
            p = null;
            c = null;
            if (filled == 0) {
                return false;
            }
            if (target < times[Oldest()]) {
                return false;
            }
            int best = head;
            float bestErr = float.MaxValue;
            int n = 0;
            while (n < filled) {
                int idx = ((head - n) % capacity + capacity) % capacity;
                float err = times[idx] - target;
                if (err < 0f) {
                    err = -err;
                }
                if (err < bestErr) {
                    bestErr = err;
                    best = idx;
                } else if (times[idx] < target) {
                    // Walking backwards in time, already past the target and no longer improving:
                    // nothing older can be nearer, so stop instead of scanning the whole ring for
                    // every echo of every frame.
                    break;
                }
                n = n + 1;
            }
            p = positions[best];
            c = confidence[best];
            return true;
        }

        public void Clear() {
            head = -1;
            filled = 0;
        }
    }
}
