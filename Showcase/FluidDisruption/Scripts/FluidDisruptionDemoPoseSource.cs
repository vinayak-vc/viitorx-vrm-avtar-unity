using System;

using ShowcaseShared;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>One step of the demo programme.</summary>
    [Serializable]
    public sealed class DemoSegment {
        public string name = "segment";
        [Tooltip("Length in seconds.")]
        public float seconds = 6f;
        [Tooltip("False = the person is gone: the simulated stream stops, as when someone leaves.")]
        public bool visible = true;
        [Tooltip("Sideways travel half-width in metres; 0 stands still.")]
        public float travelAmplitude;
        [Tooltip("Sideways travel cycles per second.")]
        public float travelHz = 0.12f;
        [Tooltip("Left arm swings per second; 0 = resting.")]
        public float leftHz;
        [Tooltip("Right arm swings per second; 0 = resting.")]
        public float rightHz;
        [Tooltip("Swing size, 0..1 of the arm's raise range.")]
        public float swing = 0.35f;
        [Tooltip("Where the right arm is in its cycle relative to the left (0.5 = opposite).")]
        public float rightPhase;
        [Tooltip("The raise the arm swings around, 0 hanging .. 1 overhead.")]
        public float centreLift = FluidDisruptionDemoPoseSource.RestLift;

        public DemoSegment() {
        }

        public DemoSegment(string name, float seconds, bool visible, float travelAmplitude, float leftHz,
                           float rightHz, float swing, float rightPhase, float centreLift) {
            this.name = name;
            this.seconds = seconds;
            this.visible = visible;
            this.travelAmplitude = travelAmplitude;
            this.leftHz = leftHz;
            this.rightHz = rightHz;
            this.swing = swing;
            this.rightPhase = rightPhase;
            this.centreLift = centreLift;
        }
    }

    /// <summary>
    /// The deterministic demo programme: a scripted choreography that drives the shared
    /// <see cref="SimulatedPoseSource"/> through its external-control hooks, so the figure still goes
    /// through the same landmark format, SkeletonPose / HandPose code and adapter as a live person.
    ///
    /// Programme: nobody (calm baseline), a person appears and stands still, sideways travel, left hand
    /// slowly, right hand quickly, both hands in opposition, both hands toward each other overhead, a
    /// long stillness, the person disappears, and reappears gesturing. It loops.
    ///
    /// Determinism: the programme advances in fixed 1/120 s steps, whatever the frame time, so the same
    /// elapsed time always gives the same figure.
    /// </summary>
    [Serializable]
    public sealed class FluidDisruptionDemoPoseSource {
        public const float Step = 1f / 120f;

        /// <summary>Arm raise of a resting arm: hanging at the side.</summary>
        public const float RestLift = 0.06f;

        [Tooltip("Run the programme. Off = the simulated figure stands still in the middle (use the debug keys).")]
        public bool playProgramme = true;
        [Tooltip("Loop forever. Off = the programme ends on its last segment.")]
        public bool loop = true;
        public DemoSegment[] segments = DefaultProgramme();

        private float leftPhase;
        private float rightPhaseAcc;
        private float travelPhase;
        private float offsetX;
        private float leftLift;
        private float rightLift;
        private float accumulator;
        private float clock;
        private int holdIndex = -1;

        /// <summary>Index of the segment being played.</summary>
        public int SegmentIndex { get; private set; }

        /// <summary>Seconds since the start of the current segment.</summary>
        public float SegmentTime { get; private set; }

        public float Clock {
            get {
                return clock;
            }
        }

        public string SegmentName {
            get {
                if (segments == null || segments.Length == 0) {
                    return "-";
                }
                return segments[Mathf.Clamp(SegmentIndex, 0, segments.Length - 1)].name;
            }
        }

        public bool PersonVisible {
            get {
                if (segments == null || segments.Length == 0) {
                    return true;
                }
                return segments[Mathf.Clamp(SegmentIndex, 0, segments.Length - 1)].visible;
            }
        }

        public static DemoSegment[] DefaultProgramme() {
            return new DemoSegment[] {
                new DemoSegment("Nobody: calm baseline", 6f, false, 0f, 0f, 0f, 0f, 0f, RestLift),
                new DemoSegment("Person appears, standing still", 6f, true, 0f, 0f, 0f, 0f, 0f, RestLift),
                new DemoSegment("Torso moves sideways", 9f, true, 0.9f, 0f, 0f, 0f, 0f, RestLift),
                new DemoSegment("Left hand, slow", 7f, true, 0f, 0.30f, 0f, 0.30f, 0f, 0.45f),
                new DemoSegment("Right hand, fast", 7f, true, 0f, 0f, 1.5f, 0.38f, 0f, 0.45f),
                new DemoSegment("Both hands, opposite", 8f, true, 0f, 0.65f, 0.65f, 0.35f, 0.5f, 0.45f),
                new DemoSegment("Both hands toward each other", 7f, true, 0f, 0.5f, 0.5f, 0.32f, 0f, 0.62f),
                new DemoSegment("Stillness: the fluid settles", 10f, true, 0f, 0f, 0f, 0f, 0f, RestLift),
                new DemoSegment("Person leaves", 6f, false, 0f, 0f, 0f, 0f, 0f, RestLift),
                new DemoSegment("Person returns, both hands", 6f, true, 0.4f, 0.8f, 0.8f, 0.35f, 0.25f, 0.45f)
            };
        }

        /// <summary>Total length of one pass of the programme.</summary>
        public float TotalSeconds() {
            float total = 0f;
            if (segments != null) {
                for (int i = 0; i < segments.Length; i++) {
                    total += segments[i].seconds;
                }
            }
            return total;
        }

        public void Restart() {
            clock = 0f;
            SegmentIndex = 0;
            SegmentTime = 0f;
            leftPhase = 0f;
            rightPhaseAcc = 0f;
            travelPhase = 0f;
            offsetX = 0f;
            leftLift = RestLift;
            rightLift = RestLift;
            accumulator = 0f;
            holdIndex = -1;
        }

        /// <summary>Jump to a segment and stay on it (debug keys). -1 returns to the running programme.</summary>
        public void Hold(int index) {
            if (segments == null || segments.Length == 0 || index < 0) {
                holdIndex = -1;
                return;
            }
            holdIndex = Mathf.Clamp(index, 0, segments.Length - 1);
            SegmentIndex = holdIndex;
            SegmentTime = 0f;
        }

        public bool IsHolding {
            get {
                return holdIndex >= 0;
            }
        }

        /// <summary>Advances the programme and writes the result into the simulated figure.</summary>
        public void Tick(float dt, SimulatedPoseSource target) {
            target.externalControl = true;
            if (!playProgramme || segments == null || segments.Length == 0) {
                target.externalOffsetX = 0f;
                target.externalLeftLift = RestLift;
                target.externalRightLift = RestLift;
                target.streamActive = true;
                return;
            }

            accumulator += Mathf.Min(dt, 0.25f);
            while (accumulator >= Step) {
                accumulator -= Step;
                Advance(Step);
            }

            DemoSegment segment = segments[SegmentIndex];
            target.streamActive = segment.visible;
            target.externalOffsetX = offsetX;
            target.externalLeftLift = leftLift;
            target.externalRightLift = rightLift;
        }

        private void Advance(float step) {
            clock += step;
            SegmentTime += step;
            if (holdIndex < 0 && SegmentTime >= segments[SegmentIndex].seconds) {
                SegmentTime -= segments[SegmentIndex].seconds;
                if (SegmentIndex + 1 < segments.Length) {
                    SegmentIndex++;
                } else if (loop) {
                    SegmentIndex = 0;
                }
            }

            DemoSegment s = segments[SegmentIndex];
            leftPhase += s.leftHz * step;
            rightPhaseAcc += s.rightHz * step;
            travelPhase += s.travelHz * step;

            float travelTarget = s.travelAmplitude * Mathf.Sin(travelPhase * 2f * Mathf.PI);
            float leftTarget = s.centreLift
                               + (s.leftHz > 0f ? s.swing * Mathf.Sin(leftPhase * 2f * Mathf.PI) : 0f);
            float rightTarget = s.centreLift
                                + (s.rightHz > 0f ? s.swing * Mathf.Sin((rightPhaseAcc + s.rightPhase) * 2f * Mathf.PI) : 0f);
            if (s.leftHz <= 0f) {
                leftTarget = RestLift;
            }
            if (s.rightHz <= 0f) {
                rightTarget = RestLift;
            }

            // A critically damped follow: segment changes glide instead of jumping.
            float k = 1f - Mathf.Exp(-step / 0.25f);
            offsetX = Mathf.Lerp(offsetX, travelTarget, k);
            leftLift = Mathf.Lerp(leftLift, leftTarget, 1f - Mathf.Exp(-step / 0.12f));
            rightLift = Mathf.Lerp(rightLift, rightTarget, 1f - Mathf.Exp(-step / 0.12f));
        }
    }
}
