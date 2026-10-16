using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

using VirtualMirror.App;
using VirtualMirror.Body;
using VirtualMirror.Core;
using VirtualMirror.IO;

using Object = UnityEngine.Object;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The application's own avatar path (<see cref="AppBootstrap"/>, scene "Mirror") and the package's <see cref="BodyBootstrap"/>, fed the SAME body-wire datagrams
    /// over UDP with the same avatar, and the bones they leave compared. <c>BodyAvatarDriver</c> is a copy of the application's Kalidokit path; this is the measurement that says
    /// whether the copy still behaves like the original. The datagrams arrive on a wall clock, so two runs of one pipeline differ a little: the package is run twice
    /// and the application is judged against THAT spread, never against zero. Each run prints its numbers (search the log for "equivalence").
    /// The application's settings file is moved aside for the test and put back, so nothing of the user's is read or changed.
    /// </summary>
    public sealed class AppBootstrapWireEquivalencePlayTests {
        private struct Phase {
            public string Name;
            public bool ArmsOut;
            public int Left;
            public int Right;
            public float Seconds;

            public Phase(string name, bool armsOut, int left, int right, float seconds) {
                Name = name;
                ArmsOut = armsOut;
                Left = left;
                Right = right;
                Seconds = seconds;
            }
        }

        private static readonly Phase[] Phases = {
            new Phase("arms down, hands open", false, 1, 1, 2.5f),
            new Phase("arms out", true, 1, 1, 2.5f),
            new Phase("left fist", true, 2, 1, 2f),
            new Phase("right fist", true, 1, 2, 2f),
            new Phase("arms down again", false, 1, 1, 2.5f)
        };

        private static readonly HumanBodyBones[] Bones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftThumbProximal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightThumbProximal
        };

        // per phase, the bones' local rotations 0.4 s after the phase began (the transition) and at its end (the settled pose)
        private sealed class Recording {
            public readonly List<Quaternion[]> Mid = new List<Quaternion[]>();
            public readonly List<Quaternion[]> End = new List<Quaternion[]>();
            public readonly List<string> World = new List<string>();
        }

        private static string AvatarPath([CallerFilePath] string ownPath = "") {
            string game = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ownPath)));
            return Path.GetFullPath(Path.Combine(game, "Packages", "com.viitorx.avtar.tracking.body", "Tests", "Runtime", "Data", "VirtualMirrorRigTemplate.vrm.bytes"));
        }

        private static int FreePort() {
            UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            probe.Close();
            return port;
        }

        private static void SetField(object target, string name, object value) {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, target.GetType().Name + " has a field '" + name + "'");
            field.SetValue(target, value);
        }

        private static Quaternion[] Snapshot(Animator animator) {
            Quaternion[] rotations = new Quaternion[Bones.Length];
            for (int i = 0; i < Bones.Length; i++) {
                Transform bone = animator.GetBoneTransform(Bones[i]);
                rotations[i] = bone != null ? bone.localRotation : Quaternion.identity;
            }
            return rotations;
        }

        private static IEnumerator Play(BodyWireSender wire, Func<Animator> animator, Recording recording) {
            foreach (Phase phase in Phases) {
                wire.ArmsOut = phase.ArmsOut;
                wire.LeftHand = phase.Left;
                wire.RightHand = phase.Right;
                float start = Time.realtimeSinceStartup;
                bool tookMid = false;
                while (Time.realtimeSinceStartup - start < phase.Seconds) {
                    wire.Pump();
                    yield return null;
                    if (!tookMid && Time.realtimeSinceStartup - start >= 0.4f) {
                        recording.Mid.Add(Snapshot(animator()));
                        tookMid = true;
                    }
                }
                recording.End.Add(Snapshot(animator()));
                Animator a = animator();
                Vector3 lDir = (a.GetBoneTransform(HumanBodyBones.LeftLowerArm).position - a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
                Vector3 rDir = (a.GetBoneTransform(HumanBodyBones.RightLowerArm).position - a.GetBoneTransform(HumanBodyBones.RightUpperArm).position).normalized;
                Vector3 lShoulder = a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                Vector3 lElbow = a.GetBoneTransform(HumanBodyBones.LeftLowerArm).position;
                string crossing = Mathf.Abs(lElbow.x) > Mathf.Abs(lShoulder.x) ? "arm goes OUT from the body" : "arm CROSSES the body";
                recording.World.Add(crossing + " | " + "avatar yaw " + a.transform.eulerAngles.y.ToString("F0") + " | left upper arm dir " + lDir.ToString("F2") + " | right upper arm dir " + rDir.ToString("F2")
                    + " | left shoulder x " + a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position.x.ToString("F2") + " | hips world rot " + a.GetBoneTransform(HumanBodyBones.Hips).rotation.eulerAngles.ToString("F0"));
            }
        }

        private static IEnumerator RunApplication(Recording recording) {
            PathProvider paths = new PathProvider();
            string settingsFile = paths.SettingsFilePath;
            string aside = settingsFile + ".equivalence-test-aside";
            bool hadSettings = File.Exists(settingsFile);
            if (hadSettings) {
                File.Copy(settingsFile, aside, true);
                File.Delete(settingsFile);
            }
            Scene original = SceneManager.GetActiveScene();
            Scene mirror = SceneManager.CreateScene("Mirror");
            SceneManager.SetActiveScene(mirror);
            GameObject avatarRoot = new GameObject("AvatarRoot");
            GameObject appObject = new GameObject("AppBootstrap under test");
            appObject.SetActive(false);
            BodyWireSender wire = null;
            try {
                AppBootstrap app = appObject.AddComponent<AppBootstrap>();
                int port = FreePort();
                SetField(app, "autoStartSidecar", false);
                SetField(app, "oakUdpPort", port);
                SetField(app, "loadMirrorSceneOnStart", false);
                SetField(app, "surviveSceneLoads", false);
                SetField(app, "showDebugSkeleton", false);
                SetField(app, "cameraTiltDegrees", 6f);   // the value the Bootstrap scene ships with
                appObject.SetActive(true);
                yield return null;
                Assert.IsNotNull(app.AvatarSession, "the application built its avatar session in the Mirror scene");
                Awaitable<AvatarLoadResult> pending = app.AvatarSession.LoadFromPathAsync(AvatarPath(), true);
                while (!pending.GetAwaiter().IsCompleted) {
                    yield return null;
                }
                AvatarLoadResult result = pending.GetAwaiter().GetResult();
                Assert.IsTrue(result.IsSuccess, "the application loaded the avatar: " + result.Message);
                wire = new BodyWireSender(port);
                yield return Play(wire, delegate { return app.AvatarSession.Current.Animator; }, recording);
            } finally {
                if (wire != null) {
                    wire.Dispose();
                }
                Object.DestroyImmediate(appObject);
                Object.DestroyImmediate(avatarRoot);
                SceneManager.SetActiveScene(original);
                if (hadSettings) {
                    File.Copy(aside, settingsFile, true);
                    File.Delete(aside);
                } else if (File.Exists(settingsFile)) {
                    File.Delete(settingsFile);
                }
            }
            yield return SceneManager.UnloadSceneAsync(mirror);
        }

        private static IEnumerator RunPackage(Recording recording, float yaw) {
            GameObject bootstrapObject = new GameObject("BodyBootstrap under test");
            bootstrapObject.SetActive(false);
            BodyWireSender wire = null;
            try {
                BodyBootstrap bootstrap = bootstrapObject.AddComponent<BodyBootstrap>();
                bootstrap.Settings.AutoStartEngine = false;
                bootstrap.Settings.Port = FreePort();
                bootstrap.Settings.CameraTiltDegrees = 6f;
                bootstrap.Settings.AvatarYawDegrees = yaw;
                SetField(bootstrap, "loadAvatarOnStart", false);
                bootstrapObject.SetActive(true);
                yield return null;
                Awaitable<AvatarLoadResult> pending = bootstrap.LoadAvatarAsync(new AvatarLoadRequest(File.ReadAllBytes(AvatarPath()), "rig-template", true));
                while (!pending.GetAwaiter().IsCompleted) {
                    yield return null;
                }
                Assert.IsTrue(pending.GetAwaiter().GetResult().IsSuccess);
                wire = new BodyWireSender(bootstrap.Settings.Port);
                yield return Play(wire, delegate { return bootstrap.Avatar.Animator; }, recording);
            } finally {
                if (wire != null) {
                    wire.Dispose();
                }
                Object.DestroyImmediate(bootstrapObject);
            }
        }

        // the largest angle, over the bones, between two recordings of one moment
        private static float Worst(List<Quaternion[]> a, List<Quaternion[]> b, int phase, out string bone) {
            float worst = 0f;
            bone = string.Empty;
            for (int i = 0; i < Bones.Length; i++) {
                float angle = Quaternion.Angle(a[phase][i], b[phase][i]);
                if (angle > worst) {
                    worst = angle;
                    bone = Bones[i].ToString();
                }
            }
            return worst;
        }

        [UnityTest]
        public IEnumerator TheApplicationsAvatarPathAndThePackagesLeaveTheSameBonesWithinTheSpreadOfOneOfThem() {
            LogAssert.ignoreFailingMessages = true;
            Recording application = new Recording();
            Recording packageA = new Recording();
            Recording packageB = new Recording();
            Recording packageTurned = new Recording();
            yield return RunApplication(application);
            yield return RunPackage(packageA, 180f);
            yield return RunPackage(packageB, 180f);
            yield return RunPackage(packageTurned, 0f);
            LogAssert.ignoreFailingMessages = false;

            StringBuilder report = new StringBuilder();
            report.AppendLine("equivalence: degrees between recordings of the same moment, the worst bone (name)");
            report.AppendLine("equivalence:   phase | moment | application vs package (the like-for-like one) | package A vs package B (the spread of one pipeline) | application vs package with AvatarYawDegrees 0 (information: the arms cross)");
            float worstApplication = 0f;
            float worstSpread = 0f;
            for (int phase = 0; phase < Phases.Length; phase++) {
                for (int moment = 0; moment < 2; moment++) {
                    List<Quaternion[]> a = moment == 0 ? application.Mid : application.End;
                    List<Quaternion[]> pa = moment == 0 ? packageA.Mid : packageA.End;
                    List<Quaternion[]> pb = moment == 0 ? packageB.Mid : packageB.End;
                    List<Quaternion[]> pt = moment == 0 ? packageTurned.Mid : packageTurned.End;
                    string b1;
                    string b2;
                    string b3;
                    float appVsA = Worst(a, pa, phase, out b1);
                    float spread = Worst(pa, pb, phase, out b2);
                    float appVsB = Worst(a, pt, phase, out b3);
                    report.AppendLine("equivalence:   " + Phases[phase].Name + " | " + (moment == 0 ? "0.4 s in " : "settled   ") + " | " + appVsA.ToString("F2") + " (" + b1 + ") | "
                        + spread.ToString("F2") + " (" + b2 + ") | " + appVsB.ToString("F2") + " (" + b3 + ")");
                    if (moment == 1) {
                        worstApplication = Mathf.Max(worstApplication, appVsA);
                        worstSpread = Mathf.Max(worstSpread, spread);
                    }
                }
            }
            report.AppendLine("equivalence: SETTLED poses, worst application-vs-package " + worstApplication.ToString("F2") + " degrees, worst package-vs-package " + worstSpread.ToString("F2") + " degrees");
            string[] names = { "application", "package A (default yaw 180)", "package B (default yaw 180)", "package with AvatarYawDegrees 0" };
            Recording[] all = { application, packageA, packageB, packageTurned };
            for (int r = 0; r < all.Length; r++) {
                for (int phase = 0; phase < Phases.Length; phase++) {
                    Quaternion[] end = all[r].End[phase];
                    report.AppendLine("equivalence: world " + names[r] + " | " + Phases[phase].Name + " | " + all[r].World[phase]);
                    report.AppendLine("equivalence: raw " + names[r] + " | " + Phases[phase].Name + " | LeftUpperArm " + end[6].eulerAngles.ToString("F1") + " | RightUpperArm " + end[9].eulerAngles.ToString("F1") + " | LeftIndexProximal " + end[18].eulerAngles.ToString("F1"));
                }
            }
            Debug.Log(report.ToString());
            Assert.LessOrEqual(worstApplication, Mathf.Max(3f, worstSpread * 3f), "the application's avatar path and the package leave the same settled pose (see the 'equivalence' lines)");
        }
    }
}
