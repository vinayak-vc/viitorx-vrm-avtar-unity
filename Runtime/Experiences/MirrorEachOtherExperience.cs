using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 18 — MIRROR EACH OTHER. No target pose. Two of you hold the same shape, and
    /// the screen says how alike you are.
    ///
    /// WHY IT IS THE STRONGEST REUSE IN THE SET. Pose Match already normalises joint error by the
    /// PLAYER'S OWN torso length, and it does that for a reason that turns out to be exactly the
    /// reason person-to-person comparison needs: so that a tall adult and a child are judged on
    /// whether their SHAPE matches, not on whether their absolute limb positions coincide. Swap the
    /// authored template for the other person's live pose and the whole scoring apparatus is already
    /// correct. An absolute-metres comparison between two differently sized people would be
    /// unplayable for every pair except two people of the same build.
    ///
    /// THE METRIC IS SYMMETRIC IN THE PAIR, so it survives an identity switch intact. similarity(A,B)
    /// is a mean of per-joint distances between two normalised poses: exchanging A and B gives the
    /// same number, and nothing is stored between frames. When the tracker silently trades two
    /// identities — 0.015 m per frame against a 0.35 m margin, zero events logged — the same two
    /// bodies are still being compared and the same number comes out.
    ///
    /// NOBODY IS RIGHT AND NOBODY IS WRONG, which is the social point. Pose Match has an authored
    /// answer and measures your distance from it, so somebody is failing. Here the score is a property
    /// of the PAIR, and the only way to raise it is for both of you to move toward each other. That
    /// turns out to be a completely different thing to watch strangers do.
    ///
    /// THE HOLD TIMER BELONGS TO THE ROOM, NOT TO A PAIR. It runs while ANY pair is above the
    /// threshold and resets when none is. Keying it to a specific pair would put identity back into
    /// the accumulated state — a switch would hand one pair's streak to another — for nothing that a
    /// viewer could see, because what a viewer reads is "we are holding it", not "pair 1-3 is holding
    /// it".
    ///
    /// DIRECT BY DEFAULT, MIRRORED ON X. Standing side by side facing the camera, "the same shape"
    /// means you both raise your own left arm, which is a DIRECT comparison of normalised offsets.
    /// Facing each other, mirroring is what people actually do, and that needs the left/right joints
    /// swapped and the sideways axis negated. Both are one key apart because which one is right
    /// depends on how the room is laid out, and that is not something a file can know.
    /// </summary>
    public sealed class MirrorEachOtherExperience : ExperienceBase {
        [Header("Mirror Each Other")]
        [Tooltip("How alike a pair has to be before it counts, 0 to 1.")]
        [SerializeField] private float matchFraction = 0.72f;
        [Tooltip("Seconds a pair must hold the match before it is celebrated.")]
        [SerializeField] private float holdSeconds = 0.6f;
        [Tooltip("Compare mirrored (left to right) rather than directly. Toggle live with X.")]
        [SerializeField] private bool mirrored;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>Joints that are scored. The head and the four limb ends plus elbows and knees: the
        /// joints a person can consciously place. Hips and shoulders are excluded because they are the
        /// frame the pose is measured against, so they would always agree and inflate the score. Same
        /// set as Pose Match, deliberately — a person who has played one should find the other judges
        /// them the same way.</summary>
        private static readonly int[] Scored = { 0, 13, 14, 15, 16, 25, 26, 27, 28 };

        /// <summary>Left/right joint pairs, for the mirrored comparison.</summary>
        private static readonly int[] MirrorOf = CrowdMath.BuildMirrorTable(PoseFrame.LandmarkCount);

        /// <summary>A joint counts as agreeing within this many TORSO LENGTHS. 0.30 of a torso is
        /// roughly 13 cm on an adult — a shade looser than Pose Match's 0.28, because here BOTH sides
        /// carry tracking error rather than one side being an exact authored template.</summary>
        private const float Tolerance = 0.30f;

        private BodyRenderer[] renderers;
        private Transform[][] markers;
        private Material[][] markerMaterials;
        private LineRenderer bridge;
        private Material bridgeMaterial;

        private readonly CrowdPalette colours = new CrowdPalette();
        private readonly List<float> normalised = new List<float>();

        private int bestA = -1;
        private int bestB = -1;
        private float bestScore;
        private float holding;
        private int matches;
        private bool celebrating;
        private float celebrateTimer;

        private static readonly Color AgreeColour = new Color(0.25f, 2.1f, 0.6f, 1f);
        private static readonly Color DisagreeColour = new Color(2.2f, 0.65f, 0.25f, 1f);

        public override string Title {
            get {
                return "MIRROR EACH OTHER";
            }
        }

        public override string Instruction {
            get {
                return "Two of you. Hold the same shape. Nobody is right - you just have to agree.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      X direct/mirrored";
        }

        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            markers = new Transform[MaxBodies][];
            markerMaterials = new Material[MaxBodies][];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Dim = 0.75f;
                renderers[i].Hide();

                // Feedback has to be ON THE PEOPLE. A percentage in the corner tells a pair they are
                // 64% alike and nothing about which limb to move; a green knee and an orange wrist
                // tells them exactly.
                markers[i] = new Transform[Scored.Length];
                markerMaterials[i] = new Material[Scored.Length];
                int m = 0;
                while (m < Scored.Length) {
                    markers[i][m] = ShowStage.Sphere(root, "mark_" + i + "_" + m, 0.075f, Palette,
                                                     DisagreeColour, true, out markerMaterials[i][m]);
                    markers[i][m].gameObject.SetActive(false);
                    m = m + 1;
                }
                i = i + 1;
            }

            bridgeMaterial = Palette.NewAdditive(Palette.Warm);
            bridge = ShowStage.Line(root, "bridge", 0.02f, bridgeMaterial);
            bridge.enabled = false;
        }

        protected override void ResetExperience() {
            colours.Clear();
            matches = 0;
            holding = 0f;
            celebrating = false;
            celebrateTimer = 0f;
            bestA = -1;
            bestB = -1;
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.X)) {
                mirrored = !mirrored;
            }
        }

        protected override void Play(float deltaSeconds) {
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;
            int drawn = Mathf.Min(bodies.Count, MaxBodies);
            while (drawn > 0 && !bodies[drawn - 1].Pose.Valid) {
                drawn = drawn - 1;
            }

            FindBestPair(bodies, drawn);
            DrawBodies(bodies, drawn);
            DrawMarkers(bodies, drawn);
            DrawBridge(bodies);
            UpdateHold(deltaSeconds);
        }

        /// <summary>
        /// Score every pair and keep the best. O(N^2) over at most eight people, which at three is
        /// three comparisons a frame — far below the cost of drawing them.
        /// </summary>
        private void FindBestPair(IReadOnlyList<TrackedBody> bodies, int drawn) {
            bestA = -1;
            bestB = -1;
            bestScore = 0f;
            int a = 0;
            while (a < drawn) {
                int b = a + 1;
                while (b < drawn) {
                    // Only pairs where BOTH people were actually seen this frame. A held pose is being
                    // predicted, and two predictions agree with each other beautifully.
                    if (bodies[a].IsScorable && bodies[b].IsScorable) {
                        float score = Similarity(bodies[a].Pose, bodies[b].Pose, null);
                        if (score > bestScore) {
                            bestScore = score;
                            bestA = a;
                            bestB = b;
                        }
                    }
                    b = b + 1;
                }
                a = a + 1;
            }
        }

        /// <summary>
        /// How alike two poses are, 0 to 1. The measure itself lives in <see cref="CrowdMath"/> so
        /// that the one property this scene depends on - that it is SYMMETRIC in its two arguments -
        /// is asserted by a unit test rather than only by this comment.
        /// </summary>
        private float Similarity(SkeletonPose pa, SkeletonPose pb, List<float> perJoint) {
            return CrowdMath.Similarity(pa, pb, Scored, MirrorOf, mirrored, Tolerance, perJoint);
        }

        private void DrawBodies(IReadOnlyList<TrackedBody> bodies, int drawn) {
            int i = 0;
            while (i < renderers.Length) {
                if (i >= drawn) {
                    renderers[i].Hide();
                    i = i + 1;
                    continue;
                }
                bool inPair = i == bestA || i == bestB;
                renderers[i].Dim = (inPair ? 0.95f : 0.45f) * (bodies[i].IsScorable ? 1f : 0.6f);
                renderers[i].Render(bodies[i].Pose, colours.ColourFor(bodies[i].Id));
                i = i + 1;
            }
        }

        private void DrawMarkers(IReadOnlyList<TrackedBody> bodies, int drawn) {
            int i = 0;
            while (i < markers.Length) {
                bool inPair = i == bestA || i == bestB;
                int m = 0;
                while (m < Scored.Length) {
                    markers[i][m].gameObject.SetActive(false);
                    m = m + 1;
                }
                if (!inPair) {
                    i = i + 1;
                    continue;
                }
                i = i + 1;
            }
            if (bestA < 0 || bestB < 0 || bestA >= drawn || bestB >= drawn) {
                return;
            }

            // Recomputed with per-joint detail for the pair being shown. One extra evaluation a frame,
            // which buys the only feedback that tells a person which arm to move.
            Similarity(bodies[bestA].Pose, bodies[bestB].Pose, normalised);
            PlaceMarkers(bestA, bodies[bestA].Pose, false);
            PlaceMarkers(bestB, bodies[bestB].Pose, true);
        }

        private void PlaceMarkers(int slot, SkeletonPose pose, bool useMirrorIndex) {
            int m = 0;
            while (m < Scored.Length) {
                int joint = useMirrorIndex && mirrored ? MirrorOf[Scored[m]] : Scored[m];
                bool show = pose.Present[joint] && m < normalised.Count;
                markers[slot][m].gameObject.SetActive(show);
                if (show) {
                    float agreement = normalised[m];
                    markers[slot][m].position = pose.World[joint];
                    markers[slot][m].localScale = Vector3.one * (0.055f + 0.045f * agreement);
                    SkeletonShowPalette.SetColour(markerMaterials[slot][m],
                        Color.Lerp(DisagreeColour, AgreeColour, agreement)
                        * (0.6f + 0.9f * agreement));
                }
                m = m + 1;
            }
        }

        private void DrawBridge(IReadOnlyList<TrackedBody> bodies) {
            bool show = bestA >= 0 && bestB >= 0 && bestA < bodies.Count && bestB < bodies.Count;
            bridge.enabled = show;
            if (!show) {
                return;
            }
            Vector3 a = CrowdMath.ChestOf(bodies[bestA].Pose);
            Vector3 b = CrowdMath.ChestOf(bodies[bestB].Pose);
            bridge.SetPosition(0, a);
            bridge.SetPosition(1, b);
            float width = 0.005f + 0.04f * bestScore;
            bridge.startWidth = width;
            bridge.endWidth = width;
            SkeletonShowPalette.SetColour(bridgeMaterial,
                Color.Lerp(Palette.Cool, AgreeColour, bestScore) * (0.3f + 1.6f * bestScore));
        }

        private void UpdateHold(float dt) {
            if (celebrating) {
                celebrateTimer = celebrateTimer - dt;
                if (celebrateTimer <= 0f) {
                    celebrating = false;
                    holding = 0f;
                }
                return;
            }

            // Held, not merely touched: two people sweeping their arms past each other pass through
            // agreement for a frame or two, and rewarding that would reward flailing rather than
            // arriving at the same shape.
            if (bestScore >= matchFraction && ScoringAllowed) {
                holding = holding + dt;
                if (holding >= holdSeconds) {
                    matches = matches + 1;
                    celebrating = true;
                    celebrateTimer = 1.2f;
                    Audio.Play(SoundCue.Match, matches % 5, 0.85f);
                }
            } else {
                holding = 0f;
            }
        }

        protected override string StatusText() {
            string mode = mirrored ? "MIRRORED (facing each other)" : "DIRECT (side by side)";
            if (!Stage.HasCrowd) {
                return "SINGLE-PERSON SENDER upstream - this needs two people.\n"
                       + "Run multiperson_udp_sender.py.";
            }
            if (Stage.Bodies.Count < 2) {
                return "Bring somebody in and hold the same shape.      " + mode;
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed      " + mode;
            if (celebrating) {
                return line + "\n\nTOGETHER!      " + matches + " so far";
            }
            int percent = Mathf.RoundToInt(bestScore * 100f);
            string bar = new string('|', Mathf.Clamp(Mathf.RoundToInt(holding * 20f), 0, 40));
            return line
                   + "\nbest pair " + percent + "%   (need "
                   + Mathf.RoundToInt(matchFraction * 100f) + "%)      together " + matches
                   + "\nhold " + bar;
        }
    }
}
