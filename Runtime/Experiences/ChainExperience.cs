using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 15 — CHAIN. Hold hands and the current flows down the line. Link everybody in
    /// the room into one chain and the whole thing lights.
    ///
    /// THE MOST DIRECT IDEA IN THE SET, and the one with a real risk that is NOT about identity.
    /// Everything else here is designed around an identity switch. This one is safe from that by
    /// construction — the chain is a GRAPH over wrist positions, rebuilt from scratch every frame,
    /// with no id read anywhere — but it leans on something that has never been measured: whether the
    /// DETECTOR still separates two people standing close enough to hold hands.
    ///
    /// SAY THAT PLAINLY, BECAUSE IT IS THE MOST LIKELY WAY THIS SCENE FAILS. The sidecar detects
    /// people as boxes and poses each box. Two people shoulder to shoulder is the classic case for
    /// two boxes merging into one, and F-32's seven-dancer clip is where hand plausibility fell to
    /// 59.9% — identity contamination, not range. If people vanish from the count as they join hands,
    /// that is the detector merging them, not the chain logic. The HUD watches for exactly that and
    /// says so rather than letting it read as a bug in the game. Fixing it is a detector question and
    /// belongs upstream.
    ///
    /// WRISTS, NOT FINGERTIPS. Wrists are body joints and are tracked at any distance the body is;
    /// hand landmarks come on a separate channel, only for the primary person, and degrade with both
    /// distance and crowding. A scene about several people touching cannot be built on a channel that
    /// only exists for one of them — the same call Bonds made, for the same reason.
    ///
    /// THE CIRCUIT'S STATE BELONGS TO THE CIRCUIT. How long the chain has been complete is counted,
    /// and that is accumulated state — but it is a property of the GRAPH, not of any person in it. Two
    /// people swapping identities leaves the graph identical, so the count is untouched. A per-person
    /// "you have been connected for 8 seconds" would not survive the same swap, which is why there
    /// isn't one.
    ///
    /// EVERYONE MUST BE IN IT. The chain ignites only when the connected component covers every person
    /// on screen. That is what turns it from a thing two people do into a thing a room does — and it
    /// is why a person standing apart is drawn with a visible open end rather than just left dark.
    /// </summary>
    public sealed class ChainExperience : ExperienceBase {
        [Header("Chain")]
        [Tooltip("How close two people's nearest hands must be to count as linked, in metres. "
                 + "Generous on purpose: this is a wrist-to-wrist distance between two people who "
                 + "are actually holding hands, so it includes both their hands.")]
        [SerializeField] private float linkMetres = 0.34f;
        [Tooltip("Metres of hysteresis before a link breaks. Without it a link flickers on tracker "
                 + "noise while two people stand exactly at the threshold.")]
        [SerializeField] private float breakMargin = 0.10f;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>One link per PAIR at worst, so N people need N*(N-1)/2.</summary>
        private const int MaxLinks = MaxBodies * (MaxBodies - 1) / 2;

        /// <summary>Beads travelling along each link. Enough to read as a current rather than as a
        /// dot; few enough that the link is still a line.</summary>
        private const int BeadsPerLink = 5;

        /// <summary>Seconds a completed circuit has to hold before it counts. A chain that fires the
        /// instant two hands brush past each other rewards flailing, not connecting.</summary>
        private const float IgniteHold = 0.35f;

        private BodyRenderer[] renderers;
        private LineRenderer[] links;
        private Material[] linkMaterials;
        private Transform[][] beads;
        private Material[][] beadMaterials;
        private Transform[] openEnds;
        private Material[] openEndMaterials;

        private readonly CrowdPalette colours = new CrowdPalette();
        private readonly int[] parent = new int[MaxBodies];
        private readonly bool[] linked = new bool[MaxBodies];
        private readonly bool[,] wasLinked = new bool[MaxBodies, MaxBodies];
        private readonly List<int> linkA = new List<int>();
        private readonly List<int> linkB = new List<int>();
        private readonly List<Vector3> linkFrom = new List<Vector3>();
        private readonly List<Vector3> linkTo = new List<Vector3>();

        private float phase;
        private bool complete;
        private float completeFor;
        private float longestCircuit;
        private int circuits;
        private bool ignited;
        private int detectedPeak;
        private float detectedPeakAge;

        public override string Title {
            get {
                return "CHAIN";
            }
        }

        public override string Instruction {
            get {
                return "Hold hands. Link everybody in the room and the whole chain lights.";
            }
        }

        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            openEnds = new Transform[MaxBodies];
            openEndMaterials = new Material[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Hide();
                // A small light at an unconnected person's free hand. Being left out has to be
                // VISIBLE and has to look like an invitation rather than a fault.
                openEnds[i] = ShowStage.Sphere(root, "open_" + i, 0.13f, Palette, Palette.Cool, true,
                                               out openEndMaterials[i]);
                openEnds[i].gameObject.SetActive(false);
                i = i + 1;
            }

            links = new LineRenderer[MaxLinks];
            linkMaterials = new Material[MaxLinks];
            beads = new Transform[MaxLinks][];
            beadMaterials = new Material[MaxLinks][];
            i = 0;
            while (i < MaxLinks) {
                linkMaterials[i] = Palette.NewAdditive(Palette.Warm);
                links[i] = ShowStage.Line(root, "link_" + i, 0.03f, linkMaterials[i]);
                links[i].enabled = false;
                beads[i] = new Transform[BeadsPerLink];
                beadMaterials[i] = new Material[BeadsPerLink];
                int b = 0;
                while (b < BeadsPerLink) {
                    beads[i][b] = ShowStage.Sphere(root, "spark_" + i + "_" + b, 0.09f, Palette,
                                                   Palette.Hot, true, out beadMaterials[i][b]);
                    beads[i][b].gameObject.SetActive(false);
                    b = b + 1;
                }
                i = i + 1;
            }
        }

        protected override void ResetExperience() {
            colours.Clear();
            circuits = 0;
            longestCircuit = 0f;
            completeFor = 0f;
            ignited = false;
            detectedPeak = 0;
            int a = 0;
            while (a < MaxBodies) {
                int b = 0;
                while (b < MaxBodies) {
                    wasLinked[a, b] = false;
                    b = b + 1;
                }
                a = a + 1;
            }
        }

        protected override void Play(float deltaSeconds) {
            phase = phase + deltaSeconds * 0.8f;
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;
            int drawn = Mathf.Min(bodies.Count, MaxBodies);
            while (drawn > 0 && !bodies[drawn - 1].Pose.Valid) {
                drawn = drawn - 1;
            }

            BuildLinks(bodies, drawn);
            complete = IsOneCircuit(drawn);
            UpdateIgnition(drawn, deltaSeconds);
            WatchForMerges(deltaSeconds, drawn);
            Draw(bodies, drawn);
        }

        // Every pair whose nearest hands are within reach, with hysteresis so a link that exists does
        // not drop out on a millimetre of tracker noise.
        private void BuildLinks(IReadOnlyList<TrackedBody> bodies, int drawn) {
            linkA.Clear();
            linkB.Clear();
            linkFrom.Clear();
            linkTo.Clear();
            int i = 0;
            while (i < MaxBodies) {
                parent[i] = i;
                linked[i] = false;
                i = i + 1;
            }

            int a = 0;
            while (a < drawn) {
                int b = a + 1;
                while (b < drawn) {
                    Vector3 handA;
                    Vector3 handB;
                    float gap = NearestHandGap(bodies[a].Pose, bodies[b].Pose, out handA, out handB);
                    // Wider to keep than to make: a link that exists survives to linkMetres +
                    // breakMargin, so two people holding hands do not flicker apart and back.
                    float threshold = wasLinked[a, b] ? linkMetres + breakMargin : linkMetres;
                    bool joined = gap >= 0f && gap < threshold;
                    wasLinked[a, b] = joined;
                    wasLinked[b, a] = joined;
                    if (joined && linkA.Count < MaxLinks) {
                        linkA.Add(a);
                        linkB.Add(b);
                        linkFrom.Add(handA);
                        linkTo.Add(handB);
                        linked[a] = true;
                        linked[b] = true;
                        Union(a, b);
                    }
                    b = b + 1;
                }
                a = a + 1;
            }

            // Pairs involving people who have left have to be forgotten, or a stale "was linked" entry
            // makes the next person to take that list position start out already connected.
            int stale = drawn;
            while (stale < MaxBodies) {
                int other = 0;
                while (other < MaxBodies) {
                    wasLinked[stale, other] = false;
                    wasLinked[other, stale] = false;
                    other = other + 1;
                }
                stale = stale + 1;
            }
        }

        private int Find(int x) {
            while (parent[x] != x) {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private void Union(int a, int b) {
            int ra = Find(a);
            int rb = Find(b);
            if (ra != rb) {
                parent[rb] = ra;
            }
        }

        // One component covering everybody. Two people is the smallest chain worth lighting.
        private bool IsOneCircuit(int drawn) {
            if (drawn < 2) {
                return false;
            }
            int root = Find(0);
            int i = 1;
            while (i < drawn) {
                if (Find(i) != root) {
                    return false;
                }
                i = i + 1;
            }
            return true;
        }

        private void UpdateIgnition(int drawn, float dt) {
            // Only a chain of people actually SEEN this frame counts. A person held through an
            // occlusion is being predicted, and a predicted hand completing a circuit is a circuit
            // nobody made.
            bool honest = complete && ScoringAllowed && AllScorable(drawn);
            if (honest) {
                completeFor = completeFor + dt;
                if (completeFor > longestCircuit) {
                    longestCircuit = completeFor;
                }
                if (!ignited && completeFor >= IgniteHold) {
                    ignited = true;
                    circuits = circuits + 1;
                    Audio.Play(SoundCue.Match, Mathf.Min(drawn, 6), 0.9f);
                }
            } else {
                if (ignited) {
                    Audio.Play(SoundCue.Release, 0, 0.35f);
                }
                ignited = false;
                completeFor = 0f;
            }
        }

        private bool AllScorable(int drawn) {
            int i = 0;
            while (i < drawn) {
                if (!Stage.Bodies[i].IsScorable) {
                    return false;
                }
                i = i + 1;
            }
            return drawn > 0;
        }

        // The honest warning. If the detector saw more people a moment ago than it does now, while
        // people are close enough to be linking, the most likely explanation is two boxes merging -
        // which is a detector limitation this scene sits directly on top of and has never been
        // measured against. It is a heuristic and it is labelled as one in the HUD.
        private void WatchForMerges(float dt, int drawn) {
            detectedPeakAge = detectedPeakAge + dt;
            if (Stage.DetectedCount >= detectedPeak || detectedPeakAge > 3f) {
                detectedPeak = Stage.DetectedCount;
                detectedPeakAge = 0f;
            }
        }

        private bool SuspectMerge {
            get {
                return Stage.HasCrowd && linkA.Count > 0 && detectedPeak > Stage.DetectedCount;
            }
        }

        private void Draw(IReadOnlyList<TrackedBody> bodies, int drawn) {
            float flare = ignited ? 1f : 0f;

            int i = 0;
            while (i < renderers.Length) {
                bool show = i < drawn;
                openEnds[i].gameObject.SetActive(show && !linked[i]);
                if (!show) {
                    renderers[i].Hide();
                    i = i + 1;
                    continue;
                }
                SkeletonPose pose = bodies[i].Pose;
                Color c = colours.ColourFor(bodies[i].Id);
                renderers[i].Dim = (linked[i] ? 0.9f : 0.5f) + 0.5f * flare;
                renderers[i].Render(pose, Color.Lerp(c, Color.white * 1.4f, flare * 0.6f));

                if (!linked[i]) {
                    // The free hand, offered. Whichever wrist is further from everybody else, so the
                    // light appears on the side somebody could actually reach.
                    Vector3 at = FreeHand(pose, bodies, drawn, i);
                    openEnds[i].position = at;
                    float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3f);
                    openEnds[i].localScale = Vector3.one * (0.10f + 0.04f * pulse);
                    SkeletonShowPalette.SetColour(openEndMaterials[i], c * (0.5f + 0.8f * pulse));
                }
                i = i + 1;
            }

            int link = 0;
            while (link < links.Length) {
                bool show = link < linkA.Count;
                links[link].enabled = show;
                int b = 0;
                while (b < BeadsPerLink) {
                    beads[link][b].gameObject.SetActive(show);
                    b = b + 1;
                }
                if (!show) {
                    link = link + 1;
                    continue;
                }

                Vector3 from = linkFrom[link];
                Vector3 to = linkTo[link];
                links[link].SetPosition(0, from);
                links[link].SetPosition(1, to);
                float width = 0.018f + 0.030f * flare;
                links[link].startWidth = width;
                links[link].endWidth = width;
                SkeletonShowPalette.SetColour(linkMaterials[link],
                    Color.Lerp(Palette.Warm, Palette.Hot, 0.35f + 0.65f * flare) * (1f + 1.2f * flare));

                // The current. Its phase is time, not identity, so a switch cannot disturb it.
                b = 0;
                while (b < BeadsPerLink) {
                    float t = Mathf.Repeat(phase + (b / (float)BeadsPerLink) + link * 0.17f, 1f);
                    beads[link][b].position = Vector3.Lerp(from, to, t);
                    float size = 0.055f + 0.05f * flare;
                    beads[link][b].localScale = Vector3.one * size;
                    SkeletonShowPalette.SetColour(beadMaterials[link][b],
                                                  Palette.Hot * (0.8f + 1.4f * flare));
                    b = b + 1;
                }
                link = link + 1;
            }
        }

        // The wrist furthest from everybody else's wrists - the hand that is actually free.
        private static Vector3 FreeHand(SkeletonPose pose, IReadOnlyList<TrackedBody> bodies,
                                        int drawn, int self) {
            int[] wrists = { 15, 16 };
            Vector3 best = pose.Centre;
            float bestScore = -1f;
            int w = 0;
            while (w < wrists.Length) {
                if (!pose.Present[wrists[w]]) {
                    w = w + 1;
                    continue;
                }
                Vector3 at = pose.World[wrists[w]];
                float nearest = float.MaxValue;
                int other = 0;
                while (other < drawn) {
                    if (other != self && bodies[other].Pose.Valid) {
                        nearest = Mathf.Min(nearest, Vector3.Distance(at, bodies[other].Pose.Centre));
                    }
                    other = other + 1;
                }
                float score = nearest == float.MaxValue ? 0f : nearest;
                if (bestScore < 0f || score > bestScore) {
                    bestScore = score;
                    best = at;
                }
                w = w + 1;
            }
            return best;
        }

        // Closest approach between any of A's wrists and any of B's, or -1 when neither has one
        // tracked. Same rule as Bonds, for the same reason.
        private static float NearestHandGap(SkeletonPose pa, SkeletonPose pb,
                                            out Vector3 fromA, out Vector3 fromB) {
            fromA = Vector3.zero;
            fromB = Vector3.zero;
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
                            fromA = pa.World[hands[i]];
                            fromB = pb.World[hands[j]];
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
                return "SINGLE-PERSON SENDER upstream - a chain needs at least two people.\n"
                       + "Run multiperson_udp_sender.py.";
            }
            if (Stage.Bodies.Count == 0) {
                return "Step in front of the camera, and bring somebody to hold hands with.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed   "
                          + linkA.Count + " link" + (linkA.Count == 1 ? "" : "s");
            if (ignited) {
                line = line + "\nCIRCUIT COMPLETE - held " + completeFor.ToString("F1") + " s"
                       + "      best " + longestCircuit.ToString("F1") + " s"
                       + "      circuits " + circuits;
            } else if (Stage.Bodies.Count < 2) {
                line = line + "\nBring somebody in. One person cannot be a chain.";
            } else {
                int loose = 0;
                int i = 0;
                while (i < Stage.Bodies.Count && i < MaxBodies) {
                    if (!linked[i]) {
                        loose = loose + 1;
                    }
                    i = i + 1;
                }
                line = line + "\n" + (loose == 0
                    ? "Everyone is linked but not in one chain yet - close the gap."
                    : loose + " still unconnected - reach for the glowing hand.");
            }
            if (SuspectMerge) {
                // The known, untested failure. Named as a detector limitation rather than left to
                // look like the chain losing people.
                line = line + "\nHEADS UP: the detector saw " + detectedPeak + " people a moment ago "
                       + "and sees " + Stage.DetectedCount + " now.\nStanding close enough to hold "
                       + "hands can merge two detections into one - a known, unmeasured limit (F-32).";
            }
            return line;
        }
    }
}
