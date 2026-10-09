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
