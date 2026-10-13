using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using NUnit.Framework;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="HandsReceiver"/> and <see cref="FaceReceiver"/> over REAL UDP sockets on loopback, fed the datagrams the Python encoders produced.
    /// Staleness is tested with an injected clock (no sleeping); everything that crosses the socket polls with a deadline.
    /// </summary>
    public sealed class VisionReceiverTests {
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

        private HandsReceiver StartHands(Clock clock, int capacity) {
            HandsReceiver receiver = new HandsReceiver(0, true, 0.5, null, clock == null ? null : (Func<double>)clock.Now, capacity);
            Assert.IsTrue(receiver.Start(), "port 0 binds an ephemeral port");
            return receiver;
        }

        private FaceReceiver StartFace(Clock clock) {
            FaceReceiver receiver = new FaceReceiver(0, true, 0.5, null, clock == null ? null : (Func<double>)clock.Now, FaceMessage.DefaultCapacity);
            Assert.IsTrue(receiver.Start());
            return receiver;
        }

        private void Send(int port, byte[] data) {
            sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
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
        public void ADatagramSentOverUdpArrivesParsed() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                Assert.AreEqual(StreamState.NoStream, receiver.State);
                Assert.IsTrue(double.IsInfinity(receiver.AgeSeconds));
                HandsMessage message = new HandsMessage();
                Assert.IsFalse(receiver.TryGetNew(message), "nothing has arrived yet");
                Assert.IsFalse(receiver.TryGetLatest(message));
                Send(receiver.Port, VisionFixtures.Load("hands_two.json"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the datagram");
                Assert.IsTrue(receiver.TryGetNew(message));
                Assert.AreEqual(2, message.Count);
                Assert.AreEqual(HandGesture.Open, message.Hand(0).Gesture);
                Assert.AreEqual(1042L, message.Sequence);
                Assert.AreEqual(StreamState.Live, receiver.State);
                Assert.IsFalse(receiver.TryGetNew(message), "a message is new once");
                HandsMessage again = new HandsMessage();
                Assert.IsTrue(receiver.TryGetLatest(again), "but the latest stays available");
                Assert.AreEqual(2, again.Count);
            }
        }

        [Test]
        public void AFaceDatagramOfTwentyFiveKilobytesArrivesWhole() {
            using (FaceReceiver receiver = StartFace(null)) {
                Send(receiver.Port, VisionFixtures.Load("face_two.json"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the large datagram");
                FaceMessage message = new FaceMessage();
                Assert.IsTrue(receiver.TryGetNew(message));
                Assert.AreEqual(2, message.Count);
                Assert.AreEqual(FaceData.MeshLandmarkCount, message.Face(1).LandmarkCount);
            }
        }

        [Test]
        public void TheStreamGoesStaleWhenDatagramsStopAndLiveAgainWhenTheyResume() {
            Clock clock = new Clock();
            using (HandsReceiver receiver = StartHands(clock, 4)) {
                Assert.AreEqual(StreamState.NoStream, receiver.State);
                clock.Seconds = 10.0;
                Send(receiver.Port, VisionFixtures.Load("hands_two.json"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the first datagram");
                Assert.AreEqual(StreamState.Live, receiver.State);
                clock.Seconds = 10.4;
                Assert.AreEqual(StreamState.Live, receiver.State, "0.4 s old is within the 0.5 s limit");
                Assert.AreEqual(0.4, receiver.AgeSeconds, 1e-9);
                clock.Seconds = 10.6;
                Assert.AreEqual(StreamState.Stale, receiver.State, "0.6 s old is past it");
                Send(receiver.Port, VisionFixtures.Bytes(VisionFixtures.LoadText("hands_two.json").Replace("\"seq\":1042", "\"seq\":1043")));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "the next datagram");
                Assert.AreEqual(StreamState.Live, receiver.State);
            }
        }

        [Test]
        public void ARepeatedOrBackwardsSequenceInTheSameSessionIsCountedAndDropped() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                string text = VisionFixtures.LoadText("hands_two.json");
                Send(receiver.Port, VisionFixtures.Bytes(text));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "seq 1042");
                Send(receiver.Port, VisionFixtures.Bytes(text));
                WaitFor(delegate () { return receiver.OutOfOrder >= 1; }, "the duplicate");
                Send(receiver.Port, VisionFixtures.Bytes(text.Replace("\"seq\":1042", "\"seq\":1041")));
                WaitFor(delegate () { return receiver.OutOfOrder >= 2; }, "the older datagram");
                Send(receiver.Port, VisionFixtures.Bytes(text.Replace("\"seq\":1042", "\"seq\":1043")));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "seq 1043");
                Assert.AreEqual(2L, receiver.Accepted);
                Assert.AreEqual(2L, receiver.OutOfOrder);
                HandsMessage message = new HandsMessage();
                receiver.TryGetLatest(message);
                Assert.AreEqual(1043L, message.Sequence, "the consumer sees the newest accepted, never an older one");
            }
        }

        [Test]
        public void ANewSessionRestartsTheSequenceAndIsCounted() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                string text = VisionFixtures.LoadText("hands_two.json");
                Send(receiver.Port, VisionFixtures.Bytes(text));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the first session");
                Assert.AreEqual(0L, receiver.SessionChanges, "the first session is not a change");
                Send(receiver.Port, VisionFixtures.Bytes(text.Replace("hd0123456789", "hd9999999999").Replace("\"seq\":1042", "\"seq\":5")));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "a restarted sender");
                Assert.AreEqual(1L, receiver.SessionChanges);
                Assert.AreEqual(0L, receiver.OutOfOrder, "seq 5 after 1042 is a restart, not a stale datagram");
                HandsMessage message = new HandsMessage();
                receiver.TryGetLatest(message);
                Assert.AreEqual("hd9999999999", message.SessionId);
                Assert.AreEqual(5L, message.Sequence);
            }
        }

        [Test]
        public void GarbageIsCountedByReasonAndTheStreamCarriesOn() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                Send(receiver.Port, VisionFixtures.Load("bad_not_json.txt"));
                Send(receiver.Port, VisionFixtures.Load("bad_version.json"));
                Send(receiver.Port, VisionFixtures.Load("bad_kind.json"));
                Send(receiver.Port, VisionFixtures.Load("bad_truncated.json"));
                Send(receiver.Port, new byte[] { 0xff, 0xfe, 0x00, 0x01 });
                WaitFor(delegate () { return receiver.Received >= 5; }, "the bad datagrams");
                Send(receiver.Port, VisionFixtures.Load("hands_two.json"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "a good datagram after the bad ones");
                Assert.AreEqual(5L, receiver.Rejected);
                Assert.AreEqual(1L, receiver.CountOf(WireParseResult.UnknownVersion));
                Assert.AreEqual(1L, receiver.CountOf(WireParseResult.WrongKind));
                Assert.AreEqual(1L, receiver.CountOf(WireParseResult.Truncated));
                Assert.AreEqual(2L, receiver.CountOf(WireParseResult.NotJson));
                Assert.AreEqual(1L, receiver.CountOf(WireParseResult.Ok));
                Assert.AreEqual(StreamState.Live, receiver.State, "bad datagrams do not make the stream live, and good ones after them do");
            }
        }

        [Test]
        public void ARejectedDatagramNeverReplacesTheLastGoodMessage() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                Send(receiver.Port, VisionFixtures.Load("hands_two.json"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the good datagram");
                Send(receiver.Port, VisionFixtures.Load("bad_landmarks.json"));
                WaitFor(delegate () { return receiver.Rejected >= 1; }, "the bad datagram");
                HandsMessage message = new HandsMessage();
                Assert.IsTrue(receiver.TryGetLatest(message));
                Assert.AreEqual(2, message.Count);
                Assert.AreEqual(HandGesture.Open, message.Hand(0).Gesture);
            }
        }

        [Test]
        public void ARejectedDatagramWithANewerSequenceIsNotPublishedAsNew() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                string text = VisionFixtures.LoadText("hands_two.json");
                Send(receiver.Port, VisionFixtures.Bytes(text));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the good datagram");
                HandsMessage message = new HandsMessage();
                Assert.IsTrue(receiver.TryGetNew(message));
                string bad = VisionFixtures.LoadText("bad_landmarks.json").Replace("\"seq\":1042", "\"seq\":9000");
                Send(receiver.Port, VisionFixtures.Bytes(bad));
                WaitFor(delegate () { return receiver.Rejected >= 1; }, "the bad datagram");
                Assert.IsFalse(receiver.TryGetNew(message), "a refused datagram is not something new");
                Assert.IsTrue(receiver.TryGetLatest(message));
                Assert.AreEqual(1042L, message.Sequence);
                Send(receiver.Port, VisionFixtures.Bytes(text.Replace("\"seq\":1042", "\"seq\":1043")));
                WaitFor(delegate () { return receiver.Accepted >= 2; }, "a good datagram after the refused one");
                Assert.AreEqual(0L, receiver.OutOfOrder, "the refused 9000 did not move the sequence on");
            }
        }

        [Test]
        public void AReceiverWithRoomForFewerHandsCountsTheRest() {
            using (HandsReceiver receiver = StartHands(null, 2)) {
                Send(receiver.Port, VisionFixtures.Load("hands_four_gestures.json"));
                WaitFor(delegate () { return receiver.Accepted >= 1; }, "the datagram");
                HandsMessage message = new HandsMessage(2);
                Assert.IsTrue(receiver.TryGetNew(message));
                Assert.AreEqual(2, message.Count);
                Assert.AreEqual(4, message.DeclaredCount);
            }
        }

        [Test]
        public void ASecondReceiverOnTheSamePortFailsToStartInsteadOfSteals() {
            using (HandsReceiver first = StartHands(null, 4)) {
                HandsReceiver second = new HandsReceiver(first.Port, true, 0.5, null, null, 4);
                Assert.IsFalse(second.Start(), "the port is taken");
                Assert.IsFalse(second.IsRunning);
                second.Dispose();
                Send(first.Port, VisionFixtures.Load("hands_two.json"));
                WaitFor(delegate () { return first.Accepted >= 1; }, "the first receiver still works");
            }
        }

        [Test]
        public void StopAndRestartAndDisposeAreSafe() {
            HandsReceiver receiver = StartHands(null, 4);
            int firstPort = receiver.Port;
            Assert.IsTrue(receiver.IsRunning);
            Assert.IsTrue(receiver.Start(), "starting a running receiver is a no-op that reports success");
            receiver.Stop();
            Assert.IsFalse(receiver.IsRunning);
            receiver.Stop();
            Assert.IsTrue(receiver.Start(), "it can be started again");
            Assert.IsTrue(receiver.IsRunning);
            Send(receiver.Port, VisionFixtures.Load("hands_two.json"));
            WaitFor(delegate () { return receiver.Accepted >= 1; }, "a datagram after the restart");
            receiver.Dispose();
            receiver.Dispose();
            Assert.IsFalse(receiver.IsRunning);
            Assert.GreaterOrEqual(firstPort, 1);
        }

        [Test]
        public void ANoLongerListeningReceiverDoesNotBlockTheProcess() {
            HandsReceiver receiver = StartHands(null, 4);
            int port = receiver.Port;
            receiver.Dispose();
            Send(port, VisionFixtures.Load("hands_two.json"));
            using (HandsReceiver reborn = new HandsReceiver(port, true, 0.5, null, null, 4)) {
                Assert.IsTrue(reborn.Start(), "the port is free again once the first receiver is disposed");
            }
        }

        [Test]
        public void AConsumerNeverSeesAHalfWrittenMessageWhileDatagramsArriveFast() {
            using (FaceReceiver receiver = StartFace(null)) {
                string text = VisionFixtures.LoadText("face_no_landmarks.json");
                bool stop = false;
                Exception failure = null;
                Thread consumer = new Thread(delegate () {
                    FaceMessage message = new FaceMessage();
                    try {
                        while (!stop) {
                            if (receiver.TryGetNew(message)) {
                                if (message.Count != 1 || message.Face(0).Id != 1 || message.Face(0).Blendshape(ArkitBlendshapes.JawOpen) < 0.69f) {
                                    failure = new Exception("a torn message: count " + message.Count);
                                    return;
                                }
                            }
                        }
                    } catch (Exception exception) {
                        failure = exception;
                    }
                });
                consumer.Start();
                int sequence = 2000;
                while (sequence < 2600) {
                    Send(receiver.Port, VisionFixtures.Bytes(text.Replace("\"seq\":1042", "\"seq\":" + sequence)));
                    sequence = sequence + 1;
                }
                WaitFor(delegate () { return receiver.Accepted + receiver.OutOfOrder >= 300; }, "most of the burst");
                stop = true;
                consumer.Join(2000);
                Assert.IsNull(failure, failure == null ? string.Empty : failure.Message);
                Assert.AreEqual(0L, receiver.Rejected, "no datagram of the burst failed to parse");
            }
        }

        [Test]
        public void AnOversizeDatagramIsNotAnException() {
            using (HandsReceiver receiver = StartHands(null, 4)) {
                byte[] huge = new byte[60000];
                int i = 0;
                while (i < huge.Length) {
                    huge[i] = (byte)'a';
                    i = i + 1;
                }
                Send(receiver.Port, huge);
                WaitFor(delegate () { return receiver.Received >= 1; }, "the large datagram");
                Assert.AreEqual(1L, receiver.CountOf(WireParseResult.NotJson));
                Assert.IsTrue(receiver.IsRunning);
            }
        }
    }
}
