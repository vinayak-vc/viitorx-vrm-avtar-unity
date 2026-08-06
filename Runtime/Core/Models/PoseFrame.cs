using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Body landmarks for one tracked frame, indexed by <see cref="JointId"/> (33 BlazePose points).
    /// Reusable and mutable to avoid per-frame allocations on the hot path: a producer owns a
    /// <see cref="PoseFrame"/> instance, fills it in place each frame with <see cref="SetLandmark"/> +
    /// <see cref="SetMeta"/> (or <see cref="MarkInvalid"/>), and hands the reference to a single
    /// consumer that reads it immediately. When <see cref="IsValid"/> is false, no person was tracked.
    /// </summary>
    public sealed class PoseFrame {
        public const int LandmarkCount = 33;

        private readonly PoseLandmark[] landmarks;
        private double timestampSeconds;
        private bool isValid;

        public PoseFrame() {
            landmarks = new PoseLandmark[LandmarkCount];
        }

        public bool IsValid {
            get {
                return isValid;
            }
        }

        public double TimestampSeconds {
            get {
                return timestampSeconds;
            }
        }

        public PoseLandmark GetLandmark(JointId joint) {
            int index = (int)joint;
            if (index < 0 || index >= landmarks.Length) {
                return new PoseLandmark(Vector3.zero, 0f);
            }
            return landmarks[index];
        }

        public void SetLandmark(int index, PoseLandmark landmark) {
            if (index < 0 || index >= landmarks.Length) {
                return;
            }
            landmarks[index] = landmark;
        }

        public void SetMeta(double timestampSeconds, bool isValid) {
            this.timestampSeconds = timestampSeconds;
            this.isValid = isValid;
        }

        public void MarkInvalid(double timestampSeconds) {
            this.timestampSeconds = timestampSeconds;
            isValid = false;
        }
    }
}
