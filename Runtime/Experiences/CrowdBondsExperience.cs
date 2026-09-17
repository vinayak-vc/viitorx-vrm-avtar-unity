using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-32 EXPERIENCE 9 — BONDS. The first experience that needs more than one person.
    ///
    /// Everyone in frame gets their own colour. A line is drawn between every pair of people, and it
    /// brightens and thickens the closer they stand. Reach out and touch someone's hand and the bond
    /// between you flares and rings.
    ///
    /// WHY THIS ONE FIRST, out of everything two people unlock. Two properties decide whether a
    /// multi-person experience survives contact with real tracking, and this has both:
    ///
    ///  1. IT IS ROBUST TO AN ID SWITCH. Every other multi-person idea - a score per player, a
    ///     drawing per person, a duel - is *ruined* when the tracker swaps two identities, because
    ///     accumulated state follows the wrong body. ID switches WILL happen: on the seven-dancer
    ///     regression clip a few identities are lost to long occlusions. Here a switch costs two people
    ///     trading colours for a moment. Nothing accumulates, so nothing is corrupted. That makes
    ///     this the honest FIRST multi-person experience, before the tracker has earned more trust.
    ///
    ///  2. IT IS SELF-EVIDENT. A viewer does not need the concept explained: a line appears between
    ///     two people and responds to them moving together. It demonstrates, in one glance, the
    ///     thing that was impossible until now - that the system knows these are two DIFFERENT
    ///     people and is relating them to each other.
    ///
    /// EVERYTHING IS A RELATIVE MEASURE - distance between pairs, hand proximity - and relative
    /// measures need no calibration and no absolute accuracy. Two people 1.2 m apart read as 1.2 m
    /// apart even if both are 20 cm off in absolute depth, because the error is common-mode. That is
    /// the same reason Pose Match scores in torso units.
    ///
    /// WITH A SINGLE-PERSON SENDER this degrades honestly to one lonely coloured body and a line
    /// that says so, rather than looking broken.
    /// </summary>
    public sealed class CrowdBondsExperience : ExperienceBase {
        [Header("Bonds")]
        [Tooltip("Metres apart at which a bond is at its faintest. Beyond this the line fades out.")]
        [SerializeField] private float bondFarMetres = 3.0f;
        [Tooltip("Metres apart at which a bond is at its brightest.")]
        [SerializeField] private float bondNearMetres = 0.6f;
        [Tooltip("How close two people's hands must be to count as a touch, in metres.")]
        [SerializeField] private float touchMetres = 0.28f;

        /// <summary>Most people ever drawn. CrowdFrame caps the wire at 8; the sidecar's pose budget
        /// caps it far lower (measured 20.7 ms per person). This is only a rendering ceiling.</summary>
        private const int MaxBodies = 8;

        /// <summary>One line per PAIR, so N people need N*(N-1)/2 bonds. At the cap that is 28.</summary>
        private const int MaxBonds = MaxBodies * (MaxBodies - 1) / 2;

        private BodyRenderer[] renderers;
        private LineRenderer[] bonds;
        private Material[] bondMaterials;
        private Transform[] touchFlares;
        private Material[] touchFlareMaterials;

        /// <summary>F-34: the per-id colour table moved to <see cref="CrowdPalette"/> so that a
        /// person keeps the same colour in every crowd scene, not just this one. Same colours, same
        /// assignment order, same eviction rule - only the ownership changed.</summary>
        private readonly CrowdPalette colours = new CrowdPalette();

        private int touchCount;
        private float touchCooldown;
        private float closestPair = -1f;

        public override string Title {
            get {
                return "BONDS";
            }
        }

        public override string Instruction {
            get {
                return "Bring a friend. The closer you stand, the stronger the line. Touch hands.";
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Hide();
                i = i + 1;
            }

            bonds = new LineRenderer[MaxBonds];
            bondMaterials = new Material[MaxBonds];
            touchFlares = new Transform[MaxBonds];
            touchFlareMaterials = new Material[MaxBonds];
            i = 0;
            while (i < MaxBonds) {
                bondMaterials[i] = Palette.NewAdditive(Palette.Cool);
                bonds[i] = ShowStage.Line(root, "bond_" + i, 0.02f, bondMaterials[i]);
                bonds[i].enabled = false;
                touchFlares[i] = ShowStage.Sphere(root, "flare_" + i, 0.22f, Palette,
                                                  Palette.Hot, true, out touchFlareMaterials[i]);
                touchFlares[i].gameObject.SetActive(false);
                i = i + 1;
            }
        }

        protected override void ResetExperience() {
            touchCount = 0;
            colours.Clear();
        }

        // A colour is assigned per track id and remembered, so a person keeps their colour for as
        // long as the tracker keeps their identity. Assigning by list POSITION instead would make
        // everyone's colour change whenever anybody joined or left, which reads as a bug.
        private Color ColourFor(int id) {
            return colours.ColourFor(id);
        }

        protected override void Play(float deltaSeconds) {
            touchCooldown = Mathf.Max(0f, touchCooldown - deltaSeconds);
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;

            int drawn = 0;
            int i = 0;
            while (i < renderers.Length) {
                if (i < bodies.Count && bodies[i].Pose.Valid) {
                    Color c = ColourFor(bodies[i].Id);
                    // A LOST person is drawn dimmer rather than hidden: flickering somebody out
                    // because a friend walked in front of them looks far worse than a held pose.
                    float dim = bodies[i].IsScorable ? 1f : 0.45f;
                    renderers[i].Dim = dim;
                    renderers[i].RenderRaw(bodies[i].Pose.World, bodies[i].Pose.Confidence,
                                           c * dim, 1f);
                    drawn = drawn + 1;
                } else {
                    renderers[i].Hide();
                }
                i = i + 1;
            }

            DrawBonds(bodies, drawn);
        }

        private void DrawBonds(IReadOnlyList<TrackedBody> bodies, int drawn) {
            int bond = 0;
            closestPair = -1f;
            int a = 0;
            while (a < drawn && bond < bonds.Length) {
                int b = a + 1;
                while (b < drawn && bond < bonds.Length) {
                    SkeletonPose pa = bodies[a].Pose;
                    SkeletonPose pb = bodies[b].Pose;
                    if (!pa.Valid || !pb.Valid) {
                        bonds[bond].enabled = false;
                        touchFlares[bond].gameObject.SetActive(false);
                        bond = bond + 1;
                        b = b + 1;
                        continue;
                    }

                    float gap = Vector3.Distance(pa.Centre, pb.Centre);
                    if (closestPair < 0f || gap < closestPair) {
                        closestPair = gap;
                    }
                    // Closeness 0 (far) to 1 (touching), used for every visual property of the bond
                    // so they all say the same thing.
                    float close = Mathf.Clamp01(Mathf.InverseLerp(bondFarMetres, bondNearMetres, gap));

                    bonds[bond].enabled = close > 0.02f;
                    if (bonds[bond].enabled) {
                        // Drawn chest-to-chest rather than hip-to-hip: a line between hips passes
                        // through both bodies and reads as a floor marking, not a connection.
                        Vector3 pointA = ChestOf(pa);
                        Vector3 pointB = ChestOf(pb);
                        bonds[bond].SetPosition(0, pointA);
                        bonds[bond].SetPosition(1, pointB);
                        float width = 0.006f + 0.045f * close;
                        bonds[bond].startWidth = width;
                        bonds[bond].endWidth = width;
                        Color ca = ColourFor(bodies[a].Id);
                        Color cb = ColourFor(bodies[b].Id);
                        // The bond takes BOTH people's colours mixed, so it visibly belongs to the
                        // pair rather than to either of them.
                        SkeletonShowPalette.SetColour(bondMaterials[bond],
                                                      Color.Lerp(ca, cb, 0.5f) * (0.25f + 1.4f * close));
                    }

                    float touchGap = NearestHandGap(pa, pb);
                    bool touching = touchGap >= 0f && touchGap < touchMetres;
                    touchFlares[bond].gameObject.SetActive(touching);
                    if (touching) {
                        touchFlares[bond].position = 0.5f * (NearestHandA + NearestHandB);
                        float t = 1f - Mathf.Clamp01(touchGap / touchMetres);
                        touchFlares[bond].localScale = Vector3.one * (0.10f + 0.22f * t);
                        SkeletonShowPalette.SetColour(touchFlareMaterials[bond], Palette.Hot * (1f + t));
                        if (touchCooldown <= 0f && bodies[a].IsScorable && bodies[b].IsScorable) {
                            touchCount = touchCount + 1;
                            Audio.Play(SoundCue.Match, touchCount % 5, 0.65f);
                            touchCooldown = 0.6f;
                        }
                    }

                    bond = bond + 1;
                    b = b + 1;
                }
                a = a + 1;
            }

            while (bond < bonds.Length) {
                bonds[bond].enabled = false;
                touchFlares[bond].gameObject.SetActive(false);
                bond = bond + 1;
            }
        }

        private static Vector3 ChestOf(SkeletonPose pose) {
            if (pose.Present[11] && pose.Present[12]) {
                return 0.5f * (pose.World[11] + pose.World[12]);
            }
            return pose.Centre;
        }

        private Vector3 NearestHandA;
        private Vector3 NearestHandB;

        // Closest approach between any of A's hands and any of B's, or -1 when neither has a hand
        // tracked. Wrists, not fingertips: wrists are body joints and are tracked at any distance,
        // where finger landmarks are only reliable close in.
        private float NearestHandGap(SkeletonPose pa, SkeletonPose pb) {
            float best = -1f;
            int[] hands = { 15, 16 };
            int i = 0;
            while (i < hands.Length) {
                int j = 0;
                while (j < hands.Length) {
                    if (pa.Present[hands[i]] && pb.Present[hands[j]]) {
                        float d = Vector3.Distance(pa.World[hands[i]], pb.World[hands[j]]);
                        if (best < 0f || d < best) {
                            best = d;
                            NearestHandA = pa.World[hands[i]];
                            NearestHandB = pb.World[hands[j]];
                        }
                    }
                    j = j + 1;
                }
                i = i + 1;
            }
            return best;
        }

        protected override string StatusText() {
            if (!Stage.HasCrowd) {
                // Said plainly rather than shown as an error. A single-person sender is a valid
                // configuration, and the fix is a different sidecar, not a different scene.
                return "SINGLE-PERSON SENDER upstream - only one person can be tracked.\n"
                       + "Run multiperson_udp_sender.py to see bonds between people.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed";
            if (Stage.Bodies.Count < 2) {
                line = line + "\nBring someone else into frame - a bond needs two people.";
            } else if (closestPair >= 0f) {
                line = line + "\nclosest pair " + closestPair.ToString("F2") + " m"
                       + "      touches " + touchCount;
            }
            return line;
        }
    }
}
