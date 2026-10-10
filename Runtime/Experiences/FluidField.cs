using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — THE VELOCITY FIELD, owned once so that one person and a crowd stir the SAME fluid.
    ///
    /// This is F-30's fluid, lifted out of <see cref="FluidFieldExperience"/> unchanged — same grid,
    /// same decay constant, same injection falloff, same bilinear sample — because the crowd version
    /// is not a different simulation, only a different set of things allowed to push it. Copying it
    /// would have meant two fluids that drift apart the first time either is tuned, and a viewer
    /// moving between the two scenes would feel the room behave differently for no reason.
    ///
    /// THE THREE THINGS A FLUID ACTUALLY NEEDS, and the one it does not:
    ///   * INJECTION — a moving joint pushes the cells near it, in the direction it is travelling
    ///   * ADVECTION — each particle is carried by the field it sits in
    ///   * DISSIPATION — the field decays, or one energetic person leaves the room spinning forever
    /// There is no pressure solve and no incompressibility. Deliberate: those are what make a fluid
    /// expensive, and what they buy is physical accuracy nobody watching can distinguish from the
    /// swirl this already produces.
    ///
    /// WHY IT IS SPLIT INTO <see cref="Allocate"/> AND <see cref="BuildVisuals"/>. Everything that
    /// decides how the fluid BEHAVES is plain arrays and Mathf, so it can be unit-tested with no
    /// Editor and no player; only the particle spheres need Unity objects. That split is what lets
    /// the one property the crowd scene depends on — that injection is symmetric in the people who
    /// supplied it — be asserted by a test rather than argued for in a comment.
    ///
    /// COST IS BOUNDED BY DESIGN. The grid is coarse (the field is smooth, so a fine grid stores
    /// detail no viewer can see) and injection only touches cells inside a joint's own radius rather
    /// than sweeping the whole grid per joint. Everything is preallocated; the per-frame loops
    /// allocate nothing, which matters far more with three people injecting than with one.
    /// </summary>
    public sealed class FluidField {
        /// <summary>Seconds for the field to lose most of its energy. Long enough that a swirl
        /// outlives the gesture that made it — which is the entire effect — and short enough that the
        /// room settles before the next visitor arrives.</summary>
        public const float DecayTau = 2.2f;

        /// <summary>A joint pushes cells within this radius. About an arm's width: smaller and the
        /// wake is a thin scratch, larger and the whole field moves as one block.</summary>
        public const float StirRadius = 0.45f;

        /// <summary>Below this a joint is effectively still, and injecting its jitter would make the
        /// field seethe around a motionless person.</summary>
        public const float MinStirSpeed = 0.12f;

        /// <summary>Joints that stir. Hands and feet do most of the work; the hips carry the body's
        /// bulk, so walking through the field displaces it as a whole rather than only where the
        /// limbs are.</summary>
        public static readonly int[] Stirrers = { 15, 16, 27, 28, 23, 24, 0 };

        private int gridSize;
        private float extent;
        private float stirStrength = 2.2f;

        private Vector2[] velocity;      // per cell, in world XZ
        private Vector3[] particlePos;
        private Vector3[] particleVel;
        private Transform[] particleVisuals;
        private Material[] particleMaterials;
        private Transform particleRoot;
        private SkeletonShowPalette palette;
        private float floorY;

        public int GridSize {
            get {
                return gridSize;
            }
        }

        public float Extent {
            get {
                return extent;
            }
        }

        public int ParticleCount {
            get {
                return particlePos == null ? 0 : particlePos.Length;
            }
        }

        /// <summary>How hard a moving body pushes the fluid.</summary>
        public float StirStrength {
            get {
                return stirStrength;
            }
            set {
                stirStrength = value;
            }
        }

        /// <summary>Mean cell speed, for a HUD. Cheap enough to call once a frame.</summary>
        public float MeanEnergy {
            get {
                if (velocity == null || velocity.Length == 0) {
                    return 0f;
                }
                float total = 0f;
                int i = 0;
                while (i < velocity.Length) {
                    total = total + velocity[i].magnitude;
                    i = i + 1;
                }
                return total / velocity.Length;
            }
        }

        /// <summary>Allocate the grid and the particle state. No Unity objects are created, so this
        /// is safe to call from a test.</summary>
        public void Allocate(int cells, float metresAcross, int particles) {
            gridSize = Mathf.Clamp(cells, 8, 64);
            extent = Mathf.Max(0.5f, metresAcross);
            velocity = new Vector2[gridSize * gridSize];
            int count = Mathf.Clamp(particles, 0, 4000);
            particlePos = new Vector3[count];
            particleVel = new Vector3[count];
            int i = 0;
            while (i < count) {
                Respawn(i);
                i = i + 1;
            }
        }

        /// <summary>Create the particle spheres. Call after <see cref="Allocate"/>.</summary>
        public void BuildVisuals(Transform parent, SkeletonShowPalette sharedPalette) {
            palette = sharedPalette;
            particleRoot = new GameObject("Fluid").transform;
            particleRoot.SetParent(parent, false);
            particleVisuals = new Transform[particlePos.Length];
            particleMaterials = new Material[particlePos.Length];
            int i = 0;
            while (i < particleVisuals.Length) {
                particleVisuals[i] = ShowStage.Sphere(particleRoot, "p" + i, 0.028f, palette,
                                                      palette.Cool, true, out particleMaterials[i]);
                i = i + 1;
            }
        }

        /// <summary>Still the field and scatter the particles again.</summary>
        public void Reset() {
            int i = 0;
            while (velocity != null && i < velocity.Length) {
                velocity[i] = Vector2.zero;
                i = i + 1;
            }
            i = 0;
            while (particlePos != null && i < particlePos.Length) {
                Respawn(i);
                particleVel[i] = Vector3.zero;
                i = i + 1;
            }
        }

        private void Respawn(int i) {
            float half = extent * 0.5f;
            particlePos[i] = new Vector3(Random.Range(-half, half),
                                         floorY + Random.Range(0.02f, 0.9f),
                                         Random.Range(-half, half));
        }

        /// <summary>
        /// Push the field where this body is moving, in the direction it is moving. Returns the total
        /// stirring speed contributed, so a caller can drive a HUD or the sound layer from it.
        ///
        /// THE PROPERTY THE WHOLE CROWD SCENE RESTS ON: this reads the pose and nothing else. No id,
        /// no index, no per-person accumulator. Calling it for A then B leaves the grid in exactly the
        /// state that calling it for B then A would, because each call is a `+=` into shared cells and
        /// addition does not care who went first. That is what makes a shared fluid structurally
        /// incapable of being corrupted by an identity switch — there is no identity in it to corrupt.
        /// </summary>
        public float Inject(SkeletonPose pose, float dt) {
            if (pose == null || !pose.Valid || velocity == null) {
                return 0f;
            }
            float half = extent * 0.5f;
            float cell = extent / gridSize;
            float total = 0f;

            int s = 0;
            while (s < Stirrers.Length) {
                int joint = Stirrers[s];
                if (!pose.Present[joint]) {
                    s = s + 1;
                    continue;
                }
                float speed = pose.Speed[joint];
                if (speed < MinStirSpeed) {
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
                // DIRECTION from Velocity, MAGNITUDE from Speed - see SkeletonPose.Velocity. The
                // vector partly cancels on a reversing sweep, which is exactly the vigorous stir a
                // person is trying to make.
                push = push.normalized * Mathf.Min(speed, 4f) * stirStrength;

                // Only the cells within this joint's radius, rather than the whole grid per joint.
                // With three people that is the difference between ~21 local stamps and three full
                // grid sweeps a frame.
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
            return total;
        }

        /// <summary>Decay the field. Pure; separated from <see cref="Step"/> so a test can run the
        /// simulation without any particles at all.</summary>
        public void Decay(float dt) {
            float keep = Mathf.Exp(-dt / DecayTau);
            int i = 0;
            while (velocity != null && i < velocity.Length) {
                velocity[i] = velocity[i] * keep;
                i = i + 1;
            }
        }

        /// <summary>Decay, advect and draw. The visuals are skipped when
        /// <see cref="BuildVisuals"/> was never called, which is what makes the whole class testable.</summary>
        public void Step(float dt, float floor) {
            floorY = floor;
            Decay(dt);
            Advect(dt);
        }

        private void Advect(float dt) {
            float half = extent * 0.5f;
            int i = 0;
            while (i < particlePos.Length) {
                Vector2 field = Sample(particlePos[i]);
                // Blend toward the field rather than snapping to it, so a particle has inertia and the
                // motion looks like a fluid carrying it instead of a lookup table moving dots.
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

                if (particleVisuals != null) {
                    particleVisuals[i].position = particlePos[i];
                    float speed = particleVel[i].magnitude;
                    float heat = Mathf.Clamp01(speed / 1.8f);
                    particleVisuals[i].localScale = Vector3.one * (0.022f + 0.030f * heat);
                    SkeletonShowPalette.SetColour(particleMaterials[i], palette.Heat(heat));
                }
                i = i + 1;
            }
        }

        /// <summary>Bilinear sample of the velocity field. Nearest-cell sampling makes particles jump
        /// between cells in visible rows, which reads as a grid rather than as a fluid.</summary>
        public Vector2 Sample(Vector3 world) {
            if (velocity == null) {
                return Vector2.zero;
            }
            float half = extent * 0.5f;
            float cell = extent / gridSize;
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

        /// <summary>The raw cell velocity, or zero outside the grid.</summary>
        public Vector2 At(int x, int z) {
            if (velocity == null || x < 0 || x >= gridSize || z < 0 || z >= gridSize) {
                return Vector2.zero;
            }
            return velocity[z * gridSize + x];
        }
    }
}
