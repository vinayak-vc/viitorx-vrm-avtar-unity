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

        // ===================================================================================
        // ARM RETARGET V2 - near-straight-elbow roll stability.
        // docs/UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md section 2.
        //
        // The V1 live capture measured 31 forearm roll pops above 45 deg, 68% of them below 15 deg of
        // elbow bend, with 7 of the 10 steps above 90 deg inside the single 10.0-12.5 deg bend bin that
        // straddles the old sin-0.2 (11.54 deg) threshold. These tests pin the state machine that
        // replaced it: hysteresis, a continuity guard, and a bounded, debounced re-acquisition.
        // ===================================================================================

        /// <summary>
        /// Build a source arm whose elbow is bent by <paramref name="bendDeg"/>, on the side of the upper
        /// arm picked by <paramref name="side"/> (+1 / -1 give OPPOSITE elbow-plane normals). The upper arm
        /// is always the same direction, so any change in the solved upper direction would be the solver
        /// leaking roll into the aim.
        /// </summary>
        private static ArmAimResult SolveBend(ref ArmRollState state, ArmRestBasis rest,
                                              float bendDeg, float side) {
            Vector3 shoulder = SourceLeftShoulder;
            Vector3 axis = Vector3.right;
            Vector3 elbow = shoulder + axis * UpperLength;
            float a = bendDeg * Mathf.Deg2Rad;
            Vector3 fore = (axis * Mathf.Cos(a) + Vector3.back * side * Mathf.Sin(a)).normalized;
            return ArmAimSolver.Solve(shoulder, elbow, elbow + fore * ForeLength, rest, ref state, true);
        }

        private static float RollStep(float previous, float current) {
            return Mathf.Abs(Mathf.DeltaAngle(previous, current));
        }

        [Test]
        public void V2_ThresholdChatter_DoesNotFlipTheRoll() {
            // (1) THRESHOLD CHATTER. Oscillate the bend across the OLD single threshold (11.54 deg) with
            // the plane sign alternating - exactly the 10.0-12.5 deg bin where the live capture put 7 of
            // its 10 roll steps above 90 deg. Hysteresis must keep the state put: the dead band
            // [8.6 deg, 20.5 deg] strictly contains this oscillation, so the plane is never entered.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            SolveBend(ref state, rest, 60f, 1f);        // establish a real normal
            SolveBend(ref state, rest, 2f, 1f);         // straighten: releases the hysteresis latch
            float previous = SolveBend(ref state, rest, 2f, 1f).RollDeg;
            float worst = 0f;
            int flips = 0;
            for (int i = 0; i < 40; i++) {
                // 10.5 / 12.5 deg straddles the OLD sin-0.2 threshold (11.54 deg): under V1 this alternated
                // Held / ElbowPlane every frame. The plane side alternates too, so every entry V1 allowed
                // would have reversed the normal.
                float bend = (i % 2 == 0) ? 10.5f : 12.5f;
                float side = (i % 2 == 0) ? 1f : -1f;
                ArmAimResult r = SolveBend(ref state, rest, bend, side);
                Assert.IsTrue(r.Valid);
                Assert.AreNotEqual(ArmNormalSource.ElbowPlane, r.NormalSource,
                                   "the elbow plane must not be entered inside the hysteresis dead band");
                float step = RollStep(previous, r.RollDeg);
                worst = Mathf.Max(worst, step);
                if (step > 90f) {
                    flips = flips + 1;
                }
                previous = r.RollDeg;
            }
            Assert.AreEqual(0, flips, "threshold chatter must not produce a single roll flip");
            Assert.Less(worst, ArmAimSolver.RollSlewMaxDeg,
                        "roll must stay put while the bend chatters across the old threshold");

            // The other half of hysteresis: once the plane IS entered, a drop back into the dead band must
            // NOT release it (that asymmetry is what a single threshold lacked).
            ArmAimResult entered = SolveBend(ref state, rest, 45f, 1f);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, entered.NormalSource);
            ArmAimResult stillLocked = SolveBend(ref state, rest, 15f, 1f);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, stillLocked.NormalSource,
                            "15 deg is above the exit threshold: a locked plane must stay locked");
        }

        [Test]
        public void V2_StraightToBendToStraight_KeepsRollContinuous() {
            // (2) STRAIGHT -> BEND -> STRAIGHT. Passing through straight reverses cross(upper, forearm).
            // The roll must not snap when the arm re-bends on the other side.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            float previous = SolveBend(ref state, rest, 70f, 1f).RollDeg;
            float worst = 0f;
            float[] bends = new float[] { 60f, 40f, 25f, 12f, 4f, 0f, 4f, 12f, 25f, 40f, 60f, 70f };
            for (int i = 0; i < bends.Length; i++) {
                float side = i < 6 ? 1f : -1f;
                ArmAimResult r = SolveBend(ref state, rest, bends[i], side);
                Assert.IsTrue(r.Valid);
                worst = Mathf.Max(worst, RollStep(previous, r.RollDeg));
                previous = r.RollDeg;
            }
            for (int i = 0; i < 30; i++) {
                ArmAimResult r = SolveBend(ref state, rest, 70f, -1f);
                worst = Mathf.Max(worst, RollStep(previous, r.RollDeg));
                previous = r.RollDeg;
            }
            Assert.LessOrEqual(worst, ArmAimSolver.RollSlewMaxDeg + DirTolerance,
                               "no frame may move the roll more than the bounded slew rate");
        }

        [Test]
        public void V2_StraightToShallowBend_HoldsRatherThanEntering() {
            // (3) STRAIGHT -> SHALLOW BEND. A bend that never reaches the enter threshold must not take
            // over the roll; the previously established normal keeps it.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            ArmAimResult seeded = SolveBend(ref state, rest, 70f, 1f);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, seeded.NormalSource);
            Vector3 seededNormal = seeded.BendNormal;

            SolveBend(ref state, rest, 2f, 1f);                          // straighten: releases the latch
            ArmAimResult shallow = SolveBend(ref state, rest, 15f, -1f); // shallow, and on the other side
            Assert.AreNotEqual(ArmNormalSource.ElbowPlane, shallow.NormalSource,
                               "15 deg is inside the dead band: the plane must not be entered");
            Assert.Less(Vector3.Angle(seededNormal, shallow.BendNormal), 5f,
                        "a sub-threshold bend must not steal the roll reference");

            // ...but a bend past the enter threshold still does drive it, so roll steering is not lost.
            ArmAimResult deep = SolveBend(ref state, rest, 45f, 1f);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, deep.NormalSource,
                            "a clear bend must still drive the roll from real geometry");
        }

        [Test]
        public void V2_ShallowBendNoisyCrossing_ProducesNoFlip() {
            // (4) SHALLOW BEND, NOISY CROSSING. Pseudo-random bends around the old threshold with a random
            // plane side - the live near-straight noise case. Deterministic seed, so this is reproducible.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            SolveBend(ref state, rest, 55f, 1f);
            float previous = SolveBend(ref state, rest, 55f, 1f).RollDeg;
            Random.InitState(20260909);
            float worst = 0f;
            int flips = 0;
            for (int i = 0; i < 400; i++) {
                float bend = Random.Range(0f, 18f);
                float side = Random.value < 0.5f ? 1f : -1f;
                ArmAimResult r = SolveBend(ref state, rest, bend, side);
                Assert.IsTrue(r.Valid);
                float step = RollStep(previous, r.RollDeg);
                worst = Mathf.Max(worst, step);
                if (step > 90f) {
                    flips = flips + 1;
                }
                previous = r.RollDeg;
            }
            Assert.AreEqual(0, flips, "noisy near-straight crossings must not flip the roll");
            Assert.LessOrEqual(worst, ArmAimSolver.RollSlewMaxDeg + DirTolerance,
                               "worst single-frame roll step under near-straight noise");
        }

        [Test]
        public void V2_OppositeNormal_IsRejected_NotAcceptedImmediately() {
            // (5) OPPOSITE-NORMAL REJECTION. A single frame proposing the exactly-reversed plane, at a bend
            // where the plane WOULD otherwise be trusted, must not be taken.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            ArmAimResult held = SolveBend(ref state, rest, 60f, 1f);
            Assert.AreEqual(ArmNormalSource.ElbowPlane, held.NormalSource);
            Vector3 heldNormal = held.BendNormal;

            ArmAimResult flipped = SolveBend(ref state, rest, 60f, -1f);
            Assert.AreEqual(ArmNormalSource.Guarded, flipped.NormalSource,
                            "a reversed plane must be guarded, not adopted");
            Assert.Greater(Vector3.Dot(heldNormal, flipped.BendNormal), 0.99f,
                           "the held normal must survive the reversed candidate unchanged");

            // Sustained and coherent, it is eventually believed - otherwise a genuine re-bend the other way
            // would be locked out forever. It arrives via the bounded slew, never as a snap.
            ArmNormalSource last = ArmNormalSource.Guarded;
            for (int i = 0; i < ArmAimSolver.FlipConfirmFrames + 2; i++) {
                last = SolveBend(ref state, rest, 60f, -1f).NormalSource;
            }
            Assert.AreEqual(ArmNormalSource.Slewed, last,
                            "a sustained coherent reversal must eventually be accepted, via the slew");
        }

        [Test]
        public void V2_NoRollStepAbove90Degrees_AcrossAnAdversarialSequence() {
            // (6) NO ROLL JUMP ABOVE 90 deg. One long adversarial sequence: sweeps, chatter, reversals,
            // straightening and re-bending. Not one frame may move the roll more than the bounded rate.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            Random.InitState(4242);
            float previous = SolveBend(ref state, rest, 50f, 1f).RollDeg;
            float worst = 0f;
            int over90 = 0;
            int over45 = 0;
            for (int i = 0; i < 2000; i++) {
                float bend;
                if (i % 5 == 0) {
                    bend = Random.Range(0f, 12f);          // near straight
                } else if (i % 5 == 1) {
                    bend = Random.Range(9f, 22f);          // straddling both thresholds
                } else {
                    bend = Random.Range(20f, 140f);        // clearly bent
                }
                float side = Random.value < 0.35f ? -1f : 1f;
                ArmAimResult r = SolveBend(ref state, rest, bend, side);
                Assert.IsTrue(r.Valid);
                float step = RollStep(previous, r.RollDeg);
                worst = Mathf.Max(worst, step);
                if (step > 90f) {
                    over90 = over90 + 1;
                }
                if (step > 45f) {
                    over45 = over45 + 1;
                }
                previous = r.RollDeg;
            }
            Assert.AreEqual(0, over90, "no single frame may change the roll by more than 90 deg");
            Assert.AreEqual(0, over45, "and in fact none may exceed the 30 deg slew bound");
            Assert.LessOrEqual(worst, ArmAimSolver.RollSlewMaxDeg + DirTolerance);
        }

        [Test]
        public void V2_DirectionsAreUnchangedByTheRollStateMachine() {
            // (7) DIRECTION PRESERVED EXACTLY. LookRotation(dir, normal) maps forward onto dir whatever the
            // normal is, so the guard can only ever change roll. Drive the same adversarial sequence and
            // assert BOTH bone directions still equal the mirrored source directions to numerical
            // precision - including on the frames where the guard rejected or slewed.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            Random.InitState(4242);
            Vector3 axis = Vector3.right;
            float worstUpper = 0f;
            float worstFore = 0f;
            int guarded = 0;
            for (int i = 0; i < 2000; i++) {
                float bend;
                if (i % 5 == 0) {
                    bend = Random.Range(0f, 12f);
                } else if (i % 5 == 1) {
                    bend = Random.Range(9f, 22f);
                } else {
                    bend = Random.Range(20f, 140f);
                }
                float side = Random.value < 0.35f ? -1f : 1f;
                float a = bend * Mathf.Deg2Rad;
                Vector3 fore = (axis * Mathf.Cos(a) + Vector3.back * side * Mathf.Sin(a)).normalized;
                ArmAimResult r = SolveBend(ref state, rest, bend, side);
                Assert.IsTrue(r.Valid);
                if (r.NormalSource == ArmNormalSource.Guarded || r.NormalSource == ArmNormalSource.Slewed) {
                    guarded = guarded + 1;
                }
                Vector3 gotUpper = r.UpperRig * rest.UpperDir;
                Vector3 gotFore = r.LowerRig * rest.LowerDir;
                worstUpper = Mathf.Max(worstUpper, Vector3.Angle(Mirror(axis), gotUpper));
                worstFore = Mathf.Max(worstFore, Vector3.Angle(Mirror(fore), gotFore));
            }
            Assert.Greater(guarded, 0, "the sequence must actually exercise the guard");
            Assert.Less(worstUpper, DirTolerance,
                        "upper-arm direction must be untouched by the roll state machine");
            Assert.Less(worstFore, DirTolerance,
                        "forearm direction must be untouched by the roll state machine");
        }

        [Test]
        public void V2_HysteresisThresholds_MatchTheMeasuredLiveDistribution() {
            // The constants are evidence, not taste: the enter threshold must sit ABOVE the entire measured
            // unreliable region (all 10 live roll steps above 90 deg were at bend below 12.5 deg) and the
            // exit threshold BELOW it, so the dead band contains the 10.0-12.5 deg hot zone outright.
            float enterDeg = Mathf.Asin(ArmAimSolver.BendSinEnter) * Mathf.Rad2Deg;
            float exitDeg = Mathf.Asin(ArmAimSolver.BendSinExit) * Mathf.Rad2Deg;
            Assert.Less(exitDeg, 10f, "exit must sit below the measured 10.0-12.5 deg hot zone");
            Assert.Greater(enterDeg, 12.5f, "enter must sit above the measured hot zone");
            Assert.Greater(enterDeg - exitDeg, 8f, "the dead band must be wide enough to stop chatter");
            Assert.LessOrEqual(ArmAimSolver.RollSlewMaxDeg, ArmAimSolver.RollStepMaxDeg);
        }

        [Test]
        public void V2_GuardIsRateBounded_AtExactlyTheDocumentedAngles() {
            // The guard compares COSINES so the per-frame path carries no acos. That is only equivalent
            // to comparing angles while the cosine constants match the degree constants, and the two are
            // declared separately — so pin the relationship rather than trusting it.
            //
            // Driven by a sustained reversal, the roll must advance in steps of exactly RollSlewMaxDeg.
            ArmRollState state = new ArmRollState();
            ArmRestBasis rest = RightRest();
            SolveBend(ref state, rest, 60f, 1f);
            float previous = SolveBend(ref state, rest, 60f, 1f).RollDeg;
            float largestStep = 0f;
            for (int i = 0; i < 40; i++) {
                ArmAimResult r = SolveBend(ref state, rest, 60f, -1f);
                largestStep = Mathf.Max(largestStep, RollStep(previous, r.RollDeg));
                previous = r.RollDeg;
            }
            Assert.AreEqual(ArmAimSolver.RollSlewMaxDeg, largestStep, 0.2f,
                            "the slew must move the roll by exactly the documented bound per frame — a "
                            + "mismatch means the cosine constants have drifted from the degree constants");
        }
    }
}
