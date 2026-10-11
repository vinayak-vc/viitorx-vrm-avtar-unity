using ChromaticRain;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

using Joint = ChromaticRain.Joint;

namespace FluidDisruption.Tests {
    /// <summary>
    /// EditMode tests for the showcase's pure logic: the movement estimator, the A-F state controller,
    /// the interaction-point tracker (hand loss, stale input), the demo programme and the shared
    /// tracking-source status rules. Scene, shader and FluXY behaviour are verified in Play mode.
    /// </summary>
    public sealed class FluidDisruptionTests {
        private const float Dt = 1f / 60f;

        private static FluidDisruptionSettings NewSettings() {
            return ScriptableObject.CreateInstance<FluidDisruptionSettings>();
        }

        // ------------------------------------------------------------------ movement estimator

        private static void Move(MovementImpulseEstimator e, ref Vector2 p, Vector2 velocity, float seconds,
                                 float dt, MovementTuning t) {
            int steps = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++) {
                p += velocity * dt;
                e.Update(true, p, dt, t);
            }
        }

        [Test]
        public void Estimator_JitterInsideTheDeadZoneGivesNoDisturbance() {
            FluidDisruptionSettings s = NewSettings();
            MovementImpulseEstimator e = new MovementImpulseEstimator();
            Vector2 p = new Vector2(0.3f, 0.5f);
            float maxIntensity = 0f;
            for (int i = 0; i < 600; i++) {
                // +-1 mm of tracking noise at 60 Hz is about 0.06 m/s, below the 0.12 m/s dead zone.
                float noise = (i % 2 == 0 ? 0.0005f : -0.0005f);
                e.Update(true, p + new Vector2(noise, -noise), Dt, s.handMovement);
                maxIntensity = Mathf.Max(maxIntensity, e.Intensity);
            }
            Assert.AreEqual(0f, maxIntensity, 1e-6f);
        }

        [Test]
        public void Estimator_StationaryPointHasNoIntensity() {
            FluidDisruptionSettings s = NewSettings();
            MovementImpulseEstimator e = new MovementImpulseEstimator();
            Vector2 p = Vector2.zero;
            Move(e, ref p, new Vector2(1f, 0f), 1f, Dt, s.handMovement);
            Assert.Greater(e.Intensity, 0.2f);
            Move(e, ref p, Vector2.zero, 1.5f, Dt, s.handMovement);
            Assert.AreEqual(0f, e.Intensity, 1e-3f);
            Assert.AreEqual(0f, e.Speed, 0.02f);
        }

        [Test]
        public void Estimator_FasterMovementGivesAStrongerAndSmoothlyRisingResponse() {
            FluidDisruptionSettings s = NewSettings();
            float previous = -1f;
            float[] speeds = { 0.05f, 0.3f, 0.7f, 1.2f, 2.0f, 4.0f };
            for (int i = 0; i < speeds.Length; i++) {
                MovementImpulseEstimator e = new MovementImpulseEstimator();
                Vector2 p = Vector2.zero;
                Move(e, ref p, new Vector2(speeds[i], 0f), 1.5f, Dt, s.handMovement);
                Assert.GreaterOrEqual(e.Intensity, previous, "intensity must not drop as speed rises (" + speeds[i] + ")");
                previous = e.Intensity;
            }
            Assert.AreEqual(1f, previous, 1e-3f, "a fast gesture reaches full strength");
        }

        [Test]
        public void Estimator_UsesTheRealElapsedTimeNotTheFrameCount() {
            FluidDisruptionSettings s = NewSettings();
            MovementImpulseEstimator a = new MovementImpulseEstimator();
            MovementImpulseEstimator b = new MovementImpulseEstimator();
            Vector2 pa = Vector2.zero;
            Vector2 pb = Vector2.zero;
            Move(a, ref pa, new Vector2(1f, 0f), 1.5f, 1f / 30f, s.handMovement);
            Move(b, ref pb, new Vector2(1f, 0f), 1.5f, 1f / 120f, s.handMovement);
            Assert.AreEqual(1f, a.Speed, 0.03f);
            Assert.AreEqual(1f, b.Speed, 0.03f);
        }

        [Test]
        public void Estimator_SpeedIsClamped_AndAGlitchJumpIsIgnored() {
            FluidDisruptionSettings s = NewSettings();
            MovementImpulseEstimator e = new MovementImpulseEstimator();
            Vector2 p = Vector2.zero;
            Move(e, ref p, new Vector2(9f, 0f), 1f, Dt, s.handMovement);
            Assert.LessOrEqual(e.Speed, s.handMovement.maxSpeed + 1e-3f);

            MovementImpulseEstimator g = new MovementImpulseEstimator();
            g.Update(true, Vector2.zero, Dt, s.handMovement);
            g.Update(true, new Vector2(2f, 0f), Dt, s.handMovement);
            Assert.AreEqual(0f, g.Intensity, 1e-4f, "a 2 m jump in one frame is a glitch, not a gesture");
            Assert.AreEqual(0f, g.Speed, 1e-4f);
        }

        [Test]
        public void Estimator_AnInvalidSampleResets_AndReacquisitionCarriesNoVelocity() {
            FluidDisruptionSettings s = NewSettings();
            MovementImpulseEstimator e = new MovementImpulseEstimator();
            Vector2 p = Vector2.zero;
            Move(e, ref p, new Vector2(1.5f, 0f), 1f, Dt, s.handMovement);
            Assert.IsTrue(e.HasSample);
            Assert.Greater(e.Intensity, 0.3f);

            e.Update(false, Vector2.zero, Dt, s.handMovement);
            Assert.IsFalse(e.HasSample);
            Assert.AreEqual(0f, e.Intensity);
            Assert.AreEqual(Vector2.zero, e.Velocity);

            // The hand comes back 1.5 m away: it must start there, with no velocity at all.
            e.Update(true, new Vector2(1.5f, 0.4f), Dt, s.handMovement);
            Assert.IsTrue(e.HasSample);
            Assert.AreEqual(0f, e.Intensity);
            Assert.AreEqual(0f, e.Speed);
            Assert.AreEqual(new Vector2(1.5f, 0.4f), e.Position);
        }

        [Test]
        public void Estimator_NonFinitePositionsAreIgnored() {
            FluidDisruptionSettings s = NewSettings();
            MovementImpulseEstimator e = new MovementImpulseEstimator();
            e.Update(true, Vector2.zero, Dt, s.handMovement);
            e.Update(true, new Vector2(float.NaN, 0f), Dt, s.handMovement);
            e.Update(true, new Vector2(0f, float.PositiveInfinity), Dt, s.handMovement);
            Assert.AreEqual(Vector2.zero, e.Position);
            Assert.IsFalse(float.IsNaN(e.Speed));
        }

        // ------------------------------------------------------------------ state controller

        private static void Run(FluidDisruptionStateController c, bool tracked, float l, float r, float t,
                                float seconds, FluidDisruptionSettings s) {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) {
                c.Tick(tracked, l, r, t, Dt, s);
            }
        }

        [Test]
        public void State_NobodyTracked_StaysCalmWithAClosedGate() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, false, 0f, 0f, 0f, 5f, s);
            Assert.AreEqual(DisruptionState.Calm, c.State);
            Assert.AreEqual(0f, c.Gate);
            Assert.AreEqual(0f, c.Pulse);
        }

        [Test]
        public void State_ABriefGlimpseIsNotAPerson() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0f, 0f, 0f, s.enterSeconds * 0.5f, s);
            Assert.AreEqual(DisruptionState.Calm, c.State);
        }

        [Test]
        public void State_ConfirmedPerson_GivesOneAppearedFlagAndAGentlePulse() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            int appeared = 0;
            float maxPulse = 0f;
            float maxGateStep = 0f;
            float lastGate = 0f;
            int steps = Mathf.RoundToInt((s.enterSeconds + s.pulseSeconds + 0.5f) / Dt);
            for (int i = 0; i < steps; i++) {
                c.Tick(true, 0f, 0f, 0f, Dt, s);
                if (c.PersonAppeared) {
                    appeared++;
                }
                maxPulse = Mathf.Max(maxPulse, c.Pulse);
                maxGateStep = Mathf.Max(maxGateStep, Mathf.Abs(c.Gate - lastGate));
                lastGate = c.Gate;
            }
            Assert.AreEqual(1, appeared);
            Assert.Greater(maxPulse, 0.9f);
            Assert.LessOrEqual(maxPulse, 1f);
            Assert.LessOrEqual(maxGateStep, Dt / s.gateSeconds + 1e-4f, "the gate must ease, never snap");
            Assert.AreEqual(DisruptionState.Settling, c.State, "a person who stands still settles after the swirl");
        }

        [Test]
        public void State_OneHandMoving_IsActive_BothHands_IsInteracting() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0.6f, 0f, 0f, s.enterSeconds + s.pulseSeconds + 0.3f, s);
            Assert.AreEqual(DisruptionState.Active, c.State);
            Run(c, true, 0.6f, 0.6f, 0f, 0.3f, s);
            Assert.AreEqual(DisruptionState.Interacting, c.State);
            Run(c, true, 0f, 0.6f, 0f, 0.3f, s);
            Assert.AreEqual(DisruptionState.Active, c.State);
        }

        [Test]
        public void State_StandingStill_SettlesAfterTheHoldAndIsNotResetByIt() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0.6f, 0.6f, 0.4f, s.enterSeconds + s.pulseSeconds + 0.5f, s);
            Assert.AreEqual(DisruptionState.Interacting, c.State);
            Run(c, true, 0f, 0f, 0f, s.settleHoldSeconds * 0.5f, s);
            Assert.AreNotEqual(DisruptionState.Settling, c.State, "not before the hold time");
            Run(c, true, 0f, 0f, 0f, s.settleHoldSeconds + 0.3f, s);
            Assert.AreEqual(DisruptionState.Settling, c.State);
            Assert.AreEqual(1f, c.Gate, 1e-3f, "the gate stays open while the person is present");
            Run(c, true, 0.7f, 0f, 0f, 0.2f, s);
            Assert.AreEqual(DisruptionState.Active, c.State, "moving again wakes it");
        }

        [Test]
        public void State_Loss_ClosesTheGateThenReturnsToCalm_AndTrackingReturnRestartsFromTheNewPose() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0.6f, 0f, 0f, s.enterSeconds + s.pulseSeconds + 0.5f, s);
            Assert.AreEqual(DisruptionState.Active, c.State);

            Run(c, false, 0f, 0f, 0f, s.lostTimeoutSeconds + 0.1f, s);
            Assert.AreEqual(DisruptionState.Lost, c.State);
            Run(c, false, 0f, 0f, 0f, s.gateSeconds + 0.1f, s);
            Assert.AreEqual(0f, c.Gate, 1e-4f);
            Run(c, false, 0f, 0f, 0f, s.lostHoldSeconds + 0.2f, s);
            Assert.AreEqual(DisruptionState.Calm, c.State);

            // And back again.
            bool appeared = false;
            int steps = Mathf.RoundToInt((s.enterSeconds + 0.1f) / Dt);
            for (int i = 0; i < steps; i++) {
                c.Tick(true, 0f, 0f, 0f, Dt, s);
                appeared |= c.PersonAppeared;
            }
            Assert.IsTrue(appeared);
            Assert.AreEqual(DisruptionState.Detected, c.State);
        }

        [Test]
        public void State_ReturnWhileStillLost_AlsoRestartsFromTheNewPose() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0.6f, 0f, 0f, s.enterSeconds + s.pulseSeconds + 0.5f, s);
            Run(c, false, 0f, 0f, 0f, s.lostTimeoutSeconds + 0.2f, s);
            Assert.AreEqual(DisruptionState.Lost, c.State);
            bool appeared = false;
            int steps = Mathf.RoundToInt((s.enterSeconds + 0.1f) / Dt);
            for (int i = 0; i < steps; i++) {
                c.Tick(true, 0f, 0f, 0f, Dt, s);
                appeared |= c.PersonAppeared;
            }
            Assert.IsTrue(appeared);
            Assert.AreEqual(DisruptionState.Detected, c.State);
        }

        [Test]
        public void State_TheGateStartsClosingTheMomentTrackingDrops_BeforeTheLostTimeout() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0.6f, 0f, 0f, s.enterSeconds + s.pulseSeconds + 0.5f, s);
            Assert.AreEqual(1f, c.Gate, 1e-3f);
            Run(c, false, 0f, 0f, 0f, s.lostTimeoutSeconds * 0.4f, s);
            Assert.AreNotEqual(DisruptionState.Lost, c.State, "still within the timeout");
            Assert.Less(c.Gate, 0.95f, "no new impulse may be injected from a pose that stopped arriving");
            Run(c, false, 0f, 0f, 0f, s.gateSeconds, s);
            Assert.AreEqual(0f, c.Gate, 1e-4f);
        }

        [Test]
        public void State_AShortDropoutDoesNotLoseThePerson() {
            FluidDisruptionSettings s = NewSettings();
            FluidDisruptionStateController c = new FluidDisruptionStateController();
            Run(c, true, 0.6f, 0f, 0f, s.enterSeconds + s.pulseSeconds + 0.5f, s);
            Run(c, false, 0f, 0f, 0f, s.lostTimeoutSeconds * 0.5f, s);
            Run(c, true, 0.6f, 0f, 0f, 0.3f, s);
            Assert.AreEqual(DisruptionState.Active, c.State);
        }

        // ------------------------------------------------------------------ interaction points

        private sealed class Rig {
            public readonly FluidDisruptionSettings Settings = NewSettings();
            public readonly SimulatedPoseSource Sim = new SimulatedPoseSource();
            public readonly FluidDisruptionDemoPoseSource Demo = new FluidDisruptionDemoPoseSource();
            public readonly ShowcaseBody Body = new ShowcaseBody();
            public readonly ShowcasePoseAdapter Adapter = new ShowcasePoseAdapter();
            public readonly ScreenSpaceMapper Mapper = new ScreenSpaceMapper();
            public readonly InteractionPointTracker Points = new InteractionPointTracker();
            public readonly SkeletonPose Pose = new SkeletonPose();
            public readonly HandPose Left = new HandPose();
            public readonly HandPose Right = new HandPose();

            public Rig() {
                Mapper.Configure(Settings, 16f / 9f);
                Adapter.Reset(Body);
                Demo.Restart();
                Demo.segments = new DemoSegment[] {
                    new DemoSegment("still", 100f, true, 0f, 0f, 0f, 0f, 0f, FluidDisruptionDemoPoseSource.RestLift)
                };
            }

            // One frame through the same path the controller uses: simulated figure -> SkeletonPose and
            // HandPose -> adapter -> interaction points.
            public void Step(bool follow) {
                Demo.Tick(Dt, Sim);
                Sim.Tick(Dt);
                Vector3 origin = new Vector3(Sim.OffsetX, SimulatedPoseSource.HipHeight, 0f);
                Pose.Fill(Sim.Frame, origin, 1f, Dt);
                Left.Fill(Sim.Hands, true, origin, 1f);
                Right.Fill(Sim.Hands, false, origin, 1f);
                Adapter.Update(Body, Pose, Left, Right, null, Sim.StreamActive, Dt, Settings, Mapper);
                Points.Update(Body, follow && Sim.StreamActive && Body.CoreFresh, Dt, Settings);
            }

            public void Run(float seconds, bool follow) {
                int steps = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < steps; i++) {
                    Step(follow);
                }
            }
        }

        private static Rig NewRig(DemoSegment segment) {
            Rig rig = new Rig();
            rig.Demo.segments = new DemoSegment[] { segment };
            rig.Run(1.2f, true);
            rig.Adapter.RequestSnap();
            rig.Run(0.2f, true);
            rig.Points.Reset();
            return rig;
        }

        [Test]
        public void Points_AStillFigureHasNoMovementOnAnyPoint() {
            Rig rig = NewRig(new DemoSegment("still", 100f, true, 0f, 0f, 0f, 0f, 0f, FluidDisruptionDemoPoseSource.RestLift));
            float max = 0f;
            for (int i = 0; i < 300; i++) {
                rig.Step(true);
                max = Mathf.Max(max, rig.Points.Left.Intensity, rig.Points.Right.Intensity, rig.Points.Torso.Intensity);
            }
            Assert.IsTrue(rig.Points.Left.HasSample && rig.Points.Right.HasSample && rig.Points.Torso.HasSample);
            Assert.Less(max, 0.15f, "standing still must add (almost) no disturbance");
        }

        [Test]
        public void Points_TheLeftHandMovingDoesNotMoveTheRightHand() {
            Rig rig = NewRig(new DemoSegment("left", 100f, true, 0f, 0.6f, 0f, 0.35f, 0f, 0.45f));
            float left = 0f;
            float right = 0f;
            for (int i = 0; i < 480; i++) {
                rig.Step(true);
                left = Mathf.Max(left, rig.Points.Left.Intensity);
                right = Mathf.Max(right, rig.Points.Right.Intensity);
            }
            Assert.Greater(left, 0.3f);
            Assert.Less(right, 0.1f);
        }

        [Test]
        public void Points_AFastHandIsStrongerThanASlowOne() {
            Rig slow = NewRig(new DemoSegment("slow", 100f, true, 0f, 0f, 0.25f, 0.3f, 0f, 0.45f));
            Rig fast = NewRig(new DemoSegment("fast", 100f, true, 0f, 0f, 1.5f, 0.38f, 0f, 0.45f));
            float slowMax = 0f;
            float fastMax = 0f;
            for (int i = 0; i < 480; i++) {
                slow.Step(true);
                fast.Step(true);
                slowMax = Mathf.Max(slowMax, slow.Points.Right.Intensity);
                fastMax = Mathf.Max(fastMax, fast.Points.Right.Intensity);
            }
            Assert.Greater(fastMax, slowMax + 0.3f);
            Assert.Greater(fastMax, 0.8f);
        }

        [Test]
        public void Points_TravellingSidewaysMovesTheTorso() {
            Rig rig = NewRig(new DemoSegment("travel", 100f, true, 0.9f, 0f, 0f, 0f, 0f, FluidDisruptionDemoPoseSource.RestLift));
            float torso = 0f;
            for (int i = 0; i < 720; i++) {
                rig.Step(true);
                torso = Mathf.Max(torso, rig.Points.Torso.Intensity);
            }
            Assert.Greater(torso, 0.25f);
        }

        [Test]
        public void Points_ALostHandLandmark_StopsItsTrackAtOnce_AndRestartsWithoutAJump() {
            Rig rig = NewRig(new DemoSegment("left", 100f, true, 0f, 0.6f, 0f, 0.35f, 0f, 0.45f));
            rig.Run(1f, true);
            Assert.IsTrue(rig.Points.Left.HasSample);

            long mask = (1L << Joint.LeftElbow) | (1L << Joint.LeftWrist);
            rig.Sim.lostJointMask = mask;
            rig.Run(0.1f, true);
            Assert.IsFalse(rig.Points.Left.HasSample, "no wrist measurement, no track (and no held position is driven)");
            Assert.AreEqual(0f, rig.Points.Left.Intensity);
            Assert.AreEqual(Vector2.zero, rig.Points.Left.Velocity);
            Assert.IsTrue(rig.Points.Torso.HasSample, "the rest of the body is unaffected");

            rig.Sim.lostJointMask = 0L;
            rig.Run(1f / 60f * 4f, true);
            Assert.IsTrue(rig.Points.Left.HasSample);
            Assert.Less(rig.Points.Left.Intensity, 0.35f, "re-acquiring eases in; the catch-up is not a gesture");
        }

        [Test]
        public void Points_TheStreamStopping_StopsEveryTrack_AndNothingIsDrivenFromStaleData() {
            Rig rig = NewRig(new DemoSegment("both", 100f, true, 0f, 0.8f, 0.8f, 0.35f, 0.5f, 0.45f));
            rig.Run(1f, true);
            Assert.IsTrue(rig.Points.Left.HasSample);

            rig.Sim.streamActive = false;
            rig.Demo.segments[0].visible = false;
            rig.Run(0.3f, true);
            Assert.IsFalse(rig.Points.Left.HasSample);
            Assert.IsFalse(rig.Points.Right.HasSample);
            Assert.IsFalse(rig.Points.Torso.HasSample);
            Assert.AreEqual(0f, rig.Points.Left.Intensity);
            Assert.AreEqual(0f, rig.Points.Right.Intensity);
            Assert.AreEqual(0f, rig.Points.Torso.Intensity);
        }

        [Test]
        public void Points_NotFollowing_MeansNoTracksEvenWithAFreshBody() {
            Rig rig = NewRig(new DemoSegment("both", 100f, true, 0f, 0.8f, 0.8f, 0.35f, 0.5f, 0.45f));
            rig.Run(0.5f, false);
            Assert.IsFalse(rig.Points.Left.HasSample);
            Assert.IsFalse(rig.Points.Right.HasSample);
            Assert.IsFalse(rig.Points.Torso.HasSample);
        }

        [Test]
        public void Points_AreInsideTheViewForAFullBody() {
            Rig rig = NewRig(new DemoSegment("still", 100f, true, 0f, 0f, 0f, 0f, 0f, FluidDisruptionDemoPoseSource.RestLift));
            float halfW = rig.Mapper.ViewWidth * 0.5f;
            float halfH = rig.Mapper.ViewHeight * 0.5f;
            Vector2[] ps = { rig.Points.Left.Position, rig.Points.Right.Position, rig.Points.Torso.Position };
            for (int i = 0; i < ps.Length; i++) {
                Assert.Less(Mathf.Abs(ps[i].x), halfW);
                Assert.Less(Mathf.Abs(ps[i].y), halfH);
            }
        }

        [Test]
        public void Points_LeftIsOnTheScreensLeftWhenMirrored_AndRightWhenNot() {
            Rig mirrored = NewRig(new DemoSegment("still", 100f, true, 0f, 0f, 0f, 0f, 0f, FluidDisruptionDemoPoseSource.RestLift));
            mirrored.Sim.mirrorX = true;
            mirrored.Run(0.3f, true);
            // The simulation writes the live landmark layout (left at negative X) and mirrors like the
            // live converter does; the figure's left hand must lie on the left of the screen.
            Assert.Less(mirrored.Points.Left.Position.x, mirrored.Points.Right.Position.x);

            Rig flipped = new Rig();
            flipped.Sim.mirrorX = false;
            flipped.Demo.segments = new DemoSegment[] {
                new DemoSegment("still", 100f, true, 0f, 0f, 0f, 0f, 0f, FluidDisruptionDemoPoseSource.RestLift)
            };
            flipped.Run(1.4f, true);
            Assert.Greater(flipped.Points.Left.Position.x, flipped.Points.Right.Position.x);
        }

        // ------------------------------------------------------------------ demo programme

        [Test]
        public void Demo_ProgrammeCoversEveryRequiredSituation() {
            DemoSegment[] segments = FluidDisruptionDemoPoseSource.DefaultProgramme();
            bool still = false;
            bool travel = false;
            bool leftOnly = false;
            bool rightOnly = false;
            bool both = false;
            bool slow = false;
            bool fast = false;
            bool absent = false;
            bool reappear = false;
            for (int i = 0; i < segments.Length; i++) {
                DemoSegment d = segments[i];
                still |= d.visible && d.travelAmplitude <= 0f && d.leftHz <= 0f && d.rightHz <= 0f;
                travel |= d.visible && d.travelAmplitude > 0.3f && d.leftHz <= 0f && d.rightHz <= 0f;
                leftOnly |= d.leftHz > 0f && d.rightHz <= 0f;
                rightOnly |= d.rightHz > 0f && d.leftHz <= 0f;
                both |= d.leftHz > 0f && d.rightHz > 0f;
                slow |= d.leftHz > 0f && d.leftHz <= 0.4f;
                fast |= d.rightHz >= 1.2f;
                absent |= !d.visible;
                reappear |= absent && d.visible;
            }
            Assert.IsTrue(still && travel && leftOnly && rightOnly && both && slow && fast && absent && reappear);
        }

        [Test]
        public void Demo_IsDeterministicWhateverTheFrameRate() {
            FluidDisruptionDemoPoseSource a = new FluidDisruptionDemoPoseSource();
            FluidDisruptionDemoPoseSource b = new FluidDisruptionDemoPoseSource();
            SimulatedPoseSource sa = new SimulatedPoseSource();
            SimulatedPoseSource sb = new SimulatedPoseSource();
            a.Restart();
            b.Restart();
            for (int i = 0; i < 60 * 20; i++) {
                a.Tick(1f / 60f, sa);
            }
            for (int i = 0; i < 30 * 20; i++) {
                b.Tick(1f / 30f, sb);
            }
            Assert.AreEqual(a.SegmentIndex, b.SegmentIndex);
            Assert.AreEqual(sa.externalOffsetX, sb.externalOffsetX, 0.02f);
            Assert.AreEqual(sa.externalLeftLift, sb.externalLeftLift, 0.02f);
            Assert.AreEqual(sa.externalRightLift, sb.externalRightLift, 0.02f);
            Assert.AreEqual(sa.streamActive, sb.streamActive);
        }

        [Test]
        public void Demo_AdvancesThroughTheSegments_ThePersonLeavesAndReturns_AndItLoops() {
            FluidDisruptionDemoPoseSource d = new FluidDisruptionDemoPoseSource();
            SimulatedPoseSource sim = new SimulatedPoseSource();
            d.Restart();
            bool sawAbsent = false;
            bool sawPresentAfterAbsent = false;
            int maxIndex = 0;
            int steps = Mathf.RoundToInt(d.TotalSeconds() * 60f) + 60;
            for (int i = 0; i < steps; i++) {
                d.Tick(Dt, sim);
                maxIndex = Mathf.Max(maxIndex, d.SegmentIndex);
                if (!sim.streamActive) {
                    sawAbsent = true;
                } else if (sawAbsent) {
                    sawPresentAfterAbsent = true;
                }
            }
            Assert.IsTrue(sawAbsent && sawPresentAfterAbsent);
            Assert.AreEqual(d.segments.Length - 1, maxIndex);
            Assert.Less(d.SegmentIndex, 2, "after one full pass it starts over");
            Assert.IsTrue(sim.externalControl);
        }

        [Test]
        public void Demo_HoldStaysOnTheChosenSegment() {
            FluidDisruptionDemoPoseSource d = new FluidDisruptionDemoPoseSource();
            SimulatedPoseSource sim = new SimulatedPoseSource();
            d.Restart();
            d.Hold(4);
            for (int i = 0; i < 60 * 40; i++) {
                d.Tick(Dt, sim);
            }
            Assert.AreEqual(4, d.SegmentIndex);
            Assert.IsTrue(d.IsHolding);
            d.Hold(-1);
            Assert.IsFalse(d.IsHolding);
        }

        // ------------------------------------------------------------------ settings and wiring

        [Test]
        public void Settings_TheBaselineIsIndependentOfTheInteractionAndTheDefaultsAreSane() {
            FluidDisruptionSettings s = NewSettings();
            Assert.GreaterOrEqual(s.baselineSources.Length, 3);
            Assert.LessOrEqual(s.baselineSources.Length, 8);
            for (int i = 0; i < s.baselineSources.Length; i++) {
                BaselineSourceConfig c = s.baselineSources[i];
                Assert.Greater(c.period, 20f, "the baseline must move slowly");
                Assert.Less(c.flowStrength, 0.3f);
                Assert.Greater(c.rate, 0f);
            }
            Assert.Greater(s.fieldWeight, 0f, "the baseline field is maintained");
            Assert.Greater(s.fieldRate, 0f);
            Assert.Greater(s.handMovement.fullSpeed, s.handMovement.deadZoneSpeed);
            Assert.Greater(s.torsoMovement.fullSpeed, s.torsoMovement.deadZoneSpeed);
            Assert.Greater(s.handDensity, s.torsoDensity, "the torso is a lower-intensity source than a hand");
            Assert.AreEqual(s.palette[0].r, s.PaletteColor(-7).r, 1e-6f, "palette indices wrap");
        }

        [Test]
        public void Settings_QualityProfilesDifferAndPerformanceIsCheaper() {
            FluidDisruptionSettings s = NewSettings();
            QualityProfile perf = s.ProfileFor(FluidQuality.Performance);
            QualityProfile quality = s.ProfileFor(FluidQuality.Quality);
            Assert.Less(perf.resolution * perf.supersampling, quality.resolution * quality.supersampling);
        }

        [Test]
        public void SoftSplat_IsOpaqueInTheMiddleAndFadesSmoothlyToNothingAtTheRim() {
            Assert.AreEqual(1f, FluidTargets.SoftSplatFalloff(0f), 1e-6f);
            Assert.AreEqual(0f, FluidTargets.SoftSplatFalloff(1f), 1e-6f);
            Assert.AreEqual(0f, FluidTargets.SoftSplatFalloff(3f), 1e-6f, "beyond the rim there is nothing");
            float previous = 2f;
            for (int i = 0; i <= 20; i++) {
                float v = FluidTargets.SoftSplatFalloff(i / 20f);
                Assert.Less(v, previous + 1e-6f, "monotonic: no ring, no hard edge");
                previous = v;
            }
            // The slope at the rim is zero (no visible edge): the last step is tiny compared to the first.
            float edgeStep = FluidTargets.SoftSplatFalloff(0.95f) - FluidTargets.SoftSplatFalloff(1f);
            Assert.Less(edgeStep, 0.01f);
        }
    }
}
