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
        private const float HipsYawDamp = 0.7f;       // Kalidokit rigRotation dampeners (reference demo values)
        private const float SpineYawDamp = 0.45f;
        private const float ChestYawDamp = 0.25f;
        private float torsoYawScale = 1f;             // 0 = frontal-lock, 1 = full (live-tunable)
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

            // Torso YAW (ADR-027): reject the +/-180 ambiguity flips + OAK depth noise, follow real turns.
            // Applied to hips + spine yaw, scaled by torsoYawScale (0 = frontal-lock). Roll stays gated by
            // torsoRoll (0 = upright). Bend keeps its own Spine/Chest split (ADR-026).
            float hipsYaw = DampYaw(pose.Hips.y, ref hipsYawRate, ref hipsYawSmooth, dt) * torsoYawScale;
            float spineYaw = DampYaw(pose.Spine.y, ref spineYawRate, ref spineYawSmooth, dt) * torsoYawScale;
            float spineRoll = pose.Spine.z * torsoRoll;
            float hipsRoll = pose.Hips.z * torsoRoll;
            float spineShare = chest != null ? 0.5f : 1f;

            // Kalidokit per-bone dampeners on yaw/roll (Hips 0.7, Spine 0.45, Chest 0.25); the waist bend keeps
            // its intended total (spineShare + 0.5 = 1.0 across Spine+Chest).
            ApplyBone(spine, new Vector3(bend * spineShare, spineYaw * SpineYawDamp, spineRoll * SpineYawDamp));
            if (chest != null) {
                ApplyBone(chest, new Vector3(bend * 0.5f, spineYaw * ChestYawDamp, spineRoll * ChestYawDamp));
            }
            // P0-1: confidence-gated arms. Kalidokit cross-maps sides — pose.*Left is solved from landmarks
            // 12/14/16 (right shoulder/elbow/wrist), pose.*Right from 11/13/15 — so gate on those.
            ApplyLimb(leftArmGate, "leftArm", Min3(conf[12], conf[14], conf[16]),
                      leftUpperArm, pose.UpperArmLeft, leftLowerArm, pose.LowerArmLeft);
            ApplyLimb(rightArmGate, "rightArm", Min3(conf[11], conf[13], conf[15]),
                      rightUpperArm, pose.UpperArmRight, rightLowerArm, pose.LowerArmRight);
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
            ApplyBone(hips, new Vector3(0f, hipsYaw * HipsYawDamp, hipsRoll * HipsYawDamp));

            appliedFrameCounter = appliedFrameCounter + 1;
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
