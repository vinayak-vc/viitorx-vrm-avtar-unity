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
                builder.Build(rig.Body, rig.Settings, Dt);
            }
            return builder;
        }

        [Test]
        public void Silhouette_FullBodyHasEverySegmentAndAHead() {
            Rig rig = new Rig();
            rig.Run(2f);
            SilhouetteBuilder b = BuildFrom(rig);
            Assert.AreEqual(16, b.CapCount);
            Assert.Greater(b.HeadDirection.z, 0.99f);
        }

        [Test]
        public void Silhouette_LimbsStartExactlyWhereTheirParentJointIs() {
            Rig rig = new Rig();
            rig.Run(2f);
            SilhouetteBuilder b = BuildFrom(rig);
            bool foundUpperArm = false;
            Vector2 shoulder = rig.Body.Position[Joint.LeftShoulder];
            Vector2 elbow = rig.Body.Position[Joint.LeftElbow];
            for (int i = 0; i < b.CapCount; i++) {
                Vector2 a = new Vector2(b.Caps[i].x, b.Caps[i].y);
                Vector2 e = new Vector2(b.Caps[i].z, b.Caps[i].w);
                if ((a - shoulder).magnitude < 1e-4f && (e - elbow).magnitude < 1e-4f) {
                    foundUpperArm = true;
                }
            }
            Assert.IsTrue(foundUpperArm, "the upper arm runs from the shoulder to the elbow with no gap");
        }

        [Test]
        public void Silhouette_AbsurdLimbLengthIsClampedNotDrawn() {
            Rig rig = new Rig();
            rig.Sim.stationary = true;
            rig.Run(2f);
            rig.Body.Position[Joint.LeftElbow] = rig.Body.Position[Joint.LeftShoulder] + new Vector2(20f, 20f);
            SilhouetteBuilder b = new SilhouetteBuilder();
            b.Build(rig.Body, rig.Settings, Dt);
            // The longest believable segment is a leg, 0.62 m, widened by at most 1.35.
            float limit = rig.Settings.maxLegSegment * 1.35f + 0.05f;
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
            rig.Body.Weight[Joint.LeftKnee] = 0f;
            SilhouetteBuilder b = new SilhouetteBuilder();
            b.Build(rig.Body, rig.Settings, Dt);
            Assert.AreEqual(14, b.CapCount, "thigh and shin of that leg only");
        }

        [Test]
        public void Silhouette_NoBodyBuildsNothing() {
            Rig rig = new Rig();
            SilhouetteBuilder b = new SilhouetteBuilder();
            b.Build(rig.Body, rig.Settings, Dt);
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
    }
}
