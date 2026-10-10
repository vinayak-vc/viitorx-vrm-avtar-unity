using UnityEngine;
using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Operating Volume (F-48)": the camera-to-hip distance band outside which a pose is refused.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Operating Volume (F-48)", Order = 50)]
        [Tooltip("F-48: judge every pose by its camera-to-hip distance and refuse the ones outside the " +
                 "range the camera can measure. Outside the range the last good body is held, the " +
                 "avatar does not follow the hip and the calibration learns nothing; after the hold " +
                 "the avatar stays where it was and the state is reported. Off = the pre-F-48 " +
                 "behaviour (every pose is used), kept as the A/B baseline.\n\n" +
                 "WHY: SR7 spent minutes at 0.9-1.3 m, below F-44's usable 1.4-4.5 m. Nothing in the " +
                 "transport was wrong; the depth behind the pose was.")]
        [SerializeField] private bool useOperatingVolume = true;
        [Tooltip("Nearest hip distance, metres, at which depth is trusted. F-44 measured the usable " +
                 "band as ~1.4-4.5 m; 1.35 m leaves a 5 cm margin.")]
        [SerializeField] private float volumeNearMetres = OperatingVolume.DefaultNearLimitMetres;
        [Tooltip("Furthest hip distance, metres, at which depth is trusted. F-44's 4.5 m is " +
                 "arithmetic (pixels on target), not a capture - the 3-4 m recording is what confirms it.")]
        [SerializeField] private float volumeFarMetres = OperatingVolume.DefaultFarLimitMetres;
        [Tooltip("Seconds the last good body is held after the subject leaves the range.")]
        [SerializeField] private float volumeHoldSeconds = OperatingVolume.DefaultHoldSeconds;
        [Tooltip("Consecutive in-range poses required before the calibration may learn again.")]
        [SerializeField] private int volumeSettlePoses = OperatingVolume.DefaultSettlePoses;
    }
}
