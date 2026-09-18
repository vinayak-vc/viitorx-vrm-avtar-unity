using System.Collections.Generic;

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

        /// <summary>The tracked body in world space, with speed, floor and telemetry attached.
        /// With a multi-person sender this is the MOST-ESTABLISHED person, which is exactly what the
        /// sender publishes at the payload root - so every single-person consumer keeps working.</summary>
        public readonly SkeletonPose Pose = new SkeletonPose();

        // ---- F-32 CROWD --------------------------------------------------------------------------
        // Everyone else. Bodies[0] is the same person as Pose; the rest only exist when a
        // multi-person sender is running. A single-person sender leaves Bodies with exactly one
        // entry, so an experience written against Bodies works with BOTH senders and does not need
        // to know which one is upstream - which is the whole point of the additive wire design.
        private readonly List<TrackedBody> bodies = new List<TrackedBody>();
        private readonly CrowdFrame crowd = new CrowdFrame();
        private readonly Dictionary<int, SkeletonPose> posesById = new Dictionary<int, SkeletonPose>();

        /// <summary>Every tracked person this frame, most-established first.</summary>
        public IReadOnlyList<TrackedBody> Bodies {
            get {
                return bodies;
            }
        }

        /// <summary>True when the upstream sender is publishing a crowd at all. False for the
        /// single-person sender - which is NOT an error, and an experience should say "one person"
        /// rather than "multi-person unavailable".</summary>
        public bool HasCrowd { get; private set; }

        /// <summary>How many people the DETECTOR saw, which can exceed Bodies.Count: the sidecar
        /// poses only as many as the GPU budget allows (measured 20.7 ms each). Surfacing both is
        /// what lets a HUD say "5 seen, 3 tracked" instead of pretending nobody else is there.</summary>
        public int DetectedCount { get; private set; }

        /// <summary>How many people the sidecar's tracker is holding, posed or not.</summary>
        public int TrackedCount { get; private set; }

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

        /// <summary>
        /// Walk left and the figure walks left. Drives the staged body's X and Z from the measured
        /// mid-hip, relative to wherever the first tracked person was standing.
        ///
        /// OFF, every body was pinned to <see cref="BodyOrigin"/> and only the CROWD moved — each
        /// extra person was offset by their own hip relative to the primary's, so two people
        /// separated correctly while one person stayed nailed to the spot however far they walked.
        /// That is what "the skeleton is not changing position if I go left or right" was.
        ///
        /// Turning it on translates the whole group, so the pair geometry every crowd experience
        /// reads — who is near whom — is EXACTLY as before: it is a difference, and a common offset
        /// cancels out of a difference.
        /// </summary>
        public bool FollowPosition = true;

        /// <summary>Scene metres per real metre walked. 1 = life-size. Below 1 keeps a big room
        /// inside a small stage; 0 is <see cref="FollowPosition"/> off.</summary>
        public float FollowScale = 1f;

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

        /// <summary>How long a late-arriving floor may still be SNAPPED to rather than eased into.
        /// Long enough to cover a normal acquisition (the feet usually land within a frame or two of
        /// the body), short enough that nobody has settled on the figure yet.</summary>
        private const float GroundSnapSeconds = 2f;

        /// <summary>Seconds this body has been continuously live. Reset when the stage goes quiet, so
        /// the next visitor gets the snap and not an eased slide.</summary>
        private float liveFor;

        /// <summary>Walk-following smoothing. MUCH faster than grounding (1.5 s): the floor is a
        /// slow-moving estimate that should not chase noise, whereas a person walking is a real,
        /// fast signal and a slow follow reads as the figure being dragged along behind them.</summary>
        private const float FollowTau = 0.12f;
        private Vector3 followOffset;
        private Vector3 followNeutral;
        private bool hasFollowNeutral;
        private bool hasTelemetry;

        public TrackedStage(ILogService logService, int port, float poseInterpolationDelayMs) {
            log = logService;
            udpPort = port;
            interpolationDelayMs = poseInterpolationDelayMs;
        }

        public void Start(bool flipX, bool flipY, bool flipZ) {
            // F-43: tilt comes from CameraMount, not from this call's arguments, because it
            // describes the bracket rather than the caller. Both callers (ExperienceBase and
            // SceneLauncher) therefore get it without either one having to carry the value.
            converter = new PoseSpaceConverter(flipX, flipY, flipZ, CameraMount.TiltDegrees);
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
            if (presence.Changed) {
                if (log != null) {
                    log.Log(LogLevel.Info, presence.Present
                            ? "presence: person acquired - going live."
                            : "presence: nobody tracked - returning to the attract loop.");
                }
                if (!presence.Present) {
                    // The room emptied. Forget where the last person stood, so the next one to walk
                    // up re-centres on the authored framing instead of inheriting a stranger's
                    // offset and starting the session standing off to one side. The floor goes with
                    // it: a new visitor is a fresh acquisition and gets the snap, not a slide.
                    ResetFollow();
                    ResetGrounding();
                }
            }

            hasTelemetry = provider.TryGetTelemetry(telemetry);
            LatencyMs = hasTelemetry ? telemetry.AgeSeconds * 1000f : -1f;

            IsLive = live;
            IsAttract = false;
            liveFor = live ? liveFor + deltaSeconds : 0f;
            // BEFORE the fills below, all of which read StagingOrigin(). Grounding runs at the end
            // of Tick and so lands one frame later, which is fine for a slow floor estimate and
            // would not be for a walking person.
            UpdateFollow(frame, live, deltaSeconds);
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

            UpdateCrowd(deltaSeconds);
            UpdateGrounding(deltaSeconds);
        }

        // Turn the provider's crowd into world-space bodies, reusing one SkeletonPose per track id.
        //
        // WHY POSES ARE KEYED ON TRACK ID rather than list position: SkeletonPose carries TEMPORAL
        // state - per-joint speed, velocity, the floor estimate - derived by differencing against
        // the previous frame. If the list order changed and pose[0] were reused for a different
        // person, every one of those would be differenced across two different bodies and report a
        // spike. Keying on the id means a person's own history follows them, and a genuinely new
        // person starts with a clean one.
        private void UpdateCrowd(float deltaSeconds) {
            bodies.Clear();
            HasCrowd = provider != null && provider.TryGetCrowd(crowd);
            if (!HasCrowd) {
                DetectedCount = IsLive ? 1 : 0;
                TrackedCount = DetectedCount;
                // Single-person sender: present the one body we have under the same API, so an
                // experience written for a crowd degrades to a crowd of one rather than to nothing.
                if (Pose.Valid) {
                    bodies.Add(new TrackedBody(0, Pose, IsAttract ? "ATTRACT" : "CONFIRMED"));
                }
                return;
            }

            DetectedCount = crowd.DetectedCount;
            TrackedCount = crowd.TrackedCount;

            // WHERE EACH PERSON STANDS. Landmarks are HIP-RELATIVE - every body is authored around
            // its own hip at the origin - so staging them all at the same point draws the whole
            // crowd exactly on top of each other. (It did: the first F-32 acceptance run measured a
            // mean pair gap of 0.00 m across 850 frames with three people on screen.)
            //
            // Each person is therefore offset by their OWN measured mid-hip relative to the primary
            // person's. The primary lands on BodyOrigin exactly as in the single-person case, so
            // nothing about the existing framing changes, and everyone else is placed at their real
            // distance and direction from them. Relative geometry is what every multi-person
            // experience actually reads - who is near whom - and a common-mode depth error cancels
            // out of a difference, so this is more robust than absolute placement would be.
            Vector3 reference = Vector3.zero;
            bool hasReference = false;
            PersonPose primaryPerson = crowd.Get(0);
            if (primaryPerson != null && primaryPerson.Pose.HasRootPosition) {
                reference = primaryPerson.Pose.RootPositionMetres;
                hasReference = true;
            }

            int i = 0;
            while (i < crowd.Count) {
                PersonPose person = crowd.Get(i);
                SkeletonPose pose;
                if (!posesById.TryGetValue(person.Id, out pose)) {
                    pose = new SkeletonPose();
                    posesById[person.Id] = pose;
                }
                Vector3 origin = StagingOrigin();
                if (hasReference && person.Pose.HasRootPosition) {
                    Vector3 offset = (person.Pose.RootPositionMetres - reference) * BodyScale;
                    // Y is deliberately NOT offset. Vertical placement is owned by the grounding
                    // loop, which stands the figure on its own measured floor; letting a noisy hip
                    // height through here would make people bob relative to each other.
                    origin = new Vector3(origin.x + offset.x, origin.y, origin.z + offset.z);
                }
                pose.Fill(person.Pose, origin, BodyScale, deltaSeconds);
                // The crowd carries no per-person trust channel yet - the sidecar's per-joint
                // trackers are single-person only (see multiperson_udp_sender's header). Leaving
                // this null is the honest answer; a HUD then reports UNREPORTED rather than
                // inventing a state for a person nobody measured.
                pose.Telemetry = null;
                bodies.Add(new TrackedBody(person.Id, pose, person.State));
                i = i + 1;
            }

            // Retire the poses of people who have gone, so a long session does not accumulate one
            // SkeletonPose per visitor who ever stood in front of the camera.
            if (posesById.Count > crowd.Count + 8) {
                retired.Clear();
                foreach (KeyValuePair<int, SkeletonPose> kv in posesById) {
                    if (crowd.ById(kv.Key) == null) {
                        retired.Add(kv.Key);
                    }
                }
                int r = 0;
                while (r < retired.Count) {
                    posesById.Remove(retired[r]);
                    r = r + 1;
                }
            }
        }

        private readonly List<int> retired = new List<int>();

        /// <summary>Where the mid-hip sits this frame: authored origin, plus grounding on Y, plus
        /// the walked offset on X/Z. One accessor, so the body and the hands can never be staged
        /// differently — which is exactly what would happen if a caller added the walk itself.</summary>
        public Vector3 StagingOrigin() {
            return new Vector3(BodyOrigin.x + followOffset.x,
                               BodyOrigin.y + groundOffset,
                               BodyOrigin.z + followOffset.z);
        }

        /// <summary>
        /// Track where the person has walked to, relative to where the first one stood.
        ///
        /// RELATIVE TO A NEUTRAL, not absolute, because the camera's origin is the camera — staging
        /// a person at their absolute measured Z would put them wherever they happened to be
        /// standing when the scene opened, which for a 2 m subject is 2 m behind the authored
        /// framing. The neutral is captured on first sight and re-captured after an absence.
        ///
        /// Y IS NOT FOLLOWED. Vertical placement belongs to the grounding loop, which stands the
        /// figure on its own measured floor; hip height is the noisiest of the three axes and
        /// letting it through makes the figure bob.
        /// </summary>
        private void UpdateFollow(PoseFrame frame, bool live, float dt) {
            if (!FollowPosition) {
                followOffset = Vector3.Lerp(followOffset, Vector3.zero,
                                            1f - Mathf.Exp(-dt / FollowTau));
                return;
            }
            if (!live || frame == null || !frame.HasRootPosition) {
                // Hold the last offset rather than easing home: a person who steps out of frame for
                // a moment should not have the figure slide back to the middle and then out again.
                return;
            }
            Vector3 root = frame.RootPositionMetres;
            if (!hasFollowNeutral) {
                followNeutral = root;
                followOffset = Vector3.zero;
                hasFollowNeutral = true;
                return;
            }
            float k = BodyScale * FollowScale;
            Vector3 target = new Vector3((root.x - followNeutral.x) * k, 0f,
                                         (root.z - followNeutral.z) * k);
            followOffset = Vector3.Lerp(followOffset, target, 1f - Mathf.Exp(-dt / FollowTau));
        }

        /// <summary>Forget where the person was standing, so the next one re-centres. Called when
        /// the stage goes quiet, and available to an experience that restages by hand.</summary>
        public void ResetFollow() {
            hasFollowNeutral = false;
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
                hasGroundOffset = true;
                // SNAP ONLY WHILE THE BODY IS STILL ARRIVING.
                //
                // The snap is right for the case it was written for - a person walks up, the floor
                // is known within a frame or two, and easing in from zero would slide the figure up
                // through the floor for a second and a half while they watch.
                //
                // But the condition is not "the first frame", it is "the first frame WITH A FLOOR",
                // and the floor needs a FOOT (FootContacts = heels and toes). If the feet are not
                // tracked - too close to the camera, cut off at the frame edge, behind a desk - then
                // HasFloor stays false and this never runs. When the feet are finally found, thirty
                // seconds in, the snap fires then: a whole-body vertical teleport in front of
                // somebody who has been standing still. Measured live on 2026-09-17, reported as
                // "the leg was not recognized and when found the skeleton jumped at 00:27".
                //
                // So: snap while nobody has settled on the figure yet, and ease once they have. A
                // slide is the lesser evil of the two once the scene has been on screen a while.
                if (liveFor <= GroundSnapSeconds) {
                    groundOffset = target;
                    return;
                }
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
