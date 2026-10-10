using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Tracking Source": which tracker feeds the avatar (OAK-D sidecar, Sentis, MediaPipe), its UDP port, and how the sidecar is started.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Tracking Source", Order = 10, ExpandedByDefault = true)]
        [Tooltip("OAK-D depth camera via the Python sidecar over UDP — the production path. Takes priority over the other providers when it starts.")]
        [SerializeField] private bool useOakUdpTracking = true; // B2 (Python sidecar over UDP) — ADR-016
        [SerializeField] private int oakUdpPort = 8899;
        [Tooltip("Start the Python tracking sidecar automatically (ADR-064). Turn OFF to run sidecar_supervisor.py by hand. Either way, an already-running supervisor is detected via its lock port and never double-started.")]
        [SerializeField] private bool autoStartSidecar = true;
        [Tooltip("Must match sidecar_supervisor.py --lock-port. Used to detect an existing supervisor rather than spawning a second producer onto the same UDP port.")]
        [SerializeField] private int sidecarLockPort = 8897;
        [Tooltip("Sender for scenes that need ONE person (every SinglePerson scene, plus Mirror). The dedicated single-person pipeline: RTMW3D whole-body with hands, F-21 ownership and the measured filter chain. Not the multi-person sender capped to one — that keeps a detector and a crop stage this does not need.")]
        [SerializeField] private string sidecarSinglePersonSender = "wholebody_udp_sender.py";
        [Tooltip("Sender for scenes that need TWO OR MORE people. Only this one produces a crowd.")]
        [SerializeField] private string sidecarMultiPersonSender = "multiperson_udp_sender.py";
        [Tooltip("People posed per frame by the multi-person sender. Ignored by the single-person one.")]
        [SerializeField] private int sidecarMaxPoses = 3;
        [Tooltip("Launch the sidecar with --show so an OpenCV preview window displays what the camera sees and tracks. OFF by default: it is a diagnostic window, it takes keyboard focus away from the app, and the production path should not depend on one being open.")]
        [SerializeField] private bool sidecarShowPreview = false;
        [Tooltip("Leave empty to use the packaged model path. In the editor that resolves to Assets/SentisModel/rtmw3d-x.onnx.")]
        [SerializeField] private string sidecarModelPathOverride = "";
        [Tooltip("The OAK-D is physically rotated 90° while running SINGLE-person scenes. Portrait puts the sensor's long axis vertical, which is more pixels on one standing person. Press O in play mode to flip this for whichever sender is running.")]
        [SerializeField] private bool sidecarSinglePersonPortrait = true;
        [Tooltip("The OAK-D is physically rotated 90° while running CROWD scenes. Landscape is usually right here: it covers more of the room side-to-side, and the person detector runs on the unrotated camera stream, so a rotated mount makes it read people lying sideways.")]
        [SerializeField] private bool sidecarMultiPersonPortrait = false;
        [Tooltip("Which way the camera is rotated, seen from behind it. Applies to whichever sender is in portrait.")]
        [SerializeField] private string sidecarPortraitDirection = "ccw";
        [SerializeField] private int sidecarSubpixelBits = 3;

        [Tooltip("F-44 WORKING VOLUME. Stereo mono resolution. 800p doubles the focal length in " +
                 "pixels, so f*B doubles and depth error HALVES at every range. 400p is the " +
                 "pre-F-44 default. Measured cost of 800p + native RGB together: 30.0 -> 29.2 fps.")]
        [SerializeField] private string sidecarMonoResolution = "800p";

        [Tooltip("F-44 WORKING VOLUME. RGB ISP scale. 1/1 keeps the sensor's native 1280x800 at " +
                 "FULL field of view (verified: FOV unchanged, fy 284.6 -> 569.3), which doubles " +
                 "the pixels on a body and therefore doubles the distance at which the pose model " +
                 "still sees the same detail. 1/2 is the pre-F-44 640x400. Costs no GPU time - " +
                 "RTMW3D always resizes its crop to 288x384.")]
        [SerializeField] private string sidecarRgbIspScale = "1/1";
        [Tooltip("Use the sample video file instead of a live webcam (ignored on the OAK-D path).")]
        [SerializeField] private bool useVideoSource = false;
        [Tooltip("MediaPipe pose on the webcam/video (CPU). Fallback when OAK/Sentis are off.")]
        [SerializeField] private bool useMediaPipeTracking = true;
    }
}
