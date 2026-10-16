using UnityEngine;

namespace VirtualMirror.Retargeting.Kalidokit {
    /// <summary>
    /// C# port of the math helpers from Kalidokit (yeemachine/kalidokit, MIT) — the parts the arm/hand
    /// solvers use: the relative 2D-angle rotation extraction (<see cref="FindRotation"/>), the 3-point
    /// plane roll/pitch/yaw (<see cref="RollPitchYaw"/>), the joint bend angle
    /// (<see cref="AngleBetween3DCoords"/>), and the two angle normalizers. Ported verbatim (same formulas,
    /// same normalization) so the proven solver behaviour is preserved; only the container type changes
    /// (Unity <see cref="Vector3"/> instead of Kalidokit's Vector class). ADR-022.
    ///
    /// IMPORTANT: these operate on landmark positions in Kalidokit's expected convention (MediaPipe world:
    /// X right, Y DOWN, Z toward camera). The caller adapts our PoseFrame (Unity, Y up) into that
    /// convention before calling. Angles are radians.
    /// </summary>
    public static class KMath {
        public const float Pi = Mathf.PI;
        public const float TwoPi = Mathf.PI * 2f;

        public static float Clamp(float val, float min, float max) {
            return Mathf.Max(Mathf.Min(val, max), min);
        }

        // atan2 of the 2D vector (cx,cy)->(ex,ey).
        public static float Find2DAngle(float cx, float cy, float ex, float ey) {
            float dy = ey - cy;
            float dx = ex - cx;
            return Mathf.Atan2(dy, dx);
        }

        // Kalidokit Vector.normalizeRadians — ported verbatim (returns value scaled to roughly [-1,1]).
        public static float NormalizeRadians(float radians) {
            if (radians >= Pi / 2f) {
                radians -= TwoPi;
            }
            if (radians <= -Pi / 2f) {
                radians += TwoPi;
                radians = Pi - radians;
            }
            return radians / Pi;
        }

        // Kalidokit Vector.normalizeAngle — ported verbatim (wraps to (-PI,PI], returns scaled to [-1,1]).
        public static float NormalizeAngle(float radians) {
            float angle = radians % TwoPi;
            if (angle > Pi) {
                angle = angle - TwoPi;
            } else if (angle < -Pi) {
                angle = TwoPi + angle;
            }
            return angle / Pi;
        }

        // Kalidokit Vector.findRotation — per-axis relative rotation from a->b (normalized).
        public static Vector3 FindRotation(Vector3 a, Vector3 b) {
            return new Vector3(
                NormalizeRadians(Find2DAngle(a.z, a.x, b.z, b.x)),
                NormalizeRadians(Find2DAngle(a.z, a.y, b.z, b.y)),
                NormalizeRadians(Find2DAngle(a.x, a.y, b.x, b.y)));
        }

        // Kalidokit Vector.angleBetween3DCoords — the bend angle at b formed by a-b-c (normalized).
        public static float AngleBetween3DCoords(Vector3 a, Vector3 b, Vector3 c) {
            Vector3 v1 = a - b;
            Vector3 v2 = c - b;
            if (v1.sqrMagnitude < 1e-12f || v2.sqrMagnitude < 1e-12f) {
                return 0f;
            }
            float dot = Vector3.Dot(v1.normalized, v2.normalized);
            float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            return NormalizeRadians(angle);
        }

        // Kalidokit Vector.rollPitchYaw (2-point form, no c) — used by the hips + spine.
        public static Vector3 RollPitchYaw2(Vector3 a, Vector3 b) {
            return new Vector3(
                NormalizeAngle(Find2DAngle(a.z, a.y, b.z, b.y)),
                NormalizeAngle(Find2DAngle(a.z, a.x, b.z, b.x)),
                NormalizeAngle(Find2DAngle(a.x, a.y, b.x, b.y)));
        }

        // Kalidokit Vector.rollPitchYaw (3-point form) — the roll/pitch/yaw of the plane through a,b,c.
        // Used by the hand solver for the wrist. Returns normalized-angle values.
        public static Vector3 RollPitchYaw(Vector3 a, Vector3 b, Vector3 c) {
            Vector3 qb = b - a;
            Vector3 qc = c - a;
            Vector3 n = Vector3.Cross(qb, qc);
            if (n.sqrMagnitude < 1e-12f || qb.sqrMagnitude < 1e-12f) {
                return Vector3.zero;
            }
            Vector3 unitZ = n.normalized;
            Vector3 unitX = qb.normalized;
            Vector3 unitY = Vector3.Cross(unitZ, unitX);

            float beta = SafeAsin(unitZ.x);
            float alpha = SafeAtan2(-unitZ.y, unitZ.z);
            float gamma = SafeAtan2(-unitY.x, unitX.x);
            return new Vector3(NormalizeAngle(alpha), NormalizeAngle(beta), NormalizeAngle(gamma));
        }

        private static float SafeAsin(float v) {
            float r = Mathf.Asin(Mathf.Clamp(v, -1f, 1f));
            return float.IsNaN(r) ? 0f : r;
        }

        private static float SafeAtan2(float y, float x) {
            if (Mathf.Approximately(y, 0f) && Mathf.Approximately(x, 0f)) {
                return 0f;
            }
            return Mathf.Atan2(y, x);
        }

        // Kalidokit helpers.remap — clamp to [min,max] then scale to 0..1.
        public static float Remap(float val, float min, float max) {
            return (Clamp(val, min, max) - min) / (max - min);
        }

        // Kalidokit Vector.toSphericalCoords with the LEG axisMap {x:"y", y:"z", z:"x"} baked in (legs are
        // the only caller). Returns (theta, phi) in radians for a unit-ish direction.
        private static void SphericalCoords(Vector3 dir, out float theta, out float phi) {
            float len = dir.magnitude;
            if (len < 1e-8f) {
                theta = 0f;
                phi = 0f;
                return;
            }
            // axisMap: x<-y, y<-z, z<-x  →  theta = atan2(dir.z, dir.y); phi = acos(dir.x / len)
            theta = Mathf.Atan2(dir.z, dir.y);
            phi = Mathf.Acos(Mathf.Clamp(dir.x / len, -1f, 1f));
        }

        // Kalidokit Vector.getSphericalCoords(a,b, legAxisMap): direction a->b.
        public static void GetSphericalCoords(Vector3 a, Vector3 b, out float theta, out float phi) {
            Vector3 v = (b - a).normalized;
            float t, p;
            SphericalCoords(v, out t, out p);
            theta = NormalizeAngle(-t);
            phi = NormalizeAngle(Pi / 2f - p);
        }

        // Kalidokit Vector.getRelativeSphericalCoords(a,b,c, legAxisMap): dir a->b relative to b->c.
        public static void GetRelativeSphericalCoords(Vector3 a, Vector3 b, Vector3 c, out float theta, out float phi) {
            Vector3 v1 = (b - a).normalized;
            Vector3 v2 = (c - b).normalized;
            float t1, p1, t2, p2;
            SphericalCoords(v1, out t1, out p1);
            SphericalCoords(v2, out t2, out p2);
            theta = NormalizeAngle(t1 - t2);
            phi = NormalizeAngle(p1 - p2);
        }

        // Build a three.js-style Euler('XYZ') quaternion from radians, then convert to Unity's left-handed
        // frame. Kalidoface applies Kalidokit euler to the three-vrm NORMALIZED bones; UniVRM's control rig
        // exposes the SAME VRM1.0 normalized bones, so the only difference is world handedness (three is
        // right-handed +Z-out, Unity left-handed +Z-in). `eulerSigns` pre-negates the euler per axis and
        // `flipQuat` picks the handedness conversion — both GLOBAL (one convention for the whole body), the
        // single knob that replaces the old per-bone axis guessing (ADR-022 control-rig path).
        public static Quaternion ThreeEulerToUnity(Vector3 eulerRadians, Vector3 eulerSigns, int flipQuat) {
            float x = eulerRadians.x * eulerSigns.x;
            float y = eulerRadians.y * eulerSigns.y;
            float z = eulerRadians.z * eulerSigns.z;
            float c1 = Mathf.Cos(x * 0.5f);
            float c2 = Mathf.Cos(y * 0.5f);
            float c3 = Mathf.Cos(z * 0.5f);
            float s1 = Mathf.Sin(x * 0.5f);
            float s2 = Mathf.Sin(y * 0.5f);
            float s3 = Mathf.Sin(z * 0.5f);
            // three.js Euler order "XYZ" -> quaternion.
            float qx = s1 * c2 * c3 + c1 * s2 * s3;
            float qy = c1 * s2 * c3 - s1 * c2 * s3;
            float qz = c1 * c2 * s3 + s1 * s2 * c3;
            float qw = c1 * c2 * c3 - s1 * s2 * s3;
            switch (flipQuat) {
                case 1:
                    return new Quaternion(qx, qy, -qz, qw);
                case 2:
                    return new Quaternion(qx, -qy, -qz, qw);
                case 3:
                    return new Quaternion(-qx, qy, -qz, qw);
                default:
                    // 0: mirror Z (the standard three RH -> Unity LH conversion). Negate X and Y components.
                    return new Quaternion(-qx, -qy, qz, qw);
            }
        }
    }
}
