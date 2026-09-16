using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 2 — BUBBLE POP. Bubbles appear around you in a real metre-scale volume. Hit
    /// them with a hand, a foot or your head. Sixty seconds, a score, a combo.
    ///
    /// WHY THIS ONE. It is the only experience here with a game loop — a start, a timer, a score and
    /// an end — and a loop is what makes a person have a SECOND go. It also needs nothing from the
    /// hands: it runs on body joints alone, so it keeps working at the distance where hand tracking
    /// has already been measured as unreliable (59.9% plausible at 2.9 m). It is the experience to
    /// show in a big room.
    ///
    /// SPAWNING IS IN BODY SPACE, NOT WORLD SPACE. Bubbles are placed relative to the player's own
    /// hip and shoulder width, so a tall person and a short person both get bubbles they can reach
    /// and neither gets one inside their own chest. A fixed world-space box would be perfect for
    /// exactly one person's height and wrong for everybody else.
    ///
    /// A HIT NEEDS SPEED AS WELL AS PROXIMITY, and that is the single decision that makes this feel
    /// like a game rather than a proximity sensor. Standing with a hand resting inside a bubble pops
    /// nothing; a deliberate swipe pops it. Without the speed term the whole field clears itself the
    /// moment somebody walks through it.
    /// </summary>
    public sealed class BubblePopExperience : ExperienceBase {
        [Header("Bubble Pop")]
        [Tooltip("Seconds per round.")]
        [SerializeField] private float roundSeconds = 60f;
        [Tooltip("How many bubbles are in the air at once.")]
        [SerializeField] private int bubbleCount = 7;

        /// <summary>A bubble within this of a striker counts as reachable. Roughly a hand's width
        /// plus the bubble's own radius, so it pops when it LOOKS touched rather than when the joint
        /// centre is inside it.</summary>
        private const float HitRadius = 0.22f;

        /// <summary>The striker must also be moving this fast. A hand resting in a bubble is not a
        /// hit; F-28 measured a wave at 1-2 m/s, so this sits well below a deliberate swipe and well
        /// above idle drift.</summary>
        private const float HitSpeed = 0.9f;

        /// <summary>Consecutive pops within this window keep the combo alive.</summary>
        private const float ComboWindow = 1.6f;

        private const float BubbleRadius = 0.13f;

        /// <summary>Joints that can pop a bubble: both wrists, both ankles, and the head. Elbows and
        /// knees are deliberately excluded - they sweep through space as a side effect of moving the
        /// limbs that matter, and would pop bubbles nobody aimed at.</summary>
        private static readonly int[] Strikers = { 15, 16, 27, 28, 0 };

        private BodyRenderer body;
        private Transform bubbleRoot;
        private Bubble[] bubbles;
        private int score;
        private int best;
        private int combo;
        private int bestCombo;
        private float comboTimer;
        private float timeLeft;
        private bool roundOver;
        private float flash;

        private sealed class Bubble {
            public Transform Visual;
            public Material Material;
            public Vector3 Position;
            public float Phase;       // drives the idle bob, so bubbles do not sit dead still
            public float Life;        // seconds alive, for the spawn-in scale
            public bool Active;
        }

        public override string Title {
            get {
                return "BUBBLE POP";
            }
        }

        public override string Instruction {
            get {
                return "Pop the bubbles. Hands, feet or head - but you have to SWING at them.";
            }
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root);
            body.Dim = 0.55f;

            bubbleRoot = new GameObject("Bubbles").transform;
            bubbleRoot.SetParent(root, false);
            bubbles = new Bubble[Mathf.Max(1, bubbleCount)];
            int i = 0;
            while (i < bubbles.Length) {
                Bubble b = new Bubble();
                b.Visual = ShowStage.Sphere(bubbleRoot, "bubble_" + i, BubbleRadius * 2f, Palette,
                                            Palette.Warm, true, out b.Material);
                b.Active = false;
                b.Visual.gameObject.SetActive(false);
                bubbles[i] = b;
                i = i + 1;
            }
            ResetExperience();
        }

        protected override void ResetExperience() {
            score = 0;
            combo = 0;
            bestCombo = 0;
            comboTimer = 0f;
            timeLeft = roundSeconds;
            roundOver = false;
            int i = 0;
            while (i < bubbles.Length) {
                bubbles[i].Active = false;
                bubbles[i].Visual.gameObject.SetActive(false);
                i = i + 1;
            }
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            flash = Mathf.Max(0f, flash - deltaSeconds * 3f);

            // The clock only runs for a REAL player. During attract the bubbles still float — an
            // empty room should look alive — but nothing is timed and nothing is scored, because a
            // high score set by the synthetic figure would be a lie.
            if (!ScoringAllowed) {
                DriftBubbles(deltaSeconds);
                return;
            }

            if (!roundOver) {
                timeLeft = timeLeft - deltaSeconds;
                if (timeLeft <= 0f) {
                    timeLeft = 0f;
                    roundOver = true;
                    if (score > best) {
                        best = score;
                    }
                }
            }

            comboTimer = Mathf.Max(0f, comboTimer - deltaSeconds);
            if (comboTimer <= 0f) {
                combo = 0;
            }

            EnsureBubbles();
            DriftBubbles(deltaSeconds);
            if (!roundOver) {
                CheckHits(deltaSeconds);
            }
        }

        // Keep the field full. Spawning one per frame rather than all at once means a cleared field
        // refills over a second or so, which reads as the game responding rather than resetting.
        private void EnsureBubbles() {
            int i = 0;
            while (i < bubbles.Length) {
                if (!bubbles[i].Active) {
                    Spawn(bubbles[i]);
                    return;
                }
                i = i + 1;
            }
        }

        // Placed relative to THIS player's body, so everyone gets a reachable field.
        private void Spawn(Bubble b) {
            SkeletonPose pose = Stage.Pose;
            Vector3 centre = pose.Centre;
            float reach = Mathf.Max(0.45f, Vector3.Distance(pose.World[11], pose.World[12]) * 1.9f);

            float angle = Random.Range(-1.15f, 1.15f);          // in front of the player, not behind
            float radius = Random.Range(reach * 0.75f, reach * 1.6f);
            float height = Random.Range(-0.55f, 0.85f);

            b.Position = centre + new Vector3(Mathf.Sin(angle) * radius,
                                              height,
                                              -Mathf.Abs(Mathf.Cos(angle)) * radius * 0.55f);
            // Never below the floor: a bubble buried in the grid cannot be hit and looks like a bug.
            if (Stage.Pose.HasFloor) {
                b.Position.y = Mathf.Max(b.Position.y, Stage.Pose.FloorY + BubbleRadius + 0.05f);
            }
            b.Phase = Random.Range(0f, Mathf.PI * 2f);
            b.Life = 0f;
            b.Active = true;
            b.Visual.gameObject.SetActive(true);
        }

        private void DriftBubbles(float dt) {
            int i = 0;
            while (i < bubbles.Length) {
                Bubble b = bubbles[i];
                if (b.Active) {
                    b.Life = b.Life + dt;
                    b.Phase = b.Phase + dt * 1.4f;
                    // Scale in over ~0.25 s. A bubble that simply appears at full size reads as a
                    // glitch; one that grows reads as arriving.
                    float grow = Mathf.Clamp01(b.Life / 0.25f);
                    float bob = Mathf.Sin(b.Phase) * 0.035f;
                    b.Visual.position = b.Position + new Vector3(0f, bob, 0f);
                    b.Visual.localScale = Vector3.one * BubbleRadius * 2f * grow;
                    SkeletonShowPalette.SetColour(b.Material,
                        Color.Lerp(Palette.Warm, Palette.Hot, 0.5f + 0.5f * Mathf.Sin(b.Phase * 0.6f)));
                }
                i = i + 1;
            }
        }

        private void CheckHits(float dt) {
            SkeletonPose pose = Stage.Pose;
            int i = 0;
            while (i < bubbles.Length) {
                Bubble b = bubbles[i];
                if (!b.Active || b.Life < 0.2f) {
                    // The spawn-in grace stops a bubble that materialises on top of a moving hand
                    // from popping on the frame it appears, which feels arbitrary to the player.
                    i = i + 1;
                    continue;
                }
                int s = 0;
                while (s < Strikers.Length) {
                    int joint = Strikers[s];
                    if (pose.Present[joint]
                        && pose.Speed[joint] >= HitSpeed
                        && Vector3.Distance(pose.World[joint], b.Visual.position) <= HitRadius) {
                        Pop(b);
                        break;
                    }
                    s = s + 1;
                }
                i = i + 1;
            }
        }

        private void Pop(Bubble b) {
            b.Active = false;
            b.Visual.gameObject.SetActive(false);
            combo = combo + 1;
            if (combo > bestCombo) {
                bestCombo = combo;
            }
            comboTimer = ComboWindow;
            // The combo multiplies, so a fast sequence is worth disproportionately more than the
            // same bubbles popped slowly. That is the whole incentive to move.
            score = score + 10 * Mathf.Min(combo, 5);
            flash = 1f;
            // The combo picks the scale degree, so a rally rises in pitch. That is the whole reason
            // ExperienceAudio takes a step rather than a frequency: a caller can compose without
            // being able to produce a wrong note.
            Audio.Play(SoundCue.Pop, combo - 1, 0.7f);
        }

        protected override string StatusText() {
            if (!ScoringAllowed) {
                return Stage.IsAttract
                    ? "Step in front of the camera to play."
                    : "Waiting for a player.";
            }
            string line = "SCORE " + score + "      TIME " + Mathf.CeilToInt(timeLeft) + "s";
            if (combo > 1) {
                line = line + "      COMBO x" + Mathf.Min(combo, 5);
            }
            line = line + "\nbest " + best + "      best combo x" + Mathf.Min(bestCombo, 5);
            if (roundOver) {
                line = line + "\n\nTIME UP - final score " + score + ".  Press R to play again.";
            }
            return line;
        }
    }
}
