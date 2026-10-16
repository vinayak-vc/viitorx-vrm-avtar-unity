using System.Text;

using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace Viitorx.Tracking.Samples {
    /// <summary>
    /// The hands and face sample: every hand as a 21-point skeleton and the face as a point cloud with head axes, at their estimated place in front of
    /// the main camera, and a status box that says what the engine is doing. It asks the running <see cref="VisionBootstrap"/> for the hands and face
    /// streams (in mode Auto that starts the engine for them, ONE process for both: the webcam is read once) and draws what arrives.
    ///
    /// Positions are the engine's ESTIMATES (an assumed field of view and an average hand size): a mirror, not a measurement. Nothing here needs a body
    /// or an avatar. Change the camera, the engine path and every option on the VisionBootstrap, or at run time in its F6 panel.
    /// </summary>
    [AddComponentMenu("Viitorx Tracking Samples/Hands And Face Sample")]
    public sealed class HandsAndFaceSample : VisionHostClient {
        private static readonly VisionStream[] BothStreams = { VisionStream.Hands, VisionStream.Face };

        [SerializeField] private bool showHands = true;
        [SerializeField] private bool showFace = true;
        [Tooltip("Show the status box (the engine in use, each stream's state and rate, what each sender is doing).")]
        [SerializeField] private bool showStatus = true;
        [Tooltip("Camera space is relative to this transform. Empty: the main camera.")]
        [SerializeField] private Transform cameraSpace;

        private readonly StringBuilder text = new StringBuilder(512);
        private HandsGizmoView handsView;
        private FaceGizmoView faceView;
        private GUIStyle style;

        protected override VisionStream[] Streams {
            get {
                return BothStreams;
            }
        }

        protected override void OnBound(VisionHost visionHost) {
            if (showHands) {
                GameObject hands = new GameObject("Hands");
                hands.transform.SetParent(transform, false);
                handsView = hands.AddComponent<HandsGizmoView>();
                handsView.SetAnchor(cameraSpace);
                visionHost.HandsUpdated += handsView.Show;
            }
            if (showFace) {
                GameObject face = new GameObject("Face");
                face.transform.SetParent(transform, false);
                faceView = face.AddComponent<FaceGizmoView>();
                faceView.SetAnchor(cameraSpace);
                visionHost.FaceUpdated += faceView.Show;
            }
        }

        protected override void OnUnbound(VisionHost visionHost) {
            if (handsView != null) {
                visionHost.HandsUpdated -= handsView.Show;
                Destroy(handsView.gameObject);
                handsView = null;
            }
            if (faceView != null) {
                visionHost.FaceUpdated -= faceView.Show;
                Destroy(faceView.gameObject);
                faceView = null;
            }
        }

        protected override void Update() {
            base.Update();
            if (Host == null) {
                return;
            }
            if (handsView != null && Host.StateOf(VisionStream.Hands) != StreamState.Live) {
                handsView.Hide();
            }
            if (faceView != null && Host.StateOf(VisionStream.Face) != StreamState.Live) {
                faceView.Hide();
            }
        }

        private void OnGUI() {
            if (!showStatus) {
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
                AppendStream(VisionStream.Hands, "hands ", Host.HandsRate);
                AppendStream(VisionStream.Face, "face  ", Host.FaceRate);
                text.Append("F6: settings (Source, engine path, per-stream options)");
            }
            GUI.Box(new Rect(12f, 12f, 520f, Host == null ? 44f : 118f), text.ToString(), style);
        }

        private void AppendStream(VisionStream stream, string label, float rate) {
            text.Append(label).Append(Host.StateOf(stream)).Append("  ").Append(rate.ToString("0")).Append(" msg/s  ").Append(Host.SenderStatus(stream)).Append('\n');
        }
    }
}
