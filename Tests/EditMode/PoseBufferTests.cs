using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Tests {
    /// <summary>
    /// P1-3 unit tests for <see cref="PoseBuffer"/> — the timestamped pose buffer + interpolation that
    /// replaces latest-wins re-application. Covers the 12 cases in the P1-3 brief.
    /// </summary>
    public sealed class PoseBufferTests {
        private const int Wrist = (int)JointId.LeftWrist;

        private static PoseFrame Make(float x, double t, float conf = 0.9f, bool root = true) {
            PoseFrame f = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                f.SetLandmark(i, new PoseLandmark(new Vector3(x, 0f, 0f), conf));
            }
            f.SetMeta(t, true);
            if (root) {
                f.SetRootPosition(new Vector3(x * 2f, 0f, 0f));
            }
            return f;
        }

        // ---------------------------------------------------------------- 1
        [Test]
        public void TwoEquallySpacedPoses_InterpolateMidpoint() {
            PoseBuffer b = new PoseBuffer(8);
            Assert.IsTrue(b.Push(Make(0f, 100.0), 1, 100.0));
            Assert.IsTrue(b.Push(Make(1f, 100.1), 2, 100.1));
            PoseFrame o = new PoseFrame();
            Assert.IsTrue(b.Sample(100.05, o));
            Assert.AreEqual(0.5f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f,
                "midpoint of two equally spaced poses");
            Assert.AreEqual(0.5f, b.LastAlpha, 1e-4f);
            Assert.AreEqual(1, b.LastSeqA);
            Assert.AreEqual(2, b.LastSeqB);
        }

        // ---------------------------------------------------------------- 2
        [Test]
        public void IrregularlySpacedPoses_UseTimestampsNotFrameCount() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 1, 100.0);
            b.Push(Make(1f, 100.2), 2, 100.2);   // a 200 ms gap, not 33 ms
            PoseFrame o = new PoseFrame();
            b.Sample(100.15, o);                  // 75% of the way through
            Assert.AreEqual(0.75f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f,
                "alpha must come from timestamps, not an assumed 30 fps");
        }

        // ---------------------------------------------------------------- 3
        [Test]
        public void DuplicateSequence_IsIgnored() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 5, 100.0);
            Assert.IsFalse(b.Push(Make(9f, 100.1), 5, 100.1), "same seq must be rejected");
            Assert.AreEqual(1, b.Depth);
            Assert.AreEqual(1, b.RejectedDuplicate);
        }

        // ---------------------------------------------------------------- 4
        [Test]
        public void OutOfOrderSequence_IsRejected() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 10, 100.0);
            Assert.IsFalse(b.Push(Make(9f, 100.1), 4, 100.1), "older seq must be rejected");
            Assert.AreEqual(1, b.Depth);
            Assert.AreEqual(1, b.RejectedOutOfOrder);
        }

        // ---------------------------------------------------------------- 5
        [Test]
        public void DroppedPacket_StillInterpolatesAcrossTheGap() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 1, 100.0);
            // seq 2 never arrives
            b.Push(Make(2f, 100.2), 3, 100.2);
            PoseFrame o = new PoseFrame();
            Assert.IsTrue(b.Sample(100.1, o));
            Assert.AreEqual(1f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f,
                "a missing seq must not break interpolation between its neighbours");
            Assert.AreEqual(1, b.LastSeqA);
            Assert.AreEqual(3, b.LastSeqB);
        }

        // ---------------------------------------------------------------- 6
        [Test]
        public void EmptyBuffer_SampleReturnsFalse() {
            PoseBuffer b = new PoseBuffer(8);
            PoseFrame o = new PoseFrame();
            Assert.IsFalse(b.Sample(100.0, o));
        }

        // ---------------------------------------------------------------- 7
        [Test]
        public void SinglePoseBuffer_ReturnsThatPose() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(3f, 100.0), 1, 100.0);
            PoseFrame o = new PoseFrame();
            Assert.IsTrue(b.Sample(100.5, o));
            Assert.AreEqual(3f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f);
        }

        // ---------------------------------------------------------------- 8
        [Test]
        public void NormalInterpolation_QuarterAndThreeQuarter() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 1, 100.0);
            b.Push(Make(4f, 100.4), 2, 100.4);
            PoseFrame o = new PoseFrame();
            b.Sample(100.1, o);
            Assert.AreEqual(1f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f);
            b.Sample(100.3, o);
            Assert.AreEqual(3f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f);
        }

        // ---------------------------------------------------------------- 9
        [Test]
        public void RootPosition_IsInterpolated() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 1, 100.0);
            b.Push(Make(1f, 100.1), 2, 100.1);
            PoseFrame o = new PoseFrame();
            b.Sample(100.05, o);
            Assert.IsTrue(o.HasRootPosition);
            Assert.AreEqual(1f, o.RootPositionMetres.x, 1e-4f, "root lerps on the same timeline");
        }

        // ---------------------------------------------------------------- 10
        [Test]
        public void QuaternionSlerp_TakesShortestArc() {
            Quaternion a = Quaternion.Euler(0f, 0f, 0f);
            Quaternion c = Quaternion.Euler(0f, 90f, 0f);
            Quaternion mid = PoseBuffer.SlerpRotation(a, c, 0.5f);
            Assert.AreEqual(45f, Quaternion.Angle(a, mid), 0.5f, "half of a 90 deg arc");
        }

        // ---------------------------------------------------------------- 11
        [Test]
        public void BufferOverflow_IsBoundedAndKeepsNewest() {
            PoseBuffer b = new PoseBuffer(4);
            for (int i = 0; i < 20; i++) {
                b.Push(Make(i, 100.0 + i * 0.1), i + 1, 100.0 + i * 0.1);
            }
            Assert.AreEqual(4, b.Depth, "ring must stay bounded (no unbounded growth)");
            Assert.AreEqual(20, b.NewestSeq);
            Assert.IsTrue(b.DroppedOld > 0, "old poses were dropped");
            PoseFrame o = new PoseFrame();
            b.Sample(100.0 + 19 * 0.1, o);
            Assert.AreEqual(19f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-3f);
        }

        // ---------------------------------------------------------------- 12
        [Test]
        public void TimestampRegression_IsRejected() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 1, 100.0);
            Assert.IsFalse(b.Push(Make(1f, 99.0), 2, 99.0),
                "a backwards timestamp makes alpha meaningless and must be rejected");
            Assert.AreEqual(1, b.Depth);
        }

        // ------------------------------------------------- safety: P0-1 preservation
        [Test]
        public void InvalidEndpoint_IsNeverPositionInterpolated() {
            // The sidecar emits a dropped joint as [0,0,0,0]. Lerping valid -> zero would place the
            // joint half-way to the ORIGIN — the exact F-01 limb-collapse bug P0-1 exists to prevent.
            PoseBuffer b = new PoseBuffer(8);
            PoseFrame a = Make(0f, 100.0);      // endpoints must DIFFER or "interpolates" proves nothing
            PoseFrame c = Make(1f, 100.1);
            c.SetLandmark(Wrist, new PoseLandmark(Vector3.zero, 0f));   // wrist dropped by the sidecar
            b.Push(a, 1, 100.0);
            b.Push(c, 2, 100.1);
            PoseFrame o = new PoseFrame();
            b.Sample(100.05, o);
            PoseLandmark w = o.GetLandmark(JointId.LeftWrist);
            Assert.AreEqual(0f, w.Position.x, 1e-4f,
                "must carry the VALID endpoint's position (0), not lerp 0->origin");
            Assert.AreEqual(0f, w.Confidence, 1e-6f, "confidence min() = 0 so the LimbGate holds");
            Assert.AreEqual(0.5f, o.GetLandmark(JointId.LeftElbow).Position.x, 1e-4f,
                "other joints still interpolate normally (0 -> 1 at alpha 0.5)");
        }

        [Test]
        public void NoExtrapolation_BeyondNewest() {
            PoseBuffer b = new PoseBuffer(8);
            b.Push(Make(0f, 100.0), 1, 100.0);
            b.Push(Make(1f, 100.1), 2, 100.1);
            PoseFrame o = new PoseFrame();
            b.Sample(200.0, o);   // far past the newest sample
            Assert.AreEqual(1f, o.GetLandmark(JointId.LeftWrist).Position.x, 1e-4f,
                "must clamp to newest, never extrapolate (P1-3 scope)");
        }
    }
}
