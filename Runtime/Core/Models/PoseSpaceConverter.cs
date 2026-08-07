using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Converts raw MediaPipe world-landmark axes (metres; origin at hip centre; X right, Y down,
    /// Z toward camera) into Unity space (Y up). Each axis sign is configurable because the exact
    /// handedness/mirror is empirically tuned per SDS-025 §3. Flipping X mirrors left/right.
    /// </summary>
    public sealed class PoseSpaceConverter {
        // LOW-C: volatile because SetFlip* is called from the main thread (the live "Mirror" panel toggle)
        // while ToUnity/ToUnityPosition read these on the provider worker thread(s) — and the OAK/MediaPipe
        // hand path now shares this single instance across two workers. volatile prevents torn/stale reads.
        private volatile float signX;
        private volatile float signY;
        private volatile float signZ;

        public PoseSpaceConverter(bool flipX, bool flipY, bool flipZ) {
            signX = flipX ? -1f : 1f;
            signY = flipY ? -1f : 1f;
            signZ = flipZ ? -1f : 1f;
        }

        // Live-settable so the "Mirror Reflection (Flip X)" panel toggle can flip left/right at runtime
        // without rebuilding the tracking provider. The same converter instance is shared by the active
        // provider, so the next frame picks up the new sign.
        public void SetFlipX(bool flipX) {
            signX = flipX ? -1f : 1f;
        }

        public void SetFlipY(bool flipY) {
            signY = flipY ? -1f : 1f;
        }

        public void SetFlipZ(bool flipZ) {
            signZ = flipZ ? -1f : 1f;
        }

        public Vector3 ToUnity(float x, float y, float z) {
            return new Vector3(x * signX, y * signY, z * signZ);
        }

        // World-position variant for the OAK-D spatial hip anchor. The depth sensor's xyz is X-right,
        // Y-UP, Z-forward — the OPPOSITE Y sense to the GHUM landmarks (Y-down). So Y passes straight
        // through to Unity's Y-up (jumping raises the avatar), while X/Z keep the mirror/forward signs
        // for parity with the pose. Using ToUnity here would double-invert Y (jump → avatar sinks).
        public Vector3 ToUnityPosition(float x, float y, float z) {
            return new Vector3(x * signX, y, z * signZ);
        }
    }
}
