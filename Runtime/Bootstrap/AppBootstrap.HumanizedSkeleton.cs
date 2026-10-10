using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Humanized Skeleton (F-27)": the humanized skeleton layer: bone lengths, joint limits, velocity and re-acquire behaviour.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Humanized Skeleton (F-27)", Order = 40)]
        [Tooltip("F-27: run the tracked pose through the humanized skeleton before the retarget sees it - " +
                 "fixed bone lengths, anatomical joint limits, teleport rejection, per-joint hold and a " +
                 "smooth re-acquire. Tracking data is treated as a SUGGESTION. Off = the raw filtered " +
                 "pose goes straight to the retarget, which is the pre-F-27 behaviour and the A/B baseline.")]
        [SerializeField] private bool useHumanizedSkeleton = true;
        [Tooltip("F-27: 'was this joint emitted at all', NOT 'is it good enough to drive a limb'. The " +
                 "sidecar zero-fills a dropped joint to confidence 0. The quality question belongs to " +
                 "P0-1's limbConfidenceThreshold downstream; setting this to the same value would preempt " +
                 "a gate that already works.")]
        [SerializeField] private float humanizedConfidenceFloor = 0.05f;
        [Tooltip("F-27: seconds to ease a returning joint back in. Exists so a re-acquire does not SNAP, " +
                 "not to smooth - longer reads as the avatar lagging its own tracking on every recovery.")]
        [SerializeField] private float humanizedRecoverSeconds = 0.15f;
        [Tooltip("F-27 A/B: reject/clamp joints that move faster than their class can.")]
        [SerializeField] private bool humanizeVelocity = true;
        [Tooltip("F-27 A/B: hold every bone at its calibrated length (the learned skeleton).")]
        [SerializeField] private bool humanizeBoneLengths = true;
        [Tooltip("F-27 A/B: enforce anatomical joint limits (elbow/knee hinge, hip cone, neck cone, " +
                 "torso twist, knees not bending forwards).")]
        [SerializeField] private bool humanizeAngles = true;
    }
}
