using System.Collections.Generic;

using UnityEngine;

using UniVRM10;

using VirtualMirror.Core;
using VirtualMirror.Retargeting.Kalidokit;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Whole-body Kalidokit driver (ADR-022). Applies the ported Kalidokit pose
    /// (<see cref="KalidokitPoseSolver"/>) to UniVRM's NORMALIZED control rig
    /// (<see cref="Vrm10RuntimeControlRig"/>) — the same VRM1.0 normalized bones three-vrm exposes, so
    /// Kalidokit's numbers apply with a SINGLE global three→Unity handedness conversion instead of the
    /// per-bone axis guessing that stalled the raw-bone path. The avatar must be loaded WITH the control
    /// rig generated (<see cref="VirtualMirror.Avatar.Vrm.UniVrmAvatarLoader.GenerateControlRig"/>).
    ///
    /// Requires the control rig to be driven each frame before UniVRM's <c>Runtime.Process()</c> runs; call
    /// <see cref="Apply"/> from LateUpdate. All bones share one convention: <see cref="eulerSigns"/> +
    /// <see cref="flipQuat"/> (both live-tunable) — the ONE knob the user dials once (see ADR-022).
    /// </summary>
    public sealed class KalidokitControlRigDriver {
        private Vrm10Instance vrm;
        private Vrm10RuntimeControlRig controlRig;

        private Transform hips;
        private Transform spine;
        private Transform chest;
        private Transform upperChest;   // TORSO V5: bound where the rig provides it (was never written)
        private Transform leftUpperArm;
        private Transform leftLowerArm;
        private Transform leftHand;
        private Transform rightUpperArm;
        private Transform rightLowerArm;
        private Transform rightHand;
        private Transform leftUpperLeg;
        private Transform leftLowerLeg;
        private Transform rightUpperLeg;
        private Transform rightLowerLeg;

        private readonly Vector3[] landmarks = new Vector3[PoseFrame.LandmarkCount];
        private bool bound;

        // Global convention knobs (live-tunable from AppBootstrap). Defaults are the standard three→Unity
        // conversion (mirror Z); if the whole body is mis-oriented, change flipQuat (0..3) and/or eulerSigns.
        private Vector3 eulerSigns = Vector3.one;
        private int flipQuat;
        private float lerpAmount = 0.5f;
        private bool driveLegs = true;
        private bool mirrorX;
        // Torso side-lean (roll) is the least reliable torso DOF and produced a constant "leaned right"
        // from noisy shoulder tilt. Scales the roll (z) of hips + spine: 0 = upright (no lean, ADR-019
        // StableUp precedent), 1 = full side-lean tracking. Live-tunable.
        private float torsoRoll;
        // Waist forward-bend (Milestone-2, ADR-025) + robustness (ADR-026): the Kalidokit port zeroes spine
        // pitch, so add a forward bend derived from the measured trunk depth (mid-hip -> mid-shoulder tilt).
        // The raw trunk pitch carries a SLOW, DISTANCE-DEPENDENT systematic offset (RTMW3D measures the upper
        // body farther; the offset drifts as the user walks: upright reads ~+5.5 deg at 2 m, ~0 at 3 m) plus
        // per-frame noise (std ~6 deg) and occasional 35 deg spikes (pipeline-log measured, 2026-08-11). A
        // single fixed neutral captured once (the old approach) could not track that drift, so the avatar
        // leaned at rest and the real bend drowned in noise. Instead: subtract a SLOW ADAPTIVE baseline
        // (high-pass — removes the drifting offset but KEEPS the faster intentional bend), then rate-limit
        // (spike reject) and lightly low-pass the result. All live-tunable; C re-seeds the baseline.
        private float spineBendScale = 1f;
        private float spineBaseline;                 // slow EMA of raw trunk pitch = the adaptive neutral (rad)
        private bool hasSpineBaseline;
        private readonly List<float> spineWarmup = new List<float>();
        private const int SpineWarmupFrames = 45;    // ~2 s @ ~21 fps: seed baseline from the median first
        private float spineBendBaselineTau = 8f;      // s; larger holds a sustained bend longer + corrects drift slower (huge ~= fixed neutral)
        private float spineBendSmoothTau = 0.12f;     // s; output low-pass that kills residual pitch jitter
        private float spineBendMaxRateDeg = 250f;     // deg/s; rate-limit that rejects the 35 deg/frame spikes
        private float spineBendRateLimited;           // rate-limited bend (rad), pre-smoothing
        private float spineBendSmoothed;              // final applied bend (rad)

        // Torso YAW damping for stable 360-turn (ADR-027). Kalidokit derives torso facing from the DEPTH
        // separation of the shoulder/hip line (2-point rollPitchYaw); that estimate is hypersensitive to OAK
        // depth noise at range (pipeline-log measured: rest yaw ~-10 deg, excursions to -119 deg, and +/-180
        // deg AMBIGUITY FLIPS from the calcHips jump-fix branches). Un-flattening the trunk (ADR-025, for
        // turn+bend) fed that noise straight in -> the chest over-twists and DRAGS the arms (the "wrong pose"
        // regression). Fix: (1) RATE-LIMIT the yaw (a spurious +/-180 flip bounces back before the slew
        // follows it = rejected; a genuine gradual turn is followed), (2) light low-pass, (3) a soft
        // DEAD-ZONE that keeps small/noisy yaw at frontal. Applied to hips + spine yaw; live-scaled by
        // torsoYawScale (0 = frontal-lock fallback, 1 = full damped turn). Kalidokit's per-bone dampeners
        // (Hips 0.7, Spine 0.45, Chest 0.25) are also restored (our path had used 1.0 + a 0.5/0.5 split ->
        // ~1.4x over-twist). See ADR-027.
        // ROLL keeps Kalidokit's per-bone dampeners. YAW does NOT — see the V5 block below. These are
        // inert while torsoRoll = 0, and are named for the only channel that still uses them.
        private const float HipsRollDamp = 0.7f;
        private const float SpineRollDamp = 0.45f;
        private const float ChestRollDamp = 0.25f;

        // TORSO V5 (ADR-037): the yaw composition is RELATIVE, not two absolute signals summed.
        //
        // The old form gave Hips 0.70*hipYaw and Spine+Chest (0.45+0.25)=0.70*shoulderYaw. Because the
        // bones NEST, the rendered shoulder line is the SUM of the chain, so it came out as
        //     0.70*hipYaw + 0.70*shoulderYaw
        // i.e. two ABSOLUTE yaws added. Measured consequences (V4 §2b): a rigid turn where hipYaw ==
        // shoulderYaw is multiplied ~1.4x (measured 1.194 live, 1.230 held); opposing hip/shoulder yaw
        // CANCELS (gain +0.116); and a 52 deg twist INVERTS the torso (gain -2.944). No scalar can fix
        // a gain that ranges -2.944..+1.230, which is why torsoYawScale was only ever a plaster.
        //
        // V5 decomposes it the way the body actually works:
        //     Hips            <- absolute hipYaw                       (gain 1.0)
        //     Spine/Chest/UC  <- relative twist (shoulderYaw - hipYaw), weights summing to 1.0
        // so the chain sums to hipYaw + (shoulderYaw - hipYaw) = shoulderYaw EXACTLY, for every
        // hip/shoulder ratio. A rigid rotation is therefore never multiplied, and a twist is
        // reproduced as a twist.
        //
        // Weights follow human axial rotation, which is mostly thoracic, not lumbar (lumbar ~5 deg vs
        // thoracic ~35 deg of the total): Spine(lumbar) least, Chest and UpperChest(thoracic) most.
        // They MUST sum to 1.0 — the sum is the gain. NormaliseTwistWeights redistributes when a rig
        // lacks Chest or UpperChest, so the sum stays 1.0 on any humanoid.
        private const float SpineTwistWeight = 0.20f;
        private const float ChestTwistWeight = 0.40f;
        private const float UpperChestTwistWeight = 0.40f;

        // Gain on the whole torso yaw path. 0 = frontal-lock (the ADR-027 fallback, still reachable);
        // 1 = physically correct follow. It is NO LONGER a compensation for a broken composition —
        // with V5 the correct value is 1 and anything else is a deliberate partial-follow (ADR-037).
        private float torsoYawScale = 1f;             // 0 = frontal-lock, 1 = full (live-tunable)

        // TORSO V5: 4-point trunk validity (see TrunkGate). Without it a dropped detection makes
        // atan2(0,0) command exactly +90 deg of yaw — measured live on an empty room.
        private TrunkGateState trunkGate;
        private TrunkRejectReason lastTrunkReject;    // diag-only
        private int trunkRejectCount;                 // diag-only
        private float yawMaxRateDeg = 140f;           // deg/s; below a human turn (~180 deg/s) so flips are rejected but real turns follow
        private float yawSmoothTau = 0.15f;           // s; output low-pass
        private float yawDeadzoneLoDeg = 8f;          // deg; |yaw| below this -> frontal (kills rest noise)
        private float yawDeadzoneHiDeg = 22f;         // deg; full turn above this (soft knee between)
        private float hipsYawRate = float.NaN, hipsYawSmooth;   // NaN = seed on first frame (self-seeding per signal)
        private float spineYawRate = float.NaN, spineYawSmooth;

        // JointId left<->right pairs for a true reflection (negate X AND swap sides — negate-X alone crosses
        // the limbs, ADR-018). Flattened pairs.
        private static readonly int[] MirrorPairs = new int[] {
            1, 4, 2, 5, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
        };

        // Finger driving on the control rig (curl-based, ADR-022 fingers). Reuses the HandFrame per-finger
        // curls from any hand provider; rotates each normalized finger bone about a single tunable axis.
        private struct FingerJoint {
            public Transform Bone;
            public Quaternion Rest;
            public int Finger;      // 0=thumb,1=index,2=middle,3=ring,4=little
            public float MaxDegrees;
        }
        private readonly List<FingerJoint> leftFingers = new List<FingerJoint>();
        private readonly List<FingerJoint> rightFingers = new List<FingerJoint>();
        private Vector3 fingerCurlAxis = new Vector3(0f, 0f, -1f); // normalized-bone flexion axis (tunable)
        private float fingerWeight = 1f;

        // Wrist bend (ADR-021/023). The control rig owns bone LOCAL rotations, so the wrist is applied to the
        // RAW skeleton Hand bones AFTER Runtime.Process (see AppBootstrap) as a ROLL-FREE swing of the palm's
        // forward axis, delta-from-a-captured-neutral (FromToRotation has no roll DOF → cannot helicopter).
        private Transform rawLeftHand;
        private Transform rawRightHand;
        private Quaternion leftNeutralPalm = Quaternion.identity;
        private Quaternion rightNeutralPalm = Quaternion.identity;
        private bool hasLeftPalmNeutral;
        private bool hasRightPalmNeutral;
        private float wristWeight = 0.7f;

        // P0-1 (audit §F-01): per-limb confidence gate. Each limb accepts a fresh solve only when its driving
        // joints are confident enough; otherwise it HOLDS its last valid rotation (never zero → no collapse).
        // The driving joints follow Kalidokit's left/right cross-map (KalidokitArmSolver/PoseSolver): the LEFT
        // bones are solved from the RIGHT-side landmarks and vice-versa, so the gate reads those indices.
        private readonly LimbGate leftArmGate = new LimbGate();
        private readonly LimbGate rightArmGate = new LimbGate();
        private readonly LimbGate leftLegGate = new LimbGate();
        private readonly LimbGate rightLegGate = new LimbGate();
        private readonly float[] conf = new float[PoseFrame.LandmarkCount];
        private float limbConfidenceThreshold = 0.3f;   // centralized, live-tunable from AppBootstrap
        private ILogService limbLogService;              // optional; transitions + aggregates only (not per-frame)
        private long appliedFrameCounter;
        private const int AggregateLogEvery = 300;       // ~10-15 s: periodic held/reacquire aggregate

        // ARM RETARGET V1 (docs/UNITY_ARM_RETARGET_V1_2026-09-08.md). The Kalidokit arm branch was measured
        // at 28.2 deg mean / 47.5 deg max direction error with 20.2 deg of left/right asymmetry on a
        // SYMMETRIC input; <see cref="ArmAimSolver"/> replaces it with a quaternion-native aim solve whose
        // roll is pinned by the elbow plane. The rest basis is MEASURED from this rig at Bind (never
        // hardcoded), so an A-pose avatar needs no calibration. useAimArms=false restores the old Euler
        // path unchanged, for A/B and rollback.
        private bool useAimArms = true;
        private ArmRestBasis leftArmRest;
        private ArmRestBasis rightArmRest;
        private ArmRollState leftArmRoll;
        private ArmRollState rightArmRoll;
        private ArmAimResult leftArmAim;      // last solve, for the direction trace
        private ArmAimResult rightArmAim;

        // DIAG-ONLY (UNITY_RETARGETING_AUDIT_2026-09-08 §12): direction trace. When enabled, logs — per traced
        // bone, every Nth applied frame — the WANTED bone direction (derived from the same landmark array the
        // solver consumed) next to the ACHIEVED normalized-bone direction, plus the error angle and the solver
        // euler that produced it. This is what makes "the skeleton is right but the avatar is wrong" a measured
        // number instead of an impression. Default OFF; drives no behaviour, allocates nothing per frame.
        //
        // "Wanted" accounts for the two conventions the retarget already relies on:
        //  * Kalidokit's left/right CROSS-map — the avatar's LEFT arm is solved from the source RIGHT arm
        //    (landmarks 12/14/16), so the trace compares against those same landmarks; and
        //  * the mirror — a mirror reflects across the sagittal plane, so the wanted direction is the source
        //    segment with X negated. Together these two are the intended mirror behaviour, not an error.
        // Both the control-rig bones and PoseFrame live in the avatar-model frame (X = avatar's LEFT, Y = up,
        // Z = avatar's BACK), and ApplyRootPosition only TRANSLATES the root, so world directions are
        // directly comparable.
        private bool traceDirections;
        private int traceEveryFrames = 60;
        private Transform leftFoot;      // diag-only: needed to read the lower-leg direction
        private Transform rightFoot;

        public bool IsBound {
            get {
                return bound;
            }
        }

        public void SetTuning(Vector3 signs, int flip, float lerp, bool legs, bool mirror, float torsoRollScale) {
            eulerSigns = signs;
            flipQuat = flip;
            lerpAmount = Mathf.Clamp01(lerp);
            driveLegs = legs;
            mirrorX = mirror;
            torsoRoll = Mathf.Clamp01(torsoRollScale);
        }

        public void Bind(Vrm10Instance vrmInstance) {
            bound = false;
            vrm = vrmInstance;
            controlRig = null;
            if (vrm == null || vrm.Runtime == null) {
                return;
            }
            controlRig = vrm.Runtime.ControlRig;
            if (controlRig == null) {
                // Avatar was not loaded with a control rig — cannot use this path.
                return;
            }
            // Take MANUAL control of the VRM update: with auto-update the instance's Process() races our
            // LateUpdate control-rig writes (it runs first and applies the REST pose → avatar stuck in
            // T-pose). Switch to None and call Process() ourselves right AFTER we write the bones each
            // frame, so our pose (and expressions/springbones) always applies in order. Restored on Unbind.
            vrm.UpdateType = Vrm10Instance.UpdateTypes.None;
            hips = controlRig.GetBoneTransform(HumanBodyBones.Hips);
            spine = controlRig.GetBoneTransform(HumanBodyBones.Spine);
            chest = controlRig.GetBoneTransform(HumanBodyBones.Chest);
            // TORSO V5: UpperChest was never bound, so on a rig that HAS it (this one does — spine.003,
            // the shoulders' direct parent) a quarter of the trunk chain was rigid by omission rather
            // than by design. It is optional in the VRM humanoid, so everything downstream treats null
            // as "redistribute its share" rather than assuming it exists.
            upperChest = controlRig.GetBoneTransform(HumanBodyBones.UpperChest);
            leftUpperArm = controlRig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = controlRig.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = controlRig.GetBoneTransform(HumanBodyBones.LeftHand);
            rightUpperArm = controlRig.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLowerArm = controlRig.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = controlRig.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpperLeg = controlRig.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            leftLowerLeg = controlRig.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightUpperLeg = controlRig.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rightLowerLeg = controlRig.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            leftFoot = controlRig.GetBoneTransform(HumanBodyBones.LeftFoot);    // diag-only (direction trace)
            rightFoot = controlRig.GetBoneTransform(HumanBodyBones.RightFoot);
            // Raw skeleton hands for the post-Process wrist bend (the control rig writes to these via Process
            // each frame, so the wrist must be applied to them AFTER Process — see ApplyWrist / AppBootstrap).
            UnityEngine.Animator rawAnimator = vrm.GetComponentInChildren<UnityEngine.Animator>();
            rawLeftHand = rawAnimator != null ? rawAnimator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            rawRightHand = rawAnimator != null ? rawAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            hasLeftPalmNeutral = false;
            hasRightPalmNeutral = false;
            leftArmGate.Reset();      // P0-1: fresh hold state for the new rig (no stale held pose)
            rightArmGate.Reset();
            leftLegGate.Reset();
            rightLegGate.Reset();
            trunkGate.Reset();        // TORSO V5: never carry a held torso yaw across avatars
            lastTrunkReject = TrunkRejectReason.None;
            trunkRejectCount = 0;
            BuildArmRestBases();      // ARM RETARGET V1: measure this rig's arm rest orientation, once
            BindFingers();
            bound = hips != null || leftUpperArm != null || rightUpperArm != null;
        }

        public void Unbind() {
            if (vrm != null) {
                vrm.UpdateType = Vrm10Instance.UpdateTypes.Update; // restore auto-update for the FK path
            }
            bound = false;
            controlRig = null;
            vrm = null;
            rawLeftHand = null;
            rawRightHand = null;
            hasLeftPalmNeutral = false;
            hasRightPalmNeutral = false;
        }

        /// <summary>
        /// Runs the VRM runtime (control rig → skeleton, constraints, springbones, look-at, expressions).
        /// Call once per frame AFTER <see cref="Apply"/> and after the expression retarget, since the VRM is
        /// set to manual (UpdateTypes.None) while this driver owns it. This is what makes the pose actually
        /// show — without it the avatar stays in T-pose (the reported bug).
        /// </summary>
        public void ProcessRuntime() {
            if (!bound || vrm == null || vrm.Runtime == null) {
                return;
            }
            vrm.Runtime.Process();
        }

        public void Apply(PoseFrame frame) {
            if (!bound || frame == null || !frame.IsValid) {
                return;
            }
            // Adapt PoseFrame (Unity Y-up) into Kalidokit's convention (Y-down); indices == MediaPipe order.
            // mirrorX = true presents a reflected skeleton to the solver (negate X + swap L/R) so the avatar
            // mirrors the user like a real mirror instead of copying same-side ("looks right but mirrored").
            float mx = mirrorX ? -1f : 1f;
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                PoseLandmark lmk = frame.GetLandmark((JointId)i);
                Vector3 p = lmk.Position;
                landmarks[i] = new Vector3(p.x * mx, -p.y, p.z);
                conf[i] = lmk.Confidence;   // P0-1: carry per-joint confidence alongside the position
                i = i + 1;
            }
            if (mirrorX) {
                int pi = 0;
                while (pi < MirrorPairs.Length) {
                    int a = MirrorPairs[pi];
                    int b = MirrorPairs[pi + 1];
                    Vector3 tmp = landmarks[a];
                    landmarks[a] = landmarks[b];
                    landmarks[b] = tmp;
                    // Swap confidence identically so conf[] stays aligned with the landmarks the solver used.
                    float ctmp = conf[a];
                    conf[a] = conf[b];
                    conf[b] = ctmp;
                    pi = pi + 2;
                }
            }

            KalidokitFullPose pose = KalidokitPoseSolver.Solve(landmarks);

            float dt = Time.deltaTime;
            if (dt <= 0f) {
                dt = 0.02f;
            }

            // Waist FORWARD-BEND (Milestone-2, ADR-026): Kalidokit's calcHips/spine zeroes spine pitch (a
            // webcam can't see forward bend), so the avatar never bent at the waist even though the OAK depth
            // captures it. Derive the trunk's forward tilt from the measured mid-hip -> mid-shoulder vector
            // (adapted convention: X right, Y DOWN, Z toward camera) with an ADAPTIVE baseline (high-pass),
            // rate-limit + smooth. Result -> `bend` (a pitch to add to Spine/Chest, split like before).
            float bend = 0f;
            if (spineBendScale != 0f) {
                Vector3 midSh = (landmarks[11] + landmarks[12]) * 0.5f;
                Vector3 midHip = (landmarks[23] + landmarks[24]) * 0.5f;
                Vector3 trunk = midSh - midHip;
                float vert = -trunk.y;                       // torso vertical extent (adapted Y is down)
                if (vert > 0.05f) {                          // skip degenerate/garbage trunks (e.g. all-zero-Z startup)
                    float rawPitch = Mathf.Atan2(trunk.z, vert);
                    if (!hasSpineBaseline) {
                        // Warm-up: seed the adaptive baseline from the MEDIAN of the first frames (robust to
                        // the one all-zero-Z startup frame), applying no bend until it is seeded.
                        spineWarmup.Add(rawPitch);
                        if (spineWarmup.Count >= SpineWarmupFrames) {
                            spineBaseline = Median(spineWarmup);
                            hasSpineBaseline = true;
                        }
                    } else {
                        // High-pass: the baseline slowly follows the drifting systematic offset; the bend is
                        // the FAST residual above it (so upright -> ~0 at any distance, a real bend shows).
                        float aSlow = 1f - Mathf.Exp(-dt / Mathf.Max(0.1f, spineBendBaselineTau));
                        spineBaseline += aSlow * (rawPitch - spineBaseline);
                        float targetBend = (rawPitch - spineBaseline) * spineBendScale;
                        // Spike reject: rate-limit the change (a hard 35 deg/frame lurch cannot get through).
                        float maxStep = Mathf.Deg2Rad * spineBendMaxRateDeg * dt;
                        spineBendRateLimited += Mathf.Clamp(targetBend - spineBendRateLimited, -maxStep, maxStep);
                        // Light low-pass to kill the residual per-frame pitch jitter.
                        float aFast = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, spineBendSmoothTau));
                        spineBendSmoothed += aFast * (spineBendRateLimited - spineBendSmoothed);
                        bend = spineBendSmoothed;
                    }
                }
            }

            // TORSO V5 (ADR-037), step 1 of 2: GATE the trunk before anything consumes it.
            // Kalidokit reads landmarks 11/12/23/24 with no validation; an absent detection is [0,0,0]
            // and atan2(0,0) becomes exactly +90 deg of yaw. Hip and shoulder are gated TOGETHER — the
            // composition below uses their DIFFERENCE, so mixing a fresh value with a held one would
            // fabricate a twist the human never made.
            TrunkGateResult trunkYaw = TrunkGate.Evaluate(
                landmarks[11], landmarks[12], landmarks[23], landmarks[24],
                pose.Hips.y, pose.Spine.y, dt, ref trunkGate);
            if (!trunkYaw.Fresh) {
                lastTrunkReject = trunkYaw.Reason;
                trunkRejectCount = trunkRejectCount + 1;
            }

            // ADR-027 conditioning is unchanged and still runs: rate-limit -> low-pass -> soft dead-zone.
            // It is now fed the GATED signal, so a degenerate frame can no longer enter its filter state.
            float hipsYawAbs = DampYaw(trunkYaw.HipYaw, ref hipsYawRate, ref hipsYawSmooth, dt);
            float shoulderYawAbs = DampYaw(trunkYaw.ShoulderYaw, ref spineYawRate, ref spineYawSmooth, dt);

            // TORSO V5, step 2 of 2: RELATIVE composition. Hips take the absolute hip yaw; the upper
            // trunk takes only the TWIST that the hips do not already account for. Because the bones
            // nest, the rendered shoulder line sums to
            //     hipYaw + (shoulderYaw - hipYaw) = shoulderYaw
            // exactly, so a rigid turn has gain 1.0 and is never multiplied (the old form summed two
            // absolute yaws and reached 1.4x, cancelled, or inverted — V4 §2b).
            float relativeYaw = shoulderYawAbs - hipsYawAbs;
            float hipsYaw = hipsYawAbs * torsoYawScale;
            float twist = relativeYaw * torsoYawScale;
            float wSpine, wChest, wUpperChest;
            NormaliseTwistWeights(out wSpine, out wChest, out wUpperChest);

            float spineRoll = pose.Spine.z * torsoRoll;
            float hipsRoll = pose.Hips.z * torsoRoll;
            float spineShare = chest != null ? 0.5f : 1f;

            // Bend (pitch) keeps its existing Spine/Chest split (ADR-026) untouched; roll keeps
            // Kalidokit's dampeners. Only the YAW channel changes here.
            ApplyBone(spine, new Vector3(bend * spineShare, twist * wSpine, spineRoll * SpineRollDamp));
            if (chest != null) {
                ApplyBone(chest, new Vector3(bend * 0.5f, twist * wChest, spineRoll * ChestRollDamp));
            }
            if (upperChest != null) {
                // Yaw only: adding bend here would change the waist pitch distribution, which is a
                // separate, already-validated behaviour (ADR-026) and is deliberately left alone.
                ApplyBone(upperChest, new Vector3(0f, twist * wUpperChest, 0f));
            }
            // P0-1: confidence-gated arms. Kalidokit cross-maps sides — pose.*Left is solved from landmarks
            // 12/14/16 (right shoulder/elbow/wrist), pose.*Right from 11/13/15 — so gate on those. ARM
            // RETARGET V1 keeps that cross-map (it IS the intended mirror) and only replaces the maths that
            // turns those three landmarks into two rotations. The old Euler path stays reachable via
            // useAimArms=false for A/B and rollback.
            if (useAimArms) {
                ApplyAimArm(leftArmGate, "leftArm", Min3(conf[12], conf[14], conf[16]),
                            leftUpperArm, leftLowerArm, leftArmRest, ref leftArmRoll,
                            12, 14, 16, out leftArmAim);
                ApplyAimArm(rightArmGate, "rightArm", Min3(conf[11], conf[13], conf[15]),
                            rightUpperArm, rightLowerArm, rightArmRest, ref rightArmRoll,
                            11, 13, 15, out rightArmAim);
            } else {
                ApplyLimb(leftArmGate, "leftArm", Min3(conf[12], conf[14], conf[16]),
                          leftUpperArm, pose.UpperArmLeft, leftLowerArm, pose.LowerArmLeft);
                ApplyLimb(rightArmGate, "rightArm", Min3(conf[11], conf[13], conf[15]),
                          rightUpperArm, pose.UpperArmRight, rightLowerArm, pose.LowerArmRight);
            }
            // Hands: Kalidokit derives the Hand rotation from the body-pose hand points (wrist + pinky/index
            // MCPs — indices 15/17/19 for the right, 16/18/20 for the left). On the OAK whole-body stream
            // those hand points (17-22) are often ALL ZERO (not provided), so FindRotation(wrist, origin)
            // yields a garbage FIXED rotation — the persistent hand "curve" that never tracks the wrist
            // (found live 2026-08-10, ADR-023). Only drive the Hand bone when its hand points are present;
            // otherwise drive it to REST (identity euler) so the hand stays straight instead of curled.
            bool leftHandValid = landmarks[18] != Vector3.zero || landmarks[20] != Vector3.zero;
            bool rightHandValid = landmarks[17] != Vector3.zero || landmarks[19] != Vector3.zero;
            ApplyBone(leftHand, leftHandValid ? pose.HandLeft : Vector3.zero);
            ApplyBone(rightHand, rightHandValid ? pose.HandRight : Vector3.zero);
            if (driveLegs) {
                // P0-1: confidence-gated legs (same cross-map: pose.*Left from 24/26/28, pose.*Right from
                // 23/25/27). Occluded lower body arrives as conf 0 → the leg HOLDS instead of folding.
                ApplyLimb(leftLegGate, "leftLeg", Min3(conf[24], conf[26], conf[28]),
                          leftUpperLeg, pose.UpperLegLeft, leftLowerLeg, pose.LowerLegLeft);
                ApplyLimb(rightLegGate, "rightLeg", Min3(conf[23], conf[25], conf[27]),
                          rightUpperLeg, pose.UpperLegRight, rightLowerLeg, pose.LowerLegRight);
            }
            // Hips rotation only (position handled elsewhere); damped yaw (above) + roll gated by torsoRoll,
            // with Kalidokit's Hips 0.7 dampener. Pitch is 0 (Kalidokit sets hips.x = 0). Torso is NOT gated
            // (the audit found it already stable; P0 must not degrade it).
            // TORSO V5: gain 1.0 on the absolute hip yaw. The old 0.70 dampener was half of the
            // double-counting that made a rigid turn over-rotate; roll keeps its dampener.
            ApplyBone(hips, new Vector3(0f, hipsYaw, hipsRoll * HipsRollDamp));

            appliedFrameCounter = appliedFrameCounter + 1;
            if (traceDirections) {
                TraceDirections(pose);
            }
            if (limbLogService != null && appliedFrameCounter % AggregateLogEvery == 0) {
                limbLogService.Log(LogLevel.Info,
                    "[P0-1] aggregate frames=" + appliedFrameCounter
                    + " leftArm(held=" + leftArmGate.HeldFrames + ",holds=" + leftArmGate.HoldEvents + ",reacq=" + leftArmGate.ReacquireEvents + ")"
                    + " rightArm(held=" + rightArmGate.HeldFrames + ",holds=" + rightArmGate.HoldEvents + ",reacq=" + rightArmGate.ReacquireEvents + ")"
                    + " leftLeg(held=" + leftLegGate.HeldFrames + ",holds=" + leftLegGate.HoldEvents + ",reacq=" + leftLegGate.ReacquireEvents + ")"
                    + " rightLeg(held=" + rightLegGate.HeldFrames + ",holds=" + rightLegGate.HoldEvents + ",reacq=" + rightLegGate.ReacquireEvents + ")");
            }
        }

        // P0-1: apply a limb's two bones through its confidence gate. HIGH conf → fresh solve; LOW/invalid →
        // hold the last valid rotation (never zero). ApplyBone's existing slerp makes re-acquire smooth (no
        // snap). Transitions (VALID↔HELD) are logged sparsely; healthy frames are silent.
        private void ApplyLimb(LimbGate gate, string name, float limbConf,
                               Transform upperBone, Vector3 upperEuler, Transform lowerBone, Vector3 lowerEuler) {
            Vector3 targetUpper;
            Vector3 targetLower;
            bool transitioned;
            if (gate.Resolve(limbConf, limbConfidenceThreshold, upperEuler, lowerEuler,
                             out targetUpper, out targetLower, out transitioned)) {
                ApplyBone(upperBone, targetUpper);
                ApplyBone(lowerBone, targetLower);
            }
            if (transitioned && limbLogService != null) {
                bool held = gate.CurrentState == LimbGate.State.Held;
                limbLogService.Log(LogLevel.Info,
                    "[P0-1] limb=" + name + " conf=" + limbConf.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " thr=" + limbConfidenceThreshold.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " state=" + gate.CurrentState + " action=" + (held ? "HOLD" : "REACQUIRE")
                    + " heldFrames=" + gate.HeldFrames + " confFailures=" + gate.ConfidenceFailures);
            }
        }

        private static float Min3(float a, float b, float c) {
            return Mathf.Min(a, Mathf.Min(b, c));
        }

        // ==================== ARM RETARGET V1 ====================

        /// <summary>
        /// Switch the arm branch between <see cref="ArmAimSolver"/> (true, default) and the original
        /// Kalidokit Euler path (false). Flipping it resets the arm gates + roll state, because the gate's
        /// held payload and the roll history are per-formulation — holding one path's pose into the other
        /// would apply a stale rotation for one frame.
        /// </summary>
        public void SetUseAimArms(bool enabled) {
            if (useAimArms == enabled) {
                return;
            }
            useAimArms = enabled;
            leftArmGate.Reset();
            rightArmGate.Reset();
            leftArmRoll.Reset();
            rightArmRoll.Reset();
        }

        // The control-rig REST FRAME: the frame in which every control bone rests at identity rotation.
        // UniVRM parents the control bones under a "Runtime Control Rig" transform that itself starts at
        // identity, so that transform's live rotation IS the rest frame. Reading it per frame (instead of
        // assuming world == model) keeps the solve correct if the avatar root is ever rotated.
        private Quaternion RigFrame() {
            if (hips != null && hips.parent != null) {
                return hips.parent.rotation;
            }
            return Quaternion.identity;
        }

        // Measure both arms' rest orientation from the live rig at bind time. `lateral` and `up` are derived
        // from the rig itself (upper-arm span and hip->spine), so the rest elbow hinge needs no world
        // constant and no per-avatar tuning.
        private void BuildArmRestBases() {
            leftArmRest = default(ArmRestBasis);
            rightArmRest = default(ArmRestBasis);
            leftArmRoll.Reset();
            rightArmRoll.Reset();
            Quaternion toRig = Quaternion.Inverse(RigFrame());
            Vector3 leftUpper = RestSegment(toRig, leftUpperArm, leftLowerArm);
            Vector3 leftLower = RestSegment(toRig, leftLowerArm, leftHand);
            Vector3 rightUpper = RestSegment(toRig, rightUpperArm, rightLowerArm);
            Vector3 rightLower = RestSegment(toRig, rightLowerArm, rightHand);
            // Lateral = right upper arm -> left upper arm, i.e. the avatar's LEFT. Up = hips -> spine.
            Vector3 lateral = Vector3.zero;
            if (leftUpperArm != null && rightUpperArm != null) {
                lateral = toRig * (leftUpperArm.position - rightUpperArm.position);
            }
            Vector3 up = RestSegment(toRig, hips, spine);
            if (up.sqrMagnitude < 1e-8f) {
                up = Vector3.up;
            }
            leftArmRest = ArmAimSolver.BuildRestBasis(leftUpper, leftLower, lateral, up);
            rightArmRest = ArmAimSolver.BuildRestBasis(rightUpper, rightLower, lateral, up);
            if (limbLogService != null) {
                limbLogService.Log(LogLevel.Info,
                    "[ARM-V1] rest basis measured: left(valid=" + leftArmRest.Valid
                    + " upper=" + Fmt(leftArmRest.UpperDir) + " lower=" + Fmt(leftArmRest.LowerDir)
                    + " hinge=" + Fmt(leftArmRest.BendNormal) + ")"
                    + " right(valid=" + rightArmRest.Valid
                    + " upper=" + Fmt(rightArmRest.UpperDir) + " lower=" + Fmt(rightArmRest.LowerDir)
                    + " hinge=" + Fmt(rightArmRest.BendNormal) + ")");
            }
        }

        private static Vector3 RestSegment(Quaternion toRig, Transform bone, Transform child) {
            if (bone == null || child == null) {
                return Vector3.zero;
            }
            return toRig * (child.position - bone.position);
        }

        /// <summary>
        /// Solve + apply one arm through <see cref="ArmAimSolver"/>, still behind the P0-1 confidence gate.
        /// <paramref name="shoulderIndex"/>/<paramref name="elbowIndex"/>/<paramref name="wristIndex"/> are
        /// the source landmarks for THIS avatar bone under Kalidokit's cross-map (preserved deliberately).
        /// </summary>
        private void ApplyAimArm(LimbGate gate, string name, float limbConf,
                                 Transform upperBone, Transform lowerBone,
                                 ArmRestBasis rest, ref ArmRollState roll,
                                 int shoulderIndex, int elbowIndex, int wristIndex,
                                 out ArmAimResult result) {
            // The solver works in the rig rest frame; landmarks[] is Kalidokit convention (Y DOWN).
            result = ArmAimSolver.Solve(
                KalidokitToRig(landmarks[shoulderIndex]),
                KalidokitToRig(landmarks[elbowIndex]),
                KalidokitToRig(landmarks[wristIndex]),
                rest, ref roll, !mirrorX);

            // A degenerate solve (missing shoulder/elbow) is treated as zero confidence, so the gate HOLDS
            // the last valid rotation instead of the caller inventing one.
            float effectiveConf = result.Valid ? limbConf : 0f;
            Quaternion targetUpper;
            Quaternion targetLower;
            bool transitioned;
            if (gate.Resolve(effectiveConf, limbConfidenceThreshold, result.UpperRig, result.LowerRig,
                             out targetUpper, out targetLower, out transitioned)) {
                // Rig-frame rotation -> bone-local. The upper arm's parent may be Shoulder or Chest (either
                // way it is driven or left at rest by the existing torso path, which this task does not
                // touch), so read the parent's live rotation rather than assuming the chain.
                Quaternion rigFrame = RigFrame();
                if (upperBone != null) {
                    Quaternion parentRig = upperBone.parent != null
                        ? Quaternion.Inverse(rigFrame) * upperBone.parent.rotation
                        : Quaternion.identity;
                    ApplyBoneRotation(upperBone, Quaternion.Inverse(parentRig) * targetUpper);
                }
                // The forearm is expressed relative to the upper arm, so the rig frame cancels exactly.
                ApplyBoneRotation(lowerBone, Quaternion.Inverse(targetUpper) * targetLower);
            }
            if (transitioned && limbLogService != null) {
                bool held = gate.CurrentState == LimbGate.State.Held;
                limbLogService.Log(LogLevel.Info,
                    "[P0-1] limb=" + name + " conf=" + effectiveConf.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " thr=" + limbConfidenceThreshold.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " state=" + gate.CurrentState + " action=" + (held ? "HOLD" : "REACQUIRE")
                    + " heldFrames=" + gate.HeldFrames + " confFailures=" + gate.ConfidenceFailures);
            }
        }

        // landmarks[] is in Kalidokit convention (X = subject's LEFT, Y DOWN, Z = BACK); the control-rig rest
        // frame is the same but Y UP. Pure component map, so it serves for positions and deltas alike.
        private static Vector3 KalidokitToRig(Vector3 v) {
            return new Vector3(v.x, -v.y, v.z);
        }

        /// <summary>DIAG-ONLY: enable the per-bone direction trace (see the <c>traceDirections</c> field).
        /// Default OFF. <paramref name="everyFrames"/> throttles the log (1 = every applied frame).</summary>
        public void SetDirectionTrace(bool enabled, int everyFrames) {
            traceDirections = enabled;
            traceEveryFrames = everyFrames > 0 ? everyFrames : 1;
        }

        // DIAG-ONLY. Reads the CURRENT control-rig bone directions (the transforms were written moments ago in
        // Apply, so the hierarchy is already up to date) and compares each against the direction the source
        // landmarks call for. Logs nothing when the logger is absent or this frame is throttled out.
        private void TraceDirections(KalidokitFullPose pose) {
            if (limbLogService == null || appliedFrameCounter % traceEveryFrames != 0) {
                return;
            }
            TraceBone("leftUpperArm", leftUpperArm, leftLowerArm, 12, 14, pose.UpperArmLeft);
            TraceBone("leftLowerArm", leftLowerArm, leftHand, 14, 16, pose.LowerArmLeft);
            TraceBone("rightUpperArm", rightUpperArm, rightLowerArm, 11, 13, pose.UpperArmRight);
            TraceBone("rightLowerArm", rightLowerArm, rightHand, 13, 15, pose.LowerArmRight);
            TraceBone("leftUpperLeg", leftUpperLeg, leftLowerLeg, 24, 26, pose.UpperLegLeft);
            TraceBone("leftLowerLeg", leftLowerLeg, leftFoot, 26, 28, pose.LowerLegLeft);
            TraceBone("rightUpperLeg", rightUpperLeg, rightLowerLeg, 23, 25, pose.UpperLegRight);
            TraceBone("rightLowerLeg", rightLowerLeg, rightFoot, 25, 27, pose.LowerLegRight);
            if (useAimArms) {
                // ARM RETARGET V1 §21: the aim solver's own inputs/outputs, so a live log can separate a
                // direction error from a roll error (a spinning roll at 0 deg direction error is the
                // helicopter failure mode, and direction accuracy alone would hide it).
                TraceAimArm("leftArm", leftArmAim, leftUpperArm, leftLowerArm, leftHand);
                TraceAimArm("rightArm", rightArmAim, rightUpperArm, rightLowerArm, rightHand);
            }
            // Torso facing: the chest's forward axis vs the source shoulder line's normal. This is the DOF
            // torsoYawScale gates, so it is traced separately from the limb directions.
            if (chest != null) {
                Vector3 shoulderLine = KalidokitToRig(landmarks[11] - landmarks[12]);
                Vector3 sourceForward = Vector3.Cross(Vector3.up, shoulderLine);
                Vector3 wantForward = MirrorX(sourceForward);
                Vector3 gotForward = chest.forward * -1f;   // avatar faces -Z in the model frame
                LogTrace("chestFacing", wantForward, gotForward, Vector3.zero);
            }
        }

        private void TraceBone(string name, Transform bone, Transform child, int sourceFrom, int sourceTo, Vector3 solvedEuler) {
            if (bone == null || child == null) {
                return;
            }
            Vector3 want = MirrorX(KalidokitToRig(landmarks[sourceTo] - landmarks[sourceFrom]));
            Vector3 got = child.position - bone.position;
            LogTrace(name, want, got, solvedEuler);
        }

        // DIAG-ONLY (ARM RETARGET V1 §21).
        private void TraceAimArm(string name, ArmAimResult aim, Transform upperBone, Transform lowerBone, Transform hand) {
            if (!aim.Valid || upperBone == null || lowerBone == null) {
                return;
            }
            Quaternion toRig = Quaternion.Inverse(RigFrame());
            Vector3 gotUpper = toRig * (lowerBone.position - upperBone.position);
            Vector3 gotLower = hand != null ? toRig * (hand.position - lowerBone.position) : Vector3.zero;
            float upperErr = gotUpper.sqrMagnitude > 1e-8f ? Vector3.Angle(aim.TargetUpperDir, gotUpper) : -1f;
            float lowerErr = gotLower.sqrMagnitude > 1e-8f ? Vector3.Angle(aim.TargetLowerDir, gotLower) : -1f;
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            limbLogService.Log(LogLevel.Info,
                "[ARM-V1-TRACE] frame=" + appliedFrameCounter + " arm=" + name
                + " targetUpperDir=" + Fmt(aim.TargetUpperDir) + " gotUpperDir=" + Fmt(gotUpper.normalized)
                + " upperErrDeg=" + upperErr.ToString("F1", ci)
                + " targetForearmDir=" + Fmt(aim.TargetLowerDir) + " gotForearmDir=" + Fmt(gotLower.normalized)
                + " forearmErrDeg=" + lowerErr.ToString("F1", ci)
                + " bendDeg=" + aim.BendDeg.ToString("F1", ci)
                + " rollDeg=" + aim.RollDeg.ToString("F1", ci)
                + " bendNormal=" + Fmt(aim.BendNormal)
                + " normalSource=" + aim.NormalSource);
        }

        private void LogTrace(string name, Vector3 want, Vector3 got, Vector3 solvedEuler) {
            if (want.sqrMagnitude < 1e-8f || got.sqrMagnitude < 1e-8f) {
                return;
            }
            want = want.normalized;
            got = got.normalized;
            float error = Vector3.Angle(want, got);
            limbLogService.Log(LogLevel.Info,
                "[RETARGET-TRACE] frame=" + appliedFrameCounter
                + " bone=" + name
                + " want=" + Fmt(want) + " got=" + Fmt(got)
                + " errDeg=" + error.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + " solverEulerDeg=" + Fmt(solvedEuler * Mathf.Rad2Deg));
        }

        // A mirror reflects across the sagittal plane; combined with Kalidokit's left/right cross-map this is
        // the intended mirror behaviour (see the traceDirections field comment). When mirrorX is ON the input
        // landmarks were ALREADY reflected before the solve, so reflecting again would compare against the
        // wrong side — hence the conditional rather than an unconditional negate.
        private Vector3 MirrorX(Vector3 v) {
            return mirrorX ? v : new Vector3(-v.x, v.y, v.z);
        }

        /// <summary>
        /// TORSO V5: the twist weights for the bones this rig actually has, renormalised to sum to
        /// EXACTLY 1.0. The sum IS the gain of the upper-trunk stage — if a rig lacks UpperChest and
        /// its share were simply dropped, the shoulder line would under-rotate by that share and the
        /// composition's defining property (chain total == shoulderYaw) would silently break. Missing
        /// bones therefore hand their share to the ones that remain, never to nothing.
        /// </summary>
        private void NormaliseTwistWeights(out float wSpine, out float wChest, out float wUpperChest) {
            wSpine = spine != null ? SpineTwistWeight : 0f;
            wChest = chest != null ? ChestTwistWeight : 0f;
            wUpperChest = upperChest != null ? UpperChestTwistWeight : 0f;
            float total = wSpine + wChest + wUpperChest;
            if (total <= 1e-6f) {
                return;                       // no trunk bones at all — nothing to drive
            }
            float inv = 1f / total;
            wSpine = wSpine * inv;
            wChest = wChest * inv;
            wUpperChest = wUpperChest * inv;
        }

        private static string Fmt(Vector3 v) {
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            return "(" + v.x.ToString("F2", ci) + "," + v.y.ToString("F2", ci) + "," + v.z.ToString("F2", ci) + ")";
        }

        // Torso-yaw conditioner (ADR-027): rate-limit (rejects the +/-180 ambiguity flips while still
        // following a genuine gradual turn) -> low-pass -> soft dead-zone (small/noisy yaw snaps to frontal).
        // Self-seeds per signal (NaN sentinel) so there is no startup slew. Input/output radians.
        private float DampYaw(float rawRad, ref float rateLimited, ref float smoothed, float dt) {
            if (float.IsNaN(rateLimited)) {
                rateLimited = rawRad;
                smoothed = rawRad;
            }
            float maxStep = Mathf.Deg2Rad * yawMaxRateDeg * dt;
            rateLimited += Mathf.Clamp(rawRad - rateLimited, -maxStep, maxStep);
            float aFast = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, yawSmoothTau));
            smoothed += aFast * (rateLimited - smoothed);
            float lo = Mathf.Deg2Rad * yawDeadzoneLoDeg;
            float hi = Mathf.Deg2Rad * yawDeadzoneHiDeg;
            float gate = Mathf.SmoothStep(0f, 1f, (Mathf.Abs(smoothed) - lo) / Mathf.Max(0.001f, hi - lo));
            return smoothed * gate;
        }

        private void ApplyBone(Transform bone, Vector3 eulerRadians) {
            if (bone == null) {
                return;
            }
            // Normalized-bone rest is identity, so the converted quaternion is the absolute local rotation
            // (matches three-vrm getNormalizedBoneNode). Slerp for damping.
            Quaternion target = KMath.ThreeEulerToUnity(eulerRadians, eulerSigns, flipQuat);
            bone.localRotation = Quaternion.Slerp(bone.localRotation, target, lerpAmount);
        }

        /// <summary>
        /// Quaternion form of <see cref="ApplyBone"/>, for the quaternion-native arm solver: identical
        /// ownership and identical <see cref="lerpAmount"/> damping, minus the Euler round-trip that
        /// <c>ThreeEulerToUnity</c> would otherwise force (ARM RETARGET V1 §5). Normalized-bone rest is
        /// identity, so the supplied rotation IS the absolute local rotation.
        /// </summary>
        private void ApplyBoneRotation(Transform bone, Quaternion localRotation) {
            if (bone == null) {
                return;
            }
            bone.localRotation = Quaternion.Slerp(bone.localRotation, localRotation, lerpAmount);
        }

        public void SetFingerTuning(Vector3 curlAxis, float weight) {
            fingerCurlAxis = curlAxis;
            fingerWeight = Mathf.Clamp01(weight);
        }

        public void SetWristWeight(float weight) {
            wristWeight = Mathf.Clamp01(weight);
        }

        /// <summary>P0-1: centralized limb-confidence threshold (live-tunable). A limb whose weakest driving
        /// joint's confidence is below this holds its last valid rotation instead of applying a fresh (possibly
        /// origin-collapsed) solve.</summary>
        public void SetLimbConfidence(float threshold) {
            limbConfidenceThreshold = Mathf.Clamp01(threshold);
        }

        /// <summary>DIAG-ONLY (P0 acceptance §6): the live per-limb gate state, so an applied-frame log can
        /// PROVE the Unity LimbGate itself entered HOLD (rather than inferring it from the Python hold).
        /// Encoding per limb: 1 = HELD, 0 = VALID. Read-only; drives no behavior.</summary>
        public void GetGateStates(out int lArm, out int rArm, out int lLeg, out int rLeg,
                                  out int lArmHeldFrames, out int rArmHeldFrames,
                                  out int lLegHeldFrames, out int rLegHeldFrames) {
            lArm = leftArmGate.CurrentState == LimbGate.State.Held ? 1 : 0;
            rArm = rightArmGate.CurrentState == LimbGate.State.Held ? 1 : 0;
            lLeg = leftLegGate.CurrentState == LimbGate.State.Held ? 1 : 0;
            rLeg = rightLegGate.CurrentState == LimbGate.State.Held ? 1 : 0;
            lArmHeldFrames = leftArmGate.HeldFrames;
            rArmHeldFrames = rightArmGate.HeldFrames;
            lLegHeldFrames = leftLegGate.HeldFrames;
            rLegHeldFrames = rightLegGate.HeldFrames;
        }

        /// <summary>DIAG-ONLY (P0 acceptance §6/§8): cumulative gate counters — hold starts and re-acquires
        /// per limb, for the acceptance evidence table.</summary>
        public void GetGateCounters(out long holds, out long reacquires, out long confFailures) {
            holds = leftArmGate.HoldEvents + rightArmGate.HoldEvents + leftLegGate.HoldEvents + rightLegGate.HoldEvents;
            reacquires = leftArmGate.ReacquireEvents + rightArmGate.ReacquireEvents + leftLegGate.ReacquireEvents + rightLegGate.ReacquireEvents;
            confFailures = leftArmGate.ConfidenceFailures + rightArmGate.ConfidenceFailures
                         + leftLegGate.ConfidenceFailures + rightLegGate.ConfidenceFailures;
        }

        /// <summary>Optional diagnostics sink for P0-1. Logs only limb VALID↔HELD transitions + periodic
        /// aggregates — never per-frame for healthy joints.</summary>
        public void SetLogger(ILogService log) {
            limbLogService = log;
        }

        public void SetSpineBend(float scale) {
            spineBendScale = scale;
        }

        /// <summary>
        /// Live-tune the waist-bend dynamics (ADR-026). <paramref name="baselineTau"/> (s) trades holding a
        /// SUSTAINED bend (larger) against correcting the distance-drift/systematic lean faster (smaller);
        /// a very large value ~= a fixed neutral. Called each frame from AppBootstrap so it is Inspector-live.
        /// </summary>
        public void SetSpineBendDynamics(float baselineTau) {
            spineBendBaselineTau = Mathf.Max(0.1f, baselineTau);
        }

        /// <summary>Live-tune the damped body-turn amount (ADR-027): 0 = frontal-lock (no yaw), 1 = full
        /// (Kalidokit-damped) turn. Lower this if a noisy/far setup still swings the torso.</summary>
        public void SetTorsoYawScale(float scale) {
            torsoYawScale = Mathf.Clamp01(scale);
        }

        /// <summary>Re-seed the neutral palm (per hand) + the adaptive spine-bend baseline (C key). Press while
        /// standing upright so "no bend" maps to the current relaxed posture.</summary>
        public void Recalibrate() {
            hasLeftPalmNeutral = false;
            hasRightPalmNeutral = false;
            hasSpineBaseline = false;
            spineWarmup.Clear();
            spineBendRateLimited = 0f;
            spineBendSmoothed = 0f;
            hipsYawRate = float.NaN;      // re-seed the torso-yaw conditioners (ADR-027)
            spineYawRate = float.NaN;
            hipsYawSmooth = 0f;
            spineYawSmooth = 0f;
            leftArmRoll.Reset();          // ARM RETARGET V1: drop the carried elbow-plane roll history
            rightArmRoll.Reset();
        }

        // Median of a small buffer (used to seed the spine-bend baseline robustly against a startup outlier).
        private static float Median(List<float> values) {
            if (values == null || values.Count == 0) {
                return 0f;
            }
            List<float> copy = new List<float>(values);
            copy.Sort();
            int mid = copy.Count / 2;
            return (copy.Count % 2 == 1) ? copy[mid] : (copy[mid - 1] + copy[mid]) * 0.5f;
        }

        /// <summary>
        /// Drive the WRIST bend from the hand provider's palm orientation. Applied to the RAW Hand bones, so it
        /// MUST be called AFTER <see cref="ProcessRuntime"/> (the control rig would otherwise overwrite it).
        /// ROLL-FREE swing of the palm's forward axis, delta-from-neutral (ADR-021): bends toward where the
        /// hand points, structurally cannot spin. wristWeight 0 disables (fingers still curl).
        /// </summary>
        public void ApplyWrist(HandFrame frame) {
            if (!bound || frame == null || !frame.IsValid || wristWeight <= 0f) {
                return;
            }
            ApplyWristBone(rawLeftHand, frame.LeftWristTracked, frame.LeftWristRotation, true);
            ApplyWristBone(rawRightHand, frame.RightWristTracked, frame.RightWristRotation, false);
        }

        private void ApplyWristBone(Transform hand, bool tracked, Quaternion palm, bool isLeft) {
            if (hand == null || !tracked) {
                return;
            }
            bool hasNeutral = isLeft ? hasLeftPalmNeutral : hasRightPalmNeutral;
            if (!hasNeutral) {
                if (isLeft) {
                    leftNeutralPalm = palm;
                    hasLeftPalmNeutral = true;
                } else {
                    rightNeutralPalm = palm;
                    hasRightPalmNeutral = true;
                }
                return;
            }
            Quaternion neutral = isLeft ? leftNeutralPalm : rightNeutralPalm;
            Vector3 neutralForward = neutral * Vector3.forward;
            Vector3 currentForward = palm * Vector3.forward;
            if (neutralForward.sqrMagnitude < 1e-8f || currentForward.sqrMagnitude < 1e-8f) {
                return;
            }
            Quaternion swing = Quaternion.FromToRotation(neutralForward, currentForward);
            hand.rotation = Quaternion.Slerp(hand.rotation, swing * hand.rotation, wristWeight);
        }

        /// <summary>
        /// Drive the control-rig FINGER bones from the HandFrame per-finger curls (ADR-022 fingers). Curl-
        /// based (open/close), reusing the existing hand providers; not the full 21-landmark Kalidokit
        /// HandSolver (that needs a close hand source to be worthwhile). Call after <see cref="Apply"/> and
        /// before <see cref="ProcessRuntime"/>.
        /// </summary>
        public void ApplyFingers(HandFrame frame) {
            if (!bound || frame == null || !frame.IsValid || fingerWeight <= 0f) {
                return;
            }
            // Per-hand sign (ADR-023 / port audit A1): the left & right normalized finger bones share a
            // PARALLEL (non-mirrored) local frame, so ONE shared axis+sign curls one hand IN and the other
            // OUT (hyperextends). Kalidokit's HandSolver uses invert = side==Right?1:-1; mirror that here so
            // BOTH hands curl into the palm. If both hyperextend, flip kalidokitFingerCurlAxis (tunes both).
            ApplyHandFingers(leftFingers, frame.LeftThumbCurl, frame.LeftIndexCurl, frame.LeftMiddleCurl, frame.LeftRingCurl, frame.LeftLittleCurl, -1f);
            ApplyHandFingers(rightFingers, frame.RightThumbCurl, frame.RightIndexCurl, frame.RightMiddleCurl, frame.RightRingCurl, frame.RightLittleCurl, 1f);
        }

        private void ApplyHandFingers(List<FingerJoint> list, float thumb, float index, float middle, float ring, float little, float sideSign) {
            float[] curls = new float[] { thumb, index, middle, ring, little };
            int k = 0;
            while (k < list.Count) {
                FingerJoint joint = list[k];
                k = k + 1;
                if (joint.Bone == null) {
                    continue;
                }
                float curl = Mathf.Clamp01(curls[joint.Finger]) * fingerWeight;
                Quaternion rot = Quaternion.AngleAxis(curl * joint.MaxDegrees * sideSign, fingerCurlAxis);
                joint.Bone.localRotation = Quaternion.Slerp(joint.Bone.localRotation, joint.Rest * rot, lerpAmount);
            }
        }

        private void BindFingers() {
            leftFingers.Clear();
            rightFingers.Clear();
            // finger index: 0=thumb,1=index,2=middle,3=ring,4=little; maxDeg per joint (proximal/inter/distal).
            AddFinger(leftFingers, HumanBodyBones.LeftThumbProximal, 0, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftThumbIntermediate, 0, 30f);
            AddFinger(leftFingers, HumanBodyBones.LeftThumbDistal, 0, 30f);
            AddFinger(leftFingers, HumanBodyBones.LeftIndexProximal, 1, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftIndexIntermediate, 1, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftIndexDistal, 1, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftMiddleProximal, 2, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftMiddleIntermediate, 2, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftMiddleDistal, 2, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftRingProximal, 3, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftRingIntermediate, 3, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftRingDistal, 3, 40f);
            AddFinger(leftFingers, HumanBodyBones.LeftLittleProximal, 4, 50f);
            AddFinger(leftFingers, HumanBodyBones.LeftLittleIntermediate, 4, 60f);
            AddFinger(leftFingers, HumanBodyBones.LeftLittleDistal, 4, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightThumbProximal, 0, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightThumbIntermediate, 0, 30f);
            AddFinger(rightFingers, HumanBodyBones.RightThumbDistal, 0, 30f);
            AddFinger(rightFingers, HumanBodyBones.RightIndexProximal, 1, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightIndexIntermediate, 1, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightIndexDistal, 1, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightMiddleProximal, 2, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightMiddleIntermediate, 2, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightMiddleDistal, 2, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightRingProximal, 3, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightRingIntermediate, 3, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightRingDistal, 3, 40f);
            AddFinger(rightFingers, HumanBodyBones.RightLittleProximal, 4, 50f);
            AddFinger(rightFingers, HumanBodyBones.RightLittleIntermediate, 4, 60f);
            AddFinger(rightFingers, HumanBodyBones.RightLittleDistal, 4, 40f);
        }

        private void AddFinger(List<FingerJoint> list, HumanBodyBones bone, int finger, float maxDegrees) {
            Transform t = controlRig.GetBoneTransform(bone);
            if (t == null) {
                return;
            }
            FingerJoint joint;
            joint.Bone = t;
            joint.Rest = t.localRotation;
            joint.Finger = finger;
            joint.MaxDegrees = maxDegrees;
            list.Add(joint);
        }
    }
}
