using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 10 — COLLECTIVE. One fluid, made by everybody at once, and you cannot tell
    /// which eddy is yours. Wakes merge, and they outlive both of the people who made them.
    ///
    /// WHY THIS IS THE FIRST CROWD EXPERIENCE TO BUILD AFTER BONDS. It is the only design in the set
    /// where more people makes the thing BETTER rather than more crowded. Every other multi-person
    /// idea divides a fixed screen between N players; a fluid is a single shared medium, and the
    /// third person's wake tangling into the first two is the entire point. A room of one is a
    /// person stirring water. A room of four is weather.
    ///
    /// IT IS STRUCTURALLY IMMUNE TO AN IDENTITY SWITCH, which is the property that sorts every
    /// multi-person idea into "build it" and "do not". <see cref="FluidField.Inject"/> reads a pose
    /// and nothing else — no id, no list index, no per-person accumulator — and writes `+=` into
    /// shared cells. Injecting A then B leaves the grid exactly as injecting B then A would. So when
    /// the tracker swaps two identities there is nothing to corrupt, because nothing here ever knew
    /// which body a push came from.
    ///
    /// THAT MATTERS BECAUSE A SWITCH IS NOT A JUMP YOU CAN CATCH. The live two-person session
    /// measured an identity migrating between two humans at 0.015 m per frame against a 0.35 m
    /// margin — 97 datagrams, zero events logged. There is no discontinuity to detect and no guard
    /// to write. An experience either survives a switch by construction or it does not survive at
    /// all; this one does not need to know a switch happened, which is a stronger position than
    /// detecting one.
    ///
    /// LOST PEOPLE STILL STIR IT. A person held through an occlusion is being predicted rather than
    /// seen, and in a scoring experience that would be dishonest. Nothing is scored here, and the
    /// alternative — a wake that cuts out every time somebody walks in front of somebody else —
    /// looks far more broken than a wake that carries on for half a second on a held pose.
    ///
    /// WITH A SINGLE-PERSON SENDER upstream this degrades to exactly <see cref="FluidFieldExperience"/>,
    /// because a crowd of one is a crowd. The HUD says which sender it is looking at rather than
    /// pretending the room is empty.
    /// </summary>
    public sealed class CollectiveFluidExperience : ExperienceBase {
        [Header("Collective Fluid")]
        [Tooltip("Grid cells across the play area. Higher is finer and slower.")]
        [SerializeField] private int gridSize = 28;
        [Tooltip("Metres across the simulated area. Wider than the single-person scene because a "
                 + "crowd spreads out - people stand side by side, not on one spot.")]
        [SerializeField] private float fieldExtent = 4.6f;
        [Tooltip("Particles carried by the field.")]
        [SerializeField] private int particleCount = 1100;
        [Tooltip("How hard a moving body pushes the fluid.")]
        [SerializeField] private float stirStrength = 2.2f;

        /// <summary>Rendering ceiling only. CrowdFrame caps the wire at 8; the sidecar's pose budget
        /// caps it far lower - 20.43 ms per person at p50, so three people land near 16 fps.</summary>
        private const int MaxBodies = 8;

        private readonly FluidField field = new FluidField();
        private BodyRenderer[] renderers;
        private readonly CrowdPalette colours = new CrowdPalette();
        private float floorY;
        private float energy;
        private int stirring;

        public override string Title {
            get {
                return "COLLECTIVE";
            }
        }

        public override string Instruction {
            get {
                return "Bring everyone. You are all stirring the same water - find someone else's wake.";
            }
        }

        // Nobody's progress exists to be wiped, and a fourth person walking in must not still the
        // room for the three already in it.
        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            field.Allocate(gridSize, fieldExtent, particleCount);
            field.StirStrength = stirStrength;
            field.BuildVisuals(root, Palette);

            renderers = new BodyRenderer[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                // Quieter than in Bonds. Here the fluid is the subject and the bodies are what is
                // stirring it; at full brightness eight skeletons drown the thing they are making.
                renderers[i].Dim = 0.55f;
                renderers[i].Hide();
                i = i + 1;
            }
        }

        protected override void ResetExperience() {
            field.Reset();
            colours.Clear();
            energy = 0f;
        }

        protected override void Play(float deltaSeconds) {
            float dt = Mathf.Min(deltaSeconds, 1f / 30f);   // a long frame must not blow the sim up
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;

            if (Stage.Pose.HasFloor) {
                floorY = Stage.Pose.FloorY;
            }

            float stirred = 0f;
            stirring = 0;
            int i = 0;
            while (i < renderers.Length) {
                if (i < bodies.Count && bodies[i].Pose.Valid) {
                    SkeletonPose pose = bodies[i].Pose;
                    // A LOST person is drawn dimmer rather than hidden - see the class note.
                    float dim = bodies[i].IsScorable ? 0.55f : 0.28f;
                    renderers[i].Dim = dim;
                    renderers[i].Render(pose, colours.ColourFor(bodies[i].Id));
                    float contribution = field.Inject(pose, dt);
                    if (contribution > 0f) {
                        stirring = stirring + 1;
                    }
                    stirred = stirred + contribution;
                } else {
                    renderers[i].Hide();
                }
                i = i + 1;
            }

            // Normalised by the number of people, not by a fixed constant: with three people in the
            // room the raw total is three times larger for the same amount of effort each, and a HUD
            // that pegs at 100% the moment a second person arrives says nothing.
            float perPerson = bodies.Count > 0 ? stirred / bodies.Count : 0f;
            energy = Mathf.Lerp(energy, Mathf.Clamp01(perPerson / 6f), 1f - Mathf.Exp(-dt / 0.3f));

            field.Step(dt, floorY);
        }

        protected override string StatusText() {
            if (!Stage.HasCrowd) {
                return "SINGLE-PERSON SENDER upstream - one person is stirring.\n"
                       + "Run multiperson_udp_sender.py and the whole room stirs the same water.";
            }
            if (Stage.Bodies.Count == 0) {
                return "Step in front of the camera and move. It keeps moving after you stop.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed   "
                          + stirring + " stirring";
            line = line + "\neffort " + Mathf.RoundToInt(energy * 100f) + "% each"
                   + "      field energy " + field.MeanEnergy.ToString("F2")
                   + "      " + field.ParticleCount + " particles on a "
                   + field.GridSize + "x" + field.GridSize + " grid";
            if (Stage.Bodies.Count == 1) {
                line = line + "\nBring someone else in - the wakes merge.";
            }
            return line;
        }
    }
}
