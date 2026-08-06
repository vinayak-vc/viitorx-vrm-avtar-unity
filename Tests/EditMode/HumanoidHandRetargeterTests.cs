using NUnit.Framework;
using VirtualMirror.Core;
using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    [TestFixture]
    public class HumanoidHandRetargeterTests {
        [Test]
        public void Unbound_IsBoundIsFalse() {
            HumanoidHandRetargeter retargeter = new HumanoidHandRetargeter();
            Assert.IsFalse(retargeter.IsBound);
        }

        [Test]
        public void Bind_NullAnimator_IsBoundStaysFalse() {
            HumanoidHandRetargeter retargeter = new HumanoidHandRetargeter();
            retargeter.Bind(null);
            Assert.IsFalse(retargeter.IsBound);
        }

        [Test]
        public void Apply_WhenUnbound_DoesNotThrow() {
            HumanoidHandRetargeter retargeter = new HumanoidHandRetargeter();
            HandFrame frame = new HandFrame();
            frame.SetCurls(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            frame.SetMeta(1.0, true);
            Assert.DoesNotThrow(() => retargeter.Apply(frame));
        }
    }
}
