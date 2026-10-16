using System.Collections;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using VirtualMirror.Body;
using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Tracking;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="BodyAvatarDriver"/> with a real VRM and its control rig, fed by a provider of the test's own (no engine, no socket): which stages move which
    /// bones, what stops them, and what the driver says when it cannot drive. Play mode, because the VRM loads through UniVRM's runtime path.
    /// </summary>
    public sealed class BodyAvatarDriverPlayTests {
        private const float FrameSeconds = 1f / 30f;

        /// <summary>The waving pose of <see cref="FakeBodyTrackingProvider"/>, optionally with a measured hip that the test moves.</summary>
        private sealed class ScriptedProvider : IBodyTrackingProvider {
            private readonly FakeBodyTrackingProvider inner = new FakeBodyTrackingProvider();
            public bool HasHip;
            public Vector3 Hip;
            public int Ticks;

            public bool IsRunning {
                get {
                    return inner.IsRunning;
                }
            }

            public void StartTracking() {
                inner.StartTracking();
            }

            public void StopTracking() {
                inner.StopTracking();
            }

            public void Tick(float deltaSeconds) {
                inner.Tick(deltaSeconds);
                Ticks = Ticks + 1;
            }

            public bool TryGetLatestFrame(out PoseFrame frame) {
                inner.TryGetLatestFrame(out frame);
                if (HasHip) {
                    frame.SetRootPosition(Hip);
                } else {
                    frame.ClearRootPosition();
                }
                return true;
            }

            public void Dispose() {
                inner.Dispose();
            }
        }

        private readonly BodyTestSupport.RecordingLog log = new BodyTestSupport.RecordingLog();
        private GameObject parent;
        private BodyTestSupport.LoadedAvatar loaded;

        [SetUp]
        public void SetUp() {
            parent = new GameObject("body test parent");
            loaded = new BodyTestSupport.LoadedAvatar();
        }

        [TearDown]
        public void TearDown() {
            if (loaded != null && loaded.Instance != null) {
                loaded.Instance.Dispose();
            }
            if (parent != null) {
                Object.DestroyImmediate(parent);
            }
        }

        private Transform Bone(HumanBodyBones bone) {
            return loaded.Instance.Animator.GetBoneTransform(bone);
        }

        // Runs the driver for `frames` frames and returns the largest angle each of the two upper arms reached away from where it started.
        private IEnumerator Run(BodyAvatarDriver driver, int frames, float[] reached) {
            Transform left = Bone(HumanBodyBones.LeftUpperArm);
            Transform right = Bone(HumanBodyBones.RightUpperArm);
            Quaternion leftStart = left.localRotation;
            Quaternion rightStart = right.localRotation;
            reached[0] = 0f;
            reached[1] = 0f;
            for (int frame = 0; frame < frames; frame = frame + 1) {
                driver.Update(FrameSeconds);
                reached[0] = Mathf.Max(reached[0], BodyTestSupport.Degrees(leftStart, left.localRotation));
                reached[1] = Mathf.Max(reached[1], BodyTestSupport.Degrees(rightStart, right.localRotation));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TheDriverBindsAnAvatarLoadedWithItsControlRig() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), new ScriptedProvider());
            Assert.IsFalse(driver.IsBound, "nothing is bound before Bind");
            Assert.IsTrue(driver.Bind(loaded.Instance));
            Assert.IsTrue(driver.IsBound);
            driver.Unbind();
            Assert.IsFalse(driver.IsBound, "unbinding lets go of the avatar");
        }

        [UnityTest]
        public IEnumerator AnAvatarLoadedWithoutTheControlRigIsRefusedAndTheReasonIsLogged() {
            yield return BodyTestSupport.LoadAvatar(log, false, parent.transform, loaded);
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), new ScriptedProvider());
            Assert.IsFalse(driver.Bind(loaded.Instance));
            Assert.IsFalse(driver.IsBound);
            StringAssert.Contains("control rig", log.All());
            StringAssert.Contains("GenerateControlRig", log.All(), "the log names what to change");
        }

        [Test]
        public void NothingToBindIsRefusedWithoutThrowing() {
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), new ScriptedProvider());
            Assert.IsFalse(driver.Bind(null));
            Assert.IsFalse(driver.IsBound);
            driver.Update(FrameSeconds);
            driver.Recalibrate();
            driver.Unbind();
        }

        [UnityTest]
        public IEnumerator TheWavingPoseMovesBothUpperArms() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            float[] reached = new float[2];
            yield return Run(driver, 90, reached);
            Assert.Greater(reached[0], 10f, "the left upper arm followed the pose (degrees from where it started)");
            Assert.Greater(reached[1], 10f, "the right upper arm followed the pose");
            Assert.AreEqual(90, provider.Ticks, "the driver ticks its provider once per frame");
        }

        [UnityTest]
        public IEnumerator TheArmsMoveWithTheHumanizedSkeletonOffToo() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            BodySettings settings = new BodySettings();
            settings.UseHumanizedSkeleton = false;
            BodyAvatarDriver driver = new BodyAvatarDriver(log, settings, provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            float[] reached = new float[2];
            yield return Run(driver, 90, reached);
            Assert.Greater(reached[0], 10f);
            Assert.Greater(reached[1], 10f);
        }

        [UnityTest]
        public IEnumerator AfterUnbindingNoBoneMovesAnyMore() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            float[] reached = new float[2];
            yield return Run(driver, 40, reached);
            driver.Unbind();
            Transform left = Bone(HumanBodyBones.LeftUpperArm);
            Quaternion atUnbind = left.localRotation;
            yield return Run(driver, 40, reached);
            Assert.AreEqual(0f, reached[0], 0.001f, "an unbound driver writes no bone");
            Assert.AreEqual(0f, BodyTestSupport.Degrees(atUnbind, left.localRotation), 0.001f);
        }

        [UnityTest]
        public IEnumerator RecalibratingKeepsTheAvatarBoundAndDriven() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            float[] reached = new float[2];
            yield return Run(driver, 20, reached);
            driver.Recalibrate();
            Assert.IsTrue(driver.IsBound);
            yield return Run(driver, 60, reached);
            Assert.Greater(reached[0], 5f, "still following after the neutral was taken again");
        }

        [UnityTest]
        public IEnumerator TheAvatarRootFollowsTheHipWhenPositionTrackingIsOn() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            provider.HasHip = true;
            provider.Hip = new Vector3(0f, 0.9f, 2.5f);
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            Vector3 start = loaded.Instance.Root.transform.position;
            float[] reached = new float[2];
            yield return Run(driver, 10, reached);
            provider.Hip = new Vector3(0.5f, 0.9f, 2.5f);
            yield return Run(driver, 60, reached);
            Vector3 moved = loaded.Instance.Root.transform.position - start;
            Assert.Greater(moved.x, 0.3f, "the hip moved 0.5 m sideways and the avatar followed (metres)");
            Assert.Less(moved.x, 0.55f, "and not further than the hip went (scale 1)");
            Assert.AreEqual(0f, moved.y, 0.02f, "the hip did not rise, so neither did the avatar (no jump to an absolute position)");
            Assert.AreEqual(0f, moved.z, 0.02f, "the hip did not come nearer, so neither did the avatar");
        }

        [UnityTest]
        public IEnumerator ThePositionScaleScalesTheFollow() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            provider.HasHip = true;
            provider.Hip = new Vector3(0f, 0.9f, 2.5f);
            BodySettings settings = new BodySettings();
            settings.PositionScale = 2f;
            BodyAvatarDriver driver = new BodyAvatarDriver(log, settings, provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            Vector3 start = loaded.Instance.Root.transform.position;
            float[] reached = new float[2];
            yield return Run(driver, 10, reached);
            provider.Hip = new Vector3(0.4f, 0.9f, 2.5f);
            yield return Run(driver, 90, reached);
            float moved = loaded.Instance.Root.transform.position.x - start.x;
            Assert.Greater(moved, 0.7f, "a hip move of 0.4 m moved the avatar about twice as far (metres)");
            Assert.Less(moved, 0.85f);
        }

        [UnityTest]
        public IEnumerator RecalibratingTakesTheCurrentHipAsTheNewNeutral() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            provider.HasHip = true;
            provider.Hip = new Vector3(0f, 0.9f, 2.5f);
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            Vector3 start = loaded.Instance.Root.transform.position;
            float[] reached = new float[2];
            yield return Run(driver, 10, reached);
            provider.Hip = new Vector3(0.5f, 0.9f, 2.5f);
            yield return Run(driver, 60, reached);
            Assert.Greater(loaded.Instance.Root.transform.position.x - start.x, 0.3f, "followed the hip");
            driver.Recalibrate();
            yield return Run(driver, 90, reached);
            Assert.AreEqual(0f, loaded.Instance.Root.transform.position.x - start.x, 0.05f, "the hip where it now stands is the new neutral: the avatar is back where it began");
        }

        [UnityTest]
        public IEnumerator TheAvatarRootStaysPutWhenPositionTrackingIsOff() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            provider.HasHip = true;
            provider.Hip = new Vector3(0f, 0.9f, 2.5f);
            BodySettings settings = new BodySettings();
            settings.TrackPosition = false;
            BodyAvatarDriver driver = new BodyAvatarDriver(log, settings, provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            Vector3 start = loaded.Instance.Root.transform.position;
            float[] reached = new float[2];
            yield return Run(driver, 10, reached);
            provider.Hip = new Vector3(0.5f, 0.9f, 2.5f);
            yield return Run(driver, 60, reached);
            Assert.AreEqual(0f, (loaded.Instance.Root.transform.position - start).magnitude, 1e-5f);
        }

        [UnityTest]
        public IEnumerator ABodyTooCloseToTheCameraDoesNotMoveTheAvatarRootAndTheLogSaysWhy() {
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            ScriptedProvider provider = new ScriptedProvider();
            provider.StartTracking();
            provider.HasHip = true;
            provider.Hip = new Vector3(0f, 0.9f, 0.5f);
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            Assert.IsTrue(driver.Bind(loaded.Instance));
            Vector3 start = loaded.Instance.Root.transform.position;
            float[] reached = new float[2];
            yield return Run(driver, 10, reached);
            provider.Hip = new Vector3(0.5f, 0.9f, 0.5f);
            yield return Run(driver, 30, reached);
            Assert.AreEqual(PoseAdmission.Outside, driver.VolumeGate.Admission, "0.5 m is nearer than the camera can measure");
            Assert.AreEqual(0f, (loaded.Instance.Root.transform.position - start).magnitude, 1e-5f, "no follow outside the operating volume");
            StringAssert.Contains("operating volume", log.All());
        }
    }
}
