using System.Globalization;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Body;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="BodyEngineArguments"/>: the settings-to-flags mapping of the engine's body command. The two whole lines are fixtures
    /// (BodyFixtures/args_body_*.txt) that python-sidecar~/tests/test_body_cli_contract.py parses with the body sender's own argparse parser, so a flag
    /// emitted here that the sender does not accept (or reads differently) fails in one of the two suites.
    /// </summary>
    public sealed class BodyEngineArgumentsTests {
        private const string LanSource = "http://192.168.1.5:3000/viewer?room=room-abc";

        private static string Expected(string fixture) {
            return BodyFixtures.LoadText(fixture).Trim();
        }

        // Lets a test look for a whole flag (and its value) without matching the start of a longer one.
        private static string Padded(string line) {
            return " " + line + " ";
        }

        [Test]
        public void TheDefaultSettingsGiveTheDefaultCommandLine() {
            Assert.AreEqual(Expected("args_body_default.txt"), BodyEngineArguments.Build(new BodySettings(), "camera:0"));
        }

        [Test]
        public void EverySettingReachesItsFlag() {
            BodySettings settings = new BodySettings();
            settings.Port = 9001;
            settings.LoopFile = false;
            settings.HorizontalFovDegrees = 75f;
            settings.SubjectHeightMetres = 1.8f;
            settings.DepthRangeMetres = new Vector2(2f, 5.5f);
            settings.RotateDegrees = 90;
            settings.AutoOrient = false;
            settings.PartialBody = false;
            settings.FlipHorizontal = true;
            settings.CameraSize = new Vector2Int(640, 480);
            settings.AssumeMeasuredDepth = true;
            Assert.AreEqual(Expected("args_body_custom.txt"), BodyEngineArguments.Build(settings, LanSource));
        }

        [Test]
        public void TheLoopIsACountOfZeroForForeverNotASwitch() {
            BodySettings settings = new BodySettings();
            settings.LoopFile = true;
            StringAssert.Contains(" --loop 0 ", Padded(BodyEngineArguments.Build(settings, "clip.mp4")));
            settings.LoopFile = false;
            StringAssert.Contains(" --loop 1 ", Padded(BodyEngineArguments.Build(settings, "clip.mp4")));
            StringAssert.DoesNotContain("--no-loop", BodyEngineArguments.Build(settings, "clip.mp4"));
        }

        [Test]
        public void ARotationThatIsNotAQuarterTurnIsNotForwarded() {
            BodySettings settings = new BodySettings();
            settings.RotateDegrees = 45;
            StringAssert.DoesNotContain("--rotate", BodyEngineArguments.Build(settings, "camera:0"));
            settings.RotateDegrees = 0;
            StringAssert.DoesNotContain("--rotate", BodyEngineArguments.Build(settings, "camera:0"));
            foreach (int degrees in new int[] { 90, 180, 270 }) {
                settings.RotateDegrees = degrees;
                StringAssert.Contains(" --rotate " + degrees + " ", Padded(BodyEngineArguments.Build(settings, "camera:0")));
            }
        }

        [Test]
        public void TheDepthRangeIsSentOnlyWhenClampingIsOnAndTheRangeIsAnInterval() {
            BodySettings settings = new BodySettings();
            settings.ClampDepth = false;
            StringAssert.DoesNotContain("--depth-range", BodyEngineArguments.Build(settings, "camera:0"));
            settings.ClampDepth = true;
            settings.DepthRangeMetres = new Vector2(4f, 1.6f);
            StringAssert.DoesNotContain("--depth-range", BodyEngineArguments.Build(settings, "camera:0"));
            settings.DepthRangeMetres = new Vector2(3f, 3f);
            StringAssert.DoesNotContain("--depth-range", BodyEngineArguments.Build(settings, "camera:0"));
            settings.DepthRangeMetres = new Vector2(0f, 4f);
            StringAssert.DoesNotContain("--depth-range", BodyEngineArguments.Build(settings, "camera:0"));
            settings.DepthRangeMetres = new Vector2(1.6f, 4f);
            StringAssert.Contains(" --depth-range 1.6 4 ", Padded(BodyEngineArguments.Build(settings, "camera:0")));
        }

        [Test]
        public void TheFieldOfViewAndTheSubjectHeightAreClampedToWhatTheSenderAccepts() {
            BodySettings settings = new BodySettings();
            settings.HorizontalFovDegrees = 500f;
            settings.SubjectHeightMetres = 9f;
            string line = Padded(BodyEngineArguments.Build(settings, "camera:0"));
            StringAssert.Contains(" --hfov 140 ", line);
            StringAssert.Contains(" --subject-height 2.5 ", line);
            settings.HorizontalFovDegrees = 1f;
            settings.SubjectHeightMetres = 0f;
            line = Padded(BodyEngineArguments.Build(settings, "camera:0"));
            StringAssert.Contains(" --hfov 20 ", line);
            StringAssert.Contains(" --subject-height 0.5 ", line);
        }

        [Test]
        public void ASmallCameraSizeIsNotSentAndTheSmallestAcceptedOneIs() {
            BodySettings settings = new BodySettings();
            settings.CameraSize = new Vector2Int(100, 100);
            StringAssert.DoesNotContain("--camera-size", BodyEngineArguments.Build(settings, "camera:0"));
            settings.CameraSize = new Vector2Int(159, 120);
            StringAssert.DoesNotContain("--camera-size", BodyEngineArguments.Build(settings, "camera:0"));
            settings.CameraSize = new Vector2Int(160, 119);
            StringAssert.DoesNotContain("--camera-size", BodyEngineArguments.Build(settings, "camera:0"));
            settings.CameraSize = new Vector2Int(160, 120);
            StringAssert.Contains(" --camera-size 160 120 ", Padded(BodyEngineArguments.Build(settings, "camera:0")));
        }

        [Test]
        public void TheSourceIsQuotedAndACharacterThatCouldEndTheQuotingIsDropped() {
            StringAssert.StartsWith("--video \"C:/my clips/a.mp4\" --host", BodyEngineArguments.Build(new BodySettings(), "C:/my clips/a.mp4"));
            StringAssert.StartsWith("--video \"a --evilb\" --host", BodyEngineArguments.Build(new BodySettings(), "a\" --evil\"b"));
            StringAssert.StartsWith("--video \"\" --host", BodyEngineArguments.Build(new BodySettings(), null));
        }

        [Test]
        public void ThePortAndTheLoopbackHostAreAlwaysSent() {
            BodySettings settings = new BodySettings();
            settings.Port = 12345;
            StringAssert.Contains(" --host 127.0.0.1 --port 12345 ", Padded(BodyEngineArguments.Build(settings, "camera:0")));
        }

        [Test]
        public void TheNumbersDoNotDependOnTheCulture() {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                BodySettings settings = new BodySettings();
                settings.SubjectHeightMetres = 1.75f;
                StringAssert.Contains(" --subject-height 1.75 ", Padded(BodyEngineArguments.Build(settings, "camera:0")));
            } finally {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        [Test]
        public void TheDefaultsAreTheOnesTheApplicationShipsWith() {
            BodySettings settings = new BodySettings();
            Assert.AreEqual(8899, settings.Port);
            Assert.AreEqual("camera:0", settings.Source);
            Assert.IsTrue(settings.AutoStartEngine);
            Assert.IsTrue(settings.AutoOrient);
            Assert.IsTrue(settings.PartialBody);
            Assert.IsFalse(settings.FlipHorizontal);
            Assert.AreEqual(1.7f, settings.SubjectHeightMetres, 1e-6f);
            Assert.AreEqual(60f, settings.HorizontalFovDegrees, 1e-6f);
            Assert.AreEqual(new Vector2(1.6f, 4f), settings.DepthRangeMetres);
            Assert.AreEqual(40f, settings.PoseInterpolationDelayMs, 1e-6f);
            Assert.AreEqual(0.5f, settings.PoseStaleSeconds, 1e-6f);
            Assert.AreEqual(0.3f, settings.LimbConfidenceThreshold, 1e-6f);
            Assert.IsTrue(settings.TrackLegs);
            Assert.IsTrue(settings.TrackHands);
            Assert.IsTrue(settings.TrackPosition);
            Assert.IsTrue(settings.UseHumanizedSkeleton);
            Assert.IsFalse(settings.Mirror);
        }
    }
}
