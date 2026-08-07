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
        // H6: a leg is only driven when its ankle sits at least this far BELOW the hips along torso-up.
        // Occluded/out-of-frame lower bodies make BlazePose/RTMW3D hallucinate collapsed legs with
        // pass-through confidence; without this the FK path folds/splays them (the IK path already gated
        // on this, but the FK path — the shipping default — did not). Metres, hip-relative.
        private const float MinLegDropMetres = 0.35f;

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
            public readonly bool IsLeg;
            public readonly bool IsLeftLeg;

            public bool Active;

            public BoundSegment(Transform bone, Quaternion restRotation, Vector3 restDirection, JointId[] fromJoints, JointId[] toJoints, bool isLimb, bool isLeg, bool isLeftLeg) {
                Bone = bone;
                RestRotation = restRotation;
                RestDirection = restDirection;
                FromJoints = fromJoints;
                ToJoints = toJoints;
                IsLimb = isLimb;
                IsLeg = isLeg;
                IsLeftLeg = isLeftLeg;
                Active = false;
            }
        }

        private sealed class BasisBone {
            public readonly Transform Bone;
            public readonly Quaternion AvatarRestRotation;
            public readonly JointId RightFrom;
            public readonly JointId RightTo;
            // When true, this bone uses a STABLE vertical up instead of the live torso-up. Used for the Hips
            // (pelvis): with the shared torso-up, a forward waist bend tilts the up-vector and rotates BOTH
            // hips and spine together → the whole body tilts rigidly. Anchoring the hips to vertical means the
            // hips only track facing (yaw) / side-lean from the hip line and stay upright, so the SPINE (which
            // keeps the live torso-up) carries the forward bend → a real waist bend, not a rigid tilt.
            public readonly bool StableUp;

            public Quaternion NeutralBasis;
            public bool HasNeutral;

            public BasisBone(Transform bone, Quaternion avatarRestRotation, JointId rightFrom, JointId rightTo, bool stableUp) {
                Bone = bone;
                AvatarRestRotation = avatarRestRotation;
                RightFrom = rightFrom;
                RightTo = rightTo;
                StableUp = stableUp;
                NeutralBasis = Quaternion.identity;
                HasNeutral = false;
            }
        }

        private readonly SegmentDefinition[] definitions;
        private readonly List<BoundSegment> boundSegments;
        private readonly List<BasisBone> basisBones;

        private bool torsoActive;
        private bool armsLegsDrivenExternally;
        private bool trackLegs = true;
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

        /// <summary>
        /// Enable/disable driving the leg bones from pose landmarks. When false the legs are held at their
        /// bind (straight standing) pose — used when the lower body is occluded / out of frame, where
        /// BlazePose hallucinates the legs and would otherwise bend or splay the avatar.
        /// </summary>
        public void SetTrackLegs(bool value) {
            trackLegs = value;
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
            // Only lock the neutral facing while the user is roughly FACING the camera (the shoulder line runs
            // across X, not into Z). This auto-calibrates without the C key: the neutral is captured the moment
            // you face the camera, so a turned/back-facing start no longer locks a bad neutral (which looked
            // like "flipped hands" until C was pressed). Until then the torso simply rests front-facing.
            Vector3 shoulderLine = frame.GetLandmark(JointId.LeftShoulder).Position - frame.GetLandmark(JointId.RightShoulder).Position;
            bool frontFacing = Mathf.Abs(shoulderLine.x) >= Mathf.Abs(shoulderLine.z);
            bool canCaptureNeutral = strongUp && neutralWarmupFrames >= NeutralWarmupRequired && frontFacing;
            int index = 0;
            while (index < basisBones.Count) {
                BasisBone basisBone = basisBones[index];
                index = index + 1;
                Vector3 right = frame.GetLandmark(basisBone.RightTo).Position - frame.GetLandmark(basisBone.RightFrom).Position;
                if (right.magnitude < MinTorsoVectorMagnitude) {
                    continue;
                }
                // Hips use a stable vertical up AND a horizontally-projected hip-line, so they track only
                // facing (yaw): any vertical component of the tracked hip line (sensor noise / off-centre
                // body-orientation error) can no longer roll the whole avatar → no rigid whole-body tilt,
                // and uprightness no longer depends on a good neutral calibration. The spine keeps the live
                // torso-up + shoulder line so it still carries real lean/bend. See BasisBone.StableUp.
                Vector3 boneUp;
                Vector3 boneRight;
                if (basisBone.StableUp) {
                    boneUp = Vector3.up;
                    boneRight = new Vector3(right.x, 0f, right.z);
                } else {
                    boneUp = up;
                    boneRight = right;
                }
                Quaternion currentBasis;
                if (!RotationFromVectors.TryBasis(boneRight, boneUp, out currentBasis)) {
                    continue;
                }
                if (!basisBone.HasNeutral) {
                    if (!canCaptureNeutral) {
                        // LOW-B: rest front-facing until a new neutral is captured, rather than freezing at
                        // the last written pose (which looked stuck after a mid-session Recalibrate).
                        basisBone.Bone.rotation = basisBone.AvatarRestRotation;
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
            bool leftLegOk;
            bool rightLegOk;
            ComputeLegPlausibility(frame, out leftLegOk, out rightLegOk);
            int index = 0;
            while (index < boundSegments.Count) {
                BoundSegment segment = boundSegments[index];
                index = index + 1;
                if (segment.IsLeg) {
                    // Hold the leg at its bind (straight) pose when leg tracking is off OR the leg is not
                    // plausibly standing (H6: ankle not clearly below the hips → occluded/hallucinated).
                    bool sideOk = segment.IsLeftLeg ? leftLegOk : rightLegOk;
                    if (!trackLegs || !sideOk) {
                        segment.Active = false;
                        segment.Bone.rotation = segment.RestRotation;
                        continue;
                    }
                }
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

        private static bool IsLegBone(HumanBodyBones bone) {
            return bone == HumanBodyBones.LeftUpperLeg || bone == HumanBodyBones.LeftLowerLeg
                || bone == HumanBodyBones.RightUpperLeg || bone == HumanBodyBones.RightLowerLeg;
        }

        private static bool IsLeftLegBone(HumanBodyBones bone) {
            return bone == HumanBodyBones.LeftUpperLeg || bone == HumanBodyBones.LeftLowerLeg;
        }

        // H6: is a leg confidently "standing" — its ankle at least MinLegDropMetres below the hips along
        // torso-up? If neither the torso basis nor the drop is present, returns false (hold the leg).
        private void ComputeLegPlausibility(PoseFrame frame, out bool leftLegOk, out bool rightLegOk) {
            leftLegOk = false;
            rightLegOk = false;
            Vector3 midHip = 0.5f * (frame.GetLandmark(JointId.LeftHip).Position + frame.GetLandmark(JointId.RightHip).Position);
            Vector3 midShoulder = 0.5f * (frame.GetLandmark(JointId.LeftShoulder).Position + frame.GetLandmark(JointId.RightShoulder).Position);
            Vector3 torsoUp = midShoulder - midHip;
            if (torsoUp.magnitude < MinTorsoVectorMagnitude) {
                return;
            }
            torsoUp = torsoUp.normalized;
            float leftDrop = Vector3.Dot(-torsoUp, frame.GetLandmark(JointId.LeftAnkle).Position - midHip);
            float rightDrop = Vector3.Dot(-torsoUp, frame.GetLandmark(JointId.RightAnkle).Position - midHip);
            leftLegOk = leftDrop >= MinLegDropMetres;
            rightLegOk = rightDrop >= MinLegDropMetres;
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
                boundSegments.Add(new BoundSegment(bone, bone.rotation, restDirection, definition.FromJoints, definition.ToJoints, definition.IsLimb, IsLegBone(definition.Bone), IsLeftLegBone(definition.Bone)));
            }
        }

        private void BindBasisBones(Animator animator) {
            // Hips: stable vertical up (yaw/side-lean only, stays upright). Spine: live torso-up (carries bend).
            AddBasisBone(animator, HumanBodyBones.Hips, JointId.LeftHip, JointId.RightHip, true);
            AddBasisBone(animator, HumanBodyBones.Spine, JointId.LeftShoulder, JointId.RightShoulder, false);
        }

        private void AddBasisBone(Animator animator, HumanBodyBones bone, JointId rightFrom, JointId rightTo, bool stableUp) {
            Transform transform = animator.GetBoneTransform(bone);
            if (transform == null) {
                return;
            }
            basisBones.Add(new BasisBone(transform, transform.rotation, rightFrom, rightTo, stableUp));
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
            // Neck/head orientation is intentionally NOT driven from pose landmarks. Deriving neck pitch
            // from midShoulder->nose (or ->ear) is unreliable under the tuned tracking Z convention and
            // craned the head UP instead of forward (see ADR-011). The head now rests forward; head
            // orientation will later come from the face landmarker's head pose (SDS-007 §4). The limb
            // segments below are IK-owned and used by this FK path only when the IK driver is disabled.
            return new SegmentDefinition[] {
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
