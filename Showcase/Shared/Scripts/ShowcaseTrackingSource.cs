using System;
using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking.OakD;

namespace ShowcaseShared {
    /// <summary>One person of the live crowd, as a showcase needs them. Reused every frame.</summary>
    public sealed class ShowcaseLiveBody {
        /// <summary>The sidecar's track id: stable while the person is held, never reused.</summary>
        public int Id;

        /// <summary>World-space pose with this person's own history. Owned by the stage.</summary>
        public SkeletonPose Pose;

        /// <summary>The most-established person: the one the stage also publishes as its single pose, and the only
        /// one that has hands and a trust channel.</summary>
        public bool Primary;

        /// <summary>True when <see cref="RootX"/> is a measurement.</summary>
        public bool HasRoot;

        /// <summary>Sideways position of the person's hips in scene metres, measured from the camera's axis
        /// (mirrored like the pose). The stage itself only places everyone relative to the first person.</summary>
        public float RootX;
    }

    /// <summary>
    /// Where a showcase gets its person from: the EXISTING <see cref="TrackedStage"/> (the project's UDP
    /// provider, space converter, humanized layer and grounding), or a deterministic simulated pose that
    /// goes through the same SkeletonPose / HandPose code. Shared by every showcase scene.
    ///
    /// It opens no socket of its own beyond the stage's, and starts no process: the scene's
    /// AppBootstrap owns the sidecar. It also decides what the status chip may truthfully say
    /// (<see cref="Signal"/>): LIVE only while fresh, accepted data arrives.
    /// </summary>
    public sealed class ShowcaseTrackingSource : ICrowdSource {
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
        private readonly CrowdFrame crowdCopy = new CrowdFrame();
        private readonly List<ShowcaseLiveBody> liveBodies = new List<ShowcaseLiveBody>();
        private int liveBodyCount;
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

        /// <summary>True while the live sidecar is publishing a crowd (several people may be tracked). False for a
        /// single-person sender and for the simulation: <see cref="LiveBodyCount"/> is then 0 and the pose that
        /// <see cref="Tick"/> returns is the only person.</summary>
        public bool CrowdActive {
            get {
                return liveBodyCount > 0;
            }
        }

        /// <summary>How many people the live crowd holds this frame, most-established first.</summary>
        public int LiveBodyCount {
            get {
                return liveBodyCount;
            }
        }

        public ShowcaseLiveBody LiveBody(int index) {
            return liveBodies[index];
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

            liveBodyCount = 0;
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
                CollectLiveBodies(liveFresh);
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

        // The crowd, with each person's ABSOLUTE sideways position. The stage places the first person at its own
        // origin and everyone else relative to them, so if the first person leaves, the others would all jump; the
        // sidecar's root position per person is what lets a showcase put each one where the camera sees them.
        private void CollectLiveBodies(bool liveFresh) {
            liveBodyCount = 0;
            if (!liveFresh || !stage.HasCrowd || stage.Provider == null || !stage.Provider.TryGetCrowd(crowdCopy)) {
                return;
            }
            IReadOnlyList<TrackedBody> bodies = stage.Bodies;
            for (int i = 0; i < bodies.Count; i++) {
                TrackedBody tracked = bodies[i];
                if (tracked.Pose == null || !tracked.Pose.Valid) {
                    continue;
                }
                while (liveBodies.Count <= liveBodyCount) {
                    liveBodies.Add(new ShowcaseLiveBody());
                }
                ShowcaseLiveBody body = liveBodies[liveBodyCount];
                body.Id = tracked.Id;
                body.Pose = tracked.Pose;
                body.Primary = i == 0;
                PersonPose person = crowdCopy.ById(tracked.Id);
                body.HasRoot = person != null && person.Pose.HasRootPosition;
                body.RootX = body.HasRoot ? person.Pose.RootPositionMetres.x * stage.BodyScale : 0f;
                liveBodyCount = liveBodyCount + 1;
            }
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
