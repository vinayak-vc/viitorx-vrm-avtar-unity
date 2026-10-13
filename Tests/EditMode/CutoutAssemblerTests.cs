using System.Collections.Generic;

using NUnit.Framework;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="CutoutAssembler"/>: datagrams in, whole frames out, with the loss, reordering, duplication and restarts a UDP burst can suffer.
    /// The reference frame is python-sidecar~/cutout_wire.py's own output (Tests/EditMode/VisionFixtures/cutout_*.bin); the larger and faulty frames
    /// come from <see cref="CutoutTestFrames"/>, which reproduces those bytes.
    /// </summary>
    public sealed class CutoutAssemblerTests {
        private static List<byte[]> Family(string name, int count) {
            List<byte[]> list = new List<byte[]>();
            int i = 0;
            while (i < count) {
                list.Add(VisionFixtures.Load("cutout_" + name + "_0" + i + ".bin"));
                i = i + 1;
            }
            return list;
        }

        private static bool Feed(CutoutAssembler assembler, byte[] datagram) {
            return assembler.Add(datagram, datagram.Length);
        }

        private static CutoutFrame Take(CutoutAssembler assembler) {
            CutoutFrame mine = new CutoutFrame();
            assembler.SwapFrameWith(mine);
            return mine;
        }

        private static void AssertReferencePixels(CutoutFrame frame, byte blue) {
            Assert.AreEqual(768, frame.ColorLength);
            // Pixel (3, 2) of the 16x12 picture is R = 48, G = 40, B = blue, A = 255; pixel (15, 11) is R = 240, G = 220.
            int at = (2 * 16 + 3) * 4;
            Assert.AreEqual(48, frame.Payload[frame.ColorOffset + at]);
            Assert.AreEqual(40, frame.Payload[frame.ColorOffset + at + 1]);
            Assert.AreEqual(blue, frame.Payload[frame.ColorOffset + at + 2]);
            Assert.AreEqual(255, frame.Payload[frame.ColorOffset + at + 3]);
            int last = (11 * 16 + 15) * 4;
            Assert.AreEqual(240, frame.Payload[frame.ColorOffset + last]);
            Assert.AreEqual(220, frame.Payload[frame.ColorOffset + last + 1]);
            // Mask pixel (5, 2) of the 8x6 mask is 5 (5 + 8 x 2) = 105.
            Assert.AreEqual(105, frame.MaskArray[frame.MaskOffset + 2 * 8 + 5]);
            Assert.AreEqual(0, frame.MaskArray[frame.MaskOffset]);
            Assert.AreEqual(235, frame.MaskArray[frame.MaskOffset + 47]);
        }

        [Test]
        public void TheReferenceFrameCompletesOnItsLastDatagramAndOnlyThen() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> datagrams = Family("rgba", 4);
            Assert.IsFalse(Feed(assembler, datagrams[0]));
            Assert.IsFalse(Feed(assembler, datagrams[1]));
            Assert.IsFalse(Feed(assembler, datagrams[2]));
            Assert.AreEqual(0L, assembler.Completed);
            Assert.IsTrue(Feed(assembler, datagrams[3]));
            Assert.AreEqual(1L, assembler.Completed);
            CutoutFrame frame = assembler.Frame;
            Assert.AreEqual(0xA1B2C3D4u, frame.Session);
            Assert.AreEqual(1042u, frame.Sequence);
            Assert.AreEqual(2, frame.People);
            Assert.AreEqual(321, frame.ForegroundPermille);
            Assert.IsTrue(frame.Mirrored);
            Assert.IsTrue(frame.Live);
            Assert.IsFalse(frame.Nobody);
            Assert.IsTrue(frame.HasColor);
            Assert.IsTrue(frame.HasMask);
            Assert.AreEqual(CutoutWire.ModelGeneral, frame.ModelId);
            Assert.AreEqual(0.321f, frame.ForegroundFraction, 1e-6f);
            AssertReferencePixels(frame, 128);
        }

        [Test]
        public void TheDatagramsMayArriveInAnyOrder() {
            int[][] orders = new int[][] {
                new int[] { 3, 2, 1, 0 },
                new int[] { 2, 0, 3, 1 },
                new int[] { 1, 3, 0, 2 }
            };
            int o = 0;
            while (o < orders.Length) {
                CutoutAssembler assembler = new CutoutAssembler();
                List<byte[]> datagrams = Family("rgba", 4);
                bool done = false;
                int i = 0;
                while (i < orders[o].Length) {
                    done = Feed(assembler, datagrams[orders[o][i]]);
                    i = i + 1;
                }
                Assert.IsTrue(done, "order " + o);
                AssertReferencePixels(assembler.Frame, 128);
                o = o + 1;
            }
        }

        [Test]
        public void ARepeatedDatagramIsCountedAndChangesNothing() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> datagrams = Family("rgba", 4);
            Feed(assembler, datagrams[0]);
            Feed(assembler, datagrams[1]);
            Assert.IsFalse(Feed(assembler, datagrams[1]));
            Assert.AreEqual(1L, assembler.Duplicate);
            Feed(assembler, datagrams[2]);
            Assert.IsTrue(Feed(assembler, datagrams[3]));
            AssertReferencePixels(assembler.Frame, 128);
            Assert.AreEqual(1L, assembler.Completed);
        }

        [Test]
        public void ALateRepeatOfACompletedFrameIsADuplicateNotANewFrame() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> datagrams = Family("rgba", 4);
            int i = 0;
            while (i < 4) {
                Feed(assembler, datagrams[i]);
                i = i + 1;
            }
            Assert.IsFalse(Feed(assembler, datagrams[0]), "a second copy of chunk 0 must not start a frame that can never finish");
            Assert.IsFalse(Feed(assembler, datagrams[2]));
            Assert.AreEqual(2L, assembler.Duplicate);
            Assert.AreEqual(1L, assembler.Completed);
            List<byte[]> next = Family("next", 4);
            i = 0;
            bool done = false;
            while (i < 4) {
                done = Feed(assembler, next[i]);
                i = i + 1;
            }
            Assert.IsTrue(done, "and the following frame is unaffected");
            AssertReferencePixels(assembler.Frame, 200);
        }

        [Test]
        public void ALostDatagramLosesThatFrameAndTheNextOneAbandonsIt() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> first = Family("rgba", 4);
            Feed(assembler, first[0]);
            Feed(assembler, first[1]);
            Feed(assembler, first[3]);
            Assert.AreEqual(0L, assembler.Completed, "chunk 2 never arrived");
            List<byte[]> next = Family("next", 4);
            Assert.IsFalse(Feed(assembler, next[0]));
            Assert.AreEqual(1L, assembler.Abandoned, "the unfinished frame is dropped as soon as a newer one starts");
            Feed(assembler, next[1]);
            Feed(assembler, next[2]);
            Assert.IsTrue(Feed(assembler, next[3]));
            AssertReferencePixels(assembler.Frame, 200);
            Assert.AreEqual(1043u, assembler.Frame.Sequence);
        }

        [Test]
        public void ADatagramOfAnOlderFrameIsIgnoredOnceANewerOneHasStarted() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> older = Family("rgba", 4);
            List<byte[]> newer = Family("next", 4);
            Feed(assembler, newer[0]);
            Assert.IsFalse(Feed(assembler, older[1]));
            Assert.AreEqual(1L, assembler.Stale);
            Feed(assembler, newer[1]);
            Feed(assembler, newer[2]);
            Assert.IsTrue(Feed(assembler, newer[3]));
            AssertReferencePixels(assembler.Frame, 200);
            Assert.AreEqual(0L, assembler.Abandoned);
        }

        [Test]
        public void ANewSessionIsANewProducerEvenWithALowerSequenceNumber() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> first = Family("rgba", 4);
            Feed(assembler, first[0]);
            Feed(assembler, first[1]);
            List<byte[]> restarted = Family("other", 4);
            bool done = false;
            int i = 0;
            while (i < 4) {
                done = Feed(assembler, restarted[i]);
                i = i + 1;
            }
            Assert.IsTrue(done, "sequence 5 of a new session is accepted although 1042 came before it");
            Assert.AreEqual(1L, assembler.SessionChanges);
            Assert.AreEqual(1L, assembler.Abandoned, "the half-assembled frame of the old session is dropped");
            Assert.AreEqual(0x0BADF00Du, assembler.Frame.Session);
            AssertReferencePixels(assembler.Frame, 50);
        }

        [Test]
        public void SequenceNumbersWrapAroundTwoToTheThirtyTwo() {
            CutoutAssembler assembler = new CutoutAssembler();
            CutoutTestFrames.Spec before = new CutoutTestFrames.Spec();
            before.Sequence = 0xFFFFFFFFu;
            CutoutTestFrames.Spec after = new CutoutTestFrames.Spec();
            after.Sequence = 0u;
            after.Color = CutoutTestFrames.RgbaPicture(16, 12, 77);
            bool done = false;
            foreach (byte[] datagram in CutoutTestFrames.Encode(before)) {
                done = Feed(assembler, datagram);
            }
            Assert.IsTrue(done);
            done = false;
            foreach (byte[] datagram in CutoutTestFrames.Encode(after)) {
                done = Feed(assembler, datagram);
            }
            Assert.IsTrue(done, "frame 0 follows frame 4294967295");
            Assert.AreEqual(0u, assembler.Frame.Sequence);
            Assert.AreEqual(0L, assembler.Stale);
            AssertReferencePixels(assembler.Frame, 77);
        }

        [Test]
        public void ASingleDatagramFrameCompletesImmediately() {
            CutoutAssembler assembler = new CutoutAssembler();
            byte[] datagram = VisionFixtures.Load("cutout_maskonly_00.bin");
            Assert.IsTrue(Feed(assembler, datagram), "a frame that fits one datagram needs no assembly");
            Assert.IsFalse(assembler.Frame.HasColor);
            Assert.AreEqual(CutoutColorEncoding.None, assembler.Frame.ColorEncoding);
        }

        [Test]
        public void ADeflatedMaskIsInflatedOnTheReceivingSide() {
            CutoutAssembler assembler = new CutoutAssembler();
            Assert.IsTrue(Feed(assembler, VisionFixtures.Load("cutout_maskonly_00.bin")));
            CutoutFrame frame = assembler.Frame;
            Assert.AreEqual(CutoutMaskEncoding.Deflate, frame.MaskEncoding);
            Assert.AreEqual(0, frame.MaskOffset, "the inflated copy has its own buffer");
            Assert.AreNotSame(frame.Payload, frame.MaskArray);
            // The 32x24 mask is a 16x12 rectangle of 255 whose top-left pixel is (8, 6).
            Assert.AreEqual(255, frame.MaskArray[6 * 32 + 8]);
            Assert.AreEqual(255, frame.MaskArray[17 * 32 + 23]);
            Assert.AreEqual(0, frame.MaskArray[5 * 32 + 8]);
            Assert.AreEqual(0, frame.MaskArray[18 * 32 + 23]);
            int set = 0;
            int i = 0;
            while (i < 32 * 24) {
                if (frame.MaskArray[i] == 255) {
                    set = set + 1;
                }
                i = i + 1;
            }
            Assert.AreEqual(16 * 12, set);
        }

        [Test]
        public void ADeflateStreamThatInflatesPastTheDeclaredSizeIsRefused() {
            CutoutAssembler assembler = new CutoutAssembler();
            Assert.IsFalse(Feed(assembler, VisionFixtures.Load("cutout_bad_inflate_00.bin")), "the header is fine; the stream is a decompression bomb");
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Size));
            Assert.AreEqual(0L, assembler.Completed);
        }

        [Test]
        public void ADeflateStreamThatInflatesToLessThanDeclaredIsRefused() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            spec.ColorEncoding = CutoutColorEncoding.None;
            spec.Color = new byte[0];
            spec.MaskEncoding = CutoutMaskEncoding.Deflate;
            spec.MaskWidth = 32;
            spec.MaskHeight = 24;
            spec.Mask = CutoutTestFrames.DeflateRaw(new byte[100]);
            CutoutAssembler assembler = new CutoutAssembler();
            bool done = false;
            foreach (byte[] datagram in CutoutTestFrames.Encode(spec)) {
                done = Feed(assembler, datagram);
            }
            Assert.IsFalse(done);
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Size));
        }

        [Test]
        public void GarbageWhereADeflateStreamShouldBeIsRefusedWithoutThrowing() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            spec.ColorEncoding = CutoutColorEncoding.None;
            spec.Color = new byte[0];
            spec.MaskEncoding = CutoutMaskEncoding.Deflate;
            spec.MaskWidth = 32;
            spec.MaskHeight = 24;
            spec.Mask = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            CutoutAssembler assembler = new CutoutAssembler();
            bool done = false;
            foreach (byte[] datagram in CutoutTestFrames.Encode(spec)) {
                done = Feed(assembler, datagram);
            }
            Assert.IsFalse(done);
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Size));
        }

        [Test]
        public void EveryMalformedFixtureIsCountedUnderItsReasonAndNothingThrows() {
            CutoutAssembler assembler = new CutoutAssembler();
            Feed(assembler, VisionFixtures.Load("cutout_bad_magic.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_version.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_kind.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_header_len.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_chunk_index.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_sizes.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_dimension.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_bad_encoding.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_truncated.bin"));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Magic));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Version));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Kind));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Header));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Chunk));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Size));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Dimensions));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Encoding));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Short));
            Assert.AreEqual(9L, assembler.Rejected);
            Assert.AreEqual(CutoutReject.Short, assembler.LastReject);
            Assert.AreEqual(0L, assembler.Completed);
        }

        [Test]
        public void GoodDatagramsAfterBadOnesStillAssemble() {
            CutoutAssembler assembler = new CutoutAssembler();
            Feed(assembler, VisionFixtures.Load("cutout_bad_magic.bin"));
            Feed(assembler, VisionFixtures.Load("cutout_truncated.bin"));
            bool done = false;
            foreach (byte[] datagram in Family("rgba", 4)) {
                done = Feed(assembler, datagram);
            }
            Assert.IsTrue(done);
            Assert.AreEqual(CutoutReject.None, assembler.LastReject, "the last datagram was fine");
        }

        [Test]
        public void ADatagramWhoseFrameFieldsDisagreeWithTheFirstChunkIsRefused() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> datagrams = Family("rgba", 4);
            Feed(assembler, datagrams[0]);
            CutoutTestFrames.Spec liar = new CutoutTestFrames.Spec();
            liar.MaskHeight = 6;
            liar.MaskWidth = 16;
            liar.Mask = CutoutTestFrames.Ramp(16, 6);
            byte[] second = CutoutTestFrames.Datagram(liar, 1, 4, 256, 768 + 96, new byte[256]);
            Assert.IsFalse(Feed(assembler, second));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Header), "same sequence, a different frame: the chunk is not used");
            Feed(assembler, datagrams[1]);
            Feed(assembler, datagrams[2]);
            Assert.IsTrue(Feed(assembler, datagrams[3]), "and the real frame still completes");
            AssertReferencePixels(assembler.Frame, 128);
        }

        [Test]
        public void ChunksThatDoNotAddUpToThePayloadAreRefusedNotShownWithHoles() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            CutoutAssembler assembler = new CutoutAssembler();
            // Two chunks of a 816-byte frame that together cover only 90 bytes: every index arrived, the bytes did not.
            Assert.IsFalse(Feed(assembler, CutoutTestFrames.Datagram(spec, 0, 2, 0, 816, new byte[50])));
            Assert.IsFalse(Feed(assembler, CutoutTestFrames.Datagram(spec, 1, 2, 50, 816, new byte[40])));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Size));
            Assert.AreEqual(0L, assembler.Completed);
            Assert.IsFalse(Feed(assembler, CutoutTestFrames.Datagram(spec, 0, 2, 0, 816, new byte[50])), "a straggler of the refused frame is a duplicate");
            Assert.AreEqual(1L, assembler.Duplicate);
        }

        [Test]
        public void AChunkThatWouldWritePastThePayloadIsRefusedAtTheHeader() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            CutoutAssembler assembler = new CutoutAssembler();
            Assert.IsFalse(Feed(assembler, CutoutTestFrames.Datagram(spec, 0, 4, 700, 816, new byte[200])));
            Assert.AreEqual(1L, assembler.RejectedOf(CutoutReject.Chunk));
        }

        [Test]
        public void AFrameOfManyChunksAssemblesFromAShuffledStream() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            spec.ColorWidth = 640;
            spec.ColorHeight = 360;
            spec.Color = CutoutTestFrames.RgbaPicture(640, 360, 9);
            spec.MaskWidth = 320;
            spec.MaskHeight = 180;
            spec.Mask = CutoutTestFrames.Ramp(320, 180);
            spec.ChunkBytes = 1200;
            List<byte[]> datagrams = CutoutTestFrames.Encode(spec);
            Assert.Greater(datagrams.Count, 700, "a 640x360 frame over a network-sized chunk is hundreds of datagrams");
            // A deterministic shuffle: walk the list with a stride that is coprime to its length.
            int count = datagrams.Count;
            int stride = 7;
            while (Gcd(stride, count) != 1) {
                stride = stride + 2;
            }
            CutoutAssembler assembler = new CutoutAssembler();
            bool done = false;
            int step = 0;
            int at = 0;
            while (step < count) {
                bool finished = Feed(assembler, datagrams[at]);
                if (step < count - 1) {
                    Assert.IsFalse(finished, "complete only when the last one arrives (step " + step + ")");
                } else {
                    done = finished;
                }
                at = (at + stride) % count;
                step = step + 1;
            }
            Assert.IsTrue(done);
            CutoutFrame frame = assembler.Frame;
            Assert.AreEqual(640 * 360 * 4, frame.ColorLength);
            int pixel = (100 * 640 + 200) * 4;
            Assert.AreEqual(CutoutTestFrames.Low(200 * 16), frame.Payload[pixel]);
            Assert.AreEqual(CutoutTestFrames.Low(100 * 20), frame.Payload[pixel + 1]);
            Assert.AreEqual(9, frame.Payload[pixel + 2]);
            Assert.AreEqual(CutoutTestFrames.Low((50 * 320 + 60) * 5), frame.MaskArray[frame.MaskOffset + 50 * 320 + 60]);
        }

        private static int Gcd(int a, int b) {
            while (b != 0) {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        [Test]
        public void ATakenFrameIsNeverOverwrittenByTheNextOneBeingAssembled() {
            CutoutAssembler assembler = new CutoutAssembler();
            foreach (byte[] datagram in Family("rgba", 4)) {
                Feed(assembler, datagram);
            }
            CutoutFrame a = Take(assembler);
            foreach (byte[] datagram in Family("next", 4)) {
                Feed(assembler, datagram);
            }
            CutoutFrame b = Take(assembler);
            Assert.AreNotSame(a.Payload, b.Payload, "each taken frame owns its buffer");
            AssertReferencePixels(a, 128);
            AssertReferencePixels(b, 200);
            Assert.AreEqual(1042u, a.Sequence);
            Assert.AreEqual(1043u, b.Sequence);
            foreach (byte[] datagram in Family("other", 4)) {
                Feed(assembler, datagram);
            }
            AssertReferencePixels(a, 128);
            AssertReferencePixels(b, 200);
        }

        [Test]
        public void ReusingTheSameConsumerFrameRecyclesTwoBuffersAndAllocatesNoMore() {
            CutoutAssembler assembler = new CutoutAssembler();
            CutoutFrame consumer = new CutoutFrame();
            List<byte[]> seen = new List<byte[]>();
            int round = 0;
            while (round < 8) {
                CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
                spec.Sequence = (uint)(2000 + round);
                bool done = false;
                foreach (byte[] datagram in CutoutTestFrames.Encode(spec)) {
                    done = Feed(assembler, datagram);
                }
                Assert.IsTrue(done);
                assembler.SwapFrameWith(consumer);
                AssertReferencePixels(consumer, 128);
                if (!seen.Contains(consumer.Payload)) {
                    seen.Add(consumer.Payload);
                }
                round = round + 1;
            }
            Assert.AreEqual(2, seen.Count, "the consumer and the assembler trade the same two arrays for the rest of the stream");
            Assert.LessOrEqual(consumer.Payload.Length, 1024, "816 bytes plus a quarter of slack: the buffers settle at the stream's size");
        }

        [Test]
        public void ResetForgetsThePartialFrameButKeepsTheCounters() {
            CutoutAssembler assembler = new CutoutAssembler();
            List<byte[]> datagrams = Family("rgba", 4);
            Feed(assembler, datagrams[0]);
            Feed(assembler, datagrams[1]);
            assembler.Reset();
            Feed(assembler, datagrams[2]);
            Assert.IsFalse(Feed(assembler, datagrams[3]), "chunks 0 and 1 were forgotten");
            Assert.AreEqual(0L, assembler.Completed);
            foreach (byte[] datagram in Family("next", 4)) {
                Feed(assembler, datagram);
            }
            Assert.AreEqual(1L, assembler.Completed);
        }
    }
}
