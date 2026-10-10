using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 6 — FLUID. You are standing in something. Move and it moves with you; stop
    /// and it keeps swirling for a while before settling.
    ///
    /// WHY IT READS AS IMMERSIVE WHERE A PARTICLE BURST DOES NOT. A burst is an EVENT: it fires and
    /// it is over, so the room only responds at the instants you trigger it. A fluid has MEMORY —
    /// the swirl you made two seconds ago is still there — so the space appears to have been changed
    /// by you rather than to be reacting to you. That difference is entirely the persistence of the
    /// velocity field, and it is why this is simulated rather than faked with trails.
    ///
    /// THE SIMULATION ITSELF LIVES IN <see cref="FluidField"/> as of F-34, unchanged — same grid,
    /// same decay, same injection falloff. It moved because the crowd scene stirs the SAME fluid with
    /// several people, and two copies of a fluid drift apart the first time either is tuned. This
    /// file is now only the single-person framing of it: one body stirs, and the HUD says so.
    /// </summary>
    public sealed class FluidFieldExperience : ExperienceBase {
        [Header("Fluid")]
        [Tooltip("Grid cells across the play area. Higher is finer and slower.")]
        [SerializeField] private int gridSize = 24;
        [Tooltip("Metres across the simulated area.")]
        [SerializeField] private float fieldExtent = 3.2f;
        [Tooltip("Particles carried by the field.")]
        [SerializeField] private int particleCount = 900;
        [Tooltip("How hard a moving body pushes the fluid.")]
        [SerializeField] private float stirStrength = 2.2f;

        private BodyRenderer body;
        private readonly FluidField field = new FluidField();
        private float floorY;
        private float energy;

        public override string Title {
            get {
                return "FLUID";
            }
        }

        public override string Instruction {
            get {
                return "Walk through it. Sweep your hands. It keeps moving after you stop.";
            }
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root);
            body.Dim = 0.7f;

            field.Allocate(gridSize, fieldExtent, particleCount);
            field.StirStrength = stirStrength;
            field.BuildVisuals(root, Palette);
        }

        protected override void ResetExperience() {
            field.Reset();
            energy = 0f;
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            float dt = Mathf.Min(deltaSeconds, 1f / 30f);   // a long frame must not blow the sim up

            if (Stage.Pose.HasFloor) {
                floorY = Stage.Pose.FloorY;
            }

            // The attract figure DOES stir the fluid, unlike every scoring experience where it is
            // excluded. Nothing is being scored here, and an empty room with a figure moving through
            // a living fluid is exactly the attract state this whole scene wants.
            float stirred = field.Inject(Stage.Pose, dt);
            energy = Mathf.Lerp(energy, Mathf.Clamp01(stirred / 6f), 1f - Mathf.Exp(-dt / 0.3f));
            field.Step(dt, floorY);
        }

        protected override string StatusText() {
            if (!Stage.Pose.Valid) {
                return "Step in front of the camera and move.";
            }
            return "stirring " + Mathf.RoundToInt(energy * 100f) + "%"
                   + "      field energy " + field.MeanEnergy.ToString("F2")
                   + "      " + field.ParticleCount + " particles on a "
                   + field.GridSize + "x" + field.GridSize + " grid";
        }
    }
}
