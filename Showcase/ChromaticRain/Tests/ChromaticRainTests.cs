using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace ChromaticRain.Tests {
    /// <summary>
    /// EditMode tests for the showcase's pure logic: the A-F state machine, the view mapping, the
    /// pose adapter (joint loss, staleness, snapping), the silhouette builder and the simulated
    /// pose. Scene, shader and FluXY behaviour are verified in Play mode, not here.
    /// </summary>
    public sealed class ChromaticRainTests {
        private const float Dt = 1f / 60f;

        private static ChromaticRainSettings NewSettings() {
            return ScriptableObject.CreateInstance<ChromaticRainSettings>();
        }

        // ------------------------------------------------------------------ state machine

        private static void Run(ShowcaseStateMachine m, bool tracked, float energy, float seconds,
                                ChromaticRainSettings s) {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) {
                m.Tick(tracked, energy, Dt, s);
            }
        }

        [Test]
        public void StateMachine_NoOneTracked_StaysInNoPersonWithZeroAlpha() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, false, 0f, 5f, s);
            Assert.AreEqual(ShowcasePhase.NoPerson, m.Phase);
            Assert.AreEqual(0f, m.SilhouetteAlpha);
            Assert.AreEqual(0f, m.FluidActivity);
        }

        [Test]
        public void StateMachine_BriefTrackingBelowTheEnterDelay_DoesNotShowASilhouette() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, true, 0.5f, s.enterSeconds * 0.5f, s);
            Assert.AreEqual(ShowcasePhase.NoPerson, m.Phase);
            Assert.AreEqual(0f, m.SilhouetteAlpha);
        }

        [Test]
        public void StateMachine_SustainedTracking_FadesInThenSettles() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, true, 0.5f, s.enterSeconds + 0.05f, s);
            Assert.AreEqual(ShowcasePhase.Detected, m.Phase);
            Run(m, true, 0.5f, s.fadeInSeconds + 0.3f, s);
            Assert.AreEqual(ShowcasePhase.Moving, m.Phase);
            Assert.AreEqual(1f, m.SilhouetteAlpha, 1e-4f);
            Assert.Greater(m.FluidActivity, 0.99f);
        }

        [Test]
        public void StateMachine_StandingStill_BecomesStationary() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, true, 0.5f, s.enterSeconds + s.fadeInSeconds + 0.3f, s);
            Assert.AreEqual(ShowcasePhase.Moving, m.Phase);
            Run(m, true, 0f, s.stillHoldSeconds + 0.3f, s);
            Assert.AreEqual(ShowcasePhase.Stationary, m.Phase);
            Run(m, true, 0.5f, 0.1f, s);
            Assert.AreEqual(ShowcasePhase.Moving, m.Phase);
        }

        [Test]
        public void StateMachine_Loss_FadesOutMonotonicallyAndReturnsToNoPerson() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, true, 0.5f, 2f, s);
            Run(m, false, 0f, s.lostTimeoutSeconds + 0.05f, s);
            Assert.AreEqual(ShowcasePhase.Lost, m.Phase);
            float previous = m.SilhouetteAlpha;
            int steps = Mathf.RoundToInt((s.fadeOutSeconds + 0.5f) / Dt);
            for (int i = 0; i < steps; i++) {
                m.Tick(false, 0f, Dt, s);
                Assert.LessOrEqual(m.SilhouetteAlpha, previous + 1e-6f, "alpha must never rise while lost");
                previous = m.SilhouetteAlpha;
            }
            Assert.AreEqual(ShowcasePhase.NoPerson, m.Phase);
            Assert.AreEqual(0f, m.SilhouetteAlpha);
            Assert.AreEqual(0f, m.FluidActivity);
        }

        [Test]
        public void StateMachine_ShortDropout_DoesNotTriggerLoss() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, true, 0.5f, 2f, s);
            Run(m, false, 0f, s.lostTimeoutSeconds * 0.5f, s);
            Run(m, true, 0.5f, 0.5f, s);
            Assert.AreEqual(ShowcasePhase.Moving, m.Phase);
            Assert.AreEqual(1f, m.SilhouetteAlpha, 1e-4f);
        }

        [Test]
        public void StateMachine_Resume_ClearsTheOldSilhouetteBeforeSnapping() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            Run(m, true, 0.5f, 2f, s);
            Run(m, false, 0f, s.lostTimeoutSeconds + 0.3f, s);
            Assert.AreEqual(ShowcasePhase.Lost, m.Phase);
            Assert.Greater(m.SilhouetteAlpha, 0f, "the old silhouette is still partly visible");

            int snaps = 0;
            float alphaAtSnap = -1f;
            int steps = Mathf.RoundToInt(3f / Dt);
            for (int i = 0; i < steps; i++) {
                m.Tick(true, 0.5f, Dt, s);
                if (m.SnapRequested) {
                    snaps++;
                    alphaAtSnap = m.SilhouetteAlpha;
                }
            }
            Assert.AreEqual(1, snaps, "exactly one snap");
            Assert.AreEqual(0f, alphaAtSnap, 1e-4f, "the old silhouette is gone before the new pose is placed");
            Assert.AreEqual(1f, m.SilhouetteAlpha, 1e-4f);
        }

        [Test]
        public void StateMachine_FirstAcquisition_RequestsOneSnap() {
            ChromaticRainSettings s = NewSettings();
            ShowcaseStateMachine m = new ShowcaseStateMachine();
            int snaps = 0;
            int steps = Mathf.RoundToInt(2f / Dt);
            for (int i = 0; i < steps; i++) {
                m.Tick(true, 0.5f, Dt, s);
                if (m.SnapRequested) {
                    snaps++;
                }
            }
            Assert.AreEqual(1, snaps);
        }

        // ------------------------------------------------------------------ mapping

        [Test]
        public void Mapper_ScalesTheBodyPositionButNotItsShape() {
            ScreenSpaceMapper map = new ScreenSpaceMapper();
            map.ViewCentreY = 0.95f;
            map.LateralGain = 2f;
            Vector3 centre = new Vector3(0.5f, 0.95f, 0f);
            Vector3 hand = centre + new Vector3(0.3f, 0.4f, 0f);
            Vector2 c = map.Map(centre, centre);
            Vector2 h = map.Map(hand, centre);
            Assert.AreEqual(1.0f, c.x, 1e-5f, "position offset is scaled by the gain");
            Assert.AreEqual(0f, c.y, 1e-5f);
            Assert.AreEqual(0.3f, h.x - c.x, 1e-5f, "shape is life-size");
            Assert.AreEqual(0.4f, h.y - c.y, 1e-5f);
        }

        [Test]
        public void Mapper_ViewportCentreAndEdges() {
            ScreenSpaceMapper map = new ScreenSpaceMapper();
            map.ViewHeight = 2.8f;
            map.Aspect = 2f;
            Vector2 centre = map.ToViewport(Vector2.zero);
            Vector2 topRight = map.ToViewport(new Vector2(2.8f, 1.4f));
            Assert.AreEqual(0.5f, centre.x, 1e-5f);
            Assert.AreEqual(0.5f, centre.y, 1e-5f);
            Assert.AreEqual(1f, topRight.x, 1e-5f);
            Assert.AreEqual(1f, topRight.y, 1e-5f);
        }

        // ------------------------------------------------------------------ simulated pose

        private sealed class Rig {
            public readonly SimulatedPoseSource Sim = new SimulatedPoseSource();
            public readonly SkeletonPose Pose = new SkeletonPose();
            public readonly HandPose Left = new HandPose();
            public readonly HandPose Right = new HandPose();
            public readonly ShowcasePoseAdapter Adapter = new ShowcasePoseAdapter();
            public readonly ShowcaseBody Body = new ShowcaseBody();
            public readonly ScreenSpaceMapper Mapper = new ScreenSpaceMapper();
            public readonly ChromaticRainSettings Settings = NewSettings();
            public readonly SilhouetteSkin Skin = SilhouetteSkin.Anatomical();
            public bool SourceFresh = true;

            public Rig() {
                Mapper.Configure(Settings, 16f / 9f);
                Adapter.Reset(Body);
            }

            public void Step() {
                if (Sim.StreamActive) {
                    Sim.Tick(Dt);
                    Vector3 origin = new Vector3(Sim.OffsetX, SimulatedPoseSource.HipHeight, 0f);
                    Pose.Fill(Sim.Frame, origin, 1f, Dt);
                    Left.Fill(Sim.Hands, true, origin, 1f);
                    Right.Fill(Sim.Hands, false, origin, 1f);
                }
                Adapter.Update(Body, Pose, Left, Right, null, SourceFresh && Sim.StreamActive, Dt, Settings, Mapper);
            }

            public void Run(float seconds) {
                int steps = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < steps; i++) {
                    Step();
                }
            }
        }

        [Test]
        public void Sim_IsDeterministic() {
            SimulatedPoseSource a = new SimulatedPoseSource();
            SimulatedPoseSource b = new SimulatedPoseSource();
            for (int i = 0; i < 600; i++) {
                a.Tick(Dt);
                b.Tick(Dt);
            }
            for (int j = 0; j < PoseFrame.LandmarkCount; j++) {
                Assert.AreEqual(a.Frame.GetLandmark((JointId)j).Position, b.Frame.GetLandmark((JointId)j).Position);
            }
            Assert.AreEqual(a.OffsetX, b.OffsetX);
        }

        [Test]
        public void Sim_LimbLengthsStayHumanAsTheArmsMove() {
            SimulatedPoseSource sim = new SimulatedPoseSource();
            for (int i = 0; i < 60 * 30; i++) {
                sim.Tick(Dt);
                PoseFrame f = sim.Frame;
                float upper = Vector3.Distance(f.GetLandmark(JointId.LeftShoulder).Position, f.GetLandmark(JointId.LeftElbow).Position);
                float fore = Vector3.Distance(f.GetLandmark(JointId.LeftElbow).Position, f.GetLandmark(JointId.LeftWrist).Position);
                Assert.AreEqual(0.28f, upper, 0.01f);
                Assert.AreEqual(0.25f, fore, 0.01f);
            }
        }

        [Test]
        public void Sim_HandsPassTheRealHandPlausibilityChecks() {
            Rig rig = new Rig();
            rig.Run(2f);
            Assert.IsTrue(rig.Left.Valid, "left hand");
            Assert.IsTrue(rig.Right.Valid, "right hand");
            Assert.IsFalse(rig.Left.Implausible);
        }

        [Test]
        public void Sim_MirrorFlipsTheXSignLikeTheLiveConverter() {
            SimulatedPoseSource mirrored = new SimulatedPoseSource();
            SimulatedPoseSource plain = new SimulatedPoseSource();
            mirrored.mirrorX = true;
            plain.mirrorX = false;
            mirrored.Tick(Dt);
            plain.Tick(Dt);
            Vector3 m = mirrored.Frame.GetLandmark(JointId.LeftWrist).Position;
            Vector3 p = plain.Frame.GetLandmark(JointId.LeftWrist).Position;
            Assert.Less(m.x, 0f, "mirrored: left is on the screen's left");
            Assert.AreEqual(-m.x, p.x, 1e-5f);
            Assert.AreEqual(m.y, p.y, 1e-5f);
        }

        // ------------------------------------------------------------------ adapter

        [Test]
        public void Adapter_AFullBodyIsTrackedAndInsideTheView() {
            Rig rig = new Rig();
            rig.Run(2f);
            Assert.IsTrue(rig.Body.CoreFresh);
            float halfH = rig.Mapper.ViewHeight * 0.5f;
            foreach (int j in new int[] { Joint.Nose, Joint.LeftWrist, Joint.RightAnkle, Joint.LeftToe }) {
                Assert.AreEqual(1f, rig.Body.Weight[j], 1e-3f, "weight of " + j);
                Assert.Less(Mathf.Abs(rig.Body.Position[j].y), halfH, "inside the view: " + j);
            }
        }

        [Test]
        public void Adapter_LostJoint_IsHeldThenFadedAndNeverMovedToTheOrigin() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Sim.armPattern = SimulatedArmPattern.Down;
            rig.Run(2f);
            int wrist = Joint.LeftWrist;
            Vector2 held = rig.Body.Position[wrist];
            Assert.AreEqual(1f, rig.Body.Weight[wrist], 1e-3f);

            rig.Sim.lostJointMask = 1L << wrist;
            float previousWeight = 1f;
            float grace = rig.Settings.jointGraceSeconds;
            float fade = rig.Settings.jointFadeSeconds;
            int steps = Mathf.RoundToInt((grace + fade + 0.3f) / Dt);
            for (int i = 0; i < steps; i++) {
                rig.Step();
                Assert.AreEqual(held.x, rig.Body.Position[wrist].x, 0.02f, "held in place, not dragged");
                Assert.AreEqual(held.y, rig.Body.Position[wrist].y, 0.02f);
                Assert.LessOrEqual(rig.Body.Weight[wrist], previousWeight + 1e-6f);
                previousWeight = rig.Body.Weight[wrist];
            }
            Assert.AreEqual(0f, rig.Body.Weight[wrist], 1e-4f, "faded out after the grace period");

            rig.Sim.lostJointMask = 0L;
            rig.Run(1f);
            Assert.AreEqual(1f, rig.Body.Weight[wrist], 1e-3f, "and back once it is measured again");
        }

        [Test]
        public void Adapter_WholeBodyLoss_HoldsTheShapeInsteadOfCollapsingLimbByLimb() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Run(1.5f);
            Vector2[] before = new Vector2[Joint.SlotCount];
            for (int i = 0; i < Joint.SlotCount; i++) {
                before[i] = rig.Body.Position[i];
            }
            rig.SourceFresh = false;
            rig.Run(rig.Settings.jointGraceSeconds + rig.Settings.jointFadeSeconds + 1f);
            Assert.IsFalse(rig.Body.CoreFresh);
            foreach (int j in new int[] { Joint.Nose, Joint.LeftWrist, Joint.RightAnkle, Joint.LeftToe }) {
                Assert.AreEqual(1f, rig.Body.Weight[j], 1e-3f, "weight held for the state machine to fade: " + j);
                Assert.AreEqual(before[j].x, rig.Body.Position[j].x, 0.03f);
                Assert.AreEqual(before[j].y, rig.Body.Position[j].y, 0.03f);
            }
        }

        [Test]
        public void Adapter_StalePoseIsNeverAPerson() {
            Rig rig = new Rig();
            rig.Run(1f);
            Assert.IsTrue(rig.Body.CoreFresh);
            rig.SourceFresh = false;
            for (int i = 0; i < 120; i++) {
                rig.Step();
                Assert.IsFalse(rig.Body.CoreFresh, "a stale pose must not count as tracked");
            }
        }

        [Test]
        public void Adapter_NonFiniteLandmarksAreIgnored() {
            Rig rig = new Rig();
            rig.Run(1f);
            Vector2 before = rig.Body.Position[Joint.RightElbow];
            rig.Pose.World[Joint.RightElbow] = new Vector3(float.NaN, 1f, 0f);
            rig.Adapter.Update(rig.Body, rig.Pose, rig.Left, rig.Right, null, true, Dt, rig.Settings, rig.Mapper);
            Assert.IsFalse(float.IsNaN(rig.Body.Position[Joint.RightElbow].x));
            Assert.AreEqual(before.x, rig.Body.Position[Joint.RightElbow].x, 0.05f);
        }

        [Test]
        public void Adapter_Snap_PlacesANewPersonExactlyWithoutSlidingFromTheOldOne() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Run(1f);
            // A different person elsewhere: the staged origin moves a long way in one frame.
            Vector3 origin = new Vector3(1.0f, SimulatedPoseSource.HipHeight, 0f);
            rig.Pose.Fill(rig.Sim.Frame, origin, 1f, Dt);
            rig.Adapter.RequestSnap();
            rig.Adapter.Update(rig.Body, rig.Pose, rig.Left, rig.Right, null, true, Dt, rig.Settings, rig.Mapper);
            Vector3 centre = rig.Pose.Centre;
            Vector2 expected = rig.Mapper.Map(rig.Pose.World[Joint.Nose], centre);
            Assert.AreEqual(expected.x, rig.Body.Position[Joint.Nose].x, 1e-4f);
            Assert.AreEqual(expected.y, rig.Body.Position[Joint.Nose].y, 1e-4f);
            Assert.AreEqual(Vector2.zero, rig.Body.Velocity[Joint.Nose]);
        }

        [Test]
        public void Adapter_SnapForgetsJointsTheNewPersonHasNotShown() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Run(1f);
            Assert.AreEqual(1f, rig.Body.Weight[Joint.RightAnkle], 1e-3f);
            rig.Sim.lostJointMask = 1L << Joint.RightAnkle;
            rig.Step();
            rig.Adapter.RequestSnap();
            rig.Step();
            Assert.AreEqual(0f, rig.Body.Weight[Joint.RightAnkle], 1e-4f);
        }

        [Test]
        public void Adapter_TrustChannelDownWeightsUncertainJoints() {
            Assert.AreEqual(1f, ShowcasePoseAdapter.TrustFactor(null, 15));
            TrackingTelemetry t = new TrackingTelemetry();
            t.HasTrustChannel = true;
            t.SetState(15, JointTrackState.Lost);
            t.SetState(13, JointTrackState.Predicted);
            t.SetState(11, JointTrackState.Tracked);
            Assert.AreEqual(0f, ShowcasePoseAdapter.TrustFactor(t, 15));
            Assert.Less(ShowcasePoseAdapter.TrustFactor(t, 13), 1f);
            Assert.AreEqual(1f, ShowcasePoseAdapter.TrustFactor(t, 11));
        }

        // ------------------------------------------------------------------ silhouette

        private static SilhouetteBuilder BuildFrom(Rig rig) {
            SilhouetteBuilder builder = new SilhouetteBuilder();
            for (int i = 0; i < 60; i++) {
                builder.Build(rig.Body, rig.Skin, Dt);
            }
            return builder;
        }

        [Test]
        public void Silhouette_FullBodyHasEverySegmentAndAHead() {
            Rig rig = new Rig();
            rig.Run(2f);
            SilhouetteBuilder b = BuildFrom(rig);
            Assert.Greater(b.CapCount, 25, "an anatomical body is many shapes: torso tapers, shoulders, discs, calves, feet");
            Assert.LessOrEqual(b.CapCount, SilhouetteBuilder.MaxCaps);
            Assert.Greater(b.HeadDirection.z, 0.99f);
        }

        [Test]
        public void Silhouette_LimbsStartExactlyWhereTheirParentJointIsAndRunUnbroken() {
            Rig rig = new Rig();
            rig.Run(2f);
            SilhouetteBuilder b = BuildFrom(rig);
            Vector2 shoulder = rig.Body.Position[Joint.LeftShoulder];
            Vector2 elbow = rig.Body.Position[Joint.LeftElbow];
            int start = -1;
            for (int i = 0; i < b.CapCount; i++) {
                Vector2 a = new Vector2(b.Caps[i].x, b.Caps[i].y);
                if ((a - shoulder).magnitude < 1e-4f && b.CapRadii[i].w < 0.5f
                    && Mathf.Abs(b.CapRadii[i].x - rig.Skin.upperArmShoulderRadius * b.WidthScale) < 1e-4f) {
                    start = i;
                    break;
                }
            }
            Assert.GreaterOrEqual(start, 0, "the upper arm starts exactly at the shoulder");
            // The upper arm is a chain of pieces: each starts where the previous one ended, and the chain ends at the elbow.
            int index = start;
            Vector2 end = new Vector2(b.Caps[index].z, b.Caps[index].w);
            while ((end - elbow).magnitude > 1e-4f && index + 1 < b.CapCount) {
                index++;
                Vector2 next = new Vector2(b.Caps[index].x, b.Caps[index].y);
                Assert.AreEqual(0f, (next - end).magnitude, 1e-4f, "no gap between the pieces of the upper arm");
                end = new Vector2(b.Caps[index].z, b.Caps[index].w);
            }
            Assert.AreEqual(0f, (end - elbow).magnitude, 1e-4f, "the upper arm ends exactly at the elbow");
        }

        [Test]
        public void Silhouette_AbsurdLimbLengthIsClampedNotDrawn() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Run(2f);
            rig.Body.Position[Joint.LeftElbow] = rig.Body.Position[Joint.LeftShoulder] + new Vector2(20f, 20f);
            SilhouetteBuilder b = new SilhouetteBuilder();
            b.Build(rig.Body, rig.Skin, Dt);
            // The longest believable segment is a leg, 0.62 m, widened by at most 1.35.
            float limit = rig.Skin.maxLegSegment * 1.35f + 0.05f;
            for (int i = 0; i < b.CapCount; i++) {
                Vector2 a = new Vector2(b.Caps[i].x, b.Caps[i].y);
                Vector2 e = new Vector2(b.Caps[i].z, b.Caps[i].w);
                Assert.Less((e - a).magnitude, limit, "no shape may span the screen (cap " + i + ")");
            }
        }

        [Test]
        public void Silhouette_AMissingJointDropsOnlyItsSegments() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Run(2f);
            SilhouetteBuilder before = new SilhouetteBuilder();
            before.Build(rig.Body, rig.Skin, Dt);
            rig.Body.Weight[Joint.LeftKnee] = 0f;
            SilhouetteBuilder b = new SilhouetteBuilder();
            b.Build(rig.Body, rig.Skin, Dt);
            Assert.Less(b.CapCount, before.CapCount, "the left thigh, shin and foot go");
            Assert.GreaterOrEqual(b.CapCount, before.CapCount - 9, "and nothing else: the torso, arms and the other leg stay");
            Vector2 rightKnee = rig.Body.Position[Joint.RightKnee];
            Assert.Less(b.Distance(rightKnee, rig.Skin.shapeBlend), 0f, "the other leg is still there");
        }

        [Test]
        public void Silhouette_NoBodyBuildsNothing() {
            Rig rig = new Rig();
            SilhouetteBuilder b = new SilhouetteBuilder();
            b.Build(rig.Body, rig.Skin, Dt);
            Assert.AreEqual(0, b.CapCount);
            Assert.AreEqual(0f, b.HeadDirection.z);
        }

        [Test]
        public void Silhouette_BuilderNeverEmitsMoreShapesThanTheShaderHolds() {
            Rig rig = new Rig();
            rig.Run(2f);
            SilhouetteBuilder b = BuildFrom(rig);
            Assert.LessOrEqual(b.CapCount, SilhouetteBuilder.MaxCaps);
            Assert.AreEqual(SilhouetteBuilder.MaxCaps, b.Caps.Length);
        }

        // ------------------------------------------------------------------ several people

        [Test]
        public void People_AnIdKeepsItsSlotAndANewIdTakesTheFirstFreeOne() {
            ShowcasePerson[] people = new ShowcasePerson[] { new ShowcasePerson(), new ShowcasePerson(), new ShowcasePerson() };
            int a = PersonSlots.Acquire(people, 7);
            int b = PersonSlots.Acquire(people, 12);
            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
            Assert.AreEqual(a, PersonSlots.Acquire(people, 7), "the same id finds its own slot again");
            Assert.AreEqual(b, PersonSlots.Acquire(people, 12));
            Assert.AreEqual(2, PersonSlots.Acquire(people, 30));
            Assert.AreEqual(7, people[0].Id);
            Assert.IsTrue(people[0].InUse);
        }

        [Test]
        public void People_WhenEverySlotIsTakenTheNewcomerIsNotDrawnAndNobodyIsEvicted() {
            ShowcasePerson[] people = new ShowcasePerson[] { new ShowcasePerson(), new ShowcasePerson() };
            PersonSlots.Acquire(people, 1);
            PersonSlots.Acquire(people, 2);
            Assert.AreEqual(PersonSlots.NoSlot, PersonSlots.Acquire(people, 3));
            Assert.AreEqual(1, people[0].Id);
            Assert.AreEqual(2, people[1].Id);
        }

        [Test]
        public void People_AReleasedSlotIsReusedAndTheNewPersonInheritsNothing() {
            ShowcaseSettingsAndMapper env = new ShowcaseSettingsAndMapper();
            ShowcasePerson[] people = new ShowcasePerson[] { new ShowcasePerson(), new ShowcasePerson() };
            int slot = PersonSlots.Acquire(people, 5);
            Rig rig = new Rig();
            rig.Run(1f);
            people[slot].Adapter.LateralShift = 0.8f;
            people[slot].Adapter.Update(people[slot].Body, rig.Pose, rig.Left, rig.Right, null, true, Dt, env.Settings, env.Mapper);
            Assert.IsTrue(people[slot].Body.CoreFresh);

            people[slot].Release();
            Assert.IsFalse(people[slot].InUse);
            Assert.AreEqual(0f, people[slot].Adapter.LateralShift, "a released slot forgets the placement");
            Assert.IsFalse(people[slot].Body.CoreFresh, "and the body");
            Assert.AreEqual(ShowcasePhase.NoPerson, people[slot].Machine.Phase);

            Assert.AreEqual(slot, PersonSlots.Acquire(people, 6), "the freed slot is the first free one");
            Assert.AreEqual(6, people[slot].Id);
            Assert.AreEqual(0f, people[slot].Body.Weight[Joint.LeftShoulder]);
        }

        [Test]
        public void People_TheSingleIdIsNotARealTrackId() {
            Assert.Less(PersonSlots.SingleId, 0, "sidecar track ids are never negative, so the single-person slot cannot collide with one");
        }

        [Test]
        public void People_ALateralShiftMovesTheWholeBodySidewaysAndNeverReshapesIt() {
            Rig plain = new Rig();
            plain.Sim.stationary = true;
            plain.Run(2f);
            Rig shifted = new Rig();
            shifted.Sim.stationary = true;
            shifted.Adapter.LateralShift = 0.7f;
            shifted.Run(2f);

            float gain = plain.Settings.lateralGain;
            Vector2 dHip = shifted.Body.HipCentre - plain.Body.HipCentre;
            Assert.AreEqual(0.7f * gain, dHip.x, 0.02f, "the centre moves by the shift times the lateral gain");
            Assert.AreEqual(0f, dHip.y, 0.01f, "never vertically");
            Vector2 plainArm = plain.Body.Position[Joint.LeftWrist] - plain.Body.HipCentre;
            Vector2 shiftedArm = shifted.Body.Position[Joint.LeftWrist] - shifted.Body.HipCentre;
            Assert.AreEqual(plainArm.x, shiftedArm.x, 0.02f, "the body keeps its shape: the wrist stays where it was relative to the hip");
            Assert.AreEqual(plainArm.y, shiftedArm.y, 0.02f);
        }

        [Test]
        public void People_NoShiftIsTheSinglePersonBehaviourExactly() {
            Rig a = new Rig();
            Rig b = new Rig();
            b.Adapter.LateralShift = 0f;
            a.Run(2f);
            b.Run(2f);
            for (int j = 0; j < Joint.SlotCount; j++) {
                Assert.AreEqual(a.Body.Position[j], b.Body.Position[j], "joint " + j);
            }
        }

        [Test]
        public void People_TwoPeopleAtDifferentShiftsStandApartByTheirShiftDifference() {
            Rig left = new Rig();
            Rig right = new Rig();
            left.Sim.stationary = true;
            right.Sim.stationary = true;
            left.Adapter.LateralShift = -0.5f;
            right.Adapter.LateralShift = 0.5f;
            left.Run(2f);
            right.Run(2f);
            float gap = right.Body.HipCentre.x - left.Body.HipCentre.x;
            Assert.AreEqual(1.0f * left.Settings.lateralGain, gap, 0.03f, "1 m apart in the room is lateralGain metres apart on screen");
            Assert.Greater(gap, left.Body.ShoulderWidth * 2f, "and two bodies standing a metre apart do not overlap");
        }

        // ------------------------------------------------------------------ the shape of the body

        // A standing figure in view metres, hips at the origin, built directly so every pose is exact. Shoulder width 0.40.
        private static ShowcaseBody Figure(float leftArmUpper, float leftArmFore, float rightArmUpper, float rightArmFore,
                                           float kneeSpread, float ankleSpread) {
            ShowcaseBody b = new ShowcaseBody();
            for (int j = 0; j < Joint.SlotCount; j++) {
                b.Weight[j] = 0f;
            }
            Place(b, Joint.Nose, 0f, 0.70f);
            Place(b, Joint.LeftEar, -0.08f, 0.71f);
            Place(b, Joint.RightEar, 0.08f, 0.71f);
            Place(b, Joint.LeftShoulder, -0.20f, 0.50f);
            Place(b, Joint.RightShoulder, 0.20f, 0.50f);
            Place(b, Joint.LeftHip, -0.09f, 0f);
            Place(b, Joint.RightHip, 0.09f, 0f);
            Place(b, Joint.LeftKnee, -0.09f - kneeSpread, -0.45f);
            Place(b, Joint.RightKnee, 0.09f + kneeSpread, -0.45f);
            Place(b, Joint.LeftAnkle, -0.09f - ankleSpread, -0.86f);
            Place(b, Joint.RightAnkle, 0.09f + ankleSpread, -0.86f);
            Place(b, Joint.LeftToe, -0.10f - ankleSpread, -0.93f);
            Place(b, Joint.RightToe, 0.10f + ankleSpread, -0.93f);
            PlaceArm(b, -1f, Joint.LeftShoulder, Joint.LeftElbow, Joint.LeftWrist, leftArmUpper, leftArmFore);
            PlaceArm(b, 1f, Joint.RightShoulder, Joint.RightElbow, Joint.RightWrist, rightArmUpper, rightArmFore);
            b.ShoulderWidth = 0.40f;
            b.CoreFresh = true;
            return b;
        }

        private static void Place(ShowcaseBody b, int joint, float x, float y) {
            b.Position[joint] = new Vector2(x, y);
            b.Weight[joint] = 1f;
            b.Fresh[joint] = true;
        }

        // Angles are degrees from straight down: 0 hanging, 90 straight out to the side, 180 straight up.
        private static void PlaceArm(ShowcaseBody b, float side, int shoulder, int elbow, int wrist, float upper, float fore) {
            Vector2 s = b.Position[shoulder];
            float ua = upper * Mathf.Deg2Rad;
            float fa = fore * Mathf.Deg2Rad;
            Vector2 e = s + new Vector2(side * Mathf.Sin(ua), -Mathf.Cos(ua)) * 0.28f;
            Vector2 w = e + new Vector2(side * Mathf.Sin(fa), -Mathf.Cos(fa)) * 0.25f;
            Place(b, elbow, e.x, e.y);
            Place(b, wrist, w.x, w.y);
        }

        private static SilhouetteBuilder BuildFigure(ShowcaseBody body, SilhouetteSkin s) {
            SilhouetteBuilder builder = new SilhouetteBuilder();
            builder.Build(body, s, Dt);
            return builder;
        }

        private static bool Inside(SilhouetteBuilder b, SilhouetteSkin s, float x, float y) {
            return b.Distance(new Vector2(x, y), s.shapeBlend) < 0f;
        }

        // Width of the filled shape along a horizontal line, for the run that contains (x0, y): where it starts and ends.
        private static float RunWidth(SilhouetteBuilder b, SilhouetteSkin s, float x0, float y) {
            if (!Inside(b, s, x0, y)) {
                return 0f;
            }
            float step = 0.004f;
            float left = x0;
            while (left > x0 - 1.5f && Inside(b, s, left - step, y)) {
                left -= step;
            }
            float right = x0;
            while (right < x0 + 1.5f && Inside(b, s, right + step, y)) {
                right += step;
            }
            return right - left;
        }

        private struct PoseCase {
            public string Name;
            public float LeftUpper;
            public float LeftFore;
            public float RightUpper;
            public float RightFore;
            public float KneeSpread;
            public float AnkleSpread;

            public PoseCase(string name, float lu, float lf, float ru, float rf, float knee, float ankle) {
                Name = name;
                LeftUpper = lu;
                LeftFore = lf;
                RightUpper = ru;
                RightFore = rf;
                KneeSpread = knee;
                AnkleSpread = ankle;
            }
        }

        private static readonly PoseCase[] Poses = new PoseCase[] {
            new PoseCase("neutral, arms down", 8f, 4f, 8f, 4f, 0.01f, 0.01f),
            new PoseCase("both arms raised", 170f, 175f, 170f, 175f, 0.01f, 0.01f),
            new PoseCase("one arm raised, one down", 165f, 175f, 8f, 4f, 0.01f, 0.01f),
            new PoseCase("arms stretched outward", 90f, 90f, 90f, 90f, 0.01f, 0.01f),
            new PoseCase("arms bent at the elbows", 20f, 140f, 20f, 140f, 0.01f, 0.01f),
            new PoseCase("legs apart", 8f, 4f, 8f, 4f, 0.12f, 0.24f)
        };

        [Test]
        public void Body_EveryBoneLiesInsideTheFilledShapeInEveryPose() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            for (int p = 0; p < Poses.Length; p++) {
                PoseCase c = Poses[p];
                ShowcaseBody body = Figure(c.LeftUpper, c.LeftFore, c.RightUpper, c.RightFore, c.KneeSpread, c.AnkleSpread);
                SilhouetteBuilder b = BuildFigure(body, s);
                int[] bones = new int[] {
                    Joint.LeftShoulder, Joint.LeftElbow, Joint.LeftElbow, Joint.LeftWrist,
                    Joint.RightShoulder, Joint.RightElbow, Joint.RightElbow, Joint.RightWrist,
                    Joint.LeftHip, Joint.LeftKnee, Joint.LeftKnee, Joint.LeftAnkle,
                    Joint.RightHip, Joint.RightKnee, Joint.RightKnee, Joint.RightAnkle,
                    Joint.LeftShoulder, Joint.RightShoulder, Joint.LeftHip, Joint.RightHip
                };
                for (int i = 0; i + 1 < bones.Length; i += 2) {
                    for (int k = 0; k <= 10; k++) {
                        Vector2 pt = Vector2.Lerp(body.Position[bones[i]], body.Position[bones[i + 1]], k / 10f);
                        Assert.IsTrue(Inside(b, s, pt.x, pt.y), c.Name + ": bone " + bones[i] + "-" + bones[i + 1] + " at " + k / 10f + " is outside the body");
                    }
                }
                // The spine, from the middle of the hips to the middle of the shoulders, is inside the torso. (The line from a
                // shoulder joint to the hip joint below it runs along the flank, which is the body's edge, so it is not tested.)
                Vector2 hipMid = (body.Position[Joint.LeftHip] + body.Position[Joint.RightHip]) * 0.5f;
                Vector2 shoulderMid = (body.Position[Joint.LeftShoulder] + body.Position[Joint.RightShoulder]) * 0.5f;
                for (int k = 0; k <= 10; k++) {
                    Vector2 pt = Vector2.Lerp(hipMid, shoulderMid, k / 10f);
                    Assert.IsTrue(Inside(b, s, pt.x, pt.y), c.Name + ": the spine at " + k / 10f + " is outside the body");
                }
                Vector2 nose = body.Position[Joint.Nose];
                Assert.IsTrue(Inside(b, s, nose.x, nose.y), c.Name + ": the head is filled");
            }
        }

        [Test]
        public void Body_HasNoHolesAndNoStrayBlobsInEveryPose() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            const float Cell = 0.02f;
            const int Nx = 150;
            const int Ny = 130;
            for (int p = 0; p < Poses.Length; p++) {
                PoseCase c = Poses[p];
                SilhouetteBuilder b = BuildFigure(Figure(c.LeftUpper, c.LeftFore, c.RightUpper, c.RightFore, c.KneeSpread, c.AnkleSpread), s);
                bool[,] inside = new bool[Nx, Ny];
                for (int ix = 0; ix < Nx; ix++) {
                    for (int iy = 0; iy < Ny; iy++) {
                        inside[ix, iy] = Inside(b, s, (ix - Nx / 2) * Cell, (iy - 60) * Cell);
                    }
                }
                // A hole is an outside cell the outside cannot reach from the border.
                bool[,] reached = new bool[Nx, Ny];
                System.Collections.Generic.Stack<Vector2Int> stack = new System.Collections.Generic.Stack<Vector2Int>();
                for (int ix = 0; ix < Nx; ix++) {
                    stack.Push(new Vector2Int(ix, 0));
                    stack.Push(new Vector2Int(ix, Ny - 1));
                }
                for (int iy = 0; iy < Ny; iy++) {
                    stack.Push(new Vector2Int(0, iy));
                    stack.Push(new Vector2Int(Nx - 1, iy));
                }
                while (stack.Count > 0) {
                    Vector2Int cell = stack.Pop();
                    if (cell.x < 0 || cell.y < 0 || cell.x >= Nx || cell.y >= Ny || reached[cell.x, cell.y] || inside[cell.x, cell.y]) {
                        continue;
                    }
                    reached[cell.x, cell.y] = true;
                    stack.Push(new Vector2Int(cell.x + 1, cell.y));
                    stack.Push(new Vector2Int(cell.x - 1, cell.y));
                    stack.Push(new Vector2Int(cell.x, cell.y + 1));
                    stack.Push(new Vector2Int(cell.x, cell.y - 1));
                }
                int holes = 0;
                for (int ix = 0; ix < Nx; ix++) {
                    for (int iy = 0; iy < Ny; iy++) {
                        if (!inside[ix, iy] && !reached[ix, iy]) {
                            holes++;
                        }
                    }
                }
                Assert.AreEqual(0, holes, c.Name + ": the filled body must have no hollow parts");
            }
        }

        [Test]
        public void Body_TheTorsoHasAWaistAndTheLegsHaveVolume() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            SilhouetteBuilder b = BuildFigure(Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f), s);
            float hips = RunWidth(b, s, 0f, 0.02f);
            float waist = RunWidth(b, s, 0f, 0.27f);
            Assert.Greater(hips, 0.30f, "a pelvis, not a stick: " + hips);
            Assert.Less(hips, 0.46f, "and not inflated: " + hips);
            Assert.Greater(waist, 0.20f, "waist " + waist);
            Assert.Less(waist, hips, "the waist is narrower than the pelvis");

            float thigh = RunWidth(b, s, 0.09f, -0.15f);
            Assert.Greater(thigh, 0.12f, "a thigh has volume: " + thigh);
            Assert.Less(thigh, 0.26f, "thigh " + thigh);
            float calf = RunWidth(b, s, 0.10f, -0.57f);
            Assert.Greater(calf, 0.09f, "a calf has volume: " + calf);
            float ankle = RunWidth(b, s, 0.10f, -0.84f);
            Assert.Less(ankle, calf, "the ankle is narrower than the calf");
            float shoulders = RunWidth(b, s, 0f, 0.52f);
            Assert.Greater(shoulders, 0.40f, "broad shoulders: " + shoulders);
        }

        [Test]
        public void Body_TheArea_IsThatOfAPersonNotAWireframe() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            SilhouetteBuilder b = BuildFigure(Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f), s);
            const float Cell = 0.01f;
            int count = 0;
            for (float y = -1.1f; y < 0.95f; y += Cell) {
                for (float x = -0.8f; x < 0.8f; x += Cell) {
                    if (Inside(b, s, x, y)) {
                        count++;
                    }
                }
            }
            float area = count * Cell * Cell;
            // A standing adult seen from the front covers roughly 0.45-0.65 m2.
            Assert.Greater(area, 0.38f, "area " + area);
            Assert.Less(area, 0.80f, "area " + area);
        }

        [Test]
        public void Body_ARaisedArmMovesTheMaskWithIt() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            ShowcaseBody down = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            ShowcaseBody up = Figure(8f, 4f, 170f, 175f, 0.01f, 0.01f);
            SilhouetteBuilder bd = BuildFigure(down, s);
            SilhouetteBuilder bu = BuildFigure(up, s);
            Vector2 raisedWrist = up.Position[Joint.RightWrist];
            Vector2 hangingWrist = down.Position[Joint.RightWrist];
            Assert.IsTrue(Inside(bu, s, raisedWrist.x, raisedWrist.y), "the mask reaches the raised hand");
            Assert.IsFalse(Inside(bd, s, raisedWrist.x, raisedWrist.y), "and the hanging pose does not");
            Assert.IsFalse(Inside(bu, s, hangingWrist.x + 0.12f, hangingWrist.y), "the arm has left its hanging place (the space beside the hip is empty)");
            Assert.IsTrue(Inside(bu, s, up.Position[Joint.LeftWrist].x, up.Position[Joint.LeftWrist].y), "the other arm is unaffected");
        }

        [Test]
        public void Body_ALostWristOrElbowDropsOnlyThatLimbSection() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            ShowcaseBody body = Figure(60f, 90f, 60f, 90f, 0.01f, 0.01f);
            SilhouetteBuilder full = BuildFigure(body, s);
            Vector2 wrist = body.Position[Joint.RightWrist];
            Vector2 elbow = body.Position[Joint.RightElbow];
            Assert.IsTrue(Inside(full, s, wrist.x, wrist.y));

            body.Weight[Joint.RightWrist] = 0f;
            SilhouetteBuilder noWrist = BuildFigure(body, s);
            Assert.IsTrue(Inside(noWrist, s, elbow.x, elbow.y), "the upper arm stays");
            Assert.IsFalse(Inside(noWrist, s, wrist.x + 0.1f, wrist.y), "the lost forearm is not drawn out to a stale place");
            Assert.IsTrue(Inside(noWrist, s, body.Position[Joint.LeftWrist].x, body.Position[Joint.LeftWrist].y), "the other arm is untouched");

            body.Weight[Joint.RightElbow] = 0f;
            SilhouetteBuilder noElbow = BuildFigure(body, s);
            Vector2 shoulder = body.Position[Joint.RightShoulder];
            Assert.IsTrue(Inside(noElbow, s, shoulder.x, shoulder.y), "the shoulder stays");
            Assert.IsFalse(Inside(noElbow, s, elbow.x, elbow.y), "the arm is gone, not stretched to the origin");
        }

        [Test]
        public void Body_NoLandmarkAtTheOriginEverPullsAShapeAcrossTheScreen() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            ShowcaseBody body = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            // A wrist that jumped 20 m away, and a knee that is NaN: neither may produce a long shape.
            body.Position[Joint.LeftWrist] = body.Position[Joint.LeftWrist] + new Vector2(20f, 20f);
            body.Position[Joint.RightKnee] = new Vector2(float.NaN, float.NaN);
            SilhouetteBuilder b = BuildFigure(body, s);
            for (float x = -3f; x <= 3f; x += 0.1f) {
                Assert.IsFalse(Inside(b, s, x, 2.5f), "nothing far above the body at x=" + x);
                Assert.IsFalse(Inside(b, s, x, -2.5f), "nothing far below at x=" + x);
            }
            Assert.IsFalse(float.IsNaN(b.Distance(new Vector2(0f, 0f), s.shapeBlend)));
        }

        [Test]
        public void Body_TheOutlineIsASeparateOptionFromTheFill() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            Assert.IsTrue(s.outlineEnabled, "on by default: a filled body with a subtle edge");
            Assert.Greater(s.outlineIntensity, 0f);
            // Switching it off does not touch the mask: the shape comes from the builder alone.
            SilhouetteBuilder a = BuildFigure(Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f), s);
            float before = a.Distance(new Vector2(0.05f, 0.2f), s.shapeBlend);
            s.outlineEnabled = false;
            SilhouetteBuilder c = BuildFigure(Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f), s);
            Assert.AreEqual(before, c.Distance(new Vector2(0.05f, 0.2f), s.shapeBlend), 1e-6f);
        }

        [Test]
        public void Body_TheBuilderAgreesWithTheShaderOnTheMaskCapacity() {
            string shader = System.IO.File.ReadAllText("Assets/Games/viitorx-vrm-avtar-unity/Showcase/ChromaticRain/Shaders/ChromaticRain_SilhouetteMask.shader");
            Assert.IsTrue(shader.Contains("#define MAX_CAPS " + SilhouetteBuilder.MaxCaps), "the shader's array size must equal SilhouetteBuilder.MaxCaps");
        }

        // ------------------------------------------------------------------ the skins

        [Test]
        public void Skins_TheBuiltInSetHasSeveralDistinctNamedSkins() {
            SilhouetteSkinSet set = SilhouetteSkinSet.CreateDefault();
            Assert.GreaterOrEqual(set.Count, 5, "a handful of looks ship ready to use");
            System.Collections.Generic.HashSet<string> names = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < set.Count; i++) {
                Assert.IsFalse(string.IsNullOrEmpty(set.NameOf(i)), "every skin has a name");
                Assert.IsTrue(names.Add(set.NameOf(i)), "names are unique: " + set.NameOf(i));
            }
            Assert.AreEqual("Anatomical", set.NameOf(0), "the reference figure is the first skin");
            Assert.AreNotEqual(set.Get(0).waistRadius, set.Get(1).waistRadius, "the skins differ in proportions");
            Assert.AreNotEqual(set.Get(0).outlineColor, set.Get(3).outlineColor, "and in look");
            Object.DestroyImmediate(set);
        }

        [Test]
        public void Skins_IndexingWrapsAndNeverReturnsNull() {
            SilhouetteSkinSet set = SilhouetteSkinSet.CreateDefault();
            int n = set.Count;
            Assert.AreSame(set.Get(0), set.Get(n), "past the end wraps to the start");
            Assert.AreSame(set.Get(n - 1), set.Get(-1), "before the start wraps to the end");
            Assert.AreEqual(0, set.Next(n - 1));
            Assert.AreEqual(1, set.Next(0));
            Assert.IsTrue(set.StartSkin >= 0 && set.StartSkin < n);
            Object.DestroyImmediate(set);
            SilhouetteSkinSet empty = ScriptableObject.CreateInstance<SilhouetteSkinSet>();
            System.Reflection.FieldInfo list = typeof(SilhouetteSkinSet).GetField("skins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            list.SetValue(empty, new System.Collections.Generic.List<SilhouetteSkin>());
            Assert.IsNotNull(empty.Get(3), "an empty set still yields a usable skin");
            Assert.AreEqual(0, empty.StartSkin);
            Object.DestroyImmediate(empty);
        }

        [Test]
        public void Skins_ACloneIsIndependentOfTheOriginal() {
            SilhouetteSkin original = SilhouetteSkin.Anatomical();
            SilhouetteSkin copy = original.Clone();
            copy.name = "Mine";
            copy.waistRadius = 0.2f;
            Assert.AreEqual("Anatomical", original.name);
            Assert.AreEqual(0.145f, original.waistRadius, 1e-6f, "editing the copy leaves the original alone");
        }

        [Test]
        public void Skins_EveryBuiltInSkinDrawsAFilledBodyWithNoHolesInEveryPose() {
            System.Collections.Generic.List<SilhouetteSkin> all = SilhouetteSkin.BuiltIns();
            for (int k = 0; k < all.Count; k++) {
                SilhouetteSkin s = all[k];
                for (int p = 0; p < Poses.Length; p++) {
                    PoseCase c = Poses[p];
                    ShowcaseBody body = Figure(c.LeftUpper, c.LeftFore, c.RightUpper, c.RightFore, c.KneeSpread, c.AnkleSpread);
                    SilhouetteBuilder b = BuildFigure(body, s);
                    Assert.Greater(b.CapCount, 20, s.name + " / " + c.Name + ": the body is built");
                    int[] joints = new int[] { Joint.Nose, Joint.LeftShoulder, Joint.RightShoulder, Joint.LeftElbow, Joint.RightElbow,
                                               Joint.LeftWrist, Joint.RightWrist, Joint.LeftKnee, Joint.RightKnee, Joint.LeftAnkle, Joint.RightAnkle };
                    for (int j = 0; j < joints.Length; j++) {
                        Vector2 pt = body.Position[joints[j]];
                        Assert.IsTrue(Inside(b, s, pt.x, pt.y), s.name + " / " + c.Name + ": joint " + joints[j] + " is inside the body");
                    }
                    Vector2 hipMid = (body.Position[Joint.LeftHip] + body.Position[Joint.RightHip]) * 0.5f;
                    Vector2 shoulderMid = (body.Position[Joint.LeftShoulder] + body.Position[Joint.RightShoulder]) * 0.5f;
                    for (int t = 0; t <= 10; t++) {
                        Vector2 pt = Vector2.Lerp(hipMid, shoulderMid, t / 10f);
                        Assert.IsTrue(Inside(b, s, pt.x, pt.y), s.name + " / " + c.Name + ": the spine is inside the body");
                    }
                }
            }
        }

        [Test]
        public void Skins_ASlimBodyIsNarrowerAndAnAthleticOneBroaderThanTheAnatomicalOne() {
            ShowcaseBody body = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            float slim = RunWidth(BuildFigure(body, SilhouetteSkin.Slim()), SilhouetteSkin.Slim(), 0f, 0.02f);
            float normal = RunWidth(BuildFigure(body, SilhouetteSkin.Anatomical()), SilhouetteSkin.Anatomical(), 0f, 0.02f);
            float athletic = RunWidth(BuildFigure(body, SilhouetteSkin.Athletic()), SilhouetteSkin.Athletic(), 0f, 0.02f);
            Assert.Less(slim, normal, "pelvis width: slim " + slim + " < anatomical " + normal);
            Assert.Less(normal, athletic, "pelvis width: anatomical " + normal + " < athletic " + athletic);
        }

        [Test]
        public void Skins_TheMinimalSkinHasNoOutlineAndTheOthersDo() {
            Assert.IsFalse(SilhouetteSkin.Minimal().outlineEnabled);
            Assert.IsTrue(SilhouetteSkin.Anatomical().outlineEnabled);
            Assert.IsTrue(SilhouetteSkin.Neon().outlineEnabled);
            Assert.Greater(SilhouetteSkin.Neon().outlineIntensity, SilhouetteSkin.Anatomical().outlineIntensity, "neon is brighter");
        }

        [Test]
        public void Skins_TheShaderCanHoldTheMostShapesABodyUses() {
            ShowcaseBody body = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            SilhouetteBuilder b = BuildFigure(body, SilhouetteSkin.Anatomical());
            Assert.Less(b.CapCount, SilhouetteBuilder.MaxCaps, "a full body leaves headroom in the shader's array");
        }

        // ------------------------------------------------------------------ F-60: a body the camera cuts off

        private static readonly int[] BelowTheFrame = {
            Joint.LeftHip, Joint.RightHip, Joint.LeftKnee, Joint.RightKnee, Joint.LeftAnkle, Joint.RightAnkle,
            Joint.LeftHeel, Joint.RightHeel, Joint.LeftToe, Joint.RightToe
        };

        // What the sidecar sends for a person at a laptop: the head, shoulders and arms present, everything from the hips
        // down absent ([0, 0, 0, 0]), and the root anchored on the shoulders (or, for the head-only tier, no shoulders).
        private static void StepCutOff(Rig rig, PoseAnchor anchor, bool shouldersToo) {
            rig.Sim.Tick(Dt);
            for (int i = 0; i < BelowTheFrame.Length; i++) {
                rig.Sim.Frame.SetLandmark(BelowTheFrame[i], new PoseLandmark(Vector3.zero, 0f));
            }
            if (shouldersToo) {
                rig.Sim.Frame.SetLandmark(Joint.LeftShoulder, new PoseLandmark(Vector3.zero, 0f));
                rig.Sim.Frame.SetLandmark(Joint.RightShoulder, new PoseLandmark(Vector3.zero, 0f));
            }
            rig.Sim.Frame.SetAnchor(anchor);
            Vector3 origin = new Vector3(rig.Sim.OffsetX, SimulatedPoseSource.HipHeight, 0f);
            rig.Pose.Fill(rig.Sim.Frame, origin, 1f, Dt);
            rig.Left.Fill(rig.Sim.Hands, true, origin, 1f);
            rig.Right.Fill(rig.Sim.Hands, false, origin, 1f);
            rig.Adapter.Update(rig.Body, rig.Pose, rig.Left, rig.Right, null, true, Dt, rig.Settings, rig.Mapper);
        }

        private static void RunCutOff(Rig rig, PoseAnchor anchor, bool shouldersToo, float seconds) {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) {
                StepCutOff(rig, anchor, shouldersToo);
            }
        }

        [Test]
        public void CutOff_AnEstimatedRootKeepsTheBodyTrackedAfterTheHipsLeaveThePicture() {
            Rig rig = new Rig();
            rig.Run(2f);
            Assert.IsTrue(rig.Body.CoreFresh);
            Assert.IsFalse(rig.Body.RootEstimated);
            RunCutOff(rig, PoseAnchor.Shoulders, false, 2.5f);
            Assert.IsTrue(rig.Body.CoreFresh, "the shoulders are measured and the sender placed the hip: a person, not a loss");
            Assert.IsTrue(rig.Body.RootEstimated);
            Vector2 shoulderMid = (rig.Body.Position[Joint.LeftShoulder] + rig.Body.Position[Joint.RightShoulder]) * 0.5f;
            float below = shoulderMid.y - rig.Body.HipCentre.y;
            Assert.Greater(below, 0.3f, "the estimated hip centre sits well below the shoulders: " + below);
            Assert.Less(below, 0.8f);
            Assert.IsFalse(float.IsNaN(rig.Body.HipCentre.x) || float.IsNaN(rig.Body.HipCentre.y));
            Assert.Less(rig.Body.Weight[Joint.LeftHip], 0.05f, "no hip joint is drawn: it was never measured");
        }

        [Test]
        public void CutOff_ABodyThatIsCutOffFromTheFirstFrameIsDrawn() {
            Rig rig = new Rig();
            RunCutOff(rig, PoseAnchor.Shoulders, false, 0.6f);
            Assert.IsTrue(rig.Body.CoreFresh);
            Assert.Greater(rig.Body.Weight[Joint.LeftShoulder], 0.9f);
            Assert.Greater(rig.Body.Weight[Joint.Nose], 0.9f);
            Assert.Greater(rig.Body.ShoulderWidth, 0.2f, "the shoulder width is measured, so the figure is scaled to this person");
        }

        [Test]
        public void CutOff_WithoutTheSendersAnchorAHipLessFrameIsStillNotACore() {
            // The pre-F-60 rule, kept for every sender that does not say: no hip, no torso. Nothing is invented.
            Rig rig = new Rig();
            rig.Run(2f);
            RunCutOff(rig, PoseAnchor.Hips, false, 2.5f);
            Assert.IsFalse(rig.Body.CoreFresh, "an unflagged frame without hips is not a measured core");
            Assert.IsFalse(rig.Body.RootEstimated);
        }

        [Test]
        public void CutOff_AHeadOnlyFrameIsNotATorso() {
            Rig rig = new Rig();
            rig.Run(2f);
            RunCutOff(rig, PoseAnchor.Head, true, 2.5f);
            Assert.IsFalse(rig.Body.CoreFresh, "no shoulders: there is a person to acknowledge but no torso to draw");
        }

        private static ShowcaseBody CutOffFigure(bool rootEstimated) {
            ShowcaseBody body = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            for (int i = 0; i < BelowTheFrame.Length; i++) {
                body.Weight[BelowTheFrame[i]] = 0f;
                body.Fresh[BelowTheFrame[i]] = false;
            }
            body.RootEstimated = rootEstimated;
            body.HipCentre = Vector2.zero;
            return body;
        }

        [Test]
        public void CutOff_TheSilhouetteHasATorsoBuiltToTheEstimatedHipCentre() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            SilhouetteBuilder b = BuildFigure(CutOffFigure(true), s);
            Assert.IsTrue(Inside(b, s, 0f, 0.25f), "the chest");
            Assert.IsTrue(Inside(b, s, 0f, 0.0f), "the waist, where the hips would be");
            Assert.IsTrue(Inside(b, s, 0f, 0.60f), "the neck and head");
            Assert.IsTrue(Inside(b, s, -0.30f, 0.30f) || Inside(b, s, -0.26f, 0.30f), "an arm hangs beside the body");
            Assert.Less(b.CapCount, SilhouetteBuilder.MaxCaps);
        }

        [Test]
        public void CutOff_TheBodyContinuesBelowTheScreenWhenTheSkinSaysSo() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            Assert.IsTrue(s.extendCutOffBody, "on by default: a person at a desk does not end at the waist");
            SilhouetteBuilder b = BuildFigure(CutOffFigure(true), s);
            Assert.IsTrue(Inside(b, s, 0f, -0.8f), "below the pelvis, where there is no leg");
            Assert.IsTrue(Inside(b, s, 0f, -1.6f), "and on down past the bottom of the view");
            Assert.IsFalse(Inside(b, s, 0.5f, -1.2f), "but only as wide as the hips");
            // No notch where the column meets the pelvis: the width through the junction is the width below it.
            float below = RunWidth(b, s, 0f, -0.6f);
            Assert.Greater(below, 0.28f, "a pelvis-wide column: " + below);
            for (float y = 0f; y >= -0.30f; y -= 0.025f) {
                Assert.GreaterOrEqual(RunWidth(b, s, 0f, y), below * 0.93f, "no pinch at y=" + y + " (the crotch is near -0.10)");
            }
            s.extendCutOffBody = false;
            SilhouetteBuilder bare = BuildFigure(CutOffFigure(true), s);
            Assert.IsFalse(Inside(bare, s, 0f, -1.2f), "off: the torso ends at the pelvis");
            Assert.IsTrue(Inside(bare, s, 0f, 0.25f), "...and is still there");
        }

        [Test]
        public void CutOff_HipsOnTheEdgeOfThePictureWithNoLegsAreExtendedToo() {
            // The hips flicker in and out of a picture whose edge cuts them, so the sender's anchor is not what says the
            // body is cut off: the absent legs are.
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            ShowcaseBody body = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            int[] legs = { Joint.LeftKnee, Joint.RightKnee, Joint.LeftAnkle, Joint.RightAnkle, Joint.LeftHeel, Joint.RightHeel, Joint.LeftToe, Joint.RightToe };
            for (int i = 0; i < legs.Length; i++) {
                body.Weight[legs[i]] = 0f;
                body.Fresh[legs[i]] = false;
            }
            body.RootEstimated = false;
            SilhouetteBuilder b = BuildFigure(body, s);
            Assert.IsTrue(Inside(b, s, 0f, 0.25f), "the torso is built from the hip joints that are there");
            Assert.IsTrue(Inside(b, s, 0f, -1.6f), "and continues, because no leg is in the picture");
            body.Weight[Joint.LeftKnee] = 1f;
            body.Weight[Joint.LeftAnkle] = 1f;
            SilhouetteBuilder oneLeg = BuildFigure(body, s);
            Assert.IsFalse(Inside(oneLeg, s, 0f, -1.6f), "one leg is back: not cut off");
        }

        [Test]
        public void CutOff_AHipLessBodyWithNoSenderAnchorGetsNoInventedTorso() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            SilhouetteBuilder b = BuildFigure(CutOffFigure(false), s);
            Assert.IsFalse(Inside(b, s, 0f, 0.0f), "no estimated root, no hips: no torso is made up");
            Assert.IsFalse(Inside(b, s, 0f, -1.2f));
        }

        [Test]
        public void CutOff_AFullBodyIsNotExtended() {
            SilhouetteSkin s = SilhouetteSkin.Anatomical();
            ShowcaseBody body = Figure(8f, 4f, 8f, 4f, 0.01f, 0.01f);
            body.RootEstimated = false;
            SilhouetteBuilder b = BuildFigure(body, s);
            Assert.IsFalse(Inside(b, s, 0f, -1.6f), "legs end at the feet; nothing continues below them");
        }

        private sealed class ShowcaseSettingsAndMapper {
            public readonly ChromaticRainSettings Settings = NewSettings();
            public readonly ScreenSpaceMapper Mapper = new ScreenSpaceMapper();

            public ShowcaseSettingsAndMapper() {
                Mapper.Configure(Settings, 16f / 9f);
            }
        }
    }
}
