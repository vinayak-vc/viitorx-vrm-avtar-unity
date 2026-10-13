using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// Builds cutout datagrams for the tests: the same bytes python-sidecar~/cutout_wire.py (encode_datagrams) writes, laid out here from the
    /// documented header so a test can make a frame of any size, chunking, order or fault. It is checked against the Python fixtures
    /// (<see cref="CutoutWireTests"/>: it reproduces cutout_rgba_00..03.bin byte for byte), so a test frame is never built to a layout the real
    /// encoder does not use.
    /// </summary>
    internal static class CutoutTestFrames {
        public const uint ReferenceSession = 0xA1B2C3D4u;
        public const uint OtherSession = 0x0BADF00Du;
        public const uint ReferenceSequence = 1042u;
        public const double ReferenceSent = 1791890123.456;
        public const int ReferenceChunkBytes = 256;

        /// <summary>The fields of one frame, with the reference frame's values as the defaults.</summary>
        public sealed class Spec {
            public uint Session = ReferenceSession;
            public uint Sequence = ReferenceSequence;
            public double Sent = ReferenceSent;
            public float Latency = 38.2f;
            public float Infer = 12.5f;
            public byte Flags = CutoutWire.FlagMirrored | CutoutWire.FlagLive;
            public CutoutColorEncoding ColorEncoding = CutoutColorEncoding.Rgba;
            public CutoutMaskEncoding MaskEncoding = CutoutMaskEncoding.Raw;
            public int ModelId = CutoutWire.ModelGeneral;
            public int ColorWidth = 16;
            public int ColorHeight = 12;
            public int MaskWidth = 8;
            public int MaskHeight = 6;
            public int ForegroundPermille = 321;
            public int People = 2;
            public byte[] Color;
            public byte[] Mask;
            public int ChunkBytes = ReferenceChunkBytes;

            public Spec() {
                Color = RgbaPicture(16, 12, 128);
                Mask = Ramp(8, 6);
            }
        }

        /// <summary>The low eight bits of <paramref name="value"/>: what a byte holds after the test pictures' arithmetic wraps.</summary>
        public static byte Low(int value) {
            return unchecked((byte)value);
        }

        /// <summary>A raw RGBA picture whose pixel (x, y) is R = 16x, G = 20y, B = <paramref name="blue"/>, A = 255 (each wrapped to a byte).</summary>
        public static byte[] RgbaPicture(int width, int height, byte blue) {
            byte[] pixels = new byte[width * height * 4];
            int y = 0;
            while (y < height) {
                int x = 0;
                while (x < width) {
                    int at = (y * width + x) * 4;
                    pixels[at] = (byte)(x * 16);
                    pixels[at + 1] = (byte)(y * 20);
                    pixels[at + 2] = blue;
                    pixels[at + 3] = 255;
                    x = x + 1;
                }
                y = y + 1;
            }
            return pixels;
        }

        /// <summary>A mask whose pixel i (row-major) is 5 i, wrapped to a byte: every value differs from its neighbours, so a misplaced byte shows.</summary>
        public static byte[] Ramp(int width, int height) {
            byte[] mask = new byte[width * height];
            int i = 0;
            while (i < mask.Length) {
                mask[i] = (byte)(i * 5);
                i = i + 1;
            }
            return mask;
        }

        /// <summary>A raw RFC 1951 stream, which is what the sender's deflate_raw writes and what DeflateStream reads.</summary>
        public static byte[] DeflateRaw(byte[] data) {
            using (MemoryStream output = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) {
                    deflate.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        public static List<byte[]> Encode(Spec spec) {
            int total = spec.Color.Length + spec.Mask.Length;
            int count = Math.Max(1, (total + spec.ChunkBytes - 1) / spec.ChunkBytes);
            List<byte[]> datagrams = new List<byte[]>();
            int index = 0;
            while (index < count) {
                int start = index * spec.ChunkBytes;
                int end = Math.Min(total, start + spec.ChunkBytes);
                datagrams.Add(Datagram(spec, index, count, start, total, Slice(spec, start, end)));
                index = index + 1;
            }
            return datagrams;
        }

        /// <summary>One datagram with explicit chunk fields, so a test can lie about any of them.</summary>
        public static byte[] Datagram(Spec spec, int chunkIndex, int chunkCount, int chunkOffset, int payloadLength, byte[] chunk) {
            using (MemoryStream stream = new MemoryStream()) {
                using (BinaryWriter writer = new BinaryWriter(stream)) {
                    writer.Write((byte)'V');
                    writer.Write((byte)'X');
                    writer.Write((byte)'C');
                    writer.Write((byte)'O');
                    writer.Write((byte)1);
                    writer.Write((byte)1);
                    writer.Write((ushort)CutoutWire.HeaderBytes);
                    writer.Write(spec.Session);
                    writer.Write(spec.Sequence);
                    writer.Write(spec.Sent);
                    writer.Write(spec.Latency);
                    writer.Write(spec.Infer);
                    writer.Write((ushort)chunkIndex);
                    writer.Write((ushort)chunkCount);
                    writer.Write((uint)chunkOffset);
                    writer.Write((uint)payloadLength);
                    writer.Write(spec.Flags);
                    writer.Write((byte)spec.ColorEncoding);
                    writer.Write((byte)spec.MaskEncoding);
                    writer.Write((byte)spec.ModelId);
                    writer.Write((ushort)spec.ColorWidth);
                    writer.Write((ushort)spec.ColorHeight);
                    writer.Write((ushort)spec.MaskWidth);
                    writer.Write((ushort)spec.MaskHeight);
                    writer.Write((uint)spec.Color.Length);
                    writer.Write((uint)spec.Mask.Length);
                    writer.Write((ushort)spec.ForegroundPermille);
                    writer.Write((byte)spec.People);
                    writer.Write((byte)0);
                    writer.Write((uint)0);
                    writer.Write(chunk);
                }
                return stream.ToArray();
            }
        }

        private static byte[] Slice(Spec spec, int start, int end) {
            byte[] chunk = new byte[end - start];
            int i = 0;
            while (i < chunk.Length) {
                int at = start + i;
                chunk[i] = at < spec.Color.Length ? spec.Color[at] : spec.Mask[at - spec.Color.Length];
                i = i + 1;
            }
            return chunk;
        }
    }
}
