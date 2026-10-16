using System;
using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using VirtualMirror.Body;
using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;

using Object = UnityEngine.Object;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The part of the body path that only runs with the REAL stream receiver: datagrams of the body wire (docs/WIRE.md section 6) sent over loopback UDP to a
    /// <see cref="BodyBootstrap"/> with a real VRM. They reach what the fake-provider driver tests cannot: the parser, the hands of the body stream and the
    /// fingers they drive, which side is which, the stale-stream failsafe that releases the avatar to its neutral pose, and the recovery from it. No engine, no camera.
    /// Camera space on the wire is X right, Y down, Z forward; the points are hip-relative metres.
    /// </summary>
    public sealed class BodyWirePlayTests {
        private const float SendIntervalSeconds = 0.033f;

        private readonly BodyTestSupport.RecordingLog log = new BodyTestSupport.RecordingLog();
        private GameObject bootstrapObject;
        private BodyWireSender sender;

        [SetUp]
        public void SetUp() {
            if (BodyBootstrap.Instance != null) {
                Object.DestroyImmediate(BodyBootstrap.Instance.gameObject);
            }
        }

        [TearDown]
        public void TearDown() {
            if (sender != null) {
                sender.Dispose();
                sender = null;
            }
            if (bootstrapObject != null) {
                Object.DestroyImmediate(bootstrapObject);
            }
        }

        // A bootstrap with the engine off, a free port and the test avatar loaded and bound; the test then plays the engine.
        private IEnumerator Start(Action<BodySettings> configure, BodyBootstrap[] holder) {
            bootstrapObject = new GameObject("BodyBootstrap on the wire");
            bootstrapObject.SetActive(false);
            BodyBootstrap bootstrap = bootstrapObject.AddComponent<BodyBootstrap>();
            bootstrap.Settings.AutoStartEngine = false;
            bootstrap.Settings.Port = BodyTestSupport.FreePort();
            FieldInfo load = typeof(BodyBootstrap).GetField("loadAvatarOnStart", BindingFlags.NonPublic | BindingFlags.Instance);
            load.SetValue(bootstrap, false);
            if (configure != null) {
                configure(bootstrap.Settings);
            }
            bootstrapObject.SetActive(true);
            yield return null;
            Awaitable<AvatarLoadResult> pending = bootstrap.LoadAvatarAsync(new AvatarLoadRequest(System.IO.File.ReadAllBytes(BodyTestSupport.AvatarPath()), "rig-template", true));
            while (!pending.GetAwaiter().IsCompleted) {
                yield return null;
            }
            AvatarLoadResult result = pending.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsTrue(bootstrap.Driver.IsBound);
            sender = new BodyWireSender(bootstrap.Settings.Port);
            holder[0] = bootstrap;
        }

        private static IEnumerator Send(BodyWireSender wire, float seconds) {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) {
                wire.Pump();
                yield return null;
            }
        }

        private static IEnumerator Wait(float seconds) {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) {
                yield return null;
            }
        }

        private static float Elevation(Animator animator, HumanBodyBones upper, HumanBodyBones lower) {
            Vector3 direction = (animator.GetBoneTransform(lower).position - animator.GetBoneTransform(upper).position).normalized;
            return Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static float LeftArm(BodyBootstrap b) {
            return Elevation(b.Avatar.Animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
        }

        private static float RightArm(BodyBootstrap b) {
            return Elevation(b.Avatar.Animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
        }

        [UnityTest]
        public IEnumerator ADatagramOfArmsAtTheSidesPutsTheAvatarsArmsDown() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(null, holder);
            BodyBootstrap bootstrap = holder[0];
            Assert.Less(Mathf.Abs(LeftArm(bootstrap)), 10f, "before any datagram the avatar is in its T-pose");
            sender.ArmsOut = false;
            yield return Send(sender, 2.5f);
            Assert.AreEqual(TrackingState.Live, bootstrap.Provider.State, bootstrap.StatusText);
            Assert.Greater(bootstrap.Provider.ReceivedCount, 30L, "the receiver parsed the datagrams");
            Assert.AreEqual(0L, bootstrap.Provider.ParseErrorCount);
            Assert.Less(LeftArm(bootstrap), -45f, "the left upper arm hangs down (degrees above the horizontal)");
            Assert.Less(RightArm(bootstrap), -45f, "the right upper arm hangs down");
        }

        [UnityTest]
        public IEnumerator TheArmsFollowWhenThePersonRaisesThemToTheSides() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(null, holder);
            BodyBootstrap bootstrap = holder[0];
            sender.ArmsOut = false;
            yield return Send(sender, 2f);
            Assert.Less(LeftArm(bootstrap), -45f);
            sender.ArmsOut = true;
            yield return Send(sender, 2.5f);
            Assert.Greater(LeftArm(bootstrap), -25f, "the left arm came up to the level");
            Assert.Greater(RightArm(bootstrap), -25f, "the right arm came up to the level");
        }

        // true when the elbow is further from the body's middle than the shoulder: the arm goes out, it does not cross the chest
        private static bool ArmGoesOut(Animator animator, HumanBodyBones upper, HumanBodyBones lower) {
            float shoulder = Mathf.Abs(animator.GetBoneTransform(upper).position.x);
            float elbow = Mathf.Abs(animator.GetBoneTransform(lower).position.x);
            return elbow > shoulder + 0.1f;
        }

        [UnityTest]
        public IEnumerator ArmsHeldOutToTheSidesGoOutFromTheBodyAndDoNotCrossIt() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(null, holder);
            BodyBootstrap bootstrap = holder[0];
            sender.ArmsOut = true;
            yield return Send(sender, 2.5f);
            Animator animator = bootstrap.Avatar.Animator;
            Assert.IsTrue(ArmGoesOut(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), "the left arm goes out (the avatar is turned to the model frame before it is bound)");
            Assert.IsTrue(ArmGoesOut(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), "and so does the right one");
        }

        [UnityTest]
        public IEnumerator WithoutTheTurnTheArmsCrossTheBodySoTheSettingMatters() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(delegate(BodySettings settings) {
                settings.AvatarYawDegrees = 0f;
            }, holder);
            BodyBootstrap bootstrap = holder[0];
            sender.ArmsOut = true;
            yield return Send(sender, 2.5f);
            Animator animator = bootstrap.Avatar.Animator;
            Assert.IsFalse(ArmGoesOut(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), "documented: an avatar left facing +Z is driven with the arms across the chest");
        }

        [UnityTest]
        public IEnumerator AFistOfTheLeftHandCurlsTheLeftFingersAndOnlyThose() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(null, holder);
            BodyBootstrap bootstrap = holder[0];
            Animator animator = bootstrap.Avatar.Animator;
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Assert.IsNotNull(left, "the test avatar has finger bones");
            sender.LeftHand = 1;
            sender.RightHand = 1;
            yield return Send(sender, 1.5f);
            Quaternion leftOpen = left.localRotation;
            Quaternion rightOpen = right.localRotation;
            sender.LeftHand = 2;
            yield return Send(sender, 2f);
            Assert.Greater(Quaternion.Angle(leftOpen, left.localRotation), 25f, "the left index finger closed (degrees)");
            Assert.Less(Quaternion.Angle(rightOpen, right.localRotation), 5f, "the right hand stayed open: the sides are not swapped");
            sender.LeftHand = 1;
            sender.RightHand = 2;
            yield return Send(sender, 2f);
            Assert.Less(Quaternion.Angle(leftOpen, left.localRotation), 8f, "the left hand opened again");
            Assert.Greater(Quaternion.Angle(rightOpen, right.localRotation), 25f, "and the right one closed");
        }

        [UnityTest]
        public IEnumerator WithHandsOffInTheSettingsNoFingerMoves() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(delegate(BodySettings settings) {
                settings.TrackHands = false;
            }, holder);
            BodyBootstrap bootstrap = holder[0];
            Transform left = bootstrap.Avatar.Animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            Quaternion rest = left.localRotation;
            sender.LeftHand = 2;
            sender.RightHand = 2;
            yield return Send(sender, 2.5f);
            Assert.AreEqual(TrackingState.Live, bootstrap.Provider.State);
            Assert.Less(Quaternion.Angle(rest, left.localRotation), 1f, "a fist on the wire left the finger alone");
            Assert.Less(LeftArm(bootstrap), -45f, "while the body was still followed");
        }

        [UnityTest]
        public IEnumerator AStreamThatStopsReleasesTheAvatarToItsNeutralPose() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(null, holder);
            BodyBootstrap bootstrap = holder[0];
            sender.ArmsOut = false;
            yield return Send(sender, 2.5f);
            Assert.Less(LeftArm(bootstrap), -45f, "the avatar followed the pose before the stream stopped");
            // no more datagrams: after 2 s the receiver reports StaleFailsafe and the driver eases the avatar to the T-pose (0.5 s time constant)
            yield return Wait(2.6f);
            Assert.AreEqual(TrackingState.StaleFailsafe, bootstrap.Provider.State, bootstrap.StatusText);
            yield return Wait(3f);
            Assert.Greater(LeftArm(bootstrap), -20f, "the left arm was released to the neutral pose, not held in the last human pose");
            Assert.Greater(RightArm(bootstrap), -20f);
        }

        [UnityTest]
        public IEnumerator AStreamThatComesBackIsFollowedAgainFromTheNeutralPose() {
            BodyBootstrap[] holder = new BodyBootstrap[1];
            yield return Start(null, holder);
            BodyBootstrap bootstrap = holder[0];
            sender.ArmsOut = false;
            yield return Send(sender, 2f);
            yield return Wait(5.5f);
            Assert.AreEqual(TrackingState.StaleFailsafe, bootstrap.Provider.State);
            Assert.Greater(LeftArm(bootstrap), -20f, "released");
            yield return Send(sender, 4f);
            Assert.AreEqual(TrackingState.Live, bootstrap.Provider.State, bootstrap.StatusText);
            Assert.Less(LeftArm(bootstrap), -45f, "the avatar follows the person again after the stream recovered");
        }

        private static IEnumerator Drive(BodyAvatarDriver driver, BodyWireSender wire, float seconds, bool send) {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) {
                if (send) {
                    wire.Pump();
                }
                driver.Update(Time.deltaTime);
                yield return null;
            }
        }

        private static int Count(string text, string part) {
            int count = 0;
            int at = text.IndexOf(part, StringComparison.Ordinal);
            while (at >= 0) {
                count = count + 1;
                at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
            }
            return count;
        }

        [UnityTest]
        public IEnumerator TheFailsafeAndTheRecoveryAreEachLoggedOncePerStall() {
            GameObject parent = new GameObject("parent of the avatar");
            BodyTestSupport.LoadedAvatar loaded = new BodyTestSupport.LoadedAvatar();
            yield return BodyTestSupport.LoadAvatar(log, true, parent.transform, loaded);
            int port = BodyTestSupport.FreePort();
            OakDUdpPoseProvider provider = new OakDUdpPoseProvider(log, new PoseSpaceConverter(false, true, false), port);
            provider.SetPoseInterpolation(40f);
            provider.StartTracking();
            BodyAvatarDriver driver = new BodyAvatarDriver(log, new BodySettings(), provider);
            BodyWireSender wire = new BodyWireSender(port);
            try {
                Assert.IsTrue(driver.Bind(loaded.Instance));
                // stream, stall past the 2 s failsafe, stream again, stall again, stream again: two stalls, two recoveries
                for (int stall = 0; stall < 2; stall = stall + 1) {
                    yield return Drive(driver, wire, 2f, true);
                    yield return Drive(driver, wire, 3.2f, false);
                }
                yield return Drive(driver, wire, 3f, true);
                string all = log.All();
                Assert.AreEqual(2, Count(all, "tracking stream is stale beyond the failsafe"), "each stall is announced once, not once per frame and not only the first");
                Assert.AreEqual(2, Count(all, "tracking stream recovered"), "each recovery is announced once");
            } finally {
                wire.Dispose();
                driver.Unbind();
                provider.Dispose();
                loaded.Instance.Dispose();
                Object.DestroyImmediate(parent);
            }
        }
    }
}
