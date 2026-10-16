using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Draws the first face of the stream as a landmark point cloud and three head axes, placed from the estimated position and the picture's own
    /// geometry (each landmark's pixel through the sender's focal length at the face's distance): a quick way to SEE that the landmarks, the position
    /// and the head pose agree with the real face. Axes: red = the head's right, green = up, blue = the way the face looks.
    ///
    /// The anchor is the transform the camera space is relative to: the Unity camera that stands in for the real one (default: the main camera).
    /// A face whose position was refused is drawn <c>fallbackDistance</c> away. Demo only; nothing else depends on it. NOT verified in the Editor.
    /// </summary>
    [AddComponentMenu("Vision/Face Gizmo View")]
    public sealed class FaceGizmoView : MonoBehaviour {
        private const int AxisVertexCount = 6;

        [SerializeField] private FaceReceiverBehaviour source;
        [Tooltip("Camera space is relative to this transform. Empty = the main camera.")]
        [SerializeField] private Transform anchor;
        [SerializeField] private float fallbackDistance = 0.6f;
        [SerializeField] private float axisLength = 0.06f;
        [SerializeField] private Color pointColor = new Color(0.35f, 0.9f, 0.35f, 1f);
        [SerializeField] private Color irisColor = new Color(1f, 0.86f, 0.24f, 1f);

        private GameObject pointsOwner;
        private GameObject axesOwner;
        private Mesh pointsMesh;
        private Mesh axesMesh;
        private Material material;
        private Vector3[] pointVertices;
        private Color[] pointColors;
        private int[] pointIndices;
        private readonly Vector3[] axisVertices = new Vector3[AxisVertexCount];
        private readonly Color[] axisColors = new Color[AxisVertexCount];
        private bool subscribed;

        /// <summary>Wires the view to a receiver and an anchor when it is created from code.</summary>
        public void Bind(FaceReceiverBehaviour receiver, Transform cameraSpace) {
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
                source = GetComponent<FaceReceiverBehaviour>();
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
            SetVisible(false);
        }

        private void OnDestroy() {
            if (pointsOwner != null) {
                Destroy(pointsOwner);
            }
            if (axesOwner != null) {
                Destroy(axesOwner);
            }
            if (pointsMesh != null) {
                Destroy(pointsMesh);
            }
            if (axesMesh != null) {
                Destroy(axesMesh);
            }
            if (material != null) {
                Destroy(material);
            }
        }

        /// <summary>Sets the transform the camera space is relative to (null = the main camera). For a view driven by <see cref="Show"/> instead of a receiver.</summary>
        public void SetAnchor(Transform cameraSpace) {
            anchor = cameraSpace;
        }

        /// <summary>Hides the point cloud and the axes.</summary>
        public void Hide() {
            SetVisible(false);
        }

        /// <summary>Draws the first face of this message. Called by the receiver behaviour's event, or by whoever owns the data (the AppBootstrap vision host).</summary>
        public void Show(FaceMessage message) {
            if (message.Count < 1 || message.Face(0).LandmarkCount == 0 || message.FocalPixels <= 0f) {
                SetVisible(false);
                return;
            }
            FaceData face = message.Face(0);
            EnsureObjects();
            Transform space = ResolveAnchor();
            float depth = face.HasPosition ? face.Position.z : fallbackDistance;
            float scale = depth / message.FocalPixels;
            float halfWidth = message.ImageWidth * 0.5f;
            float halfHeight = message.ImageHeight * 0.5f;
            int count = face.LandmarkCount;
            int i = 0;
            while (i < count) {
                Vector3 lm = face.ImageLandmarks[i];
                float x = (lm.x * message.ImageWidth - halfWidth) * scale;
                float y = -(lm.y * message.ImageHeight - halfHeight) * scale;
                float z = depth + lm.z * message.ImageWidth * scale;
                pointVertices[i] = space.TransformPoint(new Vector3(x, y, z));
                i = i + 1;
            }
            while (i < pointVertices.Length) {
                pointVertices[i] = pointVertices[0];
                i = i + 1;
            }
            pointsMesh.SetVertices(pointVertices);
            pointsMesh.SetColors(pointColors);
            pointsOwner.SetActive(true);

            if (face.PoseValid && count > 1) {
                Vector3 origin = pointVertices[1];
                Quaternion q = face.HeadRotation;
                SetAxis(0, origin, space.TransformDirection(VisionMath.Rotate(q, Vector3.right)), Color.red);
                SetAxis(1, origin, space.TransformDirection(VisionMath.Rotate(q, Vector3.up)), Color.green);
                SetAxis(2, origin, space.TransformDirection(VisionMath.Rotate(q, new Vector3(0f, 0f, -1f))), Color.blue);
                axesMesh.SetVertices(axisVertices);
                axesMesh.SetColors(axisColors);
                axesOwner.SetActive(true);
            } else {
                axesOwner.SetActive(false);
            }
        }

        private void SetAxis(int index, Vector3 origin, Vector3 direction, Color color) {
            axisVertices[index * 2] = origin;
            axisVertices[index * 2 + 1] = origin + direction * axisLength;
            axisColors[index * 2] = color;
            axisColors[index * 2 + 1] = color;
        }

        private Transform ResolveAnchor() {
            if (anchor != null) {
                return anchor;
            }
            Camera main = Camera.main;
            return main != null ? main.transform : transform;
        }

        private void EnsureObjects() {
            if (pointsOwner != null) {
                return;
            }
            material = new Material(Shader.Find("Sprites/Default"));
            pointVertices = new Vector3[FaceData.LandmarkCapacity];
            pointColors = new Color[FaceData.LandmarkCapacity];
            pointIndices = new int[FaceData.LandmarkCapacity];
            int i = 0;
            while (i < FaceData.LandmarkCapacity) {
                pointIndices[i] = i;
                pointColors[i] = i >= FaceData.MeshLandmarkCount ? irisColor : pointColor;
                i = i + 1;
            }
            pointsMesh = new Mesh();
            pointsMesh.name = "FacePoints";
            pointsMesh.MarkDynamic();
            pointsMesh.SetVertices(pointVertices);
            pointsMesh.SetColors(pointColors);
            pointsMesh.SetIndices(pointIndices, MeshTopology.Points, 0);
            pointsMesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            pointsOwner = CreateRenderer("Face Points", pointsMesh);

            axesMesh = new Mesh();
            axesMesh.name = "HeadAxes";
            axesMesh.MarkDynamic();
            axesMesh.SetVertices(axisVertices);
            axesMesh.SetColors(axisColors);
            axesMesh.SetIndices(new int[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Lines, 0);
            axesMesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            axesOwner = CreateRenderer("Head Axes", axesMesh);
        }

        private GameObject CreateRenderer(string objectName, Mesh mesh) {
            GameObject owner = new GameObject(objectName);
            owner.hideFlags = HideFlags.DontSave;
            owner.AddComponent<MeshFilter>().sharedMesh = mesh;
            owner.AddComponent<MeshRenderer>().sharedMaterial = material;
            owner.SetActive(false);
            return owner;
        }

        private void SetVisible(bool visible) {
            if (pointsOwner != null) {
                pointsOwner.SetActive(visible);
            }
            if (axesOwner != null) {
                axesOwner.SetActive(visible);
            }
        }
    }
}
