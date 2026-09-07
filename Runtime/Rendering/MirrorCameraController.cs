using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace VirtualMirror.Rendering {
    /// <summary>
    /// Frames the loaded avatar in the mirror view by positioning the target camera along -Z to fit
    /// the avatar's renderer bounds (VRM avatars face -Z). Called whenever the avatar changes, so it
    /// handles avatars of any height (e.g. tall VRoid vs short chibi).
    /// </summary>
    public sealed class MirrorCameraController : MonoBehaviour {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float paddingFactor = 1.3f;
        [SerializeField] private float lookHeightFraction = 0.55f;
        [SerializeField] Transform avatarRoot;

        [SerializeField]
        private Vector3 avatarPositionOffset = Vector3.zero;

        [SerializeField]
        private bool drawPredictedAvatar = true;

        public void Frame(Transform avatarRoot) {
            if (avatarRoot == null) {
                return;
            }
            this.avatarRoot = avatarRoot;
            Camera activeCamera = targetCamera != null ? targetCamera : Camera.main;
            if (activeCamera == null) {
                return;
            }
            Bounds bounds;
            if (!TryComputeBounds(avatarRoot, out bounds)) {
                return;
            }
            Vector3 lookTarget = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * lookHeightFraction, bounds.center.z);
            float verticalFovRad = activeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distanceForHeight = bounds.extents.y / Mathf.Tan(verticalFovRad);
            float horizontalFovRad = Mathf.Atan(Mathf.Tan(verticalFovRad) * activeCamera.aspect);
            float distanceForWidth = bounds.extents.x / Mathf.Tan(horizontalFovRad);
            float distance = Mathf.Max(distanceForHeight, distanceForWidth) * paddingFactor + bounds.extents.z;
            // Guard the degenerate zero-bounds avatar: distance 0 puts the camera inside the model and
            // gives LookRotation a ~zero direction (Unity warns, yields identity). Clamp to a minimum.
            distance = Mathf.Max(distance, 0.5f);
            Vector3 cameraPosition = new Vector3(lookTarget.x, lookTarget.y, bounds.center.z - distance);
            Vector3 lookDirection = lookTarget - cameraPosition;
            if (lookDirection.sqrMagnitude < 1e-6f) {
                return;
            }
            activeCamera.transform.position = cameraPosition;
            activeCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        private static bool TryComputeBounds(Transform root, out Bounds bounds) {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) {
                bounds = new Bounds(root.position, Vector3.one);
                return false;
            }
            bounds = renderers[0].bounds;
            int index = 1;
            while (index < renderers.Length) {
                bounds.Encapsulate(renderers[index].bounds);
                index = index + 1;
            }
            return true;
        }


        private void OnDrawGizmosSelected() {
            if (!drawPredictedAvatar) {
                return;
            }

            if (targetCamera == null) {
                return;
            }

            Transform avatarRoot = this.avatarRoot;

            if (avatarRoot == null) {
                return;
            }

            Bounds bounds;
            if (!TryComputeBounds(avatarRoot, out bounds)) {
                return;
            }

            Camera camera = targetCamera;

            Vector3 lookTarget = new Vector3(
                bounds.center.x,
                bounds.min.y + bounds.size.y * lookHeightFraction,
                bounds.center.z);

            float verticalFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distanceForHeight = bounds.extents.y / Mathf.Tan(verticalFovRad);

            float horizontalFovRad =
                Mathf.Atan(Mathf.Tan(verticalFovRad) * camera.aspect);

            float distanceForWidth =
                bounds.extents.x / Mathf.Tan(horizontalFovRad);

            float distance =
                Mathf.Max(distanceForHeight, distanceForWidth) *
                paddingFactor +
                bounds.extents.z;

            distance = Mathf.Max(distance, 0.5f);

            Vector3 desiredCameraPosition = new Vector3(
                lookTarget.x,
                lookTarget.y,
                bounds.center.z - distance);

            // Where the avatar would move so the CURRENT camera matches the framing.
            Vector3 predictedAvatarPosition =
                avatarRoot.position +
                (camera.transform.position - desiredCameraPosition) +
                avatarPositionOffset;

            // ---------------------------------------------------------------------
            // Current Avatar
            // ---------------------------------------------------------------------

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(avatarRoot.position, 0.05f);

            // ---------------------------------------------------------------------
            // Predicted Avatar
            // ---------------------------------------------------------------------

            Vector3 delta = predictedAvatarPosition - avatarRoot.position;

            Bounds predictedBounds = bounds;
            predictedBounds.center += delta;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(predictedBounds.center, predictedBounds.size);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(predictedAvatarPosition, 0.06f);

            Gizmos.DrawLine(avatarRoot.position, predictedAvatarPosition);

#if UNITY_EDITOR
            Handles.color = Color.white;
            Handles.Label(
                predictedAvatarPosition + Vector3.up * 0.15f,
                "Predicted Avatar Position");
#endif

            // Camera
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(camera.transform.position, 0.05f);
            Gizmos.DrawLine(camera.transform.position, predictedBounds.center);
        }
    }
}
