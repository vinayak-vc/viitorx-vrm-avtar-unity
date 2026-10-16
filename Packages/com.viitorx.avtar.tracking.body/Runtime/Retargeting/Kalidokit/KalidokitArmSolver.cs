using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Retargeting.Kalidokit {
    /// <summary>
    /// The arm rotations Kalidokit's <c>calcArms</c> produces, as Euler radians per bone (Kalidokit /
    /// three-vrm convention). Consumed by <see cref="KalidokitRetargeter"/>, which maps them onto the
    /// Unity humanoid bones.
    /// </summary>
    public struct KalidokitArmPose {
        public Vector3 UpperArmRight;
        public Vector3 UpperArmLeft;
        public Vector3 LowerArmRight;
        public Vector3 LowerArmLeft;
        public Vector3 HandRight;
        public Vector3 HandLeft;
    }

    /// <summary>
    /// C# port of Kalidokit's <c>PoseSolver/calcArms</c> + <c>rigArm</c> (yeemachine/kalidokit, MIT) —
    /// ADR-022. Computes UpperArm / LowerArm / Hand rotations from the pose landmarks with the roll
    /// derived from the limb geometry (elbow bend + shoulder plane), which is what the old vector-FK path
    /// could not do (it guessed limb roll from a global reference → the forearm helicopter). The magic
    /// scale/clamp constants are Kalidokit's, verbatim.
    ///
    /// <paramref name="lm"/> is a 33-slot array of landmark positions indexed by <see cref="JointId"/>
    /// (== MediaPipe pose indices), already adapted to Kalidokit's convention (X right, Y down, Z toward
    /// camera) by the caller.
    /// </summary>
    public static class KalidokitArmSolver {
        private const int RightInvert = 1;   // Kalidokit RIGHT
        private const int LeftInvert = -1;   // Kalidokit LEFT

        public static KalidokitArmPose Solve(Vector3[] lm) {
            // Pure rotation calculations (Kalidokit calcArms). NOTE the r/l cross-mapping is Kalidokit's:
            // its "r" (right) is computed from landmarks 11/13/15 (MediaPipe LEFT-side indices) and vice
            // versa — preserved verbatim so the tuned rig constants line up.
            Vector3 upperArmR = KMath.FindRotation(lm[11], lm[13]);
            Vector3 upperArmL = KMath.FindRotation(lm[12], lm[14]);
            upperArmR.y = KMath.AngleBetween3DCoords(lm[12], lm[11], lm[13]);
            upperArmL.y = KMath.AngleBetween3DCoords(lm[11], lm[12], lm[14]);

            Vector3 lowerArmR = KMath.FindRotation(lm[13], lm[15]);
            Vector3 lowerArmL = KMath.FindRotation(lm[14], lm[16]);
            lowerArmR.y = KMath.AngleBetween3DCoords(lm[11], lm[13], lm[15]);
            lowerArmL.y = KMath.AngleBetween3DCoords(lm[12], lm[14], lm[16]);
            lowerArmR.z = KMath.Clamp(lowerArmR.z, -2.14f, 0f);
            lowerArmL.z = KMath.Clamp(lowerArmL.z, -2.14f, 0f);

            Vector3 handR = KMath.FindRotation(lm[15], 0.5f * (lm[17] + lm[19]));
            Vector3 handL = KMath.FindRotation(lm[16], 0.5f * (lm[18] + lm[20]));

            RigArm(ref upperArmR, ref lowerArmR, ref handR, RightInvert);
            RigArm(ref upperArmL, ref lowerArmL, ref handL, LeftInvert);

            KalidokitArmPose pose;
            pose.UpperArmRight = upperArmR;
            pose.UpperArmLeft = upperArmL;
            pose.LowerArmRight = lowerArmR;
            pose.LowerArmLeft = lowerArmL;
            pose.HandRight = handR;
            pose.HandLeft = handL;
            return pose;
        }

        // Kalidokit rigArm — clamps normalized rotations into human limits (radians). Order matters:
        // upperArm.y reads the ORIGINAL lowerArm.x/z before lowerArm is scaled, so lowerArm is scaled after.
        private static void RigArm(ref Vector3 upperArm, ref Vector3 lowerArm, ref Vector3 hand, int invert) {
            float lowerArmXOriginal = lowerArm.x;
            float lowerArmZOriginal = lowerArm.z;

            upperArm.z *= -2.3f * invert;
            upperArm.y *= KMath.Pi * invert;
            upperArm.y -= lowerArmXOriginal;
            upperArm.y -= -invert * Mathf.Max(lowerArmZOriginal, 0f);
            upperArm.x -= 0.3f * invert;

            lowerArm.z *= -2.14f * invert;
            lowerArm.y *= 2.14f * invert;
            lowerArm.x *= 2.14f * invert;

            upperArm.x = KMath.Clamp(upperArm.x, -0.5f, KMath.Pi);
            lowerArm.x = KMath.Clamp(lowerArm.x, -0.3f, 0.3f);

            hand.y = KMath.Clamp(hand.z * 2f, -0.6f, 0.6f); // side to side
            hand.z = hand.z * -2.3f * invert;               // up down
        }
    }
}
