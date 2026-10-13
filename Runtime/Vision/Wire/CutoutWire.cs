using System;

namespace VirtualMirror.Vision {
    /// <summary>How the picture of a cutout frame is stored (header byte 45).</summary>
    public enum CutoutColorEncoding {
        None = 0,
        Rgba = 1,
        Jpeg = 2
    }

    /// <summary>How the mask of a cutout frame is stored (header byte 46).</summary>
    public enum CutoutMaskEncoding {
        Raw = 1,
        Deflate = 2
    }

    /// <summary>Why a cutout datagram was refused. The words are the Python encoder's (cutout_wire.REJECT_*), so a log line reads the same on both sides.</summary>
    public enum CutoutReject {
        None = 0,
        Short = 1,
        Magic = 2,
        Version = 3,
        Kind = 4,
        Header = 5,
        Chunk = 6,
        Size = 7,
        Dimensions = 8,
        Encoding = 9
    }

    /// <summary>The 72-byte header every datagram of a cutout frame starts with, decoded. A plain struct: reading a header allocates nothing.</summary>
    public struct CutoutHeader {
        public uint Session;
        public uint Sequence;
        public double SentSeconds;
        public float LatencyMs;
        public float InferMs;
        public int ChunkIndex;
        public int ChunkCount;
        public int ChunkOffset;
        public int PayloadLength;
        public byte Flags;
        public CutoutColorEncoding ColorEncoding;
        public CutoutMaskEncoding MaskEncoding;
        public int ModelId;
        public int ColorWidth;
        public int ColorHeight;
        public int MaskWidth;
        public int MaskHeight;
        public int ColorLength;
        public int MaskLength;
        public int ForegroundPermille;
        public int People;
        public int HeaderLength;
        public int ChunkLength;
    }

    /// <summary>
    /// The CUTOUT wire (python-sidecar~/cutout_wire.py documents every byte): one camera picture and its person mask per FRAME, a frame cut into
    /// UDP datagrams, default port 8903. This class reads and validates one datagram's header. Everything a receiver relies on is checked here and
    /// nothing is clamped or repaired: a datagram that does not make sense is refused with the reason, and a frame is never larger than
    /// <see cref="MaxPayloadBytes"/>, so a sender that lies cannot make a receiver allocate without limit.
    ///
    /// The rules are the Python encoder's own, and both are pinned to the same bytes (Tests/EditMode/VisionFixtures/cutout_*.bin, written by
    /// python-sidecar~/tools/video/make_vision_fixtures.py).
    /// </summary>
    public static class CutoutWire {
        public const int Version = 1;
        public const int KindFrame = 1;
        public const int DefaultPort = 8903;
        public const int HeaderBytes = 72;
        public const int MaxPayloadBytes = 48 * 1024 * 1024;
        public const int MaxDimension = 4096;
        public const int MaxChunkBytes = 65000;

        public const byte FlagMirrored = 1;
        public const byte FlagLive = 2;
        public const byte FlagNobody = 4;

        public const int ModelUnknown = 0;
        public const int ModelGeneral = 1;
        public const int ModelLandscape = 2;

        private const byte MagicV = (byte)'V';
        private const byte MagicX = (byte)'X';
        private const byte MagicC = (byte)'C';
        private const byte MagicO = (byte)'O';

        /// <summary>The number of <see cref="CutoutReject"/> values, for a counter array.</summary>
        public static readonly int RejectKindCount = Enum.GetValues(typeof(CutoutReject)).Length;

        /// <summary>True when frame number <paramref name="a"/> is later than <paramref name="b"/>, with the u32 wrap-around (a positive signed 32-bit difference).</summary>
        public static bool IsNewer(uint a, uint b) {
            int delta = unchecked((int)(a - b));
            return delta > 0;
        }

        /// <summary>
        /// Decodes and validates the header at the start of <paramref name="data"/>. Returns <see cref="CutoutReject.None"/> and fills
        /// <paramref name="header"/> when the datagram is a well-formed chunk of a version-1 frame; otherwise the reason it is not.
        /// </summary>
        public static CutoutReject TryReadHeader(byte[] data, int length, out CutoutHeader header) {
            header = new CutoutHeader();
            if (data == null || length < HeaderBytes || length > data.Length) {
                return CutoutReject.Short;
            }
            if (data[0] != MagicV || data[1] != MagicX || data[2] != MagicC || data[3] != MagicO) {
                return CutoutReject.Magic;
            }
            if (data[4] != Version) {
                return CutoutReject.Version;
            }
            if (data[5] != KindFrame) {
                return CutoutReject.Kind;
            }
            int headerLength = ReadU16(data, 6);
            if (headerLength < HeaderBytes || headerLength > length) {
                return CutoutReject.Header;
            }
            header.HeaderLength = headerLength;
            header.Session = ReadU32(data, 8);
            header.Sequence = ReadU32(data, 12);
            header.SentSeconds = ReadF64(data, 16);
            header.LatencyMs = ReadF32(data, 24);
            header.InferMs = ReadF32(data, 28);
            header.ChunkIndex = ReadU16(data, 32);
            header.ChunkCount = ReadU16(data, 34);
            long chunkOffset = ReadU32(data, 36);
            long payloadLength = ReadU32(data, 40);
            header.Flags = data[44];
            int colorEncoding = data[45];
            int maskEncoding = data[46];
            header.ModelId = data[47];
            header.ColorWidth = ReadU16(data, 48);
            header.ColorHeight = ReadU16(data, 50);
            header.MaskWidth = ReadU16(data, 52);
            header.MaskHeight = ReadU16(data, 54);
            long colorLength = ReadU32(data, 56);
            long maskLength = ReadU32(data, 60);
            header.ForegroundPermille = ReadU16(data, 64);
            header.People = data[66];
            header.ChunkLength = length - headerLength;

            if (header.ChunkCount < 1 || header.ChunkIndex >= header.ChunkCount) {
                return CutoutReject.Chunk;
            }
            if (header.ChunkLength < 1 || chunkOffset + header.ChunkLength > payloadLength) {
                return CutoutReject.Chunk;
            }
            if (colorLength + maskLength != payloadLength) {
                return CutoutReject.Size;
            }
            // The sizes are checked in the order the Python encoder checks them, so one malformed datagram has the same name on both sides.
            if (header.ColorWidth > MaxDimension || header.ColorHeight > MaxDimension || header.MaskWidth > MaxDimension || header.MaskHeight > MaxDimension) {
                return CutoutReject.Dimensions;
            }
            if (colorEncoding != (int)CutoutColorEncoding.None && colorEncoding != (int)CutoutColorEncoding.Rgba && colorEncoding != (int)CutoutColorEncoding.Jpeg) {
                return CutoutReject.Encoding;
            }
            if (maskEncoding != (int)CutoutMaskEncoding.Raw && maskEncoding != (int)CutoutMaskEncoding.Deflate) {
                return CutoutReject.Encoding;
            }
            if (header.MaskWidth < 1 || header.MaskHeight < 1) {
                return CutoutReject.Dimensions;
            }
            if (colorEncoding == (int)CutoutColorEncoding.None) {
                if (colorLength != 0) {
                    return CutoutReject.Size;
                }
            } else {
                if (header.ColorWidth < 1 || header.ColorHeight < 1) {
                    return CutoutReject.Dimensions;
                }
                if (colorLength < 1) {
                    return CutoutReject.Size;
                }
                if (colorEncoding == (int)CutoutColorEncoding.Rgba && colorLength != (long)header.ColorWidth * header.ColorHeight * 4L) {
                    return CutoutReject.Size;
                }
            }
            if (maskLength < 1) {
                return CutoutReject.Size;
            }
            if (maskEncoding == (int)CutoutMaskEncoding.Raw && maskLength != (long)header.MaskWidth * header.MaskHeight) {
                return CutoutReject.Size;
            }
            if (payloadLength > MaxPayloadBytes) {
                return CutoutReject.Size;
            }
            header.ColorEncoding = (CutoutColorEncoding)colorEncoding;
            header.MaskEncoding = (CutoutMaskEncoding)maskEncoding;
            header.ChunkOffset = (int)chunkOffset;
            header.PayloadLength = (int)payloadLength;
            header.ColorLength = (int)colorLength;
            header.MaskLength = (int)maskLength;
            return CutoutReject.None;
        }

        private static int ReadU16(byte[] data, int offset) {
            return data[offset] | (data[offset + 1] << 8);
        }

        private static uint ReadU32(byte[] data, int offset) {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static float ReadF32(byte[] data, int offset) {
            return BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(data, offset)));
        }

        private static double ReadF64(byte[] data, int offset) {
            ulong low = ReadU32(data, offset);
            ulong high = ReadU32(data, offset + 4);
            return BitConverter.Int64BitsToDouble(unchecked((long)(low | (high << 32))));
        }
    }
}
