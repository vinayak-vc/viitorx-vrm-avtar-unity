using System.Globalization;

namespace VirtualMirror.Core.Humanize {
    /// <summary>
    /// F-48 — decides, pose by pose, whether the subject is inside <see cref="OperatingVolume"/>, and
    /// keeps the account of why not.
    ///
    /// ONE INSTANCE PER CONSUMER (the avatar app, the skeleton show, a tracked stage). It holds state -
    /// how long the subject has been outside, how many consecutive poses have been inside - so it cannot
    /// be a static, and it deliberately knows nothing about avatars, rendering or the humanize layer.
    /// The consumer asks it two questions: <see cref="Admission"/> (what may this pose be used for) and
    /// <see cref="Expired"/> (has the subject been outside for so long that holding the last body would
    /// now be a lie).
    ///
    /// WHAT IT DOES NOT DO, deliberately. It never edits the pose and never clamps a position. It does
    /// not look at the transport either: a datagram that arrives on time and describes a subject who is
    /// too close is a healthy stream carrying out-of-range data, and the counters here exist so the next
    /// recording can tell that apart from starvation (SR6) without guessing.
    ///
    /// COUNTERS ARE PER POSE, NOT PER CALL. The render loop runs at ~100-190 fps over a ~30 Hz stream,
    /// so counting calls would report a rejection rate that belongs to the display. A pose is recognised
    /// by its timestamp; the hold clock, by contrast, runs on render time, because "how long has the
    /// body on screen been held" is a question about the screen.
    /// </summary>
    public sealed class OperatingVolumeGate {
        private VolumeVerdict verdict = VolumeVerdict.NoPose;
        private PoseAdmission admission = PoseAdmission.Outside;
        private double lastTimestamp = double.NaN;
        private float rangeMetres = -1f;
        private float outsideSeconds;
        private int settledPoses;
        private bool expired;
        private bool rootEstimated;

        /// <summary>What the most recent pose was judged to be.</summary>
        public VolumeVerdict Verdict {
            get {
                return verdict;
            }
        }

        /// <summary>What the most recent pose may be used for.</summary>
        public PoseAdmission Admission {
            get {
                return admission;
            }
        }

        /// <summary>True once the subject has been outside the volume for longer than
        /// <see cref="OperatingVolume.HoldSeconds"/>. From here a consumer shows the state instead of
        /// a body.</summary>
        public bool Expired {
            get {
                return expired;
            }
        }

        /// <summary>F-60: the latest pose's root was ESTIMATED (the hips were not in the picture), so it was judged
        /// against the relaxed near limit.</summary>
        public bool RootEstimated {
            get {
                return rootEstimated;
            }
        }

        /// <summary>Camera-to-hip range of the latest pose, metres; negative when it had no root.</summary>
        public float RangeMetres {
            get {
                return rangeMetres;
            }
        }

        /// <summary>Seconds the subject has been continuously outside the volume.</summary>
        public float OutsideSeconds {
            get {
                return outsideSeconds;
            }
        }

        /// <summary>True when this pose may be drawn and may move the follow position.</summary>
        public bool AllowsMotion {
            get {
                return admission != PoseAdmission.Outside;
            }
        }

        // ---- cumulative record, one count per POSE -------------------------------------------------
        public long PosesTooNear { get; private set; }
        public long PosesTooFar { get; private set; }
        public long PosesRootInvalid { get; private set; }
        public long PosesNoRoot { get; private set; }

        /// <summary>Poses that arrived while the last good body was being held for them.</summary>
        public long PosesHeld { get; private set; }

        /// <summary>Times the hold ran out and the consumer was told to stop showing a body.</summary>
        public long HoldExpirations { get; private set; }

        /// <summary>Forget everything: a new session, or the subject left and the room is empty.</summary>
        public void Reset() {
            verdict = VolumeVerdict.NoPose;
            admission = PoseAdmission.Outside;
            lastTimestamp = double.NaN;
            rangeMetres = -1f;
            outsideSeconds = 0f;
            settledPoses = 0;
            expired = false;
            rootEstimated = false;
        }

        public void ResetCounters() {
            PosesTooNear = 0L;
            PosesTooFar = 0L;
            PosesRootInvalid = 0L;
            PosesNoRoot = 0L;
            PosesHeld = 0L;
            HoldExpirations = 0L;
        }

        /// <summary>
        /// Judge the latest pose. Call once per render frame, with the same frame the consumer is about
        /// to use. <paramref name="frame"/> may be null or invalid when nobody is tracked; that clears the
        /// clocks, so a subject who leaves and returns is treated as arriving.
        /// </summary>
        public void Update(PoseFrame frame, float deltaSeconds) {
            if (frame == null || !frame.IsValid) {
                verdict = VolumeVerdict.NoPose;
                admission = PoseAdmission.Outside;
                outsideSeconds = 0f;
                settledPoses = 0;
                expired = false;
                lastTimestamp = double.NaN;
                rangeMetres = -1f;
                rootEstimated = false;
                return;
            }

            rootEstimated = frame.RootEstimated;
            bool newPose = double.IsNaN(lastTimestamp) || frame.TimestampSeconds != lastTimestamp;
            lastTimestamp = frame.TimestampSeconds;

            if (!OperatingVolume.Enabled) {
                // Switched off: pre-F-48 behaviour. Nothing is counted, because nothing was judged.
                verdict = frame.HasRootPosition ? VolumeVerdict.InVolume : VolumeVerdict.NoRoot;
                admission = PoseAdmission.Trusted;
                rangeMetres = OperatingVolume.RangeOf(frame);
                outsideSeconds = 0f;
                settledPoses = 0;
                expired = false;
                return;
            }

            rangeMetres = OperatingVolume.RangeOf(frame);
            verdict = OperatingVolume.Classify(rangeMetres, verdict, rootEstimated);

            if (verdict == VolumeVerdict.InVolume) {
                outsideSeconds = 0f;
                expired = false;
                if (newPose) {
                    settledPoses = settledPoses + 1;
                }
                admission = settledPoses >= OperatingVolume.SettlePoses
                    ? PoseAdmission.Trusted : PoseAdmission.Settling;
                return;
            }

            if (verdict == VolumeVerdict.NoRoot) {
                // Cannot be judged, so it is not refused: an RGB-only source has no root and must keep
                // working. Counted, so a sender that SHOULD have one and stopped sending it is visible.
                outsideSeconds = 0f;
                expired = false;
                admission = PoseAdmission.Trusted;
                if (newPose) {
                    PosesNoRoot = PosesNoRoot + 1L;
                }
                return;
            }

            // TooNear, TooFar or RootInvalid: the pose is not usable.
            admission = PoseAdmission.Outside;
            settledPoses = 0;
            outsideSeconds = outsideSeconds + (deltaSeconds > 0f ? deltaSeconds : 0f);
            if (newPose) {
                if (verdict == VolumeVerdict.TooNear) {
                    PosesTooNear = PosesTooNear + 1L;
                } else if (verdict == VolumeVerdict.TooFar) {
                    PosesTooFar = PosesTooFar + 1L;
                } else {
                    PosesRootInvalid = PosesRootInvalid + 1L;
                }
                PosesHeld = PosesHeld + 1L;
            }
            bool wasExpired = expired;
            expired = outsideSeconds > OperatingVolume.HoldSeconds;
            if (expired && !wasExpired) {
                HoldExpirations = HoldExpirations + 1L;
            }
        }

        /// <summary>
        /// What to tell the person, or null when there is nothing to say. Written as an instruction
        /// where one exists - "step back" - and as a statement where it does not, because the honest
        /// answer to a bad depth reading is not always something the subject can fix.
        /// </summary>
        public string Message() {
            string shortText = ShortMessage();
            if (shortText == null) {
                return null;
            }
            if (verdict == VolumeVerdict.RootInvalid) {
                return shortText + " - the sensor sent no usable hip distance";
            }
            CultureInfo ci = CultureInfo.InvariantCulture;
            return shortText + "   (" + (rootEstimated ? "estimated hip " : "hip ") + rangeMetres.ToString("F2", ci)
                   + " m, working range " + OperatingVolume.NearLimitFor(rootEstimated).ToString("F2", ci) + "-"
                   + OperatingVolume.FarLimitMetres.ToString("F2", ci) + " m)";
        }

        /// <summary>The same instruction without the figures, for a line with little room.</summary>
        public string ShortMessage() {
            if (verdict == VolumeVerdict.TooNear) {
                return "TOO CLOSE - step back";
            }
            if (verdict == VolumeVerdict.TooFar) {
                return "TOO FAR - step closer";
            }
            if (verdict == VolumeVerdict.RootInvalid) {
                return "ROOT DEPTH INVALID";
            }
            return null;
        }

        /// <summary>One line for a HUD: the live range, the band, and every counter that explains a
        /// rejection. Kept short enough to survive a 1100 px capture.</summary>
        public string Summary() {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string range = rangeMetres >= 0f ? rangeMetres.ToString("F2", ci) + "m" : "--";
            string state = verdict == VolumeVerdict.InVolume ? admission.ToString().ToUpperInvariant()
                                                             : verdict.ToString().ToUpperInvariant();
            if (!OperatingVolume.Enabled) {
                state = "OFF";
            }
            return "VOLUME " + state + (rootEstimated ? "  est.hip " : "  hip ") + range
                 + " [" + OperatingVolume.NearLimitFor(rootEstimated).ToString("F2", ci)
                 + "-" + OperatingVolume.FarLimitMetres.ToString("F2", ci) + "]"
                 + "  near " + PosesTooNear + "  far " + PosesTooFar
                 + "  invalid " + PosesRootInvalid + "  held " + PosesHeld
                 + "  expired " + HoldExpirations;
        }
    }
}
