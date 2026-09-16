using System;
using System.Collections.Generic;
using System.IO;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// Resolves every filesystem path the Python sidecar needs, for both the editor and a built
    /// player. Deliberately free of UnityEngine calls so the EditMode suite can drive it with
    /// synthetic roots instead of whatever happens to be installed on the build machine.
    ///
    /// V1 packaging (ADR-064): the sidecar SOURCE ships inside StreamingAssets, but the virtualenv
    /// and the ~369 MB ONNX model do NOT. A Windows venv is not relocatable - pyvenv.cfg carries an
    /// absolute `home` pointing at the base interpreter and Lib/ holds only site-packages, no
    /// stdlib - so a copied venv silently breaks on any machine that lacks the same Python at the
    /// same path. Both are installed once on the target by setup_sidecar.ps1 instead.
    /// </summary>
    public static class SidecarPaths {
        /// <summary>Editor-side folder name. The trailing '~' is what keeps Unity from importing it.</summary>
        public const string EditorSidecarFolderName = "python-sidecar~";

        /// <summary>Player-side folder name, under StreamingAssets. No '~': it is not an Assets path.</summary>
        public const string PlayerSidecarFolderName = "Sidecar";

        public const string SupervisorScriptName = "sidecar_supervisor.py";
        public const string SenderScriptName = "wholebody_udp_sender.py";
        public const string VenvPythonRelativePath = ".venv/Scripts/python.exe";
        public const string SetupScriptName = "setup_sidecar.ps1";

        /// <summary>Where the player looks for the model. The editor uses the in-Assets copy instead.</summary>
        public const string PlayerModelRelativePath = "models/rtmw3d-x.onnx";

        /// <summary>Editor model location, relative to Assets/.</summary>
        public const string EditorModelRelativeToAssets = "SentisModel/rtmw3d-x.onnx";

        /// <summary>
        /// Editor: Application.dataPath is "&lt;project&gt;/Assets", and the sidecar lives at
        /// "&lt;project&gt;/Assets/Games/viitorx-vrm-avtar-unity/python-sidecar~". Walk down rather than
        /// hardcoding the whole chain so a repo move does not break it.
        /// </summary>
        public static string ResolveEditorRoot(string dataPath) {
            if (string.IsNullOrEmpty(dataPath)) {
                return null;
            }
            string games = Path.Combine(dataPath, "Games");
            if (!Directory.Exists(games)) {
                return null;
            }
            foreach (string gameDir in Directory.GetDirectories(games)) {
                string candidate = Path.Combine(gameDir, EditorSidecarFolderName);
                if (File.Exists(Path.Combine(candidate, SupervisorScriptName))) {
                    return NormalizeSeparators(candidate);
                }
            }
            return null;
        }

        /// <summary>
        /// Player: StreamingAssets on a Windows standalone build is a plain folder next to the
        /// executable ("&lt;App&gt;_Data/StreamingAssets"), so this is an ordinary readable path. It is
        /// NOT readable this way on Android, which is why v1 is Windows-only (ADR-064).
        /// </summary>
        public static string ResolvePlayerRoot(string streamingAssetsPath) {
            if (string.IsNullOrEmpty(streamingAssetsPath)) {
                return null;
            }
            return NormalizeSeparators(Path.Combine(streamingAssetsPath, PlayerSidecarFolderName));
        }

        /// <summary>Builds the concrete path set for a resolved sidecar root.</summary>
        public static SidecarPathSet Resolve(string sidecarRoot, string modelPathOverride) {
            SidecarPathSet set = new SidecarPathSet();
            set.Root = NormalizeSeparators(sidecarRoot);
            if (string.IsNullOrEmpty(set.Root)) {
                return set;
            }
            set.PythonExe = NormalizeSeparators(Path.Combine(set.Root, VenvPythonRelativePath));
            set.SupervisorScript = NormalizeSeparators(Path.Combine(set.Root, SupervisorScriptName));
            set.SenderScript = NormalizeSeparators(Path.Combine(set.Root, SenderScriptName));
            set.SetupScript = NormalizeSeparators(Path.Combine(set.Root, SetupScriptName));
            set.ModelFile = !string.IsNullOrEmpty(modelPathOverride)
                ? NormalizeSeparators(modelPathOverride)
                : NormalizeSeparators(Path.Combine(set.Root, PlayerModelRelativePath));
            return set;
        }

        /// <summary>
        /// Every reason this path set cannot be launched, in the order a human should fix them.
        /// Returns an empty list when the sidecar is launchable.
        ///
        /// The missing-interpreter case is a HARD failure on purpose. Falling back to `python` on
        /// PATH resolves a different environment whose onnxruntime cannot load its GPU provider and
        /// silently drops to CPU - 183 ms/frame instead of ~31 ms. Correctness is unaffected, so the
        /// only symptom is that everything is mysteriously slow. Never fall back.
        /// </summary>
        public static List<string> Validate(SidecarPathSet paths) {
            List<string> problems = new List<string>();
            if (paths == null || string.IsNullOrEmpty(paths.Root)) {
                problems.Add("Sidecar root could not be resolved.");
                return problems;
            }
            if (!Directory.Exists(paths.Root)) {
                problems.Add("Sidecar folder is missing: " + paths.Root);
                return problems;
            }
            if (!File.Exists(paths.SupervisorScript)) {
                problems.Add("Supervisor script is missing: " + paths.SupervisorScript);
            }
            if (!File.Exists(paths.SenderScript)) {
                problems.Add("Sidecar script is missing: " + paths.SenderScript);
            }
            if (!File.Exists(paths.PythonExe)) {
                problems.Add("Virtualenv interpreter is missing: " + paths.PythonExe
                             + "  -> run " + paths.SetupScript + " once on this machine."
                             + "  (Refusing to fall back to 'python' on PATH: that resolves a"
                             + " different environment which runs inference ~6x slower on CPU"
                             + " without reporting an error.)");
            }
            if (!File.Exists(paths.ModelFile)) {
                problems.Add("Pose model is missing: " + paths.ModelFile
                             + "  -> copy rtmw3d-x.onnx there (see README.md).");
            }
            return problems;
        }

        private static string NormalizeSeparators(string path) {
            if (string.IsNullOrEmpty(path)) {
                return path;
            }
            return path.Replace('\\', '/');
        }
    }

    /// <summary>Concrete, absolute paths for one sidecar installation.</summary>
    public sealed class SidecarPathSet {
        public string Root;
        public string PythonExe;
        public string SupervisorScript;
        public string SenderScript;
        public string SetupScript;
        public string ModelFile;
    }
}
