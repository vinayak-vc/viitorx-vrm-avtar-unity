using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using NUnit.Framework;

using VirtualMirror.App;
using VirtualMirror.Core;
using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="VisionHost"/>: the receivers, the per-frame hand-over and the demand bookkeeping, over real UDP sockets on loopback with the sender
    /// auto-start OFF (so no process is started), except the engine tests, which start a stand-in executable (a copy of cmd.exe) to see WHICH program
    /// the host chooses. Starting the real senders needs the engine and a camera and is NOT covered here.
    /// </summary>
    public sealed class VisionHostTests {
        private const int WaitMilliseconds = 3000;

        private sealed class RecordingLog : ILogService {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Infos = new List<string>();

            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Errors.Add(message);
                } else if (level == LogLevel.Info) {
                    Infos.Add(message);
                }
            }

            public void LogException(Exception exception, string context) {
                Errors.Add(context + ": " + exception.Message);
            }

            public void Flush() {
            }
        }

        private UdpClient sender;
        private RecordingLog log;

        [SetUp]
        public void SetUp() {
            sender = new UdpClient();
            log = new RecordingLog();
        }

        [TearDown]
        public void TearDown() {
            sender.Close();
        }

        private static int FreePort() {
            UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            probe.Close();
            return port;
        }

        private VisionSettings Settings(VisionSenderMode hands, VisionSenderMode face) {
            return Settings(hands, face, VisionSenderMode.Off);
        }

        private VisionSettings Settings(VisionSenderMode hands, VisionSenderMode face, VisionSenderMode cutout) {
            VisionSettings settings = new VisionSettings();
            settings.AutoStartSenders = false;
            settings.HandsMode = hands;
            settings.FaceMode = face;
            settings.CutoutMode = cutout;
            settings.HandsPort = FreePort();
            settings.FacePort = FreePort();
            settings.CutoutPort = FreePort();
            return settings;
        }

        private void Send(int port, byte[] data) {
            sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
        }

        private static void TickUntil(VisionHost host, Func<bool> condition, string what) {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(WaitMilliseconds);
            while (!condition()) {
                if (DateTime.UtcNow > deadline) {
                    Assert.Fail("timed out waiting for " + what);
                }
                host.Tick(0.01f);
                Thread.Sleep(2);
            }
        }

        [Test]
        public void ReceiversExistOnlyForTheStreamsThatAreNotOff() {
            using (HostScope scope = new HostScope(new VisionHost(log, Settings(VisionSenderMode.Auto, VisionSenderMode.Off)))) {
                scope.Host.Tick(1f);
                Assert.IsNotNull(scope.Host.Hands);
                Assert.IsNull(scope.Host.Face);
                Assert.IsTrue(scope.Host.Hands.IsRunning);
                Assert.AreEqual("off", scope.Host.SenderStatus(VisionStream.Face));
                Assert.AreEqual(StreamState.NoStream, scope.Host.StateOf(VisionStream.Face));
            }
        }

        [Test]
        public void TheHandsStreamOffMeansNoHandsReceiver() {
            using (HostScope scope = new HostScope(new VisionHost(log, Settings(VisionSenderMode.Off, VisionSenderMode.Auto)))) {
                scope.Host.Tick(1f);
                Assert.IsNull(scope.Host.Hands);
                Assert.IsNotNull(scope.Host.Face);
                Assert.AreEqual("off", scope.Host.SenderStatus(VisionStream.Hands));
            }
        }

        [Test]
        public void ANewMessageReachesTheSubscriberOncePerDatagramOnTheTickingThread() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                int handsEvents = 0;
                int faceEvents = 0;
                int lastCount = -1;
                host.HandsUpdated += delegate (HandsMessage message) {
                    handsEvents = handsEvents + 1;
                    lastCount = message.Count;
                };
                host.FaceUpdated += delegate (FaceMessage message) {
                    faceEvents = faceEvents + 1;
                };
                host.Tick(1f);
                Send(settings.HandsPort, VisionFixtures.Load("hands_two.json"));
                TickUntil(host, delegate () { return handsEvents >= 1; }, "the hands event");
                Assert.AreEqual(2, lastCount);
                Assert.AreEqual(StreamState.Live, host.StateOf(VisionStream.Hands));
                host.Tick(0.01f);
                host.Tick(0.01f);
                Assert.AreEqual(1, handsEvents, "one datagram is one event, however often the host ticks");
                Assert.AreEqual(0, faceEvents, "the face stream saw nothing");
                Send(settings.FacePort, VisionFixtures.Load("face_full.json"));
                TickUntil(host, delegate () { return faceEvents >= 1; }, "the face event");
                Assert.AreEqual(1L, host.FaceUpdates);
                Assert.AreEqual("streaming from a sender started elsewhere", host.SenderStatus(VisionStream.Face));
            }
        }

        [Test]
        public void TheMessageRateIsMeasuredOverTheTickedSecond() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Off);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                host.Tick(1f);
                Assert.AreEqual(0f, host.HandsRate, "no messages yet");
                Send(settings.HandsPort, VisionFixtures.Load("hands_two.json"));
                TickUntil(host, delegate () { return host.HandsUpdates >= 1; }, "the first message");
                host.Tick(1f);
                Assert.Greater(host.HandsRate, 0f, "one message in the ticked second is a rate above zero");
                Assert.Less(host.HandsRate, 2f);
            }
        }

        [Test]
        public void AChangedPortRebindsTheReceiver() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Off);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                host.Tick(1f);
                int first = host.Hands.Port;
                Assert.AreEqual(settings.HandsPort, first);
                int moved = FreePort();
                settings.HandsPort = moved;
                host.Tick(1f);
                Assert.AreEqual(moved, host.Hands.Port, "the receiver follows the setting");
                int events = 0;
                host.HandsUpdated += delegate (HandsMessage message) {
                    events = events + 1;
                };
                Send(first, VisionFixtures.Load("hands_two.json"));
                Send(moved, VisionFixtures.Load("hands_two.json"));
                TickUntil(host, delegate () { return events >= 1; }, "a datagram on the new port");
                Assert.AreEqual(1, events, "nothing is read from the old port any more");
            }
        }

        [Test]
        public void TurningAStreamOffClosesItsSocket() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                scope.Host.Tick(1f);
                Assert.IsNotNull(scope.Host.Face);
                int port = settings.FacePort;
                settings.FaceMode = VisionSenderMode.Off;
                scope.Host.Tick(1f);
                Assert.IsNull(scope.Host.Face);
                UdpClient taken = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                taken.Close();
            }
        }

        [Test]
        public void ATakenPortIsReportedOnceAndNotRetriedEveryTick() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Off);
            UdpClient squatter = new UdpClient(new IPEndPoint(IPAddress.Loopback, settings.HandsPort));
            try {
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    scope.Host.Tick(1f);
                    Assert.IsNull(scope.Host.Hands);
                    StringAssert.Contains("IN USE", scope.Host.SenderStatus(VisionStream.Hands));
                    int afterFirst = log.Errors.Count;
                    Assert.GreaterOrEqual(afterFirst, 1, "the failure is logged");
                    scope.Host.Tick(1f);
                    scope.Host.Tick(1f);
                    Assert.AreEqual(afterFirst, log.Errors.Count, "and not again on every reconcile");
                }
            } finally {
                squatter.Close();
            }
        }

        [Test]
        public void DemandIsCountedPerStreamAndNeverGoesNegative() {
            using (HostScope scope = new HostScope(new VisionHost(log, Settings(VisionSenderMode.Auto, VisionSenderMode.Auto)))) {
                VisionHost host = scope.Host;
                Assert.IsFalse(host.HasDemand(VisionStream.Hands));
                host.AddDemand(VisionStream.Hands);
                host.AddDemand(VisionStream.Hands);
                Assert.IsTrue(host.HasDemand(VisionStream.Hands));
                Assert.IsFalse(host.HasDemand(VisionStream.Face));
                host.RemoveDemand(VisionStream.Hands);
                Assert.IsTrue(host.HasDemand(VisionStream.Hands), "two scenes asked, one let go");
                host.RemoveDemand(VisionStream.Hands);
                host.RemoveDemand(VisionStream.Hands);
                Assert.IsFalse(host.HasDemand(VisionStream.Hands));
                host.AddDemand(VisionStream.Hands);
                Assert.IsTrue(host.HasDemand(VisionStream.Hands), "an extra release did not leave a debt");
            }
        }

        [Test]
        public void ASenderRunsOnlyWhenAutoStartIsOnTheReceiverIsUpAndTheModeOrDemandAsksForIt() {
            Assert.IsTrue(VisionHost.IsSenderWanted(true, VisionSenderMode.Always, 0, true), "Always runs without any demand");
            Assert.IsTrue(VisionHost.IsSenderWanted(true, VisionSenderMode.Auto, 1, true), "Auto runs while a scene asks");
            Assert.IsFalse(VisionHost.IsSenderWanted(true, VisionSenderMode.Auto, 0, true), "Auto idles with no scene asking");
            Assert.IsFalse(VisionHost.IsSenderWanted(true, VisionSenderMode.Off, 3, true), "Off never runs");
            Assert.IsFalse(VisionHost.IsSenderWanted(false, VisionSenderMode.Always, 3, true), "auto-start off: the user runs the sender");
            Assert.IsFalse(VisionHost.IsSenderWanted(true, VisionSenderMode.Always, 3, false), "no receiver, no sender");
        }

        [Test]
        public void StatusExplainsWhyNoSenderIsRunning() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                scope.Host.Tick(1f);
                Assert.AreEqual("waiting for a sender (auto-start is off)", scope.Host.SenderStatus(VisionStream.Hands));
                settings.AutoStartSenders = true;
                Assert.AreEqual("idle (starts when a scene needs it)", scope.Host.SenderStatus(VisionStream.Hands));
                scope.Host.AddDemand(VisionStream.Hands);
                Assert.AreEqual("waiting to start the sender", scope.Host.SenderStatus(VisionStream.Hands));
            }
        }

        private static void TickUntilLogged(VisionHost host, List<string> lines, string fragment) {
            for (int i = 0; i < 6; i++) {
                host.Tick(1f);
                foreach (string line in lines) {
                    if (line.Contains(fragment)) {
                        return;
                    }
                }
            }
            Assert.Fail("never logged: " + fragment);
        }

        [Test]
        public void ASenderIsStartedFromTheCompiledEngineWhenTheSettingNamesIt() {
            string shell = Environment.GetEnvironmentVariable("ComSpec");
            Assume.That(!string.IsNullOrEmpty(shell) && System.IO.File.Exists(shell), "needs cmd.exe to stand in for the engine");
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vt_engine_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.Copy(shell, System.IO.Path.Combine(folder, "viitorx-tracking.exe"));
            try {
                VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Off);
                settings.AutoStartSenders = true;
                settings.EnginePath = folder;
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    TickUntilLogged(scope.Host, log.Infos, "started the Hands sender");
                    StringAssert.Contains("viitorx-tracking.exe", scope.Host.EngineDescription);
                    StringAssert.Contains("the path in the settings", scope.Host.EngineDescription);
                    Assert.IsEmpty(log.Errors, "the engine path was valid: " + string.Join(" | ", log.Errors.ToArray()));
                }
            } finally {
                try {
                    System.IO.Directory.Delete(folder, true);
                } catch (Exception) {
                    // the stand-in may still be shutting down; the temp folder is not ours to insist on
                }
            }
        }

        private static string StandInEngineFolder() {
            string shell = Environment.GetEnvironmentVariable("ComSpec");
            Assume.That(!string.IsNullOrEmpty(shell) && System.IO.File.Exists(shell), "needs cmd.exe to stand in for the engine");
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vt_engine_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.Copy(shell, System.IO.Path.Combine(folder, "viitorx-tracking.exe"));
            return folder;
        }

        private static void RemoveQuietly(string folder) {
            try {
                System.IO.Directory.Delete(folder, true);
            } catch (Exception) {
                // the stand-in may still be shutting down; the temp folder is not ours to insist on
            }
        }

        [Test]
        public void SeveralWantedStreamsAreServedByOneEngineProcessNotOnePerStream() {
            string folder = StandInEngineFolder();
            try {
                VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Always, VisionSenderMode.Always);
                settings.AutoStartSenders = true;
                settings.EnginePath = folder;
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    TickUntilLogged(scope.Host, log.Infos, "started ONE engine process for hands, face, cutout");
                    Assert.AreEqual(0, log.Infos.FindAll(delegate (string line) { return line.Contains("sender (") && line.Contains("started the "); }).Count,
                                    "no per-stream process was started beside the group");
                    Assert.IsEmpty(log.Errors, string.Join(" | ", log.Errors.ToArray()));
                }
            } finally {
                RemoveQuietly(folder);
            }
        }

        [Test]
        public void TheGroupNamesOnlyTheStreamsThatAreWanted() {
            string folder = StandInEngineFolder();
            try {
                VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Off, VisionSenderMode.Always);
                settings.AutoStartSenders = true;
                settings.EnginePath = folder;
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    TickUntilLogged(scope.Host, log.Infos, "started ONE engine process for hands, cutout");
                }
            } finally {
                RemoveQuietly(folder);
            }
        }

        [Test]
        public void RestartingOneStreamOfTheGroupRestartsTheOneProcess() {
            string folder = StandInEngineFolder();
            try {
                VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Always, VisionSenderMode.Off);
                settings.AutoStartSenders = true;
                settings.EnginePath = folder;
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    TickUntilLogged(scope.Host, log.Infos, "started ONE engine process for hands, face");
                    int first = log.Infos.FindAll(delegate (string line) { return line.Contains("started ONE engine process"); }).Count;
                    scope.Host.RestartSender(VisionStream.Face);
                    for (int i = 0; i < 8; i++) {
                        scope.Host.Tick(1f);
                    }
                    int second = log.Infos.FindAll(delegate (string line) { return line.Contains("started ONE engine process"); }).Count;
                    Assert.Greater(second, first, "the restart of one stream of the group started the process again");
                }
            } finally {
                RemoveQuietly(folder);
            }
        }

        [Test]
        public void ASingleWantedStreamKeepsItsOwnProcessAndNoGroupIsStarted() {
            string folder = StandInEngineFolder();
            try {
                VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Off, VisionSenderMode.Off);
                settings.AutoStartSenders = true;
                settings.EnginePath = folder;
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    TickUntilLogged(scope.Host, log.Infos, "started the Hands sender");
                    Assert.AreEqual(0, log.Infos.FindAll(delegate (string line) { return line.Contains("ONE engine process"); }).Count);
                }
            } finally {
                RemoveQuietly(folder);
            }
        }

        [Test]
        public void ThePythonProjectIsNeverGivenTheCombinedCommand() {
            VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Always, VisionSenderMode.Off);
            settings.AutoStartSenders = true;
            settings.UsePythonProject = true;
            settings.PythonProjectPath = "C:/no/python/project";
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                TickUntilLogged(scope.Host, log.Errors, "sender did not start");
                Assert.AreEqual(0, log.Infos.FindAll(delegate (string line) { return line.Contains("ONE engine process"); }).Count);
                Assert.AreEqual(0, log.Errors.FindAll(delegate (string line) { return line.Contains("the engine did not start for"); }).Count, "the combined command is never attempted for a Python project");
            }
        }

        [Test]
        public void ABadEnginePathIsReportedAndNoSenderIsStarted() {
            VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Off);
            settings.AutoStartSenders = true;
            settings.EnginePath = "C:/definitely/not/here";
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                TickUntilLogged(scope.Host, log.Errors, "did not start");
                StringAssert.Contains("not found", scope.Host.EngineDescription);
                StringAssert.Contains("C:/definitely/not/here", scope.Host.EngineDescription);
                Assert.AreEqual(0, log.Infos.FindAll(delegate (string line) { return line.Contains("started the Hands sender"); }).Count);
            }
        }

        [Test]
        public void UsePythonProjectIsHonouredEvenWhenAnEngineIsNamed() {
            VisionSettings settings = Settings(VisionSenderMode.Always, VisionSenderMode.Off);
            settings.AutoStartSenders = true;
            settings.EnginePath = "C:/definitely/not/here";
            settings.UsePythonProject = true;
            settings.PythonProjectPath = "C:/no/python/project";
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                TickUntilLogged(scope.Host, log.Errors, "did not start");
                StringAssert.Contains("Use Python Project is on", scope.Host.EngineDescription);
                StringAssert.DoesNotContain("C:/definitely/not/here", scope.Host.EngineDescription, "the engine path is not consulted when the Python project is asked for");
            }
        }

        [Test]
        public void DisposeFreesBothPortsAndStopsEvents() {
            VisionSettings settings = Settings(VisionSenderMode.Auto, VisionSenderMode.Auto);
            VisionHost host = new VisionHost(log, settings);
            int events = 0;
            host.HandsUpdated += delegate (HandsMessage message) {
                events = events + 1;
            };
            host.Tick(1f);
            host.Dispose();
            host.Dispose();
            UdpClient hands = new UdpClient(new IPEndPoint(IPAddress.Loopback, settings.HandsPort));
            UdpClient face = new UdpClient(new IPEndPoint(IPAddress.Loopback, settings.FacePort));
            hands.Close();
            face.Close();
            host.Tick(1f);
            Assert.AreEqual(0, events);
            Assert.IsNull(host.Hands);
        }

        // ------------------------------------------------------------------ the cutout stream

        private void SendFamily(int port, string family, int count) {
            int i = 0;
            while (i < count) {
                Send(port, VisionFixtures.Load("cutout_" + family + "_0" + i + ".bin"));
                i = i + 1;
            }
        }

        [Test]
        public void TheCutoutReceiverExistsOnlyWhileTheStreamIsNotOff() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                scope.Host.Tick(1f);
                Assert.IsNull(scope.Host.Hands);
                Assert.IsNull(scope.Host.Face);
                Assert.IsNotNull(scope.Host.Cutout);
                Assert.IsTrue(scope.Host.Cutout.IsRunning);
                Assert.AreEqual(settings.CutoutPort, scope.Host.Cutout.Port);
                settings.CutoutMode = VisionSenderMode.Off;
                scope.Host.Tick(1f);
                Assert.IsNull(scope.Host.Cutout);
                Assert.AreEqual("off", scope.Host.SenderStatus(VisionStream.Cutout));
                Assert.AreEqual(StreamState.NoStream, scope.Host.StateOf(VisionStream.Cutout));
            }
        }

        [Test]
        public void ACutoutFrameIsUploadedAndAnnouncedOncePerFrameOnTheTickingThread() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                int events = 0;
                uint lastSequence = 0u;
                host.CutoutUpdated += delegate (CutoutTextures textures) {
                    events = events + 1;
                    lastSequence = textures.Sequence;
                };
                host.Tick(1f);
                Assert.IsFalse(host.CutoutTextures.HasFrame);
                SendFamily(settings.CutoutPort, "rgba", 4);
                TickUntil(host, delegate () { return events >= 1; }, "the cutout event");
                Assert.AreEqual(1042u, lastSequence);
                Assert.IsTrue(host.CutoutTextures.HasFrame);
                Assert.AreEqual(16, host.CutoutTextures.ColorTexture.width);
                Assert.AreEqual(8, host.CutoutTextures.MaskTexture.width);
                Assert.AreEqual(StreamState.Live, host.StateOf(VisionStream.Cutout));
                host.Tick(0.01f);
                host.Tick(0.01f);
                Assert.AreEqual(1, events, "one frame is one event, however often the host ticks");
                Assert.AreEqual(1L, host.CutoutUpdates);
                Assert.AreEqual("streaming from a sender started elsewhere", host.SenderStatus(VisionStream.Cutout));
                SendFamily(settings.CutoutPort, "next", 4);
                TickUntil(host, delegate () { return events >= 2; }, "the second frame");
                Assert.AreEqual(1043u, lastSequence);
                Assert.AreSame(host.CutoutTextures, host.CutoutTextures, "the same textures object for the life of the host");
            }
        }

        [Test]
        public void TheCutoutRateIsMeasuredOverTheTickedSecond() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                host.Tick(1f);
                Assert.AreEqual(0f, host.CutoutRate);
                SendFamily(settings.CutoutPort, "rgba", 4);
                TickUntil(host, delegate () { return host.CutoutUpdates >= 1; }, "the first frame");
                host.Tick(1f);
                Assert.Greater(host.CutoutRate, 0f);
                Assert.Less(host.CutoutRate, 2f);
            }
        }

        [Test]
        public void TurningTheCutoutOffClosesItsSocketAndEmptiesTheTextures() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                host.Tick(1f);
                SendFamily(settings.CutoutPort, "rgba", 4);
                TickUntil(host, delegate () { return host.CutoutUpdates >= 1; }, "a frame");
                int port = settings.CutoutPort;
                settings.CutoutMode = VisionSenderMode.Off;
                host.Tick(1f);
                Assert.IsNull(host.Cutout);
                Assert.IsFalse(host.CutoutTextures.HasFrame, "a frame of a stream that no longer exists is not shown");
                UdpClient taken = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                taken.Close();
            }
        }

        [Test]
        public void AChangedCutoutPortRebindsTheReceiver() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                VisionHost host = scope.Host;
                host.Tick(1f);
                int first = host.Cutout.Port;
                int moved = FreePort();
                settings.CutoutPort = moved;
                host.Tick(1f);
                Assert.AreEqual(moved, host.Cutout.Port);
                int events = 0;
                host.CutoutUpdated += delegate (CutoutTextures textures) {
                    events = events + 1;
                };
                SendFamily(first, "rgba", 4);
                SendFamily(moved, "next", 4);
                TickUntil(host, delegate () { return events >= 1; }, "a frame on the new port");
                Assert.AreEqual(1043u, host.CutoutTextures.Sequence, "nothing is read from the old port any more");
            }
        }

        [Test]
        public void ATakenCutoutPortIsReportedOnceAndNotRetriedEveryTick() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            UdpClient squatter = new UdpClient(new IPEndPoint(IPAddress.Loopback, settings.CutoutPort));
            try {
                using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                    scope.Host.Tick(1f);
                    Assert.IsNull(scope.Host.Cutout);
                    StringAssert.Contains("IN USE", scope.Host.SenderStatus(VisionStream.Cutout));
                    int afterFirst = log.Errors.Count;
                    Assert.GreaterOrEqual(afterFirst, 1);
                    scope.Host.Tick(1f);
                    scope.Host.Tick(1f);
                    Assert.AreEqual(afterFirst, log.Errors.Count, "and not again on every reconcile");
                }
            } finally {
                squatter.Close();
            }
        }

        [Test]
        public void TheCutoutDemandIsCountedAlongsideTheOthersWithoutTouchingThem() {
            using (HostScope scope = new HostScope(new VisionHost(log, Settings(VisionSenderMode.Auto, VisionSenderMode.Auto, VisionSenderMode.Auto)))) {
                VisionHost host = scope.Host;
                host.AddDemand(VisionStream.Cutout);
                Assert.IsTrue(host.HasDemand(VisionStream.Cutout));
                Assert.IsFalse(host.HasDemand(VisionStream.Hands));
                Assert.IsFalse(host.HasDemand(VisionStream.Face));
                host.RemoveDemand(VisionStream.Cutout);
                host.RemoveDemand(VisionStream.Cutout);
                Assert.IsFalse(host.HasDemand(VisionStream.Cutout), "an extra release does not leave a debt");
            }
        }

        [Test]
        public void TheCutoutStatusExplainsWhyNoSenderIsRunning() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                scope.Host.Tick(1f);
                Assert.AreEqual("waiting for a sender (auto-start is off)", scope.Host.SenderStatus(VisionStream.Cutout));
                settings.AutoStartSenders = true;
                Assert.AreEqual("idle (starts when a scene needs it)", scope.Host.SenderStatus(VisionStream.Cutout));
                scope.Host.AddDemand(VisionStream.Cutout);
                Assert.AreEqual("waiting to start the sender", scope.Host.SenderStatus(VisionStream.Cutout));
            }
        }

        [Test]
        public void TheModeOfEachStreamComesFromItsOwnSetting() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Auto, VisionSenderMode.Always);
            using (HostScope scope = new HostScope(new VisionHost(log, settings))) {
                Assert.AreEqual(VisionSenderMode.Off, scope.Host.ModeOf(VisionStream.Hands));
                Assert.AreEqual(VisionSenderMode.Auto, scope.Host.ModeOf(VisionStream.Face));
                Assert.AreEqual(VisionSenderMode.Always, scope.Host.ModeOf(VisionStream.Cutout));
            }
        }

        [Test]
        public void DisposeFreesTheCutoutPortAndStopsItsEvents() {
            VisionSettings settings = Settings(VisionSenderMode.Off, VisionSenderMode.Off, VisionSenderMode.Auto);
            VisionHost host = new VisionHost(log, settings);
            int events = 0;
            host.CutoutUpdated += delegate (CutoutTextures textures) {
                events = events + 1;
            };
            host.Tick(1f);
            host.Dispose();
            host.Dispose();
            UdpClient cutout = new UdpClient(new IPEndPoint(IPAddress.Loopback, settings.CutoutPort));
            cutout.Close();
            host.Tick(1f);
            Assert.AreEqual(0, events);
            Assert.IsNull(host.Cutout);
        }

        private sealed class HostScope : IDisposable {
            public readonly VisionHost Host;

            public HostScope(VisionHost host) {
                Host = host;
            }

            public void Dispose() {
                Host.Dispose();
            }
        }
    }
}
