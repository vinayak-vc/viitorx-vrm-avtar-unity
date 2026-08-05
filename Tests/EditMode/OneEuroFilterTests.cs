using NUnit.Framework;
using UnityEngine;
using VirtualMirror.Tracking.Filtering;

namespace VirtualMirror.Tests {
    [TestFixture]
    public class OneEuroFilterTests {
        [Test]
        public void Filter_FirstFrame_ReturnsInputValue() {
            OneEuroFilter filter = new OneEuroFilter(1f, 0.007f, 1f);
            float result = filter.Filter(10f, 0.016f);
            Assert.AreEqual(10f, result, 1e-5f);
        }

        [Test]
        public void Filter_ConstantInput_ReturnsConstantValue() {
            OneEuroFilter filter = new OneEuroFilter(1f, 0.007f, 1f);
            filter.Filter(5f, 0.016f);
            float step1 = filter.Filter(5f, 0.016f);
            float step2 = filter.Filter(5f, 0.016f);
            Assert.AreEqual(5f, step1, 1e-5f);
            Assert.AreEqual(5f, step2, 1e-5f);
        }

        [Test]
        public void Filter_JitterInput_SmoothsSignal() {
            OneEuroFilter filter = new OneEuroFilter(1f, 0.001f, 1f);
            filter.Filter(0f, 0.016f);
            float noisyValue = 1f;
            float filteredValue = filter.Filter(noisyValue, 0.016f);
            Assert.Less(filteredValue, noisyValue);
            Assert.Greater(filteredValue, 0f);
        }

        [Test]
        public void Reset_ClearsHistoryAndReturnsFirstValue() {
            OneEuroFilter filter = new OneEuroFilter(1f, 0.007f, 1f);
            filter.Filter(100f, 0.016f);
            filter.Filter(50f, 0.016f);
            filter.Reset();
            float newValue = 200f;
            float result = filter.Filter(newValue, 0.016f);
            Assert.AreEqual(newValue, result, 1e-5f);
        }
    }
}
