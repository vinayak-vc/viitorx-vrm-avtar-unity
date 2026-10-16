using System;
using System.IO;
using UnityEngine;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// The one place that knows where the sidecar lives in each run mode. Kept apart from
    /// <see cref="SidecarPaths"/> so that the path logic itself stays testable without a Unity
    /// player loop; this type is the thin UnityEngine-facing shim over it.
    /// </summary>
    public static class SidecarLocator {

        /// <summary>
        /// Resolves the sidecar installation for however we are currently running.
        ///
        /// Editor: the working tree at Assets/Games/&lt;game&gt;/python-sidecar~, which is the source of
        /// truth. The trailing '~' keeps Unity from importing 83 .py files and generating .meta
        /// churn for them.
        ///
        /// Player: StreamingAssets/Sidecar, populated at build time by SidecarBuildPostprocessor.
        /// </summary>
        /// <summary>
        /// Finds the engine for this machine (see <see cref="EngineLocator"/> for the order). <paramref name="settingPath"/> is the path typed into the
        /// settings (the executable or its folder); <paramref name="usePythonProject"/> forces the Python source tree, at <paramref name="pythonProjectPath"/>
        /// or, when that is empty, where the application keeps it. With <paramref name="allowPythonFallback"/> the Python source tree is also the last resort.
        /// </summary>
        public static EngineLocation ResolveEngine(string settingPath, bool usePythonProject, string pythonProjectPath, bool allowPythonFallback, string pinnedVersion) {
            EngineLocatorInputs inputs = new EngineLocatorInputs();
            inputs.SettingPath = settingPath ?? string.Empty;
            inputs.UsePythonProject = usePythonProject;
            inputs.PythonProjectPath = pythonProjectPath ?? string.Empty;
            inputs.EnvironmentValue = Environment.GetEnvironmentVariable(EngineLocator.EnvironmentVariableName) ?? string.Empty;
            inputs.RegistryInstallDir = EngineRegistry.ReadInstallDir();
            inputs.LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            inputs.StreamingAssetsPath = Application.streamingAssetsPath;
            inputs.PinnedVersion = pinnedVersion ?? string.Empty;
            inputs.AllowPythonFallback = allowPythonFallback;
            inputs.DefaultPythonRoot = Application.isEditor
                ? SidecarPaths.ResolveEditorRoot(Application.dataPath)
                : SidecarPaths.ResolvePlayerRoot(Application.streamingAssetsPath);
            return EngineLocator.Resolve(inputs, File.Exists, Directory.Exists, Directory.GetDirectories);
        }

        public static SidecarPathSet Resolve(string modelPathOverride) {
            string root = Application.isEditor
                ? SidecarPaths.ResolveEditorRoot(Application.dataPath)
                : SidecarPaths.ResolvePlayerRoot(Application.streamingAssetsPath);

            string model = modelPathOverride;
            if (string.IsNullOrEmpty(model) && Application.isEditor) {
                // In the editor the 369 MB model already sits in Assets/SentisModel; there is no
                // reason to keep a second copy under the sidecar folder.
                string editorModel = Path.Combine(Application.dataPath, SidecarPaths.EditorModelRelativeToAssets);
                if (File.Exists(editorModel)) {
                    // ADR-082: the FP16 build when it sits beside it. Only this DEFAULT is upgraded; a model
                    // path the user typed into the Inspector (modelPathOverride) is used exactly as given.
                    model = SidecarPaths.PreferFp16(editorModel, File.Exists).Replace('\\', '/');
                }
            }
            return SidecarPaths.Resolve(root, model);
        }
    }
}
