namespace VirtualMirror.Core {
    /// <summary>
    /// Immutable frame of facial expression weights (0.0 to 1.0) for eyes, mouth, and emotions.
    /// </summary>
    public sealed class FaceFrame {
        private readonly float blinkLeft;
        private readonly float blinkRight;
        private readonly float mouthOpen;
        private readonly float smile;
        private readonly float angry;
        private readonly float surprised;
        private readonly double timestampSeconds;
        private readonly bool isValid;

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

        public FaceFrame(float blinkLeft, float blinkRight, float mouthOpen, float smile, float angry, float surprised, double timestampSeconds, bool isValid) {
            this.blinkLeft = blinkLeft;
            this.blinkRight = blinkRight;
            this.mouthOpen = mouthOpen;
            this.smile = smile;
            this.angry = angry;
            this.surprised = surprised;
            this.timestampSeconds = timestampSeconds;
            this.isValid = isValid;
        }
    }
}
