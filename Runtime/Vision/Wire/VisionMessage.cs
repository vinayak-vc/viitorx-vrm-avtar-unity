using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("VirtualMirror.Tests")]

namespace VirtualMirror.Vision {
    /// <summary>
    /// The fields every hands and face datagram carries (the header of hand_wire.py / face_wire.py), filled in place by a parser. Reusable and
    /// mutable like <c>HandFrame</c>: a receiver owns instances and copies one to a consumer under a lock, so nothing is allocated per datagram.
    /// </summary>
    public abstract class VisionMessage {
        private string sessionId = string.Empty;
        private long sequence;
        private double timestampUnix;
        private float latencyMs;
        private VisionSource source;
        private int imageWidth;
        private int imageHeight;
        private float focalPixels;
        private bool mirrored;
        private int count;
        private int declaredCount;

        /// <summary>The producer session (<c>sid</c>). A new value is a new producer: a consumer should drop what it remembered about ids.</summary>
        public string SessionId {
            get {
                return sessionId;
            }
            internal set {
                sessionId = value;
            }
        }

        /// <summary>Per-session datagram counter (<c>seq</c>), increasing.</summary>
        public long Sequence {
            get {
                return sequence;
            }
            internal set {
                sequence = value;
            }
        }

        /// <summary>The sender's wall clock, unix seconds, when it built the datagram (<c>t</c>).</summary>
        public double TimestampUnix {
            get {
                return timestampUnix;
            }
            internal set {
                timestampUnix = value;
            }
        }

        /// <summary>Milliseconds the sidecar spent from reading the camera frame to building the datagram (<c>lat</c>).</summary>
        public float LatencyMs {
            get {
                return latencyMs;
            }
            internal set {
                latencyMs = value;
            }
        }

        public VisionSource Source {
            get {
                return source;
            }
            internal set {
                source = value;
            }
        }

        /// <summary>Width in pixels of the frame the model saw, after the sender's --rotate and --flip (<c>img[0]</c>).</summary>
        public int ImageWidth {
            get {
                return imageWidth;
            }
            internal set {
                imageWidth = value;
            }
        }

        public int ImageHeight {
            get {
                return imageHeight;
            }
            internal set {
                imageHeight = value;
            }
        }

        /// <summary>The focal length in pixels the 3D positions were computed with (<c>fpx</c>). ASSUMED by the sidecar from its --hfov: a position scales with it.</summary>
        public float FocalPixels {
            get {
                return focalPixels;
            }
            internal set {
                focalPixels = value;
            }
        }

        /// <summary>True when the sender mirrored the picture before the model (<c>mir</c>): a selfie view. Positions, yaw and roll are then mirrored too.</summary>
        public bool Mirrored {
            get {
                return mirrored;
            }
            internal set {
                mirrored = value;
            }
        }

        /// <summary>How many items (hands or faces) this message holds. Never more than the capacity it was created with.</summary>
        public int Count {
            get {
                return count;
            }
            internal set {
                count = value;
            }
        }

        /// <summary>How many items the datagram declared (<c>n</c>). More than <see cref="Count"/> when the datagram held more than this message has room for.</summary>
        public int DeclaredCount {
            get {
                return declaredCount;
            }
            internal set {
                declaredCount = value;
            }
        }

        public abstract void CopyFrom(VisionMessage other);

        internal void ResetHeader() {
            sequence = 0L;
            timestampUnix = 0.0;
            latencyMs = 0f;
            source = VisionSource.Unknown;
            imageWidth = 0;
            imageHeight = 0;
            focalPixels = 0f;
            mirrored = false;
            count = 0;
            declaredCount = 0;
        }

        protected void CopyHeaderFrom(VisionMessage other) {
            sessionId = other.sessionId;
            sequence = other.sequence;
            timestampUnix = other.timestampUnix;
            latencyMs = other.latencyMs;
            source = other.source;
            imageWidth = other.imageWidth;
            imageHeight = other.imageHeight;
            focalPixels = other.focalPixels;
            mirrored = other.mirrored;
            count = other.count;
            declaredCount = other.declaredCount;
        }
    }
}
