using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using VirtualMirror.Body;
using VirtualMirror.Tracking.OakD;

using Object = UnityEngine.Object;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The whole body path with the REAL compiled engine: <see cref="BodyBootstrap"/> starts <c>viitorx-tracking body</c> on a video file, the receiver goes
    /// Live, the avatar (a real VRM with its control rig) takes the person's pose, and the engine process is gone when the bootstrap is destroyed.
    /// Needs the engine, a clip with a person in it and the models, so it is skipped (Ignored) unless the environment names them:
    /// <c>VIITORX_TEST_ENGINE</c> (the engine folder), <c>VIITORX_TEST_CLIP</c> (a video file) and <c>VIITORX_MODELS_DIR</c> (the models, with the body model).
    /// </summary>
    public sealed class BodyBootstrapEnginePlayTests {
        private const string EngineProcessName = "viitorx-tracking";
        private const float TimeoutSeconds = 240f;

        private GameObject bootstrapObject;

        [SetUp]
        public void SetUp() {
            if (BodyBootstrap.Instance != null) {
                Object.DestroyImmediate(BodyBootstrap.Instance.gameObject);
            }
        }

        [TearDown]
        public void TearDown() {
            if (bootstrapObject != null) {
                Object.DestroyImmediate(bootstrapObject);
            }
        }

        private static string Required(string name) {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) {
                Assert.Ignore(name + " is not set: the compiled engine is not part of this run.");
            }
            return value;
        }

        private static void SetField(BodyBootstrap bootstrap, string name, object value) {
            FieldInfo field = typeof(BodyBootstrap).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "BodyBootstrap has a serialized field '" + name + "'");
            field.SetValue(bootstrap, value);
        }

        private static float ElevationDegrees(Vector3 direction) {
            return Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        [UnityTest]
        public IEnumerator AnEngineOfAnotherWireGenerationIsRefusedAndNoProcessIsStarted() {
            string engine = Required("VIITORX_TEST_ENGINE");
            string clip = Required("VIITORX_TEST_CLIP");
            int processesBefore = Process.GetProcessesByName(EngineProcessName).Length;
            int generation = EngineCompatibility.SupportedWireSpecMajor;
            EngineCompatibility.SupportedWireSpecMajor = generation + 1;
            try {
                bootstrapObject = new GameObject("BodyBootstrap with an engine of another generation");
                bootstrapObject.SetActive(false);
                BodyBootstrap bootstrap = bootstrapObject.AddComponent<BodyBootstrap>();
                bootstrap.Settings.EnginePath = engine;
                bootstrap.Settings.Source = clip;
                bootstrap.Settings.Port = BodyTestSupport.FreePort();
                SetField(bootstrap, "loadAvatarOnStart", false);
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Body: the engine was NOT started"));
                bootstrapObject.SetActive(true);
                yield return null;
                yield return new WaitForSecondsRealtime(1f);
                Assert.IsNull(bootstrap.Launcher, "no engine was started");
                Assert.AreEqual(processesBefore, Process.GetProcessesByName(EngineProcessName).Length);
                StringAssert.Contains("not compatible", bootstrap.StatusText);
            } finally {
                EngineCompatibility.SupportedWireSpecMajor = generation;
            }
        }

        [UnityTest]
        public IEnumerator TheEngineStreamsAPersonAndTheAvatarTakesThePose() {
            string engine = Required("VIITORX_TEST_ENGINE");
            string clip = Required("VIITORX_TEST_CLIP");
            Required("VIITORX_MODELS_DIR");
            int processesBefore = Process.GetProcessesByName(EngineProcessName).Length;

            bootstrapObject = new GameObject("BodyBootstrap with the engine");
            bootstrapObject.SetActive(false);
            BodyBootstrap bootstrap = bootstrapObject.AddComponent<BodyBootstrap>();
            bootstrap.Settings.EnginePath = engine;
            bootstrap.Settings.Source = clip;
            bootstrap.Settings.Port = BodyTestSupport.FreePort();
            SetField(bootstrap, "avatarFile", BodyTestSupport.AvatarPath());
            bootstrapObject.SetActive(true);

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while ((bootstrap.Provider == null || bootstrap.Provider.State != TrackingState.Live || bootstrap.Provider.ReceivedCount <= 20L || bootstrap.Avatar == null) && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
            Assert.IsNotNull(bootstrap.Launcher, "the bootstrap started the engine: " + bootstrap.StatusText);
            Assert.IsTrue(bootstrap.Launcher.IsOwnedProcessAlive, "and it is running: " + bootstrap.StatusText);
            Assert.AreEqual(processesBefore + 1, Process.GetProcessesByName(EngineProcessName).Length, "exactly one engine process more than before");
            Assert.IsNotNull(bootstrap.Avatar, "the avatar loaded: " + bootstrap.StatusText);
            StringAssert.Contains("(process running)", bootstrap.StatusText);
            StringAssert.DoesNotContain("engine: engine:", bootstrap.StatusText);
            StringAssert.DoesNotContain("starting", bootstrap.StatusText, "the status does not claim the engine is still starting once the stream is Live");
            Assert.IsTrue(bootstrap.Driver.IsBound, "and is bound to the driver");
            Assert.AreEqual(TrackingState.Live, bootstrap.Provider.State, "the body stream went Live: " + bootstrap.StatusText);
            Assert.Greater(bootstrap.Provider.ReceivedCount, 20L, "datagrams arrived from the engine");
            Assert.AreEqual(0L, bootstrap.Provider.ParseErrorCount, "and every one parsed");

            // the VRM starts in its T-pose (the upper arms level); a person with the arms down puts them tens of degrees below the horizontal
            Animator animator = bootstrap.Avatar.Animator;
            Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform rightElbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            float reached = 0f;
            float until = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < until) {
                reached = Mathf.Max(reached, Mathf.Abs(ElevationDegrees(leftElbow.position - leftShoulder.position)));
                reached = Mathf.Max(reached, Mathf.Abs(ElevationDegrees(rightElbow.position - rightShoulder.position)));
                yield return null;
            }
            UnityEngine.Debug.Log("[BodyBootstrapEnginePlayTests] " + bootstrap.StatusText.Replace('\n', '|') + " | an upper arm left the level by up to " + reached.ToString("F1") + " degrees");
            Assert.Greater(reached, 15f, "an upper arm left the level line it starts on (degrees): the avatar took the person's pose");

            Object.Destroy(bootstrapObject);
            bootstrapObject = null;
            float gone = Time.realtimeSinceStartup + 20f;
            while (Process.GetProcessesByName(EngineProcessName).Length > processesBefore && Time.realtimeSinceStartup < gone) {
                yield return null;
            }
            Assert.AreEqual(processesBefore, Process.GetProcessesByName(EngineProcessName).Length, "the engine process ended with the bootstrap");
        }
    }
}
