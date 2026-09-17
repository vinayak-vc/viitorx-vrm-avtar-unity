using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 17 — PODIUM. One crown, and it goes to whoever is winning RIGHT NOW. Tallest,
    /// then stillest, then fastest, round and round.
    ///
    /// WHY THIS IS SAFE WHEN A SCOREBOARD IS NOT, and it is a single sentence: the crown is a function
    /// of the current frame's geometry, so it follows a BODY rather than a NAME. When the tracker
    /// swaps two identities — which it does silently, measured at 0.015 m per frame against a 0.35 m
    /// margin with nothing logged — the two labels move and the two bodies do not, so the crown stays
    /// exactly where it was, on the person who is actually tallest. Nobody watching can tell anything
    /// happened, because nothing visible did.
    ///
    /// LATCH IT AND IT BREAKS. The obvious next feature is "hold the crown for ten seconds to win",
    /// and that feature is what would ruin the scene: a hold timer is per-person accumulated state,
    /// the exact thing a silent switch corrupts, and the failure would be maximally visible — a
    /// person watching their timer transfer to somebody else mid-round. So there is deliberately no
    /// timer, no running total and no winner. The crown is a live readout, not a competition with a
    /// memory. This is the clearest example in the set of the rule that sorts every multi-person idea:
    /// read the current frame, never accumulate against an identity.
    ///
    /// THE THREE CATEGORIES ARE DELIBERATELY DIFFERENT IN KIND. Tallest is a fact about your body and
    /// how you are standing, which is measured against your OWN floor and so is honest for a child
    /// standing next to an adult on a step. Stillest and fastest are the two opposite things the rest
    /// of the show asks for, side by side, so a group learns in one minute that this room reads both.
    ///
    /// HEIGHT IS MEASURED AGAINST EACH PERSON'S OWN FLOOR, via
    /// <see cref="SkeletonPose.HeightAboveFloor"/>, which is the F-29 foot work doing the job it was
    /// measured for — the floor comes from the four foot CONTACT points, not the ankles, which sat
    /// 81–127 mm high. A shared floor would hand the crown to whoever happens to be standing on the
    /// higher part of an uneven room.
    /// </summary>
    public sealed class PodiumExperience : ExperienceBase {
        [Header("Podium")]
        [Tooltip("Seconds per category before it moves on. Long enough to work out what is being "
                 + "asked, short enough that nobody has to keep doing it.")]
        [SerializeField] private float secondsPerRound = 20f;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>Points in the crown ring. Enough to read as a circle from across a room.</summary>
        private const int CrownPoints = 9;

        /// <summary>A lead this small is not a lead. Below it the crown holds where it is rather than
        /// jittering between two people who are level, which is what tracker noise would otherwise do
        /// several times a second.</summary>
        private const float LeadMargin = 0.03f;

        private enum Category {
            Tallest,
            Stillest,
            Fastest,
        }

        private BodyRenderer[] renderers;
        private LineRenderer[] bars;
        private Material[] barMaterials;
        private Transform[] crown;
        private Material[] crownMaterials;
        private Transform beam;
        private Material beamMaterial;

        private readonly CrowdPalette colours = new CrowdPalette();
        private readonly List<float> values = new List<float>();

        private Category category = Category.Tallest;
        private float roundLeft;
        private int leader = -1;
        private int leaderId = int.MinValue;
        private float leaderValue;
        private float crownSpin;
        private float cueCooldown;

        public override string Title {
            get {
                return "PODIUM";
            }
        }

        public override string Instruction {
            get {
                return "One crown, live. Tallest, then stillest, then fastest. Take it off somebody.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      N next category";
        }

        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            bars = new LineRenderer[MaxBodies];
            barMaterials = new Material[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Hide();
                // A bar beside each person showing their value in the current category, normalised
                // against the leader. Without it the crown says who is winning but never says by what
                // or by how much, and a person who cannot see the measure cannot play with it.
                barMaterials[i] = Palette.NewAdditive(Palette.Cool);
                bars[i] = ShowStage.Line(root, "bar_" + i, 0.03f, barMaterials[i]);
                bars[i].enabled = false;
                i = i + 1;
            }

            crown = new Transform[CrownPoints];
            crownMaterials = new Material[CrownPoints];
            i = 0;
            while (i < CrownPoints) {
                crown[i] = ShowStage.Sphere(root, "crown_" + i, 0.07f, Palette, Palette.Hot, true,
                                            out crownMaterials[i]);
                crown[i].gameObject.SetActive(false);
                i = i + 1;
            }
            beam = ShowStage.Sphere(root, "beam", 1f, Palette, Palette.Hot, true, out beamMaterial);
            beam.gameObject.SetActive(false);

            roundLeft = secondsPerRound;
        }

        protected override void ResetExperience() {
            colours.Clear();
            category = Category.Tallest;
            roundLeft = secondsPerRound;
            leader = -1;
            leaderId = int.MinValue;
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.N)) {
                NextCategory();
            }
        }

        private void NextCategory() {
            category = (Category)(((int)category + 1) % 3);
            roundLeft = secondsPerRound;
            // The crown is re-derived next frame anyway; clearing the leader only stops a change cue
            // firing simply because the measure changed underneath it.
            leaderId = int.MinValue;
        }

        protected override void Play(float deltaSeconds) {
            cueCooldown = Mathf.Max(0f, cueCooldown - deltaSeconds);
            crownSpin = crownSpin + deltaSeconds * 0.9f;
            roundLeft = roundLeft - deltaSeconds;
            if (roundLeft <= 0f) {
                NextCategory();
            }

            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;
            int drawn = Mathf.Min(bodies.Count, MaxBodies);
            while (drawn > 0 && !bodies[drawn - 1].Pose.Valid) {
                drawn = drawn - 1;
            }

            Measure(bodies, drawn);
            Draw(bodies, drawn);
        }

        /// <summary>
        /// Everybody's value in the current category, and who leads. Pure function of this frame.
        /// </summary>
        private void Measure(IReadOnlyList<TrackedBody> bodies, int drawn) {
            values.Clear();
            int best = -1;
            float bestValue = 0f;

            int i = 0;
            while (i < drawn) {
                float v = ValueOf(bodies[i], category);
                values.Add(v);
                // Only a person actually SEEN this frame can hold the crown. A held pose is being
                // predicted, and a predicted body winning "stillest" would win it every time, because
                // a prediction that has stopped updating is perfectly still.
                if (bodies[i].IsScorable && ScoringAllowed && (best < 0 || v > bestValue)) {
                    best = i;
                    bestValue = v;
                }
                i = i + 1;
            }

            // Hold the crown where it is unless somebody has actually pulled ahead. Re-derived every
            // frame either way - this margin is about tracker noise, not about latching a winner.
            if (best >= 0 && leader >= 0 && leader < values.Count
                && bodies.Count > leader && bodies[leader].IsScorable
                && bestValue - values[leader] < LeadMargin) {
                best = leader;
                bestValue = values[leader];
            }

            leader = best;
            leaderValue = bestValue;

            int newId = best >= 0 ? bodies[best].Id : int.MinValue;
            if (newId != leaderId && newId != int.MinValue && leaderId != int.MinValue
                && cueCooldown <= 0f) {
                // The crown changed hands. This is the one place an id is compared, and the worst a
                // silent identity switch can do here is fire one chime that nothing depended on - no
                // total moves, because there are no totals.
                Audio.Play(SoundCue.Match, (int)category * 2, 0.7f);
                cueCooldown = 0.7f;
            }
            leaderId = newId;
        }

        private static float ValueOf(TrackedBody body, Category what) {
            SkeletonPose pose = body.Pose;
            if (!pose.Valid) {
                return 0f;
            }
            if (what == Category.Tallest) {
                // Head above THEIR OWN measured floor. Normalised by a 2.2 m ceiling purely so the
                // bars are comparable; the comparison itself is in metres.
                float head = pose.HasFloor && pose.Present[0] ? pose.HeightAboveFloor(0) : 0f;
                return Mathf.Clamp01(head / 2.2f);
            }
            if (what == Category.Stillest) {
                // 1 at a dead stop, 0 at four times the still threshold - the same 0.35 m/s that
                // decides whether a foot is planted, on the same scale. See ExperienceTuning.
                float moving = Mathf.Clamp01(pose.Energy
                                             / Mathf.Max(1e-3f, ExperienceTuning.StillEnergy * 4f));
                return 1f - moving;
            }
            return Mathf.Clamp01(pose.Energy);
        }

        private void Draw(IReadOnlyList<TrackedBody> bodies, int drawn) {
            float top = 0f;
            int i = 0;
            while (i < values.Count) {
                if (values[i] > top) {
                    top = values[i];
                }
                i = i + 1;
            }
            top = Mathf.Max(top, 0.05f);

            i = 0;
            while (i < renderers.Length) {
                bool show = i < drawn;
                bars[i].enabled = show;
                if (!show) {
                    renderers[i].Hide();
                    i = i + 1;
                    continue;
                }

                SkeletonPose pose = bodies[i].Pose;
                Color c = colours.ColourFor(bodies[i].Id);
                bool crowned = i == leader;
                float share = values[i] / top;

                renderers[i].Dim = (crowned ? 1f : 0.5f) * (bodies[i].IsScorable ? 1f : 0.6f);
                renderers[i].Render(pose, c);

                float floor = pose.HasFloor ? pose.FloorY : 0f;
                Vector3 chest = ChestOf(pose);
                // Beside the person, not through them: a bar drawn on the body hides the body, which
                // in two of the three categories is the thing being judged.
                float x = chest.x + 0.55f;
                bars[i].SetPosition(0, new Vector3(x, floor + 0.02f, chest.z));
                bars[i].SetPosition(1, new Vector3(x, floor + 0.05f + 1.8f * share, chest.z));
                float width = 0.02f + 0.03f * share;
                bars[i].startWidth = width;
                bars[i].endWidth = width;
                SkeletonShowPalette.SetColour(barMaterials[i],
                                              Color.Lerp(c * 0.35f, Palette.Hot, crowned ? 1f : share));
                i = i + 1;
            }

            DrawCrown(bodies, drawn);
        }

        private void DrawCrown(IReadOnlyList<TrackedBody> bodies, int drawn) {
            bool show = leader >= 0 && leader < drawn;
            beam.gameObject.SetActive(show);
            int i = 0;
            while (i < CrownPoints) {
                crown[i].gameObject.SetActive(show);
                i = i + 1;
            }
            if (!show) {
                return;
            }

            SkeletonPose pose = bodies[leader].Pose;
            Vector3 head = pose.Present[0] ? pose.World[0] : ChestOf(pose) + Vector3.up * 0.45f;
            float radius = 0.22f;
            i = 0;
            while (i < CrownPoints) {
                float angle = crownSpin + (Mathf.PI * 2f) * (i / (float)CrownPoints);
                // Alternating heights, so it reads as a crown rather than as a halo.
                float lift = 0.26f + ((i % 2 == 0) ? 0.07f : 0f);
                crown[i].position = head + new Vector3(Mathf.Cos(angle) * radius, lift,
                                                       Mathf.Sin(angle) * radius);
                crown[i].localScale = Vector3.one * (0.055f + (i % 2 == 0 ? 0.02f : 0f));
                SkeletonShowPalette.SetColour(crownMaterials[i], Palette.Hot * 1.3f);
                i = i + 1;
            }

            float floor = pose.HasFloor ? pose.FloorY : 0f;
            beam.position = new Vector3(head.x, floor + 0.005f, head.z);
            beam.localScale = new Vector3(0.9f, 0.008f, 0.9f);
            SkeletonShowPalette.SetColour(beamMaterial, Palette.Hot * 0.4f);
        }

        private static Vector3 ChestOf(SkeletonPose pose) {
            if (pose.Present[11] && pose.Present[12]) {
                return 0.5f * (pose.World[11] + pose.World[12]);
            }
            return pose.Centre;
        }

        private string CategoryName() {
            if (category == Category.Tallest) {
                return "TALLEST";
            }
            return category == Category.Stillest ? "STILLEST" : "FASTEST";
        }

        private string LeaderReading(IReadOnlyList<TrackedBody> bodies) {
            if (leader < 0 || leader >= bodies.Count) {
                return string.Empty;
            }
            SkeletonPose pose = bodies[leader].Pose;
            if (category == Category.Tallest) {
                return pose.HasFloor && pose.Present[0]
                    ? pose.HeightAboveFloor(0).ToString("F2") + " m to the head"
                    : "no floor measured yet";
            }
            return "energy " + pose.Energy.ToString("F2");
        }

        protected override string StatusText() {
            if (Stage.Bodies.Count == 0) {
                return "Step in front of the camera. The crown is yours by default.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed";
            line = line + "\nROUND: " + CategoryName()
                   + "      " + Mathf.CeilToInt(Mathf.Max(0f, roundLeft)) + " s left"
                   + (leader >= 0 ? "      leading with " + LeaderReading(Stage.Bodies) : string.Empty);
            if (!ScoringAllowed) {
                line = line + "\nNobody real is being judged - the crown waits for a tracked person.";
            } else if (Stage.Bodies.Count < 2) {
                line = line + "\nNobody to take it off you. Bring somebody in.";
            }
            // Worth saying out loud in the one scene where a visitor will look for a running score.
            line = line + "\nNothing is kept: the crown is live, so it always follows the body rather "
                   + "than the label.";
            return line;
        }
    }
}
