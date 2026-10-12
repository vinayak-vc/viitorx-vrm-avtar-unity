using System;

using ChromaticRain;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>One solver quality level.</summary>
    [Serializable]
    public sealed class QualityProfile {
        [Tooltip("FluXY rounds this DOWN to a power of two: 128, 256, 512.")]
        public int resolution = 256;
        [Tooltip("Dye buffer detail: 2 means the dye is twice the velocity resolution.")]
        [Range(1, 4)] public int supersampling = 1;
        [Tooltip("Most simulation steps FluXY may take in one frame.")]
        [Range(1, 8)] public int maxSteps = 2;

        public QualityProfile() {
        }

        public QualityProfile(int resolution, int supersampling, int maxSteps) {
            this.resolution = resolution;
            this.supersampling = supersampling;
            this.maxSteps = maxSteps;
        }
    }

    /// <summary>
    /// Every tunable of the Fluid Disruption showcase in one asset, so nothing is a constant scattered
    /// across scripts. The baseline (calm) group and the interaction group are independent on purpose:
    /// changing one never changes the other. Distances are view metres: the screen is
    /// <see cref="viewHeightMetres"/> tall and the person is drawn life-size.
    /// </summary>
    [CreateAssetMenu(fileName = "FluidDisruptionSettings", menuName = "Showcase/Fluid Disruption Settings")]
    public sealed class FluidDisruptionSettings : ScriptableObject, ITrackingTuning {
        [Header("Palette (gamma colours, converted to linear on use)")]
        [Tooltip("0 midnight, 1 cyan, 2 turquoise, 3 electric blue, 4 violet, 5 lavender, 6 magenta accent.")]
        public Color[] palette = new Color[] {
            new Color(0.05f, 0.09f, 0.42f, 1f),
            new Color(0.00f, 0.82f, 1.00f, 1f),
            new Color(0.05f, 0.86f, 0.88f, 1f),
            new Color(0.10f, 0.36f, 1.00f, 1f),
            new Color(0.48f, 0.26f, 1.00f, 1f),
            new Color(0.74f, 0.64f, 1.00f, 1f),
            new Color(1.00f, 0.20f, 0.78f, 1f)
        };
        [Tooltip("Colour the screen shows where there is no fluid at all. Deep midnight, never pure black.")]
        public Color backgroundLow = new Color(0.040f, 0.080f, 0.220f, 1f);
        public Color backgroundHigh = new Color(0.070f, 0.160f, 0.400f, 1f);

        [Header("Baseline: the calm fluid (independent of any person)")]
        [Tooltip("The baseline field is a pattern of wide, wavy colour bands. Every pass pulls the dye a little "
                 + "toward it, so the fluid is always recognisable and settles back to it after a disturbance.")]
        [Range(0f, 1f)] public float fieldWeight = 0.2f;
        [Tooltip("Passes per second. With the weight this sets how fast the baseline is restored.")]
        public float fieldRate = 10f;
        [Tooltip("The pattern drifts slowly, as a fraction of the fluid area (x, y).")]
        public Vector2 fieldDrift = new Vector2(0.05f, 0.02f);
        [Tooltip("Seconds per drift cycle (x, y).")]
        public Vector2 fieldDriftPeriod = new Vector2(70f, 110f);
        [Tooltip("The pattern's colours breathe by this much, 0..1: gradual colour transitions.")]
        [Range(0f, 0.5f)] public float fieldTintSwing = 0.18f;
        [Tooltip("Dye thickness of the pattern: how bright the calm surface is.")]
        [Range(0.02f, 0.4f)] public float fieldThickness = 0.16f;
        [Tooltip("Flow sources: wide bands that push slowly, in alternating directions.")]
        public BaselineSourceConfig[] baselineSources = new BaselineSourceConfig[] {
            new BaselineSourceConfig(new Vector2(0.00f, 0.34f), new Vector2(0.05f, 0.07f), 74f, 0.00f, 0f, 0.06f, new Vector2(6.0f, 0.95f), 3f, 2f),
            new BaselineSourceConfig(new Vector2(0.00f, 0.17f), new Vector2(0.05f, 0.07f), 86f, 0.30f, 180f, 0.06f, new Vector2(6.0f, 0.95f), -4f, 2f),
            new BaselineSourceConfig(new Vector2(0.00f, 0.00f), new Vector2(0.05f, 0.07f), 66f, 0.55f, 0f, 0.05f, new Vector2(6.0f, 0.95f), 2f, 2f),
            new BaselineSourceConfig(new Vector2(0.00f, -0.17f), new Vector2(0.05f, 0.07f), 92f, 0.15f, 180f, 0.06f, new Vector2(6.0f, 0.95f), -3f, 2f),
            new BaselineSourceConfig(new Vector2(0.00f, -0.34f), new Vector2(0.05f, 0.07f), 78f, 0.80f, 0f, 0.06f, new Vector2(6.0f, 0.95f), 4f, 2f)
        };
        [Tooltip("Dye lost per second. Low: the baseline field restores what is lost.")]
        [Range(0f, 0.5f)] public float dyeDissipation = 0.02f;
        [Tooltip("Speed lost per second as a fraction (FluXY viscosity). Higher settles a disturbance sooner.")]
        [Range(0f, 1f)] public float viscosity = 0.15f;
        [Tooltip("FluXY vorticity confinement: keeps swirls alive. Too high turns calm water into soup.")]
        [Range(0f, 40f)] public float turbulence = 9f;
        [Range(0f, 1f)] public float pressure = 1f;
        [Tooltip("A very slight constant drift of the whole surface.")]
        public Vector2 drift = new Vector2(0.01f, 0f);
        [Tooltip("The fluid area is this much larger than the screen, so the border artefacts stay out of view.")]
        [Range(1f, 1.4f)] public float overscan = 1.15f;
        [Tooltip("Width of the soft edge where dye and motion fade at the fluid area's border.")]
        [Range(0f, 0.3f)] public float edgeWidth = 0.1f;
        [Tooltip("How quickly dye drains inside that edge.")]
        public float edgeDrain = 0.9f;
        [Tooltip("Slow curl noise stirred in by the flow sources (0 = none).")]
        [Range(0f, 3f)] public float baselineSwirl = 0.3f;

        [Header("Interaction: hands and torso")]
        public MovementTuning handMovement = new MovementTuning(0.12f, 1.6f, 5f, 14f, 0.08f);
        public MovementTuning torsoMovement = new MovementTuning(0.10f, 0.9f, 3f, 8f, 0.18f);
        [Tooltip("Diameter of a hand splat at rest / at full speed, in metres.")]
        public Vector2 handSize = new Vector2(0.16f, 0.30f);
        [Tooltip("Diameter of the torso splat in metres: broad, slower than the hands.")]
        public float torsoSize = 0.75f;
        [Tooltip("Splats per metre travelled. A target that does not move injects nothing.")]
        public float splatsPerMetre = 40f;
        [Tooltip("Hand dye weight at full speed, 0..1. Scaled by speed, so a still hand adds none.")]
        [Range(0f, 1f)] public float handDensity = 0.14f;
        [Tooltip("Torso dye weight at full speed: low on purpose.")]
        [Range(0f, 1f)] public float torsoDensity = 0.05f;
        [Tooltip("How strongly a moving hand pushes the fluid (FluXY velocity scale at full speed).")]
        public float handStir = 3.2f;
        [Tooltip("How strongly the torso pushes the fluid at full speed.")]
        public float torsoStir = 1.8f;
        [Range(0f, 1f)] public float velocityWeight = 0.6f;
        [Tooltip("Curl noise stirred in at full hand speed: fast gestures make visible turbulence.")]
        [Range(0f, 8f)] public float handTurbulence = 3.5f;
        [Tooltip("Colours: a hand starts here and turns toward the accent as it speeds up.")]
        public int leftHandColour = 1;
        public int rightHandColour = 4;
        public int accentColour = 5;
        [Tooltip("How much of the accent a full-speed gesture adds, 0..1.")]
        [Range(0f, 1f)] public float accentAtFullSpeed = 0.35f;
        [Tooltip("Faster than this a hand jump is a glitch, never a gesture.")]
        public float maxTargetJump = 1.2f;

        [Header("Detection response (state B)")]
        [Tooltip("Length of the gentle swirl around the body when a person is first confirmed.")]
        public float pulseSeconds = 1.2f;
        public float pulseRate = 14f;
        [Range(0f, 1f)] public float pulseDensity = 0.22f;
        [Range(0f, 8f)] public float pulseSwirl = 2.6f;
        public float pulseSize = 1.0f;

        [Header("State machine")]
        [Tooltip("Continuous good tracking needed before a person counts as detected.")]
        public float enterSeconds = 0.25f;
        [Tooltip("Continuous loss before state F.")]
        public float lostTimeoutSeconds = 0.5f;
        [Tooltip("State F lasts this long before the scene is Calm again. Nothing injects meanwhile.")]
        public float lostHoldSeconds = 2.5f;
        [Tooltip("A point above this strength is moving (state C).")]
        [Range(0f, 1f)] public float movingThreshold = 0.12f;
        [Tooltip("All points below this strength for settleHoldSeconds = standing still (state E).")]
        [Range(0f, 1f)] public float stillThreshold = 0.05f;
        public float settleHoldSeconds = 1.0f;
        [Tooltip("Seconds the injection gate takes to open or close.")]
        public float gateSeconds = 0.35f;

        [Header("Tracking")]
        [Tooltip("Joint confidence below this counts as not seen; trust ramps up over the next 0.2.")]
        [Range(0f, 0.9f)] public float minJointConfidence = 0.10f;
        public float jointGraceSeconds = 0.4f;
        public float jointFadeSeconds = 0.35f;
        [Tooltip("A pose older than this is stale and is not a person.")]
        public float maxPoseAgeSeconds = 0.5f;
        [Tooltip("Auto mode: seconds without live data before the simulation takes over.")]
        public float autoFallbackSeconds = 3f;
        public float smoothingSlowTau = 0.09f;
        public float smoothingFastTau = 0.025f;
        public float smoothingFastSpeed = 1.5f;
        [Tooltip("Metres of world height shown from the bottom to the top of the screen.")]
        public float viewHeightMetres = 2.4f;
        [Tooltip("World height shown at the centre of the screen. The hips sit near 0.95 m.")]
        public float viewCentreY = 1.05f;
        [Tooltip("Sideways steps are exaggerated by this so a person can cross the whole screen.")]
        public float lateralGain = 1.8f;

        [Header("Fluid simulation (the quality level and on/off switches live on the controller)")]
        public QualityProfile performanceProfile = new QualityProfile(256, 1, 2);
        public QualityProfile qualityProfile = new QualityProfile(512, 2, 3);

        [Header("Rendering (shader enhancements are switched on the controller)")]
        [Tooltip("Dye gain before the soft clip.")]
        [Range(0f, 8f)] public float dyeGain = 3.4f;
        [Tooltip("Brightness of the highlights along crests and gradients.")]
        [Range(0f, 3f)] public float crestStrength = 0.7f;
        [Tooltip("Relief: how strongly the dye thickness shapes the surface light.")]
        [Range(0f, 40f)] public float relief = 8f;
        [Tooltip("How much flow-carried surface detail ripples the layers.")]
        [Range(0f, 1f)] public float detailAmount = 0.35f;
        [Range(0.2f, 8f)] public float detailScale = 2.2f;
        [Tooltip("How fast the detail is carried by the flow.")]
        [Range(0f, 4f)] public float detailAdvection = 1.2f;
        [Tooltip("Number of faint contour layers across the dye thickness.")]
        [Range(0f, 24f)] public float layers = 5f;
        [Range(0f, 1f)] public float layerDepth = 0.15f;
        [Tooltip("Brightness of the ramp colour on the thickest dye.")]
        [Range(0f, 2f)] public float rampGlow = 0.5f;
        [Tooltip("Colour split along the flow around fast movement.")]
        [Range(0f, 0.02f)] public float chromaticSplit = 0.006f;
        [Tooltip("How far the dye is read back along the flow (streaks).")]
        [Range(0f, 0.2f)] public float streakLength = 0.05f;
        [Tooltip("Brightening of fast-moving fluid.")]
        [Range(0f, 2f)] public float motionGlow = 0.35f;
        [Range(0f, 1f)] public float vignette = 0.35f;

        float ITrackingTuning.MinJointConfidence {
            get {
                return minJointConfidence;
            }
        }

        float ITrackingTuning.JointGraceSeconds {
            get {
                return jointGraceSeconds;
            }
        }

        float ITrackingTuning.JointFadeSeconds {
            get {
                return jointFadeSeconds;
            }
        }

        float ITrackingTuning.SmoothingSlowTau {
            get {
                return smoothingSlowTau;
            }
        }

        float ITrackingTuning.SmoothingFastTau {
            get {
                return smoothingFastTau;
            }
        }

        float ITrackingTuning.SmoothingFastSpeed {
            get {
                return smoothingFastSpeed;
            }
        }

        float ITrackingTuning.MaxPoseAgeSeconds {
            get {
                return maxPoseAgeSeconds;
            }
        }

        float ITrackingTuning.AutoFallbackSeconds {
            get {
                return autoFallbackSeconds;
            }
        }

        float ITrackingTuning.ViewHeightMetres {
            get {
                return viewHeightMetres;
            }
        }

        float ITrackingTuning.ViewCentreY {
            get {
                return viewCentreY;
            }
        }

        float ITrackingTuning.LateralGain {
            get {
                return lateralGain;
            }
        }

        public QualityProfile ProfileFor(FluidQuality quality) {
            return quality == FluidQuality.Quality ? qualityProfile : performanceProfile;
        }

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
