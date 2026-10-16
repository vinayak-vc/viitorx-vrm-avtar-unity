namespace VirtualMirror.Core {
    /// <summary>
    /// Requested camera configuration. An empty <see cref="DeviceName"/> selects the default device;
    /// width/height/fps are hints the driver may not honour exactly.
    /// </summary>
    public sealed class CameraCaptureRequest {
        private readonly string deviceName;
        private readonly int requestedWidth;
        private readonly int requestedHeight;
        private readonly int requestedFps;

        public CameraCaptureRequest(string deviceName, int requestedWidth, int requestedHeight, int requestedFps) {
            this.deviceName = deviceName;
            this.requestedWidth = requestedWidth;
            this.requestedHeight = requestedHeight;
            this.requestedFps = requestedFps;
        }

        public string DeviceName {
            get {
                return deviceName;
            }
        }

        public int RequestedWidth {
            get {
                return requestedWidth;
            }
        }

        public int RequestedHeight {
            get {
                return requestedHeight;
            }
        }

        public int RequestedFps {
            get {
                return requestedFps;
            }
        }
    }
}
