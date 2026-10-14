using System;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

using UniVRM10;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Camera;
using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Diagnostics;
using VirtualMirror.IK;
using VirtualMirror.IO;
using VirtualMirror.Rendering;
using VirtualMirror.Retargeting;
using VirtualMirror.Settings;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Tracking;
using VirtualMirror.Tracking.Filtering;
using VirtualMirror.Tracking.MediaPipe;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Tracking.Sentis;
using VirtualMirror.UI;

namespace VirtualMirror.App {
    /// <summary>
    /// The per-frame body pipeline of the avatar path: stream failsafe, filter, humanize, operating volume, retarget, face, hands, fingers, and the debug skeleton. Behaviour only; the settings are declared in the other partials.
    /// </summary>
    public sealed partial class AppBootstrap {
        private bool IsPoseStale(PoseFrame frame) {
            if (frame == null || !frame.IsValid) {
                return true;
            }
            double timestamp = frame.TimestampSeconds;
            if (timestamp != lastFrameTimestamp) {
                lastFrameTimestamp = timestamp;
                lastFreshTime = Time.unscaledTime;
            }
            return (Time.unscaledTime - lastFreshTime) > poseStaleSeconds;
        }

        /// <summary>
        /// One LateUpdate of the avatar path. Each stage below is its own method so the order is readable here and
        /// each stage can be understood (and tested) alone: stream failsafe, bind the avatar, then body, face, hands,
        /// and the control-rig runtime last.
        /// </summary>
        private void UpdateTracking() {
            if (bodyProvider == null || retargeter == null || jointFilter == null) {
                return;
            }
            float deltaSeconds = Time.deltaTime;
            if (bodyProvider is SentisPoseProvider) {
                // Push the amplitude knobs live so they can be tuned in the Inspector during Play.
                SentisPoseProvider sentisProvider = (SentisPoseProvider)bodyProvider;
                sentisProvider.SetTuning(sentisMetreScale, sentisDepthScale);
            }
            bodyProvider.Tick(deltaSeconds);
            bool streamFailsafe = UpdateStreamFailsafe(deltaSeconds);
            if (Input.GetKeyDown(KeyCode.C)) {
                RecalibrateAll();
            }

            Animator animator = null;
            if (avatarSession != null && avatarSession.Current != null) {
                animator = avatarSession.Current.Animator;
            }
            if (animator == null) {
                return;
            }
            if (animator != boundAnimator) {
                BindAvatar(animator);
            }
            // ADR-022 whole-body: when on + the control rig is present, it owns the whole skeleton (FK/IK +
            // hand-curl bypassed). Requires the avatar to have loaded WITH the control rig (useKalidokitBody
            // set before Play).
            bool kalidokitBodyActive = useKalidokitBody && kalidokitControlRig != null && kalidokitControlRig.IsBound;
            PoseFrame frame;
            if (!streamFailsafe && bodyProvider.TryGetLatestFrame(out frame)) {
                ApplyBodyFrame(frame, deltaSeconds, kalidokitBodyActive);
            }
            ApplyFace(deltaSeconds);
            HandFrame kaliHandFrame;
            bool haveKaliHandFrame = ApplyHands(animator, deltaSeconds, kalidokitBodyActive, out kaliHandFrame);
            FinishControlRigFrame(kalidokitBodyActive, haveKaliHandFrame, kaliHandFrame);
        }

        // ---- F-20A: is the stream actually LIVE, not merely receiving? --------------------------
        // F-19 measured ReceivedCount climbing and ParseErrors at 0 while the avatar rendered a
        // 208-second-old pose. The old response to staleness was to stop calling Apply, which IS
        // the freeze. When the stream is dead beyond the failsafe threshold the avatar is eased to
        // its neutral pose instead, and no tracking pose is consumed this frame. Returns whether the
        // failsafe is active.
        private bool UpdateStreamFailsafe(float deltaSeconds) {
            bool streamFailsafe = false;
            if (bodyProvider is OakDUdpPoseProvider) {
                streamFailsafe = ((OakDUdpPoseProvider)bodyProvider).State == TrackingState.StaleFailsafe;
            }
            if (streamFailsafe) {
                if (!streamFailsafeActive) {
                    streamFailsafeActive = true;
                    if (kalidokitControlRig != null) {
                        kalidokitControlRig.ResetHoldState();
                    }
                    if (humanizedSkeleton != null) {
                        // F-27: the stream died, so the learned skeleton and every held direction belong
                        // to a session that is over. Carrying them into the next one would dress the next
                        // person in the last person's bone lengths.
                        humanizedSkeleton.Reset();
                        lastHumanizedFrame = null;
                        volumeGate.Reset();
                    }
                    logService.Log(LogLevel.Warning,
                        "F-20A: tracking stream stale beyond the failsafe threshold - releasing the avatar "
                        + "to its neutral pose (the last tracked pose is NOT held indefinitely).");
                }
                if (kalidokitControlRig != null && kalidokitControlRig.IsBound) {
                    kalidokitControlRig.ReleaseToRest(deltaSeconds / StreamRestReleaseSeconds);
                    kalidokitControlRig.ProcessRuntime();
                }
            } else if (streamFailsafeActive) {
                streamFailsafeActive = false;
                logService.Log(LogLevel.Info,
                    "F-20A: tracking stream recovered - resuming pose application from the neutral pose.");
            }
            return streamFailsafe;
        }

        private void RecalibrateAll() {
            retargeter.Recalibrate();
            hasPositionNeutral = false;
            if (handRetargeter != null) {
                handRetargeter.Recalibrate();
            }
            if (kalidokitControlRig != null) {
                kalidokitControlRig.Recalibrate(); // re-capture the wrist neutral palm
            }
        }

        // The avatar changed (first load or a hot swap): bind every retargeter to its rig.
        private void BindAvatar(Animator animator) {
            retargeter.Bind(animator);
            retargeter.SetTrackLegs(trackLegs);
            if (kalidokitControlRig != null) {
                // ADR-022 whole-body: bind the normalized control rig (null unless loaded with it).
                UniVRM10.Vrm10Instance vrmForRig = avatarSession.Current.Root != null ? avatarSession.Current.Root.GetComponent<UniVRM10.Vrm10Instance>() : null;
                kalidokitControlRig.Bind(vrmForRig);
            }
            // H3: the avatar was hot-swapped - unbind the face + hand retargeters too so the guarded
            // `!IsBound` rebinds below re-target the NEW avatar's rig instead of the disposed one.
            if (handRetargeter != null) {
                handRetargeter.Unbind();
            }
            if (expressionRetargeter != null) {
                expressionRetargeter.Unbind();
            }
            Transform avatarRoot = avatarSession.Current.Root != null ? avatarSession.Current.Root.transform : null;
            avatarRootTransform = avatarRoot;
            if (avatarRootTransform != null) {
                avatarRootInitialPosition = avatarRootTransform.position;
            }
            hasPositionNeutral = false;
            positionCurrentOffset = Vector3.zero;
            if (ikSolver != null) {
                ikSolver.Bind(animator, avatarRoot);
                ikSolver.SetLegTracking(trackLegs);
            }
            boundAnimator = animator;
        }

        // M3/M18: the OAK sidecar already smooths (One-Euro + outlier gate) at the source, so running Unity's
        // One-Euro again would double-filter and add lag; its frames pass through unfiltered.
        // M10: for the MediaPipe/Sentis paths TryGetLatestFrame returns the SAME cached frame on every render
        // tick, so filtering each Update (~60 Hz) over ~30 Hz data mistuned the One-Euro derivative (dt = render
        // delta, not the inter-sample delta). Filter ONLY on a genuinely-new frame (timestamp changed), using the
        // timestamp delta, and reuse the last filtered buffer on repeats.
        private PoseFrame FilterFrame(PoseFrame frame, float deltaSeconds) {
            if (bodyProvider is OakDUdpPoseProvider) {
                return frame;
            }
            double frameTimestamp = frame.TimestampSeconds;
            if (lastFilteredFrame == null || frameTimestamp != lastFilterTimestamp) {
                float sampleDelta = deltaSeconds;
                if (lastFilteredFrame != null && frameTimestamp > lastFilterTimestamp) {
                    sampleDelta = (float)(frameTimestamp - lastFilterTimestamp);
                }
                lastFilteredFrame = jointFilter.Filter(frame, sampleDelta);
                lastFilterTimestamp = frameTimestamp;
            }
            return lastFilteredFrame;
        }

        // ---- F-27 HUMANIZED SKELETON -------------------------------------------------------
        // tracking -> filtering -> [HERE] -> retarget. Fixed bone lengths, anatomical joint limits, teleport
        // rejection, per-joint hold in LOCAL space and a smooth re-acquire. Confidence passes through untouched,
        // so P0-1's LimbGate downstream still sees an unobserved limb as unobserved.
        //
        // RUN ONCE PER POSE, NOT ONCE PER RENDER FRAME. TryGetLatestFrame returns the SAME cached pose on every
        // render tick, and the editor renders at ~100 fps over a ~30 Hz stream - so processing per tick would run
        // this layer ~3x per pose with dt = the RENDER delta. Its temporal stages would then advance three times
        // per observation, which MEASURED as the humanized stream jumping MORE between frames than the raw one
        // (0.046 m vs 0.014 m). Same trap as the One-Euro note above, fixed the same way: key off the timestamp and
        // use the INTER-POSE delta.
        private PoseFrame HumanizeFrame(PoseFrame filtered, float deltaSeconds, bool volumeHold) {
            if (!useHumanizedSkeleton || humanizedSkeleton == null) {
                return filtered;
            }
            double poseTimestamp = filtered.TimestampSeconds;
            if (lastHumanizedFrame == null || poseTimestamp != lastHumanizeTimestamp) {
                float poseDelta = deltaSeconds;
                if (lastHumanizedFrame != null && poseTimestamp > lastHumanizeTimestamp) {
                    poseDelta = (float)(poseTimestamp - lastHumanizeTimestamp);
                }
                humanizedSkeleton.SetTuning(humanizedConfidenceFloor, humanizedRecoverSeconds);
                humanizedSkeleton.SetStages(humanizeVelocity, humanizeBoneLengths, humanizeAngles);
                lastHumanizedFrame = humanizedSkeleton.Process(filtered, poseDelta,
                    hasHumanizeTelemetry ? humanizeTelemetry : null, volumeGate.Admission);
                lastHumanizeTimestamp = poseTimestamp;
                if (HumanizeTap != null && !volumeHold) {
                    // Tapped inside the new-pose branch on purpose: a recording keyed to render frames would
                    // count the same pose several times and report a jump rate that belongs to the display,
                    // not the tracker.
                    HumanizeTap(filtered, lastHumanizedFrame, poseDelta);
                }
            }
            return lastHumanizedFrame;
        }

        private void ApplyBodyFrame(PoseFrame frame, float deltaSeconds, bool kalidokitBodyActive) {
            PoseFrame filtered = FilterFrame(frame, deltaSeconds);
            // The debug skeleton is drawn from the RAW filtered pose, deliberately and permanently. Its whole
            // diagnostic value is that it shows what the TRACKER said, so that "the skeleton is right and the
            // avatar is not" stays a meaningful sentence (F-26). Drawing the humanized pose here would make the
            // skeleton agree with the avatar by construction and destroy the only independent reference.
            RenderDebugSkeleton(filtered);

            // ---- F-48 OPERATING VOLUME: is the subject inside the range the camera can measure? Everything
            // below - the humanized layer, the retarget and the root follow - obeys the verdict; the debug
            // skeleton above is drawn before it so what the tracker said stays visible when the answer is
            // "do not use it".
            volumeGate.Update(frame, deltaSeconds);
            hasHumanizeTelemetry = bodyProvider is OakDUdpPoseProvider
                && ((OakDUdpPoseProvider)bodyProvider).TryGetTelemetry(humanizeTelemetry);
            ReportVolumeTransitions();
            bool volumeHold = volumeGate.Admission == PoseAdmission.Outside;

            PoseFrame humanized = HumanizeFrame(filtered, deltaSeconds, volumeHold);
            // May this pose drive the avatar? Not once the subject has been out of range longer than the hold:
            // the avatar then stays exactly where it last was, which is the honest answer, rather than being
            // posed from data the camera cannot supply. Inside the hold it is fed the humanized layer's held
            // body (re-applying an unchanged pose is a no-op); with humanize OFF there is no layer to hold, so
            // the raw pose must not be applied at all.
            bool applyBody = !volumeGate.Expired
                && (!volumeHold || (useHumanizedSkeleton && humanizedSkeleton != null));
            if (kalidokitBodyActive) {
                ApplyKalidokitBody(filtered, humanized, applyBody);
            } else {
                ApplyLegacyBody(humanized, applyBody);
            }
            // Physical movement (walk/jump): drive the avatar ROOT from the OAK measured hip xyz on BOTH retarget
            // paths. F-48: ONLY from an in-range pose. A 0.66 m hip drove the avatar toward the camera in SR7; the
            // offset is neutral-relative, so a single out-of-range sample would also have been smoothed in and
            // then slowly walked back, which reads as a lurch.
            if (!volumeHold) {
                ApplyRootPosition(frame, deltaSeconds);
            }
        }

        // ADR-022 whole-body: the normalized control rig owns the entire skeleton, so release FK + IK (they must
        // not write raw bones the control rig would overwrite each Process).
        private void ApplyKalidokitBody(PoseFrame filtered, PoseFrame humanized, bool applyBody) {
            retargeter.SetArmsLegsDrivenExternally(true);
            retargeter.SetArmsExternallyDriven(true);
            if (ikSolver != null && ikSolver.IsBound) {
                ikSolver.SetActive(false);
            }
            if (!applyBody || IsPoseStale(humanized)) {
                return;
            }
            kalidokitControlRig.SetTuning(kalidokitBodyEulerSigns, kalidokitBodyFlipQuat, kalidokitBodyLerp, kalidokitBodyLegs, kalidokitBodyMirror, kalidokitBodyTorsoRoll);
            kalidokitControlRig.SetSpineBend(kalidokitSpineBendScale);
            kalidokitControlRig.SetSpineBendDynamics(kalidokitSpineBendBaselineTau);
            kalidokitControlRig.SetTorsoYawScale(kalidokitTorsoYawScale);
            kalidokitControlRig.SetLimbConfidence(limbConfidenceThreshold); // P0-1 live-tunable gate
            kalidokitControlRig.SetUseAimArms(kalidokitAimArms);            // ARM RETARGET V1 (live A/B)
            kalidokitControlRig.SetDirectionTrace(kalidokitDirectionTrace, kalidokitDirectionTraceEveryFrames);
            // F-26 + F-27: the fidelity metric measures against the RAW tracked pose, not against whatever drove
            // the avatar. With the humanized layer on, those differ - and scoring the avatar against its own input
            // would flatter that layer by construction. Set before Apply so a sample taken this frame sees this
            // frame's reference.
            kalidokitControlRig.SetFidelityReference(filtered);
            kalidokitControlRig.Apply(humanized);
        }

        // FK and IK are mutually exclusive on the arm/leg bones: when the IK solver is active and bound it owns
        // the limbs, so FK drives only torso (hips/spine) + neck. Otherwise FK drives everything and the solver is
        // released so the rig stops deforming the limbs.
        private void ApplyLegacyBody(PoseFrame humanized, bool applyBody) {
            bool ikActive = useIkDriver && ikSolver != null && ikSolver.IsBound;
            retargeter.SetArmsLegsDrivenExternally(ikActive);
            // FK owns the arms on this legacy path (the Kalidokit whole-body path drives arms via the control rig).
            retargeter.SetArmsExternallyDriven(false);
            if (ikSolver != null && ikSolver.IsBound) {
                ikSolver.SetActive(useIkDriver);
            }
            if (applyBody && !IsPoseStale(humanized)) {
                retargeter.Apply(humanized, retargetMinConfidence, retargetExitConfidence);
                if (ikActive) {
                    ikSolver.Apply(humanized, retargetMinConfidence);
                }
            }
        }

        private void ApplyFace(float deltaSeconds) {
            if (useFaceTracking && faceProvider != null && expressionRetargeter != null) {
                faceProvider.Tick(deltaSeconds);
                if (avatarSession != null && avatarSession.Current != null && avatarSession.Current.Root != null) {
                    UniVRM10.Vrm10Instance vrmInstance = avatarSession.Current.Root.GetComponent<UniVRM10.Vrm10Instance>();
                    if (vrmInstance != null && !expressionRetargeter.IsBound) {
                        expressionRetargeter.Bind(vrmInstance);
                    }
                }
                FaceFrame faceFrame;
                if (faceProvider.TryGetFrame(out faceFrame)) {
                    expressionRetargeter.Apply(faceFrame);
                }
            } else if (expressionRetargeter != null && expressionRetargeter.IsBound) {
                // Face tracking toggled off: reset expressions to neutral so the face does not freeze.
                expressionRetargeter.Apply(null);
            }
        }

        // Hands. Legacy path: the hand-curl retargeter drives the fingers. Kalidokit path (ADR-022 fingers + ADR-023
        // wrist): the control rig owns the skeleton, so its fingers are driven from the provider's curls BEFORE
        // ProcessRuntime and its WRIST AFTER it, on the raw hand bone; the frame is returned so the wrist can reuse
        // it. Returns true when such a frame exists.
        private bool ApplyHands(Animator animator, float deltaSeconds, bool kalidokitBodyActive, out HandFrame kaliHandFrame) {
            kaliHandFrame = null;
            if (!kalidokitBodyActive && useHandTracking && handProvider != null && handRetargeter != null) {
                handProvider.Tick(deltaSeconds);
                // ADR-021: push the wrist weight every frame so the serialized wristRotationWeight is live-tunable
                // during Play (0 disables wrist rotation, fingers still curl).
                handRetargeter.SetWristWeight(wristRotationWeight);
                if (!handRetargeter.IsBound) {
                    handRetargeter.Bind(animator);
                }
                HandFrame handFrame;
                if (handProvider.TryGetFrame(out handFrame)) {
                    handRetargeter.Apply(handFrame);
                }
            } else if (!kalidokitBodyActive && handRetargeter != null && handRetargeter.IsBound) {
                // LOW-A: hand tracking toggled off - relax fingers to the open rest pose so they don't freeze
                // mid-curl (mirrors the face-tracking reset).
                handRetargeter.ResetToOpen();
            }

            if (kalidokitBodyActive && useHandTracking && handProvider != null && kalidokitControlRig != null) {
                handProvider.Tick(deltaSeconds);
                kalidokitControlRig.SetFingerTuning(kalidokitFingerCurlAxis, kalidokitFingerWeight);
                kalidokitControlRig.SetWristWeight(wristRotationWeight); // live-tunable; 0 disables the wrist
                if (handProvider.TryGetFrame(out kaliHandFrame)) {
                    kalidokitControlRig.ApplyFingers(kaliHandFrame);
                    return true;
                }
            }
            return false;
        }

        // ADR-022 whole-body: the VRM is set to manual update while the control rig owns it, so run its runtime
        // ONCE here - AFTER the body pose + face expressions + fingers were written this frame - to apply
        // everything (control rig -> skeleton, springbones, expressions). Without this the avatar stays in T-pose.
        private void FinishControlRigFrame(bool kalidokitBodyActive, bool haveKaliHandFrame, HandFrame kaliHandFrame) {
            if (!kalidokitBodyActive || kalidokitControlRig == null) {
                return;
            }
            kalidokitControlRig.ProcessRuntime();
            // ADR-023: the WRIST bend is applied to the RAW hand bone AFTER Process (Process would overwrite a
            // control-rig write). Roll-free swing from the OAK hand stream's palm forward.
            if (haveKaliHandFrame) {
                kalidokitControlRig.ApplyWrist(kaliHandFrame);
            }
            if (pipelineLogging) {
                WriteModelLog();
            }
        }

        // Live line-rendered debug skeleton of the RAW tracked pose, drawn beside the avatar so tracking can
        // be validated by eye against the retargeted VRM (the user's request). Lazily created; toggled live by
        // showDebugSkeleton. Purely diagnostic — no effect on the avatar.
        private void RenderDebugSkeleton(PoseFrame frame) {
            if (!showDebugSkeleton) {
                if (debugSkeleton != null && debugSkeleton.gameObject.activeSelf) {
                    debugSkeleton.gameObject.SetActive(false);
                }
                return;
            }
            if (debugSkeleton == null) {
                GameObject skeletonObject = new GameObject("PoseDebugSkeleton");
                debugSkeleton = skeletonObject.AddComponent<PoseDebugSkeleton>();
                Vector3 basePosition = avatarRootTransform != null ? avatarRootInitialPosition : Vector3.zero;
                skeletonObject.transform.position = basePosition + debugSkeletonOffset;
                skeletonObject.transform.localScale = Vector3.one * debugSkeletonScale;
            }
            if (!debugSkeleton.gameObject.activeSelf) {
                debugSkeleton.gameObject.SetActive(true);
            }
            debugSkeleton.Render(frame);
        }

        // F-48: say so, once per change, in the log AND on the diagnostics overlay. A held or withheld
        // avatar beside a perfectly healthy UDP stream looks exactly like a broken tracker, so the
        // reason has to be on screen and in the log at the moment it begins - never only in a counter.
        private void ReportVolumeTransitions() {
            bool outside = volumeGate.Admission == PoseAdmission.Outside;
            bool expired = volumeGate.Expired;
            if (outside == volumeWasOutside && expired == volumeWasExpired) {
                return;
            }
            volumeWasOutside = outside;
            volumeWasExpired = expired;
            string message = outside ? volumeGate.Message() : null;
            if (logService != null) {
                if (outside) {
                    logService.Log(LogLevel.Warning, "F-48 operating volume: " + message
                        + (expired ? " - hold ran out, the avatar is no longer driven."
                                   : " - holding the last good body; no follow, no calibration."));
                } else {
                    logService.Log(LogLevel.Info,
                        "F-48 operating volume: back in range - " + volumeGate.Summary());
                }
            }
            if (diagnosticsHud != null) {
                diagnosticsHud.SetTrackingStatus(DescribeActiveTracking()
                    + (message != null ? "  |  " + message : string.Empty));
            }
        }

        // Physical movement: translate the avatar root by the OAK-measured mid-hip position (neutral-relative,
        // scaled, exponentially smoothed) so the avatar walks/jumps with the user. Shared by both retarget
        // paths (Milestone-2). No-op when the frame carries no measured root (RGB paths) or trackPosition off.
        private void ApplyRootPosition(PoseFrame frame, float deltaSeconds) {
            if (!trackPosition || avatarRootTransform == null || frame == null || !frame.HasRootPosition) {
                return;
            }
            if (!hasPositionNeutral) {
                positionNeutralHip = frame.RootPositionMetres;
                hasPositionNeutral = true;
            }
            Vector3 targetOffset = (frame.RootPositionMetres - positionNeutralHip) * positionScale;
            float lerpT = 1f - Mathf.Exp(-positionSmoothing * deltaSeconds);
            positionCurrentOffset = Vector3.Lerp(positionCurrentOffset, targetOffset, lerpT);
            avatarRootTransform.position = avatarRootInitialPosition + positionCurrentOffset;
        }
    }
}
