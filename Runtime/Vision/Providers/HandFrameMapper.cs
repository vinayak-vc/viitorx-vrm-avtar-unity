using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Turns a <see cref="HandsMessage"/> into the project's existing <see cref="HandFrame"/> (finger curls and a palm rotation per hand), so
    /// anything that already consumes a hand provider - the humanoid hand retargeter, the VRM avatar - can run from a camera that sees hands only.
    /// The richer data (21 landmarks, position, gesture, pinch) stays on the message; this is the narrow bridge.
    ///
    /// Per side the best hand is used (tracked over held, then the more confident). A side with no hand reads as untracked with zero curls, which
    /// the consumers already treat as "not detected". <c>swapSides</c> exchanges left and right: use it when the avatar is a mirror of the user
    /// (the wire's sides are the user's own).
    /// </summary>
    public static class HandFrameMapper {
        private static readonly float[] ZeroCurls = new float[HandData.FingerCount];

        public static void Fill(HandFrame frame, HandsMessage message, bool swapSides, double timestampSeconds) {
            HandData left;
            HandData right;
            message.TryFindSide(swapSides ? HandSide.Right : HandSide.Left, out left);
            message.TryFindSide(swapSides ? HandSide.Left : HandSide.Right, out right);
            if (left == null && right == null) {
                frame.SetCurls(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                frame.SetWristRotations(Quaternion.identity, false, Quaternion.identity, false);
                frame.MarkInvalid(timestampSeconds);
                return;
            }
            float[] l = left != null ? left.FingerCurls : ZeroCurls;
            float[] r = right != null ? right.FingerCurls : ZeroCurls;
            frame.SetCurls(l[0], l[1], l[2], l[3], l[4], r[0], r[1], r[2], r[3], r[4]);
            frame.SetWristRotations(
                left != null ? PalmOf(left) : Quaternion.identity, left != null,
                right != null ? PalmOf(right) : Quaternion.identity, right != null);
            frame.SetMeta(timestampSeconds, true);
        }

        public static Quaternion PalmOf(HandData hand) {
            Vector3[] w = hand.WorldLandmarks;
            return VisionMath.PalmRotation(w[0], w[5], w[9], w[17]);
        }
    }
}
