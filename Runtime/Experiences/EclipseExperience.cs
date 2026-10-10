using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 13 — ECLIPSE. There is one light, and it is where the camera is. Whoever
    /// stands in front of you takes your light. Step out from behind them and you come back.
    ///
    /// WHY THIS IS THE EXPERIENCE THAT ONLY THIS SYSTEM CAN RUN. Everything else in the set could be
    /// approximated on a webcam with a good 2D pose model. This one cannot, because it is built
    /// entirely on DEPTH ORDERING: the system knows, in metres, which of two people is physically
    /// nearer the camera. A flat camera can guess that from apparent size and be wrong about a tall
    /// person standing further back — and it is wrong exactly when two people line up, which is the
    /// only moment this scene cares about. So this is the one that demonstrates the stereo camera
    /// rather than the pose model.
    ///
    /// IT IS RECOMPUTED EVERY FRAME FROM z, SO LABELS ARE IRRELEVANT. Who is shading whom is a
    /// function of the current frame's geometry and nothing else — no id, no accumulator, no "who was
    /// in front last time". An identity switch, which nothing announces (measured live at 0.015 m per
    /// frame against a 0.35 m margin, zero events logged), swaps two labels attached to two positions
    /// and leaves every position where it was, so the picture does not change at all.
    ///
    /// IT USES RELATIVE DEPTH, NEVER ABSOLUTE, and that is deliberate. F-29 measured the floor to
    /// about 21 mm on a single subject at 1.4 m; accuracy beyond about 2 m on one subject has NOT
    /// been measured, so no scene here may claim an absolute distance. A comparison between two
    /// people cancels the common-mode part of the error — if both are 20 cm deep, the one in front is
    /// still in front — which is the same argument Bonds makes for pair distances and Pose Match
    /// makes for torso units.
    ///
    /// A FULLY ECLIPSED PERSON IS STILL DRAWN, at <see cref="MinLight"/>. Taking somebody off the
    /// screen because a friend stepped in front of them looks like the tracking failed, and the
    /// tracking has not failed — it is reporting exactly what it should. Dimming says "you are
    /// behind someone"; vanishing says "we lost you".
    ///
    /// WITHOUT A MEASURED MID-HIP there is no depth to order by: <see cref="SkeletonPose.CameraDepth"/>
    /// returns zero when the sender publishes no root position, and zero is the honest answer rather
    /// than a plausible guess. The scene then lights everybody and the HUD says why, instead of
    /// inventing an ordering out of the staging offsets.
    /// </summary>
    public sealed class EclipseExperience : ExperienceBase {
        [Header("Eclipse")]
        [Tooltip("How wide a person's shadow is, in metres either side of them. Roughly a shoulder "
                 + "width plus a margin, so standing directly behind somebody eclipses you and "
                 + "standing beside them does not.")]
        [SerializeField] private float shadeHalfWidth = 0.55f;
        [Tooltip("How much nearer somebody must be before they count as being in front, in metres. "
                 + "Below this the two are level and neither shades the other.")]
        [SerializeField] private float depthMargin = 0.18f;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>Light a fully eclipsed person still gets. Never zero — see the class note.</summary>
        private const float MinLight = 0.18f;

        /// <summary>Below this a person reads as eclipsed, for the HUD and the sound cue.</summary>
        private const float EclipsedBelow = 0.35f;

        private BodyRenderer[] renderers;
        private LineRenderer[] rays;
        private Material[] rayMaterials;
        private Transform[] shadows;
        private Material[] shadowMaterials;
        private Transform lamp;
        private Material lampMaterial;

        private readonly CrowdPalette colours = new CrowdPalette();
        private readonly List<float> depths = new List<float>();
        private readonly List<float> litness = new List<float>();
        private bool haveDepth;
        private int eclipsed;
        private int lastEclipsed = -1;
        private float cueCooldown;

        public override string Title {
            get {
                return "ECLIPSE";
            }
        }

        public override string Instruction {
            get {
                return "One light, at the camera. Step in front of someone and you take theirs.";
            }
        }

        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            rays = new LineRenderer[MaxBodies];
            rayMaterials = new Material[MaxBodies];
            shadows = new Transform[MaxBodies];
            shadowMaterials = new Material[MaxBodies];

            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Hide();

                rayMaterials[i] = Palette.NewAdditive(Palette.Warm);
                rays[i] = ShowStage.Line(root, "ray_" + i, 0.008f, rayMaterials[i]);
                rays[i].enabled = false;

                // A flattened disc BEHIND each person, away from the lamp. Without a cast shadow the
                // dimming has no visible cause, and a viewer reads it as the tracking fading rather
                // than as somebody blocking the light.
                shadows[i] = ShowStage.Sphere(root, "shade_" + i, 1f, Palette,
                                              new Color(0.02f, 0.06f, 0.18f, 1f), true,
                                              out shadowMaterials[i]);
                shadows[i].gameObject.SetActive(false);
                i = i + 1;
            }

            // The lamp sits where the camera is, slightly above it, so the geometry on screen matches
            // the geometry the depth measurement describes.
            lamp = ShowStage.Sphere(root, "lamp", 0.34f, Palette, Palette.Hot, true, out lampMaterial);
            lamp.position = new Vector3(0f, 1.9f, -3.1f);
        }

        protected override void ResetExperience() {
            colours.Clear();
            lastEclipsed = -1;
        }

        protected override void Play(float deltaSeconds) {
            cueCooldown = Mathf.Max(0f, cueCooldown - deltaSeconds);
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;

            int drawn = MeasureDepths(bodies);
            ComputeLight(bodies, drawn);
            Draw(bodies, drawn);

            // A room-level event rather than a per-person one: a COUNT is symmetric in the people it
            // counts, so it survives a switch that a per-person "you were eclipsed" cue would not.
            if (eclipsed != lastEclipsed && cueCooldown <= 0f && ScoringAllowed && drawn >= 2) {
                Audio.Play(eclipsed > lastEclipsed ? SoundCue.Depart : SoundCue.Arrive,
                           Mathf.Max(0, eclipsed), 0.5f);
                cueCooldown = 0.5f;
            }
            if (cueCooldown <= 0f || lastEclipsed < 0) {
                lastEclipsed = eclipsed;
            }

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.3f);
            SkeletonShowPalette.SetColour(lampMaterial, Palette.Hot * pulse);
        }

        // Real camera depth per person, from the two hip landmarks. -1 when this person has none.
        private int MeasureDepths(IReadOnlyList<TrackedBody> bodies) {
            depths.Clear();
            haveDepth = false;
            int drawn = 0;
            int i = 0;
            while (i < bodies.Count && i < MaxBodies) {
                SkeletonPose pose = bodies[i].Pose;
                if (!pose.Valid) {
                    break;
                }
                float left = pose.Present[23] ? pose.CameraDepth(23) : 0f;
                float right = pose.Present[24] ? pose.CameraDepth(24) : 0f;
                float depth = -1f;
                if (left > 0f && right > 0f) {
                    depth = 0.5f * (left + right);
                } else if (left > 0f) {
                    depth = left;
                } else if (right > 0f) {
                    depth = right;
                }
                if (depth > 0f) {
                    haveDepth = true;
                }
                depths.Add(depth);
                drawn = drawn + 1;
                i = i + 1;
            }
            return drawn;
        }

        /// <summary>
        /// Light each person receives, 1 (clear) down to <see cref="MinLight"/> (fully behind
        /// somebody). Pure function of this frame's positions and depths.
        /// </summary>
        private void ComputeLight(IReadOnlyList<TrackedBody> bodies, int drawn) {
            litness.Clear();
            eclipsed = 0;
            int i = 0;
            while (i < drawn) {
                float shade = 0f;
                if (haveDepth && depths[i] > 0f) {
                    Vector3 me = ChestOf(bodies[i].Pose);
                    int j = 0;
                    while (j < drawn) {
                        // Strictly nearer by more than the margin. Without the margin two people at
                        // the same depth trade the shadow back and forth every frame on tracker
                        // noise, which strobes.
                        if (j != i && depths[j] > 0f && depths[j] < depths[i] - depthMargin) {
                            Vector3 them = ChestOf(bodies[j].Pose);
                            float lateral = Mathf.Abs(me.x - them.x);
                            float overlap = 1f - Mathf.Clamp01(lateral / Mathf.Max(0.05f, shadeHalfWidth));
                            // Squared so the edge of a shadow is soft: a hard cutoff makes a person
                            // flick between lit and dark as they sidestep.
                            shade = shade + overlap * overlap;
                        }
                        j = j + 1;
                    }
                }
                float lit = Mathf.Lerp(1f, MinLight, Mathf.Clamp01(shade));
                litness.Add(lit);
                if (lit < EclipsedBelow) {
                    eclipsed = eclipsed + 1;
                }
                i = i + 1;
            }
        }

        private void Draw(IReadOnlyList<TrackedBody> bodies, int drawn) {
            int i = 0;
            while (i < renderers.Length) {
                bool show = i < drawn;
                rays[i].enabled = show;
                shadows[i].gameObject.SetActive(show);
                if (!show) {
                    renderers[i].Hide();
                    i = i + 1;
                    continue;
                }

                SkeletonPose pose = bodies[i].Pose;
                float lit = litness[i];
                Color c = colours.ColourFor(bodies[i].Id);

                renderers[i].Dim = lit * (bodies[i].IsScorable ? 1f : 0.55f);
                renderers[i].Render(pose, c);

                Vector3 chest = ChestOf(pose);
                rays[i].SetPosition(0, lamp.position);
                rays[i].SetPosition(1, chest);
                float width = 0.003f + 0.016f * lit;
                rays[i].startWidth = width;
                rays[i].endWidth = width;
                SkeletonShowPalette.SetColour(rayMaterials[i], Color.Lerp(Palette.Cool, c, lit) * lit);

                // The shadow is thrown AWAY from the lamp along the lamp-to-person direction, and it
                // is as strong as the light this person is blocking - which is 1 - (what they are
                // receiving), so somebody standing in a shadow casts almost none of their own.
                float floor = pose.HasFloor ? pose.FloorY : 0f;
                Vector3 away = chest - lamp.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) {
                    away = away.normalized;
                } else {
                    away = Vector3.forward;
                }
                float strength = lit;
                float size = 0.45f + 0.65f * strength;
                shadows[i].position = new Vector3(chest.x + away.x * 0.55f, floor + 0.004f,
                                                  chest.z + away.z * 0.55f);
                shadows[i].localScale = new Vector3(size, 0.008f, size * 1.5f);
                SkeletonShowPalette.SetColour(shadowMaterials[i],
                                              new Color(0.02f, 0.06f, 0.18f, 1f) * (0.2f + 0.9f * strength));
                i = i + 1;
            }
        }

        private static Vector3 ChestOf(SkeletonPose pose) {
            if (pose.Present[11] && pose.Present[12]) {
                return 0.5f * (pose.World[11] + pose.World[12]);
            }
            return pose.Centre;
        }

        protected override string StatusText() {
            if (Stage.Bodies.Count == 0) {
                return "Step in front of the camera. There is one light and it is behind it.";
            }
            if (!haveDepth) {
                // Said plainly. Depth ordering is the whole scene, and a sender that publishes no
                // measured mid-hip cannot provide it; inventing an order from the staging offsets
                // would look like it worked and be a fabrication.
                return "NO MEASURED DEPTH on the wire - nobody can be placed in front of anybody.\n"
                       + "Everyone is lit. The sender must publish a root position for this to work.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed   "
                          + eclipsed + " in shade";
            line = line + "\nfrom the camera: ";
            int i = 0;
            while (i < depths.Count) {
                if (depths[i] > 0f) {
                    line = line + depths[i].ToString("F2") + " m ("
                           + Mathf.RoundToInt(litness[i] * 100f) + "% lit)   ";
                }
                i = i + 1;
            }
            if (Stage.Bodies.Count < 2) {
                line = line + "\nNothing can eclipse you on your own - bring somebody in.";
            }
            return line;
        }
    }
}
