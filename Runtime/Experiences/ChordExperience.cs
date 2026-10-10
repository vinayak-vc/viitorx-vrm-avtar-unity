using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 14 — CHORD. Everyone in the room is a note. How far apart you stand is the
    /// interval between you, and how much you move is how loud you are. Walk and the chord reshapes.
    ///
    /// WHY THIS IS THE ONE PEOPLE UNDERSTAND WITHOUT BEING TOLD. The instruction is "walk around",
    /// and the feedback is instant, continuous and in a sense nobody has to look at — you hear the
    /// room change while you are still moving. Every other experience asks the visitor to watch a
    /// screen. This one works with your back turned, which in a real space means it works on the
    /// people queueing as well as the people playing.
    ///
    /// IT CANNOT PLAY A WRONG NOTE, and that is a structural guarantee rather than careful tuning.
    /// Every pitch is a degree of the pentatonic scale via <see cref="ExperienceAudio.PentatonicHz"/>
    /// — ADR-068 — on which no interval clashes with any other. So any number of people in any
    /// arrangement, standing anywhere, produces a consonant chord. That matters more here than
    /// anywhere else in the show: the trigger source is not a designed sequence, it is strangers
    /// wandering, and an hour of that on a chromatic scale is what makes staff mute an exhibit by
    /// lunchtime.
    ///
    /// THE CHORD IS SYMMETRIC IN THE PEOPLE. A person's degree is
    /// round((x − x_leftmost) / metresPerStep), so the SET of notes sounding is a function of the SET
    /// of positions and nothing else. Relabelling two people does not change it, because no label is
    /// read to compute it. An identity switch, which nothing announces (measured live at 0.015 m per
    /// frame against a 0.35 m margin, zero events logged), is therefore literally inaudible: the same
    /// chord comes out.
    ///
    /// A VOICE SLOT IS A RESOURCE AND FOLLOWS AN ID; THE NOTE IT PLAYS DOES NOT. A slot is taken on
    /// first sight and returned on departure through <see cref="CrowdRoster"/>, so a person's voice
    /// does not restart every time the crowd list re-sorts — that would click. Which physical source
    /// plays which pitch is unobservable: the mix is 2D, so nothing is located anywhere.
    ///
    /// DISTANCE IS RELATIVE, so it needs no calibration and no absolute depth accuracy. Two people
    /// 1.2 m apart read as 1.2 m apart even if both are 20 cm off in absolute depth, because the
    /// error is common-mode and cancels out of a difference — the same argument Bonds makes.
    /// </summary>
    public sealed class ChordExperience : ExperienceBase {
        [Header("Chord")]
        [Tooltip("Metres of separation per scale degree. Smaller means the chord changes more as "
                 + "people move; larger means a person has to walk to change their note.")]
        [SerializeField] private float metresPerStep = 0.55f;
        [Tooltip("Highest degree anyone can reach. Beyond this the scale is clamped rather than "
                 + "wrapping, so somebody at the far wall does not suddenly sound like the root.")]
        [SerializeField] private int maxStep = 9;
        [Tooltip("Whole-body energy at which a voice is at full volume.")]
        [SerializeField] private float loudEnergy = 0.30f;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = CrowdVoices.MaxVoices;

        /// <summary>Beads on each person's string. One per degree they are above the root, so the
        /// pitch is COUNTABLE on screen rather than only audible.</summary>
        private const int Beads = 10;

        /// <summary>A voice never falls completely silent while its person is standing there. A
        /// motionless person dropping out of the chord reads as the system losing them; a quiet held
        /// note reads as them being still, which is what is true.</summary>
        private const float FloorLevel = 0.12f;

        private BodyRenderer[] renderers;
        private LineRenderer[] strings;
        private Material[] stringMaterials;
        private Transform[][] beads;
        private Material[][] beadMaterials;
        private LineRenderer[] intervals;
        private Material[] intervalMaterials;

        private readonly CrowdVoices voices = new CrowdVoices();
        private readonly CrowdPalette colours = new CrowdPalette();
        private readonly Dictionary<int, int> slotOfId = new Dictionary<int, int>();
        private readonly Stack<int> freeSlots = new Stack<int>();
        private readonly List<int> steps = new List<int>();
        private readonly List<int> order = new List<int>();

        public override string Title {
            get {
                return "CHORD";
            }
        }

        public override string Instruction {
            get {
                return "You are a note. Walk apart to change the chord. Move to be heard.";
            }
        }

        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            voices.Build(root);

            renderers = new BodyRenderer[MaxBodies];
            strings = new LineRenderer[MaxBodies];
            stringMaterials = new Material[MaxBodies];
            beads = new Transform[MaxBodies][];
            beadMaterials = new Material[MaxBodies][];
            intervals = new LineRenderer[MaxBodies];
            intervalMaterials = new Material[MaxBodies];

            int i = MaxBodies - 1;
            while (i >= 0) {
                // Pushed in reverse so slot 0 is handed out first and a lone visitor always lands on
                // the same voice - which makes the scene reproducible when testing it alone.
                freeSlots.Push(i);
                i = i - 1;
            }

            i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Dim = 0.7f;
                renderers[i].Hide();

                stringMaterials[i] = Palette.NewAdditive(Palette.Cool);
                strings[i] = ShowStage.Line(root, "string_" + i, 0.012f, stringMaterials[i]);
                strings[i].enabled = false;

                beads[i] = new Transform[Beads];
                beadMaterials[i] = new Material[Beads];
                int b = 0;
                while (b < Beads) {
                    beads[i][b] = ShowStage.Sphere(root, "bead_" + i + "_" + b, 0.07f, Palette,
                                                   Palette.Warm, true, out beadMaterials[i][b]);
                    beads[i][b].gameObject.SetActive(false);
                    b = b + 1;
                }

                intervalMaterials[i] = Palette.NewAdditive(Palette.Warm);
                intervals[i] = ShowStage.Line(root, "interval_" + i, 0.008f, intervalMaterials[i]);
                intervals[i].enabled = false;
                i = i + 1;
            }
        }

        // Slots are taken LAZILY rather than on arrival, and that is not a style choice. R clears
        // the bank, and CrowdRoster only reports somebody as ARRIVING once - so anyone already
        // standing in front of the camera when the reset happened would never be handed a voice
        // again, and the chord would stay silent until they left and came back.
        private int SlotFor(int id) {
            int slot;
            if (slotOfId.TryGetValue(id, out slot)) {
                return slot;
            }
            if (freeSlots.Count == 0) {
                return -1;
            }
            slot = freeSlots.Pop();
            slotOfId[id] = slot;
            return slot;
        }

        protected override void OnPersonLeft(int id) {
            int slot;
            if (slotOfId.TryGetValue(id, out slot)) {
                voices.Silence(slot);
                slotOfId.Remove(id);
                freeSlots.Push(slot);
            }
        }

        protected override void ResetExperience() {
            colours.Clear();
            voices.SilenceAll();
            slotOfId.Clear();
            freeSlots.Clear();
            int i = MaxBodies - 1;
            while (i >= 0) {
                freeSlots.Push(i);
                i = i - 1;
            }
        }

        protected override void Play(float deltaSeconds) {
            // The shared mute key has to reach the voice bank too, or M silences the drone and leaves
            // eight pads running.
            voices.Enabled = Audio.Enabled;
            voices.Volume = Audio.Volume;

            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;
            int drawn = Mathf.Min(bodies.Count, MaxBodies);
            while (drawn > 0 && !bodies[drawn - 1].Pose.Valid) {
                drawn = drawn - 1;
            }

            ComputeChord(bodies, drawn);
            DrawAndSound(bodies, drawn);
            voices.Tick(deltaSeconds);
        }

        /// <summary>
        /// Everybody's degree, from the leftmost person outward. Pure function of this frame's
        /// positions — see the class note for why that is the whole safety argument.
        /// </summary>
        private void ComputeChord(IReadOnlyList<TrackedBody> bodies, int drawn) {
            steps.Clear();
            order.Clear();
            if (drawn == 0) {
                return;
            }

            float leftmost = float.MaxValue;
            int i = 0;
            while (i < drawn) {
                float x = ChestOf(bodies[i].Pose).x;
                if (x < leftmost) {
                    leftmost = x;
                }
                i = i + 1;
            }

            i = 0;
            while (i < drawn) {
                float gap = ChestOf(bodies[i].Pose).x - leftmost;
                int step = Mathf.Clamp(Mathf.RoundToInt(gap / Mathf.Max(0.05f, metresPerStep)),
                                       0, Mathf.Max(1, maxStep));
                steps.Add(step);
                order.Add(i);
                i = i + 1;
            }

            // Left-to-right order, for the interval arcs only. An insertion sort: at most eight
            // entries, already nearly sorted most frames, and it allocates nothing.
            i = 1;
            while (i < order.Count) {
                int key = order[i];
                float keyX = ChestOf(bodies[key].Pose).x;
                int j = i - 1;
                while (j >= 0 && ChestOf(bodies[order[j]].Pose).x > keyX) {
                    order[j + 1] = order[j];
                    j = j - 1;
                }
                order[j + 1] = key;
                i = i + 1;
            }
        }

        private void DrawAndSound(IReadOnlyList<TrackedBody> bodies, int drawn) {
            int i = 0;
            while (i < renderers.Length) {
                if (i >= drawn) {
                    renderers[i].Hide();
                    strings[i].enabled = false;
                    intervals[i].enabled = false;
                    int hidden = 0;
                    while (hidden < Beads) {
                        beads[i][hidden].gameObject.SetActive(false);
                        hidden = hidden + 1;
                    }
                    i = i + 1;
                    continue;
                }

                SkeletonPose pose = bodies[i].Pose;
                Color c = colours.ColourFor(bodies[i].Id);
                int step = steps[i];
                float loud = Mathf.Clamp01(pose.Energy / Mathf.Max(0.01f, loudEnergy));
                float level = FloorLevel + (1f - FloorLevel) * loud;

                renderers[i].Dim = 0.45f + 0.5f * loud;
                renderers[i].Render(pose, c);

                // A LOST person keeps their note at the floor level rather than dropping out: an
                // occlusion should not punch a hole in the chord.
                if (!bodies[i].IsScorable) {
                    level = FloorLevel;
                }

                int slot = SlotFor(bodies[i].Id);
                if (slot >= 0) {
                    voices.SetVoice(slot, step, level);
                }

                DrawString(i, pose, c, step, loud);
                i = i + 1;
            }

            DrawIntervals(bodies, drawn);
        }

        private void DrawString(int index, SkeletonPose pose, Color c, int step, float loud) {
            Vector3 chest = ChestOf(pose);
            float floor = pose.HasFloor ? pose.FloorY : 0f;
            // Height IS the pitch, so a viewer can see the chord as a shape before they work out that
            // it is also audible. Beads count the degree, so it is readable rather than approximate.
            float top = floor + 0.35f + 0.26f * step;
            strings[index].enabled = true;
            strings[index].SetPosition(0, new Vector3(chest.x, floor + 0.02f, chest.z));
            strings[index].SetPosition(1, new Vector3(chest.x, top, chest.z));
            float width = 0.005f + 0.022f * loud;
            strings[index].startWidth = width;
            strings[index].endWidth = width;
            SkeletonShowPalette.SetColour(stringMaterials[index], c * (0.2f + 1.3f * loud));

            int b = 0;
            while (b < Beads) {
                bool show = b <= step && b < Beads;
                beads[index][b].gameObject.SetActive(show);
                if (show) {
                    float y = floor + 0.35f + 0.26f * b;
                    beads[index][b].position = new Vector3(chest.x, y, chest.z);
                    float size = 0.05f + 0.035f * loud;
                    beads[index][b].localScale = Vector3.one * size;
                    SkeletonShowPalette.SetColour(beadMaterials[index][b],
                                                  Color.Lerp(c, Palette.Hot, b / (float)Beads)
                                                  * (0.35f + 0.9f * loud));
                }
                b = b + 1;
            }
        }

        // One arc per ADJACENT pair, left to right. Arcs between every pair would be N*(N-1)/2 lines
        // saying the same thing; the neighbours are what a person can actually act on by stepping.
        private void DrawIntervals(IReadOnlyList<TrackedBody> bodies, int drawn) {
            int drawnArcs = 0;
            int k = 0;
            while (k + 1 < order.Count && drawnArcs < intervals.Length) {
                int a = order[k];
                int b = order[k + 1];
                Vector3 pa = ChestOf(bodies[a].Pose);
                Vector3 pb = ChestOf(bodies[b].Pose);
                int degrees = Mathf.Abs(steps[b] - steps[a]);
                intervals[drawnArcs].enabled = true;
                intervals[drawnArcs].SetPosition(0, pa);
                intervals[drawnArcs].SetPosition(1, pb);
                float width = 0.005f + 0.006f * degrees;
                intervals[drawnArcs].startWidth = width;
                intervals[drawnArcs].endWidth = width;
                SkeletonShowPalette.SetColour(intervalMaterials[drawnArcs],
                                              Palette.Heat(Mathf.Clamp01(degrees / 6f)) * 0.7f);
                drawnArcs = drawnArcs + 1;
                k = k + 1;
            }
            while (drawnArcs < intervals.Length) {
                intervals[drawnArcs].enabled = false;
                drawnArcs = drawnArcs + 1;
            }
        }

        private static Vector3 ChestOf(SkeletonPose pose) {
            if (pose.Present[11] && pose.Present[12]) {
                return 0.5f * (pose.World[11] + pose.World[12]);
            }
            return pose.Centre;
        }

        protected override string StatusText() {
            if (steps.Count == 0) {
                return "Step in front of the camera. You are the root note.";
            }
            string chord = string.Empty;
            int k = 0;
            while (k < order.Count) {
                int step = steps[order[k]];
                chord = chord + (step == 0 ? "root" : "+" + step) + "  ";
                k = k + 1;
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed";
            line = line + "\nchord   " + chord.Trim()
                   + "      one degree every " + metresPerStep.ToString("F2") + " m";
            if (steps.Count == 1) {
                line = line + "\nA root on its own is not a chord. Bring somebody in.";
            }
            if (!Audio.Enabled) {
                line = line + "\nSOUND IS MUTED - press M. This scene is mostly the sound.";
            }
            return line;
        }
    }
}
