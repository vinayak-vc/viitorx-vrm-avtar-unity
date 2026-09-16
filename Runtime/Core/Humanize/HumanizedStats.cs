namespace VirtualMirror.Core.Humanize {
    /// <summary>
    /// F-27 — what the humanized layer actually DID, per frame and cumulatively.
    ///
    /// WHY IT IS A FIRST-CLASS TYPE AND NOT A LOG LINE. A correction layer is dangerous in exactly one
    /// way: it can make a bad pose look plausible while hiding that anything was wrong. Every stage
    /// therefore counts its own interventions, and the single most important field is
    /// <see cref="FramesUntouched"/> — during GOOD tracking this layer must do nothing at all, because
    /// the requirement is that real movement is neither smoothed nor delayed. A session in which this
    /// layer is constantly active is not a session it is protecting; it is a session it is distorting,
    /// and the counters are what tells those two apart instead of an opinion about how the avatar looks.
    /// </summary>
    public struct HumanizedStats {
        // --- rejection (a sample refused before it reached the body) --------------------------
        public long RejectedNonFinite;     // NaN / Inf from a parse or a divide
        public long RejectedOrigin;        // zero-filled dropped joint sitting on the mid-hip
        public long RejectedOutOfBody;     // further from the hip than any human anatomy reaches
        public long RejectedLowConfidence; // below the tracker's own confidence floor

        // --- temporal ------------------------------------------------------------------------
        public long HeldJointFrames;       // joint-frames served from the last valid LOCAL direction
        public long VelocityClamped;       // joint-frames whose step exceeded the class speed ceiling
        public long JumpAccepted;          // clamps that persisted long enough to be real motion
        public long RecoveringJointFrames; // joint-frames inside a re-acquire blend

        // --- structure -----------------------------------------------------------------------
        public long BoneLengthCorrected;   // bones whose observed length was replaced by the calibrated one
        public long HingeClamped;          // elbow/knee interior angles pulled back into range
        public long ConeClamped;           // upper arm / thigh / head pulled back inside its cone
        public long KneeUnbent;            // knees caught bending the wrong way
        public long TwistClamped;          // shoulder-vs-hip differential rotation past any spine

        // --- the latency proxy ---------------------------------------------------------------
        public long FramesProcessed;
        public long FramesUntouched;       // frames in which NOTHING above fired: a pure pass-through
        // Frames in which the ONLY thing that fired was bone-length normalisation. Counted apart from
        // FramesUntouched because it is a per-frame geometric projection with no temporal state: it
        // cannot introduce lag, however often it fires, whereas a hold or a velocity clamp can.
        public long FramesOnlyBoneLength;

        /// <summary>Largest single-frame correction applied to any landmark, metres. A layer that is
        /// working reads near zero here in good tracking and spikes only on a real fault.</summary>
        public float MaxCorrectionM;

        /// <summary>Largest observed bone-length error before correction, metres. This is the raw
        /// tracker's structural noise, measured rather than assumed.</summary>
        public float MaxBoneErrorM;

        /// <summary>Fraction of frames the layer passed through completely unmodified, 0..1.
        /// The honest answer to "does this add latency": a pass-through frame cannot.</summary>
        public float UntouchedFraction {
            get {
                if (FramesProcessed <= 0L) {
                    return 0f;
                }
                return (float)FramesUntouched / (float)FramesProcessed;
            }
        }

        public void Reset() {
            RejectedNonFinite = 0L;
            RejectedOrigin = 0L;
            RejectedOutOfBody = 0L;
            RejectedLowConfidence = 0L;
            HeldJointFrames = 0L;
            VelocityClamped = 0L;
            JumpAccepted = 0L;
            RecoveringJointFrames = 0L;
            BoneLengthCorrected = 0L;
            HingeClamped = 0L;
            ConeClamped = 0L;
            KneeUnbent = 0L;
            TwistClamped = 0L;
            FramesProcessed = 0L;
            FramesUntouched = 0L;
            FramesOnlyBoneLength = 0L;
            MaxCorrectionM = 0f;
            MaxBoneErrorM = 0f;
        }

        /// <summary>One-line summary for the HUD and the periodic aggregate log.</summary>
        public override string ToString() {
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            return "frames=" + FramesProcessed
                 + " untouched=" + (100f * UntouchedFraction).ToString("F1", ci) + "%"
                 + " held=" + HeldJointFrames
                 + " vclamp=" + VelocityClamped
                 + " bone=" + BoneLengthCorrected
                 + " hinge=" + HingeClamped
                 + " cone=" + ConeClamped
                 + " knee=" + KneeUnbent
                 + " twist=" + TwistClamped
                 + " rej(nan/org/far/conf)=" + RejectedNonFinite + "/" + RejectedOrigin
                 + "/" + RejectedOutOfBody + "/" + RejectedLowConfidence
                 + " maxCorr=" + MaxCorrectionM.ToString("F3", ci) + "m";
        }
    }
}
