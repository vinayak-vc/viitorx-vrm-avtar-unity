using System;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>Who starts the Python sender for one stream.</summary>
    public enum VisionSenderMode {
        Off = 0,
        Auto = 1,
        Always = 2
    }

    /// <summary>ONNX Runtime provider choice, as the senders' --provider.</summary>
    public enum VisionProvider {
        Auto = 0,
        Dml = 1,
        Cpu = 2
    }

    /// <summary>How the hand network's handedness label becomes Left or Right, as the hands sender's --sides.</summary>
    public enum HandSidesMode {
        Auto = 0,
        Model = 1,
        Swapped = 2
    }

    /// <summary>Which landmarks the face datagram carries, as the face sender's --landmarks.</summary>
    public enum FaceLandmarkMode {
        Full = 0,
        Mesh = 1,
        None = 2
    }

    /// <summary>Whether the cutout sender segments a crop around each person it detects or the whole picture, as its --roi.</summary>
    public enum CutoutRoiMode {
        Auto = 0,
        On = 1,
        Off = 2
    }

    /// <summary>How the camera picture travels in the cutout datagrams, as the cutout sender's --color.</summary>
    public enum CutoutColorMode {
        Rgba = 0,
        Jpeg = 1,
        None = 2
    }

    /// <summary>How the person mask travels in the cutout datagrams, as the cutout sender's --mask-enc.</summary>
    public enum CutoutMaskMode {
        Raw = 0,
        Deflate = 1
    }

    /// <summary>
    /// Every setting of the hands, face and cutout senders and receivers, in one serializable object. <c>AppBootstrap</c> holds one (the
    /// "Hands and Face" Inspector section), the runtime control panel edits it live, and <see cref="VisionSenderArguments"/> turns it into the
    /// senders' command lines. No behaviour lives here.
    /// </summary>
    [Serializable]
    public sealed class VisionSettings {
        [Header("Senders")]
        [Tooltip("Start the Python hands and face senders for you, with the camera and options below. Turn OFF to run python-sidecar~/scripts/run_hands_demo.bat or run_face_demo.bat by hand; the receivers still listen on the ports below.")]
        [SerializeField] private bool autoStartSenders = true;
        [Tooltip("Open the control panel (the F6 window) as soon as the app starts.")]
        [SerializeField] private bool showPanelOnStart = false;
        [Tooltip("The key that shows or hides the Hands and Face control panel at runtime.")]
        [SerializeField] private KeyCode panelKey = KeyCode.F6;

        [Header("Source: the camera both senders read")]
        [Tooltip("WHAT TO TRACK. camera:0 (or just 0) is a webcam; a phone appears there when DroidCam, Iriun or Camo exposes it. A LAN camera streaming viewer link (http://192.168.1.5:3000/viewer?room=room-abc123) is received over WebRTC; the PIN goes in Lan Camera Pin. rtsp:// and http://192.168.x.x/video streams, and a video file path or direct link, also work. ONE reader per camera: a webcam or a LAN viewer cannot be opened by the hands sender and the face sender at the same time, which is why the two modes below default to Auto (only the sender the current scene needs runs).")]
        [SerializeField] private string source = "camera:0";
        [Tooltip("LAN CAMERA ONLY. The access PIN of the LAN Camera Streaming server. It reaches the sender through its environment, never a command line or a log. The field is masked but saved into the scene as plain text: leave it empty and set the CAMERA_PIN environment variable if the PIN should not live in the project.")]
        [MaskedString]
        [SerializeField] private string lanCameraPin = string.Empty;
        [Tooltip("Mirror the picture before tracking, a selfie view. On for a webcam you face; it also mirrors the hand letters and the position X.")]
        [SerializeField] private bool flip = true;
        [Tooltip("Rotate each frame CLOCKWISE before tracking: 0, 90, 180 or 270. A phone held upright often arrives sideways.")]
        [SerializeField] private int rotateDegrees = 0;
        [Tooltip("ASSUMED horizontal field of view of the camera, degrees. It sets the scale of every metre in the position estimate; it is not measured.")]
        [Range(20f, 140f)]
        [SerializeField] private float horizontalFovDegrees = 60f;
        [Tooltip("Read the field of view as spanning the LONGER side of the picture, so a phone keeps one focal length in portrait and landscape.")]
        [SerializeField] private bool fovAcrossLongSide = false;
        [Tooltip("Requested webcam resolution. 1280x720 is plenty. Ignored for files and streams.")]
        [SerializeField] private Vector2Int cameraSize = new Vector2Int(1280, 720);
        [Tooltip("Requested webcam frame rate.")]
        [Range(5, 60)]
        [SerializeField] private int cameraFps = 30;
        [Tooltip("FILES ONLY. Replay the clip forever.")]
        [SerializeField] private bool loopFile = true;
        [Tooltip("ONNX Runtime provider for the networks. Auto = DirectML (GPU) then CPU. CPU is as fast for these small networks and has no 250 ms warm-up.")]
        [SerializeField] private VisionProvider provider = VisionProvider.Auto;
        [Tooltip("One-Euro smoothing of the landmarks. Blendshapes are always read from the raw landmarks and smoothed on their own.")]
        [SerializeField] private bool smoothing = true;
        [Tooltip("Open the sender's own OpenCV window (the picture with the result drawn on it). A diagnostic: it takes keyboard focus from the app.")]
        [SerializeField] private bool showPreviewWindow = false;

        [Header("Hands")]
        [Tooltip("Off: no receiver, no sender. Auto: the receiver always listens and the sender runs only while a scene that needs hands is loaded (the Hands showcase). Always: the sender runs whenever the app does.")]
        [SerializeField] private VisionSenderMode handsMode = VisionSenderMode.Auto;
        [Tooltip("UDP port of the hands wire. Must match hands_udp_sender.py --port.")]
        [SerializeField] private int handsPort = 8901;
        [Tooltip("Most hands tracked at once. The receiver has room for this many.")]
        [Range(1, 4)]
        [SerializeField] private int maxHands = 2;
        [Tooltip("Palm detector score needed to try a hand. Lower finds smaller or turned hands and adds false hands.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float palmConfidence = 0.5f;
        [Tooltip("Landmark confidence a NEW hand needs.")]
        [Range(0.05f, 0.99f)]
        [SerializeField] private float handConfidence = 0.6f;
        [Tooltip("Landmark confidence below which a tracked hand is lost.")]
        [Range(0.05f, 0.99f)]
        [SerializeField] private float handKeepConfidence = 0.4f;
        [Tooltip("Your hand size relative to an average adult hand; 1.1 = 10% larger. Scales the depth estimate: a hand 20% bigger than assumed reads 20% too near.")]
        [Range(0.6f, 1.6f)]
        [SerializeField] private float handScale = 1f;
        [Tooltip("How the network's handedness label becomes Left or Right. Auto follows the picture: a mirrored picture keeps the label, an unmirrored one swaps it. Check it by raising your RIGHT hand.")]
        [SerializeField] private HandSidesMode handSides = HandSidesMode.Auto;

        [Header("Face")]
        [Tooltip("Off: no receiver, no sender. Auto: the receiver always listens and the sender runs only while a scene that needs a face is loaded (the Face showcase). Always: the sender runs whenever the app does.")]
        [SerializeField] private VisionSenderMode faceMode = VisionSenderMode.Auto;
        [Tooltip("UDP port of the face wire. Must match face_udp_sender.py --port.")]
        [SerializeField] private int facePort = 8902;
        [Tooltip("Most faces tracked at once.")]
        [Range(1, 4)]
        [SerializeField] private int maxFaces = 1;
        [Tooltip("Detector score needed to try a face. The detector is SHORT RANGE: a face under about 10% of the frame is not found.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float faceDetectorConfidence = 0.5f;
        [Tooltip("Mesh presence a NEW face needs.")]
        [Range(0.05f, 0.99f)]
        [SerializeField] private float faceConfidence = 0.6f;
        [Tooltip("Mesh presence below which a tracked face is lost.")]
        [Range(0.05f, 0.99f)]
        [SerializeField] private float faceKeepConfidence = 0.4f;
        [Tooltip("The face eye-corner width relative to an average adult 0.09 m; 1.1 = 10% wider. Scales the depth estimate.")]
        [Range(0.6f, 1.6f)]
        [SerializeField] private float faceScale = 1f;
        [Tooltip("Run the blendshape network and send the 52 scores. Off is landmarks and head pose only (cheaper).")]
        [SerializeField] private bool blendshapes = true;
        [Tooltip("Which landmarks go in the datagram: Full (478, with irises), Mesh (468) or None. A full face is 13 KB a message; None is under 2 KB.")]
        [SerializeField] private FaceLandmarkMode faceLandmarks = FaceLandmarkMode.Full;

        [Header("Cutout (S-01): the person cut out of the camera picture")]
        [Tooltip("Off: no receiver, no sender. Auto: the receiver always listens and the sender runs only while a scene that shows the cutout is loaded (the Cutout showcase). Always: the sender runs whenever the app does. The sender reads the Source camera above, so a webcam cannot be shared with a running hands or face sender.")]
        [SerializeField] private VisionSenderMode cutoutMode = VisionSenderMode.Auto;
        [Tooltip("UDP port of the cutout wire. Must match cutout_udp_sender.py --port.")]
        [SerializeField] private int cutoutPort = 8903;
        [Tooltip("Auto: segment a crop around each person the detector finds when the detector's weights are present, else the whole picture. On: refuse to start without the detector. Off: whole picture only (right for a face filling a webcam, poor for someone far away).")]
        [SerializeField] private CutoutRoiMode cutoutRoi = CutoutRoiMode.Auto;
        [Tooltip("Most people cut out per frame.")]
        [Range(1, 8)]
        [SerializeField] private int cutoutMaxPeople = 4;
        [Tooltip("Person detector score needed to count a person. Lower finds smaller or partly hidden people and adds false ones.")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float cutoutDetectorConfidence = 0.4f;
        [Tooltip("Pixels on the mask's long side (it is never enlarged past the picture). 0 = the picture's own size. Smaller is faster and softer.")]
        [Range(0, 1280)]
        [SerializeField] private int cutoutMaskLongSide = 640;
        [Tooltip("Probability at or below which a pixel is background. With High it sets how soft the edge is.")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float cutoutEdgeLow = 0.5f;
        [Tooltip("Probability at or above which a pixel is fully the person.")]
        [Range(0.1f, 0.95f)]
        [SerializeField] private float cutoutEdgeHigh = 0.85f;
        [Tooltip("Snap the mask's edge to the picture's own edges (a guided filter). Costs about a millisecond; fixes a blocky outline.")]
        [SerializeField] private bool cutoutGuided = true;
        [Tooltip("Drop specks far from the person and fill pinholes inside.")]
        [SerializeField] private bool cutoutCleanup = true;
        [Tooltip("Average over frames to calm the flicker of the outline. Adds a little lag to a fast movement.")]
        [SerializeField] private bool cutoutTemporal = true;
        [Tooltip("Average each answer with the mirrored picture's: a cleaner mask for about 4 ms more per frame.")]
        [SerializeField] private bool cutoutHighQuality = false;
        [Tooltip("With nobody detected, segment the whole picture instead of sending an empty mask.")]
        [SerializeField] private bool cutoutWholePictureFallback = true;
        [Tooltip("Paint the other people out of each person's crop before the network looks, so two people standing apart are not joined by the dark background between them. Does nothing for one person.")]
        [SerializeField] private bool cutoutIsolatePeople = true;
        [Tooltip("How the picture travels. Rgba: raw, fastest on this machine (3.7 MB a frame at 720p). Jpeg: small, for a network; Unity decodes it on the main thread. None: the mask only (the Original and Person modes then cannot show the camera).")]
        [SerializeField] private CutoutColorMode cutoutColor = CutoutColorMode.Rgba;
        [Tooltip("JPEG quality, used with Jpeg.")]
        [Range(30, 100)]
        [SerializeField] private int cutoutJpegQuality = 85;
        [Tooltip("Raw is fastest. Deflate shrinks the mask for a network; Unity inflates it on the receive thread and allocates a little each frame.")]
        [SerializeField] private CutoutMaskMode cutoutMaskEncoding = CutoutMaskMode.Raw;
        [Tooltip("Shrink the picture that is sent so its long side is at most this. 0 = send it as it is. The mask follows its own setting.")]
        [Range(0, 3840)]
        [SerializeField] private int cutoutSendLongSide = 1280;
        [Tooltip("Payload bytes per datagram. 60000 fills a loopback datagram. About 1200 for a sender on a real network, where a larger datagram is split and lost whole.")]
        [Range(256, 65000)]
        [SerializeField] private int cutoutChunkBytes = 60000;
        [Tooltip("Mirror the picture in the sender, before it is cut out. Leave off: the Cutout view mirrors on the GPU, instantly. Turn on only when something else reads the cutout stream and needs it mirrored.")]
        [SerializeField] private bool cutoutFlipInSender = false;

        [Header("Receiver")]
        [Tooltip("Listen on every network interface instead of 127.0.0.1 only, so a sender on another machine can reach this app. Leave off unless you mean it: the wire has no authentication.")]
        [SerializeField] private bool acceptFromNetwork = false;
        [Tooltip("Seconds without a datagram after which a stream counts as stale.")]
        [Range(0.1f, 5f)]
        [SerializeField] private float staleSeconds = 0.5f;
        [Tooltip("Exchange Left and Right when the hands are handed to the humanoid retargeter, for a rig that is mirrored the other way.")]
        [SerializeField] private bool swapHandSides = false;

        public bool AutoStartSenders {
            get {
                return autoStartSenders;
            }
            set {
                autoStartSenders = value;
            }
        }

        public bool ShowPanelOnStart {
            get {
                return showPanelOnStart;
            }
            set {
                showPanelOnStart = value;
            }
        }

        public KeyCode PanelKey {
            get {
                return panelKey;
            }
            set {
                panelKey = value;
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

        public bool Flip {
            get {
                return flip;
            }
            set {
                flip = value;
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

        public float HorizontalFovDegrees {
            get {
                return horizontalFovDegrees;
            }
            set {
                horizontalFovDegrees = value;
            }
        }

        public bool FovAcrossLongSide {
            get {
                return fovAcrossLongSide;
            }
            set {
                fovAcrossLongSide = value;
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

        public int CameraFps {
            get {
                return cameraFps;
            }
            set {
                cameraFps = value;
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

        public VisionProvider Provider {
            get {
                return provider;
            }
            set {
                provider = value;
            }
        }

        public bool Smoothing {
            get {
                return smoothing;
            }
            set {
                smoothing = value;
            }
        }

        public bool ShowPreviewWindow {
            get {
                return showPreviewWindow;
            }
            set {
                showPreviewWindow = value;
            }
        }

        public VisionSenderMode HandsMode {
            get {
                return handsMode;
            }
            set {
                handsMode = value;
            }
        }

        public int HandsPort {
            get {
                return handsPort;
            }
            set {
                handsPort = value;
            }
        }

        public int MaxHands {
            get {
                return maxHands;
            }
            set {
                maxHands = value;
            }
        }

        public float PalmConfidence {
            get {
                return palmConfidence;
            }
            set {
                palmConfidence = value;
            }
        }

        public float HandConfidence {
            get {
                return handConfidence;
            }
            set {
                handConfidence = value;
            }
        }

        public float HandKeepConfidence {
            get {
                return handKeepConfidence;
            }
            set {
                handKeepConfidence = value;
            }
        }

        public float HandScale {
            get {
                return handScale;
            }
            set {
                handScale = value;
            }
        }

        public HandSidesMode HandSides {
            get {
                return handSides;
            }
            set {
                handSides = value;
            }
        }

        public VisionSenderMode FaceMode {
            get {
                return faceMode;
            }
            set {
                faceMode = value;
            }
        }

        public int FacePort {
            get {
                return facePort;
            }
            set {
                facePort = value;
            }
        }

        public int MaxFaces {
            get {
                return maxFaces;
            }
            set {
                maxFaces = value;
            }
        }

        public float FaceDetectorConfidence {
            get {
                return faceDetectorConfidence;
            }
            set {
                faceDetectorConfidence = value;
            }
        }

        public float FaceConfidence {
            get {
                return faceConfidence;
            }
            set {
                faceConfidence = value;
            }
        }

        public float FaceKeepConfidence {
            get {
                return faceKeepConfidence;
            }
            set {
                faceKeepConfidence = value;
            }
        }

        public float FaceScale {
            get {
                return faceScale;
            }
            set {
                faceScale = value;
            }
        }

        public bool Blendshapes {
            get {
                return blendshapes;
            }
            set {
                blendshapes = value;
            }
        }

        public FaceLandmarkMode FaceLandmarks {
            get {
                return faceLandmarks;
            }
            set {
                faceLandmarks = value;
            }
        }

        public VisionSenderMode CutoutMode {
            get {
                return cutoutMode;
            }
            set {
                cutoutMode = value;
            }
        }

        public int CutoutPort {
            get {
                return cutoutPort;
            }
            set {
                cutoutPort = value;
            }
        }

        public CutoutRoiMode CutoutRoi {
            get {
                return cutoutRoi;
            }
            set {
                cutoutRoi = value;
            }
        }

        public int CutoutMaxPeople {
            get {
                return cutoutMaxPeople;
            }
            set {
                cutoutMaxPeople = value;
            }
        }

        public float CutoutDetectorConfidence {
            get {
                return cutoutDetectorConfidence;
            }
            set {
                cutoutDetectorConfidence = value;
            }
        }

        public int CutoutMaskLongSide {
            get {
                return cutoutMaskLongSide;
            }
            set {
                cutoutMaskLongSide = value;
            }
        }

        public float CutoutEdgeLow {
            get {
                return cutoutEdgeLow;
            }
            set {
                cutoutEdgeLow = value;
            }
        }

        public float CutoutEdgeHigh {
            get {
                return cutoutEdgeHigh;
            }
            set {
                cutoutEdgeHigh = value;
            }
        }

        public bool CutoutGuided {
            get {
                return cutoutGuided;
            }
            set {
                cutoutGuided = value;
            }
        }

        public bool CutoutCleanup {
            get {
                return cutoutCleanup;
            }
            set {
                cutoutCleanup = value;
            }
        }

        public bool CutoutTemporal {
            get {
                return cutoutTemporal;
            }
            set {
                cutoutTemporal = value;
            }
        }

        public bool CutoutHighQuality {
            get {
                return cutoutHighQuality;
            }
            set {
                cutoutHighQuality = value;
            }
        }

        public bool CutoutWholePictureFallback {
            get {
                return cutoutWholePictureFallback;
            }
            set {
                cutoutWholePictureFallback = value;
            }
        }

        public bool CutoutIsolatePeople {
            get {
                return cutoutIsolatePeople;
            }
            set {
                cutoutIsolatePeople = value;
            }
        }

        public CutoutColorMode CutoutColor {
            get {
                return cutoutColor;
            }
            set {
                cutoutColor = value;
            }
        }

        public int CutoutJpegQuality {
            get {
                return cutoutJpegQuality;
            }
            set {
                cutoutJpegQuality = value;
            }
        }

        public CutoutMaskMode CutoutMaskEncoding {
            get {
                return cutoutMaskEncoding;
            }
            set {
                cutoutMaskEncoding = value;
            }
        }

        public int CutoutSendLongSide {
            get {
                return cutoutSendLongSide;
            }
            set {
                cutoutSendLongSide = value;
            }
        }

        public int CutoutChunkBytes {
            get {
                return cutoutChunkBytes;
            }
            set {
                cutoutChunkBytes = value;
            }
        }

        public bool CutoutFlipInSender {
            get {
                return cutoutFlipInSender;
            }
            set {
                cutoutFlipInSender = value;
            }
        }

        public bool AcceptFromNetwork {
            get {
                return acceptFromNetwork;
            }
            set {
                acceptFromNetwork = value;
            }
        }

        public float StaleSeconds {
            get {
                return staleSeconds;
            }
            set {
                staleSeconds = value;
            }
        }

        public bool SwapHandSides {
            get {
                return swapHandSides;
            }
            set {
                swapHandSides = value;
            }
        }

        /// <summary>A copy that shares nothing with this one, for "reset to the Inspector values".</summary>
        public VisionSettings Clone() {
            return JsonUtility.FromJson<VisionSettings>(JsonUtility.ToJson(this));
        }

        /// <summary>Overwrites every field with the other's.</summary>
        public void CopyFrom(VisionSettings other) {
            if (other == null) {
                return;
            }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
