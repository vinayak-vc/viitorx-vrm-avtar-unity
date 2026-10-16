#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

namespace VirtualMirror.Body.Editor {
    /// <summary>
    /// UniVRM is not on Unity's registry, so the package cannot install it. Without it the avatar, the retargeting and the BodyBootstrap are not compiled (the
    /// body wire receiver still is); this says so once per editor session instead of leaving a project with no BodyBootstrap and no explanation.
    /// </summary>
    [InitializeOnLoad]
    internal static class BodyDependencyCheck {
        private const string SessionKey = "Viitorx.Body.UniVrmWarned";

        static BodyDependencyCheck() {
#if !VIITORX_UNIVRM
            if (SessionState.GetBool(SessionKey, false)) {
                return;
            }
            SessionState.SetBool(SessionKey, true);
            Debug.LogWarning("[Viitorx] com.viitorx.avtar.tracking.body: UniVRM (com.vrmc.vrm) is not installed, so the avatar loader, the Kalidokit retargeting and BodyBootstrap are not compiled. "
                + "Add the OpenUPM scoped registry (https://package.openupm.com, scope com.vrmc) to Packages/manifest.json and install com.vrmc.vrm; see the package README.");
#endif
        }
    }
}
#endif
