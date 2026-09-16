using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 4 — DEPTH REACH. Rings float at three different distances. A ring only counts
    /// when your hand is at ITS depth — reaching to the right spot on screen is not enough.
    ///
    /// WHY THIS EXISTS AT ALL. Every other experience here would work, more or less, on an ordinary
    /// webcam. This one would not, and that is its entire purpose: it is the demonstration that
    /// answers "why the depth camera?" without a slide. A viewer sees a hand in the right place on
    /// screen fail to score, then sees the same hand score when it moves NEARER or FURTHER, and the
    /// point is made by the thing itself.
    ///
    /// THE DEPTH TEST IS SEPARATE FROM THE SCREEN TEST, deliberately, rather than folded into one 3D
    /// distance. A single sphere test would let a viewer conclude the system is doing ordinary 2D
    /// hit detection with a generous radius. Splitting them means the HUD can say "on target,
    /// 23 cm too near" — naming the axis that a flat camera cannot measure at all.
    ///
    /// HONEST LIMIT, and it is on screen rather than buried here: on a monocular source every joint
    /// depth is INFERRED, not measured, and the trust channel says so. The experience still plays —
    /// the model's monocular depth is real information — but the HUD reports which one it is,
    /// because a depth demo that cannot tell you whether the depth was measured is the one thing
    /// this should never be.
    /// </summary>
    public sealed class DepthReachExperience : ExperienceBase {
        [Header("Depth Reach")]
        [Tooltip("Seconds per round.")]
        [SerializeField] private float roundSeconds = 60f;

        /// <summary>How close the hand must be to the ring's centre ACROSS the screen.</summary>
        private const float LateralTolerance = 0.20f;

        /// <summary>And how close in DEPTH. Tighter than the lateral tolerance on purpose: depth is
        /// the axis being demonstrated, so it has to be the axis that is actually hard.</summary>
        private const float DepthTolerance = 0.14f;

        /// <summary>The ring must be held, so a hand sweeping through every depth on the way past
        /// cannot collect one by accident.</summary>
        private const float HoldSeconds = 0.35f;

        private const int RingCount = 3;

        private BodyRenderer body;
        private Transform ringRoot;
        private Ring[] rings;
        private int score;
        private int best;
        private float timeLeft;
        private bool roundOver;
        private int activeRing = -1;
        private string feedback = string.Empty;

        private sealed class Ring {
            public Transform Visual;
            public Material Material;
            public Transform Core;
            public Material CoreMaterial;
            public Vector3 Position;
            public float Depth;      // metres in front of the player's own hips
            public float Hold;
            public bool Active;
        }

        public override string Title {
            get {
                return "DEPTH REACH";
            }
        }

        public override string Instruction {
            get {
                return "Reach INTO the glowing ring - not just at it. Distance counts.";
            }
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root);
            body.Dim = 0.6f;

            ringRoot = new GameObject("Rings").transform;
            ringRoot.SetParent(root, false);
            rings = new Ring[RingCount];
            int i = 0;
            while (i < rings.Length) {
                Ring r = new Ring();
                // A torus would be ideal; a flattened sphere reads as a ring at this size and needs
                // no mesh asset, which keeps the scene a camera and one GameObject.
                r.Visual = ShowStage.Sphere(ringRoot, "ring_" + i, 0.42f, Palette, Palette.Cool,
                                            true, out r.Material);
                r.Visual.localScale = new Vector3(0.42f, 0.42f, 0.06f);
                r.Core = ShowStage.Sphere(ringRoot, "ring_core_" + i, 0.10f, Palette, Palette.Warm,
                                          true, out r.CoreMaterial);
                r.Active = false;
                r.Visual.gameObject.SetActive(false);
                r.Core.gameObject.SetActive(false);
                rings[i] = r;
                i = i + 1;
            }
            ResetExperience();
        }

        protected override void ResetExperience() {
            score = 0;
            timeLeft = roundSeconds;
            roundOver = false;
            activeRing = -1;
            feedback = string.Empty;
            int i = 0;
            while (i < rings.Length) {
                rings[i].Active = false;
                rings[i].Hold = 0f;
                rings[i].Visual.gameObject.SetActive(false);
                rings[i].Core.gameObject.SetActive(false);
                i = i + 1;
            }
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);

            if (!ScoringAllowed) {
                feedback = Stage.IsAttract
                    ? "Step in front of the camera to play."
                    : "Waiting for a player.";
                return;
            }

            if (!roundOver) {
                timeLeft = timeLeft - deltaSeconds;
                if (timeLeft <= 0f) {
                    timeLeft = 0f;
                    roundOver = true;
                    if (score > best) {
                        best = score;
                    }
                }
            }

            EnsureRings();
            UpdateRings(deltaSeconds);
        }

        // Three rings at three genuinely different distances, placed relative to the player so the
        // near one is always reachable and the far one always requires a lean or a step.
        private void EnsureRings() {
            bool any = false;
            int i = 0;
            while (i < rings.Length) {
                if (rings[i].Active) {
                    any = true;
                }
                i = i + 1;
            }
            if (any) {
                return;
            }

            SkeletonPose pose = Stage.Pose;
            Vector3 centre = pose.Centre;
            float reach = Mathf.Max(0.5f, Vector3.Distance(pose.World[11], pose.World[12]) * 2.0f);
            float[] depths = { reach * 0.55f, reach * 0.95f, reach * 1.35f };

            i = 0;
            while (i < rings.Length) {
                Ring r = rings[i];
                float side = (i - 1) * reach * 0.62f;
                float height = Random.Range(0.05f, 0.65f);
                r.Depth = depths[i];
                // -Z is toward the camera: the player faces the camera, so reaching "out" is -Z.
                r.Position = centre + new Vector3(side, height, -r.Depth);
                r.Hold = 0f;
                r.Active = true;
                r.Visual.gameObject.SetActive(true);
                r.Core.gameObject.SetActive(true);
                i = i + 1;
            }
            activeRing = Random.Range(0, rings.Length);
        }

        private void UpdateRings(float dt) {
            SkeletonPose pose = Stage.Pose;
            feedback = string.Empty;

            int i = 0;
            while (i < rings.Length) {
                Ring r = rings[i];
                if (!r.Active) {
                    i = i + 1;
                    continue;
                }
                bool isTarget = i == activeRing;
                r.Visual.position = r.Position;
                r.Core.position = r.Position;
                r.Core.gameObject.SetActive(isTarget);

                if (!isTarget) {
                    // Inactive rings stay visible but dim, so the player can see the depths on offer
                    // and understands the next one is not arbitrary.
                    SkeletonShowPalette.SetColour(r.Material, new Color(0.08f, 0.22f, 0.5f, 1f));
                    i = i + 1;
                    continue;
                }

                float bestLateral = float.MaxValue;
                float bestDepthError = 0f;
                bool insideDepth = false;
                bool insideLateral = false;

                int h = 0;
                while (h < 2) {
                    int wrist = h == 0 ? 15 : 16;
                    if (pose.Present[wrist]) {
                        Vector3 hand = pose.World[wrist];
                        // Lateral = across the screen only. Depth = along Z only. Kept apart so the
                        // HUD can name which one is wrong, which is the whole demonstration.
                        float lateral = Vector2.Distance(new Vector2(hand.x, hand.y),
                                                         new Vector2(r.Position.x, r.Position.y));
                        float depthError = hand.z - r.Position.z;
                        if (lateral < bestLateral) {
                            bestLateral = lateral;
                            bestDepthError = depthError;
                        }
                        if (lateral <= LateralTolerance) {
                            insideLateral = true;
                            if (Mathf.Abs(depthError) <= DepthTolerance) {
                                insideDepth = true;
                            }
                        }
                    }
                    h = h + 1;
                }

                if (insideLateral && insideDepth) {
                    r.Hold = r.Hold + dt;
                    SkeletonShowPalette.SetColour(r.Material, new Color(0.3f, 2.2f, 0.7f, 1f));
                    feedback = "IN THE RING - hold it";
                    if (r.Hold >= HoldSeconds && !roundOver) {
                        score = score + 1;
                        Retire();
                        return;
                    }
                } else {
                    r.Hold = 0f;
                    if (insideLateral) {
                        // The interesting case, and the one worth spelling out: the hand is exactly
                        // where a flat camera would call it a hit, and it is not one.
                        SkeletonShowPalette.SetColour(r.Material, new Color(2.3f, 1.2f, 0.15f, 1f));
                        feedback = bestDepthError > 0f
                            ? "ON TARGET but " + Mathf.RoundToInt(Mathf.Abs(bestDepthError) * 100f)
                              + " cm TOO FAR BACK - reach further out"
                            : "ON TARGET but " + Mathf.RoundToInt(Mathf.Abs(bestDepthError) * 100f)
                              + " cm TOO FAR FORWARD - pull back";
                    } else {
                        SkeletonShowPalette.SetColour(r.Material, Palette.Cool);
                    }
                }
                i = i + 1;
            }
        }

        private void Retire() {
            int i = 0;
            while (i < rings.Length) {
                rings[i].Active = false;
                rings[i].Hold = 0f;
                rings[i].Visual.gameObject.SetActive(false);
                rings[i].Core.gameObject.SetActive(false);
                i = i + 1;
            }
            activeRing = -1;
        }

        protected override string StatusText() {
            if (!ScoringAllowed) {
                return feedback;
            }
            string depthSource = "depth: unreported";
            TrackingTelemetry t = Stage.Telemetry;
            if (t != null && t.HasDepthChannel) {
                int measured = t.MeasuredDepthCount();
                depthSource = measured > 0
                    ? "depth: " + measured + "/33 MEASURED by the stereo pair"
                    // Said plainly. A depth demo that cannot tell you whether the depth was measured
                    // is the one thing this must never be.
                    : "depth: INFERRED (monocular source - no stereo pair on this input)";
            }
            string line = "SCORE " + score + "      TIME " + Mathf.CeilToInt(timeLeft) + "s"
                          + "      best " + best;
            if (Stage.Pose.Present[15]) {
                line = line + "\nleft hand " + Stage.Pose.CameraDepth(15).ToString("F2")
                       + " m from camera";
            }
            if (Stage.Pose.Present[16]) {
                line = line + "      right hand " + Stage.Pose.CameraDepth(16).ToString("F2") + " m";
            }
            line = line + "\n" + depthSource;
            if (!string.IsNullOrEmpty(feedback)) {
                line = line + "\n\n" + feedback;
            }
            if (roundOver) {
                line = line + "\n\nTIME UP - final score " + score + ".  Press R to play again.";
            }
            return line;
        }
    }
}
