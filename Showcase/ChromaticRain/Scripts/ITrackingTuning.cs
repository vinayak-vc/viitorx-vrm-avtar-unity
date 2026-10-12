namespace ChromaticRain {
    /// <summary>
    /// The tracking-side tunables that the shared pose path (<see cref="ShowcasePoseAdapter"/>,
    /// <see cref="ScreenSpaceMapper"/>, <see cref="ShowcaseTrackingSource"/>) reads. Each showcase keeps
    /// its own settings asset and implements this, so the shared code never depends on one showcase.
    /// </summary>
    public interface ITrackingTuning {
        float MinJointConfidence { get; }
        float JointGraceSeconds { get; }
        float JointFadeSeconds { get; }
        float SmoothingSlowTau { get; }
        float SmoothingFastTau { get; }
        float SmoothingFastSpeed { get; }
        float MaxPoseAgeSeconds { get; }
        float AutoFallbackSeconds { get; }
        float ViewHeightMetres { get; }
        float ViewCentreY { get; }
        float LateralGain { get; }
    }
}
