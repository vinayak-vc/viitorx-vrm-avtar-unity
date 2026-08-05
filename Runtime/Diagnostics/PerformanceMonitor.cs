using UnityEngine;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// Measures real-time FPS and frame time interval statistics over a rolling window.
    /// </summary>
    public sealed class PerformanceMonitor {
        private readonly float sampleWindowSeconds;
        private float frameCount;
        private float timeAccumulator;
        private float currentFps;
        private float currentFrameTimeMs;

        public float Fps {
            get {
                return currentFps;
            }
        }

        public float FrameTimeMs {
            get {
                return currentFrameTimeMs;
            }
        }

        public PerformanceMonitor(float sampleWindowSeconds = 0.5f) {
            this.sampleWindowSeconds = sampleWindowSeconds > 0f ? sampleWindowSeconds : 0.5f;
            this.frameCount = 0f;
            this.timeAccumulator = 0f;
            this.currentFps = 60f;
            this.currentFrameTimeMs = 16.6f;
        }

        public void Tick(float unscaledDeltaTime) {
            frameCount = frameCount + 1f;
            timeAccumulator = timeAccumulator + unscaledDeltaTime;

            if (timeAccumulator >= sampleWindowSeconds) {
                currentFps = frameCount / timeAccumulator;
                currentFrameTimeMs = (timeAccumulator / frameCount) * 1000f;
                frameCount = 0f;
                timeAccumulator = 0f;
            }
        }
    }
}
