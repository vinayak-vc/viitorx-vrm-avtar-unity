using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>P1-1 / P1-4's verdict about one joint, mirrored from joint_tracker.TrackingState.
    /// The ints are the sidecar's own, so the two enums cannot drift apart silently.</summary>
    public enum JointTrackState {
        /// <summary>No tracker covers this joint. Only the 12 joints in the sidecar's
        /// DEFAULT_TRACKED have one; reporting the other 21 as healthy would overstate what the
        /// system actually knows about them.</summary>
        NoTracker = -1,
        Tracked = 0,
        Weak = 1,
        Predicted = 2,
        Lost = 3,
        Recovering = 4,
    }

    /// <summary>
    /// F-29 — THE TRUST CHANNEL. What the pipeline believes about the pose it just sent, carried
    /// alongside the pose itself so a consumer can SHOW it.
    ///
    /// WHY THIS IS NOT PART OF <see cref="PoseFrame"/>. PoseFrame is the tracking CONTRACT — where
    /// the joints are, in hip-centred metres, with a confidence. Every consumer in the project reads
    /// it and none of them should have to care that a diagnostic channel exists. This is the same
    /// separation <c>SkeletonShow.SkeletonPose</c> already makes for rendering concerns, for the same
    /// reason: a contract that accumulates every caller's extras stops being a contract.
    ///
    /// WHY IT IS WORTH CARRYING AT ALL. P1-1's tracker, P1-4's recovery and F-21's ownership decide
    /// something about every joint of every frame, and until now that reasoning reached only a log
    /// file. On screen, a confidently measured elbow and one being extrapolated through an occlusion
    /// are the same white dot. A viewer therefore cannot tell a working system from a lucky one, and
    /// neither can an operator standing next to it.
    ///
    /// HONESTY NOTE, and it matters when reading a HUD built on this: these values describe the most
    /// recently RECEIVED datagram, while the pose being drawn is P1-3's interpolation between two
    /// buffered poses (40 ms behind by default). They are therefore in step to within the
    /// presentation delay, not exactly. Making them exact would mean interpolating enum states
    /// between two frames, which has no meaning. <see cref="AgeSeconds"/> is how far apart they are.
    ///
    /// Reusable and mutable, like every other frame type here: the producer fills one instance in
    /// place and a single consumer reads it immediately.
    /// </summary>
    public sealed class TrackingTelemetry {
        public const int JointCount = 33;

        private readonly JointTrackState[] states = new JointTrackState[JointCount];
        private readonly bool[] depthMeasured = new bool[JointCount];
        private readonly float[] confidence = new float[JointCount];

        /// <summary>True when the sender supplied the F-29 fields at all. False for an older sidecar,
        /// which is a real and different condition from "everything is untracked" — a HUD must be
        /// able to say "this stream does not report its state" rather than invent a green light.</summary>
        public bool HasTrustChannel;

        /// <summary>True when the sender supplied per-joint depth provenance (<c>src</c>).</summary>
        public bool HasDepthChannel;

        /// <summary>F-21 ownership state ("LOCKED", "ACQUIRING", …), or null when the sidecar runs
        /// with --no-ownership. Null means "not running", NOT "not locked".</summary>
        public string OwnershipState;

        /// <summary>Sidecar-side latency in milliseconds: camera timestamp through to the payload
        /// being built. Negative when the sender does not report it. This is only the sidecar's leg —
        /// a consumer adds its own receive-to-present time for a true end-to-end figure.</summary>
        public float SidecarLatencyMs = -1f;

        /// <summary>How long ago the sidecar BUILT this payload, in seconds — the wire hop plus
        /// however long it has sat here. Added to <see cref="SidecarLatencyMs"/> it is the true
        /// end-to-end figure; neither half can be derived from the other side alone.</summary>
        public float AgeSeconds;

        public long Seq;

        /// <summary>The sidecar's send time, in EPOCH seconds (its <c>t</c> field). Comparable to
        /// DateTimeOffset.UtcNow because the sidecar runs on this machine.
        ///
        /// KEPT SEPARATE FROM <see cref="ArrivalSeconds"/> ON PURPOSE. Arrival is measured on the
        /// provider's own Stopwatch, which counts from provider start and is NOT an epoch. Subtracting
        /// one from the other yields the epoch itself — about 1.8e12 ms — which is precisely the
        /// mistake this pair of separately-named fields exists to prevent, and which an earlier
        /// version of this code made.</summary>
        public double SendEpochSeconds;

        /// <summary>Arrival time on the provider's Stopwatch clock. Only ever compare this to other
        /// values from that same clock.</summary>
        public double ArrivalSeconds;

        public JointTrackState State(int joint) {
            if (joint < 0 || joint >= JointCount) {
                return JointTrackState.NoTracker;
            }
            return states[joint];
        }

        /// <summary>True when this joint's depth was MEASURED by the stereo pair; false when it fell
        /// back to the hip plane or the model's own monocular estimate. On a video-file source every
        /// joint is false, which is correct and is the point — the HUD shows an honest 0% coverage
        /// rather than implying a sensor that is not there.</summary>
        public bool DepthMeasured(int joint) {
            if (joint < 0 || joint >= JointCount) {
                return false;
            }
            return depthMeasured[joint];
        }

        public float Confidence(int joint) {
            if (joint < 0 || joint >= JointCount) {
                return 0f;
            }
            return confidence[joint];
        }

        public void SetState(int joint, JointTrackState state) {
            if (joint >= 0 && joint < JointCount) {
                states[joint] = state;
            }
        }

        public void SetDepthMeasured(int joint, bool measured) {
            if (joint >= 0 && joint < JointCount) {
                depthMeasured[joint] = measured;
            }
        }

        public void SetConfidence(int joint, float value) {
            if (joint >= 0 && joint < JointCount) {
                confidence[joint] = Mathf.Clamp01(value);
            }
        }

        /// <summary>Joints the pipeline is actively tracking and currently trusts — TRACKED or
        /// RECOVERING. WEAK is deliberately excluded: it is the tracker saying it is unsure, and a
        /// count that includes it would report a struggling stream as healthy.</summary>
        public int TrustedJointCount() {
            int n = 0;
            int i = 0;
            while (i < JointCount) {
                if (states[i] == JointTrackState.Tracked || states[i] == JointTrackState.Recovering) {
                    n = n + 1;
                }
                i = i + 1;
            }
            return n;
        }

        /// <summary>How many joints carry a real stereo measurement this frame.</summary>
        public int MeasuredDepthCount() {
            int n = 0;
            int i = 0;
            while (i < JointCount) {
                if (depthMeasured[i]) {
                    n = n + 1;
                }
                i = i + 1;
            }
            return n;
        }

        /// <summary>Joints reported with any usable confidence, regardless of tracker state.</summary>
        public int ValidJointCount(float minConfidence) {
            int n = 0;
            int i = 0;
            while (i < JointCount) {
                if (confidence[i] > minConfidence) {
                    n = n + 1;
                }
                i = i + 1;
            }
            return n;
        }

        public void Reset() {
            int i = 0;
            while (i < JointCount) {
                states[i] = JointTrackState.NoTracker;
                depthMeasured[i] = false;
                confidence[i] = 0f;
                i = i + 1;
            }
            HasTrustChannel = false;
            HasDepthChannel = false;
            OwnershipState = null;
            SidecarLatencyMs = -1f;
            AgeSeconds = 0f;
            Seq = -1;
            SendEpochSeconds = 0.0;
            ArrivalSeconds = 0.0;
        }

        /// <summary>Copy into <paramref name="target"/>. Used to hand a worker-thread snapshot to the
        /// main thread without exposing the instance the receive thread is still writing.</summary>
        public void CopyTo(TrackingTelemetry target) {
            if (target == null) {
                return;
            }
            int i = 0;
            while (i < JointCount) {
                target.states[i] = states[i];
                target.depthMeasured[i] = depthMeasured[i];
                target.confidence[i] = confidence[i];
                i = i + 1;
            }
            target.HasTrustChannel = HasTrustChannel;
            target.HasDepthChannel = HasDepthChannel;
            target.OwnershipState = OwnershipState;
            target.SidecarLatencyMs = SidecarLatencyMs;
            target.AgeSeconds = AgeSeconds;
            target.Seq = Seq;
            target.SendEpochSeconds = SendEpochSeconds;
            target.ArrivalSeconds = ArrivalSeconds;
        }
    }
}
