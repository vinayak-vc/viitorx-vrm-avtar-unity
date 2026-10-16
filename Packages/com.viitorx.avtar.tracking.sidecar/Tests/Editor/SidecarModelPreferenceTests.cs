using System.Collections.Generic;
using NUnit.Framework;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// ADR-082 made the FP16 pose model the default, but Unity launched the sidecar with an explicit FP32
    /// path, which bypassed the sidecar's own preference. These pin the rule Unity now applies: take the
    /// FP16 build when it exists, never invent a path, and leave everything else exactly as it was.
    /// </summary>
    public sealed class SidecarModelPreferenceTests {
        private const string Fp32 = "D:/proj/Assets/SentisModel/rtmw3d-x.onnx";
        private const string Fp16 = "D:/proj/Assets/SentisModel/rtmw3d-x-fp16.onnx";

        private static System.Func<string, bool> Existing(params string[] paths) {
            HashSet<string> set = new HashSet<string>(paths);
            return delegate(string p) {
                return set.Contains(p);
            };
        }

        [Test]
        public void TheFp16SiblingIsTheSameNameWithASuffix() {
            Assert.AreEqual(Fp16, SidecarPaths.Fp16Sibling(Fp32));
            Assert.AreEqual("a/b/C-fp16.ONNX", SidecarPaths.Fp16Sibling("a/b/C.ONNX"),
                            "the extension is matched case-insensitively and keeps its case");
            Assert.AreEqual("m-fp16.onnx", SidecarPaths.Fp16Sibling("m.onnx"));
        }

        [Test]
        public void ThereIsNoSiblingForAnythingThatIsNotAnFp32OnnxPath() {
            Assert.AreEqual(string.Empty, SidecarPaths.Fp16Sibling(null));
            Assert.AreEqual(string.Empty, SidecarPaths.Fp16Sibling(string.Empty));
            Assert.AreEqual(string.Empty, SidecarPaths.Fp16Sibling("D:/m/model.bytes"), "not an .onnx file");
            Assert.AreEqual(string.Empty, SidecarPaths.Fp16Sibling(Fp16), "already the FP16 build: not -fp16-fp16");
        }

        [Test]
        public void TheFp16BuildIsUsedWhenItExists() {
            Assert.AreEqual(Fp16, SidecarPaths.PreferFp16(Fp32, Existing(Fp32, Fp16)));
            Assert.AreEqual(Fp16, SidecarPaths.PreferFp16(Fp32, Existing(Fp16)), "even if only the FP16 file is there");
        }

        [Test]
        public void AMachineWithoutTheFp16BuildKeepsTheModelItAlreadyHad() {
            Assert.AreEqual(Fp32, SidecarPaths.PreferFp16(Fp32, Existing(Fp32)),
                            "a machine that never ran the converter must launch exactly as before");
            Assert.AreEqual(Fp32, SidecarPaths.PreferFp16(Fp32, Existing()));
        }

        [Test]
        public void AnExplicitFp16PathOrAnOddPathIsLeftAlone() {
            Assert.AreEqual(Fp16, SidecarPaths.PreferFp16(Fp16, Existing(Fp16)));
            Assert.AreEqual("D:/m/model.bytes", SidecarPaths.PreferFp16("D:/m/model.bytes", Existing("D:/m/model-fp16.bytes")));
            Assert.AreEqual(Fp32, SidecarPaths.PreferFp16(Fp32, null), "no way to check -> no change");
            Assert.IsNull(SidecarPaths.PreferFp16(null, Existing(Fp16)));
        }

        [Test]
        public void TheDefaultEditorModelNameHasTheSiblingTheConverterWrites() {
            string fp32 = "D:/proj/Assets/" + SidecarPaths.EditorModelRelativeToAssets;
            Assert.AreEqual("D:/proj/Assets/SentisModel/rtmw3d-x-fp16.onnx", SidecarPaths.Fp16Sibling(fp32),
                            "tools/model/f45_make_fp16.py writes rtmw3d-x-fp16.onnx beside rtmw3d-x.onnx");
        }
    }
}
