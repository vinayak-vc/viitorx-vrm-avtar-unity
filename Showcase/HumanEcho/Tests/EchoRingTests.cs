using NUnit.Framework;

using UnityEngine;

namespace HumanEcho.Tests {
    /// <summary>
    /// The echo ring on the real GPU: it is shared by the echo layer and the delayed outline, so both call
    /// <see cref="EchoRing.Advance"/> in the
    /// same frame, in either order, and a mask frame must be captured exactly once. A stalled stream must not
    /// leave old captures behind.
    /// </summary>
    public sealed class EchoRingTests {
        private const int Width = 128;
        private const int Height = 96;

        private sealed class Rig {
            public PersonField Field;
            public EchoRing Ring;
            public Texture2D Mask;
            public Texture2D Color;
        }

        private static Rig NewRig() {
            Shader fieldShader = Shader.Find("HumanEcho/PersonField");
            Shader echoShader = Shader.Find("HumanEcho/Echo");
            Assert.IsNotNull(fieldShader);
            Assert.IsNotNull(echoShader);
            Rig rig = new Rig();
            rig.Field = new PersonField(fieldShader);
            rig.Ring = new EchoRing(echoShader, 17);
            rig.Ring.SpacingSeconds = 0.14f;
            rig.Mask = GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                return GpuTestUtil.Box(x, y, 30f, 20f, 90f, 80f, 3f);
            });
            rig.Color = GpuTestUtil.ColorTexture(Width, Height, new Color32(200, 200, 200, 255));
            return rig;
        }

        private static void Dispose(Rig rig) {
            rig.Ring.Dispose();
            rig.Field.Dispose();
            GpuTestUtil.Destroy(rig.Mask);
            GpuTestUtil.Destroy(rig.Color);
        }

        [Test]
        public void TwoLayersAdvancingTheSameRingInOneFrameCaptureOnce() {
            Rig rig = NewRig();
            try {
                rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, 0f);
                bool first = rig.Ring.Advance(rig.Field, 0f);
                bool second = rig.Ring.Advance(rig.Field, 0f);
                Assert.IsTrue(first, "the first call captures the mask");
                Assert.IsFalse(second, "the second call in the same frame must find it already handled");
                Assert.AreEqual(1, rig.Ring.ValidCount);

                // The next mask frame, long enough after: one more capture, again however many layers ask.
                rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, 0.2f);
                bool third = rig.Ring.Advance(rig.Field, 0.2f);
                bool fourth = rig.Ring.Advance(rig.Field, 0.2f);
                Assert.IsTrue(third);
                Assert.IsFalse(fourth);
                Assert.AreEqual(2, rig.Ring.ValidCount);
                Assert.AreEqual(Width, rig.Ring.Width);
                Assert.AreEqual(Height, rig.Ring.Height);
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void ARepeatedFieldFrameIsNotCapturedAgain() {
            Rig rig = NewRig();
            try {
                rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, 0f);
                Assert.IsTrue(rig.Ring.Advance(rig.Field, 0f));
                // No new mask arrived (the display runs faster than the sender): time passes, nothing is
                // captured.
                Assert.IsFalse(rig.Ring.Advance(rig.Field, 0.5f));
                Assert.AreEqual(1, rig.Ring.ValidCount);
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void AStalledStreamLeavesNoOldHistory() {
            Rig rig = NewRig();
            try {
                for (int i = 0; i < 4; i++) {
                    rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, i * 0.2f);
                    rig.Ring.Advance(rig.Field, i * 0.2f);
                }
                Assert.AreEqual(4, rig.Ring.ValidCount);

                // Ten seconds later the stream returns: the poses from before the gap must not reappear as
                // echoes.
                rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, 10.6f);
                Assert.IsTrue(rig.Ring.Advance(rig.Field, 10.6f));
                Assert.AreEqual(1, rig.Ring.ValidCount, "the history older than the stall limit is forgotten");
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void ResetEmptiesTheRingAndTheNextMaskIsCapturedAtOnce() {
            Rig rig = NewRig();
            try {
                rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, 0f);
                rig.Ring.Advance(rig.Field, 0f);
                rig.Ring.Reset();
                Assert.AreEqual(0, rig.Ring.ValidCount);
                Assert.IsTrue(rig.Ring.Advance(rig.Field, 0.01f), "after a reset the very next call captures");
                Assert.AreEqual(1, rig.Ring.ValidCount);
            } finally {
                Dispose(rig);
            }
        }
    }
}
