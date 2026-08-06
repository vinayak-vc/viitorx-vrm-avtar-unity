using System.Collections.Generic;
using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Retargets finger curl values from <see cref="HandFrame"/> onto humanoid avatar finger joints.
    /// Rotates proximal, intermediate, and distal finger joints along their local bend axis (pitch).
    /// </summary>
    public sealed class HumanoidHandRetargeter {
        private sealed class FingerJointPair {
            public readonly Transform transform;
            public readonly Quaternion restLocalRotation;
            public readonly Vector3 bendAxis;
            public readonly float maxDegrees;

            public FingerJointPair(Transform transform, Vector3 bendAxis, float maxDegrees) {
                this.transform = transform;
                this.restLocalRotation = transform != null ? transform.localRotation : Quaternion.identity;
                this.bendAxis = bendAxis;
                this.maxDegrees = maxDegrees;
            }
        }

        private readonly List<FingerJointPair> leftFingerJoints = new List<FingerJointPair>();
        private readonly List<FingerJointPair> rightFingerJoints = new List<FingerJointPair>();
        private bool bound;

        private Transform leftHand;
        private Transform rightHand;
        private Quaternion leftNeutralPalm = Quaternion.identity;
        private Quaternion rightNeutralPalm = Quaternion.identity;
        private bool leftHasNeutral;
        private bool rightHasNeutral;
        private float wristWeight = 0.7f;

        public bool IsBound {
            get {
                return bound;
            }
        }

        /// <summary>
        /// Blend weight (0..1) for wrist/palm orientation. 0 disables wrist rotation (fingers still curl).
        /// The mapping from MediaPipe hand landmarks to the avatar wrist is convention-sensitive; this is
        /// exposed so it can be tuned live like the pose mirror flags.
        /// </summary>
        public void SetWristWeight(float weight) {
            wristWeight = Mathf.Clamp01(weight);
        }

        /// <summary>Re-capture each hand's neutral palm orientation on the next tracked frame.</summary>
        public void Recalibrate() {
            leftHasNeutral = false;
            rightHasNeutral = false;
        }

        public void Bind(Animator animator) {
            Unbind();
            if (animator == null || !animator.isHuman) {
                return;
            }

            BindHandFingers(animator, true);
            BindHandFingers(animator, false);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftHasNeutral = false;
            rightHasNeutral = false;
            bound = leftFingerJoints.Count > 0 || rightFingerJoints.Count > 0 || leftHand != null || rightHand != null;
        }

        public void Unbind() {
            leftFingerJoints.Clear();
            rightFingerJoints.Clear();
            leftHand = null;
            rightHand = null;
            leftHasNeutral = false;
            rightHasNeutral = false;
            bound = false;
        }

        public void Apply(HandFrame frame) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }

            ApplyHandFingers(leftFingerJoints, frame.LeftThumbCurl, frame.LeftIndexCurl, frame.LeftMiddleCurl, frame.LeftRingCurl, frame.LeftLittleCurl);
            ApplyHandFingers(rightFingerJoints, frame.RightThumbCurl, frame.RightIndexCurl, frame.RightMiddleCurl, frame.RightRingCurl, frame.RightLittleCurl);
            ApplyWrist(true, frame.LeftWristTracked, frame.LeftWristRotation);
            ApplyWrist(false, frame.RightWristTracked, frame.RightWristRotation);
        }

        // Rotate the hand by the palm's change since a captured neutral (delta-from-neutral, like the torso
        // basis) so the absolute landmark->bone axis convention cancels and only relative wrist motion shows.
        // Applied in world space on top of the IK-posed forearm; runs after IK in LateUpdate so it survives.
        private void ApplyWrist(bool isLeft, bool tracked, Quaternion palm) {
            Transform hand = isLeft ? leftHand : rightHand;
            if (hand == null || !tracked || wristWeight <= 0f) {
                return;
            }
            bool hasNeutral = isLeft ? leftHasNeutral : rightHasNeutral;
            if (!hasNeutral) {
                if (isLeft) {
                    leftNeutralPalm = palm;
                    leftHasNeutral = true;
                } else {
                    rightNeutralPalm = palm;
                    rightHasNeutral = true;
                }
                return;
            }
            Quaternion neutral = isLeft ? leftNeutralPalm : rightNeutralPalm;
            Quaternion worldDelta = palm * Quaternion.Inverse(neutral);
            hand.rotation = Quaternion.Slerp(hand.rotation, worldDelta * hand.rotation, wristWeight);
        }

        private void BindHandFingers(Animator animator, bool isLeft) {
            List<FingerJointPair> jointsList = isLeft ? leftFingerJoints : rightFingerJoints;
            Vector3 bendAxis = Vector3.right;

            HumanBodyBones[] proximalBones = isLeft ? new HumanBodyBones[] {
                HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal
            } : new HumanBodyBones[] {
                HumanBodyBones.RightThumbProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal
            };

            HumanBodyBones[] intermediateBones = isLeft ? new HumanBodyBones[] {
                HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftLittleIntermediate
            } : new HumanBodyBones[] {
                HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightLittleIntermediate
            };

            HumanBodyBones[] distalBones = isLeft ? new HumanBodyBones[] {
                HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleDistal
            } : new HumanBodyBones[] {
                HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal
            };

            int i = 0;
            while (i < 5) {
                AddBoneIfPresent(jointsList, animator, proximalBones[i], bendAxis, 50f);
                AddBoneIfPresent(jointsList, animator, intermediateBones[i], bendAxis, 60f);
                AddBoneIfPresent(jointsList, animator, distalBones[i], bendAxis, 40f);
                i = i + 1;
            }
        }

        private static void AddBoneIfPresent(List<FingerJointPair> list, Animator animator, HumanBodyBones boneId, Vector3 bendAxis, float maxDegrees) {
            Transform boneTransform = animator.GetBoneTransform(boneId);
            if (boneTransform != null) {
                list.Add(new FingerJointPair(boneTransform, bendAxis, maxDegrees));
            }
        }

        private static void ApplyHandFingers(List<FingerJointPair> list, float thumbCurl, float indexCurl, float middleCurl, float ringCurl, float littleCurl) {
            float[] curls = new float[] { thumbCurl, thumbCurl, thumbCurl, indexCurl, indexCurl, indexCurl, middleCurl, middleCurl, middleCurl, ringCurl, ringCurl, ringCurl, littleCurl, littleCurl, littleCurl };
            int i = 0;
            int count = list.Count;
            while (i < count && i < curls.Length) {
                FingerJointPair joint = list[i];
                if (joint.transform != null)
                {
                    float curlAmount = Mathf.Clamp01(curls[i]);
                    Quaternion curlRotation = Quaternion.AngleAxis(curlAmount * joint.maxDegrees, joint.bendAxis);
                    joint.transform.localRotation = joint.restLocalRotation * curlRotation;
                }
                i = i + 1;
            }
        }
    }
}
