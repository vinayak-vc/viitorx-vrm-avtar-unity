using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-29 — one hand in world space, plus the gestures read from it. The hand equivalent of
    /// <see cref="SkeletonPose"/>, and separate for the same reason: derive once, here, so every mode
    /// agrees on what a pinch is instead of each inventing a threshold.
    ///
    /// EVERY GESTURE MEASURE IS SCALE-INVARIANT, and that is not a stylistic choice. Hand landmark
    /// depth is derived from the same monocular/stereo estimate as the body, so the hand's apparent
    /// SIZE varies with how well that estimate is doing. A pinch threshold in absolute millimetres
    /// would therefore fire on the quality of that estimate rather than on intent. Dividing by the
    /// hand's own palm length cancels the error that matters.
    ///
    /// THE PLAUSIBILITY GATE IS THE HONEST QUALITY SIGNAL. A palm outside 50-160 mm is not a hand
    /// shape whatever the model reported, and the fraction of frames passing it is the cleanest
    /// available statement of whether hand tracking is working:
    ///
    ///     single subject, 1.40 m     palm  84 mm mean     99.1% plausible
    ///     single subject, 1.96 m     palm  90 mm mean     93.7% plausible
    ///     SEVEN-person clip          palm 165 mm mean     59.9% plausible   (p95 362 mm)
    ///
    /// READ THAT THIRD ROW CORRECTLY - F-29 originally recorded it as a 2.9 m DISTANCE effect and
    /// that was wrong. The clip contains seven dancers, and the single-person crop migrates between
    /// them, so the "hand" is several different people's hands averaged together (F-32). Hand
    /// tracking does NOT collapse at 2 m; the genuine single-subject limit beyond ~2 m is untested.
    /// <see cref="Implausible"/> is that gate, and a mode should draw nothing when it trips rather
    /// than render a 36 cm hand.
    ///
    /// CALIBRATION CAVEAT, stated because the numbers below look more settled than they are: the
    /// thresholds come from the DISTRIBUTION of ordinary hand poses in two dance clips, not from a
    /// recording of someone deliberately pinching and releasing. They separate "hand closed" from
    /// "hand open" correctly on that data. A proper calibration pass — a subject performing each
    /// gesture on cue — is the right way to tighten them, and has not been done.
    /// </summary>
    public sealed class HandPose {
        /// <summary>Wrist to middle-knuckle length outside this band is not a hand. Derived from the
        /// measured distributions: it admits 99.1% of close-range observations while rejecting the
        /// impossible 36 cm palms the far clip produces.</summary>
        public const float MinPalmMetres = 0.05f;
        public const float MaxPalmMetres = 0.16f;

        // Pinch, as thumb-index tip gap divided by palm length. The measured median of an ordinary
        // (non-pinching) hand is 0.52 and the 5th percentile is 0.25, so the closed end of the range
        // sits below ~0.3. Enter at 0.35 and release at 0.48: without the gap between them a hand
        // resting near the threshold strobes the pinch on and off every frame.
        private const float PinchEnter = 0.35f;
        private const float PinchExit = 0.48f;

        // A finger counts as extended when its tip is this many times farther from the wrist than its
        // own knuckle is. Measured medians for an open hand are 1.48-1.71 across the five fingers and
        // the 5th percentiles are 1.09-1.30, so 1.45 sits between them. The little finger is the
        // weakest separated (median 1.48) and will therefore be the first to miscount.
        private const float ExtendEnter = 1.45f;
        private const float ExtendExit = 1.30f;

        /// <summary>True when a hand was tracked AND passed the plausibility gate.</summary>
        public bool Valid;

        /// <summary>True when the sidecar sent this hand but its shape was rejected. Distinct from
        /// simply absent: it means hand tracking is failing here, which is worth showing.</summary>
        public bool Implausible;

        public bool IsLeft;

        public readonly Vector3[] World = new Vector3[RawHandFrame.LandmarkCount];

        /// <summary>Wrist to middle-knuckle distance, in metres. The hand's own scale reference.</summary>
        public float PalmMetres;

        /// <summary>Palm facing direction — the normal of the wrist/index/little triangle.</summary>
        public Vector3 PalmNormal = Vector3.forward;

        /// <summary>Along the fingers, wrist toward the middle knuckle.</summary>
        public Vector3 PalmForward = Vector3.up;

        /// <summary>A full orientation built from the two axes above, for parenting an object to the
        /// palm. COMPUTED ON DEMAND rather than stored during <see cref="Fill"/>: Quaternion.LookRotation
        /// is a native engine call, and every consumer so far wants the two direction vectors instead.
        /// Deriving it only where it is actually used keeps this class pure managed maths, which is
        /// what lets the gesture thresholds be exercised headlessly against recorded wire data.</summary>
        public Quaternion PalmRotation {
            get {
                if (!Valid) {
                    return Quaternion.identity;
                }
                return Quaternion.LookRotation(PalmNormal, PalmForward);
            }
        }

        /// <summary>Thumb-tip to index-tip gap in metres. Honest, but remember it inherits the
        /// hand-scale error described on the class — compare <see cref="PinchNormalised"/> instead
        /// when deciding anything.</summary>
        public float ThumbIndexMetres;

        /// <summary>The same gap divided by palm length. This is the one to threshold on.</summary>
        public float PinchNormalised = 1f;

        /// <summary>Hysteretic pinch state.</summary>
        public bool Pinching;

        /// <summary>Which of the five fingers are extended, thumb first.</summary>
        public readonly bool[] Extended = new bool[5];

        /// <summary>How many fingers are extended, 0-5.</summary>
        public int FingerCount;

        /// <summary>
        /// Fill from the raw frame. <paramref name="origin"/> and <paramref name="scale"/> stage the
        /// hip-relative points in the scene exactly as <see cref="SkeletonPose.Fill"/> does, so a hand
        /// and the arm it belongs to land in the same place.
        /// </summary>
        public void Fill(RawHandFrame frame, bool isLeft, Vector3 origin, float scale) {
            IsLeft = isLeft;
            bool tracked = frame != null && (isLeft ? frame.LeftTracked : frame.RightTracked);
            if (!tracked) {
                Valid = false;
                Implausible = false;
                return;
            }

            int i = 0;
            while (i < RawHandFrame.LandmarkCount) {
                World[i] = origin + frame.Landmark(isLeft, i) * scale;
                i = i + 1;
            }

            // Palm length is measured in WORLD units and divided back by scale, so the plausibility
            // band stays in real metres however the scene is staged. Staging the show at half size
            // must not make every hand implausible.
            PalmMetres = Vector3.Distance(World[RawHandFrame.Wrist], World[9]) / Mathf.Max(1e-4f, scale);
            if (PalmMetres < MinPalmMetres || PalmMetres > MaxPalmMetres) {
                Valid = false;
                Implausible = true;
                Pinching = false;
                FingerCount = 0;
                return;
            }
            Implausible = false;
            Valid = true;

            UpdateOrientation();
            UpdateGestures(scale);
        }

        private void UpdateOrientation() {
            Vector3 wrist = World[RawHandFrame.Wrist];
            Vector3 indexKnuckle = World[5];
            Vector3 littleKnuckle = World[17];
            Vector3 forward = World[9] - wrist;
            Vector3 across = littleKnuckle - indexKnuckle;
            Vector3 normal = Vector3.Cross(forward, across);
            if (forward.sqrMagnitude < 1e-8f || normal.sqrMagnitude < 1e-8f) {
                // Degenerate palm triangle — keep the previous orientation rather than snapping to
                // identity, which would spin anything parented to the palm for a single frame.
                return;
            }
            PalmForward = forward.normalized;
            PalmNormal = normal.normalized;
        }

        private void UpdateGestures(float scale) {
            float palmWorld = Mathf.Max(1e-4f, PalmMetres * scale);
            ThumbIndexMetres = Vector3.Distance(World[4], World[8]) / Mathf.Max(1e-4f, scale);
            PinchNormalised = Vector3.Distance(World[4], World[8]) / palmWorld;
            // Hysteresis in both directions: enter tight, leave loose.
            if (Pinching) {
                Pinching = PinchNormalised < PinchExit;
            } else {
                Pinching = PinchNormalised < PinchEnter;
            }

            int count = 0;
            int f = 0;
            while (f < RawHandFrame.Fingertips.Length) {
                int tip = RawHandFrame.Fingertips[f];
                int knuckle = RawHandFrame.Knuckles[f];
                float knuckleReach = Vector3.Distance(World[RawHandFrame.Wrist], World[knuckle]);
                float tipReach = Vector3.Distance(World[RawHandFrame.Wrist], World[tip]);
                float ratio = tipReach / Mathf.Max(1e-4f, knuckleReach);
                if (Extended[f]) {
                    Extended[f] = ratio > ExtendExit;
                } else {
                    Extended[f] = ratio > ExtendEnter;
                }
                if (Extended[f]) {
                    count = count + 1;
                }
                f = f + 1;
            }
            FingerCount = count;
        }
    }
}
