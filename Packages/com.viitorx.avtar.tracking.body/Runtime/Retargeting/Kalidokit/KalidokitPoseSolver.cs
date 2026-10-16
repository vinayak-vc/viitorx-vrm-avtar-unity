using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Retargeting.Kalidokit {
    /// <summary>
    /// Whole-body pose Kalidokit produces (Euler radians per bone, three-vrm convention). Rotations only —
    /// hips world position is handled elsewhere (OAK root / not driven for webcam). ADR-022.
    /// </summary>
    public struct KalidokitFullPose {
        public Vector3 Hips;
        public Vector3 Spine;
        public Vector3 UpperArmRight;
        public Vector3 UpperArmLeft;
        public Vector3 LowerArmRight;
        public Vector3 LowerArmLeft;
        public Vector3 HandRight;
        public Vector3 HandLeft;
        public Vector3 UpperLegRight;
        public Vector3 UpperLegLeft;
        public Vector3 LowerLegRight;
        public Vector3 LowerLegLeft;
    }

    /// <summary>
    /// C# port of Kalidokit's full PoseSolver (yeemachine/kalidokit, MIT) — arms (<see cref="KalidokitArmSolver"/>)
    /// + hips/spine (<c>calcHips</c>, rotation only) + legs (<c>calcLegs</c>), ADR-022. Verbatim formulas +
    /// magic constants. <paramref name="lm"/> is 33 landmark positions indexed by <see cref="JointId"/>
    /// (== MediaPipe pose indices), already adapted to Kalidokit's convention (X right, Y down, Z toward
    /// camera) by the caller.
    /// </summary>
    public static class KalidokitPoseSolver {
        private const float UpperLegZOffset = 0.1f; // Kalidokit offsets.upperLeg.z

        public static KalidokitFullPose Solve(Vector3[] lm) {
            KalidokitArmPose arms = KalidokitArmSolver.Solve(lm);

            KalidokitFullPose pose;
            pose.UpperArmRight = arms.UpperArmRight;
            pose.UpperArmLeft = arms.UpperArmLeft;
            pose.LowerArmRight = arms.LowerArmRight;
            pose.LowerArmLeft = arms.LowerArmLeft;
            pose.HandRight = arms.HandRight;
            pose.HandLeft = arms.HandLeft;

            CalcHipsAndSpine(lm, out pose.Hips, out pose.Spine);
            CalcLegs(lm, out pose.UpperLegRight, out pose.UpperLegLeft, out pose.LowerLegRight, out pose.LowerLegLeft);
            return pose;
        }

        // Kalidokit calcHips (rotation only) + rigHips (×PI). Hips from the hip line (23,24), spine from the
        // shoulder line (11,12), each via the 2-point rollPitchYaw with the same -PI/PI jump fixes.
        private static void CalcHipsAndSpine(Vector3[] lm, out Vector3 hipsEuler, out Vector3 spineEuler) {
            Vector3 hips = KMath.RollPitchYaw2(lm[23], lm[24]);
            if (hips.y > 0.5f) {
                hips.y -= 2f;
            }
            hips.y += 0.5f;
            if (hips.z > 0f) {
                hips.z = 1f - hips.z;
            }
            if (hips.z < 0f) {
                hips.z = -1f - hips.z;
            }
            float turnHips = KMath.Remap(Mathf.Abs(hips.y), 0.2f, 0.4f);
            hips.z *= 1f - turnHips;
            hips.x = 0f;

            Vector3 spine = KMath.RollPitchYaw2(lm[11], lm[12]);
            if (spine.y > 0.5f) {
                spine.y -= 2f;
            }
            spine.y += 0.5f;
            if (spine.z > 0f) {
                spine.z = 1f - spine.z;
            }
            if (spine.z < 0f) {
                spine.z = -1f - spine.z;
            }
            float turnSpine = KMath.Remap(Mathf.Abs(spine.y), 0.2f, 0.4f);
            spine.z *= 1f - turnSpine;
            spine.x = 0f;

            hipsEuler = hips * KMath.Pi;
            spineEuler = spine * KMath.Pi;
        }

        // Kalidokit calcLegs + rigLeg. r/l cross-mapping is Kalidokit's (its "r" reads MediaPipe LEFT-leg
        // indices 23/25/27), preserved verbatim so the rig clamps line up.
        private static void CalcLegs(Vector3[] lm, out Vector3 upperLegR, out Vector3 upperLegL, out Vector3 lowerLegR, out Vector3 lowerLegL) {
            float rUpperTheta, rUpperPhi, lUpperTheta, lUpperPhi;
            KMath.GetSphericalCoords(lm[23], lm[25], out rUpperTheta, out rUpperPhi);
            KMath.GetSphericalCoords(lm[24], lm[26], out lUpperTheta, out lUpperPhi);

            float rLowerTheta, rLowerPhi, lLowerTheta, lLowerPhi;
            KMath.GetRelativeSphericalCoords(lm[23], lm[25], lm[27], out rLowerTheta, out rLowerPhi);
            KMath.GetRelativeSphericalCoords(lm[24], lm[26], lm[28], out lLowerTheta, out lLowerPhi);

            Vector3 hipRotation = KMath.FindRotation(lm[23], lm[24]);

            Vector3 upperR = new Vector3(rUpperTheta, rLowerPhi, rUpperPhi - hipRotation.z);
            Vector3 upperL = new Vector3(lUpperTheta, lLowerPhi, lUpperPhi - hipRotation.z);
            Vector3 lowerR = new Vector3(-Mathf.Abs(rLowerTheta), 0f, 0f);
            Vector3 lowerL = new Vector3(-Mathf.Abs(lLowerTheta), 0f, 0f);

            upperLegR = RigUpperLeg(upperR, 1);
            upperLegL = RigUpperLeg(upperL, -1);
            lowerLegR = new Vector3(lowerR.x * KMath.Pi, lowerR.y * KMath.Pi, lowerR.z * KMath.Pi);
            lowerLegL = new Vector3(lowerL.x * KMath.Pi, lowerL.y * KMath.Pi, lowerL.z * KMath.Pi);
        }

        private static Vector3 RigUpperLeg(Vector3 upperLeg, int invert) {
            return new Vector3(
                KMath.Clamp(upperLeg.x, 0f, 0.5f) * KMath.Pi,
                KMath.Clamp(upperLeg.y, -0.25f, 0.25f) * KMath.Pi,
                KMath.Clamp(upperLeg.z, -0.5f, 0.5f) * KMath.Pi + invert * UpperLegZOffset);
        }
    }
}
