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

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void Bind(Animator animator) {
            Unbind();
            if (animator == null || !animator.isHuman) {
                return;
            }

            BindHandFingers(animator, true);
            BindHandFingers(animator, false);
            bound = leftFingerJoints.Count > 0 || rightFingerJoints.Count > 0;
        }

        public void Unbind() {
            leftFingerJoints.Clear();
            rightFingerJoints.Clear();
            bound = false;
        }

        public void Apply(HandFrame frame) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }

            ApplyHandFingers(leftFingerJoints, frame.LeftThumbCurl, frame.LeftIndexCurl, frame.LeftMiddleCurl, frame.LeftRingCurl, frame.LeftLittleCurl);
            ApplyHandFingers(rightFingerJoints, frame.RightThumbCurl, frame.RightIndexCurl, frame.RightMiddleCurl, frame.RightRingCurl, frame.RightLittleCurl);
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
