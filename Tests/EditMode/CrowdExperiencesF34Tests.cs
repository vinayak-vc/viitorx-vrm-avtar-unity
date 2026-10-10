using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Experiences;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-34 unit tests: the crowd experiences' shared machinery, and the one claim every one of them
    /// rests on.
    ///
    /// THE CLAIM, stated once so the assertions below are readable as a group: an identity switch is
    /// NOT a jump anybody can catch. The live two-person session measured an identity migrating
    /// between two humans at 0.015 m per frame against a 0.35 m margin — 97 datagrams, zero events
    /// logged. There is no discontinuity to detect and therefore no guard to write. An experience
    /// survives a switch only if it is SYMMETRIC IN ITS PARTICIPANTS and reads only the current
    /// frame. These tests assert that symmetry directly, on the functions the scenes actually call,
    /// so it is checked rather than argued for in a comment.
    ///
    /// SCOPE, stated because it bounds what a green run here proves. Everything covered is pure
    /// managed logic that runs without a player. The renderers, the scene wiring and the audio bank
    /// need an Editor or a play session and are NOT covered; the play-mode checks are listed in
    /// docs/F34_CROWD_EXPERIENCES_2026-09-17.md.
    /// </summary>
    public sealed class CrowdExperiencesF34Tests {
        private static readonly Vector3 Origin = new Vector3(0f, 0.95f, 0f);

        // ---------------------------------------------------------------- CrowdPalette

        [Test]
        public void Palette_KeepsAPersonsColourAcrossFrames() {
            CrowdPalette palette = new CrowdPalette();
            Color first = palette.ColourFor(7);
            palette.ColourFor(8);
            palette.ColourFor(9);
            Assert.AreEqual(first, palette.ColourFor(7),
                            "a person must not change colour because other people arrived");
        }

        [Test]
        public void Palette_GivesEveryoneADifferentColourUpToTheTableSize() {
            CrowdPalette palette = new CrowdPalette();
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < CrowdPalette.Colours.Length; i++) {
                Assert.IsTrue(used.Add(palette.IndexFor(100 + i)),
                              "two people inside the table size shared a colour");
            }
        }

        [Test]
        public void Palette_ForgetsOldVisitorsRatherThanGrowingForever() {
            CrowdPalette palette = new CrowdPalette();
            for (int i = 0; i < 500; i++) {
                palette.ColourFor(i);
            }
            // Ids are never reused, so everything the table is holding beyond the cap can only be
            // people who have gone. An installation left running all day must not accumulate one
            // entry per visitor.
            Assert.LessOrEqual(palette.Remembered, 64);
        }

        // ---------------------------------------------------------------- CrowdRoster

        [Test]
        public void Roster_ReportsAnArrivalOnceAndNotAgain() {
            CrowdRoster roster = new CrowdRoster();
            roster.Update(Crowd(1, 2));
            Assert.AreEqual(2, roster.Arrived.Count);
            roster.Update(Crowd(1, 2));
            Assert.AreEqual(0, roster.Arrived.Count, "a person already present is not an arrival");
        }

        [Test]
        public void Roster_ReportsADeparture() {
            CrowdRoster roster = new CrowdRoster();
            roster.Update(Crowd(1, 2));
            roster.Update(Crowd(1));
            Assert.AreEqual(1, roster.Left.Count);
            Assert.AreEqual(2, roster.Left[0]);
            Assert.IsFalse(roster.Contains(2));
        }

        [Test]
        public void Roster_IsUnmovedByTheCrowdBeingReSorted() {
            // THE reason this exists. TrackedStage publishes the crowd most-established-first and
            // re-sorts it every frame, so anything keyed on list POSITION sees people arriving and
            // leaving constantly. Keyed on the id, a re-sort is a no-op.
            CrowdRoster roster = new CrowdRoster();
            roster.Update(Crowd(4, 5, 6));
            roster.Update(Crowd(6, 4, 5));
            Assert.AreEqual(0, roster.Arrived.Count, "a re-sort must not look like an arrival");
            Assert.AreEqual(0, roster.Left.Count, "a re-sort must not look like a departure");
            Assert.AreEqual(3, roster.Count);
        }

        // ---------------------------------------------------------------- SkeletonPose history

        [Test]
        public void ResetHistory_ClearsSpeedButKeepsTheFloor() {
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(StandingFrame(-0.88f, -1.0f, 0f), Origin, 1f, 1f / 30f);
            pose.Fill(StandingFrame(-0.88f, -1.0f, 0.3f), Origin, 1f, 1f / 30f);
            Assert.Greater(pose.Speed[(int)JointId.LeftHip], 0f, "the body did move");
            Assert.IsTrue(pose.HasFloor);
            float floor = pose.FloorY;

            pose.ResetHistory();

            Assert.AreEqual(0f, pose.Speed[(int)JointId.LeftHip], 1e-6f);
            Assert.AreEqual(0f, pose.Energy, 1e-6f);
            // The floor is a property of the ROOM. Re-estimating it would make every floor effect
            // jump at exactly the moment the guard fired, which is the symptom this prevents.
            Assert.IsTrue(pose.HasFloor);
            Assert.AreEqual(floor, pose.FloorY, 1e-6f);
        }

        [Test]
        public void TeleportGuard_SuppressesTheSpikeFromAGroundingSnap() {
            // TrackedStage snaps its ground offset the first time it has a floor, which can move the
            // staging origin by the better part of a metre in one frame. Without the guard the next
            // frame differences two positions measured against different origins and reports a spike
            // on the WHOLE body - above every strike gate in the experiences at once.
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(StandingFrame(-0.88f, -1.0f, 0f), Origin, 1f, 1f / 30f);
            pose.Fill(StandingFrame(-0.88f, -1.0f, 0f), Origin + new Vector3(0f, 1.0f, 0f), 1f,
                      1f / 30f);
            Assert.Less(pose.Speed[(int)JointId.LeftWrist], ExperienceTuning.TouchSpeed,
                        "an origin jump must not read as the body moving");
        }

        [Test]
        public void TeleportGuard_LeavesOrdinaryMovementAlone() {
            // A fast but human step: 0.3 m in a 30 Hz frame is 9 m/s at the hip, just under the guard.
            // The guard must not be so tight that it eats real movement - that would silently zero the
            // speed the experiences are built on.
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(StandingFrame(-0.88f, -1.0f, 0f), Origin, 1f, 1f / 30f);
            pose.Fill(StandingFrame(-0.88f, -1.0f, 0.3f), Origin, 1f, 1f / 30f);
            Assert.Greater(pose.Speed[(int)JointId.LeftHip], ExperienceTuning.StillSpeed);
        }

        [Test]
        public void TeleportSpeed_IsAboveAnythingAPersonCanDo() {
            // A sprinter's hip travels at about 8 m/s. Anything faster is the frame of reference
            // moving, not a body.
            Assert.Greater(SkeletonPose.TeleportSpeed, 8f);
        }

        // ---------------------------------------------------------------- tuning

        [Test]
        public void StillEnergy_IsTheStillSpeedOnTheEnergyScale() {
            // Derived, not chosen: "still" has to mean the same thing to a foot and to a whole body,
            // or Podium and Stillness disagree with Footprints about who is standing still.
            Assert.AreEqual(ExperienceTuning.StillSpeed / SkeletonPose.FastSpeed,
                            ExperienceTuning.StillEnergy, 1e-6f);
        }

        [Test]
        public void ThresholdsStillMatchTheValuesTheyWereTunedAt() {
            // These are the numbers the shipped scenes were tuned with at ~21 fps. The test exists so
            // that a re-measurement at ~16 fps is a deliberate, visible change rather than a drift.
            Assert.AreEqual(0.9f, ExperienceTuning.StrikeSpeed, 1e-6f);
            Assert.AreEqual(0.55f, ExperienceTuning.TouchSpeed, 1e-6f);
            Assert.AreEqual(0.35f, ExperienceTuning.StillSpeed, 1e-6f);
        }

        // ---------------------------------------------------------------- FluidField

        [Test]
        public void Fluid_InjectionIsSymmetricInThePeople() {
            // THE property Collective rests on. A velocity field integrates injections with `+=`, so
            // injecting A then B must leave the grid exactly as injecting B then A would. No id is
            // read anywhere, so there is no identity in the field to corrupt.
            SkeletonPose a = MovingPose(0.4f, 0f);
            SkeletonPose b = MovingPose(-0.5f, 0.3f);

            FluidField first = new FluidField();
            first.Allocate(16, 3.2f, 0);
            first.Inject(a, 1f / 30f);
            first.Inject(b, 1f / 30f);

            FluidField second = new FluidField();
            second.Allocate(16, 3.2f, 0);
            second.Inject(b, 1f / 30f);
            second.Inject(a, 1f / 30f);

            for (int z = 0; z < 16; z++) {
                for (int x = 0; x < 16; x++) {
                    Assert.AreEqual(first.At(x, z).x, second.At(x, z).x, 1e-5f,
                                    "cell " + x + "," + z + " depends on injection order");
                    Assert.AreEqual(first.At(x, z).y, second.At(x, z).y, 1e-5f,
                                    "cell " + x + "," + z + " depends on injection order");
                }
            }
        }

        [Test]
        public void Fluid_MorePeopleStirItMore() {
            SkeletonPose a = MovingPose(0.4f, 0f);
            SkeletonPose b = MovingPose(0.45f, 0.02f);

            FluidField alone = new FluidField();
            alone.Allocate(16, 3.2f, 0);
            alone.Inject(a, 1f / 30f);

            FluidField together = new FluidField();
            together.Allocate(16, 3.2f, 0);
            together.Inject(a, 1f / 30f);
            together.Inject(b, 1f / 30f);

            Assert.Greater(together.MeanEnergy, alone.MeanEnergy,
                           "two people in the same place must make a bigger wake than one");
        }

        [Test]
        public void Fluid_DecaysTowardStillnessAndSamplesZeroOutsideTheGrid() {
            FluidField field = new FluidField();
            field.Allocate(16, 3.2f, 0);
            field.Inject(MovingPose(0f, 0f), 1f / 30f);
            float before = field.MeanEnergy;
            Assert.Greater(before, 0f);

            // Twenty seconds, which is about nine times the 2.2 s decay constant: one energetic
            // person must not leave the room spinning after they have gone.
            for (int i = 0; i < 600; i++) {
                field.Decay(1f / 30f);
            }
            Assert.Less(field.MeanEnergy, before * 0.01f);
            Assert.AreEqual(Vector2.zero, field.Sample(new Vector3(50f, 0f, 50f)));
        }

        // ---------------------------------------------------------------- FloorMarks

        [Test]
        public void Marks_ShiftingWeightGrowsTheMarkYouAreOnRatherThanStampingANewOne() {
            FloorMarks marks = new FloorMarks();
            marks.Grow(new Vector3(0f, 0f, 0f), 0.5f);
            marks.Grow(new Vector3(0.1f, 0f, 0.05f), 0.5f);   // inside the 0.22 m merge radius
            Assert.AreEqual(1, marks.Count);
            Assert.AreEqual(1.0f, marks.DwellAt(0), 1e-5f);
        }

        [Test]
        public void Marks_ADifferentPlaceIsADifferentMark() {
            FloorMarks marks = new FloorMarks();
            marks.Grow(new Vector3(0f, 0f, 0f), 0.5f);
            marks.Grow(new Vector3(1.2f, 0f, 0f), 0.5f);
            Assert.AreEqual(2, marks.Count);
        }

        [Test]
        public void Marks_CarryNoIdentity() {
            // Traces' whole safety argument: a mark is a PLACE. Two different people standing in the
            // same spot grow the SAME mark, because nothing about who was standing there is stored -
            // so an identity switch has nothing here to corrupt.
            FloorMarks marks = new FloorMarks();
            marks.Grow(new Vector3(0.5f, 0f, 0.5f), 1f);
            marks.Grow(new Vector3(0.5f, 0f, 0.5f), 1f);
            Assert.AreEqual(1, marks.Count);
            Assert.AreEqual(2f, marks.DwellAt(0), 1e-5f);
            Assert.AreEqual(2f, marks.TotalDwell, 1e-5f);
        }

        [Test]
        public void Marks_DropTheOldestRatherThanGrowingForever() {
            FloorMarks marks = new FloorMarks();
            marks.MaxMarks = 5;
            for (int i = 0; i < 40; i++) {
                marks.Grow(new Vector3(i * 1.0f, 0f, 0f), 0.1f);
            }
            Assert.AreEqual(5, marks.Count);
        }

        [Test]
        public void Marks_ReportEachPetalExactlyOnceSoTheSoundIsNotRetriggered() {
            FloorMarks marks = new FloorMarks();
            marks.SecondsToFullBloom = 9f;   // one petal per second, with Petals = 9
            int opened = 0;
            for (int i = 0; i < 90; i++) {
                if (marks.Grow(Vector3.zero, 0.1f) >= 0) {
                    opened = opened + 1;
                }
            }
            Assert.AreEqual(FloorMarks.Petals, opened);
        }

        // ---------------------------------------------------------------- CrowdMath

        [Test]
        public void Centroid_DoesNotCareWhoIsListedFirst() {
            // Stillness' safety argument, asserted. Relabelling two people cannot move the light.
            List<Vector3> points = new List<Vector3> {
                new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 2f),
            };
            List<float> weights = new List<float> { 0.2f, 0.9f, 0.4f };
            Vector3 straight = CrowdMath.WeightedCentroid(points, weights);

            List<Vector3> shuffled = new List<Vector3> { points[2], points[0], points[1] };
            List<float> reordered = new List<float> { weights[2], weights[0], weights[1] };
            Vector3 permuted = CrowdMath.WeightedCentroid(shuffled, reordered);

            Assert.AreEqual(straight.x, permuted.x, 1e-5f);
            Assert.AreEqual(straight.z, permuted.z, 1e-5f);
        }

        [Test]
        public void Centroid_LeansTowardTheHeavierPerson() {
            List<Vector3> points = new List<Vector3> {
                new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
            };
            Vector3 at = CrowdMath.WeightedCentroid(points, new List<float> { 0.1f, 0.9f });
            Assert.Greater(at.x, 0.5f, "the stillest person should draw the light to them");
        }

        [Test]
        public void Centroid_FallsBackToThePlainMiddleWhenNobodyIsStill() {
            // Everybody moving leaves every weight at effectively zero; dividing by that would throw
            // the light somewhere arbitrary and far away.
            List<Vector3> points = new List<Vector3> {
                new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f),
            };
            Vector3 at = CrowdMath.WeightedCentroid(points, new List<float> { 0f, 0f });
            Assert.AreEqual(0f, at.x, 1e-5f);
        }

        [Test]
        public void Similarity_IsSymmetricInThePair() {
            // Mirror Each Other's safety argument, asserted: exchanging the two people returns the
            // same number, so a switch cannot change the score.
            SkeletonPose a = PoseFromFrame(ArmsFrame(0.9f, 0.2f));
            SkeletonPose b = PoseFromFrame(ArmsFrame(0.7f, 0.4f));
            int[] scored = { 0, 13, 14, 15, 16, 25, 26, 27, 28 };
            int[] mirror = CrowdMath.BuildMirrorTable(PoseFrame.LandmarkCount);
            float ab = CrowdMath.Similarity(a, b, scored, mirror, false, 0.30f, null);
            float ba = CrowdMath.Similarity(b, a, scored, mirror, false, 0.30f, null);
            Assert.AreEqual(ab, ba, 1e-5f);
        }

        [Test]
        public void Similarity_ScoresAnIdenticalShapePerfectly() {
            SkeletonPose a = PoseFromFrame(ArmsFrame(0.8f, 0.3f));
            SkeletonPose b = PoseFromFrame(ArmsFrame(0.8f, 0.3f));
            int[] scored = { 0, 13, 14, 15, 16, 25, 26, 27, 28 };
            Assert.AreEqual(1f, CrowdMath.Similarity(a, b, scored, null, false, 0.30f, null), 1e-4f);
        }

        [Test]
        public void Similarity_JudgesShapeRatherThanSize() {
            // The reason Pose Match's torso normalisation was reused rather than replaced: a tall
            // adult and a child holding the same shape must score the same as two identical adults.
            SkeletonPose adult = PoseFromFrame(ArmsFrame(0.8f, 0.3f, 1f));
            SkeletonPose child = PoseFromFrame(ArmsFrame(0.8f, 0.3f, 0.6f));
            int[] scored = { 0, 13, 14, 15, 16, 25, 26, 27, 28 };
            Assert.AreEqual(1f, CrowdMath.Similarity(adult, child, scored, null, false, 0.30f, null),
                            1e-3f);
        }

        [Test]
        public void Similarity_RefusesToJudgeAPairItCanBarelySee() {
            SkeletonPose a = PoseFromFrame(ArmsFrame(0.8f, 0.3f));
            SkeletonPose b = PoseFromFrame(StandingFrame(-0.88f, -1.0f, 0f));   // no arms set
            int[] scored = { 0, 13, 14, 15, 16, 25, 26, 27, 28 };
            Assert.AreEqual(0f, CrowdMath.Similarity(a, b, scored, null, false, 0.30f, null), 1e-5f);
        }

        [Test]
        public void MirrorTable_PairsLeftWithRightAndLeavesTheSpineAlone() {
            int[] table = CrowdMath.BuildMirrorTable(PoseFrame.LandmarkCount);
            Assert.AreEqual((int)JointId.RightWrist, table[(int)JointId.LeftWrist]);
            Assert.AreEqual((int)JointId.LeftWrist, table[(int)JointId.RightWrist]);
            Assert.AreEqual((int)JointId.Nose, table[(int)JointId.Nose]);
        }

        [Test]
        public void Stillness_FallsAsAPersonMoves() {
            SkeletonPose still = PoseFromFrame(StandingFrame(-0.88f, -1.0f, 0f));
            SkeletonPose moving = MovingPose(0.5f, 0.2f);
            Assert.Greater(CrowdMath.Stillness(still, 3f), CrowdMath.Stillness(moving, 3f));
        }

        // ---------------------------------------------------------------- helpers

        private static IReadOnlyList<TrackedBody> Crowd(params int[] ids) {
            List<TrackedBody> bodies = new List<TrackedBody>();
            for (int i = 0; i < ids.Length; i++) {
                bodies.Add(new TrackedBody(ids[i], new SkeletonPose(), "CONFIRMED"));
            }
            return bodies;
        }

        private static SkeletonPose PoseFromFrame(PoseFrame frame) {
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(frame, Origin, 1f, 1f / 30f);
            return pose;
        }

        // A body that moved between two frames, so its joints carry a real Speed and Velocity.
        private static SkeletonPose MovingPose(float x, float z) {
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(WavingFrame(x, z, 0f), Origin, 1f, 1f / 30f);
            pose.Fill(WavingFrame(x, z, 0.06f), Origin, 1f, 1f / 30f);
            return pose;
        }

        // A standing body: hips at the origin, ankles and foot contacts at the given heights.
        private static PoseFrame StandingFrame(float ankleY, float footY, float x) {
            PoseFrame f = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                f.SetLandmark(i, new PoseLandmark(Vector3.zero, 0f));
            }
            Set(f, JointId.LeftHip, x - 0.09f, 0f, 0f);
            Set(f, JointId.RightHip, x + 0.09f, 0f, 0f);
            Set(f, JointId.LeftShoulder, x - 0.18f, 0.5f, 0f);
            Set(f, JointId.RightShoulder, x + 0.18f, 0.5f, 0f);
            Set(f, JointId.LeftAnkle, x - 0.1f, ankleY, 0f);
            Set(f, JointId.RightAnkle, x + 0.1f, ankleY, 0f);
            Set(f, JointId.LeftHeel, x - 0.1f, footY, 0f);
            Set(f, JointId.RightHeel, x + 0.1f, footY, 0f);
            Set(f, JointId.LeftFootIndex, x - 0.1f, footY, 0f);
            Set(f, JointId.RightFootIndex, x + 0.1f, footY, 0f);
            f.SetMeta(0.0, true);
            f.SetRootPosition(new Vector3(x, 0f, 2.0f));
            return f;
        }

        // A body with its arms and legs placed, scaled about the hip so the same SHAPE can be built
        // at two different sizes.
        private static PoseFrame ArmsFrame(float armY, float armOut, float scale = 1f) {
            PoseFrame f = StandingFrame(-0.88f * scale, -1.0f * scale, 0f);
            Set(f, JointId.LeftShoulder, -0.18f * scale, 0.5f * scale, 0f);
            Set(f, JointId.RightShoulder, 0.18f * scale, 0.5f * scale, 0f);
            Set(f, JointId.Nose, 0f, 0.78f * scale, 0f);
            Set(f, JointId.LeftElbow, -(0.3f + armOut) * scale, armY * 0.6f * scale, 0f);
            Set(f, JointId.RightElbow, (0.3f + armOut) * scale, armY * 0.6f * scale, 0f);
            Set(f, JointId.LeftWrist, -(0.5f + armOut) * scale, armY * scale, 0f);
            Set(f, JointId.RightWrist, (0.5f + armOut) * scale, armY * scale, 0f);
            Set(f, JointId.LeftKnee, -0.12f * scale, -0.45f * scale, 0f);
            Set(f, JointId.RightKnee, 0.12f * scale, -0.45f * scale, 0f);
            // Scaled too: StandingFrame places these in absolute metres, and leaving them would make
            // the "child" a differently PROPORTIONED body rather than a smaller one.
            Set(f, JointId.LeftAnkle, -0.1f * scale, -0.88f * scale, 0f);
            Set(f, JointId.RightAnkle, 0.1f * scale, -0.88f * scale, 0f);
            return f;
        }

        // A standing body with its hands somewhere else, so differencing two of these produces speed.
        //
        // The sweep is SIDEWAYS, not vertical. FluidField is a horizontal XZ velocity field: it takes
        // its push direction from the XZ part of the joint velocity, so a purely up-and-down wave
        // injects nothing at all - which is correct behaviour and was worth finding out here.
        private static PoseFrame WavingFrame(float x, float z, float sweep) {
            PoseFrame f = StandingFrame(-0.88f, -1.0f, x);
            Set(f, JointId.LeftWrist, x - 0.45f + sweep, 0.3f, z + sweep);
            Set(f, JointId.RightWrist, x + 0.45f + sweep, 0.3f, z + sweep);
            Set(f, JointId.Nose, x, 0.78f, z);
            return f;
        }

        private static void Set(PoseFrame f, JointId joint, float x, float y, float z) {
            f.SetLandmark((int)joint, new PoseLandmark(new Vector3(x, y, z), 0.9f));
        }
    }
}
