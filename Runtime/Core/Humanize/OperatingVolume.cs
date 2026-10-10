using UnityEngine;

namespace VirtualMirror.Core.Humanize {
    /// <summary>What the operating-volume test concluded about one pose.</summary>
    public enum VolumeVerdict {
        /// <summary>No valid pose was offered.</summary>
        NoPose = 0,
        /// <summary>The root depth is inside the working band.</summary>
        InVolume = 1,
        /// <summary>The subject is nearer than the camera can measure them.</summary>
        TooNear = 2,
        /// <summary>The subject is further than the camera can measure them.</summary>
        TooFar = 3,
        /// <summary>A root position arrived but it is not a depth: non-finite, or effectively zero.</summary>
        RootInvalid = 4,
        /// <summary>The sender supplied no root position, so the volume cannot be judged. Not a fault:
        /// RGB-only sources never have one.</summary>
        NoRoot = 5,
    }

    /// <summary>
    /// How much a pose may be trusted by the humanize layer. Ordered: every step up adds a permission.
    /// </summary>
    public enum PoseAdmission {
        /// <summary>The pose is outside the working volume. It must not be drawn, must not move the
        /// follow position and must not teach the calibration; the last good body is held instead.</summary>
        Outside = 0,
        /// <summary>The pose is inside the volume but has only just arrived, so the depth behind it has
        /// not had time to settle. It may be drawn and followed; it may not be learned from.</summary>
        Settling = 1,
        /// <summary>Inside the volume and settled: everything is allowed, including calibration.</summary>
        Trusted = 2,
    }

    /// <summary>
    /// F-48 — THE OPERATING VOLUME. The range of camera-to-hip distance over which this system's depth
    /// is believable, and the single place that range is stated.
    ///
    /// WHY IT EXISTS (SR7). The subject spent minutes at 0.91 - 1.28 m, with one excursion to 0.66 m.
    /// F-44 measured the usable band as ~1.4 - 4.5 m, so none of that was inside it. Nearer than the
    /// band the body fills the frame, landmarks drop out, and what stereo depth remains is partial -
    /// the TRUST HUD read 17-21 of 33 joints measured. Everything downstream still ran on it: the
    /// calibration learned bone lengths from inferred depth, the avatar was walked toward the camera
    /// by a 0.66 m hip, and the debug skeleton collapsed to a vertical line while the transport stayed
    /// perfectly healthy. The transport was never the fault; the INPUT was outside what it can describe.
    ///
    /// WHY A STATIC. The limits describe the camera and the room, not a component, and four consumers
    /// (the avatar app, the skeleton show, every experience) must agree on them. It is the same shape as
    /// <see cref="CameraMount"/> and for the same reason; <c>AppBootstrap</c> publishes them from its
    /// Inspector so they are inspectable and tunable without a rebuild, and a scene launched on its
    /// own reads the defaults below.
    ///
    /// DO NOT CLAMP. The tempting fix is to pin the root to 1.35 m when it reads less. That invents
    /// motion the subject did not make and hides a camera fault behind a plausible picture. The correct
    /// answer to out-of-range data is to stop using it and to say so.
    /// </summary>
    public static class OperatingVolume {
        /// <summary>Default near limit, metres. F-44 puts the usable band's near edge at ~1.4 m; 1.35 m
        /// leaves a 5 cm margin for the difference between the sensor's depth and the levelled depth
        /// measured here. Conservative on purpose: SR7's degradation began well inside 1.3 m.</summary>
        public const float DefaultNearLimitMetres = 1.35f;

        /// <summary>Default far limit, metres. F-44: pixels-on-target at 4.5 m match what is measured
        /// good at 2 m. The claim is arithmetic, not a capture (F-44 §NOT DONE) - so this is the number
        /// the 3-4 m recording is there to confirm or move.</summary>
        public const float DefaultFarLimitMetres = 4.5f;

        /// <summary>Default seconds a body is held after leaving the volume, before the stage reports
        /// the state instead of showing a frozen figure. Long enough to ride through a step across the
        /// boundary; short enough that nobody watches a stale body for long.</summary>
        public const float DefaultHoldSeconds = 0.75f;

        /// <summary>Default poses that must be consecutively in-volume before calibration may learn
        /// again. ~0.2 s at 30 Hz: depth on the first frames after re-entry is the least settled.</summary>
        public const int DefaultSettlePoses = 6;

        /// <summary>A root nearer than this is not a depth at all. A hip cannot be 5 cm from the lens;
        /// that is a zero-filled field, and calling it TooNear would misdescribe the fault.</summary>
        public const float InvalidBelowMetres = 0.05f;

        /// <summary>Hysteresis on both edges, metres: re-entry needs this much margin, so a subject
        /// standing exactly on a limit does not flip the verdict every pose.</summary>
        public const float HysteresisMetres = 0.05f;

        // volatile: written on the main thread at startup, read wherever the next gate is built.
        private static volatile bool enabled = true;
        private static float nearLimit = DefaultNearLimitMetres;
        private static float farLimit = DefaultFarLimitMetres;
        private static float holdSeconds = DefaultHoldSeconds;
        private static int settlePoses = DefaultSettlePoses;

        /// <summary>Master switch. Off, every pose is Trusted - the pre-F-48 behaviour, and the A/B
        /// baseline. It does NOT switch off the measured-depth rule in the calibration, which is a
        /// separate protection with a separate reason.</summary>
        public static bool Enabled {
            get {
                return enabled;
            }
            set {
                enabled = value;
            }
        }

        public static float NearLimitMetres {
            get {
                return nearLimit;
            }
        }

        public static float FarLimitMetres {
            get {
                return farLimit;
            }
        }

        public static float HoldSeconds {
            get {
                return holdSeconds;
            }
        }

        public static int SettlePoses {
            get {
                return settlePoses;
            }
        }

        /// <summary>Set the band. Out-of-order or nonsensical values are repaired rather than trusted:
        /// a far limit below the near limit would classify every pose as out of volume.</summary>
        public static void Configure(float nearMetres, float farMetres, float holdSecondsValue,
                                     int settlePosesValue) {
            float near = Mathf.Max(InvalidBelowMetres + HysteresisMetres, nearMetres);
            float far = Mathf.Max(near + 2f * HysteresisMetres + 0.1f, farMetres);
            nearLimit = near;
            farLimit = far;
            holdSeconds = Mathf.Max(0f, holdSecondsValue);
            settlePoses = Mathf.Max(0, settlePosesValue);
        }

        /// <summary>Back to the shipped defaults. For tests and for a scene that wants a clean slate.</summary>
        public static void ResetToDefaults() {
            enabled = true;
            nearLimit = DefaultNearLimitMetres;
            farLimit = DefaultFarLimitMetres;
            holdSeconds = DefaultHoldSeconds;
            settlePoses = DefaultSettlePoses;
        }

        /// <summary>
        /// The camera-to-hip range of a pose, metres, or a negative number when it has no root. This is
        /// |z| of the root position, the same definition <c>SkeletonPose.CameraDepth</c> uses, so the
        /// figure the gate judges is the figure the TRUST HUD prints. After F-43's levelling it is the
        /// levelled forward distance, which differs from the sensor's depth by at most
        /// (hip height x sin(tilt)) - ~0.1 m at 20 deg - and the 5 cm margin plus the conservative
        /// limits absorb that.
        /// </summary>
        public static float RangeOf(PoseFrame frame) {
            if (frame == null || !frame.HasRootPosition) {
                return -1f;
            }
            Vector3 root = frame.RootPositionMetres;
            if (float.IsNaN(root.z) || float.IsInfinity(root.z)) {
                return float.NaN;
            }
            return Mathf.Abs(root.z);
        }

        /// <summary>
        /// Classify one range. <paramref name="previous"/> supplies the hysteresis: a pose that was
        /// TooNear must clear the limit by <see cref="HysteresisMetres"/> to count as in volume.
        /// </summary>
        public static VolumeVerdict Classify(float rangeMetres, VolumeVerdict previous) {
            if (rangeMetres < 0f) {
                return VolumeVerdict.NoRoot;
            }
            if (float.IsNaN(rangeMetres) || rangeMetres < InvalidBelowMetres) {
                return VolumeVerdict.RootInvalid;
            }
            float near = previous == VolumeVerdict.TooNear ? nearLimit + HysteresisMetres : nearLimit;
            float far = previous == VolumeVerdict.TooFar ? farLimit - HysteresisMetres : farLimit;
            if (rangeMetres < near) {
                return VolumeVerdict.TooNear;
            }
            if (rangeMetres > far) {
                return VolumeVerdict.TooFar;
            }
            return VolumeVerdict.InVolume;
        }
    }
}
