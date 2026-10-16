using System;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>What <see cref="CutoutView"/> draws of the camera picture and the person mask.</summary>
    public enum CutoutMode {
        /// <summary>The camera picture as it is. The mask is ignored.</summary>
        Original = 0,

        /// <summary>The picture where the mask says person; transparent elsewhere, or replaced by a <see cref="CutoutBackdrop"/>.</summary>
        Person = 1,

        /// <summary>One colour where the mask says person; transparent elsewhere, or replaced by a <see cref="CutoutBackdrop"/>.</summary>
        Silhouette = 2
    }

    /// <summary>What is drawn behind the person. None leaves the scene behind the view showing through the transparent parts.</summary>
    public enum CutoutBackdrop {
        None = 0,
        Checker = 1,
        Color = 2,
        Gradient = 3,
        Image = 4
    }

    /// <summary>Whether the display is flipped left to right. Auto flips a live camera that the sender did not already flip: a selfie view.</summary>
    public enum CutoutMirror {
        Auto = 0,
        Off = 1,
        On = 2
    }

    /// <summary>How the picture is placed in the camera's view: Contain shows all of it (bars at the sides), Cover fills the view and crops.</summary>
    public enum CutoutFit {
        Contain = 0,
        Cover = 1
    }

    /// <summary>
    /// How the cutout is shown: the mode, the silhouette colour, the glow, the mirror and the backdrop, in one serializable object that a view
    /// reads every frame. Changing a value changes the next frame; nothing restarts, and the sender is never involved.
    /// </summary>
    [Serializable]
    public sealed class CutoutLook {
        [Tooltip("Original: the camera picture. Person: the person cut out of it. Silhouette: the person as one colour.")]
        [SerializeField] private CutoutMode mode = CutoutMode.Person;
        [Tooltip("Flip the display left to right. Auto flips a live camera the sender did not flip (a selfie view) and leaves a video file alone.")]
        [SerializeField] private CutoutMirror mirror = CutoutMirror.Auto;
        [Tooltip("The Silhouette colour. Red by default; the alpha scales how solid it is.")]
        [SerializeField] private Color silhouetteColor = new Color(1f, 0.08f, 0.08f, 1f);
        [Tooltip("1 shows the mask as the sender made it. Higher makes the edge crisper (a hard cut-out); never below 1 unless you want it softer.")]
        [Range(0.5f, 4f)]
        [SerializeField] private float edgeContrast = 1f;
        [Tooltip("A halo outside the person that traces the outline.")]
        [SerializeField] private bool glow = false;
        [SerializeField] private Color glowColor = new Color(1f, 0.35f, 0.35f, 1f);
        [Tooltip("How far the halo reaches, in pixels of the mask (the mask is usually 640 px on its long side).")]
        [Range(1f, 16f)]
        [SerializeField] private float glowWidth = 5f;
        [Range(0f, 2f)]
        [SerializeField] private float glowStrength = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float opacity = 1f;
        [Tooltip("What is behind the person. None: the scene behind the view shows through.")]
        [SerializeField] private CutoutBackdrop backdrop = CutoutBackdrop.Checker;
        [Tooltip("Backdrop Color, and the top of a Gradient.")]
        [SerializeField] private Color backdropColor = new Color(0.02f, 0.03f, 0.06f, 1f);
        [Tooltip("The bottom of a Gradient.")]
        [SerializeField] private Color backdropColor2 = new Color(0.10f, 0.16f, 0.30f, 1f);
        [Tooltip("Backdrop Image. Cover fit: it fills the view and crops what does not fit.")]
        [SerializeField] private Texture backdropImage;

        public CutoutMode Mode {
            get {
                return mode;
            }
            set {
                mode = value;
            }
        }

        public CutoutMirror Mirror {
            get {
                return mirror;
            }
            set {
                mirror = value;
            }
        }

        public Color SilhouetteColor {
            get {
                return silhouetteColor;
            }
            set {
                silhouetteColor = value;
            }
        }

        public float EdgeContrast {
            get {
                return edgeContrast;
            }
            set {
                edgeContrast = value;
            }
        }

        public bool Glow {
            get {
                return glow;
            }
            set {
                glow = value;
            }
        }

        public Color GlowColor {
            get {
                return glowColor;
            }
            set {
                glowColor = value;
            }
        }

        public float GlowWidth {
            get {
                return glowWidth;
            }
            set {
                glowWidth = value;
            }
        }

        public float GlowStrength {
            get {
                return glowStrength;
            }
            set {
                glowStrength = value;
            }
        }

        public float Opacity {
            get {
                return opacity;
            }
            set {
                opacity = value;
            }
        }

        public CutoutBackdrop Backdrop {
            get {
                return backdrop;
            }
            set {
                backdrop = value;
            }
        }

        public Color BackdropColor {
            get {
                return backdropColor;
            }
            set {
                backdropColor = value;
            }
        }

        public Color BackdropColor2 {
            get {
                return backdropColor2;
            }
            set {
                backdropColor2 = value;
            }
        }

        public Texture BackdropImage {
            get {
                return backdropImage;
            }
            set {
                backdropImage = value;
            }
        }
    }
}
