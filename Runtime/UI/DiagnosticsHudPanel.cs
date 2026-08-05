using TMPro;
using UnityEngine;
using VirtualMirror.Diagnostics;

namespace VirtualMirror.UI {
    /// <summary>
    /// On-screen corner overlay rendering real-time performance metrics (FPS, frame time, tracking mode, avatar name).
    /// </summary>
    public sealed class DiagnosticsHudPanel : MonoBehaviour {
        [SerializeField] private TextMeshProUGUI fpsText;
        [SerializeField] private TextMeshProUGUI trackingStatusText;
        [SerializeField] private TextMeshProUGUI avatarStatusText;
        [SerializeField] private TextMeshProUGUI cameraStatusText;

        private PerformanceMonitor performanceMonitor;
        private string trackingMode = "MediaPipe Pose (CPU)";
        private string cameraInfo = "Webcam 1280x720@30fps";
        private string avatarName = "None";

        public void Initialize(PerformanceMonitor monitor) {
            performanceMonitor = monitor;
        }

        public void SetTrackingStatus(string status) {
            trackingMode = !string.IsNullOrEmpty(status) ? status : "Inactive";
        }

        public void SetCameraInfo(string info) {
            cameraInfo = !string.IsNullOrEmpty(info) ? info : "Disabled";
        }

        public void SetAvatarName(string name) {
            avatarName = !string.IsNullOrEmpty(name) ? name : "None";
        }

        private void Update() {
            if (performanceMonitor != null) {
                performanceMonitor.Tick(Time.unscaledDeltaTime);
                if (fpsText != null) {
                    fpsText.text = string.Format("{0:0.0} FPS ({1:0.0} ms)", performanceMonitor.Fps, performanceMonitor.FrameTimeMs);
                }
            }

            if (trackingStatusText != null) {
                trackingStatusText.text = "Tracking: " + trackingMode;
            }

            if (cameraStatusText != null) {
                cameraStatusText.text = "Camera: " + cameraInfo;
            }

            if (avatarStatusText != null) {
                avatarStatusText.text = "Avatar: " + avatarName;
            }
        }
    }
}
