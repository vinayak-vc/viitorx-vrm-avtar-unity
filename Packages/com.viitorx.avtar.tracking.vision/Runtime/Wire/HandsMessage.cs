namespace VirtualMirror.Vision {
    /// <summary>A parsed hands datagram: the header and up to <see cref="Capacity"/> hands. Reusable and mutable.</summary>
    public sealed class HandsMessage : VisionMessage {
        public const int DefaultCapacity = 4;

        private readonly HandData[] hands;

        public HandsMessage() : this(DefaultCapacity) {
        }

        public HandsMessage(int capacity) {
            int size = capacity < 1 ? 1 : capacity;
            hands = new HandData[size];
            int i = 0;
            while (i < size) {
                hands[i] = new HandData();
                i = i + 1;
            }
        }

        public int Capacity {
            get {
                return hands.Length;
            }
        }

        /// <summary>The hand at <paramref name="index"/> (0 to <see cref="VisionMessage.Count"/> - 1). The datagram's own order: the sidecar sends the most established hand first.</summary>
        public HandData Hand(int index) {
            return hands[index];
        }

        /// <summary>The best hand of a side: a tracked one over a held one, then the more confident. False when that side is absent.</summary>
        public bool TryFindSide(HandSide side, out HandData found) {
            found = null;
            int i = 0;
            while (i < Count) {
                HandData candidate = hands[i];
                if (candidate.Side == side && IsBetter(candidate, found)) {
                    found = candidate;
                }
                i = i + 1;
            }
            return found != null;
        }

        public override void CopyFrom(VisionMessage other) {
            HandsMessage source = other as HandsMessage;
            if (source == null) {
                return;
            }
            CopyHeaderFrom(source);
            int i = 0;
            while (i < Count && i < hands.Length) {
                hands[i].CopyFrom(source.hands[i]);
                i = i + 1;
            }
        }

        internal void Clear() {
            ResetHeader();
            int i = 0;
            while (i < hands.Length) {
                hands[i].Clear();
                i = i + 1;
            }
        }

        internal HandData Slot(int index) {
            return hands[index];
        }

        private static bool IsBetter(HandData candidate, HandData current) {
            if (current == null) {
                return true;
            }
            if (candidate.State != current.State) {
                return candidate.State == TrackState.Tracked;
            }
            return candidate.Confidence > current.Confidence;
        }
    }
}
