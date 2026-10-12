using System;

using Unity.Profiling;

using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// Optional frame-time and managed-allocation probe over a rolling window of frames. Allocates
    /// nothing after start-up; the debug view reads it, and a test can read it to measure a
    /// configuration. Frame times are wall-clock (they include vsync waits and, in the Editor, the
    /// Editor's own overhead).
    /// </summary>
    public sealed class ShowcasePerformanceProbe : MonoBehaviour {
        public const int Window = 600;

        private readonly float[] frames = new float[Window];
        private readonly float[] sorted = new float[Window];
        private int count;
        private int head;
        private ProfilerRecorder gcRecorder;
        private long gcBytesWindow;
        private int gcFrames;

        private void OnEnable() {
            gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        private void OnDisable() {
            gcRecorder.Dispose();
        }

        private void Update() {
            frames[head] = Time.unscaledDeltaTime * 1000f;
            head = (head + 1) % Window;
            if (count < Window) {
                count++;
            }
            if (gcRecorder.Valid) {
                gcBytesWindow += gcRecorder.LastValue;
                gcFrames++;
            }
        }

        /// <summary>Forget the window, e.g. after changing a configuration being measured.</summary>
        public void ResetWindow() {
            count = 0;
            head = 0;
            gcBytesWindow = 0L;
            gcFrames = 0;
        }

        public int Frames {
            get {
                return count;
            }
        }

        /// <summary>Average, 95th-percentile and worst frame time in ms over the window, and the
        /// managed bytes allocated per frame since the last reset.</summary>
        public bool TryGetStats(out float averageMs, out float p95Ms, out float maxMs, out float gcBytesPerFrame) {
            averageMs = 0f;
            p95Ms = 0f;
            maxMs = 0f;
            gcBytesPerFrame = 0f;
            if (count < 10) {
                return false;
            }
            float sum = 0f;
            for (int i = 0; i < count; i++) {
                sorted[i] = frames[i];
                sum += frames[i];
            }
            Array.Sort(sorted, 0, count);
            averageMs = sum / count;
            p95Ms = sorted[Mathf.Min(count - 1, Mathf.CeilToInt(count * 0.95f) - 1)];
            maxMs = sorted[count - 1];
            gcBytesPerFrame = gcFrames > 0 ? gcBytesWindow / (float)gcFrames : 0f;
            return true;
        }
    }
}
