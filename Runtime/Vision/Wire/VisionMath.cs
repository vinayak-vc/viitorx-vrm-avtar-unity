using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// The coordinate and rotation conversions between the sidecar's camera space (x right, y DOWN, z forward) and Unity's (x right, y UP, z forward).
    ///
    /// Written in plain managed arithmetic on purpose: <c>Quaternion.LookRotation</c>, <c>Slerp</c> and friends are native calls that cannot run
    /// outside a player, so anything built on them cannot be unit-tested headless. Nothing here needs the engine.
    /// </summary>
    public static class VisionMath {
        /// <summary>A camera-space point from the wire as a Unity point: the y axis is flipped.</summary>
        public static Vector3 WireToUnity(float x, float y, float z) {
            return new Vector3(x, -y, z);
        }

        /// <summary>A wire image landmark (fractions of the frame, origin top-left, y down) as a viewport point (origin bottom-left, y up).</summary>
        public static Vector2 ImageToViewport(Vector3 image) {
            return new Vector2(image.x, 1f - image.y);
        }

        /// <summary>
        /// The head rotation in Unity axes from the wire's <c>rot</c>: nine numbers, row-major, whose COLUMNS are the head's x, y, z axes in camera
        /// axes (x right, y down, z away), identity for a frontal upright face. The wire's frame and Unity's differ by a reflection of y, so the matrix
        /// is conjugated by it (every entry that mixes y with another axis changes sign), which keeps a proper rotation.
        /// </summary>
        public static Quaternion HeadRotationFromWire(float[] rowMajor) {
            if (rowMajor == null || rowMajor.Length < 9) {
                return Quaternion.identity;
            }
            return FromMatrix(
                rowMajor[0], -rowMajor[1], rowMajor[2],
                -rowMajor[3], rowMajor[4], -rowMajor[5],
                rowMajor[6], -rowMajor[7], rowMajor[8]);
        }

        /// <summary>The rotation whose z axis is <paramref name="forward"/> and whose y axis is as close to <paramref name="up"/> as it can be (what <c>Quaternion.LookRotation</c> returns, without the native call).</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) {
            Vector3 z = forward.normalized;
            Vector3 x = Vector3.Cross(up, z);
            if (x.sqrMagnitude < 1e-12f) {
                Vector3 fallback = Mathf.Abs(z.y) < 0.99f ? Vector3.up : Vector3.right;
                x = Vector3.Cross(fallback, z);
            }
            x = x.normalized;
            Vector3 y = Vector3.Cross(z, x);
            return FromMatrix(x.x, y.x, z.x, x.y, y.y, z.y, x.z, y.z, z.z);
        }

        /// <summary>
        /// The palm's orientation from four hand landmarks (wrist, index knuckle, middle knuckle, little knuckle), in the same way the OAK-D hand path
        /// derives it: forward is wrist to middle knuckle, the palm normal is forward x (index - little). Identity when the landmarks are degenerate.
        /// Consumers apply a wrist rotation relative to a neutral pose, so only the convention's consistency matters, not its absolute axes.
        /// </summary>
        public static Quaternion PalmRotation(Vector3 wrist, Vector3 indexKnuckle, Vector3 middleKnuckle, Vector3 littleKnuckle) {
            Vector3 forward = middleKnuckle - wrist;
            Vector3 across = indexKnuckle - littleKnuckle;
            if (forward.sqrMagnitude < 1e-10f || across.sqrMagnitude < 1e-10f) {
                return Quaternion.identity;
            }
            Vector3 normal = Vector3.Cross(forward, across);
            if (normal.sqrMagnitude < 1e-10f) {
                return Quaternion.identity;
            }
            return LookRotation(forward, normal);
        }

        /// <summary>The unit quaternion for a proper rotation matrix (entries named row, column), acting on column vectors.</summary>
        public static Quaternion FromMatrix(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22) {
            float trace = m00 + m11 + m22;
            float x;
            float y;
            float z;
            float w;
            if (trace > 0f) {
                float s = Mathf.Sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            } else if (m00 > m11 && m00 > m22) {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s;
                x = 0.25f * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            } else if (m11 > m22) {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25f * s;
                z = (m12 + m21) / s;
            } else {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25f * s;
            }
            float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (length < 1e-12f) {
                return Quaternion.identity;
            }
            return new Quaternion(x / length, y / length, z / length, w / length);
        }

        /// <summary>Rotates a vector by a quaternion with plain arithmetic (the engine's operator is managed too, but this keeps the whole chain testable without the engine).</summary>
        public static Vector3 Rotate(Quaternion q, Vector3 v) {
            float tx = 2f * (q.y * v.z - q.z * v.y);
            float ty = 2f * (q.z * v.x - q.x * v.z);
            float tz = 2f * (q.x * v.y - q.y * v.x);
            return new Vector3(
                v.x + q.w * tx + (q.y * tz - q.z * ty),
                v.y + q.w * ty + (q.z * tx - q.x * tz),
                v.z + q.w * tz + (q.x * ty - q.y * tx));
        }
    }
}
