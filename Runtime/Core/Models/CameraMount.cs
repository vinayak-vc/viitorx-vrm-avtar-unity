namespace VirtualMirror.Core {
    /// <summary>
    /// F-43 — THE PHYSICAL CAMERA MOUNT. One installation has one camera on one bracket, so its
    /// pitch is one number, and this is where that number lives.
    ///
    /// WHY A STATIC AND NOT A SERIALIZED FIELD PER COMPONENT. Four components build a
    /// <see cref="PoseSpaceConverter"/> — AppBootstrap, SkeletonShowBootstrap, and TrackedStage from
    /// both ExperienceBase and SceneLauncher. `flipX` is declared separately in all four, and the
    /// cost of that showed up immediately: turning the mirror on meant editing twenty scene files,
    /// and a scene launched standalone silently disagreed with one launched through the bootstrap.
    /// Tilt has exactly the same shape and would have exactly the same failure, except worse — a
    /// mirror that is wrong looks wrong instantly, whereas a tilt that is wrong just makes the floor
    /// drift with distance and reads as "the tracking is a bit off".
    ///
    /// A mutable global is normally the wrong answer. It is the right one here because the thing
    /// being described genuinely IS global and genuinely IS mutable only by a person with a
    /// spanner: every consumer in the process is looking through the same lens on the same bracket.
    /// </summary>
    public static class CameraMount {
        // volatile for the same reason PoseSpaceConverter's signs are: this is written on the main
        // thread at startup and read by whatever thread next builds a converter.
        private static volatile float tiltDegrees;

        /// <summary>
        /// The mount's pitch away from horizontal in degrees, POSITIVE WHEN THE CAMERA IS PITCHED
        /// DOWN (nose toward the floor). 0 — a level mount — is the default and costs nothing.
        ///
        /// MEASURING IT. `python-sidecar~/tools/capture/f19_level.py` reads the pitch from the
        /// board's own BNO086 IMU and writes it to `evidence/oak_v4/f19/mount.json` as `tilt_deg`.
        /// The last recorded run reads 5.89° against that tool's own 2° tolerance, so the production
        /// mount was already out of level before anyone chose to tilt it deliberately.
        ///
        /// THE SIGN IS NOT MEASURED. mount.json records magnitude only — its note says "the IMU sign
        /// convention is not asserted", because the board returns an identity IMU-to-camera
        /// extrinsic that F-19 disproved directly. Read the direction off the bracket by eye. A
        /// wrong sign doubles the error rather than removing it, so 0 is better than a guess.
        ///
        /// WHAT IT BUYS. Nothing in the pipeline compensated for pitch before F-43, and
        /// <see cref="SkeletonPose"/>'s floor is a single scalar learned from foot contacts. A pitch
        /// of θ shears the floor by tan(θ) metres per metre of depth, which one scalar cannot
        /// represent: 15° is 0.27 m/m and crosses the 0.08 m planted band within 30 cm of approach.
        /// Setting this correctly is what makes a deliberately angled mount usable at all.
        /// </summary>
        public static float TiltDegrees {
            get {
                return tiltDegrees;
            }
            set {
                tiltDegrees = value;
            }
        }
    }
}
