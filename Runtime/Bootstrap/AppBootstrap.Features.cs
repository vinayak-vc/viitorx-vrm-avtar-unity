using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Features": face, hand, leg and position tracking switches and weights.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Features", Order = 70)]
        [SerializeField] private bool useFaceTracking = true;
        [SerializeField] private bool useHandTracking = true;
        [Tooltip("Wrist rotation weight (0 disables wrist rotation; fingers still curl). Live-tunable.")]
        [SerializeField] private float wristRotationWeight = 0f;
        [Tooltip("Track legs (off keeps legs straight when the lower body is occluded / out of frame).")]
        [SerializeField] private bool trackLegs = true;
        [Tooltip("Translate the avatar from the OAK-D measured hip position (walk/jump with the user).")]
        [SerializeField] private bool trackPosition = true;
        [SerializeField] private float positionScale = 1f;
    }
}
