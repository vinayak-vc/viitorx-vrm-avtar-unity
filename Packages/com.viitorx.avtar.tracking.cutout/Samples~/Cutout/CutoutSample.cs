using System.Text;

using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace Viitorx.Tracking.Samples {
    /// <summary>
    /// The cutout sample: the person in front of the camera, cut out of the picture by the engine and drawn by a <see cref="CutoutView"/>. It asks the running
    /// <see cref="VisionBootstrap"/> for the cutout stream (in mode Auto that starts the engine for it) and hands the textures to the view. The buttons at the
    /// top-left choose how the person is shown (the camera as it is, the person alone, one colour) and what is behind them; they only change shader parameters,
    /// so nothing restarts and the camera is never reopened. With the legacy Input Manager the keys 1, 2 and 3 do the same as the first three buttons.
    ///
    /// Needs the Universal Render Pipeline (the two shaders include its core library). The camera and every segmentation option are on the VisionBootstrap,
    /// or in its F6 panel at run time.
    /// </summary>
    [AddComponentMenu("Viitorx Tracking Samples/Cutout Sample")]
    public sealed class CutoutSample : VisionHostClient {
        private static readonly VisionStream[] OnlyCutout = { VisionStream.Cutout };
        private static readonly string[] ModeNames = { "Original", "Person", "Silhouette" };
        private static readonly string[] BackdropNames = { "None", "Checker", "Color", "Gradient" };

        [Tooltip("The view that draws the cutout. Its two materials (shaders VirtualMirror/Cutout and VirtualMirror/CutoutBackdrop) are part of this sample.")]
        [SerializeField] private CutoutView view;
        [SerializeField] private bool showControls = true;

        private readonly StringBuilder text = new StringBuilder(256);
        private GUIStyle style;

        protected override VisionStream[] Streams {
            get {
                return OnlyCutout;
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

        protected override void OnBound(VisionHost visionHost) {
            if (view == null) {
                Debug.LogError("CutoutSample: no CutoutView is assigned, so nothing can be shown.");
                return;
            }
            view.Bind(visionHost.CutoutTextures);
        }

        protected override void OnUnbound(VisionHost visionHost) {
            if (view != null) {
                view.Bind(null);
            }
        }

        protected override void Update() {
            base.Update();
#if ENABLE_LEGACY_INPUT_MANAGER
            if (view == null) {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha1)) {
                view.Look.Mode = CutoutMode.Original;
            }
            if (Input.GetKeyDown(KeyCode.Alpha2)) {
                view.Look.Mode = CutoutMode.Person;
            }
            if (Input.GetKeyDown(KeyCode.Alpha3)) {
                view.Look.Mode = CutoutMode.Silhouette;
            }
#endif
        }

        private void OnGUI() {
            if (!showControls) {
                return;
            }
            if (style == null) {
                style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.UpperLeft;
                style.wordWrap = true;
                style.fontSize = 13;
            }
            text.Length = 0;
            if (Host == null) {
                text.Append("No VisionBootstrap is running: add one to a GameObject in this scene.");
            } else {
                text.Append(Host.EngineDescription).Append('\n');
                text.Append("cutout ").Append(Host.StateOf(VisionStream.Cutout)).Append("  ").Append(Host.CutoutRate.ToString("0")).Append(" frames/s  ").Append(Host.SenderStatus(VisionStream.Cutout)).Append('\n');
                text.Append("F6: settings (Source, engine path, segmentation options)");
            }
            GUI.Box(new Rect(12f, 12f, 560f, Host == null ? 44f : 82f), text.ToString(), style);
            if (view == null) {
                return;
            }
            GUILayout.BeginArea(new Rect(12f, 102f, 560f, 90f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Show", GUILayout.Width(56f));
            view.Look.Mode = (CutoutMode)GUILayout.Toolbar((int)view.Look.Mode, ModeNames);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Behind", GUILayout.Width(56f));
            view.Look.Backdrop = (CutoutBackdrop)GUILayout.Toolbar((int)view.Look.Backdrop, BackdropNames);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
