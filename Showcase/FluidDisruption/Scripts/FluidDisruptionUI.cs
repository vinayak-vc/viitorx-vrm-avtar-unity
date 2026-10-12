using ChromaticRain;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// The only on-screen text in the normal presentation, all of it in the corners: the title, one
    /// status line (the input, then whether a person is tracked), one short instruction, and two tiny
    /// controls (debug, reset). IMGUI, like the project's other showcase scenes. The status line is
    /// rebuilt only when what it says actually changes.
    ///
    /// It never suggests a person was detected because the scene merely runs: PERSON DETECTED needs a
    /// confirmed person AND a torso measured this frame.
    /// </summary>
    public sealed class FluidDisruptionUI : MonoBehaviour {
        public const string TitleText = "FLUID DISRUPTION";
        public const string InstructionText = "Move your hands to disturb the fluid";
        public const string PersonText = "PERSON DETECTED";
        public const string WaitingText = "WAITING FOR PERSON";

        [SerializeField] private FluidDisruptionController controller;

        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle hintStyle;
        private GUIStyle buttonStyle;
        private string inputText = string.Empty;
        private string personText = WaitingText;
        private Color inputColor = Color.gray;
        private Color personColor = Color.gray;
        private SignalStatus lastSignal = (SignalStatus)(-1);
        private bool lastPerson;
        private string lastLabel = string.Empty;
        private bool built;

        /// <summary>The input indicator as the chip shows it, for tests and the debug view.</summary>
        public string InputText {
            get {
                return inputText;
            }
        }

        public string PersonStateText {
            get {
                return personText;
            }
        }

        private void BuildStyles() {
            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 15;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.78f, 0.92f, 1f, 0.85f);

            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 12;
            statusStyle.normal.textColor = new Color(0.82f, 0.9f, 1f, 0.85f);

            hintStyle = new GUIStyle(GUI.skin.label);
            hintStyle.fontSize = 13;
            hintStyle.normal.textColor = new Color(0.78f, 0.88f, 1f, 0.6f);

            buttonStyle = new GUIStyle(GUI.skin.label);
            buttonStyle.fontSize = 11;
            buttonStyle.alignment = TextAnchor.MiddleRight;
            buttonStyle.normal.textColor = new Color(0.78f, 0.88f, 1f, 0.5f);
        }

        private void RefreshStatus() {
            SignalStatus signal = controller.Signal;
            bool person = controller.PersonTracked;
            string label = controller.LiveSourceLabel;
            if (built && signal == lastSignal && person == lastPerson
                && string.Equals(label, lastLabel, System.StringComparison.Ordinal)) {
                return;
            }
            built = true;
            lastSignal = signal;
            lastPerson = person;
            lastLabel = label;
            string from = string.IsNullOrEmpty(label) ? string.Empty : " (" + label + ")";
            switch (signal) {
                case SignalStatus.Live:
                    inputText = "LIVE" + from;
                    inputColor = new Color(0.35f, 1f, 0.6f, 1f);
                    break;
                case SignalStatus.Simulated:
                    inputText = "SIMULATED";
                    inputColor = new Color(1f, 0.72f, 0.2f, 1f);
                    break;
                case SignalStatus.SimulatedFallback:
                    inputText = "SIMULATED (no live signal)";
                    inputColor = new Color(1f, 0.72f, 0.2f, 1f);
                    break;
                case SignalStatus.PortInUse:
                    inputText = "TRACKING PORT IN USE";
                    inputColor = new Color(1f, 0.35f, 0.35f, 1f);
                    break;
                default:
                    inputText = "NO SIGNAL" + from;
                    inputColor = new Color(0.62f, 0.68f, 0.8f, 1f);
                    break;
            }
            personText = person ? PersonText : WaitingText;
            personColor = person ? new Color(0.5f, 0.95f, 1f, 1f) : new Color(0.62f, 0.68f, 0.8f, 1f);
        }

        private void OnGUI() {
            if (controller == null || !controller.UiVisible) {
                return;
            }
            if (titleStyle == null) {
                BuildStyles();
            }
            RefreshStatus();

            float margin = 18f;
            GUI.Label(new Rect(margin, 12f, 400f, 24f), TitleText, titleStyle);

            Color previous = GUI.color;
            GUI.color = inputColor;
            GUI.DrawTexture(new Rect(margin + 1f, 41f, 8f, 8f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(margin + 16f, 34f, 700f, 22f), inputText, statusStyle);

            GUI.color = personColor;
            GUI.DrawTexture(new Rect(margin + 1f, 63f, 8f, 8f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(margin + 16f, 56f, 700f, 22f), personText, statusStyle);

            GUI.Label(new Rect(margin, Screen.height - 34f, 600f, 24f), InstructionText, hintStyle);

            if (GUI.Button(new Rect(Screen.width - 196f, Screen.height - 32f, 90f, 22f), "reset", buttonStyle)) {
                controller.ResetFluid();
            }
            string toggle = controller.DebugVisible ? "debug  on" : "debug";
            if (GUI.Button(new Rect(Screen.width - 108f, Screen.height - 32f, 90f, 22f), toggle, buttonStyle)) {
                controller.ToggleDebug();
            }
        }
    }
}
