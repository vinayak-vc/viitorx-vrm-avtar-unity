namespace VirtualMirror.Core.Humanize {
    /// <summary>
    /// F-27 — the ANATOMY the humanized layer enforces: which landmark hangs off which, how fast a
    /// human joint can actually move, and which joint angles a human body cannot produce.
    ///
    /// SEPARATED FROM THE ALGORITHM ON PURPOSE. Every number here is a claim about human bodies, not
    /// about this codebase, and each one is sourced in its comment. Mixing them into the solver is how
    /// a tuning constant becomes indistinguishable from a measurement — which this project has already
    /// paid for once (F-26 §6). Anything in this file can be argued with on anatomical grounds alone.
    ///
    /// SPACE: <see cref="PoseFrame"/> landmarks are METRES, HIP-CENTRED, Y-UP (the sidecar emits
    /// hip-relative GHUM metres; <see cref="PoseSpaceConverter"/> flips Y). So mid-hip sits at ~origin,
    /// and a dropped joint — which the sidecar zero-fills — also sits at the origin. That collision is
    /// why <see cref="HumanizedSkeleton"/> tests distance-from-origin explicitly rather than trusting
    /// confidence alone.
    /// </summary>
    public static class HumanBodyModel {
        /// <summary>Virtual mid-hip slot. Not a tracked landmark — derived from 23/24 each frame.</summary>
        public const int MidHip = 33;

        /// <summary>Virtual mid-shoulder slot. Derived from 11/12 each frame.</summary>
        public const int MidShoulder = 34;

        /// <summary>Working-array size: the 33 tracked landmarks plus the two virtual joints.</summary>
        public const int SlotCount = 35;

        /// <summary>
        /// How a joint is allowed to behave. The distinction is not cosmetic: a TORSO joint that moves
        /// at 8 m/s is a tracking fault, while a HAND that does is a person waving.
        /// </summary>
        public enum JointClass {
            Torso,
            Head,
            Face,
            Limb,        // elbows, knees — driven by a big muscle, mid speed
            Extremity,   // wrists, ankles — fastest thing on a body
            Digit,       // fingers, heels, toes — small, fast, low value if wrong
        }

        /// <summary>
        /// One rigid segment: <see cref="Child"/> hangs off <see cref="Parent"/> at a fixed length.
        /// <see cref="Mirror"/> is the index of the opposite-side bone in <see cref="Bones"/>, or -1 for
        /// a midline bone. Left/right lengths are averaged through it, because real limbs are symmetric
        /// to within a couple of per cent and the tracker's are not.
        /// </summary>
        public struct Bone {
            public int Parent;
            public int Child;
            public JointClass Class;
            public int Mirror;

            public Bone(int parent, int child, JointClass jointClass, int mirror) {
                Parent = parent;
                Child = child;
                Class = jointClass;
                Mirror = mirror;
            }
        }

        /// <summary>
        /// The structural skeleton, PARENTS STRICTLY BEFORE CHILDREN so one forward pass rebuilds the
        /// whole body. Landmarks 1–10 (eyes/ears/mouth) are deliberately absent: nothing in the body
        /// retarget reads them (verified against the driver and the Kalidokit solver), so rebuilding
        /// them would be inventing structure for no consumer. They still get the per-joint plausibility,
        /// hold and velocity stages — they are simply not re-anchored to a bone length.
        /// </summary>
        public static readonly Bone[] Bones = {
            // --- trunk -------------------------------------------------------------------------
            new Bone(MidHip, 23, JointClass.Torso, 1),      //  0 mid-hip -> left hip
            new Bone(MidHip, 24, JointClass.Torso, 0),      //  1 mid-hip -> right hip
            new Bone(MidHip, MidShoulder, JointClass.Torso, -1), // 2 the trunk itself
            new Bone(MidShoulder, 11, JointClass.Torso, 4), //  3 mid-shoulder -> left shoulder
            new Bone(MidShoulder, 12, JointClass.Torso, 3), //  4 mid-shoulder -> right shoulder
            new Bone(MidShoulder, 0, JointClass.Head, -1),  //  5 neck -> nose
            // --- arms --------------------------------------------------------------------------
            new Bone(11, 13, JointClass.Limb, 8),           //  6 left upper arm
            new Bone(13, 15, JointClass.Extremity, 9),      //  7 left forearm
            new Bone(12, 14, JointClass.Limb, 6),           //  8 right upper arm
            new Bone(14, 16, JointClass.Extremity, 7),      //  9 right forearm
            // --- legs --------------------------------------------------------------------------
            new Bone(23, 25, JointClass.Limb, 12),          // 10 left thigh
            new Bone(25, 27, JointClass.Extremity, 13),     // 11 left shin
            new Bone(24, 26, JointClass.Limb, 10),          // 12 right thigh
            new Bone(26, 28, JointClass.Extremity, 11),     // 13 right shin
            // --- feet --------------------------------------------------------------------------
            new Bone(27, 29, JointClass.Digit, 16),         // 14 left heel
            new Bone(27, 31, JointClass.Digit, 17),         // 15 left toe
            new Bone(28, 30, JointClass.Digit, 14),         // 16 right heel
            new Bone(28, 32, JointClass.Digit, 15),         // 17 right toe
            // --- hands (the finger points the driver reads for the hand direction) --------------
            new Bone(15, 17, JointClass.Digit, 21),         // 18 left pinky
            new Bone(15, 19, JointClass.Digit, 22),         // 19 left index
            new Bone(15, 21, JointClass.Digit, 23),         // 20 left thumb
            new Bone(16, 18, JointClass.Digit, 18),         // 21 right pinky
            new Bone(16, 20, JointClass.Digit, 19),         // 22 right index
            new Bone(16, 22, JointClass.Digit, 20),         // 23 right thumb
        };

        /// <summary>
        /// Per-class speed ceiling, metres/second. These are OUTLIER REJECTORS, set ABOVE what a person
        /// in front of a mirror produces, not motion budgets: the requirement is explicitly that real
        /// movement must not be smoothed or delayed, so a ceiling that a real arm can reach would be a
        /// defect, not a safety margin. Sources: sprint hip translation peaks ~4 m/s; a thrown-punch
        /// wrist peaks ~8–10 m/s; a boxer's jab hand ~11 m/s. Everything a mirror user does is far
        /// below these, so in good tracking this stage is inert — which the stats verify per session
        /// rather than assume.
        /// </summary>
        public static float MaxSpeed(JointClass jointClass) {
            if (jointClass == JointClass.Torso) {
                return 4.0f;
            }
            if (jointClass == JointClass.Head) {
                return 4.0f;
            }
            if (jointClass == JointClass.Face) {
                return 4.0f;
            }
            if (jointClass == JointClass.Limb) {
                return 7.0f;
            }
            if (jointClass == JointClass.Extremity) {
                return 12.0f;
            }
            return 14.0f;   // Digit
        }

        // --- hinge limits -----------------------------------------------------------------------
        // The INTERIOR angle at the joint: 180 deg is a straight limb, smaller is more folded. Both
        // ends are anatomical facts rather than preferences.

        /// <summary>Elbow: full flexion brings the forearm to ~25–30 deg of the upper arm; the soft
        /// tissue of the biceps stops it before the bones meet. 25 deg is the permissive end.</summary>
        public const float ElbowMinDeg = 25f;

        /// <summary>Elbow: a straight arm is 180 deg. Healthy hyperextension is ~0–10 deg, and beyond
        /// that the joint is broken, so 185 deg is a generous ceiling rather than a cosmetic one.</summary>
        public const float ElbowMaxDeg = 185f;

        /// <summary>Knee: full flexion (heel to buttock) is ~30–35 deg interior.</summary>
        public const float KneeMinDeg = 30f;

        /// <summary>Knee: a straight leg is 180 deg; genu recurvatum past ~10 deg is pathological.</summary>
        public const float KneeMaxDeg = 185f;

        // THERE IS DELIBERATELY NO SHOULDER CONE, and this is a measured decision rather than an
        // omission. A cone about the trunk's DOWN axis is the wrong shape for a shoulder: the
        // gleno-humeral joint plus scapular rotation genuinely reaches 180 deg of elevation — an arm
        // flat against the ear — so the only cone a real shoulder violates is one of exactly 180 deg,
        // which constrains nothing. A 175 deg cone was implemented first and measured: it fired on
        // precisely ONE pose in a full arm sweep, arms-straight-overhead, moving the elbow 24 mm. That
        // is the single pose F-26 §4.1 records as this project's worst avatar failure, so the cone was
        // truncating the one thing it most needed to leave alone.
        //
        // The constraint a shoulder actually has is that the upper arm cannot pass THROUGH the torso,
        // which is a volume test, not a cone, and is not attempted here. `HumanizedSkeletonSelfTest`
        // carries the arms-overhead case as a permanent regression guard against re-adding a cone.

        /// <summary>
        /// Hip cone half-angle from the trunk's DOWN axis. Far less mobile than the shoulder, and
        /// unlike the shoulder a cone IS the right shape for it: ~120 deg flexion with the knee bent,
        /// ~45 deg abduction, ~30 deg extension. 135 deg is the permissive end of that envelope, and a
        /// thigh pointing further than that from the trunk's down axis is not a leg.
        /// </summary>
        public const float HipConeDeg = 135f;

        /// <summary>
        /// Head: the nose direction relative to the trunk's up axis. Neck flexion ~60 deg, extension
        /// ~70 deg, lateral ~45 deg; 75 deg covers all of them with room to spare.
        /// </summary>
        public const float NeckConeDeg = 75f;

        /// <summary>
        /// Shoulder line vs hip line, about the trunk axis. Real thoracic + lumbar differential rotation
        /// tops out near 60–70 deg. 90 deg is an IMPOSSIBILITY REJECTOR, chosen deliberately wide so it
        /// cannot interfere with Torso V5's composition or V6's wrap guard, which own the yaw POLICY
        /// (ADR-037 / ADR-045). This layer only refuses what no spine can do.
        /// </summary>
        public const float TorsoTwistDeg = 90f;

        /// <summary>
        /// A non-hip landmark this close to the origin is the sidecar's zero-fill, not a body part: the
        /// frame is hip-centred, so the origin IS the mid-hip and no elbow, knee, wrist or ankle is ever
        /// 2 cm from it. This is the direct test for the limb-collapse failure that P0-1 gates
        /// downstream and P1-3 refuses to interpolate across.
        /// </summary>
        public const float CollapseRadiusM = 0.02f;

        /// <summary>
        /// Nothing on a human body is further than this from the mid-hip. A 2.72 m man's fingertips
        /// reach ~1.4 m from his hips with the arm overhead; 1.6 m is past any real anatomy, so a
        /// landmark beyond it is a depth blow-up or a parse error, never a person.
        /// </summary>
        public const float BodyRadiusM = 1.6f;

        /// <summary>Plausible human bone lengths, metres, for the calibration's own sanity check.
        /// A "bone" measured outside this band is rejected as a calibration sample.</summary>
        public const float MinBoneM = 0.01f;

        /// <summary>See <see cref="MinBoneM"/>. A femur is ~0.45 m on a very tall adult.</summary>
        public const float MaxBoneM = 0.80f;
    }
}
