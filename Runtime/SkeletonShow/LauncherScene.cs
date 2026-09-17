using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-35 — GETTING BACK TO THE MENU, owned once so every scene agrees on the key and on what
    /// happens when the menu is not there.
    ///
    /// WHY THIS IS A SEPARATE STATIC RATHER THAN A METHOD ON THE LAUNCHER. Two different bases run
    /// the scenes — <c>ExperienceBase</c> in the Experiences assembly and
    /// <see cref="SkeletonShowBootstrap"/> in this one — and Experiences references SkeletonShow, not
    /// the other way round. Anything both must call therefore has to live here. It also keeps the
    /// scenes independent of the launcher: they call this, it does nothing if no launcher is present,
    /// and every scene still runs on its own exactly as before.
    ///
    /// A SCENE CAN ONLY BE LOADED IF IT IS IN BUILD SETTINGS, and a scene that is not produces a
    /// runtime error rather than a no-op. So availability is CHECKED rather than assumed — see
    /// <see cref="IsAvailable"/> — and a missing launcher is reported once, in words that say what to
    /// do about it, instead of once per keypress.
    /// </summary>
    public static class LauncherScene {
        /// <summary>The menu scene's name. Must match <c>Scenes/Launcher.unity</c>.</summary>
        public const string SceneName = "Launcher";

        /// <summary>The key that goes back to the menu, from anywhere.</summary>
        public const KeyCode ReturnKey = KeyCode.Escape;

        private static int cachedCount = -1;
        private static bool cachedAvailable;
        private static bool warned;

        /// <summary>
        /// True when the launcher is in Build Settings and can actually be loaded.
        ///
        /// Recomputed whenever the number of scenes in Build Settings changes, which is the one thing
        /// that can alter the answer while the Editor is open.
        /// </summary>
        public static bool IsAvailable {
            get {
                int count = SceneManager.sceneCountInBuildSettings;
                if (count != cachedCount) {
                    cachedCount = count;
                    cachedAvailable = IsInBuildSettings(SceneName);
                }
                return cachedAvailable;
            }
        }

        /// <summary>
        /// Whether a scene NAME is in Build Settings.
        ///
        /// Walks the build list rather than calling <c>Application.CanStreamedLevelBeLoaded</c>: this
        /// is the supported path, it needs no string-path guessing, and it is what lets the launcher
        /// grey a row out and say why instead of throwing when somebody clicks it.
        /// </summary>
        public static bool IsInBuildSettings(string sceneName) {
            if (string.IsNullOrEmpty(sceneName)) {
                return false;
            }
            int count = SceneManager.sceneCountInBuildSettings;
            int i = 0;
            while (i < count) {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (NameOf(path) == sceneName) {
                    return true;
                }
                i = i + 1;
            }
            return false;
        }

        /// <summary>"Assets/.../Bonds.unity" -> "Bonds".</summary>
        public static string NameOf(string scenePath) {
            if (string.IsNullOrEmpty(scenePath)) {
                return string.Empty;
            }
            int slash = scenePath.LastIndexOf('/');
            int start = slash >= 0 ? slash + 1 : 0;
            int dot = scenePath.LastIndexOf('.');
            int end = dot > start ? dot : scenePath.Length;
            return scenePath.Substring(start, end - start);
        }

        /// <summary>True on the frame the return key was pressed.</summary>
        public static bool ReturnRequested() {
            return Input.GetKeyDown(ReturnKey);
        }

        /// <summary>
        /// Go back to the menu. Returns false, with one explanatory log, when there is no menu to go
        /// back to — a scene opened on its own in the Editor is a perfectly normal way to work, and it
        /// must not start spamming the console because somebody leaned on Escape.
        /// </summary>
        public static bool Load() {
            if (!IsAvailable) {
                if (!warned) {
                    warned = true;
                    Debug.LogWarning("[Launcher] No '" + SceneName + "' scene in Build Settings, so "
                                     + "there is nowhere to go back to. Add Scenes/" + SceneName
                                     + ".unity to File > Build Profiles > Scene List.");
                }
                return false;
            }
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            return true;
        }

        /// <summary>The line to append to a scene's on-screen key help. Empty when there is no
        /// launcher, so a scene run on its own does not advertise a key that does nothing.</summary>
        public static string KeyHelp() {
            return IsAvailable ? "      ESC menu" : string.Empty;
        }
    }
}
