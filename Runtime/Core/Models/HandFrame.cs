namespace VirtualMirror.Core {
    /// <summary>
    /// Immutable data model containing finger curl values (0.0 open to 1.0 curled) for both hands.
    /// </summary>
    public sealed class HandFrame {
        private readonly float leftThumbCurl;
        private readonly float leftIndexCurl;
        private readonly float leftMiddleCurl;
        private readonly float leftRingCurl;
        private readonly float leftLittleCurl;

        private readonly float rightThumbCurl;
        private readonly float rightIndexCurl;
        private readonly float rightMiddleCurl;
        private readonly float rightRingCurl;
        private readonly float rightLittleCurl;

        private readonly double timestampSeconds;
        private readonly bool isValid;

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

        public HandFrame(
            float leftThumbCurl, float leftIndexCurl, float leftMiddleCurl, float leftRingCurl, float leftLittleCurl,
            float rightThumbCurl, float rightIndexCurl, float rightMiddleCurl, float rightRingCurl, float rightLittleCurl,
            double timestampSeconds, bool isValid) {
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
            this.timestampSeconds = timestampSeconds;
            this.isValid = isValid;
        }
    }
}
