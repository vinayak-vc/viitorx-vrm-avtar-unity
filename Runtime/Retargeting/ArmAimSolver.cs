using UnityEngine;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// The rest orientation of one arm, measured ONCE from the VRM normalized control rig at bind time —
    /// never hardcoded, so an A-pose or non-standard rig works without a new calibration system.
    /// All directions are in the CONTROL-RIG REST FRAME (see <see cref="ArmAimSolver"/>).
    /// </summary>
    public struct ArmRestBasis {
        public Vector3 UpperDir;    // upperArm -> lowerArm at rest
        public Vector3 LowerDir;    // lowerArm -> hand at rest
        public Vector3 BendNormal;  // the elbow hinge axis at rest (see ArmAimSolver.BuildRestBasis)
        public bool Valid;
    }

    /// <summary>
    /// Per-arm roll continuity state. The elbow plane is the roll reference, and a straight arm has no
    /// elbow plane — so the last well-determined normal is carried across those frames instead of letting
    /// the roll snap to an arbitrary value. Reset on bind / recalibrate.
    ///
    /// V2 (docs/UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md §2) adds the hysteresis latch and the
    /// flip-confirmation counter that together stop the elbow plane chattering around a single bend
    /// threshold. All fields are value types — the struct stays allocation-free on the per-frame path.
    /// </summary>
    public struct ArmRollState {
        public Vector3 BendNormal;      // last ACCEPTED bend normal (rig frame); zero = none yet
        public bool PlaneLocked;        // hysteresis latch: currently trusting the elbow plane
        public Vector3 PendingNormal;   // a candidate the continuity guard rejected, under confirmation
        public int PendingCount;        // consecutive self-consistent proposals of PendingNormal

        public void Reset() {
            BendNormal = Vector3.zero;
            PlaneLocked = false;
            PendingNormal = Vector3.zero;
            PendingCount = 0;
        }
    }

    /// <summary>Where this frame's bend normal came from — diagnostics only.</summary>
    public enum ArmNormalSource {
        ElbowPlane = 0,   // the elbow is meaningfully bent: the source geometry determines the roll
        Held = 1,         // arm (near) straight: carried the previous normal, re-orthogonalised
        RestCarried = 2,  // arm (near) straight and no history: rest normal through the roll-free swing
        Guarded = 3,      // elbow bent enough, but the candidate normal was rejected as an implausible
                          // one-frame roll jump (or an outright plane reversal) and the previous one held
        Slewed = 4,       // a confirmed large roll change, being stepped in at the bounded rate
    }

    public struct ArmAimResult {
        public bool Valid;
        public Quaternion UpperRig;      // upper-arm rotation, in the control-rig rest frame
        public Quaternion LowerRig;      // lower-arm rotation, in the control-rig rest frame
        // ---- diagnostics (§21) ----
        public Vector3 TargetUpperDir;
        public Vector3 TargetLowerDir;
        public Vector3 BendNormal;
        public float RollDeg;            // roll about the upper-arm axis, vs the roll-free carried rest normal
        public float BendDeg;            // elbow bend angle
        public ArmNormalSource NormalSource;
    }

    /// <summary>
    /// ARM RETARGET V1 (docs/UNITY_ARM_RETARGET_V1_2026-09-08.md). Replaces
    /// <see cref="Kalidokit.KalidokitArmSolver"/>'s Euler formulation with a direct, quaternion-native
    /// aim solve on the SAME VRM normalized control rig.
    ///
    /// Why: the forensic audit (docs/UNITY_RETARGETING_AUDIT_2026-09-08.md) measured the Kalidokit arm
    /// branch at 28.2 deg mean / 47.5 deg max direction error with 20.2 deg of left/right asymmetry on a
    /// perfectly SYMMETRIC input, and proved every single-constant rescue made it worse. The four
    /// structural faults were: (1) the opposite arm built as (-x,-y,-z) of the first, when the sagittal
    /// reflection of a three.js XYZ Euler is (+x,-y,-z); (2) a permanently saturated forearm clamp
    /// (+-0.3 rad) pinning ~17 deg of bend; (3) pi-normalized values mixed with radians; (4) forearm
    /// numbers cross-coupled into the shoulder swing. None of that survives here: each arm is solved
    /// INDEPENDENTLY from its own source vectors, and no Euler angle is used at any point.
    ///
    /// FRAME. Everything is in the control-rig REST FRAME: the frame in which every
    /// <c>Vrm10ControlBone</c> rests at identity rotation (UniVRM builds the control bones as fresh
    /// GameObjects, so their rest rotation is identity relative to the "Runtime Control Rig" transform).
    /// The audit measured that frame as X = avatar's LEFT, Y = up, Z = avatar's BACK — the same anatomical
    /// frame <see cref="Core.PoseFrame"/> already lives in, which is why the source directions can be used
    /// as targets with no new global transform.
    ///
    /// MIRROR. The caller keeps Kalidokit's left/right CROSS-map (the avatar's LEFT arm is driven from the
    /// subject's RIGHT shoulder/elbow/wrist) and passes <c>mirrorSagittal = true</c>, which negates X on
    /// the source points. Together those two are the intended mirror, verified against SR.mp4 - they are
    /// NOT a bug and are preserved exactly. Solving each arm from its own mirrored source vectors is what
    /// makes the result symmetric by construction rather than by an Euler sign trick.
    ///
    /// ROLL. Aiming alone leaves the rotation about the bone axis free (that freedom is what produced the
    /// historical forearm "helicopter"). The roll is pinned by the ELBOW PLANE: the bend normal
    /// <c>cross(upperDir, forearmDir)</c>. That is an ordered cross product, so it has no sign ambiguity
    /// and cannot flip by itself; it only becomes uninformative when the arm straightens, which is handled
    /// by <see cref="ArmRollState"/>. No world up-vector and no global reference enters the per-frame
    /// solve — only the rest basis, measured once from the rig.
    ///
    /// ROLL V2 (live evidence, 2026-09-09). "Cannot flip by itself" is true of the cross product but NOT of
    /// the arm: passing THROUGH straight swaps the plane's sign, and near straight the landmark noise does
    /// the same. V1 switched roll source on a single bend threshold, and the live capture found the flips
    /// piled up exactly there — 7 of 10 roll steps above 90 deg inside the 10.0-12.5 deg bend bin that
    /// straddles it, none at all above 12.5 deg. Three additions, all confined to the roll reference:
    /// (1) HYSTERESIS — separate enter/exit bends, whose dead band contains the whole hot zone;
    /// (2) a CONTINUITY GUARD — a candidate plane that reverses, or implies more than
    /// <see cref="RollStepMaxDeg"/> of roll in one frame, is not believed;
    /// (3) DEBOUNCED RE-ACQUISITION — a rejected candidate that keeps being proposed coherently while the
    /// elbow is clearly bent is eventually accepted, stepped in at <see cref="RollSlewMaxDeg"/> per frame.
    /// Bone DIRECTIONS are untouched by all three: <c>LookRotation(dir, normal)</c> maps forward onto
    /// <c>dir</c> whatever the normal is, so the guard can only ever change the roll.
    ///
    /// Allocation-free and branch-light: pure struct math, safe on the per-frame path.
    /// </summary>
    public static class ArmAimSolver {
        /// <summary>
        /// sin(elbow bend) required to START trusting the elbow plane. 0.35 ~= 20.5 deg.
        ///
        /// V2, measured. The V1 value was a SINGLE threshold at sin 0.2 (11.54 deg), and the live capture of
        /// 2026-09-09 showed the failure sitting exactly on it: binning every wrapped roll step of the dense
        /// (~40 Hz) trace by elbow bend gave 7 of the 10 steps above 90 deg inside the 10.0-12.5 deg bin
        /// (n=79, p90 63.8 deg, p99 165.3 deg, max 176.9 deg), 2 more in 7.5-10 deg, and ZERO anywhere above
        /// 12.5 deg (n=2310, worst p99 18.6 deg). A single threshold inside that band makes the solver
        /// alternate between ElbowPlane and Held from frame to frame, and each alternation can reverse
        /// cross(upper, forearm) — a 180 deg roll flip.
        ///
        /// 0.35 clears the whole measured unreliable region by ~8 deg. Above it the live data contains
        /// no >90 deg roll step at all and a single >45 deg step in 2125 samples (0.05%).
        /// </summary>
        public const float BendSinEnter = 0.35f;

        /// <summary>
        /// sin(elbow bend) below which a locked elbow plane is RELEASED. 0.15 ~= 8.6 deg.
        ///
        /// Deliberately below the 10.0-12.5 deg hot zone, so an elbow hovering near the old 11.5 deg
        /// threshold cannot toggle the state at all: the dead band [8.6 deg, 20.5 deg] strictly contains it.
        /// Live dwell measurement justifies the width — bend lingers in [11.5, 20) deg for a median of 4 and
        /// a 95th-percentile of 17 consecutive dense samples (100-425 ms), i.e. long enough for a single
        /// threshold to chatter many times per crossing.
        /// </summary>
        public const float BendSinExit = 0.15f;

        /// <summary>
        /// Largest roll change, in degrees about the bone axis, that one frame's fresh elbow plane may
        /// imply before it is treated as implausible rather than real. The live dense trace contains ZERO
        /// wrapped roll steps above 90 deg once the elbow is bent past 12.5 deg, and 90 deg in one ~4 ms
        /// render frame is ~22 000 deg/s — orders of magnitude beyond a human forearm.
        /// </summary>
        public const float RollStepMaxDeg = 90f;

        /// <summary>
        /// Consecutive self-consistent proposals a guard-rejected normal needs before it is believed.
        /// Without this a genuine re-bend the other way would be locked out forever; with it, noise (which
        /// does not repeat coherently) cannot overturn a held roll. Only counted while the elbow is bent
        /// past <see cref="BendSinEnter"/>.
        /// </summary>
        public const int FlipConfirmFrames = 6;

        /// <summary>
        /// Once a large roll change is confirmed, the reference normal is rotated toward it by at most this
        /// many degrees per solve, so the roll can never step more than this in a single frame. This is a
        /// bounded rate on ONE signal — the roll reference — not a smoothing filter: bone DIRECTIONS are
        /// untouched by it, and in normal motion it never engages (the live dense p99 roll step at ~40 Hz is
        /// 18.9 deg, and the solver runs ~6x faster still). Per-frame rather than per-second to match the
        /// existing per-frame <c>lerpAmount</c> slerp in the driver.
        /// </summary>
        public const float RollSlewMaxDeg = 30f;

        // Below this the source segment is degenerate (zero-filled landmark, coincident joints).
        private const float MinSegment = 1e-4f;

        // sin(5 deg)^2. Below this the roll reference is effectively ALONG the bone and carries no twist
        // information for it — see the fallback in Solve.
        private const float RollDegenerateSinSq = 0.0076f;

        // cos(RollStepMaxDeg) and cos(RollSlewMaxDeg). The guard compares cosines rather than angles so
        // the per-frame path carries no acos; the two are equivalent because both bounds are fixed and
        // cos is monotonically decreasing over [0, 180].
        private const float CosRollStepMax = 0f;             // cos(90 deg)
        private const float CosRollSlewMax = 0.8660254f;     // cos(30 deg)

        /// <summary>
        /// Measure one arm's rest basis from the live control rig. Call at bind time, once.
        /// <paramref name="lateral"/> and <paramref name="up"/> are also rig-derived (see the driver): they
        /// fix the rest elbow hinge as <c>cross(upperDir, forward)</c> with <c>forward = -cross(lateral,
        /// up)</c>, i.e. the anatomical fact that a bent human elbow sends the forearm toward the FACE.
        /// That is the one anatomical choice in the solver, and it is a rest-basis constant — it never
        /// enters the per-frame roll, which comes from the source elbow plane.
        /// </summary>
        public static ArmRestBasis BuildRestBasis(Vector3 upperDir, Vector3 lowerDir, Vector3 lateral, Vector3 up) {
            ArmRestBasis basis;
            basis.UpperDir = Vector3.zero;
            basis.LowerDir = Vector3.zero;
            basis.BendNormal = Vector3.zero;
            basis.Valid = false;
            if (upperDir.sqrMagnitude < MinSegment || lowerDir.sqrMagnitude < MinSegment) {
                return basis;
            }
            basis.UpperDir = upperDir.normalized;
            basis.LowerDir = lowerDir.normalized;
            Vector3 forward = Vector3.zero;
            if (lateral.sqrMagnitude > MinSegment && up.sqrMagnitude > MinSegment) {
                forward = -Vector3.Cross(lateral.normalized, up.normalized);
            }
            if (forward.sqrMagnitude < MinSegment) {
                // Rig too degenerate to derive a facing (should not happen for a humanoid VRM). Fall back to
                // the VRM 1.0 facing: the spec model faces +Z in glTF and UniVRM imports with ReverseZ, so
                // the avatar faces -Z in Unity (measured in the audit for both sample avatars).
                forward = Vector3.back;
            }
            Vector3 normal = Vector3.Cross(basis.UpperDir, forward.normalized);
            if (normal.sqrMagnitude < MinSegment) {
                return basis;   // upper arm parallel to the facing: no sane hinge, leave invalid
            }
            basis.BendNormal = normal.normalized;
            basis.Valid = true;
            return basis;
        }

        /// <summary>
        /// Solve one arm. <paramref name="shoulder"/>/<paramref name="elbow"/>/<paramref name="wrist"/> are
        /// source landmark positions in the control-rig rest frame (the caller converts out of the
        /// Kalidokit Y-down convention). Returns rig-frame rotations; the caller converts to bone-local.
        /// </summary>
        public static ArmAimResult Solve(Vector3 shoulder, Vector3 elbow, Vector3 wrist,
                                         ArmRestBasis rest, ref ArmRollState state, bool mirrorSagittal) {
            ArmAimResult result = default(ArmAimResult);
            result.UpperRig = Quaternion.identity;
            result.LowerRig = Quaternion.identity;
            if (!rest.Valid) {
                return result;
            }
            if (mirrorSagittal) {
                shoulder.x = -shoulder.x;
                elbow.x = -elbow.x;
                wrist.x = -wrist.x;
            }

            Vector3 upperDelta = elbow - shoulder;
            if (upperDelta.sqrMagnitude < MinSegment) {
                return result;   // no upper arm to aim — caller holds the last valid rotation via LimbGate
            }
            Vector3 upper = upperDelta.normalized;
            Vector3 foreDelta = wrist - elbow;
            // A missing wrist leaves the arm straight rather than aiming the forearm at the origin (which is
            // how the old path produced a fixed garbage rotation on zero-filled landmarks).
            Vector3 fore = foreDelta.sqrMagnitude < MinSegment ? upper : foreDelta.normalized;

            Vector3 cross = Vector3.Cross(upper, fore);
            float bendSin = cross.magnitude;
            // HYSTERESIS (V2). A higher bar to START trusting the elbow plane than to KEEP trusting it, so
            // an elbow hovering near the old single threshold cannot alternate between the two roll sources
            // (that alternation was the measured 180 deg flip — see BendSinEnter).
            float gate = state.PlaneLocked ? BendSinExit : BendSinEnter;
            Vector3 normal;
            if (bendSin >= gate) {
                state.PlaneLocked = true;
                normal = ResolveNormal(cross / bendSin, upper, bendSin, ref state, out result.NormalSource);
            } else {
                state.PlaneLocked = false;
                state.PendingNormal = Vector3.zero;
                state.PendingCount = 0;
                normal = CarryNormal(upper, rest, ref state, out result.NormalSource);
            }
            if (normal.sqrMagnitude < MinSegment) {
                return result;
            }

            // The rotation that carries (restUpper, restNormal) onto (upper, normal), and likewise for the
            // forearm. LookRotation maps (0,0,1) -> forward and (0,1,0) -> up, so
            // LookRotation(a, n) * inverse(LookRotation(restA, restN)) maps restA -> a and restN -> n.
            // Using the SAME normal for both bones is what keeps the forearm's twist tied to the elbow
            // plane instead of inventing one (§9).
            Quaternion restUpperFrame = Quaternion.LookRotation(rest.UpperDir, rest.BendNormal);
            Quaternion restLowerFrame = Quaternion.LookRotation(rest.LowerDir, rest.BendNormal);
            // A roll reference lying ALONG a bone cannot orient that bone's twist, and LookRotation would
            // silently substitute an arbitrary up — the exact pop this guard exists to prevent. The normal
            // is perpendicular to `upper` by construction, so only the forearm can hit it, and only when a
            // held normal meets a forearm that has swung onto it (reachable at the guard's rejection
            // boundary, where held sits ~90 deg from the candidate). Fall back for THAT BONE ONLY to the
            // roll-free carried reference: FromToRotation has no roll degree of freedom, so it cannot
            // helicopter, and the upper arm keeps the shared normal.
            Vector3 lowerUp = normal;
            if (Vector3.Cross(fore, normal).sqrMagnitude < RollDegenerateSinSq) {
                lowerUp = Quaternion.FromToRotation(rest.LowerDir, fore) * rest.BendNormal;
            }
            result.UpperRig = Quaternion.LookRotation(upper, normal) * Quaternion.Inverse(restUpperFrame);
            result.LowerRig = Quaternion.LookRotation(fore, lowerUp) * Quaternion.Inverse(restLowerFrame);

            result.TargetUpperDir = upper;
            result.TargetLowerDir = fore;
            result.BendNormal = normal;
            result.BendDeg = Vector3.Angle(upper, fore);
            // Roll relative to the roll-free aim swing: 0 means "pure swing, no rotation about the bone
            // axis". A helicopter would show as this value spinning while the directions stay put.
            Vector3 carried = Quaternion.FromToRotation(rest.UpperDir, upper) * rest.BendNormal;
            result.RollDeg = Vector3.SignedAngle(carried, normal, upper);
            result.Valid = true;
            return result;
        }

        /// <summary>
        /// CONTINUITY GUARD (V2). The elbow is bent enough to determine a plane, but a fresh
        /// <c>cross(upper, forearm)</c> can still reverse — the arm passing through straight swaps its sign,
        /// and a noisy landmark near straight does the same. Accepting that verbatim is a 180 deg roll flip.
        ///
        /// So a candidate is believed only when it is a PLAUSIBLE continuation of the held normal: same
        /// hemisphere, and no more than <see cref="RollStepMaxDeg"/> of roll about the current bone axis.
        /// A rejected candidate that keeps being proposed, coherently, while the elbow stays clearly bent is
        /// eventually believed (<see cref="FlipConfirmFrames"/>) — otherwise a genuine re-bend the other way
        /// would be locked out forever — and even then it is stepped in at <see cref="RollSlewMaxDeg"/> per
        /// frame rather than snapped.
        ///
        /// Both comparisons are made about the CURRENT bone axis, so swinging the arm is never mistaken for
        /// rolling it.
        /// </summary>
        private static Vector3 ResolveNormal(Vector3 candidate, Vector3 upper, float bendSin,
                                             ref ArmRollState state, out ArmNormalSource source) {
            source = ArmNormalSource.ElbowPlane;
            Vector3 held = Orthogonalise(state.BendNormal, upper);
            Vector3 fresh = Orthogonalise(candidate, upper);
            if (held.sqrMagnitude < MinSegment || fresh.sqrMagnitude < MinSegment) {
                // No usable history (first frame after a reset), or the candidate is parallel to the bone:
                // nothing to be inconsistent with.
                state.PendingNormal = Vector3.zero;
                state.PendingCount = 0;
                state.BendNormal = candidate;
                return candidate;
            }
            // Both vectors are unit and perpendicular to `upper`, so their dot IS cos(roll step). Every
            // test here is against a FIXED angle, so comparing cosines decides exactly the same way as
            // comparing angles — without an acos on the per-frame path (measured: the acos was most of
            // this guard's cost). The angle itself is only ever needed on the rare slew branch.
            float cosStep = Vector3.Dot(held, fresh);
            bool reversed = Vector3.Dot(candidate, state.BendNormal) < 0f;
            bool believable = cosStep >= CosRollStepMax && !reversed;

            if (believable) {
                state.PendingNormal = Vector3.zero;
                state.PendingCount = 0;
            } else {
                // Count an implausible candidate as evidence only while the elbow is unambiguously bent —
                // a candidate computed near the straight limit is exactly the noise this guard rejects.
                if (bendSin >= BendSinEnter && state.PendingNormal.sqrMagnitude > MinSegment
                        && Vector3.Dot(candidate, state.PendingNormal) > 0f) {
                    state.PendingCount = state.PendingCount + 1;
                } else {
                    state.PendingCount = bendSin >= BendSinEnter ? 1 : 0;
                }
                state.PendingNormal = candidate;
                if (state.PendingCount < FlipConfirmFrames) {
                    source = ArmNormalSource.Guarded;
                    return held;
                }
                // Sustained and self-consistent: a real reversal, so let it through the bound below.
            }

            // ONE rate bound, on BOTH paths. Applying it only to confirmed reversals left the ordinary
            // accept path free to snap the remainder once the held normal came within RollStepMaxDeg of the
            // candidate — measured as a 60 deg single-frame jump while walking a reversal in. With the bound
            // here instead, "no frame moves the roll by more than RollSlewMaxDeg" is an invariant of the
            // solver rather than a property of one branch.
            if (cosStep < CosRollSlewMax) {
                float sign = Vector3.Dot(Vector3.Cross(held, fresh), upper) < 0f ? -1f : 1f;
                Vector3 stepped = Quaternion.AngleAxis(RollSlewMaxDeg * sign, upper) * held;
                state.BendNormal = stepped;
                source = ArmNormalSource.Slewed;
                return stepped;
            }
            state.PendingNormal = Vector3.zero;
            state.PendingCount = 0;
            state.BendNormal = candidate;
            return candidate;
        }

        // Project a vector onto the plane perpendicular to the bone axis and normalise it. Returns zero when
        // the vector is (near) parallel to the axis, i.e. carries no roll information about it.
        private static Vector3 Orthogonalise(Vector3 v, Vector3 axis) {
            if (v.sqrMagnitude < MinSegment) {
                return Vector3.zero;
            }
            Vector3 projected = v - axis * Vector3.Dot(v, axis);
            if (projected.sqrMagnitude < 1e-8f) {
                return Vector3.zero;
            }
            return projected.normalized;
        }

        // Arm (nearly) straight: the elbow plane carries no roll information. Prefer the last
        // well-determined normal, re-orthogonalised against the current bone axis so the roll stays
        // continuous through the straight interval. With no history, carry the REST normal through the
        // roll-free aim swing — FromToRotation has no roll degree of freedom, so this cannot helicopter.
        private static Vector3 CarryNormal(Vector3 upper, ArmRestBasis rest, ref ArmRollState state,
                                           out ArmNormalSource source) {
            if (state.BendNormal.sqrMagnitude > MinSegment) {
                Vector3 projected = state.BendNormal - upper * Vector3.Dot(state.BendNormal, upper);
                if (projected.sqrMagnitude > 1e-8f) {
                    source = ArmNormalSource.Held;
                    return projected.normalized;
                }
            }
            source = ArmNormalSource.RestCarried;
            return (Quaternion.FromToRotation(rest.UpperDir, upper) * rest.BendNormal).normalized;
        }
    }
}
