using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Converts raw MediaPipe world-landmark axes (metres; origin at hip centre; X right, Y down,
    /// Z toward camera) into Unity space (Y up). Each axis sign is configurable because the exact
    /// handedness/mirror is empirically tuned per SDS-025 §3. Flipping X mirrors left/right.
    /// </summary>
    public sealed class PoseSpaceConverter {
        private readonly float signX;
        private readonly float signY;
        private readonly float signZ;

        public PoseSpaceConverter(bool flipX, bool flipY, bool flipZ) {
            signX = flipX ? -1f : 1f;
            signY = flipY ? -1f : 1f;
            signZ = flipZ ? -1f : 1f;
        }

        public Vector3 ToUnity(float x, float y, float z) {
            return new Vector3(x * signX, y * signY, z * signZ);
        }
    }
}
