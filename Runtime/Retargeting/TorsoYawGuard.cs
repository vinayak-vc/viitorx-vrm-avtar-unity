using UnityEngine;

namespace VirtualMirror.Retargeting {
    /// <summary>What the torso-yaw guard did with this frame's measurement (diagnostics + policy).</summary>
    public enum TorsoYawState {
        /// <summary>Nothing has ever been accepted; the caller must drive frontal.</summary>
        NoValue = 0,
        /// <summary>Fresh measurement, inside the validated envelope. Used as-is.</summary>
        Valid = 1,
        /// <summary>|yaw| exceeded <see cref="TorsoYawGuard.WrapGuardDeg"/> — the sign is not
        /// trustworthy, so the last valid yaw is held rather than followed into the ±180° flip.</summary>
        WrapGuarded = 2,
        /// <summary>Fresh and non-wrapping, but beyond the F-15 validated range, so it is clamped to
        /// the envelope rather than believed.</summary>
        OutOfRange = 3,
    }

    /// <summary>Carried state for <see cref="TorsoYawGuard"/>. Struct + no allocation, as TrunkGateState.</summary>
    public struct TorsoYawGuardState {
        /// <summary>True once a measurement has ever been accepted (nothing is held before that).</summary>
        public bool HasValid;
        public float HeldHipYaw;         // radians, UNCLAMPED — the clamp is applied on output only,
        public float HeldShoulderYaw;    // so returning into range restores the true value immediately.
        /// <summary>Consecutive frames the wrap guard has been holding. 0 while accepting.</summary>
        public int HoldStreak;

        // ---- diagnostics only; never read by the policy (V6 §10 asks for hold statistics) ----
        public int FramesTotal;
        public int FramesHeld;
        public int FramesOutOfRange;
        public int LongestHold;
        public int HoldRuns;
        public int HoldFramesSum;

        public void Reset() {
            HasValid = false;
            HeldHipYaw = 0f;
            HeldShoulderYaw = 0f;
            HoldStreak = 0;
            FramesTotal = 0;
            FramesHeld = 0;
            FramesOutOfRange = 0;
            LongestHold = 0;
            HoldRuns = 0;
            HoldFramesSum = 0;
        }
    }

    /// <summary>Result of one guard evaluation. Yaws are radians and already range-limited.</summary>
    public struct TorsoYawGuardResult {
        public bool HasValue;
        public float HipYaw;
        public float ShoulderYaw;
        public TorsoYawState State;
        /// <summary>How many consecutive frames the wrap guard has now been holding (0 when fresh).</summary>
        public int HoldFrames;
    }

    /// <summary>
    /// TORSO V6 — the wrap guard and the explicit operating envelope, between the V5 trunk gate and
    /// the V5 composition. It changes only which yaw the composition is allowed to see; it does not
    /// change the composition, the weights, the conditioner, or how the yaw is measured.
    ///
    /// <para><b>Why a wrap guard.</b> The torso yaw is <c>atan2(Δx, Δz)</c> over the shoulder line.
    /// As the subject turns edge-on, <c>Δx → 0</c> and the angle becomes unstable and flips through
    /// ±180°. F-10 measured the symptom (sign accuracy 78.49 %, and 89.52 % once the ±180° wrap is
    /// excluded); F-14 showed the flip is a property of the <i>composition</i>, not of the depth —
    /// a wrap-guarded <c>yaw3D</c> reached the perfect-sign ceiling (MAE 8.34° = the unsigned
    /// magnitude's own error, 0.00 % wrong sign) and the raw shoulder Δz matched it to the decimal
    /// while rescuing 0 frames. F-15 replicated that on a second subject: 1 true rescue in 318
    /// frames. So the guard is the whole fix, and no additional sign source is warranted.</para>
    ///
    /// <para><b>Why an explicit range.</b> F-15 held ±30/±45/±60 for the first time and measured the
    /// shoulder line running 63.8 px front-on → 17.3 px at 60° → <b>7–8 px at ±90°</b>. At that width
    /// two 5×5 depth windows land on one surface, so the sign has nothing to read. Block-level over
    /// the label-validated whole-body blocks: <b>|heading| ≤ 60° → 6 correct, 0 wrong, 0 no-signal;
    /// |heading| = 90° → 0 correct, 1 wrong, 1 no-signal.</b> The envelope is therefore a measured
    /// property of the rig at this baseline and resolution, not a tuning knob.</para>
    ///
    /// <para><b>Out-of-range policy is a CLAMP, deliberately.</b> Clamping is continuous — no snap at
    /// the boundary — deterministic, and it never extrapolates through the unreliable region. It
    /// keeps the sign, which F-15 validated, and drops only the magnitude, which it did not. Holding
    /// instead would freeze the avatar mid-turn; following the raw value would import the very
    /// measurement F-15 showed is wrong at ±90°.</para>
    ///
    /// <para><b>Hip and shoulder are guarded TOGETHER</b>, exactly as in <see cref="TrunkGate"/>: the
    /// V5 composition consumes <c>shoulderYaw − hipYaw</c>, so mixing a fresh value with a held one
    /// would fabricate a twist the human never made.</para>
    ///
    /// <para><b>What this does NOT solve.</b> <c>|yaw2D| = acos(...)</c> is bounded to 90°, so a
    /// correct sign still yields only −90…+90 (ADR-041). SIGN and MAGNITUDE-BEYOND-90 are separate
    /// problems and this class addresses only the first. See ADR-045.</para>
    /// </summary>
    public static class TorsoYawGuard {
        /// <summary>Degrees. The F-10 wrap guard, validated in F-14 and replicated in F-15. Above this
        /// the atan2 composition is in its ±180° flip and the sign cannot be trusted. NOT a new
        /// threshold — deliberately the same 150° F-10 proposed and F-14/F-15 scored.</summary>
        public const float WrapGuardDeg = 150f;

        /// <summary>Degrees. The F-15 validated operating envelope. Beyond this the shoulder line is
        /// too foreshortened for the sign to be measurable (17.3 px at 60°, 7–8 px at ±90°).</summary>
        public const float ValidatedRangeDeg = 60f;

        /// <summary>
        /// Apply the wrap guard and the operating envelope to one gated trunk measurement.
        /// <paramref name="hipYaw"/> / <paramref name="shoulderYaw"/> are <see cref="TrunkGate"/>'s
        /// output in radians (fresh or already held by that gate); <paramref name="trunkFresh"/> is
        /// its <c>Fresh</c> flag. Returns range-limited yaws plus the state that produced them.
        /// </summary>
        public static TorsoYawGuardResult Evaluate(
                float hipYaw, float shoulderYaw, bool trunkFresh,
                ref TorsoYawGuardState state) {
            TorsoYawGuardResult r = default(TorsoYawGuardResult);
            state.FramesTotal = state.FramesTotal + 1;

            bool finite = !float.IsNaN(hipYaw) && !float.IsInfinity(hipYaw)
                          && !float.IsNaN(shoulderYaw) && !float.IsInfinity(shoulderYaw);

            // The wrap guard fires on EITHER line. The composition uses their difference, so a wrapped
            // hip with a clean shoulder would fabricate a ~180 deg twist.
            float wrap = Mathf.Deg2Rad * WrapGuardDeg;
            bool wrapped = !finite
                           || Mathf.Abs(hipYaw) > wrap
                           || Mathf.Abs(shoulderYaw) > wrap;

            if (wrapped || !trunkFresh) {
                // HOLD. Nothing is accepted into the held state, so a wrap can never become the value
                // the avatar reacquires to. Before the first accepted frame there is nothing to hold
                // and the caller drives frontal.
                if (!state.HasValid) {
                    r.HasValue = false;
                    r.State = TorsoYawState.NoValue;
                    r.HipYaw = 0f;
                    r.ShoulderYaw = 0f;
                    r.HoldFrames = 0;
                    return r;
                }
                if (state.HoldStreak == 0) {
                    state.HoldRuns = state.HoldRuns + 1;
                }
                state.HoldStreak = state.HoldStreak + 1;
                state.FramesHeld = state.FramesHeld + 1;
                state.HoldFramesSum = state.HoldFramesSum + 1;
                if (state.HoldStreak > state.LongestHold) {
                    state.LongestHold = state.HoldStreak;
                }
                r.HasValue = true;
                r.State = wrapped ? TorsoYawState.WrapGuarded : TorsoYawState.Valid;
                r.HoldFrames = state.HoldStreak;
                r.HipYaw = ClampToRange(state.HeldHipYaw);
                r.ShoulderYaw = ClampToRange(state.HeldShoulderYaw);
                return r;
            }

            // ACCEPT. Store UNCLAMPED so that returning into the envelope restores the true value on
            // the very next frame instead of easing back from the boundary.
            state.HasValid = true;
            state.HeldHipYaw = hipYaw;
            state.HeldShoulderYaw = shoulderYaw;
            state.HoldStreak = 0;

            float limit = Mathf.Deg2Rad * ValidatedRangeDeg;
            bool beyond = Mathf.Abs(hipYaw) > limit || Mathf.Abs(shoulderYaw) > limit;
            if (beyond) {
                state.FramesOutOfRange = state.FramesOutOfRange + 1;
            }
            r.HasValue = true;
            r.State = beyond ? TorsoYawState.OutOfRange : TorsoYawState.Valid;
            r.HoldFrames = 0;
            r.HipYaw = ClampToRange(hipYaw);
            r.ShoulderYaw = ClampToRange(shoulderYaw);
            return r;
        }

        /// <summary>Clamp to the validated envelope. Continuous at the boundary, so crossing it
        /// produces no snap; the sign (validated) survives and only the magnitude (not validated
        /// beyond 60°) is limited.</summary>
        public static float ClampToRange(float radians) {
            float limit = Mathf.Deg2Rad * ValidatedRangeDeg;
            return Mathf.Clamp(radians, -limit, limit);
        }

        /// <summary>Mean hold length in frames over completed and current hold runs; 0 if never held.
        /// Diagnostics only (V6 §10).</summary>
        public static float AverageHoldFrames(ref TorsoYawGuardState state) {
            return state.HoldRuns > 0 ? (float)state.HoldFramesSum / state.HoldRuns : 0f;
        }

        /// <summary>Share of evaluated frames the guard held, in percent. Diagnostics only.</summary>
        public static float HeldPercent(ref TorsoYawGuardState state) {
            return state.FramesTotal > 0 ? 100f * state.FramesHeld / state.FramesTotal : 0f;
        }
    }
}
