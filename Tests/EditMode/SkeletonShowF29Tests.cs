using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-29 unit tests: the trust channel's consumer side, the foot/floor estimator, hand gestures,
    /// the presence gate and the attract figure.
    ///
    /// SCOPE, stated because it bounds what a green run here proves. Everything covered is pure
    /// managed logic that can run without a player. The parts NOT covered here are covered elsewhere
    /// and deliberately: the wire format end-to-end (the F-29 headless harness, which drives the real
    /// <c>OakDUdpPoseProvider</c> over a real socket with recorded packets), and the renderers, which
    /// need an Editor.
    ///
    /// Every threshold asserted below is a MEASURED value from the two regression clips, not a
    /// preference. Where a number here disagrees with the code, the measurement in
    /// <c>docs/F29_TRUST_HANDS_FEET_ATTRACT_2026-09-16.md</c> settles it.
    /// </summary>
    public sealed class SkeletonShowF29Tests {
        private static readonly Vector3 Origin = new Vector3(0f, 0.95f, 0f);

        // ---------------------------------------------------------------- presence gate

        [Test]
        public void PresenceGate_DoesNotGoLiveBeforeTheEnterDelay() {
            PresenceGate gate = new PresenceGate();
            Step(gate, true, PresenceGate.EnterSeconds * 0.5f);
            Assert.IsFalse(gate.Present, "half the enter delay must not trigger");
        }

        [Test]
        public void PresenceGate_GoesLiveAfterTheEnterDelay() {
            PresenceGate gate = new PresenceGate();
            Step(gate, true, PresenceGate.EnterSeconds + 0.1f);
            Assert.IsTrue(gate.Present);
        }

        [Test]
        public void PresenceGate_SurvivesADropoutShorterThanTheLeaveDelay() {
            PresenceGate gate = new PresenceGate();
            Step(gate, true, PresenceGate.EnterSeconds + 0.1f);
            Step(gate, false, PresenceGate.LeaveSeconds - 0.5f);
            // The single most important property here: an occlusion, a turn, or F-21 briefly losing
            // its lock must NOT dump a present visitor back to the attract loop mid-interaction.
            Assert.IsTrue(gate.Present, "a dropout shorter than the leave delay must be survived");
        }

        [Test]
        public void PresenceGate_ReleasesAfterTheLeaveDelay() {
            PresenceGate gate = new PresenceGate();
            Step(gate, true, PresenceGate.EnterSeconds + 0.1f);
            Step(gate, false, PresenceGate.LeaveSeconds + 0.2f);
            Assert.IsFalse(gate.Present);
        }

        [Test]
        public void PresenceGate_LeaveDelayExceedsTheProviderStaleFailsafe() {
            // The tracking layer already owns a 2 s failsafe. If this gate fired first it would be a
            // second, competing definition of "gone" - and the faster one always wins, which would
            // make the tracking layer's own handling unreachable.
            Assert.Greater(PresenceGate.LeaveSeconds, 2.0f);
        }

        private static void Step(PresenceGate gate, bool tracked, float seconds) {
            int steps = Mathf.CeilToInt(seconds / 0.02f);
            for (int i = 0; i < steps; i++) {
                gate.Update(tracked, 0.02f);
            }
        }

        // ---------------------------------------------------------------- floor and feet

        [Test]
        public void Floor_TracksTheFootContactsNotTheAnkles() {
            // Ankles 120 mm above the heels/toes, as a real body has them. The floor must follow the
            // contact points: an ankle-based floor was measured 81-127 mm too high on the clips, and
            // that is exactly the offset every floor effect was carrying.
            SkeletonPose pose = new SkeletonPose();
            PoseFrame frame = StandingFrame(ankleY: -0.88f, footY: -1.0f);
            for (int i = 0; i < 240; i++) {
                pose.Fill(frame, Origin, 1f, 1f / 60f);
            }
            Assert.IsTrue(pose.HasFloor);
            float expected = Origin.y + 1.0f * -1f;   // flipY is applied by the provider, not here
            Assert.AreEqual(Origin.y - 1.0f, pose.FloorY, 0.02f,
                "floor should sit at the foot contacts, not 120 mm up at the ankles");
            Assert.AreEqual(expected, pose.FloorY, 0.02f);
        }

        [Test]
        public void Floor_FallsFastAndRisesSlowly() {
            // The floor is where the lowest foot has RECENTLY been. A foot leaving the ground must not
            // drag the floor up with it, or every footstep effect would chase the foot into the air.
            SkeletonPose pose = new SkeletonPose();
            PoseFrame low = StandingFrame(-0.88f, -1.0f);
            for (int i = 0; i < 240; i++) {
                pose.Fill(low, Origin, 1f, 1f / 60f);
            }
            float settled = pose.FloorY;

            PoseFrame lifted = StandingFrame(-0.68f, -0.8f);   // both feet 200 mm up
            for (int i = 0; i < 6; i++) {                       // 0.1 s
                pose.Fill(lifted, Origin, 1f, 1f / 60f);
            }
            Assert.Less(pose.FloorY - settled, 0.06f,
                "0.1 s of both feet lifted must barely move the floor");

            PoseFrame dropped = StandingFrame(-1.08f, -1.2f);   // floor genuinely lower
            for (int i = 0; i < 12; i++) {                      // 0.2 s
                pose.Fill(dropped, Origin, 1f, 1f / 60f);
            }
            Assert.Less(pose.FloorY, settled - 0.15f,
                "a genuinely lower contact must be adopted almost at once");
        }

        [Test]
        public void Floor_SurvivesTheSubjectLeavingFrame() {
            SkeletonPose pose = new SkeletonPose();
            PoseFrame frame = StandingFrame(-0.88f, -1.0f);
            for (int i = 0; i < 240; i++) {
                pose.Fill(frame, Origin, 1f, 1f / 60f);
            }
            float before = pose.FloorY;
            for (int i = 0; i < 120; i++) {
                pose.Fill(null, Origin, 1f, 1f / 60f);
            }
            // The floor is a property of the ROOM. Re-estimating it on every re-acquisition would
            // make floor effects jump each time somebody walks back into frame.
            Assert.AreEqual(before, pose.FloorY, 1e-4f, "the floor must outlive the body");
            foreach (bool planted in pose.Planted) {
                Assert.IsFalse(planted, "nothing can be planted when there is no body");
            }
        }

        [Test]
        public void Planted_RequiresBothProximityAndStillness() {
            SkeletonPose pose = new SkeletonPose();
            PoseFrame frame = StandingFrame(-0.88f, -1.0f);
            for (int i = 0; i < 240; i++) {
                pose.Fill(frame, Origin, 1f, 1f / 60f);
            }
            Assert.IsTrue(pose.Planted[0], "a still foot on the floor is planted");

            // Same height, but sweeping sideways each frame: a foot crossing the floor band at speed
            // is mid-stride, not planted.
            //
            // F-34 CHANGED THE INPUT HERE, not the assertion. This used to step 0.4 m per 60 Hz frame,
            // which is 24 m/s - three times a sprinter's hip and not a movement a body can make. The
            // F-34 identity guard now recognises that as the frame of reference moving rather than the
            // person, and throws the history away (see SkeletonPose.TeleportSpeed). 0.1 m per frame is
            // 6 m/s: a genuinely fast foot, far above the 0.35 m/s planted gate and far below the
            // 12 m/s guard, so this tests what it always meant to test.
            for (int i = 0; i < 20; i++) {
                float x = (i % 2 == 0) ? 0f : 0.1f;
                pose.Fill(StandingFrame(-0.88f, -1.0f, x), Origin, 1f, 1f / 60f);
            }
            Assert.Greater(pose.Speed[(int)JointId.LeftHeel], SkeletonPose.PlantedMaxSpeed,
                           "the foot really is moving faster than the planted gate");
            Assert.IsFalse(pose.Planted[0], "a fast foot must not read as planted");
        }

        [Test]
        public void Planted_IsNotFooledByTheHistoryBeingDiscarded() {
            // The other side of the same coin, pinned because it is new behaviour: when the guard DOES
            // fire, speed goes to zero, and a zero speed is exactly what the planted test looks for.
            // A foot that is genuinely off the floor must still not read as planted, whatever the
            // guard did to the history.
            SkeletonPose pose = new SkeletonPose();
            for (int i = 0; i < 240; i++) {
                pose.Fill(StandingFrame(-0.88f, -1.0f), Origin, 1f, 1f / 60f);
            }
            Assert.IsTrue(pose.Planted[0]);

            // A metre sideways in one frame at 60 Hz is 60 m/s - a teleport, so the guard fires and
            // the history is discarded. The feet are also 300 mm up, which is well outside the 80 mm
            // planted band, and proximity is not something the guard can affect.
            pose.Fill(StandingFrame(-0.58f, -0.7f, 1.0f), Origin, 1f, 1f / 60f);
            Assert.IsFalse(pose.Planted[0], "a lifted foot is not planted even with no speed history");
        }

        [Test]
        public void CameraDepth_IsZeroWithoutAMeasuredRoot() {
            // No measured mid-hip means no distance to the subject. Reporting zero is the honest
            // answer; reporting a plausible-looking number would be a fabrication in a HUD whose
            // entire purpose is to say what is and is not known.
            SkeletonPose pose = new SkeletonPose();
            PoseFrame frame = StandingFrame(-0.88f, -1.0f);
            frame.ClearRootPosition();
            pose.Fill(frame, Origin, 1f, 1f / 60f);
            Assert.AreEqual(0f, pose.CameraDepth((int)JointId.LeftHip), 1e-6f);
        }

        [Test]
        public void CameraDepth_AddsTheHipRelativeLandmarkToTheMeasuredRoot() {
            SkeletonPose pose = new SkeletonPose();
            PoseFrame frame = StandingFrame(-0.88f, -1.0f);
            // Wrist 0.3 m nearer the camera than the hips, hips measured at 2.0 m.
            frame.SetLandmark((int)JointId.LeftWrist, new PoseLandmark(new Vector3(0.2f, 0f, -0.3f), 0.9f));
            frame.SetRootPosition(new Vector3(0f, 0f, 2.0f));
            pose.Fill(frame, Origin, 1f, 1f / 60f);
            Assert.AreEqual(1.7f, pose.CameraDepth((int)JointId.LeftWrist), 1e-3f);
            Assert.AreEqual(2.0f, pose.CameraDepth((int)JointId.LeftHip), 1e-3f);
        }

        [Test]
        public void Bones_IncludeTheHeels() {
            // F-28 drew ankle-to-toe only, so the body stood on its toe-tips. Each foot is now a
            // triangle, which is what gives it a direction on the floor.
            Assert.IsTrue(HasBone(27, 29), "left ankle to heel");
            Assert.IsTrue(HasBone(29, 31), "left heel to toe");
            Assert.IsTrue(HasBone(28, 30), "right ankle to heel");
            Assert.IsTrue(HasBone(30, 32), "right heel to toe");
        }

        private static bool HasBone(int a, int b) {
            for (int i = 0; i < SkeletonPose.Bones.Length; i += 2) {
                if ((SkeletonPose.Bones[i] == a && SkeletonPose.Bones[i + 1] == b)
                    || (SkeletonPose.Bones[i] == b && SkeletonPose.Bones[i + 1] == a)) {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- hands

        [Test]
        public void Hand_ImplausiblePalmIsRefusedNotDrawn() {
            // The 2.9 m failure mode: the model reports a 360 mm palm. Rendering it confidently
            // invites a viewer to conclude the tracking is broken in some interesting way, when the
            // truth is simply that the subject is out of range.
            HandPose hand = Fill(BuildHand(palm: 0.36f, tipGap: 0.05f), 1f);
            Assert.IsFalse(hand.Valid);
            Assert.IsTrue(hand.Implausible, "an impossible hand must be reported, not silently dropped");
        }

        [Test]
        public void Hand_PlausiblePalmIsAccepted() {
            HandPose hand = Fill(BuildHand(palm: 0.09f, tipGap: 0.05f), 1f);
            Assert.IsTrue(hand.Valid);
            Assert.IsFalse(hand.Implausible);
        }

        [Test]
        public void Hand_TouchingFingertipsReadAsAPinch() {
            HandPose hand = Fill(BuildHand(palm: 0.09f, tipGap: 0.008f), 1f);
            Assert.IsTrue(hand.Valid);
            Assert.IsTrue(hand.Pinching);
        }

        [Test]
        public void Hand_OpenFingertipsDoNotReadAsAPinch() {
            // 0.52 palms is the measured MEDIAN of an ordinary non-pinching hand.
            HandPose hand = Fill(BuildHand(palm: 0.09f, tipGap: 0.09f * 0.52f), 1f);
            Assert.IsTrue(hand.Valid);
            Assert.IsFalse(hand.Pinching);
        }

        [Test]
        public void Hand_PinchHasHysteresis() {
            // Sitting exactly between the enter and exit thresholds must HOLD the current state.
            // Without this a hand resting near the threshold strobes the pinch every frame, which on
            // screen looks like the tracking failing rather than a gesture being held.
            HandPose hand = new HandPose();
            RawHandFrame closed = BuildHand(0.09f, 0.008f);
            hand.Fill(closed, false, Vector3.zero, 1f);
            Assert.IsTrue(hand.Pinching);

            RawHandFrame between = BuildHand(0.09f, 0.09f * 0.42f);   // between 0.35 and 0.48
            hand.Fill(between, false, Vector3.zero, 1f);
            Assert.IsTrue(hand.Pinching, "must stay pinched inside the hysteresis band");

            RawHandFrame open = BuildHand(0.09f, 0.09f * 0.60f);
            hand.Fill(open, false, Vector3.zero, 1f);
            Assert.IsFalse(hand.Pinching);
            hand.Fill(between, false, Vector3.zero, 1f);
            Assert.IsFalse(hand.Pinching, "must stay open inside the hysteresis band");
        }

        [Test]
        public void Hand_GestureVerdictIsIndependentOfSceneScale() {
            // Hand landmark depth inherits the body's scale error, so an absolute-millimetre
            // threshold would fire on subject distance rather than on intent. Staging the show at
            // half size must likewise not change what a gesture means.
            RawHandFrame raw = BuildHand(0.09f, 0.008f);
            HandPose full = Fill(raw, 1f);
            HandPose half = Fill(raw, 0.5f);
            Assert.AreEqual(full.PinchNormalised, half.PinchNormalised, 1e-3f);
            Assert.AreEqual(full.PalmMetres, half.PalmMetres, 1e-3f);
            Assert.AreEqual(full.Pinching, half.Pinching);
        }

        [Test]
        public void Hand_UntrackedIsNeitherValidNorImplausible() {
            // Three distinct states, and a HUD must be able to tell them apart: tracked, out of
            // range, and simply not sent (the sidecar omits a hand below its confidence gate).
            HandPose hand = new HandPose();
            hand.Fill(null, true, Vector3.zero, 1f);
            Assert.IsFalse(hand.Valid);
            Assert.IsFalse(hand.Implausible);
        }

        private static HandPose Fill(RawHandFrame raw, float scale) {
            HandPose hand = new HandPose();
            hand.Fill(raw, false, Vector3.zero, scale);
            return hand;
        }

        // A hand with a chosen palm length and thumb-index gap. Knuckles are laid out across the palm
        // so the orientation basis is well formed.
        private static RawHandFrame BuildHand(float palm, float tipGap) {
            Vector3[] p = new Vector3[RawHandFrame.LandmarkCount];
            p[0] = Vector3.zero;                                  // wrist
            p[9] = new Vector3(0f, palm, 0f);                     // middle MCP - defines palm length
            p[5] = new Vector3(-0.3f * palm, palm * 0.95f, 0f);   // index MCP
            p[17] = new Vector3(0.3f * palm, palm * 0.9f, 0f);    // little MCP
            p[2] = new Vector3(-0.35f * palm, palm * 0.4f, 0f);   // thumb MCP
            p[13] = new Vector3(0.18f * palm, palm * 0.93f, 0f);  // ring MCP
            p[4] = new Vector3(-0.5f * palm, palm * 1.1f, 0f);    // thumb tip
            p[8] = p[4] + new Vector3(tipGap, 0f, 0f);            // index tip, `tipGap` away
            p[12] = new Vector3(0f, palm * 1.9f, 0f);
            p[16] = new Vector3(0.18f * palm, palm * 1.8f, 0f);
            p[20] = new Vector3(0.3f * palm, palm * 1.7f, 0f);
            RawHandFrame raw = new RawHandFrame();
            raw.SetHand(false, p);
            raw.SetMeta(0.0);
            return raw;
        }

        // ---------------------------------------------------------------- attract

        [Test]
        public void Attract_LeavesUnmodelledJointsUnobserved() {
            // Emitting an unmodelled slot at the origin with confidence is the exact fault the F-27
            // humanized layer exists to reject. The attract loop must not be the thing that produces
            // it.
            AttractSkeleton attract = new AttractSkeleton();
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(attract.Tick(1f / 60f), Origin, 1f, 1f / 60f);
            int present = 0;
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                if (pose.Present[i]) {
                    present++;
                }
            }
            Assert.AreEqual(17, present, "attract models 17 joints and claims no others");
        }

        [Test]
        public void Attract_CarriesNoMeasuredRoot() {
            // There is no body, so there is no distance to it. This is what stops the trust HUD
            // printing a confident depth for a figure that does not exist.
            AttractSkeleton attract = new AttractSkeleton();
            PoseFrame frame = attract.Tick(1f / 60f);
            Assert.IsFalse(frame.HasRootPosition);
        }

        [Test]
        public void Attract_ActuallyMoves() {
            AttractSkeleton attract = new AttractSkeleton();
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(attract.Tick(1f / 60f), Origin, 1f, 1f / 60f);
            Vector3 first = pose.World[(int)JointId.LeftWrist];
            float travel = 0f;
            for (int i = 0; i < 600; i++) {
                pose.Fill(attract.Tick(1f / 60f), Origin, 1f, 1f / 60f);
                travel = Mathf.Max(travel, Vector3.Distance(first, pose.World[(int)JointId.LeftWrist]));
            }
            Assert.Greater(travel, 0.15f, "an attract loop that does not move is a still image");
        }

        [Test]
        public void Attract_StaysAtHumanProportions() {
            // It must read as a person from across a room, and it must never be mistakable for a
            // tracked one - so it is checked for plausibility, and labelled in the HUD.
            AttractSkeleton attract = new AttractSkeleton();
            SkeletonPose pose = new SkeletonPose();
            for (int i = 0; i < 600; i++) {
                pose.Fill(attract.Tick(1f / 60f), Origin, 1f, 1f / 60f);
                float head = pose.World[(int)JointId.Nose].y;
                Assert.That(head, Is.InRange(1.4f, 1.9f), "head height must stay human");
                Assert.IsTrue(pose.HasFloor);
            }
        }

        // ---------------------------------------------------------------- helpers

        // A standing body: hips at the origin, ankles and foot contacts at the given heights. Y is
        // already Unity-space (up) here, so the values are negative below the hips.
        private static PoseFrame StandingFrame(float ankleY, float footY, float x = 0f) {
            PoseFrame f = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                f.SetLandmark(i, new PoseLandmark(Vector3.zero, 0f));
            }
            Set(f, JointId.LeftHip, x - 0.09f, 0f);
            Set(f, JointId.RightHip, x + 0.09f, 0f);
            Set(f, JointId.LeftShoulder, x - 0.18f, 0.5f);
            Set(f, JointId.RightShoulder, x + 0.18f, 0.5f);
            Set(f, JointId.LeftAnkle, x - 0.1f, ankleY);
            Set(f, JointId.RightAnkle, x + 0.1f, ankleY);
            Set(f, JointId.LeftHeel, x - 0.1f, footY);
            Set(f, JointId.RightHeel, x + 0.1f, footY);
            Set(f, JointId.LeftFootIndex, x - 0.1f, footY);
            Set(f, JointId.RightFootIndex, x + 0.1f, footY);
            f.SetMeta(0.0, true);
            f.SetRootPosition(new Vector3(0f, 0f, 2.0f));
            return f;
        }

        private static void Set(PoseFrame f, JointId joint, float x, float y) {
            f.SetLandmark((int)joint, new PoseLandmark(new Vector3(x, y, 0f), 0.9f));
        }
    }
}
