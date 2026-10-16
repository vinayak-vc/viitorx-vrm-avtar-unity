using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using NUnit.Framework;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="CutoutReceiver"/> over a REAL UDP socket on loopback, fed the datagrams the Python encoder produced (the reference frame is four
    /// datagrams) and larger frames of the real chunk size. Staleness uses an injected clock; everything that crosses the socket polls with a deadline.
    /// </summary>
    public sealed class CutoutReceiverTests {
        private const int WaitMilliseconds = 3000;

        private sealed class Clock {
            public double Seconds;

            public double Now() {
                return Seconds;
            }
        }

        private UdpClient sender;

        [SetUp]
        public void SetUp() {
            sender = new UdpClient();
        }

        [TearDown]
        public void TearDown() {
            sender.Close();
        }

        private static CutoutReceiver Start(Clock clock) {
            CutoutReceiver receiver = new CutoutReceiver(0, true, 0.5, null, clock == null ? null : (Func<double>)clock.Now, null);
            Assert.IsTrue(receiver.Start(), "port 0 binds an ephemeral port");
            return receiver;
        }

        private void Send(CutoutReceiver receiver, byte[] data) {
            sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, receiver.Port));
        }

        private void SendAll(CutoutReceiver receiver, List<byte[]> datagrams) {
            int i = 0;
            while (i < datagrams.Count) {
                Send(receiver, datagrams[i]);
                i = i + 1;
            }
        }

        private static List<byte[]> Family(string name, int count) {
            List<byte[]> list = new List<byte[]>();
            int i = 0;
            while (i < count) {
                list.Add(VisionFixtures.Load("cutout_" + name + "_0" + i + ".bin"));
                i = i + 1;
            }
            return list;
        }

        private static void WaitFor(Func<bool> condition, string what) {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(WaitMilliseconds);
            while (!condition()) {
                if (DateTime.UtcNow > deadline) {
                    Assert.Fail("timed out waiting for " + what);
                }
                Thread.Sleep(2);
            }
        }

        [Test]
        public void AFrameSentAsFourDatagramsArrivesAsOneFrame() {
            using (CutoutReceiver receiver = Start(null)) {
                Assert.AreEqual(StreamState.NoStream, receiver.State);
                Assert.IsTrue(double.IsInfinity(receiver.AgeSeconds));
                CutoutFrame frame = new CutoutFrame();
                Assert.IsFalse(receiver.TryGetNew(frame), "nothing has arrived yet");
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the frame");
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.AreEqual(1042u, frame.Sequence);
                Assert.AreEqual(2, frame.People);
                Assert.AreEqual(16, frame.ColorWidth);
                Assert.AreEqual(105, frame.MaskArray[frame.MaskOffset + 2 * 8 + 5]);
                Assert.AreEqual(48, frame.Payload[(2 * 16 + 3) * 4], "pixel (3, 2), red");
                Assert.AreEqual(StreamState.Live, receiver.State);
                Assert.IsFalse(receiver.TryGetNew(frame), "a frame is new once");
                Assert.AreEqual(4L, receiver.Received);
            }
        }

        [Test]
        public void TheFrameCarriesThePicturesWallClockStampWhenItArrived() {
            Clock clock = new Clock();
            double stamp = 1791890123.5;
            using (CutoutReceiver receiver = new CutoutReceiver(0, true, 0.5, null, clock.Now, delegate () { return stamp; })) {
                Assert.IsTrue(receiver.Start());
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the frame");
                CutoutFrame frame = new CutoutFrame();
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.AreEqual(stamp, frame.ReceivedUnixSeconds, 1e-9);
                Assert.AreEqual(1791890123.456, frame.SentUnixSeconds, 1e-6);
                Assert.AreEqual(44.0, (frame.ReceivedUnixSeconds - frame.SentUnixSeconds) * 1000.0, 0.01, "the transport figure the HUD shows");
            }
        }

        [Test]
        public void TheStreamGoesStaleWhenFramesStopAndLiveAgainWhenTheyResume() {
            Clock clock = new Clock();
            using (CutoutReceiver receiver = Start(clock)) {
                clock.Seconds = 10.0;
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the first frame");
                Assert.AreEqual(StreamState.Live, receiver.State);
                clock.Seconds = 10.4;
                Assert.AreEqual(StreamState.Live, receiver.State, "0.4 s old is within the 0.5 s limit");
                Assert.AreEqual(0.4, receiver.AgeSeconds, 1e-9);
                clock.Seconds = 10.6;
                Assert.AreEqual(StreamState.Stale, receiver.State, "0.6 s old is past it");
                SendAll(receiver, Family("next", 4));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "the next frame");
                Assert.AreEqual(StreamState.Live, receiver.State);
            }
        }

        [Test]
        public void ADatagramOfAHalfSentFrameDoesNotMakeTheStreamLive() {
            using (CutoutReceiver receiver = Start(null)) {
                List<byte[]> datagrams = Family("rgba", 4);
                Send(receiver, datagrams[0]);
                Send(receiver, datagrams[1]);
                WaitFor(delegate () { return receiver.Received >= 2; }, "the two datagrams");
                Assert.AreEqual(StreamState.NoStream, receiver.State, "no whole frame has arrived");
                Assert.AreEqual(0L, receiver.Accepted);
            }
        }

        [Test]
        public void ATwoFramesBeforeTheConsumerLooksDropTheOlderAndCountIt() {
            using (CutoutReceiver receiver = Start(null)) {
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the first frame");
                SendAll(receiver, Family("next", 4));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "the second frame");
                Assert.AreEqual(1L, receiver.Overwritten, "the first was never taken");
                CutoutFrame frame = new CutoutFrame();
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.AreEqual(1043u, frame.Sequence, "the consumer gets the newest, never a queue");
                Assert.AreEqual(200, frame.Payload[(2 * 16 + 3) * 4 + 2], "pixel (3, 2), blue of the second frame");
            }
        }

        [Test]
        public void AFrameTakenBeforeTheNextArrivesIsNotCountedAsDropped() {
            using (CutoutReceiver receiver = Start(null)) {
                CutoutFrame frame = new CutoutFrame();
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the first frame");
                Assert.IsTrue(receiver.TryGetNew(frame));
                SendAll(receiver, Family("next", 4));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "the second frame");
                Assert.AreEqual(0L, receiver.Overwritten);
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.AreEqual(1043u, frame.Sequence);
            }
        }

        [Test]
        public void BadDatagramsAreCountedByReasonAndNeverThrow() {
            using (CutoutReceiver receiver = Start(null)) {
                Send(receiver, VisionFixtures.Load("cutout_bad_magic.bin"));
                Send(receiver, VisionFixtures.Load("cutout_bad_sizes.bin"));
                Send(receiver, VisionFixtures.Load("cutout_truncated.bin"));
                Send(receiver, new byte[] { 1, 2, 3 });
                WaitFor(delegate () { return receiver.Rejected >= 4; }, "the bad datagrams");
                Assert.AreEqual(1L, receiver.RejectedOf(CutoutReject.Magic));
                Assert.AreEqual(1L, receiver.RejectedOf(CutoutReject.Size));
                Assert.AreEqual(2L, receiver.RejectedOf(CutoutReject.Short));
                Assert.AreEqual(4L, receiver.Rejected);
                Assert.AreEqual(StreamState.NoStream, receiver.State);
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "a good frame after the bad ones");
            }
        }

        [Test]
        public void ASenderThatRestartsIsCountedAndItsFirstFrameIsShown() {
            using (CutoutReceiver receiver = Start(null)) {
                SendAll(receiver, Family("rgba", 4));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the first frame");
                SendAll(receiver, Family("other", 4));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "the restarted sender's frame");
                Assert.AreEqual(1L, receiver.SessionChanges);
                CutoutFrame frame = new CutoutFrame();
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.AreEqual(5u, frame.Sequence);
                Assert.AreEqual(0x0BADF00Du, frame.Session);
            }
        }

        [Test]
        public void ALargeFrameOfSixtyKilobyteDatagramsArrivesWholeThroughTheSocketBuffer() {
            using (CutoutReceiver receiver = Start(null)) {
                CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
                spec.ColorWidth = 1280;
                spec.ColorHeight = 720;
                spec.Color = CutoutTestFrames.RgbaPicture(1280, 720, 33);
                spec.MaskWidth = 640;
                spec.MaskHeight = 360;
                spec.Mask = CutoutTestFrames.Ramp(640, 360);
                spec.ChunkBytes = 60000;
                List<byte[]> datagrams = CutoutTestFrames.Encode(spec);
                Assert.AreEqual(66, datagrams.Count, "3.7 MB of picture and 230 KB of mask in 60 KB datagrams");
                SendAll(receiver, datagrams);
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the 720p frame");
                CutoutFrame frame = new CutoutFrame();
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.AreEqual(1280, frame.ColorWidth);
                int pixel = (300 * 1280 + 700) * 4;
                Assert.AreEqual(CutoutTestFrames.Low(700 * 16), frame.Payload[pixel]);
                Assert.AreEqual(CutoutTestFrames.Low(300 * 20), frame.Payload[pixel + 1]);
                Assert.AreEqual(33, frame.Payload[pixel + 2]);
                Assert.AreEqual(CutoutTestFrames.Low((180 * 640 + 320) * 5), frame.MaskArray[frame.MaskOffset + 180 * 640 + 320]);
                Assert.AreEqual(0L, receiver.Abandoned);
            }
        }

        [Test]
        public void ADeflatedMaskReachesTheConsumerInflated() {
            using (CutoutReceiver receiver = Start(null)) {
                Send(receiver, VisionFixtures.Load("cutout_maskonly_00.bin"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the mask-only frame");
                CutoutFrame frame = new CutoutFrame();
                Assert.IsTrue(receiver.TryGetNew(frame));
                Assert.IsFalse(frame.HasColor);
                Assert.AreEqual(255, frame.MaskArray[frame.MaskOffset + 6 * 32 + 8]);
                Assert.AreEqual(0, frame.MaskArray[frame.MaskOffset + 5 * 32 + 8]);
            }
        }

        [Test]
        public void AFrameTakenOnAnotherThreadIsNeverTornByTheStreamStillArriving() {
            using (CutoutReceiver receiver = Start(null)) {
                int taken = 0;
                int torn = 0;
                int stop = 0;
                Thread consumer = new Thread(delegate () {
                    CutoutFrame frame = new CutoutFrame();
                    while (Volatile.Read(ref stop) == 0) {
                        if (receiver.TryGetNew(frame)) {
                            taken = taken + 1;
                            // Every pixel of one frame has the same blue (the sequence's low byte); a frame mixed from two would not.
                            byte blue = frame.Payload[2];
                            int i = 0;
                            while (i < 16 * 12) {
                                if (frame.Payload[i * 4 + 2] != blue) {
                                    torn = torn + 1;
                                    break;
                                }
                                i = i + 1;
                            }
                        }
                    }
                });
                consumer.Start();
                int round = 0;
                while (round < 200) {
                    CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
                    spec.Sequence = (uint)(5000 + round);
                    spec.Color = CutoutTestFrames.RgbaPicture(16, 12, (byte)round);
                    SendAll(receiver, CutoutTestFrames.Encode(spec));
                    round = round + 1;
                    if (round % 10 == 0) {
                        Thread.Sleep(2);
                    }
                }
                WaitFor(delegate () { return receiver.Accepted + receiver.Abandoned >= 100; }, "most of the stream");
                Thread.Sleep(50);
                Volatile.Write(ref stop, 1);
                consumer.Join(1000);
                Assert.Greater(taken, 0);
                Assert.AreEqual(0, torn, "a frame the consumer holds is not written to by the receive thread");
            }
        }

        [Test]
        public void ATakenPortIsReportedAndStartFails() {
            UdpClient squatter = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            try {
                int port = ((IPEndPoint)squatter.Client.LocalEndPoint).Port;
                using (CutoutReceiver receiver = new CutoutReceiver(port, true, 0.5, null, null, null)) {
                    Assert.IsFalse(receiver.Start());
                    Assert.IsFalse(receiver.IsRunning);
                }
            } finally {
                squatter.Close();
            }
        }

        [Test]
        public void StoppingFreesThePortAndALaterStartWorks() {
            CutoutReceiver receiver = Start(null);
            int port = receiver.Port;
            receiver.Stop();
            UdpClient taken = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            taken.Close();
            Assert.IsFalse(receiver.IsRunning);
            receiver.Dispose();
        }

        [Test]
        public void TheReceiveQueueIsLargeEnoughForABurstOfSixtyKilobyteDatagrams() {
            Assert.GreaterOrEqual(CutoutReceiver.SocketBufferBytes, 8 * 1024 * 1024, "a 720p frame is 3.9 MB and arrives back to back");
        }
    }
}
