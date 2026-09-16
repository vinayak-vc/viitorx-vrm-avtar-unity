using UnityEngine;

namespace VirtualMirror.Retargeting {
    /// <summary>Why a trunk measurement was refused this frame (diagnostics only).</summary>
    public enum TrunkRejectReason {
        None = 0,
        NotFinite = 1,
        ShoulderSpan = 2,
        HipSpan = 3,
        SpanImplausible = 4,
        YawJump = 5,
    }

    /// <summary>Carried state for <see cref="TrunkGate"/>. Struct + no allocation, as ArmRollState.</summary>
    public struct TrunkGateState {
        /// <summary>True once a measurement has ever been accepted (nothing is held before that).</summary>
        public bool HasValid;
        public float HeldHipYaw;         // radians
        public float HeldShoulderYaw;    // radians
        /// <summary>Consecutive continuity rejections; re-acquires at <see cref="TrunkGate.ReacquireFrames"/>.</summary>
        public int RejectStreak;

        public void Reset() {
            HasValid = false;
            HeldHipYaw = 0f;
            HeldShoulderYaw = 0f;
            RejectStreak = 0;
        }
    }

    public struct TrunkGateResult {
        /// <summary>A fresh measurement was accepted this frame.</summary>
        public bool Fresh;
        /// <summary>False only before the first accepted frame — the caller must then drive frontal.</summary>
        public bool HasValue;
        public float HipYaw;             // radians; fresh or held
        public float ShoulderYaw;        // radians; fresh or held
        public TrunkRejectReason Reason;
    }

    /// <summary>
    /// TORSO V5 — validity gate for the 4-point trunk (both shoulders + both hips).
    ///
    /// <para>The torso yaw is <c>atan2(Δx, Δz)</c> over a landmark PAIR, and Kalidokit's
    /// <c>CalcHipsAndSpine</c> reads those four landmarks with no validation at all (P0-1
    /// <c>LimbGate</c> covers arms and legs only). When the sidecar has no detection it emits
    /// <c>[0,0,0]</c>, and then <c>atan2(0,0) = 0</c> which the <c>+0.5</c> jump-fix turns into
    /// <b>exactly +90° of commanded yaw</b> — measured live on an empty room, and previously masked
    /// only because <c>torsoYawScale</c> was 0. This gate exists so that can never reach the avatar.</para>
    ///
    /// <para>It is deliberately GEOMETRIC, not confidence-based: the V4 report measured landmark
    /// confidence of 0.71 accompanying a fully hallucinated person, so a confidence threshold does not
    /// separate the populations. Semantic hallucination is explicitly out of scope here; what this
    /// gate guarantees is that a <i>degenerate or discontinuous</i> measurement is never converted into
    /// a confident yaw command.</para>
    ///
    /// <para>Hip and shoulder are gated TOGETHER and never independently. The V5 composition consumes
    /// <c>shoulderYaw − hipYaw</c>, so mixing a fresh value with a held one would fabricate a twist
    /// that the human never made.</para>
    ///
    /// <para>Thresholds are measured, not guessed — see
    /// <c>python-sidecar~/trunk_gate_thresholds.py</c> over 111,546 recorded frames:</para>
    /// <list type="bullet">
    /// <item>shoulder span: degenerate p95 <b>0.054 m</b> vs real p01 <b>0.156 m</b></item>
    /// <item>hip span: degenerate p95 <b>0.091 m</b> vs real p01 <b>0.123 m</b></item>
    /// <item>a <b>0.10 m</b> floor rejects <b>0.000 %</b> of real shoulder lines and <b>0.195 %</b> of real hip lines</item>
    /// <item>real yaw rate p99 <b>129 °/s</b>, p99.9 <b>435 °/s</b>; a <b>500 °/s</b> ceiling rejects <b>0.078 %</b></item>
    /// </list>
    /// </summary>
    public static class TrunkGate {
        /// <summary>Metres. Below this a shoulder/hip line is degenerate. Real lines start at 0.123 m (p01).</summary>
        public const float MinSpan = 0.10f;
        /// <summary>Metres. A human trunk line is never this wide; the corpus p95 is 0.366 m. Catches gross breakage.</summary>
        public const float MaxSpan = 1.0f;
        /// <summary>deg/s. Sits far above the conditioner's own 140 °/s slew so the two never fight:
        /// the conditioner SMOOTHS the plausible, this REJECTS the impossible before it enters the filter.</summary>
        public const float MaxYawRateDegPerSec = 500f;
        /// <summary>Consecutive continuity rejections before the gate re-seeds, so a genuine teleport
        /// (a different person steps in) cannot lock the trunk out forever. ~0.3 s at 21–30 Hz.</summary>
        public const int ReacquireFrames = 8;

        private const float MinDt = 1e-4f;

        /// <summary>
        /// Validate one trunk measurement. <paramref name="solvedHipYaw"/> and
        /// <paramref name="solvedShoulderYaw"/> are the solver's own y-channel outputs (radians), passed
        /// in rather than recomputed so the gate and the solver can never disagree about the angle.
        /// </summary>
        public static TrunkGateResult Evaluate(
                Vector3 shoulderL, Vector3 shoulderR, Vector3 hipL, Vector3 hipR,
                float solvedHipYaw, float solvedShoulderYaw, float dt,
                ref TrunkGateState state) {
            TrunkGateResult r = default(TrunkGateResult);
            r.Reason = TrunkRejectReason.None;

            TrunkRejectReason bad = Validate(shoulderL, shoulderR, hipL, hipR,
                                             solvedHipYaw, solvedShoulderYaw);
            if (bad == TrunkRejectReason.None && state.HasValid) {
                // Continuity: reject a step no human trunk could make. Checked on BOTH lines; either
                // failing rejects the whole frame, because the composition uses their difference.
                float step = Mathf.Max(
                    Mathf.Abs(WrapPi(solvedShoulderYaw - state.HeldShoulderYaw)),
                    Mathf.Abs(WrapPi(solvedHipYaw - state.HeldHipYaw)));
                float limit = Mathf.Deg2Rad * MaxYawRateDegPerSec * Mathf.Max(MinDt, dt);
                if (step > limit) {
                    bad = TrunkRejectReason.YawJump;
                }
            }

            if (bad == TrunkRejectReason.None) {
                state.HasValid = true;
                state.RejectStreak = 0;
                state.HeldHipYaw = solvedHipYaw;
                state.HeldShoulderYaw = solvedShoulderYaw;
                r.Fresh = true;
            } else if (bad == TrunkRejectReason.YawJump &&
                       state.RejectStreak + 1 >= ReacquireFrames) {
                // Sustained disagreement with the held value IS the new truth — re-seed rather than
                // hold a stale pose forever.
                state.RejectStreak = 0;
                state.HeldHipYaw = solvedHipYaw;
                state.HeldShoulderYaw = solvedShoulderYaw;
                r.Fresh = true;
                r.Reason = TrunkRejectReason.None;
                r.HasValue = true;
                r.HipYaw = state.HeldHipYaw;
                r.ShoulderYaw = state.HeldShoulderYaw;
                return r;
            } else {
                // Only a continuity rejection accumulates; a degenerate frame carries no candidate to
                // re-acquire TO, so it must not push the gate towards accepting one.
                state.RejectStreak = bad == TrunkRejectReason.YawJump ? state.RejectStreak + 1 : 0;
                r.Reason = bad;
            }

            r.HasValue = state.HasValid;
            r.HipYaw = state.HasValid ? state.HeldHipYaw : 0f;
            r.ShoulderYaw = state.HasValid ? state.HeldShoulderYaw : 0f;
            return r;
        }

        /// <summary>Geometry-only validity, exposed so tests can assert the reason without the state machine.</summary>
        public static TrunkRejectReason Validate(Vector3 shoulderL, Vector3 shoulderR,
                                                 Vector3 hipL, Vector3 hipR,
                                                 float solvedHipYaw, float solvedShoulderYaw) {
            if (!IsFinite(shoulderL) || !IsFinite(shoulderR) || !IsFinite(hipL) || !IsFinite(hipR) ||
                !IsFinite(solvedHipYaw) || !IsFinite(solvedShoulderYaw)) {
                return TrunkRejectReason.NotFinite;
            }
            // Span is the full 3-D separation, not just |Δx|: a line seen edge-on has a small Δx but is
            // still a real measurement, and using |Δx| alone would reject exactly the turned poses this
            // work exists to support.
            float shoulderSpan = (shoulderL - shoulderR).magnitude;
            float hipSpan = (hipL - hipR).magnitude;
            if (shoulderSpan < MinSpan) {
                return TrunkRejectReason.ShoulderSpan;
            }
            if (hipSpan < MinSpan) {
                return TrunkRejectReason.HipSpan;
            }
            if (shoulderSpan > MaxSpan || hipSpan > MaxSpan) {
                return TrunkRejectReason.SpanImplausible;
            }
            return TrunkRejectReason.None;
        }

        private static bool IsFinite(Vector3 v) {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        private static bool IsFinite(float f) {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        /// <summary>Shortest signed difference on the circle, radians.</summary>
        private static float WrapPi(float radians) {
            float a = Mathf.Repeat(radians + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            return a;
        }
    }
}
