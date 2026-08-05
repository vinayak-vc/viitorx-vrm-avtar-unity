using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Immutable snapshot of body landmarks for one tracked frame. Indexed by <see cref="JointId"/>
    /// (33 BlazePose landmarks). When <see cref="IsValid"/> is false, no person was tracked.
    /// </summary>
    public sealed class PoseFrame {
        public const int LandmarkCount = 33;

        private readonly PoseLandmark[] landmarks;
        private readonly double timestampSeconds;
        private readonly bool isValid;

        public PoseFrame(PoseLandmark[] landmarks, double timestampSeconds, bool isValid) {
            this.landmarks = landmarks;
            this.timestampSeconds = timestampSeconds;
            this.isValid = isValid;
        }

        public bool IsValid {
            get {
                return isValid && landmarks != null && landmarks.Length >= LandmarkCount;
            }
        }

        public double TimestampSeconds {
            get {
                return timestampSeconds;
            }
        }

        public PoseLandmark GetLandmark(JointId joint) {
            int index = (int)joint;
            if (landmarks == null || index < 0 || index >= landmarks.Length) {
                return new PoseLandmark(Vector3.zero, 0f);
            }
            return landmarks[index];
        }
    }
}
