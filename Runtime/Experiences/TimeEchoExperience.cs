using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-31 EXPERIENCE 8 — TIME ECHO. You, a few seconds ago, standing behind you. Raise an arm and a
    /// wave of arms rises after it. Walk across the room and a procession follows.
    ///
    /// WHY THIS ONE IS WORTH MORE THAN ITS SIZE. The hardest limitation in this whole project is that
    /// RTMW3D-x is a single-person top-down model with no detector and no track id — F-21's live
    /// two-person acceptance FAILED, and multi-person is a model change, not a scene change. This
    /// makes a crowd out of one person without touching any of that. It is the only idea here that
    /// turns the constraint into the subject.
    ///
    /// AND IT CANNOT BE WRONG IN A NEW WAY. Everything on screen is a replay of pose data that has
    /// already been validated — no new sensing, no new thresholds, no new failure mode. It uses body
    /// joints only, so it works at the distance where the hands are already known to be unreliable
    /// (59.9% plausible on the crowded clip) and needs no floor precision.
    ///
    /// WORLD POSITIONS ARE STORED, NOT HIP-RELATIVE ONES. An echo is a record of where the body
    /// actually WAS, so it must stay there while the person moves away from it. Storing hip-relative
    /// poses and re-staging them would glue every echo to the live body and destroy the whole effect.
    /// The cost is that the slow grounding correction is baked into old frames, which at 1.5 s tau
    /// and a few centimetres is far below what a viewer can see.
    ///
    /// COLOUR MEANS AGE HERE, not speed — see <see cref="BodyRenderer.RenderRaw"/> for why.
    /// </summary>
    public sealed class TimeEchoExperience : ExperienceBase {
        [Header("Time Echo")]
        [Tooltip("How far back the oldest echo reaches, in seconds. The buffer is sized from this.")]
        [SerializeField] private float longestDelaySeconds = 8f;
        [Tooltip("How many copies of you trail behind.")]
        [SerializeField] private int echoCount = 4;
        [Tooltip("Metres each successive echo is pushed sideways. 0 stacks them in place, which is "
                 + "the cleanest 'wave of arms'; raise it for a procession.")]
        [SerializeField] private float lateralSpread;
        [Tooltip("Metres each successive echo is pushed back from the camera, so the older ones sit "
                 + "further away rather than z-fighting with the newer ones.")]
        [SerializeField] private float depthSpread = 0.35f;

        /// <summary>Poses are recorded at a fixed rate rather than per render frame: the render rate
        /// varies, and a buffer whose time span depends on frame rate would change how far back the
        /// echoes reach whenever the scene got busier.</summary>
        private const float RecordHz = 60f;

        private BodyRenderer live;
        private BodyRenderer[] echoes;
        private EchoBuffer buffer;
        private float recordTimer;
        private float echoEnergy;
        private int lastChimeStep = -1;
        private float chimeCooldown;

        public override string Title {
            get {
                return "TIME ECHO";
            }
        }

        public override string Instruction {
            get {
                return "Move. Everything you just did is still happening behind you.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      C clear the echoes";
        }

        protected override void BuildExperience(Transform root) {
            live = new BodyRenderer(Palette);
            live.Build(root);

            echoCount = Mathf.Clamp(echoCount, 1, 8);
            longestDelaySeconds = Mathf.Clamp(longestDelaySeconds, 0.5f, 30f);
            echoes = new BodyRenderer[echoCount];
            int i = 0;
            while (i < echoCount) {
                echoes[i] = new BodyRenderer(Palette);
                echoes[i].Build(root);
                echoes[i].Hide();
                i = i + 1;
            }
            // One second of headroom so the oldest echo is never asking for a frame that fell off
            // the end of the ring on the frame it was needed.
            buffer = new EchoBuffer(Mathf.CeilToInt((longestDelaySeconds + 1f) * RecordHz));
        }

        protected override void ResetExperience() {
            if (buffer != null) {
                buffer.Clear();
            }
            int i = 0;
            while (echoes != null && i < echoes.Length) {
                echoes[i].Hide();
                i = i + 1;
            }
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.C)) {
                ResetExperience();
            }
        }

        protected override void Play(float deltaSeconds) {
            SkeletonPose pose = Stage.Pose;
            live.Render(pose);

            float now = Time.time;
            // The attract figure IS recorded and echoed. Nothing is scored here, and an empty room
            // showing a figure trailing its own past is a better advertisement for this experience
            // than a single motionless body would be.
            if (pose.Valid) {
                recordTimer = recordTimer + deltaSeconds;
                if (recordTimer >= 1f / RecordHz) {
                    recordTimer = 0f;
                    buffer.Push(pose, now);
                }
            }

            DrawEchoes(now, deltaSeconds);
        }

        private void DrawEchoes(float now, float deltaSeconds) {
            float loudest = 0f;
            int i = 0;
            while (i < echoes.Length) {
                // Delays are spread evenly across the window, oldest last. With one echo that puts it
                // at the full delay rather than halfway, which is what a single-echo setup wants.
                float t = echoes.Length == 1 ? 1f : (i + 1) / (float)echoes.Length;
                float delay = longestDelaySeconds * t;

                Vector3[] p;
                float[] c;
                if (!buffer.Sample(now - delay, out p, out c)) {
                    echoes[i].Hide();
                    i = i + 1;
                    continue;
                }

                // Older echoes fade and cool toward the background, and thin out. All three together
                // are what make the trail read as receding into the past rather than as four bodies.
                float age = t;
                float fade = Mathf.Lerp(0.55f, 0.10f, age);
                Color tint = Color.Lerp(Palette.Warm, Palette.Cool, age) * fade;
                float width = Mathf.Lerp(0.9f, 0.45f, age);

                Vector3 offset = new Vector3(lateralSpread * (i + 1), 0f, depthSpread * (i + 1));
                if (offset.sqrMagnitude > 0f) {
                    // Offsetting copies the array rather than mutating it: the buffer's frames are
                    // shared between echoes on the same frame, and shifting in place would drag every
                    // other echo with it.
                    int j = 0;
                    while (j < PoseFrame.LandmarkCount) {
                        offsetScratch[j] = p[j] + offset;
                        j = j + 1;
                    }
                    echoes[i].RenderRaw(offsetScratch, c, tint, width);
                } else {
                    echoes[i].RenderRaw(p, c, tint, width);
                }

                float motion = EchoMotion(p, c);
                if (motion > loudest) {
                    loudest = motion;
                }
                i = i + 1;
            }

            // The echoes get a voice of their own: a soft chime when a trailing copy makes a big
            // movement, so a gesture is heard again as it passes down the line. Rate-limited, or a
            // vigorous person turns it into a rattle.
            echoEnergy = Mathf.Lerp(echoEnergy, loudest, 1f - Mathf.Exp(-deltaSeconds / 0.2f));
            chimeCooldown = Mathf.Max(0f, chimeCooldown - deltaSeconds);
            if (echoEnergy > 0.55f && chimeCooldown <= 0f) {
                lastChimeStep = (lastChimeStep + 1) % 5;
                Audio.Play(SoundCue.Bloom, lastChimeStep, 0.35f);
                chimeCooldown = 0.45f;
            }
        }

        private readonly Vector3[] offsetScratch = new Vector3[PoseFrame.LandmarkCount];
        private readonly Vector3[] previousEcho = new Vector3[PoseFrame.LandmarkCount];
        private bool hasPreviousEcho;

        // How much the OLDEST echo is moving, measured between successive frames of it. Used only to
        // decide whether it is worth a sound; it is not shown anywhere.
        private float EchoMotion(Vector3[] p, float[] c) {
            float total = 0f;
            int counted = 0;
            int i = 0;
            while (i < SkeletonPose.Extremities.Length) {
                int joint = SkeletonPose.Extremities[i];
                if (c[joint] > 0.05f && hasPreviousEcho) {
                    total = total + Vector3.Distance(p[joint], previousEcho[joint]);
                    counted = counted + 1;
                }
                i = i + 1;
            }
            i = 0;
            while (i < PoseFrame.LandmarkCount) {
                previousEcho[i] = p[i];
                i = i + 1;
            }
            hasPreviousEcho = true;
            return counted > 0 ? Mathf.Clamp01(total / counted / 0.08f) : 0f;
        }

        protected override string StatusText() {
            if (!Stage.Pose.Valid) {
                return "Step in front of the camera.";
            }
            float span = buffer.OldestAge(Time.time);
            if (span < longestDelaySeconds) {
                // Said plainly, because for the first few seconds the trail is genuinely still
                // filling and a visitor should not think the fewer copies are a fault.
                return "recording - " + span.ToString("F1") + " s of " + longestDelaySeconds.ToString("F0")
                       + " s buffered\n" + echoCount + " echoes, the oldest "
                       + longestDelaySeconds.ToString("F0") + " s behind you";
            }
            return echoCount + " echoes    oldest " + longestDelaySeconds.ToString("F0") + " s behind you"
                   + "\nspread " + lateralSpread.ToString("F2") + " m sideways, "
                   + depthSpread.ToString("F2") + " m back";
        }
    }
}
