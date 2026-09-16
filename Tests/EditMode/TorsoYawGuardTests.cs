using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    /// <summary>
    /// TORSO V6 wrap-guard + operating-envelope tests
    /// (docs/V6_TORSO_YAW_IMPLEMENTATION_VALIDATION_2026-09-10.md, ADR-045).
    ///
    /// Two properties are under test, and they are separate:
    ///
    /// <para><b>1. The wrap must never reach the avatar.</b> The torso yaw is <c>atan2(Δx, Δz)</c>
    /// over the shoulder line; as the subject turns edge-on <c>Δx → 0</c> and the angle flips through
    /// ±180°. F-10 measured 78.49 % sign accuracy, rising to 89.52 % once that wrap is excluded. The
    /// guard must hold the last valid yaw rather than follow the flip — in particular a +45° subject
    /// must never render as −135°.</para>
    ///
    /// <para><b>2. Beyond ±60° the magnitude was never validated.</b> F-15 held ±30/±45/±60 for the
    /// first time and measured the shoulder line at 63.8 px front-on, 17.3 px at 60° and 7–8 px at
    /// ±90°; block-level, |heading| ≤ 60° scored 6 correct / 0 wrong / 0 no-signal while
    /// |heading| = 90° scored 0 correct / 1 wrong / 1 no-signal. So the envelope is clamped, not
    /// believed — and clamping is continuous, so crossing the boundary produces no snap.</para>
    ///
    /// Thresholds are inherited, not invented: 150° is F-10's own wrap guard (scored in F-14 and
    /// replicated in F-15) and 60° is the F-15 measured envelope.
    /// </summary>
    public sealed class TorsoYawGuardTests {
        private const float Tol = 1e-4f;

        private static float Rad(float deg) {
            return Mathf.Deg2Rad * deg;
        }

        private static float Deg(float rad) {
            return Mathf.Rad2Deg * rad;
        }

        /// <summary>Feed one whole-body yaw (hips and shoulders rigid, i.e. zero twist).</summary>
        private static TorsoYawGuardResult Step(ref TorsoYawGuardState s, float deg) {
            return TorsoYawGuard.Evaluate(Rad(deg), Rad(deg), true, ref s);
        }

        // ---------------------------------------------------------------- basic validity ----------

        [TestCase(0f)]
        [TestCase(30f)]
        [TestCase(-30f)]
        [TestCase(45f)]
        [TestCase(-45f)]
        [TestCase(60f)]
        [TestCase(-60f)]
        public void InsideEnvelope_PassesThroughUnchanged(float deg) {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            TorsoYawGuardResult r = Step(ref s, deg);
            Assert.IsTrue(r.HasValue);
            Assert.AreEqual(TorsoYawState.Valid, r.State, "inside ±60 must be Valid");
            Assert.AreEqual(deg, Deg(r.ShoulderYaw), 0.01f, "the measurement must not be altered");
            Assert.AreEqual(deg, Deg(r.HipYaw), 0.01f);
            Assert.AreEqual(0, r.HoldFrames);
        }

        // ---------------------------------------------------------------- boundary ----------------

        [TestCase(59f, 59f, TorsoYawState.Valid)]
        [TestCase(60f, 60f, TorsoYawState.Valid)]
        [TestCase(61f, 60f, TorsoYawState.OutOfRange)]
        [TestCase(75f, 60f, TorsoYawState.OutOfRange)]
        [TestCase(90f, 60f, TorsoYawState.OutOfRange)]
        [TestCase(-59f, -59f, TorsoYawState.Valid)]
        [TestCase(-60f, -60f, TorsoYawState.Valid)]
        [TestCase(-61f, -60f, TorsoYawState.OutOfRange)]
        [TestCase(-75f, -60f, TorsoYawState.OutOfRange)]
        [TestCase(-90f, -60f, TorsoYawState.OutOfRange)]
        public void Boundary_ClampsAndReportsState(float inDeg, float outDeg, TorsoYawState want) {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            TorsoYawGuardResult r = Step(ref s, inDeg);
            Assert.AreEqual(want, r.State);
            Assert.AreEqual(outDeg, Deg(r.ShoulderYaw), 0.01f);
        }

        [Test]
        public void Boundary_IsContinuous_NoSnapAcrossSixtyDegrees() {
            // The clamp must not introduce a step. Sweeping through the boundary, the largest
            // frame-to-frame change must stay at the sweep's own step size.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            float prev = 0f;
            float maxStep = 0f;
            for (float d = 50f; d <= 70f; d += 0.5f) {
                float got = Deg(Step(ref s, d).ShoulderYaw);
                if (d > 50f) {
                    maxStep = Mathf.Max(maxStep, Mathf.Abs(got - prev));
                }
                prev = got;
            }
            Assert.LessOrEqual(maxStep, 0.51f, "clamping must be continuous — no snap at ±60");
        }

        // ---------------------------------------------------------------- wrap --------------------

        [TestCase(149f, TorsoYawState.OutOfRange)]   // below the guard: clamped, still followed
        [TestCase(150f, TorsoYawState.OutOfRange)]   // exactly at the guard is NOT wrapped (> is strict)
        [TestCase(151f, TorsoYawState.WrapGuarded)]
        [TestCase(179f, TorsoYawState.WrapGuarded)]
        [TestCase(-149f, TorsoYawState.OutOfRange)]
        [TestCase(-150f, TorsoYawState.OutOfRange)]
        [TestCase(-151f, TorsoYawState.WrapGuarded)]
        [TestCase(-179f, TorsoYawState.WrapGuarded)]
        public void WrapThreshold_FiresAboveOneFiftyOnly(float deg, TorsoYawState want) {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);                       // establish a valid state to hold
            TorsoYawGuardResult r = Step(ref s, deg);
            Assert.AreEqual(want, r.State);
        }

        [Test]
        public void Wrap_HoldsLastValid_DoesNotFollowTheFlip() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            TorsoYawGuardResult r = Step(ref s, -170f);   // the ±180 flip of a +45 subject
            Assert.AreEqual(TorsoYawState.WrapGuarded, r.State);
            Assert.AreEqual(45f, Deg(r.ShoulderYaw), 0.01f, "must hold +45, not follow to -170");
            Assert.Greater(Deg(r.ShoulderYaw), 0f, "the SIGN must survive the wrap");
        }

        [Test]
        public void Wrap_IsNeverAcceptedIntoTheHeldState() {
            // The property that matters most: a wrapped frame must not become the value the avatar
            // later reacquires to.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            for (int i = 0; i < 20; i++) {
                Step(ref s, -175f);
            }
            Assert.AreEqual(45f, Deg(s.HeldShoulderYaw), 0.01f,
                            "20 wrapped frames must not have displaced the held +45");
        }

        // ---------------------------------------------------------------- safety ------------------

        [Test]
        public void NoInvalidMeasurementEverRendersAsOneEighty() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 30f);
            float[] hostile = { 180f, -180f, 179.9f, -179.9f, 165f, -165f, 151f, -151f };
            for (int i = 0; i < hostile.Length; i++) {
                TorsoYawGuardResult r = Step(ref s, hostile[i]);
                Assert.LessOrEqual(Mathf.Abs(Deg(r.ShoulderYaw)), TorsoYawGuard.ValidatedRangeDeg + 0.01f,
                                   "output must stay inside the envelope for input " + hostile[i]);
            }
        }

        [Test]
        public void NonFiniteInput_HoldsRatherThanPropagates() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            TorsoYawGuardResult r = TorsoYawGuard.Evaluate(float.NaN, float.NaN, true, ref s);
            Assert.AreEqual(TorsoYawState.WrapGuarded, r.State);
            Assert.AreEqual(45f, Deg(r.ShoulderYaw), 0.01f);
            r = TorsoYawGuard.Evaluate(float.PositiveInfinity, 0f, true, ref s);
            Assert.AreEqual(45f, Deg(r.ShoulderYaw), 0.01f);
        }

        [Test]
        public void FirstFrameInvalid_DrivesFrontal_NotNinety() {
            // The V4 failure mode: an absent detection resolving to exactly +90°. With nothing held,
            // the guard must report NoValue and emit 0, never a confident angle.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            TorsoYawGuardResult r = Step(ref s, 175f);
            Assert.IsFalse(r.HasValue);
            Assert.AreEqual(TorsoYawState.NoValue, r.State);
            Assert.AreEqual(0f, Deg(r.ShoulderYaw), 0.01f);
            Assert.Greater(Mathf.Abs(Deg(r.ShoulderYaw) - 90f), 1f, "must not be the +90° sentinel");
        }

        [Test]
        public void TrunkGateRejection_HoldsWithoutMarkingWrap() {
            // A stale-but-well-formed frame (TrunkGate said not fresh) is held, but it is NOT a wrap:
            // the states must stay distinguishable for diagnostics.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            TorsoYawGuardResult r = TorsoYawGuard.Evaluate(Rad(45f), Rad(45f), false, ref s);
            Assert.AreEqual(TorsoYawState.Valid, r.State);
            Assert.AreEqual(1, r.HoldFrames);
            Assert.AreEqual(45f, Deg(r.ShoulderYaw), 0.01f);
        }

        // ---------------------------------------------------------------- recovery ----------------

        [Test]
        public void Recovery_SameSign_ReturnsImmediatelyAndExactly() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            Step(ref s, 175f);
            TorsoYawGuardResult r = Step(ref s, 45f);
            Assert.AreEqual(TorsoYawState.Valid, r.State);
            Assert.AreEqual(45f, Deg(r.ShoulderYaw), 0.01f);
            Assert.AreEqual(0, r.HoldFrames, "the hold streak must clear on reacquisition");
        }

        [Test]
        public void Recovery_OppositeSign_IsFollowed_NotSuppressed() {
            // The guard must not become a sign latch: a genuine turn to the other side, arriving as a
            // well-formed in-range measurement, has to be followed.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            Step(ref s, 175f);
            TorsoYawGuardResult r = Step(ref s, -45f);
            Assert.AreEqual(TorsoYawState.Valid, r.State);
            Assert.AreEqual(-45f, Deg(r.ShoulderYaw), 0.01f);
        }

        [Test]
        public void Recovery_AfterLongInvalidRun_ReacquiresExactly() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, -50f);
            for (int i = 0; i < 200; i++) {
                Step(ref s, 178f);
            }
            Assert.AreEqual(200, s.LongestHold);
            TorsoYawGuardResult r = Step(ref s, -50f);
            Assert.AreEqual(TorsoYawState.Valid, r.State);
            Assert.AreEqual(-50f, Deg(r.ShoulderYaw), 0.01f);
            Assert.AreEqual(0, r.HoldFrames);
        }

        [Test]
        public void RepeatedInvalidFrames_HoldOneValueAndCountIt() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 33f);
            for (int i = 1; i <= 10; i++) {
                TorsoYawGuardResult r = Step(ref s, 170f);
                Assert.AreEqual(33f, Deg(r.ShoulderYaw), 0.01f, "held value must not drift");
                Assert.AreEqual(i, r.HoldFrames);
            }
            Assert.AreEqual(10, s.LongestHold);
            Assert.AreEqual(1, s.HoldRuns);
        }

        [Test]
        public void OutOfRangeIsNotAHold_ItIsAFreshClampedMeasurement() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 20f);
            TorsoYawGuardResult r = Step(ref s, 80f);
            Assert.AreEqual(TorsoYawState.OutOfRange, r.State);
            Assert.AreEqual(0, r.HoldFrames, "clamping is not holding");
            Assert.AreEqual(80f, Deg(s.HeldShoulderYaw), 0.01f,
                            "the held state keeps the UNCLAMPED value so returning is instant");
        }

        // ---------------------------------------------------------------- transitions (§5) --------

        [TestCase(45f)]
        [TestCase(-45f)]
        [TestCase(60f)]
        [TestCase(-60f)]
        public void Transition_ThroughCentreAndBack_KeepsSign(float deg) {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Assert.AreEqual(deg, Deg(Step(ref s, deg).ShoulderYaw), 0.01f);
            Assert.AreEqual(0f, Deg(Step(ref s, 0f).ShoulderYaw), 0.01f);
            TorsoYawGuardResult r = Step(ref s, deg);
            Assert.AreEqual(deg, Deg(r.ShoulderYaw), 0.01f);
            Assert.AreEqual(Mathf.Sign(deg), Mathf.Sign(Deg(r.ShoulderYaw)));
        }

        [TestCase(60f)]
        [TestCase(-60f)]
        public void Transition_ThroughEdgeOnAndBack_NeverInvertsSign(float deg) {
            // Edge-on is where F-15 measured the collapse. Passing through it must not leave the
            // avatar facing the wrong way when the subject returns.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, deg);
            for (int i = 0; i < 15; i++) {
                TorsoYawGuardResult mid = Step(ref s, deg > 0f ? 176f : -176f);
                Assert.AreEqual(Mathf.Sign(deg), Mathf.Sign(Deg(mid.ShoulderYaw)),
                                "sign must not invert while edge-on");
            }
            Assert.AreEqual(deg, Deg(Step(ref s, deg).ShoulderYaw), 0.01f);
        }

        [TestCase(45f)]
        [TestCase(-45f)]
        public void Transition_ThroughWrapAndBack_Restores(float deg) {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, deg);
            Step(ref s, deg > 0f ? -178f : 178f);       // the flipped reading
            Assert.AreEqual(deg, Deg(Step(ref s, deg).ShoulderYaw), 0.01f);
        }

        [Test]
        public void Transition_LeftToCentreToRight_AndBack() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Assert.AreEqual(-45f, Deg(Step(ref s, -45f).ShoulderYaw), 0.01f);
            Assert.AreEqual(0f, Deg(Step(ref s, 0f).ShoulderYaw), 0.01f);
            Assert.AreEqual(45f, Deg(Step(ref s, 45f).ShoulderYaw), 0.01f);
            Assert.AreEqual(0f, Deg(Step(ref s, 0f).ShoulderYaw), 0.01f);
            Assert.AreEqual(-45f, Deg(Step(ref s, -45f).ShoulderYaw), 0.01f);
        }

        // ---------------------------------------------------------------- composition safety ------

        [Test]
        public void HipAndShoulderAreGuardedTogether_NeverMixed() {
            // V5 composes shoulderYaw − hipYaw. If a wrapped shoulder were held while a clean hip was
            // followed, the difference would fabricate a twist the human never made.
            TorsoYawGuardState s = default(TorsoYawGuardState);
            TorsoYawGuard.Evaluate(Rad(20f), Rad(30f), true, ref s);
            TorsoYawGuardResult r = TorsoYawGuard.Evaluate(Rad(20f), Rad(170f), true, ref s);
            Assert.AreEqual(TorsoYawState.WrapGuarded, r.State);
            Assert.AreEqual(20f, Deg(r.HipYaw), 0.01f, "the clean hip must be held too");
            Assert.AreEqual(30f, Deg(r.ShoulderYaw), 0.01f);
            Assert.AreEqual(10f, Deg(r.ShoulderYaw - r.HipYaw), 0.01f,
                            "the twist must stay the last VALID twist, not a fabricated one");
        }

        [Test]
        public void TwistIsPreservedInsideTheEnvelope() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            TorsoYawGuardResult r = TorsoYawGuard.Evaluate(Rad(10f), Rad(45f), true, ref s);
            Assert.AreEqual(TorsoYawState.Valid, r.State);
            Assert.AreEqual(35f, Deg(r.ShoulderYaw - r.HipYaw), 0.01f);
        }

        // ---------------------------------------------------------------- statistics --------------

        [Test]
        public void HoldStatistics_AreCountedForReporting() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 40f);                       // 1 valid
            Step(ref s, 170f);                      // hold run 1: 2 frames
            Step(ref s, 170f);
            Step(ref s, 40f);                       // valid
            Step(ref s, 170f);                      // hold run 2: 1 frame
            Step(ref s, 40f);                       // valid
            Assert.AreEqual(6, s.FramesTotal);
            Assert.AreEqual(3, s.FramesHeld);
            Assert.AreEqual(2, s.LongestHold);
            Assert.AreEqual(2, s.HoldRuns);
            Assert.AreEqual(50f, TorsoYawGuard.HeldPercent(ref s), 0.01f);
            Assert.AreEqual(1.5f, TorsoYawGuard.AverageHoldFrames(ref s), 0.01f);
        }

        [Test]
        public void Reset_ClearsHeldStateAndStatistics() {
            TorsoYawGuardState s = default(TorsoYawGuardState);
            Step(ref s, 45f);
            Step(ref s, 170f);
            s.Reset();
            Assert.IsFalse(s.HasValid);
            Assert.AreEqual(0, s.FramesTotal);
            Assert.AreEqual(0, s.LongestHold);
            TorsoYawGuardResult r = Step(ref s, 175f);
            Assert.AreEqual(TorsoYawState.NoValue, r.State, "after Reset there is nothing to hold");
        }

        // ---------------------------------------------------------------- thresholds --------------

        [Test]
        public void ThresholdsAreTheInheritedOnes_NotNewlyInvented() {
            // 150° is F-10's wrap guard, scored in F-14 and replicated in F-15.
            // 60° is the F-15 measured envelope. A change here invalidates that evidence.
            Assert.AreEqual(150f, TorsoYawGuard.WrapGuardDeg, Tol);
            Assert.AreEqual(60f, TorsoYawGuard.ValidatedRangeDeg, Tol);
        }
    }
}
