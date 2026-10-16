using NUnit.Framework;

using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-20A §19 — deterministic tests for producer-session identification, the stale watchdog and the
    /// pose buffer's behaviour across a session boundary.
    ///
    /// These reproduce the F-19 production failure as a test: a sidecar restart makes <c>seq</c> go
    /// backwards, after which P1-3 rejected every packet forever while the transport reported healthy.
    /// <see cref="SidecarRestart_NewSession_IsAcceptedNotRejectedForever"/> is that case.
    ///
    /// Everything here takes its clock as a parameter, so no test sleeps and none is timing-flaky.
    /// </summary>
    public sealed class TrackingStreamHealthTests {
        private const double Warn = 0.15;
        private const double Hold = 0.5;
        private const double Failsafe = 2.0;

        private static TrackingStreamHealth NewHealth() {
            return new TrackingStreamHealth(Warn, Hold, Failsafe);
        }

        // ------------------------------------------------------------------ session identification

        [Test]
        public void FirstDatagram_IsFirstSession() {
            TrackingStreamHealth h = NewHealth();
            Assert.AreEqual(SessionVerdict.FirstSession, h.ClassifyDatagram("aaa", 1, 100.0));
            Assert.AreEqual("aaa", h.SessionId);
        }

        [Test]
        public void SameSessionMonotonic_StaysSameSession() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            for (int i = 2; i <= 50; i++) {
                Assert.AreEqual(SessionVerdict.SameSession, h.ClassifyDatagram("aaa", i, 100.0 + i * 0.033));
                h.OnPoseAccepted(i, 100.0 + i * 0.033);
            }
            Assert.AreEqual(0, h.SessionTransitions);
        }

        [Test]
        public void DifferentSessionId_IsNewSession_Immediately() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1000, 100.0);
            h.OnPoseAccepted(1000, 100.0);
            // A restarted sidecar: brand new id, seq back to 1.
            Assert.AreEqual(SessionVerdict.NewSession, h.ClassifyDatagram("bbb", 1, 101.0));
            Assert.AreEqual("bbb", h.SessionId);
            Assert.AreEqual(1, h.SessionTransitions);
            Assert.AreEqual(1, h.AcceptedNewSession);
        }

        [Test]
        public void SeqResetWithinSameSessionId_IsNotASessionChange() {
            // Ordering protection must NOT be weakened: inside one session a backwards seq is still
            // just an out-of-order packet, and the buffer must go on rejecting it.
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1000, 100.0);
            h.OnPoseAccepted(1000, 100.0);
            for (int i = 0; i < 40; i++) {
                Assert.AreEqual(SessionVerdict.SameSession, h.ClassifyDatagram("aaa", 1, 101.0 + i));
            }
            Assert.AreEqual(0, h.SessionTransitions);
        }

        [Test]
        public void LegacySender_SingleStrayBackwardsPacket_IsNotASessionChange() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram(null, 500, 100.0);
            h.OnPoseAccepted(500, 100.0);
            Assert.AreEqual(SessionVerdict.SameSession, h.ClassifyDatagram(null, 3, 100.01));
            Assert.AreEqual(0, h.SessionTransitions);
        }

        [Test]
        public void LegacySender_SustainedRegressionAfterQuiet_RecoversAsNewSession() {
            // The bounded last-resort path for senders with no sid. Requires BOTH a run of regressions
            // AND a quiet period, so neither alone can trip it.
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram(null, 500, 100.0);
            h.OnPoseAccepted(500, 100.0);
            SessionVerdict last = SessionVerdict.SameSession;
            for (int i = 1; i <= 8; i++) {
                last = h.ClassifyDatagram(null, i, 101.0 + i * 0.033);   // >0.25 s quiet since accept
            }
            Assert.AreEqual(SessionVerdict.NewSession, last);
            Assert.AreEqual(1, h.SessionTransitions);
        }

        [Test]
        public void LegacySender_RegressionWithoutQuietPeriod_DoesNotTrip() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram(null, 500, 100.0);
            h.OnPoseAccepted(500, 100.0);
            for (int i = 1; i <= 20; i++) {
                // no quiet period: still accepting at the same instant
                Assert.AreEqual(SessionVerdict.SameSession, h.ClassifyDatagram(null, i, 100.0));
            }
            Assert.AreEqual(0, h.SessionTransitions);
        }

        [Test]
        public void SidCapableProducerReplacedByLegacyOne_IsNewSession() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 10, 100.0);
            h.OnPoseAccepted(10, 100.0);
            Assert.AreEqual(SessionVerdict.NewSession, h.ClassifyDatagram(null, 1, 101.0));
        }

        [Test]
        public void PacketFromAnAlreadyLeftSession_IsRejected_NotReadopted() {
            // Found by the §14 live injection test: ten packets from the previous session arrived
            // after a new one had taken over and were ADOPTED, flushing a healthy buffer. A datagram
            // in flight when its producer died must be dropped, not followed.
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("A", 100, 100.0);
            h.OnPoseAccepted(100, 100.0);
            Assert.AreEqual(SessionVerdict.NewSession, h.ClassifyDatagram("B", 1, 101.0));
            h.OnPoseAccepted(1, 101.0);
            for (int i = 0; i < 10; i++) {
                Assert.AreEqual(SessionVerdict.OldSession, h.ClassifyDatagram("A", 500 + i, 101.1));
            }
            Assert.AreEqual(10, h.RejectedOldSession);
            Assert.AreEqual("B", h.SessionId, "the current session must not flap back");
            Assert.AreEqual(1, h.SessionTransitions, "a late old-session packet is not a transition");
        }

        [Test]
        public void ThreeSessionsInSequence_EachNewOneAdopted_OldOnesRejected() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("A", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            Assert.AreEqual(SessionVerdict.NewSession, h.ClassifyDatagram("B", 1, 101.0));
            Assert.AreEqual(SessionVerdict.NewSession, h.ClassifyDatagram("C", 1, 102.0));
            Assert.AreEqual(SessionVerdict.OldSession, h.ClassifyDatagram("A", 9, 102.1));
            Assert.AreEqual(SessionVerdict.OldSession, h.ClassifyDatagram("B", 9, 102.1));
            Assert.AreEqual(SessionVerdict.SameSession, h.ClassifyDatagram("C", 2, 102.2));
            Assert.AreEqual(2, h.SessionTransitions);
        }

        // ------------------------------------------------------------------------- stale watchdog

        [Test]
        public void NoDatagrams_IsNoStream() {
            Assert.AreEqual(TrackingState.NoStream, NewHealth().Evaluate(100.0));
        }

        [Test]
        public void DatagramsButNoAcceptedPose_IsConnecting() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            Assert.AreEqual(TrackingState.Connecting, h.Evaluate(100.0));
        }

        [Test]
        public void FreshStream_IsLive() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            Assert.AreEqual(TrackingState.Live, h.Evaluate(100.05));
            Assert.IsTrue(h.IsFresh(100.05));
        }

        [Test]
        public void JustPastWarn_IsStillLive_ButNotFresh() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            Assert.AreEqual(TrackingState.Live, h.Evaluate(100.0 + Warn + 0.01));
            Assert.IsFalse(h.IsFresh(100.0 + Warn + 0.01));
        }

        [Test]
        public void ProducerGone_PastHold_IsStaleHold() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            // nothing arriving at all: the producer is gone
            Assert.AreEqual(TrackingState.StaleHold, h.Evaluate(100.0 + Hold + 0.6));
        }

        [Test]
        public void PacketsArrivingButNoneAccepted_PastHold_IsReconnecting() {
            // This is the exact F-19 shape: datagrams flowing, nothing accepted.
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            h.ClassifyDatagram("aaa", 1, 100.7);           // arriving, rejected by the buffer
            Assert.AreEqual(TrackingState.Reconnecting, h.Evaluate(100.7));
        }

        [Test]
        public void PastFailsafe_IsStaleFailsafe() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            Assert.AreEqual(TrackingState.StaleFailsafe, h.Evaluate(100.0 + Failsafe + 0.01));
        }

        [Test]
        public void F19Scenario_MinutesOfPacketsNeverAccepted_ReachesFailsafe() {
            // 208 seconds was the measured case. It must not report anything but failsafe.
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 22678, 100.0);
            h.OnPoseAccepted(22678, 100.0);
            double t = 100.0;
            for (int i = 1; i <= 5000; i++) {
                t = 100.0 + i * 0.0333;
                h.ClassifyDatagram("aaa", i, t);           // same sid, seq regressed -> never accepted
            }
            Assert.Greater(t - 100.0, 150.0);
            Assert.AreEqual(TrackingState.StaleFailsafe, h.Evaluate(t));
        }

        [Test]
        public void RecoveryAfterStale_GoesThroughRecovering_ThenLive() {
            TrackingStreamHealth h = NewHealth();
            h.ClassifyDatagram("aaa", 1, 100.0);
            h.OnPoseAccepted(1, 100.0);
            Assert.AreEqual(TrackingState.StaleFailsafe, h.Evaluate(103.0));   // latches wasStale
            h.ClassifyDatagram("bbb", 1, 103.1);
            h.OnSessionReset(103.1);
            h.OnPoseAccepted(1, 103.1);
            Assert.AreEqual(TrackingState.Recovering, h.Evaluate(103.12));
            h.OnPoseAccepted(2, 103.4);
            Assert.AreEqual(TrackingState.Live, h.Evaluate(103.45));
        }

        // ----------------------------------------------------------- pose buffer session behaviour

        private static PoseFrame Frame(float x) {
            PoseFrame f = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                f.SetLandmark(i, new PoseLandmark(new UnityEngine.Vector3(x, 0f, 0f), 1f));
            }
            f.SetMeta(x, true);
            return f;
        }

        [Test]
        public void SidecarRestart_NewSession_IsAcceptedNotRejectedForever() {
            // THE F-19 REGRESSION TEST. Session A runs to a high seq; session B restarts at 1. Without
            // the session flush every B packet is rejected out-of-order and the avatar freezes.
            PoseBuffer buffer = new PoseBuffer(16);
            TrackingStreamHealth h = NewHealth();
            double t = 100.0;
            for (int seq = 1; seq <= 1000; seq++) {
                t = 100.0 + seq * 0.033;
                h.ClassifyDatagram("A", seq, t);
                Assert.IsTrue(buffer.Push(Frame(seq), seq, t));
                h.OnPoseAccepted(seq, t);
            }
            long rejectedBefore = buffer.RejectedOutOfOrder;

            for (int seq = 1; seq <= 1000; seq++) {
                t += 0.033;
                SessionVerdict v = h.ClassifyDatagram("B", seq, t);
                if (v == SessionVerdict.NewSession) {
                    buffer.Clear();
                    h.OnSessionReset(t);
                }
                bool pushed = buffer.Push(Frame(10000 + seq), seq, t);
                Assert.IsTrue(pushed, "session B seq " + seq + " must be accepted");
                h.OnPoseAccepted(seq, t);
            }
            Assert.AreEqual(rejectedBefore, buffer.RejectedOutOfOrder, "no new out-of-order rejections");
            Assert.AreEqual(TrackingState.Live, h.Evaluate(t + 0.01));
        }

        [Test]
        public void SessionChange_FlushesBuffer_SoNoInterpolationAcrossTheBoundary() {
            PoseBuffer buffer = new PoseBuffer(16);
            buffer.Push(Frame(1f), 1, 100.0);
            buffer.Push(Frame(2f), 2, 100.033);
            buffer.Push(Frame(3f), 3, 100.066);
            Assert.AreEqual(3, buffer.Depth);

            buffer.Clear();                       // what the provider does on NewSession
            buffer.Push(Frame(50f), 1, 100.10);   // session B's first pose
            Assert.AreEqual(1, buffer.Depth, "buffer must hold ONLY the new session's pose");

            PoseFrame output = new PoseFrame();
            Assert.IsTrue(buffer.Sample(100.10, output));
            // A single-entry buffer cannot interpolate, so the sample is session B's pose exactly —
            // never a blend of 3 and 50.
            Assert.AreEqual(50f, output.GetLandmark(JointId.Nose).Position.x, 1e-4f);
        }

        [Test]
        public void WithoutSessionFlush_TheOldFailureReproduces() {
            // Guards the guard: if the flush is ever removed, this test shows what happens.
            PoseBuffer buffer = new PoseBuffer(16);
            for (int seq = 1; seq <= 100; seq++) {
                buffer.Push(Frame(seq), seq, 100.0 + seq * 0.033);
            }
            double t = 110.0;
            int accepted = 0;
            for (int seq = 1; seq <= 100; seq++) {
                t += 0.033;
                if (buffer.Push(Frame(seq), seq, t)) {
                    accepted++;
                }
            }
            Assert.AreEqual(0, accepted, "this is the F-19 failure: every restarted packet rejected");
            // 99 land below newestSeq (out-of-order) and exactly one — the packet whose seq equals the
            // old newestSeq — is rejected as a DUPLICATE instead. Asserting the split rather than a
            // loose bound keeps the test honest about which rule fired.
            Assert.AreEqual(99, buffer.RejectedOutOfOrder);
            Assert.AreEqual(1, buffer.RejectedDuplicate);
            Assert.AreEqual(100, buffer.RejectedOutOfOrder + buffer.RejectedDuplicate);
        }

        [Test]
        public void LargeSequenceGap_SameSession_IsAccepted() {
            PoseBuffer buffer = new PoseBuffer(16);
            Assert.IsTrue(buffer.Push(Frame(1f), 10, 100.0));
            Assert.IsTrue(buffer.Push(Frame(2f), 9000, 100.033), "a burst loss must not wedge the buffer");
        }

        [Test]
        public void BackwardTimestamp_IsRejected() {
            PoseBuffer buffer = new PoseBuffer(16);
            Assert.IsTrue(buffer.Push(Frame(1f), 1, 100.0));
            Assert.IsFalse(buffer.Push(Frame(2f), 2, 99.0), "a clock regression must not be accepted");
        }

        [Test]
        public void DuplicatePacket_IsRejected() {
            PoseBuffer buffer = new PoseBuffer(16);
            Assert.IsTrue(buffer.Push(Frame(1f), 5, 100.0));
            Assert.IsFalse(buffer.Push(Frame(1f), 5, 100.033));
            Assert.AreEqual(1, buffer.RejectedDuplicate);
        }
    }
}
