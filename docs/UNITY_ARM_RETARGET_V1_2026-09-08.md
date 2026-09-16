# Unity Arm Retargeting V1 — 2026-09-08

Implements the single change recommended by `docs/UNITY_RETARGETING_AUDIT_2026-09-08.md` §15: replace the
arm branch structurally with a quaternion aim solve on the existing VRM normalized control rig, with the
roll pinned by the elbow plane. **Arms only.** Legs, torso, head, hands, feet, IK, face, Python/OAK-D,
P0/P1-1/P1-2/P1-3/F-08 and the network protocol are untouched; P1-4 stays disabled.

All numbers below are measured in the **shipping C#**, inside the Unity Editor (Roslyn `execute_code` and
the EditMode Test Runner) — not from a Python model.

---

## 1. Executive verdict

**CONDITIONAL PASS.**

Every objectively measurable acceptance criterion in the brief's §22 passes, in the real build:

| measure | old (Kalidokit `RigArm`) | new (`ArmAimSolver`) |
|---|---|---|
| direction error, A1–A11, mean | **39.63°** | **0.0000°** |
| direction error, A1–A11, max | **162.81°** | **0.0000°** |
| L/R asymmetry on symmetric input | **20.2°** (audit) / 34.4° worst here | **0.0000°** |
| roll continuity, full 360° elbow-plane sweep | n/a (no elbow-plane roll) | **10.00°/step, 0 flips, 0.0000° direction error** |
| noise σ = 2 cm / 4 cm | 41.2° / 43.9° | **7.4° / 14.8°** |
| CPU, both arms | 3.435 µs | **2.215 µs** (−35 %) |
| allocation per frame | 0 B | **0 B** |
| EditMode tests | — | **70/70 pass** (23 new arm tests) |

> **UPDATE 2026-09-09 — the condition below has been discharged.** §12 and §13 were executed live
> against a real subject; see `docs/UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`. Live result:
> avatar-vs-landmark error **35.78° → 0.46°** mean over nine motions, elbow reproduction
> **21.98° → 0.31°**, L/R asymmetry **22.6° → 0.5°**. Live verdict **CONDITIONAL PASS** — retargeting
> fixed; one arm-side defect remains (near-straight-elbow forearm roll pops, `BendSinMin`), and the
> dominant remaining error is now upstream landmark quality, not the retarget.

The original condition, kept for the record: **§12 live Unity and §13 SR.mp4 A/B were NOT EXECUTED.**
Both need the OAK-D sidecar running with a subject in front of the camera, which was not available in
that session, and an SR.mp4 A/B additionally needs a *new* recording (the existing one is the old
build). The maths, the integration path, P0 safety and performance were verified there; the on-camera
sign-off was outstanding. §12/§13 give the exact procedure that was subsequently followed.

---

## 2. Files changed

| file | change |
|---|---|
| `Runtime/Retargeting/ArmAimSolver.cs` | **new.** The solver: rest-basis measurement, aim solve, elbow-plane roll, straight-arm roll hold, diagnostics payload. Pure struct math, no allocation, no `Transform` knowledge. |
| `Runtime/Retargeting/KalidokitControlRigDriver.cs` | routes arms through the new solver behind `useAimArms` (default **true**); measures both arms' rest basis at `Bind`; adds `ApplyBoneRotation` (quaternion twin of `ApplyBone`, same `lerpAmount`); adds `RigFrame()`; resets roll state on `Recalibrate`; adds the §16 arm trace. |
| `Runtime/Retargeting/LimbGate.cs` | adds a **quaternion** `Resolve` overload and factors the VALID/HELD state machine into a shared private `Step()` so the arm and leg paths cannot diverge. Euler path behaviour unchanged. |
| `Runtime/Bootstrap/AppBootstrap.cs` | `kalidokitAimArms` (default **true**, live-tunable) pushed each frame for live A/B and rollback. |
| `Tests/EditMode/ArmAimSolverTests.cs` | **new.** 23 deterministic tests: A1–A11 directions, symmetry, forearm-not-pinned, elbow-plane roll, 360° roll sweep, straighten/re-bend hold, degenerate landmarks, P0 quaternion gate. |
| `docs/audit/retarget_trace/` | harness + `README.md` + `arm_v1_measure.cs.txt` (the editor measurement bodies). |

**Not changed:** `Runtime/Retargeting/Kalidokit/KalidokitArmSolver.cs` — the old implementation is byte-for-byte
intact and reachable via `kalidokitAimArms = false`, for A/B and rollback. Not one Kalidokit constant was
edited. `Scenes/Bootstrap.unity` is untouched; the new serialized fields take their C# defaults.

---

## 3. Old arm architecture

```
landmarks (Kalidokit conv: X = subject LEFT, Y DOWN, Z BACK)
        ↓
FindRotation(shoulder, elbow)              -> per-axis atan2, each ÷ π  ("π-normalized", NOT radians)
AngleBetween3DCoords(otherSh, sh, elbow)   -> overwrites .y
        ↓
RigArm(invert = ±1)
   upperArm.z *= -2.3 * invert
   upperArm.y *= PI * invert;  upperArm.y -= lowerArm.x_raw;  upperArm.y -= -invert*max(lowerArm.z_raw,0)
   upperArm.x -= 0.3 * invert
   lowerArm.{x,y,z} *= ±2.14;  clamp(lowerArm.x, -0.3, 0.3);  clamp(upperArm.x, -0.5, PI)
        ↓
KMath.ThreeEulerToUnity(euler, (1,1,1), flipQuat 2)   -> three.js XYZ Euler -> quaternion -> component flip
        ↓
ApplyBone -> Slerp into normalized bone localRotation
```

Four structural faults, all measured in the audit:

1. **Antisymmetric Euler mirroring.** The opposite arm comes out as `(−x,−y,−z)` of the first. The sagittal
   reflection of a three.js XYZ Euler is `(+x,−y,−z)`: with `M = diag(−1,1,1)`,
   `M·Rx(x)·M = Rx(x)` but `M·Ry(y)·M = Ry(−y)` and `M·Rz(z)·M = Rz(−z)`. The `x` term carries the wrong
   relative sign, injected by `upperArm.x -= 0.3f * invert`.
2. **Saturated forearm clamp.** `lowerArm.x` arrives as ±1.07 rad and is clamped to ±0.3 — permanently
   saturated, pinning ~17.2° of forearm bend in every pose.
3. **Mixed units.** π-normalized values multiplied by `PI` in some places and by `2.3`/`2.14` in others,
   and `upperArm.y -= lowerArmXOriginal` subtracts a π-normalized value from a radian value.
4. **Forearm→shoulder cross-coupling.** Two of the four terms in the shoulder swing are raw forearm
   numbers, so the upper arm moves when only the wrist does.

## 4. New arm architecture

```
landmarks (Kalidokit conv)
        ↓  KalidokitToRig: (x, −y, z)          [Y-down -> Y-up; nothing else changes]
shoulder / elbow / wrist in the CONTROL-RIG REST FRAME
        ↓  sagittal mirror: negate X on all three points   (only when kalidokitBodyMirror is OFF)
u = normalize(elbow − shoulder)
f = normalize(wrist − elbow)
n = normalize(cross(u, f))                     [the elbow plane = the roll reference]
        ↓
qUpper_rig = LookRotation(u, n) · inverse(LookRotation(restUpper, restNormal))
qLower_rig = LookRotation(f, n) · inverse(LookRotation(restLower, restNormal))
        ↓  P0-1 LimbGate (quaternion overload) — fresh solve / hold last valid
qUpper_local = inverse(parent_rig) · qUpper_rig
qLower_local = inverse(qUpper_rig) · qLower_rig          [rig frame cancels exactly]
        ↓  ApplyBoneRotation -> Slerp(lerpAmount) into normalized bone localRotation
        ↓  existing torso/leg writes, existing ProcessRuntime
VRM
```

Each arm is solved **independently from its own source vectors**. No Euler angle appears anywhere; no
`invert` flag decides one arm from the other; no clamp, no gain, no offset constant. Symmetry is a property
of the construction, not something tuned in.

The old path's ownership model is unchanged: the driver still owns the normalized control rig, still writes
in `LateUpdate` before `ProcessRuntime`, still gates every arm through `LimbGate`, and still damps with the
same `lerpAmount`.

## 5. Mathematical formulation

For each arm, given source points `S, E, W` in the rig frame and a rest basis `(rU, rL, rN)`:

```
u = normalize(E − S)                        wanted upper-arm direction
f = normalize(W − E)                        wanted forearm direction
n = normalize(u × f)                        bend normal — the elbow plane
```

`Quaternion.LookRotation(a, b)` maps `(0,0,1) → a` and `(0,1,0) → b`. Therefore

```
Q(a, b) := LookRotation(a, b)
qUpper  = Q(u, n) · Q(rU, rN)⁻¹              maps rU → u  and  rN → n
qLower  = Q(f, n) · Q(rL, rN)⁻¹              maps rL → f  and  rN → n
```

`qUpper` is the unique rotation that both aims the bone and fixes its roll: aiming alone determines only
`rU → u` (two degrees of freedom); `rN → n` supplies the third. Because `n ⊥ u` and `rN ⊥ rU` exactly,
`LookRotation`'s internal orthogonalisation is a no-op and both frames are exactly orthonormal.

Rest check: with `u = rU`, `f = rL`, `n = rN`, both reduce to identity, so a source arm in the avatar's rest
configuration produces the avatar's rest pose. Verified by test (`ForearmBend_IsNotPinned`).

**Rest basis** (`BuildRestBasis`), measured from the rig at bind time:

```
rU = normalize(lowerArm.position − upperArm.position)     from the live control rig
rL = normalize(hand.position     − lowerArm.position)
lateral = leftUpperArm.position − rightUpperArm.position  (avatar's LEFT)
up      = spine.position − hips.position
forward = −(lateral × up)                                 (avatar's facing, rig-derived)
rN = normalize(rU × forward)                              the rest elbow hinge
```

`rN` encodes exactly one anatomical fact — **a bent human elbow sends the forearm toward the face** — and
it is the only anatomical assumption in the solver. It is a *rest-basis* constant, evaluated once; it never
participates in the per-frame roll, which comes wholly from the source elbow plane. Both arms fall out with
opposed hinges automatically (`(0,+1,0)` and `(0,−1,0)` on a T-pose rig), from geometry rather than a flag —
asserted by `RestBasis_IsMeasured_NotHardcoded`.

### Rest-basis independence (no new calibration system)

Measured, not assumed. The solver hits 0° on rigs whose arms are *not* an exact T-pose, including the
project's actual VRM 0.x avatar and two synthetic A-pose rigs:

```
VirtualMirrorRigTemplate.vrm (exact T)    hingeL (0.00, 1.00, 0.00) hingeR (0.00,-1.00, 0.00)  mean 0.00000  max 0.00000  L/R asym 0.00000
Anime+Boy+V-tuber.vrm (measured)          hingeL (0.05, 1.00, 0.00) hingeR (0.05,-1.00, 0.00)  mean 0.00000  max 0.00000  L/R asym 0.00000
synthetic A-pose (arms 45 deg down)       hingeL (0.71, 0.71, 0.00) hingeR (0.71,-0.71, 0.00)  mean 0.00000  max 0.00000  L/R asym 0.00000
synthetic A-pose + 10 deg forward sweep   hingeL (0.71, 0.71, 0.00) hingeR (0.71,-0.71, 0.00)  mean 0.00000  max 0.00000  L/R asym 0.00000
```

## 6. Coordinate-space handling

Nothing new was introduced. `flipQuat`, `eulerSigns`, `PoseSpaceConverter` and the rest basis are all as
the audit left them.

* **The frame.** `Vrm10ControlBone` builds each control bone as a fresh `GameObject` parented with
  `worldPositionStays: true`, so every control bone rests at identity rotation *relative to the
  "Runtime Control Rig" transform*. That transform is the rest frame. The audit measured it as
  **X = avatar's LEFT, Y = up, Z = avatar's BACK** — the same anatomical frame `PoseFrame` already lives in,
  which is why source directions can be used directly as targets.
* **`RigFrame()`** reads that transform's live rotation (`hips.parent.rotation`) every frame instead of
  assuming world == model. It is identity in the shipping scene (`AvatarRoot` is at identity, and
  `ApplyRootPosition` only *translates*), so this costs one quaternion and removes an implicit assumption
  the old code relied on: rotating `AvatarRoot` no longer breaks the arms.
* **Local vs world.** Validated against the real hierarchy rather than assumed:
  `qUpper_local = inverse(inverse(rigFrame) · upperBone.parent.rotation) · qUpper_rig`. The parent is read
  live, so it works whether the upper arm's parent is `Shoulder`, `Chest` or `UpperChest`, and whether the
  torso path drives it or leaves it at rest. For the forearm the rig frame cancels algebraically, so
  `qLower_local = inverse(qUpper_rig) · qLower_rig` exactly.
* **`ThreeEulerToUnity` is bypassed for arms only.** It is still the path for hips, spine, chest, hands and
  legs, unchanged. The arms go through `ApplyBoneRotation`, which is `ApplyBone` minus the Euler round-trip
  — same slerp, same `lerpAmount`, same ownership.

## 7. Mirror handling

Unchanged and deliberate. Two mechanisms compose into the mirror:

1. **Kalidokit's left/right cross-map**, preserved verbatim: the avatar's **LEFT** arm is driven from
   landmarks **12/14/16** (the subject's RIGHT shoulder/elbow/wrist), and the avatar's RIGHT from 11/13/15.
2. **The sagittal reflection**: `ArmAimSolver.Solve(..., mirrorSagittal)` negates X on the three source
   points. The driver passes `!mirrorX`, so when `kalidokitBodyMirror` is OFF (the shipping value) the
   solver applies the reflection, and when it is ON the input was already reflected upstream and the solver
   does not reflect twice.

Together these are the anatomically correct mirror — the audit verified it against SR.mp4 at t = 150 s
(the subject raises the arm that appears image-left; the avatar raises the arm that appears screen-right,
i.e. the same side of the user's own visual field). No left/right naming was "fixed".

Crucially, the mirror is now applied to **points**, before any solve. The old path tried to obtain the
opposite arm by negating Euler components, which is the mathematically wrong operation for a reflection and
is precisely fault (1) in §3. Solving each mirrored arm independently is what makes
`SymmetricSource_ProducesSymmetricError` pass at 0.0000° instead of 20.2°.

## 8. Elbow-plane / roll handling

The roll reference is the bend normal `n = u × f`.

* **No sign ambiguity.** `n` is an *ordered* cross product of two measured vectors, so it cannot flip on its
  own. A 180° reversal requires `f` to pass through `u` — i.e. the arm genuinely straightening and
  re-bending the other way, which is a real pose change, not an artefact.
* **No global reference.** No world up-vector, no camera axis, no fixed model axis enters the per-frame
  roll. That is the specific failure the ADR-022 "forearm helicopter" came from.
* **Same normal for both bones.** `qUpper` and `qLower` are built from the *same* `n`, so the forearm's
  twist is tied to the elbow plane rather than solved independently. There is no free twist channel to pick
  up noise.
* **Straight arm → hold, never snap.** Below `BendSinMin = 0.2` (≈ 11.5° of bend, or ≥ 168.5° hyperextension
  — equally uninformative) the elbow plane carries no roll information. The solver then re-orthogonalises
  the **last well-determined** normal against the current bone axis (`ArmNormalSource.Held`), so the roll
  is continuous across the straight interval. With no history at all it carries the rest normal through the
  roll-free aim swing (`RestCarried`) — `FromToRotation` has no roll degree of freedom, so that branch is
  structurally incapable of helicoptering.
* **Twist is not invented.** §9 of the brief: when the pose lacks the information, the solver holds a stable
  reference instead of manufacturing a value from noise.

`ArmRollState` is reset on `Bind`, on `Recalibrate` (the C key) and whenever `useAimArms` is toggled.

## 9. Deterministic pose results (§13)

A1–A11, shipping C#, torso at rest, `err = angle(wanted, achieved)` in degrees. `u` = upper arm,
`f` = forearm; L/R are the **avatar's** arms.

```
A1  standing arms down   OLD L u  41.3 f  41.3  R u   6.9 f   6.9 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A2  right arm up         OLD L u   6.9 f  54.4  R u   6.9 f   6.9 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A3  left arm up          OLD L u  41.3 f  41.3  R u  41.3 f  20.0 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A4  both arms overhead   OLD L u   6.9 f  54.4  R u  41.3 f  20.0 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A5  T-pose               OLD L u  28.6 f  28.6  R u  28.6 f  28.6 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A6  arms forward         OLD L u  17.2 f  17.2  R u  17.2 f  17.2 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A7  arms backward        OLD L u 162.8 f 162.8  R u 162.8 f 162.8 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A8  elbows bent fwd      OLD L u   0.0 f  30.7  R u   0.0 f  30.7 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend  90.0
A9  asymmetric           OLD L u  28.6 f  53.4  R u  41.3 f  93.2 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L  -90.0 R    0.0 bend  45.0
A10 body rotated 45deg   OLD L u  41.3 f  41.3  R u   6.9 f   6.9 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0
A11 body rotated 90deg   OLD L u  41.3 f  41.3  R u   6.9 f   6.9 | NEW L u 0.0000 f 0.0000  R u 0.0000 f 0.0000 | roll L    0.0 R    0.0 bend   0.0

OLD mean 39.63  max 162.81   |   NEW mean 0.0000  max 0.0000   |   worst NEW L/R asym 0.0000
```

**A7 is worth its own line: 162.8°.** With the arms behind the body the old solver points them almost
exactly backwards — a full inversion, not a distortion. That is the pose SR.mp4's block 6 ("left hand
behind back 3 s, out, 8 s, out — then right") exercises for 30 s per pass.

A10/A11 as specified rotate a body whose arms hang vertically, and a vertical direction is yaw-invariant, so
they do not actually exercise the yawed path. Added a variant that does — arms out sideways with the elbows
bent forward, whole body yawed:

```
yaw    0 deg (arms out, elbow fwd):  OLD u   0.0 f  30.7  |  NEW u 0.0000 f 0.0000  roll    0.0
yaw   45 deg (arms out, elbow fwd):  OLD u  30.7 f  17.1  |  NEW u 0.0000 f 0.0000  roll    0.0
yaw   90 deg (arms out, elbow fwd):  OLD u  62.7 f  34.6  |  NEW u 0.0000 f 0.0000  roll    0.0
yaw  -45 deg (arms out, elbow fwd):  OLD u  30.7 f  61.0  |  NEW u 0.0000 f 0.0000  roll    0.0
```

Note: the arms are scored in isolation here, with the torso at rest, because torso yaw is out of scope for
this task (`kalidokitTorsoYawScale: 0`, and the 1.40× gain bug from the audit §7 is untouched). A yawed
*source* still retargets exactly onto the frontal avatar's arms.

## 10. Symmetry results (§14)

Perfectly symmetric source input; the two avatar arms must show the same error and must be mirror images.

| pose | OLD L | OLD R | OLD \|Δ\| | NEW L | NEW R | NEW \|Δ\| |
|---|---|---|---|---|---|---|
| A1 standing arms down | 15.5° | 35.7° | **20.2°** | 0.0000° | 0.0000° | **0.0000°** |
| A4 both arms overhead | 6.9° | 41.3° | **34.4°** | 0.0000° | 0.0000° | **0.0000°** |
| A5 T-pose | 28.6° | 28.6° | 0.0° | 0.0000° | 0.0000° | **0.0000°** |
| A6 arms forward | 17.2° | 17.2° | 0.0° | 0.0000° | 0.0000° | **0.0000°** |
| A7 arms backward | 162.8° | 162.8° | 0.0° | 0.0000° | 0.0000° | **0.0000°** |
| A8 elbows bent fwd | 0.0° | 0.0° | 0.0° | 0.0000° | 0.0000° | **0.0000°** |

(A1 uses the audit's own arm fixture for continuity with the 20.2° figure; the wider §9 fixture set gives
34.4° as the worst old asymmetry.)

**Tolerance.** `SymmetryTolerance = 0.05°`, chosen from numerical precision, not visual preference: a
direction round-trips through two float32 quaternion products, whose observed residual is
`0.0000°` at the printed precision and below `1e-3°` in the raw values. 0.05° is ~50× the observed floor
and still ~3 orders of magnitude below anything perceivable. Asserted by
`SymmetricSource_ProducesSymmetricError` for four symmetric poses, on both the per-arm error and the
mirror-image relation between the two avatar arms.

## 11. Roll results (§15)

Direction accuracy alone is explicitly *not* accepted as sufficient, so roll is measured separately.

**Discrete elbow-plane poses.** Flipping the source elbow plane must flip the avatar's bend normal — it
does, and `ElbowPlane_DrivesRoll_AndTheBendNormalFollows` asserts `dot(nFwd, nBack) < −0.99`:

| pose | wanted bend normal | achieved bend normal | normal error | roll |
|---|---|---|---|---|
| arm horizontal, elbow forward | (0, −1, 0) | (0, −1, 0) | 0.000° | 0.0° |
| arm horizontal, elbow backward | (0, +1, 0) | (0, +1, 0) | 0.000° | 180.0° |
| arm horizontal, forearm up | (0, 0, −1) | (0, 0, −1) | 0.000° | −90.0° |
| arm horizontal, forearm down | (0, 0, +1) | (0, 0, +1) | 0.000° | +90.0° |
| arm overhead, forearm forward | (−1, 0, 0) | (−1, 0, 0) | 0.000° | 0.0° |
| arm overhead, forearm backward | (+1, 0, 0) | (+1, 0, 0) | 0.000° | 180.0° |

**Full 360° sweep.** Forearm bent 60°, its plane swept around the upper-arm axis in 10° steps:

```
sweep   0 deg -> roll   -90.0  uErr  0.000  fErr  0.000  src=ElbowPlane
sweep  60 deg -> roll  -150.0  uErr  0.000  fErr  0.000  src=ElbowPlane
sweep 120 deg -> roll   150.0  uErr  0.000  fErr  0.000  src=ElbowPlane
sweep 180 deg -> roll    90.0  uErr  0.000  fErr  0.000  src=ElbowPlane
sweep 240 deg -> roll    30.0  uErr  0.000  fErr  0.000  src=ElbowPlane
sweep 300 deg -> roll   -30.0  uErr  0.000  fErr  0.000  src=ElbowPlane

max roll step per 10 deg sample = 10.00 deg | >90 deg flips = 0 | worst direction error across the whole sweep = 0.0000 deg
```

The roll advances exactly one step per source step across the full turn, including through the ±180° wrap
(that wrap is a reporting artefact of expressing roll in (−180, 180]; `Mathf.DeltaAngle` shows the true step
is 10°). **No 180° flip, no sudden reversal, no helicopter** — asserted by `RollSweep_IsContinuous_NoFlips`,
which fails on any step > 10.05° or any jump > 90°.

**Straighten and re-bend** — the case where the roll reference disappears:

```
bend    60 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=ElbowPlane
bend    30 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=ElbowPlane
bend    10 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=Held
bend     3 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=Held
bend     0 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=Held
bend     3 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=Held
bend    10 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=Held
bend    30 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=ElbowPlane
bend    60 deg -> roll     0.0  uErr  0.000  fErr  0.000  src=ElbowPlane
```

The source switches to `Held` at the `BendSinMin` threshold and back again, with the roll continuous and
zero direction error throughout. `StraightArm_HoldsPreviousRoll_InsteadOfSnapping` and
`FirstFrameStraightArm_UsesRestCarriedNormal` assert both branches.

## 12. Live Unity results (§17)

**EXECUTED 2026-09-09.** Full results in
`docs/UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`; headline numbers in the update box in §1.
The procedure below is the one that was run (via `python-sidecar~/arm_v1_live_capture.py`, which drives
the nine motions on a countdown and records per-motion epoch windows for the analysis).

*Original text, for the record:* NOT EXECUTED. The Unity Editor was attached that session (the tests, the
benchmark and every table above ran in it), but a live run needs the OAK-D sidecar streaming and a
subject in front of the camera — neither was available then.

The Editor-side verification that *was* possible has been done: the project compiles clean (0 errors,
0 warnings), the whole EditMode suite passes 70/70, the solver is exercised on the actual VRMs' measured
rest bases, and the driver's rest-basis measurement and local/world composition are covered by the
formulation in §5/§6.

Procedure to run it:

1. Start the sidecar as usual (`python-sidecar~`, unchanged — do not pass `--recovery`).
2. In `Bootstrap.unity` → `AppBootstrap`, tick **`kalidokitDirectionTrace`** (leave
   `kalidokitDirectionTraceEveryFrames` at 60) and confirm **`kalidokitAimArms`** is ticked.
3. Enter Play. The log should carry one `[ARM-V1] rest basis measured:` line per avatar load; both arms must
   report `valid=True` with opposed `hinge` vectors.
4. Run the nine motions from the brief: arms relaxed → one arm horizontal → both horizontal → one overhead →
   both overhead → fast waving → arm circles → asymmetric → dance.
5. Watch `upperErrDeg` and `forearmErrDeg` in the `[ARM-V1-TRACE]` lines. These compare the solver's own
   target against the achieved control-rig bone direction, so on live data they measure the
   solve→rig→bone path, and should stay near 0 apart from the `lerpAmount = 0.5` slerp lag during fast
   motion.
6. Watch `rollDeg` during arm circles specifically: it should vary smoothly. A value spinning while
   `upperErrDeg` stays at 0 is the helicopter signature.
7. Compare against the green `PoseDebugSkeleton` — they should now agree, which is the thing SR.mp4 showed
   they did not.
8. Untick `kalidokitAimArms` mid-Play for a live A/B against the old branch (the gates and roll state reset
   automatically on the toggle).
9. Untick `kalidokitDirectionTrace` when done — it is chatty.

## 13. SR.mp4 comparison (§18)

**EXECUTED 2026-09-09** as a live A/B rather than a video comparison — `SR.mp4` is a recording of the
*old* build, so the branch was toggled mid-Play instead, giving both branches the same camera, the same
model warm-up and the same subject. Outcome per symptom is in §5 of the live-validation report; every
row below except the hand/forearm one is confirmed fixed on camera.

What the deterministic results predicted for each failure the audit mapped out of that video:

| SR.mp4 symptom (audit §14) | old, measured | new, measured | expected on camera |
|---|---|---|---|
| standing, arms hanging → avatar's arms bent ~90° forward, hands at the belly ("cactus arms") | 41.3° / 6.9°, forearm pinned +17.2° | **0.0000°** | arms hang; no constant forearm bend |
| subject raises **both** arms → avatar raises **one** | 20.2°–34.4° L/R asymmetry | **0.0000°** | both arms move together |
| overhead reaches not reachable | A4 up to 54.4° | **0.0000°** | arms reach fully overhead |
| T-pose reads wrong | 28.6° on all four bones | **0.0000°** | clean T |
| block 6, hand behind back | **162.8°** — arms point almost exactly backwards | **0.0000°** | arm actually goes behind the back |
| fast waving / dance | 39.6° mean, and 43.9° at σ = 4 cm | 0.0° clean, 14.8° at σ = 4 cm | tracks; residual is landmark noise, not the retarget |
| hands/forearms stiff | — | unchanged | **still stiff** — hand landmarks 17–22 never arrive (21/33 slots) and `wristRotationWeight: 0`. Out of scope; see §16. |

## 14. Performance (§20)

200 000 iterations of both arms per solver, inside the Editor, Roslyn-compiled:

| | old (`KalidokitArmSolver` + 4× `ThreeEulerToUnity`) | new (`ArmAimSolver` ×2) |
|---|---|---|
| total for 200 000 frames | 686.92 ms | **443.03 ms** |
| per frame (both arms) | 3.435 µs | **2.215 µs** |
| delta | — | **−1.219 µs/frame (−35 %)** |
| allocation | 0 B | **0 B** |

No latency regression — a small improvement. At the OAK path's ~21 fps this is ~0.005 % of a frame either
way, so the honest headline is "no meaningful difference"; the point is that it is not *worse* and it does
not allocate. Four trig-heavy `ThreeEulerToUnity` calls plus the `FindRotation`/`AngleBetween3DCoords`
`atan2`/`acos` chain are replaced by cross/dot products and two `LookRotation`s.

The rest basis is measured **once at `Bind`**, not per frame. `RigFrame()` adds one `Transform.rotation`
read per arm application.

## 15. Regression

| item | status |
|---|---|
| EditMode test suite | **70/70 pass**, 0 failed, 0 skipped (was 47; +23 new arm tests) |
| Compilation | 0 errors, 0 warnings — `validate_script` on all four touched C# files (`ArmAimSolver`, `KalidokitControlRigDriver`, `LimbGate`, `ArmAimSolverTests`), plus a forced full asset refresh + recompile and a clean Editor console |
| `KalidokitArmSolver.cs` | **unmodified** — old path intact, reachable via `kalidokitAimArms = false` |
| leg solver, `KMath` leg mapping | **unchanged** — no file touched |
| torso yaw, spine bend, `flipQuat`, `eulerSigns` | **unchanged** |
| head, hands, feet, IK, face tracking | **unchanged** |
| `PoseSpaceConverter`, `poseFlipX/Y/Z` | **unchanged** |
| P0-1 `LimbGate` semantics | **unchanged** for the euler/leg path; the quaternion overload shares the same `Step()` state machine. `LimbGateTests` (existing) pass; two new tests cover the quaternion overload's hold and never-valid cases |
| Python / OAK-D / RTMW3D / network protocol | **unchanged** — no file under `python-sidecar~` touched (its `M` in git is a pre-existing submodule pointer move) |
| P1-1 / P1-2 / P1-3 / F-08 | **unchanged** |
| P1-4 | **still disabled** (`--recovery` defaults to False) |
| `Scenes/Bootstrap.unity` | **unchanged** — new fields take C# defaults (`kalidokitAimArms = true`, `kalidokitDirectionTrace = false`) |

## 16. Known limitations

1. ~~**Live and video validation outstanding** (§12, §13).~~ **CLOSED 2026-09-09** — executed; see
   `docs/UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`. It surfaced one new live-only finding,
   recorded against limitation 4 below.
2. **Wrist and hand are still inert, by design.** The hand landmarks (17–22) are never populated by the
   sidecar — 21 of 33 slots arrive — and `wristRotationWeight: 0`. Explicitly out of scope; the arms will be
   correct while the hands stay at rest.
3. **Forearm twist (pronation/supination) is not recovered, and cannot be** from shoulder/elbow/wrist alone:
   three points give the bend plane, not the roll of the hand about the forearm. The solver deliberately
   holds the elbow-plane-consistent twist rather than inventing one. Recovering real pronation needs a hand
   or palm normal — i.e. hand landmarks (limitation 2).
4. **`BendSinMin = 0.2` is a judgement call**, not a measured optimum. It trades "roll follows a small
   bend" against "roll chases noise near straight". At σ = 2 cm landmark noise a nominally straight arm
   reads ~10–15° of spurious bend, which is why the threshold sits at ~11.5°. If live data shows roll
   jitter on a nearly-straight arm, raise it; if a shallow deliberate bend fails to steer the roll, lower it.
   It is a `const` — deliberately not exposed as a live knob until live data justifies a value.

   **Live data now exists, and it says raise it.** 2026-09-09: 31 single-frame forearm roll pops above
   45° were measured, **68 % of them at an elbow bend below 15°**, median bend **12.3°** against this
   threshold's ≈11.5°. Roll steps above 90° number **10 in 45 s** at bend < 15° and **zero** at any
   larger bend; roll-step p99 is 130.9°/134.1° below 15° of bend versus 10.2°/7.3° above 40°. The
   bone *direction* is unaffected (0.0–0.2° in the same windows) — what flips is the twist about the
   forearm. Deliberately **not changed** in that task, which was evidence-only. This is the highest-
   impact change available inside the arm retargeting scope.
5. **No temporal smoothing was added.** The only damping is the pre-existing `lerpAmount = 0.5` slerp in
   `ApplyBoneRotation`. That is intentional (it keeps the deterministic tests exact), but it means the noise
   figures in §1 are the *unsmoothed* solver's. If live fast motion shows jitter, the fix belongs in the
   existing filter/lerp, not in the solver.
6. **Torso still does not follow the body turn**, so a yawed subject gets correct arms on a frontal chest.
   That is audit §7 (`torsoYawScale: 0` plus the 1.40× gain bug) and a separate controlled change.
7. **Elbow position is not enforced.** This is FK: the bone *directions* are exact, but the avatar's limb
   proportions differ from the subject's, so the elbow and wrist land at the avatar's own bone lengths.
   Matching world positions would need IK — explicitly out of scope, and not indicated by any measurement.
8. **The rest-frame assumption is now explicit rather than implicit.** `RigFrame()` handles a rotated
   `AvatarRoot` correctly, but `PoseFrame` directions are still interpreted in the rig rest frame — which is
   only the same anatomical frame because `poseFlipY: 1` is the sole active flip and UniVRM imports with
   `Axes.Z`. Changing `poseFlipX`/`poseFlipZ` would break that correspondence for arms and legs alike.

## 17. Final decision

**ARM RETARGETING ACCEPTED.**

Every §22 criterion is met, measured in the shipping C#:

- [x] neutral arms correct — 0.0000° (A1)
- [x] horizontal arms correct — 0.0000° (A5, and yawed variants)
- [x] overhead arms correct — 0.0000° (A2/A3/A4)
- [x] both arms behave symmetrically — 0.0000° L/R asymmetry, was 20.2°–34.4°
- [x] forearms bend correctly — 0.0000° on the forearm in every pose
- [x] no constant +17° forearm bend — `ForearmBend_IsNotPinned`
- [x] no cactus-arm posture — A1 arms hang at 0.0000°
- [x] no one-arm-only failure — symmetry tests, four poses, both error and mirror relation
- [x] elbow-plane roll remains stable — 10.00°/step across a full 360° sweep
- [x] no helicopter rotation — 0 flips; roll-free fallback is structurally incapable of it
- [x] 0° clean-pose directional error — 0.0000°, tolerance 0.05° set from float32 precision
- [x] noise behaviour acceptable — 3.7° / 7.4° / 14.8° at σ = 1/2/4 cm vs 41.1° / 41.2° / 43.9° old
- [x] P0 safety intact — quaternion gate shares the state machine; a degenerate solve reports
      `Valid = false` and is fed to the gate as zero confidence, so the limb HOLDS
- [x] legs unchanged
- [x] torso unchanged
- [x] latency does not materially regress — 2.215 µs vs 3.435 µs, 0 B allocated
- [x] all existing tests pass — 70/70

Accepted on the maths, the integration path, P0 safety and performance.

**Live sign-off completed 2026-09-09** (§12, §13) — see
`docs/UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`. Live verdict **CONDITIONAL PASS**: every
SR.mp4 retargeting symptom is fixed on camera (error 35.78° → 0.46° mean, asymmetry 22.6° → 0.5°, elbow
range 97.9–175.1° → 28.6–179.9°), the old branch was shown to fail *even when tracking is good*, and the
two residuals are (a) near-straight-elbow forearm roll pops, see limitation 4, and (b) upstream landmark
quality, which is now the dominant error source and is not an arm-retargeting problem.

Roll back at any time by unticking `kalidokitAimArms` — the old branch is untouched, and was exercised
live in that run.
