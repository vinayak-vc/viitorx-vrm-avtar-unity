using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Tracking {
    /// <summary>
    /// Procedural fake face tracking provider for testing facial expressions (blinks, mouth opening, smiles).
    /// </summary>
    public sealed class FakeFaceTrackingProvider : IFaceTrackingProvider {
        private readonly FaceFrame frame = new FaceFrame();
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

        public bool TryGetFrame(out FaceFrame faceFrame) {
            if (!tracking) {
                faceFrame = null;
                return false;
            }

            // Periodic eye blink every 3 seconds
            float blinkCycle = elapsedTime % 3f;
            float blink = (blinkCycle > 2.8f) ? Mathf.Sin((blinkCycle - 2.8f) / 0.2f * Mathf.PI) : 0f;

            // Smooth mouth open wave
            float mouthOpen = (Mathf.Sin(elapsedTime * 2f) + 1f) * 0.25f;

            // Subtle smile wave
            float smile = (Mathf.Cos(elapsedTime * 1.5f) + 1f) * 0.3f;

            frame.SetExpressions(blink, blink, mouthOpen, smile, 0f, 0f);
            frame.SetMeta(elapsedTime, true);
            faceFrame = frame;
            return true;
        }

        public void Dispose() {
            StopTracking();
        }
    }
}
