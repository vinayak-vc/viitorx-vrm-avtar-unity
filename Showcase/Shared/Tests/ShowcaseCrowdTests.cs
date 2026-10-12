using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

using Joint = ShowcaseShared.Joint;

namespace ShowcaseShared.Tests {
    /// <summary>
    /// The crowd logic every showcase shares: who gets a slot, who is stepped with what, where each person is placed, when a
    /// slot is freed. Tested with a stand-in experience, so that what is proven here holds for every showcase built on it
    /// (Chromatic Rain and Fluid Disruption today, whatever comes next).
    /// </summary>
    public sealed class ShowcaseCrowdTests {
        private const float Dt = 1f / 30f;

        private sealed class TestPerson : TrackedPerson {
            public int ExperienceResets;

            protected override void ResetExperience() {
                ExperienceResets = ExperienceResets + 1;
            }
        }

        private sealed class StepRecord {
            public int Id;
            public bool HadPose;
            public bool HadHands;
            public bool Fresh;
            public float LateralShift;
        }

        // Records every step, and calls a person finished once they have been absent for AbsentFramesToFinish steps.
        private sealed class RecordingStepper : ICrowdStepper<TestPerson> {
            public readonly List<StepRecord> Steps = new List<StepRecord>();
            public int AbsentFramesToFinish = 3;
            private readonly Dictionary<int, int> absent = new Dictionary<int, int>();

            public void StepPerson(TestPerson person, SkeletonPose pose, HandPose left, HandPose right,
                                   TrackingTelemetry telemetry, bool fresh, float dt) {
                StepRecord record = new StepRecord();
                record.Id = person.Id;
                record.HadPose = pose != null;
                record.HadHands = left != null && right != null;
                record.Fresh = fresh;
                record.LateralShift = person.Adapter.LateralShift;
                Steps.Add(record);
                int count;
                absent.TryGetValue(person.Id, out count);
                absent[person.Id] = pose == null ? count + 1 : 0;
            }

            public bool IsFinished(TestPerson person) {
                int count;
                absent.TryGetValue(person.Id, out count);
                return count >= AbsentFramesToFinish;
            }

            public int StepsFor(int id) {
                int n = 0;
                for (int i = 0; i < Steps.Count; i++) {
                    if (Steps[i].Id == id) {
                        n = n + 1;
                    }
                }
                return n;
            }
        }

        private sealed class FakeCrowd : ICrowdSource {
            public readonly List<ShowcaseLiveBody> Bodies = new List<ShowcaseLiveBody>();
            public bool Active;

            public bool CrowdActive {
                get {
                    return Active;
                }
            }

            public int LiveBodyCount {
                get {
                    return Active ? Bodies.Count : 0;
                }
            }

            public ShowcaseLiveBody LiveBody(int index) {
                return Bodies[index];
            }
        }

        private static SkeletonPose PoseWithHips(bool hips, PoseAnchor anchor) {
            PoseFrame frame = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                frame.SetLandmark(i, new PoseLandmark(Vector3.zero, 0f));
            }
            frame.SetLandmark((int)JointId.LeftShoulder, new PoseLandmark(new Vector3(-0.18f, 0.5f, 0f), 0.9f));
            frame.SetLandmark((int)JointId.RightShoulder, new PoseLandmark(new Vector3(0.18f, 0.5f, 0f), 0.9f));
            if (hips) {
                frame.SetLandmark((int)JointId.LeftHip, new PoseLandmark(new Vector3(-0.09f, 0f, 0f), 0.9f));
                frame.SetLandmark((int)JointId.RightHip, new PoseLandmark(new Vector3(0.09f, 0f, 0f), 0.9f));
            }
            frame.SetMeta(0.0, true);
            frame.SetRootPosition(new Vector3(0f, 0f, 2f));
            frame.SetAnchor(anchor);
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(frame, new Vector3(0f, 0.95f, 0f), 1f, Dt);
            return pose;
        }

        private static ShowcaseLiveBody Live(int id, bool primary, float rootX, SkeletonPose pose) {
            ShowcaseLiveBody body = new ShowcaseLiveBody();
            body.Id = id;
            body.Primary = primary;
            body.HasRoot = true;
            body.RootX = rootX;
            body.Pose = pose;
            return body;
        }

        private static ShowcaseCrowd<TestPerson> NewCrowd(int slots) {
            TestPerson[] people = new TestPerson[slots];
            for (int i = 0; i < slots; i++) {
                people[i] = new TestPerson();
            }
            return new ShowcaseCrowd<TestPerson>(people);
        }

        // ------------------------------------------------------------------ the single-person path

        [Test]
        public void Single_ANonCrowdSourceGivesOnePersonInTheSingleSlotWithEverythingTheSourceHas() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(3);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            SkeletonPose pose = PoseWithHips(true, PoseAnchor.Hips);
            HandPose left = new HandPose();
            HandPose right = new HandPose();

            crowd.Update(source, true, pose, left, right, null, Dt, stepper);

            Assert.AreEqual(1, stepper.Steps.Count);
            Assert.AreEqual(PersonSlots.SingleId, stepper.Steps[0].Id);
            Assert.IsTrue(stepper.Steps[0].HadPose && stepper.Steps[0].HadHands && stepper.Steps[0].Fresh);
            Assert.IsTrue(crowd.At(0).InUse && crowd.At(0).Present);
            Assert.AreEqual(0f, stepper.Steps[0].LateralShift, "the stage already places a single person");
        }

        [Test]
        public void Single_AStaleSourceStepsThePersonAsNotFresh() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(2);
            RecordingStepper stepper = new RecordingStepper();
            crowd.Update(new FakeCrowd(), false, PoseWithHips(true, PoseAnchor.Hips), null, null, null, Dt, stepper);
            Assert.IsFalse(stepper.Steps[0].Fresh);
        }

        // ------------------------------------------------------------------ several people

        [Test]
        public void Crowd_EveryPersonGetsTheirOwnSlotAndKeepsItWhileTheyStay() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(3);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(7, true, 0.4f, PoseWithHips(true, PoseAnchor.Hips)));
            source.Bodies.Add(Live(12, false, -0.6f, PoseWithHips(true, PoseAnchor.Hips)));

            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(7, crowd.At(0).Id);
            Assert.AreEqual(12, crowd.At(1).Id);
            Assert.IsFalse(crowd.At(2).InUse);

            // The order changes (the sidecar lists the most established first); nobody changes slot.
            source.Bodies.Reverse();
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(7, crowd.At(0).Id);
            Assert.AreEqual(12, crowd.At(1).Id);
            Assert.AreEqual(2, stepper.StepsFor(7));
            Assert.AreEqual(2, stepper.StepsFor(12));
        }

        [Test]
        public void Crowd_OnlyThePrimaryPersonHasHandsAndTheTrustChannel() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(3);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0f, PoseWithHips(true, PoseAnchor.Hips)));
            source.Bodies.Add(Live(2, false, 0.5f, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, new HandPose(), new HandPose(), null, Dt, stepper);
            Assert.IsTrue(stepper.Steps[0].HadHands, "the first person has hands");
            Assert.IsFalse(stepper.Steps[1].HadHands, "the others are drawn from the body");
        }

        [Test]
        public void Crowd_WhenEverySlotIsTakenTheNewcomerIsNotSteppedAndNobodyIsEvicted() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(2);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            for (int id = 1; id <= 3; id++) {
                source.Bodies.Add(Live(id, id == 1, 0f, PoseWithHips(true, PoseAnchor.Hips)));
            }
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(1, crowd.At(0).Id);
            Assert.AreEqual(2, crowd.At(1).Id);
            Assert.AreEqual(0, stepper.StepsFor(3));
        }

        // ------------------------------------------------------------------ where each person stands

        [Test]
        public void Placement_APersonIsPlacedByTheirOwnMeasuredRootNotByTheFirstPerson() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(2);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0.8f, PoseWithHips(true, PoseAnchor.Hips)));
            source.Bodies.Add(Live(2, false, -0.5f, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(0.8f, stepper.Steps[0].LateralShift, 0.01f);
            Assert.AreEqual(-0.5f, stepper.Steps[1].LateralShift, 0.01f);
        }

        [Test]
        public void Placement_WithoutBothHipsMeasuredTheLastShiftStaysSoAPersonDoesNotSlide() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(1);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0.8f, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            source.Bodies[0] = Live(1, true, -1.5f, PoseWithHips(false, PoseAnchor.Hips));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(0.8f, stepper.Steps[1].LateralShift, 0.01f, "no hips, no new measurement of where they stand");
        }

        [Test]
        public void Placement_AnEstimatedRootCountsBecauseTheSenderPlacedItItself() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(1);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0.6f, PoseWithHips(false, PoseAnchor.Shoulders)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(0.6f, stepper.Steps[0].LateralShift, 0.01f, "F-60: a body the camera cuts off is still placed");
        }

        [Test]
        public void Placement_ANonFiniteRootIsIgnored() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(1);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, float.NaN, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(0f, stepper.Steps[0].LateralShift);
        }

        // ------------------------------------------------------------------ leaving

        [Test]
        public void Leaving_AnAbsentPersonIsSteppedAsLostUntilTheExperienceIsDoneThenTheirSlotIsFreed() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(2);
            RecordingStepper stepper = new RecordingStepper();
            stepper.AbsentFramesToFinish = 3;
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0f, PoseWithHips(true, PoseAnchor.Hips)));
            source.Bodies.Add(Live(2, false, 0.5f, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);

            source.Bodies.RemoveAt(1);
            for (int frame = 0; frame < 2; frame++) {
                crowd.Update(source, true, null, null, null, null, Dt, stepper);
            }
            Assert.IsTrue(crowd.At(1).InUse, "still fading out: the slot is kept");
            StepRecord last = stepper.Steps[stepper.Steps.Count - 1];
            Assert.AreEqual(2, last.Id);
            Assert.IsFalse(last.HadPose);
            Assert.IsFalse(last.Fresh);

            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.IsFalse(crowd.At(1).InUse, "the experience is done with them: the slot is free");
            Assert.IsTrue(crowd.At(0).InUse, "and nobody else is touched");
        }

        [Test]
        public void Leaving_AReleasedSlotIsReusedAndTheNewPersonInheritsNothing() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(1);
            RecordingStepper stepper = new RecordingStepper();
            stepper.AbsentFramesToFinish = 1;
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0.9f, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(0.9f, crowd.At(0).Adapter.LateralShift, 0.01f);

            source.Bodies.Clear();
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.IsFalse(crowd.At(0).InUse);
            Assert.AreEqual(0f, crowd.At(0).Adapter.LateralShift, "a released slot forgets where its person stood");

            source.Bodies.Add(Live(5, true, 0f, PoseWithHips(false, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            Assert.AreEqual(5, crowd.At(0).Id);
            Assert.AreEqual(0f, stepper.Steps[stepper.Steps.Count - 1].LateralShift, "the newcomer starts where the camera sees them");
        }

        [Test]
        public void Leaving_ReleaseAllFreesEverySlotAndResetsTheExperienceState() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(3);
            RecordingStepper stepper = new RecordingStepper();
            FakeCrowd source = new FakeCrowd();
            source.Active = true;
            source.Bodies.Add(Live(1, true, 0f, PoseWithHips(true, PoseAnchor.Hips)));
            source.Bodies.Add(Live(2, false, 0.5f, PoseWithHips(true, PoseAnchor.Hips)));
            crowd.Update(source, true, null, null, null, null, Dt, stepper);
            int resetsBefore = crowd.At(0).ExperienceResets;

            crowd.ReleaseAll();
            for (int i = 0; i < crowd.Count; i++) {
                Assert.IsFalse(crowd.At(i).InUse);
            }
            Assert.Greater(crowd.At(0).ExperienceResets, resetsBefore, "the experience's own per-person state is reset too");
        }

        // ------------------------------------------------------------------ the primary person

        [Test]
        public void Primary_IsTheFirstSlotInUseAndTheFirstSlotWhenNobodyIs() {
            ShowcaseCrowd<TestPerson> crowd = NewCrowd(3);
            Assert.AreEqual(0, crowd.PrimaryIndex);
            Assert.AreSame(crowd.At(0), crowd.Primary);
            PersonSlots.Acquire(crowd.People, 9);
            PersonSlots.Acquire(crowd.People, 10);
            crowd.At(0).Release();
            Assert.AreEqual(1, crowd.PrimaryIndex, "the first one in use");
            Assert.AreSame(crowd.At(1), crowd.Primary);
        }

        [Test]
        public void Slots_TheSingleIdIsNotARealTrackId() {
            Assert.Less(PersonSlots.SingleId, 0, "sidecar track ids are never negative, so the single-person slot cannot collide with one");
        }

        [Test]
        public void Slots_JointSlotsAreTheBlazePoseIndicesTheSkeletonPoseUses() {
            Assert.AreEqual((int)JointId.LeftShoulder, Joint.LeftShoulder);
            Assert.AreEqual((int)JointId.RightHip, Joint.RightHip);
            Assert.AreEqual((int)JointId.LeftHeel, Joint.LeftHeel);
            Assert.AreEqual(PoseFrame.LandmarkCount, Joint.BodyCount);
        }
    }
}
