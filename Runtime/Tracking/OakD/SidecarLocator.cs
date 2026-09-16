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
                    model = editorModel.Replace('\\', '/');
                }
            }
            return SidecarPaths.Resolve(root, model);
        }
    }
}
