using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 7 — OBJECTS. Things in the room with weight. Bat them, kick them, head them,
    /// or pinch one out of the air and carry it.
    ///
    /// WHY THIS IS THE ONE PEOPLE PLAY WITH LONGEST. The other experiences respond to you; these
    /// objects OUTLAST you. Hit one and it keeps travelling, bounces, slows down and settles — so a
    /// person discovers they can aim, rally, juggle and set up shots that nobody designed. A system
    /// that only reacts frame-by-frame can be exhausted in about twenty seconds; one with state that
    /// persists between your inputs cannot.
    ///
    /// THE PHYSICS IS DELIBERATELY HAND-WRITTEN rather than Unity's. Three reasons, in order of how
    /// much they mattered: the tracked body is not a rigidbody and never will be — it teleports
    /// between frames, and a physics engine resolves a teleporting collider by launching whatever it
    /// touches across the room; the joints' MEASURED velocity is the interesting input and is exactly
    /// what a kinematic collider throws away; and this needs no colliders, no fixed timestep and no
    /// project settings, so the scene stays a camera and one GameObject. Explicit integration with a
    /// capped timestep is both simpler and better behaved here.
    ///
    /// GRABBING USES PINCH, so it degrades honestly: hand tracking degrades with BOTH distance and crowding (99.1%
    /// plausible on a single subject at 1.4 m, 93.7% at 1.96 m, 59.9% on a SEVEN-PERSON clip - see
    /// F-32: that last figure measures identity contamination, not range), and if the hands are not readable the objects are still
    /// completely playable by batting them with wrists, feet and head. The rich interaction is a
    /// bonus at close range, not a dependency.
    /// </summary>
    public sealed class ObjectPlayExperience : ExperienceBase {
        [Header("Objects")]
        [SerializeField] private int objectCount = 6;
        [Tooltip("Metres per second squared. Earth is 9.81; lower makes objects float and gives a "
                 + "person time to react, which is more fun than realism here.")]
        [SerializeField] private float gravity = 3.6f;
        [Tooltip("Fraction of speed kept on a floor bounce.")]
        [SerializeField] private float bounciness = 0.62f;

        /// <summary>Joints that can strike an object, and the radius each one hits with. Hands get a
        /// generous radius because that is where a person aims; the head gets a small one so a
        /// header has to be deliberate.</summary>
        private static readonly int[] Strikers = { 15, 16, 27, 28, 0, 13, 14 };
        private static readonly float[] StrikerRadius = { 0.20f, 0.20f, 0.17f, 0.17f, 0.15f, 0.13f, 0.13f };

        private const float ObjectRadius = 0.14f;
        private const float PlayExtent = 2.6f;

        /// <summary>Below this a strike is a touch, not a hit. Without it, resting a hand against a
        /// ball slowly pushes it across the room, which feels broken rather than gentle. Owned by
        /// <see cref="ExperienceTuning"/> since F-34 - see there for the frame-rate caveat.</summary>
        private const float MinStrikeSpeed = ExperienceTuning.TouchSpeed;

        /// <summary>How much of the joint's speed is transferred. Above 1 so a deliberate swing
        /// sends an object properly flying — a 1:1 transfer feels oddly dead, because a real hand
        /// hits with a forearm and a body behind it.</summary>
        private const float StrikeTransfer = 1.5f;

        private BodyRenderer body;
        private Transform objectRoot;
        private PlayObject[] objects;
        private int hits;
        private int longestRally;
        private int rally;
        private float rallyTimer;

        private sealed class PlayObject {
            public Transform Visual;
            public Material Material;
            public Transform Shadow;
            public Material ShadowMaterial;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Spin;
            public int HeldBy;       // -1 none, 0 left hand, 1 right hand
            public float HitFlash;
        }

        public override string Title {
            get {
                return "OBJECTS";
            }
        }

        public override string Instruction {
            get {
                return "Hit them, kick them, head them. Pinch to pick one up and let go to throw it.";
            }
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root, withHands: true);
            body.Dim = 0.6f;

            objectRoot = new GameObject("Objects").transform;
            objectRoot.SetParent(root, false);
            objects = new PlayObject[Mathf.Clamp(objectCount, 1, 24)];
            int i = 0;
            while (i < objects.Length) {
                PlayObject o = new PlayObject();
                o.Visual = ShowStage.Sphere(objectRoot, "obj_" + i, ObjectRadius * 2f, Palette,
                                            Palette.Warm, true, out o.Material);
                // A flattened disc on the floor under each object. Without a ground contact cue,
                // depth is genuinely ambiguous on a 2D screen and nobody can tell whether a ball is
                // near and low or far and high.
                o.Shadow = ShowStage.Sphere(objectRoot, "shadow_" + i, 1f, Palette,
                                            new Color(0.05f, 0.18f, 0.45f, 1f), true,
                                            out o.ShadowMaterial);
                o.HeldBy = -1;
                objects[i] = o;
                i = i + 1;
            }
            ResetExperience();
        }

        protected override void ResetExperience() {
            hits = 0;
            rally = 0;
            longestRally = 0;
            rallyTimer = 0f;
            float floor = Stage != null && Stage.Pose.HasFloor ? Stage.Pose.FloorY : 0f;
            int i = 0;
            while (i < objects.Length) {
                float angle = (i / (float)objects.Length) * Mathf.PI * 2f;
                objects[i].Position = new Vector3(Mathf.Cos(angle) * 1.1f,
                                                  floor + 1.2f + i * 0.18f,
                                                  Mathf.Sin(angle) * 0.6f - 0.4f);
                objects[i].Velocity = Vector3.zero;
                objects[i].HeldBy = -1;
                objects[i].Spin = Random.Range(0f, 6.28f);
                i = i + 1;
            }
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            body.RenderHands(Stage.Left, Stage.Right);

            // Capped so a hitch cannot tunnel an object through the floor or across the room.
            float dt = Mathf.Min(deltaSeconds, 1f / 30f);
            float floor = Stage.Pose.HasFloor ? Stage.Pose.FloorY : 0f;

            rallyTimer = Mathf.Max(0f, rallyTimer - dt);
            if (rallyTimer <= 0f) {
                rally = 0;
            }

            UpdateGrabs();

            int i = 0;
            while (i < objects.Length) {
                PlayObject o = objects[i];
                o.HitFlash = Mathf.Max(0f, o.HitFlash - dt * 3f);

                if (o.HeldBy >= 0) {
                    Carry(o, dt);
                } else {
                    Integrate(o, dt, floor);
                    if (Stage.Pose.Valid) {
                        Strike(o);
                    }
                }
                Draw(o, floor, dt);
                i = i + 1;
            }
        }

        // Pinch to pick up, release to throw. The throw velocity is the HAND's velocity, so a flick
        // throws hard and an open palm just drops it — which is what a person expects without being
        // told, and is only possible because the hand's velocity is measured rather than inferred
        // from where the object was last frame.
        private void UpdateGrabs() {
            TryGrab(Stage.Left, 0);
            TryGrab(Stage.Right, 1);
        }

        private void TryGrab(HandPose hand, int handId) {
            bool canHold = hand != null && hand.Valid && hand.Pinching;
            Vector3 grip = canHold ? hand.World[8] : Vector3.zero;

            int i = 0;
            while (i < objects.Length) {
                PlayObject o = objects[i];
                if (o.HeldBy == handId && !canHold) {
                    // Released. Hand velocity comes from the wrist, which is tracked as a body joint
                    // and therefore has a smoothed velocity; the fingertip does not.
                    int wrist = handId == 0 ? 15 : 16;
                    // Same rule as a strike: direction from Velocity, magnitude from Speed.
                    Vector3 throwVel = Vector3.zero;
                    if (Stage.Pose.Present[wrist]) {
                        Vector3 v = Stage.Pose.Velocity[wrist];
                        if (v.sqrMagnitude > 1e-6f) {
                            throwVel = v.normalized * Stage.Pose.Speed[wrist] * 1.3f;
                        }
                    }
                    o.Velocity = Vector3.ClampMagnitude(throwVel, 9f);
                    o.HeldBy = -1;
                    Audio.Play(SoundCue.Release, 2, 0.45f);
                }
                i = i + 1;
            }
            if (!canHold) {
                return;
            }

            // Already holding something with this hand? Then nothing else can be picked up by it.
            i = 0;
            while (i < objects.Length) {
                if (objects[i].HeldBy == handId) {
                    return;
                }
                i = i + 1;
            }

            int nearest = -1;
            float best = 0.30f;
            i = 0;
            while (i < objects.Length) {
                if (objects[i].HeldBy < 0) {
                    float d = Vector3.Distance(objects[i].Position, grip);
                    if (d < best) {
                        best = d;
                        nearest = i;
                    }
                }
                i = i + 1;
            }
            if (nearest >= 0) {
                objects[nearest].HeldBy = handId;
                objects[nearest].Velocity = Vector3.zero;
                Audio.Play(SoundCue.Grab, 0, 0.45f);
            }
        }

        private void Carry(PlayObject o, float dt) {
            HandPose hand = o.HeldBy == 0 ? Stage.Left : Stage.Right;
            if (hand == null || !hand.Valid) {
                o.HeldBy = -1;   // the hand stopped being readable; drop rather than freeze in mid-air
                return;
            }
            Vector3 target = hand.World[8];
            // Eased rather than snapped, so a carried object trails the hand slightly and reads as
            // having weight instead of being welded to it.
            o.Position = Vector3.Lerp(o.Position, target, 1f - Mathf.Exp(-dt / 0.06f));
            o.Velocity = Vector3.zero;
        }

        private void Integrate(PlayObject o, float dt, float floor) {
            o.Velocity = o.Velocity + Vector3.down * gravity * dt;
            o.Position = o.Position + o.Velocity * dt;

            float rest = floor + ObjectRadius;
            if (o.Position.y < rest) {
                o.Position.y = rest;
                if (o.Velocity.y < 0f) {
                    o.Velocity.y = -o.Velocity.y * bounciness;
                    // Horizontal friction on contact, so a ball rolling along the ground stops
                    // instead of sliding forever.
                    o.Velocity.x = o.Velocity.x * 0.86f;
                    o.Velocity.z = o.Velocity.z * 0.86f;
                    if (o.Velocity.y < 0.4f) {
                        o.Velocity.y = 0f;   // settle rather than jitter at rest
                    }
                }
            }

            // Soft walls: objects knocked out of the play area are nudged back rather than lost. An
            // installation that gradually loses its props needs an attendant.
            if (Mathf.Abs(o.Position.x) > PlayExtent) {
                o.Position.x = Mathf.Sign(o.Position.x) * PlayExtent;
                o.Velocity.x = -o.Velocity.x * 0.7f;
            }
            if (Mathf.Abs(o.Position.z) > PlayExtent) {
                o.Position.z = Mathf.Sign(o.Position.z) * PlayExtent;
                o.Velocity.z = -o.Velocity.z * 0.7f;
            }
            o.Velocity = o.Velocity * Mathf.Exp(-dt / 6f);   // air drag
        }

        private void Strike(PlayObject o) {
            SkeletonPose pose = Stage.Pose;
            int s = 0;
            while (s < Strikers.Length) {
                int joint = Strikers[s];
                if (!pose.Present[joint]) {
                    s = s + 1;
                    continue;
                }
                float speed = pose.Speed[joint];
                if (speed < MinStrikeSpeed) {
                    s = s + 1;
                    continue;
                }
                Vector3 jointPos = pose.World[joint];
                float reach = StrikerRadius[s] + ObjectRadius;
                Vector3 offset = o.Position - jointPos;
                if (offset.sqrMagnitude > reach * reach) {
                    s = s + 1;
                    continue;
                }

                // Push along the joint's own travel, blended with the direction from the joint to
                // the object. Pure joint velocity sends everything the same way regardless of where
                // it was hit; pure offset makes it impossible to aim. Both together lets a person
                // steer with the angle of the strike, which is what makes it feel like contact.
                Vector3 away = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector3.up;
                // DIRECTION from Velocity, MAGNITUDE from Speed - see SkeletonPose.Velocity. Using
                // the velocity vector's own length here would under-report a fast swing by about a
                // third, because a swing reverses and the smoothed vector partly cancels. That is
                // exactly the hardest hit a player can throw, and it would land as the weakest.
                Vector3 swing = pose.Velocity[joint];
                Vector3 swingDir = swing.sqrMagnitude > 1e-6f ? swing.normalized : away;
                Vector3 push = (swingDir * speed * StrikeTransfer) + away * speed * 0.7f;
                o.Velocity = Vector3.ClampMagnitude(push, 11f);
                // Lift the object clear of the striker so the next frame does not hit it again and
                // rattle it in place.
                o.Position = jointPos + away * (reach + 0.02f);
                o.HitFlash = 1f;

                // Louder for a harder swing: the sound carries the force, which is the feedback a
                // person needs to learn they can aim.
                Audio.Play(SoundCue.Hit, rally, Mathf.Clamp01(0.35f + speed * 0.18f));
                hits = hits + 1;
                rally = rally + 1;
                rallyTimer = 2.5f;
                if (rally > longestRally) {
                    longestRally = rally;
                }
                return;
            }
        }

        private void Draw(PlayObject o, float floor, float dt) {
            o.Spin = o.Spin + dt * (1f + o.Velocity.magnitude);
            o.Visual.position = o.Position;
            o.Visual.localScale = Vector3.one * ObjectRadius * 2f * (1f + o.HitFlash * 0.25f);

            Color colour = o.HeldBy >= 0
                ? Palette.Hot
                : Color.Lerp(Palette.Cool, Palette.Warm,
                             Mathf.Clamp01(o.Velocity.magnitude / 4f));
            SkeletonShowPalette.SetColour(o.Material,
                Color.Lerp(colour, Color.white * 2.5f, o.HitFlash));

            // The shadow shrinks with height, which is the cue that tells a viewer how high an
            // object is without needing a second camera angle.
            float height = Mathf.Max(0f, o.Position.y - floor);
            float shrink = Mathf.Clamp01(1f - height / 2.5f);
            float size = (ObjectRadius * 2f) * (0.5f + 0.8f * shrink);
            o.Shadow.position = new Vector3(o.Position.x, floor + 0.006f, o.Position.z);
            o.Shadow.localScale = new Vector3(size, 0.008f, size);
            SkeletonShowPalette.SetColour(o.ShadowMaterial,
                new Color(0.05f, 0.18f, 0.45f, 1f) * (0.25f + 0.75f * shrink));
        }

        protected override string StatusText() {
            if (!Stage.Pose.Valid) {
                return "Step in front of the camera. The objects are already there.";
            }
            int held = 0;
            int i = 0;
            while (i < objects.Length) {
                if (objects[i].HeldBy >= 0) {
                    held = held + 1;
                }
                i = i + 1;
            }
            string line = "hits " + hits + "      rally " + rally + "      longest " + longestRally;
            if (held > 0) {
                line = line + "      HOLDING " + held;
            }
            if (Stage.Left.Implausible || Stage.Right.Implausible) {
                // Grabbing needs readable hands; batting does not. Worth saying, because the
                // objects still work and a visitor should not think the whole thing is broken.
                line = line + "\nhands out of range - you can still hit them, but not pick them up";
            }
            return line;
        }
    }
}
