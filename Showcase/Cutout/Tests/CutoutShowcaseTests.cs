using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Vision;

namespace CutoutShowcase.Tests {
    /// <summary>
    /// The Cutout showcase's own rules: stepping through webcams, and what each key or button does to the <see cref="CutoutLook"/> of its
    /// <see cref="CutoutView"/>. The IMGUI drawing is judged by eye; the state it changes is tested here through the same public methods the
    /// keys call.
    /// </summary>
    public sealed class CutoutShowcaseTests {
        private GameObject host;
        private CutoutView view;
        private CutoutDemo demo;

        [SetUp]
        public void SetUp() {
            host = new GameObject("Cutout Demo Test");
            view = host.AddComponent<CutoutView>();
            demo = host.AddComponent<CutoutDemo>();
            demo.View = view;
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void SteppingForwardWalksTheCameraIndicesAndWraps() {
            Assert.AreEqual("camera:1", CutoutCameraSource.Step("camera:0", 1));
            Assert.AreEqual("camera:2", CutoutCameraSource.Step("camera:1", 1));
            Assert.AreEqual("camera:0", CutoutCameraSource.Step("camera:15", 1), "after the highest index the sender accepts");
            Assert.AreEqual("camera:15", CutoutCameraSource.Step("camera:0", -1));
            Assert.AreEqual("camera:3", CutoutCameraSource.Step("camera:5", -2));
        }

        [Test]
        public void ABareDigitAndTheShortFormAreWebcamsToo() {
            Assert.AreEqual("camera:8", CutoutCameraSource.Step("7", 1));
            Assert.AreEqual("camera:3", CutoutCameraSource.Step("cam:2", 1));
            Assert.AreEqual("camera:2", CutoutCameraSource.Step(" camera: 1 ", 1));
            Assert.AreEqual("camera:1", CutoutCameraSource.Step("CAMERA:0", 1));
        }

        [Test]
        public void AnythingThatIsNotAWebcamBecomesCameraZero() {
            Assert.AreEqual("camera:0", CutoutCameraSource.Step("D:/clips/video.webm", 1));
            Assert.AreEqual("camera:0", CutoutCameraSource.Step("D:/clips/video.webm", -1));
            Assert.AreEqual("camera:0", CutoutCameraSource.Step("http://192.168.1.5:3000/viewer?room=room-abc", 1));
            Assert.AreEqual("camera:0", CutoutCameraSource.Step(string.Empty, 1));
            Assert.AreEqual("camera:0", CutoutCameraSource.Step(null, 1));
            Assert.AreEqual("camera:0", CutoutCameraSource.Step("camera:99", 1), "an index the sender would refuse is not a webcam");
        }

        [Test]
        public void TryIndexOfReadsOnlyWhatTheSenderAccepts() {
            int index;
            Assert.IsTrue(CutoutCameraSource.TryIndexOf("camera:12", out index));
            Assert.AreEqual(12, index);
            Assert.IsTrue(CutoutCameraSource.TryIndexOf("0", out index));
            Assert.AreEqual(0, index);
            Assert.IsFalse(CutoutCameraSource.TryIndexOf("16", out index));
            Assert.IsFalse(CutoutCameraSource.TryIndexOf("-1", out index));
            Assert.IsFalse(CutoutCameraSource.TryIndexOf("camera:", out index));
            Assert.IsFalse(CutoutCameraSource.TryIndexOf("camera:one", out index));
            Assert.IsFalse(CutoutCameraSource.TryIndexOf("0.mp4", out index));
        }

        [Test]
        public void TheModeKeysSetTheMode() {
            demo.SetMode(CutoutMode.Original);
            Assert.AreEqual(CutoutMode.Original, view.Look.Mode);
            demo.SetMode(CutoutMode.Silhouette);
            Assert.AreEqual(CutoutMode.Silhouette, view.Look.Mode);
            demo.SetMode(CutoutMode.Person);
            Assert.AreEqual(CutoutMode.Person, view.Look.Mode);
        }

        [Test]
        public void TheBackdropKeyCyclesAndSkipsTheImageWhenThereIsNone() {
            view.Look.Backdrop = CutoutBackdrop.None;
            demo.CycleBackdrop();
            Assert.AreEqual(CutoutBackdrop.Checker, view.Look.Backdrop);
            demo.CycleBackdrop();
            Assert.AreEqual(CutoutBackdrop.Color, view.Look.Backdrop);
            demo.CycleBackdrop();
            Assert.AreEqual(CutoutBackdrop.Gradient, view.Look.Backdrop);
            demo.CycleBackdrop();
            Assert.AreEqual(CutoutBackdrop.None, view.Look.Backdrop, "Image is skipped without a picture");
            Texture2D picture = new Texture2D(4, 4);
            view.Look.BackdropImage = picture;
            demo.CycleBackdrop();
            demo.CycleBackdrop();
            demo.CycleBackdrop();
            demo.CycleBackdrop();
            Assert.AreEqual(CutoutBackdrop.Image, view.Look.Backdrop, "and reached with one");
            Object.DestroyImmediate(picture);
        }

        [Test]
        public void ChoosingTheImageBackdropWithoutAPictureChangesNothing() {
            view.Look.Backdrop = CutoutBackdrop.Checker;
            demo.SetBackdrop(CutoutBackdrop.Image);
            Assert.AreEqual(CutoutBackdrop.Checker, view.Look.Backdrop);
            demo.SetBackdrop(CutoutBackdrop.Gradient);
            Assert.AreEqual(CutoutBackdrop.Gradient, view.Look.Backdrop);
        }

        [Test]
        public void TheColourKeyCyclesThroughRedFirstAndWrapsAround() {
            Assert.AreEqual(0, demo.SilhouetteIndex);
            demo.SetSilhouetteColor(0);
            Color red = view.Look.SilhouetteColor;
            Assert.Greater(red.r, 0.9f);
            Assert.Less(red.g, 0.2f);
            Assert.Less(red.b, 0.2f);
            int steps = 0;
            while (steps < 7) {
                demo.CycleSilhouetteColor();
                steps = steps + 1;
            }
            Assert.AreEqual(0, demo.SilhouetteIndex, "seven colours, so seven presses come back to red");
            Assert.AreEqual(red, view.Look.SilhouetteColor);
        }

        [Test]
        public void ChoosingAColourOutOfRangeIsClampedAndTheGlowFollowsTheColour() {
            demo.SetSilhouetteColor(99);
            Assert.AreEqual(6, demo.SilhouetteIndex);
            demo.SetSilhouetteColor(-4);
            Assert.AreEqual(0, demo.SilhouetteIndex);
            Color glow = view.Look.GlowColor;
            Assert.GreaterOrEqual(glow.r, view.Look.SilhouetteColor.r, "the halo is a lighter shade of the colour it surrounds");
            Assert.GreaterOrEqual(glow.g, view.Look.SilhouetteColor.g);
        }

        [Test]
        public void TheGlowMirrorAndFitKeysToggle() {
            bool glow = view.Look.Glow;
            demo.ToggleGlow();
            Assert.AreEqual(!glow, view.Look.Glow);
            demo.ToggleGlow();
            Assert.AreEqual(glow, view.Look.Glow);
            view.Look.Mirror = CutoutMirror.Auto;
            demo.CycleMirror();
            Assert.AreEqual(CutoutMirror.Off, view.Look.Mirror);
            demo.CycleMirror();
            Assert.AreEqual(CutoutMirror.On, view.Look.Mirror);
            demo.CycleMirror();
            Assert.AreEqual(CutoutMirror.Auto, view.Look.Mirror);
            view.Fit = CutoutFit.Contain;
            demo.ToggleFit();
            Assert.AreEqual(CutoutFit.Cover, view.Fit);
            demo.ToggleFit();
            Assert.AreEqual(CutoutFit.Contain, view.Fit);
        }

        [Test]
        public void SteppingTheCameraBeforeTheHostIsUpDoesNothing() {
            demo.StepCamera(1);
            demo.ApplySourceText();
            Assert.AreEqual(0, demo.SilhouetteIndex);
        }
    }
}
