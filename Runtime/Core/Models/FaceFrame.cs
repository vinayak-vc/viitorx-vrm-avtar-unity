namespace VirtualMirror.Core {
    /// <summary>
    /// Facial expression weights (0.0 to 1.0) for eyes, mouth, and emotions for one tracked frame.
    /// Reusable and mutable to avoid per-frame allocations on the hot path: a producer owns a
    /// <see cref="FaceFrame"/> instance, fills it in place each frame with <see cref="SetExpressions"/> +
    /// <see cref="SetMeta"/> (or <see cref="MarkInvalid"/>), and hands the reference to a single consumer
    /// that reads it immediately. When <see cref="IsValid"/> is false, no face was tracked.
    /// </summary>
    public sealed class FaceFrame {
        private float blinkLeft;
        private float blinkRight;
        private float mouthOpen;
        private float smile;
        private float angry;
        private float surprised;
        private double timestampSeconds;
        private bool isValid;

        public float BlinkLeft {
            get {
                return blinkLeft;
            }
        }

        public float BlinkRight {
            get {
                return blinkRight;
            }
        }

        public float MouthOpen {
            get {
                return mouthOpen;
            }
        }

        public float Smile {
            get {
                return smile;
            }
        }

        public float Angry {
            get {
                return angry;
            }
        }

        public float Surprised {
            get {
                return surprised;
            }
        }

        public double TimestampSeconds {
            get {
                return timestampSeconds;
            }
        }

        public bool IsValid {
            get {
                return isValid;
            }
        }

        public void SetExpressions(float blinkLeft, float blinkRight, float mouthOpen, float smile, float angry, float surprised) {
            this.blinkLeft = blinkLeft;
            this.blinkRight = blinkRight;
            this.mouthOpen = mouthOpen;
            this.smile = smile;
            this.angry = angry;
            this.surprised = surprised;
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
