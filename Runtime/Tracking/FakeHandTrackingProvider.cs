using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Tracking {
    /// <summary>
    /// Procedural fake hand tracking provider for testing finger flexing & curling.
    /// </summary>
    public sealed class FakeHandTrackingProvider : IHandTrackingProvider {
        private bool tracking;
        private float elapsedTime;

        public bool IsTracking {
            get {
                return tracking;
            }
        }

        public bool StartTracking() {
            tracking = true;
            elapsedTime = 0f;
            return true;
        }

        public void Tick(float deltaSeconds) {
            if (!tracking) {
                return;
            }
            elapsedTime = elapsedTime + deltaSeconds;
        }

        public void StopTracking() {
            tracking = false;
        }

        public bool TryGetFrame(out HandFrame frame) {
            if (!tracking) {
                frame = null;
                return false;
            }

            // Wave fingers smoothly
            float thumb = (Mathf.Sin(elapsedTime * 2f) + 1f) * 0.4f;
            float index = (Mathf.Sin(elapsedTime * 2.2f + 0.2f) + 1f) * 0.4f;
            float middle = (Mathf.Sin(elapsedTime * 2.4f + 0.4f) + 1f) * 0.4f;
            float ring = (Mathf.Sin(elapsedTime * 2.6f + 0.6f) + 1f) * 0.4f;
            float little = (Mathf.Sin(elapsedTime * 2.8f + 0.8f) + 1f) * 0.4f;

            frame = new HandFrame(
                thumb, index, middle, ring, little,
                thumb, index, middle, ring, little,
                elapsedTime, true
            );
            return true;
        }

        public void Dispose() {
            StopTracking();
        }
    }
}
