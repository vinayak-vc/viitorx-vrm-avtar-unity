using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// Starting the compiled engine through <see cref="SidecarProcessLauncher"/>: the command line it builds, the refusals before anything is spawned,
    /// and (on Windows) a real child process that is started, seen alive and stopped. The Python paths are covered by SidecarDirectLaunchF36Tests and
    /// must not change.
    /// </summary>
    public sealed class SidecarEngineLaunchTests {
        private sealed class RecordingLog : ILogService {
            public readonly List<string> Lines = new List<string>();

            public void Log(LogLevel level, string message) {
                lock (Lines) {
                    Lines.Add(level + ": " + message);
                }
            }

            public void LogException(System.Exception exception, string context) {
                lock (Lines) {
                    Lines.Add("EXCEPTION: " + context + ": " + exception.Message);
                }
            }

            public void Flush() {
            }

            public string All() {
                lock (Lines) {
                    return string.Join("\n", Lines.ToArray());
                }
            }
        }

        [Test]
        public void TheCommandLineIsTheSubcommandThenItsOptionsThenTheParentPid() {
            Assert.AreEqual("hands --video camera:0 --port 8901 --parent-pid 4242",
                            SidecarProcessLauncher.BuildEngineArguments("hands", "--video camera:0 --port 8901", 4242));
        }

        [Test]
        public void NoOptionsAndNoPidGiveJustTheSubcommand() {
            Assert.AreEqual("face", SidecarProcessLauncher.BuildEngineArguments("face", string.Empty, 0));
            Assert.AreEqual("face", SidecarProcessLauncher.BuildEngineArguments("face", null, -1));
        }

        [Test]
        public void NoModelPathIsEverSentBecauseTheEngineFindsItsModelsBesideItself() {
            string arguments = SidecarProcessLauncher.BuildEngineArguments("cutout", "--video camera:0", 7);
            StringAssert.DoesNotContain("--model", arguments);
            StringAssert.DoesNotContain("--palm-model", arguments);
        }

        [Test]
        public void ADefaultLauncherHasNoEngineSoEveryExistingPathIsUnchanged() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            Assert.IsEmpty(options.EngineExecutable);
            Assert.IsEmpty(options.EngineCommand);
        }

        [Test]
        public void AMissingExecutableFailsBeforeAnythingIsSpawnedAndSaysSo() {
            RecordingLog log = new RecordingLog();
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.DirectSenderOnly = true;
            options.EngineExecutable = "C:/definitely/not/here/viitorx-tracking.exe";
            options.EngineCommand = "hands";
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(log, options);
            Assert.IsFalse(launcher.Start(null));
            Assert.AreEqual(SidecarLaunchState.Failed, launcher.State);
            StringAssert.Contains("Engine executable is missing", launcher.LastError);
            StringAssert.Contains("cannot start the engine", log.All());
            launcher.Dispose();
        }

        [Test]
        public void AMissingSubcommandFailsBeforeAnythingIsSpawned() {
            string file = Path.GetTempFileName();
            try {
                SidecarLaunchOptions options = new SidecarLaunchOptions();
                options.DirectSenderOnly = true;
                options.EngineExecutable = file;
                SidecarProcessLauncher launcher = new SidecarProcessLauncher(new RecordingLog(), options);
                Assert.IsFalse(launcher.Start(null));
                StringAssert.Contains("No engine command", launcher.LastError);
                launcher.Dispose();
            } finally {
                File.Delete(file);
            }
        }

        [Test]
        public void AutoStartOffStillMeansTheUserRunsTheEngineThemselves() {
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.AutoStart = false;
            options.EngineExecutable = "C:/anything/viitorx-tracking.exe";
            options.EngineCommand = "hands";
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(new RecordingLog(), options);
            Assert.IsFalse(launcher.Start(null));
            Assert.AreEqual(SidecarLaunchState.Disabled, launcher.State);
            launcher.Dispose();
        }

#if UNITY_EDITOR_WIN
        [Test]
        public void ARealChildIsStartedWithTheEngineCommandLineSeenAliveAndStopped() {
            string shell = System.Environment.GetEnvironmentVariable("ComSpec");
            Assume.That(!string.IsNullOrEmpty(shell) && File.Exists(shell), "needs cmd.exe");
            RecordingLog log = new RecordingLog();
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.DirectSenderOnly = true;
            options.EngineExecutable = shell.Replace('\\', '/');
            options.EngineCommand = "/c";
            // The child lives for about 20 s; "&rem" turns the --parent-pid the launcher appends into a comment.
            options.DirectArguments = "ping -n 20 127.0.0.1 >nul &rem";
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(log, options);
            try {
                Assert.IsTrue(launcher.Start(null), launcher.LastError);
                Assert.AreEqual(SidecarLaunchState.Starting, launcher.State);
                Assert.IsTrue(launcher.IsOwnedProcessAlive, "the child must be running");
                StringAssert.Contains("/c ping -n 20 127.0.0.1 >nul &rem --parent-pid ", log.All());
                launcher.Stop();
                Assert.IsFalse(launcher.IsOwnedProcessAlive, "Stop ends the child");
                Assert.AreEqual(SidecarLaunchState.Stopped, launcher.State);
            } finally {
                launcher.Dispose();
            }
        }

        [Test]
        public void AChildThatExitsOnItsOwnIsReportedAsFailedWithItsExitCode() {
            string shell = System.Environment.GetEnvironmentVariable("ComSpec");
            Assume.That(!string.IsNullOrEmpty(shell) && File.Exists(shell), "needs cmd.exe");
            RecordingLog log = new RecordingLog();
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.DirectSenderOnly = true;
            options.EngineExecutable = shell.Replace('\\', '/');
            options.EngineCommand = "/c";
            options.DirectArguments = "exit 3 &rem";
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(log, options);
            try {
                Assert.IsTrue(launcher.Start(null), launcher.LastError);
                float waited = 0f;
                while (launcher.State != SidecarLaunchState.Failed && waited < 10f) {
                    System.Threading.Thread.Sleep(50);
                    waited += 0.05f;
                }
                Assert.AreEqual(SidecarLaunchState.Failed, launcher.State);
                StringAssert.Contains("engine (/c) exited unexpectedly, code 3", launcher.LastError);
            } finally {
                launcher.Dispose();
            }
        }
#endif
    }
}
