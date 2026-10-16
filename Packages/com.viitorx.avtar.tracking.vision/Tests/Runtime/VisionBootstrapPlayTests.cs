using System.Collections;
using System.Net;
using System.Net.Sockets;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using VirtualMirror.App;
using VirtualMirror.Core;
using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="VisionBootstrap"/>, <see cref="VisionHostRegistry"/> and <see cref="VisionHostClient"/> in Play mode (Awake, Update and OnDestroy only run there).
    /// Every bootstrap here has auto-start OFF and free ports, so no engine is started; starting the engine through the host is covered by the host tests.
    /// </summary>
    public sealed class VisionBootstrapPlayTests {
        private sealed class RecordingClient : VisionHostClient {
            public int BoundCount;
            public int UnboundCount;
            public VisionStream[] Wanted = { VisionStream.Hands };

            protected override VisionStream[] Streams {
                get {
                    return Wanted;
                }
            }

            protected override void OnBound(VisionHost visionHost) {
                BoundCount = BoundCount + 1;
            }

            protected override void OnUnbound(VisionHost visionHost) {
                UnboundCount = UnboundCount + 1;
            }
        }

        private GameObject bootstrapObject;
        private GameObject clientObject;

        [SetUp]
        public void SetUp() {
            // an earlier test (in this or another assembly) may have left a bootstrap that survives scene loads: every test here starts from nothing
            if (VisionBootstrap.Instance != null) {
                Object.DestroyImmediate(VisionBootstrap.Instance.gameObject);
            }
            foreach (VisionHostClient client in Object.FindObjectsByType<VisionHostClient>(FindObjectsSortMode.None)) {
                Object.DestroyImmediate(client.gameObject);
            }
            Assert.IsNull(VisionHostRegistry.Current, "a clean start");
        }

        [TearDown]
        public void TearDown() {
            if (clientObject != null) {
                Object.DestroyImmediate(clientObject);
            }
            if (bootstrapObject != null) {
                Object.DestroyImmediate(bootstrapObject);
            }
            Assert.IsNull(VisionHostRegistry.Current, "nothing may stay registered after a test");
        }

        private static int FreePort() {
            UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            probe.Close();
            return port;
        }

        private static GameObject InactiveBootstrap(out VisionBootstrap bootstrap) {
            GameObject go = new GameObject("VisionBootstrap under test");
            go.SetActive(false);
            bootstrap = go.AddComponent<VisionBootstrap>();
            VisionSettings settings = bootstrap.Settings;
            settings.AutoStartSenders = false;
            settings.HandsPort = FreePort();
            settings.FacePort = FreePort();
            settings.CutoutPort = FreePort();
            return go;
        }

        [UnityTest]
        public IEnumerator TheBootstrapCreatesTheHostRegistersItAndAddsThePanel() {
            VisionBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            Assert.IsNull(VisionHostRegistry.Current, "nothing is registered before the object is active");
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.IsNotNull(bootstrap.Host);
            Assert.AreSame(bootstrap.Host, VisionHostRegistry.Current);
            Assert.AreSame(bootstrap, VisionBootstrap.Instance);
            Assert.IsNotNull(bootstrapObject.GetComponent<VisionControlPanel>(), "the F6 panel comes with it");
        }

        [UnityTest]
        public IEnumerator ThePanelIsBoundToTheHostsSettingsSoItOpensWhenAsked() {
            VisionBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrap.Settings.ShowPanelOnStart = true;
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.IsTrue(bootstrapObject.GetComponent<VisionControlPanel>().Visible, "bound: it reads Show Panel On Start from the host's settings");
        }

        [UnityTest]
        public IEnumerator TheBootstrapTicksTheHostSoTheReceiversAreListening() {
            VisionBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrap.Settings.HandsMode = VisionSenderMode.Auto;
            bootstrapObject.SetActive(true);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.IsNotNull(bootstrap.Host.Hands, "the host was ticked: the hands receiver exists");
            Assert.IsTrue(bootstrap.Host.Hands.IsRunning);
        }

        [UnityTest]
        public IEnumerator DestroyingTheBootstrapUnregistersTheHostAndFreesItsPorts() {
            VisionBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrap.Settings.HandsMode = VisionSenderMode.Auto;
            int port = bootstrap.Settings.HandsPort;
            bootstrapObject.SetActive(true);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.IsNotNull(bootstrap.Host.Hands, "the receiver holds the port while the bootstrap lives");
            Object.Destroy(bootstrapObject);
            yield return null;
            Assert.IsNull(VisionHostRegistry.Current);
            Assert.IsNull(VisionBootstrap.Instance);
            UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            probe.Close();
            bootstrapObject = null;
        }

        [UnityTest]
        public IEnumerator ASecondBootstrapIsRefusedAndTheFirstKeepsTheHost() {
            VisionBootstrap first;
            bootstrapObject = InactiveBootstrap(out first);
            bootstrapObject.SetActive(true);
            yield return null;
            VisionHost firstHost = first.Host;
            VisionBootstrap second;
            GameObject secondObject = InactiveBootstrap(out second);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("A VisionBootstrap already exists"));
            secondObject.SetActive(true);
            yield return null;
            Assert.IsTrue(second == null, "the second component is removed");
            Assert.AreSame(firstHost, VisionHostRegistry.Current);
            Assert.AreSame(first, VisionBootstrap.Instance);
            Object.Destroy(secondObject);
        }

        [UnityTest]
        public IEnumerator AClientThatWakesBeforeTheBootstrapBindsWhenTheHostAppears() {
            clientObject = new GameObject("client");
            RecordingClient client = clientObject.AddComponent<RecordingClient>();
            yield return null;
            Assert.AreEqual(0, client.BoundCount, "no host yet");
            VisionBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.AreEqual(1, client.BoundCount);
            Assert.IsTrue(bootstrap.Host.HasDemand(VisionStream.Hands), "the client asked for its stream");
            Assert.IsFalse(bootstrap.Host.HasDemand(VisionStream.Face));
        }

        [UnityTest]
        public IEnumerator DisablingTheClientLetsGoOfItsStreamsAndEnablingItAsksAgain() {
            VisionBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            clientObject = new GameObject("client");
            RecordingClient client = clientObject.AddComponent<RecordingClient>();
            client.Wanted = new VisionStream[] { VisionStream.Hands, VisionStream.Face };
            yield return null;
            Assert.IsTrue(bootstrap.Host.HasDemand(VisionStream.Hands));
            Assert.IsTrue(bootstrap.Host.HasDemand(VisionStream.Face));
            client.enabled = false;
            Assert.IsFalse(bootstrap.Host.HasDemand(VisionStream.Hands), "released on disable");
            Assert.IsFalse(bootstrap.Host.HasDemand(VisionStream.Face));
            Assert.AreEqual(1, client.UnboundCount);
            client.enabled = true;
            yield return null;
            Assert.AreEqual(2, client.BoundCount);
            Assert.IsTrue(bootstrap.Host.HasDemand(VisionStream.Hands));
        }

        [UnityTest]
        public IEnumerator AClientLetsGoWhenTheHostGoesAwayAndBindsToTheNextOne() {
            VisionBootstrap first;
            bootstrapObject = InactiveBootstrap(out first);
            bootstrapObject.SetActive(true);
            yield return null;
            clientObject = new GameObject("client");
            RecordingClient client = clientObject.AddComponent<RecordingClient>();
            yield return null;
            Assert.AreEqual(1, client.BoundCount);
            Object.Destroy(bootstrapObject);
            yield return null;
            yield return null;
            Assert.AreEqual(1, client.UnboundCount, "the host is gone: the client noticed");
            VisionBootstrap next;
            bootstrapObject = InactiveBootstrap(out next);
            bootstrapObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.AreEqual(2, client.BoundCount, "and bound to the new host");
            Assert.IsTrue(next.Host.HasDemand(VisionStream.Hands));
        }

        [Test]
        public void TheRegistryKeepsTheNewestHostAndOnlyForgetsTheOneItIsToldAbout() {
            VisionSettings settings = new VisionSettings();
            settings.AutoStartSenders = false;
            settings.HandsMode = VisionSenderMode.Off;
            settings.FaceMode = VisionSenderMode.Off;
            settings.CutoutMode = VisionSenderMode.Off;
            VisionHost a = new VisionHost(new UnityDebugLogService(), settings);
            VisionHost b = new VisionHost(new UnityDebugLogService(), settings);
            try {
                VisionHostRegistry.Register(a);
                VisionHostRegistry.Register(b);
                Assert.AreSame(b, VisionHostRegistry.Current);
                VisionHostRegistry.Unregister(a);
                Assert.AreSame(b, VisionHostRegistry.Current, "forgetting a stale host does not drop the current one");
                VisionHostRegistry.Unregister(b);
                Assert.IsNull(VisionHostRegistry.Current);
            } finally {
                VisionHostRegistry.Unregister(a);
                VisionHostRegistry.Unregister(b);
                a.Dispose();
                b.Dispose();
            }
        }
    }
}
