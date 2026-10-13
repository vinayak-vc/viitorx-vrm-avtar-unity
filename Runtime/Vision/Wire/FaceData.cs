using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// One face from a face datagram (face_wire.py), in Unity terms. Reusable and mutable: a parser fills it in place.
    ///
    /// SPACES. <see cref="ImageLandmarks"/> are the wire's own: x and y are fractions of the frame (0..1, origin top-left, y DOWN), z is the mesh
    /// network's relative depth in the same scale as x (smaller = nearer). <see cref="Position"/> is Unity's: metres, x right, y UP, z forward.
    /// <see cref="HeadRotation"/> is a rotation in those Unity axes with IDENTITY meaning a frontal, upright face looking at the camera: it turns
    /// the neutral look direction (0, 0, -1) to where the face looks. A character that faces +z needs a half turn about y to follow it.
    ///
    /// The position and the head angles are ESTIMATES (an assumed eye width and field of view; four landmarks), not measurements. The blendshapes
    /// and landmarks do not depend on those assumptions. <see cref="Yaw"/>, <see cref="Pitch"/> and <see cref="Roll"/> are degrees as the sidecar
    /// defines them: yaw positive moves the nose toward the image's right, pitch positive moves it down, roll positive tilts the eye line clockwise
    /// on screen (face_geometry.py). All three are mirrored when <see cref="VisionMessage.Mirrored"/>.
    /// </summary>
    public sealed class FaceData {
        public const int MeshLandmarkCount = 468;
        public const int LandmarkCapacity = 478;

        private readonly float[] blendshapes = new float[ArkitBlendshapes.Count];
        private readonly Vector3[] imageLandmarks = new Vector3[LandmarkCapacity];

        private int id;
        private float confidence;
        private TrackState state;
        private bool hasBlendshapes;
        private int landmarkCount;
        private bool hasPosition;
        private Vector3 position;
        private PositionRefusal positionRefusal;
        private float yaw;
        private float pitch;
        private float roll;
        private Quaternion headRotation = Quaternion.identity;
        private bool poseValid;

        public int Id {
            get {
                return id;
            }
            internal set {
                id = value;
            }
        }

        /// <summary>The mesh network's face-presence for this frame, 0..1.</summary>
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

        /// <summary>False when the sender ran without the blendshape network (<c>--no-blendshapes</c>); <see cref="Blendshapes"/> is then all zero.</summary>
        public bool HasBlendshapes {
            get {
                return hasBlendshapes;
            }
            internal set {
                hasBlendshapes = value;
            }
        }

        /// <summary>The 52 scores, 0..1, in <see cref="ArkitBlendshapes"/> order. Do not modify.</summary>
        public float[] Blendshapes {
            get {
                return blendshapes;
            }
        }

        public float Blendshape(int index) {
            if (index < 0 || index >= blendshapes.Length) {
                return 0f;
            }
            return blendshapes[index];
        }

        /// <summary>How many of <see cref="ImageLandmarks"/> are valid: 478 (mesh and iris), 468 (mesh only) or 0 (the sender ran with <c>--landmarks none</c>).</summary>
        public int LandmarkCount {
            get {
                return landmarkCount;
            }
            internal set {
                landmarkCount = value;
            }
        }

        /// <summary>The landmarks in the wire's image space (x, y fractions of the frame, y down; z relative). Valid up to <see cref="LandmarkCount"/>. Do not modify.</summary>
        public Vector3[] ImageLandmarks {
            get {
                return imageLandmarks;
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

        /// <summary>The point between the eyes in camera space, metres, Unity axes (x right, y up, z forward). Only meaningful when <see cref="HasPosition"/>.</summary>
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

        public float Yaw {
            get {
                return yaw;
            }
            internal set {
                yaw = value;
            }
        }

        public float Pitch {
            get {
                return pitch;
            }
            internal set {
                pitch = value;
            }
        }

        public float Roll {
            get {
                return roll;
            }
            internal set {
                roll = value;
            }
        }

        /// <summary>The head's rotation in Unity camera axes; identity is a frontal upright face. Only meaningful when <see cref="PoseValid"/>.</summary>
        public Quaternion HeadRotation {
            get {
                return headRotation;
            }
            internal set {
                headRotation = value;
            }
        }

        /// <summary>False when the sidecar's landmarks were degenerate and the pose fields are not valid.</summary>
        public bool PoseValid {
            get {
                return poseValid;
            }
            internal set {
                poseValid = value;
            }
        }

        public void CopyFrom(FaceData other) {
            int i = 0;
            while (i < blendshapes.Length) {
                blendshapes[i] = other.blendshapes[i];
                i = i + 1;
            }
            i = 0;
            while (i < other.landmarkCount) {
                imageLandmarks[i] = other.imageLandmarks[i];
                i = i + 1;
            }
            id = other.id;
            confidence = other.confidence;
            state = other.state;
            hasBlendshapes = other.hasBlendshapes;
            landmarkCount = other.landmarkCount;
            hasPosition = other.hasPosition;
            position = other.position;
            positionRefusal = other.positionRefusal;
            yaw = other.yaw;
            pitch = other.pitch;
            roll = other.roll;
            headRotation = other.headRotation;
            poseValid = other.poseValid;
        }

        internal void Clear() {
            int i = 0;
            while (i < blendshapes.Length) {
                blendshapes[i] = 0f;
                i = i + 1;
            }
            id = 0;
            confidence = 0f;
            state = TrackState.Tracked;
            hasBlendshapes = false;
            landmarkCount = 0;
            hasPosition = false;
            position = Vector3.zero;
            positionRefusal = PositionRefusal.None;
            yaw = 0f;
            pitch = 0f;
            roll = 0f;
            headRotation = Quaternion.identity;
            poseValid = false;
        }
    }
}
