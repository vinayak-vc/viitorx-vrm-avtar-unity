using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Animations.Rigging;

using VirtualMirror.Core;

namespace VirtualMirror.IK {
    /// <summary>
    /// <see cref="IIkSolver"/> implementation using Unity Animation Rigging (ADR-002, SDS-011).
    /// Dynamically constructs a Rig and TwoBoneIKConstraints for arms and legs on the target humanoid
    /// avatar, and drives their target/hint transforms from <see cref="PoseFrame"/> landmarks.
    ///
    /// Landmark offsets are MediaPipe world metres sized for a real human (~1.7 m span); the avatar may
    /// be any scale (e.g. a 0.76 m chibi), so each frame the offset is normalized by
    /// (avatar limb length / tracked limb length) measured at <see cref="Bind"/>, keeping targets reachable.
    /// IK is position-only (targetRotationWeight = 0) because wrist/ankle orientation is not tracked.
    /// <see cref="SetActive"/> lets the composition root disable the solver at runtime (releasing the rig
    /// so FK can drive the limbs instead), keeping FK and IK mutually exclusive on the arm/leg bones.
    /// </summary>
    public sealed class AnimationRiggingIkDriver : IIkSolver {
        // Minimum ankle-below-hip drop (MediaPipe world metres, ~1.7 m human) for the lower body to count
        // as visible/standing. Below this the legs are treated as occluded and leg IK is held off.
        private const float MinLegDropMetres = 0.35f;

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

        private float leftArmLength;
        private float rightArmLength;
        private float leftLegLength;
        private float rightLegLength;
        private Quaternion hipsRestRotation;

        private bool bound;
        private bool active;

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
            // Capture the hips' rest world rotation so landmark offsets (which are in tracking/camera axes)
            // can be re-expressed in the avatar's facing. Without this, an avatar whose rest hips face a
            // non-identity direction (e.g. Y=180) gets its hand/foot targets placed behind the body.
            Transform hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            hipsRestRotation = hipsBone != null ? hipsBone.rotation : Quaternion.identity;
            Transform rootTransform = targetParent != null ? targetParent : animator.transform;
            CreateTargets(rootTransform);
            CreateRigHierarchy();
            MeasureLimbLengths();
            active = true;
            bound = true;
        }

        /// <summary>
        /// Enable or disable the solver at runtime. When disabled the constraint weights are zeroed so the
        /// rig stops deforming the arm/leg bones, letting FK drive them instead (mutual exclusion).
        /// </summary>
        public void SetActive(bool value) {
            if (active == value) {
                return;
            }
            active = value;
            if (!active) {
                SetConstraintWeight(leftArmConstraint, 0f);
                SetConstraintWeight(rightArmConstraint, 0f);
                SetConstraintWeight(leftLegConstraint, 0f);
                SetConstraintWeight(rightLegConstraint, 0f);
            }
        }

        public void Apply(PoseFrame frame, float minConfidence) {
            if (!bound || !active || frame == null || !frame.IsValid || animator == null) {
                return;
            }
            // Each limb target is anchored at the AVATAR's own limb-root bone (shoulder / hip joint) and
            // only the limb-local offset (root-joint -> tip) is scaled. Anchoring at the avatar hips and
            // scaling the full hip-centred landmark vector (as before) shrank the torso->shoulder span by
            // the arm ratio too, collapsing the hand/foot target down to hip level. See UpdateArm/UpdateLeg.
            UpdateArm(frame, HumanBodyBones.LeftUpperArm, JointId.LeftShoulder, JointId.LeftElbow, JointId.LeftWrist, leftHandTarget, leftElbowHint, leftArmConstraint, minConfidence, leftArmLength);
            UpdateArm(frame, HumanBodyBones.RightUpperArm, JointId.RightShoulder, JointId.RightElbow, JointId.RightWrist, rightHandTarget, rightElbowHint, rightArmConstraint, minConfidence, rightArmLength);
            UpdateLeg(frame, HumanBodyBones.LeftUpperLeg, JointId.LeftHip, JointId.LeftKnee, JointId.LeftAnkle, leftFootTarget, leftKneeHint, leftLegConstraint, minConfidence, leftLegLength);
            UpdateLeg(frame, HumanBodyBones.RightUpperLeg, JointId.RightHip, JointId.RightKnee, JointId.RightAnkle, rightFootTarget, rightKneeHint, rightLegConstraint, minConfidence, rightLegLength);
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
            leftArmLength = 0f;
            rightArmLength = 0f;
            leftLegLength = 0f;
            rightLegLength = 0f;
            hipsRestRotation = Quaternion.identity;
            active = false;
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

        private void MeasureLimbLengths() {
            leftArmLength = MeasureChain(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            rightArmLength = MeasureChain(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            leftLegLength = MeasureChain(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            rightLegLength = MeasureChain(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        private float MeasureChain(HumanBodyBones root, HumanBodyBones mid, HumanBodyBones tip) {
            Transform rootTransform = animator.GetBoneTransform(root);
            Transform midTransform = animator.GetBoneTransform(mid);
            Transform tipTransform = animator.GetBoneTransform(tip);
            if (rootTransform == null || midTransform == null || tipTransform == null) {
                return 0f;
            }
            return Vector3.Distance(rootTransform.position, midTransform.position) + Vector3.Distance(midTransform.position, tipTransform.position);
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
            // Position-only IK: the target's rotation is never tracked/set, so weighting it would snap
            // the hand/foot to world-identity rotation. Keep rotation at rest (weight 0).
            data.targetRotationWeight = 0f;
            data.hintWeight = 1f;
            constraint.data = data;

            return constraint;
        }

        private void UpdateArm(PoseFrame frame, HumanBodyBones rootBone, JointId shoulderJoint, JointId elbowJoint, JointId wristJoint, Transform target, Transform hint, TwoBoneIKConstraint constraint, float minConfidence, float avatarLength) {
            if (constraint == null) {
                return;
            }
            PoseLandmark wristLandmark = frame.GetLandmark(wristJoint);
            PoseLandmark elbowLandmark = frame.GetLandmark(elbowJoint);

            if (wristLandmark.Confidence >= minConfidence && elbowLandmark.Confidence >= minConfidence) {
                Transform rootTransform = animator.GetBoneTransform(rootBone);
                if (rootTransform == null) {
                    constraint.weight = 0f;
                    return;
                }
                // Anchor at the avatar's shoulder (limb root) and scale ONLY the shoulder->wrist offset,
                // so the target lands an avatar-arm-length away from the avatar shoulder — never sunk to the hips.
                Vector3 anchor = rootTransform.position;
                Vector3 shoulderPosition = frame.GetLandmark(shoulderJoint).Position;
                float trackedLength = Vector3.Distance(shoulderPosition, elbowLandmark.Position) + Vector3.Distance(elbowLandmark.Position, wristLandmark.Position);
                float scale = ComputeScale(avatarLength, trackedLength);
                target.position = anchor + hipsRestRotation * ((wristLandmark.Position - shoulderPosition) * scale);
                hint.position = anchor + hipsRestRotation * ((elbowLandmark.Position - shoulderPosition) * scale);
                constraint.weight = 1f;
            } else {
                constraint.weight = 0f;
            }
        }

        private void UpdateLeg(PoseFrame frame, HumanBodyBones rootBone, JointId hipJoint, JointId kneeJoint, JointId ankleJoint, Transform target, Transform hint, TwoBoneIKConstraint constraint, float minConfidence, float avatarLength) {
            if (constraint == null) {
                return;
            }
            PoseLandmark ankleLandmark = frame.GetLandmark(ankleJoint);
            PoseLandmark kneeLandmark = frame.GetLandmark(kneeJoint);

            if (ankleLandmark.Confidence >= minConfidence && kneeLandmark.Confidence >= minConfidence) {
                Transform rootTransform = animator.GetBoneTransform(rootBone);
                if (rootTransform == null) {
                    constraint.weight = 0f;
                    return;
                }
                Vector3 hipPosition = frame.GetLandmark(hipJoint).Position;
                // Lower-body plausibility gate (SDS-011 §8): drive leg IK only when the tracked ankle sits
                // clearly below the hip — i.e. the legs are actually in frame (standing / full body). On a
                // seated or upper-body webcam the lower body is occluded and MediaPipe still emits collapsed
                // leg landmarks that would FOLD the avatar's legs; hold the leg at rest (weight 0) instead.
                Vector3 torsoUp = TorsoUp(frame);
                float ankleDrop = Vector3.Dot(-torsoUp, ankleLandmark.Position - hipPosition);
                if (ankleDrop < MinLegDropMetres) {
                    constraint.weight = 0f;
                    return;
                }
                // Anchor at the avatar's hip joint (limb root) and scale ONLY the hip->ankle offset.
                Vector3 anchor = rootTransform.position;
                float trackedLength = Vector3.Distance(hipPosition, kneeLandmark.Position) + Vector3.Distance(kneeLandmark.Position, ankleLandmark.Position);
                float scale = ComputeScale(avatarLength, trackedLength);
                target.position = anchor + hipsRestRotation * ((ankleLandmark.Position - hipPosition) * scale);
                hint.position = anchor + hipsRestRotation * ((kneeLandmark.Position - hipPosition) * scale);
                constraint.weight = 1f;
            } else {
                constraint.weight = 0f;
            }
        }

        private Vector3 TorsoUp(PoseFrame frame) {
            Vector3 midShoulder = 0.5f * (frame.GetLandmark(JointId.LeftShoulder).Position + frame.GetLandmark(JointId.RightShoulder).Position);
            Vector3 midHip = 0.5f * (frame.GetLandmark(JointId.LeftHip).Position + frame.GetLandmark(JointId.RightHip).Position);
            Vector3 up = midShoulder - midHip;
            if (up.sqrMagnitude < 1e-6f) {
                return Vector3.up;
            }
            return up.normalized;
        }

        private static float ComputeScale(float avatarLength, float trackedLength) {
            if (avatarLength <= 1e-4f || trackedLength <= 1e-4f) {
                return 1f;
            }
            return avatarLength / trackedLength;
        }

        private static void SetConstraintWeight(TwoBoneIKConstraint constraint, float weight) {
            if (constraint != null) {
                constraint.weight = weight;
            }
        }
    }
}
