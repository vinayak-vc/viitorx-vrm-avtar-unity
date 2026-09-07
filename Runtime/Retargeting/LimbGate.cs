using UnityEngine;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// P0-1 confidence-gated limb hold (audit AUDIT_FBT_2026-09-07 §F-01). A limb's driving joints
    /// (shoulder/elbow/wrist or hip/knee/ankle) can arrive invalid (confidence 0 = zero-filled by the
    /// sidecar emit gate) or low-confidence; feeding those to the Kalidokit solve produces a rotation
    /// aimed at the origin → the limb collapses. This gate sits at APPLICATION time (not inside the solver)
    /// and decides, per limb, whether to APPLY the freshly-solved rotation or HOLD the last valid one.
    ///
    /// States are intentionally minimal (VALID / HELD) — the P1 TRACKED/PREDICTED/LOST machine is out of
    /// scope. The gate never substitutes zero: a low/invalid limb keeps its last good rotation, so the
    /// existing per-bone slerp re-acquires smoothly (no snap) when confidence returns.
    ///
    /// Each limb has exactly two driven bones (upper + lower), so two euler slots are held with no per-frame
    /// allocation. Pure logic → unit-testable without a VRM (see the P0 validation harness).
    /// </summary>
    public sealed class LimbGate {
        public enum State {
            Valid,
            Held,
        }

        private Vector3 lastUpper;
        private Vector3 lastLower;
        private bool hasValid;

        public State CurrentState = State.Valid;
        public int HeldFrames;
        public long HoldEvents;        // count of VALID→HELD transitions (a fresh hold began)
        public long ReacquireEvents;   // count of HELD→VALID transitions (limb re-acquired)
        public long ConfidenceFailures; // count of frames the limb confidence was below threshold

        /// <summary>
        /// Decide the target eulers for this limb's two bones.
        /// Returns false only when there is NO valid state to hold yet (startup + invalid) — the caller then
        /// leaves the bones untouched (rest), never zero. Otherwise returns true with (upper,lower):
        ///   limbConf ≥ threshold → the fresh solve (VALID),
        ///   limbConf &lt; threshold → the last valid solve (HELD).
        /// <paramref name="transitioned"/> is true on a VALID↔HELD state change (for sparse logging).
        /// </summary>
        public bool Resolve(float limbConf, float threshold, Vector3 freshUpper, Vector3 freshLower,
                            out Vector3 targetUpper, out Vector3 targetLower, out bool transitioned) {
            transitioned = false;
            if (limbConf >= threshold) {
                if (CurrentState == State.Held && hasValid) {
                    ReacquireEvents = ReacquireEvents + 1;
                    transitioned = true;
                }
                lastUpper = freshUpper;
                lastLower = freshLower;
                hasValid = true;
                HeldFrames = 0;
                CurrentState = State.Valid;
                targetUpper = freshUpper;
                targetLower = freshLower;
                return true;
            }

            ConfidenceFailures = ConfidenceFailures + 1;
            if (!hasValid) {
                // Nothing valid ever seen — do NOT invent a rotation (that would be the origin-collapse bug).
                targetUpper = Vector3.zero;
                targetLower = Vector3.zero;
                return false;
            }
            if (CurrentState != State.Held) {
                HoldEvents = HoldEvents + 1;
                transitioned = true;
            }
            CurrentState = State.Held;
            HeldFrames = HeldFrames + 1;
            targetUpper = lastUpper;
            targetLower = lastLower;
            return true;
        }

        public void Reset() {
            hasValid = false;
            HeldFrames = 0;
            CurrentState = State.Valid;
            // counters are cumulative diagnostics; intentionally not reset here.
        }
    }
}
