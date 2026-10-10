using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 3 — POSE MATCH. A ghost figure holds a pose. Match it before the bar runs
    /// out. Each of your joints turns green as it lands.
    ///
    /// WHY THIS ONE MATTERS MOST TO A SCEPTICAL VIEWER. Every other experience asks you to take the
    /// tracking on trust: a bubble pops, and you assume the system saw your hand. Here the target is
    /// on screen next to you and the viewer judges the match with their own eyes. If the tracking is
    /// wrong, it is obviously wrong — which is exactly why it is worth showing, and why it is the
    /// natural demo for a physiotherapy, fitness or training pitch.
    ///
    /// SCORING IS SCALE-NORMALISED, and this is the difference between a working demo and one that
    /// only works for the person who built it. Joint error is divided by the player's own torso
    /// length, so a tall adult and a child are judged on whether their SHAPE matches, not on how
    /// closely their absolute limb positions coincide with a fixed template. An absolute-metres
    /// threshold would make the game easy for one body and impossible for the next.
    ///
    /// The target poses are authored as normalised offsets from the hip, in torso units, for the
    /// same reason.
    /// </summary>
    public sealed class PoseMatchExperience : ExperienceBase {
        [Header("Pose Match")]
        [Tooltip("Seconds allowed per pose.")]
        [SerializeField] private float secondsPerPose = 12f;
        [Tooltip("Fraction of joints that must land before the pose counts as matched.")]
        [SerializeField] private float requiredFraction = 0.75f;

        /// <summary>A joint counts as landed within this many TORSO LENGTHS of its target. 0.28 of a
        /// torso is roughly 12 cm on an adult — tight enough that a wrong limb fails, loose enough
        /// that it does not demand millimetre accuracy the tracker has never claimed to have.</summary>
        private const float MatchTolerance = 0.28f;

        /// <summary>Joints that are scored. The head and the four limb ends plus elbows and knees:
        /// the joints a person can consciously place. Hips and shoulders are excluded because they
        /// are the frame the pose is measured against, so they would always score perfectly and
        /// inflate the result.</summary>
        private static readonly int[] Scored = { 0, 13, 14, 15, 16, 25, 26, 27, 28 };

        private BodyRenderer body;
        private Transform ghostRoot;
        private LineRenderer[] ghostBones;
        private Material[] ghostBoneMaterials;
        private Transform[] ghostJoints;
        private Material[] ghostJointMaterials;
        private Transform[] markers;
        private Material[] markerMaterials;

        private int poseIndex = -1;
        private float timeLeft;
        private int matched;
        private int attempted;
        private float holdTimer;
        private float lastScore;
        private bool celebrating;
        private float celebrateTimer;

        // Target poses in TORSO UNITS from the mid-hip: x right, y up, z forward (the player faces
        // -z). Only the scored joints plus the frame joints are given; everything else is drawn from
        // the player's own proportions so the ghost is always a plausible body.
        private static readonly PoseTemplate[] Templates = {
            new PoseTemplate("T-POSE", new[] {
                (13, new Vector3(-0.55f, 0.62f, 0f)), (15, new Vector3(-0.95f, 0.62f, 0f)),
                (14, new Vector3(0.55f, 0.62f, 0f)),  (16, new Vector3(0.95f, 0.62f, 0f)),
                (25, new Vector3(-0.20f, -0.55f, 0f)), (27, new Vector3(-0.20f, -1.10f, 0f)),
                (26, new Vector3(0.20f, -0.55f, 0f)),  (28, new Vector3(0.20f, -1.10f, 0f)),
                (0,  new Vector3(0f, 0.90f, 0f)),
            }),
            new PoseTemplate("HANDS UP", new[] {
                (13, new Vector3(-0.45f, 0.75f, 0f)), (15, new Vector3(-0.50f, 1.15f, 0f)),
                (14, new Vector3(0.45f, 0.75f, 0f)),  (16, new Vector3(0.50f, 1.15f, 0f)),
                (25, new Vector3(-0.20f, -0.55f, 0f)), (27, new Vector3(-0.20f, -1.10f, 0f)),
                (26, new Vector3(0.20f, -0.55f, 0f)),  (28, new Vector3(0.20f, -1.10f, 0f)),
                (0,  new Vector3(0f, 0.90f, 0f)),
            }),
            new PoseTemplate("STAR", new[] {
                (13, new Vector3(-0.60f, 0.72f, 0f)), (15, new Vector3(-0.95f, 1.05f, 0f)),
                (14, new Vector3(0.60f, 0.72f, 0f)),  (16, new Vector3(0.95f, 1.05f, 0f)),
                (25, new Vector3(-0.42f, -0.52f, 0f)), (27, new Vector3(-0.62f, -1.02f, 0f)),
                (26, new Vector3(0.42f, -0.52f, 0f)),  (28, new Vector3(0.62f, -1.02f, 0f)),
                (0,  new Vector3(0f, 0.90f, 0f)),
            }),
            new PoseTemplate("ONE ARM UP", new[] {
                (13, new Vector3(-0.38f, 0.20f, 0f)), (15, new Vector3(-0.42f, -0.18f, 0f)),
                (14, new Vector3(0.45f, 0.75f, 0f)),  (16, new Vector3(0.50f, 1.18f, 0f)),
                (25, new Vector3(-0.20f, -0.55f, 0f)), (27, new Vector3(-0.20f, -1.10f, 0f)),
                (26, new Vector3(0.20f, -0.55f, 0f)),  (28, new Vector3(0.20f, -1.10f, 0f)),
                (0,  new Vector3(0f, 0.90f, 0f)),
            }),
            new PoseTemplate("THINKER", new[] {
                (13, new Vector3(-0.40f, 0.30f, -0.20f)), (15, new Vector3(-0.18f, 0.78f, -0.35f)),
                (14, new Vector3(0.42f, 0.18f, 0f)),      (16, new Vector3(0.30f, -0.22f, -0.10f)),
                (25, new Vector3(-0.20f, -0.55f, 0f)), (27, new Vector3(-0.20f, -1.10f, 0f)),
                (26, new Vector3(0.20f, -0.55f, 0f)),  (28, new Vector3(0.20f, -1.10f, 0f)),
                (0,  new Vector3(0f, 0.88f, -0.05f)),
            }),
        };

        private sealed class PoseTemplate {
            public readonly string Name;
            public readonly (int joint, Vector3 offset)[] Joints;

            public PoseTemplate(string name, (int, Vector3)[] joints) {
                Name = name;
                Joints = joints;
            }
        }

        private readonly Vector3[] ghostWorld = new Vector3[PoseFrame.LandmarkCount];
        private readonly bool[] ghostHas = new bool[PoseFrame.LandmarkCount];

        public override string Title {
            get {
                return "POSE MATCH";
            }
        }

        public override string Instruction {
            get {
                return "Copy the ghost. Your joints turn green as they land.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      N next pose";
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root);
            body.Dim = 0.8f;

            ghostRoot = new GameObject("Ghost").transform;
            ghostRoot.SetParent(root, false);

            int boneCount = SkeletonPose.Bones.Length / 2;
            ghostBones = new LineRenderer[boneCount];
            ghostBoneMaterials = new Material[boneCount];
            int i = 0;
            while (i < boneCount) {
                ghostBoneMaterials[i] = Palette.NewAdditive(GhostColour);
                ghostBones[i] = ShowStage.Line(ghostRoot, "ghost_bone_" + i, 0.022f, ghostBoneMaterials[i]);
                i = i + 1;
            }
            ghostJoints = new Transform[PoseFrame.LandmarkCount];
            ghostJointMaterials = new Material[PoseFrame.LandmarkCount];
            i = 0;
            while (i < ghostJoints.Length) {
                ghostJoints[i] = ShowStage.Sphere(ghostRoot, "ghost_joint_" + i, 0.05f, Palette,
                                                  GhostColour, true, out ghostJointMaterials[i]);
                i = i + 1;
            }

            // A marker per SCORED joint, sitting on the player's own joint and turning green when it
            // lands. Feedback has to be on the PLAYER, not only on the target: a person looking at
            // their own arm needs to see it go green there.
            markers = new Transform[Scored.Length];
            markerMaterials = new Material[Scored.Length];
            i = 0;
            while (i < markers.Length) {
                markers[i] = ShowStage.Sphere(ghostRoot, "marker_" + i, 0.075f, Palette,
                                              MissColour, true, out markerMaterials[i]);
                i = i + 1;
            }
            ResetExperience();
        }

        private static readonly Color GhostColour = new Color(0.25f, 0.65f, 1.4f, 0.55f);
        private static readonly Color HitColour = new Color(0.25f, 2.1f, 0.6f, 1f);
        private static readonly Color MissColour = new Color(2.2f, 0.65f, 0.25f, 1f);

        protected override void ResetExperience() {
            matched = 0;
            attempted = 0;
            poseIndex = -1;
            NextPose();
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.N)) {
                NextPose();
            }
        }

        private void NextPose() {
            poseIndex = (poseIndex + 1) % Templates.Length;
            timeLeft = secondsPerPose;
            holdTimer = 0f;
            celebrating = false;
            celebrateTimer = 0f;
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            SkeletonPose pose = Stage.Pose;

            if (!pose.Valid) {
                ghostRoot.gameObject.SetActive(false);
                return;
            }
            ghostRoot.gameObject.SetActive(true);

            BuildGhost(pose);
            DrawGhost();

            // The attract figure is drawn a target and even shown matching it — an empty room
            // demonstrating the game teaches a passer-by what it is — but nothing is scored and the
            // clock does not run, because a synthetic body did not earn anything.
            if (!ScoringAllowed) {
                UpdateMarkers(pose, out _);
                return;
            }

            int landed;
            UpdateMarkers(pose, out landed);
            lastScore = Scored.Length > 0 ? (float)landed / Scored.Length : 0f;

            if (celebrating) {
                celebrateTimer = celebrateTimer - deltaSeconds;
                if (celebrateTimer <= 0f) {
                    NextPose();
                }
                return;
            }

            if (lastScore >= requiredFraction) {
                // Held, not merely touched. A pose that counts the instant a limb sweeps through the
                // right place rewards flailing; a short hold requires the person to actually arrive.
                holdTimer = holdTimer + deltaSeconds;
                if (holdTimer >= 0.4f) {
                    matched = matched + 1;
                    attempted = attempted + 1;
                    celebrating = true;
                    celebrateTimer = 1.2f;
                    Audio.Play(SoundCue.Match, 0, 0.8f);
                }
            } else {
                holdTimer = 0f;
            }

            timeLeft = timeLeft - deltaSeconds;
            if (timeLeft <= 0f) {
                attempted = attempted + 1;
                NextPose();
            }
        }

        // Place the target in the player's own proportions, next to them.
        private void BuildGhost(SkeletonPose pose) {
            float torso = TorsoLength(pose);
            Vector3 centre = pose.Centre;

            int i = 0;
            while (i < ghostHas.Length) {
                ghostHas[i] = false;
                i = i + 1;
            }

            // The frame joints come straight from the player, so the ghost has their build.
            Copy(pose, 11, centre, ref torso);
            Copy(pose, 12, centre, ref torso);
            Copy(pose, 23, centre, ref torso);
            Copy(pose, 24, centre, ref torso);

            PoseTemplate template = Templates[poseIndex];
            i = 0;
            while (i < template.Joints.Length) {
                int joint = template.Joints[i].joint;
                ghostWorld[joint] = centre + template.Joints[i].offset * torso;
                ghostHas[joint] = true;
                i = i + 1;
            }
            // Feet follow their ankles so the ghost is not standing on stumps.
            Derive(27, 29, new Vector3(0f, -0.06f, 0.05f), torso);
            Derive(27, 31, new Vector3(0f, -0.07f, -0.13f), torso);
            Derive(28, 30, new Vector3(0f, -0.06f, 0.05f), torso);
            Derive(28, 32, new Vector3(0f, -0.07f, -0.13f), torso);
        }

        private void Copy(SkeletonPose pose, int joint, Vector3 centre, ref float torso) {
            if (pose.Present[joint]) {
                ghostWorld[joint] = pose.World[joint];
                ghostHas[joint] = true;
            }
        }

        private void Derive(int from, int to, Vector3 offset, float torso) {
            if (ghostHas[from]) {
                ghostWorld[to] = ghostWorld[from] + offset * torso;
                ghostHas[to] = true;
            }
        }

        // Mid-hip to mid-shoulder. The one length every pose and tolerance here is measured in, so a
        // child and an adult are judged on shape rather than on absolute reach.
        private static float TorsoLength(SkeletonPose pose) {
            if (pose.Present[11] && pose.Present[12] && pose.Present[23] && pose.Present[24]) {
                Vector3 shoulder = 0.5f * (pose.World[11] + pose.World[12]);
                Vector3 hip = 0.5f * (pose.World[23] + pose.World[24]);
                float d = Vector3.Distance(shoulder, hip);
                if (d > 0.15f) {
                    return d;
                }
            }
            return 0.5f;   // a plausible adult torso, used only until the real one is measurable
        }

        private void DrawGhost() {
            int i = 0;
            while (i < ghostBones.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = ghostHas[a] && ghostHas[b];
                ghostBones[i].enabled = visible;
                if (visible) {
                    ghostBones[i].SetPosition(0, ghostWorld[a]);
                    ghostBones[i].SetPosition(1, ghostWorld[b]);
                    SkeletonShowPalette.SetColour(ghostBoneMaterials[i],
                                                  celebrating ? HitColour : GhostColour);
                }
                i = i + 1;
            }
            i = 0;
            while (i < ghostJoints.Length) {
                ghostJoints[i].gameObject.SetActive(ghostHas[i]);
                if (ghostHas[i]) {
                    ghostJoints[i].position = ghostWorld[i];
                    SkeletonShowPalette.SetColour(ghostJointMaterials[i],
                                                  celebrating ? HitColour : GhostColour);
                }
                i = i + 1;
            }
        }

        private void UpdateMarkers(SkeletonPose pose, out int landed) {
            float torso = TorsoLength(pose);
            float tolerance = MatchTolerance * torso;
            landed = 0;
            int i = 0;
            while (i < Scored.Length) {
                int joint = Scored[i];
                bool usable = pose.Present[joint] && ghostHas[joint];
                markers[i].gameObject.SetActive(usable);
                if (usable) {
                    float error = Vector3.Distance(pose.World[joint], ghostWorld[joint]);
                    bool hit = error <= tolerance;
                    if (hit) {
                        landed = landed + 1;
                    }
                    markers[i].position = pose.World[joint];
                    markers[i].localScale = Vector3.one * (hit ? 0.09f : 0.06f);
                    SkeletonShowPalette.SetColour(markerMaterials[i], hit ? HitColour : MissColour);
                }
                i = i + 1;
            }
        }

        protected override string StatusText() {
            if (poseIndex < 0) {
                return null;
            }
            string name = Templates[poseIndex].Name;
            if (!ScoringAllowed) {
                return "TARGET: " + name + "\nStep in front of the camera to play.";
            }
            if (celebrating) {
                return "TARGET: " + name + "\n\nMATCHED!      " + matched + " of " + attempted;
            }
            int percent = Mathf.RoundToInt(lastScore * 100f);
            string bar = new string('|', Mathf.RoundToInt(timeLeft * 2f));
            return "TARGET: " + name
                   + "\nmatch " + percent + "%   (need " + Mathf.RoundToInt(requiredFraction * 100f) + "%)"
                   + "      matched " + matched + " of " + attempted
                   + "\ntime " + bar;
        }
    }
}
