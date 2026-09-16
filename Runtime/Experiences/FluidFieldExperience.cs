using UnityEngine;

using VirtualMirror.Core;
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
    /// velocity field below, and it is why this is simulated rather than faked with trails.
    ///
    /// IT IS A REAL VELOCITY FIELD, on a coarse grid, with the three things a fluid actually needs:
    ///   * INJECTION - a moving joint pushes the cells near it, in the direction it is travelling
    ///   * ADVECTION - each particle is carried by the field it sits in
    ///   * DISSIPATION - the field decays, or one energetic person leaves the room spinning forever
    /// There is no pressure solve and no incompressibility. That is a deliberate omission: those are
    /// what make a fluid expensive, and what they buy is physical accuracy nobody watching can
    /// distinguish from the swirl this already produces.
    ///
    /// COST IS BOUNDED BY DESIGN. The grid is coarse (the field is smooth, so a fine grid stores
    /// detail no viewer can see) and injection only touches cells within a joint's own radius rather
    /// than sweeping the whole grid per joint. Everything is preallocated; the per-frame loops
    /// allocate nothing.
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

        /// <summary>Seconds for the field to lose most of its energy. Long enough that a swirl
        /// outlives the gesture that made it — which is the entire effect — and short enough that
        /// the room settles before the next visitor arrives.</summary>
        private const float FieldDecayTau = 2.2f;

        /// <summary>Joints that stir. Hands and feet do most of the work; the hips carry the body's
        /// bulk, so walking through the field displaces it as a whole rather than only where the
        /// limbs are.</summary>
        private static readonly int[] Stirrers = { 15, 16, 27, 28, 23, 24, 0 };

        /// <summary>A joint pushes cells within this radius. About an arm's width: smaller and the
        /// wake is a thin scratch, larger and the whole field moves as one block.</summary>
        private const float StirRadius = 0.45f;

        private BodyRenderer body;
        private Transform particleRoot;

        private Vector2[] velocity;      // per cell, in world XZ
        private Vector3[] particlePos;
        private Vector3[] particleVel;
        private Transform[] particleVisuals;
        private Material[] particleMaterials;
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

            gridSize = Mathf.Clamp(gridSize, 8, 64);
            velocity = new Vector2[gridSize * gridSize];

            particleRoot = new GameObject("Fluid").transform;
            particleRoot.SetParent(root, false);
            particleCount = Mathf.Clamp(particleCount, 50, 4000);
            particlePos = new Vector3[particleCount];
            particleVel = new Vector3[particleCount];
            particleVisuals = new Transform[particleCount];
            particleMaterials = new Material[particleCount];

            int i = 0;
            while (i < particleCount) {
                particleVisuals[i] = ShowStage.Sphere(particleRoot, "p" + i, 0.028f, Palette,
                                                      Palette.Cool, true, out particleMaterials[i]);
                Respawn(i);
                i = i + 1;
            }
        }

        protected override void ResetExperience() {
            int i = 0;
            while (i < velocity.Length) {
                velocity[i] = Vector2.zero;
                i = i + 1;
            }
            i = 0;
            while (i < particleCount) {
                Respawn(i);
                particleVel[i] = Vector3.zero;
                i = i + 1;
            }
            energy = 0f;
        }

        private void Respawn(int i) {
            float half = fieldExtent * 0.5f;
            particlePos[i] = new Vector3(Random.Range(-half, half),
                                         floorY + Random.Range(0.02f, 0.9f),
                                         Random.Range(-half, half));
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
            if (Stage.Pose.Valid) {
                Inject(dt);
            }
            Decay(dt);
            Advect(dt);
        }

        // Push the field where the body is moving, in the direction it is moving.
        private void Inject(float dt) {
            SkeletonPose pose = Stage.Pose;
            float half = fieldExtent * 0.5f;
            float cell = fieldExtent / gridSize;
            float total = 0f;

            int s = 0;
            while (s < Stirrers.Length) {
                int joint = Stirrers[s];
                if (!pose.Present[joint]) {
                    s = s + 1;
                    continue;
                }
                float speed = pose.Speed[joint];
                if (speed < 0.12f) {
                    // Below this a joint is effectively still, and injecting its jitter would make
                    // the field seethe around a motionless person.
                    s = s + 1;
                    continue;
                }
                total = total + speed;

                Vector3 p = pose.World[joint];
                Vector3 dir = pose.Velocity[joint];
                Vector2 push = new Vector2(dir.x, dir.z);
                if (push.sqrMagnitude < 1e-6f) {
                    s = s + 1;
                    continue;
                }
                push = push.normalized * Mathf.Min(speed, 4f) * stirStrength;

                // Only the cells within this joint's radius, rather than the whole grid per joint.
                int radiusCells = Mathf.CeilToInt(StirRadius / cell);
                int cx = Mathf.RoundToInt((p.x + half) / cell);
                int cz = Mathf.RoundToInt((p.z + half) / cell);
                int gz = cz - radiusCells;
                while (gz <= cz + radiusCells) {
                    int gx = cx - radiusCells;
                    while (gx <= cx + radiusCells) {
                        if (gx >= 0 && gx < gridSize && gz >= 0 && gz < gridSize) {
                            float wx = -half + (gx + 0.5f) * cell;
                            float wz = -half + (gz + 0.5f) * cell;
                            float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(p.x, p.z));
                            if (d < StirRadius) {
                                // Smooth falloff so the wake has a soft edge; a hard cutoff shows the
                                // grid as a visible square around every hand.
                                float w = 1f - (d / StirRadius);
                                velocity[gz * gridSize + gx] += push * (w * w * dt * 6f);
                            }
                        }
                        gx = gx + 1;
                    }
                    gz = gz + 1;
                }
                s = s + 1;
            }
            energy = Mathf.Lerp(energy, Mathf.Clamp01(total / 6f), 1f - Mathf.Exp(-dt / 0.3f));
        }

        private void Decay(float dt) {
            float keep = Mathf.Exp(-dt / FieldDecayTau);
            int i = 0;
            while (i < velocity.Length) {
                velocity[i] = velocity[i] * keep;
                i = i + 1;
            }
        }

        private void Advect(float dt) {
            float half = fieldExtent * 0.5f;
            int i = 0;
            while (i < particleCount) {
                Vector2 field = Sample(particlePos[i]);
                // Blend toward the field rather than snapping to it, so a particle has inertia and
                // the motion looks like a fluid carrying it instead of a lookup table moving dots.
                particleVel[i] = Vector3.Lerp(particleVel[i], new Vector3(field.x, 0f, field.y),
                                              1f - Mathf.Exp(-dt / 0.25f));
                particlePos[i] = particlePos[i] + particleVel[i] * dt;

                // Settle gently toward the floor so the field does not end up a flat sheet.
                float rest = floorY + 0.06f;
                particlePos[i].y = Mathf.Lerp(particlePos[i].y, rest, 1f - Mathf.Exp(-dt / 2.5f));

                if (Mathf.Abs(particlePos[i].x) > half || Mathf.Abs(particlePos[i].z) > half) {
                    // Swept out of the area: reappear on the far side so the field never drains.
                    particlePos[i].x = Mathf.Clamp(-particlePos[i].x, -half, half);
                    particlePos[i].z = Mathf.Clamp(-particlePos[i].z, -half, half);
                    particleVel[i] = Vector3.zero;
                }

                particleVisuals[i].position = particlePos[i];
                float speed = particleVel[i].magnitude;
                float heat = Mathf.Clamp01(speed / 1.8f);
                particleVisuals[i].localScale = Vector3.one * (0.022f + 0.030f * heat);
                SkeletonShowPalette.SetColour(particleMaterials[i], Palette.Heat(heat));
                i = i + 1;
            }
        }

        // Bilinear sample of the velocity field. Nearest-cell sampling makes particles jump between
        // cells in visible rows, which reads as a grid rather than as a fluid.
        private Vector2 Sample(Vector3 world) {
            float half = fieldExtent * 0.5f;
            float cell = fieldExtent / gridSize;
            float fx = (world.x + half) / cell - 0.5f;
            float fz = (world.z + half) / cell - 0.5f;
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            Vector2 v00 = At(x0, z0);
            Vector2 v10 = At(x0 + 1, z0);
            Vector2 v01 = At(x0, z0 + 1);
            Vector2 v11 = At(x0 + 1, z0 + 1);
            return Vector2.Lerp(Vector2.Lerp(v00, v10, tx), Vector2.Lerp(v01, v11, tx), tz);
        }

        private Vector2 At(int x, int z) {
            if (x < 0 || x >= gridSize || z < 0 || z >= gridSize) {
                return Vector2.zero;
            }
            return velocity[z * gridSize + x];
        }

        protected override string StatusText() {
            if (!Stage.Pose.Valid) {
                return "Step in front of the camera and move.";
            }
            float total = 0f;
            int i = 0;
            while (i < velocity.Length) {
                total = total + velocity[i].magnitude;
                i = i + 1;
            }
            return "stirring " + Mathf.RoundToInt(energy * 100f) + "%"
                   + "      field energy " + (total / velocity.Length).ToString("F2")
                   + "      " + particleCount + " particles on a "
                   + gridSize + "x" + gridSize + " grid";
        }
    }
}
