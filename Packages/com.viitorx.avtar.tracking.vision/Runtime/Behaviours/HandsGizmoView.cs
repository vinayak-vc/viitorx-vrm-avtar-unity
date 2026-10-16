using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Draws every hand of the stream as a 21-point skeleton in the scene, positioned from the estimated 3D position: a quick way to SEE that the
    /// shape, the position and the left/right assignment are right. Held hands are dimmed. A hand whose position was refused is drawn
    /// <c>fallbackDistance</c> in front of the anchor, centred, so its shape is still visible.
    ///
    /// The anchor is the transform the camera space is relative to: the Unity camera that stands in for the real one (default: the main camera).
    /// Demo only; nothing else depends on it. NOT verified in the Editor.
    /// </summary>
    [AddComponentMenu("Vision/Hands Gizmo View")]
    public sealed class HandsGizmoView : MonoBehaviour {
        private const int LandmarkCount = HandData.LandmarkCount;

        [SerializeField] private HandsReceiverBehaviour source;
        [Tooltip("Camera space is relative to this transform. Empty = the main camera.")]
        [SerializeField] private Transform anchor;
        [SerializeField] private float fallbackDistance = 0.5f;
        [Tooltip("Scales the hand's own size (not its position). 1 = the sidecar's metres.")]
        [SerializeField] private float shapeScale = 1f;
        [SerializeField] private Color leftColor = new Color(0.25f, 0.63f, 1f, 1f);
        [SerializeField] private Color rightColor = new Color(1f, 0.78f, 0.24f, 1f);
        [SerializeField] private float heldDimming = 0.4f;

        private sealed class HandLines {
            public GameObject owner;
            public Mesh mesh;
            public readonly Vector3[] vertices = new Vector3[LandmarkCount];
            public readonly Color[] colors = new Color[LandmarkCount];
        }

        private HandLines[] views;
        private Material material;
        private bool subscribed;

        /// <summary>Wires the view to a receiver and an anchor when it is created from code.</summary>
        public void Bind(HandsReceiverBehaviour receiver, Transform cameraSpace) {
            if (subscribed && source != null) {
                source.Updated -= Show;
                subscribed = false;
            }
            source = receiver;
            anchor = cameraSpace;
            if (isActiveAndEnabled && source != null) {
                source.Updated += Show;
                subscribed = true;
            }
        }

        private void OnEnable() {
            if (source == null) {
                source = GetComponent<HandsReceiverBehaviour>();
            }
            if (source != null && !subscribed) {
                source.Updated += Show;
                subscribed = true;
            }
        }

        private void OnDisable() {
            if (subscribed && source != null) {
                source.Updated -= Show;
            }
            subscribed = false;
            HideAll();
        }

        private void OnDestroy() {
            if (views != null) {
                int i = 0;
                while (i < views.Length) {
                    if (views[i].owner != null) {
                        Destroy(views[i].owner);
                    }
                    if (views[i].mesh != null) {
                        Destroy(views[i].mesh);
                    }
                    i = i + 1;
                }
            }
            if (material != null) {
                Destroy(material);
            }
        }

        /// <summary>Sets the transform the camera space is relative to (null = the main camera). For a view driven by <see cref="Show"/> instead of a receiver.</summary>
        public void SetAnchor(Transform cameraSpace) {
            anchor = cameraSpace;
        }

        /// <summary>Hides every hand.</summary>
        public void Hide() {
            HideAll();
        }

        /// <summary>Draws the hands of this message. Called by the receiver behaviour's event, or by whoever owns the data (the AppBootstrap vision host).</summary>
        public void Show(HandsMessage message) {
            EnsureViews(message.Capacity);
            Transform space = ResolveAnchor();
            int i = 0;
            while (i < views.Length) {
                HandLines view = views[i];
                if (i >= message.Count) {
                    view.owner.SetActive(false);
                    i = i + 1;
                    continue;
                }
                HandData hand = message.Hand(i);
                Color color = hand.Side == HandSide.Left ? leftColor : rightColor;
                if (hand.State == TrackState.Held) {
                    color = Color.Lerp(color, Color.black, 1f - heldDimming);
                }
                Vector3 centre = hand.HasPosition ? hand.Position : new Vector3(0f, 0f, fallbackDistance);
                int j = 0;
                while (j < LandmarkCount) {
                    Vector3 local = centre + hand.WorldLandmarks[j] * shapeScale;
                    view.vertices[j] = space.TransformPoint(local);
                    view.colors[j] = color;
                    j = j + 1;
                }
                view.mesh.SetVertices(view.vertices);
                view.mesh.SetColors(view.colors);
                view.owner.SetActive(true);
                i = i + 1;
            }
        }

        private Transform ResolveAnchor() {
            if (anchor != null) {
                return anchor;
            }
            Camera main = Camera.main;
            return main != null ? main.transform : transform;
        }

        private void EnsureViews(int capacity) {
            if (views != null && views.Length == capacity) {
                return;
            }
            if (material == null) {
                material = new Material(Shader.Find("Sprites/Default"));
            }
            views = new HandLines[capacity];
            int[] indices = RawHandBoneIndices();
            int i = 0;
            while (i < capacity) {
                HandLines view = new HandLines();
                view.owner = new GameObject("Hand " + i);
                view.owner.hideFlags = HideFlags.DontSave;
                view.mesh = new Mesh();
                view.mesh.name = "HandLines";
                view.mesh.MarkDynamic();
                view.mesh.SetVertices(view.vertices);
                view.mesh.SetColors(view.colors);
                view.mesh.SetIndices(indices, MeshTopology.Lines, 0);
                view.mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
                view.owner.AddComponent<MeshFilter>().sharedMesh = view.mesh;
                view.owner.AddComponent<MeshRenderer>().sharedMaterial = material;
                view.owner.SetActive(false);
                views[i] = view;
                i = i + 1;
            }
        }

        private static int[] RawHandBoneIndices() {
            return VirtualMirror.Core.RawHandFrame.Bones;
        }

        private void HideAll() {
            if (views == null) {
                return;
            }
            int i = 0;
            while (i < views.Length) {
                if (views[i].owner != null) {
                    views[i].owner.SetActive(false);
                }
                i = i + 1;
            }
        }
    }
}
