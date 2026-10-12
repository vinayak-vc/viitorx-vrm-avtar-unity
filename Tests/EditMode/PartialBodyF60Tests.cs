using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-60 tests: a body the camera cuts off (a laptop webcam). The sidecar anchors each frame on the hips, else the
    /// shoulders, else the nose, and says which in the datagram's <c>fr</c> field; Unity must carry that field end to end
    /// and judge a frame with an ESTIMATED root against a near limit that fits a webcam.
    ///
    /// WHAT THESE ARE A REGRESSION FOR. Measured on RTMW3D (python-sidecar~ ADR-096): on waist-up and chest-up crops of a
    /// full-body clip the model still answers for the hips at confidence 0.6, outside the image, so the old hip gate
    /// either passed a frame with a hallucinated root and a 2.0 m depth, or dropped it. The estimated root of a person at
    /// 0.4-0.8 m is then refused by the stereo camera's 1.35 m near limit, which is the failure a webcam user would see:
    /// "TOO CLOSE" for sitting at their own desk.
    /// </summary>
    public sealed class PartialBodyF60Tests {
        private const float Dt = 1f / 30f;
        private const int TestPort = 18961;

        [SetUp]
        public void ResetVolumeDefaults() {
            OperatingVolume.ResetToDefaults();
        }

        [TearDown]
        public void RestoreVolumeDefaults() {
            OperatingVolume.ResetToDefaults();
        }

        private static PoseFrame Posed(float depthMetres, double timestamp, PoseAnchor anchor) {
            PoseFrame frame = new PoseFrame();
            frame.SetMeta(timestamp, true);
            frame.SetRootPosition(new Vector3(0f, 0f, depthMetres));
            frame.SetAnchor(anchor);
            return frame;
        }

        // ================================================================ the data carries the anchor

        [Test]
        public void Anchor_APoseFrameIsAnchoredOnTheHipsUnlessTheSenderSaysOtherwise() {
            PoseFrame frame = new PoseFrame();
            Assert.AreEqual(PoseAnchor.Hips, frame.Anchor);
            Assert.IsFalse(frame.RootEstimated);
            frame.SetAnchor(PoseAnchor.Shoulders);
            Assert.IsTrue(frame.RootEstimated);
            frame.MarkInvalid(1.0);
            Assert.AreEqual(PoseAnchor.Hips, frame.Anchor, "an invalid frame forgets its anchor, so the next one starts clean");
        }

        [Test]
        public void Anchor_TheWireNumberMapsToTheEnumAndNothingElseIsTrusted() {
            Assert.AreEqual(PoseAnchor.Hips, PoseFrame.AnchorFromWire(0));
            Assert.AreEqual(PoseAnchor.Shoulders, PoseFrame.AnchorFromWire(1));
            Assert.AreEqual(PoseAnchor.Head, PoseFrame.AnchorFromWire(2));
            Assert.AreEqual(PoseAnchor.Hips, PoseFrame.AnchorFromWire(3), "an unknown tier must not relax the volume");
            Assert.AreEqual(PoseAnchor.Hips, PoseFrame.AnchorFromWire(-1));
            Assert.AreEqual(PoseAnchor.Hips, PoseFrame.AnchorFromWire(int.MaxValue));
        }

        [Test]
        public void Anchor_SurvivesTheInterpolationBufferAndTheWeakerAnchorWins() {
            PoseBuffer buffer = new PoseBuffer(8);
            buffer.Push(Posed(2.0f, 100.0, PoseAnchor.Hips), 1, 100.0);
            buffer.Push(Posed(2.0f, 100.1, PoseAnchor.Shoulders), 2, 100.1);
            PoseFrame mid = new PoseFrame();
            Assert.IsTrue(buffer.Sample(100.05, mid));
            Assert.AreEqual(PoseAnchor.Shoulders, mid.Anchor, "a root blended from a measured and an estimated one is an estimate");
            PoseFrame newest = new PoseFrame();
            Assert.IsTrue(buffer.Sample(100.5, newest));
            Assert.AreEqual(PoseAnchor.Shoulders, newest.Anchor, "a clamped-to-newest sample keeps the newest frame's anchor");
            PoseFrame older = new PoseFrame();
            Assert.IsTrue(buffer.Sample(99.0, older));
            Assert.AreEqual(PoseAnchor.Hips, older.Anchor, "and the oldest keeps its own");
        }

        [Test]
        public void Anchor_SurvivesTheCrowdCopy() {
            CrowdFrame crowd = new CrowdFrame();
            crowd.Begin();
            PersonPose a = crowd.Add(7, "CONFIRMED");
            a.Pose.SetMeta(1.0, true);
            a.Pose.SetAnchor(PoseAnchor.Shoulders);
            PersonPose b = crowd.Add(8, "CONFIRMED");
            b.Pose.SetMeta(1.0, true);
            crowd.SetMeta(1.0, true);
            CrowdFrame copy = new CrowdFrame();
            crowd.CopyTo(copy);
            Assert.AreEqual(PoseAnchor.Shoulders, copy.ById(7).Pose.Anchor);
            Assert.AreEqual(PoseAnchor.Hips, copy.ById(8).Pose.Anchor, "each person keeps their OWN anchor");
        }

        [Test]
        public void Anchor_TheSkeletonPoseCentreIsTheRootWhenTheHipsAreNotInThePicture() {
            // A hip that is half-present would pull a mean-of-the-hips centre off the root; the root is stated instead.
            PoseFrame frame = new PoseFrame();
            frame.SetMeta(1.0, true);
            frame.SetRootPosition(new Vector3(0.1f, 0f, 0.6f));
            frame.SetAnchor(PoseAnchor.Shoulders);
            frame.SetLandmark((int)JointId.LeftShoulder, new PoseLandmark(new Vector3(0.2f, 0.5f, 0f), 0.9f));
            frame.SetLandmark((int)JointId.RightShoulder, new PoseLandmark(new Vector3(-0.2f, 0.5f, 0f), 0.9f));
            frame.SetLandmark((int)JointId.LeftHip, new PoseLandmark(new Vector3(0.3f, -0.1f, 0f), 0.35f));
            SkeletonPose pose = new SkeletonPose();
            Vector3 origin = new Vector3(0.4f, 0.95f, 0f);
            pose.Fill(frame, origin, 1f, Dt);
            Assert.IsTrue(pose.RootEstimated);
            Assert.AreEqual(origin.x, pose.Centre.x, 1e-5f);
            Assert.AreEqual(origin.y, pose.Centre.y, 1e-5f);

            frame.SetAnchor(PoseAnchor.Hips);
            frame.SetLandmark((int)JointId.RightHip, new PoseLandmark(new Vector3(-0.1f, -0.1f, 0f), 0.9f));
            frame.SetLandmark((int)JointId.LeftHip, new PoseLandmark(new Vector3(0.1f, -0.1f, 0f), 0.9f));
            pose.Fill(frame, origin, 1f, Dt);
            Assert.IsFalse(pose.RootEstimated);
            Assert.AreEqual(origin.y - 0.1f, pose.Centre.y, 1e-5f, "with the hips measured the centre is still their mean");
        }

        // ================================================================ the volume

        [Test]
        public void Volume_AnEstimatedRootIsJudgedAgainstAWebcamNearLimit() {
            Assert.AreEqual(VolumeVerdict.TooNear, OperatingVolume.Classify(0.6f, VolumeVerdict.NoPose),
                            "a measured hip at 0.6 m is nearer than the stereo camera can measure");
            Assert.AreEqual(VolumeVerdict.InVolume, OperatingVolume.Classify(0.6f, VolumeVerdict.NoPose, true),
                            "an estimated root at 0.6 m is a person at a laptop");
            Assert.AreEqual(VolumeVerdict.TooNear, OperatingVolume.Classify(0.3f, VolumeVerdict.NoPose, true),
                            "but the relaxed limit is a limit, not a free pass");
            Assert.AreEqual(VolumeVerdict.TooFar, OperatingVolume.Classify(5.2f, VolumeVerdict.NoPose, true), "the far edge is shared");
            Assert.AreEqual(VolumeVerdict.RootInvalid, OperatingVolume.Classify(0.01f, VolumeVerdict.NoPose, true),
                            "and a zero-filled root is invalid whatever the limit");
            Assert.AreEqual(VolumeVerdict.NoRoot, OperatingVolume.Classify(-1f, VolumeVerdict.NoPose, true));
        }

        [Test]
        public void Volume_TheRelaxedLimitHasItsOwnHysteresis() {
            VolumeVerdict v = OperatingVolume.Classify(0.30f, VolumeVerdict.NoPose, true);
            Assert.AreEqual(VolumeVerdict.TooNear, v);
            v = OperatingVolume.Classify(0.37f, v, true);
            Assert.AreEqual(VolumeVerdict.TooNear, v, "0.37 m has not cleared 0.35 + the 5 cm margin");
            v = OperatingVolume.Classify(0.41f, v, true);
            Assert.AreEqual(VolumeVerdict.InVolume, v);
        }

        [Test]
        public void Volume_TheRelaxedLimitIsConfigurableAndNeverAboveTheStrictOne() {
            OperatingVolume.Configure(1.35f, 4.5f, 0.75f, 6, 0.5f);
            Assert.AreEqual(0.5f, OperatingVolume.PartialNearLimitMetres, 1e-6f);
            Assert.AreEqual(VolumeVerdict.TooNear, OperatingVolume.Classify(0.45f, VolumeVerdict.NoPose, true));
            OperatingVolume.Configure(1.35f, 4.5f, 0.75f, 6, 3.0f);
            Assert.AreEqual(1.35f, OperatingVolume.PartialNearLimitMetres, 1e-6f, "a relaxed limit above the strict one would be a stricter one");
            OperatingVolume.Configure(1.35f, 4.5f, 0.75f, 6, -2f);
            Assert.Greater(OperatingVolume.PartialNearLimitMetres, 0f, "nonsense is repaired, not trusted");
            OperatingVolume.Configure(1.0f, 4.5f, 0.75f, 6);
            Assert.AreEqual(OperatingVolume.DefaultPartialNearLimitMetres, OperatingVolume.PartialNearLimitMetres, 1e-6f,
                            "the old four-argument call leaves the relaxed limit at its default");
            OperatingVolume.ResetToDefaults();
            Assert.AreEqual(OperatingVolume.DefaultPartialNearLimitMetres, OperatingVolume.PartialNearLimitMetres);
        }

        [Test]
        public void Gate_AWebcamUserAtHalfAMetreIsTrustedAndAHipAtHalfAMetreIsStillRefused() {
            OperatingVolumeGate webcam = new OperatingVolumeGate();
            OperatingVolumeGate stereo = new OperatingVolumeGate();
            for (int i = 0; i < 10; i++) {
                webcam.Update(Posed(0.5f, 100.0 + i * Dt, PoseAnchor.Shoulders), Dt);
                stereo.Update(Posed(0.5f, 100.0 + i * Dt, PoseAnchor.Hips), Dt);
            }
            Assert.AreEqual(VolumeVerdict.InVolume, webcam.Verdict);
            Assert.AreEqual(PoseAdmission.Trusted, webcam.Admission, "settled after the usual number of poses");
            Assert.IsTrue(webcam.RootEstimated);
            Assert.AreEqual(VolumeVerdict.TooNear, stereo.Verdict, "a measured hip at 0.5 m is exactly the SR7 failure the volume exists for");
            Assert.AreEqual(PoseAdmission.Outside, stereo.Admission);
            Assert.IsFalse(stereo.RootEstimated);
        }

        [Test]
        public void Gate_ATooCloseWebcamUserIsToldTheRelaxedBand() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(0.2f, 100.0, PoseAnchor.Shoulders), Dt);
            Assert.AreEqual(VolumeVerdict.TooNear, gate.Verdict);
            string message = gate.Message();
            StringAssert.Contains("TOO CLOSE", message);
            StringAssert.Contains("estimated hip", message, "it says the figure is an estimate");
            StringAssert.Contains(OperatingVolume.PartialNearLimitMetres.ToString("F2", CultureInfo.InvariantCulture), message,
                                  "and names the band it was judged against, not the stereo camera's");
            StringAssert.Contains("est.hip", gate.Summary());
        }

        [Test]
        public void Gate_TheMeasuredAndEstimatedBandsDoNotLeakIntoEachOther() {
            OperatingVolumeGate gate = new OperatingVolumeGate();
            gate.Update(Posed(0.5f, 100.0, PoseAnchor.Shoulders), Dt);
            Assert.AreEqual(VolumeVerdict.InVolume, gate.Verdict);
            gate.Update(Posed(0.5f, 100.0 + Dt, PoseAnchor.Hips), Dt);
            Assert.AreEqual(VolumeVerdict.TooNear, gate.Verdict, "the very next pose, with its hips back, is a stereo-camera pose again");
            gate.Update(Posed(0.5f, 100.0 + 2 * Dt, PoseAnchor.Shoulders), Dt);
            Assert.AreEqual(VolumeVerdict.InVolume, gate.Verdict, "...and it relaxes again when they are gone");
        }

        // ================================================================ the launch switch

        private static string BuildArguments(SidecarLaunchOptions options) {
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(null, options);
            SidecarPathSet paths = SidecarPaths.Resolve("D:/sidecar", string.Empty);
            MethodInfo build = typeof(SidecarProcessLauncher).GetMethod("BuildArguments", BindingFlags.Instance | BindingFlags.NonPublic);
            return (string)build.Invoke(launcher, new object[] { paths });
        }

        [Test]
        public void Launch_PartialBodyIsTheDefaultAndSendsNothing() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoSource = "camera:0";
            Assert.IsTrue(options.VideoPartialBody, "the options default matches the sender's");
            StringAssert.DoesNotContain("partial-body", BuildArguments(options), "the default command line is the F-57 one, unchanged");
        }

        [Test]
        public void Launch_TurningItOffSendsTheOptOutAndNothingElseChanges() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoSource = "camera:0";
            string before = BuildArguments(options);
            options.VideoPartialBody = false;
            string after = BuildArguments(options);
            StringAssert.Contains("--no-video-partial-body", after);
            Assert.AreEqual(before, after.Replace(" --no-video-partial-body", string.Empty), "nothing else on the command line changes");
        }

        [Test]
        public void Launch_TheOpenSwitchNeverReachesACameraSessionWithNoVideo() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoPartialBody = false;
            StringAssert.DoesNotContain("partial-body", BuildArguments(options), "the OAK-D command line must be untouched whatever the bool says");
        }

        // ================================================================ end to end: UDP -> stage

        // A person at a desk: head, shoulders, arms. No hips, no legs. Sent as the sidecar sends it (Y down).
        private static string CutOffJson(long seq, float depthMetres, int anchor, bool includeAnchor) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(2200);
            double epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            sb.Append("{\"seq\":").Append(seq).Append(",\"sid\":\"f60-test\",\"t\":").Append(epoch.ToString("F4", ci));
            if (includeAnchor) {
                sb.Append(",\"fr\":").Append(anchor);
            }
            sb.Append(",\"lm\":[");
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                float x = 0f;
                float y = 0f;
                float c = 0f;
                if (i == 0) {
                    x = 0f; y = -0.72f; c = 0.9f;
                } else if (i == 11) {
                    x = -0.2f; y = -0.5f; c = 0.9f;
                } else if (i == 12) {
                    x = 0.2f; y = -0.5f; c = 0.9f;
                } else if (i == 13) {
                    x = -0.26f; y = -0.28f; c = 0.9f;
                } else if (i == 14) {
                    x = 0.26f; y = -0.28f; c = 0.9f;
                } else if (i == 15) {
                    x = -0.28f; y = -0.06f; c = 0.9f;
                } else if (i == 16) {
                    x = 0.28f; y = -0.06f; c = 0.9f;
                }
                if (i > 0) {
                    sb.Append(',');
                }
                sb.Append('[').Append(x.ToString("F4", ci)).Append(',').Append(y.ToString("F4", ci)).Append(",0,")
                  .Append(c.ToString("F2", ci)).Append(']');
            }
            sb.Append("],\"xyz\":[0,-300,").Append((depthMetres * 1000f).ToString("F1", ci)).Append("],\"src\":[");
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                if (i > 0) {
                    sb.Append(',');
                }
                sb.Append('0');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void Pump(TrackedStage stage, UdpClient sender, int port, ref long seq, float seconds, float depth,
                                 int anchor, bool includeAnchor) {
            IPEndPoint target = new IPEndPoint(IPAddress.Loopback, port);
            int steps = (int)(seconds * 30f);
            for (int i = 0; i < steps; i++) {
                seq = seq + 1;
                byte[] payload = Encoding.UTF8.GetBytes(CutOffJson(seq, depth, anchor, includeAnchor));
                sender.Send(payload, payload.Length, target);
                Thread.Sleep(33);
                stage.Tick(Dt);
            }
        }

        [Test]
        public void Stage_AWebcamBodyAtHalfAMetreIsTrackedNotRefusedAsTooClose() {
            TrackedStage stage = new TrackedStage(new SilentLog(), TestPort, 40f);
            stage.Start(true, true, false);
            UdpClient sender = new UdpClient();
            try {
                long seq = 0;
                Pump(stage, sender, TestPort, ref seq, 1.8f, 0.5f, 1, true);
                Assert.AreEqual(PoseAnchor.Shoulders, stage.Pose.Anchor, "the sender's fr field reached the stage's pose");
                Assert.IsTrue(stage.Pose.RootEstimated);
                Assert.AreEqual(VolumeVerdict.InVolume, stage.Volume.Verdict, "0.5 m is inside the relaxed band");
                Assert.AreEqual(PoseAdmission.Trusted, stage.Volume.Admission);
                Assert.IsTrue(stage.IsLive, "past the 0.75 s hold, and still live");
                Assert.IsFalse(stage.Volume.Expired);
                Assert.IsTrue(stage.Pose.Valid);
                Assert.IsTrue(stage.Pose.Present[(int)JointId.LeftShoulder] && stage.Pose.Present[(int)JointId.RightShoulder]);
                Assert.IsFalse(stage.Pose.Present[(int)JointId.LeftHip], "the hips were never sent as present");
                Assert.AreEqual("TRACKING", stage.StatusLine());
                Assert.AreEqual(0L, stage.Provider.ParseErrorCount);
            } finally {
                sender.Close();
                stage.Stop();
            }
        }

        [Test]
        public void Stage_TheSameFramesWithoutTheFieldAreTheOldStereoBehaviour() {
            TrackedStage stage = new TrackedStage(new SilentLog(), TestPort + 1, 40f);
            stage.Start(true, true, false);
            UdpClient sender = new UdpClient();
            try {
                long seq = 0;
                Pump(stage, sender, TestPort + 1, ref seq, 1.8f, 0.5f, 0, false);
                Assert.AreEqual(PoseAnchor.Hips, stage.Pose.Anchor, "a datagram with no fr field is a hip-anchored one");
                Assert.AreEqual(VolumeVerdict.TooNear, stage.Volume.Verdict, "so 0.5 m is refused exactly as before F-60");
                Assert.IsTrue(stage.Volume.Expired);
                StringAssert.Contains("TOO CLOSE", stage.StatusLine());
            } finally {
                sender.Close();
                stage.Stop();
            }
        }

        // ================================================================ the floor must not be made up
        //
        // Measured live on 2026-10-12 (two recordings of a seated person at a desk, phone camera): the model answered for
        // feet that are not in the picture, a few frames at a time and ABOVE the hips; the floor FALLS in 50 ms so each was
        // adopted, and the grounding loop - which chases a floor held in world coordinates - then moved the whole figure
        // until it was 8 m off the screen. These tests are the regression for both halves of that.

        private static readonly Vector3 FloorOrigin = new Vector3(0f, 0.95f, 0f);

        // Y is up here, as in the pose the provider hands the stage (flipY already applied).
        private static PoseFrame FloorFrame(float footY, bool ankles, PoseAnchor anchor) {
            PoseFrame f = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                f.SetLandmark(i, new PoseLandmark(Vector3.zero, 0f));
            }
            f.SetLandmark((int)JointId.LeftShoulder, new PoseLandmark(new Vector3(-0.18f, 0.5f, 0f), 0.9f));
            f.SetLandmark((int)JointId.RightShoulder, new PoseLandmark(new Vector3(0.18f, 0.5f, 0f), 0.9f));
            f.SetLandmark((int)JointId.LeftHip, new PoseLandmark(new Vector3(-0.09f, 0f, 0f), 0.9f));
            f.SetLandmark((int)JointId.RightHip, new PoseLandmark(new Vector3(0.09f, 0f, 0f), 0.9f));
            if (ankles) {
                f.SetLandmark((int)JointId.LeftAnkle, new PoseLandmark(new Vector3(-0.1f, footY + 0.12f, 0f), 0.9f));
                f.SetLandmark((int)JointId.RightAnkle, new PoseLandmark(new Vector3(0.1f, footY + 0.12f, 0f), 0.9f));
            }
            f.SetLandmark((int)JointId.LeftHeel, new PoseLandmark(new Vector3(-0.1f, footY, 0f), 0.9f));
            f.SetLandmark((int)JointId.RightHeel, new PoseLandmark(new Vector3(0.1f, footY, 0f), 0.9f));
            f.SetLandmark((int)JointId.LeftFootIndex, new PoseLandmark(new Vector3(-0.1f, footY, 0f), 0.9f));
            f.SetLandmark((int)JointId.RightFootIndex, new PoseLandmark(new Vector3(0.1f, footY, 0f), 0.9f));
            f.SetMeta(0.0, true);
            f.SetRootPosition(new Vector3(0f, 0f, 2.0f));
            f.SetAnchor(anchor);
            return f;
        }

        private static SkeletonPose Filled(PoseFrame frame, int frames) {
            SkeletonPose pose = new SkeletonPose();
            for (int i = 0; i < frames; i++) {
                pose.Fill(frame, FloorOrigin, 1f, Dt);
            }
            return pose;
        }

        [Test]
        public void Floor_AStandingBodyStillFindsItsFloor() {
            SkeletonPose pose = Filled(FloorFrame(-0.9f, true, PoseAnchor.Hips), 30);
            Assert.IsTrue(pose.HasFloor);
            Assert.IsTrue(pose.FloorFresh);
            Assert.AreEqual(FloorOrigin.y - 0.9f, pose.FloorY, 0.02f);
        }

        [Test]
        public void Floor_FeetOfABodyWhoseRootIsEstimatedAreNotAFloor() {
            Assert.IsFalse(Filled(FloorFrame(-0.9f, true, PoseAnchor.Shoulders), 60).HasFloor,
                "hips not in the picture, so the model's feet are a guess");
            Assert.IsFalse(Filled(FloorFrame(-0.9f, true, PoseAnchor.Head), 60).HasFloor);
        }

        [Test]
        public void Floor_AFootAboveOrBesideTheHipsIsNotAFloor() {
            Assert.IsFalse(Filled(FloorFrame(0.55f, true, PoseAnchor.Hips), 60).HasFloor, "measured: invented feet 0.5 m ABOVE the hips");
            Assert.IsFalse(Filled(FloorFrame(-0.15f, true, PoseAnchor.Hips), 60).HasFloor, "or within 0.2 m of them");
            Assert.IsTrue(Filled(FloorFrame(-0.45f, true, PoseAnchor.Hips), 60).HasFloor, "a seated person's feet are 0.45 m down and are real");
        }

        [Test]
        public void Floor_AFootWithoutItsAnkleIsNotAContact() {
            Assert.IsFalse(Filled(FloorFrame(-0.9f, false, PoseAnchor.Hips), 60).HasFloor);
        }

        [Test]
        public void Floor_NeedsTheContactToHoldForAFewFramesBeforeItMovesTheFloor() {
            SkeletonPose pose = Filled(FloorFrame(-0.9f, true, PoseAnchor.Hips), SkeletonPose.FloorMinStreakFrames - 1);
            Assert.IsFalse(pose.HasFloor, "one frame short: still no floor");
            pose.Fill(FloorFrame(-0.9f, true, PoseAnchor.Hips), FloorOrigin, 1f, Dt);
            Assert.IsTrue(pose.HasFloor);
            // A foot that comes and goes never accumulates the streak.
            SkeletonPose flicker = new SkeletonPose();
            for (int i = 0; i < 60; i++) {
                flicker.Fill(FloorFrame(-0.9f, i % 3 != 0, PoseAnchor.Hips), FloorOrigin, 1f, Dt);
            }
            Assert.IsFalse(flicker.HasFloor, "a contact present two frames in three is a flicker");
        }

        [Test]
        public void Floor_FreshOnlyWhileAContactIsBeingMeasured() {
            SkeletonPose pose = Filled(FloorFrame(-0.9f, true, PoseAnchor.Hips), 30);
            Assert.IsTrue(pose.FloorFresh);
            pose.Fill(FloorFrame(-0.9f, false, PoseAnchor.Hips), FloorOrigin, 1f, Dt);
            Assert.IsFalse(pose.FloorFresh, "the floor is kept, but not re-measured");
            Assert.IsTrue(pose.HasFloor);
        }

        // A standing person as the sidecar sends it (Y down), with feet this far below the hips; none when feetBelow <= 0.
        private static string StandingJson(long seq, float feetBelow) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(2400);
            double epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            sb.Append("{\"seq\":").Append(seq).Append(",\"sid\":\"f60-ground\",\"t\":").Append(epoch.ToString("F4", ci)).Append(",\"lm\":[");
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                float x = 0f;
                float y = 0f;
                float c = 0f;
                if (i == 0) {
                    x = 0f; y = -0.72f; c = 0.9f;
                } else if (i == 11 || i == 12) {
                    x = i == 11 ? -0.18f : 0.18f; y = -0.5f; c = 0.9f;
                } else if (i == 23 || i == 24) {
                    x = i == 23 ? -0.09f : 0.09f; y = 0f; c = 0.9f;
                } else if (feetBelow > 0f && (i == 27 || i == 28)) {
                    x = i == 27 ? -0.1f : 0.1f; y = feetBelow - 0.12f; c = 0.9f;
                } else if (feetBelow > 0f && i >= 29 && i <= 32) {
                    x = (i & 1) == 1 ? -0.1f : 0.1f; y = feetBelow; c = 0.9f;
                }
                if (i > 0) {
                    sb.Append(',');
                }
                sb.Append('[').Append(x.ToString("F4", ci)).Append(',').Append(y.ToString("F4", ci)).Append(",0,")
                  .Append(c.ToString("F2", ci)).Append(']');
            }
            sb.Append("],\"xyz\":[0,0,2000],\"src\":[");
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                if (i > 0) {
                    sb.Append(',');
                }
                sb.Append('0');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [Test]
        public void Stage_TheFigureStaysPutWhenTheFeetLeaveThePicture() {
            TrackedStage stage = new TrackedStage(new SilentLog(), TestPort + 2, 40f);
            stage.Start(true, true, false);
            UdpClient sender = new UdpClient();
            try {
                IPEndPoint target = new IPEndPoint(IPAddress.Loopback, TestPort + 2);
                long seq = 0;
                // Feet 0.6 m below the hips (a floor 0.35 m above the standing origin) for just long enough to be believed, then
                // none at all. The grounding loop snaps to the floor on that first belief, which moves the world; if the floor
                // is then chased without being re-measured it is a stale number in a world that has moved, and never arrives.
                for (int i = 0; i < SkeletonPose.FloorMinStreakFrames + 1; i++) {
                    seq = seq + 1;
                    byte[] payload = Encoding.UTF8.GetBytes(StandingJson(seq, 0.6f));
                    sender.Send(payload, payload.Length, target);
                    Thread.Sleep(33);
                    stage.Tick(Dt);
                }
                float settledAt = 0f;
                for (int i = 0; i < 90; i++) {
                    seq = seq + 1;
                    byte[] payload = Encoding.UTF8.GetBytes(StandingJson(seq, 0f));
                    sender.Send(payload, payload.Length, target);
                    Thread.Sleep(33);
                    stage.Tick(Dt);
                    if (i == 30) {
                        settledAt = stage.StagingOrigin().y;
                    }
                }
                float end = stage.StagingOrigin().y;
                Assert.IsTrue(stage.Pose.HasFloor, "the test is about a floor that exists and then is not re-measured");
                Assert.IsFalse(stage.Pose.FloorFresh);
                Assert.AreEqual(settledAt, end, 0.02f, "with no foot to refresh the floor the figure holds still (it used to drift away)");
                Assert.LessOrEqual(Mathf.Abs(end - 0.95f), 0.75f + 1e-3f, "and never leaves the grounding band");
            } finally {
                sender.Close();
                stage.Stop();
            }
        }

        private sealed class SilentLog : ILogService {
            public void Log(LogLevel level, string message) {
            }

            public void LogException(Exception exception, string context) {
            }

            public void Flush() {
            }
        }
    }
}
