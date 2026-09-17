using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace VirtualMirror.Editor {
    /// <summary>
    /// F-35 — PUT EVERY DEMONSTRATION SCENE BACK INTO BUILD SETTINGS, in one click.
    ///
    /// WHY THIS HAS TO EXIST. `ProjectSettings/EditorBuildSettings.asset` is **gitignored** in this
    /// project (`.gitignore` line 10). So the scene list is per-machine: it is registered here and it
    /// will NOT arrive with a clone. On a fresh checkout the launcher opens with every row greyed out
    /// and the message "NOT IN BUILD SETTINGS", which is honest but is not a fix.
    ///
    /// A scene that is not registered cannot be loaded at runtime — `SceneManager.LoadScene` fails
    /// with a console error *after* the click — so without this the launcher is a menu of dead
    /// buttons on any machine but the one it was built on. Adding twenty scenes by hand through the
    /// Build Profiles window is exactly the sort of chore that gets done wrong once and then debugged
    /// for an hour.
    ///
    /// WHAT IT DOES NOT DO, deliberately:
    ///   * It never REORDERS or REMOVES anything already registered, and it never touches index 0 —
    ///     that is the startup scene of a build, and this must not be able to change what the product
    ///     does on launch. New entries are appended, with one stated exception: the boot scene goes
    ///     in at index 1 the first time it is added, so a build runs it straight after the licence
    ///     check. See <see cref="BootScene"/>.
    ///   * It never disables an existing entry.
    ///   * It adds every scene under <c>Scenes/</c> except <c>Mirror.unity</c>, which is loaded
    ///     additively by <c>Bootstrap</c> after the settings, avatar and UI are wired — registering it
    ///     would invite somebody to open it on its own and find an unwired scene.
    /// </summary>
    public static class SceneRegistrar {
        private const string ScenesFolder = "Assets/Games/viitorx-vrm-avtar-unity/Scenes";

        /// <summary>Loaded additively by Bootstrap, never on its own. See the class note.</summary>
        private static readonly string[] Excluded = { "Mirror" };

        /// <summary>
        /// The boot scene, which starts the sidecar and then opens the launcher. It is placed
        /// immediately after index 0 when it is first added, rather than appended like everything
        /// else, because it is meant to be the first thing a build runs after the licence check.
        ///
        /// Index 0 is still never touched, and an entry that is ALREADY registered is never moved —
        /// so this can reorder nothing that somebody has deliberately arranged. Nothing here loads a
        /// scene by build index; every load is by name.
        /// </summary>
        private const string BootScene = "SidecarBoot";

        [MenuItem("Virtual Mirror/Register All Scenes in Build Settings", priority = 200)]
        public static void RegisterAll() {
            if (!Directory.Exists(ScenesFolder)) {
                Debug.LogError("[SceneRegistrar] No folder at " + ScenesFolder
                               + ". Has the package moved?");
                return;
            }

            List<EditorBuildSettingsScene> scenes =
                new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            HashSet<string> already = new HashSet<string>();
            int i = 0;
            while (i < scenes.Count) {
                already.Add(Normalise(scenes[i].path));
                i = i + 1;
            }

            string[] found = Directory.GetFiles(ScenesFolder, "*.unity", SearchOption.TopDirectoryOnly);
            System.Array.Sort(found);

            int added = 0;
            List<string> skipped = new List<string>();
            int f = 0;
            while (f < found.Length) {
                string path = found[f].Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                if (System.Array.IndexOf(Excluded, name) >= 0) {
                    skipped.Add(name + " (loaded additively by Bootstrap)");
                } else if (already.Contains(Normalise(path))) {
                    skipped.Add(name + " (already registered)");
                } else if (name == BootScene && scenes.Count > 0) {
                    // Straight after index 0, so a build runs it first. See the BootScene note.
                    scenes.Insert(1, new EditorBuildSettingsScene(path, true));
                    added = added + 1;
                } else {
                    // Appended, never inserted: index 0 is the startup scene of a build.
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                    added = added + 1;
                }
                f = f + 1;
            }

            if (added > 0) {
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            Debug.Log("[SceneRegistrar] " + added + " scene" + (added == 1 ? "" : "s")
                      + " added; " + skipped.Count + " left alone; "
                      + scenes.Count + " in Build Settings now. Startup scene is still '"
                      + (scenes.Count > 0 ? Path.GetFileNameWithoutExtension(scenes[0].path) : "none")
                      + "'.");
        }

        /// <summary>Report what is registered without changing anything — the first thing to run when
        /// a launcher row is greyed out.</summary>
        [MenuItem("Virtual Mirror/List Scenes in Build Settings", priority = 201)]
        public static void ListRegistered() {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            string report = "[SceneRegistrar] " + scenes.Length + " scene(s) in Build Settings:";
            int i = 0;
            while (i < scenes.Length) {
                report = report + "\n  " + i + "  "
                         + Path.GetFileNameWithoutExtension(scenes[i].path)
                         + (scenes[i].enabled ? string.Empty : "   (DISABLED - cannot be loaded)");
                i = i + 1;
            }
            Debug.Log(report);
        }

        // Compared case-insensitively with forward slashes: the same asset can be written either way
        // on Windows, and a duplicate entry is worse than a missing one.
        private static string Normalise(string path) {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').ToLowerInvariant();
        }
    }
}
