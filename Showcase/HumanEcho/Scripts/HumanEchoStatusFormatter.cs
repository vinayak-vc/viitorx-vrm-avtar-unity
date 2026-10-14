using System.Globalization;
using System.Text;

using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// The text of the Human Echo HUD, built from plain values so it can be tested without a scene. Numbers
    /// use the invariant culture: a decimal comma in a status line would read as two numbers. The HUD calls
    /// these a few times a second and keeps the results, never once per frame.
    /// </summary>
    public static class HumanEchoStatusFormatter {
        /// <summary>The number of toggleable layers (the length of <see cref="HumanEchoLayerId"/>).</summary>
        public const int LayerCount = 5;

        public const string WaitingText = "WAITING FOR CAMERA";
        public const string LiveText = "LIVE";
        public const string VideoFileText = "VIDEO FILE";

        /// <summary>The key list. Layer letters are listed again, coloured by state, in the layer line above
        /// it.</summary>
        public const string KeyHelp =
            "1-9 look    Tab next look    M mirror    C clear fluid\n"
            + "D debug tiles    H hide this panel\n"
            + "S E B P L  turn a layer on or off\n"
            + "F6 vision control panel    Esc launcher";

        private const string OnColor = "#9CFFC4";
        private const string OffColor = "#7A8294";

        private static readonly string[] LayerNames = { "smoke", "echo", "body", "particles", "line" };
        private static readonly string[] LayerKeyLabels = { "S", "E", "B", "P", "L" };

        public static string LayerName(HumanEchoLayerId layer) {
            int index = (int)layer;
            if (index < 0 || index >= LayerNames.Length) {
                return layer.ToString();
            }
            return LayerNames[index];
        }

        /// <summary>The letter of the key that toggles the layer, as it is printed in the HUD.</summary>
        public static string LayerKeyLabel(HumanEchoLayerId layer) {
            int index = (int)layer;
            if (index < 0 || index >= LayerKeyLabels.Length) {
                return "?";
            }
            return LayerKeyLabels[index];
        }

        public static string MirrorName(MirrorMode mode) {
            switch (mode) {
                case MirrorMode.Auto:
                    return "Auto";
                case MirrorMode.On:
                    return "On";
                case MirrorMode.Off:
                    return "Off";
                default:
                    return mode.ToString();
            }
        }

        /// <summary>"LOOK 2/3   Red Echo". A look without a name is shown by its number.</summary>
        public static string LookHeader(int index, int count, string name) {
            string number = (index + 1).ToString(CultureInfo.InvariantCulture);
            string total = count.ToString(CultureInfo.InvariantCulture);
            string label = string.IsNullOrEmpty(name) ? "Look " + number : name;
            return "LOOK " + number + "/" + total + "   " + label;
        }

        /// <summary>
        /// The one-line input state. LIVE is only ever returned for <see cref="HumanEchoInputKind.Live"/>;
        /// everything without fresh data reads "WAITING FOR CAMERA", with the silence in seconds once frames
        /// have been seen before.
        /// </summary>
        public static string InputLine(HumanEchoInputKind kind, float secondsSinceFrame) {
            switch (kind) {
                case HumanEchoInputKind.Live:
                    return LiveText;
                case HumanEchoInputKind.VideoFile:
                    return VideoFileText;
                case HumanEchoInputKind.Stale:
                    float silence = Mathf.Clamp(secondsSinceFrame, 0f, 999f);
                    string seconds = silence.ToString("0", CultureInfo.InvariantCulture);
                    return WaitingText + " (no frame for " + seconds + " s)";
                default:
                    return WaitingText;
            }
        }

        /// <summary>"fps 60   mask 17.8/s   mirror Auto (flipped)": the display rate, the mask rate and what
        /// the mirror is really doing.</summary>
        public static string Rates(float fps, float maskRate, MirrorMode mode, bool mirrorApplied) {
            return "fps " + fps.ToString("0", CultureInfo.InvariantCulture)
                   + "   mask " + maskRate.ToString("0.0", CultureInfo.InvariantCulture) + "/s"
                   + "   mirror " + MirrorName(mode) + (mirrorApplied ? " (flipped)" : " (not flipped)");
        }

        /// <summary>
        /// "S smoke   E echo   B body   P particles   L line", one entry per layer in
        /// <see cref="HumanEchoLayerId"/> order. With <paramref name="richText"/> each entry is wrapped in a
        /// colour tag (bright when on, grey when off) for a GUIStyle with rich text. Without it an entry that
        /// is off is written "[S smoke off]", so the state survives plain text.
        /// </summary>
        public static string Layers(bool[] states, bool richText) {
            StringBuilder text = new StringBuilder(160);
            for (int i = 0; i < LayerCount; i++) {
                HumanEchoLayerId layer = (HumanEchoLayerId)i;
                bool on = states != null && i < states.Length && states[i];
                if (i > 0) {
                    text.Append("   ");
                }
                if (richText) {
                    text.Append("<color=").Append(on ? OnColor : OffColor).Append('>');
                    text.Append(LayerKeyLabel(layer)).Append(' ').Append(LayerName(layer));
                    text.Append("</color>");
                } else if (on) {
                    text.Append(LayerKeyLabel(layer)).Append(' ').Append(LayerName(layer));
                } else {
                    text.Append('[').Append(LayerKeyLabel(layer)).Append(' ').Append(LayerName(layer));
                    text.Append(" off]");
                }
            }
            return text.ToString();
        }
    }
}
