using System.Collections.Generic;
using System.Globalization;

using NUnit.Framework;

using UnityEngine;

namespace HumanEcho.Tests {
    /// <summary>
    /// The logic behind the Human Echo HUD: which key does what, when the input may be called LIVE, and the
    /// exact text of the status block. The IMGUI drawing itself is judged by eye; everything it shows or does
    /// comes from the classes tested here.
    /// </summary>
    public sealed class HumanEchoUITests {
        // ------------------------------------------------------------------ keys

        [Test]
        public void NumberKeysSelectLooksZeroBasedOnBothKeyboardBlocks() {
            for (int i = 0; i < HumanEchoKeys.MaxLooks; i++) {
                KeyCode row = (KeyCode)((int)KeyCode.Alpha1 + i);
                KeyCode pad = (KeyCode)((int)KeyCode.Keypad1 + i);
                HumanEchoCommand fromRow;
                HumanEchoCommand fromPad;
                Assert.IsTrue(HumanEchoKeys.TryMap(row, out fromRow), row.ToString());
                Assert.IsTrue(HumanEchoKeys.TryMap(pad, out fromPad), pad.ToString());
                Assert.AreEqual(HumanEchoAction.SelectLook, fromRow.Action);
                Assert.AreEqual(HumanEchoAction.SelectLook, fromPad.Action);
                Assert.AreEqual(i, fromRow.LookIndex);
                Assert.AreEqual(i, fromPad.LookIndex);
            }
        }

        [Test]
        public void ZeroAndTheKeysNextToTheDigitBlocksAreNotLooks() {
            Assert.AreEqual(-1, HumanEchoKeys.LookIndexOf(KeyCode.Alpha0));
            Assert.AreEqual(-1, HumanEchoKeys.LookIndexOf(KeyCode.Keypad0));
            Assert.AreEqual(-1, HumanEchoKeys.LookIndexOf(KeyCode.Colon));
            Assert.AreEqual(-1, HumanEchoKeys.LookIndexOf(KeyCode.KeypadPeriod));
            Assert.AreEqual(-1, HumanEchoKeys.LookIndexOf(KeyCode.A));
            HumanEchoCommand command;
            Assert.IsFalse(HumanEchoKeys.TryMap(KeyCode.Alpha0, out command));
            Assert.AreEqual(HumanEchoAction.None, command.Action);
        }

        [Test]
        public void TheSingleActionKeysMapToTheirActions() {
            AssertAction(KeyCode.Tab, HumanEchoAction.NextLook);
            AssertAction(KeyCode.M, HumanEchoAction.CycleMirror);
            AssertAction(KeyCode.C, HumanEchoAction.ClearFluid);
            AssertAction(KeyCode.D, HumanEchoAction.ToggleDebugTiles);
            AssertAction(KeyCode.H, HumanEchoAction.ToggleHud);
        }

        [Test]
        public void TheLayerKeysToggleTheLayerTheyAreNamedAfter() {
            AssertLayer(KeyCode.S, HumanEchoLayerId.Smoke);
            AssertLayer(KeyCode.E, HumanEchoLayerId.Echo);
            AssertLayer(KeyCode.B, HumanEchoLayerId.Person);
            AssertLayer(KeyCode.P, HumanEchoLayerId.Particles);
            AssertLayer(KeyCode.L, HumanEchoLayerId.Outline);
        }

        [Test]
        public void EachLayersKeyAndPrintedLabelAgreeWithTheKeyMap() {
            for (int i = 0; i < HumanEchoStatusFormatter.LayerCount; i++) {
                HumanEchoLayerId layer = (HumanEchoLayerId)i;
                KeyCode key = HumanEchoKeys.KeyOfLayer(layer);
                HumanEchoCommand command;
                Assert.IsTrue(HumanEchoKeys.TryMap(key, out command), layer.ToString());
                Assert.AreEqual(HumanEchoAction.ToggleLayer, command.Action);
                Assert.AreEqual(layer, command.Layer);
                Assert.AreEqual(key.ToString(), HumanEchoStatusFormatter.LayerKeyLabel(layer),
                                "the HUD prints the key that really toggles it");
            }
        }

        [Test]
        public void KeysOwnedByTheBootstrapTheVisionPanelAndTheLauncherAreNeverTaken() {
            KeyCode[] reserved = { KeyCode.O, KeyCode.F6, KeyCode.Escape };
            for (int i = 0; i < reserved.Length; i++) {
                HumanEchoCommand command;
                Assert.IsFalse(HumanEchoKeys.TryMap(reserved[i], out command), reserved[i].ToString());
                Assert.IsFalse(Contains(HumanEchoKeys.HandledKeys, reserved[i]),
                               reserved[i].ToString() + " must not be polled");
            }
        }

        [Test]
        public void TheHandledKeyListHasNoDuplicatesAndEveryEntryMaps() {
            HashSet<KeyCode> seen = new HashSet<KeyCode>();
            for (int i = 0; i < HumanEchoKeys.HandledKeys.Count; i++) {
                KeyCode key = HumanEchoKeys.HandledKeys[i];
                Assert.IsTrue(seen.Add(key), key.ToString() + " is listed twice");
                HumanEchoCommand command;
                Assert.IsTrue(HumanEchoKeys.TryMap(key, out command), key.ToString());
            }
            // 9 digits on each of the two keyboard blocks (18), Tab, M, C, D, H (5) and the five layer
            // letters (5).
            Assert.AreEqual(28, HumanEchoKeys.HandledKeys.Count);
        }

        [Test]
        public void NoKeyThatIsNotPolledIsMapped() {
            foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode))) {
                HumanEchoCommand command;
                if (HumanEchoKeys.TryMap(key, out command)) {
                    Assert.IsTrue(Contains(HumanEchoKeys.HandledKeys, key),
                                  key.ToString() + " maps but the HUD would never see it");
                }
            }
        }

        [Test]
        public void AKeyTheShowcaseDoesNotOwnMapsToTheEmptyCommand() {
            // O belongs to the bootstrap and F6 to the vision panel: this showcase must leave them alone.
            KeyCode[] foreign = new KeyCode[] { KeyCode.O, KeyCode.F6, KeyCode.Escape, KeyCode.Z };
            for (int i = 0; i < foreign.Length; i++) {
                HumanEchoCommand command;
                Assert.IsFalse(HumanEchoKeys.TryMap(foreign[i], out command), foreign[i].ToString());
                Assert.AreEqual(HumanEchoAction.None, command.Action, foreign[i].ToString());
                Assert.AreEqual(-1, command.LookIndex, foreign[i].ToString());
            }
        }

        [Test]
        public void AFluidThatRunsWithoutBeingDrawnIsNotListedAsASmokeLayer() {
            HumanEchoSettings look = ScriptableObject.CreateInstance<HumanEchoSettings>();
            try {
                SetAll(look, false);
                SetEnabled(look, HumanEchoLayerId.Smoke, true);
                look.smoke.drawn = false;
                Assert.IsFalse(HumanEchoLayerRules.LookEnables(look, HumanEchoLayerId.Smoke),
                               "the particle looks run the fluid for its velocity only");
                Assert.IsFalse(HumanEchoLayerRules.IsDrawn(look, HumanEchoLayerId.Smoke, true));
                look.smoke.drawn = true;
                Assert.IsTrue(HumanEchoLayerRules.IsDrawn(look, HumanEchoLayerId.Smoke, true));
            } finally {
                Object.DestroyImmediate(look);
            }
        }

        // ------------------------------------------------------------------ which layers are drawn

        [Test]
        public void ALayerIsDrawnOnlyWhenSwitchedOnAndUsedByTheLook() {
            HumanEchoSettings look = ScriptableObject.CreateInstance<HumanEchoSettings>();
            try {
                SetAll(look, false);
                SetEnabled(look, HumanEchoLayerId.Smoke, true);
                Assert.IsTrue(HumanEchoLayerRules.IsDrawn(look, HumanEchoLayerId.Smoke, true));
                Assert.IsFalse(HumanEchoLayerRules.IsDrawn(look, HumanEchoLayerId.Smoke, false),
                               "switched off by its key");
                Assert.IsFalse(HumanEchoLayerRules.IsDrawn(look, HumanEchoLayerId.Echo, true),
                               "the look does not use echo");
            } finally {
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void EachLayersSettingsBlockEnablesThatLayerAndNoOther() {
            HumanEchoSettings look = ScriptableObject.CreateInstance<HumanEchoSettings>();
            try {
                for (int target = 0; target < HumanEchoStatusFormatter.LayerCount; target++) {
                    HumanEchoLayerId enabledLayer = (HumanEchoLayerId)target;
                    SetAll(look, false);
                    SetEnabled(look, enabledLayer, true);
                    for (int other = 0; other < HumanEchoStatusFormatter.LayerCount; other++) {
                        HumanEchoLayerId asked = (HumanEchoLayerId)other;
                        string message = "only " + enabledLayer + " is enabled, asked about " + asked;
                        bool enabled = HumanEchoLayerRules.LookEnables(look, asked);
                        Assert.AreEqual(other == target, enabled, message);
                    }
                }
            } finally {
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void NoLookEnablesNothing() {
            for (int i = 0; i < HumanEchoStatusFormatter.LayerCount; i++) {
                Assert.IsFalse(HumanEchoLayerRules.LookEnables(null, (HumanEchoLayerId)i));
                Assert.IsFalse(HumanEchoLayerRules.IsDrawn(null, (HumanEchoLayerId)i, true));
            }
        }

        // ------------------------------------------------------------------ the LIVE rule

        [Test]
        public void NothingSeenIsWaitingNotLive() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            Assert.AreEqual(HumanEchoInputKind.Waiting, state.Kind(0f));
            state.Observe(false, 0, false, 1f);
            Assert.AreEqual(HumanEchoInputKind.Waiting, state.Kind(1f));
            Assert.IsTrue(float.IsPositiveInfinity(state.SecondsSinceFrame(1f)));
        }

        [Test]
        public void AFreshFrameFromACameraIsLive() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 1, true, 10f);
            Assert.AreEqual(HumanEchoInputKind.Live, state.Kind(10f));
            Assert.AreEqual(HumanEchoInputKind.Live, state.Kind(11.9f));
        }

        [Test]
        public void LiveEndsWhenTheVersionStopsChanging() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 5, true, 10f);
            state.Observe(true, 5, true, 11f);
            Assert.AreEqual(HumanEchoInputKind.Live, state.Kind(11f), "one second of silence is still live");
            state.Observe(true, 5, true, 12.5f);
            Assert.AreEqual(HumanEchoInputKind.Stale, state.Kind(12.5f),
                            "2.5 s without a new frame is not live, however long the object exists");
        }

        [Test]
        public void ANewVersionEveryNowAndThenKeepsItLive() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            int version = 0;
            for (int step = 0; step <= 100; step++) {
                float now = step * 0.1f;
                if (step % 6 == 0) {
                    version = version + 1;
                }
                state.Observe(true, version, true, now);
            }
            Assert.AreEqual(HumanEchoInputKind.Live, state.Kind(10f));
        }

        [Test]
        public void TheFreshnessWindowIncludesItsEdgeAndNothingLater() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 1, true, 0f);
            Assert.AreEqual(HumanEchoInputKind.Live, state.Kind(2f));
            Assert.AreEqual(HumanEchoInputKind.Stale, state.Kind(2.01f));
        }

        [Test]
        public void AVideoFileIsNeverCalledLive() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 1, false, 0f);
            Assert.AreEqual(HumanEchoInputKind.VideoFile, state.Kind(0.1f));
        }

        [Test]
        public void AStreamWhoseFrameDisappearedIsNotLiveEvenInsideTheWindow() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 1, true, 0f);
            state.Observe(false, 0, false, 0.5f);
            Assert.AreEqual(HumanEchoInputKind.Stale, state.Kind(0.5f));
        }

        [Test]
        public void ARestartedStreamWithTheSameCounterCountsAsNewData() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 3, true, 0f);
            state.Observe(false, 0, false, 1f);
            state.Observe(true, 3, true, 5f);
            Assert.AreEqual(HumanEchoInputKind.Live, state.Kind(5.1f));
        }

        [Test]
        public void SecondsSinceFrameCountsFromTheLastNewVersion() {
            HumanEchoInputState state = new HumanEchoInputState(2f);
            state.Observe(true, 1, true, 4f);
            state.Observe(true, 1, true, 6f);
            Assert.AreEqual(3f, state.SecondsSinceFrame(7f), 0.0001f);
            state.Observe(true, 2, true, 7f);
            Assert.AreEqual(0f, state.SecondsSinceFrame(7f), 0.0001f);
        }

        // ------------------------------------------------------------------ the text

        [Test]
        public void OnlyLiveInputSaysLive() {
            Assert.AreEqual("LIVE", HumanEchoStatusFormatter.InputLine(HumanEchoInputKind.Live, 0f));
            Assert.AreEqual("VIDEO FILE",
                            HumanEchoStatusFormatter.InputLine(HumanEchoInputKind.VideoFile, 0f));
            Assert.AreEqual("WAITING FOR CAMERA",
                            HumanEchoStatusFormatter.InputLine(HumanEchoInputKind.Waiting,
                                                               float.PositiveInfinity));
            string stale = HumanEchoStatusFormatter.InputLine(HumanEchoInputKind.Stale, 5.4f);
            Assert.AreEqual("WAITING FOR CAMERA (no frame for 5 s)", stale);
            Assert.IsFalse(stale.Contains("LIVE"));
        }

        [Test]
        public void ALongSilenceIsCappedSoTheLineStaysShort() {
            Assert.AreEqual("WAITING FOR CAMERA (no frame for 999 s)",
                            HumanEchoStatusFormatter.InputLine(HumanEchoInputKind.Stale, 100000f));
        }

        [Test]
        public void TheLookHeaderShowsPositionAndNameOrFallsBackToTheNumber() {
            Assert.AreEqual("LOOK 1/3   Rainbow Smoke",
                            HumanEchoStatusFormatter.LookHeader(0, 3, "Rainbow Smoke"));
            Assert.AreEqual("LOOK 2/3   Look 2", HumanEchoStatusFormatter.LookHeader(1, 3, string.Empty));
            Assert.AreEqual("LOOK 3/3   Look 3", HumanEchoStatusFormatter.LookHeader(2, 3, null));
        }

        [Test]
        public void TheRatesLineIgnoresTheMachinesDecimalSeparator() {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("fps 60   mask 17.8/s   mirror Auto (flipped)",
                                HumanEchoStatusFormatter.Rates(60.2f, 17.84f, MirrorMode.Auto, true));
            } finally {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void TheRatesLineSaysWhatTheMirrorIsReallyDoing() {
            Assert.AreEqual("fps 30   mask 0.0/s   mirror Off (not flipped)",
                            HumanEchoStatusFormatter.Rates(30f, 0f, MirrorMode.Off, false));
            Assert.AreEqual("fps 144   mask 17.5/s   mirror On (flipped)",
                            HumanEchoStatusFormatter.Rates(144f, 17.5f, MirrorMode.On, true));
        }

        [Test]
        public void MirrorNamesAreTheModeNames() {
            Assert.AreEqual("Auto", HumanEchoStatusFormatter.MirrorName(MirrorMode.Auto));
            Assert.AreEqual("On", HumanEchoStatusFormatter.MirrorName(MirrorMode.On));
            Assert.AreEqual("Off", HumanEchoStatusFormatter.MirrorName(MirrorMode.Off));
        }

        [Test]
        public void PlainLayerTextMarksTheLayersThatAreOff() {
            bool[] states = { true, false, true, false, true };
            Assert.AreEqual("S smoke   [E echo off]   B body   [P particles off]   L line",
                            HumanEchoStatusFormatter.Layers(states, false));
        }

        [Test]
        public void RichLayerTextColoursEveryEntryAndOnAndOffDiffer() {
            bool[] states = { true, false, false, false, false };
            string text = HumanEchoStatusFormatter.Layers(states, true);
            Assert.AreEqual(5, CountOf(text, "<color="));
            Assert.AreEqual(5, CountOf(text, "</color>"));
            string onColor = ColorTagOf(text, 0);
            string offColor = ColorTagOf(text, 1);
            Assert.AreNotEqual(onColor, offColor);
            Assert.AreEqual(offColor, ColorTagOf(text, 4));
            Assert.IsTrue(text.Contains(">S smoke</color>"));
        }

        [Test]
        public void MissingOrShortLayerStatesCountAsOff() {
            Assert.AreEqual("[S smoke off]   [E echo off]   [B body off]   [P particles off]   [L line off]",
                            HumanEchoStatusFormatter.Layers(null, false));
            Assert.AreEqual("S smoke   [E echo off]   [B body off]   [P particles off]   [L line off]",
                            HumanEchoStatusFormatter.Layers(new bool[] { true }, false));
        }

        [Test]
        public void EveryLayerHasADistinctNameAndKeyLabel() {
            HashSet<string> names = new HashSet<string>();
            HashSet<string> labels = new HashSet<string>();
            for (int i = 0; i < HumanEchoStatusFormatter.LayerCount; i++) {
                HumanEchoLayerId layer = (HumanEchoLayerId)i;
                Assert.IsFalse(string.IsNullOrEmpty(HumanEchoStatusFormatter.LayerName(layer)));
                Assert.IsTrue(names.Add(HumanEchoStatusFormatter.LayerName(layer)));
                Assert.IsTrue(labels.Add(HumanEchoStatusFormatter.LayerKeyLabel(layer)));
            }
            Assert.AreEqual("?", HumanEchoStatusFormatter.LayerKeyLabel((HumanEchoLayerId)99));
        }

        [Test]
        public void TheKeyHelpListsEveryKeyTheHudOwnsAndNoneItDoesNot() {
            string help = HumanEchoStatusFormatter.KeyHelp;
            Assert.IsTrue(help.Contains("1-9"));
            Assert.IsTrue(help.Contains("Tab"));
            Assert.IsTrue(help.Contains("M mirror"));
            Assert.IsTrue(help.Contains("C clear"));
            Assert.IsTrue(help.Contains("D debug"));
            Assert.IsTrue(help.Contains("H hide"));
            Assert.IsTrue(help.Contains("S E B P L"));
            Assert.IsTrue(help.Contains("F6"));
            Assert.IsTrue(help.Contains("Esc"));
            Assert.IsFalse(help.Contains("O "), "O belongs to the bootstrap");
        }

        // ------------------------------------------------------------------ helpers

        private static void SetAll(HumanEchoSettings look, bool enabled) {
            for (int i = 0; i < HumanEchoStatusFormatter.LayerCount; i++) {
                SetEnabled(look, (HumanEchoLayerId)i, enabled);
            }
        }

        private static void SetEnabled(HumanEchoSettings look, HumanEchoLayerId layer, bool enabled) {
            switch (layer) {
                case HumanEchoLayerId.Smoke:
                    look.smoke.enabled = enabled;
                    break;
                case HumanEchoLayerId.Echo:
                    look.echo.enabled = enabled;
                    break;
                case HumanEchoLayerId.Person:
                    look.person.enabled = enabled;
                    break;
                case HumanEchoLayerId.Particles:
                    look.particles.enabled = enabled;
                    break;
                default:
                    look.outline.enabled = enabled;
                    break;
            }
        }

        private static void AssertAction(KeyCode key, HumanEchoAction expected) {
            HumanEchoCommand command;
            Assert.IsTrue(HumanEchoKeys.TryMap(key, out command), key.ToString());
            Assert.AreEqual(expected, command.Action, key.ToString());
        }

        private static void AssertLayer(KeyCode key, HumanEchoLayerId expected) {
            HumanEchoCommand command;
            Assert.IsTrue(HumanEchoKeys.TryMap(key, out command), key.ToString());
            Assert.AreEqual(HumanEchoAction.ToggleLayer, command.Action, key.ToString());
            Assert.AreEqual(expected, command.Layer, key.ToString());
        }

        private static bool Contains(IReadOnlyList<KeyCode> keys, KeyCode key) {
            for (int i = 0; i < keys.Count; i++) {
                if (keys[i] == key) {
                    return true;
                }
            }
            return false;
        }

        private static int CountOf(string text, string part) {
            int count = 0;
            int index = text.IndexOf(part, System.StringComparison.Ordinal);
            while (index >= 0) {
                count = count + 1;
                index = text.IndexOf(part, index + part.Length, System.StringComparison.Ordinal);
            }
            return count;
        }

        // The text between "<color=" and the closing ">" of the n-th colour tag (0-based).
        private static string ColorTagOf(string text, int occurrence) {
            int start = -1;
            for (int i = 0; i <= occurrence; i++) {
                start = text.IndexOf("<color=", start + 1, System.StringComparison.Ordinal);
            }
            int end = text.IndexOf('>', start);
            return text.Substring(start, end - start);
        }
    }
}
