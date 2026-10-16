using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="VisionSenderArguments"/>: the settings-to-flags mapping for the hands, face and cutout senders. The expected lines are fixtures
    /// (Tests/EditMode/VisionFixtures/args_*.txt) that python-sidecar~/tests/test_vision_cli_contract.py parses with the senders' own argparse
    /// parsers, so a flag the C# side emits that the Python side does not accept (or reads differently) fails in one of the two suites.
    /// </summary>
    public sealed class VisionSenderArgumentsTests {
        private static string Expected(string fixture) {
            return VisionFixtures.LoadText(fixture).Trim();
        }

        private static void ApplyCustomCommon(VisionSettings settings) {
            settings.Flip = false;
            settings.RotateDegrees = 90;
            settings.HorizontalFovDegrees = 75f;
            settings.FovAcrossLongSide = true;
            settings.CameraSize = new Vector2Int(640, 480);
            settings.CameraFps = 15;
            settings.LoopFile = false;
            settings.Provider = VisionProvider.Cpu;
            settings.Smoothing = false;
            settings.ShowPreviewWindow = true;
        }

        private const string CustomSource = "http://192.168.1.5:3000/viewer?room=room-abc";

        [Test]
        public void Hands_DefaultSettingsGiveTheDefaultCommandLine() {
            Assert.AreEqual(Expected("args_hands_default.txt"), VisionSenderArguments.Hands(new VisionSettings(), "camera:0"));
        }

        [Test]
        public void Hands_EverySettingReachesItsFlag() {
            VisionSettings settings = new VisionSettings();
            ApplyCustomCommon(settings);
            settings.HandsPort = 9001;
            settings.MaxHands = 3;
            settings.PalmConfidence = 0.3f;
            settings.HandConfidence = 0.7f;
            settings.HandKeepConfidence = 0.35f;
            settings.HandScale = 1.1f;
            settings.HandSides = HandSidesMode.Swapped;
            Assert.AreEqual(Expected("args_hands_custom.txt"), VisionSenderArguments.Hands(settings, CustomSource));
        }

        [Test]
        public void Face_DefaultSettingsGiveTheDefaultCommandLine() {
            Assert.AreEqual(Expected("args_face_default.txt"), VisionSenderArguments.Face(new VisionSettings(), "camera:0"));
        }

        [Test]
        public void Face_EverySettingReachesItsFlag() {
            VisionSettings settings = new VisionSettings();
            ApplyCustomCommon(settings);
            settings.FacePort = 9002;
            settings.MaxFaces = 2;
            settings.FaceDetectorConfidence = 0.3f;
            settings.FaceConfidence = 0.7f;
            settings.FaceKeepConfidence = 0.35f;
            settings.FaceScale = 1.1f;
            settings.Blendshapes = false;
            settings.FaceLandmarks = FaceLandmarkMode.Mesh;
            Assert.AreEqual(Expected("args_face_custom.txt"), VisionSenderArguments.Face(settings, CustomSource));
        }

        private static VisionSettings CustomEverything() {
            VisionSettings settings = new VisionSettings();
            ApplyCustomCommon(settings);
            settings.HandsPort = 9001;
            settings.MaxHands = 3;
            settings.PalmConfidence = 0.3f;
            settings.HandConfidence = 0.7f;
            settings.HandKeepConfidence = 0.35f;
            settings.HandScale = 1.1f;
            settings.HandSides = HandSidesMode.Swapped;
            settings.FacePort = 9002;
            settings.MaxFaces = 2;
            settings.FaceDetectorConfidence = 0.3f;
            settings.FaceConfidence = 0.7f;
            settings.FaceKeepConfidence = 0.35f;
            settings.FaceScale = 1.1f;
            settings.Blendshapes = false;
            settings.FaceLandmarks = FaceLandmarkMode.Mesh;
            settings.CutoutPort = 9003;
            settings.CutoutRoi = CutoutRoiMode.On;
            settings.CutoutDetectorConfidence = 0.3f;
            settings.CutoutMaxPeople = 2;
            settings.CutoutMaskLongSide = 480;
            settings.CutoutEdgeLow = 0.4f;
            settings.CutoutEdgeHigh = 0.8f;
            settings.CutoutGuided = false;
            settings.CutoutCleanup = false;
            settings.CutoutTemporal = false;
            settings.CutoutHighQuality = true;
            settings.CutoutWholePictureFallback = false;
            settings.CutoutIsolatePeople = false;
            settings.CutoutColor = CutoutColorMode.Jpeg;
            settings.CutoutJpegQuality = 70;
            settings.CutoutMaskEncoding = CutoutMaskMode.Deflate;
            settings.CutoutSendLongSide = 960;
            settings.CutoutChunkBytes = 1200;
            settings.CutoutFlipInSender = true;
            return settings;
        }

        private static string Segment(string line, string stream) {
            string[] parts = line.Split(new string[] { " --stream " }, System.StringSplitOptions.None);
            for (int i = 1; i < parts.Length; i++) {
                if (parts[i].StartsWith(stream + " ")) {
                    return parts[i];
                }
            }
            return string.Empty;
        }

        [Test]
        public void Run_DefaultSettingsGiveTheCombinedCommandLine() {
            Assert.AreEqual(Expected("args_run_default.txt"), VisionSenderArguments.Run(new VisionSettings(), "camera:0", true, true, true));
        }

        [Test]
        public void Run_EverySettingReachesItsFlag() {
            Assert.AreEqual(Expected("args_run_custom.txt"), VisionSenderArguments.Run(CustomEverything(), CustomSource, true, true, true));
        }

        [Test]
        public void Run_OnlyTheWantedStreamsAppearAndTheSourceIsNamedOnce() {
            string line = VisionSenderArguments.Run(new VisionSettings(), "camera:0", true, false, true);
            StringAssert.Contains(" --stream hands ", line);
            StringAssert.Contains(" --stream cutout ", line);
            StringAssert.DoesNotContain("--stream face", line);
            Assert.AreEqual(1, line.Split(new string[] { "--video" }, System.StringSplitOptions.None).Length - 1, "the camera is named once: it is read once");
            Assert.IsTrue(line.StartsWith("--video \"camera:0\" "), "the source options come before the first --stream");
        }

        [Test]
        public void Run_AStreamGetsExactlyTheOptionsItsSingleLineHas() {
            VisionSettings settings = CustomEverything();
            string run = VisionSenderArguments.Run(settings, CustomSource, true, true, true);
            string hands = VisionSenderArguments.Hands(settings, CustomSource);
            string face = VisionSenderArguments.Face(settings, CustomSource);
            string cutout = VisionSenderArguments.Cutout(settings, CustomSource);
            StringAssert.Contains(hands.Substring(hands.IndexOf(" --max-hands ")), Segment(run, "hands"));
            StringAssert.Contains(face.Substring(face.IndexOf(" --max-faces ")), Segment(run, "face"));
            StringAssert.Contains(cutout.Substring(cutout.IndexOf(" --roi ")), Segment(run, "cutout"));
        }

        [Test]
        public void Run_TheMirrorIsPerStreamBecauseHandsAndFaceMirrorAndTheCutoutDoesNot() {
            VisionSettings settings = new VisionSettings();
            settings.Flip = true;
            settings.CutoutFlipInSender = false;
            string run = VisionSenderArguments.Run(settings, "camera:0", true, true, true);
            StringAssert.Contains(" --flip ", Segment(run, "hands"));
            StringAssert.Contains(" --flip ", Segment(run, "face"));
            StringAssert.Contains(" --no-flip ", Segment(run, "cutout"));
            StringAssert.DoesNotContain("--flip", run.Substring(0, run.IndexOf(" --stream ")), "no stream option before the first --stream");
        }

        [Test]
        public void Run_NoFieldOfViewReachesTheCutoutAndNoPortIsSharedBetweenStreams() {
            string run = VisionSenderArguments.Run(new VisionSettings(), "camera:0", true, true, true);
            StringAssert.DoesNotContain("--hfov", Segment(run, "cutout"));
            StringAssert.Contains("--port 8901", Segment(run, "hands"));
            StringAssert.Contains("--port 8902", Segment(run, "face"));
            StringAssert.Contains("--port 8903", Segment(run, "cutout"));
        }

        [Test]
        public void Cutout_DefaultSettingsGiveTheDefaultCommandLine() {
            Assert.AreEqual(Expected("args_cutout_default.txt"), VisionSenderArguments.Cutout(new VisionSettings(), "camera:0"));
        }

        [Test]
        public void Cutout_EverySettingReachesItsFlag() {
            VisionSettings settings = new VisionSettings();
            ApplyCustomCommon(settings);
            // The mirror and the temporal filter are the cutout's OWN switches: the shared Flip is off here and the line still has --flip,
            // because CutoutFlipInSender is on. The next test pins the other direction.
            settings.CutoutPort = 9003;
            settings.CutoutRoi = CutoutRoiMode.On;
            settings.CutoutDetectorConfidence = 0.3f;
            settings.CutoutMaxPeople = 2;
            settings.CutoutMaskLongSide = 480;
            settings.CutoutEdgeLow = 0.4f;
            settings.CutoutEdgeHigh = 0.8f;
            settings.CutoutGuided = false;
            settings.CutoutCleanup = false;
            settings.CutoutTemporal = false;
            settings.CutoutHighQuality = true;
            settings.CutoutWholePictureFallback = false;
            settings.CutoutIsolatePeople = false;
            settings.CutoutColor = CutoutColorMode.Jpeg;
            settings.CutoutJpegQuality = 70;
            settings.CutoutMaskEncoding = CutoutMaskMode.Deflate;
            settings.CutoutSendLongSide = 960;
            settings.CutoutChunkBytes = 1200;
            settings.CutoutFlipInSender = true;
            Assert.AreEqual(Expected("args_cutout_custom.txt"), VisionSenderArguments.Cutout(settings, CustomSource));
        }

        [Test]
        public void Cutout_TheSharedMirrorAndSmoothingSwitchesDoNotReachIt() {
            VisionSettings settings = new VisionSettings();
            settings.Flip = true;
            settings.Smoothing = false;
            string line = VisionSenderArguments.Cutout(settings, "camera:0");
            StringAssert.Contains(" --no-flip ", line, "hands and face mirror in the sender; the cutout mirrors on the GPU");
            StringAssert.Contains(" --smooth ", line, "the landmark filter is not the mask's temporal filter");
            StringAssert.DoesNotContain("--hfov", line, "nothing in a cutout is measured in metres");
        }

        [Test]
        public void Cutout_ValuesOutsideWhatTheSenderAcceptsAreClampedNotForwarded() {
            VisionSettings settings = new VisionSettings();
            settings.CutoutMaxPeople = 99;
            settings.CutoutDetectorConfidence = 5f;
            settings.CutoutMaskLongSide = 50;
            settings.CutoutSendLongSide = 99999;
            settings.CutoutChunkBytes = 10;
            settings.CutoutEdgeLow = 0.9f;
            settings.CutoutEdgeHigh = 0.2f;
            settings.CutoutColor = CutoutColorMode.Jpeg;
            settings.CutoutJpegQuality = 500;
            string line = VisionSenderArguments.Cutout(settings, "camera:0");
            StringAssert.Contains("--max-people 8 ", line);
            StringAssert.Contains("--det-conf 0.9 ", line);
            StringAssert.Contains("--mask-long-side 160 ", line, "a mask of fewer than 160 px cannot outline anything");
            StringAssert.Contains("--send-long-side 3840 ", line);
            StringAssert.Contains("--chunk-bytes 256", line);
            StringAssert.Contains("--edge-low 0.9 ", line);
            StringAssert.Contains("--edge-high 0.95 ", line, "the ramp's top stays above its bottom whatever the sliders say");
            StringAssert.Contains("--jpeg-quality 100 ", line);
        }

        [Test]
        public void Cutout_ZeroMeansThePicturesOwnSizeAndJpegQualityIsSentOnlyWithJpeg() {
            VisionSettings settings = new VisionSettings();
            settings.CutoutMaskLongSide = 0;
            settings.CutoutSendLongSide = 0;
            string line = VisionSenderArguments.Cutout(settings, "camera:0");
            StringAssert.Contains("--mask-long-side 0 ", line);
            StringAssert.Contains("--send-long-side 0 ", line);
            StringAssert.DoesNotContain("--jpeg-quality", line, "rgba carries no quality");
            settings.CutoutColor = CutoutColorMode.None;
            StringAssert.Contains("--color none ", VisionSenderArguments.Cutout(settings, "camera:0"));
        }

        [Test]
        public void Cutout_NamesMatchTheChoicesTheSenderAccepts() {
            Assert.AreEqual("auto", VisionSenderArguments.RoiName(CutoutRoiMode.Auto));
            Assert.AreEqual("on", VisionSenderArguments.RoiName(CutoutRoiMode.On));
            Assert.AreEqual("off", VisionSenderArguments.RoiName(CutoutRoiMode.Off));
            Assert.AreEqual("rgba", VisionSenderArguments.ColorName(CutoutColorMode.Rgba));
            Assert.AreEqual("jpeg", VisionSenderArguments.ColorName(CutoutColorMode.Jpeg));
            Assert.AreEqual("none", VisionSenderArguments.ColorName(CutoutColorMode.None));
            Assert.AreEqual("raw", VisionSenderArguments.MaskEncodingName(CutoutMaskMode.Raw));
            Assert.AreEqual("deflate", VisionSenderArguments.MaskEncodingName(CutoutMaskMode.Deflate));
            Assert.AreEqual("tools/cutout/cutout_udp_sender.py", VisionSenderArguments.CutoutScript);
        }

        [Test]
        public void TheCutoutWireAndItsSettingsAgreeOnThePort() {
            Assert.AreEqual(CutoutWire.DefaultPort, new VisionSettings().CutoutPort);
            Assert.AreEqual(CutoutWire.DefaultPort, CutoutReceiver.DefaultPort);
            Assert.AreEqual(8903, CutoutWire.DefaultPort);
        }

        [Test]
        public void TheCutoutSettingsSurviveACloneAndACopy() {
            VisionSettings settings = new VisionSettings();
            settings.CutoutMode = VisionSenderMode.Always;
            settings.CutoutColor = CutoutColorMode.Jpeg;
            settings.CutoutMaxPeople = 3;
            settings.CutoutEdgeHigh = 0.7f;
            settings.CutoutFlipInSender = true;
            VisionSettings clone = settings.Clone();
            Assert.AreEqual(VisionSenderMode.Always, clone.CutoutMode);
            Assert.AreEqual(CutoutColorMode.Jpeg, clone.CutoutColor);
            Assert.AreEqual(3, clone.CutoutMaxPeople);
            Assert.AreEqual(0.7f, clone.CutoutEdgeHigh, 1e-6f);
            Assert.IsTrue(clone.CutoutFlipInSender);
            VisionSettings target = new VisionSettings();
            target.CopyFrom(settings);
            Assert.AreEqual(VisionSenderArguments.Cutout(settings, "camera:0"), VisionSenderArguments.Cutout(target, "camera:0"));
        }

        [Test]
        public void ValuesOutsideWhatTheSendersAcceptAreClampedNotForwarded() {
            VisionSettings settings = new VisionSettings();
            settings.RotateDegrees = 45;
            settings.HorizontalFovDegrees = 400f;
            settings.CameraFps = 500;
            settings.CameraSize = new Vector2Int(10, 10);
            settings.MaxHands = 99;
            settings.PalmConfidence = -1f;
            settings.HandScale = 9f;
            string line = VisionSenderArguments.Hands(settings, "camera:0");
            StringAssert.DoesNotContain("--rotate", line, "an unsupported rotation is read as none");
            StringAssert.Contains("--hfov 140 ", line);
            StringAssert.Contains("--camera-fps 60 ", line);
            StringAssert.DoesNotContain("--camera-size", line, "a size under 160x120 is left to the sender's default");
            StringAssert.Contains("--max-hands 4 ", line);
            StringAssert.Contains("--palm-conf 0.05 ", line);
            StringAssert.Contains("--hand-scale 1.6 ", line);
        }

        [Test]
        public void ASourceNeverBreaksOutOfItsQuotes() {
            string line = VisionSenderArguments.Face(new VisionSettings(), "a\" --port 1 \"b");
            StringAssert.StartsWith("--video \"a --port 1 b\" ", line);
        }

        [Test]
        public void Rotation_OnlyTheFourQuarterTurnsSurvive() {
            Assert.AreEqual(0, VisionSenderArguments.NormalizedRotation(0));
            Assert.AreEqual(90, VisionSenderArguments.NormalizedRotation(90));
            Assert.AreEqual(180, VisionSenderArguments.NormalizedRotation(180));
            Assert.AreEqual(270, VisionSenderArguments.NormalizedRotation(270));
            Assert.AreEqual(0, VisionSenderArguments.NormalizedRotation(-90));
            Assert.AreEqual(0, VisionSenderArguments.NormalizedRotation(360));
        }

        [Test]
        public void Names_MatchTheChoicesTheSendersAccept() {
            Assert.AreEqual("auto", VisionSenderArguments.ProviderName(VisionProvider.Auto));
            Assert.AreEqual("dml", VisionSenderArguments.ProviderName(VisionProvider.Dml));
            Assert.AreEqual("cpu", VisionSenderArguments.ProviderName(VisionProvider.Cpu));
            Assert.AreEqual("auto", VisionSenderArguments.SidesName(HandSidesMode.Auto));
            Assert.AreEqual("model", VisionSenderArguments.SidesName(HandSidesMode.Model));
            Assert.AreEqual("swapped", VisionSenderArguments.SidesName(HandSidesMode.Swapped));
            Assert.AreEqual("full", VisionSenderArguments.LandmarksName(FaceLandmarkMode.Full));
            Assert.AreEqual("mesh", VisionSenderArguments.LandmarksName(FaceLandmarkMode.Mesh));
            Assert.AreEqual("none", VisionSenderArguments.LandmarksName(FaceLandmarkMode.None));
        }
    }
}
