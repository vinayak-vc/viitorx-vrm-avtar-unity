using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// P1-3 — bounded timestamped pose buffer + temporal interpolation.
    ///
    /// The audit measured ~11.7 Unity applies per received pose: the same pose was re-applied every
    /// render frame and crudely dragged toward with a fixed slerp, while <c>seq</c>/<c>t</c> were parsed
    /// but never used. This buffer replaces "latest-wins + slerp" with actual TEMPORAL RECONSTRUCTION:
    /// render at <c>now - interpolationDelay</c> and interpolate between the two poses that bracket it.
    ///
    ///     network   A ---------- B ---------- C
    ///     render              ↑ renderTime
    ///                    lerp(A, B, alpha)
    ///
    /// This is NOT another smoothing stage — P0 and P1-1 own tracking stability. Interpolation only
    /// reconstructs where the body was at the presentation instant.
    ///
    /// SAFETY RULE (preserves P0-1 / F-01): a landmark is NEVER position-interpolated across an invalid
    /// endpoint. The sidecar emits a dropped joint as [0,0,0,0], so lerping valid→zero would place the
    /// joint half-way to the origin — exactly the limb-collapse bug P0-1 exists to prevent. When either
    /// endpoint is invalid the valid endpoint's POSITION is carried and the confidence is min(a,b), so a
    /// dropped joint still arrives at the LimbGate as confidence 0 and the gate holds.
    ///
    /// Not thread-safe: the owner serialises access (the provider holds its lock).
    /// </summary>
    public sealed class PoseBuffer {
        private readonly PoseFrame[] frames;
        private readonly long[] seqs;
        private readonly double[] times;
        private readonly int capacity;
        private int count;
        private int head;               // index of the NEWEST entry
        private long newestSeq = long.MinValue;
        private long oldestSeq;
        private long lastSeqA;
        private long lastSeqB;
        private float lastAlpha;
        private double lastRenderTime;
        private long rejectedOutOfOrder;
        private long rejectedDuplicate;
        private long droppedOld;
        private long samplesInterpolated;
        private long samplesClampedNewest;
        private long samplesClampedOldest;

        // --- diagnostics (read by the periodic aggregate log; never per-frame) ---
        public int Depth {
            get {
                return count;
            }
        }

        public long OldestSeq {
            get {
                return oldestSeq;
            }
        }

        public long NewestSeq {
            get {
                return newestSeq == long.MinValue ? -1 : newestSeq;
            }
        }

        public long LastSeqA {
            get {
                return lastSeqA;
            }
        }

        public long LastSeqB {
            get {
                return lastSeqB;
            }
        }

        public float LastAlpha {
            get {
                return lastAlpha;
            }
        }

        public double LastRenderTime {
            get {
                return lastRenderTime;
            }
        }

        public long RejectedOutOfOrder {
            get {
                return rejectedOutOfOrder;
            }
        }

        public long RejectedDuplicate {
            get {
                return rejectedDuplicate;
            }
        }

        public long DroppedOld {
            get {
                return droppedOld;
            }
        }

        public long SamplesInterpolated {
            get {
                return samplesInterpolated;
            }
        }

        public long SamplesClampedNewest {
            get {
                return samplesClampedNewest;
            }
        }

        public long SamplesClampedOldest {
            get {
                return samplesClampedOldest;
            }
        }

        public PoseBuffer(int capacity) {
            this.capacity = capacity < 2 ? 2 : capacity;
            frames = new PoseFrame[this.capacity];
            seqs = new long[this.capacity];
            times = new double[this.capacity];
            for (int i = 0; i < this.capacity; i++) {
                frames[i] = new PoseFrame();
            }
            Clear();
        }

        public void Clear() {
            count = 0;
            head = -1;
            newestSeq = long.MinValue;
            oldestSeq = -1;
            lastSeqA = -1;
            lastSeqB = -1;
            lastAlpha = 0f;
        }

        /// <summary>
        /// Insert a pose. Rejects duplicate and out-of-order sequence numbers; the ring bounds memory,
        /// so the oldest entry is overwritten once full (no unbounded growth).
        /// Returns false when the packet was rejected.
        /// </summary>
        public bool Push(PoseFrame source, long seq, double timestampSeconds) {
            if (source == null) {
                return false;
            }
            if (newestSeq != long.MinValue && seq >= 0) {
                if (seq == newestSeq) {
                    rejectedDuplicate = rejectedDuplicate + 1;
                    return false;
                }
                if (seq < newestSeq) {
                    // A late packet cannot be inserted mid-ring without reordering, and reordering a
                    // real-time buffer buys nothing: by definition its interval has already rendered.
                    rejectedOutOfOrder = rejectedOutOfOrder + 1;
                    return false;
                }
            }
            // Reject a timestamp that goes backwards (clock regression) even if seq advanced —
            // interpolation divides by (tB - tA) and a non-monotonic axis makes alpha meaningless.
            if (count > 0 && timestampSeconds <= times[head]) {
                rejectedOutOfOrder = rejectedOutOfOrder + 1;
                return false;
            }

            if (count == capacity) {
                droppedOld = droppedOld + 1;
            }
            head = (head + 1) % capacity;
            CopyInto(source, frames[head]);
            seqs[head] = seq;
            times[head] = timestampSeconds;
            if (count < capacity) {
                count = count + 1;
            }
            newestSeq = seq;
            oldestSeq = seqs[IndexFromOldest(0)];
            return true;
        }

        /// <summary>
        /// Sample the buffer at <paramref name="renderTime"/> into <paramref name="output"/>.
        /// Interpolates between the two poses bracketing renderTime. Never extrapolates: a renderTime
        /// past the newest pose clamps to the newest (a renderTime before the oldest clamps to oldest).
        /// Returns false when the buffer is empty.
        /// </summary>
        public bool Sample(double renderTime, PoseFrame output) {
            if (count == 0 || output == null) {
                return false;
            }
            lastRenderTime = renderTime;
            if (count == 1) {
                int only = IndexFromOldest(0);
                CopyInto(frames[only], output);
                lastSeqA = seqs[only];
                lastSeqB = seqs[only];
                lastAlpha = 0f;
                samplesClampedNewest = samplesClampedNewest + 1;
                return true;
            }

            int oldest = IndexFromOldest(0);
            if (renderTime <= times[oldest]) {
                CopyInto(frames[oldest], output);
                lastSeqA = seqs[oldest];
                lastSeqB = seqs[oldest];
                lastAlpha = 0f;
                samplesClampedOldest = samplesClampedOldest + 1;
                return true;
            }
            if (renderTime >= times[head]) {
                // No future sample yet. Do NOT extrapolate (P1-3 scope) — present the newest pose.
                CopyInto(frames[head], output);
                lastSeqA = seqs[head];
                lastSeqB = seqs[head];
                lastAlpha = 1f;
                samplesClampedNewest = samplesClampedNewest + 1;
                return true;
            }

            // find the bracketing pair (walk from newest backwards; the buffer is small)
            int bIdx = head;
            int aIdx = head;
            for (int back = 1; back < count; back++) {
                int idx = IndexFromNewest(back);
                if (times[idx] <= renderTime) {
                    aIdx = idx;
                    bIdx = IndexFromNewest(back - 1);
                    break;
                }
            }
            double span = times[bIdx] - times[aIdx];
            float alpha = span > 1e-9 ? (float)((renderTime - times[aIdx]) / span) : 0f;
            if (alpha < 0f) {
                alpha = 0f;
            } else if (alpha > 1f) {
                alpha = 1f;
            }
            Interpolate(frames[aIdx], frames[bIdx], alpha, output);
            lastSeqA = seqs[aIdx];
            lastSeqB = seqs[bIdx];
            lastAlpha = alpha;
            samplesInterpolated = samplesInterpolated + 1;
            return true;
        }

        /// <summary>Epoch-seconds timestamp of the newest buffered pose (-1 when empty).</summary>
        public double NewestTime() {
            return count == 0 ? -1.0 : times[head];
        }

        private int IndexFromNewest(int back) {
            int i = head - back;
            while (i < 0) {
                i += capacity;
            }
            return i;
        }

        private int IndexFromOldest(int forward) {
            return IndexFromNewest(count - 1 - forward);
        }

        private static void CopyInto(PoseFrame src, PoseFrame dst) {
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                dst.SetLandmark(i, src.GetLandmark((JointId)i));
            }
            dst.SetMeta(src.TimestampSeconds, src.IsValid);
            if (src.HasRootPosition) {
                dst.SetRootPosition(src.RootPositionMetres);
            } else {
                dst.ClearRootPosition();
            }
        }

        /// <summary>Positional lerp per landmark + root; see the SAFETY RULE in the class summary.</summary>
        private static void Interpolate(PoseFrame a, PoseFrame b, float alpha, PoseFrame output) {
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                PoseLandmark la = a.GetLandmark((JointId)i);
                PoseLandmark lb = b.GetLandmark((JointId)i);
                float ca = la.Confidence;
                float cb = lb.Confidence;
                if (ca <= 0f || cb <= 0f) {
                    // NEVER lerp across an invalid endpoint (would drag the joint toward the origin).
                    // Carry the valid side's position; confidence min() => 0, so the LimbGate holds.
                    Vector3 keep = ca > 0f ? la.Position : lb.Position;
                    output.SetLandmark(i, new PoseLandmark(keep, ca < cb ? ca : cb));
                    continue;
                }
                output.SetLandmark(i, new PoseLandmark(
                    Vector3.Lerp(la.Position, lb.Position, alpha),
                    Mathf.Lerp(ca, cb, alpha)));
            }
            double t = a.TimestampSeconds + (b.TimestampSeconds - a.TimestampSeconds) * alpha;
            output.SetMeta(t, a.IsValid && b.IsValid);
            if (a.HasRootPosition && b.HasRootPosition) {
                output.SetRootPosition(Vector3.Lerp(a.RootPositionMetres, b.RootPositionMetres, alpha));
            } else if (b.HasRootPosition) {
                output.SetRootPosition(b.RootPositionMetres);
            } else if (a.HasRootPosition) {
                output.SetRootPosition(a.RootPositionMetres);
            } else {
                output.ClearRootPosition();
            }
        }

        /// <summary>Quaternion slerp helper for the rotational streams (hand palm basis), kept here so
        /// the buffer owns one consistent interpolation policy. Shortest-arc, normalised.</summary>
        public static Quaternion SlerpRotation(Quaternion a, Quaternion b, float alpha) {
            return Quaternion.Slerp(a, b, alpha);
        }
    }
}
