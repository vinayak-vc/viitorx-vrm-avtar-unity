using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// A single tracked landmark: a position plus a 0..1 confidence (visibility/presence).
    /// Position space is provider-defined (raw MediaPipe space); converters live in Retargeting.
    /// </summary>
    public readonly struct PoseLandmark {
        private readonly Vector3 position;
        private readonly float confidence;

        public PoseLandmark(Vector3 position, float confidence) {
            this.position = position;
            this.confidence = confidence;
        }

        public Vector3 Position {
            get {
                return position;
            }
        }

        public float Confidence {
            get {
                return confidence;
            }
        }
    }
}
