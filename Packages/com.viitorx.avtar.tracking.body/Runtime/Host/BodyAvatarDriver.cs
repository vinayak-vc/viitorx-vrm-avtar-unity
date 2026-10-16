using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Retargeting;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Body {
    /// <summary>
    /// One frame of the body path, from a provider to a VRM avatar's normalized control rig: stream failsafe, operating volume, the humanized skeleton,
    /// the Kalidokit whole-body solve, the fingers and the wrist from the body stream's hands, the control rig's runtime, and the avatar root's follow of
    /// the hip. It is the Kalidokit path of the Virtual Mirror application's <c>AppBootstrap</c> (its stages, their order and their defaults) without the
    /// application around it: no settings store, no scenes, no panels, no debug skeleton. A plain class, driven by <see cref="BodyBootstrap"/> or by a
    /// caller's own component through <see cref="Update"/>.
    ///
    /// The provider's frames are used as they come: the engine already smooths at the source, so no second filter is run on an
    /// <see cref="OakDUdpPoseProvider"/>. The avatar must have been loaded with the control rig generated
    /// (<c>UniVrmAvatarLoader.GenerateControlRig</c>), or <see cref="Bind"/> reports that and nothing is driven. It must also be turned 180 degrees about the vertical
    /// axis BEFORE <see cref="Bind"/> (a VRM loads looking toward +Z; the retargeting works in the model frame, where it looks toward -Z): with the avatar as loaded the arms
    /// cross the body. <see cref="BodyBootstrap"/> does this (<c>BodySettings.AvatarYawDegrees</c>); a caller binding an avatar itself must too.
    /// </summary>
    public sealed class BodyAvatarDriver {
        // How long the release to the neutral pose takes when the stream is dead; the same constant the application uses.
        private const float StreamRestReleaseSeconds = 0.5f;

        // The Kalidokit tuning the application ships with (AppBootstrap.KalidokitBody): not settings of this package, because they only make sense together.
        private const int FlipQuat = 2;
        private const float BodyLerp = 0.5f;
        private const float TorsoRoll = 1f;
        private const float SpineBendBaselineTau = 8f;
        private const float TorsoYawScale = 1f;
        private const float FingerWeight = 1f;
        private const float HumanizedConfidenceFloor = 0.05f;
        private const float HumanizedRecoverSeconds = 0.15f;
        private const float WristWeight = 0f;
        private static readonly Vector3 EulerSigns = Vector3.one;
        private static readonly Vector3 FingerCurlAxis = new Vector3(0f, 0f, -1f);

        private readonly ILogService log;
        private readonly BodySettings settings;
        private readonly IBodyTrackingProvider provider;
        private readonly OakDUdpPoseProvider oakProvider;
        private readonly KalidokitControlRigDriver controlRig;
        private readonly HumanizedSkeleton humanizedSkeleton;
        private readonly OperatingVolumeGate volumeGate;
        private readonly TrackingTelemetry telemetry;

        private IAvatarInstance avatar;
        private Transform avatarRoot;
        private Vector3 avatarRootInitialPosition;
        private bool hasTelemetry;
        private bool streamFailsafeActive;
        private bool volumeWasOutside;
        private bool volumeWasExpired;
        private double lastFrameTimestamp;
        private float lastFreshTime;
        private PoseFrame lastHumanizedFrame;
        private double lastHumanizeTimestamp;
        private bool hasPositionNeutral;
        private Vector3 positionNeutralHip;
        private Vector3 positionCurrentOffset;

        public BodyAvatarDriver(ILogService log, BodySettings settings, IBodyTrackingProvider provider) {
            if (log == null) {
                throw new System.ArgumentNullException(nameof(log));
            }
            if (settings == null) {
                throw new System.ArgumentNullException(nameof(settings));
            }
            if (provider == null) {
                throw new System.ArgumentNullException(nameof(provider));
            }
            this.log = log;
            this.settings = settings;
            this.provider = provider;
            oakProvider = provider as OakDUdpPoseProvider;
            controlRig = new KalidokitControlRigDriver();
            controlRig.SetLogger(log);
            humanizedSkeleton = new HumanizedSkeleton();
            volumeGate = new OperatingVolumeGate();
            telemetry = new TrackingTelemetry();
        }

        /// <summary>True when an avatar with a control rig is bound and <see cref="Update"/> drives it.</summary>
        public bool IsBound {
            get {
                return avatar != null && controlRig.IsBound;
            }
        }

        /// <summary>Why the avatar is or is not following the person (the operating volume's verdict).</summary>
        public OperatingVolumeGate VolumeGate {
            get {
                return volumeGate;
            }
        }

        /// <summary>The Kalidokit driver, for diagnostics (gate counters, fidelity). Not for driving the avatar from outside.</summary>
        public KalidokitControlRigDriver ControlRig {
            get {
                return controlRig;
            }
        }

        /// <summary>
        /// Binds the avatar. Returns false, and says why in the log, when it carries no <c>Vrm10Instance</c> or was loaded without the control rig.
        /// Binding another avatar replaces the first (its hold state and calibration are dropped).
        /// </summary>
        public bool Bind(IAvatarInstance instance) {
            Unbind();
            if (instance == null || instance.Root == null) {
                log.Log(LogLevel.Warning, "Body: no avatar to bind.");
                return false;
            }
            UniVRM10.Vrm10Instance vrm = instance.Root.GetComponent<UniVRM10.Vrm10Instance>();
            if (vrm == null) {
                log.Log(LogLevel.Warning, "Body: the avatar '" + instance.Root.name + "' has no Vrm10Instance; it cannot be driven.");
                return false;
            }
            controlRig.Bind(vrm);
            if (!controlRig.IsBound) {
                log.Log(LogLevel.Warning, "Body: the avatar '" + instance.Root.name + "' has no VRM control rig. Load it with UniVrmAvatarLoader.GenerateControlRig = true.");
                return false;
            }
            avatar = instance;
            avatarRoot = instance.Root.transform;
            avatarRootInitialPosition = avatarRoot.position;
            hasPositionNeutral = false;
            positionCurrentOffset = Vector3.zero;
            log.Log(LogLevel.Info, "Body: driving '" + instance.Root.name + "'.");
            return true;
        }

        public void Unbind() {
            controlRig.Unbind();
            avatar = null;
            avatarRoot = null;
            hasPositionNeutral = false;
            positionCurrentOffset = Vector3.zero;
        }

        /// <summary>The C key of the application: take the neutral again (the person stands upright and still).</summary>
        public void Recalibrate() {
            hasPositionNeutral = false;
            controlRig.Recalibrate();
        }

        /// <summary>One frame: tick the provider, then (when an avatar is bound) apply the pose. Call it from LateUpdate.</summary>
        public void Update(float deltaSeconds) {
            provider.Tick(deltaSeconds);
            bool streamFailsafe = UpdateStreamFailsafe(deltaSeconds);
            if (!IsBound) {
                return;
            }
            PoseFrame frame;
            if (!streamFailsafe && provider.TryGetLatestFrame(out frame)) {
                ApplyBodyFrame(frame, deltaSeconds);
            }
            HandFrame handFrame;
            bool haveHandFrame = ApplyHands(deltaSeconds, out handFrame);
            controlRig.ProcessRuntime();
            if (haveHandFrame) {
                controlRig.ApplyWrist(handFrame);
            }
        }

        private bool IsPoseStale(PoseFrame frame) {
            if (frame == null || !frame.IsValid) {
                return true;
            }
            double timestamp = frame.TimestampSeconds;
            if (timestamp != lastFrameTimestamp) {
                lastFrameTimestamp = timestamp;
                lastFreshTime = Time.unscaledTime;
            }
            return (Time.unscaledTime - lastFreshTime) > settings.PoseStaleSeconds;
        }

        // The stream is dead beyond the failsafe threshold: ease the avatar to its neutral pose, forget the learned skeleton (it belongs to a session that is
        // over), and consume no pose this frame. Returns whether the failsafe is active.
        private bool UpdateStreamFailsafe(float deltaSeconds) {
            bool streamFailsafe = false;
            if (oakProvider != null) {
                streamFailsafe = oakProvider.State == TrackingState.StaleFailsafe;
            }
            if (streamFailsafe) {
                if (!streamFailsafeActive) {
                    streamFailsafeActive = true;
                    controlRig.ResetHoldState();
                    humanizedSkeleton.Reset();
                    lastHumanizedFrame = null;
                    volumeGate.Reset();
                    log.Log(LogLevel.Warning, "Body: the tracking stream is stale beyond the failsafe threshold; releasing the avatar to its neutral pose.");
                }
                if (controlRig.IsBound) {
                    controlRig.ReleaseToRest(deltaSeconds / StreamRestReleaseSeconds);
                    controlRig.ProcessRuntime();
                }
            } else if (streamFailsafeActive) {
                streamFailsafeActive = false;
                log.Log(LogLevel.Info, "Body: the tracking stream recovered; resuming from the neutral pose.");
            }
            return streamFailsafe;
        }

        private void ApplyBodyFrame(PoseFrame frame, float deltaSeconds) {
            volumeGate.Update(frame, deltaSeconds);
            hasTelemetry = oakProvider != null && oakProvider.TryGetTelemetry(telemetry);
            ReportVolumeTransitions();
            bool volumeHold = volumeGate.Admission == PoseAdmission.Outside;
            PoseFrame humanized = HumanizeFrame(frame, deltaSeconds);
            // May this pose drive the avatar? Not once the person has been out of range longer than the hold: the avatar then stays where it last was,
            // rather than being posed from data the camera cannot supply. Inside the hold it is fed the humanized layer's held body.
            bool applyBody = !volumeGate.Expired && (!volumeHold || settings.UseHumanizedSkeleton);
            if (applyBody && !IsPoseStale(humanized)) {
                controlRig.SetTuning(EulerSigns, FlipQuat, BodyLerp, settings.TrackLegs, false, TorsoRoll);
                controlRig.SetSpineBend(settings.SpineBendScale);
                controlRig.SetSpineBendDynamics(SpineBendBaselineTau);
                controlRig.SetTorsoYawScale(TorsoYawScale);
                controlRig.SetLimbConfidence(settings.LimbConfidenceThreshold);
                controlRig.SetUseAimArms(true);
                // The fidelity metric measures against the RAW tracked pose, not against whatever drove the avatar.
                controlRig.SetFidelityReference(frame);
                controlRig.Apply(humanized);
            }
            // Walk and jump: the avatar root follows the hip, and only an in-range pose may move it.
            if (!volumeHold) {
                ApplyRootPosition(frame, deltaSeconds);
            }
        }

        // Run ONCE PER POSE, not once per render frame: the provider returns the same cached pose on every render tick, and the layer's temporal stages
        // would otherwise advance several times per observation. Keyed off the pose's timestamp and given the inter-pose delta.
        private PoseFrame HumanizeFrame(PoseFrame frame, float deltaSeconds) {
            if (!settings.UseHumanizedSkeleton) {
                return frame;
            }
            double poseTimestamp = frame.TimestampSeconds;
            if (lastHumanizedFrame == null || poseTimestamp != lastHumanizeTimestamp) {
                float poseDelta = deltaSeconds;
                if (lastHumanizedFrame != null && poseTimestamp > lastHumanizeTimestamp) {
                    poseDelta = (float)(poseTimestamp - lastHumanizeTimestamp);
                }
                humanizedSkeleton.SetTuning(HumanizedConfidenceFloor, HumanizedRecoverSeconds);
                humanizedSkeleton.SetStages(true, true, true);
                lastHumanizedFrame = humanizedSkeleton.Process(frame, poseDelta, hasTelemetry ? telemetry : null, volumeGate.Admission);
                lastHumanizeTimestamp = poseTimestamp;
            }
            return lastHumanizedFrame;
        }

        // The fingers come from the hands of the body stream (the Kalidokit control rig owns the skeleton, so its fingers are driven before ProcessRuntime and
        // its wrist after it). Returns true when a hand frame exists, so the wrist can reuse it.
        private bool ApplyHands(float deltaSeconds, out HandFrame handFrame) {
            handFrame = null;
            if (!settings.TrackHands || oakProvider == null) {
                return false;
            }
            controlRig.SetFingerTuning(FingerCurlAxis, FingerWeight);
            controlRig.SetWristWeight(WristWeight);
            if (oakProvider.TryGetHandFrame(out handFrame)) {
                controlRig.ApplyFingers(handFrame);
                return true;
            }
            return false;
        }

        private void ApplyRootPosition(PoseFrame frame, float deltaSeconds) {
            if (!settings.TrackPosition || avatarRoot == null || frame == null || !frame.HasRootPosition) {
                return;
            }
            if (!hasPositionNeutral) {
                positionNeutralHip = frame.RootPositionMetres;
                hasPositionNeutral = true;
            }
            Vector3 targetOffset = (frame.RootPositionMetres - positionNeutralHip) * settings.PositionScale;
            float lerpT = 1f - Mathf.Exp(-settings.PositionSmoothing * deltaSeconds);
            positionCurrentOffset = Vector3.Lerp(positionCurrentOffset, targetOffset, lerpT);
            avatarRoot.position = avatarRootInitialPosition + positionCurrentOffset;
        }

        // Say so, once per change: a held or withheld avatar beside a healthy UDP stream looks exactly like a broken tracker, so the reason has to be in the log
        // at the moment it begins.
        private void ReportVolumeTransitions() {
            bool outside = volumeGate.Admission == PoseAdmission.Outside;
            bool expired = volumeGate.Expired;
            if (outside == volumeWasOutside && expired == volumeWasExpired) {
                return;
            }
            volumeWasOutside = outside;
            volumeWasExpired = expired;
            if (outside) {
                log.Log(LogLevel.Warning, "Body operating volume: " + volumeGate.Message()
                    + (expired ? " - the hold ran out, the avatar is no longer driven." : " - holding the last good body; no follow, no calibration."));
            } else {
                log.Log(LogLevel.Info, "Body operating volume: back in range - " + volumeGate.Summary());
            }
        }
    }
}
