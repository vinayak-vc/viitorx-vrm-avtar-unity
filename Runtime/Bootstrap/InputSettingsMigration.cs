using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.App {
    /// <summary>
    /// One-time move of the body path's own copy of the input settings (video source, LAN camera PIN, loop,
    /// webcam size) into the single copy that now serves every stream, <see cref="VisionSettings"/>. Older scenes
    /// hold the values in the legacy AppBootstrap fields; the legacy fields still load (they keep their old
    /// serialized names through FormerlySerializedAs) and this carries them over once.
    ///
    /// The body copy wins only when the body path actually tracked a video: otherwise the user never chose
    /// anything there, and overwriting the vision source with an unused default would be the bug.
    /// </summary>
    public static class InputSettingsMigration {
        /// <summary>The settings layout this migration produces. Stored in AppBootstrap so it runs once per scene.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Applies the legacy body-path values to <paramref name="vision"/> when <paramref name="storedVersion"/> is
        /// older than <see cref="CurrentVersion"/>. Returns true when anything was written.
        /// </summary>
        public static bool Apply(int storedVersion, bool bodyTrackedVideo, string legacySource, string legacyPin, bool legacyLoop,
                Vector2Int legacySize, VisionSettings vision) {
            if (vision == null || storedVersion >= CurrentVersion || !bodyTrackedVideo) {
                return false;
            }
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(legacySource) && vision.Source != legacySource) {
                vision.Source = legacySource;
                changed = true;
            }
            // A PIN typed on the vision side is never replaced by an empty legacy one.
            if (!string.IsNullOrEmpty(legacyPin) && vision.LanCameraPin != legacyPin) {
                vision.LanCameraPin = legacyPin;
                changed = true;
            }
            if (vision.LoopFile != legacyLoop) {
                vision.LoopFile = legacyLoop;
                changed = true;
            }
            if (legacySize.x > 0 && legacySize.y > 0 && vision.CameraSize != legacySize) {
                vision.CameraSize = legacySize;
                changed = true;
            }
            return changed;
        }
    }
}
