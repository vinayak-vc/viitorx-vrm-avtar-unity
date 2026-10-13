using System.Text;

using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.App {
    /// <summary>
    /// The runtime control panel for the hands, face and cutout streams: every setting of <see cref="VisionSettings"/> (the "Hands and Face"
    /// section of <see cref="AppBootstrap"/>) as a control, the state of the senders and receivers, and what each sender is running with. Opened
    /// with <see cref="VisionSettings.PanelKey"/> (F6 by default). An IMGUI window, so it needs no prefab and works in any scene the bootstrap
    /// lives in.
    ///
    /// Changes apply to the running senders after a second of quiet (the host compares command lines), or at once with Apply. They are
    /// NOT saved: tick the same setting on AppBootstrap to make it stick. NOT TESTED in the Editor: written to the documented IMGUI API.
    /// </summary>
    public sealed class VisionControlPanel : MonoBehaviour {
        private const int WindowId = 7316;
        private const float WindowWidth = 480f;
        private static readonly string[] TabNames = { "Status", "Source", "Hands", "Face", "Cutout", "Receiver" };
        private static readonly string[] ModeNames = { "Off", "Auto", "Always" };
        private static readonly string[] ProviderNames = { "Auto", "DirectML", "CPU" };
        private static readonly string[] SidesNames = { "Auto", "Model", "Swapped" };
        private static readonly string[] LandmarkNames = { "Full 478", "Mesh 468", "None" };
        private static readonly string[] RoiNames = { "Auto", "On", "Off" };
        private static readonly string[] ColorNames = { "Raw RGBA", "JPEG", "None" };
        private static readonly string[] MaskEncodingNames = { "Raw", "Deflate" };
        private static readonly string[] RotationNames = { "0", "90", "180", "270" };
        private static readonly string[] SizeNames = { "640x480", "1280x720", "1920x1080" };
        private static readonly Vector2Int[] Sizes = { new Vector2Int(640, 480), new Vector2Int(1280, 720), new Vector2Int(1920, 1080) };

        private VisionHost host;
        private VisionSettings settings;
        private VisionSettings pristine;
        private bool visible;
        private int tab;
        private Vector2 scroll;
        private Rect window = new Rect(16f, 16f, WindowWidth, 640f);
        private GUIStyle statusStyle;
        private GUIStyle codeStyle;
        private readonly StringBuilder text = new StringBuilder(512);

        public bool Visible {
            get {
                return visible;
            }
            set {
                visible = value;
            }
        }

        /// <summary>Attaches the panel to the host whose settings it edits. The values at this moment are what Reset returns to.</summary>
        public void Bind(VisionHost visionHost) {
            host = visionHost;
            settings = visionHost == null ? null : visionHost.Settings;
            pristine = settings == null ? null : settings.Clone();
            visible = settings != null && settings.ShowPanelOnStart;
        }

        private void Update() {
            if (settings != null && Input.GetKeyDown(settings.PanelKey)) {
                visible = !visible;
            }
        }

        private void OnGUI() {
            if (host == null || settings == null) {
                return;
            }
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            if (!visible) {
                // The hint shows only while a scene is using one of the streams: it must not appear in the body-tracking scenes.
                if (host.HasDemand(VisionStream.Hands) || host.HasDemand(VisionStream.Face) || host.HasDemand(VisionStream.Cutout)) {
                    GUI.Label(new Rect(8f, Screen.height / scale - 22f, 300f, 20f), settings.PanelKey + ": vision control panel");
                }
                GUI.matrix = previous;
                return;
            }
            EnsureStyles();
            window.height = Mathf.Min(660f, Screen.height / scale - 24f);
            window = GUI.Window(WindowId, window, DrawWindow, "Hands, Face and Cutout   (" + settings.PanelKey + " hides)");
            GUI.matrix = previous;
        }

        private void EnsureStyles() {
            if (statusStyle != null) {
                return;
            }
            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.wordWrap = true;
            codeStyle = new GUIStyle(GUI.skin.label);
            codeStyle.wordWrap = true;
            codeStyle.fontSize = 10;
        }

        private void DrawWindow(int id) {
            tab = GUILayout.Toolbar(tab, TabNames);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(window.height - 110f));
            if (tab == 0) {
                DrawStatus();
            } else if (tab == 1) {
                DrawSource();
            } else if (tab == 2) {
                DrawHands();
            } else if (tab == 3) {
                DrawFace();
            } else if (tab == 4) {
                DrawCutout();
            } else {
                DrawReceiver();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply now")) {
                host.ApplyNow();
            }
            if (GUILayout.Button("Reset to start values")) {
                settings.CopyFrom(pristine);
                host.ApplyNow();
            }
            if (GUILayout.Button("Close")) {
                visible = false;
            }
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 22f));
        }

        // ------------------------------------------------------------------ tabs

        private void DrawStatus() {
            DrawStreamStatus(VisionStream.Hands, "HANDS", settings.HandsPort);
            GUILayout.Space(8f);
            DrawStreamStatus(VisionStream.Face, "FACE", settings.FacePort);
            GUILayout.Space(8f);
            DrawStreamStatus(VisionStream.Cutout, "CUTOUT", settings.CutoutPort);
            GUILayout.Space(8f);
            if (!settings.AutoStartSenders) {
                GUILayout.Label("Auto-start is OFF: run python-sidecar~/scripts/run_hands_demo.bat, run_face_demo.bat or run_cutout_demo.bat yourself.", statusStyle);
            }
            int always = (settings.HandsMode == VisionSenderMode.Always ? 1 : 0) + (settings.FaceMode == VisionSenderMode.Always ? 1 : 0)
                + (settings.CutoutMode == VisionSenderMode.Always ? 1 : 0);
            if (always > 1) {
                GUILayout.Label("More than one sender is Always on. One webcam or LAN camera cannot be read by two programs at once, so the later ones will fail to open it. Use Auto, or point each at its own camera.", statusStyle);
            }
        }

        private void DrawStreamStatus(VisionStream stream, string title, int port) {
            VisionSenderMode mode = host.ModeOf(stream);
            GUILayout.Label(title + "   mode: " + mode + "   port " + port);
            GUILayout.Label("sender : " + host.SenderStatus(stream), statusStyle);
            if (mode == VisionSenderMode.Off) {
                return;
            }
            text.Length = 0;
            if (stream == VisionStream.Hands) {
                AppendHandsReceiver(text);
            } else if (stream == VisionStream.Cutout) {
                AppendCutoutReceiver(text);
            } else {
                AppendFaceReceiver(text);
            }
            GUILayout.Label(text.ToString(), statusStyle);
            if (GUILayout.Button("Restart " + title.ToLowerInvariant() + " sender")) {
                host.RestartSender(stream);
            }
            string command;
            if (stream == VisionStream.Hands) {
                command = "hands_udp_sender.py " + VisionSenderArguments.Hands(settings, settings.Source);
            } else if (stream == VisionStream.Cutout) {
                command = "cutout_udp_sender.py " + VisionSenderArguments.Cutout(settings, settings.Source);
            } else {
                command = "face_udp_sender.py " + VisionSenderArguments.Face(settings, settings.Source);
            }
            GUILayout.Label(command, codeStyle);
        }

        private void AppendCutoutReceiver(StringBuilder sb) {
            CutoutReceiver receiver = host.Cutout;
            if (receiver == null) {
                sb.Append("receiver: not listening");
                return;
            }
            sb.Append("stream : ").Append(receiver.State);
            if (receiver.State != StreamState.NoStream) {
                sb.Append("  (last frame ").Append(receiver.AgeSeconds.ToString("0.0")).Append(" s ago)");
            }
            sb.Append("\n").Append(host.CutoutRate.ToString("0.0")).Append(" frames/s   whole ").Append(receiver.Accepted)
              .Append("  abandoned ").Append(receiver.Abandoned).Append("  dropped by Unity ").Append(receiver.Overwritten)
              .Append("  refused ").Append(receiver.Rejected).Append("  restarts ").Append(receiver.SessionChanges);
            CutoutTextures textures = host.CutoutTextures;
            if (textures.HasFrame && receiver.State == StreamState.Live) {
                sb.Append("\nframe ").Append(textures.ColorWidth).Append("x").Append(textures.ColorHeight).Append(" ").Append(textures.ColorEncoding)
                  .Append(", mask ").Append(textures.MaskWidth).Append("x").Append(textures.MaskHeight)
                  .Append("\npeople ").Append(textures.People).Append("  area ").Append((textures.ForegroundFraction * 100f).ToString("0.0")).Append("%")
                  .Append("  sender ").Append(textures.InferMs.ToString("0.0")).Append(" ms (to packed ").Append(textures.SenderLatencyMs.ToString("0")).Append(" ms)")
                  .Append("\nupload ").Append(textures.UploadMilliseconds.ToString("0.00")).Append(" ms");
            }
        }

        private void AppendHandsReceiver(StringBuilder sb) {
            HandsReceiver receiver = host.Hands;
            if (receiver == null) {
                sb.Append("receiver: not listening");
                return;
            }
            sb.Append("stream : ").Append(receiver.State);
            if (receiver.State != StreamState.NoStream) {
                sb.Append("  (last datagram ").Append(receiver.AgeSeconds.ToString("0.0")).Append(" s ago)");
            }
            sb.Append("\n").Append(host.HandsRate.ToString("0.0")).Append(" msg/s   received ").Append(receiver.Received)
              .Append("  accepted ").Append(receiver.Accepted).Append("  rejected ").Append(receiver.Rejected)
              .Append("  out of order ").Append(receiver.OutOfOrder).Append("  restarts ").Append(receiver.SessionChanges);
            HandsMessage message = host.HandsMessage;
            if (message != null && receiver.State == StreamState.Live) {
                sb.Append("\nhands in view: ").Append(message.Count);
                int i = 0;
                while (i < message.Count) {
                    HandData hand = message.Hand(i);
                    sb.Append("\n  #").Append(hand.Id).Append(' ').Append(hand.Side).Append(' ').Append(hand.Gesture)
                      .Append("  pinch ").Append(hand.Pinch.ToString("0.00"));
                    if (hand.HasPosition) {
                        sb.Append("  at ").Append(hand.Position.ToString("0.00"));
                    }
                    i = i + 1;
                }
            }
        }

        private void AppendFaceReceiver(StringBuilder sb) {
            FaceReceiver receiver = host.Face;
            if (receiver == null) {
                sb.Append("receiver: not listening");
                return;
            }
            sb.Append("stream : ").Append(receiver.State);
            if (receiver.State != StreamState.NoStream) {
                sb.Append("  (last datagram ").Append(receiver.AgeSeconds.ToString("0.0")).Append(" s ago)");
            }
            sb.Append("\n").Append(host.FaceRate.ToString("0.0")).Append(" msg/s   received ").Append(receiver.Received)
              .Append("  accepted ").Append(receiver.Accepted).Append("  rejected ").Append(receiver.Rejected)
              .Append("  out of order ").Append(receiver.OutOfOrder).Append("  restarts ").Append(receiver.SessionChanges);
            FaceMessage message = host.FaceMessage;
            if (message != null && receiver.State == StreamState.Live) {
                sb.Append("\nfaces in view: ").Append(message.Count);
                int i = 0;
                while (i < message.Count) {
                    FaceData face = message.Face(i);
                    sb.Append("\n  #").Append(face.Id).Append("  yaw ").Append(face.Yaw.ToString("0")).Append("  pitch ").Append(face.Pitch.ToString("0"))
                      .Append("  roll ").Append(face.Roll.ToString("0"))
                      .Append("  jaw ").Append(face.Blendshape(ArkitBlendshapes.JawOpen).ToString("0.00"))
                      .Append("  smile ").Append(Mathf.Max(face.Blendshape(ArkitBlendshapes.MouthSmileLeft), face.Blendshape(ArkitBlendshapes.MouthSmileRight)).ToString("0.00"));
                    i = i + 1;
                }
            }
        }

        private void DrawSource() {
            settings.AutoStartSenders = GUILayout.Toggle(settings.AutoStartSenders, "Start the senders for me");
            GUILayout.Label("Camera (camera:0, a LAN viewer link, rtsp:// or http:// stream, a video file)");
            settings.Source = GUILayout.TextField(settings.Source);
            GUILayout.Label("LAN camera PIN (masked, not saved)");
            settings.LanCameraPin = GUILayout.PasswordField(settings.LanCameraPin, '*');
            settings.Flip = GUILayout.Toggle(settings.Flip, "Mirror the picture (selfie view; hands and face)");
            settings.RotateDegrees = RotationValue(GUILayout.Toolbar(RotationIndex(settings.RotateDegrees), RotationNames));
            GUILayout.Label("^ rotate clockwise before tracking, degrees");
            settings.HorizontalFovDegrees = Slider("Assumed horizontal FOV", settings.HorizontalFovDegrees, 20f, 140f, "0", " deg");
            settings.FovAcrossLongSide = GUILayout.Toggle(settings.FovAcrossLongSide, "FOV spans the longer side (phones)");
            GUILayout.Label("Webcam resolution");
            settings.CameraSize = Sizes[GUILayout.Toolbar(SizeIndex(settings.CameraSize), SizeNames)];
            settings.CameraFps = Mathf.RoundToInt(Slider("Webcam fps", settings.CameraFps, 5f, 60f, "0", ""));
            settings.LoopFile = GUILayout.Toggle(settings.LoopFile, "Loop a video file");
            GUILayout.Label("Inference provider");
            settings.Provider = (VisionProvider)GUILayout.Toolbar((int)settings.Provider, ProviderNames);
            settings.Smoothing = GUILayout.Toggle(settings.Smoothing, "Smooth the landmarks");
            settings.ShowPreviewWindow = GUILayout.Toggle(settings.ShowPreviewWindow, "Open the sender's own preview window");
        }

        private void DrawHands() {
            GUILayout.Label("Hands sender");
            settings.HandsMode = (VisionSenderMode)GUILayout.Toolbar((int)settings.HandsMode, ModeNames);
            GUILayout.Label("UDP port " + settings.HandsPort + " (set on AppBootstrap)");
            settings.MaxHands = Mathf.RoundToInt(Slider("Most hands", settings.MaxHands, 1f, 4f, "0", ""));
            settings.PalmConfidence = Slider("Palm detector score", settings.PalmConfidence, 0.05f, 0.95f, "0.00", "");
            settings.HandConfidence = Slider("New hand needs", settings.HandConfidence, 0.05f, 0.99f, "0.00", "");
            settings.HandKeepConfidence = Slider("Hand lost below", settings.HandKeepConfidence, 0.05f, 0.99f, "0.00", "");
            settings.HandScale = Slider("Hand size vs average", settings.HandScale, 0.6f, 1.6f, "0.00", "x");
            GUILayout.Label("Left or right label");
            settings.HandSides = (HandSidesMode)GUILayout.Toolbar((int)settings.HandSides, SidesNames);
        }

        private void DrawFace() {
            GUILayout.Label("Face sender");
            settings.FaceMode = (VisionSenderMode)GUILayout.Toolbar((int)settings.FaceMode, ModeNames);
            GUILayout.Label("UDP port " + settings.FacePort + " (set on AppBootstrap)");
            settings.MaxFaces = Mathf.RoundToInt(Slider("Most faces", settings.MaxFaces, 1f, 4f, "0", ""));
            settings.FaceDetectorConfidence = Slider("Detector score", settings.FaceDetectorConfidence, 0.05f, 0.95f, "0.00", "");
            settings.FaceConfidence = Slider("New face needs", settings.FaceConfidence, 0.05f, 0.99f, "0.00", "");
            settings.FaceKeepConfidence = Slider("Face lost below", settings.FaceKeepConfidence, 0.05f, 0.99f, "0.00", "");
            settings.FaceScale = Slider("Face width vs average", settings.FaceScale, 0.6f, 1.6f, "0.00", "x");
            settings.Blendshapes = GUILayout.Toggle(settings.Blendshapes, "Send the 52 blendshapes");
            GUILayout.Label("Landmarks in the datagram");
            settings.FaceLandmarks = (FaceLandmarkMode)GUILayout.Toolbar((int)settings.FaceLandmarks, LandmarkNames);
        }

        private void DrawCutout() {
            GUILayout.Label("Cutout sender: the person cut out of the camera picture");
            settings.CutoutMode = (VisionSenderMode)GUILayout.Toolbar((int)settings.CutoutMode, ModeNames);
            GUILayout.Label("UDP port " + settings.CutoutPort + " (set on AppBootstrap). It reads the Source camera, so a webcam is shared with no other sender.", statusStyle);
            GUILayout.Label("Person detector (a crop per person)");
            settings.CutoutRoi = (CutoutRoiMode)GUILayout.Toolbar((int)settings.CutoutRoi, RoiNames);
            settings.CutoutMaxPeople = Mathf.RoundToInt(Slider("Most people", settings.CutoutMaxPeople, 1f, 8f, "0", ""));
            settings.CutoutDetectorConfidence = Slider("Detector score", settings.CutoutDetectorConfidence, 0.1f, 0.9f, "0.00", "");
            settings.CutoutWholePictureFallback = GUILayout.Toggle(settings.CutoutWholePictureFallback, "Nobody found: cut out the whole picture");
            settings.CutoutIsolatePeople = GUILayout.Toggle(settings.CutoutIsolatePeople, "Keep people apart (paint each one's neighbours out)");
            GUILayout.Label("The mask");
            settings.CutoutMaskLongSide = Mathf.RoundToInt(Slider("Mask long side (0 = picture)", settings.CutoutMaskLongSide, 0f, 1280f, "0", " px") / 10f) * 10;
            settings.CutoutEdgeLow = Slider("Edge: background below", settings.CutoutEdgeLow, 0.05f, 0.9f, "0.00", "");
            settings.CutoutEdgeHigh = Slider("Edge: person above", settings.CutoutEdgeHigh, 0.1f, 0.95f, "0.00", "");
            settings.CutoutGuided = GUILayout.Toggle(settings.CutoutGuided, "Snap the edge to the picture (guided filter)");
            settings.CutoutCleanup = GUILayout.Toggle(settings.CutoutCleanup, "Drop specks, fill pinholes");
            settings.CutoutTemporal = GUILayout.Toggle(settings.CutoutTemporal, "Calm the flicker over frames");
            settings.CutoutHighQuality = GUILayout.Toggle(settings.CutoutHighQuality, "High quality (average with the mirrored picture, +4 ms)");
            GUILayout.Label("What travels to Unity");
            settings.CutoutColor = (CutoutColorMode)GUILayout.Toolbar((int)settings.CutoutColor, ColorNames);
            if (settings.CutoutColor == CutoutColorMode.Jpeg) {
                settings.CutoutJpegQuality = Mathf.RoundToInt(Slider("JPEG quality", settings.CutoutJpegQuality, 30f, 100f, "0", ""));
            }
            settings.CutoutMaskEncoding = (CutoutMaskMode)GUILayout.Toolbar((int)settings.CutoutMaskEncoding, MaskEncodingNames);
            settings.CutoutSendLongSide = Mathf.RoundToInt(Slider("Picture long side (0 = as is)", settings.CutoutSendLongSide, 0f, 3840f, "0", " px") / 10f) * 10;
            settings.CutoutChunkBytes = Mathf.RoundToInt(Slider("Bytes per datagram", settings.CutoutChunkBytes, 256f, 65000f, "0", ""));
            settings.CutoutFlipInSender = GUILayout.Toggle(settings.CutoutFlipInSender, "Mirror in the sender (the view mirrors for free)");
            GUILayout.Label("Camera, rotation, resolution and provider are on the Source tab.", statusStyle);
        }

        private void DrawReceiver() {
            settings.AcceptFromNetwork = GUILayout.Toggle(settings.AcceptFromNetwork, "Listen on the network, not only 127.0.0.1");
            if (settings.AcceptFromNetwork) {
                GUILayout.Label("The wire has no authentication: anyone on the network can send hands, faces or camera pictures to this app.", statusStyle);
            }
            settings.StaleSeconds = Slider("Stale after", settings.StaleSeconds, 0.1f, 5f, "0.0", " s");
            settings.SwapHandSides = GUILayout.Toggle(settings.SwapHandSides, "Swap left and right for the humanoid retargeter");
            GUILayout.Label("Panel key: " + settings.PanelKey + " (set on AppBootstrap)");
        }

        // ------------------------------------------------------------------ controls

        private static float Slider(string label, float value, float min, float max, string format, string unit) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(190f));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(190f));
            GUILayout.Label(next.ToString(format) + unit, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            return next;
        }

        private static int RotationIndex(int degrees) {
            int normalized = VisionSenderArguments.NormalizedRotation(degrees);
            return normalized / 90;
        }

        private static int RotationValue(int index) {
            return Mathf.Clamp(index, 0, 3) * 90;
        }

        private static int SizeIndex(Vector2Int size) {
            int i = 0;
            while (i < Sizes.Length) {
                if (Sizes[i] == size) {
                    return i;
                }
                i = i + 1;
            }
            return 1;
        }
    }
}
