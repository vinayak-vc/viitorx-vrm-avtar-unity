using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Kalidokit Body — active whole-body retarget (ADR-022/023)": the whole-body Kalidokit retarget: axes, smoothing, spine, fingers, arm solver, limb gate and pose interpolation.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Kalidokit Body — active whole-body retarget (ADR-022/023)", Order = 30)]
        [Tooltip("Drive the whole skeleton via the VRM normalized control rig. MUST be set BEFORE Play (the control rig is generated at load).")]
        [SerializeField] private bool useKalidokitBody = true;
        [Tooltip("Global three.js->Unity handedness conversion (0..3). 2 is correct for this rig.")]
        [SerializeField] private int kalidokitBodyFlipQuat = 2;
        [SerializeField] private Vector3 kalidokitBodyEulerSigns = Vector3.one;
        [Tooltip("Smoothing toward the solved pose (0..1).")]
        [SerializeField] private float kalidokitBodyLerp = 0.5f;
        [Tooltip("Reflect the input skeleton (negate X + swap L/R). Leave OFF — reflecting the input twists a rotation retarget (ADR-023); a true mirror must be done output-side.")]
        [SerializeField] private bool kalidokitBodyMirror = false;
        [Tooltip("Torso side-lean amount (0 = upright; 1 = full side-lean tracking).")]
        [SerializeField] private float kalidokitBodyTorsoRoll = 1f;
        [Tooltip("Waist forward-bend from OAK depth (0 = off/upright, 1 = full; raise for a more pronounced bend). Flip the sign if it bends the wrong way. Press C standing upright to re-seed. Live-tunable.")]
        [SerializeField] private float kalidokitSpineBendScale = 1f;
        [Tooltip("Waist-bend baseline time-constant (s). Larger holds a sustained bend longer but corrects the distance/systematic lean slower; very large ~= a fixed neutral (ADR-026). Live-tunable.")]
        [SerializeField] private float kalidokitSpineBendBaselineTau = 8f;
        [Tooltip("Body-turn amount (ADR-027): 0 = frontal-lock (default — a mirror is frontal, and turning is unreliable past ~2 m where depth degrades → false 'facing away'). Raise toward 1 ONLY when standing close (<~1.8 m) if you want body-turn. Live-tunable.")]
        [Range(0f, 1f)]
        [SerializeField] private float kalidokitTorsoYawScale = 1f;
        [SerializeField] private bool kalidokitBodyLegs = true;
        [Tooltip("Normalized-bone flexion axis for the curl-driven fingers.")]
        [SerializeField] private Vector3 kalidokitFingerCurlAxis = new Vector3(0f, 0f, -1f);
        [Tooltip("Finger curl weight (0 disables fingers).")]
        [SerializeField] private float kalidokitFingerWeight = 1f;
        [Tooltip("P1-3: presentation delay (ms) for the timestamped pose buffer. Unity renders the pose " +
                 "interpolated at (now - this), so render frames between two datagrams show real " +
                 "in-between motion instead of the same pose re-applied ~11.7x. This is the ONLY " +
                 "latency P1-3 adds. 0 = disabled (original latest-wins behaviour). Start small: one " +
                 "packet interval at ~21 fps is ~48 ms, so 35-50 ms usually always has a future sample.")]
        [SerializeField] private float poseInterpolationDelayMs = 40f;

        [Tooltip("ARM RETARGET V1: solve the arms with the quaternion aim solver (elbow-plane roll) instead of " +
                 "the Kalidokit Euler branch. The audit measured the Euler branch at 28.2 deg mean / 47.5 deg " +
                 "max direction error with 20.2 deg L/R asymmetry on symmetric input; the aim solver measures " +
                 "0.0 deg on the same fixtures. Untick to A/B against the old path or to roll back. " +
                 "Live-tunable.")]
        [SerializeField] private bool kalidokitAimArms = true;

        [Tooltip("DIAG-ONLY (retargeting audit §12): log, per traced bone, the WANTED bone direction (from the " +
                 "landmarks the solver consumed) next to the ACHIEVED normalized-bone direction plus the error " +
                 "angle. Turns 'the skeleton looks right but the avatar looks wrong' into a number. Drives no " +
                 "behaviour. Leave OFF except while auditing — it is chatty.")]
        [SerializeField] private bool kalidokitDirectionTrace = false;
        [Tooltip("Throttle for the direction trace: log every Nth applied pose frame (~21 fps, so 60 is ~3 s).")]
        [SerializeField] private int kalidokitDirectionTraceEveryFrames = 60;

        [Tooltip("P0-1: a limb (arm/leg) accepts a fresh Kalidokit solve only when its weakest driving joint's " +
                 "confidence is at least this; below it, the limb HOLDS its last valid rotation instead of " +
                 "collapsing toward the origin on an invalid/occluded joint. 0 disables the gate.")]
        [SerializeField] private float limbConfidenceThreshold = 0.3f;
    }
}
