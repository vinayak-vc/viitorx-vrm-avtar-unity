using UnityEngine;

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

        public void Frame(Transform avatarRoot) {
            if (avatarRoot == null) {
                return;
            }
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
            Vector3 cameraPosition = new Vector3(lookTarget.x, lookTarget.y, bounds.center.z - distance);
            activeCamera.transform.position = cameraPosition;
            activeCamera.transform.rotation = Quaternion.LookRotation(lookTarget - cameraPosition, Vector3.up);
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
    }
}
