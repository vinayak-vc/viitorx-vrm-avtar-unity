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

        /// <summary>
        /// F-43. The mount's downward pitch, pre-resolved into the cos/sin the hot path needs.
        ///
        /// Held as ONE immutable object rather than two volatile floats because a live change has to
        /// be all-or-nothing: two separate fields can be read between the cos write and the sin
        /// write, and the reader then rotates by an angle that is not a rotation at all. Reference
        /// assignment is atomic, so a worker thread sees either the whole old tilt or the whole new
        /// one. That matters here for the same reason the signs above are volatile — this object is
        /// shared across the provider worker threads.
        /// </summary>
        private sealed class Tilt {
            public readonly float Degrees;
            public readonly float Cos;
            public readonly float Sin;
            public readonly bool Identity;

            public Tilt(float degrees) {
                Degrees = degrees;
                float radians = degrees * Mathf.Deg2Rad;
                Cos = Mathf.Cos(radians);
                Sin = Mathf.Sin(radians);
                // Below a hundredth of a degree the rotation is not distinguishable from identity at
                // any range this camera works at, so the hot path skips it outright.
                Identity = Mathf.Abs(degrees) < 0.01f;
            }
        }

        private volatile Tilt tilt = new Tilt(0f);

        public PoseSpaceConverter(bool flipX, bool flipY, bool flipZ)
            : this(flipX, flipY, flipZ, 0f) {
        }

        public PoseSpaceConverter(bool flipX, bool flipY, bool flipZ, float tiltDegrees) {
            signX = flipX ? -1f : 1f;
            signY = flipY ? -1f : 1f;
            signZ = flipZ ? -1f : 1f;
            tilt = new Tilt(tiltDegrees);
        }

        /// <summary>
        /// F-43. The camera mount's pitch away from horizontal, in degrees, POSITIVE WHEN THE CAMERA
        /// IS PITCHED DOWN (nose toward the floor) — which is what every install looking at standing
        /// people does. 0 is a level mount and costs nothing.
        ///
        /// WHY THE SIGN IS YOURS TO SUPPLY. `f19_level.py` reads the pitch off the board's own
        /// BNO086 and writes it to evidence/oak_v4/f19/mount.json, but it records MAGNITUDE ONLY —
        /// its own note says "the IMU sign convention is not asserted", because the device
        /// calibration returns an identity IMU-to-camera extrinsic that was disproved directly. So
        /// the magnitude is measured and the direction is read off the physical mount by eye.
        /// Guessing the sign here would be worse than leaving this at 0: the wrong sign DOUBLES the
        /// error instead of removing it.
        /// </summary>
        public void SetTiltDegrees(float tiltDegrees) {
            tilt = new Tilt(tiltDegrees);
        }

        public float TiltDegrees {
            get {
                return tilt.Degrees;
            }
        }

        /// <summary>
        /// F-43. Rotate a sender-frame point out of the tilted camera and into a world-levelled
        /// frame, about the camera's X axis. Applied BEFORE the mirror signs, because the signs are
        /// a display convention applied to already-levelled data — mirroring first would rotate in
        /// the mirrored frame and tilt the body the wrong way whenever flipZ is on.
        ///
        /// THE FRAME THIS ASSUMES, verified rather than inherited: the sender emits X right, Y DOWN,
        /// Z forward. That is stated by oak_depth.py, both senders, f18_portrait.py and the F-19
        /// tools, and BOTH wire fields — the per-joint landmarks and the mid-hip "xyz" — come from
        /// the same back-projection, so one rotation is correct for both entry points below.
        /// (`ToUnityPosition` then passes Y through unflipped while `ToUnity` negates it. That
        /// asymmetry predates F-43 and is deliberately left alone; it is downstream of this
        /// rotation and does not affect it.)
        ///
        /// Portrait is already handled: f18_portrait rotates image, depth AND intrinsics together,
        /// so back-projection in the rotated frame returns the same X-right/Y-down/Z-forward
        /// semantics. The physical 90° roll is undone before the data reaches here, which leaves
        /// pitch as the only remaining misalignment — a rotation about X in both orientations.
        ///
        /// WHY THIS EXISTS. FloorY is a single scalar, learned from foot contacts. A camera pitched
        /// by θ shears the floor by tan(θ) metres per metre of depth, so one scalar cannot describe
        /// it: at 15° that is 0.27 m/m, which walks straight through the 0.08 m planted band in
        /// 30 cm of approach. F-18 measured this effect directly and read a slope of 0.338 m/m,
        /// implying ~19° of pitch — tan(19°) = 0.344, so the model and the measurement agree.
        /// </summary>
        private void Level(ref float y, ref float z) {
            Tilt t = tilt;
            if (t.Identity) {
                return;
            }
            float levelledY = y * t.Cos + z * t.Sin;
            float levelledZ = z * t.Cos - y * t.Sin;
            y = levelledY;
            z = levelledZ;
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
            Level(ref y, ref z);
            return new Vector3(x * signX, y * signY, z * signZ);
        }

        // World-position variant for the OAK-D spatial hip anchor. The depth sensor's xyz is X-right,
        // Y-UP, Z-forward — the OPPOSITE Y sense to the GHUM landmarks (Y-down). So Y passes straight
        // through to Unity's Y-up (jumping raises the avatar), while X/Z keep the mirror/forward signs
        // for parity with the pose. Using ToUnity here would double-invert Y (jump → avatar sinks).
        public Vector3 ToUnityPosition(float x, float y, float z) {
            Level(ref y, ref z);
            return new Vector3(x * signX, y, z * signZ);
        }
    }
}
