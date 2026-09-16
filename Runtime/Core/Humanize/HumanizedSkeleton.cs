using UnityEngine;

namespace VirtualMirror.Core.Humanize {
    /// <summary>
    /// F-27 — the HUMANIZED SKELETON layer. Sits between the existing filtering and the existing
    /// retarget and makes the pose behave like a body before the avatar ever sees it:
    ///
    ///     tracking -> existing filtering -> [ HumanizedSkeleton ] -> existing retarget -> avatar
    ///
    /// THE PREMISE: tracking output is a SUGGESTION. The tracker reports 33 positions independently,
    /// every frame, with no knowledge that they belong to one body. Nothing in it prevents a forearm
    /// from changing length, an elbow from folding through itself, a knee from bending forward, or a
    /// dropped joint from being reported at the origin. Downstream, the avatar's bones are rigid and
    /// can only be AIMED, never placed (F-26 §3), so every one of those becomes an aim error the
    /// retarget is structurally unable to absorb. This layer refuses them first.
    ///
    /// WHAT IT DELIBERATELY DOES NOT DO:
    ///  * It adds NO smoothing filter. The requirement is explicitly that real movement must not be
    ///    smoothed or delayed, and this project already owns three smoothing stages (the sidecar's
    ///    One-Euro, P1-1's tracker, P1-3's interpolation). A fourth would be latency for its own sake.
    ///    Every stage here is either a per-frame geometric projection — which cannot lag — or fires
    ///    only on a fault. <see cref="HumanizedStats.FramesUntouched"/> is how that claim is checked
    ///    per session rather than asserted.
    ///  * It does NOT change confidence. A held joint keeps the confidence it arrived with, so P0-1's
    ///    LimbGate still sees an unobserved limb as unobserved and holds the ROTATION exactly as
    ///    before. This layer fixes the POSITION so that the consumers which read landmarks WITHOUT a
    ///    gate — the Kalidokit torso solve reads 11/12/23/24 with no validation at all — get a human
    ///    input instead of a zero-filled one. Fabricating confidence here would silently disable a
    ///    protection that already works.
    ///  * It does NOT own torso yaw. Torso V5's composition and V6's wrap guard decide yaw POLICY
    ///    (ADR-037 / ADR-045). The twist limit here is set at 90 deg — past any human spine — so it can
    ///    only ever refuse the impossible, never compete with them.
    ///  * It does NOT touch the root position. Walk/jump translation is a separate channel with its own
    ///    smoothing and its own open question in F-21; conflating them would tangle two investigations.
    ///
    /// ORDER MATTERS and is not arbitrary: reject nonsense, then clamp teleports, then rebuild the body
    /// structurally, then enforce joint limits, then blend re-acquisitions. Rebuilding before rejecting
    /// would bake a bad sample into the bone lengths; clamping after rebuilding would move a joint the
    /// structure had already placed correctly.
    ///
    /// Pure logic, no Unity object model, allocation-free after construction — so it can be exercised
    /// by <c>HumanizedSkeletonSelfTest</c> with synthetic poses and injected faults, with no Editor,
    /// no avatar and no camera.
    /// </summary>
    public sealed class HumanizedSkeleton {
        // "Was this joint emitted at all", NOT "is it good enough to drive a limb". The sidecar
        // zero-fills a dropped joint to confidence 0, so this floor separates emitted-from-dropped.
        // The quality question belongs to P0-1's limbConfidenceThreshold (0.3) downstream, and setting
        // this to the same value would preempt a gate that already works.
        private const float DefaultConfidenceFloor = 0.05f;

        // A velocity clamp that never yields is indistinguishable from a broken tracker. If a joint
        // keeps demanding a big move in a consistent direction for this many consecutive frames, the
        // motion is real and the clamp gets out of the way. At ~30 Hz this is ~170 ms: long enough that
        // a single-frame teleport cannot pass, short enough that a genuine fast gesture is not dragged.
        private const int JumpAcceptFrames = 5;

        // Re-acquire blend. Short on purpose: it exists so a returning joint does not SNAP, not to
        // smooth it. Longer would be visible as the avatar lagging its own tracking on every recovery.
        private const float DefaultRecoverSeconds = 0.15f;

        // Below this the knee back-bend fix is not applied: a barely-bent leg has no meaningful bend
        // direction, and forcing one on it would invent a pose from noise.
        private const float KneeBendActiveDeg = 170f;

        // A landmark moved by less than this is treated as untouched. 1 mm is far below the tracker's
        // own noise and below anything visible on the avatar.
        private const float TouchEpsilonM = 0.001f;

        private readonly PoseFrame output = new PoseFrame();
        private readonly BoneLengthCalibrator calibrator = new BoneLengthCalibrator();

        private readonly Vector3[] observed = new Vector3[HumanBodyModel.SlotCount];
        private readonly float[] confidence = new float[HumanBodyModel.SlotCount];
        private readonly bool[] valid = new bool[HumanBodyModel.SlotCount];
        private readonly Vector3[] rebuilt = new Vector3[HumanBodyModel.SlotCount];
        private readonly Vector3[] lastAccepted = new Vector3[HumanBodyModel.SlotCount];
        private readonly bool[] hasLastAccepted = new bool[HumanBodyModel.SlotCount];
        private readonly int[] clampStreak = new int[HumanBodyModel.SlotCount];
        private readonly Vector3[] clampDir = new Vector3[HumanBodyModel.SlotCount];
        private readonly bool[] wasValid = new bool[HumanBodyModel.SlotCount];
        private readonly float[] recoverRemaining = new float[HumanBodyModel.SlotCount];

        // Held direction per bone, expressed in that bone's PARENT SEGMENT frame (or the body frame for
        // trunk bones). Holding in a local frame rather than a world position is what makes a held limb
        // swing with the body instead of detaching from it — the difference between a hold that reads
        // as "the arm is still there" and one that reads as "the avatar is broken".
        private readonly Vector3[] heldLocalDir = new Vector3[HumanBodyModel.Bones.Length];
        private readonly bool[] hasHeldLocalDir = new bool[HumanBodyModel.Bones.Length];

        private Vector3 bodyRight = Vector3.right;
        private Vector3 bodyUp = Vector3.up;
        private Vector3 bodyForward = Vector3.forward;
        // False when neither the feet nor the nose were observed this frame. Every FRONT/BACK test
        // stands down when it is false: a guess about which way a body faces is worse than no test.
        private bool hasBodyForward;

        private float confidenceFloor = DefaultConfidenceFloor;
        private float recoverSeconds = DefaultRecoverSeconds;
        private bool enforceAngles = true;
        private bool enforceBoneLengths = true;
        private bool enforceVelocity = true;

        private HumanizedStats stats;
        private HumanizedStats frameStats;   // snapshot at frame start, to detect "did anything fire"

        /// <summary>Cumulative record of what this layer has done. See <see cref="HumanizedStats"/>.</summary>
        public HumanizedStats Stats {
            get {
                return stats;
            }
        }

        /// <summary>The learned skeleton, exposed for diagnostics and for the before/after report.</summary>
        public BoneLengthCalibrator Calibration {
            get {
                return calibrator;
            }
        }

        /// <summary>
        /// Live A/B switches. Every stage can be disabled INDEPENDENTLY, because a correction layer
        /// whose contribution cannot be isolated cannot be shown to help — which is how this project
        /// arrived at an avatar nobody could prove was right (F-26 §5.1).
        /// </summary>
        public void SetStages(bool velocity, bool boneLengths, bool angles) {
            enforceVelocity = velocity;
            enforceBoneLengths = boneLengths;
            enforceAngles = angles;
        }

        public void SetTuning(float confidenceFloorValue, float recoverSecondsValue) {
            confidenceFloor = Mathf.Clamp01(confidenceFloorValue);
            recoverSeconds = Mathf.Max(0f, recoverSecondsValue);
        }

        /// <summary>Forget the learned skeleton and all temporal state. Called on a new tracking
        /// session so one subject's bone lengths and held poses never leak into another's.</summary>
        public void Reset() {
            calibrator.Reset();
            int i = 0;
            while (i < HumanBodyModel.SlotCount) {
                hasLastAccepted[i] = false;
                clampStreak[i] = 0;
                wasValid[i] = false;
                recoverRemaining[i] = 0f;
                i = i + 1;
            }
            int b = 0;
            while (b < HumanBodyModel.Bones.Length) {
                hasHeldLocalDir[b] = false;
                b = b + 1;
            }
        }

        public void ResetStats() {
            stats.Reset();
        }

        /// <summary>
        /// Humanize one frame. Returns an internally-owned frame that is valid until the next call —
        /// the same reuse contract <see cref="PoseFrame"/> already documents, so this adds no
        /// allocation to the per-frame path.
        ///
        /// An INVALID input (no person tracked) is passed straight through as invalid. This layer will
        /// not fabricate a body where the tracker reports none; holding a whole person indefinitely is
        /// the freeze that F-20A's failsafe exists to prevent, and that decision belongs to the
        /// failsafe, not here.
        /// </summary>
        public PoseFrame Process(PoseFrame input, float deltaSeconds) {
            if (input == null || !input.IsValid) {
                output.MarkInvalid(input != null ? input.TimestampSeconds : 0.0);
                return output;
            }
            float dt = deltaSeconds > 0f ? deltaSeconds : 1f / 30f;
            frameStats = stats;
            stats.FramesProcessed = stats.FramesProcessed + 1L;

            Gather(input);
            ClassifyValidity();
            if (enforceVelocity) {
                ClampVelocity(dt);
            }
            BuildBodyFrame();
            LearnBoneLengths();
            Rebuild(dt);
            if (enforceAngles) {
                FixKneeDirection();
            }
            Emit(input);
            TallyFrame();
            return output;
        }

        // ---------------------------------------------------------------------------------------
        // 1. Gather: the tracked landmarks plus the two virtual joints the body hangs from.
        // ---------------------------------------------------------------------------------------
        private void Gather(PoseFrame input) {
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                PoseLandmark lmk = input.GetLandmark((JointId)i);
                observed[i] = lmk.Position;
                confidence[i] = lmk.Confidence;
                i = i + 1;
            }
            // The virtual joints inherit the WEAKER of their two sources: a mid-hip built from one good
            // hip and one dropped hip is half-way to the origin, which is the collapse this whole layer
            // exists to refuse. min() makes that case fail the confidence floor instead of averaging.
            observed[HumanBodyModel.MidHip] = 0.5f * (observed[23] + observed[24]);
            confidence[HumanBodyModel.MidHip] = Mathf.Min(confidence[23], confidence[24]);
            observed[HumanBodyModel.MidShoulder] = 0.5f * (observed[11] + observed[12]);
            confidence[HumanBodyModel.MidShoulder] = Mathf.Min(confidence[11], confidence[12]);
        }

        // ---------------------------------------------------------------------------------------
        // 2. Reject what cannot be a body part. Four separate counters, because "the tracker dropped
        //    this joint" and "the depth blew up" are different faults with different fixes upstream.
        // ---------------------------------------------------------------------------------------
        private void ClassifyValidity() {
            int i = 0;
            while (i < HumanBodyModel.SlotCount) {
                Vector3 p = observed[i];
                bool ok = true;
                if (!IsFinite(p)) {
                    stats.RejectedNonFinite = stats.RejectedNonFinite + 1L;
                    ok = false;
                } else if (confidence[i] < confidenceFloor) {
                    stats.RejectedLowConfidence = stats.RejectedLowConfidence + 1L;
                    ok = false;
                } else if (p.sqrMagnitude > HumanBodyModel.BodyRadiusM * HumanBodyModel.BodyRadiusM) {
                    stats.RejectedOutOfBody = stats.RejectedOutOfBody + 1L;
                    ok = false;
                } else if (!IsHipLike(i)
                           && p.sqrMagnitude < HumanBodyModel.CollapseRadiusM * HumanBodyModel.CollapseRadiusM) {
                    // The frame is hip-centred, so the origin IS the mid-hip. A wrist reported there is
                    // the sidecar's zero-fill wearing a confidence value, not a wrist.
                    stats.RejectedOrigin = stats.RejectedOrigin + 1L;
                    ok = false;
                }
                valid[i] = ok;
                i = i + 1;
            }
        }

        // Only these legitimately sit near the origin in a hip-centred frame.
        private static bool IsHipLike(int slot) {
            return slot == HumanBodyModel.MidHip || slot == 23 || slot == 24;
        }

        private static bool IsFinite(Vector3 v) {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        // ---------------------------------------------------------------------------------------
        // 3. Teleport clamp. A step larger than the joint class can physically produce is capped, but
        //    a cap that never yields would permanently lag a genuinely fast movement — so a demand
        //    that PERSISTS is accepted as real. This is the standard outlier-versus-motion
        //    discriminator, and without the second half it would be a latency bug wearing a safety hat.
        // ---------------------------------------------------------------------------------------
        private void ClampVelocity(float dt) {
            int i = 0;
            while (i < HumanBodyModel.SlotCount) {
                if (!valid[i] || !hasLastAccepted[i]) {
                    clampStreak[i] = 0;
                    i = i + 1;
                    continue;
                }
                Vector3 previous = lastAccepted[i];
                Vector3 step = observed[i] - previous;
                float maxStep = HumanBodyModel.MaxSpeed(ClassOf(i)) * dt;
                float len = step.magnitude;
                if (len > maxStep && maxStep > 0f) {
                    // The streak only counts when the demand keeps pointing the SAME WAY. Without that
                    // test, a joint flickering between two far-apart positions — which is a tracker
                    // failing, not a person moving — would accumulate a streak and be waved through.
                    Vector3 dir = step / len;
                    bool consistent = clampStreak[i] > 0 && Vector3.Dot(dir, clampDir[i]) > 0.5f;
                    clampStreak[i] = consistent ? clampStreak[i] + 1 : 1;
                    clampDir[i] = dir;
                    if (clampStreak[i] >= JumpAcceptFrames) {
                        // Sustained in one direction for ~170 ms: the person really moved.
                        stats.JumpAccepted = stats.JumpAccepted + 1L;
                        clampStreak[i] = 0;
                    } else {
                        observed[i] = previous + dir * maxStep;
                        stats.VelocityClamped = stats.VelocityClamped + 1L;
                    }
                } else {
                    clampStreak[i] = 0;
                }
                i = i + 1;
            }
        }

        private static HumanBodyModel.JointClass ClassOf(int slot) {
            if (slot == HumanBodyModel.MidHip || slot == HumanBodyModel.MidShoulder
                || slot == 11 || slot == 12 || slot == 23 || slot == 24) {
                return HumanBodyModel.JointClass.Torso;
            }
            if (slot == 0) {
                return HumanBodyModel.JointClass.Head;
            }
            if (slot >= 1 && slot <= 10) {
                return HumanBodyModel.JointClass.Face;
            }
            if (slot == 13 || slot == 14 || slot == 25 || slot == 26) {
                return HumanBodyModel.JointClass.Limb;
            }
            if (slot == 15 || slot == 16 || slot == 27 || slot == 28) {
                return HumanBodyModel.JointClass.Extremity;
            }
            return HumanBodyModel.JointClass.Digit;
        }

        // ---------------------------------------------------------------------------------------
        // 4. The body frame every angular limit is expressed in. Built from the trunk itself, so it
        //    travels with the person: a limit measured against world axes would tighten and loosen as
        //    the user turned, which is the sort of orientation-dependent behaviour this project has
        //    already been bitten by in the torso channel.
        // ---------------------------------------------------------------------------------------
        private void BuildBodyFrame() {
            Vector3 hip = valid[HumanBodyModel.MidHip] ? observed[HumanBodyModel.MidHip] : Vector3.zero;
            Vector3 shoulder = valid[HumanBodyModel.MidShoulder]
                ? observed[HumanBodyModel.MidShoulder] : hip + Vector3.up * 0.4f;
            Vector3 up = shoulder - hip;
            if (up.sqrMagnitude < 1e-8f) {
                up = Vector3.up;
            }
            bodyUp = up.normalized;

            Vector3 across = Vector3.zero;
            if (valid[23] && valid[24]) {
                across = observed[23] - observed[24];
            } else if (valid[11] && valid[12]) {
                across = observed[11] - observed[12];
            }
            if (across.sqrMagnitude < 1e-8f) {
                across = Vector3.right;
            }
            // Orthogonalise the lateral axis against up so the frame stays a proper basis even when the
            // shoulders and hips disagree (which is exactly the torso-twist case).
            across = across - bodyUp * Vector3.Dot(across, bodyUp);
            if (across.sqrMagnitude < 1e-8f) {
                across = Vector3.Cross(bodyUp, Vector3.forward);
                if (across.sqrMagnitude < 1e-8f) {
                    across = Vector3.Cross(bodyUp, Vector3.right);
                }
            }
            bodyRight = across.normalized;

            // WHICH WAY IS THE BODY FACING. Deriving forward from cross(hipLine, up) carries a
            // handedness that depends on whether this frame's landmark 23 is on the +X or -X side —
            // and the mirror/swap conditioning happens LATER, inside the driver, so that is not knowable
            // here. Getting it backwards inverts every front/back test: the knee-direction fix then
            // reflects a perfectly normal leg on nearly every frame, which is exactly what the first
            // live measurement showed (knee un-bent firing on 84 % of poses).
            //
            // The FEET answer it unambiguously and with no convention at all: toes are in front of
            // heels on every human, and the vector between them is ~0.12 m, far above the tracker's
            // noise. The nose is the fallback (always anterior to the shoulders, but only a few cm, so
            // it is the weaker signal); the cross product is the last resort and is only reached when
            // neither feet nor nose are observed, in which case the front/back tests stand down anyway.
            Vector3 forward = Vector3.zero;
            if (valid[31] && valid[32] && valid[29] && valid[30]) {
                forward = 0.5f * (observed[31] + observed[32]) - 0.5f * (observed[29] + observed[30]);
            } else if (valid[0] && valid[HumanBodyModel.MidShoulder]) {
                forward = observed[0] - observed[HumanBodyModel.MidShoulder];
            }
            forward = forward - bodyUp * Vector3.Dot(forward, bodyUp);
            hasBodyForward = forward.sqrMagnitude > 1e-6f;
            if (hasBodyForward) {
                bodyForward = forward.normalized;
                bodyRight = Vector3.Cross(bodyUp, bodyForward).normalized;
            } else {
                bodyForward = Vector3.Cross(bodyRight, bodyUp).normalized;
            }
        }

        // ---------------------------------------------------------------------------------------
        // 5. Feed the calibrator only from frames where BOTH endpoints are trustworthy. A length
        //    measured against a held or clamped joint is a measurement of this layer's own output.
        // ---------------------------------------------------------------------------------------
        private void LearnBoneLengths() {
            int b = 0;
            while (b < HumanBodyModel.Bones.Length) {
                HumanBodyModel.Bone bone = HumanBodyModel.Bones[b];
                if (valid[bone.Parent] && valid[bone.Child]) {
                    calibrator.Observe(b, Vector3.Distance(observed[bone.Parent], observed[bone.Child]));
                }
                b = b + 1;
            }
        }

        // ---------------------------------------------------------------------------------------
        // 6. Rebuild the body outward from the mid-hip. Each joint is placed by DIRECTION from its
        //    already-rebuilt parent at the CALIBRATED bone length, so a joint can never be nearer its
        //    parent than one bone — collapse becomes unrepresentable rather than merely gated.
        // ---------------------------------------------------------------------------------------
        private void Rebuild(float dt) {
            // Pass through the slots that have no structure of their own (the face cluster). They still
            // carry their rejection and velocity results; they are simply not re-anchored.
            int i = 0;
            while (i < HumanBodyModel.SlotCount) {
                rebuilt[i] = valid[i] ? observed[i]
                                      : (hasLastAccepted[i] ? lastAccepted[i] : observed[i]);
                i = i + 1;
            }
            // The root. A hip-centred frame puts it at the origin; if the hips were rejected outright
            // the origin is still the correct and only defensible answer.
            rebuilt[HumanBodyModel.MidHip] = valid[HumanBodyModel.MidHip]
                ? observed[HumanBodyModel.MidHip] : Vector3.zero;

            // --- trunk, then the twist refusal, then limbs. The order is what lets the twist limit
            // move the shoulders BEFORE the arms are hung off them.
            int b = 0;
            while (b <= 5) {
                PlaceBone(b, dt);
                b = b + 1;
            }
            if (enforceAngles) {
                ClampTorsoTwist();
            }
            while (b < HumanBodyModel.Bones.Length) {
                PlaceBone(b, dt);
                b = b + 1;
            }
        }

        private void PlaceBone(int b, float dt) {
            HumanBodyModel.Bone bone = HumanBodyModel.Bones[b];
            Vector3 parent = rebuilt[bone.Parent];
            Quaternion frame = LocalFrameFor(b);

            Vector3 dir;
            bool fresh = valid[bone.Child];
            if (fresh) {
                Vector3 raw = observed[bone.Child] - parent;
                if (raw.sqrMagnitude < 1e-10f) {
                    fresh = false;
                    dir = Vector3.zero;
                } else {
                    dir = raw.normalized;
                }
            } else {
                dir = Vector3.zero;
            }

            if (!fresh) {
                // HOLD. Serve the last valid direction, re-expressed in the CURRENT parent frame, so a
                // held forearm swings with its upper arm instead of freezing in space.
                if (hasHeldLocalDir[b]) {
                    dir = (frame * heldLocalDir[b]).normalized;
                    stats.HeldJointFrames = stats.HeldJointFrames + 1L;
                } else {
                    // Nothing valid has ever been seen for this bone. Do NOT invent a direction — that
                    // is the origin-collapse bug in a different costume. Leave the slot where it is.
                    return;
                }
            }

            if (enforceAngles) {
                dir = ApplyAngularLimits(b, bone, dir);
            }

            // RECOVERY. A joint that comes back must not SNAP. The window is armed on the first fresh
            // frame and consumed on that same frame, so the recovery starts from the held pose rather
            // than jumping to the fresh one and easing back. Slerping the DIRECTION, not the position,
            // keeps the bone length exact for every frame of the blend.
            if (fresh && !wasValid[bone.Child] && hasHeldLocalDir[b] && recoverSeconds > 0f) {
                recoverRemaining[b] = recoverSeconds;
            }
            if (fresh && hasHeldLocalDir[b] && recoverRemaining[b] > 0f) {
                float t = 1f - Mathf.Clamp01(recoverRemaining[b] / Mathf.Max(1e-4f, recoverSeconds));
                Vector3 held = (frame * heldLocalDir[b]).normalized;
                dir = Vector3.Slerp(held, dir, t).normalized;
                recoverRemaining[b] = recoverRemaining[b] - dt;
                stats.RecoveringJointFrames = stats.RecoveringJointFrames + 1L;
            }

            float length;
            float observedLength = fresh ? Vector3.Distance(observed[bone.Child], parent) : 0f;
            float calibrated = calibrator.Length(b);
            if (enforceBoneLengths && calibrated > 0f) {
                length = calibrated;
                if (fresh) {
                    float error = Mathf.Abs(observedLength - calibrated);
                    if (error > TouchEpsilonM) {
                        stats.BoneLengthCorrected = stats.BoneLengthCorrected + 1L;
                        if (error > stats.MaxBoneErrorM) {
                            stats.MaxBoneErrorM = error;
                        }
                    }
                }
            } else if (fresh) {
                length = observedLength;
            } else {
                // Held with no calibration yet: fall back to the last distance this bone actually had.
                length = Vector3.Distance(rebuilt[bone.Child], parent);
                if (length < HumanBodyModel.MinBoneM) {
                    return;
                }
            }

            rebuilt[bone.Child] = parent + dir * length;
            heldLocalDir[b] = Quaternion.Inverse(frame) * dir;
            hasHeldLocalDir[b] = true;
        }

        /// <summary>
        /// The frame a bone's direction is HELD in. For a bone whose parent is itself the end of
        /// another bone (a forearm on an upper arm) that is the parent segment's own frame, so the
        /// whole limb moves rigidly during a hold. For a trunk bone it is the body frame.
        /// </summary>
        private Quaternion LocalFrameFor(int b) {
            HumanBodyModel.Bone bone = HumanBodyModel.Bones[b];
            int grandparent = ParentSlotOf(bone.Parent);
            Vector3 reference;
            if (grandparent >= 0) {
                reference = rebuilt[bone.Parent] - rebuilt[grandparent];
            } else {
                reference = bodyForward;
            }
            if (reference.sqrMagnitude < 1e-10f) {
                reference = bodyForward;
            }
            Vector3 forward = reference.normalized;
            // LookRotation degenerates when its two arguments are parallel, and it returns identity
            // silently rather than failing - which would put a held limb in an arbitrary direction
            // exactly when the body is most extended. Step through the basis until one is usable.
            Vector3 upHint = bodyUp;
            if (Mathf.Abs(Vector3.Dot(forward, upHint)) > 0.999f) {
                upHint = bodyForward;
            }
            if (Mathf.Abs(Vector3.Dot(forward, upHint)) > 0.999f) {
                upHint = bodyRight;
            }
            return Quaternion.LookRotation(forward, upHint);
        }

        // Precomputed once: which slot each landmark hangs off. The alternative - scanning the bone
        // table on every lookup - is called ~50 times a frame, and a table makes the topology's
        // single-parent invariant explicit rather than implied by scan order.
        private static readonly int[] ParentOfSlot = BuildParentTable();

        private static int[] BuildParentTable() {
            int[] table = new int[HumanBodyModel.SlotCount];
            int i = 0;
            while (i < table.Length) {
                table[i] = -1;
                i = i + 1;
            }
            int b = 0;
            while (b < HumanBodyModel.Bones.Length) {
                table[HumanBodyModel.Bones[b].Child] = HumanBodyModel.Bones[b].Parent;
                b = b + 1;
            }
            return table;
        }

        private static int ParentSlotOf(int slot) {
            if (slot < 0 || slot >= ParentOfSlot.Length) {
                return -1;
            }
            return ParentOfSlot[slot];
        }

        // ---------------------------------------------------------------------------------------
        // 7. Angular limits. Each is a statement about anatomy (see HumanBodyModel), applied to the
        //    DIRECTION before the length is attached, so nothing here can change a bone length.
        // ---------------------------------------------------------------------------------------
        private Vector3 ApplyAngularLimits(int b, HumanBodyModel.Bone bone, Vector3 dir) {
            // Hinges: forearm on upper arm (7, 9), shin on thigh (11, 13).
            if (b == 7 || b == 9) {
                return ClampHinge(bone, dir, HumanBodyModel.ElbowMinDeg);
            }
            if (b == 11 || b == 13) {
                return ClampHinge(bone, dir, HumanBodyModel.KneeMinDeg);
            }
            // Cones. NOTE the absence of bones 6 and 8, the upper arms: there is deliberately no
            // shoulder cone, because a cone about the trunk's down axis fires on arms-overhead and on
            // nothing else. See the measured reasoning in HumanBodyModel.
            if (b == 10 || b == 12) {
                return ClampCone(dir, -bodyUp, HumanBodyModel.HipConeDeg);
            }
            if (b == 5) {
                return ClampCone(dir, bodyUp, HumanBodyModel.NeckConeDeg);
            }
            return dir;
        }

        /// <summary>
        /// Clamp the interior angle at a hinge joint: 180 deg is a straight limb, smaller is folded.
        /// Rotating the child direction AWAY from the upper segment opens the joint; the rotation stays
        /// in the plane the two segments already span, so a clamp can never twist the limb sideways.
        ///
        /// ONLY THE MINIMUM IS ENFORCEABLE HERE, and that is a property of the data rather than a
        /// choice: three points give an interior angle that is direction-agnostic and capped at 180
        /// deg, so HYPEREXTENSION IS NOT OBSERVABLE from an elbow's three landmarks — a forearm bent
        /// 10 deg the wrong way and one bent 10 deg the right way are the same three distances. The
        /// maxima in <see cref="HumanBodyModel"/> are therefore used by the offline analyser to FLAG
        /// impossible angles in recorded data, not clamped here. The one hyperextension case that IS
        /// well defined — a knee bending forwards, which has an unambiguous sign against the body's
        /// forward axis — is handled separately by <see cref="FixKneeDirection"/>.
        /// </summary>
        private Vector3 ClampHinge(HumanBodyModel.Bone bone, Vector3 dir, float minDeg) {
            int grandparent = ParentSlotOf(bone.Parent);
            if (grandparent < 0) {
                return dir;
            }
            Vector3 toUpper = rebuilt[grandparent] - rebuilt[bone.Parent];
            if (toUpper.sqrMagnitude < 1e-10f) {
                return dir;
            }
            toUpper = toUpper.normalized;
            float interior = Vector3.Angle(toUpper, dir);
            if (interior >= minDeg) {
                return dir;
            }
            stats.HingeClamped = stats.HingeClamped + 1L;
            return Vector3.RotateTowards(dir, -toUpper, (minDeg - interior) * Mathf.Deg2Rad, 0f).normalized;
        }

        private Vector3 ClampCone(Vector3 dir, Vector3 axis, float coneDeg) {
            float angle = Vector3.Angle(axis, dir);
            if (angle <= coneDeg) {
                return dir;
            }
            stats.ConeClamped = stats.ConeClamped + 1L;
            return Vector3.RotateTowards(dir, axis, (angle - coneDeg) * Mathf.Deg2Rad, 0f).normalized;
        }

        // Shoulder line versus hip line about the trunk axis. Clamped by rotating the two shoulders
        // about the trunk, so the shoulder BONE LENGTHS are untouched and the arms (not yet placed)
        // inherit the corrected shoulders.
        private void ClampTorsoTwist() {
            Vector3 shoulderLine = rebuilt[11] - rebuilt[12];
            Vector3 hipLine = rebuilt[23] - rebuilt[24];
            shoulderLine = shoulderLine - bodyUp * Vector3.Dot(shoulderLine, bodyUp);
            hipLine = hipLine - bodyUp * Vector3.Dot(hipLine, bodyUp);
            if (shoulderLine.sqrMagnitude < 1e-8f || hipLine.sqrMagnitude < 1e-8f) {
                return;
            }
            float twist = Vector3.SignedAngle(hipLine.normalized, shoulderLine.normalized, bodyUp);
            float excess = Mathf.Abs(twist) - HumanBodyModel.TorsoTwistDeg;
            if (excess <= 0f) {
                return;
            }
            stats.TwistClamped = stats.TwistClamped + 1L;
            Quaternion correction = Quaternion.AngleAxis(-Mathf.Sign(twist) * excess, bodyUp);
            Vector3 pivot = rebuilt[HumanBodyModel.MidShoulder];
            rebuilt[11] = pivot + correction * (rebuilt[11] - pivot);
            rebuilt[12] = pivot + correction * (rebuilt[12] - pivot);
        }

        // ---------------------------------------------------------------------------------------
        // 8. Knees do not bend forwards. Applied as a REFLECTION across the plane that contains the
        //    hip-ankle line, so both the thigh and the shin lengths are preserved EXACTLY rather than
        //    approximately — a knee fix that changed the leg's length would undo stage 6.
        //
        //    Guarded twice: only when the trunk is upright enough for "forward" to mean anything, and
        //    only when the leg is actually bent. A nearly straight leg has no bend direction, and
        //    inventing one from noise would put a wobble into a standing pose.
        // ---------------------------------------------------------------------------------------
        private void FixKneeDirection() {
            if (!hasBodyForward) {
                return;
            }
            if (Vector3.Dot(bodyUp, Vector3.up) < 0.5f) {
                return;
            }
            FixOneKnee(23, 25, 27);
            FixOneKnee(24, 26, 28);
        }

        private void FixOneKnee(int hipSlot, int kneeSlot, int ankleSlot) {
            Vector3 hip = rebuilt[hipSlot];
            Vector3 knee = rebuilt[kneeSlot];
            Vector3 ankle = rebuilt[ankleSlot];
            Vector3 thigh = hip - knee;
            Vector3 shin = ankle - knee;
            if (thigh.sqrMagnitude < 1e-10f || shin.sqrMagnitude < 1e-10f) {
                return;
            }
            if (Vector3.Angle(thigh, shin) > KneeBendActiveDeg) {
                return;
            }
            Vector3 axis = ankle - hip;
            if (axis.sqrMagnitude < 1e-10f) {
                return;
            }
            Vector3 u = axis.normalized;
            Vector3 n = bodyForward - u * Vector3.Dot(bodyForward, u);
            if (n.sqrMagnitude < 1e-8f) {
                return;
            }
            n = n.normalized;
            float offset = Vector3.Dot(knee - hip, n);
            if (offset >= -0.01f) {
                return;     // knee is forward of the hip-ankle line, or close enough: a normal leg
            }
            stats.KneeUnbent = stats.KneeUnbent + 1L;
            rebuilt[kneeSlot] = knee - 2f * offset * n;
        }

        // ---------------------------------------------------------------------------------------
        // 9. Emit. Confidence passes through UNCHANGED - see the class remarks: P0-1 must keep seeing
        //    an unobserved limb as unobserved.
        // ---------------------------------------------------------------------------------------
        private void Emit(PoseFrame input) {
            float maxCorrection = 0f;
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 p = rebuilt[i];
                if (!IsFinite(p)) {
                    p = hasLastAccepted[i] ? lastAccepted[i] : Vector3.zero;
                }
                if (valid[i]) {
                    float moved = Vector3.Distance(p, observed[i]);
                    if (moved > maxCorrection) {
                        maxCorrection = moved;
                    }
                    // The ACCEPTED OBSERVATION, not the rebuilt output. Storing the rebuilt position
                    // closes a feedback loop: the structural rebuild moves a joint, the next frame's
                    // step is then measured from the moved position, the clamp fires on the difference
                    // it created itself, and the joint lags further every frame. That loop MEASURED as
                    // the velocity clamp firing on 80 % of poses in otherwise clean tracking. The clamp
                    // is a statement about the TRACKER, so it compares tracker to tracker.
                    lastAccepted[i] = observed[i];
                    hasLastAccepted[i] = true;
                }
                output.SetLandmark(i, new PoseLandmark(p, confidence[i]));
                wasValid[i] = valid[i];
                i = i + 1;
            }
            // The virtual joints have no landmark slot but their temporal state still has to advance.
            if (valid[HumanBodyModel.MidHip]) {
                lastAccepted[HumanBodyModel.MidHip] = observed[HumanBodyModel.MidHip];
                hasLastAccepted[HumanBodyModel.MidHip] = true;
            }
            if (valid[HumanBodyModel.MidShoulder]) {
                lastAccepted[HumanBodyModel.MidShoulder] = observed[HumanBodyModel.MidShoulder];
                hasLastAccepted[HumanBodyModel.MidShoulder] = true;
            }
            wasValid[HumanBodyModel.MidHip] = valid[HumanBodyModel.MidHip];
            wasValid[HumanBodyModel.MidShoulder] = valid[HumanBodyModel.MidShoulder];

            if (maxCorrection > stats.MaxCorrectionM) {
                stats.MaxCorrectionM = maxCorrection;
            }
            output.SetMeta(input.TimestampSeconds, true);
            // Root translation is another channel's business (see the class remarks) - carried, never
            // altered, so nothing downstream can tell this layer ran.
            if (input.HasRootPosition) {
                output.SetRootPosition(input.RootPositionMetres);
            } else {
                output.ClearRootPosition();
            }
        }

        // The latency proof. A frame in which no temporal stage fired cannot have introduced lag, and a
        // frame in which only bone-length normalisation fired cannot either - that stage has no memory.
        private void TallyFrame() {
            bool temporal = stats.HeldJointFrames != frameStats.HeldJointFrames
                         || stats.VelocityClamped != frameStats.VelocityClamped
                         || stats.RecoveringJointFrames != frameStats.RecoveringJointFrames
                         || stats.JumpAccepted != frameStats.JumpAccepted;
            bool structural = stats.HingeClamped != frameStats.HingeClamped
                           || stats.ConeClamped != frameStats.ConeClamped
                           || stats.KneeUnbent != frameStats.KneeUnbent
                           || stats.TwistClamped != frameStats.TwistClamped;
            bool rejected = stats.RejectedNonFinite != frameStats.RejectedNonFinite
                         || stats.RejectedOrigin != frameStats.RejectedOrigin
                         || stats.RejectedOutOfBody != frameStats.RejectedOutOfBody
                         || stats.RejectedLowConfidence != frameStats.RejectedLowConfidence;
            bool boneOnly = stats.BoneLengthCorrected != frameStats.BoneLengthCorrected;
            if (!temporal && !structural && !rejected) {
                if (boneOnly) {
                    stats.FramesOnlyBoneLength = stats.FramesOnlyBoneLength + 1L;
                } else {
                    stats.FramesUntouched = stats.FramesUntouched + 1L;
                }
            }
        }
    }
}
