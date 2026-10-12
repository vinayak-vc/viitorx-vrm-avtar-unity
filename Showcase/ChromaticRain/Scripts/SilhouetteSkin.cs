using System;
using System.Collections.Generic;

using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// One look for the silhouette: the proportions of the body AND how it is drawn (fill colour, outline, softness).
    /// Everything here is read live, so a skin can be edited while the scene runs. Several skins live in a
    /// <see cref="SilhouetteSkinSet"/>.
    /// </summary>
    [Serializable]
    public sealed class SilhouetteSkin {
        [Tooltip("Shown in the debug text and used by the B key. Keep it short.")]
        public string name = "Anatomical";

        [Header("Look: the filled body")]
        [Tooltip("Colour of the filled body in Dark Silhouette mode: a little lighter than the background so the body "
                 + "reads as a solid shape even where the rain is sparse.")]
        public Color fillColor = new Color(0.018f, 0.024f, 0.040f, 1f);
        [Tooltip("Half-width of the feathered edge, in metres.")]
        [Range(0.002f, 0.06f)] public float edgeSoftness = 0.010f;
        [Tooltip("How far, in metres, neighbouring shapes melt into each other. Small keeps the gaps between the arms "
                 + "and the body and between the legs; large makes one blob.")]
        [Range(0f, 0.15f)] public float shapeBlend = 0.030f;

        [Header("Look: the outline (optional, separate from the fill)")]
        [Tooltip("The thin luminous edge along the outside of the filled mask. Off shows the bare filled body.")]
        public bool outlineEnabled = true;
        [Tooltip("Colour of the outline.")]
        [ColorUsage(false, true)]
        public Color outlineColor = new Color(0.22f, 0.50f, 1.00f, 1f);
        [Tooltip("Brightness of the outline.")]
        [Range(0f, 2f)] public float outlineIntensity = 0.45f;
        [Tooltip("Thickness of the outline in mask pixels.")]
        [Range(0.5f, 4f)] public float outlineWidth = 1.2f;

        [Header("Body proportions (metres at a 0.40 m shoulder width)")]
        [Tooltip("The landmarks are joints, not the outside of the body: these are the half-widths that put the "
                 + "outside back. Every shape is scaled by the measured shoulder width, so a small or a large person "
                 + "keeps these proportions.")]
        public bool adaptToShoulderWidth = true;
        public float referenceShoulderWidth = 0.40f;
        public float headRadiusX = 0.092f;
        public float headRadiusY = 0.118f;
        public float neckRadius = 0.052f;
        [Tooltip("The slope from each shoulder up to the neck.")]
        public float trapeziusRadius = 0.058f;
        [Tooltip("Chest half-width at the shoulders, as a fraction of half the measured shoulder span.")]
        [Range(0.7f, 1.3f)] public float chestWidthScale = 0.92f;
        [Tooltip("Half-width of the torso at the waist, the narrowest point.")]
        public float waistRadius = 0.145f;
        [Tooltip("Where the waist sits between the hips (0) and the shoulders (1).")]
        [Range(0.2f, 0.8f)] public float waistPosition = 0.50f;
        [Tooltip("Half-width of the pelvis at its lowest point.")]
        public float torsoBottomRadius = 0.158f;
        [Tooltip("How far below the hip joints the pelvis reaches (the crotch).")]
        public float pelvisDepth = 0.10f;
        [Tooltip("How far below the shoulder joints the top of the chest sits. The trapezius and the upper arms form the "
                 + "shoulders above it, so the shoulder line slopes down from the neck.")]
        public float shoulderDrop = 0.010f;
        [Tooltip("Corner radius of the chest and pelvis: larger gives rounder shoulders and hips.")]
        public float torsoRounding = 0.070f;
        [Tooltip("A person the camera cuts off at the waist or chest (a laptop webcam) has no legs to draw. On, the body "
                 + "continues below the pelvis, as wide as the hips, to past the bottom of the screen, so they read as a "
                 + "person sitting or standing out of shot. Off draws the torso only and ends it at the pelvis.")]
        public bool extendCutOffBody = true;
        [Tooltip("Upper arm half-width at the shoulder (the deltoid) and at the elbow.")]
        public float upperArmShoulderRadius = 0.056f;
        public float upperArmElbowRadius = 0.043f;
        public float foreArmElbowRadius = 0.043f;
        public float foreArmWristRadius = 0.030f;
        public float handPalmRadius = 0.034f;
        public float handEndRadius = 0.028f;
        [Tooltip("Hand length drawn from the wrist when hand tracking is absent.")]
        public float fallbackHandLength = 0.09f;
        [Tooltip("Thigh half-width at the hip and at the knee.")]
        public float thighHipRadius = 0.080f;
        public float thighKneeRadius = 0.055f;
        [Tooltip("Shin half-width just below the knee, at the calf, and at the ankle.")]
        public float shinKneeRadius = 0.055f;
        public float calfBulgeRadius = 0.056f;
        [Tooltip("Where the calf bulge sits between the knee (0) and the ankle (1).")]
        [Range(0.1f, 0.6f)] public float calfBulgePosition = 0.27f;
        public float shinAnkleRadius = 0.033f;
        public float footAnkleRadius = 0.038f;
        public float footToeRadius = 0.030f;
        [Tooltip("Foot length drawn from the ankle when there is no toe landmark.")]
        public float fallbackFootLength = 0.09f;
        [Tooltip("Longest believable upper arm / forearm, in metres.")]
        public float maxArmSegment = 0.46f;
        [Tooltip("Longest believable thigh / shin, in metres.")]
        public float maxLegSegment = 0.62f;

        public SilhouetteSkin() {
        }

        public SilhouetteSkin(string name) {
            this.name = name;
        }

        /// <summary>An independent copy: editing the copy never touches the original.</summary>
        public SilhouetteSkin Clone() {
            return (SilhouetteSkin)MemberwiseClone();
        }

        // ---- the built-in skins -------------------------------------------------------------------------------------

        /// <summary>The reference figure: broad shoulders, a waist, natural limbs, a thin blue outline.</summary>
        public static SilhouetteSkin Anatomical() {
            return new SilhouetteSkin("Anatomical");
        }

        /// <summary>A lean build: narrower shoulders, waist and hips, slimmer limbs.</summary>
        public static SilhouetteSkin Slim() {
            SilhouetteSkin s = new SilhouetteSkin("Slim");
            s.chestWidthScale = 0.86f;
            s.waistRadius = 0.122f;
            s.torsoBottomRadius = 0.138f;
            s.neckRadius = 0.046f;
            s.trapeziusRadius = 0.050f;
            s.upperArmShoulderRadius = 0.047f;
            s.upperArmElbowRadius = 0.036f;
            s.foreArmElbowRadius = 0.036f;
            s.foreArmWristRadius = 0.026f;
            s.thighHipRadius = 0.068f;
            s.thighKneeRadius = 0.047f;
            s.shinKneeRadius = 0.047f;
            s.calfBulgeRadius = 0.048f;
            s.shinAnkleRadius = 0.028f;
            return s;
        }

        /// <summary>A strong build: wide chest, thick arms and thighs.</summary>
        public static SilhouetteSkin Athletic() {
            SilhouetteSkin s = new SilhouetteSkin("Athletic");
            s.chestWidthScale = 1.04f;
            s.waistRadius = 0.158f;
            s.torsoBottomRadius = 0.168f;
            s.neckRadius = 0.062f;
            s.trapeziusRadius = 0.070f;
            s.upperArmShoulderRadius = 0.068f;
            s.upperArmElbowRadius = 0.050f;
            s.foreArmElbowRadius = 0.049f;
            s.foreArmWristRadius = 0.034f;
            s.thighHipRadius = 0.094f;
            s.thighKneeRadius = 0.063f;
            s.shinKneeRadius = 0.062f;
            s.calfBulgeRadius = 0.066f;
            s.shinAnkleRadius = 0.037f;
            return s;
        }

        /// <summary>The anatomical body with a bright cyan, thicker outline.</summary>
        public static SilhouetteSkin Neon() {
            SilhouetteSkin s = new SilhouetteSkin("Neon");
            s.fillColor = new Color(0.008f, 0.016f, 0.040f, 1f);
            s.outlineColor = new Color(0.10f, 0.90f, 1.00f, 1f);
            s.outlineIntensity = 1.2f;
            s.outlineWidth = 1.8f;
            s.edgeSoftness = 0.008f;
            return s;
        }

        /// <summary>A soft, glowing look: a feathered edge, rounder shapes, a wide violet outline.</summary>
        public static SilhouetteSkin SoftGlow() {
            SilhouetteSkin s = new SilhouetteSkin("Soft glow");
            s.fillColor = new Color(0.020f, 0.016f, 0.040f, 1f);
            s.outlineColor = new Color(0.60f, 0.35f, 1.00f, 1f);
            s.outlineIntensity = 0.8f;
            s.outlineWidth = 3.0f;
            s.edgeSoftness = 0.030f;
            s.shapeBlend = 0.060f;
            s.torsoRounding = 0.090f;
            return s;
        }

        /// <summary>The bare filled body: no outline at all.</summary>
        public static SilhouetteSkin Minimal() {
            SilhouetteSkin s = new SilhouetteSkin("Minimal");
            s.fillColor = new Color(0.004f, 0.005f, 0.010f, 1f);
            s.outlineEnabled = false;
            return s;
        }

        /// <summary>Every built-in skin, in the order they ship.</summary>
        public static List<SilhouetteSkin> BuiltIns() {
            List<SilhouetteSkin> list = new List<SilhouetteSkin>();
            list.Add(Anatomical());
            list.Add(Slim());
            list.Add(Athletic());
            list.Add(Neon());
            list.Add(SoftGlow());
            list.Add(Minimal());
            return list;
        }
    }

    /// <summary>
    /// The silhouette skins of the Chromatic Rain showcase, in one asset. Edit a skin in the Inspector (live, while the scene
    /// runs, or at rest) or add a new one with the + of the list; the context menu of this asset adds a copy of the first
    /// skin, or restores the built-in ones. The scene's <c>Start Skin</c> picks the skin shown first; the <c>B</c> key
    /// cycles through the list while the scene runs, without changing this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "ChromaticRainSkins", menuName = "Showcase/Chromatic Rain Silhouette Skins")]
    public sealed class SilhouetteSkinSet : ScriptableObject {
        [Tooltip("The skin shown when the scene starts (the index into the list below).")]
        [SerializeField] private int startSkin;
        [SerializeField] private List<SilhouetteSkin> skins = SilhouetteSkin.BuiltIns();

        public int Count {
            get {
                return skins != null ? skins.Count : 0;
            }
        }

        public int StartSkin {
            get {
                return Count == 0 ? 0 : Mathf.Clamp(startSkin, 0, Count - 1);
            }
        }

        /// <summary>The skin at <paramref name="index"/>, wrapped into the list. Never null: an empty set yields a default skin.</summary>
        public SilhouetteSkin Get(int index) {
            if (Count == 0) {
                return fallback;
            }
            int wrapped = ((index % Count) + Count) % Count;
            if (skins[wrapped] == null) {
                skins[wrapped] = new SilhouetteSkin("Skin " + (wrapped + 1));
            }
            return skins[wrapped];
        }

        /// <summary>The index after <paramref name="index"/>, wrapping at the end.</summary>
        public int Next(int index) {
            return Count == 0 ? 0 : (index + 1) % Count;
        }

        public string NameOf(int index) {
            return Get(index).name;
        }

        /// <summary>A set with every built-in skin, for a scene that has no asset assigned.</summary>
        public static SilhouetteSkinSet CreateDefault() {
            SilhouetteSkinSet set = CreateInstance<SilhouetteSkinSet>();
            set.name = "ChromaticRainSkins (built-in)";
            return set;
        }

        private readonly SilhouetteSkin fallback = new SilhouetteSkin();

        [ContextMenu("Add a copy of the first skin")]
        private void AddCopyOfFirst() {
            SilhouetteSkin copy = Count > 0 ? skins[0].Clone() : new SilhouetteSkin();
            copy.name = copy.name + " copy";
            skins.Add(copy);
        }

        [ContextMenu("Add the built-in skins that are missing")]
        private void AddMissingBuiltIns() {
            List<SilhouetteSkin> builtIns = SilhouetteSkin.BuiltIns();
            for (int i = 0; i < builtIns.Count; i++) {
                bool found = false;
                for (int k = 0; k < skins.Count; k++) {
                    if (skins[k] != null && string.Equals(skins[k].name, builtIns[i].name, StringComparison.Ordinal)) {
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    skins.Add(builtIns[i]);
                }
            }
        }

        private void OnValidate() {
            if (skins == null) {
                skins = SilhouetteSkin.BuiltIns();
            }
            for (int i = 0; i < skins.Count; i++) {
                if (skins[i] == null) {
                    skins[i] = new SilhouetteSkin("Skin " + (i + 1));
                } else if (string.IsNullOrEmpty(skins[i].name)) {
                    skins[i].name = "Skin " + (i + 1);
                }
            }
            startSkin = Mathf.Max(0, startSkin);
        }
    }
}
