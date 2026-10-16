using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="CutoutTextures"/>: a reassembled frame becomes two GPU textures with the picture's pixels where the wire put them. The reference
    /// frame's pixels are known exactly (python-sidecar~/tools/video/make_vision_fixtures.py states them), so the tests read them back.
    /// </summary>
    public sealed class CutoutTexturesTests {
        private CutoutTextures textures;

        [SetUp]
        public void SetUp() {
            textures = new CutoutTextures();
        }

        [TearDown]
        public void TearDown() {
            textures.Dispose();
        }

        private static CutoutFrame FrameFromFixtures(string family, int count) {
            CutoutAssembler assembler = new CutoutAssembler();
            int i = 0;
            while (i < count) {
                byte[] datagram = VisionFixtures.Load("cutout_" + family + "_0" + i + ".bin");
                assembler.Add(datagram, datagram.Length);
                i = i + 1;
            }
            Assert.AreEqual(1L, assembler.Completed, family);
            CutoutFrame frame = new CutoutFrame();
            assembler.SwapFrameWith(frame);
            return frame;
        }

        private static CutoutFrame FrameFromSpec(CutoutTestFrames.Spec spec) {
            CutoutAssembler assembler = new CutoutAssembler();
            foreach (byte[] datagram in CutoutTestFrames.Encode(spec)) {
                assembler.Add(datagram, datagram.Length);
            }
            Assert.AreEqual(1L, assembler.Completed);
            CutoutFrame frame = new CutoutFrame();
            assembler.SwapFrameWith(frame);
            return frame;
        }

        private static Color32[] ReadBack(RenderTexture target) {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return pixels;
        }

        [Test]
        public void NothingIsUploadedUntilAFrameArrives() {
            Assert.IsFalse(textures.HasFrame);
            Assert.IsNull(textures.ColorTexture);
            Assert.IsNull(textures.MaskTexture);
            Assert.AreEqual(1f, textures.Aspect);
            Assert.IsFalse(textures.Upload(null));
            Assert.IsFalse(textures.Upload(new CutoutFrame()), "a frame with no mask is not a frame");
            Assert.IsFalse(textures.HasFrame);
        }

        [Test]
        public void ARawPictureLandsWithItsTopRowInRowZero() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            Texture2D color = (Texture2D)textures.ColorTexture;
            Assert.AreEqual(16, color.width);
            Assert.AreEqual(12, color.height);
            Assert.AreEqual(TextureFormat.RGBA32, color.format);
            Color32[] pixels = color.GetPixels32();
            // GetPixels32 starts at row 0, which is the wire's FIRST row: pixel (x, y) of the picture is pixels[y * 16 + x].
            Assert.AreEqual(new Color32(48, 40, 128, 255), pixels[2 * 16 + 3]);
            Assert.AreEqual(new Color32(240, 220, 128, 255), pixels[11 * 16 + 15]);
            Assert.AreEqual(new Color32(0, 0, 128, 255), pixels[0]);
        }

        [Test]
        public void TheMaskIsOneChannelAtItsOwnSize() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            Texture2D mask = (Texture2D)textures.MaskTexture;
            Assert.AreEqual(8, mask.width);
            Assert.AreEqual(6, mask.height);
            Assert.AreEqual(TextureFormat.R8, mask.format);
            Color32[] pixels = mask.GetPixels32();
            Assert.AreEqual(0, pixels[0].r);
            Assert.AreEqual(105, pixels[2 * 8 + 5].r);
            Assert.AreEqual(235, pixels[47].r);
            Assert.AreEqual(TextureWrapMode.Clamp, mask.wrapMode);
            Assert.AreEqual(FilterMode.Bilinear, mask.filterMode);
        }

        [Test]
        public void TheFramesFactsAreKeptForTheHud() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            Assert.IsTrue(textures.HasFrame);
            Assert.IsTrue(textures.HasColor);
            Assert.AreEqual(2, textures.People);
            Assert.AreEqual(0.321f, textures.ForegroundFraction, 1e-6f);
            Assert.IsTrue(textures.Mirrored);
            Assert.IsTrue(textures.Live);
            Assert.IsFalse(textures.Nobody);
            Assert.AreEqual(1042u, textures.Sequence);
            Assert.AreEqual(38.2f, textures.SenderLatencyMs, 1e-4f);
            Assert.AreEqual(12.5f, textures.InferMs, 1e-6f);
            Assert.AreEqual(CutoutWire.ModelGeneral, textures.ModelId);
            Assert.AreEqual(16f / 12f, textures.Aspect, 1e-6f);
            Assert.AreEqual(CutoutColorEncoding.Rgba, textures.ColorEncoding);
            Assert.AreEqual(8, textures.MaskWidth);
        }

        [Test]
        public void TheVersionAdvancesOnEveryUploadAndTheGenerationOnlyWhenATextureIsRecreated() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            int version = textures.Version;
            int generation = textures.Generation;
            Assert.Greater(generation, 0, "the first frame created the textures");
            Assert.IsTrue(textures.Upload(FrameFromFixtures("next", 4)));
            Assert.AreEqual(version + 1, textures.Version);
            Assert.AreEqual(generation, textures.Generation, "same size, same textures: a material need not bind again");
            CutoutTestFrames.Spec bigger = new CutoutTestFrames.Spec();
            bigger.ColorWidth = 32;
            bigger.ColorHeight = 24;
            bigger.Color = CutoutTestFrames.RgbaPicture(32, 24, 5);
            Assert.IsTrue(textures.Upload(FrameFromSpec(bigger)));
            Assert.Greater(textures.Generation, generation, "a new picture size needs a new texture");
            Assert.AreEqual(32, textures.ColorTexture.width);
            Assert.AreEqual(32f / 24f, textures.Aspect, 1e-6f);
        }

        [Test]
        public void ASecondUploadReplacesThePixels() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            Assert.IsTrue(textures.Upload(FrameFromFixtures("next", 4)));
            Color32[] pixels = ((Texture2D)textures.ColorTexture).GetPixels32();
            Assert.AreEqual(200, pixels[2 * 16 + 3].b, "the second frame's blue");
            Assert.AreEqual(1043u, textures.Sequence);
        }

        [Test]
        public void AMaskOnlyStreamHasAWhitePictureAndNoAspectFromIt() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("maskonly", 1)));
            Assert.IsFalse(textures.HasColor);
            Assert.IsNotNull(textures.ColorTexture, "a material always has something to bind");
            Assert.AreEqual(1, textures.ColorTexture.width);
            Assert.AreEqual(Color.white, ((Texture2D)textures.ColorTexture).GetPixel(0, 0));
            Assert.AreEqual(32, textures.MaskTexture.width, "the deflated mask arrives inflated");
            Assert.AreEqual(32f / 24f, textures.Aspect, 1e-6f, "the mask's own aspect stands in for the picture's");
            Color32[] mask = ((Texture2D)textures.MaskTexture).GetPixels32();
            Assert.AreEqual(255, mask[6 * 32 + 8].r);
            Assert.AreEqual(0, mask[5 * 32 + 8].r);
        }

        [Test]
        public void AJpegPictureIsDecodedAndFlippedToTheSameOrientationAsARawOne() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("jpeg", 3)));
            Assert.AreEqual(CutoutColorEncoding.Jpeg, textures.ColorEncoding);
            RenderTexture picture = (RenderTexture)textures.ColorTexture;
            Assert.AreEqual(8, picture.width);
            Assert.AreEqual(8, picture.height);
            Color32[] pixels = ReadBack(picture);
            // The 8x8 JPEG is a gradient: B = 200 - 20x, G = 30y, R = 40 + 25x + 5y (y = 0 is the TOP row). Row 0 of the texture must be that top row.
            Assert.AreEqual(200, pixels[0].b, 40, "x = 0, y = 0: blue");
            Assert.AreEqual(0, pixels[0].g, 40, "x = 0, y = 0: green");
            Assert.AreEqual(40, pixels[0].r, 40, "x = 0, y = 0: red");
            Assert.AreEqual(210, pixels[7 * 8 + 7].g, 40, "x = 7, y = 7: green");
            Assert.AreEqual(60, pixels[7 * 8 + 7].b, 40, "x = 7, y = 7: blue");
            Assert.Less(pixels[0].g, pixels[7 * 8].g, "green grows down the picture, so row 7 is greener than row 0");
            Assert.AreEqual(0L, textures.DecodeFailures);
            Assert.AreEqual(8f / 8f, textures.Aspect, 1e-6f);
            Texture2D mask = (Texture2D)textures.MaskTexture;
            Assert.AreEqual(3 * 8 + 3, mask.GetPixels32()[3 * 8 + 3].r / 4, "the raw 8x8 ramp mask beside the JPEG: 4 (x + 8y)");
        }

        [Test]
        public void ABrokenJpegKeepsThePreviousPictureAndIsCounted() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            int version = textures.Version;
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            spec.ColorEncoding = CutoutColorEncoding.Jpeg;
            spec.ColorWidth = 8;
            spec.ColorHeight = 8;
            spec.Color = new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
            spec.MaskWidth = 8;
            spec.MaskHeight = 8;
            spec.Mask = CutoutTestFrames.Ramp(8, 8);
            Assert.IsFalse(textures.Upload(FrameFromSpec(spec)));
            Assert.AreEqual(version, textures.Version, "nothing changed");
            Assert.AreEqual(1L, textures.DecodeFailures);
            Assert.AreEqual(CutoutColorEncoding.Rgba, textures.ColorEncoding);
        }

        [Test]
        public void ClearDropsTheFrameAndTheNextOneStartsAgain() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            int generation = textures.Generation;
            textures.Clear();
            Assert.IsFalse(textures.HasFrame);
            Assert.IsNull(textures.MaskTexture);
            Assert.Greater(textures.Generation, generation, "anything bound to the old textures must bind again");
            Assert.IsTrue(textures.Upload(FrameFromFixtures("next", 4)));
            Assert.IsTrue(textures.HasFrame);
        }

        [Test]
        public void ADisposedObjectRefusesFramesAndLeavesNothingBehind() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            textures.Dispose();
            Assert.IsFalse(textures.Upload(FrameFromFixtures("next", 4)));
            textures.Dispose();
        }

        [Test]
        public void TheUploadTimeIsMeasured() {
            Assert.IsTrue(textures.Upload(FrameFromFixtures("rgba", 4)));
            Assert.GreaterOrEqual(textures.UploadMilliseconds, 0f);
            Assert.Less(textures.UploadMilliseconds, 1000f);
        }

        [Test]
        public void A720pFrameUploadsAndKeepsItsCorners() {
            CutoutTestFrames.Spec spec = new CutoutTestFrames.Spec();
            spec.ColorWidth = 1280;
            spec.ColorHeight = 720;
            spec.Color = CutoutTestFrames.RgbaPicture(1280, 720, 17);
            spec.MaskWidth = 640;
            spec.MaskHeight = 360;
            spec.Mask = CutoutTestFrames.Ramp(640, 360);
            spec.ChunkBytes = 60000;
            Assert.IsTrue(textures.Upload(FrameFromSpec(spec)));
            Color32[] pixels = ((Texture2D)textures.ColorTexture).GetPixels32();
            Assert.AreEqual(1280 * 720, pixels.Length);
            Assert.AreEqual(new Color32(CutoutTestFrames.Low(1279 * 16), CutoutTestFrames.Low(719 * 20), 17, 255), pixels[719 * 1280 + 1279]);
            Assert.AreEqual(new Color32(0, 0, 17, 255), pixels[0]);
            Color32[] mask = ((Texture2D)textures.MaskTexture).GetPixels32();
            Assert.AreEqual(CutoutTestFrames.Low(((359 * 640) + 639) * 5), mask[359 * 640 + 639].r);
        }

        [Test]
        public void TheUvConstantsFlipTheVerticalAxis() {
            Assert.AreEqual(new Vector2(1f, -1f), CutoutTextures.UvScale);
            Assert.AreEqual(new Vector2(0f, 1f), CutoutTextures.UvOffset);
            Vector2 top = new Vector2(0.3f, 1f);
            Vector2 sampled = new Vector2(top.x * CutoutTextures.UvScale.x + CutoutTextures.UvOffset.x, top.y * CutoutTextures.UvScale.y + CutoutTextures.UvOffset.y);
            Assert.AreEqual(0f, sampled.y, 1e-6f, "the top of the screen (v = 1) is row 0 of the texture");
        }
    }
}
