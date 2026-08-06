namespace VirtualMirror.Core {
    /// <summary>
    /// Finger curl values (0.0 open to 1.0 curled) for both hands for one tracked frame. Reusable and
    /// mutable to avoid per-frame allocations on the hot path: a producer owns a <see cref="HandFrame"/>
    /// instance, fills it in place each frame with <see cref="SetCurls"/> + <see cref="SetMeta"/>
    /// (or <see cref="MarkInvalid"/>), and hands the reference to a single consumer that reads it
    /// immediately. When <see cref="IsValid"/> is false, no hand was tracked.
    /// </summary>
    public sealed class HandFrame {
        private float leftThumbCurl;
        private float leftIndexCurl;
        private float leftMiddleCurl;
        private float leftRingCurl;
        private float leftLittleCurl;

        private float rightThumbCurl;
        private float rightIndexCurl;
        private float rightMiddleCurl;
        private float rightRingCurl;
        private float rightLittleCurl;

        private double timestampSeconds;
        private bool isValid;

        public float LeftThumbCurl {
            get {
                return leftThumbCurl;
            }
        }

        public float LeftIndexCurl {
            get {
                return leftIndexCurl;
            }
        }

        public float LeftMiddleCurl {
            get {
                return leftMiddleCurl;
            }
        }

        public float LeftRingCurl {
            get {
                return leftRingCurl;
            }
        }

        public float LeftLittleCurl {
            get {
                return leftLittleCurl;
            }
        }

        public float RightThumbCurl {
            get {
                return rightThumbCurl;
            }
        }

        public float RightIndexCurl {
            get {
                return rightIndexCurl;
            }
        }

        public float RightMiddleCurl {
            get {
                return rightMiddleCurl;
            }
        }

        public float RightRingCurl {
            get {
                return rightRingCurl;
            }
        }

        public float RightLittleCurl {
            get {
                return rightLittleCurl;
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

        public void SetCurls(
            float leftThumbCurl, float leftIndexCurl, float leftMiddleCurl, float leftRingCurl, float leftLittleCurl,
            float rightThumbCurl, float rightIndexCurl, float rightMiddleCurl, float rightRingCurl, float rightLittleCurl) {
            this.leftThumbCurl = leftThumbCurl;
            this.leftIndexCurl = leftIndexCurl;
            this.leftMiddleCurl = leftMiddleCurl;
            this.leftRingCurl = leftRingCurl;
            this.leftLittleCurl = leftLittleCurl;
            this.rightThumbCurl = rightThumbCurl;
            this.rightIndexCurl = rightIndexCurl;
            this.rightMiddleCurl = rightMiddleCurl;
            this.rightRingCurl = rightRingCurl;
            this.rightLittleCurl = rightLittleCurl;
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
