using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — THE TWO MEASURES A CROWD EXPERIENCE IS ALLOWED TO TAKE, as pure functions.
    ///
    /// WHY THEY ARE HERE AND NOT INSIDE THE SCENES THAT USE THEM. Both carry the same claim, and it
    /// is the claim the whole multi-person design rests on: THEY ARE SYMMETRIC IN THE PEOPLE. A
    /// weighted centroid does not change when two of its terms are exchanged; a similarity between
    /// two poses does not change when the two are exchanged. That is what makes an experience built
    /// on them survive an identity switch — which nothing announces, and which was measured live at
    /// 0.015 m per frame against a 0.35 m margin, 97 datagrams, zero events logged.
    ///
    /// A claim like that belongs in a test, not in a comment. Inside a MonoBehaviour it cannot be
    /// tested at all without an Editor and a play session; as static functions over plain data it is
    /// three assertions. So these moved out purely so that the safety argument is CHECKED rather than
    /// asserted, and the scenes call them rather than keeping their own copies.
    /// </summary>
    public static class CrowdMath {
        /// <summary>
        /// The weighted centroid of a set of points, or the plain centroid when every weight is
        /// effectively zero.
        ///
        /// THE FALLBACK IS NOT A DETAIL. With everybody moving at once every weight goes to nearly
        /// nothing, and dividing by nearly nothing throws the result somewhere arbitrary and far
        /// away. Falling back to the plain centroid keeps it in the middle of the group, which is
        /// also the honest reading of "nobody is winning".
        /// </summary>
        public static Vector3 WeightedCentroid(IReadOnlyList<Vector3> points,
                                               IReadOnlyList<float> weights) {
            if (points == null || points.Count == 0) {
                return Vector3.zero;
            }
            Vector3 weighted = Vector3.zero;
            Vector3 plain = Vector3.zero;
            float total = 0f;
            int i = 0;
            while (i < points.Count) {
                float w = (weights != null && i < weights.Count) ? weights[i] : 0f;
                weighted = weighted + points[i] * w;
                plain = plain + points[i];
                total = total + w;
                i = i + 1;
            }
            if (total > 1e-3f) {
                return weighted / total;
            }
            return plain / points.Count;
        }

        /// <summary>
        /// How still a person is, 0 (moving) to 1 (as still as a planted foot), raised to
        /// <paramref name="contrast"/>.
        ///
        /// Normalised against <see cref="ExperienceTuning.StillEnergy"/> rather than against zero:
        /// no live tracked body ever reaches zero energy, so a weight that only hits 1 at a dead stop
        /// would never reach the top of its range.
        /// </summary>
        public static float Stillness(SkeletonPose pose, float contrast) {
            if (pose == null || !pose.Valid) {
                return 0f;
            }
            float moving = Mathf.Clamp01(pose.Energy
                                         / Mathf.Max(1e-3f, ExperienceTuning.StillEnergy * 4f));
            return Mathf.Pow(1f - moving, Mathf.Max(0.5f, contrast));
        }

        /// <summary>
        /// How alike two poses are, 0 to 1, measured in each person's OWN torso lengths around their
        /// OWN mid-hip — so build cancels out and only the SHAPE is compared. That normalisation is
        /// Pose Match's, and it is there for exactly this reason: a tall adult and a child must be
        /// judged on whether they are holding the same shape, not on whether their absolute limb
        /// positions coincide.
        ///
        /// SYMMETRIC IN a AND b. Each term is a distance between two normalised offsets, and distance
        /// is symmetric; the mean of symmetric terms is symmetric. Exchanging the two poses returns
        /// the same number, which is the property the scene depends on.
        ///
        /// <paramref name="mirrored"/> compares each joint against its left/right counterpart with the
        /// sideways axis negated, which is what two people facing each other actually do.
        ///
        /// <paramref name="perJoint"/>, when supplied, is filled with each scored joint's own
        /// agreement so a caller can show WHERE a pair disagrees.
        ///
        /// Returns 0 when fewer than half the scored joints are visible on both people: a pair with
        /// almost nothing in common must not score highly on the two joints that happen to line up.
        /// </summary>
        public static float Similarity(SkeletonPose a, SkeletonPose b, int[] scored, int[] mirrorOf,
                                       bool mirrored, float tolerance, List<float> perJoint) {
            if (perJoint != null) {
                perJoint.Clear();
            }
            if (a == null || b == null || !a.Valid || !b.Valid || scored == null) {
                return 0f;
            }

            float torsoA = TorsoLength(a);
            float torsoB = TorsoLength(b);
            Vector3 hipA = a.Centre;
            Vector3 hipB = b.Centre;
            float span = Mathf.Max(1e-3f, tolerance);

            float total = 0f;
            int counted = 0;
            int i = 0;
            while (i < scored.Length) {
                int ja = scored[i];
                int jb = mirrored && mirrorOf != null ? mirrorOf[ja] : ja;
                float agreement = 0f;
                if (a.Present[ja] && b.Present[jb]) {
                    Vector3 oa = (a.World[ja] - hipA) / torsoA;
                    Vector3 ob = (b.World[jb] - hipB) / torsoB;
                    if (mirrored) {
                        ob.x = -ob.x;
                    }
                    agreement = 1f - Mathf.Clamp01(Vector3.Distance(oa, ob) / span);
                    total = total + agreement;
                    counted = counted + 1;
                }
                if (perJoint != null) {
                    perJoint.Add(agreement);
                }
                i = i + 1;
            }
            if (counted < scored.Length / 2) {
                return 0f;
            }
            return total / counted;
        }

        /// <summary>Mid-hip to mid-shoulder. The one length every shape comparison here is measured
        /// in. Falls back to a plausible adult torso only until the real one is measurable, which
        /// keeps a half-visible body from producing a divide-by-nearly-zero.</summary>
        public static float TorsoLength(SkeletonPose pose) {
            if (pose != null && pose.Present[11] && pose.Present[12]
                && pose.Present[23] && pose.Present[24]) {
                Vector3 shoulder = 0.5f * (pose.World[11] + pose.World[12]);
                Vector3 hip = 0.5f * (pose.World[23] + pose.World[24]);
                float d = Vector3.Distance(shoulder, hip);
                if (d > 0.15f) {
                    return d;
                }
            }
            return 0.5f;
        }

        /// <summary>Mid-shoulder in world space, falling back to the mid-hip. Chest rather than hip
        /// because a line drawn between two people's hips passes through both bodies and reads as a
        /// floor marking rather than as a connection.</summary>
        public static Vector3 ChestOf(SkeletonPose pose) {
            if (pose == null || !pose.Valid) {
                return Vector3.zero;
            }
            if (pose.Present[11] && pose.Present[12]) {
                return 0.5f * (pose.World[11] + pose.World[12]);
            }
            return pose.Centre;
        }

        /// <summary>The MediaPipe left/right joint pairing, for a mirrored comparison. Built once; an
        /// index with no counterpart maps to itself, so the head and the hips are unaffected.</summary>
        public static int[] BuildMirrorTable(int landmarkCount) {
            int[] table = new int[landmarkCount];
            int i = 0;
            while (i < table.Length) {
                table[i] = i;
                i = i + 1;
            }
            int[,] pairs = {
                { 11, 12 }, { 13, 14 }, { 15, 16 }, { 23, 24 },
                { 25, 26 }, { 27, 28 }, { 29, 30 }, { 31, 32 },
                { 1, 4 }, { 2, 5 }, { 3, 6 }, { 7, 8 }, { 9, 10 },
                { 17, 18 }, { 19, 20 }, { 21, 22 },
            };
            int p = 0;
            while (p < pairs.GetLength(0)) {
                int a = pairs[p, 0];
                int b = pairs[p, 1];
                if (a < landmarkCount && b < landmarkCount) {
                    table[a] = b;
                    table[b] = a;
                }
                p = p + 1;
            }
            return table;
        }
    }
}
