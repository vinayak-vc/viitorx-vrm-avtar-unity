using UnityEngine;

using NUnit.Framework;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="CutoutView"/>: what it hands the shader (mode, mirror, colours, glow, backdrop) and where it puts the two quads for a camera. The
    /// shader itself is judged by eye in the Cutout showcase; these tests pin the numbers it is given. The view draws one frame per
    /// <see cref="CutoutView.Refresh"/>, which a test calls directly (edit-mode components get no Update).
    /// </summary>
    public sealed class CutoutViewTests {
        private GameObject cameraObject;
        private Camera camera;
        private GameObject viewObject;
        private CutoutView view;
        private CutoutTextures textures;

        [SetUp]
        public void SetUp() {
            cameraObject = new GameObject("Cutout Test Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 2f;
            viewObject = new GameObject("Cutout Test View");
            view = viewObject.AddComponent<CutoutView>();
            view.ViewCamera = camera;
            textures = new CutoutTextures();
            view.Bind(textures);
        }

        [TearDown]
        public void TearDown() {
            view.Release();
            Object.DestroyImmediate(viewObject);
            Object.DestroyImmediate(cameraObject);
            textures.Dispose();
        }

        private static CutoutFrame FrameFrom(CutoutTestFrames.Spec spec) {
            CutoutAssembler assembler = new CutoutAssembler();
            foreach (byte[] datagram in CutoutTestFrames.Encode(spec)) {
                assembler.Add(datagram, datagram.Length);
            }
            Assert.AreEqual(1L, assembler.Completed);
            CutoutFrame frame = new CutoutFrame();
            assembler.SwapFrameWith(frame);
            return frame;
        }

        private static CutoutFrame FixtureFrame(string family, int count) {
            CutoutAssembler assembler = new CutoutAssembler();
            int i = 0;
            while (i < count) {
                byte[] datagram = VisionFixtures.Load("cutout_" + family + "_0" + i + ".bin");
                assembler.Add(datagram, datagram.Length);
                i = i + 1;
            }
            CutoutFrame frame = new CutoutFrame();
            assembler.SwapFrameWith(frame);
            return frame;
        }

        private void Show(CutoutFrame frame) {
            Assert.IsTrue(textures.Upload(frame));
            view.Refresh();
        }

        private Transform Quad(string name) {
            return viewObject.transform.Find(name);
        }

        [Test]
        public void NothingIsDrawnBeforeTheFirstFrame() {
            view.Refresh();
            Assert.AreEqual(0, viewObject.transform.childCount, "no quads, no materials, no meshes until there is something to show");
            Assert.IsNull(view.PersonInstance);
        }

        [Test]
        public void TheFirstFrameCreatesThePersonAndTheBackdropQuadsInFrontOfTheCamera() {
            Show(FixtureFrame("rgba", 4));
            Transform person = Quad("Cutout Person");
            Transform backdrop = Quad("Cutout Backdrop");
            Assert.IsNotNull(person);
            Assert.IsNotNull(backdrop);
            Assert.IsTrue(person.gameObject.activeSelf);
            Assert.IsTrue(backdrop.gameObject.activeSelf, "the default backdrop is the checkerboard");
            Assert.AreEqual(5f, person.position.z, 1e-4f, "the camera is at the origin looking along +Z; the distance is 5");
            Assert.Greater(backdrop.position.z, person.position.z, "the backdrop sits behind the person");
            Assert.IsNotNull(view.PersonInstance.shader);
            Assert.AreEqual("VirtualMirror/Cutout", view.PersonInstance.shader.name);
            Assert.AreEqual("VirtualMirror/CutoutBackdrop", view.BackdropInstance.shader.name);
        }

        [Test]
        public void TheShaderIsGivenTheTexturesTheModeAndTheFlags() {
            Show(FixtureFrame("rgba", 4));
            Material person = view.PersonInstance;
            Assert.AreSame(textures.ColorTexture, person.GetTexture("_ColorTex"));
            Assert.AreSame(textures.MaskTexture, person.GetTexture("_MaskTex"));
            Assert.AreEqual(1f, person.GetFloat("_Mode"), "Person is the default");
            Assert.AreEqual(1f, person.GetFloat("_Opacity"));
            Assert.AreEqual(1f, person.GetFloat("_EdgeContrast"));
        }

        [Test]
        public void SwitchingTheModeChangesTheNextFrameAndNothingElse() {
            Show(FixtureFrame("rgba", 4));
            Material person = view.PersonInstance;
            view.Look.Mode = CutoutMode.Original;
            view.Refresh();
            Assert.AreEqual(0f, person.GetFloat("_Mode"));
            Assert.AreEqual(CutoutMode.Original, view.EffectiveMode);
            view.Look.Mode = CutoutMode.Silhouette;
            view.Refresh();
            Assert.AreEqual(2f, person.GetFloat("_Mode"));
            view.Look.Mode = CutoutMode.Person;
            view.Refresh();
            Assert.AreEqual(1f, person.GetFloat("_Mode"));
            Assert.AreSame(textures.ColorTexture, person.GetTexture("_ColorTex"), "the textures were not rebuilt for a mode change");
            Assert.AreSame(person, view.PersonInstance);
        }

        [Test]
        public void AStreamWithNoPictureCanOnlyShowASilhouette() {
            Show(FixtureFrame("maskonly", 1));
            Assert.IsFalse(textures.HasColor);
            view.Look.Mode = CutoutMode.Person;
            view.Refresh();
            Assert.AreEqual(CutoutMode.Silhouette, view.EffectiveMode, "there is no picture to cut out");
            Assert.AreEqual(2f, view.PersonInstance.GetFloat("_Mode"));
            view.Look.Mode = CutoutMode.Original;
            view.Refresh();
            Assert.AreEqual(CutoutMode.Silhouette, view.EffectiveMode);
        }

        [Test]
        public void TheSilhouetteColourReachesTheShader() {
            Show(FixtureFrame("rgba", 4));
            view.Look.SilhouetteColor = new Color(0.1f, 0.85f, 1f, 1f);
            view.Refresh();
            Color sent = view.PersonInstance.GetColor("_SilhouetteColor");
            Assert.AreEqual(0.1f, sent.r, 1e-3f);
            Assert.AreEqual(0.85f, sent.g, 1e-3f);
            Assert.AreEqual(1f, sent.b, 1e-3f);
        }

        [Test]
        public void MirrorAutoFlipsALiveCameraTheSenderDidNotFlipAndNothingElse() {
            CutoutTestFrames.Spec liveRaw = new CutoutTestFrames.Spec();
            liveRaw.Flags = CutoutWire.FlagLive;
            Show(FrameFrom(liveRaw));
            Assert.IsTrue(view.MirrorApplied, "a webcam in a selfie view");
            Assert.AreEqual(1f, view.PersonInstance.GetFloat("_Mirror"));

            CutoutTestFrames.Spec flipped = new CutoutTestFrames.Spec();
            flipped.Sequence = 2000u;
            flipped.Flags = CutoutWire.FlagLive | CutoutWire.FlagMirrored;
            Show(FrameFrom(flipped));
            Assert.IsFalse(view.MirrorApplied, "the sender already mirrored it: flipping again would undo it");

            CutoutTestFrames.Spec file = new CutoutTestFrames.Spec();
            file.Sequence = 2001u;
            file.Flags = 0;
            Show(FrameFrom(file));
            Assert.IsFalse(view.MirrorApplied, "a video file is shown as it is");
            Assert.AreEqual(0f, view.PersonInstance.GetFloat("_Mirror"));
        }

        [Test]
        public void MirrorOnAndOffOverrideTheAutomaticChoice() {
            Show(FixtureFrame("rgba", 4));
            view.Look.Mirror = CutoutMirror.On;
            view.Refresh();
            Assert.IsTrue(view.MirrorApplied);
            Assert.AreEqual(1f, view.PersonInstance.GetFloat("_Mirror"));
            view.Look.Mirror = CutoutMirror.Off;
            view.Refresh();
            Assert.IsFalse(view.MirrorApplied);
            Assert.AreEqual(0f, view.PersonInstance.GetFloat("_Mirror"));
        }

        [Test]
        public void TheGlowIsOffUntilAskedForAndIsNeverAppliedToTheCameraPicture() {
            Show(FixtureFrame("rgba", 4));
            Material person = view.PersonInstance;
            Assert.AreEqual(0f, person.GetFloat("_GlowWidth"));
            Assert.AreEqual(0f, person.GetFloat("_GlowStrength"));
            view.Look.Glow = true;
            view.Look.GlowWidth = 7f;
            view.Look.GlowStrength = 1.5f;
            view.Refresh();
            Assert.AreEqual(7f, person.GetFloat("_GlowWidth"));
            Assert.AreEqual(1.5f, person.GetFloat("_GlowStrength"));
            view.Look.Mode = CutoutMode.Original;
            view.Refresh();
            Assert.AreEqual(0f, person.GetFloat("_GlowWidth"), "the Original mode draws the whole camera: there is no outline to glow");
        }

        [Test]
        public void ValuesOutsideTheShadersRangeAreClampedBeforeTheyAreSent() {
            Show(FixtureFrame("rgba", 4));
            view.Look.EdgeContrast = 100f;
            view.Look.Opacity = 5f;
            view.Look.Glow = true;
            view.Look.GlowWidth = 400f;
            view.Look.GlowStrength = 9f;
            view.Refresh();
            Material person = view.PersonInstance;
            Assert.AreEqual(4f, person.GetFloat("_EdgeContrast"));
            Assert.AreEqual(1f, person.GetFloat("_Opacity"));
            Assert.AreEqual(16f, person.GetFloat("_GlowWidth"));
            Assert.AreEqual(2f, person.GetFloat("_GlowStrength"));
        }

        [Test]
        public void EachBackdropReachesTheBackdropShaderAsItsKind() {
            Show(FixtureFrame("rgba", 4));
            Material backdrop = view.BackdropInstance;
            view.Look.Backdrop = CutoutBackdrop.Checker;
            view.Refresh();
            Assert.AreEqual(0f, backdrop.GetFloat("_Kind"));
            Assert.IsTrue(Quad("Cutout Backdrop").gameObject.activeSelf);
            view.Look.Backdrop = CutoutBackdrop.Color;
            view.Look.BackdropColor = new Color(0.5f, 0.25f, 0.125f, 1f);
            view.Refresh();
            Assert.AreEqual(1f, backdrop.GetFloat("_Kind"));
            Assert.AreEqual(0.5f, backdrop.GetColor("_ColorA").r, 1e-3f);
            view.Look.Backdrop = CutoutBackdrop.Gradient;
            view.Refresh();
            Assert.AreEqual(2f, backdrop.GetFloat("_Kind"));
            view.Look.Backdrop = CutoutBackdrop.Image;
            view.Refresh();
            Assert.AreEqual(1f, backdrop.GetFloat("_Kind"), "no image assigned: a plain colour instead of a black hole");
            Texture2D picture = new Texture2D(40, 20);
            view.Look.BackdropImage = picture;
            view.Refresh();
            Assert.AreEqual(3f, backdrop.GetFloat("_Kind"));
            Assert.AreEqual(2f, backdrop.GetFloat("_ImageAspect"), 1e-4f);
            Assert.AreSame(picture, backdrop.GetTexture("_ImageTex"));
            Object.DestroyImmediate(picture);
            view.Look.BackdropImage = null;
            view.Look.Backdrop = CutoutBackdrop.None;
            view.Refresh();
            Assert.IsFalse(Quad("Cutout Backdrop").gameObject.activeSelf, "None leaves the scene behind the view showing through");
            Assert.IsTrue(Quad("Cutout Person").gameObject.activeSelf);
        }

        [Test]
        public void ThePictureIsContainedInTheViewKeepingItsAspect() {
            Show(FixtureFrame("rgba", 4));
            // The view is 20 x 10 (orthographic size 5, aspect 2); the reference picture is 16:12 = 4:3.
            Vector3 scale = Quad("Cutout Person").lossyScale;
            Assert.AreEqual(10f, scale.y, 1e-3f, "a taller picture fills the height");
            Assert.AreEqual(10f * 4f / 3f, scale.x, 1e-3f);
            Vector3 backdrop = Quad("Cutout Backdrop").lossyScale;
            Assert.AreEqual(20f, backdrop.x, 1e-3f, "the backdrop fills the whole view");
            Assert.AreEqual(10f, backdrop.y, 1e-3f);
        }

        [Test]
        public void CoverFillsTheViewAndCropsWhatDoesNotFit() {
            Show(FixtureFrame("rgba", 4));
            view.Fit = CutoutFit.Cover;
            view.Refresh();
            Vector3 scale = Quad("Cutout Person").lossyScale;
            Assert.AreEqual(20f, scale.x, 1e-3f);
            Assert.AreEqual(20f * 3f / 4f, scale.y, 1e-3f, "15 high in a 10 high view: the top and bottom are cropped");
        }

        [Test]
        public void AWidePictureInASquareViewIsFittedByItsWidth() {
            camera.aspect = 1f;
            CutoutTestFrames.Spec wide = new CutoutTestFrames.Spec();
            wide.ColorWidth = 32;
            wide.ColorHeight = 18;
            wide.Color = CutoutTestFrames.RgbaPicture(32, 18, 3);
            Show(FrameFrom(wide));
            Vector3 scale = Quad("Cutout Person").lossyScale;
            Assert.AreEqual(10f, scale.x, 1e-3f);
            Assert.AreEqual(10f * 18f / 32f, scale.y, 1e-3f);
        }

        [Test]
        public void APerspectiveCameraSeesTheQuadAtTheSameFractionOfItsView() {
            camera.orthographic = false;
            camera.fieldOfView = 40f;
            Show(FixtureFrame("rgba", 4));
            float height = 2f * 5f * Mathf.Tan(20f * Mathf.Deg2Rad);
            Vector3 scale = Quad("Cutout Person").lossyScale;
            Assert.AreEqual(height, scale.y, 1e-3f);
            Assert.AreEqual(height * 4f / 3f, scale.x, 1e-3f);
            Vector3 backdrop = Quad("Cutout Backdrop").lossyScale;
            float backdropHeight = 2f * 5.02f * Mathf.Tan(20f * Mathf.Deg2Rad);
            Assert.AreEqual(backdropHeight, backdrop.y, 1e-3f, "farther away, so large enough to still cover the whole view");
        }

        [Test]
        public void TheQuadsFollowTheCameraWhereverItIsLooking() {
            Show(FixtureFrame("rgba", 4));
            cameraObject.transform.position = new Vector3(3f, 1f, -2f);
            cameraObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            view.Refresh();
            Transform person = Quad("Cutout Person");
            Vector3 expected = cameraObject.transform.position + cameraObject.transform.forward * 5f;
            Assert.AreEqual(0f, Vector3.Distance(expected, person.position), 1e-3f);
            Assert.AreEqual(0f, Quaternion.Angle(cameraObject.transform.rotation, person.rotation), 1e-3f);
        }

        [Test]
        public void UnbindingHidesTheViewAndDoesNotDestroyTheTextures() {
            Show(FixtureFrame("rgba", 4));
            view.Bind(null);
            view.Refresh();
            Assert.IsFalse(Quad("Cutout Person").gameObject.activeSelf);
            Assert.IsFalse(Quad("Cutout Backdrop").gameObject.activeSelf);
            Assert.IsTrue(textures.HasFrame, "the host owns the textures");
            view.Bind(textures);
            view.Refresh();
            Assert.IsTrue(Quad("Cutout Person").gameObject.activeSelf);
        }

        [Test]
        public void ANewTextureSizeIsBoundToTheMaterialAgain() {
            Show(FixtureFrame("rgba", 4));
            Texture first = view.PersonInstance.GetTexture("_ColorTex");
            CutoutTestFrames.Spec bigger = new CutoutTestFrames.Spec();
            bigger.ColorWidth = 32;
            bigger.ColorHeight = 24;
            bigger.Color = CutoutTestFrames.RgbaPicture(32, 24, 5);
            Show(FrameFrom(bigger));
            Texture second = view.PersonInstance.GetTexture("_ColorTex");
            Assert.AreNotSame(first, second);
            Assert.AreSame(textures.ColorTexture, second);
            Assert.AreEqual(32, second.width);
        }

        [Test]
        public void ReleaseRemovesTheQuadsAndAllowsAFreshStart() {
            Show(FixtureFrame("rgba", 4));
            view.Release();
            view.Release();
            Assert.AreEqual(0, viewObject.transform.childCount);
            Assert.IsNull(view.PersonInstance);
            view.Refresh();
            Assert.AreEqual(2, viewObject.transform.childCount, "the next refresh builds them again");
        }

        [Test]
        public void TheRuntimeMaterialsAreCopiesSoAnAssetIsNeverChanged() {
            Material asset = new Material(Shader.Find("VirtualMirror/Cutout"));
            asset.SetFloat("_Mode", 2f);
            SerializedMaterial(asset);
            Show(FixtureFrame("rgba", 4));
            view.Look.Mode = CutoutMode.Original;
            view.Refresh();
            Assert.AreEqual(2f, asset.GetFloat("_Mode"), "the asset keeps its own value");
            Assert.AreNotSame(asset, view.PersonInstance);
            Object.DestroyImmediate(asset);
        }

        private void SerializedMaterial(Material asset) {
            System.Reflection.FieldInfo field = typeof(CutoutView).GetField("personMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "the serialized field the scene builder fills");
            field.SetValue(view, asset);
        }
    }
}
