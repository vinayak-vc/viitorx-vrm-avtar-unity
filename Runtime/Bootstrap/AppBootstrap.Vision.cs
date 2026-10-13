using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Vision;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Hands and Face (H-03)": every setting of the hands-only, face-only and cutout senders and receivers (UDP 8901, 8902 and
    /// 8903), in one <see cref="VisionSettings"/>. The behaviour is <see cref="VisionHost"/> (sockets, Python senders, message and frame hand-over)
    /// and the runtime control panel is <see cref="VisionControlPanel"/> (F6). Independent of the body path above: the body sidecar and these
    /// senders can run together, and the cutout never touches the avatar or the skeleton.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Hands and Face (H-03)", Order = 25, ExpandedByDefault = true)]
        [Tooltip("Hands-only, face-only and cutout (the person cut out of the camera picture) streams, no body needed. Scenes ask for a stream (the Hands and Face and Cutout showcase scenes do) and the sender for it starts. Open the control panel at runtime with the Panel Key (F6).")]
        [SerializeField] private VisionSettings vision = new VisionSettings();

        private VisionHost visionHost;
        private VisionControlPanel visionPanel;

        /// <summary>The hands, face and cutout streams: receivers, senders and the per-frame messages and frames. Null before the bootstrap has started.</summary>
        public VisionHost Vision {
            get {
                return visionHost;
            }
        }

        /// <summary>The runtime control panel for <see cref="Vision"/>.</summary>
        public VisionControlPanel VisionPanel {
            get {
                return visionPanel;
            }
        }

        private void InitializeVision() {
            if (vision == null) {
                vision = new VisionSettings();
            }
            visionHost = new VisionHost(logService, vision);
            visionPanel = gameObject.AddComponent<VisionControlPanel>();
            visionPanel.Bind(visionHost);
            logService.Log(LogLevel.Info, "Hands and face host ready (hands " + vision.HandsMode + " on UDP " + vision.HandsPort
                    + ", face " + vision.FaceMode + " on UDP " + vision.FacePort + ", cutout " + vision.CutoutMode + " on UDP " + vision.CutoutPort
                    + "; control panel: " + vision.PanelKey + ").");
        }

        private void TickVision() {
            if (visionHost != null) {
                visionHost.Tick(Time.unscaledDeltaTime);
            }
        }

        private void ShutdownVision() {
            if (visionHost != null) {
                visionHost.Dispose();
                visionHost = null;
            }
        }
    }
}
