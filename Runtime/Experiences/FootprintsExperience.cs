using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 5 — FOOTPRINTS. Stand still and the floor starts to remember you. A mark
    /// grows under each planted foot, and the longer you stay the more it opens — a small bloom of
    /// petals that keeps growing while you are there and stays behind when you leave.
    ///
    /// WHY THIS IS THE ONE THAT CHANGES HOW A ROOM FEELS. Every other experience rewards MOVEMENT,
    /// which means the room is only interesting while somebody is performing in it. This one rewards
    /// PRESENCE — the opposite instinct — and it accumulates, so a room that has had people in it
    /// all day looks different from one that has just opened. A visitor who walks in sees the
    /// evidence of everyone before them, which is a thing a screen cannot usually say.
    ///
    /// IT IS BUILT ON <see cref="SkeletonPose.Planted"/>, which is the F-29 foot work doing the job
    /// it was measured for: a foot counts as planted when it is within 80 mm of the estimated floor
    /// AND moving slower than 0.35 m/s. The measured floor matters here more than anywhere else —
    /// marks are laid ON the floor plane, and before F-29 that plane was 81-127 mm out, so every
    /// mark would have floated or sunk.
    ///
    /// HONEST LIMIT, and it shapes the design: floor contact was measured reliable to ~21 mm on a
    /// single subject at 1.4 m, and to ~81 mm on the seven-person clip - where, per F-32, the error
    /// is the crop migrating between dancers rather than depth drift. Accuracy beyond ~2 m on ONE
    /// subject is untested. So a mark is placed once, where the foot FIRST settled, and then never
    /// moved: whichever of those two error sources is acting, a continuously tracked mark would
    /// wander across the floor, and a mark that wanders is worse than one placed slightly wrong.
    /// </summary>
    public sealed class FootprintsExperience : ExperienceBase {
        [Header("Footprints")]
        [Tooltip("Seconds of standing for a mark to reach full bloom.")]
        [SerializeField] private float secondsToFullBloom = 12f;
        [Tooltip("How many marks the floor remembers before the oldest fades away.")]
        [SerializeField] private int maxMarks = 48;

        /// <summary>A new mark is only started this far from every existing one, so shifting weight
        /// grows the mark you are already standing on instead of stamping a new one beside it.</summary>
        private const float NewMarkDistance = 0.22f;

        /// <summary>Petals per mark. The bloom opens one petal at a time, so a viewer can SEE the
        /// count going up and works out for themselves that standing longer is what does it.</summary>
        private const int Petals = 9;

        private BodyRenderer body;
        private Transform markRoot;
        private readonly List<Mark> marks = new List<Mark>();
        private float totalDwell;

        private sealed class Mark {
            public Vector3 Position;
            public Transform Root;
            public Transform Core;
            public Material CoreMaterial;
            public Transform[] PetalVisuals;
            public Material[] PetalMaterials;
            public float Dwell;       // seconds of standing accumulated here
            public float Age;         // seconds since it was last grown, for the slow fade
            public float Spin;
        }

        public override string Title {
            get {
                return "FOOTPRINTS";
            }
        }

        public override string Instruction {
            get {
                return "Stand still. The floor remembers how long you stayed.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      C clear the floor";
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root);
            body.Dim = 0.5f;
            markRoot = new GameObject("Marks").transform;
            markRoot.SetParent(root, false);
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.C)) {
                ClearMarks();
            }
        }

        // NOT cleared when a new person arrives, unlike every other experience here. The whole point
        // is that the floor accumulates across visitors - resetting per person would throw away the
        // only thing this experience is trying to build.
        protected override void ResetExperience() {
        }

        private void ClearMarks() {
            int i = 0;
            while (i < marks.Count) {
                if (marks[i].Root != null) {
                    Object.Destroy(marks[i].Root.gameObject);
                }
                i = i + 1;
            }
            marks.Clear();
            totalDwell = 0f;
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            SkeletonPose pose = Stage.Pose;

            // Only a real person leaves a trace. The attract figure is drawn standing on the floor,
            // and if it could leave marks an unattended machine would carpet the room overnight with
            // the footprints of somebody who was never there.
            if (ScoringAllowed && pose.HasFloor) {
                int i = 0;
                while (i < SkeletonPose.FootContacts.Length) {
                    if (pose.Planted[i]) {
                        int joint = SkeletonPose.FootContacts[i];
                        if (pose.Present[joint]) {
                            Vector3 at = pose.World[joint];
                            at.y = pose.FloorY;   // marks live ON the floor plane, not on the foot
                            Grow(at, deltaSeconds);
                        }
                    }
                    i = i + 1;
                }
            }

            Animate(deltaSeconds);
        }

        // Add dwell to the mark under this foot, or start one.
        private void Grow(Vector3 at, float dt) {
            Mark nearest = null;
            float best = NewMarkDistance;
            int i = 0;
            while (i < marks.Count) {
                float d = Vector3.Distance(marks[i].Position, at);
                if (d < best) {
                    best = d;
                    nearest = marks[i];
                }
                i = i + 1;
            }
            if (nearest == null) {
                nearest = Create(at);
            }
            int petalsBefore = Mathf.FloorToInt(Mathf.Clamp01(nearest.Dwell / Mathf.Max(0.5f, secondsToFullBloom)) * Petals + 0.001f);
            nearest.Dwell = nearest.Dwell + dt;
            nearest.Age = 0f;
            totalDwell = totalDwell + dt;
            int petalsAfter = Mathf.FloorToInt(Mathf.Clamp01(nearest.Dwell / Mathf.Max(0.5f, secondsToFullBloom)) * Petals + 0.001f);
            if (petalsAfter > petalsBefore) {
                // One note per petal, climbing. Standing still is otherwise a silent activity, and
                // this is the only feedback that rewards it without asking the person to look down.
                Audio.Play(SoundCue.Bloom, petalsAfter, 0.5f);
            }
        }

        private Mark Create(Vector3 at) {
            Mark m = new Mark();
            m.Position = at;
            m.Spin = Random.Range(0f, Mathf.PI * 2f);
            m.Root = new GameObject("mark").transform;
            m.Root.SetParent(markRoot, false);
            m.Root.position = at;

            m.Core = ShowStage.Sphere(m.Root, "core", 0.06f, Palette, Palette.Warm, true,
                                      out m.CoreMaterial);
            m.Core.localScale = new Vector3(0.06f, 0.012f, 0.06f);   // flattened onto the floor

            m.PetalVisuals = new Transform[Petals];
            m.PetalMaterials = new Material[Petals];
            int i = 0;
            while (i < Petals) {
                m.PetalVisuals[i] = ShowStage.Sphere(m.Root, "petal_" + i, 0.05f, Palette,
                                                     Palette.Cool, true, out m.PetalMaterials[i]);
                m.PetalVisuals[i].gameObject.SetActive(false);
                i = i + 1;
            }

            marks.Add(m);
            while (marks.Count > maxMarks) {
                if (marks[0].Root != null) {
                    Object.Destroy(marks[0].Root.gameObject);
                }
                marks.RemoveAt(0);
            }
            return m;
        }

        private void Animate(float dt) {
            int i = 0;
            while (i < marks.Count) {
                Mark m = marks[i];
                m.Age = m.Age + dt;
                m.Spin = m.Spin + dt * 0.35f;

                float bloom = Mathf.Clamp01(m.Dwell / Mathf.Max(0.5f, secondsToFullBloom));
                // Marks fade over ten minutes rather than persisting forever: an installation that
                // never forgets ends the day as an unreadable smear of overlapping blooms.
                float fade = Mathf.Clamp01(1f - (m.Age - 60f) / 600f);

                float coreSize = 0.06f + 0.10f * bloom;
                m.Core.localScale = new Vector3(coreSize, 0.012f, coreSize);
                SkeletonShowPalette.SetColour(m.CoreMaterial,
                    Color.Lerp(Palette.Warm, Palette.Hot, bloom) * fade);

                // Petals open one at a time as the dwell grows, so the mark counts the time visibly.
                int open = Mathf.FloorToInt(bloom * Petals + 0.001f);
                int p = 0;
                while (p < Petals) {
                    bool show = p < open;
                    m.PetalVisuals[p].gameObject.SetActive(show && fade > 0.02f);
                    if (show) {
                        float angle = m.Spin + (Mathf.PI * 2f) * (p / (float)Petals);
                        float radius = 0.10f + 0.16f * bloom;
                        m.PetalVisuals[p].position = m.Position
                            + new Vector3(Mathf.Cos(angle) * radius, 0.004f, Mathf.Sin(angle) * radius);
                        float petalSize = 0.035f + 0.03f * bloom;
                        m.PetalVisuals[p].localScale = new Vector3(petalSize, 0.010f, petalSize);
                        SkeletonShowPalette.SetColour(m.PetalMaterials[p],
                            Color.Lerp(Palette.Cool, Palette.Warm, p / (float)Petals) * fade);
                    }
                    p = p + 1;
                }

                if (fade <= 0.01f) {
                    Object.Destroy(m.Root.gameObject);
                    marks.RemoveAt(i);
                    continue;
                }
                i = i + 1;
            }
        }

        protected override string StatusText() {
            if (!ScoringAllowed) {
                return marks.Count > 0
                    ? "The floor remembers " + marks.Count + " place" + (marks.Count == 1 ? "" : "s")
                      + " somebody stood."
                    : "Step in front of the camera and stand still.";
            }
            int planted = 0;
            int i = 0;
            while (i < Stage.Pose.Planted.Length) {
                if (Stage.Pose.Planted[i]) {
                    planted = planted + 1;
                }
                i = i + 1;
            }
            string feet = planted > 0
                ? planted + " foot point" + (planted == 1 ? "" : "s") + " planted - growing"
                : "keep still to leave a mark";
            return feet
                   + "\nmarks " + marks.Count + " / " + maxMarks
                   + "      total time stood " + Mathf.FloorToInt(totalDwell) + " s";
        }
    }
}
