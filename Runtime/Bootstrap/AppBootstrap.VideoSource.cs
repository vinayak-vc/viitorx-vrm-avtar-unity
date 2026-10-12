using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Video Test Source (F-49)": a video, webcam, phone or LAN camera in place of the OAK-D, plus how many people to track from it.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Video Test Source (F-49)", Order = 20, ExpandedByDefault = true)]
        [Tooltip("RUN THE SAME TRACKING ON A VIDEO INSTEAD OF THE OAK-D. Tick this, paste a link or path " +
                 "into 'Video Source', press Play. Nothing downstream changes: the sidecar sends the " +
                 "same UDP datagrams, so every scene, the skeleton show and the VRM avatar run exactly " +
                 "as they do live. Unrelated to 'Use Video Source' above, which feeds MediaPipe on a " +
                 "webcam path and is ignored on the OAK-D path.\n\n" +
                 "Changing any field below while playing restarts the sidecar after a second (the model " +
                 "reloads, ~10 s). Needs 'Use Oak Udp Tracking' and 'Auto Start Sidecar' on.\n\n" +
                 "ONE PERSON ONLY: the crowd detector runs on the OAK-D, so crowd scenes see a crowd of one. " +
                 "NO STEREO: depth is estimated, so the TRUST HUD reads 0/33 measured and the calibration " +
                 "never becomes ready unless 'Video Assume Measured Depth' is on.")]
        [SerializeField] private bool trackFromVideo = false;
        [Tooltip("WHAT TO TRACK. One of:\n" +
                 "  * a path - absolute, or relative to the project folder: Assets/Games/video/SR7.mp4\n" +
                 "  * a direct http(s) link to a video file (.mp4 .webm .mov .mkv .avi .m4v) - downloaded once, cached\n" +
                 "  * camera:0 (or just 0) - a WEBCAM. A phone appears here when an app such as DroidCam, " +
                 "Iriun or Camo exposes it to Windows. Try camera:1, camera:2 if 0 is your laptop camera.\n" +
                 "  * http://192.168.1.5:8080/video - a phone 'IP camera' app over Wi-Fi (MJPEG). An http link " +
                 "to a private/LAN address is read as a live camera, not a web page.\n" +
                 "  * rtsp://user:pass@host/stream - an IP camera. Credentials are never logged.\n" +
                 "  * http://192.168.1.5:3000/viewer?room=room-abc123 - a phone streaming through the LAN CAMERA " +
                 "STREAMING server (its sender page), received over WebRTC. Paste the VIEWER link; the access " +
                 "PIN goes in 'Lan Camera Pin' below, never in the link. Close any browser viewer open in that " +
                 "room first: the server allows one viewer.\n" +
                 "  * a web page link (YouTube...) - needs yt-dlp in the sidecar venv, which is not installed.\n" +
                 "Live sources (camera, stream, LAN camera) process only the NEWEST frame, so lag cannot build " +
                 "up, and reconnect on their own if the phone drops off.")]
        [SerializeField] private string videoSource = "Assets/Games/video/SR7.mp4";
        [Tooltip("LAN CAMERA ONLY. The access PIN of the LAN Camera Streaming server (its ACCESS_PIN). Used " +
                 "when 'Video Source' is a viewer link; ignored for everything else.\n\n" +
                 "It reaches the sidecar through its ENVIRONMENT, so it is never on a command line or in a " +
                 "log. The field is masked, but it is still saved into the scene as plain text: leave it " +
                 "empty and set the CAMERA_PIN environment variable on this machine if the PIN should not " +
                 "live in the project. The field wins when both are set.")]
        [MaskedString]
        [SerializeField] private string lanCameraPin = string.Empty;
        [Tooltip("FILES ONLY. Replay the clip forever. Off: it plays once and then the sidecar idles, so " +
                 "tracking goes stale rather than the clip restarting. A camera or stream has no end.")]
        [SerializeField] private bool videoLoop = true;
        [Tooltip("PORTRAIT OR LANDSCAPE, UPRIGHT OR SIDEWAYS: FIND OUT BY ITSELF. A phone on the LAN camera, a " +
                 "webcam or a clip can arrive portrait or landscape, and a webcam app may hand over a sideways or " +
                 "upside-down picture. With this on the sidecar tries the four rotations on a few frames (a person " +
                 "has to be in view), turns the picture upright, and repeats whenever the frame changes size (you " +
                 "turn the phone) or nothing has been tracked for a few seconds. It also uses one focal length for " +
                 "both orientations of the same lens, so the person is the same size and distance in portrait and " +
                 "landscape. 'Video Horizontal Fov Degrees' then means the field of view across the LONGER side " +
                 "of the picture. In landscape a person standing to one side is found by the person detector instead " +
                 "of waiting for them to cross the middle.\n\n" +
                 "Needs the detector weights (tools/model/fetch_yolox_s.py); without them the sidecar says so and " +
                 "uses 'Video Rotate Degrees'. Off = exactly the old behaviour.")]
        [SerializeField] private bool videoAutoOrient = true;
        [Tooltip("HEAD AND SHOULDERS ONLY IS FINE. A laptop webcam shows the upper body, not the hips or the legs. " +
                 "With this on the sidecar decides frame by frame what anchors the skeleton - the hips while they are " +
                 "in the picture, then the shoulders, then the nose - so a person is still tracked when the hips are " +
                 "out of frame, and it carries on if the hips are lost in the middle of a session. A joint outside the " +
                 "picture is reported as absent (never invented), depth comes from the shoulder or face width when " +
                 "there are no ankles, and the scenes treat the shoulders as the torso. The root is then an ESTIMATE, " +
                 "so the operating volume's near limit is relaxed to 'Volume Partial Near Metres'.\n\n" +
                 "Off = the old rule: both hips confident or the frame is dropped.")]
        [SerializeField] private bool videoPartialBody = true;
        [Tooltip("Rotate each frame CLOCKWISE before tracking. A phone held upright is often delivered as a " +
                 "sideways frame by webcam apps; the model reads the image, so it must be turned upright " +
                 "here. 0, 90, 180 or 270. With 'Video Auto Orient' on this is only the starting guess and the " +
                 "fallback when nobody is in view.")]
        [SerializeField] private int videoRotateDegrees = 0;
        [Tooltip("Mirror each frame left-right before tracking. A FRONT (selfie) camera usually arrives " +
                 "mirrored, which swaps the person's left and right. Note this is separate from the scenes' " +
                 "own mirror setting, which is a display choice.")]
        [SerializeField] private bool videoFlipHorizontal = false;
        [Tooltip("Requested webcam resolution. 1280x720 is plenty: the pose model resizes its crop of the " +
                 "body to 288x384, so more pixels add nothing and cost bandwidth. 0 keeps the default. " +
                 "Ignored for files and streams.")]
        [SerializeField] private Vector2Int videoCameraSize = new Vector2Int(1280, 720);
        [Tooltip("Label every joint's depth as MEASURED on the wire. LABEL ONLY - no coordinate changes. " +
                 "Lets the F-48 calibration run on a source with no stereo. The TRUST HUD will then say " +
                 "33/33 MEASURED about a video, so do not quote a recording made with this on as evidence " +
                 "about the sensor.")]
        [SerializeField] private bool videoAssumeMeasuredDepth = false;
        [Tooltip("Assumed height of the person in the clip, metres. The hip depth is estimated from how tall " +
                 "they look in the frame, so this moves the clip nearer or further and decides whether the " +
                 "F-48 operating volume calls it too close.")]
        [SerializeField] private float videoSubjectHeightMetres = 1.7f;
        [Tooltip("Assumed horizontal field of view of the camera that shot the clip, degrees. Unknown for " +
                 "an arbitrary clip; only scales the pose.")]
        [SerializeField] private float videoHorizontalFovDegrees = 60f;
        [Tooltip("Clamp the estimated hip depth into the range below. ON by default so a clip's own framing " +
                 "does not read as TOO CLOSE / TOO FAR and hide the body. Turn OFF to test the F-48 " +
                 "operating volume against the clip.")]
        [SerializeField] private bool videoClampDepth = true;
        [Tooltip("Min and max hip depth, metres, when clamping. Keep inside the operating volume (1.35-4.5).")]
        [SerializeField] private Vector2 videoDepthRangeMetres = new Vector2(1.6f, 4.0f);
        [Tooltip("HOW MANY PEOPLE to track from this source. 1 is the single-person path (one crop, no detector). " +
                 "2 or more runs a person detector on the GPU and tracks each person with a stable id, so a phone, " +
                 "webcam or video can drive several skeletons. Measured on an RTX 3060: 2 people ~35 fps, 3 people " +
                 "~26 fps, 5 people ~17 fps (the camera is 30), so keep it at the number of people you really have.\n\n" +
                 "Needs the detector weights once: run tools/model/fetch_yolox_s.py in python-sidecar~ (35.9 MB). " +
                 "Which scenes show several people is decided by the scene you open; a single-person scene shows the " +
                 "first person only.")]
        [Range(1, 8)]
        [SerializeField] private int videoMaxPeople = 1;

        /// <summary>F-49: the video sender, relative to the sidecar root. It lives under tools/ because
        /// it is a harness-grade sender (ADR-065 keeps the root for the camera path); the supervisor
        /// selects its command shape from the basename.</summary>
        private const string VideoSenderScript = "tools/video/video_udp_sender.py";

        /// <summary>How long the video fields must sit unchanged before the sidecar is restarted for
        /// them. The URL is edited a keystroke at a time and every keystroke is a field change; a
        /// restart costs ~10 s of model load, so it waits for the typing to stop.</summary>
        private const float VideoApplyDelaySeconds = 1.0f;

        // F-49: what the RUNNING sidecar was launched with, so a live edit is noticed. Compared field
        // by field rather than building a key string, because this runs every frame.
        private bool appliedTrackFromVideo;
        private string appliedVideoSource = string.Empty;
        private string appliedLanCameraPin = string.Empty;
        private bool appliedVideoLoop;
        private bool appliedVideoAssumeMeasured;
        private float appliedVideoSubjectHeight;
        private float appliedVideoFov;
        private bool appliedVideoClamp;
        private Vector2 appliedVideoRange;
        private int appliedVideoRotate;
        private bool appliedVideoAutoOrient;
        private bool appliedVideoPartialBody;
        private bool appliedVideoFlip;
        private Vector2Int appliedVideoCameraSize;
        private int appliedVideoMaxPeople;
        private float videoChangeSeconds;
        private string videoStatus = string.Empty;
    }
}
