using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    /// <summary>
    /// TORSO V5 trunk-gate acceptance tests (docs/UNITY_TORSO_V5_RELATIVE_YAW_2026-09-09.md §2/§4).
    ///
    /// The property under test is the one the V4 report proved was missing: a degenerate or
    /// discontinuous 4-point trunk measurement must NEVER reach the avatar as a confident yaw command.
    /// The specific failure is that Kalidokit's <c>CalcHipsAndSpine</c> reads landmarks 11/12/23/24
    /// with no validation, so an absent detection ([0,0,0]) makes <c>atan2(0,0)</c> resolve, through the
    /// <c>+0.5</c> jump-fix, to <b>exactly +90°</b> — measured live on an empty room.
    ///
    /// Thresholds under test are measured, not chosen: see <c>python-sidecar~/trunk_gate_thresholds.py</c>
    /// over 111,546 recorded frames (degenerate shoulder span p95 0.054 m vs real p01 0.156 m; real yaw
    /// rate p99.9 435 °/s).
    /// </summary>
    public sealed class TrunkGateTests {
        // A plausible standing trunk in landmark space (hip-relative metres), frontal.
        private static readonly Vector3 ShoulderL = new Vector3(0.18f, 0.50f, 0f);
        private static readonly Vector3 ShoulderR = new Vector3(-0.18f, 0.50f, 0f);
        private static readonly Vector3 HipL = new Vector3(0.10f, 0f, 0f);
        private static readonly Vector3 HipR = new Vector3(-0.10f, 0f, 0f);

        private const float Dt = 1f / 30f;

        /// <summary>The yaw the SOLVER would produce for an all-zero landmark pair — the exact bug.</summary>
        private const float SentinelYawRad = Mathf.PI * 0.5f;

        private static TrunkGateResult Step(ref TrunkGateState s, Vector3 shL, Vector3 shR,
                                            Vector3 hL, Vector3 hR, float hipYaw, float shYaw) {
            return TrunkGate.Evaluate(shL, shR, hL, hR, hipYaw, shYaw, Dt, ref s);
        }

        private static TrunkGateResult Valid(ref TrunkGateState s, float hipYawDeg, float shYawDeg) {
            return Step(ref s, ShoulderL, ShoulderR, HipL, HipR,
                        hipYawDeg * Mathf.Deg2Rad, shYawDeg * Mathf.Deg2Rad);
        }

        // ---------------------------------------------------------------- 1. zero landmarks

        [Test]
        public void ZeroLandmarks_AreRejected_AndNeverCommandNinetyDegrees() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();

            // Exactly what the sidecar emits with no detection, and exactly what the solver makes of it.
            TrunkGateResult r = Step(ref s, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                                     SentinelYawRad, SentinelYawRad);

            Assert.IsFalse(r.Fresh, "an all-zero trunk must never be accepted");
            Assert.AreEqual(TrunkRejectReason.ShoulderSpan, r.Reason);
            Assert.IsFalse(r.HasValue, "nothing has ever been valid, so there is nothing to hold");
            Assert.AreEqual(0f, r.ShoulderYaw, 1e-6f, "must fall back to FRONTAL, not to the +90 sentinel");
            Assert.AreEqual(0f, r.HipYaw, 1e-6f);
        }

        [Test]
        public void NoFalseNinetyDegreeCommand_EvenAfterAValidPose() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Valid(ref s, 10f, 12f);                       // establish a real torso

            // Subject disappears: landmarks collapse, solver says +90 deg on BOTH lines.
            for (int i = 0; i < 60; i++) {
                TrunkGateResult r = Step(ref s, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                                         SentinelYawRad, SentinelYawRad);
                Assert.IsFalse(r.Fresh);
                Assert.AreEqual(12f, r.ShoulderYaw * Mathf.Rad2Deg, 1e-3f,
                                "must HOLD the last valid yaw, never adopt the sentinel");
                Assert.AreEqual(10f, r.HipYaw * Mathf.Rad2Deg, 1e-3f);
                // The whole point, stated directly: the sentinel must be nowhere near the output.
                Assert.Greater(Mathf.Abs(r.ShoulderYaw * Mathf.Rad2Deg - 90f), 1f,
                               "output must not land on the +90 deg sentinel");
                Assert.Greater(Mathf.Abs(r.HipYaw * Mathf.Rad2Deg - 90f), 1f);
            }
        }

        // ---------------------------------------------------------------- 2/3. tiny spans

        [Test]
        public void TinyShoulderSpan_IsRejected() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            // 0.05 m apart — inside the degenerate population (p95 0.054 m), well below the 0.10 m floor.
            Vector3 a = new Vector3(0.025f, 0.5f, 0f);
            Vector3 b = new Vector3(-0.025f, 0.5f, 0f);
            TrunkGateResult r = Step(ref s, a, b, HipL, HipR, 0f, 0.6f);
            Assert.IsFalse(r.Fresh);
            Assert.AreEqual(TrunkRejectReason.ShoulderSpan, r.Reason);
        }

        [Test]
        public void TinyHipSpan_IsRejected() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Vector3 a = new Vector3(0.03f, 0f, 0f);
            Vector3 b = new Vector3(-0.03f, 0f, 0f);
            TrunkGateResult r = Step(ref s, ShoulderL, ShoulderR, a, b, 0.6f, 0f);
            Assert.IsFalse(r.Fresh);
            Assert.AreEqual(TrunkRejectReason.HipSpan, r.Reason);
        }

        [Test]
        public void ImplausiblyLargeSpan_IsRejected() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            // The corpus contains shoulder spans up to 3.2 m — gross breakage, not a human.
            Vector3 a = new Vector3(1.7f, 0.5f, 0f);
            Vector3 b = new Vector3(-1.7f, 0.5f, 0f);
            TrunkGateResult r = Step(ref s, a, b, HipL, HipR, 0f, 0.3f);
            Assert.IsFalse(r.Fresh);
            Assert.AreEqual(TrunkRejectReason.SpanImplausible, r.Reason);
        }

        // ---------------------------------------------------------------- 4. NaN / Inf

        [Test]
        public void NaNAndInf_AreRejected_OnEveryInput() {
            Vector3[] poison = {
                new Vector3(float.NaN, 0.5f, 0f),
                new Vector3(float.PositiveInfinity, 0.5f, 0f),
                new Vector3(0.18f, float.NegativeInfinity, 0f),
            };
            foreach (Vector3 p in poison) {
                TrunkGateState s = default(TrunkGateState);
                s.Reset();
                TrunkGateResult r = Step(ref s, p, ShoulderR, HipL, HipR, 0f, 0f);
                Assert.IsFalse(r.Fresh, "poisoned shoulder must be rejected: " + p);
                Assert.AreEqual(TrunkRejectReason.NotFinite, r.Reason);
            }
            // ...and a poisoned SOLVED yaw, which the landmarks alone would not reveal.
            TrunkGateState s2 = default(TrunkGateState);
            s2.Reset();
            TrunkGateResult r2 = Step(ref s2, ShoulderL, ShoulderR, HipL, HipR, float.NaN, 0f);
            Assert.IsFalse(r2.Fresh);
            Assert.AreEqual(TrunkRejectReason.NotFinite, r2.Reason);
        }

        // ---------------------------------------------------------------- 5. impossible jump

        [Test]
        public void ImpossibleYawJump_IsRejected_AndHolds() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Valid(ref s, 0f, 5f);

            // 170 deg in one 1/30 s frame = 5100 deg/s. Real motion reaches 435 deg/s at p99.9.
            TrunkGateResult r = Valid(ref s, 0f, 175f);
            Assert.IsFalse(r.Fresh, "a 5100 deg/s step is not a human torso");
            Assert.AreEqual(TrunkRejectReason.YawJump, r.Reason);
            Assert.AreEqual(5f, r.ShoulderYaw * Mathf.Rad2Deg, 1e-3f, "holds the last valid yaw");
        }

        [Test]
        public void PlausibleFastTurn_IsAccepted() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Valid(ref s, 0f, 0f);
            // 10 deg in 1/30 s = 300 deg/s: fast, but inside the measured human envelope (p99.9 435).
            TrunkGateResult r = Valid(ref s, 0f, 10f);
            Assert.IsTrue(r.Fresh, "a 300 deg/s turn is real motion and must pass");
            Assert.AreEqual(10f, r.ShoulderYaw * Mathf.Rad2Deg, 1e-3f);
        }

        [Test]
        public void SustainedDisagreement_ReacquiresRatherThanLockingOutForever() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Valid(ref s, 0f, 5f);

            // A genuine teleport (a different person steps in) must not freeze the torso permanently.
            bool reacquired = false;
            for (int i = 0; i < TrunkGate.ReacquireFrames + 2; i++) {
                TrunkGateResult r = Valid(ref s, 0f, 175f);
                if (r.Fresh) {
                    reacquired = true;
                    Assert.AreEqual(175f, r.ShoulderYaw * Mathf.Rad2Deg, 1e-3f);
                    Assert.LessOrEqual(i + 1, TrunkGate.ReacquireFrames,
                                       "must re-acquire within the documented window");
                    break;
                }
            }
            Assert.IsTrue(reacquired, "the gate must re-seed after sustained disagreement");
        }

        [Test]
        public void DegenerateFrames_DoNotDriveReacquisition() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Valid(ref s, 0f, 5f);

            // A degenerate frame carries no candidate, so no number of them may ever cause the gate to
            // adopt the +90 sentinel. This is the regression guard on the re-acquisition path itself.
            for (int i = 0; i < TrunkGate.ReacquireFrames * 5; i++) {
                TrunkGateResult r = Step(ref s, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                                         SentinelYawRad, SentinelYawRad);
                Assert.IsFalse(r.Fresh);
                Assert.AreEqual(5f, r.ShoulderYaw * Mathf.Rad2Deg, 1e-3f);
            }
        }

        // ---------------------------------------------------------------- 6. valid -> invalid -> valid

        [Test]
        public void ValidThenInvalidThenValid_HoldsThroughTheGapAndResumes() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();

            TrunkGateResult a = Valid(ref s, 8f, 14f);
            Assert.IsTrue(a.Fresh);
            Assert.AreEqual(14f, a.ShoulderYaw * Mathf.Rad2Deg, 1e-3f);

            for (int i = 0; i < 10; i++) {
                TrunkGateResult mid = Step(ref s, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                                           SentinelYawRad, SentinelYawRad);
                Assert.IsFalse(mid.Fresh);
                Assert.IsTrue(mid.HasValue);
                Assert.AreEqual(14f, mid.ShoulderYaw * Mathf.Rad2Deg, 1e-3f, "held across the gap");
                Assert.AreEqual(8f, mid.HipYaw * Mathf.Rad2Deg, 1e-3f);
            }

            TrunkGateResult back = Valid(ref s, 9f, 15f);
            Assert.IsTrue(back.Fresh, "a real trunk must be accepted again immediately");
            Assert.AreEqual(15f, back.ShoulderYaw * Mathf.Rad2Deg, 1e-3f);
            Assert.AreEqual(9f, back.HipYaw * Mathf.Rad2Deg, 1e-3f);
        }

        // ---------------------------------------------------------------- 7. invalid on first frame

        [Test]
        public void InvalidOnFirstFrame_DrivesFrontal_NotTheSentinel() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            for (int i = 0; i < 20; i++) {
                TrunkGateResult r = Step(ref s, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                                         SentinelYawRad, SentinelYawRad);
                Assert.IsFalse(r.HasValue);
                Assert.AreEqual(0f, r.ShoulderYaw, 1e-6f);
                Assert.AreEqual(0f, r.HipYaw, 1e-6f);
            }
            // and the first real trunk after that cold start is taken without a slew.
            TrunkGateResult first = Valid(ref s, -6f, -11f);
            Assert.IsTrue(first.Fresh);
            Assert.AreEqual(-11f, first.ShoulderYaw * Mathf.Rad2Deg, 1e-3f);
        }

        // ---------------------------------------------------------------- 8. valid behaviour preserved

        [Test]
        public void ValidTorso_PassesThroughUnchanged() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            // Steps must be physically reachable at this dt: the 500 deg/s ceiling allows 16.67 deg per
            // 1/30 s frame. (An earlier version of this fixture stepped 32 deg in one frame = 960 deg/s
            // and was correctly REJECTED — the fixture was wrong, not the gate.)
            float[] hip = { 0f, 3f, 6f, 2f, -2f, -5f, -1f };
            float[] sh = { 0f, 6f, 11f, 5f, -3f, -9f, -2f };
            for (int i = 0; i < hip.Length; i++) {
                TrunkGateResult r = Valid(ref s, hip[i], sh[i]);
                Assert.IsTrue(r.Fresh, "frame " + i + " is a plausible torso and must pass");
                Assert.AreEqual(hip[i], r.HipYaw * Mathf.Rad2Deg, 1e-3f, "hip yaw must not be altered");
                Assert.AreEqual(sh[i], r.ShoulderYaw * Mathf.Rad2Deg, 1e-3f, "shoulder yaw must not be altered");
            }
        }

        [Test]
        public void EdgeOnTorso_IsNotMistakenForDegenerate() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            // A subject turned ~90 deg has a tiny |dx| but a full-width line in DEPTH. Gating on |dx|
            // alone would reject exactly the turned poses this work exists to support, so the gate
            // measures the full 3-D separation.
            Vector3 a = new Vector3(0.01f, 0.5f, 0.18f);
            Vector3 b = new Vector3(-0.01f, 0.5f, -0.18f);
            TrunkGateResult r = Step(ref s, a, b, HipL, HipR, 0f, 1.5f);
            Assert.IsTrue(r.Fresh, "an edge-on shoulder line is a real measurement, not a degenerate one");
        }

        [Test]
        public void Reset_ClearsHeldState() {
            TrunkGateState s = default(TrunkGateState);
            s.Reset();
            Valid(ref s, 7f, 13f);
            s.Reset();
            TrunkGateResult r = Step(ref s, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                                     SentinelYawRad, SentinelYawRad);
            Assert.IsFalse(r.HasValue, "Reset must drop the held pose so it cannot leak across avatars");
            Assert.AreEqual(0f, r.ShoulderYaw, 1e-6f);
        }
    }
}
