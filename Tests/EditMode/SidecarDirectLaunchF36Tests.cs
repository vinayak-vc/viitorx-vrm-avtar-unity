using NUnit.Framework;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-36 unit tests: the direct-sender mode added to <see cref="SidecarProcessLauncher"/>.
    ///
    /// THE POINT OF THESE. F-36 added a second way to start the sidecar — running a sender directly
    /// instead of through the supervisor — to a class that the PRODUCT depends on. The most valuable
    /// thing to pin is therefore not the new path but the old one: that a launcher configured the way
    /// `AppBootstrap` configures it still goes through the supervisor, unchanged. A regression there
    /// would take the avatar application's watchdog out silently.
    ///
    /// SCOPE. Only the configuration and the path arithmetic are covered. Spawning a process, the
    /// kill-on-close job object and the UDP producer probe all need a real machine and are not
    /// testable here; the play-mode checks are in the F-36 section of docs/tasks.md.
    /// </summary>
    public sealed class SidecarDirectLaunchF36Tests {
        [Test]
        public void TheSupervisedPathIsStillTheDefault() {
            // The one that matters. AppBootstrap sets no DirectScript, so it must keep getting the
            // supervisor with its watchdog, crash-loop detection and environment validation.
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            Assert.IsEmpty(options.DirectScript,
                           "the product path must not have become unsupervised by default");
            Assert.IsEmpty(options.DirectArguments);
        }

        [Test]
        public void DefaultsStillDescribeTheProductionConfiguration() {
            // These are the values AppBootstrap relies on. Pinned so a change to them is deliberate.
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            Assert.IsTrue(options.AutoStart);
            Assert.AreEqual("127.0.0.1", options.Host);
            Assert.AreEqual(8899, options.UdpPort);
            Assert.AreEqual(8897, options.LockPort);
            Assert.IsTrue(options.Portrait);
            Assert.AreEqual("ccw", options.PortraitDirection);
            Assert.AreEqual(3, options.SubpixelBits);
        }

        [Test]
        public void ARelativeSenderResolvesAgainstTheSidecarRoot() {
            SidecarProcessLauncher launcher = Launcher("multiperson_udp_sender.py");
            Assert.AreEqual("C:/sidecar/multiperson_udp_sender.py",
                            launcher.ResolveDirectScript(Paths("C:/sidecar")));
        }

        [Test]
        public void ATrailingSlashOnTheRootDoesNotDoubleUp() {
            SidecarProcessLauncher launcher = Launcher("multiperson_udp_sender.py");
            Assert.AreEqual("C:/sidecar/multiperson_udp_sender.py",
                            launcher.ResolveDirectScript(Paths("C:/sidecar/")));
        }

        [Test]
        public void AnAbsoluteSenderIsTakenAsGivenAndNormalised() {
            // SidecarPaths normalises to forward slashes so logs and arguments read consistently; an
            // absolute override typed by hand will not have been.
            SidecarProcessLauncher launcher = Launcher("D:" + "\\" + "tools" + "\\" + "sender.py");
            Assert.AreEqual("D:/tools/sender.py", launcher.ResolveDirectScript(Paths("C:/sidecar")));
        }

        [Test]
        public void NoSenderMeansNoDirectScript() {
            SidecarProcessLauncher launcher = Launcher(string.Empty);
            Assert.IsEmpty(launcher.ResolveDirectScript(Paths("C:/sidecar")));
        }

        // A launcher with no log: every log call in the class is null-conditional, which is what makes
        // this constructible outside a player.
        private static SidecarProcessLauncher Launcher(string directScript) {
            SidecarLaunchOptions options = new SidecarLaunchOptions {
                DirectScript = directScript
            };
            return new SidecarProcessLauncher(null, options);
        }

        private static SidecarPathSet Paths(string root) {
            return new SidecarPathSet { Root = root };
        }
    }
}
