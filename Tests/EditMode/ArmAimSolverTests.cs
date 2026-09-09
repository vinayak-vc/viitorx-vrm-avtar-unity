using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    /// <summary>
    /// ARM RETARGET V1 acceptance tests (docs/UNITY_ARM_RETARGET_V1_2026-09-08.md §13-§15).
    ///
    /// These assert the property the forensic audit proved was achievable and the Kalidokit Euler branch
    /// could not deliver: the avatar's arm bone directions equal the (mirrored) source arm directions, to
    /// numerical precision, for every fixed pose — and identically on both sides.
    ///
    /// Everything runs in the control-rig REST FRAME (X = avatar's LEFT, Y = up, Z = avatar's BACK) with a
    /// T-posed rest basis measured from the shipping VRMs, and with the torso at rest (this task does not
    /// touch the torso), so the bone's rig-frame rotation is the solver's output directly.
    /// </summary>
    public sealed class ArmAimSolverTests {
        // Rest basis as measured from the project's VRMs (audit §2/§10): arms straight out sideways.
        private static readonly Vector3 Lateral = Vector3.right;   // avatar's LEFT is +X
        private static readonly Vector3 Up = Vector3.up;

        // Source landmark positions, hip-relative metres, in the same frame.
        private static readonly Vector3 SourceLeftShoulder = new Vector3(0.18f, 0.50f, 0f);
        private static readonly Vector3 SourceRightShoulder = new Vector3(-0.18f, 0.50f, 0f);
        private const float UpperLength = 0.28f;
        private const float ForeLength = 0.26f;

        // Direction round-trips through two quaternion products in float32; 0.05 deg is ~2 orders of
        // magnitude above the observed residual and still far below anything visible.
        private const float DirTolerance = 0.05f;
        // Symmetry must come from solving each arm independently, not from a sign trick, so the two sides
        // should agree to the same numerical floor.
        private const float SymmetryTolerance = 0.05f;

        private static ArmRestBasis LeftRest() {
            return ArmAimSolver.BuildRestBasis(Vector3.right, Vector3.right, Lateral, Up);
        }

        private static ArmRestBasis RightRest() {
            return ArmAimSolver.BuildRestBasis(Vector3.left, Vector3.left, Lateral, Up);
        }

        /// <summary>
        /// Drive the avatar's LEFT arm from the subject's RIGHT arm (Kalidokit's cross-map, preserved) and
        /// return the achieved bone directions in the rig frame.
        /// </summary>
        private static void SolveAvatarLeft(Vector3 upperDir, Vector3 foreDir,
                                            out Vector3 gotUpper, out Vector3 gotFore, out ArmAimResult result) {
            Vector3 shoulder = SourceRightShoulder;
            Vector3 elbow = shoulder + upperDir.normalized * UpperLength;
            Vector3 wrist = elbow + foreDir.normalized * ForeLength;
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = LeftRest();
            result = ArmAimSolver.Solve(shoulder, elbow, wrist, rest, ref state, true);
            gotUpper = result.UpperRig * rest.UpperDir;
            gotFore = result.LowerRig * rest.LowerDir;
        }

        private static void SolveAvatarRight(Vector3 upperDir, Vector3 foreDir,
                                             out Vector3 gotUpper, out Vector3 gotFore, out ArmAimResult result) {
            Vector3 shoulder = SourceLeftShoulder;
            Vector3 elbow = shoulder + upperDir.normalized * UpperLength;
            Vector3 wrist = elbow + foreDir.normalized * ForeLength;
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            result = ArmAimSolver.Solve(shoulder, elbow, wrist, rest, ref state, true);
            gotUpper = result.UpperRig * rest.UpperDir;
            gotFore = result.LowerRig * rest.LowerDir;
        }

        private static Vector3 Mirror(Vector3 v) {
            return new Vector3(-v.x, v.y, v.z);
        }

        [Test]
        public void RestBasis_IsMeasured_NotHardcoded() {
            ArmRestBasis left = LeftRest();
            ArmRestBasis right = RightRest();
            Assert.IsTrue(left.Valid);
            Assert.IsTrue(right.Valid);
            // The rest elbow hinge must be OPPOSITE on the two arms — that is what makes both elbows point
            // toward the face when bent, and it falls out of the rig geometry rather than an invert flag.
            Assert.Less(Vector3.Dot(left.BendNormal, right.BendNormal), -0.99f,
                        "left and right rest hinge axes must be opposed");
            Assert.AreEqual(0f, Vector3.Dot(left.BendNormal, left.UpperDir), 1e-4f,
                            "the hinge axis must be perpendicular to the bone axis");
        }

        // A1-A11: the fixed poses from the audit harness. Each case gives the SUBJECT's arm direction and
        // forearm direction; the expected avatar direction is that vector mirrored in X.
        [Test]
        [TestCase(0f, -1f, 0f, 0f, -1f, 0f, TestName = "A1_standing_arms_down")]
        [TestCase(0f, 1f, 0f, 0f, 1f, 0f, TestName = "A2_A3_arm_straight_up")]
        [TestCase(1f, 0f, 0f, 1f, 0f, 0f, TestName = "A5_T_pose_out_positiveX")]
        [TestCase(-1f, 0f, 0f, -1f, 0f, 0f, TestName = "A5_T_pose_out_negativeX")]
        [TestCase(0f, 0f, -1f, 0f, 0f, -1f, TestName = "A6_arm_forward")]
        [TestCase(0f, 0f, 1f, 0f, 0f, 1f, TestName = "A7_arm_backward")]
        [TestCase(1f, 0f, 0f, 0f, 0f, -1f, TestName = "A8_elbow_bent_forward")]
        [TestCase(0f, 1f, 0f, 0f, 0f, -1f, TestName = "A4_overhead_forearm_forward")]
        [TestCase(0.5f, 0.7f, -0.3f, -0.2f, -0.6f, 0.5f, TestName = "A9_arbitrary_asymmetric")]
        public void FixedPose_AvatarArmDirection_MatchesMirroredSource(
            float ux, float uy, float uz, float fx, float fy, float fz) {
            Vector3 upper = new Vector3(ux, uy, uz).normalized;
            Vector3 fore = new Vector3(fx, fy, fz).normalized;

            Vector3 gotUpper, gotFore;
            ArmAimResult result;
            SolveAvatarLeft(upper, fore, out gotUpper, out gotFore, out result);
            Assert.IsTrue(result.Valid, "solve must succeed for a well-formed arm");
            Assert.AreEqual(0f, Vector3.Angle(Mirror(upper), gotUpper), DirTolerance,
                            "avatar upper-arm direction");
            Assert.AreEqual(0f, Vector3.Angle(Mirror(fore), gotFore), DirTolerance,
                            "avatar forearm direction");
        }

        [Test]
        [TestCase(0f, -1f, 0f, 0f, -1f, 0f, TestName = "sym_arms_down")]
        [TestCase(0f, 1f, 0f, 0f, 1f, 0f, TestName = "sym_arms_overhead")]
        [TestCase(0f, 0f, -1f, 0f, 0f, -1f, TestName = "sym_arms_forward")]
        [TestCase(0f, 0f, 1f, 0f, 0f, 1f, TestName = "sym_arms_backward")]
        public void SymmetricSource_ProducesSymmetricError(float ux, float uy, float uz,
                                                           float fx, float fy, float fz) {
            // A symmetric pose: the subject's two arms are X-mirrors of each other.
            Vector3 upperR = new Vector3(ux, uy, uz).normalized;
            Vector3 foreR = new Vector3(fx, fy, fz).normalized;
            Vector3 upperL = Mirror(upperR);
            Vector3 foreL = Mirror(foreR);

            Vector3 gotUpperAvatarLeft, gotForeAvatarLeft, gotUpperAvatarRight, gotForeAvatarRight;
            ArmAimResult a, b;
            SolveAvatarLeft(upperR, foreR, out gotUpperAvatarLeft, out gotForeAvatarLeft, out a);
            SolveAvatarRight(upperL, foreL, out gotUpperAvatarRight, out gotForeAvatarRight, out b);

            float errLeft = Vector3.Angle(Mirror(upperR), gotUpperAvatarLeft);
            float errRight = Vector3.Angle(Mirror(upperL), gotUpperAvatarRight);
            Assert.AreEqual(errLeft, errRight, SymmetryTolerance,
                            "a symmetric input must produce the same error on both arms — the Kalidokit "
                            + "Euler branch showed 20.2 deg here");
            // And the avatar's two arms must themselves be mirror images.
            Assert.AreEqual(0f, Vector3.Angle(Mirror(gotUpperAvatarLeft), gotUpperAvatarRight), SymmetryTolerance,
                            "avatar upper arms must mirror each other");
            Assert.AreEqual(0f, Vector3.Angle(Mirror(gotForeAvatarLeft), gotForeAvatarRight), SymmetryTolerance,
                            "avatar forearms must mirror each other");
        }

        [Test]
        public void ForearmBend_IsNotPinned() {
            // The old path clamped lowerArm.x to +-0.3 rad and saturated it, pinning ~17 deg of bend on a
            // straight arm. A straight source arm must produce a straight avatar arm.
            Vector3 gotUpper, gotFore;
            ArmAimResult result;
            SolveAvatarLeft(Vector3.down, Vector3.down, out gotUpper, out gotFore, out result);
            Assert.AreEqual(0f, Vector3.Angle(gotUpper, gotFore), DirTolerance,
                            "a straight source arm must not gain a constant forearm bend");
            Assert.AreEqual(0f, result.BendDeg, DirTolerance);
        }

        [Test]
        public void ElbowPlane_DrivesRoll_AndTheBendNormalFollows() {
            // Elbow forward vs elbow backward must put the avatar's bend normal on opposite sides. If the
            // roll were taken from a fixed world reference this would not hold.
            Vector3 upFwd, foreFwd, upBack, foreBack;
            ArmAimResult rFwd, rBack;
            SolveAvatarLeft(Vector3.right, Vector3.back, out upFwd, out foreFwd, out rFwd);
            SolveAvatarLeft(Vector3.right, Vector3.forward, out upBack, out foreBack, out rBack);

            Vector3 normalFwd = Vector3.Cross(upFwd, foreFwd).normalized;
            Vector3 normalBack = Vector3.Cross(upBack, foreBack).normalized;
            Assert.Less(Vector3.Dot(normalFwd, normalBack), -0.99f,
                        "flipping the source elbow plane must flip the avatar's bend normal");
            Assert.AreEqual(ArmNormalSource.ElbowPlane, rFwd.NormalSource);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, rBack.NormalSource);
        }

        [Test]
        public void RollSweep_IsContinuous_NoFlips() {
            // Sweep the elbow plane a full turn around the upper-arm axis. The applied roll must ramp
            // monotonically in step with the sweep and never jump — a jump is the helicopter / 180 reversal.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            Vector3 axis = Vector3.right;              // subject's left arm, held horizontal
            Vector3 perpA = Vector3.up;
            Vector3 perpB = Vector3.Cross(axis, perpA);
            const int step = 10;
            float previous = 0f;
            float worstJump = 0f;
            int flips = 0;
            for (int deg = 0; deg < 360; deg += step) {
                float a = deg * Mathf.Deg2Rad;
                Vector3 radial = perpA * Mathf.Cos(a) + perpB * Mathf.Sin(a);
                Vector3 fore = (axis * Mathf.Cos(60f * Mathf.Deg2Rad) + radial * Mathf.Sin(60f * Mathf.Deg2Rad)).normalized;
                Vector3 shoulder = SourceLeftShoulder;
                Vector3 elbow = shoulder + axis * UpperLength;
                Vector3 wrist = elbow + fore * ForeLength;
                ArmAimResult result = ArmAimSolver.Solve(shoulder, elbow, wrist, rest, ref state, true);
                Assert.IsTrue(result.Valid);

                Vector3 gotUpper = result.UpperRig * rest.UpperDir;
                Vector3 gotFore = result.LowerRig * rest.LowerDir;
                Assert.AreEqual(0f, Vector3.Angle(Mirror(axis), gotUpper), DirTolerance,
                                "upper-arm direction must stay exact through the roll sweep");
                Assert.AreEqual(0f, Vector3.Angle(Mirror(fore), gotFore), DirTolerance,
                                "forearm direction must stay exact through the roll sweep");

                if (deg > 0) {
                    float jump = Mathf.Abs(Mathf.DeltaAngle(previous, result.RollDeg));
                    worstJump = Mathf.Max(worstJump, jump);
                    if (jump > 90f) {
                        flips = flips + 1;
                    }
                }
                previous = result.RollDeg;
            }
            Assert.AreEqual(0, flips, "no >90 deg roll discontinuity is allowed");
            Assert.Less(worstJump, (float)step + DirTolerance,
                        "roll must advance in step with the sweep, not jump");
        }

        [Test]
        public void StraightArm_HoldsPreviousRoll_InsteadOfSnapping() {
            // Bend the elbow (establishes a normal), then straighten. The straight frame carries no elbow
            // plane, so the solver must HOLD rather than invent a roll.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            Vector3 shoulder = SourceLeftShoulder;
            Vector3 elbow = shoulder + Vector3.right * UpperLength;

            ArmAimResult bent = ArmAimSolver.Solve(shoulder, elbow, elbow + Vector3.back * ForeLength,
                                                   rest, ref state, true);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, bent.NormalSource);
            Vector3 heldNormal = bent.BendNormal;

            ArmAimResult straight = ArmAimSolver.Solve(shoulder, elbow, elbow + Vector3.right * ForeLength,
                                                       rest, ref state, true);
            Assert.AreEqual(ArmNormalSource.Held, straight.NormalSource);
            Assert.AreEqual(0f, Vector3.Angle(heldNormal, straight.BendNormal), 1f,
                            "a straightened arm must keep the roll it had");
        }

        [Test]
        public void FirstFrameStraightArm_UsesRestCarriedNormal() {
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            Vector3 shoulder = SourceLeftShoulder;
            Vector3 elbow = shoulder + Vector3.down * UpperLength;
            ArmAimResult result = ArmAimSolver.Solve(shoulder, elbow, elbow + Vector3.down * ForeLength,
                                                     rest, ref state, true);
            Assert.IsTrue(result.Valid);
            Assert.AreEqual(ArmNormalSource.RestCarried, result.NormalSource);
            // Roll-free swing => zero roll relative to the carried rest normal (cannot helicopter).
            Assert.AreEqual(0f, result.RollDeg, 0.5f);
        }

        [Test]
        public void DegenerateLandmarks_ReportInvalid_SoTheGateCanHold() {
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            // Zero-filled landmarks (the sidecar's emit gate) collapse shoulder and elbow onto each other.
            ArmAimResult result = ArmAimSolver.Solve(Vector3.zero, Vector3.zero, Vector3.zero,
                                                     rest, ref state, true);
            Assert.IsFalse(result.Valid, "a degenerate arm must NOT produce a rotation — P0-1 holds instead");
            Assert.AreEqual(Quaternion.identity, result.UpperRig);
        }

        [Test]
        public void MissingWrist_LeavesArmStraight_NotAimedAtTheOrigin() {
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            Vector3 shoulder = SourceLeftShoulder;
            Vector3 elbow = shoulder + Vector3.down * UpperLength;
            ArmAimResult result = ArmAimSolver.Solve(shoulder, elbow, elbow, rest, ref state, true);
            Assert.IsTrue(result.Valid);
            Assert.AreEqual(0f, result.BendDeg, DirTolerance,
                            "with no wrist the forearm continues the upper arm");
        }

        [Test]
        public void LimbGate_QuaternionOverload_HoldsInsteadOfCollapsing() {
            // P0 safety must survive the switch to a quaternion payload (§12).
            LimbGate gate = new LimbGate();
            Quaternion u = Quaternion.Euler(20f, 30f, 40f);
            Quaternion l = Quaternion.Euler(-10f, 5f, 0f);
            Quaternion tu, tl;
            bool transitioned;

            Assert.IsTrue(gate.Resolve(0.9f, 0.3f, u, l, out tu, out tl, out transitioned));
            Assert.AreEqual(u, tu);

            for (int i = 0; i < 5; i++) {
                Assert.IsTrue(gate.Resolve(0f, 0.3f, Quaternion.identity, Quaternion.identity,
                                           out tu, out tl, out transitioned));
                Assert.AreEqual(u, tu, "must hold the last valid rotation, never snap to identity");
                Assert.AreEqual(l, tl);
            }
            Assert.AreEqual(LimbGate.State.Held, gate.CurrentState);
            Assert.AreEqual(1, gate.HoldEvents);
        }

        [Test]
        public void LimbGate_QuaternionOverload_NoValidYet_DoesNotApply() {
            LimbGate gate = new LimbGate();
            Quaternion tu, tl;
            bool transitioned;
            Assert.IsFalse(gate.Resolve(0f, 0.3f, Quaternion.identity, Quaternion.identity,
                                        out tu, out tl, out transitioned),
                           "with nothing valid ever seen the bones must be left at rest");
        }
    }
}
