using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Retargeting.Kalidokit;

namespace VirtualMirror.Tests {
    /// <summary>
    /// Locks the Kalidokit math port (<see cref="KMath"/>) against known values so a future refactor can't
    /// silently drift from the reference formulas (ADR-022).
    /// </summary>
    public sealed class KMathTests {
        [Test]
        public void Clamp_BoundsValue() {
            Assert.AreEqual(0.3f, KMath.Clamp(5f, -0.3f, 0.3f));
            Assert.AreEqual(-0.3f, KMath.Clamp(-5f, -0.3f, 0.3f));
            Assert.AreEqual(0.1f, KMath.Clamp(0.1f, -0.3f, 0.3f));
        }

        [Test]
        public void AngleBetween3DCoords_RightAngle_IsHalf() {
            // 90 degrees at b: NormalizeRadians(PI/2) == 0.5 by Kalidokit's normalizer.
            Vector3 a = new Vector3(1f, 0f, 0f);
            Vector3 b = new Vector3(0f, 0f, 0f);
            Vector3 c = new Vector3(0f, 1f, 0f);
            float angle = KMath.AngleBetween3DCoords(a, b, c);
            Assert.AreEqual(0.5f, angle, 1e-4f);
        }

        [Test]
        public void AngleBetween3DCoords_Degenerate_NoNaN() {
            Vector3 p = new Vector3(0f, 0f, 0f);
            float angle = KMath.AngleBetween3DCoords(p, p, p);
            Assert.IsFalse(float.IsNaN(angle));
            Assert.AreEqual(0f, angle);
        }

        [Test]
        public void FindRotation_XOffsetInZPlane_IsHalfOnX() {
            // a->b differ only in x (same z): find2DAngle(a.z,a.x,b.z,b.x)=atan2(dx,dz)=atan2(1,0)=PI/2,
            // NormalizeRadians(PI/2)=0.5.
            Vector3 a = new Vector3(0f, 0f, 1f);
            Vector3 b = new Vector3(1f, 0f, 1f);
            Vector3 r = KMath.FindRotation(a, b);
            Assert.AreEqual(0.5f, r.x, 1e-4f);
        }

        [Test]
        public void RollPitchYaw_Collinear_NoNaN() {
            Vector3 a = new Vector3(0f, 0f, 0f);
            Vector3 b = new Vector3(1f, 0f, 0f);
            Vector3 c = new Vector3(2f, 0f, 0f); // collinear → zero cross → guarded to zero
            Vector3 rpy = KMath.RollPitchYaw(a, b, c);
            Assert.IsFalse(float.IsNaN(rpy.x) || float.IsNaN(rpy.y) || float.IsNaN(rpy.z));
        }

        [Test]
        public void NormalizeAngle_WrapsAndScales() {
            // 0 -> 0, PI -> 1 (PI/PI), -PI stays within range and scales to -1..1.
            Assert.AreEqual(0f, KMath.NormalizeAngle(0f), 1e-5f);
            Assert.AreEqual(1f, KMath.NormalizeAngle(Mathf.PI), 1e-4f);
        }
    }
}
