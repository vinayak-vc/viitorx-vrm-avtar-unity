using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// F-29 — the 21 hand landmarks per hand, as positions, in the same hip-relative space as
    /// <see cref="PoseFrame"/>'s body landmarks.
    ///
    /// WHY THIS EXISTS ALONGSIDE <see cref="HandFrame"/>. HandFrame carries five finger CURLS and a
    /// palm quaternion — everything a bone-rotation retarget needs and nothing more, because that is
    /// all the avatar can consume. The sidecar has been sending the full 21 points per hand all
    /// along; the provider read them, derived the curls, and dropped them on the floor. Anything that
    /// wants to DRAW a hand, measure a pinch in real millimetres, or hit-test a fingertip needs the
    /// positions, and cannot get them back from five scalars.
    ///
    /// This does not widen HandFrame, deliberately: the avatar path consumes that type and has no use
    /// for these, and a contract that grows a field per caller stops being a contract.
    ///
    /// Landmark order is MediaPipe / COCO-WholeBody, which is what the sidecar sends:
    ///   0 wrist, 1-4 thumb (CMC, MCP, IP, TIP), 5-8 index, 9-12 middle, 13-16 ring, 17-20 little.
    /// Each finger runs proximal to distal, so index 4/8/12/16/20 are the five fingertips.
    ///
    /// Reusable and mutable, filled in place by the producer each frame.
    /// </summary>
    public sealed class RawHandFrame {
        public const int LandmarkCount = 21;

        /// <summary>Wrist. The root every other landmark is measured against for a gesture.</summary>
        public const int Wrist = 0;

        /// <summary>The five fingertips, thumb to little — the points an interaction actually uses.</summary>
        public static readonly int[] Fingertips = { 4, 8, 12, 16, 20 };

        /// <summary>Each finger's knuckle (MCP), thumb to little. Paired with <see cref="Fingertips"/>
        /// by index, so finger n runs from Knuckles[n] to Fingertips[n].</summary>
        public static readonly int[] Knuckles = { 2, 5, 9, 13, 17 };

        /// <summary>Bones for drawing a hand: palm fan from the wrist, then each finger's chain.</summary>
        public static readonly int[] Bones = {
            0, 1, 1, 2, 2, 3, 3, 4,          // thumb
            0, 5, 5, 6, 6, 7, 7, 8,          // index
            0, 9, 9, 10, 10, 11, 11, 12,     // middle
            0, 13, 13, 14, 14, 15, 15, 16,   // ring
            0, 17, 17, 18, 18, 19, 19, 20,   // little
            5, 9, 9, 13, 13, 17,             // knuckle ridge, so the palm reads as a surface
        };

        private readonly Vector3[] left = new Vector3[LandmarkCount];
        private readonly Vector3[] right = new Vector3[LandmarkCount];

        private bool leftTracked;
        private bool rightTracked;
        private double timestampSeconds;

        public bool LeftTracked {
            get {
                return leftTracked;
            }
        }

        public bool RightTracked {
            get {
                return rightTracked;
            }
        }

        public double TimestampSeconds {
            get {
                return timestampSeconds;
            }
        }

        /// <summary>Hip-relative position of one landmark. Reads zero when that hand is untracked —
        /// always check <see cref="LeftTracked"/> / <see cref="RightTracked"/> first, because the
        /// sidecar omits a hand entirely rather than sending a low-confidence one (audit H7).</summary>
        public Vector3 Landmark(bool isLeft, int index) {
            if (index < 0 || index >= LandmarkCount) {
                return Vector3.zero;
            }
            return isLeft ? left[index] : right[index];
        }

        public void SetHand(bool isLeft, Vector3[] points) {
            Vector3[] target = isLeft ? left : right;
            if (points == null || points.Length < LandmarkCount) {
                int j = 0;
                while (j < LandmarkCount) {
                    target[j] = Vector3.zero;
                    j = j + 1;
                }
                if (isLeft) {
                    leftTracked = false;
                } else {
                    rightTracked = false;
                }
                return;
            }
            int i = 0;
            while (i < LandmarkCount) {
                target[i] = points[i];
                i = i + 1;
            }
            if (isLeft) {
                leftTracked = true;
            } else {
                rightTracked = true;
            }
        }

        public void SetMeta(double timestamp) {
            timestampSeconds = timestamp;
        }

        public void MarkInvalid(double timestamp) {
            timestampSeconds = timestamp;
            leftTracked = false;
            rightTracked = false;
        }

        public void CopyTo(RawHandFrame target) {
            if (target == null) {
                return;
            }
            int i = 0;
            while (i < LandmarkCount) {
                target.left[i] = left[i];
                target.right[i] = right[i];
                i = i + 1;
            }
            target.leftTracked = leftTracked;
            target.rightTracked = rightTracked;
            target.timestampSeconds = timestampSeconds;
        }
    }
}
