using UnityEditor;
using VirtualMirror.App;

namespace VirtualMirror.Editor {
    /// <summary>
    /// AppBootstrap has over a hundred settings. This shows them as the collapsible sections declared by
    /// <c>[InspectorGroup]</c> in the <c>AppBootstrap.*.cs</c> files. See <see cref="GroupedInspectorEditor"/>.
    /// </summary>
    [CustomEditor(typeof(AppBootstrap))]
    public sealed class AppBootstrapEditor : GroupedInspectorEditor {
    }
}
