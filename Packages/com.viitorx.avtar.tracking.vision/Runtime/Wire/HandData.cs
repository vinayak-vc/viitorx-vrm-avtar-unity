using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// One hand from a hands datagram (hand_wire.py), in Unity terms. Reusable and mutable: a parser fills it in place.
    ///
    /// SPACES. <see cref="ImageLandmarks"/> are the wire's own: x and y are fractions of the frame (0..1, origin top-left, y DOWN), z is the
    /// network's relative depth in the same scale as x (smaller = nearer the camera). Everything else is Unity's: <see cref="WorldLandmarks"/>
    /// and <see cref="Position"/> are metres with x right, y UP, z forward (the wire's y is down, flipped on read).
    ///
    /// The position is an ESTIMATE from the hand's size against an average hand and an assumed field of view, absent when the sidecar refused the
    /// fit (<see cref="HasPosition"/> false, <see cref="PositionRefusal"/> says why). Never use <see cref="Position"/> without checking.
    /// </summary>
    public sealed class HandData {
        public const int LandmarkCount = 21;
        public const int FingerCount = 5;

        private readonly Vector3[] imageLandmarks = new Vector3[LandmarkCount];
        private readonly Vector3[] worldLandmarks = new Vector3[LandmarkCount];
        private readonly float[] fingerCurls = new float[FingerCount];
        private readonly bool[] fingerExtended = new bool[FingerCount];

        private int id;
        private HandSide side;
        private float handedness;
        private float confidence;
        private TrackState state;
        private bool hasPosition;
        private Vector3 position;
        private PositionRefusal positionRefusal;
        private float pinch;
        private HandGesture gesture;

        /// <summary>Stable per hand within a session, never reused.</summary>
        public int Id {
            get {
                return id;
            }
            internal set {
                id = value;
            }
        }

        public HandSide Side {
            get {
                return side;
            }
            internal set {
                side = value;
            }
        }

        /// <summary>The averaged handedness score the side was decided from, 0..1.</summary>
        public float Handedness {
            get {
                return handedness;
            }
            internal set {
                handedness = value;
            }
        }

        /// <summary>The landmark network's confidence for this frame, 0..1.</summary>
        public float Confidence {
            get {
                return confidence;
            }
            internal set {
                confidence = value;
            }
        }

        public TrackState State {
            get {
                return state;
            }
            internal set {
                state = value;
            }
        }

        /// <summary>21 landmarks in the wire's image space (x, y fractions of the frame, y down; z relative). MediaPipe order: 0 wrist, 1-4 thumb, 5-8 index, 9-12 middle, 13-16 ring, 17-20 little. Do not modify.</summary>
        public Vector3[] ImageLandmarks {
            get {
                return imageLandmarks;
            }
        }

        /// <summary>21 landmarks in metres, hand-centred, Unity axes (x right, y up, z forward). Do not modify.</summary>
        public Vector3[] WorldLandmarks {
            get {
                return worldLandmarks;
            }
        }

        public bool HasPosition {
            get {
                return hasPosition;
            }
            internal set {
                hasPosition = value;
            }
        }

        /// <summary>The hand's centre in camera space, metres, Unity axes (x right, y up, z forward). Only meaningful when <see cref="HasPosition"/>.</summary>
        public Vector3 Position {
            get {
                return position;
            }
            internal set {
                position = value;
            }
        }

        public PositionRefusal PositionRefusal {
            get {
                return positionRefusal;
            }
            internal set {
                positionRefusal = value;
            }
        }

        /// <summary>Curl 0 (straight) to 1 (closed) for thumb, index, middle, ring, little. Do not modify.</summary>
        public float[] FingerCurls {
            get {
                return fingerCurls;
            }
        }

        /// <summary>Whether each finger (thumb..little) is extended, with the sidecar's hysteresis. Do not modify.</summary>
        public bool[] FingerExtended {
            get {
                return fingerExtended;
            }
        }

        /// <summary>0..1: 1 when the thumb and index tips touch. Zero while the index finger is closed.</summary>
        public float Pinch {
            get {
                return pinch;
            }
            internal set {
                pinch = value;
            }
        }

        /// <summary>The sidecar's stabilised gesture. None below its confidence limit and for anything outside the closed set.</summary>
        public HandGesture Gesture {
            get {
                return gesture;
            }
            internal set {
                gesture = value;
            }
        }

        /// <summary>A landmark in camera space: the hand's position plus its world offset. False when the position was refused.</summary>
        public bool TryGetCameraSpaceLandmark(int index, out Vector3 point) {
            point = Vector3.zero;
            if (!hasPosition || index < 0 || index >= LandmarkCount) {
                return false;
            }
            point = position + worldLandmarks[index];
            return true;
        }

        public void CopyFrom(HandData other) {
            int i = 0;
            while (i < LandmarkCount) {
                imageLandmarks[i] = other.imageLandmarks[i];
                worldLandmarks[i] = other.worldLandmarks[i];
                i = i + 1;
            }
            i = 0;
            while (i < FingerCount) {
                fingerCurls[i] = other.fingerCurls[i];
                fingerExtended[i] = other.fingerExtended[i];
                i = i + 1;
            }
            id = other.id;
            side = other.side;
            handedness = other.handedness;
            confidence = other.confidence;
            state = other.state;
            hasPosition = other.hasPosition;
            position = other.position;
            positionRefusal = other.positionRefusal;
            pinch = other.pinch;
            gesture = other.gesture;
        }

        internal void Clear() {
            int i = 0;
            while (i < LandmarkCount) {
                imageLandmarks[i] = Vector3.zero;
                worldLandmarks[i] = Vector3.zero;
                i = i + 1;
            }
            i = 0;
            while (i < FingerCount) {
                fingerCurls[i] = 0f;
                fingerExtended[i] = false;
                i = i + 1;
            }
            id = 0;
            side = HandSide.Right;
            handedness = 0f;
            confidence = 0f;
            state = TrackState.Tracked;
            hasPosition = false;
            position = Vector3.zero;
            positionRefusal = PositionRefusal.None;
            pinch = 0f;
            gesture = HandGesture.None;
        }
    }
}
