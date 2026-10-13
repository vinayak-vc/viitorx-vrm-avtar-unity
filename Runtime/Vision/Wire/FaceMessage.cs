namespace VirtualMirror.Vision {
    /// <summary>A parsed face datagram: the header and up to <see cref="Capacity"/> faces. Reusable and mutable.</summary>
    public sealed class FaceMessage : VisionMessage {
        public const int DefaultCapacity = 4;

        private readonly FaceData[] faces;
        private int blendshapeOrdering = 1;

        public FaceMessage() : this(DefaultCapacity) {
        }

        public FaceMessage(int capacity) {
            int size = capacity < 1 ? 1 : capacity;
            faces = new FaceData[size];
            int i = 0;
            while (i < size) {
                faces[i] = new FaceData();
                i = i + 1;
            }
        }

        public int Capacity {
            get {
                return faces.Length;
            }
        }

        /// <summary>Which blendshape ordering the scores use (<c>bsv</c>). 1 is <see cref="ArkitBlendshapes"/>.</summary>
        public int BlendshapeOrdering {
            get {
                return blendshapeOrdering;
            }
            internal set {
                blendshapeOrdering = value;
            }
        }

        /// <summary>The face at <paramref name="index"/> (0 to <see cref="VisionMessage.Count"/> - 1). The datagram's own order: the sidecar sends the most established face first.</summary>
        public FaceData Face(int index) {
            return faces[index];
        }

        public override void CopyFrom(VisionMessage other) {
            FaceMessage source = other as FaceMessage;
            if (source == null) {
                return;
            }
            CopyHeaderFrom(source);
            blendshapeOrdering = source.blendshapeOrdering;
            int i = 0;
            while (i < Count && i < faces.Length) {
                faces[i].CopyFrom(source.faces[i]);
                i = i + 1;
            }
        }

        internal void Clear() {
            ResetHeader();
            blendshapeOrdering = 1;
            int i = 0;
            while (i < faces.Length) {
                faces[i].Clear();
                i = i + 1;
            }
        }

        internal FaceData Slot(int index) {
            return faces[index];
        }
    }
}
