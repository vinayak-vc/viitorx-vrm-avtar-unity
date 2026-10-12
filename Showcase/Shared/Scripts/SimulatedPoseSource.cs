using System;

using UnityEngine;

using VirtualMirror.Core;

namespace ShowcaseShared {
    public enum SimulatedArmPattern {
        /// <summary>Slow automatic mix of raised, alternating and resting arms.</summary>
        Auto,
        Down,
        RaiseBoth,
        Alternate
    }

    /// <summary>
    /// A deterministic stand-in for the tracker: a plausible full-body pose in the SAME landmark
    /// format as the live stream (hip-relative metres, Unity axes, left at negative X), including
    /// both hands. Its output goes through the same SkeletonPose / HandPose / adapter code as live
    /// data. It animates sideways travel, arm raises, a torso sway and alternating hands.
    ///
    /// Test hooks: individual joints can be marked lost, hands can be dropped, and the whole stream
    /// can be stopped (<see cref="StreamActive"/> false) to exercise staleness handling.
    /// </summary>
    [Serializable]
    public sealed class SimulatedPoseSource {
        [Tooltip("Sideways travel amplitude in metres.")]
        public float travelAmplitude = 0.8f;
        [Tooltip("Seconds for one left-right-left cycle.")]
        public float travelPeriod = 16f;
        [Tooltip("Torso sway in metres at the head.")]
        public float swayAmplitude = 0.04f;
        [Tooltip("Seconds for one arm raise cycle.")]
        public float armPeriod = 6f;
        public SimulatedArmPattern armPattern = SimulatedArmPattern.Auto;
        [Tooltip("Hold still at the centre instead of travelling.")]
        public bool stationary;
        [Tooltip("The stream is delivering. Turn off to test stale-data handling.")]
        public bool streamActive = true;
        [Tooltip("Hand tracking is delivered. Turn off to test the no-hand fallback.")]
        public bool handsActive = true;

        /// <summary>Applies the same X sign the live converter applies (PoseSpaceConverter flipX), so
        /// the mirroring switch means the same thing for the simulation as for the live stream.
        /// True = like a mirror: the figure's left side is on the screen's left.</summary>
        [NonSerialized] public bool mirrorX = true;

        /// <summary>A choreographer (for example the Fluid Disruption demo programme) drives the figure
        /// instead of the built-in travel and arm patterns. The output still goes through the same
        /// landmark format and code path.</summary>
        [NonSerialized] public bool externalControl;

        /// <summary>World X of the hip while <see cref="externalControl"/> is on, metres.</summary>
        [NonSerialized] public float externalOffsetX;

        /// <summary>Arm raise 0 (hanging) .. 1 (overhead) per side while <see cref="externalControl"/> is on.</summary>
        [NonSerialized] public float externalLeftLift;
        [NonSerialized] public float externalRightLift;

        /// <summary>Joints that report zero confidence, bit n = landmark n.</summary>
        [NonSerialized] public long lostJointMask;

        public const float HipHeight = 0.95f;

        private static readonly int[] KnuckleIndex = { 5, 9, 13, 17 };
        private static readonly float[] KnuckleOffset = { -0.022f, -0.004f, 0.014f, 0.030f };
        private static readonly float[] KnuckleLength = { 0.088f, 0.090f, 0.084f, 0.072f };

        private readonly PoseFrame frame = new PoseFrame();
        private readonly RawHandFrame hands = new RawHandFrame();
        private readonly Vector3[] leftPoints = new Vector3[RawHandFrame.LandmarkCount];
        private readonly Vector3[] rightPoints = new Vector3[RawHandFrame.LandmarkCount];
        private float clock;

        public PoseFrame Frame {
            get {
                return frame;
            }
        }

        public RawHandFrame Hands {
            get {
                return hands;
            }
        }

        /// <summary>World X of the figure's hip, in metres.</summary>
        public float OffsetX { get; private set; }

        public float Clock {
            get {
                return clock;
            }
        }

        public bool StreamActive {
            get {
                return streamActive;
            }
        }

        public void Restart() {
            clock = 0f;
            OffsetX = 0f;
        }

        public void Tick(float dt) {
            clock += dt;
            if (externalControl) {
                OffsetX = externalOffsetX;
            } else {
                OffsetX = stationary ? 0f : travelAmplitude * Mathf.Sin(clock * 2f * Mathf.PI / Mathf.Max(1f, travelPeriod));
            }

            float t = clock;
            float leftLift;
            float rightLift;
            if (externalControl) {
                leftLift = externalLeftLift;
                rightLift = externalRightLift;
            } else {
                ArmLifts(t, out leftLift, out rightLift);
            }

            float sway = Mathf.Sin(t * 0.9f);
            float drift = Mathf.Sin(t * 0.37f);

            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                Vector3 p = Rest(i);
                if (p.sqrMagnitude <= 0f && i != Joint.LeftHip && i != Joint.RightHip) {
                    frame.SetLandmark(i, new PoseLandmark(Vector3.zero, 0f));
                    continue;
                }
                float lever = Mathf.Max(0f, p.y + 0.9f) / 1.5f;
                p.x += sway * swayAmplitude * lever;
                p.z += drift * 0.02f * lever;
                frame.SetLandmark(i, new PoseLandmark(p, 0.95f));
            }

            Vector3 leftWrist;
            Vector3 leftDir;
            PoseArm(true, leftLift, out leftWrist, out leftDir);
            Vector3 rightWrist;
            Vector3 rightDir;
            PoseArm(false, rightLift, out rightWrist, out rightDir);

            BuildHand(leftPoints, leftWrist, leftDir, -1f);
            BuildHand(rightPoints, rightWrist, rightDir, 1f);

            // Joint dropouts requested by a test.
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                if ((lostJointMask & (1L << i)) != 0L) {
                    PoseLandmark lm = frame.GetLandmark((JointId)i);
                    frame.SetLandmark(i, new PoseLandmark(lm.Position, 0f));
                }
            }

            if (!mirrorX) {
                for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                    PoseLandmark lm = frame.GetLandmark((JointId)i);
                    Vector3 p = lm.Position;
                    frame.SetLandmark(i, new PoseLandmark(new Vector3(-p.x, p.y, p.z), lm.Confidence));
                }
                for (int i = 0; i < RawHandFrame.LandmarkCount; i++) {
                    leftPoints[i].x = -leftPoints[i].x;
                    rightPoints[i].x = -rightPoints[i].x;
                }
                OffsetX = -OffsetX;
            }

            frame.SetMeta(clock, true);
            frame.SetRootPosition(new Vector3(OffsetX, 0f, 2.5f));

            hands.SetHand(true, handsActive ? leftPoints : null);
            hands.SetHand(false, handsActive ? rightPoints : null);
            hands.SetMeta(clock);
        }

        private void ArmLifts(float t, out float left, out float right) {
            float cycle = t * 2f * Mathf.PI / Mathf.Max(1f, armPeriod);
            switch (armPattern) {
                case SimulatedArmPattern.Down:
                    left = 0f;
                    right = 0f;
                    break;
                case SimulatedArmPattern.RaiseBoth: {
                    float lift = 0.5f + 0.5f * Mathf.Sin(cycle);
                    left = lift;
                    right = lift;
                    break;
                }
                case SimulatedArmPattern.Alternate:
                    left = 0.5f + 0.5f * Mathf.Sin(cycle);
                    right = 0.5f + 0.5f * Mathf.Sin(cycle + Mathf.PI);
                    break;
                default: {
                    // A slow programme: both up, alternating, then resting, over four armPeriods.
                    float phase = Mathf.Repeat(t / Mathf.Max(1f, armPeriod), 4f);
                    if (phase < 1f) {
                        float lift = Mathf.SmoothStep(0f, 1f, Mathf.Sin(phase * Mathf.PI));
                        left = lift;
                        right = lift;
                    } else if (phase < 3f) {
                        left = 0.5f + 0.5f * Mathf.Sin(cycle * 2f);
                        right = 0.5f + 0.5f * Mathf.Sin(cycle * 2f + Mathf.PI);
                    } else {
                        left = 0.1f;
                        right = 0.1f;
                    }
                    break;
                }
            }
        }

        // Positions rather than rotations, as the live stream delivers them.
        private void PoseArm(bool isLeft, float lift, out Vector3 wrist, out Vector3 forearmDir) {
            int shoulderId = isLeft ? Joint.LeftShoulder : Joint.RightShoulder;
            int elbowId = isLeft ? Joint.LeftElbow : Joint.RightElbow;
            int wristId = isLeft ? Joint.LeftWrist : Joint.RightWrist;
            float side = isLeft ? -1f : 1f;
            Vector3 shoulder = frame.GetLandmark((JointId)shoulderId).Position;

            // Angle from hanging straight down, swept outward and up.
            float angle = Mathf.Lerp(12f, 165f, lift) * Mathf.Deg2Rad;
            Vector3 upperDir = new Vector3(side * Mathf.Sin(angle), -Mathf.Cos(angle), 0f);
            Vector3 elbow = shoulder + upperDir * 0.28f;
            float bend = Mathf.Lerp(10f, 28f, 1f - Mathf.Abs(lift - 0.5f) * 2f) * Mathf.Deg2Rad;
            float foreAngle = angle + bend;
            forearmDir = new Vector3(side * Mathf.Sin(foreAngle), -Mathf.Cos(foreAngle), 0f);
            wrist = elbow + forearmDir * 0.25f;

            frame.SetLandmark(elbowId, new PoseLandmark(elbow, 0.95f));
            frame.SetLandmark(wristId, new PoseLandmark(wrist, 0.95f));
            // Finger landmarks the body model also carries (pinky, index, thumb).
            int pinky = isLeft ? 17 : 18;
            int index = isLeft ? 19 : 20;
            int thumb = isLeft ? 21 : 22;
            frame.SetLandmark(pinky, new PoseLandmark(wrist + forearmDir * 0.07f, 0.9f));
            frame.SetLandmark(index, new PoseLandmark(wrist + forearmDir * 0.08f, 0.9f));
            frame.SetLandmark(thumb, new PoseLandmark(wrist + forearmDir * 0.05f, 0.9f));
        }

        // 21 hand points in MediaPipe order, extended along the forearm. Palm length 0.09 m, so it
        // passes the same plausibility checks a real hand does.
        private static void BuildHand(Vector3[] points, Vector3 wrist, Vector3 dir, float side) {
            Vector3 across = new Vector3(dir.y, -dir.x, 0f) * side;
            points[0] = wrist;
            // thumb 1-4
            for (int i = 1; i <= 4; i++) {
                points[i] = wrist + dir * (0.02f + 0.018f * i) + across * (-0.02f - 0.008f * i);
            }
            for (int f = 0; f < 4; f++) {
                int baseIndex = KnuckleIndex[f];
                Vector3 knuckle = wrist + dir * KnuckleLength[f] + across * KnuckleOffset[f];
                points[baseIndex] = knuckle;
                for (int k = 1; k <= 3; k++) {
                    points[baseIndex + k] = knuckle + dir * (0.024f * k);
                }
            }
        }

        private static Vector3 Rest(int i) {
            switch (i) {
                case 0: return new Vector3(0f, 0.68f, -0.03f);
                case 7: return new Vector3(-0.075f, 0.66f, 0f);
                case 8: return new Vector3(0.075f, 0.66f, 0f);
                case 11: return new Vector3(-0.19f, 0.50f, 0f);
                case 12: return new Vector3(0.19f, 0.50f, 0f);
                case 13: return new Vector3(-0.22f, 0.22f, 0f);
                case 14: return new Vector3(0.22f, 0.22f, 0f);
                case 15: return new Vector3(-0.24f, -0.02f, 0f);
                case 16: return new Vector3(0.24f, -0.02f, 0f);
                case 23: return new Vector3(-0.09f, 0f, 0f);
                case 24: return new Vector3(0.09f, 0f, 0f);
                case 25: return new Vector3(-0.10f, -0.45f, 0f);
                case 26: return new Vector3(0.10f, -0.45f, 0f);
                case 27: return new Vector3(-0.10f, -0.88f, 0f);
                case 28: return new Vector3(0.10f, -0.88f, 0f);
                case 29: return new Vector3(-0.10f, -0.94f, 0.05f);
                case 30: return new Vector3(0.10f, -0.94f, 0.05f);
                case 31: return new Vector3(-0.10f, -0.95f, -0.13f);
                case 32: return new Vector3(0.10f, -0.95f, -0.13f);
                default: return Vector3.zero;
            }
        }
    }
}
