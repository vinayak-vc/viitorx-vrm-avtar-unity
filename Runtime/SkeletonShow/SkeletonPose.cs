using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 — one frame of the tracked body, in WORLD space and with the things a visual mode actually
    /// wants: where each joint is, how fast it is moving, and how much the whole body is moving.
    ///
    /// WHY A SEPARATE TYPE FROM <see cref="PoseFrame"/>. PoseFrame is the tracking contract: hip-centred
    /// metres, provider space, confidence. A renderer needs world positions and SPEED, and speed is a
    /// derived quantity that every mode would otherwise compute for itself — three times, three ways,
    /// with three different smoothing choices. Computing it once here is what lets the three modes agree
    /// about what "moving fast" means, which is the only reason they look like one product.
    ///
    /// Reused in place every frame; a mode reads it immediately and holds no reference.
    /// </summary>
    public sealed class SkeletonPose {
        /// <summary>Bone pairs for drawing, matching PoseDebugSkeleton's list so both read as the same
        /// body. Torso first, then limbs, then the head spokes.</summary>
        public static readonly int[] Bones = {
            11, 12, 11, 13, 13, 15, 12, 14, 14, 16, 11, 23, 12, 24, 23, 24,
            23, 25, 25, 27, 27, 31, 24, 26, 26, 28, 28, 32, 0, 11, 0, 12,
        };

        /// <summary>Joints worth hanging an effect off: hands and feet.</summary>
        public static readonly int[] Extremities = { 15, 16, 27, 28 };

        public readonly Vector3[] World = new Vector3[PoseFrame.LandmarkCount];
        public readonly float[] Confidence = new float[PoseFrame.LandmarkCount];
        public readonly float[] Speed = new float[PoseFrame.LandmarkCount];
        public readonly bool[] Present = new bool[PoseFrame.LandmarkCount];

        private readonly Vector3[] previous = new Vector3[PoseFrame.LandmarkCount];
        private bool hasPrevious;

        /// <summary>True when a body was tracked this frame.</summary>
        public bool Valid;

        /// <summary>Mid-hip in world space — where the body is.</summary>
        public Vector3 Centre;

        /// <summary>Whole-body movement, 0 = still, 1 = vigorous. Drives every mode's intensity so
        /// they respond to the person in the same way.</summary>
        public float Energy;

        /// <summary>Speed above which a joint counts as "fast" for colour and emission purposes. A
        /// hand wave is ~1-2 m/s; this sits at the top of ordinary movement so the hot end of every
        /// palette means something rather than being reached by standing still.</summary>
        public const float FastSpeed = 2.5f;

        /// <summary>
        /// Fill from a tracked frame. <paramref name="origin"/> and <paramref name="scale"/> place the
        /// hip-centred body in the scene. Speed is measured in WORLD units per second so it is directly
        /// comparable to the scene, and is smoothed lightly — raw per-frame speed from a 30 Hz tracker
        /// flickers hard enough to make colour and particle rates strobe.
        /// </summary>
        public void Fill(PoseFrame frame, Vector3 origin, float scale, float deltaSeconds) {
            if (frame == null || !frame.IsValid) {
                Valid = false;
                hasPrevious = false;
                Energy = 0f;
                return;
            }
            float dt = deltaSeconds > 1e-4f ? deltaSeconds : 1f / 60f;
            float smooth = 1f - Mathf.Exp(-dt / 0.08f);
            float total = 0f;
            int counted = 0;
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                PoseLandmark lmk = frame.GetLandmark((JointId)i);
                Vector3 world = origin + lmk.Position * scale;
                Confidence[i] = lmk.Confidence;
                Present[i] = lmk.Confidence > 0.05f;
                if (hasPrevious && Present[i]) {
                    float instant = Vector3.Distance(world, previous[i]) / dt;
                    Speed[i] = Speed[i] + smooth * (instant - Speed[i]);
                    total = total + Speed[i];
                    counted = counted + 1;
                } else if (!Present[i]) {
                    Speed[i] = Speed[i] * (1f - smooth);
                }
                World[i] = world;
                previous[i] = world;
                i = i + 1;
            }
            hasPrevious = true;
            Valid = true;
            Centre = 0.5f * (World[23] + World[24]);
            float mean = counted > 0 ? total / counted : 0f;
            Energy = Mathf.Clamp01(mean / FastSpeed);
        }

        /// <summary>Normalised 0..1 speed for one joint, for colour and size.</summary>
        public float Heat(int joint) {
            if (joint < 0 || joint >= Speed.Length) {
                return 0f;
            }
            return Mathf.Clamp01(Speed[joint] / FastSpeed);
        }

        /// <summary>Both endpoints of a bone were observed.</summary>
        public bool BoneVisible(int a, int b) {
            return Present[a] && Present[b];
        }
    }
}
