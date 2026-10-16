using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using NUnit.Framework;

using VirtualMirror.App;
using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The compiled engine, started by <see cref="VisionHost"/> exactly as an application would, streaming a video file into the receivers over real UDP.
    /// Needs the engine and a clip, so it is skipped (Inconclusive) unless the environment names them:
    /// <c>VIITORX_TEST_ENGINE</c> (the engine folder), <c>VIITORX_TEST_CLIP</c> (a video file) and <c>VIITORX_MODELS_DIR</c> (the models; the engine reads it itself).
    /// </summary>
    public sealed class VisionHostEngineIntegrationTests {
        private const string EngineVariable = "VIITORX_TEST_ENGINE";
        private const string ClipVariable = "VIITORX_TEST_CLIP";
        private const int TimeoutSeconds = 120;

        private sealed class RecordingLog : ILogService {
            public readonly List<string> Lines = new List<string>();

            public void Log(LogLevel level, string message) {
                lock (Lines) {
                    Lines.Add(level + ": " + message);
                }
            }

            public void LogException(Exception exception, string context) {
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

        private static int FreePort() {
            UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            probe.Close();
            return port;
        }

        private static string Required(string name) {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) {
                Assert.Ignore(name + " is not set: the compiled engine is not part of this run.");
            }
            return value;
        }

        [Test]
        public void TheCompiledEngineStreamsHandsFaceAndCutoutIntoTheHost() {
            string engine = Required(EngineVariable);
            string clip = Required(ClipVariable);
            Required("VIITORX_MODELS_DIR");
            RecordingLog log = new RecordingLog();
            VisionSettings settings = new VisionSettings();
            settings.AutoStartSenders = true;
            settings.EnginePath = engine;
            settings.Source = clip;
            settings.HandsMode = VisionSenderMode.Always;
            settings.FaceMode = VisionSenderMode.Always;
            settings.CutoutMode = VisionSenderMode.Always;
            settings.HandsPort = FreePort();
            settings.FacePort = FreePort();
            settings.CutoutPort = FreePort();
            VisionHost host = new VisionHost(log, settings);
            int before = CountEngineProcesses();
            try {
                DateTime deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
                while ((host.HandsUpdates < 5 || host.FaceUpdates < 5 || host.CutoutUpdates < 5) && DateTime.UtcNow < deadline) {
                    host.Tick(0.02f);
                    Thread.Sleep(10);
                }
                Assert.GreaterOrEqual(host.HandsUpdates, 5, "hands from the compiled engine. Log:\n" + log.All());
                Assert.GreaterOrEqual(host.FaceUpdates, 5, "face from the compiled engine. Log:\n" + log.All());
                Assert.GreaterOrEqual(host.CutoutUpdates, 5, "cutout from the compiled engine. Log:\n" + log.All());
                StringAssert.Contains("viitorx-tracking.exe", host.EngineDescription);
                StringAssert.Contains("the path in the settings", host.EngineDescription);
                Assert.AreEqual(before + 1, CountEngineProcesses(), "ONE engine process serves the three streams (the camera is read once)");
                StringAssert.Contains("one engine process for hands, face, cutout", host.SenderStatus(VisionStream.Face));
                Assert.AreEqual(StreamState.Live, host.StateOf(VisionStream.Hands));
                Assert.AreEqual(StreamState.Live, host.StateOf(VisionStream.Face));
                Assert.AreEqual(StreamState.Live, host.StateOf(VisionStream.Cutout));
            } finally {
                host.Dispose();
            }
        }

        [Test]
        public void AnEngineOfAnotherWireGenerationIsRefusedForOneStreamAndForSeveralAndNoProcessIsStarted() {
            string engine = Required(EngineVariable);
            string clip = Required(ClipVariable);
            Required("VIITORX_MODELS_DIR");
            int generation = EngineCompatibility.SupportedWireSpecMajor;
            EngineCompatibility.SupportedWireSpecMajor = generation + 1;
            try {
                foreach (bool several in new bool[] { false, true }) {
                    RecordingLog log = new RecordingLog();
                    VisionSettings settings = new VisionSettings();
                    settings.AutoStartSenders = true;
                    settings.EnginePath = engine;
                    settings.Source = clip;
                    settings.HandsMode = VisionSenderMode.Always;
                    settings.FaceMode = several ? VisionSenderMode.Always : VisionSenderMode.Off;
                    settings.CutoutMode = VisionSenderMode.Off;
                    settings.HandsPort = FreePort();
                    settings.FacePort = FreePort();
                    VisionHost host = new VisionHost(log, settings);
                    int before = CountEngineProcesses();
                    try {
                        DateTime until = DateTime.UtcNow.AddSeconds(4);
                        while (DateTime.UtcNow < until) {
                            host.Tick(0.05f);
                            Thread.Sleep(20);
                        }
                        Assert.AreEqual(before, CountEngineProcesses(), (several ? "several streams" : "one stream") + ": no engine process was started. Log:\n" + log.All());
                        StringAssert.Contains("NOT started", log.All());
                        StringAssert.Contains("not compatible", log.All());
                        StringAssert.Contains("not compatible", host.EngineDescription, "the status says why nothing runs");
                        Assert.AreEqual(0, host.HandsUpdates);
                    } finally {
                        host.Dispose();
                    }
                }
            } finally {
                EngineCompatibility.SupportedWireSpecMajor = generation;
            }
        }

        [Test]
        public void TheEngineEndsWhenTheHostStopsItsSenders() {
            string engine = Required(EngineVariable);
            string clip = Required(ClipVariable);
            Required("VIITORX_MODELS_DIR");
            RecordingLog log = new RecordingLog();
            VisionSettings settings = new VisionSettings();
            settings.AutoStartSenders = true;
            settings.EnginePath = engine;
            settings.Source = clip;
            settings.HandsMode = VisionSenderMode.Always;
            settings.FaceMode = VisionSenderMode.Off;
            settings.CutoutMode = VisionSenderMode.Off;
            settings.HandsPort = FreePort();
            VisionHost host = new VisionHost(log, settings);
            int before = CountEngineProcesses();
            try {
                DateTime deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
                while (host.HandsUpdates < 3 && DateTime.UtcNow < deadline) {
                    host.Tick(0.02f);
                    Thread.Sleep(10);
                }
                Assert.GreaterOrEqual(host.HandsUpdates, 3, log.All());
                Assert.Greater(CountEngineProcesses(), before, "the engine process is running while the host streams");
            } finally {
                host.Dispose();
            }
            DateTime gone = DateTime.UtcNow.AddSeconds(15);
            while (CountEngineProcesses() > before && DateTime.UtcNow < gone) {
                Thread.Sleep(100);
            }
            Assert.AreEqual(before, CountEngineProcesses(), "Dispose must not leave the engine running");
        }

        [Test]
        public void ChangingTheWantedStreamsRestartsTheOneProcessAndTheRestKeepStreaming() {
            string engine = Required(EngineVariable);
            string clip = Required(ClipVariable);
            Required("VIITORX_MODELS_DIR");
            RecordingLog log = new RecordingLog();
            VisionSettings settings = new VisionSettings();
            settings.AutoStartSenders = true;
            settings.EnginePath = engine;
            settings.Source = clip;
            settings.HandsMode = VisionSenderMode.Always;
            settings.FaceMode = VisionSenderMode.Always;
            settings.CutoutMode = VisionSenderMode.Off;
            settings.HandsPort = FreePort();
            settings.FacePort = FreePort();
            VisionHost host = new VisionHost(log, settings);
            int before = CountEngineProcesses();
            try {
                DateTime deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
                while ((host.HandsUpdates < 5 || host.FaceUpdates < 5) && DateTime.UtcNow < deadline) {
                    host.Tick(0.02f);
                    Thread.Sleep(10);
                }
                Assert.GreaterOrEqual(host.FaceUpdates, 5, log.All());
                settings.FaceMode = VisionSenderMode.Off;
                long handsAtChange = host.HandsUpdates;
                deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
                while (!log.All().Contains("Vision: started the Hands sender") && DateTime.UtcNow < deadline) {
                    host.Tick(0.02f);
                    Thread.Sleep(10);
                }
                StringAssert.Contains("Vision: started the Hands sender", log.All(), "one stream left: it runs in its own process again");
                deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);
                while (host.HandsUpdates < handsAtChange + 20 && DateTime.UtcNow < deadline) {
                    host.Tick(0.02f);
                    Thread.Sleep(10);
                }
                Assert.GreaterOrEqual(host.HandsUpdates, handsAtChange + 20, "hands kept streaming after the restart. Log:\n" + log.All());
                Assert.AreEqual(before + 1, CountEngineProcesses(), "still exactly one engine process");
                Assert.IsNull(host.Face, "the face receiver is gone with its stream");
            } finally {
                host.Dispose();
            }
        }

        private static int CountEngineProcesses() {
            return System.Diagnostics.Process.GetProcessesByName("viitorx-tracking").Length;
        }
    }
}
