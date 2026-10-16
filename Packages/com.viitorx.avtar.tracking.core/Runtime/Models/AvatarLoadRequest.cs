namespace VirtualMirror.Core {
    /// <summary>
    /// Describes a single avatar load. When <see cref="SourceBytes"/> is set it takes precedence
    /// over <see cref="SourcePath"/>.
    /// </summary>
    public sealed class AvatarLoadRequest {
        private readonly string sourcePath;
        private readonly byte[] sourceBytes;
        private readonly string displayName;
        private readonly bool showMeshesOnLoad;

        public AvatarLoadRequest(string sourcePath, string displayName, bool showMeshesOnLoad) {
            this.sourcePath = sourcePath;
            this.sourceBytes = null;
            this.displayName = displayName;
            this.showMeshesOnLoad = showMeshesOnLoad;
        }

        public AvatarLoadRequest(byte[] sourceBytes, string displayName, bool showMeshesOnLoad) {
            this.sourcePath = null;
            this.sourceBytes = sourceBytes;
            this.displayName = displayName;
            this.showMeshesOnLoad = showMeshesOnLoad;
        }

        public string SourcePath {
            get {
                return sourcePath;
            }
        }

        public byte[] SourceBytes {
            get {
                return sourceBytes;
            }
        }

        public string DisplayName {
            get {
                return displayName;
            }
        }

        public bool ShowMeshesOnLoad {
            get {
                return showMeshesOnLoad;
            }
        }
    }
}
