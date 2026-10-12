using ShowcaseShared;

using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// The only on-screen text in the normal presentation: a title, one status chip, one short
    /// instruction and a tiny debug toggle. All of it sits in the corners, clear of the head,
    /// hands and silhouette. IMGUI, like the project's other showcase scenes (no Canvas, no prefab).
    /// The status line is rebuilt only when what it says actually changes.
    /// </summary>
    public sealed class ShowcaseUI : MonoBehaviour {
        public const string TitleText = "CHROMATIC RAIN";
        public const string InstructionText = "Move your body to disturb the colors";

        [SerializeField] private ChromaticRainShowcase showcase;

        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle hintStyle;
        private GUIStyle buttonStyle;
        private string statusText = string.Empty;
        private Color statusColor = Color.gray;
        private SignalStatus lastSignal = (SignalStatus)(-1);
        private bool lastPerson;
        private int lastPeople;
        private bool lastEverReceived;
        private string lastLabel = string.Empty;

        private void BuildStyles() {
            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 15;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.78f, 0.9f, 1f, 0.85f);

            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 12;
            statusStyle.normal.textColor = new Color(0.8f, 0.88f, 1f, 0.8f);

            hintStyle = new GUIStyle(GUI.skin.label);
            hintStyle.fontSize = 13;
            hintStyle.normal.textColor = new Color(0.78f, 0.86f, 1f, 0.55f);

            buttonStyle = new GUIStyle(GUI.skin.label);
            buttonStyle.fontSize = 11;
            buttonStyle.alignment = TextAnchor.MiddleRight;
            buttonStyle.normal.textColor = new Color(0.78f, 0.86f, 1f, 0.45f);
        }

        private void RefreshStatus() {
            SignalStatus signal = showcase.Signal;
            bool person = showcase.PersonTracked;
            int people = showcase.TrackedPeopleCount;
            bool everReceived = showcase.PacketsReceived > 0L;
            string label = showcase.LiveSourceLabel;
            if (signal == lastSignal && person == lastPerson && people == lastPeople && everReceived == lastEverReceived
                && string.Equals(label, lastLabel, System.StringComparison.Ordinal)) {
                return;
            }
            lastLabel = label;
            string from = string.IsNullOrEmpty(label) ? string.Empty : " (" + label + ")";
            lastSignal = signal;
            lastPerson = person;
            lastPeople = people;
            lastEverReceived = everReceived;
            string who = person ? (people > 1 ? people + " people tracked" : "person tracked") : "no person";
            switch (signal) {
                case SignalStatus.Live:
                    statusText = "LIVE" + from + "  -  " + who;
                    statusColor = new Color(0.35f, 1f, 0.6f, 1f);
                    break;
                case SignalStatus.Simulated:
                    statusText = "SIMULATED  -  " + who;
                    statusColor = new Color(1f, 0.72f, 0.2f, 1f);
                    break;
                case SignalStatus.SimulatedFallback:
                    statusText = "SIMULATED (no live signal)  -  " + who;
                    statusColor = new Color(1f, 0.72f, 0.2f, 1f);
                    break;
                case SignalStatus.PortInUse:
                    statusText = "TRACKING PORT IN USE  -  choose Simulated or free the port";
                    statusColor = new Color(1f, 0.35f, 0.35f, 1f);
                    break;
                default:
                    statusText = (everReceived
                        ? "NO SIGNAL  -  no tracking data right now"
                        : "NO SIGNAL  -  waiting for the sidecar") + from;
                    statusColor = new Color(0.6f, 0.65f, 0.75f, 1f);
                    break;
            }
        }

        private void OnGUI() {
            if (showcase == null || !showcase.UiVisible) {
                return;
            }
            if (titleStyle == null) {
                BuildStyles();
            }
            RefreshStatus();

            float margin = 18f;
            GUI.Label(new Rect(margin, 12f, 400f, 24f), TitleText, titleStyle);

            Color previous = GUI.color;
            GUI.color = statusColor;
            GUI.DrawTexture(new Rect(margin + 1f, 41f, 8f, 8f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(margin + 16f, 34f, 700f, 22f), statusText, statusStyle);

            GUI.Label(new Rect(margin, Screen.height - 34f, 600f, 24f), InstructionText, hintStyle);

            string toggle = showcase.DebugVisible ? "debug  on" : "debug";
            if (GUI.Button(new Rect(Screen.width - 108f, Screen.height - 32f, 90f, 22f), toggle, buttonStyle)) {
                showcase.ToggleDebug();
            }
        }
    }
}
