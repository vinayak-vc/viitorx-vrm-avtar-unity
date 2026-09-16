using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 — one frame of the tracked body, in WORLD space and with the things a visual mode actually
    /// wants: where each joint is, how fast it is moving, and how much the whole body is moving.
    ///
    /// WHY A SEPARATE TYPE FROM <see cref="PoseFrame"/>. PoseFrame is the tracking contract: hip-centred
    /// metres, provider space, confidence. A renderer needs world positions and SPEED, and speed is a
    /// derived quantity that every mode would otherwise compute for itself — three times, three ways,
    /// with three different smoothing choices. Computing it once here is what lets the three modes agree
    /// about what "moving fast" means, which is the only reason they look like one product.
    ///
    /// Reused in place every frame; a mode reads it immediately and holds no reference.
    /// </summary>
    public sealed class SkeletonPose {
        /// <summary>Bone pairs for drawing, matching PoseDebugSkeleton's list so both read as the same
        /// body. Torso first, then limbs, then the head spokes.</summary>
        public static readonly int[] Bones = {
            11, 12, 11, 13, 13, 15, 12, 14, 14, 16, 11, 23, 12, 24, 23, 24,
            23, 25, 25, 27, 27, 31, 24, 26, 26, 28, 28, 32, 0, 11, 0, 12,
            // F-29 FEET. The heels (29/30) were never drawn, so each foot was a single line from the
            // ankle to the big toe and the body appeared to stand on its toe-tips. The sidecar has
            // been sending all four foot points since the whole-body sender shipped — measured at
            // 431/431 and 330/330 frames on the two regression clips — so this is presentation
            // catching up with data that was already on the wire, not a new capability.
            // Ankle-heel-toe per side makes each foot a triangle, which is what gives it a direction
            // on the floor and lets a viewer see the foot roll rather than just translate.
            27, 29, 29, 31, 28, 30, 30, 32,
        };

        /// <summary>Joints worth hanging an effect off: hands and feet.</summary>
        public static readonly int[] Extremities = { 15, 16, 27, 28 };

        /// <summary>The four points that actually touch the ground. The ankle is NOT one of them: it
        /// measured 81-127 mm above the real contact point across the two regression clips, which is
        /// the offset any ankle-based floor effect has been carrying.</summary>
        public static readonly int[] FootContacts = { 29, 30, 31, 32 };

        public readonly Vector3[] World = new Vector3[PoseFrame.LandmarkCount];
        public readonly float[] Confidence = new float[PoseFrame.LandmarkCount];
        public readonly float[] Speed = new float[PoseFrame.LandmarkCount];
        public readonly bool[] Present = new bool[PoseFrame.LandmarkCount];

        // F-30: per-joint world velocity. Speed alone says a hand is moving but not WHICH WAY, and
        // anything that reacts directionally to the body - a fluid being stirred, an object being
        // struck - needs the direction. Derived here for the same reason Speed is: so every consumer
        // shares one definition instead of differencing positions with its own smoothing.
        //
        // READ THIS BEFORE USING ITS MAGNITUDE. |Velocity| IS NOT Speed, and the gap is not a bug.
        // Speed smooths the MAGNITUDE of each frame's displacement; Velocity smooths the VECTOR. For
        // straight-line motion they agree exactly (measured: 1.000 vs 1.000 m/s). For oscillating
        // motion the vector average partly CANCELS while the magnitude average does not, so
        // |Velocity| < Speed - measured at 33% lower on a dancing subject whose hands reverse
        // constantly. |Velocity| <= Speed always, by the triangle inequality.
        //
        // So: take DIRECTION from Velocity and MAGNITUDE from Speed. Using |Velocity| as a speed
        // silently under-reports exactly the vigorous movement an experience most wants to reward.
        public readonly Vector3[] Velocity = new Vector3[PoseFrame.LandmarkCount];

        // F-29: distance from the CAMERA to each joint, in real metres. Not derivable from World[],
        // which has been through the scene's own origin and scale — so it is computed during Fill
        // from the two pieces that carry it: the hip-relative landmark and the measured mid-hip.
        private readonly float[] cameraDepth = new float[PoseFrame.LandmarkCount];
        private bool hasCameraDepth;

        private readonly Vector3[] previous = new Vector3[PoseFrame.LandmarkCount];
        private bool hasPrevious;

        /// <summary>True when a body was tracked this frame.</summary>
        public bool Valid;

        /// <summary>Mid-hip in world space — where the body is.</summary>
        public Vector3 Centre;

        /// <summary>Whole-body movement, 0 = still, 1 = vigorous. Drives every mode's intensity so
        /// they respond to the person in the same way.</summary>
        public float Energy;

        /// <summary>Speed above which a joint counts as "fast" for colour and emission purposes. A
        /// hand wave is ~1-2 m/s; this sits at the top of ordinary movement so the hot end of every
        /// palette means something rather than being reached by standing still.</summary>
        public const float FastSpeed = 2.5f;

        // ---- F-29 FLOOR ------------------------------------------------------------------------
        // Estimated from the four FOOT contact points, not the ankles. Measured on the two
        // regression clips (docs/F29): the lowest foot point sits 127 mm below the lowest ankle on
        // video.webm and 81 mm below it on 456.webm, so every ankle-based floor effect has been
        // spawning roughly a hand's width above the ground the person is standing on.
        //
        // WHAT THE SAME MEASUREMENT SAYS ABOUT RELIABILITY, because it bounds what should be built on
        // this: the per-frame lowest foot point has a standard deviation of 36 mm at 1.4 m subject
        // distance and 66 mm at 2.9 m. Smoothing helps at close range (36 -> 21 mm over a 2 s window)
        // and HURTS at distance (66 -> 81 mm), because at 2.9 m the error is a slow drift rather than
        // noise and a longer window just tracks the drift. So: an effect that wants a floor to
        // roughly sit on is well served here; one that needs true contact detection at 3 m is not.
        //
        // The smoother is asymmetric on purpose. It follows a DESCENDING floor almost immediately and
        // rises slowly, because a foot leaving the ground must not drag the floor up with it — the
        // floor is where the lowest foot has recently been, not where the feet are now.
        private const float FloorFallTau = 0.05f;
        private const float FloorRiseTau = 0.60f;

        /// <summary>A foot within this of the estimated floor counts as planted. Set from the
        /// measured per-frame spread (36 mm at close range, 66 mm at distance) with margin, so
        /// ordinary estimator noise does not read as the foot lifting.</summary>
        public const float PlantedBandMetres = 0.08f;

        /// <summary>A planted foot must also be slower than this. A foot passing through the floor
        /// band at speed is mid-stride, not planted.</summary>
        public const float PlantedMaxSpeed = 0.35f;

        /// <summary>Estimated floor height in world Y. Valid only when <see cref="HasFloor"/>.</summary>
        public float FloorY;

        /// <summary>True once at least one foot contact point has been seen.</summary>
        public bool HasFloor;

        /// <summary>Per-foot planted state, indexed as <see cref="FootContacts"/>. What "reliable
        /// floor contact" actually amounts to in practice, exposed so a mode can use it and an
        /// operator can watch it be right or wrong.</summary>
        public readonly bool[] Planted = new bool[4];

        /// <summary>The pipeline's own verdict about this pose — per-joint tracking state, depth
        /// provenance, ownership, latency. Null when the provider has no telemetry yet. Read-only
        /// here: no mode may change what it draws because of this, only what it REPORTS, or the
        /// distinction between the tracking and its diagnosis stops meaning anything.</summary>
        public TrackingTelemetry Telemetry;

        /// <summary>
        /// Fill from a tracked frame. <paramref name="origin"/> and <paramref name="scale"/> place the
        /// hip-centred body in the scene. Speed is measured in WORLD units per second so it is directly
        /// comparable to the scene, and is smoothed lightly — raw per-frame speed from a 30 Hz tracker
        /// flickers hard enough to make colour and particle rates strobe.
        /// </summary>
        public void Fill(PoseFrame frame, Vector3 origin, float scale, float deltaSeconds) {
            if (frame == null || !frame.IsValid) {
                Valid = false;
                hasPrevious = false;
                Energy = 0f;
                // The floor itself is KEPT: it is a property of the room, and a person stepping out
                // of frame does not move it. Re-estimating from scratch on their return would make
                // every floor effect jump on re-acquisition. Contact, though, is a property of the
                // body and there is no body, so nothing is planted.
                int k = 0;
                while (k < Planted.Length) {
                    Planted[k] = false;
                    k = k + 1;
                }
                return;
            }
            float dt = deltaSeconds > 1e-4f ? deltaSeconds : 1f / 60f;
            float smooth = 1f - Mathf.Exp(-dt / 0.08f);
            float total = 0f;
            int counted = 0;
            // Absolute camera depth per joint: the landmark is hip-relative and RootPositionMetres is
            // the measured mid-hip, so their sum is the joint's distance from the camera. Both terms
            // carry the SAME converter Z sign, so the sum is ±(true depth) and the magnitude is the
            // answer regardless of how flipZ is configured. Depth toward the camera is always
            // positive - the subject cannot be behind the lens - so no information is lost by it.
            float rootDepth = frame.HasRootPosition ? frame.RootPositionMetres.z : 0f;
            hasCameraDepth = frame.HasRootPosition;
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                PoseLandmark lmk = frame.GetLandmark((JointId)i);
                Vector3 world = origin + lmk.Position * scale;
                Confidence[i] = lmk.Confidence;
                Present[i] = lmk.Confidence > 0.05f;
                cameraDepth[i] = hasCameraDepth ? Mathf.Abs(lmk.Position.z + rootDepth) : 0f;
                if (hasPrevious && Present[i]) {
                    Vector3 delta = (world - previous[i]) / dt;
                    float instant = delta.magnitude;
                    Speed[i] = Speed[i] + smooth * (instant - Speed[i]);
                    Velocity[i] = Velocity[i] + smooth * (delta - Velocity[i]);
                    total = total + Speed[i];
                    counted = counted + 1;
                } else if (!Present[i]) {
                    Speed[i] = Speed[i] * (1f - smooth);
                    Velocity[i] = Velocity[i] * (1f - smooth);
                }
                World[i] = world;
                previous[i] = world;
                i = i + 1;
            }
            hasPrevious = true;
            Valid = true;
            Centre = 0.5f * (World[23] + World[24]);
            float mean = counted > 0 ? total / counted : 0f;
            Energy = Mathf.Clamp01(mean / FastSpeed);
            UpdateFloor(dt);
        }

        // Track the floor from the foot contacts, then decide which feet are on it. See the constants
        // above for why this is asymmetric and for what the measured error actually is.
        private void UpdateFloor(float dt) {
            float lowest = float.MaxValue;
            int i = 0;
            while (i < FootContacts.Length) {
                int joint = FootContacts[i];
                if (Present[joint] && World[joint].y < lowest) {
                    lowest = World[joint].y;
                }
                i = i + 1;
            }
            if (lowest < float.MaxValue) {
                if (!HasFloor) {
                    FloorY = lowest;
                    HasFloor = true;
                } else {
                    float tau = lowest < FloorY ? FloorFallTau : FloorRiseTau;
                    FloorY = FloorY + (1f - Mathf.Exp(-dt / tau)) * (lowest - FloorY);
                }
            }

            i = 0;
            while (i < FootContacts.Length) {
                int joint = FootContacts[i];
                Planted[i] = HasFloor && Present[joint]
                             && World[joint].y - FloorY < PlantedBandMetres
                             && Speed[joint] < PlantedMaxSpeed;
                i = i + 1;
            }
        }

        /// <summary>Distance from the camera to a joint, in metres. Zero when the sender supplies no
        /// measured mid-hip, which is the honest answer rather than a plausible-looking guess.</summary>
        public float CameraDepth(int joint) {
            if (!hasCameraDepth || joint < 0 || joint >= cameraDepth.Length) {
                return 0f;
            }
            return cameraDepth[joint];
        }

        /// <summary>Height of a joint above the estimated floor, or 0 when no floor is known yet.</summary>
        public float HeightAboveFloor(int joint) {
            if (!HasFloor || joint < 0 || joint >= World.Length) {
                return 0f;
            }
            return World[joint].y - FloorY;
        }

        /// <summary>Normalised 0..1 speed for one joint, for colour and size.</summary>
        public float Heat(int joint) {
            if (joint < 0 || joint >= Speed.Length) {
                return 0f;
            }
            return Mathf.Clamp01(Speed[joint] / FastSpeed);
        }

        /// <summary>Both endpoints of a bone were observed.</summary>
        public bool BoneVisible(int a, int b) {
            return Present[a] && Present[b];
        }
    }
}
