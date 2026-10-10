using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// Every tunable of the Chromatic Rain showcase in one asset, so nothing is a constant scattered
    /// across scripts. Distances are in metres of body space unless a tooltip says "screen heights".
    /// </summary>
    [CreateAssetMenu(fileName = "ChromaticRainSettings", menuName = "Showcase/Chromatic Rain Settings")]
    public sealed class ChromaticRainSettings : ScriptableObject {
        [Header("Rain")]
        [Tooltip("Fraction of rain columns that carry streaks.")]
        [Range(0f, 1f)] public float rainDensity = 0.7f;
        [Tooltip("Fall speed of the middle layer, in screen heights per second.")]
        [Range(0.1f, 3f)] public float fallSpeed = 0.85f;
        [Tooltip("Length of the longest streaks, in screen heights.")]
        [Range(0.02f, 1f)] public float streakLength = 0.30f;
        [Tooltip("Streak width as a fraction of its column.")]
        [Range(0.05f, 1f)] public float streakWidth = 0.30f;
        [Tooltip("Overall rain brightness.")]
        [Range(0f, 3f)] public float colorIntensity = 0.9f;
        [Tooltip("Horizontal wind: slant and slow sway of the streaks.")]
        [Range(0f, 0.3f)] public float windStrength = 0.06f;
        [Tooltip("Chance that a streak is amber instead of one of the cool colours.")]
        [Range(0f, 0.2f)] public float amberChance = 0.05f;
        [Tooltip("Cyan, electric blue, violet, magenta, then amber (index 4). Five entries.")]
        [ColorUsage(false, true)]
        public Color[] palette = new Color[] {
            new Color(0.00f, 0.90f, 1.00f, 1f),
            new Color(0.10f, 0.35f, 1.00f, 1f),
            new Color(0.55f, 0.22f, 1.00f, 1f),
            new Color(1.00f, 0.10f, 0.72f, 1f),
            new Color(1.00f, 0.62f, 0.12f, 1f)
        };
        [Tooltip("Colour the camera clears to. Near-black.")]
        public Color backgroundColor = new Color(0.004f, 0.005f, 0.010f, 1f);

        [Header("Silhouette")]
        public SilhouetteMode silhouetteMode = SilhouetteMode.DarkSilhouette;
        [Tooltip("Half-width of the feathered edge, in metres.")]
        [Range(0.002f, 0.06f)] public float edgeSoftness = 0.012f;
        [Tooltip("How far, in metres, neighbouring shapes melt into each other.")]
        [Range(0f, 0.15f)] public float shapeBlend = 0.05f;
        [Tooltip("Mask height in pixels. Width follows the screen aspect.")]
        [Range(135, 540)] public int maskHeight = 270;
        [Tooltip("Colour of the body in Dark Silhouette mode.")]
        public Color silhouetteColor = new Color(0.006f, 0.008f, 0.014f, 1f);
        [Tooltip("Colour of the faint inner rim that keeps the outline readable.")]
        [ColorUsage(false, true)]
        public Color rimColor = new Color(0.22f, 0.50f, 1.00f, 1f);
        [Range(0f, 2f)] public float rimIntensity = 0.2f;
        [Tooltip("Scale the body's widths by the measured shoulder width.")]
        public bool adaptToShoulderWidth = true;

        [Header("Body proportions (metres at a 0.40 m shoulder width)")]
        public float referenceShoulderWidth = 0.40f;
        public float headRadiusX = 0.090f;
        public float headRadiusY = 0.115f;
        public float neckRadius = 0.050f;
        public float shoulderBarRadius = 0.060f;
        public float torsoTopRadius = 0.160f;
        public float torsoBottomRadius = 0.145f;
        public float hipBarRadius = 0.085f;
        public float upperArmShoulderRadius = 0.058f;
        public float upperArmElbowRadius = 0.046f;
        public float foreArmElbowRadius = 0.045f;
        public float foreArmWristRadius = 0.036f;
        public float handPalmRadius = 0.040f;
        public float handEndRadius = 0.046f;
        [Tooltip("Hand length drawn from the wrist when hand tracking is absent.")]
        public float fallbackHandLength = 0.09f;
        public float thighHipRadius = 0.095f;
        public float thighKneeRadius = 0.068f;
        public float shinKneeRadius = 0.066f;
        public float shinAnkleRadius = 0.048f;
        public float footAnkleRadius = 0.045f;
        public float footToeRadius = 0.036f;
        [Tooltip("Longest believable upper arm / forearm, in metres.")]
        public float maxArmSegment = 0.46f;
        [Tooltip("Longest believable thigh / shin, in metres.")]
        public float maxLegSegment = 0.62f;

        [Header("Tracking and timing")]
        [Tooltip("Continuous good tracking needed before the silhouette appears.")]
        public float enterSeconds = 0.25f;
        [Tooltip("Continuous loss before the silhouette starts to fade.")]
        public float lostTimeoutSeconds = 0.6f;
        public float fadeInSeconds = 0.45f;
        public float fadeOutSeconds = 0.9f;
        [Tooltip("How quickly an old silhouette is cleared when tracking resumes.")]
        public float resumeClearSeconds = 0.12f;
        [Tooltip("A joint that drops out keeps its last place for this long...")]
        public float jointGraceSeconds = 0.4f;
        [Tooltip("...then its segment shrinks away over this long.")]
        public float jointFadeSeconds = 0.35f;
        [Tooltip("Joint confidence below this counts as not seen; trust ramps up over the next 0.2. "
                 + "The sidecar's joints of a live person typically report 0.3 to 0.9.")]
        [Range(0f, 0.9f)] public float minJointConfidence = 0.10f;
        [Tooltip("A pose older than this is stale and is not a person.")]
        public float maxPoseAgeSeconds = 0.5f;
        [Tooltip("Auto mode: seconds without live data before the simulation takes over.")]
        public float autoFallbackSeconds = 3f;
        [Tooltip("Smoothing time constant when a joint is nearly still (seconds).")]
        public float smoothingSlowTau = 0.09f;
        [Tooltip("Smoothing time constant when a joint moves fast (seconds).")]
        public float smoothingFastTau = 0.025f;
        [Tooltip("Joint speed (metres per second) at which the fast constant applies.")]
        public float smoothingFastSpeed = 1.5f;
        [Tooltip("Whole-body energy above which the person counts as moving.")]
        public float movingEnergy = 0.06f;
        [Tooltip("Energy below which a moving person counts as still (hysteresis).")]
        public float stillEnergy = 0.035f;
        [Tooltip("Seconds of stillness before state D.")]
        public float stillHoldSeconds = 0.7f;

        [Header("Mapping")]
        [Tooltip("Metres of world height shown from the bottom to the top of the screen.")]
        public float viewHeightMetres = 2.8f;
        [Tooltip("World height shown at the centre of the screen. The hips sit near 0.95 m.")]
        public float viewCentreY = 0.95f;
        [Tooltip("Sideways steps are exaggerated by this so a person can cross the whole screen.")]
        public float lateralGain = 1.6f;
        [Tooltip("Mirror left and right like a real mirror (the existing tracking default).")]
        public bool mirrorX = true;

        [Header("Fluid layer (FluXY)")]
        public bool fluidEnabled = true;
        [Tooltip("Brightness of the fluid layer.")]
        [Range(0f, 1f)] public float fluidOpacity = 0.32f;
        [Tooltip("Dye gain before the soft clip.")]
        [Range(0f, 6f)] public float fluidGain = 2.0f;
        [Tooltip("Simulation resolution. FluXY rounds DOWN to a power of two: 128 low, 256 medium, 512 high.")]
        [Range(64, 512)] public int fluidResolution = 256;
        [Tooltip("Diameter of a hand splat, in metres.")]
        public float handSplatSize = 0.16f;
        [Tooltip("Diameter of the body splat, in metres.")]
        public float bodySplatSize = 0.35f;
        public bool bodyTargetEnabled = true;
        [Tooltip("Dye buffer detail: 2 means the dye is twice the velocity resolution.")]
        [Range(1, 4)] public int fluidSupersampling = 2;
        [Tooltip("How far moving dye is stretched along the flow.")]
        [Range(0f, 0.2f)] public float fluidStreakLength = 0.05f;
        [Tooltip("Fine striations across moving dye.")]
        [Range(0f, 1f)] public float fluidStriation = 0.4f;
        [Tooltip("How strongly movement stirs the fluid.")]
        public float velocityGain = 2.5f;
        [Range(0f, 1f)] public float velocityWeight = 0.6f;
        [Tooltip("Splats per metre travelled. Nothing is injected while a hand is still.")]
        public float splatsPerMetre = 40f;
        [Tooltip("Speed (m/s) below which a hand injects nothing.")]
        public float minInjectSpeed = 0.1f;
        [Tooltip("Speed (m/s) at which a hand injects at full strength.")]
        public float fullInjectSpeed = 1.2f;
        [Tooltip("Faster than this a jump is treated as a glitch, not a gesture.")]
        public float maxTargetSpeed = 6f;
        public float fluidDissipation = 0.22f;
        public float fluidTurbulence = 14f;
        [Range(0f, 1f)] public float fluidViscosity = 0.12f;
        [Tooltip("A gentle downward drift so trails follow the rain.")]
        public float fluidDrift = 0.04f;
        [Tooltip("Brightness of the velocity-direction tint inside moving fluid.")]
        [Range(0f, 2f)] public float flowTint = 0.5f;

        /// <summary>Palette entry, wrapping, safe against a short array.</summary>
        public Color PaletteColor(int index) {
            if (palette == null || palette.Length == 0) {
                return Color.white;
            }
            int wrapped = ((index % palette.Length) + palette.Length) % palette.Length;
            return palette[wrapped];
        }
    }
}
