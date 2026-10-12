using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// States A-F of the showcase in one place. Pure logic.
    ///
    /// Input each frame: is a person tracked right now, and how strongly each interaction point moves.
    /// Output: the state, the injection gate (0 = no target may inject, eased so it never snaps), the
    /// detection-swirl envelope, and one-frame flags. The caller decides what "tracked" means (fresh
    /// data AND a measured torso); this class debounces it.
    ///
    /// It never resets the fluid. A person entering or leaving only opens or closes the gate.
    /// </summary>
    public sealed class FluidDisruptionStateController {
        private float trackedFor;
        private float lostFor;
        private float stillFor;
        private float stateTime;

        public DisruptionState State { get; private set; }

        /// <summary>0..1, eased. Zero in A and F: nothing person-driven may inject.</summary>
        public float Gate { get; private set; }

        /// <summary>0..1 envelope of the detection swirl; non-zero only in state B.</summary>
        public float Pulse { get; private set; }

        /// <summary>True for exactly one frame when a person is first confirmed (entering B). The
        /// caller snaps its smoothing to the new person and restarts the movement estimators.</summary>
        public bool PersonAppeared { get; private set; }

        /// <summary>True for exactly one frame when the state changed.</summary>
        public bool Changed { get; private set; }

        /// <summary>True while a person is being followed (B to E).</summary>
        public bool PersonPresent {
            get {
                return State != DisruptionState.Calm && State != DisruptionState.Lost;
            }
        }

        public void Reset() {
            trackedFor = 0f;
            lostFor = 0f;
            stillFor = 0f;
            stateTime = 0f;
            State = DisruptionState.Calm;
            Gate = 0f;
            Pulse = 0f;
            PersonAppeared = false;
            Changed = false;
        }

        public void Tick(bool trackedNow, float leftIntensity, float rightIntensity, float torsoIntensity,
                         float dt, FluidDisruptionSettings s) {
            PersonAppeared = false;
            Changed = false;
            if (dt <= 0f) {
                return;
            }
            stateTime += dt;

            if (trackedNow) {
                trackedFor += dt;
                lostFor = 0f;
            } else {
                lostFor += dt;
                trackedFor = 0f;
            }

            float strongest = Mathf.Max(torsoIntensity, Mathf.Max(leftIntensity, rightIntensity));
            bool bothHands = leftIntensity > s.movingThreshold && rightIntensity > s.movingThreshold;

            switch (State) {
                case DisruptionState.Calm:
                    if (trackedFor >= s.enterSeconds) {
                        Enter(DisruptionState.Detected);
                        PersonAppeared = true;
                    }
                    break;

                case DisruptionState.Detected:
                    if (lostFor >= s.lostTimeoutSeconds) {
                        Enter(DisruptionState.Lost);
                    } else if (stateTime >= s.pulseSeconds) {
                        Enter(Movement(strongest, bothHands, s));
                    }
                    break;

                case DisruptionState.Active:
                case DisruptionState.Interacting:
                    if (lostFor >= s.lostTimeoutSeconds) {
                        Enter(DisruptionState.Lost);
                        break;
                    }
                    if (strongest < s.stillThreshold) {
                        stillFor += dt;
                        if (stillFor >= s.settleHoldSeconds) {
                            Enter(DisruptionState.Settling);
                        }
                    } else {
                        stillFor = 0f;
                        Enter(bothHands ? DisruptionState.Interacting : DisruptionState.Active);
                    }
                    break;

                case DisruptionState.Settling:
                    if (lostFor >= s.lostTimeoutSeconds) {
                        Enter(DisruptionState.Lost);
                    } else if (strongest > s.movingThreshold) {
                        Enter(bothHands ? DisruptionState.Interacting : DisruptionState.Active);
                    }
                    break;

                case DisruptionState.Lost:
                    if (trackedFor >= s.enterSeconds) {
                        // Tracking returned: start again from the new, valid pose.
                        Enter(DisruptionState.Detected);
                        PersonAppeared = true;
                    } else if (stateTime >= s.lostHoldSeconds) {
                        Enter(DisruptionState.Calm);
                    }
                    break;
            }

            bool injecting = State == DisruptionState.Detected || State == DisruptionState.Active
                             || State == DisruptionState.Interacting || State == DisruptionState.Settling;
            float target = injecting && trackedNow ? 1f : 0f;
            Gate = Mathf.MoveTowards(Gate, target, dt / Mathf.Max(0.02f, s.gateSeconds));

            if (State == DisruptionState.Detected) {
                float t = Mathf.Clamp01(stateTime / Mathf.Max(0.05f, s.pulseSeconds));
                Pulse = Mathf.Sin(t * Mathf.PI);
            } else {
                Pulse = 0f;
            }
        }

        private static DisruptionState Movement(float strongest, bool bothHands, FluidDisruptionSettings s) {
            if (strongest > s.movingThreshold) {
                return bothHands ? DisruptionState.Interacting : DisruptionState.Active;
            }
            return DisruptionState.Settling;
        }

        private void Enter(DisruptionState next) {
            if (next == State) {
                return;
            }
            State = next;
            Changed = true;
            stateTime = 0f;
            if (next != DisruptionState.Active && next != DisruptionState.Interacting) {
                stillFor = 0f;
            }
        }
    }
}
