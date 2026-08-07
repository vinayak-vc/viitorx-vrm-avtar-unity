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
        private Vector3 rootPositionMetres;
        private bool hasRootPosition;

        public PoseFrame() {
            landmarks = new PoseLandmark[LandmarkCount];
        }

        public bool IsValid {
            get {
                return isValid;
            }
        }

        // Measured world position of the body anchor (mid-hip) in Unity metres, when the provider supplies
        // it (e.g. the OAK-D depth camera's spatial hip location). Lets the avatar translate with the user
        // (walk/jump), which the hip-CENTRED landmarks alone cannot do. False when unavailable (RGB paths).
        public Vector3 RootPositionMetres {
            get {
                return rootPositionMetres;
            }
        }

        public bool HasRootPosition {
            get {
                return hasRootPosition;
            }
        }

        public void SetRootPosition(Vector3 metres) {
            rootPositionMetres = metres;
            hasRootPosition = true;
        }

        public void ClearRootPosition() {
            hasRootPosition = false;
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
            hasRootPosition = false;
        }
    }
}
