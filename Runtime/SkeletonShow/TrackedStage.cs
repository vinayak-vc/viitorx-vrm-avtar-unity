using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-30 — EVERYTHING BETWEEN THE SOCKET AND A USABLE BODY, owned once.
    ///
    /// WHY THIS EXISTS. F-28's bootstrap grew the whole chain inside one MonoBehaviour: the UDP
    /// provider, the space converter, the F-27 humanized layer and its once-per-pose rule, the
    /// world-space pose, both hands, the trust channel, the attract loop, the presence gate and the
    /// floor grounding. The moment a second scene wanted a tracked body, that was ~120 lines to
    /// copy — and a copy of this particular chain is worse than most, because the subtle parts
    /// (advance the humanized layer once per POSE not per frame; never let attract carry telemetry;
    /// keep the grounding loop slower than the floor estimator it feeds through) are exactly the
    /// parts a copy silently gets wrong.
    ///
    /// A PLAIN CLASS, NOT A MonoBehaviour, deliberately. If it were a component the experience would
    /// read it in its own Update and Unity's arbitrary execution order would decide whether the pose
    /// was this frame's or last frame's. The owner calls <see cref="Tick"/> and then reads — the
    /// ordering is in the code rather than in a project setting nobody looks at.
    ///
    /// It does NOT build a camera, a light or a floor. Staging is the experience's business and each
    /// one frames itself differently; <see cref="ShowStage"/> holds the shared parts of that.
    /// </summary>
    public sealed class TrackedStage {
        private readonly int udpPort;
        private readonly float interpolationDelayMs;
        private readonly ILogService log;

        private PoseSpaceConverter converter;
        private OakDUdpPoseProvider provider;
        private HumanizedSkeleton humanized;
        private PoseFrame humanizedFrame;
        private double lastPoseTimestamp;

        private readonly TrackingTelemetry telemetry = new TrackingTelemetry();
        private readonly RawHandFrame rawHands = new RawHandFrame();
        private readonly PresenceGate presence = new PresenceGate();
        private AttractSkeleton attract;

        /// <summary>The tracked body in world space, with speed, floor and telemetry attached.</summary>
        public readonly SkeletonPose Pose = new SkeletonPose();

        public readonly HandPose Left = new HandPose();
        public readonly HandPose Right = new HandPose();

        /// <summary>Where the mid-hip is placed before grounding.</summary>
        public Vector3 BodyOrigin = new Vector3(0f, 0.95f, 0f);

        /// <summary>Scene metres per body metre.</summary>
        public float BodyScale = 1f;

        /// <summary>Run the F-27 humanized layer before anything sees the pose.</summary>
        public bool UseHumanized = true;

        /// <summary>Show the synthetic figure when nobody is tracked.</summary>
        public bool AttractEnabled = true;

        /// <summary>Slide the figure so its measured foot contact meets y=0. See the F-29 report:
        /// without it a 1.4 m subject stands 153 mm inside the floor.</summary>
        public bool GroundToFloor = true;

        /// <summary>True when a real person is being tracked right now.</summary>
        public bool IsLive { get; private set; }

        /// <summary>True when the body on screen is the synthetic attract figure. An experience must
        /// check this before scoring anything — the attract loop is not a player.</summary>
        public bool IsAttract { get; private set; }

        /// <summary>True once any pose, real or synthetic, has been produced.</summary>
        public bool HasBody { get; private set; }

        public bool EverTracked { get; private set; }

        public TrackingTelemetry Telemetry {
            get {
                return hasTelemetry ? telemetry : null;
            }
        }

        /// <summary>Wire + presentation latency in ms; negative when the sender stamps no send time.</summary>
        public float LatencyMs { get; private set; }

        public PresenceGate Presence {
            get {
                return presence;
            }
        }

        public OakDUdpPoseProvider Provider {
            get {
                return provider;
            }
        }

        // See SkeletonShowBootstrap's original note: 1.5 s is more than 2x slower than the slowest
        // part of SkeletonPose's floor estimator, so the two loops cannot interact.
        private const float GroundingTau = 1.5f;
        private float groundOffset;
        private bool hasGroundOffset;
        private bool hasTelemetry;

        public TrackedStage(ILogService logService, int port, float poseInterpolationDelayMs) {
            log = logService;
            udpPort = port;
            interpolationDelayMs = poseInterpolationDelayMs;
        }

        public void Start(bool flipX, bool flipY, bool flipZ) {
            converter = new PoseSpaceConverter(flipX, flipY, flipZ);
            humanized = new HumanizedSkeleton();
            attract = new AttractSkeleton();
            provider = new OakDUdpPoseProvider(log, converter, udpPort);
            provider.SetPoseInterpolation(interpolationDelayMs);
            provider.StartTracking();
        }

        public void Stop() {
            if (provider != null) {
                provider.StopTracking();
                provider.Dispose();
                provider = null;
            }
        }

        /// <summary>
        /// Advance one render frame. Call this FIRST in the owner's Update, before reading anything.
        /// </summary>
        public void Tick(float deltaSeconds) {
            if (provider == null) {
                return;
            }
            provider.Tick(deltaSeconds);

            PoseFrame frame;
            bool live = provider.TryGetLatestFrame(out frame) && frame != null && frame.IsValid;
            presence.Update(live, deltaSeconds);
            if (presence.Changed && log != null) {
                log.Log(LogLevel.Info, presence.Present
                        ? "presence: person acquired - going live."
                        : "presence: nobody tracked - returning to the attract loop.");
            }

            hasTelemetry = provider.TryGetTelemetry(telemetry);
            LatencyMs = hasTelemetry ? telemetry.AgeSeconds * 1000f : -1f;

            IsLive = live;
            IsAttract = false;
            if (live) {
                EverTracked = true;
                PoseFrame shown = frame;
                if (UseHumanized) {
                    // Once per POSE, with the inter-pose delta - not once per render frame. Running
                    // it per frame lets a clamped joint creep between poses, which measured as the
                    // humanized stream jumping MORE than the raw one (F-27).
                    double ts = frame.TimestampSeconds;
                    if (humanizedFrame == null || ts != lastPoseTimestamp) {
                        float poseDelta = (float)(ts - lastPoseTimestamp);
                        if (poseDelta <= 0f || poseDelta > 0.5f) {
                            poseDelta = deltaSeconds;
                        }
                        humanizedFrame = humanized.Process(frame, poseDelta);
                        lastPoseTimestamp = ts;
                    }
                    shown = humanizedFrame;
                }
                Pose.Fill(shown, StagingOrigin(), BodyScale, deltaSeconds);
                Pose.Telemetry = hasTelemetry ? telemetry : null;
            } else if (AttractEnabled && !presence.Present) {
                IsAttract = true;
                Pose.Fill(attract.Tick(deltaSeconds), StagingOrigin(), BodyScale, deltaSeconds);
                // Never telemetry on a synthetic body: nothing was tracked, so there is nothing to
                // report about it, and a HUD must not be able to describe a figure that is not real.
                Pose.Telemetry = null;
            } else {
                Pose.Fill(null, StagingOrigin(), BodyScale, deltaSeconds);
                Pose.Telemetry = hasTelemetry ? telemetry : null;
            }
            HasBody = Pose.Valid;

            // Hands come on their own channel at their own cadence - the sidecar omits a hand below
            // its confidence gate - so hand validity never follows body validity. Never filled from
            // attract: a synthetic body has no hand landmarks, and inventing them is the one thing
            // that could make the attract loop pass for a real person.
            if (live && provider.TryGetRawHands(rawHands)) {
                Left.Fill(rawHands, true, StagingOrigin(), BodyScale);
                Right.Fill(rawHands, false, StagingOrigin(), BodyScale);
            } else {
                Left.Fill(null, true, StagingOrigin(), BodyScale);
                Right.Fill(null, false, StagingOrigin(), BodyScale);
            }

            UpdateGrounding(deltaSeconds);
        }

        /// <summary>Where the mid-hip sits this frame: authored origin plus grounding. One accessor,
        /// so the body and the hands can never be staged differently.</summary>
        public Vector3 StagingOrigin() {
            return new Vector3(BodyOrigin.x, BodyOrigin.y + groundOffset, BodyOrigin.z);
        }

        private void UpdateGrounding(float dt) {
            if (!GroundToFloor) {
                groundOffset = Mathf.Lerp(groundOffset, 0f, 1f - Mathf.Exp(-dt / GroundingTau));
                return;
            }
            if (!Pose.HasFloor) {
                return;
            }
            float target = groundOffset - Pose.FloorY;
            if (!hasGroundOffset) {
                // Snap the first time. Easing in from zero would slide the figure up through the
                // floor over the first second and a half, in full view of whoever just walked up.
                groundOffset = target;
                hasGroundOffset = true;
                return;
            }
            groundOffset = groundOffset + (1f - Mathf.Exp(-dt / GroundingTau)) * (target - groundOffset);
        }

        /// <summary>Re-ground from scratch, e.g. after the staging origin is changed by hand.</summary>
        public void ResetGrounding() {
            hasGroundOffset = false;
        }

        /// <summary>A short line describing the tracking state, for an experience's HUD.</summary>
        public string StatusLine() {
            if (!EverTracked) {
                return "WAITING for the sidecar on UDP " + udpPort;
            }
            if (IsAttract) {
                return "ATTRACT - synthetic figure, nobody tracked";
            }
            if (IsLive) {
                return "TRACKING";
            }
            return "NO BODY IN FRAME";
        }
    }
}
