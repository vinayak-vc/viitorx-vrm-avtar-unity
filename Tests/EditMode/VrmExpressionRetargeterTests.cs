using NUnit.Framework;
using VirtualMirror.Core;
using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    [TestFixture]
    public class VrmExpressionRetargeterTests {
        [Test]
        public void Unbound_IsBoundIsFalse() {
            VrmExpressionRetargeter retargeter = new VrmExpressionRetargeter();
            Assert.IsFalse(retargeter.IsBound);
        }

        [Test]
        public void Apply_WhenUnbound_DoesNotThrow() {
            VrmExpressionRetargeter retargeter = new VrmExpressionRetargeter();
            FaceFrame frame = new FaceFrame(0.5f, 0.5f, 0.2f, 0.8f, 0f, 0f, 1.0, true);
            Assert.DoesNotThrow(() => retargeter.Apply(frame));
        }

        [Test]
        public void Apply_WithNullFrame_DoesNotThrow() {
            VrmExpressionRetargeter retargeter = new VrmExpressionRetargeter();
            Assert.DoesNotThrow(() => retargeter.Apply(null));
        }
    }
}
