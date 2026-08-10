using System.Collections.Generic;

using UnityEngine;

using UniVRM10;

using VirtualMirror.Core;
using VirtualMirror.Retargeting.Kalidokit;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Whole-body Kalidokit driver (ADR-022). Applies the ported Kalidokit pose
    /// (<see cref="KalidokitPoseSolver"/>) to UniVRM's NORMALIZED control rig
    /// (<see cref="Vrm10RuntimeControlRig"/>) — the same VRM1.0 normalized bones three-vrm exposes, so
    /// Kalidokit's numbers apply with a SINGLE global three→Unity handedness conversion instead of the
    /// per-bone axis guessing that stalled the raw-bone path. The avatar must be loaded WITH the control
    /// rig generated (<see cref="VirtualMirror.Avatar.Vrm.UniVrmAvatarLoader.GenerateControlRig"/>).
    ///
    /// Requires the control rig to be driven each frame before UniVRM's <c>Runtime.Process()</c> runs; call
    /// <see cref="Apply"/> from LateUpdate. All bones share one convention: <see cref="eulerSigns"/> +
    /// <see cref="flipQuat"/> (both live-tunable) — the ONE knob the user dials once (see ADR-022).
    /// </summary>
    public sealed class KalidokitControlRigDriver {
        private Vrm10Instance vrm;
        private Vrm10RuntimeControlRig controlRig;

        private Transform hips;
        private Transform spine;
        private Transform chest;
        private Transform leftUpperArm;
        private Transform leftLowerArm;
        private Transform leftHand;
        private Transform rightUpperArm;
        private Transform rightLowerArm;
        private Transform rightHand;
        private Transform leftUpperLeg;
        private Transform leftLowerLeg;
        private Transform rightUpperLeg;
        private Transform rightLowerLeg;

        private readonly Vector3[] landmarks = new Vector3[PoseFrame.LandmarkCount];
        private bool bound;

        // Global convention knobs (live-tunable from AppBootstrap). Defaults are the standard three→Unity
        // conversion (mirror Z); if the whole body is mis-oriented, change flipQuat (0..3) and/or eulerSigns.
        private Vector3 eulerSigns = Vector3.one;
        private int flipQuat;
        private float lerpAmount = 0.5f;
        private bool driveLegs = true;
        private bool mirrorX;
        // Torso side-lean (roll) is the least reliable torso DOF and produced a constant "leaned right"
        // from noisy shoulder tilt. Scales the roll (z) of hips + spine: 0 = upright (no lean, ADR-019
        // StableUp precedent), 1 = full side-lean tracking. Live-tunable.
        private float torsoRoll;

        // JointId left<->right pairs for a true reflection (negate X AND swap sides — negate-X alone crosses
        // the limbs, ADR-018). Flattened pairs.
        private static readonly int[] MirrorPairs = new int[] {
            1, 4, 2, 5, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
        };

        // Finger driving on the control rig (curl-based, ADR-022 fingers). Reuses the HandFrame per-finger
        // curls from any hand provider; rotates each normalized finger bone about a single tunable axis.
        private struct FingerJoint {
            public Transform Bone;
            public Quaternion Rest;
            public int Finger;      // 0=thumb,1=index,2=middle,3=ring,4=little
            public float MaxDegrees;
        }
        private readonly List<FingerJoint> leftFingers = new List<FingerJoint>();
        private readonly List<FingerJoint> rightFingers = new List<FingerJoint>();
        private Vector3 fingerCurlAxis = new Vector3(0f, 0f, -1f); // normalized-bone flexion axis (tunable)
        private float fingerWeight = 1f;

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void SetTuning(Vector3 signs, int flip, float lerp, bool legs, bool mirror, float torsoRollScale) {
            eulerSigns = signs;
            flipQuat = flip;
            lerpAmount = Mathf.Clamp01(lerp);
            driveLegs = legs;
            mirrorX = mirror;
            torsoRoll = Mathf.Clamp01(torsoRollScale);
        }

        public void Bind(Vrm10Instance vrmInstance) {
            bound = false;
            vrm = vrmInstance;
            controlRig = null;
            if (vrm == null || vrm.Runtime == null) {
                return;
            }
            controlRig = vrm.Runtime.ControlRig;
            if (controlRig == null) {
                // Avatar was not loaded with a control rig — cannot use this path.
                return;
            }
            // Take MANUAL control of the VRM update: with auto-update the instance's Process() races our
            // LateUpdate control-rig writes (it runs first and applies the REST pose → avatar stuck in
            // T-pose). Switch to None and call Process() ourselves right AFTER we write the bones each
            // frame, so our pose (and expressions/springbones) always applies in order. Restored on Unbind.
            vrm.UpdateType = Vrm10Instance.UpdateTypes.None;
            hips = controlRig.GetBoneTransform(HumanBodyBones.Hips);
            spine = controlRig.GetBoneTransform(HumanBodyBones.Spine);
            chest = controlRig.GetBoneTransform(HumanBodyBones.Chest);
            leftUpperArm = controlRig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = controlRig.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = controlRig.GetBoneTransform(HumanBodyBones.LeftHand);
            rightUpperArm = controlRig.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLowerArm = controlRig.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = controlRig.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpperLeg = controlRig.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            leftLowerLeg = controlRig.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightUpperLeg = controlRig.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rightLowerLeg = controlRig.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            BindFingers();
            bound = hips != null || leftUpperArm != null || rightUpperArm != null;
        }

        public void Unbind() {
            if (vrm != null) {
                vrm.UpdateType = Vrm10Instance.UpdateTypes.Update; // restore auto-update for the FK path
            }
            bound = false;
            controlRig = null;
            vrm = null;
        }

        /// <summary>
        /// Runs the VRM runtime (control rig → skeleton, constraints, springbones, look-at, expressions).
        /// Call once per frame AFTER <see cref="Apply"/> and after the expression retarget, since the VRM is
        /// set to manual (UpdateTypes.None) while this driver owns it. This is what makes the pose actually
        /// show — without it the avatar stays in T-pose (the reported bug).
        /// </summary>
        public void ProcessRuntime() {
            if (!bound || vrm == null || vrm.Runtime == null) {
                return;
            }
            vrm.Runtime.Process();
        }

        public void Apply(PoseFrame frame) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }
            // Adapt PoseFrame (Unity Y-up) into Kalidokit's convention (Y-down); indices == MediaPipe order.
            // mirrorX = true presents a reflected skeleton to the solver (negate X + swap L/R) so the avatar
            // mirrors the user like a real mirror instead of copying same-side ("looks right but mirrored").
            float mx = mirrorX ? -1f : 1f;
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 p = frame.GetLandmark((JointId)i).Position;
                landmarks[i] = new Vector3(p.x * mx, -p.y, p.z);
                i = i + 1;
            }
            if (mirrorX) {
                int pi = 0;
                while (pi < MirrorPairs.Length) {
                    int a = MirrorPairs[pi];
                    int b = MirrorPairs[pi + 1];
                    Vector3 tmp = landmarks[a];
                    landmarks[a] = landmarks[b];
                    landmarks[b] = tmp;
                    pi = pi + 2;
                }
            }

            KalidokitFullPose pose = KalidokitPoseSolver.Solve(landmarks);

            // Torso: scale the roll (z) by torsoRoll (0 = upright), and DISTRIBUTE the spine rotation across
            // Spine + Chest instead of applying the full euler to BOTH (that doubled the torso rotation →
            // the constant lean). Split 50/50 when a Chest bone exists, else all on Spine.
            Vector3 spineE = pose.Spine;
            spineE.z *= torsoRoll;
            float spineShare = chest != null ? 0.5f : 1f;
            ApplyBone(spine, spineE * spineShare);
            if (chest != null) {
                ApplyBone(chest, spineE * 0.5f);
            }
            ApplyBone(leftUpperArm, pose.UpperArmLeft);
            ApplyBone(leftLowerArm, pose.LowerArmLeft);
            ApplyBone(rightUpperArm, pose.UpperArmRight);
            ApplyBone(rightLowerArm, pose.LowerArmRight);
            ApplyBone(leftHand, pose.HandLeft);
            ApplyBone(rightHand, pose.HandRight);
            if (driveLegs) {
                ApplyBone(leftUpperLeg, pose.UpperLegLeft);
                ApplyBone(leftLowerLeg, pose.LowerLegLeft);
                ApplyBone(rightUpperLeg, pose.UpperLegRight);
                ApplyBone(rightLowerLeg, pose.LowerLegRight);
            }
            // Hips rotation only (position handled elsewhere); scale roll by torsoRoll (0 = upright).
            Vector3 hipsE = pose.Hips;
            hipsE.z *= torsoRoll;
            ApplyBone(hips, hipsE);
        }

        private void ApplyBone(Transform bone, Vector3 eulerRadians) {
            if (bone == null) {
                return;
            }
            // Normalized-bone rest is identity, so the converted quaternion is the absolute local rotation
            // (matches three-vrm getNormalizedBoneNode). Slerp for damping.
            Quaternion target = KMath.ThreeEulerToUnity(eulerRadians, eulerSigns, flipQuat);
            bone.localRotation = Quaternion.Slerp(bone.localRotation, target, lerpAmount);
        }

        public void SetFingerTuning(Vector3 curlAxis, float weight) {
            fingerCurlAxis = curlAxis;
            fingerWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Drive the control-rig FINGER bones from the HandFrame per-finger curls (ADR-022 fingers). Curl-
        /// based (open/close), reusing the existing hand providers; not the full 21-landmark Kalidokit
        /// HandSolver (that needs a close hand source to be worthwhile). Call after <see cref="Apply"/> and
        /// before <see cref="ProcessRuntime"/>.
        /// </summary>
        public void ApplyFingers(HandFrame frame) {
            if (!bound || frame == null || !frame.IsValid || fingerWeight <= 0f) {
                return;
            }
            ApplyHandFingers(leftFingers, frame.LeftThumbCurl, frame.LeftIndexCurl, frame.LeftMiddleCurl, frame.LeftRingCurl, frame.LeftLittleCurl);
            ApplyHandFingers(rightFingers, frame.RightThumbCurl, frame.RightIndexCurl, frame.RightMiddleCurl, frame.RightRingCurl, frame.RightLittleCurl);
        }

        private void ApplyHandFingers(List<FingerJoint> list, float thumb, float index, float middle, float ring, float little) {
            float[] curls = new float[] { thumb, index, middle, ring, little };
            int k = 0;
            while (k < list.Count) {
                FingerJoint joint = list[k];
                k = k + 1;
                if (joint.Bone == null) {
                    continue;
                }
                float curl = Mathf.Clamp01(curls[joint.Finger]) * fingerWeight;
                Quaternion rot = Quaternion.AngleAxis(curl * joint.MaxDegrees, fingerCurlAxis);
                joint.Bone.localRotation = Quaternion.Slerp(joint.Bone.localRotation, joint.Rest * rot, lerpAmount);
            }
        }

        private void BindFingers() {
            leftFingers.Clear();
            rightFingers.Clear();
            // finger index: 0=thumb,1=index,2=middle,3=ring,4=little; maxDeg per joint (proximal/inter/distal).
            AddFinger(leftFingers, HumanBodyBones.LeftThumbProximal, 0, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftThumbIntermediate, 0, 30f);
            AddFinger(leftFingers, HumanBodyBones.LeftThumbDistal, 0, 30f);
            AddFinger(leftFingers, HumanBodyBones.LeftIndexProximal, 1, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftIndexIntermediate, 1, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftIndexDistal, 1, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftMiddleProximal, 2, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftMiddleIntermediate, 2, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftMiddleDistal, 2, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftRingProximal, 3, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftRingIntermediate, 3, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftRingDistal, 3, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftLittleProximal, 4, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftLittleIntermediate, 4, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftLittleDistal, 4, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightThumbProximal, 0, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightThumbIntermediate, 0, 30f);
            AddFinger(rightFingers, HumanBodyBones.RightThumbDistal, 0, 30f);
            AddFinger(rightFingers, HumanBodyBones.RightIndexProximal, 1, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightIndexIntermediate, 1, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightIndexDistal, 1, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightMiddleProximal, 2, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightMiddleIntermediate, 2, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightMiddleDistal, 2, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightRingProximal, 3, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightRingIntermediate, 3, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightRingDistal, 3, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightLittleProximal, 4, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightLittleIntermediate, 4, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightLittleDistal, 4, 40f);
        }

        private void AddFinger(List<FingerJoint> list, HumanBodyBones bone, int finger, float maxDegrees) {
            Transform t = controlRig.GetBoneTransform(bone);
            if (t == null) {
                return;
            }
            FingerJoint joint;
            joint.Bone = t;
            joint.Rest = t.localRotation;
            joint.Finger = finger;
            joint.MaxDegrees = maxDegrees;
            list.Add(joint);
        }
    }
}
