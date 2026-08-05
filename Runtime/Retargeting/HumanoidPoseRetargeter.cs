using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Drives a humanoid avatar from a <see cref="PoseFrame"/> using vector-based FK across the hips,
    /// spine, neck, arms and legs. Hips (root) get a full basis rotation from the hip line + torso up;
    /// each limb bone gets a roll-constrained rotation (see <see cref="RotationFromVectors"/>) so it
    /// aligns with the tracked segment without arbitrary twist. Bones are written in world space so
    /// children stay correct as parents move. Segments whose landmarks fall below the confidence
    /// threshold are held. The bound Animator is disabled; call from LateUpdate.
    /// </summary>
    public sealed class HumanoidPoseRetargeter {
        private static readonly Vector3 RollReference = Vector3.forward;

        private sealed class SegmentDefinition {
            public readonly HumanBodyBones Bone;
            public readonly HumanBodyBones RestChild;
            public readonly JointId[] FromJoints;
            public readonly JointId[] ToJoints;

            public SegmentDefinition(HumanBodyBones bone, HumanBodyBones restChild, JointId[] fromJoints, JointId[] toJoints) {
                Bone = bone;
                RestChild = restChild;
                FromJoints = fromJoints;
                ToJoints = toJoints;
            }
        }

        private sealed class BoundSegment {
            public readonly Transform Bone;
            public readonly Quaternion RestRotation;
            public readonly Vector3 RestDirection;
            public readonly JointId[] FromJoints;
            public readonly JointId[] ToJoints;

            public BoundSegment(Transform bone, Quaternion restRotation, Vector3 restDirection, JointId[] fromJoints, JointId[] toJoints) {
                Bone = bone;
                RestRotation = restRotation;
                RestDirection = restDirection;
                FromJoints = fromJoints;
                ToJoints = toJoints;
            }
        }

        private static readonly JointId[] HipsFromJoints = new JointId[] { JointId.LeftHip, JointId.RightHip, JointId.LeftShoulder, JointId.RightShoulder };

        private readonly SegmentDefinition[] definitions;
        private readonly List<BoundSegment> boundSegments;

        private Transform hipsBone;
        private Quaternion hipsRestRotation;
        private Quaternion hipsRestBasis;
        private bool hipsBound;
        private bool bound;

        public HumanoidPoseRetargeter() {
            boundSegments = new List<BoundSegment>();
            definitions = BuildDefinitions();
        }

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void Bind(Animator animator) {
            boundSegments.Clear();
            bound = false;
            hipsBound = false;
            if (animator == null || !animator.isHuman) {
                return;
            }
            BindSegments(animator);
            BindHips(animator);
            if (boundSegments.Count == 0 && !hipsBound) {
                return;
            }
            animator.enabled = false;
            bound = true;
        }

        public void Apply(PoseFrame frame, float minConfidence) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }
            ApplyHips(frame, minConfidence);
            int index = 0;
            while (index < boundSegments.Count) {
                BoundSegment segment = boundSegments[index];
                index = index + 1;
                Vector3 from;
                Vector3 to;
                if (!TryAverage(frame, segment.FromJoints, minConfidence, out from)) {
                    continue;
                }
                if (!TryAverage(frame, segment.ToJoints, minConfidence, out to)) {
                    continue;
                }
                Vector3 targetDirection = to - from;
                if (targetDirection.sqrMagnitude < 1e-8f) {
                    continue;
                }
                Quaternion delta = RotationFromVectors.Delta(segment.RestDirection, targetDirection, RollReference);
                segment.Bone.rotation = delta * segment.RestRotation;
            }
        }

        private void BindSegments(Animator animator) {
            int index = 0;
            while (index < definitions.Length) {
                SegmentDefinition definition = definitions[index];
                index = index + 1;
                Transform bone = animator.GetBoneTransform(definition.Bone);
                Transform child = animator.GetBoneTransform(definition.RestChild);
                if (bone == null || child == null) {
                    continue;
                }
                Vector3 restDirection = child.position - bone.position;
                if (restDirection.sqrMagnitude < 1e-8f) {
                    continue;
                }
                boundSegments.Add(new BoundSegment(bone, bone.rotation, restDirection, definition.FromJoints, definition.ToJoints));
            }
        }

        private void BindHips(Animator animator) {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (hips == null || leftUpperLeg == null || rightUpperLeg == null || neck == null) {
                return;
            }
            Vector3 restRight = leftUpperLeg.position - rightUpperLeg.position;
            Vector3 restUp = neck.position - hips.position;
            Quaternion restBasis;
            if (!RotationFromVectors.TryBasis(restRight, restUp, out restBasis)) {
                return;
            }
            hipsBone = hips;
            hipsRestRotation = hips.rotation;
            hipsRestBasis = restBasis;
            hipsBound = true;
        }

        private void ApplyHips(PoseFrame frame, float minConfidence) {
            if (!hipsBound) {
                return;
            }
            if (!AllConfident(frame, HipsFromJoints, minConfidence)) {
                return;
            }
            Vector3 leftHip = frame.GetLandmark(JointId.LeftHip).Position;
            Vector3 rightHip = frame.GetLandmark(JointId.RightHip).Position;
            Vector3 midShoulder = 0.5f * (frame.GetLandmark(JointId.LeftShoulder).Position + frame.GetLandmark(JointId.RightShoulder).Position);
            Vector3 midHip = 0.5f * (leftHip + rightHip);
            Vector3 targetRight = leftHip - rightHip;
            Vector3 targetUp = midShoulder - midHip;
            Quaternion targetBasis;
            if (!RotationFromVectors.TryBasis(targetRight, targetUp, out targetBasis)) {
                return;
            }
            Quaternion delta = targetBasis * Quaternion.Inverse(hipsRestBasis);
            hipsBone.rotation = delta * hipsRestRotation;
        }

        private static bool AllConfident(PoseFrame frame, JointId[] joints, float minConfidence) {
            int index = 0;
            while (index < joints.Length) {
                if (frame.GetLandmark(joints[index]).Confidence < minConfidence) {
                    return false;
                }
                index = index + 1;
            }
            return true;
        }

        private static bool TryAverage(PoseFrame frame, JointId[] joints, float minConfidence, out Vector3 average) {
            Vector3 sum = Vector3.zero;
            int count = 0;
            int index = 0;
            while (index < joints.Length) {
                PoseLandmark landmark = frame.GetLandmark(joints[index]);
                index = index + 1;
                if (landmark.Confidence < minConfidence) {
                    average = Vector3.zero;
                    return false;
                }
                sum = sum + landmark.Position;
                count = count + 1;
            }
            if (count == 0) {
                average = Vector3.zero;
                return false;
            }
            average = sum / count;
            return true;
        }

        private static SegmentDefinition[] BuildDefinitions() {
            JointId[] hips = new JointId[] { JointId.LeftHip, JointId.RightHip };
            JointId[] shoulders = new JointId[] { JointId.LeftShoulder, JointId.RightShoulder };
            return new SegmentDefinition[] {
                new SegmentDefinition(HumanBodyBones.Spine, HumanBodyBones.Neck, hips, shoulders),
                new SegmentDefinition(HumanBodyBones.Neck, HumanBodyBones.Head, shoulders, new JointId[] { JointId.Nose }),
                new SegmentDefinition(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, new JointId[] { JointId.LeftShoulder }, new JointId[] { JointId.LeftElbow }),
                new SegmentDefinition(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, new JointId[] { JointId.LeftElbow }, new JointId[] { JointId.LeftWrist }),
                new SegmentDefinition(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, new JointId[] { JointId.RightShoulder }, new JointId[] { JointId.RightElbow }),
                new SegmentDefinition(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, new JointId[] { JointId.RightElbow }, new JointId[] { JointId.RightWrist }),
                new SegmentDefinition(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, new JointId[] { JointId.LeftHip }, new JointId[] { JointId.LeftKnee }),
                new SegmentDefinition(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, new JointId[] { JointId.LeftKnee }, new JointId[] { JointId.LeftAnkle }),
                new SegmentDefinition(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, new JointId[] { JointId.RightHip }, new JointId[] { JointId.RightKnee }),
                new SegmentDefinition(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, new JointId[] { JointId.RightKnee }, new JointId[] { JointId.RightAnkle })
            };
        }
    }
}
