using NUnit.Framework;
using VirtualMirror.Diagnostics;

namespace VirtualMirror.Tests {
    [TestFixture]
    public class PerformanceMonitorTests {
        [Test]
        public void InitialValues_AreDefaultSixtyFps() {
            PerformanceMonitor monitor = new PerformanceMonitor(0.5f);
            Assert.AreEqual(60f, monitor.Fps);
            Assert.AreEqual(16.6f, monitor.FrameTimeMs, 0.1f);
        }

        [Test]
        public void Tick_AccumulatesFrames_UpdatesFpsCorrectly() {
            PerformanceMonitor monitor = new PerformanceMonitor(0.5f);
            // Simulate 30 frames over 0.5 seconds => 60 FPS
            int i = 0;
            while (i < 30) {
                monitor.Tick(0.5f / 30f);
                i = i + 1;
            }
            Assert.AreEqual(60f, monitor.Fps, 1f);
            Assert.AreEqual(16.6f, monitor.FrameTimeMs, 1f);
        }
    }
}
