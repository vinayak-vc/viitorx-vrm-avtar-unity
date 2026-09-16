using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// F-27 — a known-good human body, generated rather than tracked, plus the faults a tracker
    /// actually produces.
    ///
    /// WHY SYNTHETIC. Every claim about the humanized layer is of the form "a bad pose came in and a
    /// human pose came out". Recorded footage cannot test that, because nothing in a recording is
    /// labelled bad — the labelling would be the very judgement under test. A generated body has
    /// exactly known bone lengths and joint angles, so "the forearm is still 0.26 m" is an assertion
    /// rather than an impression. This is the same discipline the offline ownership harnesses use, and
    /// the reason three wrong instruments were caught this month instead of shipped.
    ///
    /// The proportions are an average adult: 0.50 m hip-to-shoulder, 0.28 m upper arm, 0.26 m forearm,
    /// 0.42 m thigh, 0.40 m shin. Absolute realism does not matter — CONSISTENCY does, because the
    /// assertions compare the output against the generator's own numbers.
    ///
    /// SPACE matches <see cref="PoseFrame"/>: metres, hip-centred, Y up.
    /// </summary>
    public static class SyntheticPose {
        public const float HipHalfWidth = 0.10f;
        public const float ShoulderHalfWidth = 0.18f;
        public const float TrunkHeight = 0.50f;
        public const float NeckToNose = 0.25f;
        public const float UpperArm = 0.28f;
        public const float Forearm = 0.26f;
        public const float Thigh = 0.42f;
        public const float Shin = 0.40f;
        public const float FootLength = 0.12f;
        public const float HandSpan = 0.08f;

        /// <summary>
        /// A standing body. <paramref name="armElevationDeg"/> 0 = arms hanging, 90 = arms horizontal,
        /// 180 = straight overhead. <paramref name="elbowFlexDeg"/> 0 = straight arm, 90 = right angle.
        /// <paramref name="kneeFlexDeg"/> bends both knees the ANATOMICALLY CORRECT way (shin swings
        /// backwards), so a test can tell a correct bend from the injected wrong one.
        /// </summary>
        public static void Fill(PoseFrame frame, double timestamp, float armElevationDeg,
                                float elbowFlexDeg, float kneeFlexDeg, float confidence) {
            Vector3[] p = new Vector3[PoseFrame.LandmarkCount];

            Vector3 leftHip = new Vector3(HipHalfWidth, 0f, 0f);
            Vector3 rightHip = new Vector3(-HipHalfWidth, 0f, 0f);
            Vector3 midShoulder = new Vector3(0f, TrunkHeight, 0f);
            Vector3 leftShoulder = midShoulder + new Vector3(ShoulderHalfWidth, 0f, 0f);
            Vector3 rightShoulder = midShoulder + new Vector3(-ShoulderHalfWidth, 0f, 0f);

            p[23] = leftHip;
            p[24] = rightHip;
            p[11] = leftShoulder;
            p[12] = rightShoulder;
            p[0] = midShoulder + new Vector3(0f, NeckToNose, 0.02f);

            // Arms: elevate in the coronal plane, then flex the elbow backwards (−Z), which is the
            // direction a real elbow folds when the arm is out to the side.
            float e = armElevationDeg * Mathf.Deg2Rad;
            Vector3 leftUpperDir = new Vector3(Mathf.Sin(e), -Mathf.Cos(e), 0f);
            Vector3 rightUpperDir = new Vector3(-Mathf.Sin(e), -Mathf.Cos(e), 0f);
            p[13] = leftShoulder + leftUpperDir * UpperArm;
            p[14] = rightShoulder + rightUpperDir * UpperArm;

            Quaternion leftFlex = Quaternion.AngleAxis(-elbowFlexDeg, Vector3.Cross(leftUpperDir, Vector3.forward).normalized);
            Quaternion rightFlex = Quaternion.AngleAxis(-elbowFlexDeg, Vector3.Cross(rightUpperDir, Vector3.forward).normalized);
            p[15] = p[13] + (leftFlex * leftUpperDir) * Forearm;
            p[16] = p[14] + (rightFlex * rightUpperDir) * Forearm;

            // Legs: thigh straight down, shin swings BACKWARDS (−Z) as the knee flexes, which is the
            // only direction a knee bends.
            Vector3 down = Vector3.down;
            p[25] = leftHip + down * Thigh;
            p[26] = rightHip + down * Thigh;
            float k = kneeFlexDeg * Mathf.Deg2Rad;
            Vector3 shinDir = new Vector3(0f, -Mathf.Cos(k), -Mathf.Sin(k));
            p[27] = p[25] + shinDir * Shin;
            p[28] = p[26] + shinDir * Shin;

            p[29] = p[27] + new Vector3(0f, -0.04f, -0.04f);   // heels
            p[30] = p[28] + new Vector3(0f, -0.04f, -0.04f);
            p[31] = p[27] + new Vector3(0f, -0.04f, FootLength); // toes
            p[32] = p[28] + new Vector3(0f, -0.04f, FootLength);

            Vector3 leftHand = (p[15] - p[13]).normalized;
            Vector3 rightHand = (p[16] - p[14]).normalized;
            p[17] = p[15] + leftHand * HandSpan;
            p[19] = p[15] + leftHand * HandSpan + new Vector3(0f, 0.01f, 0f);
            p[21] = p[15] + leftHand * (HandSpan * 0.7f) + new Vector3(0.02f, 0f, 0f);
            p[18] = p[16] + rightHand * HandSpan;
            p[20] = p[16] + rightHand * HandSpan + new Vector3(0f, 0.01f, 0f);
            p[22] = p[16] + rightHand * (HandSpan * 0.7f) + new Vector3(-0.02f, 0f, 0f);

            // Face cluster: a rigid offset from the nose. Nothing in the body retarget reads these;
            // they exist so the frame is complete and so their pass-through can be asserted.
            int f = 1;
            while (f <= 10) {
                p[f] = p[0] + new Vector3(0.01f * ((f % 3) - 1), 0.01f * ((f % 2) - 1), 0f);
                f = f + 1;
            }

            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                frame.SetLandmark(i, new PoseLandmark(p[i], confidence));
                i = i + 1;
            }
            frame.SetMeta(timestamp, true);
        }

        /// <summary>Copy every landmark from <paramref name="source"/> into <paramref name="target"/>.</summary>
        public static void CopyInto(PoseFrame source, PoseFrame target) {
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                target.SetLandmark(i, source.GetLandmark((JointId)i));
                i = i + 1;
            }
            target.SetMeta(source.TimestampSeconds, source.IsValid);
        }

        // --- the faults ------------------------------------------------------------------------
        // Each one is a thing a real tracker has been observed to emit, not an invented stress case.

        /// <summary>The sidecar's zero-fill for a dropped joint: position (0,0,0), confidence 0. In a
        /// hip-centred frame that places the joint ON the mid-hip, which is the limb-collapse failure
        /// P0-1 and P1-3 both guard against downstream.</summary>
        public static void Drop(PoseFrame frame, int landmark) {
            frame.SetLandmark(landmark, new PoseLandmark(Vector3.zero, 0f));
        }

        /// <summary>A joint reported at the origin but with FULL confidence — the nastier variant,
        /// because every confidence gate in the pipeline waves it through.</summary>
        public static void CollapseToOrigin(PoseFrame frame, int landmark) {
            frame.SetLandmark(landmark, new PoseLandmark(Vector3.zero, 0.95f));
        }

        /// <summary>A depth blow-up: the landmark is confidently somewhere no body reaches.</summary>
        public static void Teleport(PoseFrame frame, int landmark, Vector3 to) {
            frame.SetLandmark(landmark, new PoseLandmark(to, 0.95f));
        }

        /// <summary>NaN from a divide-by-zero in a depth reprojection.</summary>
        public static void Poison(PoseFrame frame, int landmark) {
            frame.SetLandmark(landmark, new PoseLandmark(new Vector3(float.NaN, float.NaN, float.NaN), 0.9f));
        }

        /// <summary>Stretch a bone by moving the child away from the parent along its own direction —
        /// the signature of per-joint depth noise, which changes length without changing aim.</summary>
        public static void Stretch(PoseFrame frame, int parent, int child, float factor) {
            Vector3 a = frame.GetLandmark((JointId)parent).Position;
            PoseLandmark c = frame.GetLandmark((JointId)child);
            frame.SetLandmark(child, new PoseLandmark(a + (c.Position - a) * factor, c.Confidence));
        }

        /// <summary>Fold the elbow past what a human elbow can do: place the wrist almost on the
        /// shoulder, giving an interior angle near zero.</summary>
        public static void FoldElbow(PoseFrame frame, int shoulder, int elbow, int wrist) {
            Vector3 s = frame.GetLandmark((JointId)shoulder).Position;
            Vector3 e = frame.GetLandmark((JointId)elbow).Position;
            Vector3 toShoulder = (s - e).normalized;
            frame.SetLandmark(wrist, new PoseLandmark(e + toShoulder * Forearm, 0.9f));
        }

        /// <summary>Bend a knee FORWARDS, which no knee does. The shin is swung to +Z instead of −Z.</summary>
        public static void BendKneeWrongWay(PoseFrame frame, int knee, int ankle, float degrees) {
            Vector3 k = frame.GetLandmark((JointId)knee).Position;
            float r = degrees * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(0f, -Mathf.Cos(r), Mathf.Sin(r));   // +Z = forwards = impossible
            frame.SetLandmark(ankle, new PoseLandmark(k + dir * Shin, 0.9f));
        }

        /// <summary>Twist the shoulder line past any spine by rotating both shoulders about the
        /// vertical.</summary>
        public static void TwistTorso(PoseFrame frame, float degrees) {
            Quaternion q = Quaternion.AngleAxis(degrees, Vector3.up);
            Vector3 pivot = 0.5f * (frame.GetLandmark(JointId.LeftShoulder).Position
                                  + frame.GetLandmark(JointId.RightShoulder).Position);
            int[] slots = { 11, 12 };
            int i = 0;
            while (i < slots.Length) {
                PoseLandmark l = frame.GetLandmark((JointId)slots[i]);
                frame.SetLandmark(slots[i], new PoseLandmark(pivot + q * (l.Position - pivot), l.Confidence));
                i = i + 1;
            }
        }
    }
}
