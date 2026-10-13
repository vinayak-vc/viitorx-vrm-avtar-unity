using System;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Core;
using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="CutoutStreamBinder"/>: the drop-in that puts the cutout in a scene with an AppBootstrap. The host is a real
    /// <see cref="VisionHost"/> with sender auto-start off (so no process starts); the bootstrap lookup in Update is the only part not covered.
    /// </summary>
    public sealed class CutoutStreamBinderTests {
        private sealed class QuietLog : ILogService {
            public void Log(LogLevel level, string message) {
            }

            public void LogException(Exception exception, string context) {
            }

            public void Flush() {
            }
        }

        private VisionHost host;
        private GameObject binderObject;
        private GameObject viewObject;
        private CutoutStreamBinder binder;
        private CutoutView view;

        [SetUp]
        public void SetUp() {
            VisionSettings settings = new VisionSettings();
            settings.AutoStartSenders = false;
            settings.HandsMode = VisionSenderMode.Off;
            settings.FaceMode = VisionSenderMode.Off;
            settings.CutoutMode = VisionSenderMode.Auto;
            host = new VisionHost(new QuietLog(), settings);
            viewObject = new GameObject("Binder Test View");
            view = viewObject.AddComponent<CutoutView>();
            binderObject = new GameObject("Binder Test");
            binder = binderObject.AddComponent<CutoutStreamBinder>();
            binder.View = view;
        }

        [TearDown]
        public void TearDown() {
            binder.Unbind();
            host.Dispose();
            view.Release();
            UnityEngine.Object.DestroyImmediate(binderObject);
            UnityEngine.Object.DestroyImmediate(viewObject);
        }

        [Test]
        public void BindingRequestsTheCutoutStreamAndNothingElse() {
            Assert.IsFalse(binder.IsBound);
            Assert.IsFalse(host.HasDemand(VisionStream.Cutout));
            binder.Bind(host);
            Assert.IsTrue(binder.IsBound);
            Assert.IsTrue(host.HasDemand(VisionStream.Cutout), "that is what starts the sender in Auto mode");
            Assert.IsFalse(host.HasDemand(VisionStream.Hands));
            Assert.IsFalse(host.HasDemand(VisionStream.Face));
        }

        [Test]
        public void ASecondBindWithoutAnUnbindDoesNotCountTwice() {
            binder.Bind(host);
            binder.Bind(host);
            binder.Unbind();
            Assert.IsFalse(host.HasDemand(VisionStream.Cutout), "one request, one release");
        }

        [Test]
        public void UnbindingReleasesTheStreamAndASecondUnbindOwesNothing() {
            binder.Bind(host);
            binder.Unbind();
            Assert.IsFalse(binder.IsBound);
            Assert.IsFalse(host.HasDemand(VisionStream.Cutout));
            host.AddDemand(VisionStream.Cutout);
            binder.Unbind();
            Assert.IsTrue(host.HasDemand(VisionStream.Cutout), "an unbind that was never bound must not release somebody else's request");
            host.RemoveDemand(VisionStream.Cutout);
        }

        [Test]
        public void ANullHostIsIgnored() {
            binder.Bind(null);
            Assert.IsFalse(binder.IsBound);
        }

        [Test]
        public void TheViewDrawsWhatTheHostReceivesAndStopsWhenUnbound() {
            binder.Bind(host);
            CutoutAssembler assembler = new CutoutAssembler();
            int i = 0;
            while (i < 4) {
                byte[] datagram = VisionFixtures.Load("cutout_rgba_0" + i + ".bin");
                assembler.Add(datagram, datagram.Length);
                i = i + 1;
            }
            CutoutFrame frame = new CutoutFrame();
            assembler.SwapFrameWith(frame);
            Assert.IsTrue(host.CutoutTextures.Upload(frame));
            view.Refresh();
            Assert.IsNotNull(view.PersonInstance, "the view drew the host's picture");
            Assert.AreSame(host.CutoutTextures.MaskTexture, view.PersonInstance.GetTexture("_MaskTex"));
            binder.Unbind();
            view.Refresh();
            Assert.IsFalse(viewObject.transform.Find("Cutout Person").gameObject.activeSelf);
        }

        [Test]
        public void ABinderWithNoViewStillRequestsTheStreamForOtherSystemsToRead() {
            binder.View = null;
            binder.Bind(host);
            Assert.IsTrue(host.HasDemand(VisionStream.Cutout));
            binder.Unbind();
            Assert.IsFalse(host.HasDemand(VisionStream.Cutout));
        }
    }
}
