using UnityEngine;
using UnityEngine.UI;

using VirtualMirror.Core;

namespace VirtualMirror.UI {
    /// <summary>
    /// Diagnostic view: mirrors the current camera-capture texture onto a RawImage so the capture
    /// source (webcam or sample video) is visible in the scene.
    /// </summary>
    public sealed class CameraPreviewController : MonoBehaviour {
        [SerializeField] private RawImage previewImage;

        private ICameraCapture capture;

        public void Initialize(ICameraCapture capture) {
            this.capture = capture;
        }

        private void Update() {
            if (capture == null || previewImage == null) {
                return;
            }
            Texture texture = capture.CurrentTexture;
            if (texture != null && previewImage.texture != texture) {
                previewImage.texture = texture;
            }
        }
    }
}
