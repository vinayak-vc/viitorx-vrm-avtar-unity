using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// F-60 — which body part the sender anchored a frame on, which is how far its root can be trusted. Ordered:
    /// every step down is a weaker anchor. Senders that predate F-60 never say, and that means <see cref="Hips"/>.
    /// </summary>
    public enum PoseAnchor {
        /// <summary>Both hips were in the picture: the root is the measured mid-hip. The only anchor before F-60.</summary>
        Hips = 0,
        /// <summary>The hips were not in the picture (a laptop webcam, a desk): the root was ESTIMATED from the shoulder
        /// line and a torso offset learned from this person. The lower body is absent, not invented.</summary>
        Shoulders = 1,
        /// <summary>Only the face was in the picture: the root is estimated from the nose. The shoulders are absent
        /// too, so a consumer has a person to acknowledge but no torso to draw.</summary>
        Head = 2,
    }

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
        private PoseAnchor anchor;

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

        /// <summary>What anchored this frame's root. <see cref="PoseAnchor.Hips"/> unless the sender said otherwise.</summary>
        public PoseAnchor Anchor {
            get {
                return anchor;
            }
        }

        /// <summary>The root is an estimate, not the measured mid-hip: the hips are not in the picture.</summary>
        public bool RootEstimated {
            get {
                return anchor != PoseAnchor.Hips;
            }
        }

        public void SetAnchor(PoseAnchor value) {
            anchor = value;
        }

        /// <summary>
        /// The anchor a sender's <c>fr</c> number names. Anything it does not recognise is the HIPS: unknown data must
        /// not switch a frame into the relaxed range an estimated root gets.
        /// </summary>
        public static PoseAnchor AnchorFromWire(int value) {
            if (value < (int)PoseAnchor.Hips || value > (int)PoseAnchor.Head) {
                return PoseAnchor.Hips;
            }
            return (PoseAnchor)value;
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
            anchor = PoseAnchor.Hips;
        }
    }
}
