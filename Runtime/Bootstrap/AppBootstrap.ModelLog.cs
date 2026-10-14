using System;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

using UniVRM10;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Camera;
using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Diagnostics;
using VirtualMirror.IK;
using VirtualMirror.IO;
using VirtualMirror.Rendering;
using VirtualMirror.Retargeting;
using VirtualMirror.Settings;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking;
using VirtualMirror.Tracking.Filtering;
using VirtualMirror.Tracking.MediaPipe;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Tracking.Sentis;
using VirtualMirror.UI;

namespace VirtualMirror.App {
    /// <summary>
    /// Diagnostics: the model_log.jsonl writer used by pipeline logging. Kept out of the main file because nothing in the production path depends on it.
    /// </summary>
    public sealed partial class AppBootstrap {
        // Pipeline logging (diagnostics): one model_log.jsonl line per applied Kalidokit-body frame — seq +
        // the resulting avatar bone orientations (hips facing, hands, forearms) — to diff against
        // recv_log.jsonl and localize where the retarget introduces jitter/spin (compare_logs.py).
        private void WriteModelLog() {
            if (boundAnimator == null || modelLogFailed) {
                return;
            }
            try {
                if (modelLog == null) {
                    string directory = ResolvedPipelineLogDir();
                    Directory.CreateDirectory(directory);
                    modelLog = new StreamWriter(Path.Combine(directory, "model_log.jsonl"), false);
                    modelLog.AutoFlush = true;
                }
                long seq = bodyProvider is OakDUdpPoseProvider ? ((OakDUdpPoseProvider)bodyProvider).LastSeq : -1;
                Transform hips = boundAnimator.GetBoneTransform(HumanBodyBones.Hips);
                Transform lh = boundAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rh = boundAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform ll = boundAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                Transform rl = boundAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                // DIAG-ONLY (P0 acceptance §12/§13): leg bones were never logged, so leg rotation behavior
                // (and the rotation-vs-squash question) could not be evidenced. Read-only.
                Transform lul = boundAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Transform rul = boundAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                Transform lll = boundAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                Transform rll = boundAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                // DIAG-ONLY (P0 acceptance §13): live bone LENGTHS. A rotation retarget must keep these
                // CONSTANT; logging them turns "does the avatar squash?" into a measurement, not an opinion.
                float femurL = (lul != null && lll != null) ? Vector3.Distance(lul.position, lll.position) : 0f;
                float shinL = (lll != null && boundAnimator.GetBoneTransform(HumanBodyBones.LeftFoot) != null)
                    ? Vector3.Distance(lll.position, boundAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position) : 0f;
                float upArmL = (boundAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm) != null && ll != null)
                    ? Vector3.Distance(boundAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position, ll.position) : 0f;
                float foreArmL = (ll != null && lh != null) ? Vector3.Distance(ll.position, lh.position) : 0f;
                // DIAG-ONLY (P1-4 visual validation): interior knee/elbow angles + leg forward vectors.
                // Euler angles wrap at 0/360, so frame-to-frame deltas off them are unreliable; these are
                // wrap-free and turn "did the avatar's knee invert?" into a measurement. Read-only.
                float lKneeAng = JointAngle(lul, lll, boundAnimator.GetBoneTransform(HumanBodyBones.LeftFoot));
                float rKneeAng = JointAngle(rul, rll, boundAnimator.GetBoneTransform(HumanBodyBones.RightFoot));
                float lElbowAng = JointAngle(boundAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm), ll, lh);
                float rElbowAng = JointAngle(boundAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm), rl, rh);
                // DIAG-ONLY (P0 acceptance §6): the live LimbGate states, so the log PROVES the Unity gate
                // held (1 = HELD, 0 = VALID) rather than inferring it from the Python-side hold.
                int gLA = 0, gRA = 0, gLL = 0, gRL = 0, hLA = 0, hRA = 0, hLL = 0, hRL = 0;
                if (kalidokitControlRig != null) {
                    kalidokitControlRig.GetGateStates(out gLA, out gRA, out gLL, out gRL,
                                                      out hLA, out hRA, out hLL, out hRL);
                }
                string line = "{\"seq\":" + seq
                    + ",\"hipsY\":" + LogF(hips != null ? hips.eulerAngles.y : 0f)
                    + ",\"hipsFwd\":" + LogV(hips != null ? hips.forward : Vector3.zero)
                    + ",\"lhand\":" + LogV(lh != null ? lh.eulerAngles : Vector3.zero)
                    + ",\"rhand\":" + LogV(rh != null ? rh.eulerAngles : Vector3.zero)
                    + ",\"llow\":" + LogV(ll != null ? ll.eulerAngles : Vector3.zero)
                    + ",\"rlow\":" + LogV(rl != null ? rl.eulerAngles : Vector3.zero)
                    + ",\"lhandF\":" + LogV(lh != null ? lh.forward : Vector3.zero)
                    + ",\"rhandF\":" + LogV(rh != null ? rh.forward : Vector3.zero)
                    + ",\"lupleg\":" + LogV(lul != null ? lul.eulerAngles : Vector3.zero)
                    + ",\"rupleg\":" + LogV(rul != null ? rul.eulerAngles : Vector3.zero)
                    + ",\"llowleg\":" + LogV(lll != null ? lll.eulerAngles : Vector3.zero)
                    + ",\"rlowleg\":" + LogV(rll != null ? rll.eulerAngles : Vector3.zero)
                    + ",\"gate\":{\"lArm\":" + gLA + ",\"rArm\":" + gRA + ",\"lLeg\":" + gLL + ",\"rLeg\":" + gRL
                    + ",\"hLArm\":" + hLA + ",\"hRArm\":" + hRA + ",\"hLLeg\":" + hLL + ",\"hRLeg\":" + hRL + "}"
                    + ",\"boneLen\":{\"femur\":" + LogF(femurL) + ",\"shin\":" + LogF(shinL)
                    + ",\"upArm\":" + LogF(upArmL) + ",\"foreArm\":" + LogF(foreArmL) + "}"
                    + ",\"tApply\":" + (System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"llowF\":" + LogV(ll != null ? ll.forward : Vector3.zero)
                    + ",\"rlowF\":" + LogV(rl != null ? rl.forward : Vector3.zero)
                    + ",\"kneeAng\":{\"l\":" + LogF(lKneeAng) + ",\"r\":" + LogF(rKneeAng) + "}"
                    + ",\"elbowAng\":{\"l\":" + LogF(lElbowAng) + ",\"r\":" + LogF(rElbowAng) + "}"
                    + ",\"luplegF\":" + LogV(lul != null ? lul.forward : Vector3.zero)
                    + ",\"ruplegF\":" + LogV(rul != null ? rul.forward : Vector3.zero)
                    + ",\"llowlegF\":" + LogV(lll != null ? lll.forward : Vector3.zero)
                    + ",\"rlowlegF\":" + LogV(rll != null ? rll.forward : Vector3.zero)
                    + "}";
                modelLog.WriteLine(line);
            } catch (Exception exception) {
                modelLogFailed = true;
                if (logService != null) {
                    logService.Log(LogLevel.Warning, "Pipeline log (model_log.jsonl) failed and is now off: " + exception.Message);
                }
                CloseModelLog();
            }
        }

        private void CloseModelLog() {
            if (modelLog == null) {
                return;
            }
            try {
                modelLog.Flush();
                modelLog.Dispose();
            } catch (Exception exception) {
                if (logService != null) {
                    logService.Log(LogLevel.Warning, "Pipeline log (model_log.jsonl) did not close cleanly: " + exception.Message);
                }
            }
            modelLog = null;
        }

        /// <summary>
        /// Where pipeline logs go. The serialized value is used while its drive exists; an empty value, or a path
        /// copied from another machine whose drive is missing here, falls back to a folder under the persistent
        /// data path instead of failing every run.
        /// </summary>
        private string ResolvedPipelineLogDir() {
            if (!string.IsNullOrWhiteSpace(pipelineLogDir)) {
                if (!Path.IsPathRooted(pipelineLogDir)) {
                    return pipelineLogDir;
                }
                string root = Path.GetPathRoot(pipelineLogDir);
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) {
                    return pipelineLogDir;
                }
            }
            return Path.Combine(Application.persistentDataPath, "pipeline_logs");
        }

        // DIAG-ONLY (P1-4 visual validation): interior angle a-b-c in degrees; 0 when a bone is absent.
        private static float JointAngle(Transform a, Transform b, Transform c) {
            if (a == null || b == null || c == null) {
                return 0f;
            }
            return Vector3.Angle(a.position - b.position, c.position - b.position);
        }

        private static string LogF(float v) {
            return v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string LogV(Vector3 v) {
            return "[" + LogF(v.x) + "," + LogF(v.y) + "," + LogF(v.z) + "]";
        }
    }
}
