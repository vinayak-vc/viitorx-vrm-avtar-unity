using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Retargeting.Kalidokit;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Drives the avatar ARMS (UpperArm / LowerArm / Hand) from the Kalidokit-ported arm solver
    /// (<see cref="KalidokitArmSolver"/>), ADR-022. Kalidokit derives limb ROLL from the limb geometry
    /// (elbow bend + shoulder/wrist plane), which the old vector-FK path could not do — that guessed roll
    /// from a global reference and produced the forearm "helicopter" spin.
    ///
    /// Application: Kalidokit outputs Euler radians per bone in the three-vrm normalized-bone convention.
    /// We apply them as a delta from each bone's captured REST local rotation
    /// (<c>bone.localRotation = rest * Remap(euler)</c>, slerped), where <see cref="axisSigns"/> maps
    /// Kalidokit's (right-handed, Y-up) rotation into Unity's (left-handed) bone frame. Both the axis
    /// signs and the blend are LIVE-TUNABLE (pushed from AppBootstrap) so the convention can be dialled in
    /// during Play without a recompile — the first-run values are a starting guess and WILL need one live
    /// tuning pass, like every convention-sensitive step in this project.
    /// </summary>
    public sealed class KalidokitRetargeter {
        private Transform leftUpperArm;
        private Transform leftLowerArm;
        private Transform rightUpperArm;
        private Transform rightLowerArm;
        private Transform leftHand;
        private Transform rightHand;

        private Quaternion leftUpperArmRest;
        private Quaternion leftLowerArmRest;
        private Quaternion rightUpperArmRest;
        private Quaternion rightLowerArmRest;
        private Quaternion leftHandRest;
        private Quaternion rightHandRest;

        private readonly Vector3[] landmarks = new Vector3[PoseFrame.LandmarkCount];
        private bool bound;

        // Tunables (live). axisSigns maps Kalidokit euler (x,y,z radians) onto Unity Euler degrees per axis;
        // driveHand applies the coarse hand rotation from the arm solver (finer wrist/fingers come later).
        private Vector3 axisSigns = new Vector3(-1f, -1f, 1f);
        private float lerpAmount = 0.4f;
        private bool driveHand = true;

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void SetAxisSigns(Vector3 signs) {
            axisSigns = signs;
        }

        public void SetLerp(float value) {
            lerpAmount = Mathf.Clamp01(value);
        }

        public void SetDriveHand(bool value) {
            driveHand = value;
        }

        public void Bind(Animator animator) {
            bound = false;
            leftUpperArm = null;
            if (animator == null || !animator.isHuman) {
                return;
            }
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (leftUpperArm != null) {
                leftUpperArmRest = leftUpperArm.localRotation;
            }
            if (leftLowerArm != null) {
                leftLowerArmRest = leftLowerArm.localRotation;
            }
            if (rightUpperArm != null) {
                rightUpperArmRest = rightUpperArm.localRotation;
            }
            if (rightLowerArm != null) {
                rightLowerArmRest = rightLowerArm.localRotation;
            }
            if (leftHand != null) {
                leftHandRest = leftHand.localRotation;
            }
            if (rightHand != null) {
                rightHandRest = rightHand.localRotation;
            }
            bound = leftUpperArm != null || rightUpperArm != null;
        }

        public void Unbind() {
            bound = false;
        }

        public void Apply(PoseFrame frame) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }
            // Adapt our PoseFrame (Unity: X right, Y UP, Z forward) into Kalidokit's expected convention
            // (X right, Y DOWN, Z toward camera) by flipping Y. Indices are JointId == MediaPipe pose order.
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 p = frame.GetLandmark((JointId)i).Position;
                landmarks[i] = new Vector3(p.x, -p.y, p.z);
                i = i + 1;
            }

            KalidokitArmPose pose = KalidokitArmSolver.Solve(landmarks);

            ApplyBone(leftUpperArm, leftUpperArmRest, pose.UpperArmLeft);
            ApplyBone(leftLowerArm, leftLowerArmRest, pose.LowerArmLeft);
            ApplyBone(rightUpperArm, rightUpperArmRest, pose.UpperArmRight);
            ApplyBone(rightLowerArm, rightLowerArmRest, pose.LowerArmRight);
            if (driveHand) {
                ApplyBone(leftHand, leftHandRest, pose.HandLeft);
                ApplyBone(rightHand, rightHandRest, pose.HandRight);
            }
        }

        private void ApplyBone(Transform bone, Quaternion rest, Vector3 eulerRadians) {
            if (bone == null) {
                return;
            }
            Quaternion remapped = Quaternion.Euler(
                eulerRadians.x * Mathf.Rad2Deg * axisSigns.x,
                eulerRadians.y * Mathf.Rad2Deg * axisSigns.y,
                eulerRadians.z * Mathf.Rad2Deg * axisSigns.z);
            Quaternion target = rest * remapped;
            bone.localRotation = Quaternion.Slerp(bone.localRotation, target, lerpAmount);
        }
    }
}
