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
    /// Everything about the Python sidecar the body path owns: which sender a scene needs, start, stop, the camera orientation toggle, and the live-edit watcher for the video source fields.
    /// </summary>
    public sealed partial class AppBootstrap {
        /// <summary>True when this component owns the sidecar: the sidecar path is on and it may start it.</summary>
        private bool SidecarManaged {
            get {
                return useOakUdpTracking && autoStartSidecar;
            }
        }

        /// <summary>
        /// THE RIGHT SENDER FOR THE SCENE BEING ENTERED. Called on every scene load.
        ///
        /// WHY THIS EXISTS. One sender cannot serve both jobs well. `multiperson_udp_sender.py` finds
        /// people with a detector and poses each one inside its own crop; that is what makes a crowd
        /// possible and it is strictly worse for a single person than the dedicated pipeline, which
        /// poses one body with F-21 ownership and the whole measured filter chain behind it. Running
        /// the crowd sender for a one-person scene was a real quality regression, reported as "multi
        /// person tracking gets started and that is not accurate for 1 person".
        ///
        /// WHAT IT COSTS. Switching means killing one python process and starting another, which is
        /// ~15-20 s of no tracking while RTMW3D loads. So it happens ONLY when the requirement
        /// actually changes: moving between the single-person and crowd columns. Navigating within a
        /// column, and passing back through the Launcher, cost nothing — which is why an unlisted
        /// scene is <see cref="SceneLauncher.TrackingNeed.Unspecified"/> and changes nothing rather
        /// than falling back to a default.
        /// </summary>
        private void EnsureSidecarForScene(string sceneName) {
            if (!SidecarManaged) {
                return;
            }
            SceneLauncher.TrackingNeed need = SceneLauncher.TrackingNeedFor(sceneName);
            if (need == SceneLauncher.TrackingNeed.Unspecified || need == SceneLauncher.TrackingNeed.VisionOnly) {
                return;
            }
            string wanted = need == SceneLauncher.TrackingNeed.MultiPerson
                ? sidecarMultiPersonSender
                : sidecarSinglePersonSender;
            if (trackFromVideo) {
                // F-49: one sender for every scene. There is no crowd video sender, so a crowd scene
                // does not trigger the 15-20 s restart that crossing columns costs on the camera.
                wanted = VideoSenderScript;
            }
            if (string.IsNullOrEmpty(wanted)) {
                return;
            }
            bool alive = sidecarLauncher != null
                         && (sidecarLauncher.IsOwnedProcessAlive
                             || sidecarLauncher.State == SidecarLaunchState.AttachedExternal);
            if (alive && string.Equals(activeSenderScript, wanted, StringComparison.Ordinal)) {
                return;
            }
            // Never restart a sidecar somebody else owns. Attaching means a person started it by
            // hand, or another Unity instance did; killing it to suit this scene would be a
            // surprise, and we cannot restart what we did not spawn.
            if (sidecarLauncher != null
                && sidecarLauncher.State == SidecarLaunchState.AttachedExternal) {
                logService.Log(LogLevel.Info, "Scene '" + sceneName + "' wants " + wanted
                        + ", but the running sidecar is not ours; leaving it alone.");
                return;
            }
            if (alive) {
                logService.Log(LogLevel.Info, "Scene '" + sceneName + "' needs " + wanted
                        + " (running: " + activeSenderScript + "). Restarting the sidecar — tracking "
                        + "will be absent for ~15 s while the model loads.");
                StopSidecar();
            }
            StartSidecar(wanted);
        }

        /// <summary>Is the camera mounted in portrait while THIS sender runs? Unknown senders fall
        /// back to the single-person answer, which is the one a hand-typed override is most likely
        /// to be.</summary>
        private bool PortraitForSender(string senderScript) {
            return string.Equals(senderScript, sidecarMultiPersonSender, StringComparison.Ordinal)
                ? sidecarMultiPersonPortrait
                : sidecarSinglePersonPortrait;
        }

        /// <summary>
        /// Flip the camera orientation for whatever sender is running, and restart it so the change
        /// takes effect. Bound to O in play mode.
        ///
        /// WHY A RESTART AND NOT A LIVE TOGGLE: the rotation is applied inside the Python sidecar —
        /// image, depth and intrinsics together (f18_portrait) — and there is no control channel
        /// from Unity into a running sidecar, only a command line. Restarting is the honest way to
        /// change a launch argument. It costs the same ~15 s as a column switch.
        ///
        /// This is for the moment somebody physically turns the camera on its mount and needs the
        /// software to agree, without stopping play mode to tick a box.
        /// </summary>
        private void ToggleSidecarOrientation() {
            if (trackFromVideo) {
                logService.Log(LogLevel.Info, "O (camera orientation) does nothing while tracking a video: "
                        + "there is no camera to turn.");
                return;
            }
            bool multi = string.Equals(activeSenderScript, sidecarMultiPersonSender,
                                       StringComparison.Ordinal);
            bool now;
            if (multi) {
                sidecarMultiPersonPortrait = !sidecarMultiPersonPortrait;
                now = sidecarMultiPersonPortrait;
            } else {
                sidecarSinglePersonPortrait = !sidecarSinglePersonPortrait;
                now = sidecarSinglePersonPortrait;
            }
            string which = multi ? "crowd" : "single-person";
            logService.Log(LogLevel.Info, "Camera orientation for " + which + " scenes is now "
                    + (now ? "PORTRAIT (" + sidecarPortraitDirection + ")" : "LANDSCAPE")
                    + ". Restarting the sidecar — tracking will be absent for ~15 s. This is a "
                    + "play-mode override; tick it on AppBootstrap to make it stick.");
            string sender = string.IsNullOrEmpty(activeSenderScript)
                ? sidecarSinglePersonSender
                : activeSenderScript;
            StopSidecar();
            StartSidecar(sender);
        }

        /// <summary>Stops the sidecar this instance spawned. Synchronous: the process tree is gone
        /// before this returns, so the producer probe in <see cref="StartSidecar"/> cannot see the
        /// one we just killed and mistake it for somebody else's.</summary>
        private void StopSidecar() {
            if (sidecarLauncher != null) {
                sidecarLauncher.Dispose();
                sidecarLauncher = null;
            }
            activeSenderScript = string.Empty;
        }

        /// <summary>
        /// Start the sidecar for the default case: ONE person. Both callers mean that — boot, which
        /// runs before any scene has said what it needs, and <see cref="StartTracking"/>, which only
        /// runs for `Mirror`. A crowd scene corrects this through
        /// <see cref="EnsureSidecarForScene"/> when it loads.
        /// </summary>
        private void StartSidecar() {
            StartSidecar(sidecarSinglePersonSender);
        }

        /// <summary>
        /// ADR-064: brings up the Python tracking sidecar so the user does not have to run it in a
        /// terminal. Failure here is deliberately non-fatal - the OAK-D provider still starts and
        /// the HUD reports why nothing is streaming, which is more useful than refusing to boot.
        /// </summary>
        private void StartSidecar(string senderScript) {
            // F-51: status lights for the sidecar, its watchdog, the source, the model and the tracking.
            SidecarStatusLights.Ensure();
            if (sidecarLauncher != null && (sidecarLauncher.IsOwnedProcessAlive || sidecarLauncher.State == SidecarLaunchState.AttachedExternal)) {
                return;
            }
            // F-49: resolve the video BEFORE anything is spawned, so a typo is one clear console line at
            // Play rather than a Python traceback in a log nobody opens.
            string resolvedVideo = string.Empty;
            if (trackFromVideo) {
                string videoError;
                if (!VideoSourceSpec.TryResolve(vision.Source, VideoProjectRoot(), out resolvedVideo,
                                                out videoError)) {
                    videoStatus = "VIDEO ERROR: " + videoError;
                    logService.Log(LogLevel.Error, "Video test source: " + videoError
                            + ". The sidecar was NOT started - fix 'Source' in the Hands and Face section of AppBootstrap.");
                    SnapshotVideoSettings();
                    return;
                }
                // F-50: a LAN camera cannot join the room without the PIN. Say so here, in one line, rather
                // than starting a sidecar that exits on it and is then restarted by the watchdog forever.
                if (SidecarGuards.LanCameraNeedsPin(resolvedVideo, vision.LanCameraPin)) {
                    videoStatus = "VIDEO ERROR: no LAN camera PIN";
                    logService.Log(LogLevel.Error, "Video test source: the LAN camera needs the server's access PIN. "
                            + "Fill in 'Lan Camera Pin' in the Hands and Face section of AppBootstrap (or set the "
                            + SidecarProcessLauncher.LanCameraPinEnvironmentVariable + " environment variable). "
                            + "The sidecar was NOT started.");
                    SnapshotVideoSettings();
                    return;
                }
                // F-52: several people need the detector weights, which are fetched once and not in version
                // control. Say so here in one line instead of starting a sidecar that exits on it and is then
                // restarted by the watchdog forever (the same reason as the PIN check above).
                if (VideoSourceSpec.ClampPeople(videoMaxPeople) > 1) {
                    string weights = VideoSourceSpec.DetectorWeightsPath(
                        SidecarLocator.Resolve(sidecarModelPathOverride).Root,
                        Environment.GetEnvironmentVariable(VideoSourceSpec.DetectorEnvironmentVariable));
                    if (string.IsNullOrEmpty(weights) || !File.Exists(weights)) {
                        videoStatus = "VIDEO ERROR: no person detector weights";
                        logService.Log(LogLevel.Error, "Video test source: tracking " + videoMaxPeople
                                + " people needs the person detector weights, which are not at '" + weights
                                + "'. Run  python-sidecar~/.venv/Scripts/python.exe tools/model/fetch_yolox_s.py  once "
                                + "(35.9 MB), or set 'Video Max People' to 1. The sidecar was NOT started.");
                        SnapshotVideoSettings();
                        return;
                    }
                }
                senderScript = VideoSenderScript;
            }
            if (SidecarGuards.ProducerAlreadyRunning(oakUdpPort)) {
                logService.Log(trackFromVideo ? LogLevel.Warning : LogLevel.Info,
                        "Something is already sending poses on UDP " + oakUdpPort
                        + "; attaching to it instead of starting a second sidecar."
                        + (trackFromVideo ? " The VIDEO source was requested but is NOT running - the "
                                            + "existing producer's feed is what you are seeing." : string.Empty));
                SnapshotVideoSettings();
                return;
            }
            SidecarLaunchOptions launchOptions = new SidecarLaunchOptions {
                AutoStart = autoStartSidecar,
                Host = "127.0.0.1",
                UdpPort = oakUdpPort,
                LockPort = sidecarLockPort,
                // PER SENDER, because the physical mount is what this describes and the two senders
                // are used with the camera in different positions: portrait for one person (more
                // pixels on one standing body), landscape for a crowd (more room, and the detector
                // reads upright people). Nothing here rotates anything on its own — it tells the
                // sidecar which way the camera already is.
                Portrait = PortraitForSender(senderScript),
                PortraitDirection = sidecarPortraitDirection,
                SubpixelBits = sidecarSubpixelBits,
                ModelPathOverride = sidecarModelPathOverride,
                // SUPERVISED, not direct. The multi-person sender used to be launched directly
                // because the supervisor could only build a command for the single-person one; it
                // now picks the command shape from the sender it is given, so the production path
                // gets the multi-person stream AND keeps restart-on-crash. Direct launch had no way
                // back from anything that ended the process.
                SupervisedSenderScript = senderScript,
                MaxPoses = sidecarMaxPoses,
                MonoResolution = sidecarMonoResolution,
                RgbIspScale = sidecarRgbIspScale,
                // The video sender has no preview window, and the supervisor does not forward --show to it.
                ExtraArguments = (sidecarShowPreview && !trackFromVideo) ? "--show" : string.Empty,
                VideoSource = resolvedVideo,
                LanCameraPin = vision.LanCameraPin,
                VideoLoopCount = vision.LoopFile ? 0 : 1,
                VideoAssumeMeasuredDepth = videoAssumeMeasuredDepth,
                VideoRotateDegrees = videoRotateDegrees,
                VideoAutoOrient = videoAutoOrient,
                VideoPartialBody = videoPartialBody,
                VideoFlipHorizontal = videoFlipHorizontal,
                VideoCameraWidth = vision.CameraSize.x,
                VideoCameraHeight = vision.CameraSize.y,
                VideoSubjectHeightMetres = Mathf.Clamp(videoSubjectHeightMetres, 0.5f, 2.5f),
                VideoHorizontalFovDegrees = Mathf.Clamp(videoHorizontalFovDegrees, 20f, 140f),
                VideoDepthMinMetres = videoClampDepth ? videoDepthRangeMetres.x : 0f,
                VideoDepthMaxMetres = videoClampDepth ? videoDepthRangeMetres.y : 0f,
                VideoMaxPeople = VideoSourceSpec.ClampPeople(videoMaxPeople)
            };
            sidecarLauncher = new SidecarProcessLauncher(logService, launchOptions);
            sidecarLauncher.Start(SidecarLocator.Resolve(sidecarModelPathOverride));
            SidecarStatusLights.Attach(sidecarLauncher);
            activeSenderScript = senderScript;
            videoStatus = trackFromVideo
                ? (VideoSourceSpec.IsLive(resolvedVideo) ? "LIVE " : "VIDEO ")
                  + VideoSourceSpec.DisplayName(resolvedVideo)
                : string.Empty;
            if (trackFromVideo) {
                logService.Log(LogLevel.Info, "Video test source: tracking "
                        + VideoSourceSpec.Redact(resolvedVideo) + " ("
                        + VideoSourceSpec.KindOf(resolvedVideo) + ") instead of the OAK-D camera. "
                        + "Estimated depth; "
                        + (VideoSourceSpec.ClampPeople(videoMaxPeople) > 1
                            ? "up to " + VideoSourceSpec.ClampPeople(videoMaxPeople) + " people; "
                            : "one person; ")
                        + (videoAssumeMeasuredDepth ? "depth labelled MEASURED." : "depth labelled inferred."));
            }
            SnapshotVideoSettings();
        }

        // ---- F-49 video source: live edits ------------------------------------------------------

        /// <summary>The folder that contains Assets. Relative video paths resolve against it, so
        /// Assets/Games/video/SR7.mp4 means what it says in the Project window.</summary>
        private static string VideoProjectRoot() {
            return Path.GetDirectoryName(Application.dataPath);
        }

        private void SnapshotVideoSettings() {
            appliedTrackFromVideo = trackFromVideo;
            appliedVideoSource = vision.Source;
            appliedLanCameraPin = vision.LanCameraPin;
            appliedVideoLoop = vision.LoopFile;
            appliedVideoAssumeMeasured = videoAssumeMeasuredDepth;
            appliedVideoSubjectHeight = videoSubjectHeightMetres;
            appliedVideoFov = videoHorizontalFovDegrees;
            appliedVideoClamp = videoClampDepth;
            appliedVideoRange = videoDepthRangeMetres;
            appliedVideoRotate = videoRotateDegrees;
            appliedVideoAutoOrient = videoAutoOrient;
            appliedVideoPartialBody = videoPartialBody;
            appliedVideoFlip = videoFlipHorizontal;
            appliedVideoCameraSize = vision.CameraSize;
            appliedVideoMaxPeople = videoMaxPeople;
            videoChangeSeconds = 0f;
        }

        private bool VideoSettingsDiffer() {
            return appliedTrackFromVideo != trackFromVideo
                || !string.Equals(appliedVideoSource, vision.Source, StringComparison.Ordinal)
                || !string.Equals(appliedLanCameraPin, vision.LanCameraPin, StringComparison.Ordinal)
                || appliedVideoLoop != vision.LoopFile
                || appliedVideoAssumeMeasured != videoAssumeMeasuredDepth
                || appliedVideoSubjectHeight != videoSubjectHeightMetres
                || appliedVideoFov != videoHorizontalFovDegrees
                || appliedVideoClamp != videoClampDepth
                || appliedVideoRange != videoDepthRangeMetres
                || appliedVideoRotate != videoRotateDegrees
                || appliedVideoAutoOrient != videoAutoOrient
                || appliedVideoPartialBody != videoPartialBody
                || appliedVideoFlip != videoFlipHorizontal
                || appliedVideoCameraSize != vision.CameraSize
                || appliedVideoMaxPeople != videoMaxPeople;
        }

        /// <summary>
        /// Notices an Inspector edit to the video fields during Play and restarts the sidecar for it
        /// once the value has been stable for <see cref="VideoApplyDelaySeconds"/>. Without it the
        /// boolean would only be read at boot, and "tick it and watch" would silently do nothing.
        /// A sidecar somebody else owns is never restarted - the same rule scene switching follows.
        /// </summary>
        private void WatchVideoSettings(float deltaSeconds) {
            if (!SidecarManaged || logService == null) {
                return;
            }
            // The video fields only mean something while tracking a video. Editing them with the OAK-D running
            // must not stop and restart its sidecar (~15 s without tracking) for a value nothing reads.
            if (!trackFromVideo && !appliedTrackFromVideo) {
                if (VideoSettingsDiffer()) {
                    SnapshotVideoSettings();
                }
                return;
            }
            if (!VideoSettingsDiffer()) {
                videoChangeSeconds = 0f;
                return;
            }
            videoChangeSeconds = videoChangeSeconds + deltaSeconds;
            if (videoChangeSeconds < VideoApplyDelaySeconds) {
                return;
            }
            if (sidecarLauncher != null && sidecarLauncher.State == SidecarLaunchState.AttachedExternal) {
                logService.Log(LogLevel.Info, "Video settings changed, but the running sidecar is not ours; "
                        + "leaving it alone.");
                SnapshotVideoSettings();
                return;
            }
            logService.Log(LogLevel.Info, "Video settings changed - restarting the sidecar "
                    + "(tracking will be absent for ~10 s while the model loads).");
            StopSidecar();
            if (trackFromVideo) {
                StartSidecar(VideoSenderScript);
                return;
            }
            string sceneName = SceneManager.GetActiveScene().name;
            if (SceneLauncher.TrackingNeedFor(sceneName) == SceneLauncher.TrackingNeed.Unspecified) {
                StartSidecar(sidecarSinglePersonSender);
            } else {
                EnsureSidecarForScene(sceneName);
            }
            SnapshotVideoSettings();
        }
    }
}
