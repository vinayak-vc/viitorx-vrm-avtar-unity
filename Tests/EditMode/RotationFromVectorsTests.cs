using NUnit.Framework;
using UnityEngine;
using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    [TestFixture]
    public class RotationFromVectorsTests {
        [Test]
        public void Delta_IdenticalVectors_ReturnsIdentity() {
            Vector3 forward = Vector3.forward;
            Quaternion delta = RotationFromVectors.Delta(forward, forward, Vector3.up);
            Assert.AreEqual(Quaternion.identity.x, delta.x, 1e-4f);
            Assert.AreEqual(Quaternion.identity.y, delta.y, 1e-4f);
            Assert.AreEqual(Quaternion.identity.z, delta.z, 1e-4f);
            Assert.AreEqual(Quaternion.identity.w, delta.w, 1e-4f);
        }

        [Test]
        public void Delta_ZeroLengthVector_ReturnsIdentityWithoutNaN() {
            Quaternion delta = RotationFromVectors.Delta(Vector3.zero, Vector3.forward, Vector3.up);
            Assert.IsFalse(float.IsNaN(delta.x));
            Assert.IsFalse(float.IsNaN(delta.y));
            Assert.IsFalse(float.IsNaN(delta.z));
            Assert.IsFalse(float.IsNaN(delta.w));
            Assert.AreEqual(Quaternion.identity, delta);
        }

        [Test]
        public void Delta_OrthogonalVectorChange_RotatesCorrectly() {
            Vector3 rest = Vector3.forward;
            Vector3 target = Vector3.right;
            Quaternion delta = RotationFromVectors.Delta(rest, target, Vector3.up);
            Vector3 rotated = delta * rest;
            Assert.AreEqual(target.x, rotated.x, 1e-4f);
            Assert.AreEqual(target.y, rotated.y, 1e-4f);
            Assert.AreEqual(target.z, rotated.z, 1e-4f);
        }

        [Test]
        public void TryBasis_ValidOrthogonalVectors_ReturnsTrueAndValidQuaternion() {
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;
            Quaternion basis;
            bool success = RotationFromVectors.TryBasis(right, up, out basis);
            Assert.IsTrue(success);
            Assert.IsFalse(float.IsNaN(basis.x));
            Assert.IsFalse(float.IsNaN(basis.w));
        }

        [Test]
        public void TryBasis_ParallelVectors_ReturnsFalse() {
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.right;
            Quaternion basis;
            bool success = RotationFromVectors.TryBasis(right, up, out basis);
            Assert.IsFalse(success);
            Assert.AreEqual(Quaternion.identity, basis);
        }
    }
}
