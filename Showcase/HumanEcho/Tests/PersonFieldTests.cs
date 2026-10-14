using NUnit.Framework;

using UnityEngine;

namespace HumanEcho.Tests {
    /// <summary>
    /// Drives the person field stage with synthetic textures on the real GPU (an EditMode test in the open
    /// Editor): orientation of picture and mask,
    /// mirroring, and the direction, size and confidence of the silhouette motion. Each test builds and
    /// disposes its own field.
    /// </summary>
    public sealed class PersonFieldTests {
        private const int Width = 256;
        private const int Height = 192;

        private static PersonField NewField() {
            Shader shader = Shader.Find("HumanEcho/PersonField");
            Assert.IsNotNull(shader, "shader HumanEcho/PersonField must be imported");
            return new PersonField(shader);
        }

        private static Texture2D BoxMask(float left, float top, float right, float bottom) {
            return GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                return GpuTestUtil.Box(x, y, left, top, right, bottom, 4f);
            });
        }

        // The mean motion over the pixels that are confident, plus how many there are.
        private static void MeanMotion(PersonField field, out float meanX, out float meanY, out int confident) {
            Color[] motion = GpuTestUtil.Read(field.Motion);
            double sx = 0;
            double sy = 0;
            confident = 0;
            for (int i = 0; i < motion.Length; i++) {
                if (motion[i].b > 0.2f) {
                    sx += motion[i].r;
                    sy += motion[i].g;
                    confident++;
                }
            }
            meanX = confident > 0 ? (float)(sx / confident) : 0f;
            meanY = confident > 0 ? (float)(sy / confident) : 0f;
        }

        [Test]
        public void FieldKeepsThePictureAndTheMaskUprightWithTheTopRowFirst() {
            PersonField field = NewField();
            Texture2D mask = GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                return x < Width / 2 && y < Height / 2 ? 1f : 0f;
            });
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(255, 0, 0, 255));
            try {
                Assert.IsTrue(field.Process(color, mask, Width, Height, false, 0f));
                Color[] pixels = GpuTestUtil.Read(field.Field);
                // Unity readback rows run bottom to top, so the picture's top-left quadrant is at the HIGH
                // rows and low columns.
                Color topLeft = pixels[(Height - 10) * Width + 10];
                Color topRight = pixels[(Height - 10) * Width + (Width - 10)];
                Color bottomLeft = pixels[10 * Width + 10];
                Assert.Greater(topLeft.a, 0.95f, "the mask's top-left quadrant must stay top-left");
                Assert.Less(topRight.a, 0.05f);
                Assert.Less(bottomLeft.a, 0.05f, "the bottom must not receive the top rows");
                Assert.Greater(topLeft.r, 0.9f, "the picture is carried in rgb");
                Assert.Less(topLeft.g, 0.1f);
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(mask);
                GpuTestUtil.Destroy(color);
            }
        }

        [Test]
        public void MirrorFlipsLeftToRight() {
            PersonField field = NewField();
            Texture2D mask = GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                return x < Width / 2 && y < Height / 2 ? 1f : 0f;
            });
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(255, 255, 255, 255));
            try {
                Assert.IsTrue(field.Process(color, mask, Width, Height, true, 0f));
                Color[] pixels = GpuTestUtil.Read(field.Field);
                Assert.Less(pixels[(Height - 10) * Width + 10].a, 0.05f, "mirrored: the left quadrant is now empty");
                Assert.Greater(pixels[(Height - 10) * Width + (Width - 10)].a, 0.95f, "mirrored: the right quadrant has the person");
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(mask);
                GpuTestUtil.Destroy(color);
            }
        }

        [Test]
        public void ASilhouetteMovingRightMovesRight() {
            PersonField field = NewField();
            Texture2D first = BoxMask(70f, 50f, 170f, 140f);
            Texture2D second = BoxMask(82f, 50f, 182f, 140f);
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(200, 200, 200, 255));
            try {
                field.Process(color, first, Width, Height, false, 0f);
                field.Process(color, second, Width, Height, false, 0.05f);
                float meanX;
                float meanY;
                int confident;
                MeanMotion(field, out meanX, out meanY, out confident);
                Assert.Greater(confident, 30, "the moving edges must produce confident motion");
                Assert.Greater(meanX, 0.2f, "moving right must give a positive x velocity (screen heights per second)");
                Assert.Less(Mathf.Abs(meanY), 0.35f * Mathf.Abs(meanX), "a purely horizontal move must not read as vertical");
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(first);
                GpuTestUtil.Destroy(second);
                GpuTestUtil.Destroy(color);
            }
        }

        [Test]
        public void ASilhouetteMovingUpOnTheScreenHasPositiveY() {
            PersonField field = NewField();
            // Data rows count from the TOP, so a smaller row number is higher on the screen.
            Texture2D first = BoxMask(70f, 60f, 170f, 150f);
            Texture2D second = BoxMask(70f, 48f, 170f, 138f);
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(200, 200, 200, 255));
            try {
                field.Process(color, first, Width, Height, false, 0f);
                field.Process(color, second, Width, Height, false, 0.05f);
                float meanX;
                float meanY;
                int confident;
                MeanMotion(field, out meanX, out meanY, out confident);
                Assert.Greater(confident, 30);
                Assert.Greater(meanY, 0.2f, "moving up must give a positive y velocity");
                Assert.Less(Mathf.Abs(meanX), 0.35f * Mathf.Abs(meanY));
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(first);
                GpuTestUtil.Destroy(second);
                GpuTestUtil.Destroy(color);
            }
        }

        [Test]
        public void AStillSilhouetteHasNoMotionAndNoConfidence() {
            PersonField field = NewField();
            Texture2D mask = BoxMask(70f, 50f, 170f, 140f);
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(200, 200, 200, 255));
            try {
                field.Process(color, mask, Width, Height, false, 0f);
                field.Process(color, mask, Width, Height, false, 0.05f);
                Color[] motion = GpuTestUtil.Read(field.Motion);
                float maxConfidence = 0f;
                float maxSpeed = 0f;
                for (int i = 0; i < motion.Length; i++) {
                    maxConfidence = Mathf.Max(maxConfidence, motion[i].b);
                    maxSpeed = Mathf.Max(maxSpeed, new Vector2(motion[i].r, motion[i].g).magnitude);
                }
                Assert.Less(maxConfidence, 0.02f);
                Assert.Less(maxSpeed, 0.02f);
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(mask);
                GpuTestUtil.Destroy(color);
            }
        }

        [Test]
        public void BlankEmptiesThePersonAndTheMotionAndForgetsThePreviousMask() {
            PersonField field = NewField();
            Texture2D first = BoxMask(70f, 50f, 170f, 140f);
            Texture2D second = BoxMask(82f, 50f, 182f, 140f);
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(200, 200, 200, 255));
            try {
                field.Process(color, first, Width, Height, false, 0f);
                field.Process(color, second, Width, Height, false, 0.05f);
                field.Blank();

                float maxAlpha = 0f;
                Color[] pixels = GpuTestUtil.Read(field.Field);
                for (int i = 0; i < pixels.Length; i++) {
                    maxAlpha = Mathf.Max(maxAlpha, pixels[i].a);
                }
                Assert.Less(maxAlpha, 0.01f, "a blanked field holds no person");

                float maxMotion = 0f;
                Color[] motion = GpuTestUtil.Read(field.Motion);
                for (int i = 0; i < motion.Length; i++) {
                    maxMotion = Mathf.Max(maxMotion, Mathf.Max(motion[i].b, new Vector2(motion[i].r, motion[i].g).magnitude));
                }
                Assert.Less(maxMotion, 0.01f, "and no motion");

                // The frame after a blank has nothing to differ from, so it must not invent motion out of the
                // old mask.
                field.Process(color, second, Width, Height, false, 0.2f);
                float maxConfidence = 0f;
                float maxSpeed = 0f;
                Color[] after = GpuTestUtil.Read(field.Motion);
                for (int i = 0; i < after.Length; i++) {
                    maxConfidence = Mathf.Max(maxConfidence, after[i].b);
                    maxSpeed = Mathf.Max(maxSpeed, new Vector2(after[i].r, after[i].g).magnitude);
                }
                Assert.Less(maxConfidence, 0.02f, "no false motion on the first frame after a blank");
                Assert.Less(maxSpeed, 0.02f);
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(first);
                GpuTestUtil.Destroy(second);
                GpuTestUtil.Destroy(color);
            }
        }

        [Test]
        public void ProcessRefusesMissingInputsAndReportsTheFieldSize() {
            PersonField field = NewField();
            Texture2D mask = BoxMask(70f, 50f, 170f, 140f);
            Texture2D color = GpuTestUtil.ColorTexture(Width, Height, new Color32(200, 200, 200, 255));
            try {
                Assert.IsTrue(field.Process(color, mask, Width, Height, false, 0f));
                Assert.AreEqual(1, field.Updates);
                Assert.AreEqual(Width, field.Width);
                Assert.AreEqual(Height, field.Height);
                Assert.AreEqual((float)Width / Height, field.Aspect, 0.0001f);
                Assert.IsFalse(field.Process(null, mask, Width, Height, false, 0.1f), "a missing picture is refused");
                Assert.AreEqual(1, field.Updates);
            } finally {
                field.Dispose();
                GpuTestUtil.Destroy(mask);
                GpuTestUtil.Destroy(color);
            }
        }
    }
}
