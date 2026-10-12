namespace ChromaticRain {
    public enum SilhouetteMode {
        /// <summary>A near-black body is drawn over the effect. Easiest outline to read in public.</summary>
        DarkSilhouette,
        /// <summary>Rain and fluid are removed inside the body; nothing is drawn over it.</summary>
        NegativeSpace
    }

    /// <summary>Ways to inspect the silhouette without the rain in the way. Cycled with V.</summary>
    public enum SilhouetteDebugView {
        /// <summary>The normal composition.</summary>
        Off,
        /// <summary>The filled mask, white on black.</summary>
        MaskOnly,
        /// <summary>The filled mask in dim grey with the landmarks and bones drawn over it.</summary>
        Landmarks,
        /// <summary>The contour of the mask only.</summary>
        ContourOnly
    }

    /// <summary>The A-F interaction states. One enum, one place that changes it.</summary>
    public enum ShowcasePhase {
        /// <summary>A - nobody; full rain.</summary>
        NoPerson,
        /// <summary>B - a person was just confirmed; the silhouette fades in.</summary>
        Detected,
        /// <summary>C - a person is moving.</summary>
        Moving,
        /// <summary>D - a person is standing still.</summary>
        Stationary,
        /// <summary>E - tracking was lost; the silhouette fades out.</summary>
        Lost,
        /// <summary>F - tracking returned; the old silhouette is cleared, then the new one blends in.</summary>
        Resumed
    }
}
