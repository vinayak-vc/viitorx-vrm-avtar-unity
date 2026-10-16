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

        // ------------------------------------------------- the SUPERVISED sender override

        [Test]
        public void TheProductPathIsSupervisedAndDefaultsToThePackagedSender() {
            // The regression that started all this: the multi-person sender was reachable ONLY by
            // the direct route, so choosing it silently gave up restart-on-crash. It is now a
            // supervised option, and naming nothing must still mean the packaged single-person
            // sender, byte for byte.
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            Assert.IsEmpty(options.SupervisedSenderScript);
            Assert.AreEqual(3, options.MaxPoses);

            SidecarProcessLauncher launcher = new SidecarProcessLauncher(null, options);
            SidecarPathSet paths = Paths("C:/sidecar");
            paths.SenderScript = "C:/sidecar/wholebody_udp_sender.py";
            Assert.AreEqual("C:/sidecar/wholebody_udp_sender.py",
                            launcher.ResolveSupervisedSender(paths),
                            "an unconfigured launcher must keep the sender it always had");
        }

        [Test]
        public void ASupervisedSenderOverrideResolvesAgainstTheSidecarRoot() {
            SidecarLaunchOptions options = new SidecarLaunchOptions {
                SupervisedSenderScript = "multiperson_udp_sender.py"
            };
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(null, options);
            SidecarPathSet paths = Paths("C:/sidecar/");
            paths.SenderScript = "C:/sidecar/wholebody_udp_sender.py";
            Assert.AreEqual("C:/sidecar/multiperson_udp_sender.py",
                            launcher.ResolveSupervisedSender(paths));
            Assert.IsEmpty(launcher.ResolveDirectScript(paths),
                           "choosing a supervised sender must NOT also arm the direct route");
        }

        // ---------------------------------------------------------------- the single-instance lock

        [Test]
        public void HoldingTheLockMakesEverybodyElseSeeItAsOwned() {
            // Both bootstraps now launch SUPERVISED, so the supervisor binds this port itself and
            // nothing in Unity holds it by hand any more. The probe side still matters: it is how
            // AppBootstrap decides to attach to a running sidecar instead of spawning a second
            // producer onto the same UDP port.
            int port = FreePort();
            Assert.IsFalse(SidecarSingleInstanceLock.IsHeldByAnyone(port), "port should start free");

            SidecarSingleInstanceLock owner = new SidecarSingleInstanceLock();
            Assert.IsTrue(owner.TryAcquire(port));
            Assert.IsTrue(owner.Held);
            Assert.IsTrue(SidecarSingleInstanceLock.IsHeldByAnyone(port),
                          "a second owner must be able to see that this one holds it");

            owner.Release();
            Assert.IsFalse(owner.Held);
            Assert.IsFalse(SidecarSingleInstanceLock.IsHeldByAnyone(port),
                           "releasing must let the next run take it - a stale lock blocks everything");
        }

        [Test]
        public void ASecondOwnerIsRefused() {
            int port = FreePort();
            SidecarSingleInstanceLock first = new SidecarSingleInstanceLock();
            SidecarSingleInstanceLock second = new SidecarSingleInstanceLock();
            try {
                Assert.IsTrue(first.TryAcquire(port));
                Assert.IsFalse(second.TryAcquire(port), "two owners is the failure this prevents");
                Assert.IsFalse(second.Held);
            } finally {
                first.Release();
                second.Release();
            }
        }

        [Test]
        public void ReleasingTwiceIsHarmless() {
            // Called from OnDestroy, which Unity can reach by more than one route.
            SidecarSingleInstanceLock owner = new SidecarSingleInstanceLock();
            owner.TryAcquire(FreePort());
            owner.Release();
            owner.Release();
            Assert.IsFalse(owner.Held);
        }

        [Test]
        public void TheDefaultPortIsTheSupervisorsOwn() {
            // If these ever diverge the two owners stop seeing each other, which is silent.
            Assert.AreEqual(8897, SidecarSingleInstanceLock.DefaultPort);
            Assert.AreEqual(SidecarSingleInstanceLock.DefaultPort,
                            new SidecarLaunchOptions().LockPort);
        }

        // A port the OS says is free right now. Not the real 8897: a test must never fight a sidecar
        // the developer is actually running.
        private static int FreePort() {
            System.Net.Sockets.TcpListener probe =
                new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
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
