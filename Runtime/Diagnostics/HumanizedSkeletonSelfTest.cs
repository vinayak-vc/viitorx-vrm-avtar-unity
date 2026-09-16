using System.Globalization;
using System.Text;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// F-27 — the humanized layer's test mode: artificial poses with deliberately injected bad data,
    /// and assertions that what comes out is still a human body.
    ///
    /// EVERY ANSWER HERE IS KNOWN BY CONSTRUCTION. <see cref="SyntheticPose"/> builds a body with exact
    /// bone lengths and exact joint angles, so "the forearm is still 0.26 m" and "the elbow did not
    /// fold past 25 deg" are measurements against the generator rather than impressions of a viewport.
    /// An instrument that judges a correction layer gets checked before the layer does: four wrong
    /// instruments were caught this month, and each was caught by a harness like this one.
    ///
    /// THE TWO HALVES MATTER EQUALLY. Half these cases prove the layer FIXES bad data; the other half
    /// prove it LEAVES GOOD DATA ALONE. A layer that passes only the first half is not a protection,
    /// it is a distortion — and the requirement is explicit that real movement must not be smoothed or
    /// delayed. <see cref="CleanTrackingIsPassedThrough"/> and <see cref="NoLagOnRealMotion"/> are the
    /// cases that would fail if this layer started inventing a pose of its own.
    ///
    /// Runs headless: no Editor, no Play mode, no avatar, no camera. Call <see cref="Run"/>.
    /// </summary>
    public static class HumanizedSkeletonSelfTest {
        private const float Dt = 1f / 30f;
        private const float Conf = 0.9f;

        private static int passed;
        private static int failed;
        private static StringBuilder report;

        /// <summary>Run every case. Returns a human-readable report; the last line is the tally.</summary>
        public static string Run() {
            passed = 0;
            failed = 0;
            report = new StringBuilder(4096);
            report.AppendLine("F-27 HUMANIZED SKELETON - self test (synthetic poses + injected faults)");
            report.AppendLine(new string('-', 92));

            ModelTopologyIsConsistent();
            CleanTrackingIsPassedThrough();
            NoLagOnRealMotion();
            BoneLengthsAreHeldConstant();
            ConfidentCollapseToOriginIsRefused();
            DroppedJointIsHeldAndFollowsItsParent();
            PoisonedLandmarkCannotReachTheOutput();
            TeleportIsClamped();
            SustainedFastMotionIsEventuallyAccepted();
            FlickeringIsNeverAcceptedAsMotion();
            ArmsOverheadAreNotTruncated();
            ImpossibleElbowFoldIsOpenedUp();
            KneeCannotBendForwards();
            TorsoCannotTwistPastASpine();
            RecoveryDoesNotSnap();
            WholeBodyLossIsNotFabricated();
            StagesCanBeDisabledIndependently();

            report.AppendLine(new string('-', 92));
            report.AppendLine(passed + " passed, " + failed + " failed");
            return report.ToString();
        }

        /// <summary>True when the last <see cref="Run"/> had no failures.</summary>
        public static bool LastRunPassed {
            get {
                return failed == 0;
            }
        }

        // --- assertions --------------------------------------------------------------------------

        private static void Check(string name, bool ok, string detail) {
            if (ok) {
                passed = passed + 1;
                report.AppendLine("  PASS  " + Pad(name) + detail);
            } else {
                failed = failed + 1;
                report.AppendLine("  FAIL  " + Pad(name) + detail);
            }
        }

        private static void CheckNear(string name, float got, float want, float tolerance, string units) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            bool ok = !float.IsNaN(got) && Mathf.Abs(got - want) <= tolerance;
            Check(name, ok, "got=" + got.ToString("F4", ci) + units
                          + " want=" + want.ToString("F4", ci) + units
                          + " +/-" + tolerance.ToString("F4", ci));
        }

        private static void CheckAtLeast(string name, float got, float floor, string units) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            Check(name, !float.IsNaN(got) && got >= floor,
                  "got=" + got.ToString("F3", ci) + units + " floor=" + floor.ToString("F3", ci) + units);
        }

        private static void CheckAtMost(string name, float got, float ceiling, string units) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            Check(name, !float.IsNaN(got) && got <= ceiling,
                  "got=" + got.ToString("F3", ci) + units + " ceiling=" + ceiling.ToString("F3", ci) + units);
        }

        private static string Pad(string s) {
            if (s.Length >= 54) {
                return s + "  ";
            }
            return s + new string(' ', 54 - s.Length);
        }

        // --- helpers -----------------------------------------------------------------------------

        /// <summary>Feed enough clean frames for the bone-length window to converge, then hand back a
        /// warmed layer. Everything downstream of calibration is meaningless before this.</summary>
        private static HumanizedSkeleton Warmed(out PoseFrame scratch, float armDeg, float elbowDeg) {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            scratch = new PoseFrame();
            int i = 0;
            while (i < 40) {
                SyntheticPose.Fill(scratch, i * Dt, armDeg, elbowDeg, 0f, Conf);
                layer.Process(scratch, Dt);
                i = i + 1;
            }
            layer.ResetStats();
            return layer;
        }

        // --- the cases ---------------------------------------------------------------------------

        private static void ModelTopologyIsConsistent() {
            // A mirror table that is not symmetric silently averages the wrong pair of bones, which
            // would make the body subtly asymmetric in a way no viewport inspection would catch.
            bool symmetric = true;
            bool ordered = true;
            bool[] placed = new bool[HumanBodyModel.SlotCount];
            placed[HumanBodyModel.MidHip] = true;
            int b = 0;
            while (b < HumanBodyModel.Bones.Length) {
                HumanBodyModel.Bone bone = HumanBodyModel.Bones[b];
                int m = bone.Mirror;
                if (m >= 0 && HumanBodyModel.Bones[m].Mirror != b) {
                    symmetric = false;
                }
                if (!placed[bone.Parent]) {
                    ordered = false;
                }
                placed[bone.Child] = true;
                b = b + 1;
            }
            Check("bone mirror table is symmetric", symmetric, "Mirror[Mirror[b]] == b for every bone");
            Check("parents are listed before their children", ordered, "one forward pass rebuilds the body");
        }

        private static void CleanTrackingIsPassedThrough() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            float worst = 0f;
            int i = 0;
            while (i < 60) {
                SyntheticPose.Fill(input, (40 + i) * Dt, 45f, 20f, 0f, Conf);
                PoseFrame outFrame = layer.Process(input, Dt);
                float d = PoseMetrics.MaxDisplacement(input, outFrame);
                if (d > worst) {
                    worst = d;
                }
                i = i + 1;
            }
            HumanizedStats s = layer.Stats;
            CheckAtMost("clean tracking is not moved", worst, 0.002f, "m");
            CheckAtLeast("clean tracking fires no temporal stage",
                         s.FramesUntouched + s.FramesOnlyBoneLength, 60f, " frames");
            Check("  and no joint was held, clamped or bent",
                  s.HeldJointFrames == 0L && s.VelocityClamped == 0L
                  && s.HingeClamped == 0L && s.ConeClamped == 0L && s.KneeUnbent == 0L,
                  "held=" + s.HeldJointFrames + " vclamp=" + s.VelocityClamped
                  + " hinge=" + s.HingeClamped + " cone=" + s.ConeClamped + " knee=" + s.KneeUnbent);
        }

        private static void NoLagOnRealMotion() {
            // The requirement that matters most: a real arm sweeping through its whole range must come
            // out where it went in, on the SAME frame. Any lag here is this layer inventing smoothing.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 0f, 0f);
            float worst = 0f;
            int i = 0;
            while (i < 90) {
                float arm = 180f * (i / 89f);              // 0 -> 180 deg in 3 s: a fast, real sweep
                SyntheticPose.Fill(input, (40 + i) * Dt, arm, 0f, 0f, Conf);
                PoseFrame outFrame = layer.Process(input, Dt);
                float d = Vector3.Distance(outFrame.GetLandmark(JointId.LeftWrist).Position,
                                           input.GetLandmark(JointId.LeftWrist).Position);
                if (d > worst) {
                    worst = d;
                }
                i = i + 1;
            }
            CheckAtMost("a full arm sweep arrives with zero lag", worst, 0.002f, "m");
        }

        private static void BoneLengthsAreHeldConstant() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            SyntheticPose.Fill(input, 41 * Dt, 45f, 20f, 0f, Conf);
            SyntheticPose.Stretch(input, 13, 15, 1.5f);      // forearm +50 %, aim unchanged
            float before = PoseMetrics.BoneLength(input, 13, 15);
            PoseFrame outFrame = layer.Process(input, Dt);
            float after = PoseMetrics.BoneLength(outFrame, 13, 15);
            CheckNear("a 50 % stretched forearm is restored", after, SyntheticPose.Forearm, 0.005f, "m");
            Check("  the raw stretch really was present", before > SyntheticPose.Forearm * 1.3f,
                  "input forearm=" + before.ToString("F3", CultureInfo.InvariantCulture) + "m");
        }

        private static void ConfidentCollapseToOriginIsRefused() {
            // The nastiest case: a joint at the origin wearing 0.95 confidence, so every confidence
            // gate in the pipeline waves it through. Only a geometric test can catch it.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            SyntheticPose.Fill(input, 41 * Dt, 45f, 20f, 0f, Conf);
            SyntheticPose.CollapseToOrigin(input, 15);
            PoseFrame outFrame = layer.Process(input, Dt);
            CheckNear("a confident wrist-at-the-origin keeps its forearm",
                      PoseMetrics.BoneLength(outFrame, 13, 15), SyntheticPose.Forearm, 0.01f, "m");
            CheckAtLeast("  and does not sit on the hip",
                         PoseMetrics.DistanceFromHip(outFrame, 15), 0.3f, "m");
        }

        private static void DroppedJointIsHeldAndFollowsItsParent() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 90f);
            // Drop the elbow, then raise the shoulder. A world-space hold would leave the forearm
            // behind; a LOCAL hold swings the whole limb with the body, which is the human behaviour.
            SyntheticPose.Fill(input, 41 * Dt, 45f, 90f, 0f, Conf);
            PoseFrame reference = layer.Process(input, Dt);
            float heldUpperArm = PoseMetrics.BoneLength(reference, 11, 13);

            float worstBone = 0f;
            int i = 0;
            while (i < 15) {
                SyntheticPose.Fill(input, (42 + i) * Dt, 45f + i * 2f, 90f, 0f, Conf);
                SyntheticPose.Drop(input, 13);
                SyntheticPose.Drop(input, 15);
                PoseFrame outFrame = layer.Process(input, Dt);
                float d = Mathf.Abs(PoseMetrics.BoneLength(outFrame, 11, 13) - heldUpperArm);
                if (d > worstBone) {
                    worstBone = d;
                }
                i = i + 1;
            }
            CheckAtMost("a dropped elbow keeps its bone length while held", worstBone, 0.01f, "m");
            CheckAtLeast("  the hold was actually exercised",
                         layer.Stats.HeldJointFrames, 20f, " joint-frames");
        }

        private static void PoisonedLandmarkCannotReachTheOutput() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            SyntheticPose.Fill(input, 41 * Dt, 45f, 20f, 0f, Conf);
            SyntheticPose.Poison(input, 15);
            SyntheticPose.Poison(input, 27);
            PoseFrame outFrame = layer.Process(input, Dt);
            Check("NaN never reaches the retarget", PoseMetrics.AllFinite(outFrame),
                  "a single NaN landmark propagates through a solver and takes the whole avatar with it");
            CheckAtLeast("  and the counter names the fault",
                         layer.Stats.RejectedNonFinite, 2f, " landmarks");
        }

        private static void TeleportIsClamped() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            SyntheticPose.Fill(input, 41 * Dt, 45f, 20f, 0f, Conf);
            PoseFrame before = layer.Process(input, Dt);
            Vector3 wristBefore = before.GetLandmark(JointId.LeftWrist).Position;

            SyntheticPose.Fill(input, 42 * Dt, 45f, 20f, 0f, Conf);
            SyntheticPose.Teleport(input, 15, new Vector3(0.9f, 0.9f, 0.5f));
            PoseFrame outFrame = layer.Process(input, Dt);
            float moved = Vector3.Distance(outFrame.GetLandmark(JointId.LeftWrist).Position, wristBefore);
            float ceiling = HumanBodyModel.MaxSpeed(HumanBodyModel.JointClass.Extremity) * Dt * 1.2f;
            CheckAtMost("a one-frame wrist teleport is capped at the class speed", moved, ceiling, "m");
            CheckNear("  and the forearm survives it",
                      PoseMetrics.BoneLength(outFrame, 13, 15), SyntheticPose.Forearm, 0.01f, "m");
        }

        private static void SustainedFastMotionIsEventuallyAccepted() {
            // A clamp that never yields is a latency bug in a safety hat. A demand that PERSISTS in one
            // direction is real motion and has to get through. Exercised on a TORSO joint because that
            // class has the lowest ceiling (4 m/s = 0.13 m per frame at 30 Hz) and is therefore the one
            // where a sustained above-ceiling demand can exist inside a body at all.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            int i = 0;
            while (i < 7) {
                SyntheticPose.Fill(input, (41 + i) * Dt, 45f, 20f, 0f, Conf);
                SyntheticPose.Teleport(input, 11, new Vector3(0.18f, 0.5f, 0.2f * (i + 1)));
                layer.Process(input, Dt);
                i = i + 1;
            }
            CheckAtLeast("a demand sustained in ONE direction is accepted as real motion",
                         layer.Stats.JumpAccepted, 1f, " acceptances");
        }

        private static void FlickeringIsNeverAcceptedAsMotion() {
            // The other half of the rule above. A joint jumping between two far-apart positions every
            // frame is a tracker failing, not a person moving, and must never accumulate its way past
            // the clamp however long it goes on.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            int i = 0;
            while (i < 20) {
                SyntheticPose.Fill(input, (41 + i) * Dt, 45f, 20f, 0f, Conf);
                float side = (i % 2 == 0) ? 0.7f : -0.7f;
                SyntheticPose.Teleport(input, 15, new Vector3(side, 0.4f, 0f));
                layer.Process(input, Dt);
                i = i + 1;
            }
            Check("a joint flickering between two places is never accepted",
                  layer.Stats.JumpAccepted == 0L,
                  "accepted=" + layer.Stats.JumpAccepted + " clamped=" + layer.Stats.VelocityClamped);
        }

        private static void ArmsOverheadAreNotTruncated() {
            // PERMANENT REGRESSION GUARD. F-26 §4.1 records arms-overhead as this project's worst
            // avatar failure: the skeleton puts the hands above the head and the avatar stops at
            // shoulder height. A 175 deg shoulder cone was implemented here first and measured firing
            // on this pose and no other, moving the elbow 24 mm. Any future cone about the trunk's down
            // axis will fail this case, which is the point of keeping it.
            // Warmed WITH the arms already overhead. Warming from arms-down and then jumping to
            // overhead in a single frame is a 1.1 m wrist teleport, which the velocity clamp correctly
            // catches — that measured 0.595 m of clamping and is the clamp working, not a truncation.
            // The SWEEP into this pose is covered by NoLagOnRealMotion, which reaches 180 deg smoothly.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 180f, 0f);
            SyntheticPose.Fill(input, 41 * Dt, 180f, 0f, 0f, Conf);
            PoseFrame outFrame = layer.Process(input, Dt);
            float wrist = Vector3.Distance(outFrame.GetLandmark(JointId.LeftWrist).Position,
                                           input.GetLandmark(JointId.LeftWrist).Position);
            float elbow = Vector3.Distance(outFrame.GetLandmark(JointId.LeftElbow).Position,
                                           input.GetLandmark(JointId.LeftElbow).Position);
            CheckAtMost("an arm straight overhead is not truncated (wrist)", wrist, 0.002f, "m");
            CheckAtMost("  nor is its elbow pulled down", elbow, 0.002f, "m");
            Check("  and no cone fired on it", layer.Stats.ConeClamped == 0L,
                  "cone=" + layer.Stats.ConeClamped + " - a shoulder genuinely reaches 180 deg");
        }

        private static void ImpossibleElbowFoldIsOpenedUp() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 90f, 30f);
            SyntheticPose.Fill(input, 41 * Dt, 90f, 30f, 0f, Conf);
            SyntheticPose.FoldElbow(input, 11, 13, 15);
            float rawAngle = PoseMetrics.InteriorAngle(input, 11, 13, 15);
            PoseFrame outFrame = layer.Process(input, Dt);
            float outAngle = PoseMetrics.InteriorAngle(outFrame, 11, 13, 15);
            CheckAtMost("  the injected fold really was impossible", rawAngle,
                        HumanBodyModel.ElbowMinDeg - 1f, " deg");
            CheckAtLeast("an elbow folded through itself is opened to the limit", outAngle,
                         HumanBodyModel.ElbowMinDeg - 1.5f, " deg");
        }

        private static void KneeCannotBendForwards() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 20f, 0f);
            SyntheticPose.Fill(input, 41 * Dt, 20f, 0f, 40f, Conf);
            SyntheticPose.BendKneeWrongWay(input, 25, 27, 50f);
            PoseFrame outFrame = layer.Process(input, Dt);
            // Forward is +Z in this synthetic frame (right = +X from the hip line, up = +Y).
            Vector3 hip = outFrame.GetLandmark(JointId.LeftHip).Position;
            Vector3 knee = outFrame.GetLandmark(JointId.LeftKnee).Position;
            Vector3 ankle = outFrame.GetLandmark(JointId.LeftAnkle).Position;
            Vector3 u = (ankle - hip).normalized;
            Vector3 n = Vector3.forward - u * Vector3.Dot(Vector3.forward, u);
            float offset = n.sqrMagnitude > 1e-8f ? Vector3.Dot(knee - hip, n.normalized) : 0f;
            CheckAtLeast("a knee bending forwards is reflected back", offset, -0.011f, "m");
            CheckNear("  the thigh length is preserved exactly by the reflection",
                      Vector3.Distance(hip, knee), SyntheticPose.Thigh, 0.01f, "m");
            CheckNear("  and so is the shin",
                      Vector3.Distance(knee, ankle), SyntheticPose.Shin, 0.01f, "m");
        }

        private static void TorsoCannotTwistPastASpine() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 20f, 0f);
            SyntheticPose.Fill(input, 41 * Dt, 20f, 0f, 0f, Conf);
            SyntheticPose.TwistTorso(input, 150f);
            PoseFrame outFrame = layer.Process(input, Dt);
            Vector3 shoulderLine = outFrame.GetLandmark(JointId.LeftShoulder).Position
                                 - outFrame.GetLandmark(JointId.RightShoulder).Position;
            Vector3 hipLine = outFrame.GetLandmark(JointId.LeftHip).Position
                            - outFrame.GetLandmark(JointId.RightHip).Position;
            shoulderLine.y = 0f;
            hipLine.y = 0f;
            float twist = Mathf.Abs(Vector3.SignedAngle(hipLine.normalized, shoulderLine.normalized, Vector3.up));
            CheckAtMost("150 deg of shoulder twist is brought back inside a spine", twist,
                        HumanBodyModel.TorsoTwistDeg + 2f, " deg");
        }

        private static void RecoveryDoesNotSnap() {
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            // Lose the wrist for half a second while the arm keeps moving, then give it back.
            int i = 0;
            while (i < 15) {
                SyntheticPose.Fill(input, (41 + i) * Dt, 45f + i * 3f, 20f, 0f, Conf);
                SyntheticPose.Drop(input, 15);
                layer.Process(input, Dt);
                i = i + 1;
            }
            PoseFrame previous = new PoseFrame();
            SyntheticPose.CopyInto(layer.Process(input, Dt), previous);

            float worstStep = 0f;
            int recoveryFrames = 0;
            float finalError = 0f;
            i = 0;
            while (i < 12) {
                SyntheticPose.Fill(input, (56 + i) * Dt, 90f, 20f, 0f, Conf);
                PoseFrame outFrame = layer.Process(input, Dt);
                float step = Vector3.Distance(outFrame.GetLandmark(JointId.LeftWrist).Position,
                                              previous.GetLandmark(JointId.LeftWrist).Position);
                if (step > worstStep) {
                    worstStep = step;
                }
                finalError = Vector3.Distance(outFrame.GetLandmark(JointId.LeftWrist).Position,
                                              input.GetLandmark(JointId.LeftWrist).Position);
                SyntheticPose.CopyInto(outFrame, previous);
                recoveryFrames = recoveryFrames + 1;
                i = i + 1;
            }
            CheckAtMost("a returning joint eases in instead of snapping", worstStep,
                        HumanBodyModel.MaxSpeed(HumanBodyModel.JointClass.Extremity) * Dt * 1.2f, "m");
            CheckAtMost("  and reaches the true position within the blend window", finalError, 0.01f, "m");
            CheckAtLeast("  the blend was actually entered", layer.Stats.RecoveringJointFrames, 1f, " frames");
        }

        private static void WholeBodyLossIsNotFabricated() {
            // No person tracked is not the same as a person standing still. Holding a whole body
            // indefinitely IS the freeze F-20A's failsafe exists to break, so this layer must not do it.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            input.MarkInvalid(41 * Dt);
            PoseFrame outFrame = layer.Process(input, Dt);
            Check("an invalid frame stays invalid", !outFrame.IsValid,
                  "holding a whole body indefinitely is the freeze F-20A exists to break");
        }

        private static void StagesCanBeDisabledIndependently() {
            // A correction whose contribution cannot be isolated cannot be shown to help.
            PoseFrame input;
            HumanizedSkeleton layer = Warmed(out input, 45f, 20f);
            layer.SetStages(true, false, true);
            SyntheticPose.Fill(input, 41 * Dt, 45f, 20f, 0f, Conf);
            SyntheticPose.Stretch(input, 13, 15, 1.5f);
            PoseFrame outFrame = layer.Process(input, Dt);
            float stretched = PoseMetrics.BoneLength(outFrame, 13, 15);
            layer.SetStages(true, true, true);
            Check("bone-length enforcement can be switched off for an A/B",
                  stretched > SyntheticPose.Forearm * 1.3f,
                  "forearm=" + stretched.ToString("F3", CultureInfo.InvariantCulture) + "m with the stage off");
        }
    }
}
