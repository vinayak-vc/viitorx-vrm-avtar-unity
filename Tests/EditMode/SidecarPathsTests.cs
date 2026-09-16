using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// ADR-064 packaging guards. These cover the path resolution that decides whether a BUILD can
    /// find its sidecar at all - a class of bug that does not show up in the editor, where the
    /// working tree happens to be in the right place, and only appears on someone else's machine.
    ///
    /// Hermetic: every case builds its own throwaway tree, so nothing depends on how this particular
    /// checkout is laid out or on whether the venv is installed.
    /// </summary>
    public class SidecarPathsTests {

        private string tempRoot;

        [SetUp]
        public void SetUp() {
            tempRoot = Path.Combine(Path.GetTempPath(), "vm_sidecar_tests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown() {
            try {
                if (Directory.Exists(tempRoot)) {
                    Directory.Delete(tempRoot, true);
                }
            } catch (IOException) {
                // A locked temp file must not fail the suite.
            }
        }

        /// <summary>Creates a fake Assets/ tree containing a sidecar folder, and returns the Assets path.</summary>
        private string MakeFakeAssets(bool withSupervisor) {
            string assets = Path.Combine(tempRoot, "Assets");
            string sidecar = Path.Combine(assets, "Games", "some-game", SidecarPaths.EditorSidecarFolderName);
            Directory.CreateDirectory(sidecar);
            if (withSupervisor) {
                File.WriteAllText(Path.Combine(sidecar, SidecarPaths.SupervisorScriptName), "# stub");
            }
            return assets;
        }

        [Test]
        public void ResolveEditorRoot_FindsSidecarUnderGames() {
            string assets = MakeFakeAssets(true);

            string root = SidecarPaths.ResolveEditorRoot(assets);

            Assert.IsNotNull(root, "expected the sidecar folder to be discovered under Assets/Games");
            StringAssert.EndsWith(SidecarPaths.EditorSidecarFolderName, root);
        }

        /// <summary>
        /// A Games/ folder that merely exists is not a sidecar. Identifying the folder by its
        /// supervisor script rather than by name keeps an empty or half-deleted directory from being
        /// reported as a usable install.
        /// </summary>
        [Test]
        public void ResolveEditorRoot_ReturnsNullWhenSupervisorMissing() {
            string assets = MakeFakeAssets(false);

            Assert.IsNull(SidecarPaths.ResolveEditorRoot(assets));
        }

        [Test]
        public void ResolveEditorRoot_ReturnsNullWhenNoGamesFolder() {
            string assets = Path.Combine(tempRoot, "Assets");
            Directory.CreateDirectory(assets);

            Assert.IsNull(SidecarPaths.ResolveEditorRoot(assets));
        }

        [Test]
        public void ResolvePlayerRoot_AppendsSidecarFolderToStreamingAssets() {
            string root = SidecarPaths.ResolvePlayerRoot("C:/Game/App_Data/StreamingAssets");

            Assert.AreEqual("C:/Game/App_Data/StreamingAssets/" + SidecarPaths.PlayerSidecarFolderName, root);
        }

        [Test]
        public void Resolve_NormalisesBackslashesSoLogsAndArgsAreConsistent() {
            SidecarPathSet set = SidecarPaths.Resolve("C:\\Game\\Sidecar", null);

            StringAssert.DoesNotContain("\\", set.Root);
            StringAssert.DoesNotContain("\\", set.SupervisorScript);
            StringAssert.DoesNotContain("\\", set.PythonExe);
        }

        [Test]
        public void Resolve_ModelOverrideWinsOverPackagedPath() {
            SidecarPathSet set = SidecarPaths.Resolve("C:/Game/Sidecar", "D:/models/custom.onnx");

            Assert.AreEqual("D:/models/custom.onnx", set.ModelFile);
        }

        [Test]
        public void Resolve_WithoutOverrideUsesPackagedModelPath() {
            SidecarPathSet set = SidecarPaths.Resolve("C:/Game/Sidecar", null);

            Assert.AreEqual("C:/Game/Sidecar/" + SidecarPaths.PlayerModelRelativePath, set.ModelFile);
        }

        [Test]
        public void Validate_ReportsMissingRoot() {
            List<string> problems = SidecarPaths.Validate(SidecarPaths.Resolve(null, null));

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("could not be resolved", problems[0]);
        }

        /// <summary>
        /// The missing-interpreter message must keep saying why there is no PATH fallback. Falling
        /// back to `python` on PATH resolves an environment whose onnxruntime silently drops to CPU
        /// (~183 ms/frame vs ~31), so the failure is invisible except as unexplained slowness.
        /// </summary>
        [Test]
        public void Validate_MissingInterpreterExplainsWhyThereIsNoPathFallback() {
            string root = Path.Combine(tempRoot, "Sidecar");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, SidecarPaths.SupervisorScriptName), "# stub");
            File.WriteAllText(Path.Combine(root, SidecarPaths.SenderScriptName), "# stub");

            List<string> problems = SidecarPaths.Validate(SidecarPaths.Resolve(root, null));

            string interpreterProblem = problems.Find(p => p.Contains("interpreter"));
            Assert.IsNotNull(interpreterProblem, "a missing venv interpreter must be reported");
            StringAssert.Contains(SidecarPaths.SetupScriptName, interpreterProblem);
            StringAssert.Contains("PATH", interpreterProblem);
        }

        [Test]
        public void Validate_PassesWhenEveryRequiredFileExists() {
            string root = Path.Combine(tempRoot, "Sidecar");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, ".venv", "Scripts"));
            Directory.CreateDirectory(Path.Combine(root, "models"));
            File.WriteAllText(Path.Combine(root, SidecarPaths.SupervisorScriptName), "# stub");
            File.WriteAllText(Path.Combine(root, SidecarPaths.SenderScriptName), "# stub");
            File.WriteAllText(Path.Combine(root, ".venv", "Scripts", "python.exe"), "stub");
            File.WriteAllText(Path.Combine(root, "models", "rtmw3d-x.onnx"), "stub");

            List<string> problems = SidecarPaths.Validate(SidecarPaths.Resolve(root, null));

            CollectionAssert.IsEmpty(problems, string.Join(" | ", problems.ToArray()));
        }

        [Test]
        public void Validate_ReportsMissingModelSeparatelyFromMissingInterpreter() {
            string root = Path.Combine(tempRoot, "Sidecar");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, ".venv", "Scripts"));
            File.WriteAllText(Path.Combine(root, SidecarPaths.SupervisorScriptName), "# stub");
            File.WriteAllText(Path.Combine(root, SidecarPaths.SenderScriptName), "# stub");
            File.WriteAllText(Path.Combine(root, ".venv", "Scripts", "python.exe"), "stub");

            List<string> problems = SidecarPaths.Validate(SidecarPaths.Resolve(root, null));

            Assert.AreEqual(1, problems.Count, "only the model should be missing");
            StringAssert.Contains("model", problems[0]);
        }
    }
}
