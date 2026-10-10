using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// States A-F in one place. Input: is a person tracked right now, and how much they move.
    /// Output: the phase, the silhouette alpha, the fluid activity, and one-shot flags.
    /// Pure logic, no Unity objects, so it can be tested without a scene.
    ///
    /// The caller decides what "tracked" means (fresh data AND a full torso). This class only
    /// debounces it: enter after a confirmation delay, leave after a timeout.
    /// </summary>
    public sealed class ShowcaseStateMachine {
        private float trackedFor;
        private float lostFor;
        private float stillFor;
        private float rawAlpha;
        private bool awaitingClear;

        public ShowcasePhase Phase { get; private set; }

        /// <summary>0..1, eased. Drives the mask alpha: at 0 the rain fills the whole screen.</summary>
        public float SilhouetteAlpha { get; private set; }

        /// <summary>0..1. The fluid reacts only while this is above zero.</summary>
        public float FluidActivity { get; private set; }

        /// <summary>True for exactly one frame when a new silhouette starts to fade in; the caller
        /// snaps its smoothing to the current pose so no old position is shown.</summary>
        public bool SnapRequested { get; private set; }

        /// <summary>True for exactly one frame when the phase changed.</summary>
        public bool Changed { get; private set; }

        public bool PersonVisible {
            get {
                return Phase != ShowcasePhase.NoPerson;
            }
        }

        public void Reset() {
            trackedFor = 0f;
            lostFor = 0f;
            stillFor = 0f;
            rawAlpha = 0f;
            awaitingClear = false;
            Phase = ShowcasePhase.NoPerson;
            SilhouetteAlpha = 0f;
            FluidActivity = 0f;
            SnapRequested = false;
            Changed = false;
        }

        public void Tick(bool trackedNow, float energy, float dt, ChromaticRainSettings s) {
            SnapRequested = false;
            Changed = false;
            if (dt <= 0f) {
                return;
            }

            if (trackedNow) {
                trackedFor += dt;
                lostFor = 0f;
            } else {
                lostFor += dt;
                trackedFor = 0f;
            }

            switch (Phase) {
                case ShowcasePhase.NoPerson:
                    rawAlpha = 0f;
                    if (trackedFor >= s.enterSeconds) {
                        Enter(ShowcasePhase.Detected);
                        SnapRequested = true;
                    }
                    break;

                case ShowcasePhase.Detected:
                case ShowcasePhase.Resumed:
                    if (Phase == ShowcasePhase.Resumed && awaitingClear) {
                        // Clear the old silhouette first, then snap the smoothing and fade in.
                        rawAlpha = Mathf.Max(0f, rawAlpha - dt / Mathf.Max(0.01f, s.resumeClearSeconds));
                        if (rawAlpha <= 0f) {
                            awaitingClear = false;
                            SnapRequested = true;
                        }
                    } else {
                        rawAlpha = Mathf.Min(1f, rawAlpha + dt / Mathf.Max(0.05f, s.fadeInSeconds));
                        if (rawAlpha >= 1f) {
                            Enter(energy > s.movingEnergy ? ShowcasePhase.Moving : ShowcasePhase.Stationary);
                        }
                    }
                    if (lostFor >= s.lostTimeoutSeconds) {
                        Enter(ShowcasePhase.Lost);
                    }
                    break;

                case ShowcasePhase.Moving:
                    if (energy < s.stillEnergy) {
                        stillFor += dt;
                        if (stillFor >= s.stillHoldSeconds) {
                            Enter(ShowcasePhase.Stationary);
                        }
                    } else {
                        stillFor = 0f;
                    }
                    if (lostFor >= s.lostTimeoutSeconds) {
                        Enter(ShowcasePhase.Lost);
                    }
                    break;

                case ShowcasePhase.Stationary:
                    if (energy > s.movingEnergy) {
                        Enter(ShowcasePhase.Moving);
                    }
                    if (lostFor >= s.lostTimeoutSeconds) {
                        Enter(ShowcasePhase.Lost);
                    }
                    break;

                case ShowcasePhase.Lost:
                    rawAlpha = Mathf.Max(0f, rawAlpha - dt / Mathf.Max(0.05f, s.fadeOutSeconds));
                    if (trackedFor >= s.enterSeconds) {
                        Enter(ShowcasePhase.Resumed);
                        awaitingClear = rawAlpha > 0f;
                        if (!awaitingClear) {
                            SnapRequested = true;
                        }
                    } else if (rawAlpha <= 0f) {
                        Enter(ShowcasePhase.NoPerson);
                    }
                    break;
            }

            SilhouetteAlpha = Mathf.SmoothStep(0f, 1f, rawAlpha);
            bool reacting = Phase == ShowcasePhase.Moving || Phase == ShowcasePhase.Stationary
                            || Phase == ShowcasePhase.Detected || Phase == ShowcasePhase.Resumed;
            float target = reacting && trackedNow ? 1f : 0f;
            FluidActivity = Mathf.MoveTowards(FluidActivity, target, dt / 0.4f);
        }

        private void Enter(ShowcasePhase next) {
            if (next == Phase) {
                return;
            }
            Phase = next;
            Changed = true;
            stillFor = 0f;
        }
    }
}
