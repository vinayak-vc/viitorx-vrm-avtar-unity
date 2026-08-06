using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Drives a humanoid avatar from a <see cref="PoseFrame"/> using vector-based FK.
    ///
    /// Torso (Hips + Spine) is driven by full 3-axis bases sharing the same torso-up: Hips from the
    /// hip line, Spine from the shoulder line. Both are <b>calibration-relative</b> — the tracked
    /// neutral basis is captured on the first confident frame and each bone rotates relative to it on
    /// top of the avatar rest, so the torso is straight at neutral regardless of coordinate handedness.
    ///
    /// Limbs/neck are driven per-segment with a roll-constrained rotation whose reference is the
    /// per-frame body-forward, so twist follows the body facing. Every driven part is gated with
    /// <b>hysteresis</b> (separate enter/exit confidences) to avoid flicker at the threshold. Bones are
    /// written in world space; the bound Animator is disabled so it cannot fight the writes. Call from
    /// LateUpdate. Use <see cref="Recalibrate"/> to re-capture the neutral pose.
    /// </summary>
    public sealed class HumanoidPoseRetargeter {
        private static readonly Vector3 RollReference = Vector3.forward;
        private static readonly JointId[] TorsoJoints = new JointId[] { JointId.LeftHip, JointId.RightHip, JointId.LeftShoulder, JointId.RightShoulder };
        private const float MinTorsoVectorMagnitude = 0.08f;
        private const int NeutralWarmupRequired = 8;

        private sealed class SegmentDefinition {
            public readonly HumanBodyBones Bone;
            public readonly HumanBodyBones RestChild;
            public readonly JointId[] FromJoints;
            public readonly JointId[] ToJoints;
            public readonly bool IsLimb;

            public SegmentDefinition(HumanBodyBones bone, HumanBodyBones restChild, JointId[] fromJoints, JointId[] toJoints, bool isLimb) {
                Bone = bone;
                RestChild = restChild;
                FromJoints = fromJoints;
                ToJoints = toJoints;
                IsLimb = isLimb;
            }
        }

        private sealed class BoundSegment {
            public readonly Transform Bone;
            public readonly Quaternion RestRotation;
            public readonly Vector3 RestDirection;
            public readonly JointId[] FromJoints;
            public readonly JointId[] ToJoints;
            public readonly bool IsLimb;

            public bool Active;

            public BoundSegment(Transform bone, Quaternion restRotation, Vector3 restDirection, JointId[] fromJoints, JointId[] toJoints, bool isLimb) {
                Bone = bone;
                RestRotation = restRotation;
                RestDirection = restDirection;
                FromJoints = fromJoints;
                ToJoints = toJoints;
                IsLimb = isLimb;
                Active = false;
            }
        }

        private sealed class BasisBone {
            public readonly Transform Bone;
            public readonly Quaternion AvatarRestRotation;
            public readonly JointId RightFrom;
            public readonly JointId RightTo;

            public Quaternion NeutralBasis;
            public bool HasNeutral;

            public BasisBone(Transform bone, Quaternion avatarRestRotation, JointId rightFrom, JointId rightTo) {
                Bone = bone;
                AvatarRestRotation = avatarRestRotation;
                RightFrom = rightFrom;
                RightTo = rightTo;
                NeutralBasis = Quaternion.identity;
                HasNeutral = false;
            }
        }

        private readonly SegmentDefinition[] definitions;
        private readonly List<BoundSegment> boundSegments;
        private readonly List<BasisBone> basisBones;

        private bool torsoActive;
        private bool armsLegsDrivenExternally;
        private int neutralWarmupFrames;
        private bool bound;

        public HumanoidPoseRetargeter() {
            boundSegments = new List<BoundSegment>();
            basisBones = new List<BasisBone>();
            definitions = BuildDefinitions();
        }

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void Bind(Animator animator) {
            boundSegments.Clear();
            basisBones.Clear();
            torsoActive = false;
            bound = false;
            if (animator == null || !animator.isHuman) {
                return;
            }
            BindSegments(animator);
            BindBasisBones(animator);
            if (boundSegments.Count == 0 && basisBones.Count == 0) {
                return;
            }
            // NOTE: the Animator is intentionally left enabled — the IK path (Animation Rigging) needs
            // it. FK writes here run in LateUpdate; with no AnimatorController there is no clip to
            // overwrite them, and IK layers on top afterwards.
            bound = true;
        }

        /// <summary>
        /// When true, the arm and leg segments are left to an external driver (the IK solver) and FK
        /// drives only the torso (hips/spine) + neck, so FK and IK never fight over the same bone.
        /// </summary>
        public void SetArmsLegsDrivenExternally(bool value) {
            armsLegsDrivenExternally = value;
        }

        /// <summary>Re-capture the tracked neutral pose on the next confident frame.</summary>
        public void Recalibrate() {
            neutralWarmupFrames = 0;
            int index = 0;
            while (index < basisBones.Count) {
                basisBones[index].HasNeutral = false;
                index = index + 1;
            }
        }

        public void Apply(PoseFrame frame, float enterConfidence, float exitConfidence) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }
            float torsoConfidence = MinConfidence(frame, TorsoJoints);
            torsoActive = UpdateGate(torsoActive, torsoConfidence, enterConfidence, exitConfidence);

            Vector3 rollReference = RollReference;
            if (torsoActive) {
                ApplyTorso(frame);
                rollReference = ComputeBodyForward(frame);
            }
            ApplySegments(frame, enterConfidence, exitConfidence, rollReference);
        }

        private void ApplyTorso(PoseFrame frame) {
            if (basisBones.Count == 0) {
                return;
            }
            Vector3 midShoulder = 0.5f * (frame.GetLandmark(JointId.LeftShoulder).Position + frame.GetLandmark(JointId.RightShoulder).Position);
            Vector3 midHip = 0.5f * (frame.GetLandmark(JointId.LeftHip).Position + frame.GetLandmark(JointId.RightHip).Position);
            Vector3 up = midShoulder - midHip;
            // Guard neutral capture: a real upright torso spans well over MinTorsoVectorMagnitude. A weak or
            // degenerate basis (camera warm-up, hips not yet in frame) would otherwise lock a bad neutral
            // and leave the avatar tilted/horizontal forever. Require a strong basis, stable for a few frames.
            bool strongUp = up.magnitude >= MinTorsoVectorMagnitude;
            if (strongUp) {
                if (neutralWarmupFrames < NeutralWarmupRequired) {
                    neutralWarmupFrames = neutralWarmupFrames + 1;
                }
            } else {
                neutralWarmupFrames = 0;
            }
            bool canCaptureNeutral = strongUp && neutralWarmupFrames >= NeutralWarmupRequired;
            int index = 0;
            while (index < basisBones.Count) {
                BasisBone basisBone = basisBones[index];
                index = index + 1;
                Vector3 right = frame.GetLandmark(basisBone.RightTo).Position - frame.GetLandmark(basisBone.RightFrom).Position;
                if (right.magnitude < MinTorsoVectorMagnitude) {
                    continue;
                }
                Quaternion currentBasis;
                if (!RotationFromVectors.TryBasis(right, up, out currentBasis)) {
                    continue;
                }
                if (!basisBone.HasNeutral) {
                    if (!canCaptureNeutral) {
                        continue;
                    }
                    basisBone.NeutralBasis = currentBasis;
                    basisBone.HasNeutral = true;
                }
                Quaternion delta = currentBasis * Quaternion.Inverse(basisBone.NeutralBasis);
                basisBone.Bone.rotation = delta * basisBone.AvatarRestRotation;
            }
        }

        private void ApplySegments(PoseFrame frame, float enterConfidence, float exitConfidence, Vector3 rollReference) {
            int index = 0;
            while (index < boundSegments.Count) {
                BoundSegment segment = boundSegments[index];
                index = index + 1;
                if (armsLegsDrivenExternally && segment.IsLimb) {
                    segment.Active = false;
                    continue;
                }
                float confidence = Mathf.Min(MinConfidence(frame, segment.FromJoints), MinConfidence(frame, segment.ToJoints));
                segment.Active = UpdateGate(segment.Active, confidence, enterConfidence, exitConfidence);
                if (!segment.Active) {
                    continue;
                }
                Vector3 from = Average(frame, segment.FromJoints);
                Vector3 to = Average(frame, segment.ToJoints);
                Vector3 targetDirection = to - from;
                if (targetDirection.sqrMagnitude < 1e-8f) {
                    continue;
                }
                Quaternion delta = RotationFromVectors.Delta(segment.RestDirection, targetDirection, rollReference);
                segment.Bone.rotation = delta * segment.RestRotation;
            }
        }

        private Vector3 ComputeBodyForward(PoseFrame frame) {
            Vector3 leftHip = frame.GetLandmark(JointId.LeftHip).Position;
            Vector3 rightHip = frame.GetLandmark(JointId.RightHip).Position;
            Vector3 midShoulder = 0.5f * (frame.GetLandmark(JointId.LeftShoulder).Position + frame.GetLandmark(JointId.RightShoulder).Position);
            Vector3 midHip = 0.5f * (leftHip + rightHip);
            Vector3 forward = Vector3.Cross(rightHip - leftHip, midShoulder - midHip);
            if (forward.sqrMagnitude < 1e-8f) {
                return RollReference;
            }
            return forward.normalized;
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
                boundSegments.Add(new BoundSegment(bone, bone.rotation, restDirection, definition.FromJoints, definition.ToJoints, definition.IsLimb));
            }
        }

        private void BindBasisBones(Animator animator) {
            AddBasisBone(animator, HumanBodyBones.Hips, JointId.LeftHip, JointId.RightHip);
            AddBasisBone(animator, HumanBodyBones.Spine, JointId.LeftShoulder, JointId.RightShoulder);
        }

        private void AddBasisBone(Animator animator, HumanBodyBones bone, JointId rightFrom, JointId rightTo) {
            Transform transform = animator.GetBoneTransform(bone);
            if (transform == null) {
                return;
            }
            basisBones.Add(new BasisBone(transform, transform.rotation, rightFrom, rightTo));
        }

        private static bool UpdateGate(bool active, float confidence, float enterConfidence, float exitConfidence) {
            if (active) {
                return confidence >= exitConfidence;
            }
            return confidence >= enterConfidence;
        }

        private static float MinConfidence(PoseFrame frame, JointId[] joints) {
            float min = 1f;
            int index = 0;
            while (index < joints.Length) {
                float confidence = frame.GetLandmark(joints[index]).Confidence;
                if (confidence < min) {
                    min = confidence;
                }
                index = index + 1;
            }
            return min;
        }

        private static Vector3 Average(PoseFrame frame, JointId[] joints) {
            if (joints.Length == 0) {
                return Vector3.zero;
            }
            Vector3 sum = Vector3.zero;
            int index = 0;
            while (index < joints.Length) {
                sum = sum + frame.GetLandmark(joints[index]).Position;
                index = index + 1;
            }
            return sum / joints.Length;
        }

        private static SegmentDefinition[] BuildDefinitions() {
            JointId[] shoulders = new JointId[] { JointId.LeftShoulder, JointId.RightShoulder };
            return new SegmentDefinition[] {
                new SegmentDefinition(HumanBodyBones.Neck, HumanBodyBones.Head, shoulders, new JointId[] { JointId.Nose }, false),
                new SegmentDefinition(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, new JointId[] { JointId.LeftShoulder }, new JointId[] { JointId.LeftElbow }, true),
                new SegmentDefinition(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, new JointId[] { JointId.LeftElbow }, new JointId[] { JointId.LeftWrist }, true),
                new SegmentDefinition(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, new JointId[] { JointId.RightShoulder }, new JointId[] { JointId.RightElbow }, true),
                new SegmentDefinition(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, new JointId[] { JointId.RightElbow }, new JointId[] { JointId.RightWrist }, true),
                new SegmentDefinition(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, new JointId[] { JointId.LeftHip }, new JointId[] { JointId.LeftKnee }, true),
                new SegmentDefinition(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, new JointId[] { JointId.LeftKnee }, new JointId[] { JointId.LeftAnkle }, true),
                new SegmentDefinition(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, new JointId[] { JointId.RightHip }, new JointId[] { JointId.RightKnee }, true),
                new SegmentDefinition(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, new JointId[] { JointId.RightKnee }, new JointId[] { JointId.RightAnkle }, true)
            };
        }
    }
}
