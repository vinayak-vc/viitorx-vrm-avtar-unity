using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 EXPERIENCE 16 — PASS. One ball, everybody in the room, and a count that belongs to all of
    /// you. Keep it up.
    ///
    /// THE SCORE IS THE REASON THIS EXISTS. Objects already lets one person bat things around; the
    /// obvious multi-person version is two players and two scores, and that is the WORST idea
    /// available here. A per-person accumulated score is precisely the state an undetectable identity
    /// switch corrupts, in the most visible way possible: a player watches their points appear on
    /// somebody else's counter, with nothing on screen to explain it. The switch announces nothing —
    /// measured live at 0.015 m per frame against a 0.35 m margin, 97 datagrams, zero events logged
    /// — so there is no guard to write and no way to apologise for it afterwards.
    ///
    /// SO THE UNIT OF SCORE IS THE RALLY, NOT THE PLAYER. A rally is a count of consecutive touches
    /// by anybody, and it is symmetric in the people who made them: relabelling two players leaves
    /// the rally exactly as long. It is also the better game — "keep it up between you" is
    /// cooperative, which is what you want from strangers sharing a room, where "beat each other" is
    /// what you want from two friends who arrived together.
    ///
    /// ONE PLACE IDENTITY IS READ, AND IT IS BOUNDED ON PURPOSE. A PASS is counted when the toucher
    /// is a different id from the last one, which is the only per-person fact in the scene. A switch
    /// can miscount a single pass as a self-hit or the reverse. That error does not accumulate against
    /// a person and does not appear on anybody's counter — it is one off-by-one in a shared total. It
    /// is listed here rather than hidden because the difference between that and a corrupted
    /// per-player score is the whole design argument.
    ///
    /// NO PINCH-GRABBING, unlike Objects, and that is a measurement talking rather than a
    /// simplification. Hand landmarks arrive on their own channel FOR THE PRIMARY PERSON ONLY, and
    /// plausibility falls with crowding — 99.1% on a single subject at 1.4 m, 59.9% on the
    /// seven-person clip. A crowd scene cannot be built on a channel most of the crowd does not have.
    /// Batting uses wrists, ankles, head and elbows, which every tracked person has.
    ///
    /// THE PHYSICS IS HAND-WRITTEN for the same three reasons Objects gives: the tracked body
    /// teleports between frames and a physics engine resolves a teleporting collider by launching
    /// whatever it touches across the room; the joints' MEASURED velocity is the interesting input and
    /// a kinematic collider throws it away; and this needs no colliders, no fixed timestep and no
    /// project settings, so the scene stays a camera and one GameObject.
    /// </summary>
    public sealed class PassExperience : ExperienceBase {
        [Header("Pass")]
        [Tooltip("Balls in play. One makes the rally legible; more turns it into juggling.")]
        [SerializeField] private int ballCount = 1;
        [Tooltip("Metres per second squared. Earth is 9.81; lower makes the ball float and gives "
                 + "people time to get under it, which is what keeps a rally going.")]
        [SerializeField] private float gravity = 3.0f;
        [Tooltip("Fraction of speed kept on a floor bounce. High enough that a dropped ball comes "
                 + "back up to be rescued rather than dying on the floor.")]
        [SerializeField] private float bounciness = 0.68f;
        [Tooltip("Seconds after a touch before the rally is over.")]
        [SerializeField] private float rallyWindow = 3.0f;

        /// <summary>Rendering ceiling only; the pose budget caps the real number far lower.</summary>
        private const int MaxBodies = 8;

        /// <summary>Joints that can strike, and the radius each strikes with. Hands get a generous
        /// radius because that is where a person aims; the head a small one so a header is deliberate.</summary>
        private static readonly int[] Strikers = { 15, 16, 27, 28, 0, 13, 14 };
        private static readonly float[] StrikerRadius = { 0.22f, 0.22f, 0.18f, 0.18f, 0.16f, 0.14f, 0.14f };

        private const float BallRadius = 0.16f;
        private const float PlayExtent = 3.0f;

        /// <summary>How much of the joint's speed is transferred. Above 1 so a deliberate swing sends
        /// the ball properly flying - a 1:1 transfer feels oddly dead, because a real hand hits with a
        /// forearm and a body behind it.</summary>
        private const float StrikeTransfer = 1.5f;

        /// <summary>A person cannot hit the same ball again for this long. Without it a ball resting
        /// against a moving arm is struck every frame and the rally counter runs away on its own.</summary>
        private const float TouchCooldown = 0.28f;

        private BodyRenderer[] renderers;
        private Transform ballRoot;
        private Ball[] balls;
        private readonly CrowdPalette colours = new CrowdPalette();

        private int rally;
        private int longestRally;
        private int passes;
        private int totalTouches;
        private float rallyTimer;
        private int lastToucherId = int.MinValue;

        private sealed class Ball {
            public Transform Visual;
            public Material Material;
            public Transform Shadow;
            public Material ShadowMaterial;
            public Vector3 Position;
            public Vector3 Velocity;
            public float HitFlash;
            public float Cooldown;
            public Color Tint;
        }

        public override string Title {
            get {
                return "PASS";
            }
        }

        public override string Instruction {
            get {
                return "Keep it up between you. Hit it, kick it, head it - the count is everyone's.";
            }
        }

        // The rally belongs to the room, not to whoever happened to be here first. Somebody walking in
        // mid-rally joins it; they do not end it.
        protected override bool ResetsOnNewVisitor {
            get {
                return false;
            }
        }

        protected override void BuildExperience(Transform root) {
            renderers = new BodyRenderer[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                renderers[i] = new BodyRenderer(Palette);
                renderers[i].Build(root);
                renderers[i].Dim = 0.6f;
                renderers[i].Hide();
                i = i + 1;
            }

            ballRoot = new GameObject("Balls").transform;
            ballRoot.SetParent(root, false);
            balls = new Ball[Mathf.Clamp(ballCount, 1, 4)];
            i = 0;
            while (i < balls.Length) {
                Ball b = new Ball();
                b.Tint = Palette.Warm;
                b.Visual = ShowStage.Sphere(ballRoot, "ball_" + i, BallRadius * 2f, Palette,
                                            Palette.Warm, true, out b.Material);
                // A flattened disc on the floor under the ball. Without a ground contact cue, depth is
                // genuinely ambiguous on a 2D screen and nobody can tell whether the ball is near and
                // low or far and high - which is the whole skill of getting under it.
                b.Shadow = ShowStage.Sphere(ballRoot, "shadow_" + i, 1f, Palette,
                                            new Color(0.05f, 0.18f, 0.45f, 1f), true,
                                            out b.ShadowMaterial);
                balls[i] = b;
                i = i + 1;
            }
            ResetExperience();
        }

        protected override void ResetExperience() {
            rally = 0;
            longestRally = 0;
            passes = 0;
            totalTouches = 0;
            rallyTimer = 0f;
            lastToucherId = int.MinValue;
            colours.Clear();

            float floor = Stage != null && Stage.Pose.HasFloor ? Stage.Pose.FloorY : 0f;
            int i = 0;
            while (balls != null && i < balls.Length) {
                float angle = (i / (float)balls.Length) * Mathf.PI * 2f;
                balls[i].Position = new Vector3(Mathf.Cos(angle) * 0.8f, floor + 1.6f + i * 0.3f,
                                                Mathf.Sin(angle) * 0.5f);
                balls[i].Velocity = Vector3.zero;
                balls[i].Cooldown = 0f;
                balls[i].HitFlash = 0f;
                balls[i].Tint = Palette.Warm;
                i = i + 1;
            }
        }

        protected override void Play(float deltaSeconds) {
            // Capped so a hitch cannot tunnel the ball through the floor or across the room.
            float dt = Mathf.Min(deltaSeconds, 1f / 30f);
            IReadOnlyList<TrackedBody> bodies = Stage.Bodies;
            int drawn = Mathf.Min(bodies.Count, MaxBodies);
            while (drawn > 0 && !bodies[drawn - 1].Pose.Valid) {
                drawn = drawn - 1;
            }

            float floor = LowestFloor(bodies, drawn);

            int i = 0;
            while (i < renderers.Length) {
                if (i < drawn) {
                    renderers[i].Dim = bodies[i].IsScorable ? 0.6f : 0.35f;
                    renderers[i].Render(bodies[i].Pose, colours.ColourFor(bodies[i].Id));
                } else {
                    renderers[i].Hide();
                }
                i = i + 1;
            }

            rallyTimer = Mathf.Max(0f, rallyTimer - dt);
            if (rallyTimer <= 0f && rally > 0) {
                rally = 0;
                lastToucherId = int.MinValue;
            }

            i = 0;
            while (i < balls.Length) {
                Ball b = balls[i];
                b.HitFlash = Mathf.Max(0f, b.HitFlash - dt * 3f);
                b.Cooldown = Mathf.Max(0f, b.Cooldown - dt);
                Integrate(b, dt, floor);
                Strike(b, bodies, drawn);
                Draw(b, floor, dt);
                i = i + 1;
            }
        }

        // The lowest floor anybody has measured. Everybody is standing on ONE floor, and letting the
        // ball bounce off whichever person's estimate happens to be first in the list would make it
        // change height as people came and went.
        private static float LowestFloor(IReadOnlyList<TrackedBody> bodies, int drawn) {
            float floor = 0f;
            bool have = false;
            int i = 0;
            while (i < drawn) {
                if (bodies[i].Pose.HasFloor) {
                    floor = have ? Mathf.Min(floor, bodies[i].Pose.FloorY) : bodies[i].Pose.FloorY;
                    have = true;
                }
                i = i + 1;
            }
            return floor;
        }

        private void Integrate(Ball b, float dt, float floor) {
            b.Velocity = b.Velocity + Vector3.down * gravity * dt;
            b.Position = b.Position + b.Velocity * dt;

            float rest = floor + BallRadius;
            if (b.Position.y < rest) {
                b.Position.y = rest;
                if (b.Velocity.y < 0f) {
                    b.Velocity.y = -b.Velocity.y * bounciness;
                    // Horizontal friction on contact, so a ball rolling along the ground stops instead
                    // of sliding forever.
                    b.Velocity.x = b.Velocity.x * 0.86f;
                    b.Velocity.z = b.Velocity.z * 0.86f;
                    if (b.Velocity.y < 0.4f) {
                        b.Velocity.y = 0f;   // settle rather than jitter at rest
                    }
                }
            }

            // Soft walls: a ball knocked out of the play area is nudged back rather than lost. An
            // installation that gradually loses its props needs an attendant.
            if (Mathf.Abs(b.Position.x) > PlayExtent) {
                b.Position.x = Mathf.Sign(b.Position.x) * PlayExtent;
                b.Velocity.x = -b.Velocity.x * 0.7f;
            }
            if (Mathf.Abs(b.Position.z) > PlayExtent) {
                b.Position.z = Mathf.Sign(b.Position.z) * PlayExtent;
                b.Velocity.z = -b.Velocity.z * 0.7f;
            }
            b.Velocity = b.Velocity * Mathf.Exp(-dt / 6f);   // air drag
        }

        private void Strike(Ball ball, IReadOnlyList<TrackedBody> bodies, int drawn) {
            if (ball.Cooldown > 0f) {
                return;
            }
            int person = 0;
            while (person < drawn) {
                // A held (LOST) person is drawn but cannot hit anything: their joints are being
                // predicted, and a predicted arm scoring a touch is a touch nobody made.
                if (!bodies[person].IsScorable) {
                    person = person + 1;
                    continue;
                }
                SkeletonPose pose = bodies[person].Pose;
                int s = 0;
                while (s < Strikers.Length) {
                    int joint = Strikers[s];
                    if (!pose.Present[joint]) {
                        s = s + 1;
                        continue;
                    }
                    float speed = pose.Speed[joint];
                    if (speed < ExperienceTuning.TouchSpeed) {
                        s = s + 1;
                        continue;
                    }
                    Vector3 jointPos = pose.World[joint];
                    float reach = StrikerRadius[s] + BallRadius;
                    Vector3 offset = ball.Position - jointPos;
                    if (offset.sqrMagnitude > reach * reach) {
                        s = s + 1;
                        continue;
                    }

                    Hit(ball, pose, joint, jointPos, offset, speed, bodies[person].Id);
                    return;
                }
                person = person + 1;
            }
        }

        private void Hit(Ball ball, SkeletonPose pose, int joint, Vector3 jointPos, Vector3 offset,
                         float speed, int id) {
            // Push along the joint's own travel, blended with the direction from the joint to the
            // ball. Pure joint velocity sends everything the same way regardless of where it was hit;
            // pure offset makes it impossible to aim. Both together lets a person steer with the angle
            // of the strike, which is what makes a pass to somebody else possible at all.
            Vector3 away = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector3.up;
            // DIRECTION from Velocity, MAGNITUDE from Speed - see SkeletonPose.Velocity. Using the
            // velocity vector's own length would under-report a fast swing by about a third, because a
            // swing reverses and the smoothed vector partly cancels.
            Vector3 swing = pose.Velocity[joint];
            Vector3 swingDir = swing.sqrMagnitude > 1e-6f ? swing.normalized : away;
            Vector3 push = (swingDir * speed * StrikeTransfer) + away * speed * 0.7f;
            // A little lift on every touch, so a rally between people who are not trying hard still
            // gets the ball up rather than skidding it along the floor between them.
            push = push + Vector3.up * Mathf.Min(1.6f, speed * 0.55f);
            ball.Velocity = Vector3.ClampMagnitude(push, 11f);
            // Lift the ball clear of the striker so the next frame does not hit it again and rattle it
            // in place.
            ball.Position = jointPos + away * (reachOf(joint) + 0.02f);
            ball.HitFlash = 1f;
            ball.Cooldown = TouchCooldown;
            ball.Tint = colours.ColourFor(id);

            totalTouches = totalTouches + 1;
            rally = rally + 1;
            rallyTimer = rallyWindow;
            if (rally > longestRally) {
                longestRally = rally;
            }
            // The one identity read in the scene; see the class note for exactly what a switch costs.
            if (lastToucherId != int.MinValue && lastToucherId != id) {
                passes = passes + 1;
            }
            lastToucherId = id;

            // Pitch climbs with the rally, so a long one audibly builds. Loudness carries the force,
            // which is the feedback that teaches a person they can aim.
            Audio.Play(SoundCue.Hit, rally, Mathf.Clamp01(0.35f + speed * 0.18f));
        }

        private static float reachOf(int joint) {
            int s = 0;
            while (s < Strikers.Length) {
                if (Strikers[s] == joint) {
                    return StrikerRadius[s] + BallRadius;
                }
                s = s + 1;
            }
            return BallRadius;
        }

        private void Draw(Ball b, float floor, float dt) {
            b.Visual.position = b.Position;
            b.Visual.localScale = Vector3.one * BallRadius * 2f * (1f + b.HitFlash * 0.25f);
            // The ball wears the colour of whoever touched it last, which is what makes a PASS
            // visible: the ball changes colour in mid-air on its way to somebody else.
            Color colour = Color.Lerp(b.Tint * 0.7f, b.Tint,
                                      Mathf.Clamp01(b.Velocity.magnitude / 4f));
            SkeletonShowPalette.SetColour(b.Material,
                Color.Lerp(colour, Color.white * 2.5f, b.HitFlash));

            // The shadow shrinks with height, which is the cue that tells a viewer how high the ball
            // is without needing a second camera angle.
            float height = Mathf.Max(0f, b.Position.y - floor);
            float shrink = Mathf.Clamp01(1f - height / 2.5f);
            float size = (BallRadius * 2f) * (0.5f + 0.8f * shrink);
            b.Shadow.position = new Vector3(b.Position.x, floor + 0.006f, b.Position.z);
            b.Shadow.localScale = new Vector3(size, 0.008f, size);
            SkeletonShowPalette.SetColour(b.ShadowMaterial,
                new Color(0.05f, 0.18f, 0.45f, 1f) * (0.25f + 0.75f * shrink));
        }

        protected override string StatusText() {
            if (Stage.Bodies.Count == 0) {
                return "Step in front of the camera. The ball is already there.";
            }
            string line = "people  " + Stage.DetectedCount + " seen   "
                          + Stage.TrackedCount + " tracked   "
                          + Stage.Bodies.Count + " posed";
            line = line + "\nrally " + rally
                   + "      longest " + longestRally
                   + "      passes " + passes
                   + "      touches " + totalTouches;
            if (Stage.Bodies.Count < 2) {
                line = line + "\nKeeping it up alone counts. It counts more with somebody to pass to.";
            }
            return line;
        }
    }
}
