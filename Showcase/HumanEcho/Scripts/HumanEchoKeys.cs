using System.Collections.Generic;

using UnityEngine;

namespace HumanEcho {
    /// <summary>What a key press asks the showcase to do.</summary>
    public enum HumanEchoAction {
        None = 0,
        SelectLook = 1,
        NextLook = 2,
        CycleMirror = 3,
        ClearFluid = 4,
        ToggleDebugTiles = 5,
        ToggleHud = 6,
        ToggleLayer = 7
    }

    /// <summary>One resolved key press: the action and, for the two actions that need one, its
    /// argument.</summary>
    public readonly struct HumanEchoCommand {
        private readonly HumanEchoAction action;
        private readonly int lookIndex;
        private readonly HumanEchoLayerId layer;

        public HumanEchoCommand(HumanEchoAction action, int lookIndex, HumanEchoLayerId layer) {
            this.action = action;
            this.lookIndex = lookIndex;
            this.layer = layer;
        }

        public HumanEchoAction Action {
            get {
                return action;
            }
        }

        /// <summary>Zero-based index of the look to select; meaningful for
        /// <see cref="HumanEchoAction.SelectLook"/> only.</summary>
        public int LookIndex {
            get {
                return lookIndex;
            }
        }

        /// <summary>The layer to toggle; meaningful for <see cref="HumanEchoAction.ToggleLayer"/>
        /// only.</summary>
        public HumanEchoLayerId Layer {
            get {
                return layer;
            }
        }

        public static HumanEchoCommand None {
            get {
                return new HumanEchoCommand(HumanEchoAction.None, -1, HumanEchoLayerId.Smoke);
            }
        }

        public static HumanEchoCommand Simple(HumanEchoAction action) {
            return new HumanEchoCommand(action, -1, HumanEchoLayerId.Smoke);
        }

        public static HumanEchoCommand SelectLook(int index) {
            return new HumanEchoCommand(HumanEchoAction.SelectLook, index, HumanEchoLayerId.Smoke);
        }

        public static HumanEchoCommand ToggleLayer(HumanEchoLayerId layer) {
            return new HumanEchoCommand(HumanEchoAction.ToggleLayer, -1, layer);
        }
    }

    /// <summary>
    /// The Human Echo key map, kept free of any scene object so it can be tested. The HUD polls
    /// <see cref="HandledKeys"/> and runs what <see cref="TryMap"/> says.
    ///
    /// Keys this showcase must NOT take, because something that survives scene loads owns them: O (the
    /// AppBootstrap's "I turned the camera" restart), F6 (the vision control panel) and Escape (back to the
    /// Launcher). They are absent from <see cref="HandledKeys"/> on purpose.
    /// </summary>
    public static class HumanEchoKeys {
        /// <summary>The number keys 1 to 9 select looks 0 to 8.</summary>
        public const int MaxLooks = 9;

        private static readonly KeyCode[] HandledKeyList = {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
            KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
            KeyCode.Tab, KeyCode.M, KeyCode.C, KeyCode.D, KeyCode.H,
            KeyCode.S, KeyCode.E, KeyCode.B, KeyCode.P, KeyCode.L
        };

        /// <summary>Every key that <see cref="TryMap"/> accepts; polling only these costs a few dozen
        /// comparisons a frame and allocates nothing.</summary>
        public static IReadOnlyList<KeyCode> HandledKeys {
            get {
                return HandledKeyList;
            }
        }

        /// <summary>The look a number key selects (0 to 8), or -1 for any other key. The keypad digits work
        /// like the row above the letters.</summary>
        public static int LookIndexOf(KeyCode key) {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9) {
                return key - KeyCode.Alpha1;
            }
            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9) {
                return key - KeyCode.Keypad1;
            }
            return -1;
        }

        /// <summary>The key that toggles a layer: S smoke, E echo, B body (the person), P particles, L
        /// outline line.</summary>
        public static KeyCode KeyOfLayer(HumanEchoLayerId layer) {
            switch (layer) {
                case HumanEchoLayerId.Smoke:
                    return KeyCode.S;
                case HumanEchoLayerId.Echo:
                    return KeyCode.E;
                case HumanEchoLayerId.Person:
                    return KeyCode.B;
                case HumanEchoLayerId.Particles:
                    return KeyCode.P;
                default:
                    return KeyCode.L;
            }
        }

        /// <summary>Resolves a key to a command. False, with <see cref="HumanEchoCommand.None"/>, for every
        /// key this showcase does not own.</summary>
        public static bool TryMap(KeyCode key, out HumanEchoCommand command) {
            int look = LookIndexOf(key);
            if (look >= 0) {
                command = HumanEchoCommand.SelectLook(look);
                return true;
            }
            switch (key) {
                case KeyCode.Tab:
                    command = HumanEchoCommand.Simple(HumanEchoAction.NextLook);
                    return true;
                case KeyCode.M:
                    command = HumanEchoCommand.Simple(HumanEchoAction.CycleMirror);
                    return true;
                case KeyCode.C:
                    command = HumanEchoCommand.Simple(HumanEchoAction.ClearFluid);
                    return true;
                case KeyCode.D:
                    command = HumanEchoCommand.Simple(HumanEchoAction.ToggleDebugTiles);
                    return true;
                case KeyCode.H:
                    command = HumanEchoCommand.Simple(HumanEchoAction.ToggleHud);
                    return true;
                case KeyCode.S:
                    command = HumanEchoCommand.ToggleLayer(HumanEchoLayerId.Smoke);
                    return true;
                case KeyCode.E:
                    command = HumanEchoCommand.ToggleLayer(HumanEchoLayerId.Echo);
                    return true;
                case KeyCode.B:
                    command = HumanEchoCommand.ToggleLayer(HumanEchoLayerId.Person);
                    return true;
                case KeyCode.P:
                    command = HumanEchoCommand.ToggleLayer(HumanEchoLayerId.Particles);
                    return true;
                case KeyCode.L:
                    command = HumanEchoCommand.ToggleLayer(HumanEchoLayerId.Outline);
                    return true;
                default:
                    command = HumanEchoCommand.None;
                    return false;
            }
        }
    }
}
