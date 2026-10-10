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
        /// measured 127 mm above the real contact point on the clean single-subject clip (and 81 mm on
        /// the crowded one), which is the offset any ankle-based floor effect has been carrying.</summary>
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
        // this: the per-frame lowest foot point has a standard deviation of 36 mm on a single subject
        // at 1.4 m, and smoothing improves that to 21 mm over a 2 s window.
        //
        // The second figure F-29 reported - 66 mm rising to 81 mm under smoothing, attributed to a
        // "2.9 m" subject - came from a clip that turns out to contain SEVEN dancers, where the
        // single-person crop migrates between them (F-32). Smoothing "hurting" there is what tracking
        // a moving target between different bodies looks like, not depth drift. Treat that row as a
        // measurement of identity contamination. The genuine single-subject floor accuracy beyond
        // ~2 m has NOT been measured, so do not assume either that it holds or that it fails.
        //
        // The smoother is asymmetric on purpose. It follows a DESCENDING floor almost immediately and
        // rises slowly, because a foot leaving the ground must not drag the floor up with it — the
        // floor is where the lowest foot has recently been, not where the feet are now.
        private const float FloorFallTau = 0.05f;
        private const float FloorRiseTau = 0.60f;

        /// <summary>A foot within this of the estimated floor counts as planted. Set from the
        /// measured per-frame spread (36 mm on a clean single subject, 66 mm on the contaminated
        /// crowded clip) with margin, so ordinary estimator noise does not read as the foot
        /// lifting.</summary>
        public const float PlantedBandMetres = 0.08f;

        /// <summary>A planted foot must also be slower than this. A foot passing through the floor
        /// band at speed is mid-stride, not planted.</summary>
        public const float PlantedMaxSpeed = 0.35f;

        /// <summary>
        /// F-34 IDENTITY GUARD. Above this apparent mid-hip speed the history is thrown away rather
        /// than differenced, because no person moves this fast: a sprinter's hip travels at about
        /// 8 m/s, so 12 m/s cannot be a body and must be the FRAME of reference having changed
        /// underneath it.
        ///
        /// Expressed as a SPEED and multiplied by the frame's dt, not as a fixed distance, so the
        /// same guard holds at 16 fps (multi-person) and at 60 fps (a single person on a fast host).
        /// A fixed distance would be either useless at low frame rates or trigger constantly at high
        /// ones.
        ///
        /// TWO THINGS IT CATCHES, and one it does NOT - stated because the gap is the whole reason
        /// the multi-person experiences are shaped the way they are:
        ///
        ///  * A GROUNDING SNAP. <see cref="TrackedStage"/> snaps its ground offset the first time it
        ///    has a floor, which can move the staging origin by the better part of a metre in one
        ///    frame. Every joint's world position moves with it, so the very next frame differences
        ///    two positions that were measured against different origins and reports a spike on the
        ///    WHOLE body. That spike is above every experience's strike gate.
        ///  * A GROSS IDENTITY SWITCH, where the pose under one id jumps to a body standing
        ///    somewhere else in the room.
        ///
        ///  * IT DOES NOT CATCH A SLOW SWITCH. The live two-person session measured an identity
        ///    migrating between two humans at 0.015 m per frame - roughly 0.24 m/s, three orders of
        ///    magnitude below this guard and well inside ordinary movement. Nothing in the geometry
        ///    announces it. That is why no experience here may accumulate per-person state and
        ///    expect it to stay attached to the right person; see the F-34 report.
        /// </summary>
        public const float TeleportSpeed = 12f;

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

            // Identity/origin guard, BEFORE anything is differenced. Valid and Centre still hold
            // last frame's answer at this point, which is exactly what has to be compared against.
            if (hasPrevious && Valid) {
                Vector3 hipLeft = origin + frame.GetLandmark((JointId)23).Position * scale;
                Vector3 hipRight = origin + frame.GetLandmark((JointId)24).Position * scale;
                Vector3 candidate = 0.5f * (hipLeft + hipRight);
                if ((candidate - Centre).magnitude > TeleportSpeed * dt) {
                    ResetHistory();
                }
            }
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

        /// <summary>
        /// Forget every temporal quantity derived by differencing against the previous frame - speed,
        /// velocity, foot contact - without disturbing the pose itself.
        ///
        /// WHY THIS IS NEEDED AT ALL. Speed and Velocity are differences between THIS frame's world
        /// position and the LAST one's. That is only meaningful while both belong to the same body
        /// measured against the same origin. When either changes, the difference is not a slow joint
        /// or a fast one - it is a meaningless number, and it is large. One such frame is enough to
        /// fire every strike gate in the experiences at once (0.9 m/s in Bubble Pop, 0.55 m/s in
        /// Objects) and to un-plant every planted foot (which needs Speed below 0.35 m/s), so a
        /// person standing still can score a handful of points and lose their footprints in the same
        /// frame, for no reason they can see.
        ///
        /// THE FLOOR IS DELIBERATELY KEPT. It is a property of the ROOM, not of the body: the same
        /// floor is still under whoever is standing there now. Re-estimating it from scratch would
        /// make every floor effect jump at the exact moment the guard fired, which is the visible
        /// symptom this exists to prevent.
        /// </summary>
        public void ResetHistory() {
            hasPrevious = false;
            int i = 0;
            while (i < Speed.Length) {
                Speed[i] = 0f;
                Velocity[i] = Vector3.zero;
                i = i + 1;
            }
            Energy = 0f;
            int k = 0;
            while (k < Planted.Length) {
                Planted[k] = false;
                k = k + 1;
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
