using System.Collections.Generic;

using NUnit.Framework;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="CutoutWire"/>: the 72-byte header every cutout datagram starts with, read from the bytes python-sidecar~/cutout_wire.py wrote
    /// (Tests/EditMode/VisionFixtures/cutout_*.bin). The Python suite pins the same files to its own encoder and parser, so a field moved on either
    /// side fails in one of the two.
    /// </summary>
    public sealed class CutoutWireTests {
        private static CutoutHeader ReadOk(string fixture) {
            byte[] data = VisionFixtures.Load(fixture);
            CutoutHeader header;
            CutoutReject reason = CutoutWire.TryReadHeader(data, data.Length, out header);
            Assert.AreEqual(CutoutReject.None, reason, fixture);
            return header;
        }

        [Test]
        public void TheFirstDatagramOfTheReferenceFrameReadsEveryField() {
            CutoutHeader header = ReadOk("cutout_rgba_00.bin");
            Assert.AreEqual(0xA1B2C3D4u, header.Session);
            Assert.AreEqual(1042u, header.Sequence);
            Assert.AreEqual(1791890123.456, header.SentSeconds, 1e-6);
            Assert.AreEqual(38.2f, header.LatencyMs, 1e-4f);
            Assert.AreEqual(12.5f, header.InferMs, 1e-6f);
            Assert.AreEqual(0, header.ChunkIndex);
            Assert.AreEqual(4, header.ChunkCount);
            Assert.AreEqual(0, header.ChunkOffset);
            Assert.AreEqual(816, header.PayloadLength, "16x12x4 picture + 8x6 mask");
            Assert.AreEqual(CutoutWire.FlagMirrored | CutoutWire.FlagLive, header.Flags);
            Assert.AreEqual(CutoutColorEncoding.Rgba, header.ColorEncoding);
            Assert.AreEqual(CutoutMaskEncoding.Raw, header.MaskEncoding);
            Assert.AreEqual(CutoutWire.ModelGeneral, header.ModelId);
            Assert.AreEqual(16, header.ColorWidth);
            Assert.AreEqual(12, header.ColorHeight);
            Assert.AreEqual(8, header.MaskWidth);
            Assert.AreEqual(6, header.MaskHeight);
            Assert.AreEqual(768, header.ColorLength);
            Assert.AreEqual(48, header.MaskLength);
            Assert.AreEqual(321, header.ForegroundPermille);
            Assert.AreEqual(2, header.People);
            Assert.AreEqual(CutoutWire.HeaderBytes, header.HeaderLength);
            Assert.AreEqual(256, header.ChunkLength);
        }

        [Test]
        public void TheLastDatagramCarriesTheRemainderAtItsOffset() {
            CutoutHeader header = ReadOk("cutout_rgba_03.bin");
            Assert.AreEqual(3, header.ChunkIndex);
            Assert.AreEqual(768, header.ChunkOffset);
            Assert.AreEqual(48, header.ChunkLength, "816 - 3 x 256");
            Assert.AreEqual(1042u, header.Sequence, "every chunk names the frame it belongs to");
        }

        [Test]
        public void TheMaskOnlyFrameHasNoColourAndADeflatedMask() {
            CutoutHeader header = ReadOk("cutout_maskonly_00.bin");
            Assert.AreEqual(CutoutColorEncoding.None, header.ColorEncoding);
            Assert.AreEqual(0, header.ColorLength);
            Assert.AreEqual(CutoutMaskEncoding.Deflate, header.MaskEncoding);
            Assert.AreEqual(CutoutWire.ModelLandscape, header.ModelId);
            Assert.AreEqual(32, header.MaskWidth);
            Assert.AreEqual(24, header.MaskHeight);
            Assert.AreEqual(CutoutWire.FlagLive, header.Flags);
        }

        [Test]
        public void TheJpegFrameIsAcceptedWhateverItsSize() {
            CutoutHeader header = ReadOk("cutout_jpeg_00.bin");
            Assert.AreEqual(CutoutColorEncoding.Jpeg, header.ColorEncoding);
            Assert.AreEqual(8, header.ColorWidth);
            Assert.AreEqual(3, header.ChunkCount);
            Assert.AreEqual(662 + 64, header.PayloadLength);
        }

        [Test]
        public void AHeaderIsReadFromTheStartOfALargerBufferAndTheLengthIsHonoured() {
            int length;
            byte[] padded = VisionFixtures.LoadPadded("cutout_rgba_03.bin", out length);
            CutoutHeader header;
            Assert.AreEqual(CutoutReject.None, CutoutWire.TryReadHeader(padded, length, out header));
            Assert.AreEqual(48, header.ChunkLength, "the padding after the datagram is not part of the chunk");
        }

        [Test]
        public void EveryMalformedFixtureIsRefusedForTheReasonItsNameGives() {
            AssertRefused("cutout_bad_magic.bin", CutoutReject.Magic);
            AssertRefused("cutout_bad_version.bin", CutoutReject.Version);
            AssertRefused("cutout_bad_kind.bin", CutoutReject.Kind);
            AssertRefused("cutout_bad_header_len.bin", CutoutReject.Header);
            AssertRefused("cutout_bad_chunk_index.bin", CutoutReject.Chunk);
            AssertRefused("cutout_bad_sizes.bin", CutoutReject.Size);
            AssertRefused("cutout_bad_dimension.bin", CutoutReject.Dimensions);
            AssertRefused("cutout_bad_encoding.bin", CutoutReject.Encoding);
            AssertRefused("cutout_truncated.bin", CutoutReject.Short);
        }

        private static void AssertRefused(string fixture, CutoutReject expected) {
            byte[] data = VisionFixtures.Load(fixture);
            CutoutHeader header;
            Assert.AreEqual(expected, CutoutWire.TryReadHeader(data, data.Length, out header), fixture);
        }

        [Test]
        public void NothingAndNothingUsefulAreRefusedAsShortWithoutThrowing() {
            CutoutHeader header;
            Assert.AreEqual(CutoutReject.Short, CutoutWire.TryReadHeader(null, 0, out header));
            Assert.AreEqual(CutoutReject.Short, CutoutWire.TryReadHeader(new byte[0], 0, out header));
            Assert.AreEqual(CutoutReject.Short, CutoutWire.TryReadHeader(new byte[71], 71, out header));
            byte[] data = VisionFixtures.Load("cutout_rgba_00.bin");
            Assert.AreEqual(CutoutReject.Short, CutoutWire.TryReadHeader(data, data.Length + 1, out header), "a length past the array is not trusted");
        }

        [Test]
        public void AHeaderThatDeclaresNoRoomForItsChunkIsRefused() {
            byte[] data = VisionFixtures.Load("cutout_rgba_00.bin");
            CutoutHeader header;
            Assert.AreEqual(CutoutReject.Chunk, CutoutWire.TryReadHeader(data, CutoutWire.HeaderBytes, out header), "a datagram that is only a header carries no chunk");
        }

        [Test]
        public void ARawPictureWhoseLengthIsNotItsDimensionsIsRefused() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            spec.ColorWidth = 15;
            List<byte[]> datagrams = CutoutTestFrames.Encode(spec);
            CutoutHeader header;
            Assert.AreEqual(CutoutReject.Size, CutoutWire.TryReadHeader(datagrams[0], datagrams[0].Length, out header));
        }

        [Test]
        public void AMaskWhoseLengthIsNotItsDimensionsIsRefusedWhenRawButNotWhenDeflated() {
            CutoutTestFrames.Spec raw = new CutoutTestFrames.Spec();
            raw.MaskHeight = 5;
            CutoutHeader header;
            byte[] rawDatagram = CutoutTestFrames.Encode(raw)[0];
            Assert.AreEqual(CutoutReject.Size, CutoutWire.TryReadHeader(rawDatagram, rawDatagram.Length, out header));
            CutoutTestFrames.Spec deflated = new CutoutTestFrames.Spec();
            deflated.MaskEncoding = CutoutMaskEncoding.Deflate;
            deflated.Mask = CutoutTestFrames.DeflateRaw(CutoutTestFrames.Ramp(8, 6));
            byte[] deflatedDatagram = CutoutTestFrames.Encode(deflated)[0];
            Assert.AreEqual(CutoutReject.None, CutoutWire.TryReadHeader(deflatedDatagram, deflatedDatagram.Length, out header), "a compressed length says nothing about the pixels");
        }

        [Test]
        public void APayloadPastTheLimitIsRefusedBeforeAnythingIsAllocatedForIt() {
            // The JPEG fixture (a colour section of any length, a raw 8x8 mask) made to claim 60 MB: the sections add up, the picture and the mask
            // are individually fine, and only the limit is wrong.
            byte[] data = VisionFixtures.Load("cutout_jpeg_00.bin");
            const uint colorLength = 59999936u;
            const uint maskLength = 64u;
            WriteUInt32(data, 40, colorLength + maskLength);
            WriteUInt32(data, 56, colorLength);
            WriteUInt32(data, 60, maskLength);
            CutoutHeader header;
            Assert.AreEqual(CutoutReject.Size, CutoutWire.TryReadHeader(data, data.Length, out header));
            Assert.Greater(colorLength + maskLength, (uint)CutoutWire.MaxPayloadBytes);
            WriteUInt32(data, 40, (uint)CutoutWire.MaxPayloadBytes);
            WriteUInt32(data, 56, (uint)CutoutWire.MaxPayloadBytes - maskLength);
            Assert.AreEqual(CutoutReject.None, CutoutWire.TryReadHeader(data, data.Length, out header), "exactly the limit is allowed");
        }

        private static void WriteUInt32(byte[] data, int offset, uint value) {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        [Test]
        public void TheTestEncoderReproducesThePythonFixturesByteForByte() {
            List<byte[]> mine = CutoutTestFrames.Encode(new CutoutTestFrames.Spec());
            Assert.AreEqual(4, mine.Count);
            int i = 0;
            while (i < mine.Count) {
                byte[] golden = VisionFixtures.Load("cutout_rgba_0" + i + ".bin");
                CollectionAssert.AreEqual(golden, mine[i], "datagram " + i);
                i = i + 1;
            }
            CutoutTestFrames.Spec next = new CutoutTestFrames.Spec();
            next.Sequence = 1043u;
            next.Color = CutoutTestFrames.RgbaPicture(16, 12, 200);
            List<byte[]> mineNext = CutoutTestFrames.Encode(next);
            i = 0;
            while (i < mineNext.Count) {
                CollectionAssert.AreEqual(VisionFixtures.Load("cutout_next_0" + i + ".bin"), mineNext[i], "next datagram " + i);
                i = i + 1;
            }
        }

        [Test]
        public void SequenceNumbersCompareAcrossTheWrapAround() {
            Assert.IsTrue(CutoutWire.IsNewer(1u, 0u));
            Assert.IsFalse(CutoutWire.IsNewer(0u, 1u));
            Assert.IsFalse(CutoutWire.IsNewer(7u, 7u));
            Assert.IsTrue(CutoutWire.IsNewer(0u, 0xFFFFFFFFu), "0 follows 2^32 - 1");
            Assert.IsFalse(CutoutWire.IsNewer(0xFFFFFFFFu, 0u));
            Assert.IsTrue(CutoutWire.IsNewer(5u, 0xFFFFFFFBu));
            Assert.IsFalse(CutoutWire.IsNewer(0x80000000u, 0u), "exactly half the range away is not newer");
            Assert.IsTrue(CutoutWire.IsNewer(0x7FFFFFFFu, 0u));
        }
    }
}
