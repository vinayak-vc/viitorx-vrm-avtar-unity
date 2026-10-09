using System.IO;
using System.Reflection;

using NUnit.Framework;

using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-49 tests: the "Video Test Source" Inspector value, and the supervisor command line built from it.
    ///
    /// The value comes from a text box and ends up on a command line (and, for a page link, in a
    /// downloader), so the interesting cases are the ones that must be REFUSED. The Python end validates
    /// again on its own (python-sidecar~/tests/test_video_source.py); these pin that Unity says so first,
    /// in the console, instead of the user finding a traceback in a log.
    /// </summary>
    public sealed class VideoSourceF49Tests {
        private string root;
        private string clip;

        [SetUp]
        public void MakeProjectFolder() {
            root = Path.Combine(Path.GetTempPath(), "f49_unity_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Assets", "Games", "video"));
            clip = Path.Combine(root, "Assets", "Games", "video", "clip.mp4");
            File.WriteAllBytes(clip, new byte[] { 1, 2, 3 });
        }

        [TearDown]
        public void RemoveProjectFolder() {
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
        }

        private string Resolve(string input, out string error) {
            string resolved;
            bool ok = VideoSourceSpec.TryResolve(input, root, out resolved, out error);
            return ok ? resolved : null;
        }

        [Test]
        public void ARelativeProjectPathResolvesTheWayTheProjectWindowShowsIt() {
            string error;
            string resolved = Resolve("Assets/Games/video/clip.mp4", out error);
            Assert.IsNotNull(resolved, error);
            Assert.AreEqual(Path.GetFullPath(clip).Replace('\\', '/'), resolved);
        }

        [Test]
        public void AnAbsolutePathAndAQuotedPathBothWork() {
            string error;
            Assert.IsNotNull(Resolve(clip, out error), error);
            // "Copy as path" in Explorer wraps the value in quotes.
            Assert.IsNotNull(Resolve("\"" + clip + "\"", out error), error);
        }

        [Test]
        public void ResolvedPathsUseForwardSlashes() {
            string error;
            Assert.IsFalse(Resolve(clip, out error).Contains("\\"));
        }

        [Test]
        public void HttpAndHttpsLinksAreAcceptedUnchanged() {
            string error;
            Assert.AreEqual("https://example.com/a/clip.mp4", Resolve("https://example.com/a/clip.mp4", out error));
            Assert.AreEqual("http://example.com/watch?v=abc", Resolve("http://example.com/watch?v=abc", out error));
        }

        [Test]
        public void EveryOtherSchemeIsRefusedWithAReason() {
            string[] refused = {
                "file:///C:/Windows/win.ini", "ftp://example.com/clip.mp4", "javascript://x",
                "data://text/plain;base64,AAAA", "ssh://host/clip.mp4",
            };
            int i = 0;
            while (i < refused.Length) {
                string error;
                Assert.IsNull(Resolve(refused[i], out error), refused[i]);
                StringAssert.Contains("http", error, refused[i]);
                i = i + 1;
            }
        }

        [Test]
        public void EmptyMissingAndOverlongValuesAreRefused() {
            string error;
            Assert.IsNull(Resolve("", out error));
            StringAssert.Contains("no video source", error);
            Assert.IsNull(Resolve("   ", out error));
            Assert.IsNull(Resolve(null, out error));
            Assert.IsNull(Resolve("Assets/Games/video/missing.mp4", out error));
            StringAssert.Contains("not found", error);
            Assert.IsNull(Resolve("https://example.com/" + new string('a', VideoSourceSpec.MaxLength), out error));
            StringAssert.Contains("longer", error);
        }

        [Test]
        public void AQuoteOrControlCharacterCannotReachTheCommandLine() {
            // A quote inside the value would end the quoted argument early and let the rest read as
            // further arguments.
            string error;
            Assert.IsNull(Resolve("https://example.com/a.mp4\" --show", out error));
            StringAssert.Contains("quote", error);
            Assert.IsNull(Resolve("https://example.com/a.mp4\nrm", out error));
            Assert.IsNull(Resolve("https://example.com/a.mp4\0", out error));
        }

        [Test]
        public void AWindowsDriveLetterIsAPathNotAScheme() {
            // "C:/x.mp4" has a one-letter prefix; reading it as a URL scheme would call a missing file
            // a forbidden link.
            string error;
            Assert.IsNull(Resolve("C:/definitely/not/here.mp4", out error));
            StringAssert.Contains("not found", error);
            Assert.IsFalse(VideoSourceSpec.LooksLikeUrl("C:/x.mp4"));
            Assert.IsTrue(VideoSourceSpec.LooksLikeUrl("https://x"));
            Assert.IsFalse(VideoSourceSpec.LooksLikeUrl("://x"));
        }

        [Test]
        public void TheHudNameIsShortAndReadable() {
            Assert.AreEqual("clip.mp4", VideoSourceSpec.DisplayName("D:/a/b/clip.mp4"));
            Assert.AreEqual("example.com/clip.mp4", VideoSourceSpec.DisplayName("https://example.com/v/clip.mp4"));
            Assert.AreEqual("example.com", VideoSourceSpec.DisplayName("https://example.com/"));
            Assert.AreEqual(string.Empty, VideoSourceSpec.DisplayName(null));
        }

        // ---------------------------------------------------------------- phones, webcams and streams

        [Test]
        public void ACameraIsNamedByIndexAndCanonicalised() {
            string error;
            Assert.AreEqual("camera:0", Resolve("camera:0", out error), error);
            Assert.AreEqual("camera:3", Resolve("CAM: 3", out error), "case and a space are tolerated");
            Assert.AreEqual("camera:2", Resolve("2", out error), "a bare digit is a camera");
            Assert.IsNull(Resolve("camera:99", out error));
            StringAssert.Contains("out of range", error);
        }

        [Test]
        public void ABareDigitThatNamesARealFileIsStillAFile() {
            string error;
            File.WriteAllBytes(Path.Combine(root, "7"), new byte[] { 1 });
            string resolved = Resolve("7", out error);
            Assert.IsNotNull(resolved, error);
            Assert.AreNotEqual("camera:7", resolved);
        }

        [Test]
        public void StreamLinksAreAcceptedOnTheirOwnSchemesOnly() {
            string error;
            Assert.AreEqual("rtsp://192.168.1.20:554/live", Resolve("rtsp://192.168.1.20:554/live", out error), error);
            Assert.AreEqual("http://192.168.1.5:8080/video", Resolve("http://192.168.1.5:8080/video", out error), error);
            Assert.AreEqual("stream:https://cam.example.com/live", Resolve("stream:https://cam.example.com/live", out error), error);
            Assert.IsNull(Resolve("stream:file:///C:/x", out error), "stream: cannot smuggle another scheme");
            Assert.IsNull(Resolve("stream:ftp://x/y", out error));
            Assert.IsNull(Resolve("rtmp://host/live", out error));
            StringAssert.Contains("rtsp", error);
        }

        [Test]
        public void AClassifiedSourceKnowsWhetherItIsLive() {
            Assert.AreEqual(VideoSourceKind.Camera, VideoSourceSpec.KindOf("camera:1"));
            Assert.AreEqual(VideoSourceKind.Stream, VideoSourceSpec.KindOf("rtsp://10.0.0.5/ch1"));
            Assert.AreEqual(VideoSourceKind.Stream, VideoSourceSpec.KindOf("stream:https://cam.example.com/live"));
            Assert.AreEqual(VideoSourceKind.Stream, VideoSourceSpec.KindOf("http://192.168.1.5:8080/video"),
                            "an http link to a LAN address is a phone, not a web page");
            Assert.AreEqual(VideoSourceKind.Link, VideoSourceSpec.KindOf("http://8.8.8.8/video"),
                            "a PUBLIC address is still a link");
            Assert.AreEqual(VideoSourceKind.Link, VideoSourceSpec.KindOf("http://192.168.1.5/clips/a.mp4"),
                            "a LAN link that names a video FILE is downloaded, not streamed");
            Assert.AreEqual(VideoSourceKind.File, VideoSourceSpec.KindOf("D:/a/clip.mp4"));
            Assert.IsTrue(VideoSourceSpec.IsLive("camera:0"));
            Assert.IsFalse(VideoSourceSpec.IsLive("D:/a/clip.mp4"));
        }

        [Test]
        public void LanAddressesAreRecognised() {
            Assert.IsTrue(VideoSourceSpec.IsLanHost("192.168.0.9"));
            Assert.IsTrue(VideoSourceSpec.IsLanHost("10.1.2.3"));
            Assert.IsTrue(VideoSourceSpec.IsLanHost("172.16.0.1"));
            Assert.IsTrue(VideoSourceSpec.IsLanHost("172.31.255.1"));
            Assert.IsTrue(VideoSourceSpec.IsLanHost("127.0.0.1"));
            Assert.IsTrue(VideoSourceSpec.IsLanHost("localhost"));
            Assert.IsTrue(VideoSourceSpec.IsLanHost("myphone.local"));
            Assert.IsFalse(VideoSourceSpec.IsLanHost("172.32.0.1"), "172.32 is public");
            Assert.IsFalse(VideoSourceSpec.IsLanHost("8.8.8.8"));
            Assert.IsFalse(VideoSourceSpec.IsLanHost("example.com"));
            Assert.IsFalse(VideoSourceSpec.IsLanHost(string.Empty));
        }

        [Test]
        public void CredentialsInALinkAreNeverShown() {
            Assert.AreEqual("rtsp://***@10.0.0.5/ch1", VideoSourceSpec.Redact("rtsp://admin:s3cret@10.0.0.5/ch1"));
            Assert.AreEqual("http://example.com/a?b=c@d", VideoSourceSpec.Redact("http://example.com/a?b=c@d"),
                            "an @ in a query string is not a login");
            Assert.AreEqual(string.Empty, VideoSourceSpec.Redact(null));
            string name = VideoSourceSpec.DisplayName("rtsp://admin:s3cret@10.0.0.5:8554/ch1");
            StringAssert.DoesNotContain("s3cret", name);
            StringAssert.DoesNotContain("admin", name);
            Assert.AreEqual("stream 10.0.0.5:8554", name);
            string error;
            Assert.IsNull(Resolve("stream:ftp://u:s3cret@x/y", out error));
            StringAssert.DoesNotContain("s3cret", error);
        }

        [Test]
        public void TheHudNamesLiveSourcesPlainly() {
            Assert.AreEqual("camera 2", VideoSourceSpec.DisplayName("camera:2"));
            Assert.AreEqual("stream 192.168.1.5:8080", VideoSourceSpec.DisplayName("http://192.168.1.5:8080/video"));
        }

        // ---------------------------------------------------------------- the command line

        private static string BuildArguments(SidecarLaunchOptions options) {
            SidecarLaunchOptions o = options;
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(null, o);
            SidecarPathSet paths = SidecarPaths.Resolve("D:/sidecar", string.Empty);
            MethodInfo build = typeof(SidecarProcessLauncher).GetMethod(
                "BuildArguments", BindingFlags.Instance | BindingFlags.NonPublic);
            return (string)build.Invoke(launcher, new object[] { paths });
        }

        [Test]
        public void ACameraSessionIsLaunchedWithNoVideoFlagsAtAll() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            string args = BuildArguments(options);
            StringAssert.DoesNotContain("--video", args, "the camera command line must be byte-identical to before");
        }

        [Test]
        public void AVideoSessionCarriesTheClipAndItsSettings() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.SupervisedSenderScript = "tools/video/video_udp_sender.py";
            options.VideoSource = "https://example.com/a/clip.mp4";
            options.VideoLoopCount = 0;
            options.VideoSubjectHeightMetres = 1.8f;
            options.VideoHorizontalFovDegrees = 70f;
            options.VideoDepthMinMetres = 1.6f;
            options.VideoDepthMaxMetres = 4f;
            options.VideoAssumeMeasuredDepth = true;
            string args = BuildArguments(options);

            StringAssert.Contains("--script \"D:/sidecar/tools/video/video_udp_sender.py\"", args);
            StringAssert.Contains("--video \"https://example.com/a/clip.mp4\"", args, "the clip is one quoted argument");
            StringAssert.Contains("--video-loop 0", args);
            StringAssert.Contains("--video-subject-height 1.80", args);
            StringAssert.Contains("--video-hfov 70.0", args);
            StringAssert.Contains("--video-depth-range 1.60 4.00", args);
            StringAssert.Contains("--video-assume-measured", args);
        }

        [Test]
        public void TheDepthClampIsOmittedWhenOffOrBackwards() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoSource = "D:/clip.mp4";
            StringAssert.DoesNotContain("--video-depth-range", BuildArguments(options), "0/0 means no clamp");
            options.VideoDepthMinMetres = 4f;
            options.VideoDepthMaxMetres = 1.6f;
            StringAssert.DoesNotContain("--video-depth-range", BuildArguments(options),
                                        "a backwards band is not sent; the sender would clamp every pose to one number");
            options.VideoDepthMinMetres = 1.6f;
            options.VideoDepthMaxMetres = 4f;
            options.VideoAssumeMeasuredDepth = false;
            StringAssert.DoesNotContain("--video-assume-measured", BuildArguments(options));
        }

        [Test]
        public void ARotatedMirroredWebcamCarriesItsOrientationAndSize() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoSource = "camera:1";
            options.VideoRotateDegrees = 90;
            options.VideoFlipHorizontal = true;
            options.VideoCameraWidth = 1920;
            options.VideoCameraHeight = 1080;
            string args = BuildArguments(options);
            StringAssert.Contains("--video \"camera:1\"", args);
            StringAssert.Contains("--video-rotate 90", args);
            StringAssert.Contains("--video-flip", args);
            StringAssert.Contains("--video-camera-size 1920 1080", args);
        }

        [Test]
        public void NoRotationNoMirrorAndNoSizeSendsNoneOfThem() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoSource = "camera:0";
            string args = BuildArguments(options);
            StringAssert.DoesNotContain("--video-rotate", args);
            StringAssert.DoesNotContain("--video-flip", args);
            StringAssert.DoesNotContain("--video-camera-size", args);
        }

        [Test]
        public void AnInvalidRotationOrSizeIsDroppedNotForwarded() {
            // The sender's argparse accepts 0/90/180/270 only; anything else would make Python exit
            // before the camera opens.
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.VideoSource = "camera:0";
            options.VideoRotateDegrees = 45;
            options.VideoCameraWidth = 5;
            options.VideoCameraHeight = 5;
            string args = BuildArguments(options);
            StringAssert.DoesNotContain("--video-rotate", args);
            StringAssert.DoesNotContain("--video-camera-size", args);
        }

        [Test]
        public void TheNumbersAreWrittenWithADotWhateverTheMachineCulture() {
            System.Globalization.CultureInfo saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                SidecarLaunchOptions options = new SidecarLaunchOptions();
                options.VideoSource = "D:/clip.mp4";
                options.VideoSubjectHeightMetres = 1.75f;
                StringAssert.Contains("--video-subject-height 1.75", BuildArguments(options),
                                      "a comma here would be read by Python as two arguments");
            } finally {
                System.Threading.Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }
}
