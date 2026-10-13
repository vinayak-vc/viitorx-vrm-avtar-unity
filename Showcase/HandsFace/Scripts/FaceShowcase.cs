using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace HandsFaceShowcase {
    /// <summary>
    /// The face sample scene: a face and nothing else in the camera's picture. A cartoon face (<see cref="FaceAvatar"/>) follows the head pose and
    /// all 52 blendshapes, the strongest blendshapes are listed as bars, and L shows the 478 landmarks the network found. Everything else
    /// (camera, options, the sender itself) is in the AppBootstrap "Hands and Face" settings and the F6 control panel.
    ///
    /// The head pose and the position are the sidecar's ESTIMATES (geometry from four landmarks, an assumed field of view and eye width). The
    /// avatar is shown at a fixed distance and follows only where the head is in the picture, so depth noise does not make it breathe. NOT TESTED in
    /// the Editor or against a live camera: press M if the eyes turn out to be the wrong way round on your camera.
    /// </summary>
    [AddComponentMenu("Hands and Face Showcase/Face Showcase")]
    public sealed class FaceShowcase : VisionShowcaseBase {
        private const int TopCount = 12;

        [SerializeField] private Camera showcaseCamera;
        [Tooltip("How far in front of the camera the avatar is shown, metres. Fixed on purpose: the sidecar's depth is an estimate.")]
        [SerializeField] private float displayDistance = 1.1f;
        [Tooltip("Move the avatar with where the head is in the picture.")]
        [SerializeField] private bool followPosition = true;
        [Tooltip("Mirror behaviour: the subject's LEFT eye is the one on the viewer's left. Off: the left eye is on the viewer's right, as in a photo of someone else.")]
        [SerializeField] private bool reflection = true;
        [SerializeField] private bool showLandmarks = false;

        private FaceAvatar avatar;
        private FaceGizmoView landmarks;
        private readonly int[] topIndex = new int[TopCount];
        private readonly float[] topValue = new float[TopCount];
        private int topCount;
        private float yaw;
        private float pitch;
        private float roll;

        protected override VisionStream Stream {
            get {
                return VisionStream.Face;
            }
        }

        protected override void OnBound(VisionHost visionHost) {
            avatar = new FaceAvatar(transform);
            avatar.SetVisible(false);
            GameObject landmarkObject = new GameObject("Face Landmarks");
            landmarkObject.transform.SetParent(transform, false);
            landmarks = landmarkObject.AddComponent<FaceGizmoView>();
            visionHost.FaceUpdated += OnFace;
        }

        protected override void OnUnbound(VisionHost visionHost) {
            visionHost.FaceUpdated -= OnFace;
            if (avatar != null) {
                avatar.Destroy();
                avatar = null;
            }
            if (landmarks != null) {
                Destroy(landmarks.gameObject);
                landmarks = null;
            }
        }

        protected override void Update() {
            base.Update();
            if (Input.GetKeyDown(KeyCode.L)) {
                showLandmarks = !showLandmarks;
                if (!showLandmarks && landmarks != null) {
                    landmarks.Hide();
                }
            }
            if (Input.GetKeyDown(KeyCode.M)) {
                reflection = !reflection;
            }
            if (Input.GetKeyDown(KeyCode.F)) {
                followPosition = !followPosition;
            }
        }

        private void OnFace(FaceMessage message) {
            Camera cam = ResolveCamera(showcaseCamera);
            if (cam == null || avatar == null) {
                return;
            }
            if (message.Count == 0) {
                avatar.SetVisible(false);
                if (landmarks != null) {
                    landmarks.Hide();
                }
                topCount = 0;
                return;
            }
            FaceData face = message.Face(0);
            Vector3 local = new Vector3(0f, 0f, displayDistance);
            if (followPosition && face.HasPosition && face.Position.z > 0.05f) {
                float z = face.Position.z;
                local.x = face.Position.x / z * displayDistance;
                local.y = face.Position.y / z * displayDistance;
            }
            avatar.SetVisible(true);
            avatar.Root.position = cam.transform.TransformPoint(local);
            avatar.Root.rotation = cam.transform.rotation * face.HeadRotation;
            avatar.Apply(face, reflection);
            yaw = face.Yaw;
            pitch = face.Pitch;
            roll = face.Roll;
            CollectTop(face);
            if (landmarks != null && showLandmarks) {
                landmarks.SetAnchor(cam.transform);
                landmarks.Show(message);
            }
        }

        /// <summary>The strongest blendshapes of this face, strongest first, without allocating: a small insertion into a fixed list.</summary>
        private void CollectTop(FaceData face) {
            topCount = 0;
            int i = 1;
            while (i < ArkitBlendshapes.Count) {
                float value = face.Blendshape(i);
                if (value >= 0.05f && (topCount < TopCount || value > topValue[topCount - 1])) {
                    int at = topCount < TopCount ? topCount : TopCount - 1;
                    while (at > 0 && topValue[at - 1] < value) {
                        topValue[at] = topValue[at - 1];
                        topIndex[at] = topIndex[at - 1];
                        at = at - 1;
                    }
                    topValue[at] = value;
                    topIndex[at] = i;
                    if (topCount < TopCount) {
                        topCount = topCount + 1;
                    }
                }
                i = i + 1;
            }
        }

        // ------------------------------------------------------------------ HUD

        private void OnGUI() {
            VisionHost host = Host;
            if (host == null) {
                GUI.Label(new Rect(12f, 12f, 500f, 24f), "Waiting for AppBootstrap...");
                return;
            }
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            StreamState state = host.StateOf(VisionStream.Face);
            if (state != StreamState.Live) {
                ShowcaseDraw.Box(new Rect(0f, 0f, width, 56f), new Color(0.35f, 0.1f, 0.1f, 0.85f));
                GUI.Label(new Rect(12f, 6f, width - 24f, 22f), state == StreamState.Stale ? "Face stream went quiet" : "Waiting for the face sender");
                GUI.Label(new Rect(12f, 28f, width - 24f, 22f), host.SenderStatus(VisionStream.Face) + "   (" + host.Settings.PanelKey + ": control panel)");
            } else {
                DrawFacePanel(host);
            }
            GUI.Label(new Rect(12f, height - 26f, 900f, 22f), "L  landmarks     M  swap eyes (" + (reflection ? "reflection" : "photo") + ")     F  follow position     "
                    + host.Settings.PanelKey + "  control panel");
            GUI.matrix = previous;
        }

        private void DrawFacePanel(VisionHost host) {
            FaceMessage message = host.FaceMessage;
            if (message == null) {
                return;
            }
            if (message.Count == 0) {
                GUI.Label(new Rect(12f, 70f, 400f, 22f), "no face in view");
                return;
            }
            float y = 70f;
            ShowcaseDraw.Box(new Rect(10f, y, 300f, 80f + topCount * 20f), new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(22f, y + 4f, 280f, 22f), "head   yaw " + yaw.ToString("0") + "   pitch " + pitch.ToString("0") + "   roll " + roll.ToString("0"));
            GUI.Label(new Rect(22f, y + 24f, 280f, 22f), Tags(message.Face(0)));
            GUI.Label(new Rect(22f, y + 46f, 280f, 22f), "strongest of 52 blendshapes");
            int i = 0;
            while (i < topCount) {
                float row = y + 70f + i * 20f;
                GUI.Label(new Rect(22f, row, 150f, 18f), ArkitBlendshapes.NameOf(topIndex[i]));
                ShowcaseDraw.Bar(new Rect(176f, row + 5f, 124f, 8f), topValue[i], new Color(0.4f, 0.9f, 0.5f, 1f));
                i = i + 1;
            }
        }

        private static string Tags(FaceData face) {
            string tags = string.Empty;
            if (Mathf.Max(face.Blendshape(ArkitBlendshapes.MouthSmileLeft), face.Blendshape(ArkitBlendshapes.MouthSmileRight)) > 0.5f) {
                tags = tags + "SMILE  ";
            }
            if (face.Blendshape(ArkitBlendshapes.JawOpen) > 0.5f) {
                tags = tags + "MOUTH OPEN  ";
            }
            if (face.Blendshape(ArkitBlendshapes.EyeBlinkLeft) > 0.6f && face.Blendshape(ArkitBlendshapes.EyeBlinkRight) > 0.6f) {
                tags = tags + "BLINK  ";
            }
            if (face.Blendshape(ArkitBlendshapes.BrowInnerUp) > 0.5f) {
                tags = tags + "BROWS UP  ";
            }
            return tags.Length == 0 ? "neutral" : tags;
        }
    }
}
