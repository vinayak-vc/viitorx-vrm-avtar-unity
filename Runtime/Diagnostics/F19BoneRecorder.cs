using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// F-19 §13 — samples the FINAL rendered humanoid bones and writes one JSON record per rendered
    /// frame.
    ///
    /// DIAGNOSTIC ONLY. Nothing in the product references this type; it does nothing unless a
    /// GameObject carrying it is created explicitly (F-19 does that from the MCP bridge for the
    /// duration of a capture and destroys it afterwards). It reads transforms and never writes one,
    /// so it cannot alter the pose it is measuring.
    ///
    /// WHY <see cref="Application.onBeforeRender"/> AND NOT LateUpdate: the retarget chain ends with
    /// UniVRM's Vrm10Runtime.Process(), which the driver invokes from its own update. Sampling in a
    /// LateUpdate would race that call and could capture the pose one stage early — silently
    /// measuring the wrong skeleton. onBeforeRender fires after every Update/LateUpdate and
    /// immediately before rendering, so what is recorded is exactly what is drawn.
    ///
    /// Self-terminating by construction (a standing requirement in this project after editor-update
    /// samplers were left running): it unsubscribes in OnDisable, closes the file in OnDestroy, and
    /// destroys itself after <see cref="maxSeconds"/> if that is set.
    ///
    /// Local rotation, world rotation, world position and lossyScale are all recorded. Angular
    /// velocity, maximum angular step, joint range, bone length and end-effector position are
    /// DERIVED from these by f19_analyze_bones.py rather than stored, so a stored summary can never
    /// disagree with the samples it came from.
    /// </summary>
    public sealed class F19BoneRecorder : MonoBehaviour {
        /// <summary>Label written into every record; set from outside to segment a protocol.</summary>
        public static string Block = "";

        public string outputPath = "";
        public float maxSeconds = 0f;
        public int flushEvery = 120;

        /// <summary>
        /// Optional file the protocol prompter writes the current cue into. Polled rather than
        /// pushed over the editor bridge so that labelling a block costs no round-trip and cannot
        /// stall the render loop between blocks.
        /// </summary>
        public string blockFile = "";
        public int blockPollFrames = 15;

        private static readonly HumanBodyBones[] Tracked = new HumanBodyBones[] {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        private Animator animator;
        private readonly List<KeyValuePair<string, Transform>> bones = new List<KeyValuePair<string, Transform>>();
        private StreamWriter writer;
        private int written;
        private float started;
        private object driver;
        private System.Reflection.MethodInfo gateStates;
        private bool subscribed;
        // F-20A: the UDP provider, found the same reflective way, so the rendered avatar and the
        // transport state are recorded on the SAME frame. F-19 showed that avatar-level telemetry
        // alone cannot tell a live stream from a frozen one.
        private object provider;
        private System.Reflection.PropertyInfo providerState;
        private System.Reflection.MethodInfo providerHealth;

        public int Written {
            get {
                return written;
            }
        }

        public string ResolvedPath {
            get {
                return outputPath;
            }
        }

        private void OnEnable() {
            Application.onBeforeRender += Sample;
            subscribed = true;
        }

        private void OnDisable() {
            if (subscribed) {
                Application.onBeforeRender -= Sample;
                subscribed = false;
            }
        }

        private void Start() {
            started = Time.realtimeSinceStartup;
            if (string.IsNullOrEmpty(outputPath)) {
                outputPath = Path.Combine(Application.dataPath, "f19_bones.jsonl");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            writer = new StreamWriter(outputPath, false);
            // The driver is looked up REFLECTIVELY so this assembly needs no reference to the
            // retargeting assembly; a diagnostic must not create a real dependency edge.
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length && (driver == null || provider == null); i++) {
                if (all[i] == null) {
                    continue;
                }
                System.Reflection.FieldInfo[] fields = all[i].GetType().GetFields(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                for (int f = 0; f < fields.Length; f++) {
                    if (fields[f].FieldType.Name == "KalidokitControlRigDriver") {
                        driver = fields[f].GetValue(all[i]);
                    }
                    object candidate = fields[f].GetValue(all[i]);
                    if (provider == null && candidate != null
                        && candidate.GetType().Name == "OakDUdpPoseProvider") {
                        provider = candidate;
                        providerState = provider.GetType().GetProperty("State");
                        providerHealth = provider.GetType().GetMethod("GetStreamHealth");
                    }
                }
            }
            if (driver != null) {
                gateStates = driver.GetType().GetMethod("GetGateStates");
            }
            Debug.Log("[F19] recorder -> " + outputPath + "  driver=" + (driver != null)
                      + " provider=" + (provider != null));
        }

        private void ResolveAnimator() {
            Animator[] found = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++) {
                if (found[i] != null && found[i].isHuman && found[i].gameObject.activeInHierarchy) {
                    animator = found[i];
                    break;
                }
            }
            bones.Clear();
            if (animator == null) {
                return;
            }
            for (int i = 0; i < Tracked.Length; i++) {
                Transform t = animator.GetBoneTransform(Tracked[i]);
                if (t != null) {
                    bones.Add(new KeyValuePair<string, Transform>(Tracked[i].ToString(), t));
                }
            }
            // The avatar is loaded at runtime and CAN be swapped mid-session, so the rig identity is
            // stamped on every record. F-19 found cross-run bone-length comparisons invalid without it.
            Debug.Log("[F19] bound rig '" + animator.gameObject.name + "' with " + bones.Count + " bones");
        }

        private void Sample() {
            if (writer == null) {
                return;
            }
            if (maxSeconds > 0f && Time.realtimeSinceStartup - started > maxSeconds) {
                Object.Destroy(gameObject);
                return;
            }
            if (animator == null || bones.Count == 0 || animator.gameObject == null) {
                ResolveAnimator();
                if (animator == null) {
                    return;
                }
            }

            if (!string.IsNullOrEmpty(blockFile) && blockPollFrames > 0 &&
                (Time.frameCount % blockPollFrames) == 0) {
                try {
                    if (File.Exists(blockFile)) {
                        Block = File.ReadAllText(blockFile).Trim();
                    }
                } catch {
                    // a half-written file is not worth a frame; the next poll picks it up
                }
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(2048);
            sb.Append("{\"f\":").Append(Time.frameCount);
            sb.Append(",\"t\":").Append(Time.realtimeSinceStartupAsDouble.ToString("F4", CultureInfo.InvariantCulture));
            sb.Append(",\"dt\":").Append(Time.unscaledDeltaTime.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append(",\"rig\":\"").Append(animator.gameObject.name).Append("\"");
            sb.Append(",\"block\":\"").Append(Block).Append("\"");
            sb.Append(",\"srcValid\":").Append(driver != null ? "true" : "false");
            if (providerState != null) {
                try {
                    sb.Append(",\"track\":\"").Append(providerState.GetValue(provider)).Append("\"");
                } catch {
                    providerState = null;
                }
            }
            if (providerHealth != null && (Time.frameCount % 15) == 0) {
                try {
                    object[] hv = new object[12];
                    hv[0] = null; hv[1] = null;
                    var ps = providerHealth.GetParameters();
                    for (int i = 0; i < ps.Length; i++) {
                        System.Type et = ps[i].ParameterType.GetElementType();
                        hv[i] = et == typeof(string) ? null : System.Activator.CreateInstance(et);
                    }
                    providerHealth.Invoke(provider, hv);
                    sb.Append(",\"h\":{\"sid\":\"").Append(hv[1])
                      .Append("\",\"accSeq\":").Append(hv[2])
                      .Append(",\"rxSeq\":").Append(hv[3])
                      .Append(",\"sinceAcc\":").Append(System.Convert.ToDouble(hv[4]).ToString("F3", CultureInfo.InvariantCulture))
                      .Append(",\"trans\":").Append(hv[5])
                      .Append(",\"newSess\":").Append(hv[6])
                      .Append(",\"oldSess\":").Append(hv[7])
                      .Append(",\"ooo\":").Append(hv[8])
                      .Append(",\"dup\":").Append(hv[9])
                      .Append(",\"depth\":").Append(hv[10])
                      .Append(",\"recon\":").Append(hv[11]).Append("}");
                } catch {
                    providerHealth = null;
                }
            }
            if (gateStates != null) {
                // GetGateStates(out lArm, rArm, lLeg, rLeg, lArmHeld, rArmHeld, lLegHeld, rLegHeld)
                // — eight out-parameters: the first four are the VALID/HELD state per limb, the last
                // four the consecutive held-frame counts. Recording both means a hold can be seen
                // AND its duration measured, which is what P0's LimbGate evidence needs.
                object[] args = new object[] { 0, 0, 0, 0, 0, 0, 0, 0 };
                try {
                    gateStates.Invoke(driver, args);
                    sb.Append(",\"gates\":[");
                    for (int g = 0; g < args.Length; g++) {
                        if (g > 0) {
                            sb.Append(",");
                        }
                        sb.Append(args[g]);
                    }
                    sb.Append("]");
                } catch {
                    gateStates = null;
                }
            }
            sb.Append(",\"root\":");
            Append3(sb, animator.transform.position);
            sb.Append(",\"b\":{");
            for (int i = 0; i < bones.Count; i++) {
                Transform t = bones[i].Value;
                if (t == null) {
                    continue;
                }
                if (i > 0) {
                    sb.Append(",");
                }
                sb.Append("\"").Append(bones[i].Key).Append("\":{\"p\":");
                Append3(sb, t.position);
                sb.Append(",\"w\":");
                Append4(sb, t.rotation);
                sb.Append(",\"l\":");
                Append4(sb, t.localRotation);
                sb.Append(",\"s\":");
                Append3(sb, t.lossyScale);
                sb.Append("}");
            }
            sb.Append("}}");
            writer.WriteLine(sb.ToString());
            written++;
            if (flushEvery > 0 && (written % flushEvery) == 0) {
                writer.Flush();
            }
        }

        private static void Append3(System.Text.StringBuilder sb, Vector3 v) {
            sb.Append("[").Append(v.x.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",").Append(v.y.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",").Append(v.z.ToString("F6", CultureInfo.InvariantCulture)).Append("]");
        }

        private static void Append4(System.Text.StringBuilder sb, Quaternion q) {
            sb.Append("[").Append(q.x.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",").Append(q.y.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",").Append(q.z.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",").Append(q.w.ToString("F6", CultureInfo.InvariantCulture)).Append("]");
        }

        private void OnDestroy() {
            if (subscribed) {
                Application.onBeforeRender -= Sample;
                subscribed = false;
            }
            if (writer != null) {
                writer.Flush();
                writer.Close();
                writer = null;
                Debug.Log("[F19] recorder closed after " + written + " records -> " + outputPath);
            }
        }
    }
}
