using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 MODE 3 — THE BODY DRIVES EFFECTS. Particles stream from the hands in proportion to how
    /// fast they move, a beam bridges them when both are raised, and the floor answers a fast hand
    /// with an expanding ring.
    ///
    /// WHY THIS IS THE MODE WITH A PRODUCT IN IT. The other two render the body. This one uses the
    /// body as an INPUT DEVICE, which is the only one of the three whose value does not depend on the
    /// tracking being beautiful — a ripple is convincing if the hand's position is roughly right and
    /// its speed is roughly right, both of which are already true (F-26 §2). It is also the mode that
    /// degrades most gracefully: a dropped joint stops emitting instead of drawing something wrong.
    ///
    /// EVERY EFFECT IS DRIVEN BY SPEED, not by position alone, and speed comes from SkeletonPose so
    /// all three modes agree about what fast means. The thresholds below are in metres/second against
    /// a real body: a deliberate swipe is ~2 m/s, resting hand jitter is under 0.2 m/s, so the gap is
    /// wide enough that noise cannot trigger a ripple.
    /// </summary>
    public sealed class MotionEffectsMode : ISkeletonMode {
        private const float RippleSpeed = 1.6f;      // m/s of hand movement that spawns a ring
        private const float RippleCooldown = 0.18f;  // s between rings from one hand
        private const float RippleLife = 0.9f;
        private const float RippleGrowth = 1.9f;     // metres/second of radius
        private const int RippleCount = 12;          // pooled; oldest is recycled
        private const int RippleSegments = 48;
        private const float BeamMinHeight = 0.05f;   // hands above the hips before the beam appears

        private Transform root;
        private SkeletonShowPalette palette;
        private ParticleSystem[] handEmitters;
        private TrailRenderer[] trails;
        private LineRenderer beam;
        private Material beamMaterial;
        private LineRenderer[] ripples;
        private Material[] rippleMaterials;
        private float[] rippleAge;
        private int rippleNext;
        private readonly float[] rippleCooldownLeft = new float[2];

        // A faint bone skeleton stays under the effects. Without it the particles read as weather
        // rather than as something a person is doing.
        private LineRenderer[] bones;
        private Material boneMaterial;

        private static readonly int[] Hands = { 15, 16 };

        public string Name {
            get {
                return "3  MOTION EFFECTS";
            }
        }

        public string Description {
            get {
                return "the body as an input device - hand particles, a beam, and floor ripples";
            }
        }

        public void Activate(Transform parent, SkeletonShowPalette sharedPalette) {
            root = new GameObject("Mode_MotionEffects").transform;
            root.SetParent(parent, false);
            palette = sharedPalette;

            int boneCount = SkeletonPose.Bones.Length / 2;
            bones = new LineRenderer[boneCount];
            boneMaterial = palette.NewAdditive(new Color(palette.Cool.r * 0.25f,
                                                         palette.Cool.g * 0.25f,
                                                         palette.Cool.b * 0.25f, 0.25f));
            int i = 0;
            while (i < boneCount) {
                GameObject go = new GameObject("bone_" + i);
                go.transform.SetParent(root, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = 0.008f;
                lr.endWidth = 0.008f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = boneMaterial;
                bones[i] = lr;
                i = i + 1;
            }

            handEmitters = new ParticleSystem[Hands.Length];
            trails = new TrailRenderer[Hands.Length];
            i = 0;
            while (i < Hands.Length) {
                handEmitters[i] = MakeEmitter("hand_particles_" + Hands[i]);
                GameObject t = new GameObject("hand_trail_" + Hands[i]);
                t.transform.SetParent(root, false);
                TrailRenderer tr = t.AddComponent<TrailRenderer>();
                tr.time = 0.5f;
                tr.startWidth = 0.055f;
                tr.endWidth = 0f;
                tr.minVertexDistance = 0.012f;
                tr.autodestruct = false;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.sharedMaterial = palette.NewAdditive(palette.Hot);
                trails[i] = tr;
                i = i + 1;
            }

            GameObject beamObject = new GameObject("beam");
            beamObject.transform.SetParent(root, false);
            beam = beamObject.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.numCapVertices = 4;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beamMaterial = palette.NewAdditive(palette.Hot);
            beam.sharedMaterial = beamMaterial;
            beam.enabled = false;

            ripples = new LineRenderer[RippleCount];
            rippleMaterials = new Material[RippleCount];
            rippleAge = new float[RippleCount];
            i = 0;
            while (i < RippleCount) {
                GameObject go = new GameObject("ripple_" + i);
                go.transform.SetParent(root, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.positionCount = RippleSegments;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                rippleMaterials[i] = palette.NewAdditive(palette.Warm);
                lr.sharedMaterial = rippleMaterials[i];
                lr.enabled = false;
                ripples[i] = lr;
                rippleAge[i] = RippleLife + 1f;      // starts dead
                i = i + 1;
            }
        }

        private ParticleSystem MakeEmitter(string name) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.8f;
            main.startSpeed = 0.35f;
            main.startSize = 0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.startColor = palette.Warm;
            main.gravityModifier = -0.05f;           // a slow drift upward reads as energy, not debris

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;              // driven per frame from hand speed

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.04f;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = palette.NewAdditive(palette.Warm);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        public void Deactivate() {
            if (root != null) {
                Object.Destroy(root.gameObject);
                root = null;
            }
            handEmitters = null;
            trails = null;
            beam = null;
            ripples = null;
            rippleMaterials = null;
            bones = null;
        }

        public void Render(SkeletonPose pose, float deltaSeconds) {
            if (root == null) {
                return;
            }
            root.gameObject.SetActive(true);
            AgeRipples(deltaSeconds);
            if (!pose.Valid) {
                int k = 0;
                while (k < handEmitters.Length) {
                    ParticleSystem.EmissionModule off = handEmitters[k].emission;
                    off.rateOverTime = 0f;
                    trails[k].Clear();
                    k = k + 1;
                }
                beam.enabled = false;
                SetBonesVisible(false);
                return;
            }

            int i = 0;
            while (i < bones.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = pose.BoneVisible(a, b);
                bones[i].enabled = visible;
                if (visible) {
                    bones[i].SetPosition(0, pose.World[a]);
                    bones[i].SetPosition(1, pose.World[b]);
                }
                i = i + 1;
            }

            float floorY = Mathf.Min(pose.World[27].y, pose.World[28].y) - 0.02f;
            i = 0;
            while (i < Hands.Length) {
                int joint = Hands[i];
                bool present = pose.Present[joint];
                Vector3 p = pose.World[joint];
                float heat = pose.Heat(joint);

                handEmitters[i].transform.position = p;
                ParticleSystem.EmissionModule emission = handEmitters[i].emission;
                emission.rateOverTime = present ? 40f + 320f * heat : 0f;
                ParticleSystem.MainModule main = handEmitters[i].main;
                main.startColor = palette.Heat(heat);

                trails[i].gameObject.SetActive(present);
                if (present) {
                    trails[i].transform.position = p;
                    trails[i].startWidth = 0.02f + 0.07f * heat;
                    SkeletonShowPalette.SetColour(trails[i].sharedMaterial, palette.Heat(heat));
                } else {
                    trails[i].Clear();
                }

                rippleCooldownLeft[i] = Mathf.Max(0f, rippleCooldownLeft[i] - deltaSeconds);
                if (present && pose.Speed[joint] > RippleSpeed && rippleCooldownLeft[i] <= 0f) {
                    SpawnRipple(new Vector3(p.x, floorY, p.z), palette.Heat(heat));
                    rippleCooldownLeft[i] = RippleCooldown;
                }
                i = i + 1;
            }

            // The beam is a DELIBERATE gesture, not an accident: both hands present, both above the
            // hips. Without the height test it fires whenever someone stands with their arms down,
            // which makes it feel random rather than responsive.
            bool bothHands = pose.Present[15] && pose.Present[16];
            bool raised = bothHands
                && pose.World[15].y > pose.Centre.y + BeamMinHeight
                && pose.World[16].y > pose.Centre.y + BeamMinHeight;
            beam.enabled = raised;
            if (raised) {
                beam.SetPosition(0, pose.World[15]);
                beam.SetPosition(1, pose.World[16]);
                float charge = Mathf.Clamp01(0.5f * (pose.Heat(15) + pose.Heat(16)) + 0.25f);
                beam.startWidth = 0.012f + 0.05f * charge;
                beam.endWidth = beam.startWidth;
                SkeletonShowPalette.SetColour(beamMaterial, palette.Heat(charge));
            }
        }

        private void SetBonesVisible(bool visible) {
            if (bones == null) {
                return;
            }
            int i = 0;
            while (i < bones.Length) {
                bones[i].enabled = visible;
                i = i + 1;
            }
        }

        private void SpawnRipple(Vector3 centre, Color colour) {
            int slot = rippleNext;
            rippleNext = (rippleNext + 1) % RippleCount;
            rippleAge[slot] = 0f;
            ripples[slot].enabled = true;
            ripples[slot].transform.position = centre;
            SkeletonShowPalette.SetColour(rippleMaterials[slot], colour);
        }

        private void AgeRipples(float deltaSeconds) {
            if (ripples == null) {
                return;
            }
            int i = 0;
            while (i < ripples.Length) {
                if (rippleAge[i] <= RippleLife) {
                    rippleAge[i] = rippleAge[i] + deltaSeconds;
                    float t = Mathf.Clamp01(rippleAge[i] / RippleLife);
                    if (t >= 1f) {
                        ripples[i].enabled = false;
                    } else {
                        float radius = t * RippleGrowth;
                        Vector3 centre = ripples[i].transform.position;
                        int s = 0;
                        while (s < RippleSegments) {
                            float angle = (s / (float)RippleSegments) * Mathf.PI * 2f;
                            ripples[i].SetPosition(s, centre + new Vector3(Mathf.Cos(angle) * radius,
                                                                           0f,
                                                                           Mathf.Sin(angle) * radius));
                            s = s + 1;
                        }
                        float fade = 1f - t;
                        ripples[i].startWidth = 0.035f * fade;
                        ripples[i].endWidth = ripples[i].startWidth;
                        Color c = rippleMaterials[i].HasProperty("_BaseColor")
                            ? rippleMaterials[i].GetColor("_BaseColor")
                            : Color.white;
                        SkeletonShowPalette.SetColour(rippleMaterials[i],
                            new Color(c.r, c.g, c.b, fade));
                    }
                }
                i = i + 1;
            }
        }
    }
}
