using System;

namespace VirtualMirror.Vision {
    /// <summary>
    /// One reassembled cutout frame: the sender's metadata plus the two pixel sections, in buffers this object owns and REUSES. Nothing is
    /// allocated per frame once the buffers have grown to the stream's size.
    ///
    /// The payload is the colour section followed by the mask section, exactly as the wire carries them (CutoutWire: top row first). A consumer
    /// reads the colour at <see cref="ColorOffset"/> in <see cref="Payload"/> and the mask from <see cref="MaskArray"/> at <see cref="MaskOffset"/>:
    /// for a raw mask that is the payload itself, for a deflated one it is the receiver's inflated copy, so a consumer never branches on the encoding.
    /// A JPEG colour section is still compressed here: <see cref="CutoutTextures"/> decodes it.
    ///
    /// Frames change hands by <see cref="SwapWith"/>, which exchanges the buffers instead of copying them, so the receive thread, the hand-over slot
    /// and the consumer each keep one frame and a 4 MB picture is never copied between threads.
    /// </summary>
    public sealed class CutoutFrame {
        private byte[] payload = new byte[0];
        private byte[] inflatedMask = new byte[0];
        private CutoutHeader header;
        private double receivedUnixSeconds;

        public uint Session {
            get {
                return header.Session;
            }
        }

        /// <summary>The sender's frame counter; +1 per picture, wraps at 2^32.</summary>
        public uint Sequence {
            get {
                return header.Sequence;
            }
        }

        /// <summary>The sender's wall clock when it packed the frame, unix seconds.</summary>
        public double SentUnixSeconds {
            get {
                return header.SentSeconds;
            }
        }

        /// <summary>This machine's wall clock when the last chunk arrived, unix seconds. Only comparable with <see cref="SentUnixSeconds"/> when sender and receiver share a machine.</summary>
        public double ReceivedUnixSeconds {
            get {
                return receivedUnixSeconds;
            }
            internal set {
                receivedUnixSeconds = value;
            }
        }

        /// <summary>Milliseconds from the camera frame being read to the frame being packed (the sender's own figure).</summary>
        public float SenderLatencyMs {
            get {
                return header.LatencyMs;
            }
        }

        /// <summary>Milliseconds of segmentation work for this frame (the sender's own figure).</summary>
        public float InferMs {
            get {
                return header.InferMs;
            }
        }

        public byte Flags {
            get {
                return header.Flags;
            }
        }

        /// <summary>0 unknown, 1 the square selfie network, 2 the 16:9 one (<see cref="CutoutWire.ModelGeneral"/>).</summary>
        public int ModelId {
            get {
                return header.ModelId;
            }
        }

        public CutoutColorEncoding ColorEncoding {
            get {
                return header.ColorEncoding;
            }
        }

        public CutoutMaskEncoding MaskEncoding {
            get {
                return header.MaskEncoding;
            }
        }

        public int ColorWidth {
            get {
                return header.ColorWidth;
            }
        }

        public int ColorHeight {
            get {
                return header.ColorHeight;
            }
        }

        public int MaskWidth {
            get {
                return header.MaskWidth;
            }
        }

        public int MaskHeight {
            get {
                return header.MaskHeight;
            }
        }

        /// <summary>Bytes of the colour section at the start of <see cref="Payload"/>.</summary>
        public int ColorLength {
            get {
                return header.ColorLength;
            }
        }

        /// <summary>Bytes of the mask section after the colour section (the compressed size when the mask is deflated).</summary>
        public int MaskLength {
            get {
                return header.MaskLength;
            }
        }

        /// <summary>Foreground area, thousandths of the mask (0..1000).</summary>
        public int ForegroundPermille {
            get {
                return header.ForegroundPermille;
            }
        }

        /// <summary>People the sender cut out: the larger of the people it tracked and the foreground groups of the mask; 0 for nobody.</summary>
        public int People {
            get {
                return header.People;
            }
        }

        /// <summary>The colour section then the mask section. The array is usually longer than the payload: read only <see cref="ColorLength"/> and <see cref="MaskLength"/> bytes.</summary>
        public byte[] Payload {
            get {
                return payload;
            }
        }

        /// <summary>The mask as raw A8, <see cref="MaskWidth"/> x <see cref="MaskHeight"/> bytes starting at <see cref="MaskOffset"/>, top row first.</summary>
        public byte[] MaskArray {
            get {
                return header.MaskEncoding == CutoutMaskEncoding.Raw ? payload : inflatedMask;
            }
        }

        public int MaskOffset {
            get {
                return header.MaskEncoding == CutoutMaskEncoding.Raw ? header.ColorLength : 0;
            }
        }

        public int ColorOffset {
            get {
                return 0;
            }
        }

        /// <summary>True when the sender flipped the picture left to right before it segmented (a selfie view).</summary>
        public bool Mirrored {
            get {
                return (header.Flags & CutoutWire.FlagMirrored) != 0;
            }
        }

        /// <summary>True for a camera or a stream, false for a video file.</summary>
        public bool Live {
            get {
                return (header.Flags & CutoutWire.FlagLive) != 0;
            }
        }

        /// <summary>True when the sender found nobody; the mask is then empty, or the whole-picture fallback found nothing.</summary>
        public bool Nobody {
            get {
                return (header.Flags & CutoutWire.FlagNobody) != 0;
            }
        }

        public bool HasColor {
            get {
                return header.ColorEncoding != CutoutColorEncoding.None && header.ColorLength > 0;
            }
        }

        public bool HasMask {
            get {
                return header.MaskWidth > 0 && header.MaskHeight > 0 && header.MaskLength > 0;
            }
        }

        /// <summary>Foreground area as a fraction of the picture, 0..1.</summary>
        public float ForegroundFraction {
            get {
                return header.ForegroundPermille * 0.001f;
            }
        }

        /// <summary>Makes <see cref="Payload"/> at least <paramref name="length"/> bytes. A growing buffer gets slack so a stream whose frame size wobbles (JPEG) does not reallocate every frame; its old contents are not kept.</summary>
        internal void EnsurePayloadCapacity(int length) {
            if (payload.Length >= length) {
                return;
            }
            long grown = (long)length + (length >> 2);
            payload = new byte[(int)Math.Min(grown, CutoutWire.MaxPayloadBytes)];
        }

        /// <summary>Makes the inflated-mask buffer at least <paramref name="length"/> bytes and returns it.</summary>
        internal byte[] EnsureInflatedCapacity(int length) {
            if (inflatedMask.Length < length) {
                inflatedMask = new byte[length];
            }
            return inflatedMask;
        }

        internal void SetHeader(CutoutHeader value) {
            header = value;
        }

        /// <summary>Exchanges the metadata and both buffers with <paramref name="other"/>. No pixel is copied.</summary>
        internal void SwapWith(CutoutFrame other) {
            byte[] swapPayload = payload;
            payload = other.payload;
            other.payload = swapPayload;
            byte[] swapMask = inflatedMask;
            inflatedMask = other.inflatedMask;
            other.inflatedMask = swapMask;
            CutoutHeader swapHeader = header;
            header = other.header;
            other.header = swapHeader;
            double swapReceived = receivedUnixSeconds;
            receivedUnixSeconds = other.receivedUnixSeconds;
            other.receivedUnixSeconds = swapReceived;
        }
    }
}
