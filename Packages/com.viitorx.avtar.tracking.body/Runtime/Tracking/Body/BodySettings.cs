using System;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Body {
    /// <summary>
    /// Every setting of the body path in one serializable object: where the engine is, what it watches, which wire the body arrives on and how the avatar
    /// follows it. <c>BodyBootstrap</c> holds one; <see cref="BodyEngineArguments"/> turns the engine and input parts into the <c>body</c> command line.
    /// The defaults are the ones the Virtual Mirror application ships with. No behaviour lives here.
    /// </summary>
    [Serializable]
    public sealed class BodySettings {
        [Header("Engine")]
        [Tooltip("Start the tracking engine's body sender for you. Turn OFF to run `viitorx-tracking body` yourself; the receiver still listens on the port below.")]
        [SerializeField] private bool autoStartEngine = true;
        [Tooltip("The tracking engine (viitorx-tracking.exe, or the folder that holds it). Empty: look for it in this order: the environment variable VIITORX_TRACKING_ENGINE, the machine-wide install (the installer), %LOCALAPPDATA%/Viitorx/TrackingEngine/<version>, StreamingAssets/Viitorx/TrackingEngine. A path typed here that does not hold the engine is an error, not a reason to look elsewhere. The body path runs from the compiled engine only (it needs the body model, rtmw3d-x-fp16.onnx, in the engine's models folder).")]
        [SerializeField] private string enginePath = string.Empty;

        [Header("Input")]
        [Tooltip("WHAT TO TRACK. camera:0 (or just 0) is a webcam; a LAN camera viewer link is received over WebRTC (the PIN goes in Lan Camera Pin); rtsp:// and http://192.168.x.x/video streams, and a video file path or direct link, also work. A webcam can be opened by ONE program: the body path cannot share it with the hands, face or cutout senders of the vision package.")]
        [SerializeField] private string source = "camera:0";
        [Tooltip("LAN CAMERA ONLY. The access PIN of the LAN Camera Streaming server. It reaches the engine through its environment, never a command line or a log. The field is saved into the scene as plain text: leave it empty and set the CAMERA_PIN environment variable if the PIN should not live in the project.")]
        [MaskedString]
        [SerializeField] private string lanCameraPin = string.Empty;
        [Tooltip("FILES ONLY. Replay the clip forever.")]
        [SerializeField] private bool loopFile = true;
        [Tooltip("Rotate each frame CLOCKWISE before tracking: 0, 90, 180 or 270. Ignored while Auto Orient is on.")]
        [SerializeField] private int rotateDegrees = 0;
        [Tooltip("Find the upright rotation of each picture by itself, so the same phone works in portrait and landscape (and read the field of view across the longer side).")]
        [SerializeField] private bool autoOrient = true;
        [Tooltip("Mirror the picture before tracking.")]
        [SerializeField] private bool flipHorizontal = false;
        [Tooltip("Track a person whose hips are not in the picture (a laptop webcam: head and shoulders) with an estimated hip, instead of nobody.")]
        [SerializeField] private bool partialBody = true;
        [Tooltip("ASSUMED height of the person, metres. It sets the scale of every metre in the position estimate; it is not measured.")]
        [SerializeField] private float subjectHeightMetres = 1.7f;
        [Tooltip("ASSUMED horizontal field of view of the camera, degrees.")]
        [SerializeField] private float horizontalFovDegrees = 60f;
        [Tooltip("Requested webcam resolution. Ignored for files and streams.")]
        [SerializeField] private Vector2Int cameraSize = new Vector2Int(1280, 720);
        [Tooltip("Label the estimated depth as MEASURED, so the calibration may learn from it. Only for a source whose depth really is measured.")]
        [SerializeField] private bool assumeMeasuredDepth = false;
        [Tooltip("Keep the estimated depth of the person inside the range below, the band the estimate is trustworthy in.")]
        [SerializeField] private bool clampDepth = true;
        [Tooltip("The nearest and furthest distance of the person from the camera, metres, when Clamp Depth is on.")]
        [SerializeField] private Vector2 depthRangeMetres = new Vector2(1.6f, 4f);

        [Header("Body wire")]
        [Tooltip("UDP port of the body wire. Must match `viitorx-tracking body --port`.")]
        [SerializeField] private int port = 8899;
        [Tooltip("Presentation delay (ms) of the timestamped pose buffer: the avatar shows the pose interpolated at (now - this), so frames between two datagrams show real in-between motion. 0 = latest pose wins.")]
        [SerializeField] private float poseInterpolationDelayMs = 40f;
        [Tooltip("Seconds without a new pose after which the avatar stops following it. The engine's own stale failsafe (2 s) releases the avatar to its neutral pose.")]
        [SerializeField] private float poseStaleSeconds = 0.5f;

        [Header("Following")]
        [Tooltip("Turn the avatar this many degrees about the vertical axis when it is loaded, BEFORE it is bound. The retargeting works in the avatar's model frame, where the avatar looks toward -Z; a VRM loads looking toward +Z, so 180 (what the Virtual Mirror application does to every avatar) puts it right. With 0 the arms cross the body. Put the camera in front of the avatar: at -Z looking toward +Z.")]
        [SerializeField] private float avatarYawDegrees = 180f;
        [Tooltip("Mirror the avatar: negate the X axis of the tracked pose.")]
        [SerializeField] private bool mirror = false;
        [Tooltip("Camera mount pitch, degrees, POSITIVE WHEN THE CAMERA IS PITCHED DOWN. 0 = level. Published as the process-wide CameraMount.TiltDegrees.")]
        [SerializeField] private float cameraTiltDegrees = 0f;
        [Tooltip("Track legs (off keeps the legs straight when the lower body is out of frame).")]
        [SerializeField] private bool trackLegs = true;
        [Tooltip("Drive the avatar's fingers from the hands of the body stream.")]
        [SerializeField] private bool trackHands = true;
        [Tooltip("Translate the avatar from the measured or estimated hip position (walk and jump with the person).")]
        [SerializeField] private bool trackPosition = true;
        [Tooltip("Scale of the avatar's translation.")]
        [SerializeField] private float positionScale = 1f;
        [Tooltip("How fast the avatar's position follows the hip (1/s).")]
        [SerializeField] private float positionSmoothing = 12f;
        [Tooltip("Run the tracked pose through the humanized skeleton (fixed bone lengths, joint limits, teleport rejection) before the retarget sees it.")]
        [SerializeField] private bool useHumanizedSkeleton = true;
        [Tooltip("A limb accepts a fresh solve only when its weakest joint's confidence is at least this; below it the limb holds its last rotation. 0 disables the gate.")]
        [SerializeField] private float limbConfidenceThreshold = 0.3f;
        [Tooltip("Waist forward-bend from depth (0 = upright, 1 = full).")]
        [SerializeField] private float spineBendScale = 1f;

        public bool AutoStartEngine {
            get {
                return autoStartEngine;
            }
            set {
                autoStartEngine = value;
            }
        }

        public string EnginePath {
            get {
                return enginePath;
            }
            set {
                enginePath = value;
            }
        }

        public string Source {
            get {
                return source;
            }
            set {
                source = value;
            }
        }

        public string LanCameraPin {
            get {
                return lanCameraPin;
            }
            set {
                lanCameraPin = value;
            }
        }

        public bool LoopFile {
            get {
                return loopFile;
            }
            set {
                loopFile = value;
            }
        }

        public int RotateDegrees {
            get {
                return rotateDegrees;
            }
            set {
                rotateDegrees = value;
            }
        }

        public bool AutoOrient {
            get {
                return autoOrient;
            }
            set {
                autoOrient = value;
            }
        }

        public bool FlipHorizontal {
            get {
                return flipHorizontal;
            }
            set {
                flipHorizontal = value;
            }
        }

        public bool PartialBody {
            get {
                return partialBody;
            }
            set {
                partialBody = value;
            }
        }

        public float SubjectHeightMetres {
            get {
                return subjectHeightMetres;
            }
            set {
                subjectHeightMetres = value;
            }
        }

        public float HorizontalFovDegrees {
            get {
                return horizontalFovDegrees;
            }
            set {
                horizontalFovDegrees = value;
            }
        }

        public Vector2Int CameraSize {
            get {
                return cameraSize;
            }
            set {
                cameraSize = value;
            }
        }

        public bool AssumeMeasuredDepth {
            get {
                return assumeMeasuredDepth;
            }
            set {
                assumeMeasuredDepth = value;
            }
        }

        public bool ClampDepth {
            get {
                return clampDepth;
            }
            set {
                clampDepth = value;
            }
        }

        public Vector2 DepthRangeMetres {
            get {
                return depthRangeMetres;
            }
            set {
                depthRangeMetres = value;
            }
        }

        public int Port {
            get {
                return port;
            }
            set {
                port = value;
            }
        }

        public float PoseInterpolationDelayMs {
            get {
                return poseInterpolationDelayMs;
            }
            set {
                poseInterpolationDelayMs = value;
            }
        }

        public float PoseStaleSeconds {
            get {
                return poseStaleSeconds;
            }
            set {
                poseStaleSeconds = value;
            }
        }

        public float AvatarYawDegrees {
            get {
                return avatarYawDegrees;
            }
            set {
                avatarYawDegrees = value;
            }
        }

        public bool Mirror {
            get {
                return mirror;
            }
            set {
                mirror = value;
            }
        }

        public float CameraTiltDegrees {
            get {
                return cameraTiltDegrees;
            }
            set {
                cameraTiltDegrees = value;
            }
        }

        public bool TrackLegs {
            get {
                return trackLegs;
            }
            set {
                trackLegs = value;
            }
        }

        public bool TrackHands {
            get {
                return trackHands;
            }
            set {
                trackHands = value;
            }
        }

        public bool TrackPosition {
            get {
                return trackPosition;
            }
            set {
                trackPosition = value;
            }
        }

        public float PositionScale {
            get {
                return positionScale;
            }
            set {
                positionScale = value;
            }
        }

        public float PositionSmoothing {
            get {
                return positionSmoothing;
            }
            set {
                positionSmoothing = value;
            }
        }

        public bool UseHumanizedSkeleton {
            get {
                return useHumanizedSkeleton;
            }
            set {
                useHumanizedSkeleton = value;
            }
        }

        public float LimbConfidenceThreshold {
            get {
                return limbConfidenceThreshold;
            }
            set {
                limbConfidenceThreshold = value;
            }
        }

        public float SpineBendScale {
            get {
                return spineBendScale;
            }
            set {
                spineBendScale = value;
            }
        }
    }
}
