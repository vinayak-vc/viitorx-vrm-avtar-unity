using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Animations.Rigging;

using VirtualMirror.Core;

namespace VirtualMirror.IK {
    /// <summary>
    /// <see cref="IIkSolver"/> implementation using Unity Animation Rigging (ADR-002, SDS-011).
    /// Dynamically constructs a Rig and TwoBoneIKConstraints for arms and legs on the target humanoid
    /// avatar, and drives their target/hint transforms from <see cref="PoseFrame"/> landmarks.
    /// </summary>
    public sealed class AnimationRiggingIkDriver : IIkSolver {
        private Animator animator;
        private RigBuilder rigBuilder;
        private Rig rig;
        private GameObject ikRigObject;
        private GameObject ikTargetsObject;

        private TwoBoneIKConstraint leftArmConstraint;
        private TwoBoneIKConstraint rightArmConstraint;
        private TwoBoneIKConstraint leftLegConstraint;
        private TwoBoneIKConstraint rightLegConstraint;

        private Transform leftHandTarget;
        private Transform leftElbowHint;
        private Transform rightHandTarget;
        private Transform rightElbowHint;
        private Transform leftFootTarget;
        private Transform leftKneeHint;
        private Transform rightFootTarget;
        private Transform rightKneeHint;

        private bool bound;

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void Bind(Animator targetAnimator, Transform targetParent) {
            Unbind();
            if (targetAnimator == null || !targetAnimator.isHuman) {
                return;
            }
            animator = targetAnimator;
            animator.enabled = true;
            Transform rootTransform = targetParent != null ? targetParent : animator.transform;
            CreateTargets(rootTransform);
            CreateRigHierarchy();
            bound = true;
        }

        public void Apply(PoseFrame frame, float minConfidence) {
            if (!bound || frame == null || !frame.IsValid || animator == null) {
                return;
            }
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 origin = hips != null ? hips.position : animator.transform.position;

            UpdateArm(frame, JointId.LeftWrist, JointId.LeftElbow, leftHandTarget, leftElbowHint, leftArmConstraint, minConfidence, origin);
            UpdateArm(frame, JointId.RightWrist, JointId.RightElbow, rightHandTarget, rightElbowHint, rightArmConstraint, minConfidence, origin);
            UpdateLeg(frame, JointId.LeftAnkle, JointId.LeftKnee, leftFootTarget, leftKneeHint, leftLegConstraint, minConfidence, origin);
            UpdateLeg(frame, JointId.RightAnkle, JointId.RightKnee, rightFootTarget, rightKneeHint, rightLegConstraint, minConfidence, origin);
        }

        public void Unbind() {
            if (ikRigObject != null) {
                UnityEngine.Object.Destroy(ikRigObject);
                ikRigObject = null;
            }
            if (rigBuilder != null) {
                UnityEngine.Object.Destroy(rigBuilder);
                rigBuilder = null;
            }
            if (ikTargetsObject != null) {
                UnityEngine.Object.Destroy(ikTargetsObject);
                ikTargetsObject = null;
            }
            animator = null;
            rig = null;
            leftArmConstraint = null;
            rightArmConstraint = null;
            leftLegConstraint = null;
            rightLegConstraint = null;
            bound = false;
        }

        public void Dispose() {
            Unbind();
        }

        private void CreateTargets(Transform parent) {
            ikTargetsObject = new GameObject("IKTargets");
            ikTargetsObject.transform.SetParent(parent, false);

            leftHandTarget = CreateTargetTransform("LeftHandTarget", ikTargetsObject.transform);
            leftElbowHint = CreateTargetTransform("LeftElbowHint", ikTargetsObject.transform);

            rightHandTarget = CreateTargetTransform("RightHandTarget", ikTargetsObject.transform);
            rightElbowHint = CreateTargetTransform("RightElbowHint", ikTargetsObject.transform);

            leftFootTarget = CreateTargetTransform("LeftFootTarget", ikTargetsObject.transform);
            leftKneeHint = CreateTargetTransform("LeftKneeHint", ikTargetsObject.transform);

            rightFootTarget = CreateTargetTransform("RightFootTarget", ikTargetsObject.transform);
            rightKneeHint = CreateTargetTransform("RightKneeHint", ikTargetsObject.transform);
        }

        private Transform CreateTargetTransform(string name, Transform parent) {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        private void CreateRigHierarchy() {
            rigBuilder = animator.gameObject.GetComponent<RigBuilder>();
            if (rigBuilder == null) {
                rigBuilder = animator.gameObject.AddComponent<RigBuilder>();
            }

            ikRigObject = new GameObject("VirtualMirror_Rig");
            ikRigObject.transform.SetParent(animator.transform, false);
            rig = ikRigObject.AddComponent<Rig>();

            List<RigLayer> layers = new List<RigLayer>();
            layers.Add(new RigLayer(rig));
            rigBuilder.layers = layers;

            leftArmConstraint = CreateTwoBoneConstraint("TwoBoneIK_LeftArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, leftHandTarget, leftElbowHint);
            rightArmConstraint = CreateTwoBoneConstraint("TwoBoneIK_RightArm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rightHandTarget, rightElbowHint);
            leftLegConstraint = CreateTwoBoneConstraint("TwoBoneIK_LeftLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, leftFootTarget, leftKneeHint);
            rightLegConstraint = CreateTwoBoneConstraint("TwoBoneIK_RightLeg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, rightFootTarget, rightKneeHint);

            rigBuilder.Build();
        }

        private TwoBoneIKConstraint CreateTwoBoneConstraint(string name, HumanBodyBones rootBone, HumanBodyBones midBone, HumanBodyBones tipBone, Transform target, Transform hint) {
            if (animator == null || target == null || hint == null) {
                return null;
            }

            Transform rootTransform = animator.GetBoneTransform(rootBone);
            Transform midTransform = animator.GetBoneTransform(midBone);
            Transform tipTransform = animator.GetBoneTransform(tipBone);

            if (rootTransform == null || midTransform == null || tipTransform == null) {
                return null;
            }

            GameObject constraintObject = new GameObject(name);
            constraintObject.transform.SetParent(rig.transform, false);
            TwoBoneIKConstraint constraint = constraintObject.AddComponent<TwoBoneIKConstraint>();

            TwoBoneIKConstraintData data = constraint.data;
            data.root = rootTransform;
            data.mid = midTransform;
            data.tip = tipTransform;
            data.target = target;
            data.hint = hint;
            data.targetPositionWeight = 1f;
            data.targetRotationWeight = 1f;
            data.hintWeight = 1f;
            constraint.data = data;

            return constraint;
        }

        private void UpdateArm(PoseFrame frame, JointId wristJoint, JointId elbowJoint, Transform target, Transform hint, TwoBoneIKConstraint constraint, float minConfidence, Vector3 origin) {
            if (constraint == null) {
                return;
            }
            PoseLandmark wristLandmark = frame.GetLandmark(wristJoint);
            PoseLandmark elbowLandmark = frame.GetLandmark(elbowJoint);

            if (wristLandmark.Confidence >= minConfidence && elbowLandmark.Confidence >= minConfidence) {
                target.position = origin + wristLandmark.Position;
                hint.position = origin + elbowLandmark.Position;
                constraint.weight = 1f;
            } else {
                constraint.weight = 0f;
            }
        }

        private void UpdateLeg(PoseFrame frame, JointId ankleJoint, JointId kneeJoint, Transform target, Transform hint, TwoBoneIKConstraint constraint, float minConfidence, Vector3 origin) {
            if (constraint == null) {
                return;
            }
            PoseLandmark ankleLandmark = frame.GetLandmark(ankleJoint);
            PoseLandmark kneeLandmark = frame.GetLandmark(kneeJoint);

            if (ankleLandmark.Confidence >= minConfidence && kneeLandmark.Confidence >= minConfidence) {
                target.position = origin + ankleLandmark.Position;
                hint.position = origin + kneeLandmark.Position;
                constraint.weight = 1f;
            } else {
                constraint.weight = 0f;
            }
        }
    }
}
