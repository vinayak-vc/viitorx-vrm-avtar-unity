using System.Text;

using UnityEngine;

using HandsFaceShowcase;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace CutoutShowcase {
    /// <summary>
    /// The Cutout sample scene: the person in front of the camera, cut out of the picture by the Python sidecar and drawn here. The sidecar finds the
    /// person and sends the picture with a soft mask; this scene only chooses how to show them: the camera as it is (1), the person alone on a
    /// transparent, checkered, plain, gradient or image backdrop (2), or the person as one colour, red or another (3), with an optional glowing
    /// outline. Every switch is a shader parameter: nothing restarts, the camera is never reopened and the sender is never asked anything.
    ///
    /// Nothing in this scene knows about the VRM avatar or skeletal tracking, and the hands, face and body paths are untouched; it asks the
    /// AppBootstrap vision host for the cutout stream (which is what starts the sender in Auto mode) and hands its textures to a
    /// <see cref="CutoutView"/>. Camera, resolution and every segmentation setting are in the AppBootstrap "Hands and Face" section or the F6 panel;
    /// the camera can also be stepped here with the arrow buttons or the comma and period keys.
    /// </summary>
    [AddComponentMenu("Cutout Showcase/Cutout Demo")]
    public sealed class CutoutDemo : VisionShowcaseBase {
        private const string SourceControlName = "CutoutSource";
        private const float PanelWidth = 340f;
        private const float StatusWidth = 430f;

        private static readonly Color[] SilhouetteColors = {
            new Color(1f, 0.08f, 0.08f, 1f),
            new Color(1f, 1f, 1f, 1f),
            new Color(0.1f, 0.85f, 1f, 1f),
            new Color(1f, 0.2f, 0.8f, 1f),
            new Color(1f, 0.85f, 0.1f, 1f),
            new Color(0.2f, 1f, 0.4f, 1f),
            new Color(0.02f, 0.02f, 0.03f, 1f)
        };

        private static readonly string[] SilhouetteNames = { "Red", "White", "Cyan", "Magenta", "Yellow", "Green", "Black" };
        private static readonly string[] ModeNames = { "1 Original", "2 Person", "3 Silhouette" };
        private static readonly string[] MirrorNames = { "Auto", "Off", "On" };
        private static readonly string[] FitNames = { "Contain", "Cover" };
        private static readonly string[] BackdropNames = { "None", "Checker", "Color", "Gradient", "Image" };

        [SerializeField] private CutoutView view;
        [Tooltip("Optional picture for the Image backdrop. Empty: the Image button does nothing.")]
        [SerializeField] private Texture backdropImage;
        [Tooltip("Show the control panel on the left. H toggles it.")]
        [SerializeField] private bool showPanel = true;
        [Tooltip("Show what the cutout can and cannot do at the bottom. I toggles it.")]
        [SerializeField] private bool showInfo = true;

        private int silhouetteIndex;
        private string sourceText = string.Empty;
        private readonly StringBuilder text = new StringBuilder(256);
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;

        protected override VisionStream Stream {
            get {
                return VisionStream.Cutout;
            }
        }

        public CutoutView View {
            get {
                return view;
            }
            set {
                view = value;
            }
        }

        public int SilhouetteIndex {
            get {
                return silhouetteIndex;
            }
        }

        protected override void OnBound(VisionHost visionHost) {
            if (view == null) {
                Debug.LogError("CutoutDemo: no CutoutView is assigned, so nothing can be shown.");
                return;
            }
            view.Bind(visionHost.CutoutTextures);
            if (backdropImage != null) {
                view.Look.BackdropImage = backdropImage;
            }
            view.Look.SilhouetteColor = SilhouetteColors[silhouetteIndex];
            sourceText = visionHost.Settings.Source;
        }

        protected override void OnUnbound(VisionHost visionHost) {
            if (view != null) {
                view.Bind(null);
            }
        }

        protected override void Update() {
            base.Update();
            if (GUI.GetNameOfFocusedControl() == SourceControlName) {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) {
                SetMode(CutoutMode.Original);
            }
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) {
                SetMode(CutoutMode.Person);
            }
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) {
                SetMode(CutoutMode.Silhouette);
            }
            if (Input.GetKeyDown(KeyCode.B)) {
                CycleBackdrop();
            }
            if (Input.GetKeyDown(KeyCode.C)) {
                CycleSilhouetteColor();
            }
            if (Input.GetKeyDown(KeyCode.G)) {
                ToggleGlow();
            }
            if (Input.GetKeyDown(KeyCode.M)) {
                CycleMirror();
            }
            if (Input.GetKeyDown(KeyCode.F)) {
                ToggleFit();
            }
            if (Input.GetKeyDown(KeyCode.Comma) || Input.GetKeyDown(KeyCode.LeftBracket)) {
                StepCamera(-1);
            }
            if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.RightBracket)) {
                StepCamera(1);
            }
            if (Input.GetKeyDown(KeyCode.H)) {
                showPanel = !showPanel;
            }
            if (Input.GetKeyDown(KeyCode.I)) {
                showInfo = !showInfo;
            }
        }

        // ------------------------------------------------------------------ what the keys and buttons do

        public void SetMode(CutoutMode mode) {
            if (view != null) {
                view.Look.Mode = mode;
            }
        }

        /// <summary>None, then each backdrop in turn; the Image backdrop is skipped when no picture is assigned.</summary>
        public void CycleBackdrop() {
            if (view == null) {
                return;
            }
            CutoutBackdrop next = view.Look.Backdrop;
            int guard = 0;
            while (guard < 6) {
                next = (CutoutBackdrop)(((int)next + 1) % BackdropNames.Length);
                if (next != CutoutBackdrop.Image || view.Look.BackdropImage != null) {
                    break;
                }
                guard = guard + 1;
            }
            view.Look.Backdrop = next;
        }

        public void SetBackdrop(CutoutBackdrop backdrop) {
            if (view == null) {
                return;
            }
            if (backdrop == CutoutBackdrop.Image && view.Look.BackdropImage == null) {
                return;
            }
            view.Look.Backdrop = backdrop;
        }

        public void CycleSilhouetteColor() {
            SetSilhouetteColor((silhouetteIndex + 1) % SilhouetteColors.Length);
        }

        public void SetSilhouetteColor(int index) {
            silhouetteIndex = Mathf.Clamp(index, 0, SilhouetteColors.Length - 1);
            if (view != null) {
                view.Look.SilhouetteColor = SilhouetteColors[silhouetteIndex];
                view.Look.GlowColor = Brighter(SilhouetteColors[silhouetteIndex]);
            }
        }

        public void ToggleGlow() {
            if (view != null) {
                view.Look.Glow = !view.Look.Glow;
            }
        }

        public void CycleMirror() {
            if (view != null) {
                view.Look.Mirror = (CutoutMirror)(((int)view.Look.Mirror + 1) % MirrorNames.Length);
            }
        }

        public void ToggleFit() {
            if (view != null) {
                view.Fit = view.Fit == CutoutFit.Contain ? CutoutFit.Cover : CutoutFit.Contain;
            }
        }

        /// <summary>The next (positive) or previous webcam. The sender restarts on the new camera a second later, once the number stops changing.</summary>
        public void StepCamera(int steps) {
            VisionHost host = Host;
            if (host == null) {
                return;
            }
            string next = CutoutCameraSource.Step(host.Settings.Source, steps);
            host.Settings.Source = next;
            sourceText = next;
        }

        /// <summary>Uses the text in the source box as the camera, video file or link. The host says in its status line when it is not a valid source.</summary>
        public void ApplySourceText() {
            VisionHost host = Host;
            if (host != null) {
                host.Settings.Source = sourceText.Trim();
            }
        }

        private static Color Brighter(Color color) {
            return new Color(Mathf.Lerp(color.r, 1f, 0.35f), Mathf.Lerp(color.g, 1f, 0.35f), Mathf.Lerp(color.b, 1f, 0.35f), 1f);
        }

        // ------------------------------------------------------------------ HUD

        private void OnGUI() {
            VisionHost host = Host;
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            EnsureStyles();
            if (host == null) {
                GUI.Label(new Rect(12f, 12f, 500f, 24f), "Waiting for AppBootstrap...", labelStyle);
                GUI.matrix = previous;
                return;
            }
            DrawBanner(host, width);
            DrawStatus(host, width);
            // The F6 vision panel opens over the same corner; the two do the same job, so only one is drawn at a time.
            if (showPanel && !IsVisionPanelOpen()) {
                DrawPanel(host);
            }
            DrawFooter(host, height);
            GUI.matrix = previous;
        }

        private static bool IsVisionPanelOpen() {
            AppBootstrap bootstrap = AppBootstrap.Instance;
            return bootstrap != null && bootstrap.VisionPanel != null && bootstrap.VisionPanel.Visible;
        }

        private void EnsureStyles() {
            if (labelStyle != null) {
                return;
            }
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 11;
            smallStyle.wordWrap = true;
            smallStyle.normal.textColor = new Color(0.85f, 0.88f, 0.95f, 1f);
        }

        private void DrawBanner(VisionHost host, float width) {
            StreamState state = host.StateOf(VisionStream.Cutout);
            CutoutTextures textures = host.CutoutTextures;
            if (state == StreamState.Live && textures.HasFrame) {
                return;
            }
            Box(new Rect(0f, 0f, width, 56f), new Color(0.35f, 0.1f, 0.1f, 0.88f));
            string headline = state == StreamState.Stale ? "The cutout stream went quiet" : "Waiting for the cutout sender (the first frame takes a few seconds: the networks load)";
            GUI.Label(new Rect(12f, 6f, width - 24f, 22f), headline, labelStyle);
            GUI.Label(new Rect(12f, 28f, width - 24f, 22f), host.SenderStatus(VisionStream.Cutout) + "   (" + host.Settings.PanelKey + ": control panel)", labelStyle);
        }

        private void DrawStatus(VisionHost host, float width) {
            CutoutTextures textures = host.CutoutTextures;
            StreamState state = host.StateOf(VisionStream.Cutout);
            text.Length = 0;
            text.Append(state == StreamState.Live ? "LIVE " : (state == StreamState.Stale ? "STALE " : "WAITING "));
            if (textures.HasFrame) {
                text.Append(host.CutoutRate.ToString("0.0")).Append(" frames/s\n");
                text.Append("picture ").Append(textures.HasColor ? textures.ColorWidth + "x" + textures.ColorHeight + " " + textures.ColorEncoding : "none (mask only)")
                    .Append("   mask ").Append(textures.MaskWidth).Append("x").Append(textures.MaskHeight)
                    .Append("   ").Append(textures.ModelId == CutoutWire.ModelLandscape ? "landscape net" : "square net").Append("\n");
                text.Append("people ").Append(textures.People).Append("   area ").Append((textures.ForegroundFraction * 100f).ToString("0.0")).Append("%")
                    .Append(textures.Nobody ? "   NOBODY FOUND" : string.Empty).Append("\n");
                text.Append("segmentation ").Append(textures.InferMs.ToString("0.0")).Append(" ms   sender latency ").Append(textures.SenderLatencyMs.ToString("0"))
                    .Append(" ms   Unity upload ").Append(textures.UploadMilliseconds.ToString("0.0")).Append(" ms");
                if (view != null) {
                    text.Append("\nshowing ").Append(view.EffectiveMode).Append(view.MirrorApplied ? ", mirrored" : string.Empty);
                }
            }
            float x = width - StatusWidth - 10f;
            float y = state == StreamState.Live && textures.HasFrame ? 10f : 64f;
            Box(new Rect(x, y, StatusWidth, textures.HasFrame ? 108f : 30f), new Color(0f, 0f, 0f, 0.5f));
            GUI.Label(new Rect(x + 8f, y + 4f, StatusWidth - 16f, 104f), text.ToString(), smallStyle);
        }

        private void DrawPanel(VisionHost host) {
            if (view == null) {
                return;
            }
            CutoutLook look = view.Look;
            float x = 10f;
            float y = 70f;
            Box(new Rect(x, y, PanelWidth, 400f), new Color(0f, 0f, 0f, 0.5f));
            float left = x + 10f;
            float inner = PanelWidth - 20f;
            y = y + 8f;
            GUI.Label(new Rect(left, y, inner, 20f), "CUTOUT   (H hides this panel)", labelStyle);
            y = y + 26f;

            GUI.Label(new Rect(left, y, inner, 18f), "Show", smallStyle);
            int mode = GUI.Toolbar(new Rect(left, y + 18f, inner, 26f), (int)look.Mode, ModeNames);
            if (mode != (int)look.Mode) {
                SetMode((CutoutMode)mode);
            }
            y = y + 52f;

            GUI.Label(new Rect(left, y, inner, 18f), "Silhouette colour (3): " + SilhouetteNames[silhouetteIndex] + "   C cycles", smallStyle);
            DrawSwatches(new Rect(left, y + 18f, inner, 22f));
            y = y + 48f;

            GUI.Label(new Rect(left, y, inner, 18f), "Backdrop (B)", smallStyle);
            int backdrop = GUI.Toolbar(new Rect(left, y + 18f, inner, 26f), (int)look.Backdrop, BackdropNames);
            if (backdrop != (int)look.Backdrop) {
                SetBackdrop((CutoutBackdrop)backdrop);
            }
            y = y + 52f;

            bool glow = GUI.Toggle(new Rect(left, y, 130f, 22f), look.Glow, " Glow outline (G)");
            if (glow != look.Glow) {
                ToggleGlow();
            }
            if (look.Glow) {
                GUI.Label(new Rect(left + 140f, y, 46f, 20f), "width", smallStyle);
                look.GlowWidth = GUI.HorizontalSlider(new Rect(left + 186f, y + 6f, inner - 186f, 16f), look.GlowWidth, 1f, 16f);
            }
            y = y + 30f;

            GUI.Label(new Rect(left, y, 150f, 18f), "Mirror (M)", smallStyle);
            int mirror = GUI.Toolbar(new Rect(left + 80f, y - 2f, inner - 80f, 24f), (int)look.Mirror, MirrorNames);
            if (mirror != (int)look.Mirror) {
                look.Mirror = (CutoutMirror)mirror;
            }
            y = y + 30f;

            GUI.Label(new Rect(left, y, 150f, 18f), "Fit (F)", smallStyle);
            int fit = GUI.Toolbar(new Rect(left + 80f, y - 2f, inner - 80f, 24f), (int)view.Fit, FitNames);
            if (fit != (int)view.Fit) {
                view.Fit = (CutoutFit)fit;
            }
            y = y + 34f;

            GUI.Label(new Rect(left, y, inner, 18f), "Camera ( , and . step; a video file or link works too)", smallStyle);
            if (GUI.Button(new Rect(left, y + 20f, 32f, 24f), "<")) {
                StepCamera(-1);
            }
            GUI.SetNextControlName(SourceControlName);
            sourceText = GUI.TextField(new Rect(left + 38f, y + 20f, inner - 146f, 24f), sourceText, 512);
            if (GUI.Button(new Rect(left + inner - 102f, y + 20f, 32f, 24f), ">")) {
                StepCamera(1);
            }
            if (GUI.Button(new Rect(left + inner - 64f, y + 20f, 64f, 24f), "Apply")) {
                ApplySourceText();
            }
            y = y + 50f;
            GUI.Label(new Rect(left, y, inner, 44f), "In use: " + host.Settings.Source, smallStyle);
        }

        private void DrawSwatches(Rect area) {
            float size = 22f;
            int i = 0;
            while (i < SilhouetteColors.Length) {
                Rect rect = new Rect(area.x + i * (size + 8f), area.y, size, size);
                Box(new Rect(rect.x - 2f, rect.y - 2f, size + 4f, size + 4f), i == silhouetteIndex ? Color.white : new Color(1f, 1f, 1f, 0.15f));
                Box(rect, SilhouetteColors[i]);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) {
                    SetSilhouetteColor(i);
                }
                i = i + 1;
            }
        }

        private void DrawFooter(VisionHost host, float height) {
            float y = height - 26f;
            // The control panel draws its own "F6: vision control panel" hint at the bottom left, so the key list starts to the right of it.
            GUI.Label(new Rect(250f, y, 1200f, 22f), "1 2 3 mode    B backdrop    C colour    G glow    M mirror    F fit    , . camera    H panel    I info    ESC launcher", labelStyle);
            if (!showInfo) {
                return;
            }
            Box(new Rect(0f, y - 44f, Screen.width, 42f), new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(12f, y - 42f, Screen.width / Mathf.Max(1f, Screen.height / 1080f) - 24f, 40f),
                      "The sidecar cuts the person out with a small 256 px network (MediaPipe selfie segmentation), once per person crop. Dark clothes on a dark "
                      + "background, a thin gap between arm and body and bright floor reflections can be cut or kept wrongly, and the edge is soft by design. "
                      + "Nothing here depends on the avatar or on skeleton tracking.", smallStyle);
        }

        private static void Box(Rect rect, Color color) {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
