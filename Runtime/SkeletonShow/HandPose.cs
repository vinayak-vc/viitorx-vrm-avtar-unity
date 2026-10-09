using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;

namespace VirtualMirror.SkeletonShow {
    /// <summary>Why a hand is, or is not, usable - phrased as what the person should DO about it.</summary>
    public enum HandRange {
        /// <summary>The sidecar sent no such hand. Not an error: it omits a hand below its gate.</summary>
        NotTracked = 0,
        /// <summary>Palm and every landmark are plausible.</summary>
        Ok = 1,
        /// <summary>The palm is smaller than a hand can be: the hand is too far for its fingers to be
        /// resolved. The one case where "move closer" is the right advice.</summary>
        TooFar = 2,
        /// <summary>The palm is larger than a hand can be, or a landmark has left the hand. Either the
        /// hand is too close or its depth is unreliable - and in both, moving closer makes it worse.</summary>
        TooCloseOrUnreliable = 3,
    }

    /// <summary>
    /// F-48 — what to TELL a person about their hand. A static class on pure data, so the wording that
    /// SR7 got wrong (every refusal read "OUT OF RANGE - step closer") can be asserted rather than
    /// eyeballed, and so every experience says the same thing.
    /// </summary>
    public static class HandRangeAdvice {
        /// <summary>
        /// Advice for one hand, or null when it is usable (or simply not sent) and there is nothing to say.
        /// <paramref name="body"/> is the whole-body operating-volume verdict and OUTRANKS the hand: a
        /// subject who is too close has hands the camera cannot read, whatever each hand reports.
        /// NEVER returns "step closer" for a hand that is too near or whose depth has blown up - that is
        /// the one instruction that makes both of those worse.
        /// </summary>
        public static string Describe(HandPose hand, VolumeVerdict body) {
            if (body == VolumeVerdict.TooNear) {
                return "TOO CLOSE - step back so the camera can read your hands";
            }
            if (hand == null || !hand.Implausible) {
                return null;
            }
            if (hand.Range == HandRange.TooFar) {
                return "TOO FAR - move your hand closer to the camera";
            }
            return "TOO CLOSE or depth unreliable - hold your hand further from the camera";
        }
    }

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

        // ---- PER-LANDMARK VALIDATION (F-48) ---------------------------------------------------------
        // The palm gate checks ONE segment, wrist to middle knuckle. SR7 showed that is not enough: at
        // ~48 s a left hand reported a 147 mm palm - inside the band - while its thumb-index gap was
        // 2273 mm (15.45 palms). Depth is per keypoint, so one fingertip can be metres from a hand
        // whose palm is perfect, and the scale-invariant pinch maths only divides the error by the
        // (good) palm; it does not make it small. Every landmark is therefore also bounded against the
        // hand's OWN wrist, in palms, so the test follows the hand's size instead of a fixed number.

        /// <summary>No landmark of a real hand is further than this many palm lengths from the wrist.
        /// A fully open hand's wrist-to-fingertip is ~1.9-2.2 palms and the thumb tip is nearer, so
        /// 2.8 leaves ~30 % for tracker noise while rejecting a tip that has left the hand.</summary>
        public const float MaxReachPalms = 2.8f;

        /// <summary>The absolute backstop to <see cref="MaxReachPalms"/>: no adult hand reaches 30 cm
        /// from its own wrist. It catches a blown-up palm that scales its own limit up with it.</summary>
        public const float MaxReachMetres = 0.30f;

        /// <summary>Thumb tip to index tip, in palms. An abducted thumb against an extended index is
        /// ~1.5-2 palms; past 3.0 the two tips are not on the same hand.</summary>
        public const float MaxThumbIndexPalms = 3.0f;

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

        /// <summary>
        /// WHY the hand is or is not usable, in terms a person can act on. Distinct from
        /// <see cref="Implausible"/>, which only says "sent but refused": the two refusals need
        /// OPPOSITE advice, and SR7's Air Graffiti told a too-close user to step closer.
        /// </summary>
        public HandRange Range = HandRange.NotTracked;

        /// <summary>Hand frames (one per datagram, not per render frame) refused because the palm was
        /// outside <see cref="MinPalmMetres"/>-<see cref="MaxPalmMetres"/>.</summary>
        public long RejectedPalmFrames;

        /// <summary>Hand frames refused although the palm passed: a landmark was too far from the
        /// wrist, or the two pinch tips too far apart. The SR7 failure.</summary>
        public long RejectedLandmarkFrames;

        /// <summary>Largest wrist-to-landmark distance on the most recent frame, in palms. Kept so a
        /// recording can show how far past <see cref="MaxReachPalms"/> a refused hand was.</summary>
        public float WorstReachPalms;

        // Timestamp of the last datagram counted, so a 190 fps render loop does not report a refusal
        // rate that belongs to the display. NaN until the first frame.
        private double lastCountedTimestamp = double.NaN;

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
                Range = HandRange.NotTracked;
                return;
            }
            bool newDatagram = double.IsNaN(lastCountedTimestamp)
                               || frame.TimestampSeconds != lastCountedTimestamp;
            lastCountedTimestamp = frame.TimestampSeconds;

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
                // A small palm is a far hand. A large one is a near hand OR a depth blow-up, and the
                // palm alone cannot say which - so it is not claimed to be "too close" without the
                // caveat, and the advice never tells that person to step closer.
                Range = PalmMetres < MinPalmMetres ? HandRange.TooFar : HandRange.TooCloseOrUnreliable;
                if (newDatagram) {
                    RejectedPalmFrames = RejectedPalmFrames + 1L;
                }
                return;
            }
            if (!LandmarksPlausible(scale)) {
                // The palm is a hand; something ELSE on it is not. Refuse the frame as a whole - a
                // pinch read from a tip that is metres away is worse than no pinch - and do it per
                // hand, so the other hand, judged on its own landmarks, stays usable.
                Valid = false;
                Implausible = true;
                Pinching = false;
                FingerCount = 0;
                Range = HandRange.TooCloseOrUnreliable;
                if (newDatagram) {
                    RejectedLandmarkFrames = RejectedLandmarkFrames + 1L;
                }
                return;
            }
            Implausible = false;
            Valid = true;
            Range = HandRange.Ok;

            UpdateOrientation();
            UpdateGestures(scale);
        }

        // Every landmark against the hand's own wrist, in palms and in absolute metres, plus the pinch
        // pair against each other. Distances are taken in WORLD units and divided back by scale, like
        // the palm, so staging the show at half size changes nothing.
        private bool LandmarksPlausible(float scale) {
            float safeScale = Mathf.Max(1e-4f, scale);
            float palm = Mathf.Max(1e-4f, PalmMetres);
            Vector3 wrist = World[RawHandFrame.Wrist];
            float worst = 0f;
            bool ok = true;
            int i = 1;
            while (i < RawHandFrame.LandmarkCount) {
                Vector3 p = World[i];
                float reachMetres = Vector3.Distance(wrist, p) / safeScale;
                if (float.IsNaN(reachMetres) || float.IsInfinity(reachMetres)) {
                    reachMetres = float.MaxValue;
                }
                float reachPalms = reachMetres / palm;
                if (reachPalms > worst) {
                    worst = reachPalms;
                }
                if (reachPalms > MaxReachPalms || reachMetres > MaxReachMetres) {
                    ok = false;
                }
                i = i + 1;
            }
            WorstReachPalms = worst;
            float pinchPalms = Vector3.Distance(World[4], World[8]) / safeScale / palm;
            if (float.IsNaN(pinchPalms) || pinchPalms > MaxThumbIndexPalms) {
                ok = false;
            }
            return ok;
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
