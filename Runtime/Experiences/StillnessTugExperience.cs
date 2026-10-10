using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 11 — STILLNESS. A light hangs between everybody in the room and drifts toward
    /// whoever is stillest. Stand quiet and it comes to you. Fidget and you push it away.
    ///
    /// WHY THIS ONE EARNS ITS PLACE IN A SET OF TEN. Every other experience here rewards MOVEMENT —
    /// pop the bubble, hit the ball, stir the water, hold the pose — with the single exception of
    /// Footprints. A room full of installations that all shout "move faster" has one register. This
    /// one inverts the incentive, and it does it competitively: being still is worth something only
    /// relative to the people beside you, so a group works out for itself that the game is to
    /// out-calm each other. That is a different thing to watch and a different thing to be in.
    ///
    /// THE MATHS IS A WEIGHTED CENTROID, AND THAT IS WHY IT IS SAFE. The target is
    /// sum(w_i * p_i) / sum(w_i) over the people visible THIS FRAME, with w_i falling as person i
    /// moves. A weighted sum is symmetric in its terms: relabelling two people cannot change it,
    /// because addition does not care about order and no term is stored between frames. So the one
    /// failure that ruins per-player scoring — an identity switch that nothing announces, measured
    /// live at 0.015 m per frame against a 0.35 m margin with zero events logged — is invisible here
    /// by construction rather than by a guard.
    ///
    /// THE ORB'S EASING IS THE ONLY STATE, AND IT BELONGS TO THE ORB. The light drifts toward the
    /// target rather than snapping to it, which is what makes it read as being pulled rather than
    /// teleporting. That smoothing is history — but it is the LIGHT's history, not a person's, so two
    /// people trading identities leaves it untouched. Any per-person accumulator ("you have held it
    /// for 8 seconds") would not survive the same trade, which is exactly why there isn't one.
    ///
    /// WHAT "STILL" MEANS. <see cref="SkeletonPose.Energy"/> — mean joint speed over
    /// <see cref="SkeletonPose.FastSpeed"/> — measured per person from their own pose history, which
    /// F-33 gives each tracked person. The still/moving threshold is the same 0.35 m/s the foot
    /// planting uses, expressed on the same 0..1 scale; see <see cref="ExperienceTuning"/>, including
    /// the caveat that it was tuned at ~21 fps and a crowd stream runs nearer 16.
    /// </summary>
    public sealed class StillnessTugExperience : ExperienceBase {
        [Header("Stillness")]
        [Tooltip("How sharply stillness beats movement. 1 is a gentle preference; 4 means the "
                 + "stillest person takes almost the whole pull.")]
        [SerializeField] private float contrast = 3f;
        [Tooltip("Seconds for the light to cover most of the distance to its target. Slow on "
                 + "purpose: a light that tracks instantly reads as a cursor, not as a weight.")]
        [SerializeField] private float driftTau = 1.1f;
        [Tooltip("How close the light must come to count as having arrived, in metres.")]
        [SerializeField] private float arriveMetres = 0.55f;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>Height above each person's own floor that the light hovers at. Chest height: at
        /// head height it competes with the face, and at hip height it disappears behind people.</summary>
        private const float HoverMetres = 1.25f;

        private BodyRenderer[] renderers;
        private LineRenderer[] pulls;
        private Material[] pullMaterials;
        private Transform[] rings;
        private Material[] ringMaterials;
        private Transform orb;
        private Material orbMaterial;
        private Transform halo;
        private Material haloMaterial;

        private readonly CrowdPalette colours = new CrowdPalette();
        private readonly List<float> weights = new List<float>();
        private readonly List<Vector3> chests = new List<Vector3>();
        private Vector3 orbPosition = new Vector3(0f, HoverMetres, 0f);
        private bool orbPlaced;
        private float nearestGap = -1f;
        private float stillest;
        private bool arrived;
        private float cueCooldown;

        public override string Title {
            get {
                return "STILLNESS";
            }
        }

        public override string Instruction {
            get {
                return "Whoever is stillest draws the light. Stop moving. Out-calm each other.";
            }
        }

        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            pulls = new LineRenderer[MaxBodies];
            pullMaterials = new Material[MaxBodies];
            rings = new Transform[MaxBodies];
            ringMaterials = new Material[MaxBodies];

            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Hide();

                pullMaterials[i] = Palette.NewAdditive(Palette.Cool);
                pulls[i] = ShowStage.Line(root, "pull_" + i, 0.01f, pullMaterials[i]);
                pulls[i].enabled = false;

                // A flattened disc on the floor under each person, growing as they settle. The light
                // itself only ever shows who is winning; this shows each person their OWN state,
                // which is what lets somebody work out what they are being asked to do.
                rings[i] = ShowStage.Sphere(root, "ring_" + i, 0.4f, Palette, Palette.Cool, true,
                                            out ringMaterials[i]);
                rings[i].gameObject.SetActive(false);
                i = i + 1;
            }

            orb = ShowStage.Sphere(root, "light", 0.26f, Palette, Palette.Warm, true, out orbMaterial);
            // A second, larger, fainter sphere around the first. One sphere at this size reads as a
            // ball; a ball inside a glow reads as a light, which is what it has to be for "it came to
            // me" to feel like anything.
            halo = ShowStage.Sphere(root, "halo", 0.7f, Palette, Palette.Warm, true, out haloMaterial);
        }

        protected override void ResetExperience() {
            colours.Clear();
            orbPlaced = false;
            arrived = false;
            cueCooldown = 0f;
        }

        protected override void Play(float deltaSeconds) {
            cueCooldown = Mathf.Max(0f, cueCooldown - deltaSeconds);
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;

            int drawn = DrawBodies(bodies);
            Vector3 target;
            bool haveTarget = Target(bodies, drawn, out target);

            if (haveTarget) {
                if (!orbPlaced) {
                    // Snap the first time. Easing in from the origin would send the light sliding
                    // across the room on arrival, in full view of whoever just walked up.
                    orbPosition = target;
                    orbPlaced = true;
                } else {
                    orbPosition = Vector3.Lerp(orbPosition, target,
                                               1f - Mathf.Exp(-deltaSeconds / Mathf.Max(0.05f, driftTau)));
                }
            }

            DrawPulls(bodies, drawn);
            DrawOrb(haveTarget, deltaSeconds);
        }

        private int DrawBodies(IReadOnlyList<TrackedBody> bodies) {
            int drawn = 0;
            int i = 0;
            while (i < renderers.Length) {
                if (i < bodies.Count && bodies[i].Pose.Valid) {
                    // The body brightens as it stills, so a person can see their own contribution on
                    // themselves rather than only in the light's position. Tinted toward their own
                    // colour so several people are still tellable apart - see BodyRenderer.Render's
                    // note about colour already meaning speed.
                    float w = CrowdMath.Stillness(bodies[i].Pose, contrast);
                    renderers[i].Dim = 0.35f + 0.55f * w;
                    renderers[i].Render(bodies[i].Pose, colours.ColourFor(bodies[i].Id));
                    drawn = drawn + 1;
                } else {
                    renderers[i].Hide();
                }
                i = i + 1;
            }
            return drawn;
        }

        /// <summary>
        /// The weighted centroid of everybody visible this frame. Returns false when nobody is.
        ///
        /// Order-independent by construction, which is the whole safety argument: see the class note.
        /// </summary>
        private bool Target(IReadOnlyList<TrackedBody> bodies, int drawn, out Vector3 target) {
            weights.Clear();
            chests.Clear();
            target = Vector3.zero;
            if (drawn == 0) {
                stillest = 0f;
                return false;
            }

            stillest = 0f;
            float floor = 0f;
            bool haveFloor = false;

            int i = 0;
            while (i < drawn) {
                SkeletonPose pose = bodies[i].Pose;
                float w = CrowdMath.Stillness(pose, contrast);
                weights.Add(w);
                chests.Add(CrowdMath.ChestOf(pose));
                if (w > stillest) {
                    stillest = w;
                }
                if (pose.HasFloor) {
                    floor = haveFloor ? Mathf.Min(floor, pose.FloorY) : pose.FloorY;
                    haveFloor = true;
                }
                i = i + 1;
            }

            // A weighted sum, which is symmetric in its terms and therefore cannot be disturbed by
            // two people trading identities. The all-moving fallback lives in CrowdMath - see there
            // for why dividing by a near-zero total is the one thing that must not happen.
            target = CrowdMath.WeightedCentroid(chests, weights);
            target.y = (haveFloor ? floor : 0f) + HoverMetres;
            return true;
        }

        private void DrawPulls(IReadOnlyList<TrackedBody> bodies, int drawn) {
            float total = 0f;
            int i = 0;
            while (i < weights.Count) {
                total = total + weights[i];
                i = i + 1;
            }

            nearestGap = -1f;
            i = 0;
            while (i < pulls.Length) {
                bool show = i < drawn && i < weights.Count;
                pulls[i].enabled = show;
                rings[i].gameObject.SetActive(show);
                if (!show) {
                    i = i + 1;
                    continue;
                }

                SkeletonPose pose = bodies[i].Pose;
                float share = total > 1e-3f ? weights[i] / total : 1f / Mathf.Max(1, drawn);
                Vector3 chest = CrowdMath.ChestOf(pose);
                float gap = Vector3.Distance(chest, orbPosition);
                if (nearestGap < 0f || gap < nearestGap) {
                    nearestGap = gap;
                }

                pulls[i].SetPosition(0, chest);
                pulls[i].SetPosition(1, orbPosition);
                float width = 0.004f + 0.030f * share;
                pulls[i].startWidth = width;
                pulls[i].endWidth = width;
                Color c = colours.ColourFor(bodies[i].Id);
                SkeletonShowPalette.SetColour(pullMaterials[i], c * (0.10f + 1.5f * share));

                float floor = pose.HasFloor ? pose.FloorY : 0f;
                float radius = 0.22f + 0.85f * weights[i];
                rings[i].position = new Vector3(chest.x, floor + 0.008f, chest.z);
                rings[i].localScale = new Vector3(radius, 0.010f, radius);
                SkeletonShowPalette.SetColour(ringMaterials[i], c * (0.08f + 0.9f * weights[i]));
                i = i + 1;
            }
        }

        private void DrawOrb(bool haveTarget, float dt) {
            orb.gameObject.SetActive(haveTarget);
            halo.gameObject.SetActive(haveTarget);
            if (!haveTarget) {
                arrived = false;
                return;
            }

            orb.position = orbPosition;
            halo.position = orbPosition;

            float closeness = nearestGap >= 0f
                ? Mathf.Clamp01(1f - nearestGap / Mathf.Max(0.05f, arriveMetres * 3f))
                : 0f;
            float size = 0.20f + 0.16f * closeness;
            orb.localScale = Vector3.one * size;
            halo.localScale = Vector3.one * (size * 2.6f + 0.25f * closeness);
            SkeletonShowPalette.SetColour(orbMaterial, Color.Lerp(Palette.Warm, Palette.Hot, closeness)
                                                       * (1f + 1.2f * closeness));
            SkeletonShowPalette.SetColour(haloMaterial,
                                          Color.Lerp(Palette.Cool, Palette.Warm, closeness) * 0.22f);

            // Hysteresis on the arrival so a light hovering exactly on the boundary does not chime
            // repeatedly. The 1.6x release margin is wider than the per-frame jitter of the drift.
            bool near = nearestGap >= 0f && nearestGap < arriveMetres;
            bool far = nearestGap < 0f || nearestGap > arriveMetres * 1.6f;
            if (near && !arrived && cueCooldown <= 0f && ScoringAllowed) {
                Audio.Play(SoundCue.Bloom, 5, 0.55f);
                cueCooldown = 0.8f;
                arrived = true;
            } else if (far) {
                arrived = false;
            }
        }

        protected override string StatusText() {
            if (Stage.Bodies.Count == 0) {
                return "Step in front of the camera and stand still.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed";
            line = line + "\nstillest " + Mathf.RoundToInt(stillest * 100f) + "%";
            if (nearestGap >= 0f) {
                line = line + "      light is " + nearestGap.ToString("F2") + " m away"
                       + (arrived ? "      HELD" : string.Empty);
            }
            if (Stage.Bodies.Count == 1) {
                line = line + "\nIt is yours by default. Bring somebody in and you will have to earn it.";
            }
            return line;
        }
    }
}
