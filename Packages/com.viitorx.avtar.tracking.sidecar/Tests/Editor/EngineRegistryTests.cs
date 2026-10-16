using NUnit.Framework;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>The registry read behind the machine-wide install. The installer's own key does not exist until the installer has run, so the read is checked on a value every Windows has.</summary>
    public sealed class EngineRegistryTests {
#if UNITY_EDITOR_WIN
        private const string WindowsKey = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";

        [Test]
        public void ATextValueThatExistsIsRead() {
            string windows = EngineRegistry.ReadMachineString(WindowsKey, "SystemRoot");
            Assert.IsNotEmpty(windows);
            string expected = System.Environment.GetEnvironmentVariable("SystemRoot").TrimEnd('\\');
            Assert.IsTrue(string.Equals(expected, windows.TrimEnd('\\'), System.StringComparison.OrdinalIgnoreCase), "expected " + expected + " but the registry said " + windows);
        }

        [Test]
        public void AMissingKeyOrValueIsEmptyNotAnError() {
            Assert.IsEmpty(EngineRegistry.ReadMachineString("SOFTWARE\\Viitorx\\NoSuchKey", "InstallDir"));
            Assert.IsEmpty(EngineRegistry.ReadMachineString(WindowsKey, "NoSuchValue"));
        }

        [Test]
        public void ANumberIsNotText() {
            Assert.IsEmpty(EngineRegistry.ReadMachineString(WindowsKey, "CurrentMajorVersionNumber"));
        }
#endif

        [Test]
        public void TheInstallDirReadNeverThrowsWhateverTheMachineHolds() {
            Assert.DoesNotThrow(delegate () { EngineRegistry.ReadInstallDir(); });
        }
    }
}
