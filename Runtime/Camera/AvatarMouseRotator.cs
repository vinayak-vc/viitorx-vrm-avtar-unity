using UnityEngine;

namespace VirtualMirror.Camera {
    /// <summary>
    /// Mouse drag controller that allows dragging horizontally to rotate the avatar around its Y axis.
    /// </summary>
    public sealed class AvatarMouseRotator : MonoBehaviour {
        [SerializeField] private float rotationSensitivity = 0.4f;
        [SerializeField] private bool invertDrag = false;

        private Transform targetAvatarRoot;
        private Vector3 lastMousePosition;
        private bool isDragging;

        public void SetTarget(Transform avatarRoot) {
            targetAvatarRoot = avatarRoot;
            this.enabled = false;
        }

        private void Update() {
            return;
            if (targetAvatarRoot == null) {
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) {
                isDragging = true;
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) {
                isDragging = false;
            }

            if (isDragging && (Input.GetMouseButton(0) || Input.GetMouseButton(1))) {
                Vector3 currentMouse = Input.mousePosition;
                float deltaX = currentMouse.x - lastMousePosition.x;
                lastMousePosition = currentMouse;

                float direction = invertDrag ? 1f : -1f;
                float rotationAmount = deltaX * rotationSensitivity * direction;
                targetAvatarRoot.Rotate(0f, rotationAmount, 0f, Space.World);
            }
        }
    }
}
