using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-48 unit tests: the operating volume, calibration that learns only from measured depth, the
    /// per-bone anatomical bands, and hand safety.
    ///
    /// WHAT THESE ARE A REGRESSION FOR. SR7 (Assets/Games/video/SR7.mp4): a healthy 29 fps stream at
    /// 8-31 ms carrying a subject who spent minutes at 0.66-1.28 m, below F-44's usable 1.4-4.5 m. The
    /// calibration learned bone lengths from joints whose depth was INFERRED, the avatar followed a
    /// 0.66 m hip toward the lens, a forearm of ~0.55 m passed a 0.01-0.80 m band, and one palm of
    /// 147 mm hid a thumb 2273 mm from its index. Every test below is one of those, reproduced.
    ///
    /// SCOPE. The fixtures at the bottom drive the real <see cref="TrackedStage"/> over a real UDP
    /// socket, because "the follow position does not move" is a property of the wiring, not of any one
    /// class. They take a few seconds and bind <see cref="TestPort"/>, never the production 8899.
    /// </summary>
    public sealed class NearFieldSafetyF48Tests {
        private const float Dt = 1f / 30f;
        private const float Conf = 0.9f;

        // Bone indices in HumanBodyModel.Bones, named so a test reads as anatomy rather than as a number.
        private const int LeftForearm = 7;
        private const int RightForearm = 9;
        private const int LeftUpperArm = 6;
        private const int Trunk = 2;

        [SetUp]
        public void ResetVolumeDefaults() {
            // The limits are a static shared by every consumer in the process, so a test that changes
            // them must not leak into the next.
            OperatingVolume.ResetToDefaults();
        }

        [TearDown]
        public void RestoreVolumeDefaults() {
            OperatingVolume.ResetToDefaults();
        }

        // ================================================================ the volume itself

        [Test]
        public void Volume_ClassifiesNearInAndFar() {
            Assert.AreEqual(VolumeVerdict.TooNear, OperatingVolume.Classify(0.91f, VolumeVerdict.NoPose));
            Assert.AreEqual(VolumeVerdict.TooNear, OperatingVolume.Classify(0.66f, VolumeVerdict.NoPose));
            Assert.AreEqual(VolumeVerdict.InVolume, OperatingVolume.Classify(1.6f, VolumeVerdict.NoPose));
            Assert.AreEqual(VolumeVerdict.InVolume, OperatingVolume.Classify(3.5f, VolumeVerdict.NoPose));
            Assert.AreEqual(VolumeVerdict.TooFar, OperatingVolume.Classify(5.2f, VolumeVerdict.NoPose));
        }

        [Test]
        public void Volume_SrSevenReadingsAreAllOutside() {
            // The hip depths read off the SR7 HUD. None of them is inside the F-44 band.
            float[] observed = { 0.91f, 1.07f, 1.12f, 1.28f, 0.66f };
            int i = 0;
            while (i < observed.Length) {
                Assert.AreEqual(VolumeVerdict.TooNear,
                                OperatingVolume.Classify(observed[i], VolumeVerdict.NoPose),
                                "hip " + observed[i] + " m");
                i = i + 1;
            }
        }

        [Test]
        public void Volume_ARootThatIsNotADepthIsInvalidNotTooNear() {
            // Calling a zero-filled field "too near" would misdescribe the fault and send the person
            // the wrong instruction.
            Assert.AreEqual(VolumeVerdict.RootInvalid, OperatingVolume.Classify(0f, VolumeVerdict.NoPose));
            Assert.AreEqual(VolumeVerdict.RootInvalid, OperatingVolume.Classify(0.01f, VolumeVerdict.NoPose));
            Assert.AreEqual(VolumeVerdict.RootInvalid,
                            OperatingVolume.Classify(float.NaN, VolumeVerdict.NoPose));
        }

        [Test]
        public void Volume_NoRootCannotBeJudged() {
            Assert.AreEqual(VolumeVerdict.NoRoot, OperatingVolume.Classify(-1f, VolumeVerdict.NoPose));
        }

        [Test]
        public void Volume_HysteresisStopsAFlipAtTheLimit() {
            float justInside = OperatingVolume.NearLimitMetres + 0.02f;
            Assert.AreEqual(VolumeVerdict.InVolume,
                            OperatingVolume.Classify(justInside, VolumeVerdict.InVolume));
            // Having been too near, the subject must clear the limit by the margin to count as in.
            Assert.AreEqual(VolumeVerdict.TooNear,
                            OperatingVolume.Classify(justInside, VolumeVerdict.TooNear));
            Assert.AreEqual(VolumeVerdict.InVolume,
                            OperatingVolume.Classify(OperatingVolume.NearLimitMetres
                                                     + OperatingVolume.HysteresisMetres + 0.01f,
                                                     VolumeVerdict.TooNear));
        }

        [Test]
        public void Volume_ConfigureRepairsNonsense() {
            OperatingVolume.Configure(3.0f, 1.0f, -5f, -3);
            Assert.Greater(OperatingVolume.FarLimitMetres, OperatingVolume.NearLimitMetres,
                           "a far limit below the near limit would put every pose outside the volume");
            Assert.GreaterOrEqual(OperatingVolume.HoldSeconds, 0f);
            Assert.GreaterOrEqual(OperatingVolume.SettlePoses, 0);
        }

        [Test]
        public void Volume_IsConfigurableNotHardCoded() {
            OperatingVolume.Configure(2.0f, 3.0f, 0.5f, 3);
            Assert.AreEqual(VolumeVerdict.TooNear, OperatingVolume.Classify(1.6f, VolumeVerdict.NoPose),
                            "1.6 m is inside the default band but outside this configured one");
            Assert.AreEqual(2.0f, OperatingVolume.NearLimitMetres, 1e-4f);
        }

        [Test]
        public void Volume_DefaultNearLimitSitsInsideTheDocumentedBand() {
            // F-44's usable band starts at ~1.4 m, and SR7's degradation began well inside 1.3 m.
            Assert.GreaterOrEqual(OperatingVolume.DefaultNearLimitMetres, 1.35f);
            Assert.LessOrEqual(OperatingVolume.DefaultNearLimitMetres, 1.40f);
        }

        [Test]
        public void Volume_RangeIsTheDepthMagnitudeWhateverTheFlipZSign() {
            PoseFrame ahead = new PoseFrame();
            ahead.SetRootPosition(new Vector3(0.3f, 0.2f, 2.0f));
            PoseFrame behind = new PoseFrame();
            behind.SetRootPosition(new Vector3(0.3f, 0.2f, -2.0f));
            Assert.AreEqual(2.0f, OperatingVolume.RangeOf(ahead), 1e-5f);
            Assert.AreEqual(2.0f, OperatingVolume.RangeOf(behind), 1e-5f);
            Assert.Less(OperatingVolume.RangeOf(new PoseFrame()), 0f, "no root is reported, not zero");
        }

        // ================================================================ the gate

        [Test]
        public void Gate_NearPoseIsOutsideAndAFarOneToo() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(0.91f, 1.0), Dt);
            Assert.AreEqual(VolumeVerdict.TooNear, gate.Verdict);
            Assert.AreEqual(PoseAdmission.Outside, gate.Admission);
            gate.Update(Posed(5.0f, 2.0), Dt);
            Assert.AreEqual(VolumeVerdict.TooFar, gate.Verdict);
            Assert.AreEqual(PoseAdmission.Outside, gate.Admission);
        }

        [Test]
        public void Gate_ArrivalSettlesBeforeItIsTrusted() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            int settle = OperatingVolume.SettlePoses;
            gate.Update(Posed(2.0f, 1.0), Dt);
            Assert.AreEqual(PoseAdmission.Settling, gate.Admission,
                            "a pose that has only just arrived may be drawn but not learned from");
            Assert.IsTrue(gate.AllowsMotion);
            int i = 1;
            while (i < settle) {
                gate.Update(Posed(2.0f, 1.0 + i * 0.033), Dt);
                i = i + 1;
            }
            Assert.AreEqual(PoseAdmission.Trusted, gate.Admission);
        }

        [Test]
        public void Gate_CountsPosesNotRenderFrames() {
            // 190 fps over a 30 Hz stream: counting calls would report a rejection rate that belongs
            // to the display.
            OperatingVolumeGate gate = new OperatingVolumeGate();
            PoseFrame near = Posed(0.8f, 5.0);
            int i = 0;
            while (i < 100) {
                gate.Update(near, 0.005f);
                i = i + 1;
            }
            Assert.AreEqual(1L, gate.PosesTooNear, "one pose, however many render frames saw it");
            gate.Update(Posed(0.8f, 5.033), 0.005f);
            Assert.AreEqual(2L, gate.PosesTooNear);
        }

        [Test]
        public void Gate_HoldIsBoundedThenExpires() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(2.0f, 0.0), Dt);
            double t = 0.033;
            float held = 0f;
            while (held < OperatingVolume.HoldSeconds - 0.1f) {
                gate.Update(Posed(0.8f, t), Dt);
                held = held + Dt;
                t = t + 0.033;
            }
            Assert.IsFalse(gate.Expired, "inside the hold the last body is still being shown");
            while (held < OperatingVolume.HoldSeconds + 0.2f) {
                gate.Update(Posed(0.8f, t), Dt);
                held = held + Dt;
                t = t + 0.033;
            }
            Assert.IsTrue(gate.Expired, "past the hold the consumer must stop showing a body");
            Assert.AreEqual(1L, gate.HoldExpirations);
        }

        [Test]
        public void Gate_RecoversWhenValidDepthResumes() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            double t = 0.0;
            int i = 0;
            while (i < 60) {
                gate.Update(Posed(0.8f, t), Dt);
                t = t + 0.033;
                i = i + 1;
            }
            Assert.IsTrue(gate.Expired);
            i = 0;
            while (i < OperatingVolume.SettlePoses + 2) {
                gate.Update(Posed(2.2f, t), Dt);
                t = t + 0.033;
                i = i + 1;
            }
            Assert.IsFalse(gate.Expired);
            Assert.AreEqual(PoseAdmission.Trusted, gate.Admission);
            Assert.AreEqual(VolumeVerdict.InVolume, gate.Verdict);
            Assert.AreEqual(0f, gate.OutsideSeconds, 1e-6f);
        }

        [Test]
        public void Gate_ReentryMustSettleAgainBeforeCalibrationMayLearn() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            double t = 0.0;
            int i = 0;
            while (i < 20) {
                gate.Update(Posed(2.0f, t), Dt);
                t = t + 0.033;
                i = i + 1;
            }
            Assert.AreEqual(PoseAdmission.Trusted, gate.Admission);
            gate.Update(Posed(0.7f, t), Dt);
            t = t + 0.033;
            gate.Update(Posed(2.0f, t), Dt);
            Assert.AreEqual(PoseAdmission.Settling, gate.Admission,
                            "the first pose back is the one whose depth is least settled");
        }

        [Test]
        public void Gate_NoRootIsNotRefusedButIsCounted() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            PoseFrame frame = new PoseFrame();
            frame.SetMeta(1.0, true);
            gate.Update(frame, Dt);
            Assert.AreEqual(VolumeVerdict.NoRoot, gate.Verdict);
            Assert.AreEqual(PoseAdmission.Trusted, gate.Admission, "an RGB-only source must keep working");
            Assert.AreEqual(1L, gate.PosesNoRoot);
        }

        [Test]
        public void Gate_DisabledTrustsEverythingAndCountsNothing() {
            OperatingVolume.Enabled = false;
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(0.5f, 1.0), Dt);
            Assert.AreEqual(PoseAdmission.Trusted, gate.Admission);
            Assert.AreEqual(0L, gate.PosesTooNear);
            Assert.IsFalse(gate.Expired);
        }

        [Test]
        public void Gate_NobodyTrackedClearsTheClocks() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            double t = 0.0;
            int i = 0;
            while (i < 60) {
                gate.Update(Posed(0.8f, t), Dt);
                t = t + 0.033;
                i = i + 1;
            }
            Assert.IsTrue(gate.Expired);
            gate.Update(null, Dt);
            Assert.IsFalse(gate.Expired);
            Assert.AreEqual(VolumeVerdict.NoPose, gate.Verdict);
            Assert.AreEqual(0f, gate.OutsideSeconds, 1e-6f);
        }

        [Test]
        public void Gate_TellsATooCloseUserToStepBackAndNeverCloser() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(0.91f, 1.0), Dt);
            StringAssert.Contains("step back", gate.Message());
            StringAssert.DoesNotContain("closer", gate.Message().ToLowerInvariant());
            StringAssert.Contains("0.91", gate.Message());
            gate.Update(Posed(5.0f, 2.0), Dt);
            StringAssert.Contains("step closer", gate.Message());
            gate.Update(Posed(2.0f, 3.0), Dt);
            Assert.IsNull(gate.Message(), "nothing to say when the subject is in range");
        }

        [Test]
        public void Gate_SummaryStatesTheBandAndEveryCounter() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(0.8f, 1.0), Dt);
            string summary = gate.Summary();
            StringAssert.Contains("TOONEAR", summary);
            StringAssert.Contains("near 1", summary);
            StringAssert.Contains("1.35", summary);
            StringAssert.Contains("4.50", summary);
            StringAssert.Contains("held 1", summary);
            Assert.LessOrEqual(summary.Length, 110, "must survive a 1100 px capture on one line");
        }

        // ================================================================ per-bone anatomy

        [Test]
        public void Bones_EveryBoneHasAnOrderedBandInsideTheBackstop() {
            int b = 0;
            while (b < HumanBodyModel.Bones.Length) {
                float lo = HumanBodyModel.MinBoneLengthM(b);
                float hi = HumanBodyModel.MaxBoneLengthM(b);
                Assert.Less(lo, hi, "bone " + b);
                Assert.GreaterOrEqual(lo, HumanBodyModel.MinBoneM, "bone " + b);
                Assert.LessOrEqual(hi, HumanBodyModel.MaxBoneM, "bone " + b);
                b = b + 1;
            }
        }

        [Test]
        public void Bones_AMirroredPairSharesOneBand() {
            // A left/right asymmetry in the table would make one side calibrate where the other cannot.
            int b = 0;
            while (b < HumanBodyModel.Bones.Length) {
                int m = HumanBodyModel.Bones[b].Mirror;
                if (m >= 0) {
                    Assert.AreEqual(HumanBodyModel.MinBoneLengthM(b), HumanBodyModel.MinBoneLengthM(m), 1e-6f);
                    Assert.AreEqual(HumanBodyModel.MaxBoneLengthM(b), HumanBodyModel.MaxBoneLengthM(m), 1e-6f);
                }
                b = b + 1;
            }
        }

        [Test]
        public void Bones_NormalAdultLengthsPassForEveryClass() {
            // Typical adult values, one per anatomical class, bone index -> metres.
            AssertAccepts(0, 0.10f, "half hip width");
            AssertAccepts(Trunk, 0.50f, "trunk");
            AssertAccepts(3, 0.18f, "half shoulder width");
            AssertAccepts(5, 0.26f, "neck to nose");
            AssertAccepts(LeftUpperArm, 0.30f, "upper arm");
            AssertAccepts(LeftForearm, 0.26f, "forearm");
            AssertAccepts(10, 0.42f, "thigh");
            AssertAccepts(11, 0.40f, "shin");
            AssertAccepts(14, 0.09f, "heel");
            AssertAccepts(15, 0.17f, "toe");
            AssertAccepts(19, 0.09f, "hand point");
        }

        [Test]
        public void Bones_TheGeneratorsOwnBodyIsInsideEveryBand() {
            // The F-27 self-test calibrates from SyntheticPose; a band that rejected it would silently
            // turn that whole suite into a test of the fallback path.
            AssertAccepts(0, SyntheticHipHalf, "synthetic half hip");
            AssertAccepts(Trunk, 0.50f, "synthetic trunk");
            AssertAccepts(3, 0.18f, "synthetic half shoulder");
            AssertAccepts(5, 0.25f, "synthetic neck");
            AssertAccepts(LeftUpperArm, 0.28f, "synthetic upper arm");
            AssertAccepts(LeftForearm, 0.26f, "synthetic forearm");
            AssertAccepts(10, 0.42f, "synthetic thigh");
            AssertAccepts(11, 0.40f, "synthetic shin");
            AssertAccepts(14, 0.057f, "synthetic heel");
            AssertAccepts(15, 0.126f, "synthetic toe");
            AssertAccepts(18, 0.08f, "synthetic hand");
        }

        private const float SyntheticHipHalf = 0.10f;

        [Test]
        public void Bones_ImpossibleLengthsAreRefusedForEveryClass() {
            // Each of these is under the OLD 0.80 m ceiling, which is exactly why it admitted them.
            AssertRefuses(LeftForearm, 0.55f, "a 55 cm forearm");
            AssertRefuses(LeftUpperArm, 0.60f, "a 60 cm upper arm");
            AssertRefuses(5, 0.60f, "a 60 cm neck");
            AssertRefuses(Trunk, 0.78f, "a 78 cm trunk");
            AssertRefuses(10, 0.75f, "a 75 cm thigh");
            AssertRefuses(11, 0.70f, "a 70 cm shin");
            AssertRefuses(0, 0.35f, "a 35 cm half hip width");
            AssertRefuses(3, 0.40f, "a 40 cm half shoulder width");
            AssertRefuses(14, 0.35f, "a 35 cm heel");
            AssertRefuses(15, 0.50f, "a 50 cm toe");
            AssertRefuses(19, 0.40f, "a 40 cm hand point");
            // ...and the other end: a collapsed bone is as impossible as a stretched one.
            AssertRefuses(LeftForearm, 0.05f, "a 5 cm forearm");
            AssertRefuses(10, 0.10f, "a 10 cm thigh");
            AssertRefuses(Trunk, 0.10f, "a 10 cm trunk");
            AssertRefuses(LeftUpperArm, 0.05f, "a 5 cm upper arm");
        }

        [Test]
        public void Bones_NonFiniteLengthsAreRefused() {
            Assert.IsFalse(HumanBodyModel.IsPlausibleBoneLength(LeftForearm, float.NaN));
            Assert.IsFalse(HumanBodyModel.IsPlausibleBoneLength(LeftForearm, float.PositiveInfinity));
        }

        private static void AssertAccepts(int bone, float metres, string what) {
            Assert.IsTrue(HumanBodyModel.IsPlausibleBoneLength(bone, metres), what + " must calibrate");
        }

        private static void AssertRefuses(int bone, float metres, string what) {
            Assert.IsFalse(HumanBodyModel.IsPlausibleBoneLength(bone, metres), what + " must be refused");
        }

        // ================================================================ the calibrator

        [Test]
        public void Calibrator_APerBoneImpossibleSampleNeverEntersTheWindow() {
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            int i = 0;
            while (i < 40) {
                Assert.IsFalse(calibrator.Observe(LeftForearm, 0.55f));
                i = i + 1;
            }
            Assert.AreEqual(0, calibrator.SampleCount(LeftForearm));
            Assert.IsFalse(calibrator.IsCalibrated(LeftForearm));
            Assert.AreEqual(40L, calibrator.RejectedImplausibleCount);
        }

        [Test]
        public void Calibrator_ASampleInsideTheBandIsTaken() {
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            Assert.IsTrue(calibrator.Observe(LeftForearm, 0.26f));
            Assert.AreEqual(1, calibrator.SampleCount(LeftForearm));
        }

        [Test]
        public void Calibrator_AgreeingSidesAreAveraged() {
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            Feed(calibrator, LeftForearm, 0.26f, 20);
            Feed(calibrator, RightForearm, 0.27f, 20);
            Assert.AreEqual(0.265f, calibrator.Length(LeftForearm), 1e-4f);
            Assert.AreEqual(0.265f, calibrator.Length(RightForearm), 1e-4f);
        }

        [Test]
        public void Calibrator_ABadSideDoesNotPolluteTheGoodSide() {
            // Right forearm: in-band (so it was NOT rejected for being impossible) but 38 % longer than
            // the left. Averaging would hand the good arm ~0.31 m.
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            Feed(calibrator, LeftForearm, 0.26f, 20);
            Feed(calibrator, RightForearm, 0.36f, 20);
            Assert.AreEqual(0.26f, calibrator.Length(LeftForearm), 1e-4f, "the good side keeps its own");
            Assert.AreEqual(0.36f, calibrator.Length(RightForearm), 1e-4f, "and is not mirrored onto");
        }

        [Test]
        public void Calibrator_AnImpossibleSideNeverReachesTheGoodSideEither() {
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            Feed(calibrator, LeftForearm, 0.26f, 20);
            Feed(calibrator, RightForearm, 0.55f, 20);
            Assert.AreEqual(0, calibrator.SampleCount(RightForearm));
            // One validated side serves both - the existing, deliberate behaviour for an occluded arm.
            Assert.AreEqual(0.26f, calibrator.Length(RightForearm), 1e-4f);
            Assert.AreEqual(0.26f, calibrator.Length(LeftForearm), 1e-4f);
        }

        [Test]
        public void Calibrator_ResetForgetsTheRejectionCount() {
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            calibrator.Observe(LeftForearm, 0.9f);
            calibrator.Reset();
            Assert.AreEqual(0L, calibrator.RejectedImplausibleCount);
        }

        [Test]
        public void Calibrator_TrunkAndHeadIsTheSumOfItsTwoBonesAndPlausible() {
            BoneLengthCalibrator calibrator = new BoneLengthCalibrator();
            Feed(calibrator, Trunk, 0.50f, 20);
            Feed(calibrator, 5, 0.25f, 20);
            Assert.AreEqual(0.75f, calibrator.TrunkAndHeadLength(), 1e-4f);
            Assert.AreEqual(2, calibrator.CalibratedBoneCount());
        }

        private static void Feed(BoneLengthCalibrator calibrator, int bone, float metres, int count) {
            int i = 0;
            while (i < count) {
                calibrator.Observe(bone, metres);
                i = i + 1;
            }
        }

        // ================================================================ calibration provenance

        [Test]
        public void Provenance_MeasuredDepthAllowsNormalCalibrationLearning() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            TrackingTelemetry telemetry = Telemetry(true, true);
            RunFrames(layer, telemetry, PoseAdmission.Trusted, 0.26f, 30, 0.0);

            Assert.IsTrue(layer.Calibration.IsCalibrated(LeftForearm));
            Assert.AreEqual(0.26f, layer.Calibration.Length(LeftForearm), 0.005f);
            Assert.Greater(layer.Stats.CalibSamplesAccepted, 0L);
            Assert.AreEqual(0L, layer.Stats.CalibRejectedUnmeasured);
        }

        [Test]
        public void Provenance_InferredDepthDoesNotAlterTheCalibration() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            RunFrames(layer, Telemetry(true, true), PoseAdmission.Trusted, 0.26f, 30, 0.0);
            int samplesBefore = layer.Calibration.SampleCount(LeftForearm);
            float lengthBefore = layer.Calibration.Length(LeftForearm);

            // 0.34 m is INSIDE the forearm's anatomical band, so only provenance can refuse it - this
            // is the case the per-bone band alone cannot catch. Wrists have inferred depth.
            RunFrames(layer, Telemetry(true, false), PoseAdmission.Trusted, 0.34f, 60, 2.0);

            Assert.AreEqual(samplesBefore, layer.Calibration.SampleCount(LeftForearm),
                            "no sample may be taken from an inferred joint");
            Assert.AreEqual(lengthBefore, layer.Calibration.Length(LeftForearm), 1e-5f);
            Assert.Greater(layer.Stats.CalibRejectedUnmeasured, 0L, "and the refusal is counted by name");
        }

        [Test]
        public void Provenance_TheSameStretchWithMeasuredDepthDoesTeachTheCalibration() {
            // The control. Without it the test above could pass because the layer never learns at all.
            HumanizedSkeleton layer = new HumanizedSkeleton();
            RunFrames(layer, Telemetry(true, true), PoseAdmission.Trusted, 0.26f, 30, 0.0);
            int samplesBefore = layer.Calibration.SampleCount(LeftForearm);
            RunFrames(layer, Telemetry(true, true), PoseAdmission.Trusted, 0.34f, 60, 2.0);
            Assert.Greater(layer.Calibration.SampleCount(LeftForearm), samplesBefore);
        }

        [Test]
        public void Provenance_InferredJointsAreStillOutputNotDiscarded() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            RunFrames(layer, Telemetry(true, true), PoseAdmission.Trusted, 0.26f, 30, 0.0);

            PoseFrame input = new PoseFrame();
            Body(input, 2.0, 0.34f);
            PoseFrame output = layer.Process(input, Dt, Telemetry(true, false), PoseAdmission.Trusted);

            Assert.IsTrue(output.IsValid, "inferred depth must not cost the frame");
            Vector3 elbow = output.GetLandmark(JointId.LeftElbow).Position;
            Vector3 wrist = output.GetLandmark(JointId.LeftWrist).Position;
            Assert.AreEqual(0.26f, Vector3.Distance(elbow, wrist), 0.01f,
                            "the stretch is corrected to the length learned from MEASURED frames");
        }

        [Test]
        public void Provenance_UnknownProvenanceIsNotTreatedAsInferred() {
            // The legacy overload, and a stream that reports no depth channel at all. Refusing to
            // calibrate on those would be a regression to guard against a fault they cannot report.
            HumanizedSkeleton legacy = new HumanizedSkeleton();
            PoseFrame input = new PoseFrame();
            int i = 0;
            while (i < 30) {
                Body(input, i * 0.033, 0.26f);
                legacy.Process(input, Dt);
                i = i + 1;
            }
            Assert.IsTrue(legacy.Calibration.IsCalibrated(LeftForearm));

            HumanizedSkeleton silent = new HumanizedSkeleton();
            TrackingTelemetry none = new TrackingTelemetry();
            none.HasDepthChannel = false;
            i = 0;
            while (i < 30) {
                Body(input, i * 0.033, 0.26f);
                silent.Process(input, Dt, none, PoseAdmission.Trusted);
                i = i + 1;
            }
            Assert.IsTrue(silent.Calibration.IsCalibrated(LeftForearm));
        }

        [Test]
        public void Provenance_ASourceThatMeasuresNothingNeverCalibrates() {
            // A video file reports the channel with every joint false. That must read as "no
            // calibration", with the tracker's own lengths passed through.
            HumanizedSkeleton layer = new HumanizedSkeleton();
            RunFrames(layer, Telemetry(false, false), PoseAdmission.Trusted, 0.26f, 60, 0.0);
            Assert.AreEqual(0, layer.Calibration.CalibratedBoneCount());
            Assert.AreEqual(0L, layer.Stats.CalibSamplesAccepted);
        }

        [Test]
        public void Provenance_AMeasuredPairIsRequiredForTheBoneNotJustOneEnd() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            TrackingTelemetry elbowOnly = Telemetry(true, true);
            elbowOnly.SetDepthMeasured((int)JointId.LeftWrist, false);
            RunFrames(layer, elbowOnly, PoseAdmission.Trusted, 0.26f, 40, 0.0);
            Assert.AreEqual(0, layer.Calibration.SampleCount(LeftForearm),
                            "elbow measured, wrist inferred: the forearm is a depth error, not a bone");
            Assert.Greater(layer.Calibration.SampleCount(LeftUpperArm), 0,
                           "shoulder and elbow are both measured, so the upper arm still learns");
        }

        // ================================================================ out of volume

        [Test]
        public void Outside_HoldsTheLastGoodBodyAndLearnsNothing() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            TrackingTelemetry telemetry = Telemetry(true, true);
            RunFrames(layer, telemetry, PoseAdmission.Trusted, 0.26f, 30, 0.0);

            PoseFrame good = new PoseFrame();
            Body(good, 1.0, 0.26f);
            PoseFrame lastGood = layer.Process(good, Dt, telemetry, PoseAdmission.Trusted);
            Vector3 wristBefore = lastGood.GetLandmark(JointId.LeftWrist).Position;
            int samplesBefore = layer.Calibration.SampleCount(LeftForearm);

            // The SR7 near-field frame: forearm stretched, body collapsed upward.
            PoseFrame poisoned = new PoseFrame();
            Body(poisoned, 1.1, 0.34f);
            poisoned.SetLandmark((int)JointId.LeftWrist,
                                 new PoseLandmark(new Vector3(0.1f, 1.9f, 0.9f), Conf));
            int held = 0;
            while (held < 10) {
                PoseFrame shown = layer.Process(poisoned, Dt, telemetry, PoseAdmission.Outside);
                Assert.IsTrue(shown.IsValid, "inside the hold there is still a body to show");
                Assert.AreEqual(wristBefore.x, shown.GetLandmark(JointId.LeftWrist).Position.x, 1e-5f);
                Assert.AreEqual(wristBefore.y, shown.GetLandmark(JointId.LeftWrist).Position.y, 1e-5f);
                held = held + 1;
            }

            Assert.AreEqual(samplesBefore, layer.Calibration.SampleCount(LeftForearm));
            Assert.AreEqual(10L, layer.Stats.FramesHeldOutOfVolume);
            Assert.AreEqual(10L * HumanBodyModel.Bones.Length, layer.Stats.CalibRejectedVolume);
        }

        [Test]
        public void Outside_WithNothingEverAdmittedThereIsNoBodyToHold() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            PoseFrame input = new PoseFrame();
            Body(input, 1.0, 0.26f);
            PoseFrame shown = layer.Process(input, Dt, Telemetry(true, true), PoseAdmission.Outside);
            Assert.IsFalse(shown.IsValid, "a body must not be fabricated for a subject never seen in range");
        }

        [Test]
        public void Settling_ProducesOutputButDoesNotLearn() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            PoseFrame shown = RunFrames(layer, Telemetry(true, true), PoseAdmission.Settling, 0.26f, 40, 0.0);
            Assert.IsTrue(shown.IsValid);
            Assert.AreEqual(0, layer.Calibration.CalibratedBoneCount());
            Assert.Greater(layer.Stats.CalibRejectedVolume, 0L);
            Assert.AreEqual(0L, layer.Stats.CalibSamplesAccepted);
        }

        [Test]
        public void Recovery_ReturnsToNormalOutputWithoutBeingClampedAgainstThePast() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            TrackingTelemetry telemetry = Telemetry(true, true);
            RunFrames(layer, telemetry, PoseAdmission.Trusted, 0.26f, 30, 0.0);

            PoseFrame away = new PoseFrame();
            Body(away, 1.0, 0.26f);
            int i = 0;
            while (i < 40) {
                layer.Process(away, Dt, telemetry, PoseAdmission.Outside);
                i = i + 1;
            }
            long clampedBefore = layer.Stats.VelocityClamped;

            // Back in range, arms now raised: a step far larger than a joint can make in one frame,
            // which is legitimate after a gap and would be clamped if the layer remembered the past.
            PoseFrame back = new PoseFrame();
            BodyArmsOut(back, 5.0, 0.26f);
            PoseFrame shown = null;
            i = 0;
            while (i < 14) {
                shown = layer.Process(back, Dt, telemetry, PoseAdmission.Trusted);
                i = i + 1;
            }

            Assert.IsTrue(shown.IsValid);
            Assert.AreEqual(clampedBefore, layer.Stats.VelocityClamped,
                            "the walk back in must not be read as a teleport");
            Vector3 wrist = shown.GetLandmark(JointId.LeftWrist).Position;
            Vector3 wanted = back.GetLandmark(JointId.LeftWrist).Position;
            Assert.AreEqual(0f, Vector3.Distance(wrist, wanted), 0.02f,
                            "output is the live pose again once the short re-acquire blend has run");
        }

        [Test]
        public void Recovery_CalibrationResumesOnlyAfterTheGateTrustsThePose() {
            HumanizedSkeleton layer = new HumanizedSkeleton();
            TrackingTelemetry telemetry = Telemetry(true, true);
            RunFrames(layer, telemetry, PoseAdmission.Trusted, 0.26f, 30, 0.0);
            int before = layer.Calibration.SampleCount(LeftForearm);
            RunFrames(layer, telemetry, PoseAdmission.Settling, 0.26f, 6, 5.0);
            Assert.AreEqual(before, layer.Calibration.SampleCount(LeftForearm));
            RunFrames(layer, telemetry, PoseAdmission.Trusted, 0.26f, 6, 6.0);
            Assert.Greater(layer.Calibration.SampleCount(LeftForearm), before);
        }

        // ================================================================ hands

        [Test]
        public void Hand_SrSevenThumbBlowUpIsRefusedEvenThoughThePalmIsFine() {
            // Left palm 147 mm (inside 50-160), thumb-index gap 2273 mm = 15.45 palms.
            RawHandFrame raw = new RawHandFrame();
            raw.SetHand(true, BuildHand(0.147f, 2.273f));
            raw.SetMeta(1.0);
            HandPose left = new HandPose();
            left.Fill(raw, true, Vector3.zero, 1f);

            Assert.IsFalse(left.Valid, "a valid palm alone must not make a distant thumb usable");
            Assert.IsTrue(left.Implausible);
            Assert.AreEqual(HandRange.TooCloseOrUnreliable, left.Range);
            Assert.AreEqual(1L, left.RejectedLandmarkFrames);
            Assert.AreEqual(0L, left.RejectedPalmFrames, "the palm passed; the counters must say which gate");
            Assert.IsFalse(left.Pinching);
            Assert.Greater(left.WorstReachPalms, HandPose.MaxReachPalms);
        }

        [Test]
        public void Hand_ABadLeftDoesNotDisableAValidRight() {
            RawHandFrame raw = new RawHandFrame();
            raw.SetHand(true, BuildHand(0.147f, 2.273f));
            raw.SetHand(false, BuildHand(0.09f, 0.05f));
            raw.SetMeta(1.0);
            HandPose left = new HandPose();
            HandPose right = new HandPose();
            left.Fill(raw, true, Vector3.zero, 1f);
            right.Fill(raw, false, Vector3.zero, 1f);

            Assert.IsFalse(left.Valid);
            Assert.IsTrue(right.Valid, "each hand is judged on its own landmarks");
            Assert.AreEqual(HandRange.Ok, right.Range);
            Assert.AreEqual(0L, right.RejectedLandmarkFrames);
        }

        [Test]
        public void Hand_EachLandmarkIsCheckedNotJustTheTips() {
            Vector3[] hand = BuildHand(0.09f, 0.05f);
            hand[13] = new Vector3(0.3f, 0.5f, 0f);   // a ring-finger knuckle half a metre away
            Assert.IsFalse(Filled(hand).Valid);
            Vector3[] nan = BuildHand(0.09f, 0.05f);
            nan[6] = new Vector3(float.NaN, 0f, 0f);
            Assert.IsFalse(Filled(nan).Valid, "a non-finite landmark is not a hand");
        }

        [Test]
        public void Hand_ARealOpenHandStillPasses() {
            // Tips at ~2 palms from the wrist and a wide thumb: ordinary and must not be refused.
            Vector3[] hand = BuildHand(0.09f, 0.14f);
            Assert.IsTrue(Filled(hand).Valid);
            Assert.AreEqual(HandRange.Ok, Filled(hand).Range);
            Assert.Less(Filled(hand).WorstReachPalms, HandPose.MaxReachPalms);
        }

        [Test]
        public void Hand_RefusalsAreCountedPerDatagramNotPerRenderFrame() {
            RawHandFrame raw = new RawHandFrame();
            raw.SetHand(true, BuildHand(0.147f, 2.273f));
            raw.SetMeta(7.0);
            HandPose hand = new HandPose();
            int i = 0;
            while (i < 50) {
                hand.Fill(raw, true, Vector3.zero, 1f);
                i = i + 1;
            }
            Assert.AreEqual(1L, hand.RejectedLandmarkFrames);
            raw.SetMeta(7.033);
            hand.Fill(raw, true, Vector3.zero, 1f);
            Assert.AreEqual(2L, hand.RejectedLandmarkFrames);
        }

        [Test]
        public void Hand_TheVerdictIsIndependentOfSceneScale() {
            RawHandFrame raw = new RawHandFrame();
            raw.SetHand(true, BuildHand(0.147f, 2.273f));
            raw.SetMeta(1.0);
            HandPose half = new HandPose();
            half.Fill(raw, true, Vector3.zero, 0.5f);
            Assert.IsFalse(half.Valid);
            Assert.AreEqual(HandRange.TooCloseOrUnreliable, half.Range);
        }

        [Test]
        public void Hand_RangeStatesSeparateTooFarFromTooCloseOrUnreliable() {
            HandPose tiny = Filled(BuildHand(0.03f, 0.02f));
            Assert.AreEqual(HandRange.TooFar, tiny.Range);
            Assert.AreEqual(1L, tiny.RejectedPalmFrames);

            HandPose huge = Filled(BuildHand(0.30f, 0.05f));
            Assert.AreEqual(HandRange.TooCloseOrUnreliable, huge.Range);
            Assert.AreEqual(1L, huge.RejectedPalmFrames);

            Assert.AreEqual(HandRange.Ok, Filled(BuildHand(0.09f, 0.05f)).Range);
            HandPose absent = new HandPose();
            absent.Fill(null, true, Vector3.zero, 1f);
            Assert.AreEqual(HandRange.NotTracked, absent.Range);
        }

        // ================================================================ what the person is told

        [Test]
        public void Advice_ATooFarHandIsToldToMoveCloser() {
            string advice = HandRangeAdvice.Describe(Filled(BuildHand(0.03f, 0.02f)), VolumeVerdict.InVolume);
            StringAssert.Contains("TOO FAR", advice);
            StringAssert.Contains("closer", advice);
        }

        [Test]
        public void Advice_ATooCloseOrUnreliableHandIsNeverToldToStepCloser() {
            string big = HandRangeAdvice.Describe(Filled(BuildHand(0.30f, 0.05f)), VolumeVerdict.InVolume);
            string blown = HandRangeAdvice.Describe(Filled(BuildHand(0.147f, 2.273f)), VolumeVerdict.InVolume);
            StringAssert.Contains("TOO CLOSE", big);
            StringAssert.Contains("TOO CLOSE", blown);
            StringAssert.DoesNotContain("closer", big.ToLowerInvariant());
            StringAssert.DoesNotContain("closer", blown.ToLowerInvariant());
            StringAssert.Contains("further", blown);
        }

        [Test]
        public void Advice_ATooNearBodyOutranksWhateverTheHandSays() {
            string validHand = HandRangeAdvice.Describe(Filled(BuildHand(0.09f, 0.05f)), VolumeVerdict.TooNear);
            string nullHand = HandRangeAdvice.Describe(null, VolumeVerdict.TooNear);
            StringAssert.Contains("step back", validHand);
            StringAssert.Contains("step back", nullHand);
            StringAssert.DoesNotContain("closer", validHand.ToLowerInvariant());
        }

        [Test]
        public void Advice_UsableAndAbsentHandsHaveNothingToSay() {
            Assert.IsNull(HandRangeAdvice.Describe(Filled(BuildHand(0.09f, 0.05f)), VolumeVerdict.InVolume));
            HandPose absent = new HandPose();
            absent.Fill(null, true, Vector3.zero, 1f);
            Assert.IsNull(HandRangeAdvice.Describe(absent, VolumeVerdict.InVolume));
            Assert.IsNull(HandRangeAdvice.Describe(null, VolumeVerdict.InVolume));
        }

        // ================================================================ the HUD

        [Test]
        public void Hud_EveryLineFitsAnElevenHundredPixelCapture() {
            // 15 px IMGUI text is ~7.4 px per character, so 130 characters is ~960 px, which fits 1100 px
            // with the 16 px gutter even with every counter at eight digits. SR7's single 220-character
            // line did not.
            HumanizedStats worst = new HumanizedStats();
            worst.FramesProcessed = 99999999L;
            worst.FramesUntouched = 99999999L;
            worst.FramesHeldOutOfVolume = 99999999L;
            worst.CalibSamplesAccepted = 99999999L;
            worst.CalibRejectedUnmeasured = 99999999L;
            worst.CalibRejectedVolume = 99999999L;
            worst.CalibRejectedImplausible = 99999999L;
            worst.BoneLengthCorrected = 99999999L;
            worst.HeldJointFrames = 99999999L;
            worst.VelocityClamped = 99999999L;
            worst.RejectedNonFinite = 99999999L;
            worst.RejectedOrigin = 99999999L;
            worst.RejectedOutOfBody = 99999999L;
            worst.RejectedLowConfidence = 99999999L;
            worst.MaxCorrectionM = 12.345f;
            worst.MaxBoneErrorM = 12.345f;
            string[] lines = worst.ToHud(24, 24, 1.234f).Split('\n');
            Assert.GreaterOrEqual(lines.Length, 4);
            int i = 0;
            while (i < lines.Length) {
                Assert.LessOrEqual(lines[i].Length, 130, "line " + i + ": " + lines[i]);
                i = i + 1;
            }
        }

        [Test]
        public void Hud_CarriesTheFieldsThatWereClippedInSrSeven() {
            HumanizedStats s = new HumanizedStats();
            s.MaxCorrectionM = 0.123f;
            s.MaxBoneErrorM = 0.456f;
            s.CalibRejectedUnmeasured = 11;
            s.CalibRejectedVolume = 22;
            s.FramesHeldOutOfVolume = 33;
            string hud = s.ToHud(7, 24, 0.75f);
            StringAssert.Contains("maxCorrection 0.123", hud);
            StringAssert.Contains("maxBoneError 0.456", hud);
            StringAssert.Contains("7/24 bones ready", hud);
            StringAssert.Contains("unmeasured-depth 11", hud);
            StringAssert.Contains("out-of-volume 22", hud);
            StringAssert.Contains("held-out-of-volume 33", hud);
            StringAssert.Contains("trunk+head 0.75", hud);
        }

        // ================================================================ the whole stage, over UDP

        private const int TestPort = 18947;

        [Test]
        public void Stage_TooNearFramesCannotMoveTheFollowPositionOrTheCalibration() {
            TrackedStage stage = new TrackedStage(new SilentLog(), TestPort, 40f);
            stage.Start(true, true, false);
            UdpClient sender = new UdpClient();
            try {
                Rig rig = new Rig(stage, sender);

                // A: arrive at 2.0 m, then walk to 2.6 m, so there is a follow offset to protect.
                rig.Pump(0.6f, 2.0f, true, 0.26f);
                rig.Pump(1.2f, 2.6f, true, 0.26f);
                Assert.AreEqual(VolumeVerdict.InVolume, stage.Volume.Verdict);
                Assert.AreEqual(PoseAdmission.Trusted, stage.Volume.Admission);
                Assert.IsTrue(stage.IsLive);
                Assert.IsTrue(stage.HumanizedLayer.Calibration.IsCalibrated(LeftForearm),
                              "full-body measured depth in range must calibrate");
                float walked = stage.StagingOrigin().z;
                Assert.Greater(walked, 0.3f, "the figure followed the walk from 2.0 m to 2.6 m");
                float forearm = stage.HumanizedLayer.Calibration.Length(LeftForearm);
                rig.ResetLowest();
                int forearmSamples = stage.HumanizedLayer.Calibration.SampleCount(LeftForearm);

                // B: step to 0.7 m with a stretched, inferred forearm - the SR7 near-field frame. Held
                // for 0.55 s, which is inside the 0.75 s hold.
                rig.Pump(0.55f, 0.7f, false, 0.34f);
                Assert.AreEqual(PoseAdmission.Outside, stage.Volume.Admission);
                Assert.AreEqual(VolumeVerdict.TooNear, stage.Volume.Verdict);
                Assert.IsTrue(stage.IsLive, "inside the hold the last body is still shown");
                Assert.IsTrue(stage.Pose.Valid);
                Assert.AreEqual(walked, stage.StagingOrigin().z, 0.2f,
                                "a 0.7 m hip must not walk the figure toward the camera");
                Assert.GreaterOrEqual(rig.LowestFollowZ, walked - 0.2f);
                StringAssert.Contains("TOO CLOSE", stage.StatusLine());

                // ...and on through the end of the hold.
                rig.Pump(0.9f, 0.7f, false, 0.34f);
                Assert.IsTrue(stage.Volume.Expired);
                Assert.IsFalse(stage.IsLive, "after the hold nobody is drawn");
                Assert.IsFalse(stage.Pose.Valid);
                Assert.IsFalse(stage.IsAttract, "a person standing too close is not an empty room");
                StringAssert.Contains("TOO CLOSE", stage.StatusLine());
                Assert.Greater(stage.Volume.PosesTooNear, 0L);
                Assert.AreEqual(walked, stage.StagingOrigin().z, 0.2f);
                Assert.AreEqual(forearm, stage.HumanizedLayer.Calibration.Length(LeftForearm), 0.005f,
                                "the calibration learned nothing from the near-field frames");
                Assert.AreEqual(forearmSamples, stage.HumanizedLayer.Calibration.SampleCount(LeftForearm), 3);
                Assert.Greater(stage.HumanizedLayer.Stats.FramesHeldOutOfVolume, 0L);
                Assert.AreEqual(0L, stage.Provider.ParseErrorCount);
                Assert.Greater(stage.Provider.ReceivedCount, 30L, "the transport was healthy throughout");
            } finally {
                sender.Close();
                stage.Stop();
            }
        }

        [Test]
        public void Stage_RecoversToNormalOutputWhenValidDepthResumes() {
            TrackedStage stage = new TrackedStage(new SilentLog(), TestPort + 1, 40f);
            stage.Start(true, true, false);
            UdpClient sender = new UdpClient();
            try {
                Rig rig = new Rig(stage, sender, TestPort + 1);
                rig.Pump(1.4f, 2.4f, true, 0.26f);
                rig.Pump(1.5f, 0.7f, false, 0.34f);
                Assert.IsTrue(stage.Volume.Expired);

                rig.Pump(1.6f, 2.4f, true, 0.26f);
                Assert.IsFalse(stage.Volume.Expired);
                Assert.IsTrue(stage.IsLive);
                Assert.IsTrue(stage.Pose.Valid, "the body is back");
                Assert.AreEqual(PoseAdmission.Trusted, stage.Volume.Admission);
                Assert.AreEqual(VolumeVerdict.InVolume, stage.Volume.Verdict);
                Assert.AreEqual("TRACKING", stage.StatusLine());
                Assert.AreEqual(0.26f, stage.HumanizedLayer.Calibration.Length(LeftForearm), 0.005f);
            } finally {
                sender.Close();
                stage.Stop();
            }
        }

        // Drives a stage with synthetic datagrams at ~30 Hz in real time, which is what the provider's
        // receive-clock interpolation needs. Sends the sidecar's own frame: X right, Y DOWN, Z forward.
        private sealed class Rig {
            private readonly TrackedStage stage;
            private readonly UdpClient sender;
            private readonly IPEndPoint target;
            private long seq;

            public float LowestFollowZ = float.MaxValue;

            public void ResetLowest() {
                LowestFollowZ = float.MaxValue;
            }

            public Rig(TrackedStage stage, UdpClient sender) : this(stage, sender, TestPort) {
            }

            public Rig(TrackedStage stage, UdpClient sender, int port) {
                this.stage = stage;
                this.sender = sender;
                target = new IPEndPoint(IPAddress.Loopback, port);
            }

            public void Pump(float seconds, float depthMetres, bool measured, float forearmMetres) {
                int steps = (int)(seconds * 30f);
                int i = 0;
                while (i < steps) {
                    seq = seq + 1;
                    byte[] payload = Encoding.UTF8.GetBytes(Json(seq, depthMetres, measured, forearmMetres));
                    sender.Send(payload, payload.Length, target);
                    Thread.Sleep(33);
                    stage.Tick(Dt);
                    if (stage.StagingOrigin().z < LowestFollowZ) {
                        LowestFollowZ = stage.StagingOrigin().z;
                    }
                    i = i + 1;
                }
            }
        }

        private static string Json(long seq, float depthMetres, bool measured, float forearmMetres) {
            PoseFrame body = new PoseFrame();
            Body(body, 0.0, forearmMetres);
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(2600);
            double epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            sb.Append("{\"seq\":").Append(seq).Append(",\"sid\":\"f48-test\",\"t\":")
              .Append(epoch.ToString("F4", ci)).Append(",\"lm\":[");
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 p = body.GetLandmark((JointId)i).Position;
                if (i > 0) {
                    sb.Append(',');
                }
                // The sidecar's Y points DOWN; the converter flips it, so send the negation.
                sb.Append('[').Append(p.x.ToString("F4", ci)).Append(',')
                  .Append((-p.y).ToString("F4", ci)).Append(',')
                  .Append(p.z.ToString("F4", ci)).Append(",0.9]");
                i = i + 1;
            }
            sb.Append("],\"xyz\":[0,0,").Append((depthMetres * 1000f).ToString("F1", ci)).Append("],\"src\":[");
            i = 0;
            while (i < PoseFrame.LandmarkCount) {
                if (i > 0) {
                    sb.Append(',');
                }
                // Near-field degradation as SR7 showed it: wrists and hands lose their stereo depth.
                bool inferred = !measured && (i == 15 || i == 16 || i >= 17);
                sb.Append(inferred ? '0' : '1');
                i = i + 1;
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private sealed class SilentLog : ILogService {
            public void Log(LogLevel level, string message) {
            }

            public void LogException(Exception exception, string context) {
            }

            public void Flush() {
            }
        }

        // ================================================================ builders

        private static PoseFrame Posed(float depthMetres, double timestamp) {
            PoseFrame frame = new PoseFrame();
            frame.SetMeta(timestamp, true);
            frame.SetRootPosition(new Vector3(0f, 0f, depthMetres));
            return frame;
        }

        // Feed `count` frames of one body through the layer; returns the last output. Timestamps start at
        // `startSeconds` so successive calls continue one session.
        private static PoseFrame RunFrames(HumanizedSkeleton layer, TrackingTelemetry telemetry,
                                           PoseAdmission admission, float forearmMetres, int count,
                                           double startSeconds) {
            PoseFrame input = new PoseFrame();
            PoseFrame shown = null;
            int i = 0;
            while (i < count) {
                Body(input, startSeconds + i * 0.033, forearmMetres);
                shown = layer.Process(input, Dt, telemetry, admission);
                i = i + 1;
            }
            return shown;
        }

        // Telemetry with a depth channel. `bodyMeasured` covers every joint; `wristsMeasured` then
        // overrides the wrists and hands, which is where SR7's degradation showed.
        private static TrackingTelemetry Telemetry(bool bodyMeasured, bool wristsMeasured) {
            TrackingTelemetry t = new TrackingTelemetry();
            t.HasDepthChannel = true;
            int i = 0;
            while (i < TrackingTelemetry.JointCount) {
                bool isWristOrHand = i == 15 || i == 16 || (i >= 17 && i <= 22);
                t.SetDepthMeasured(i, isWristOrHand ? wristsMeasured : bodyMeasured);
                i = i + 1;
            }
            return t;
        }

        // A standing adult, arms hanging, in the layer's frame: metres, hip-centred, Y UP. Plain vector
        // arithmetic, so it needs no engine call. The left forearm takes `forearmMetres`; the right is
        // always the normal 0.26 m, so one arm can be stretched while the other is the control.
        private static void Body(PoseFrame frame, double timestamp, float forearmMetres) {
            Vector3[] p = new Vector3[PoseFrame.LandmarkCount];
            p[23] = new Vector3(0.10f, 0f, 0f);
            p[24] = new Vector3(-0.10f, 0f, 0f);
            p[11] = new Vector3(0.18f, 0.50f, 0f);
            p[12] = new Vector3(-0.18f, 0.50f, 0f);
            p[0] = new Vector3(0f, 0.75f, 0.02f);
            p[13] = p[11] + new Vector3(0f, -0.28f, 0f);
            p[14] = p[12] + new Vector3(0f, -0.28f, 0f);
            p[15] = p[13] + new Vector3(0f, -forearmMetres, 0f);
            p[16] = p[14] + new Vector3(0f, -0.26f, 0f);
            p[25] = p[23] + new Vector3(0f, -0.42f, 0f);
            p[26] = p[24] + new Vector3(0f, -0.42f, 0f);
            p[27] = p[25] + new Vector3(0f, -0.40f, 0f);
            p[28] = p[26] + new Vector3(0f, -0.40f, 0f);
            p[29] = p[27] + new Vector3(0f, -0.04f, -0.04f);
            p[30] = p[28] + new Vector3(0f, -0.04f, -0.04f);
            p[31] = p[27] + new Vector3(0f, -0.04f, 0.12f);
            p[32] = p[28] + new Vector3(0f, -0.04f, 0.12f);
            p[17] = p[15] + new Vector3(0f, -0.08f, 0f);
            p[19] = p[15] + new Vector3(0f, -0.08f, 0.01f);
            p[21] = p[15] + new Vector3(0.02f, -0.056f, 0f);
            p[18] = p[16] + new Vector3(0f, -0.08f, 0f);
            p[20] = p[16] + new Vector3(0f, -0.08f, 0.01f);
            p[22] = p[16] + new Vector3(-0.02f, -0.056f, 0f);
            int f = 1;
            while (f <= 10) {
                p[f] = p[0] + new Vector3(0.01f * ((f % 3) - 1), 0.01f * ((f % 2) - 1), 0f);
                f = f + 1;
            }
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                frame.SetLandmark(i, new PoseLandmark(p[i], Conf));
                i = i + 1;
            }
            frame.SetMeta(timestamp, true);
        }

        // The same adult with both arms held straight out to the sides - a large, legitimate change of
        // pose, used to prove a return from a held gap is not read as a teleport.
        private static void BodyArmsOut(PoseFrame frame, double timestamp, float forearmMetres) {
            Body(frame, timestamp, forearmMetres);
            Vector3 leftShoulder = frame.GetLandmark(JointId.LeftShoulder).Position;
            Vector3 rightShoulder = frame.GetLandmark(JointId.RightShoulder).Position;
            Vector3 leftElbow = leftShoulder + new Vector3(0.28f, 0f, 0f);
            Vector3 rightElbow = rightShoulder + new Vector3(-0.28f, 0f, 0f);
            Vector3 leftWrist = leftElbow + new Vector3(forearmMetres, 0f, 0f);
            Vector3 rightWrist = rightElbow + new Vector3(-0.26f, 0f, 0f);
            frame.SetLandmark((int)JointId.LeftElbow, new PoseLandmark(leftElbow, Conf));
            frame.SetLandmark((int)JointId.RightElbow, new PoseLandmark(rightElbow, Conf));
            frame.SetLandmark((int)JointId.LeftWrist, new PoseLandmark(leftWrist, Conf));
            frame.SetLandmark((int)JointId.RightWrist, new PoseLandmark(rightWrist, Conf));
            frame.SetLandmark(17, new PoseLandmark(leftWrist + new Vector3(0.08f, 0f, 0f), Conf));
            frame.SetLandmark(19, new PoseLandmark(leftWrist + new Vector3(0.08f, 0f, 0.01f), Conf));
            frame.SetLandmark(21, new PoseLandmark(leftWrist + new Vector3(0.056f, 0.02f, 0f), Conf));
            frame.SetLandmark(18, new PoseLandmark(rightWrist + new Vector3(-0.08f, 0f, 0f), Conf));
            frame.SetLandmark(20, new PoseLandmark(rightWrist + new Vector3(-0.08f, 0f, 0.01f), Conf));
            frame.SetLandmark(22, new PoseLandmark(rightWrist + new Vector3(-0.056f, 0.02f, 0f), Conf));
        }

        // A 21-point hand with a chosen palm length and thumb-tip to index-tip gap, in the wire's
        // MediaPipe order. Knuckles lie across the palm so the orientation basis is well formed. Every
        // landmark not named is left at the wrist, which is a legal (clenched) position.
        private static Vector3[] BuildHand(float palm, float tipGap) {
            Vector3[] p = new Vector3[RawHandFrame.LandmarkCount];
            p[0] = Vector3.zero;
            p[9] = new Vector3(0f, palm, 0f);
            p[5] = new Vector3(-0.3f * palm, palm * 0.95f, 0f);
            p[17] = new Vector3(0.3f * palm, palm * 0.9f, 0f);
            p[2] = new Vector3(-0.35f * palm, palm * 0.4f, 0f);
            p[13] = new Vector3(0.18f * palm, palm * 0.93f, 0f);
            p[4] = new Vector3(-0.5f * palm, palm * 1.1f, 0f);
            p[8] = p[4] + new Vector3(tipGap, 0f, 0f);
            p[12] = new Vector3(0f, palm * 1.9f, 0f);
            p[16] = new Vector3(0.18f * palm, palm * 1.8f, 0f);
            p[20] = new Vector3(0.3f * palm, palm * 1.7f, 0f);
            return p;
        }

        private static HandPose Filled(Vector3[] points) {
            RawHandFrame raw = new RawHandFrame();
            raw.SetHand(false, points);
            raw.SetMeta(1.0);
            HandPose hand = new HandPose();
            hand.Fill(raw, false, Vector3.zero, 1f);
            return hand;
        }
    }
}
