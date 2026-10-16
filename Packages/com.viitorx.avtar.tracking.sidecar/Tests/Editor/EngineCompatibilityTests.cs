using System;
using System.IO;

using NUnit.Framework;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="EngineCompatibility"/>: the engine and these packages are released apart, and what ties them is the major number of the wire specification. A definite
    /// mismatch blocks the start with a message; an engine that cannot answer is not blocked here (starting it says what is wrong). The real engine is asked when the
    /// environment names it (<c>VIITORX_TEST_ENGINE</c>, the engine folder).
    /// </summary>
    public sealed class EngineCompatibilityTests {
        private int before;

        [SetUp]
        public void SetUp() {
            before = EngineCompatibility.SupportedWireSpecMajor;
        }

        [TearDown]
        public void TearDown() {
            EngineCompatibility.SupportedWireSpecMajor = before;
        }

        [Test]
        public void OnlyTheMajorNumberThisFamilyReadsIsSupported() {
            string reason;
            Assert.IsTrue(EngineCompatibility.IsWireSpecSupported("1.0", out reason), reason);
            Assert.IsTrue(EngineCompatibility.IsWireSpecSupported("1.7", out reason), reason);
            Assert.IsTrue(EngineCompatibility.IsWireSpecSupported("1", out reason), reason);
            Assert.IsTrue(EngineCompatibility.IsWireSpecSupported(" 1.0 ", out reason), reason);
            Assert.IsFalse(EngineCompatibility.IsWireSpecSupported("2.0", out reason));
            StringAssert.Contains("2.0", reason);
            StringAssert.Contains("1.x", reason);
            Assert.IsFalse(EngineCompatibility.IsWireSpecSupported("0.9", out reason));
            Assert.IsFalse(EngineCompatibility.IsWireSpecSupported("", out reason));
            StringAssert.Contains("does not say", reason);
            Assert.IsFalse(EngineCompatibility.IsWireSpecSupported(null, out reason));
            Assert.IsFalse(EngineCompatibility.IsWireSpecSupported("x.y", out reason));
            StringAssert.Contains("not a version", reason);
        }

        [Test]
        public void TheSupportedMajorFollowsTheSetting() {
            string reason;
            EngineCompatibility.SupportedWireSpecMajor = 2;
            Assert.IsTrue(EngineCompatibility.IsWireSpecSupported("2.3", out reason), reason);
            Assert.IsFalse(EngineCompatibility.IsWireSpecSupported("1.0", out reason));
        }

        [Test]
        public void AFileThatIsNotThereIsUnknownAndDoesNotBlock() {
            EngineCompatibilityResult result = EngineCompatibility.Check(Path.Combine(Path.GetTempPath(), "no-such-engine-" + Guid.NewGuid().ToString("N") + ".exe"));
            Assert.AreEqual(EngineCompatibilityStatus.Unknown, result.Status);
            Assert.IsFalse(result.Blocks, "starting it will report that it is missing; this check does not");
            StringAssert.Contains("does not exist", result.Message);
        }

        [Test]
        public void AProgramThatDoesNotAnswerLikeAnEngineIsUnknownAndDoesNotBlock() {
            // PING.EXE exits 1 on the unknown option: it runs, but it is not an engine
            string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
            if (!File.Exists(ping)) {
                Assert.Ignore("PING.EXE is not available.");
            }
            EngineCompatibilityResult result = EngineCompatibility.Check(ping);
            Assert.AreEqual(EngineCompatibilityStatus.Unknown, result.Status);
            Assert.IsFalse(result.Blocks);
            StringAssert.Contains("Could not ask", result.Message);
        }

        [Test]
        public void TheRealEngineIsCompatibleThenNotWhenThePackagesReadAnotherGeneration() {
            string folder = Environment.GetEnvironmentVariable("VIITORX_TEST_ENGINE");
            if (string.IsNullOrEmpty(folder)) {
                Assert.Ignore("VIITORX_TEST_ENGINE is not set: the compiled engine is not part of this run.");
            }
            string exe = Path.Combine(folder, "viitorx-tracking.exe");
            EngineCompatibilityResult result = EngineCompatibility.Check(exe);
            Assert.AreEqual(EngineCompatibilityStatus.Compatible, result.Status, result.Message);
            Assert.IsFalse(result.Blocks);
            Assert.AreEqual("1.0", result.WireSpec);
            Assert.IsNotEmpty(result.EngineVersion);
            Assert.AreSame(result, EngineCompatibility.Check(exe), "the second question is answered from memory: the engine is started once");
            EngineCompatibility.SupportedWireSpecMajor = 2;
            EngineCompatibilityResult other = EngineCompatibility.Check(exe);
            Assert.AreEqual(EngineCompatibilityStatus.Incompatible, other.Status, other.Message);
            Assert.IsTrue(other.Blocks);
            StringAssert.Contains("1.0", other.Message);
            StringAssert.Contains("2.x", other.Message);
            StringAssert.Contains("INSTALL", other.Message, "the message says where to look");
        }
    }
}
