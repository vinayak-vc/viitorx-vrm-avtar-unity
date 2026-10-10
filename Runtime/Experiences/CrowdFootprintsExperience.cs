using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 12 — TRACES. The floor remembers where EVERYBODY stood, all day. Stand
    /// together and your marks grow into each other.
    ///
    /// WHY THIS IS WORTH A SECOND FOOTPRINTS SCENE. The single-person version records one visitor at
    /// a time, so what builds up over a day is a sequence. This records a ROOM: four people standing
    /// in a loose circle leave a shape that says four people stood in a circle, and the next group to
    /// walk in can read it. That is the difference between a log and a trace.
    ///
    /// IDENTITY NEVER ENTERS, WHICH IS WHY IT IS SAFE. A mark is placed at a floor POSITION and never
    /// moved, and nothing about who was standing there is stored — no id, no colour, no owner. So the
    /// failure that ruins per-player state, an identity switch that nothing announces (measured live
    /// at 0.015 m per frame against a 0.35 m margin, zero events logged), has nothing here to
    /// corrupt. Two people trading identities changes nothing at all, because the marks were never
    /// attached to an identity.
    ///
    /// COLOUR DELIBERATELY MEANS DWELL, NOT PERSON. Colouring a mark by who made it would be easy and
    /// would look good, and it would be the one decision that put identity back into the floor: a
    /// switch would then repaint history, showing somebody standing where they never stood. Dwell is
    /// a property of the place. It is also the more useful reading — the hot marks are where people
    /// linger, which is the thing an installation's owner actually wants to see at the end of a day.
    ///
    /// EACH PERSON IS PLANTED AGAINST THEIR OWN MEASURED FLOOR, not a shared one. F-33 gives every
    /// tracked person their own filter chain and their own pose history, so
    /// <see cref="SkeletonPose.Planted"/> is computed per person from their own foot contacts. This
    /// is the one place where that per-person floor is visibly load-bearing: marks are laid ON the
    /// floor plane, and a single shared plane would sink one person's marks and float another's by
    /// the difference between where the two of them are standing.
    ///
    /// LOST PEOPLE DO NOT LEAVE MARKS. A person held through an occlusion is being predicted, and a
    /// predicted foot standing still is exactly the case that would lay a mark nobody made. The
    /// attract figure does not either, for the same reason the single-person scene excludes it: an
    /// unattended machine must not carpet the room overnight with the footprints of somebody who was
    /// never there.
    /// </summary>
    public sealed class CrowdFootprintsExperience : ExperienceBase {
        [Header("Traces")]
        [Tooltip("Seconds of standing for a mark to reach full bloom.")]
        [SerializeField] private float secondsToFullBloom = 12f;
        [Tooltip("How many marks the floor remembers before the oldest fades away. Higher than the "
                 + "single-person scene because several people fill a floor several times faster.")]
        [SerializeField] private int maxMarks = 120;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>A sound cue is only worth firing this often. Four people all opening petals at
        /// once would otherwise produce a continuous chime rather than an event.</summary>
        private const float CueCooldown = 0.25f;

        private BodyRenderer[] renderers;
        private readonly FloorMarks floor = new FloorMarks();
        private readonly CrowdPalette colours = new CrowdPalette();
        private float cueCooldown;
        private int plantedNow;
        private int standingNow;

        public override string Title {
            get {
                return "TRACES";
            }
        }

        public override string Instruction {
            get {
                return "Stand still, together. The floor remembers where all of you stood.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      C clear the floor";
        }

        // The floor is the point and it outlives everybody, so nothing here is ever reset by somebody
        // arriving. C is the only wipe, and it is deliberate and manual.
        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            floor.SecondsToFullBloom = secondsToFullBloom;
            floor.MaxMarks = maxMarks;
            floor.Build(root, Palette);

            renderers = new BodyRenderer[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                // Quiet: the floor is the subject. The bodies are only there so a person can see
                // which marks are theirs while they are making them.
                renderers[i].Dim = 0.45f;
                renderers[i].Hide();
                i = i + 1;
            }
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.C)) {
                floor.Clear();
            }
        }

        protected override void ResetExperience() {
        }

        protected override void Play(float deltaSeconds) {
            cueCooldown = Mathf.Max(0f, cueCooldown - deltaSeconds);
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;
            plantedNow = 0;
            standingNow = 0;

            int highestPetal = -1;
            int i = 0;
            while (i < renderers.Length) {
                if (i >= bodies.Count || !bodies[i].Pose.Valid) {
                    renderers[i].Hide();
                    i = i + 1;
                    continue;
                }

                SkeletonPose pose = bodies[i].Pose;
                renderers[i].Render(pose, colours.ColourFor(bodies[i].Id));

                // ONLY a person actually seen this frame, on a floor they have actually measured.
                // IsScorable is false while a person is being held through an occlusion, and during
                // attract - both of which would otherwise lay marks nobody made.
                if (!bodies[i].IsScorable || !pose.HasFloor || Stage.IsAttract) {
                    i = i + 1;
                    continue;
                }

                bool anyPlanted = false;
                int f = 0;
                while (f < SkeletonPose.FootContacts.Length) {
                    if (pose.Planted[f]) {
                        int joint = SkeletonPose.FootContacts[f];
                        if (pose.Present[joint]) {
                            plantedNow = plantedNow + 1;
                            anyPlanted = true;
                            Vector3 at = pose.World[joint];
                            at.y = pose.FloorY;   // ON the floor plane, not on the foot
                            int petal = floor.Grow(at, deltaSeconds);
                            if (petal > highestPetal) {
                                highestPetal = petal;
                            }
                        }
                    }
                    f = f + 1;
                }
                if (anyPlanted) {
                    standingNow = standingNow + 1;
                }
                i = i + 1;
            }

            // One note for the whole room per cue window, pitched at the highest petal opened. Four
            // people each triggering their own note turns a reward for stillness into a racket, which
            // is the opposite of what this scene is asking for.
            if (highestPetal >= 0 && cueCooldown <= 0f) {
                Audio.Play(SoundCue.Bloom, highestPetal, 0.5f);
                cueCooldown = CueCooldown;
            }

            floor.Animate(deltaSeconds);
        }

        protected override string StatusText() {
            if (Stage.Bodies.Count == 0) {
                return floor.Count > 0
                    ? "The floor remembers " + floor.Count + " place" + (floor.Count == 1 ? "" : "s")
                      + " somebody stood.\nStep in front of the camera and stand still."
                    : "Step in front of the camera and stand still.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed   "
                          + standingNow + " standing still";
            line = line + "\nmarks " + floor.Count + " / " + maxMarks
                   + "      " + plantedNow + " foot point" + (plantedNow == 1 ? "" : "s") + " planted"
                   + "      total time stood " + Mathf.FloorToInt(floor.TotalDwell) + " s";
            if (!Stage.HasCrowd) {
                line = line + "\nSINGLE-PERSON SENDER upstream - only one person can leave marks.";
            }
            return line;
        }
    }
}
