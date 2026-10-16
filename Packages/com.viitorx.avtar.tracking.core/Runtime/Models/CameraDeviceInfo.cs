namespace VirtualMirror.Core {
    /// <summary>
    /// Describes an available camera device.
    /// </summary>
    public readonly struct CameraDeviceInfo {
        private readonly string name;
        private readonly bool isFrontFacing;

        public CameraDeviceInfo(string name, bool isFrontFacing) {
            this.name = name;
            this.isFrontFacing = isFrontFacing;
        }

        public string Name {
            get {
                return name;
            }
        }

        public bool IsFrontFacing {
            get {
                return isFrontFacing;
            }
        }
    }
}
