using System.Collections.Generic;

using NUnit.Framework;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-35 unit tests: the launcher's catalogue and its scene-name parsing.
    ///
    /// WHAT THESE ARE FOR. Every failure mode of a menu is silent. A typo in a scene name is a button
    /// that does nothing; a duplicate row is a scene you can never reach; an eleventh row in a column
    /// is a scene the number keys cannot select. None of those throws, none of them logs, and all of
    /// them are found by a person standing in front of the camera wondering why the installation is
    /// broken. So the invariants are pinned here instead.
    ///
    /// SCOPE. Everything covered is pure managed data and string handling. Whether a scene is
    /// actually in Build Settings needs a player and is checked by the launcher itself at startup,
    /// which greys the row and says why — see <c>SceneLauncher.CacheAvailability</c>.
    /// </summary>
    public sealed class SceneLauncherF35Tests {
        [Test]
        public void EveryColumnFitsTheNumberKeyShortcut() {
            // 1-9 then 0. An eleventh row is not wrong in itself, but it cannot be reached from the
            // keyboard and nothing on screen would say so.
            Assert.LessOrEqual(SceneLauncher.SinglePersonScenes.Count, SceneLauncher.MaxShortcutRows);
            Assert.LessOrEqual(SceneLauncher.MultiPersonScenes.Count, SceneLauncher.MaxShortcutRows);
            Assert.LessOrEqual(SceneLauncher.ProductionScenes.Count, SceneLauncher.MaxShortcutRows);
        }

        [Test]
        public void NoSceneIsListedTwice() {
            HashSet<string> seen = new HashSet<string>();
            foreach (SceneLauncher.Entry entry in All()) {
                Assert.IsTrue(seen.Add(entry.Scene),
                              entry.Scene + " is listed more than once, so one of the rows is dead");
            }
        }

        [Test]
        public void EveryRowHasSomethingToShow() {
            foreach (SceneLauncher.Entry entry in All()) {
                Assert.IsFalse(string.IsNullOrEmpty(entry.Scene), "a row with no scene cannot load");
                Assert.IsFalse(string.IsNullOrEmpty(entry.Title), entry.Scene + " has no title");
                Assert.IsFalse(string.IsNullOrEmpty(entry.Blurb), entry.Scene + " has no description");
            }
        }

        [Test]
        public void TheColumnsAreDisjoint() {
            // The split is the whole point of the menu: a scene that needs two people must not appear
            // under "one person", because somebody will pick it on their own and conclude it is
            // broken.
            HashSet<string> single = new HashSet<string>();
            foreach (SceneLauncher.Entry entry in SceneLauncher.SinglePersonScenes) {
                single.Add(entry.Scene);
            }
            foreach (SceneLauncher.Entry entry in SceneLauncher.MultiPersonScenes) {
                Assert.IsFalse(single.Contains(entry.Scene),
                               entry.Scene + " is in both columns");
            }
        }

        [Test]
        public void TheLauncherDoesNotListItself() {
            // It would load the menu from the menu, which looks exactly like a crash.
            foreach (SceneLauncher.Entry entry in All()) {
                Assert.AreNotEqual(LauncherScene.SceneName, entry.Scene);
            }
        }

        [Test]
        public void EveryMultiPersonSceneShippedInF32OrF34() {
            // A guard against the columns drifting apart from the report. If a scene is added to the
            // multi-person column it belongs in F34_CROWD_EXPERIENCES too, and this is the cheapest
            // reminder available.
            string[] expected = {
                "Bonds", "CollectiveFluid", "StillnessTug", "CrowdFootprints", "Eclipse",
                "Chord", "Chain", "Pass", "Podium", "MirrorEachOther",
            };
            Assert.AreEqual(expected.Length, SceneLauncher.MultiPersonScenes.Count);
            int i = 0;
            while (i < expected.Length) {
                bool found = false;
                foreach (SceneLauncher.Entry entry in SceneLauncher.MultiPersonScenes) {
                    if (entry.Scene == expected[i]) {
                        found = true;
                    }
                }
                Assert.IsTrue(found, expected[i] + " is missing from the multi-person column");
                i = i + 1;
            }
        }

        // ---------------------------------------------------------------- path parsing

        [Test]
        public void NameOf_TakesTheSceneNameOutOfABuildSettingsPath() {
            Assert.AreEqual("Bonds",
                LauncherScene.NameOf("Assets/Games/viitorx-vrm-avtar-unity/Scenes/Bonds.unity"));
        }

        [Test]
        public void NameOf_HandlesANameWithNoFolderAndNoExtension() {
            Assert.AreEqual("Launcher", LauncherScene.NameOf("Launcher"));
            Assert.AreEqual("Launcher", LauncherScene.NameOf("Launcher.unity"));
        }

        [Test]
        public void NameOf_IsEmptyRatherThanThrowingOnNothing() {
            // SceneUtility returns an empty path for an index that is out of range, and the launcher
            // walks the build list every time it checks availability. Throwing there would take the
            // menu down.
            Assert.AreEqual(string.Empty, LauncherScene.NameOf(null));
            Assert.AreEqual(string.Empty, LauncherScene.NameOf(string.Empty));
        }

        [Test]
        public void NameOf_DoesNotEatADotInAFolderName() {
            Assert.AreEqual("Mirror", LauncherScene.NameOf("Assets/My.Game/Scenes/Mirror.unity"));
        }

        private static IEnumerable<SceneLauncher.Entry> All() {
            foreach (SceneLauncher.Entry entry in SceneLauncher.SinglePersonScenes) {
                yield return entry;
            }
            foreach (SceneLauncher.Entry entry in SceneLauncher.MultiPersonScenes) {
                yield return entry;
            }
            foreach (SceneLauncher.Entry entry in SceneLauncher.ProductionScenes) {
                yield return entry;
            }
        }
    }
}
