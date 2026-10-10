using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Pose Mapping": axis flips and the camera mount pitch.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Pose Mapping", Order = 60)]
        [SerializeField] private bool poseFlipX = false;
        [SerializeField] private bool poseFlipY = true;
        [SerializeField] private bool poseFlipZ = false;

        [Tooltip("F-43 CAMERA MOUNT PITCH, degrees, POSITIVE WHEN THE CAMERA IS PITCHED DOWN " +
                 "(nose toward the floor). 0 = level, and nothing changes at 0.\n\n" +
                 "Measure the magnitude with python-sidecar~/tools/capture/f19_level.py, which reads " +
                 "the board's own IMU and writes tilt_deg to evidence/oak_v4/f19/mount.json. Read " +
                 "the DIRECTION off the bracket by eye - mount.json records magnitude only and says " +
                 "so. A wrong sign doubles the error instead of removing it, so leave this at 0 " +
                 "rather than guessing.\n\n" +
                 "Without it a pitched camera shears the floor by tan(angle) metres per metre of " +
                 "depth - 0.27 m/m at 15 deg - and the floor is a single scalar, so the skeleton " +
                 "sinks as someone walks toward the camera and rises as they walk away. This is the " +
                 "one setting that makes a deliberately angled mount usable.")]
        [SerializeField] private float cameraTiltDegrees = 0f;
    }
}
