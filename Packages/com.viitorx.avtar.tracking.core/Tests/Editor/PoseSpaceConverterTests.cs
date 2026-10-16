using NUnit.Framework;
using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Tests {
    [TestFixture]
    public class PoseSpaceConverterTests {
        [Test]
        public void ToUnity_WithNoFlips_PreservesCoordinates() {
            PoseSpaceConverter converter = new PoseSpaceConverter(false, false, false);
            Vector3 result = converter.ToUnity(1f, 2f, 3f);
            Assert.AreEqual(1f, result.x, 1e-5f);
            Assert.AreEqual(2f, result.y, 1e-5f);
            Assert.AreEqual(3f, result.z, 1e-5f);
        }

        [Test]
        public void ToUnity_WithAllFlips_NegatesCoordinates() {
            PoseSpaceConverter converter = new PoseSpaceConverter(true, true, true);
            Vector3 result = converter.ToUnity(1f, 2f, 3f);
            Assert.AreEqual(-1f, result.x, 1e-5f);
            Assert.AreEqual(-2f, result.y, 1e-5f);
            Assert.AreEqual(-3f, result.z, 1e-5f);
        }

        [Test]
        public void ToUnity_WithDefaultYAndZFlip_FlipsYAndZOnly() {
            PoseSpaceConverter converter = new PoseSpaceConverter(false, true, true);
            Vector3 result = converter.ToUnity(1.5f, 2.5f, 3.5f);
            Assert.AreEqual(1.5f, result.x, 1e-5f);
            Assert.AreEqual(-2.5f, result.y, 1e-5f);
            Assert.AreEqual(-3.5f, result.z, 1e-5f);
        }

        [Test]
        public void ToUnity_WithZeroValues_ReturnsZeroVector() {
            PoseSpaceConverter converter = new PoseSpaceConverter(true, true, true);
            Vector3 result = converter.ToUnity(0f, 0f, 0f);
            Assert.AreEqual(0f, result.x, 1e-5f);
            Assert.AreEqual(0f, result.y, 1e-5f);
            Assert.AreEqual(0f, result.z, 1e-5f);
        }
    }
}
