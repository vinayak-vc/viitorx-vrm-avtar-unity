using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace HandsFaceShowcase {
    /// <summary>
    /// The hands sample scene: hands and nothing else in the camera's picture. Each hand is a 21-point skeleton at its estimated place in front of the
    /// camera, with fingertip trails, a gesture label and the five finger curls. Three orbs float in front of the camera: pinch one (thumb and index
    /// together) over it to pick it up, open your hand to put it down. Everything else (camera, options, the sender itself) is in the AppBootstrap
    /// "Hands and Face" settings and the F6 control panel.
    ///
    /// Positions are the sidecar's ESTIMATES (an assumed field of view and an average hand size), so the orbs are caught on screen position, not on a
    /// measured distance. NOT TESTED in the Editor or against a live camera.
    /// </summary>
    [AddComponentMenu("Hands and Face Showcase/Hands Showcase")]
    public sealed class HandsShowcase : VisionShowcaseBase {
        private const int FingerCount = 5;
        private const int WristIndex = 0;
        private const int ThumbTip = 4;
        private const int IndexTip = 8;
        private static readonly int[] Tips = { 4, 8, 12, 16, 20 };
        private static readonly string[] FingerLetters = { "T", "I", "M", "R", "P" };

        [SerializeField] private Camera showcaseCamera;
        [Tooltip("How many orbs float in front of the camera to be picked up.")]
        [SerializeField] private int orbCount = 3;
        [Tooltip("Pinch strength (0..1) at which a hand over an orb picks it up.")]
        [SerializeField] private float grabPinch = 0.7f;
        [Tooltip("Pinch strength below which a held orb is put down.")]
        [SerializeField] private float releasePinch = 0.4f;
        [Tooltip("How close, in screen heights, the pinch has to be to an orb to pick it up.")]
        [SerializeField] private float grabRadius = 0.09f;
        [SerializeField] private float orbRadius = 0.035f;
        [SerializeField] private float orbDistance = 0.5f;
        [SerializeField] private Color leftColor = new Color(0.25f, 0.63f, 1f, 1f);
        [SerializeField] private Color rightColor = new Color(1f, 0.78f, 0.24f, 1f);
        [SerializeField] private bool showTrails = true;
        [SerializeField] private bool showSkeleton = true;

        private sealed class Orb {
            public GameObject body;
            public Vector3 local;
            public Vector3 home;
            public int holder = -1;
            public bool hovered;
        }

        private HandsGizmoView skeleton;
        private Orb[] orbs;
        private TrailRenderer[] trails;
        private int[] trailHandIds;
        private int trailSlots;

        protected override VisionStream Stream {
            get {
                return VisionStream.Hands;
            }
        }

        protected override void OnBound(VisionHost visionHost) {
            GameObject skeletonObject = new GameObject("Hand Skeleton");
            skeletonObject.transform.SetParent(transform, false);
            skeleton = skeletonObject.AddComponent<HandsGizmoView>();
            SpawnOrbs();
            visionHost.HandsUpdated += OnHands;
        }

        protected override void OnUnbound(VisionHost visionHost) {
            visionHost.HandsUpdated -= OnHands;
            if (skeleton != null) {
                Destroy(skeleton.gameObject);
                skeleton = null;
            }
            DestroyOrbs();
            DestroyTrails();
        }

        protected override void Update() {
            base.Update();
            if (Input.GetKeyDown(KeyCode.R)) {
                ResetOrbs();
            }
            if (Input.GetKeyDown(KeyCode.T)) {
                showTrails = !showTrails;
            }
            if (Input.GetKeyDown(KeyCode.H)) {
                showSkeleton = !showSkeleton;
                if (!showSkeleton && skeleton != null) {
                    skeleton.Hide();
                }
            }
        }

        private void LateUpdate() {
            Camera cam = ResolveCamera(showcaseCamera);
            if (cam == null || orbs == null) {
                return;
            }
            int i = 0;
            while (i < orbs.Length) {
                Orb orb = orbs[i];
                orb.body.transform.position = cam.transform.TransformPoint(orb.local);
                float target = orbRadius * 2f * (orb.holder >= 0 ? 1.5f : (orb.hovered ? 1.2f : 1f));
                float size = Mathf.Lerp(orb.body.transform.localScale.x, target, 12f * Time.unscaledDeltaTime);
                orb.body.transform.localScale = new Vector3(size, size, size);
                Color color = orb.holder >= 0 ? new Color(1f, 0.35f, 0.85f, 1f) : (orb.hovered ? new Color(1f, 0.95f, 0.4f, 1f) : new Color(0.3f, 0.95f, 0.95f, 1f));
                ShowcaseDraw.SetColor(orb.body, color);
                i = i + 1;
            }
        }

        // ------------------------------------------------------------------ data

        private void OnHands(HandsMessage message) {
            Camera cam = ResolveCamera(showcaseCamera);
            if (cam == null) {
                return;
            }
            if (skeleton != null) {
                if (showSkeleton) {
                    skeleton.SetAnchor(cam.transform);
                    skeleton.Show(message);
                }
            }
            UpdateTrails(message, cam);
            UpdateOrbs(message, cam);
        }

        private void UpdateTrails(HandsMessage message, Camera cam) {
            EnsureTrails(message.Capacity);
            int slot = 0;
            while (slot < trailSlots) {
                bool present = slot < message.Count && showTrails;
                HandData hand = present ? message.Hand(slot) : null;
                if (hand != null && !hand.HasPosition) {
                    present = false;
                }
                int finger = 0;
                while (finger < FingerCount) {
                    TrailRenderer trail = trails[slot * FingerCount + finger];
                    if (!present) {
                        trail.emitting = false;
                        finger = finger + 1;
                        continue;
                    }
                    Vector3 tip = cam.transform.TransformPoint(hand.Position + hand.WorldLandmarks[Tips[finger]]);
                    if (trailHandIds[slot] != hand.Id) {
                        trail.Clear();
                    }
                    trail.transform.position = tip;
                    Color color = hand.Side == HandSide.Left ? leftColor : rightColor;
                    trail.startColor = color;
                    trail.endColor = new Color(color.r, color.g, color.b, 0f);
                    trail.emitting = true;
                    finger = finger + 1;
                }
                trailHandIds[slot] = present ? hand.Id : -1;
                slot = slot + 1;
            }
        }

        private void UpdateOrbs(HandsMessage message, Camera cam) {
            if (orbs == null) {
                return;
            }
            int i = 0;
            while (i < orbs.Length) {
                Orb orb = orbs[i];
                orb.hovered = false;
                if (orb.holder >= 0 && !HasHand(message, orb.holder)) {
                    orb.holder = -1;
                }
                Vector3 orbViewport = cam.WorldToViewportPoint(cam.transform.TransformPoint(orb.local));
                int h = 0;
                while (h < message.Count) {
                    HandData hand = message.Hand(h);
                    h = h + 1;
                    if (!hand.HasPosition || hand.State != TrackState.Tracked) {
                        continue;
                    }
                    Vector3 pinchLocal = hand.Position + 0.5f * (hand.WorldLandmarks[ThumbTip] + hand.WorldLandmarks[IndexTip]);
                    if (orb.holder == hand.Id) {
                        if (hand.Pinch < releasePinch) {
                            orb.holder = -1;
                        } else {
                            orb.local = pinchLocal;
                        }
                        continue;
                    }
                    if (orb.holder >= 0) {
                        continue;
                    }
                    Vector3 pinchViewport = cam.WorldToViewportPoint(cam.transform.TransformPoint(pinchLocal));
                    float dx = (pinchViewport.x - orbViewport.x) * cam.aspect;
                    float dy = pinchViewport.y - orbViewport.y;
                    bool near = Mathf.Sqrt(dx * dx + dy * dy) < grabRadius;
                    if (near) {
                        orb.hovered = true;
                        if (hand.Pinch >= grabPinch) {
                            orb.holder = hand.Id;
                            orb.local = pinchLocal;
                        }
                    }
                }
                i = i + 1;
            }
        }

        private static bool HasHand(HandsMessage message, int id) {
            int i = 0;
            while (i < message.Count) {
                if (message.Hand(i).Id == id) {
                    return true;
                }
                i = i + 1;
            }
            return false;
        }

        // ------------------------------------------------------------------ objects

        private void SpawnOrbs() {
            DestroyOrbs();
            int count = Mathf.Clamp(orbCount, 1, 6);
            orbs = new Orb[count];
            int i = 0;
            while (i < count) {
                Orb orb = new Orb();
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                orb.home = new Vector3(Mathf.Lerp(-0.16f, 0.16f, t), 0.05f * Mathf.Sin(t * Mathf.PI), orbDistance);
                orb.local = orb.home;
                orb.body = ShowcaseDraw.Primitive(PrimitiveType.Sphere, "Orb " + i, transform, new Color(0.3f, 0.95f, 0.95f, 1f));
                orb.body.transform.localScale = Vector3.one * orbRadius * 2f;
                orbs[i] = orb;
                i = i + 1;
            }
        }

        private void ResetOrbs() {
            if (orbs == null) {
                return;
            }
            int i = 0;
            while (i < orbs.Length) {
                orbs[i].local = orbs[i].home;
                orbs[i].holder = -1;
                i = i + 1;
            }
        }

        private void DestroyOrbs() {
            if (orbs == null) {
                return;
            }
            int i = 0;
            while (i < orbs.Length) {
                if (orbs[i].body != null) {
                    Destroy(orbs[i].body);
                }
                i = i + 1;
            }
            orbs = null;
        }

        private void EnsureTrails(int slots) {
            if (trails != null && trailSlots == slots) {
                return;
            }
            DestroyTrails();
            trailSlots = slots;
            trails = new TrailRenderer[slots * FingerCount];
            trailHandIds = new int[slots];
            int s = 0;
            while (s < slots) {
                trailHandIds[s] = -1;
                s = s + 1;
            }
            int i = 0;
            while (i < trails.Length) {
                GameObject go = new GameObject("Trail " + i);
                go.transform.SetParent(transform, false);
                TrailRenderer trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.55f;
                trail.startWidth = 0.012f;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.004f;
                trail.sharedMaterial = ShowcaseDraw.Unlit(Color.white);
                trail.emitting = false;
                trails[i] = trail;
                i = i + 1;
            }
        }

        private void DestroyTrails() {
            if (trails == null) {
                return;
            }
            int i = 0;
            while (i < trails.Length) {
                if (trails[i] != null) {
                    Destroy(trails[i].gameObject);
                }
                i = i + 1;
            }
            trails = null;
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
            float height = Screen.height / scale;
            DrawBanner(host);
            DrawHandPanels(host);
            DrawHandLabels(host, scale);
            GUI.Label(new Rect(12f, height - 26f, 700f, 22f), "R  put the orbs back     T  trails     H  skeleton     " + host.Settings.PanelKey + "  control panel");
            GUI.matrix = previous;
        }

        private void DrawBanner(VisionHost host) {
            StreamState state = host.StateOf(VisionStream.Hands);
            if (state == StreamState.Live) {
                return;
            }
            float width = Screen.width / Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            ShowcaseDraw.Box(new Rect(0f, 0f, width, 56f), new Color(0.35f, 0.1f, 0.1f, 0.85f));
            GUI.Label(new Rect(12f, 6f, width - 24f, 22f), state == StreamState.Stale ? "Hands stream went quiet" : "Waiting for the hands sender");
            GUI.Label(new Rect(12f, 28f, width - 24f, 22f), host.SenderStatus(VisionStream.Hands) + "   (" + host.Settings.PanelKey + ": control panel)");
        }

        private void DrawHandPanels(VisionHost host) {
            HandsMessage message = host.HandsMessage;
            if (message == null || host.StateOf(VisionStream.Hands) != StreamState.Live) {
                return;
            }
            float y = 70f;
            int i = 0;
            while (i < message.Count) {
                HandData hand = message.Hand(i);
                Color accent = hand.Side == HandSide.Left ? leftColor : rightColor;
                ShowcaseDraw.Box(new Rect(10f, y, 300f, 120f), new Color(0f, 0f, 0f, 0.45f));
                ShowcaseDraw.Box(new Rect(10f, y, 4f, 120f), accent);
                GUI.Label(new Rect(22f, y + 4f, 280f, 22f), hand.Side + " hand #" + hand.Id + (hand.State == TrackState.Held ? "  (held)" : string.Empty));
                GUI.Label(new Rect(22f, y + 24f, 280f, 22f), GestureName(hand.Gesture));
                GUI.Label(new Rect(22f, y + 46f, 60f, 18f), "pinch");
                ShowcaseDraw.Bar(new Rect(80f, y + 51f, 220f, 8f), hand.Pinch, accent);
                int f = 0;
                while (f < FingerCount) {
                    float x = 22f + f * 56f;
                    GUI.Label(new Rect(x, y + 66f, 20f, 18f), FingerLetters[f]);
                    ShowcaseDraw.Bar(new Rect(x + 14f, y + 71f, 34f, 8f), hand.FingerCurls[f], accent);
                    f = f + 1;
                }
                GUI.Label(new Rect(22f, y + 88f, 280f, 22f), hand.HasPosition ? "at " + hand.Position.ToString("0.00") + " m (estimate)" : "no position: " + hand.PositionRefusal);
                y = y + 128f;
                i = i + 1;
            }
            if (message.Count == 0) {
                GUI.Label(new Rect(12f, 70f, 400f, 22f), "no hand in view");
            }
        }

        private void DrawHandLabels(VisionHost host, float scale) {
            HandsMessage message = host.HandsMessage;
            Camera cam = ResolveCamera(showcaseCamera);
            if (message == null || cam == null || host.StateOf(VisionStream.Hands) != StreamState.Live) {
                return;
            }
            int i = 0;
            while (i < message.Count) {
                HandData hand = message.Hand(i);
                i = i + 1;
                if (!hand.HasPosition) {
                    continue;
                }
                Vector3 world = cam.transform.TransformPoint(hand.Position + hand.WorldLandmarks[WristIndex]);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) {
                    continue;
                }
                GUI.Label(new Rect(screen.x / scale - 60f, (Screen.height - screen.y) / scale + 10f, 140f, 22f), GestureName(hand.Gesture));
            }
        }

        private static string GestureName(HandGesture gesture) {
            switch (gesture) {
                case HandGesture.Open:
                    return "OPEN PALM";
                case HandGesture.Fist:
                    return "FIST";
                case HandGesture.Point:
                    return "POINT";
                case HandGesture.Victory:
                    return "VICTORY";
                case HandGesture.Rock:
                    return "ROCK";
                case HandGesture.ThumbsUp:
                    return "THUMBS UP";
                case HandGesture.Ok:
                    return "OK";
                default:
                    return "-";
            }
        }
    }
}
