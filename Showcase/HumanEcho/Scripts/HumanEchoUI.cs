using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace HumanEcho {
    /// <summary>
    /// The Human Echo HUD and its keys. IMGUI, like the project's other showcases: one compact panel (about
    /// 330 x 262 points) with the title, where the picture comes from, the active look and what it is meant
    /// to resemble, the rates, the layers and the key list. H hides everything.
    ///
    /// The input line only says LIVE while the cutout texture set has a frame whose version counter changed
    /// within the last two seconds (<see cref="HumanEchoInputState"/>); otherwise it says WAITING FOR CAMERA.
    /// The key map and every piece of text come from small pure classes (<see cref="HumanEchoKeys"/>,
    /// <see cref="HumanEchoInputState"/>, <see cref="HumanEchoStatusFormatter"/>), so this component only
    /// polls, calls the controller and draws. The text is rebuilt four times a second or when a key changed
    /// something, never once per frame.
    ///
    /// Keys it does not take, on purpose: O (the AppBootstrap), F6 (the vision control panel) and Escape (the
    /// Launcher).
    /// </summary>
    [AddComponentMenu("Virtual Mirror/Human Echo UI")]
    [DisallowMultipleComponent]
    public sealed class HumanEchoUI : MonoBehaviour {
        public const string TitleText = "HUMAN ECHO";

        private const float BoxWidth = 330f;
        private const float BoxHeight = 262f;
        private const float Margin = 10f;
        private const float Padding = 12f;
        private const float RefreshSeconds = 0.25f;

        [SerializeField] private HumanEchoController controller;
        [Tooltip("The panel starts shown; H toggles it at run time.")]
        [SerializeField] private bool visible = true;

        private readonly HumanEchoInputState inputState
            = new HumanEchoInputState(HumanEchoInputState.DefaultFreshSeconds);
        private readonly bool[] layerStates = new bool[HumanEchoStatusFormatter.LayerCount];

        private GUIStyle titleStyle;
        private GUIStyle stateStyle;
        private GUIStyle headerStyle;
        private GUIStyle smallStyle;
        private GUIStyle layerStyle;
        private GUIStyle helpStyle;

        private string inputText = HumanEchoStatusFormatter.WaitingText;
        private string lookHeader = string.Empty;
        private string notesText = string.Empty;
        private string ratesText = string.Empty;
        private string statusText = string.Empty;
        private string layersText = string.Empty;
        private Color inputColor = new Color(0.62f, 0.68f, 0.8f, 1f);

        private int lastLookIndex = -1;
        private int lastLookCount = -1;
        private string lastLookName = null;
        private int lastLayerMask = -1;
        private float nextRefreshTime;
        private bool dirty = true;
        private bool panelOpen;

        private void Awake() {
            if (controller == null) {
                controller = GetComponent<HumanEchoController>();
            }
            if (controller == null) {
                Debug.LogError("[HumanEcho] HumanEchoUI has no HumanEchoController. "
                               + "Run Showcase > Human Echo > Build Showcase Scene.", this);
                enabled = false;
            }
        }

        private void OnEnable() {
            dirty = true;
        }

        private void Update() {
            panelOpen = IsVisionPanelOpen();
            ObserveInput();
            // The vision panel's source box takes the keyboard while it has focus; the letters typed into it
            // must not also switch layers. Only while that panel is open: nothing else in this scene has a
            // text box, and IMGUI keeps a clicked box focused until another control takes it, so a stale
            // focus must not disable the keys once the panel is closed.
            if (!(panelOpen && GUIUtility.keyboardControl != 0)) {
                HandleKeys();
            }
            if (visible && (dirty || Time.unscaledTime >= nextRefreshTime)) {
                RefreshText();
            }
        }

        private void ObserveInput() {
            CutoutTextures textures = controller.Textures;
            bool hasFrame = textures != null && textures.HasFrame;
            int version = hasFrame ? textures.Version : 0;
            inputState.Observe(hasFrame, version, hasFrame && textures.Live, Time.unscaledTime);
        }

        private void HandleKeys() {
            IReadOnlyList<KeyCode> keys = HumanEchoKeys.HandledKeys;
            for (int i = 0; i < keys.Count; i++) {
                if (!Input.GetKeyDown(keys[i])) {
                    continue;
                }
                HumanEchoCommand command;
                if (HumanEchoKeys.TryMap(keys[i], out command)) {
                    Execute(command);
                }
            }
        }

        private void Execute(HumanEchoCommand command) {
            switch (command.Action) {
                case HumanEchoAction.SelectLook:
                    SelectLook(command.LookIndex);
                    break;
                case HumanEchoAction.NextLook:
                    controller.NextLook();
                    break;
                case HumanEchoAction.CycleMirror:
                    controller.CycleMirror();
                    break;
                case HumanEchoAction.ClearFluid:
                    controller.ClearFluid();
                    break;
                case HumanEchoAction.ToggleDebugTiles:
                    controller.ShowDebugTiles = !controller.ShowDebugTiles;
                    break;
                case HumanEchoAction.ToggleHud:
                    visible = !visible;
                    break;
                case HumanEchoAction.ToggleLayer:
                    controller.ToggleLayer(command.Layer);
                    break;
            }
            dirty = true;
        }

        // A number with no look behind it (key 7 with three looks) does nothing; the controller is never
        // asked for a look that is not there.
        private void SelectLook(int index) {
            IReadOnlyList<HumanEchoSettings> looks = controller.Looks;
            if (looks != null && index >= 0 && index < looks.Count) {
                controller.SelectLook(index);
            }
        }

        private static bool IsVisionPanelOpen() {
            AppBootstrap bootstrap = AppBootstrap.Instance;
            return bootstrap != null && bootstrap.VisionPanel != null && bootstrap.VisionPanel.Visible;
        }

        private void RefreshText() {
            dirty = false;
            float now = Time.unscaledTime;
            nextRefreshTime = now + RefreshSeconds;

            IReadOnlyList<HumanEchoSettings> looks = controller.Looks;
            HumanEchoSettings look = controller.CurrentLook;
            int count = looks != null ? looks.Count : 0;
            int index = controller.LookIndex;
            string lookName = look != null ? look.displayName : string.Empty;
            if (index != lastLookIndex || count != lastLookCount || !string.Equals(lookName, lastLookName)) {
                lastLookIndex = index;
                lastLookCount = count;
                lastLookName = lookName;
                lookHeader = HumanEchoStatusFormatter.LookHeader(index, count, lookName);
            }
            notesText = look != null && look.notes != null ? look.notes : string.Empty;

            HumanEchoInputKind kind = inputState.Kind(now);
            inputText = HumanEchoStatusFormatter.InputLine(kind, inputState.SecondsSinceFrame(now));
            inputColor = ColorOf(kind);

            ratesText = HumanEchoStatusFormatter.Rates(controller.DisplayFps, controller.MaskRate,
                                                       controller.Mirror, controller.MirrorApplied);
            string line = controller.StatusLine;
            statusText = line != null ? line : string.Empty;

            int mask = 0;
            for (int i = 0; i < layerStates.Length; i++) {
                HumanEchoLayerId layer = (HumanEchoLayerId)i;
                bool on = HumanEchoLayerRules.IsDrawn(look, layer, controller.IsLayerOn(layer));
                layerStates[i] = on;
                if (on) {
                    mask = mask | (1 << i);
                }
            }
            if (mask != lastLayerMask) {
                lastLayerMask = mask;
                layersText = HumanEchoStatusFormatter.Layers(layerStates, true);
            }
        }

        private static Color ColorOf(HumanEchoInputKind kind) {
            switch (kind) {
                case HumanEchoInputKind.Live:
                    return new Color(0.35f, 1f, 0.6f, 1f);
                case HumanEchoInputKind.VideoFile:
                    return new Color(0.5f, 0.85f, 1f, 1f);
                case HumanEchoInputKind.Stale:
                    return new Color(1f, 0.55f, 0.25f, 1f);
                default:
                    return new Color(1f, 0.72f, 0.2f, 1f);
            }
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI() {
            // Nothing here reacts to the mouse, so only the paint pass does any work.
            if (!visible || controller == null || Event.current.type != EventType.Repaint) {
                return;
            }
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            // The vision control panel opens at the top left, so the HUD steps to the right edge while it is
            // open.
            float x = panelOpen ? width - BoxWidth - Margin : Margin;
            DrawPanel(x, Margin);
            GUI.matrix = previous;
        }

        private void DrawPanel(float x, float y) {
            float left = x + Padding;
            float inner = BoxWidth - 2f * Padding;
            Fill(new Rect(x, y, BoxWidth, BoxHeight), new Color(0f, 0f, 0f, 0.55f));

            GUI.Label(new Rect(left, y + 6f, inner, 24f), TitleText, titleStyle);

            Fill(new Rect(left + 1f, y + 37f, 8f, 8f), inputColor);
            stateStyle.normal.textColor = inputColor;
            GUI.Label(new Rect(left + 16f, y + 30f, inner - 16f, 20f), inputText, stateStyle);

            GUI.Label(new Rect(left, y + 52f, inner, 20f), lookHeader, headerStyle);
            GUI.Label(new Rect(left, y + 72f, inner, 46f), notesText, smallStyle);
            Fill(new Rect(left, y + 120f, inner, 1f), new Color(1f, 1f, 1f, 0.15f));

            GUI.Label(new Rect(left, y + 124f, inner, 18f), ratesText, smallStyle);
            GUI.Label(new Rect(left, y + 142f, inner, 32f), statusText, smallStyle);
            GUI.Label(new Rect(left, y + 176f, inner, 18f), layersText, layerStyle);
            Fill(new Rect(left, y + 198f, inner, 1f), new Color(1f, 1f, 1f, 0.15f));

            GUI.Label(new Rect(left, y + 202f, inner, 56f), HumanEchoStatusFormatter.KeyHelp, helpStyle);
        }

        private void EnsureStyles() {
            if (titleStyle != null) {
                return;
            }
            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 16;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.9f, 0.97f, 1f, 0.95f);

            stateStyle = new GUIStyle(GUI.skin.label);
            stateStyle.fontSize = 12;
            stateStyle.fontStyle = FontStyle.Bold;
            stateStyle.clipping = TextClipping.Clip;

            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 13;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.clipping = TextClipping.Clip;
            headerStyle.normal.textColor = new Color(1f, 0.92f, 0.7f, 1f);

            smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 11;
            smallStyle.wordWrap = true;
            smallStyle.clipping = TextClipping.Clip;
            smallStyle.normal.textColor = new Color(0.82f, 0.86f, 0.95f, 1f);

            layerStyle = new GUIStyle(GUI.skin.label);
            layerStyle.fontSize = 11;
            layerStyle.richText = true;
            layerStyle.clipping = TextClipping.Clip;
            layerStyle.normal.textColor = new Color(0.82f, 0.86f, 0.95f, 1f);

            helpStyle = new GUIStyle(GUI.skin.label);
            helpStyle.fontSize = 10;
            helpStyle.wordWrap = true;
            helpStyle.clipping = TextClipping.Clip;
            helpStyle.normal.textColor = new Color(0.7f, 0.76f, 0.88f, 0.85f);
        }

        private static void Fill(Rect rect, Color color) {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
