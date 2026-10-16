using System.Collections.Generic;

using NUnit.Framework;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// Where the tracking engine is looked for, in what order, and what an unusable path means. The search touches the disk only through the three
    /// delegates, so every case here is a fake disk: no file is read.
    /// </summary>
    public sealed class EngineLocatorTests {
        private sealed class FakeDisk {
            public readonly HashSet<string> Files = new HashSet<string>();
            public readonly HashSet<string> Directories = new HashSet<string>();

            public void AddEngine(string folder) {
                AddDirectory(folder);
                Files.Add(folder + "/viitorx-tracking.exe");
            }

            public void AddDirectory(string folder) {
                string path = folder;
                while (path.Length > 0) {
                    Directories.Add(path);
                    int slash = path.LastIndexOf('/');
                    path = slash > 0 ? path.Substring(0, slash) : string.Empty;
                }
            }

            public bool FileExists(string path) {
                return Files.Contains(path.Replace('\\', '/'));
            }

            public bool DirectoryExists(string path) {
                return Directories.Contains(path.Replace('\\', '/'));
            }

            public string[] SubDirectories(string path) {
                string prefix = path.Replace('\\', '/').TrimEnd('/') + "/";
                List<string> found = new List<string>();
                foreach (string directory in Directories) {
                    if (directory.StartsWith(prefix) && directory.IndexOf('/', prefix.Length) < 0) {
                        found.Add(directory);
                    }
                }
                return found.ToArray();
            }
        }

        private static EngineLocation Resolve(EngineLocatorInputs inputs, FakeDisk disk) {
            return EngineLocator.Resolve(inputs, disk.FileExists, disk.DirectoryExists, disk.SubDirectories);
        }

        private static EngineLocatorInputs EveryPlace() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.SettingPath = "C:/set/viitorx-tracking.exe";
            inputs.EnvironmentValue = "C:/env";
            inputs.RegistryInstallDir = "C:/Program Files/Viitorx/TrackingEngine";
            inputs.LocalAppData = "C:/Users/me/AppData/Local";
            inputs.StreamingAssetsPath = "C:/game/StreamingAssets";
            return inputs;
        }

        private static FakeDisk DiskWithEveryPlace() {
            FakeDisk disk = new FakeDisk();
            disk.AddEngine("C:/set");
            disk.AddEngine("C:/env");
            disk.AddEngine("C:/Program Files/Viitorx/TrackingEngine");
            disk.AddEngine("C:/Users/me/AppData/Local/Viitorx/TrackingEngine/0.1.0");
            disk.AddEngine("C:/game/StreamingAssets/Viitorx/TrackingEngine");
            return disk;
        }

        [Test]
        public void TheSettingBeatsEveryOtherPlace() {
            EngineLocation found = Resolve(EveryPlace(), DiskWithEveryPlace());
            Assert.AreEqual(EngineKind.Executable, found.Kind);
            Assert.AreEqual(EngineSource.Setting, found.Source);
            Assert.AreEqual("C:/set/viitorx-tracking.exe", found.ExecutablePath);
        }

        [Test]
        public void EachPlaceIsTriedInTheDocumentedOrder() {
            EngineLocatorInputs inputs = EveryPlace();
            FakeDisk disk = DiskWithEveryPlace();
            inputs.SettingPath = string.Empty;
            Assert.AreEqual(EngineSource.Environment, Resolve(inputs, disk).Source);
            inputs.EnvironmentValue = string.Empty;
            Assert.AreEqual(EngineSource.Registry, Resolve(inputs, disk).Source);
            inputs.RegistryInstallDir = string.Empty;
            Assert.AreEqual(EngineSource.UserInstall, Resolve(inputs, disk).Source);
            inputs.LocalAppData = string.Empty;
            Assert.AreEqual(EngineSource.StreamingAssets, Resolve(inputs, disk).Source);
            inputs.StreamingAssetsPath = string.Empty;
            inputs.AllowPythonFallback = false;
            Assert.AreEqual(EngineKind.None, Resolve(inputs, disk).Kind);
        }

        [Test]
        public void ASettingThatNamesAFolderFindsTheExecutableInsideIt() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.SettingPath = "D:/tools/engine";
            FakeDisk disk = new FakeDisk();
            disk.AddEngine("D:/tools/engine");
            EngineLocation found = Resolve(inputs, disk);
            Assert.AreEqual("D:/tools/engine/viitorx-tracking.exe", found.ExecutablePath);
            Assert.AreEqual("D:/tools/engine", found.Folder);
        }

        [Test]
        public void AQuotedWindowsPathIsAccepted() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.SettingPath = "\"D:\\tools\\engine\\viitorx-tracking.exe\"";
            FakeDisk disk = new FakeDisk();
            disk.Files.Add("D:/tools/engine/viitorx-tracking.exe");
            disk.Files.Add("D:\\tools\\engine\\viitorx-tracking.exe");
            EngineLocation found = Resolve(inputs, disk);
            Assert.AreEqual(EngineKind.Executable, found.Kind);
            Assert.AreEqual("D:/tools/engine/viitorx-tracking.exe", found.ExecutablePath, "the path is handed on with forward slashes, as every other path here");
        }

        [Test]
        public void ABadSettingPathIsAnErrorAndNothingElseIsTried() {
            EngineLocatorInputs inputs = EveryPlace();
            inputs.SettingPath = "C:/typo/viitorx-tracking.exe";
            EngineLocation found = Resolve(inputs, DiskWithEveryPlace());
            Assert.AreEqual(EngineKind.None, found.Kind, "a typo must not quietly start a different engine");
            StringAssert.Contains("C:/typo/viitorx-tracking.exe", found.Problem);
            Assert.AreEqual(1, found.Tried.Count);
        }

        [Test]
        public void ABadEnvironmentValueIsNotedAndTheSearchContinues() {
            EngineLocatorInputs inputs = EveryPlace();
            inputs.SettingPath = string.Empty;
            inputs.EnvironmentValue = "C:/gone";
            EngineLocation found = Resolve(inputs, DiskWithEveryPlace());
            Assert.AreEqual(EngineSource.Registry, found.Source);
            Assert.AreEqual(1, found.Tried.Count);
            StringAssert.Contains("VIITORX_TRACKING_ENGINE=C:/gone", found.Tried[0]);
        }

        [Test]
        public void UsePythonProjectSkipsEveryCompiledEngine() {
            EngineLocatorInputs inputs = EveryPlace();
            inputs.UsePythonProject = true;
            inputs.PythonProjectPath = "C:/py";
            FakeDisk disk = DiskWithEveryPlace();
            disk.Files.Add("C:/py/.venv/Scripts/python.exe");
            EngineLocation found = Resolve(inputs, disk);
            Assert.AreEqual(EngineKind.PythonProject, found.Kind);
            Assert.AreEqual("C:/py", found.PythonPaths.Root);
            Assert.AreEqual("C:/py/.venv/Scripts/python.exe", found.PythonPaths.PythonExe);
        }

        [Test]
        public void UsePythonProjectWithoutAnInterpreterIsAnErrorNotAFallbackToTheCompiledEngine() {
            EngineLocatorInputs inputs = EveryPlace();
            inputs.UsePythonProject = true;
            inputs.PythonProjectPath = "C:/py";
            EngineLocation found = Resolve(inputs, DiskWithEveryPlace());
            Assert.AreEqual(EngineKind.None, found.Kind);
            StringAssert.Contains("virtualenv interpreter", found.Problem);
        }

        [Test]
        public void UsePythonProjectWithNoFolderAtAllIsAnError() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.UsePythonProject = true;
            EngineLocation found = Resolve(inputs, new FakeDisk());
            Assert.AreEqual(EngineKind.None, found.Kind);
            StringAssert.Contains("no Python project folder", found.Problem);
        }

        [Test]
        public void ThePythonSourceTreeIsTheLastResortAndOnlyWhenAllowed() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.DefaultPythonRoot = "C:/game/python-sidecar~";
            FakeDisk disk = new FakeDisk();
            disk.Files.Add("C:/game/python-sidecar~/.venv/Scripts/python.exe");
            EngineLocation found = Resolve(inputs, disk);
            Assert.AreEqual(EngineKind.PythonProject, found.Kind);
            Assert.AreEqual(EngineSource.PythonProject, found.Source);
            inputs.AllowPythonFallback = false;
            Assert.AreEqual(EngineKind.None, Resolve(inputs, disk).Kind);
        }

        [Test]
        public void ACompiledEngineBeatsThePythonSourceTreeEvenWhenBothExist() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.RegistryInstallDir = "C:/Program Files/Viitorx/TrackingEngine";
            inputs.DefaultPythonRoot = "C:/game/python-sidecar~";
            FakeDisk disk = new FakeDisk();
            disk.AddEngine("C:/Program Files/Viitorx/TrackingEngine");
            disk.Files.Add("C:/game/python-sidecar~/.venv/Scripts/python.exe");
            Assert.AreEqual(EngineKind.Executable, Resolve(inputs, disk).Kind);
        }

        [Test]
        public void TheUserInstallTakesTheHighestVersionByNumberNotByText() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.LocalAppData = "C:/L";
            FakeDisk disk = new FakeDisk();
            disk.AddEngine("C:/L/Viitorx/TrackingEngine/0.9.0");
            disk.AddEngine("C:/L/Viitorx/TrackingEngine/0.10.0");
            disk.AddEngine("C:/L/Viitorx/TrackingEngine/0.1.0");
            Assert.AreEqual("C:/L/Viitorx/TrackingEngine/0.10.0/viitorx-tracking.exe", Resolve(inputs, disk).ExecutablePath);
        }

        [Test]
        public void AVersionFolderWithoutTheExecutableIsSkipped() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.LocalAppData = "C:/L";
            FakeDisk disk = new FakeDisk();
            disk.AddDirectory("C:/L/Viitorx/TrackingEngine/0.3.0");
            disk.AddEngine("C:/L/Viitorx/TrackingEngine/0.2.0");
            Assert.AreEqual("C:/L/Viitorx/TrackingEngine/0.2.0/viitorx-tracking.exe", Resolve(inputs, disk).ExecutablePath);
        }

        [Test]
        public void APinnedVersionIsExactAndNeverFallsBackToAnotherVersion() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.LocalAppData = "C:/L";
            inputs.PinnedVersion = "0.1.0";
            inputs.AllowPythonFallback = false;
            FakeDisk disk = new FakeDisk();
            disk.AddEngine("C:/L/Viitorx/TrackingEngine/0.2.0");
            Assert.AreEqual(EngineKind.None, Resolve(inputs, disk).Kind);
            disk.AddEngine("C:/L/Viitorx/TrackingEngine/0.1.0");
            Assert.AreEqual("C:/L/Viitorx/TrackingEngine/0.1.0/viitorx-tracking.exe", Resolve(inputs, disk).ExecutablePath);
        }

        [Test]
        public void VersionsSortHighestFirstAndAPreReleaseSuffixDoesNotBreakIt() {
            List<string> names = new List<string>(new string[] { "0.2.0", "notes", "0.10.0", "0.2.0-rc1", "1.0.0" });
            names.Sort(EngineLocator.CompareVersionsDescending);
            Assert.AreEqual("1.0.0", names[0]);
            Assert.AreEqual("0.10.0", names[1]);
            Assert.AreEqual("notes", names[names.Count - 1], "a folder that is not a version comes after every real version");
        }

        [Test]
        public void NothingFoundSaysWhereItLooked() {
            EngineLocatorInputs inputs = EveryPlace();
            inputs.SettingPath = string.Empty;
            inputs.AllowPythonFallback = false;
            EngineLocation found = Resolve(inputs, new FakeDisk());
            Assert.IsFalse(found.Found);
            string text = found.Describe();
            StringAssert.Contains("not found", text);
            StringAssert.Contains("VIITORX_TRACKING_ENGINE", text);
            StringAssert.Contains("machine-wide install", text);
            StringAssert.Contains("per-user install", text);
            StringAssert.Contains("StreamingAssets", text);
        }

        [Test]
        public void DescribeNamesTheSourceOfAFoundEngine() {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.RegistryInstallDir = "C:/P";
            FakeDisk disk = new FakeDisk();
            disk.AddEngine("C:/P");
            StringAssert.Contains("the machine-wide install", Resolve(inputs, disk).Describe());
        }

        [Test]
        public void NullInputsAreRefusedWithAReasonNotAnException() {
            EngineLocation found = EngineLocator.Resolve(null, delegate (string path) { return false; }, delegate (string path) { return false; }, delegate (string path) { return null; });
            Assert.IsFalse(found.Found);
            Assert.IsNotEmpty(found.Problem);
        }
    }
}
