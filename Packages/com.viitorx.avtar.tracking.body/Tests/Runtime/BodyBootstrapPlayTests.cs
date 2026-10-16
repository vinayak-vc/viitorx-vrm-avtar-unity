using System.Collections;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using VirtualMirror.Body;
using VirtualMirror.Core;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="BodyBootstrap"/> in Play mode (Awake, Start, LateUpdate and OnDestroy only run there). Every bootstrap here has the engine OFF or pointed at
    /// nothing, and a free port, so no process is started; the engine and the camera are covered by the integration test.
    /// </summary>
    public sealed class BodyBootstrapPlayTests {
        private GameObject bootstrapObject;

        [SetUp]
        public void SetUp() {
            // an earlier test (in this or another assembly) may have left a bootstrap that survives scene loads: every test here starts from nothing
            if (BodyBootstrap.Instance != null) {
                Object.DestroyImmediate(BodyBootstrap.Instance.gameObject);
            }
        }

        [TearDown]
        public void TearDown() {
            if (bootstrapObject != null) {
                Object.DestroyImmediate(bootstrapObject);
            }
            Assert.IsNull(BodyBootstrap.Instance, "nothing may stay registered after a test");
        }

        // The bootstrap's own serialized fields are private (the Inspector owns them); a test sets them the way a scene would.
        private static void SetField(BodyBootstrap bootstrap, string name, object value) {
            FieldInfo field = typeof(BodyBootstrap).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "BodyBootstrap has a serialized field '" + name + "'");
            field.SetValue(bootstrap, value);
        }

        private static GameObject InactiveBootstrap(out BodyBootstrap bootstrap) {
            GameObject go = new GameObject("BodyBootstrap under test");
            go.SetActive(false);
            bootstrap = go.AddComponent<BodyBootstrap>();
            bootstrap.Settings.AutoStartEngine = false;
            bootstrap.Settings.Port = BodyTestSupport.FreePort();
            SetField(bootstrap, "loadAvatarOnStart", false);
            return go;
        }

        private static IEnumerator Await(Awaitable<AvatarLoadResult> pending, AvatarLoadResult[] result) {
            while (!pending.GetAwaiter().IsCompleted) {
                yield return null;
            }
            result[0] = pending.GetAwaiter().GetResult();
        }

        private static AvatarLoadRequest TestAvatar() {
            return new AvatarLoadRequest(System.IO.File.ReadAllBytes(BodyTestSupport.AvatarPath()), "rig-template", true);
        }

        [UnityTest]
        public IEnumerator TheBootstrapListensOnItsPortAndBuildsTheProviderAndTheDriver() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.AreSame(bootstrap, BodyBootstrap.Instance);
            Assert.IsNotNull(bootstrap.Provider);
            Assert.IsTrue(bootstrap.Provider.IsRunning, "the body wire receiver is listening");
            Assert.IsNotNull(bootstrap.Driver);
            Assert.IsNull(bootstrap.Launcher, "the engine is off: no process");
            StringAssert.Contains("Auto Start Engine is off", bootstrap.StatusText);
            StringAssert.Contains("UDP " + bootstrap.Settings.Port, bootstrap.StatusText);
            UdpClient probe = null;
            try {
                probe = new UdpClient(bootstrap.Settings.Port);
                Assert.Fail("the port is held by the bootstrap's receiver while it lives");
            } catch (SocketException) {
                // expected: the receiver holds the port
            } finally {
                if (probe != null) {
                    probe.Close();
                }
            }
        }

        [UnityTest]
        public IEnumerator DestroyingTheBootstrapFreesThePortAndTheInstance() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            int port = bootstrap.Settings.Port;
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.IsTrue(bootstrap.Provider.IsRunning);
            Object.Destroy(bootstrapObject);
            yield return null;
            Assert.IsNull(BodyBootstrap.Instance);
            // the same wildcard bind the receiver makes: binding only 127.0.0.1 would succeed even while the receiver still held the port
            UdpClient probe = new UdpClient(port);
            probe.Close();
            bootstrapObject = null;
        }

        [UnityTest]
        public IEnumerator ASecondBootstrapIsRefusedAndTheFirstKeepsThePort() {
            BodyBootstrap first;
            bootstrapObject = InactiveBootstrap(out first);
            bootstrapObject.SetActive(true);
            yield return null;
            LogAssert.Expect(LogType.Warning, new Regex("A BodyBootstrap already exists"));
            GameObject secondObject = new GameObject("second BodyBootstrap");
            BodyBootstrap second = secondObject.AddComponent<BodyBootstrap>();
            yield return null;
            Assert.AreSame(first, BodyBootstrap.Instance, "the first one is still the instance");
            Assert.IsTrue(second == null, "the second component removed itself");
            Assert.IsTrue(first.Provider.IsRunning, "and the first still holds the port");
            Object.DestroyImmediate(secondObject);
        }

        [UnityTest]
        public IEnumerator AnEngineThatIsNotThereIsReportedInTheStatusAndNoProcessIsStarted() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrap.Settings.AutoStartEngine = true;
            bootstrap.Settings.EnginePath = "C:/no/such/engine/folder";
            LogAssert.Expect(LogType.Error, new Regex("Body: the engine was NOT started"));
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.IsNull(bootstrap.Launcher);
            StringAssert.Contains("engine: not found", bootstrap.StatusText);
            Assert.IsTrue(bootstrap.Provider.IsRunning, "the receiver still listens: a hand-started sender can feed it");
        }

        [UnityTest]
        public IEnumerator ASourceThatCannotBeUsedIsReportedAndNoProcessIsStarted() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrap.Settings.AutoStartEngine = true;
            bootstrap.Settings.Source = "   ";
            LogAssert.Expect(LogType.Error, new Regex("Body: the engine was NOT started"));
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.IsNull(bootstrap.Launcher);
            StringAssert.Contains("SOURCE ERROR", bootstrap.StatusText);
        }

        [UnityTest]
        public IEnumerator NoAvatarSetIsReportedAndNotThrown() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            SetField(bootstrap, "loadAvatarOnStart", true);
            LogAssert.Expect(LogType.Warning, new Regex("Body: no avatar is set"));
            bootstrapObject.SetActive(true);
            yield return null;
            Assert.IsNull(bootstrap.Avatar);
            StringAssert.Contains("none set", bootstrap.StatusText);
        }

        [UnityTest]
        public IEnumerator TheConfiguredAvatarFileIsLoadedUnderTheBootstrapAndDriven() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            SetField(bootstrap, "loadAvatarOnStart", true);
            SetField(bootstrap, "avatarFile", BodyTestSupport.AvatarPath());
            bootstrapObject.SetActive(true);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (bootstrap.Avatar == null && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
            Assert.IsNotNull(bootstrap.Avatar, "the avatar loaded: " + bootstrap.StatusText);
            Assert.IsTrue(bootstrap.Driver.IsBound, "and it is bound to the driver");
            Assert.AreSame(bootstrapObject.transform, bootstrap.Avatar.Root.transform.parent, "under the bootstrap when no Avatar Parent is set");
            StringAssert.Contains("avatar: VirtualMirrorRigTemplate.vrm.bytes", bootstrap.StatusText);
        }

        [UnityTest]
        public IEnumerator TheAvatarGoesUnderTheAvatarParentWhenOneIsSet() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            GameObject stage = new GameObject("stage");
            SetField(bootstrap, "avatarParent", stage.transform);
            bootstrapObject.SetActive(true);
            yield return null;
            AvatarLoadResult[] result = new AvatarLoadResult[1];
            yield return Await(bootstrap.LoadAvatarAsync(TestAvatar()), result);
            Assert.IsTrue(result[0].IsSuccess, result[0].Message);
            Assert.AreSame(stage.transform, bootstrap.Avatar.Root.transform.parent);
            Object.DestroyImmediate(stage);
        }

        [UnityTest]
        public IEnumerator ALaterAvatarReplacesTheEarlierOneWhichIsDestroyed() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            AvatarLoadResult[] result = new AvatarLoadResult[1];
            yield return Await(bootstrap.LoadAvatarAsync(TestAvatar()), result);
            Assert.IsTrue(result[0].IsSuccess, result[0].Message);
            GameObject first = bootstrap.Avatar.Root;
            yield return Await(bootstrap.LoadAvatarAsync(TestAvatar()), result);
            Assert.IsTrue(result[0].IsSuccess, result[0].Message);
            yield return null;
            Assert.IsTrue(first == null, "the first avatar was destroyed");
            Assert.AreNotSame(first, bootstrap.Avatar.Root);
            Assert.IsTrue(bootstrap.Driver.IsBound, "the driver follows the new one");
        }

        [UnityTest]
        public IEnumerator ABrokenAvatarFileIsReportedAndLeavesNoAvatar() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            AvatarLoadResult[] result = new AvatarLoadResult[1];
            // in the order they are logged: the loader reports the exception first, then the bootstrap says the load did not succeed
            LogAssert.Expect(LogType.Error, new Regex("Avatar load failed"));
            LogAssert.Expect(LogType.Warning, new Regex("Body: the avatar did not load"));
            yield return Await(bootstrap.LoadAvatarAsync(new AvatarLoadRequest(new byte[] { 1, 2, 3, 4 }, "not-a-vrm", true)), result);
            Assert.IsFalse(result[0].IsSuccess);
            Assert.IsNull(bootstrap.Avatar);
            StringAssert.Contains("avatar: " + result[0].Status, bootstrap.StatusText);
        }

        [UnityTest]
        public IEnumerator DestroyingTheBootstrapDestroysAnAvatarThatIsUnderAnotherParentToo() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            GameObject stage = new GameObject("stage");
            SetField(bootstrap, "avatarParent", stage.transform);
            bootstrapObject.SetActive(true);
            yield return null;
            AvatarLoadResult[] result = new AvatarLoadResult[1];
            yield return Await(bootstrap.LoadAvatarAsync(TestAvatar()), result);
            Assert.IsTrue(result[0].IsSuccess, result[0].Message);
            GameObject avatarRoot = bootstrap.Avatar.Root;
            Object.Destroy(bootstrapObject);
            yield return null;
            Assert.IsTrue(avatarRoot == null, "the avatar does not outlive the bootstrap that loaded it, whatever it hangs under");
            bootstrapObject = null;
            Object.DestroyImmediate(stage);
        }

        [UnityTest]
        public IEnumerator TheCameraTiltIsPublishedToTheProcessBeforeAnythingReadsIt() {
            float before = CameraMount.TiltDegrees;
            try {
                BodyBootstrap bootstrap;
                bootstrapObject = InactiveBootstrap(out bootstrap);
                bootstrap.Settings.CameraTiltDegrees = 12.5f;
                bootstrapObject.SetActive(true);
                yield return null;
                Assert.AreEqual(12.5f, CameraMount.TiltDegrees, 1e-6f);
            } finally {
                CameraMount.TiltDegrees = before;
            }
        }

        [UnityTest]
        public IEnumerator DestroyingTheBootstrapDestroysTheAvatar() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            AvatarLoadResult[] result = new AvatarLoadResult[1];
            yield return Await(bootstrap.LoadAvatarAsync(TestAvatar()), result);
            Assert.IsTrue(result[0].IsSuccess, result[0].Message);
            GameObject avatarRoot = bootstrap.Avatar.Root;
            Object.Destroy(bootstrapObject);
            yield return null;
            Assert.IsTrue(avatarRoot == null);
            bootstrapObject = null;
        }

        [UnityTest]
        public IEnumerator ALoadThatFinishesAfterTheBootstrapWasDestroyedLeavesNothingUnderAnotherParentEither() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            GameObject stage = new GameObject("stage that outlives the bootstrap");
            SetField(bootstrap, "avatarParent", stage.transform);
            bootstrapObject.SetActive(true);
            yield return null;
            int animatorsBefore = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Length;
            Awaitable<AvatarLoadResult> pending = bootstrap.LoadAvatarAsync(TestAvatar());
            Object.Destroy(bootstrapObject);
            bootstrapObject = null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!pending.GetAwaiter().IsCompleted && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
            yield return null;
            yield return null;
            Assert.AreEqual(animatorsBefore, Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Length, "the avatar that arrived late was disposed, not left under the stage");
            Assert.AreEqual(0, stage.transform.childCount);
            Object.DestroyImmediate(stage);
        }

        [UnityTest]
        public IEnumerator ALoadThatFinishesAfterTheBootstrapWasDestroyedLeavesNothingBehind() {
            BodyBootstrap bootstrap;
            bootstrapObject = InactiveBootstrap(out bootstrap);
            bootstrapObject.SetActive(true);
            yield return null;
            int animatorsBefore = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Length;
            Awaitable<AvatarLoadResult> pending = bootstrap.LoadAvatarAsync(TestAvatar());
            Object.Destroy(bootstrapObject);
            bootstrapObject = null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!pending.GetAwaiter().IsCompleted && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
            Assert.IsTrue(pending.GetAwaiter().IsCompleted, "the load ended");
            yield return null;
            Assert.AreEqual(animatorsBefore, Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Length, "no avatar was left in the scene");
            AvatarLoadResult late = null;
            try {
                late = pending.GetAwaiter().GetResult();
            } catch (System.OperationCanceledException) {
                // a cancelled load surfaces either as a failed result or as the cancellation: neither leaves an avatar
            }
            if (late != null && late.IsSuccess) {
                Assert.IsTrue(late.Instance.Root == null, "a late success was disposed");
            }
        }
    }
}
