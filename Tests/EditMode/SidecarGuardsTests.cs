using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The two checks every sidecar start runs first (shared by AppBootstrap, SidecarBootstrap and VisionHost):
    /// is somebody already sending on the port, and does a LAN camera have its PIN. Real loopback sockets, no
    /// sidecar.
    /// </summary>
    public sealed class SidecarGuardsTests {
        private const string LanLink = "http://192.168.1.5:3000/viewer?room=room-abc123";

        private string previousPin;

        [SetUp]
        public void RememberPinVariable() {
            previousPin = Environment.GetEnvironmentVariable(SidecarProcessLauncher.LanCameraPinEnvironmentVariable);
            Environment.SetEnvironmentVariable(SidecarProcessLauncher.LanCameraPinEnvironmentVariable, null);
        }

        [TearDown]
        public void RestorePinVariable() {
            Environment.SetEnvironmentVariable(SidecarProcessLauncher.LanCameraPinEnvironmentVariable, previousPin);
        }

        private static int FreeUdpPort() {
            using (UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
                return ((IPEndPoint)socket.Client.LocalEndPoint).Port;
            }
        }

        [Test]
        public void ASilentPortIsNotAProducer() {
            Assert.IsFalse(SidecarGuards.ProducerAlreadyRunning(FreeUdpPort(), 150));
        }

        [Test]
        public void ADatagramDuringTheProbeMakesItAProducer() {
            int port = FreeUdpPort();
            Task<bool> probe = Task.Run(delegate () {
                return SidecarGuards.ProducerAlreadyRunning(port, 2000);
            });
            using (UdpClient sender = new UdpClient()) {
                byte[] payload = new byte[] { 1, 2, 3 };
                // Keep sending until the probe has bound and heard one: the bind races with this thread.
                for (int attempt = 0; attempt < 40 && !probe.IsCompleted; attempt++) {
                    sender.Send(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));
                    Thread.Sleep(25);
                }
            }
            Assert.IsTrue(probe.Wait(3000), "the probe must finish");
            Assert.IsTrue(probe.Result);
        }

        [Test]
        public void ANonLanSourceNeverNeedsAPin() {
            Assert.IsFalse(SidecarGuards.LanCameraNeedsPin("camera:0", string.Empty));
            Assert.IsFalse(SidecarGuards.LanCameraNeedsPin("rtsp://host/stream", string.Empty));
            Assert.IsFalse(SidecarGuards.LanCameraNeedsPin(string.Empty, string.Empty));
        }

        [Test]
        public void ALanCameraWithoutAnyPinNeedsOne() {
            Assert.IsTrue(SidecarGuards.LanCameraNeedsPin(LanLink, string.Empty));
            Assert.IsTrue(SidecarGuards.LanCameraNeedsPin(LanLink, "   "));
            Assert.IsTrue(SidecarGuards.LanCameraNeedsPin(LanLink, null));
        }

        [Test]
        public void ATypedPinSatisfiesIt() {
            Assert.IsFalse(SidecarGuards.LanCameraNeedsPin(LanLink, "1234"));
        }

        [Test]
        public void TheEnvironmentVariableSatisfiesItToo() {
            Environment.SetEnvironmentVariable(SidecarProcessLauncher.LanCameraPinEnvironmentVariable, "9876");
            Assert.IsFalse(SidecarGuards.LanCameraNeedsPin(LanLink, string.Empty));
        }
    }
}
