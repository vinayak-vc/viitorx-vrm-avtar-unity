using System;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking.OakD;

namespace ChromaticRain {
    /// <summary>
    /// Where a showcase gets its person from: the EXISTING <see cref="TrackedStage"/> (the project's UDP
    /// provider, space converter, humanized layer and grounding), or a deterministic simulated pose that
    /// goes through the same SkeletonPose / HandPose code. Shared by every showcase scene.
    ///
    /// It opens no socket of its own beyond the stage's, and starts no process: the scene's
    /// AppBootstrap owns the sidecar. It also decides what the status chip may truthfully say
    /// (<see cref="Signal"/>): LIVE only while fresh, accepted data arrives.
    /// </summary>
    public sealed class ShowcaseTrackingSource {
        private readonly string logTag;
        private readonly int udpPort;
        private readonly float poseInterpolationDelayMs;
        private readonly bool useHumanizedSkeleton;
        private readonly SimulatedPoseSource simulated;
        private readonly SkeletonPose simPose = new SkeletonPose();
        private readonly HandPose simLeft = new HandPose();
        private readonly HandPose simRight = new HandPose();

        private TrackedStage stage;
        private readonly ILogService log;
        private ShowcaseSource lastMode;
        private bool started;
        private float labelTimer;
        private string sourceLabel = string.Empty;
        private bool simulationActive;
        private float noLiveFor;
        private bool streamFresh;
        private bool resetRequested;
        private double sinceAccepted = -1.0;
        private TrackingState trackingState = TrackingState.NoStream;

        // Provider health, read in one call. Kept as fields so the call allocates nothing.
        private string healthSession;
        private long healthAccepted;
        private long healthReceived;
        private long healthTransitions;
        private long healthNewSession;
        private long healthRejectedOld;
        private long healthOutOfOrder;
        private long healthDuplicate;
        private int healthBuffer;
        private long healthReconnects;

        public ShowcaseTrackingSource(string logTag, int udpPort, float poseInterpolationDelayMs,
                                      bool useHumanizedSkeleton, bool mirrorX, SimulatedPoseSource simulated) {
            this.logTag = logTag;
            this.udpPort = udpPort;
            this.poseInterpolationDelayMs = poseInterpolationDelayMs;
            this.useHumanizedSkeleton = useHumanizedSkeleton;
            this.simulated = simulated;
            MirrorX = mirrorX;
            log = new SourceLog(logTag);
        }

        /// <summary>Live = the sidecar, Simulated = the built-in pose, Auto = live with a visible fallback.</summary>
        public ShowcaseSource Mode { get; set; }

        public bool MirrorX { get; private set; }

        /// <summary>What the bootstrap says the live source is, e.g. "VIDEO 147_1080p.mp4" or
        /// "LIVE camera:0"; empty for the OAK-D or when no bootstrap has said anything.</summary>
        public string LiveSourceLabel {
            get {
                return sourceLabel;
            }
        }

        public bool SimulationActive {
            get {
                return simulationActive;
            }
        }

        public bool StreamFresh {
            get {
                return streamFresh;
            }
        }

        public TrackingState ProviderState {
            get {
                return trackingState;
            }
        }

        public double SecondsSinceAccepted {
            get {
                return sinceAccepted;
            }
        }

        public long PacketsReceived {
            get {
                return stage != null && stage.Provider != null ? stage.Provider.ReceivedCount : 0L;
            }
        }

        public bool PortInUse {
            get {
                return stage != null && stage.Provider != null && !stage.Provider.IsRunning;
            }
        }

        public string OwnershipState {
            get {
                if (stage == null || stage.Telemetry == null) {
                    return "-";
                }
                return string.IsNullOrEmpty(stage.Telemetry.OwnershipState) ? "-" : stage.Telemetry.OwnershipState;
            }
        }

        /// <summary>What the status chip may truthfully say.</summary>
        public SignalStatus Signal {
            get {
                if (simulationActive) {
                    return Mode == ShowcaseSource.Simulated ? SignalStatus.Simulated : SignalStatus.SimulatedFallback;
                }
                if (PortInUse) {
                    return SignalStatus.PortInUse;
                }
                return streamFresh ? SignalStatus.Live : SignalStatus.NoSignal;
            }
        }

        /// <summary>True once after the source switched between live and simulated (or the mirror flag
        /// changed): the caller must forget the previous person so nothing of them survives.</summary>
        public bool ConsumeResetRequest() {
            bool value = resetRequested;
            resetRequested = false;
            return value;
        }

        /// <summary>
        /// One frame of the source. Returns whether the pose is a fresh, usable measurement: a live
        /// person for the stage, the running simulation otherwise.
        /// </summary>
        public bool Tick(float dt, ITrackingTuning tuning, out SkeletonPose pose, out HandPose left,
                         out HandPose right, out TrackingTelemetry telemetry) {
            if (!started) {
                // The first frame adopts the configured mode; it is not a switch.
                started = true;
                lastMode = Mode;
            }
            RefreshSourceLabel(dt);
            bool wantLive = Mode != ShowcaseSource.Simulated;
            if (wantLive && stage == null) {
                StartStage();
            } else if (!wantLive && stage != null) {
                Stop();
            }

            bool liveFresh = false;
            streamFresh = false;
            if (stage != null && stage.Provider != null && stage.Provider.IsRunning) {
                stage.Tick(dt);
                double since;
                stage.Provider.GetStreamHealth(out trackingState, out healthSession, out healthAccepted,
                                               out healthReceived, out since, out healthTransitions,
                                               out healthNewSession, out healthRejectedOld,
                                               out healthOutOfOrder, out healthDuplicate,
                                               out healthBuffer, out healthReconnects);
                sinceAccepted = since;
                streamFresh = trackingState == TrackingState.Live && since <= tuning.MaxPoseAgeSeconds;
                // A person, not merely a stream: the stage must call the pose live and not synthetic.
                liveFresh = streamFresh && stage.IsLive && !stage.IsAttract && stage.Pose.Valid;
            } else {
                sinceAccepted = -1.0;
                trackingState = stage != null && stage.Provider != null ? stage.Provider.State : TrackingState.NoStream;
            }

            bool useSimulation;
            switch (Mode) {
                case ShowcaseSource.Simulated:
                    useSimulation = true;
                    break;
                case ShowcaseSource.Live:
                    useSimulation = false;
                    break;
                default:
                    // Judged on the STREAM, not on whether somebody stands in front of the camera:
                    // a live sidecar with an empty room is "LIVE, no person" and shows the calm scene.
                    // Fall back only after a sustained absence of data, and hand control straight
                    // back as soon as live data returns.
                    if (streamFresh) {
                        noLiveFor = 0f;
                    } else {
                        noLiveFor += dt;
                    }
                    useSimulation = simulationActive
                        ? !streamFresh
                        : noLiveFor >= tuning.AutoFallbackSeconds;
                    break;
            }

            if (useSimulation != simulationActive || Mode != lastMode) {
                simulationActive = useSimulation;
                lastMode = Mode;
                ClearSimulatedPose();
                resetRequested = true;
            }

            if (simulationActive) {
                if (simulated.StreamActive) {
                    simulated.mirrorX = MirrorX;
                    simulated.Tick(dt);
                    Vector3 origin = new Vector3(simulated.OffsetX, SimulatedPoseSource.HipHeight, 0f);
                    simPose.Fill(simulated.Frame, origin, 1f, dt);
                    simLeft.Fill(simulated.Hands, true, origin, 1f);
                    simRight.Fill(simulated.Hands, false, origin, 1f);
                }
                pose = simPose;
                left = simLeft;
                right = simRight;
                telemetry = null;
                return simulated.StreamActive;
            }

            if (stage != null) {
                pose = stage.Pose;
                left = stage.Left;
                right = stage.Right;
                telemetry = stage.Telemetry;
            } else {
                pose = null;
                left = null;
                right = null;
                telemetry = null;
            }
            return liveFresh;
        }

        /// <summary>Left/right mirroring, as a real mirror does. Restarts the live listener so the
        /// converter is rebuilt with the new sign; the simulation follows the same convention.</summary>
        public void SetMirror(bool mirror) {
            if (mirror == MirrorX) {
                return;
            }
            MirrorX = mirror;
            if (stage != null) {
                Stop();
                StartStage();
            }
            ClearSimulatedPose();
            resetRequested = true;
        }

        /// <summary>Forget where everyone stood and restart from nobody.</summary>
        public void Recenter() {
            simulated.Restart();
            if (stage != null) {
                stage.ResetFollow();
                stage.ResetGrounding();
            }
            ClearSimulatedPose();
            resetRequested = true;
        }

        public void Stop() {
            if (stage != null) {
                stage.Stop();
                stage = null;
            }
        }

        // The bootstrap that owns the sidecar publishes one status line (video / webcam / phone, then
        // the sidecar's own state). Only its first part says WHAT is being tracked, and it is rebuilt
        // on every call, so it is read once a second rather than every frame.
        private void RefreshSourceLabel(float dt) {
            labelTimer -= dt;
            if (labelTimer > 0f) {
                return;
            }
            labelTimer = 1f;
            string status = SidecarBootstrap.Status;
            string label = string.Empty;
            if (!string.IsNullOrEmpty(status)) {
                int bar = status.IndexOf("  |  ", StringComparison.Ordinal);
                string head = bar >= 0 ? status.Substring(0, bar) : status;
                if (head.StartsWith("VIDEO", StringComparison.Ordinal) || head.StartsWith("LIVE", StringComparison.Ordinal)) {
                    label = head;
                }
            }
            sourceLabel = label;
        }

        private void ClearSimulatedPose() {
            simPose.Fill(null, Vector3.zero, 1f, 0.016f);
        }

        private void StartStage() {
            stage = new TrackedStage(log, udpPort, poseInterpolationDelayMs);
            stage.BodyOrigin = new Vector3(0f, SimulatedPoseSource.HipHeight, 0f);
            stage.BodyScale = 1f;
            stage.UseHumanized = useHumanizedSkeleton;
            // A synthetic attract figure is NOT a person and must never reach a showcase.
            stage.AttractEnabled = false;
            stage.GroundToFloor = true;
            stage.FollowPosition = true;
            stage.FollowScale = 1f;
            stage.Start(MirrorX, true, false);
            if (stage.Provider == null || !stage.Provider.IsRunning) {
                Debug.LogWarning("[" + logTag + "] The tracking socket on UDP " + udpPort + " could not be "
                                 + "opened (is the mirror app or another scene already running?). Live "
                                 + "tracking is unavailable; choose Simulated, or free the port.");
            }
        }

        private sealed class SourceLog : ILogService {
            private readonly string tag;

            public SourceLog(string tag) {
                this.tag = "[" + tag + "] ";
            }

            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Debug.LogError(tag + message);
                } else if (level == LogLevel.Warning) {
                    Debug.LogWarning(tag + message);
                } else {
                    Debug.Log(tag + message);
                }
            }

            public void LogException(Exception exception, string context) {
                Debug.LogError(tag + context + ": " + (exception != null ? exception.Message : "null"));
            }

            public void Flush() {
            }
        }
    }
}
