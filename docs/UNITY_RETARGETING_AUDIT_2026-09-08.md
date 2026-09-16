# Unity Retargeting Forensic Audit — 2026-09-08

Scope: find the **first** transformation that turns a good incoming skeleton into a wrong VRM pose.
Diagnostic instrumentation only. No production behaviour changed. Python / OAK-D / P1-1 / P1-2 / P1-3 /
F-08 untouched; P1-4 stays disabled.

Evidence: `Assets/Games/video/SR.mp4` (428.5 s, 1918×1078, 30 fps) plus a deterministic offline replica of
the whole chain in `docs/audit/retarget_trace/` (see §16 for how to re-run it).

---

## 1. Executive verdict

**ROOT CAUSE IDENTIFIED.**

The first bad stage is **`KalidokitArmSolver.RigArm` (and the raw angle extraction feeding it)** —
`Runtime/Retargeting/Kalidokit/KalidokitArmSolver.cs:69`.

Everything upstream and downstream of the arm solve is measurably correct:

| stage | verdict | measured |
|---|---|---|
| sidecar → `PoseSpaceConverter` → `PoseFrame` | **correct** | an aim-solver fed from `PoseFrame` reaches **0.0°** error on every bone in every test pose |
| driver's Kalidokit adaptation `(x, −y, z)` | **correct** | same 0.0° control |
| `KalidokitPoseSolver` legs (`KMath` spherical coords) | **correct** | 5.6° mean / 7.7° max — and that residual *is* Kalidokit's deliberate `UpperLegZOffset = 0.1 rad = 5.73°` splay |
| **`KalidokitArmSolver` + `RigArm`** | **WRONG** | **28.2° mean / 47.5° max**, and **20.2° left/right asymmetric on a perfectly symmetric input** |
| `KMath.ThreeEulerToUnity`, `flipQuat = 2` | **correct** | best of all four flip modes by a wide margin (15.1° vs 24.4 / 32.8 / 73.3) |
| control-rig composition, `Vrm10ControlBone` rest basis | **correct** | 0.0° control |

Three further defects are real but are *separate* from the arm root cause, and two of them are missing
features rather than corrupted transforms:

* **Torso yaw is switched off in the shipping scene** (`kalidokitTorsoYawScale: 0`) — so the chest facing
  error equals the entire body turn (45° in → 45° error, 90° in → 90° error). The *solver* is exact
  (`hips.y` reproduces the true turn to 0.1°). What makes yaw unusable when switched on is a **1.40× gain**
  bug, not the solve. §7.
* **Head and Neck are never written by this path at all** — no `HumanBodyBones.Head` / `.Neck` anywhere in
  `Runtime/`. §9.
* **Face expressions are explicitly skipped on the OAK-D path** (`AppBootstrap.cs:770`), so `faceProvider`
  is null and no blendshape is ever driven. §9.

The other agent's leg diagnosis (`KalidokitLegSolver` + `KMath` spherical coordinates as "the exact
culprit") is **refuted** by measurement — see §5 and §14. Its torso-yaw finding is **confirmed** (§7).
Its claim that the video shows sitting is **wrong**: `SR.mp4` contains no sitting segment (§8).

---

## 2. Current retargeting architecture

```
OAK-D + RTMW3D  (python-sidecar~/wholebody_udp_sender.py)
  camera space: X right, Y DOWN, Z forward(away)     21 of 33 JointId slots filled
        │  UDP JSON  { lm[33], lh[21], rh[21], xyz, src }
        ▼
OakDUdpPoseProvider.cs:373       converter.ToUnity(x,y,z)
  PoseSpaceConverter  signX=+1 (poseFlipX 0), signY=-1 (poseFlipY 1), signZ=+1 (poseFlipZ 0)
        ▼
PoseFrame            Unity space: X = subject's anatomical LEFT, Y = UP, Z = subject's BACK
        ├──────────────────────────────► PoseDebugSkeleton.Render()   (the green skeleton — raw, unretargeted)
        ▼
PoseBuffer (P1-3, 40 ms presentation delay) → JointFilterPipeline
        ▼
KalidokitControlRigDriver.Apply()
   1. adapt to Kalidokit: landmarks[i] = (x*mx, -y, z)   → X = LEFT, Y = DOWN, Z = BACK
   2. KalidokitPoseSolver.Solve()  → per-bone Euler radians (three-vrm convention)
        ├─ KalidokitArmSolver.Solve → FindRotation / AngleBetween3DCoords → RigArm
        ├─ CalcHipsAndSpine         → RollPitchYaw2 → jump-fixes → ×PI
        └─ CalcLegs                 → GetSphericalCoords / GetRelativeSphericalCoords → RigUpperLeg
   3. torso conditioners: DampYaw × torsoYawScale, roll × torsoRoll, ADR-026 spine bend
   4. LimbGate confidence gate (P0-1) per arm/leg
   5. ApplyBone: KMath.ThreeEulerToUnity(euler, eulerSigns, flipQuat) → Slerp into bone.localRotation
        ▼
Vrm10RuntimeControlRig    normalized bones — EVERY control bone's rest world rotation is IDENTITY
        ▼
Vrm10ControlBone.ProcessRecursively()   localRotation = init * (inv(initGlobal) * normalized * initGlobal)
        ▼
raw VRM skeleton → renderer
```

### Measured frames (not assumed)

The two frames the whole audit hinges on were **measured**, not inferred:

* **VRM normalized rest basis** — parsed straight out of the `.vrm` files
  (`docs/audit/retarget_trace/vrmrest.py`), with UniVRM's importer axis inversion applied.
  `Vrm10.LoadBytesAsync` passes `importerContextSettings: null`, so `ImporterContextSettings.InvertAxis`
  defaults to `Axes.Z` → glTF **Z is negated** on import. Cross-checked against the video: a +Z-facing
  VRM 1.0 glTF then faces **−Z** in Unity, and the Mirror scene camera sits at `(0, 0.59, −3.41)` with
  identity rotation looking along **+Z** — which is exactly why the avatar shows its face in `SR.mp4`.

  ```
  VirtualMirrorRigTemplate.vrm (VRM 1.0), Unity model space after ReverseZ:
    leftUpperArm  -> leftLowerArm    dir = (+1.000, +0.000, +0.000)     leftShoulder.x  = +0.02
    rightUpperArm -> rightLowerArm   dir = (-1.000, +0.000, +0.000)     rightShoulder.x = -0.02
    leftUpperLeg  -> leftLowerLeg    dir = (+0.000, -1.000, +0.000)
    spine         -> chest           dir = (+0.000, +1.000, +0.000)
  ```

  So the normalized rig frame is **X = avatar's LEFT, Y = up, Z = avatar's BACK** — the *same anatomical
  frame as `PoseFrame`*. `Anime+Boy+V-tuber.vrm` (VRM 0.x) agrees once UniVRM's 0.x→1.0 migration
  180°-yaw is accounted for.

* **Control-bone rest rotation** — `Vrm10ControlBone`'s constructor sets `ControlBone.position` but never
  its rotation, then `SetParent(parent.ControlBone, true)`. A fresh `GameObject` has identity world
  rotation, and `worldPositionStays: true` preserves it. Therefore **every** control bone rests at
  identity world rotation, so `bone.localRotation` acts about the **model's** axes and
  `W_bone = q_hips · q_spine · q_chest · … · q_bone`, with the bone's direction being `W_bone · restDir`.
  The offline replica uses exactly this rule and the aim-solver control validates it at 0.0°.

### Live configuration (from `Scenes/Bootstrap.unity`, not the C# defaults)

```
useOakUdpTracking 1   useKalidokitBody 1   useIkDriver 0
kalidokitBodyFlipQuat 2      kalidokitBodyEulerSigns (1,1,1)   kalidokitBodyLerp 0.5
kalidokitBodyMirror 0        kalidokitBodyTorsoRoll 0          kalidokitBodyLegs 1
kalidokitSpineBendScale 1    kalidokitSpineBendBaselineTau 8   kalidokitTorsoYawScale 0
poseFlipX 0  poseFlipY 1  poseFlipZ 0     limbConfidenceThreshold 0.3
wristRotationWeight 0        trackLegs 1   trackPosition 1     showDebugSkeleton 1
poseInterpolationDelayMs 40
```

---

## 3. Test poses

Eight fixed poses in `PoseFrame` space, hip-relative metres, anatomically plausible proportions
(`docs/audit/retarget_trace/audit.py`, `poses()`). Deterministic — no live capture, no noise, so any
error that shows up is the transform chain's own.

| id | pose |
|---|---|
| P1 | standing straight, arms hanging, feet under hips |
| P2 | subject's right arm straight overhead |
| P3 | both arms straight overhead |
| P4 | T-pose, both arms straight out sideways |
| P5 | subject's right leg forward |
| P6 | whole body rotated 90° about Y |
| P6b | whole body rotated 45° about Y |
| P7 | deep squat (thighs ~54° forward, shins back) |
| P8 | sitting (thighs horizontal forward, shins down, slight trunk lean) |

**Scoring.** For each bone: the *wanted* world direction is the source segment reflected in X, because the
retarget deliberately does two things that together form a mirror — Kalidokit's left/right **cross-map**
(the avatar's LEFT arm is solved from landmarks 12/14/16, the subject's RIGHT arm) plus the sagittal
reflection a mirror image implies. Both were verified against `SR.mp4` at t=150 s (subject raises the arm
that appears image-left; the avatar raises the arm that appears screen-right, i.e. the same side of the
user's own visual field). **The cross-map is not a bug** — that is settled, so it is scored as intended.

---

## 4. Arm trace

### 4.1 Result

```
############ flipQuat=2  eulerSigns=(1,1,1)  torsoYawScale=0  torsoRoll=0 ############

=== P1 standing straight ===
  bone            want dir               got dir                     err   solver euler XYZ deg
  leftUpperArm    (+0.07,-1.00,+0.00)    (+0.25,-0.95,-0.20)        15.5   (  -11.5,  -57.3,  +62.9)
  leftLowerArm    (+0.04,-1.00,+0.00)    (+0.23,-0.95,-0.22)        16.9   (  +17.2,   -1.3,   +0.0)
  rightUpperArm   (-0.07,-1.00,+0.00)    (-0.25,-0.80,-0.55)        35.7   (  +11.5,  +57.3,  -62.9)
  rightLowerArm   (-0.04,-1.00,+0.00)    (-0.23,-0.79,-0.57)        37.1   (  +17.2,   +1.3,   -0.0)

=== P4 T-pose arms out ===
  leftUpperArm    (+1.00,+0.00,+0.00)    (+0.88,+0.10,+0.47)        28.6   (  -11.5,  +28.6,   +0.0)
  rightUpperArm   (-1.00,+0.00,+0.00)    (-0.88,-0.10,+0.47)        28.6   (  +11.5,  -28.6,   -0.0)

=== P3 both arms overhead ===
  leftUpperArm    (+0.04,+1.00,+0.00)    (-0.19,+0.81,-0.56)        37.2   (  -11.5, -116.4,  -64.2)
  rightUpperArm   (-0.04,+1.00,+0.00)    (+0.19,+0.96,-0.20)        17.9   (  +11.5, +116.4,  +64.2)
  rightLowerArm   (+0.04,+1.00,+0.00)    (+0.02,+0.68,+0.74)        47.5   (  -17.2,   +3.1,  +59.7)
```

Per-pose arm mean error (deg):

| variant | P1 | P2 | P3 | P4 | P5 | P6 | P6b | P7 | P8 |
|---|---|---|---|---|---|---|---|---|---|
| **BASELINE (shipping)** | 26.3 | 33.6 | 31.8 | 28.6 | 26.3 | 24.2 | 25.9 | 26.3 | 30.2 |
| FK aim (own rig) | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 |

### 4.2 The three specific defects

**(a) Antisymmetric pitch where a mirror needs symmetric pitch.** `RigArm` builds the second arm as
`(−x, −y, −z)` of the first (P1: left `(−11.5, −57.3, +62.9)`, right `(+11.5, +57.3, −62.9)` — exact
negatives). But reflecting a three.js Euler-XYZ rotation across the sagittal plane is
`(+x, −y, −z)`, not `(−x, −y, −z)`: with `M = diag(−1,1,1)`,
`M·Rx(x)·M = Rx(x)` while `M·Ry(y)·M = Ry(−y)` and `M·Rz(z)·M = Rz(−z)`. **The `x` component carries the
wrong relative sign between the two arms**, and `upperArm.x -= 0.3f * invert`
(`KalidokitArmSolver.cs:77`) is what injects it. Consequence, straight out of the numbers: for identical
hanging arms the avatar's left arm hangs 11.5° forward and its right arm 33° forward — **20.2° of
left/right asymmetry from a perfectly symmetric input.**

**(b) A saturated forearm clamp.** `lowerArm.x = clamp(lowerArm.x, −0.3, 0.3)`
(`KalidokitArmSolver.cs:84`). `FindRotation(...).x` for any near-vertical or near-horizontal forearm is
±0.5, times `2.14 * invert` = ±1.07 rad — **always past the clamp**. Both forearms therefore sit pinned at
+0.3 rad = **+17.2° regardless of the actual arm**, in every one of the eight poses (see the constant
`+17.2` in the `lowerArm` euler column above). That is a constant forearm bend the input can never remove.

**(c) Mixed units.** `KMath.NormalizeRadians` / `NormalizeAngle` divide by π, so every `FindRotation`
component is a **π-normalized** value in roughly [−1, 1] — not radians. `RigArm` then multiplies some
components by `PI` (restoring radians) and others by `2.3` / `2.14` (arbitrary gains), and *subtracts a
raw π-normalized value from a radian value* at `upperArm.y -= lowerArmXOriginal`
(`KalidokitArmSolver.cs:75`). The result is dimensionally inconsistent, which is why no single constant
tweak fixes it (§4.3).

### 4.3 Constant tweaking does not rescue it

Each variant changes exactly one thing:

| variant | arm mean | arm max | L/R asymmetry on P1 |
|---|---|---|---|
| **BASELINE (shipping)** | **28.2** | **47.5** | **20.2** |
| drop `upperArm.x -= 0.3*invert` | 31.4 | 64.7 | 40.7 |
| make `upperArm.x` symmetric (`abs`) | 42.1 | 81.8 | 33.1 |
| symmetric `x` **and** no 0.3 offset | 41.7 | 64.7 | 0.0 |
| drop the `lowerArm.x` clamp | 29.8 | 57.1 | 20.2 |
| FK aim (own rig, roll-free) | **0.0** | **0.0** | **0.0** |

Every single-constant change makes the arms **worse**. The offset and clamp are load-bearing for a
formulation that is already wrong — this is a structural mismatch, not a mistuned constant. Per the audit
brief, nothing here was changed.

---

## 5. Leg trace

**The legs are correct.** 5.6° mean / 7.7° max across all eight poses, and the residual is Kalidokit's
own `UpperLegZOffset = 0.1f` (`KalidokitPoseSolver.cs:33`) = 5.73° of deliberate outward splay per leg,
signed by `invert`, which then propagates to the shin because the shin inherits the thigh's world rotation.

```
=== P7 deep squat ===
  leftUpperLeg    (+0.11,-0.59,-0.80)    (+0.20,-0.61,-0.77)         5.9   (  +53.7,   +9.9,  -11.9)
  leftLowerLeg    (-0.07,-0.72,+0.69)    (-0.20,-0.70,+0.68)         7.7   (  -97.9,   +0.0,   +0.0)

=== P8 sitting ===
  leftUpperLeg    (+0.02,-0.05,-1.00)    (+0.12,-0.05,-0.99)         5.7   (  +87.3,   +1.4,   -7.1)
  leftLowerLeg    (-0.00,-1.00,+0.05)    (-0.02,-1.00,+0.05)         1.4   (  -89.9,   +0.0,   +0.0)
```

`hip → knee → ankle` is reproduced to within 1.4°–7.7° including squat and sitting. `KMath`'s spherical
coordinates with the baked leg `axisMap {x←y, y←z, z←x}` are **doing their job**. `KalidokitLegSolver`
and `KMath.cs` are **not** the culprit.

Two real *limits* (not corruption), both visible in `SR.mp4`'s kick/squat block:

* `RigUpperLeg` clamps thigh pitch to `[0, 0.5] × π` = **0°…90°**. The lower bound of **0** means the thigh
  **can never extend behind the hip** — no backward kick, no walking stride, no lunge.
* `lowerLeg = (−|θ| × π, 0, 0)` — the knee is forced to bend one way only, and shin roll/yaw are hard-zeroed.
* **No `LeftFoot` / `RightFoot` bone is ever driven** (the driver binds Hips, Spine, Chest, upper/lower
  arms, hands, upper/lower legs — no feet). The ankle never rotates, so in a squat the feet stay parallel
  to their rest orientation. That is what reads as "distorted squat", not a bad knee solve.

The visible "exaggerated wide-legged stance" is the constant **5.7° splay per leg (11.5° total)** plus the
absent ankle — small, constant, and cheap to fix, and *not* a broken transform.

---

## 6. Torso trace — bend

`Kalidokit.CalcHipsAndSpine` hard-zeroes pitch (`hips.x = 0`, `spine.x = 0`,
`KalidokitPoseSolver.cs:67`/`:82`), so ADR-026 adds the waist bend from the measured trunk tilt. Two
consequences worth recording:

* **The pelvis never pitches.** `hips.x = 0` is unconditional, and the ADR-026 bend goes only to Spine and
  Chest. A forward bend therefore always folds at the waist, never at the hips.
* **A held bend decays.** The ADR-026 high-pass (`spineBendBaselineTau = 8 s`) is a documented trade-off,
  quantified here:

  | bend held | still applied |
  |---|---|
  | 0 s | 100 % |
  | 2 s | 77.9 % |
  | 4 s | 60.7 % |
  | 8 s | 36.8 % |
  | 16 s | 13.5 % |

  Fine for transient bends (the ADR's stated intent); it means a *sustained* lean — including sitting back —
  relaxes to upright. Not touched.

## 7. Torso trace — yaw

**The Kalidokit yaw solve is exact.** `hips.y` reproduces the true body turn to within 0.1°:

```
  source turn  15 deg  ->  solver hips.y= +15.0  chest facing yaw= +21.0 deg  gain=1.40
  source turn  30 deg  ->  solver hips.y= +30.0  chest facing yaw= +42.0 deg  gain=1.40
  source turn  45 deg  ->  solver hips.y= +45.0  chest facing yaw= +63.0 deg  gain=1.40
  source turn  60 deg  ->  solver hips.y= +60.0  chest facing yaw= +84.0 deg  gain=1.40
  source turn  90 deg  ->  solver hips.y= +90.0  chest facing yaw=+126.0 deg  gain=1.40
```

Two separate faults sit on top of a good solve:

1. **Shipping config zeroes it.** `kalidokitTorsoYawScale: 0` in `Scenes/Bootstrap.unity:169`. Measured
   chest facing error:

   | pose | yawScale = 0 | yawScale = 1 |
   |---|---|---|
   | P1 standing | 0.0° | 0.0° |
   | P6b turned 45° | **45.0°** | 18.0° |
   | P6 turned 90° | **90.0°** | 36.0° |

   At scale 0 the facing error *is* the whole body turn. This confirms the other agent's finding, and the
   user's reading of it: the ADR-027 workaround has become the visible defect.

2. **The gain is 1.40×, and that is why scale 0 was chosen.** ADR-027 restored Kalidokit's per-bone
   dampeners `Hips 0.7 / Spine 0.45 / Chest 0.25` to fix a "~1.4× over-twist"
   (`KalidokitControlRigDriver.cs:86-88`) — but the driver applies the **same** damped yaw to Hips *and*
   Spine *and* Chest, and those bones **compose**: `0.7 + 0.45 + 0.25 = 1.40`. The over-twist was never
   removed; it is still exactly 1.40× at every angle. In Kalidokit's reference rig the dampeners
   *distribute* one rotation across the chain; here they *stack*. A 90° turn produces 126° of chest
   facing — the avatar ends up facing further round than the user, which "drags the arms" precisely as the
   ADR-027 regression note describes. **Turning `torsoYawScale` up without fixing the gain will reproduce
   that regression**, which is why §13 does not recommend it.

## 8. Sitting trace

**`SR.mp4` contains no sitting segment.** The on-screen capture script lists the six blocks:

```
1  stand still, arms down (15s)
2  walk toward the camera to ~1 m, then back — twice (20s)
3  wave arms fast -> big circles -> overhead reaches (20s)
4  alternate kicks -> ~5 squats -> fast high knees (20s)
5  dance, energetically (25s)
6  left hand behind back 3s, out, 8s, out — then right (30s)
```

At t = 410 s — the frame previously read as "sitting" — the subject is **standing**, resting an arm on a
cabinet. The "sitting is broken" symptom is not in this evidence at all.

On the substance: **pure FK already reproduces sitting geometry.** Synthetic P8 gives thigh 5.7° and shin
1.4° error. Hip flexion, knee flexion and spine lean are all reachable by the current architecture; what
sitting additionally needs is ankle orientation (**no foot bone is driven at all**, §5) and a trunk lean
that does not decay (§6). **IK is not indicated by any measurement here.** The correct order stands:
fix FK → validate sitting geometry → only then consider foot/ground IK.

## 9. Head trace

Not corrupted — **not wired**.

* `grep -rn "HumanBodyBones.Head\|HumanBodyBones.Neck" Runtime/` → **no matches.** The Kalidokit control-rig
  driver binds Hips, Spine, Chest, upper/lower arms, hands, upper/lower legs and the finger bones. Head and
  Neck are never written. The only mention anywhere is a comment in the *legacy* `HumanoidPoseRetargeter`
  (`:419`, "Neck/head orientation is intentionally NOT driven from pose landmarks") — and that path is
  bypassed entirely (`useIkDriver: 0`, `useKalidokitBody: 1`).
* Facial expression is a **separate** system and is also inert: `AppBootstrap.cs:770` returns early when the
  body provider is `OakDUdpPoseProvider` — *"Face tracking unavailable on the OAK-D path (no RGB webcam);
  skipping face provider"* — so `faceProvider` stays null and `VrmExpressionRetargeter` never receives a
  frame. `useFaceTracking: 1` in the scene has no effect on this path.
* Nose (0) and ears (7/8) **do** arrive from the sidecar (they are in `COCO17_TO_JOINTID`), so the head data
  needed for a neck solve is present and unused.

These are two independent missing features. Neither is the retarget defect, and conflating "head bone
rotation" with "facial blendshapes" would mis-scope the fix.

## 10. Rest-pose / basis analysis

**No systematic basis error.** This was the leading hypothesis and it is disproved.

| bone | normalized rest direction (Unity model space) |
|---|---|
| leftUpperArm / leftLowerArm | (+1, 0, 0) — avatar's LEFT |
| rightUpperArm / rightLowerArm | (−1, 0, 0) |
| leftUpperLeg / leftLowerLeg | (0, −1, 0) |
| rightUpperLeg / rightLowerLeg | (0, −1, 0) |
| spine / chest | (0, +1, 0) |
| avatar facing | (0, 0, −1) |

Rest **rotation** is identity for every control bone (§2), so there is no 90° offset, no 180° flip and no
forward/up convention mismatch to compensate. The decisive proof: an aim solver (`FromToRotation` from
each bone's rest direction onto the wanted direction, composed parent-relative through this exact
hierarchy) reaches **0.0° error on every bone in every pose**. If the basis were wrong by any fixed
rotation, that control could not be exact.

The one non-obvious point, recorded so it is not re-litigated: `PoseFrame` space and the VRM normalized
frame turn out to be the **same anatomical frame** (X = left, Y = up, Z = back). That is a coincidence of
`poseFlipY: 1` alone plus UniVRM's `Axes.Z` import — it is *not* robust to changing `poseFlipX` /
`poseFlipZ`, and anything built on it should assert it rather than assume it.

## 11. Quaternion analysis

* **Multiplication order — correct.** `ApplyBone` writes `bone.localRotation`; Unity composes
  `world = parent.rotation * localRotation`, and the offline replica uses that order to reach 0.0° on the
  control. No parent-space conversion is needed because rest rotations are identity.
* **`flipQuat = 2` is correct** — verified by sweep, not by argument:

  | flipQuat | component map | mean direction error |
  |---|---|---|
  | 0 (`-x,-y,+z`) | Z-reflection | 73.3° |
  | 1 (`+x,+y,-z`) | — | 32.8° |
  | **2 (`+x,-y,-z`)** | **X-reflection** | **15.1°** |
  | 3 (`-x,+y,-z`) | — | 24.4° |

  Note this contradicts the naive handedness derivation (which predicts flip 0, the *worst* option). The
  extra reflection is absorbed by Kalidokit's own left/right cross-map, which already mirrors the subject.
  The Inspector tooltip *"2 is correct for this rig"* is **right**; it is now measured rather than
  empirical. **Do not change it.**
* **`eulerSigns = (1,1,1)`** — no per-axis pre-negation in play.
* **The Euler round-trip is where the damage is**, not the quaternion algebra. `three.js` XYZ Euler order
  is not mirror-symmetric under negating all three angles (§4.2a), so any solver that produces the opposite
  limb by `invert = ±1` on all components is structurally unable to mirror correctly. That is inherent to
  `RigArm`'s formulation.
* `Slerp(current, target, 0.5)` per frame is a first-order low-pass, not a correctness factor; the replica
  uses the converged value.

## 12. Magic constants

Inventoried, **not changed**. "Effect" is measured where it was measurable.

### `KalidokitArmSolver.RigArm` (the first bad stage)

| constant | line | affects | measured effect | evidence for the value |
|---|---|---|---|---|
| `upperArm.z *= -2.3 * invert` | :73 | shoulder roll, both arms | dominant term; ±62.9° at P1 | Kalidokit verbatim |
| `upperArm.y *= PI * invert` | :74 | shoulder swing | ±57.3° at P1 | Kalidokit verbatim |
| `upperArm.y -= lowerArmXOriginal` | :75 | shoulder swing | **mixes π-normalized into radians** | Kalidokit verbatim |
| `upperArm.y -= -invert * max(lowerArmZ, 0)` | :76 | shoulder swing | forearm→shoulder cross-coupling | Kalidokit verbatim |
| `upperArm.x -= 0.3 * invert` | :77 | shoulder pitch | **source of the 20.2° L/R asymmetry** | Kalidokit verbatim |
| `lowerArm.z *= -2.14 * invert` | :79 | elbow | — | Kalidokit verbatim |
| `lowerArm.y *= 2.14 * invert` | :80 | elbow | — | Kalidokit verbatim |
| `lowerArm.x *= 2.14 * invert` | :81 | forearm pitch | pushes past the clamp in all 8 poses | Kalidokit verbatim |
| `clamp(upperArm.x, -0.5, PI)` | :83 | shoulder pitch | inactive at these magnitudes | Kalidokit verbatim |
| `clamp(lowerArm.x, -0.3, 0.3)` | :84 | forearm pitch | **saturated → constant +17.2°** | Kalidokit verbatim |
| `clamp(lowerArm.z, -2.14, 0)` | :48 | elbow | one-way elbow | Kalidokit verbatim |
| `hand.y = clamp(hand.z*2, -0.6, 0.6)` | :86 | hand | inert (hand points absent, §12.4) | Kalidokit verbatim |
| `hand.z *= -2.3 * invert` | :87 | hand | inert | Kalidokit verbatim |

### `KalidokitPoseSolver` (legs + torso)

| constant | line | affects | measured effect |
|---|---|---|---|
| `UpperLegZOffset = 0.1` | :33 | thigh splay | **exactly the 5.7°/leg residual** |
| `clamp(upperLeg.x, 0, 0.5) × PI` | :114 | thigh pitch | 0°…90°; **thigh cannot go behind the hip** |
| `clamp(upperLeg.y, -0.25, 0.25) × PI` | :115 | thigh yaw | ±45° |
| `clamp(upperLeg.z, -0.5, 0.5) × PI` | :116 | thigh roll | ±90° |
| `lowerLeg = (-abs(theta) × PI, 0, 0)` | :103 | knee | one-way bend; shin roll/yaw zeroed |
| `hips.x = 0` / `spine.x = 0` | :67 / :82 | torso pitch | **pelvis never pitches** |
| jump-fixes `y-=2; y+=0.5; z=±1-z` | :55-78 | torso | the ±180° ambiguity source ADR-027 rate-limits |
| `Remap(abs(y), 0.2, 0.4)` roll fade | :65 / :80 | torso roll | inert (`torsoRoll = 0`) |
| `× PI` on hips/spine | :84-85 | torso | restores radians |

### `KMath`

| constant | affects | note |
|---|---|---|
| `NormalizeRadians` / `NormalizeAngle` **÷ π** | everything | source of the mixed-units hazard (§4.2c) |
| leg `axisMap {x←y, y←z, z←x}` | legs | baked in; **verified correct** (§5) |
| `flipQuat` 0..3, `eulerSigns` | all bones | `2` / `(1,1,1)` **verified correct** (§11) |

### `KalidokitControlRigDriver`

| constant | value | affects | measured effect |
|---|---|---|---|
| `HipsYawDamp / SpineYawDamp / ChestYawDamp` | 0.7 / 0.45 / 0.25 | torso yaw | **sum 1.40 → 1.40× gain** (§7) |
| `torsoYawScale` | **0** (scene) | torso yaw | facing error == full body turn |
| `torsoRoll` | 0 (scene) | torso roll | roll disabled |
| `spineBendBaselineTau` | 8 s | waist bend | held bend decays (§6) |
| `spineBendSmoothTau` / `MaxRateDeg` | 0.12 s / 250 | waist bend | spike reject |
| `SpineWarmupFrames` | 45 | waist bend | ~2 s of no bend at startup |
| `yawMaxRateDeg` / `yawSmoothTau` | 140 / 0.15 s | torso yaw | inert while scale = 0 |
| `yawDeadzoneLo / Hi` | 8° / 22° | torso yaw | inert while scale = 0 |
| `lerpAmount` | 0.5 | all bones | per-frame low-pass |
| bend split Spine/Chest | 0.5 / 0.5 | waist bend | totals 1.0 |
| `limbConfidenceThreshold` | 0.3 | arms/legs | P0-1 hold gate |
| `wristWeight` | **0** (scene) | wrist | wrist rotation disabled |
| `fingerCurlAxis` | (0,0,−1) | fingers | inert (no hand data) |
| trunk `vert > 0.05` gate | 0.05 m | waist bend | rejects degenerate trunks |

### §12.4 — inert-by-data constants

The sidecar fills **21 of 33** slots (`_body=21/33` in the video's console): `COCO17_TO_JOINTID` (17) +
`FOOT_TO_JOINTID` (4). Slots **17–22 (pinky / index / thumb)** are always **zero**. So:

* the Hand bone is driven to identity every frame (`leftHandValid` / `rightHandValid` are always false —
  correctly guarded by ADR-023, `KalidokitControlRigDriver.cs:353`), and
* `wristRotationWeight: 0` disables the ADR-021 wrist swing.

The hands are therefore **permanently at rest** by construction — matching the stiff hands throughout
`SR.mp4`. This is data availability, not a transform bug.

---

## 13. First bad stage

**`KalidokitArmSolver` — the arm angle extraction plus `RigArm`.**

Per-pose, per-region verdict (`solver output correct? / control-rig rotation correct? / final VRM bone
correct?`):

| pose | region | solver out | control-rig rot | final bone |
|---|---|---|---|---|
| P1 | arms | **NO** (26.3°, 20.2° asym) | NO (inherits) | NO |
| P1 | legs | YES (5.7° = splay) | YES | YES |
| P1 | torso | YES | YES | YES |
| P2/P3 | arms | **NO** (33.6 / 31.8°) | NO | NO |
| P4 (T-pose) | arms | **NO** (28.6°) | NO | NO |
| P5 | legs | YES (5.7°) | YES | YES |
| P6/P6b | torso yaw | **YES** (exact) | **NO** (scale 0; 1.40× when on) | NO |
| P7 (squat) | legs | YES (5.9 / 7.7°) | YES | YES (no ankle) |
| P8 (sitting) | legs | YES (5.7 / 1.4°) | YES | YES (no ankle) |
| all | head / neck | **n/a — never written** | n/a | NO |

Reading it as a chain: the pose data reaches the solver intact, the leg branch of the solver is sound, the
Euler→quaternion conversion and the control-rig composition are sound — and the arm branch injects 26–34°
of error plus a 20° left/right asymmetry before any of that runs. **Arms first; torso yaw second
(config + gain); head/neck and ankle third (unimplemented).**

---

## 14. Evidence from SR.mp4 → verified code behaviour

| timestamp | visible symptom | verified cause |
|---|---|---|
| t=96 s, t=160–192 s | subject stands with arms hanging; **debug skeleton hangs its arms correctly**; avatar holds both arms bent ~90° forward with hands in front of the belly | §4 P1: left upper arm 15.5° off, right 35.7° off, both pitched forward — `upperArm.x -= 0.3*invert` + the saturated `lowerArm.x` clamp pinning +17.2° of forearm bend |
| t=100 s, t=124–140 s | subject raises **both** arms; avatar raises **one** | §4.2a — the 20.2° L/R asymmetry, the two arms have different effective gains |
| t=150 s | subject: one arm up-bent, one arm out; avatar reproduces the gross shape on the mirrored side | the left/right **cross-map is correct** — this frame is what settles it |
| t=148 s vs t=164 s | avatar's arms swing between "out sideways" and "bent forward" while the subject barely moves | §4.2c — the mixed-units cross-coupling `upperArm.y -= lowerArmXOriginal` makes shoulder swing depend on raw forearm numbers |
| throughout | avatar's chest stays frontal while the subject turns | §7 — `kalidokitTorsoYawScale: 0` |
| throughout | avatar's head/face is static | §9 — Head/Neck never written; face provider skipped on the OAK-D path |
| throughout | hands stiff, never track the wrist | §12.4 — landmarks 17–22 always zero, `wristRotationWeight: 0` |
| t=208 s, t=232 s | squat reads as a crouch with oddly-oriented feet | §5 — thigh/knee are correct to ~6–8°; **no foot bone is driven**, and the thigh clamps at 90° |
| t=410 s | subject **standing**, arm on a cabinet | not sitting — §8 corrects the earlier reading |
| console, all frames | `_body=21/33` | §12.4 — 17 COCO + 4 foot slots; hand slots zero |

The green skeleton is `PoseDebugSkeleton.Render()` drawing `landmark.Position` **directly**, unretargeted
(`Runtime/Rendering/PoseDebugSkeleton.cs:51`). Its agreement with the subject in every frame examined is
therefore direct evidence that `PoseFrame` is good — which the 0.0° aim-solver control then converts from
"looks right" into "is right".

---

## 15. Recommended SINGLE next implementation

**Replace the arm branch only — `KalidokitArmSolver` — with a direct FK aim solve on the same normalized
control rig. Change nothing else.**

Why this one and nothing else:

* It is the **measured** first bad stage (§13).
* It is **provably sufficient** for the arms: the hybrid (FK-aim arms, everything else exactly as shipping)
  scores **0.0° arms / 5.6° legs** versus **28.2° / 5.6°** today.

  | variant | ARMS mean/max | LEGS mean/max |
  |---|---|---|
  | BASELINE (shipping) | 28.2 / 47.5 | 5.6 / 7.7 |
  | **HYBRID: FK-aim arms only** | **0.0 / 0.0** | 5.6 / 7.7 |
  | FK aim everywhere | 0.0 / 0.0 | 0.0 / 0.0 |

* It is **more noise-tolerant than what ships**, at every jitter level (mean arm error, deg; 40 reps,
  scored against the clean pose):

  | | σ=0 | σ=1 cm | σ=2 cm | σ=4 cm |
  |---|---|---|---|---|
  | shipping arms | 28.2 | 28.1 | 29.3 | 33.5 |
  | **FK-aim arms** | **0.0** | **3.8** | **7.6** | **15.1** |

  So this is not trading accuracy for fragility — the aim solve is better on clean *and* noisy input. (For
  reference the shipping **leg** path is the noise-sensitive one: 5.6 → 20.0° at σ=4 cm versus 0.0 → 9.1°
  for aim. That is an argument for later, not now.)

* It **keeps the validated parts**: `flipQuat = 2`, the rest basis, the control-rig composition, the P0-1
  `LimbGate`, the ADR-026/027 torso conditioners, the 40 ms P1-3 buffer, all untouched.

Two things the implementation **must** carry, or it will regress to a known-fixed bug:

1. **Roll.** `FromToRotation` is roll-free by construction, which is what prevents the ADR-022
   "forearm helicopter". The upper arm's roll about its own axis must be **derived from the elbow plane**
   (`shoulder → elbow → wrist`), not left free and not taken from a global reference — that global-reference
   guess is exactly what ADR-022 replaced Kalidokit in for. The 0.0° figures above validate *direction*
   only; roll needs its own acceptance check.
2. **The mirror stays where it is.** The left/right cross-map plus the sagittal reflection is the intended
   mirror (§3, confirmed at t=150 s). A new solver must reproduce it deliberately — `kalidokitBodyMirror`
   stays **off** (reflecting the input twists a rotation retarget, ADR-023).

**Not implemented.** This audit is diagnosis only.

---

## 16. Rejected fixes — do not change these yet

| change | why not |
|---|---|
| `flipQuat`, `eulerSigns` | **Measured correct.** `2` / `(1,1,1)` beats every alternative (73.3 / 32.8 / 24.4 vs 15.1). The naive handedness derivation says flip 0, which is the worst option — do not "correct" it. |
| the rest-pose basis / any per-bone offset | No basis error exists (§10); the aim control is exact. Adding offsets would mask the arm defect. |
| `KalidokitLegSolver`, `KMath` spherical coords | **Refuted.** 5.6° mean, and the residual is the deliberate `UpperLegZOffset`. Rewriting this would burn effort on a working component. |
| removing individual `RigArm` clamps/offsets | Every single-constant variant made the arms **worse** (§4.3). The formulation is wrong, not the constants. |
| raising `kalidokitTorsoYawScale` | Would reintroduce the ADR-027 regression: the gain is still **1.40×**, so a 90° turn becomes 126° and drags the arms. Fix the gain first, in its own change. |
| enabling IK / foot IK | No measurement supports it. FK already reproduces sitting and squat geometry to ~6°. What sitting is missing is an **ankle** (no foot bone driven) and a non-decaying trunk lean. Correct order: fix FK → validate → then consider IK. |
| head / neck driving, face blendshapes | Real gaps (§9) but **separate features**, and two distinct ones. Bundling them into the arm fix would make the regression surface unreadable. |
| anything in `python-sidecar~`, P1-1/2/3, F-08 | Frozen by the audit brief. The 0.0° aim control shows the incoming data is already sufficient — there is nothing to gain upstream right now. |
| `UpperLegZOffset`, the 5.7° splay | Real but cosmetic and constant. Bundle it with the leg work, not the arm fix. |

---

## 17. Regression status

Nothing in the tracking or retarget behaviour was modified.

| item | status |
|---|---|
| P1-1 (joint tracker) | **unchanged** — no file under `python-sidecar~` touched |
| P1-2 (freshness) | **unchanged** |
| P1-3 (pose buffer, 40 ms) | **unchanged** — `poseInterpolationDelayMs: 40` as shipped |
| F-08 (surface-aware depth) | **unchanged** |
| P1-4 (kinematic recovery) | **still disabled** |
| PoseSpaceConverter / poseFlip* | **unchanged** (`0 / 1 / 0`) |
| Kalidokit solvers + `KMath` | **unchanged** — not one constant edited |
| `flipQuat`, `eulerSigns`, `torsoYawScale`, `torsoRoll`, `spineBend*` | **unchanged** |
| `Scenes/Bootstrap.unity` | **unchanged** |

### Files changed (diagnostics only)

* `Runtime/Retargeting/KalidokitControlRigDriver.cs` — added a DIAG-ONLY direction trace
  (`SetDirectionTrace`, `TraceDirections`, `TraceBone`, `LogTrace`) plus two `Transform` lookups
  (`leftFoot` / `rightFoot`) used only by it. Default **OFF**; when off the added cost is one `bool` test
  per applied frame. No pose maths altered.
* `Runtime/Bootstrap/AppBootstrap.cs` — added `kalidokitDirectionTrace` (default **false**) and
  `kalidokitDirectionTraceEveryFrames` (default 60), pushed to the driver alongside the existing live
  tunables. New serialized fields are absent from the scene YAML, so they take these C# defaults.
* `docs/audit/retarget_trace/*.py` — the offline harness (new, docs only).
* `docs/UNITY_RETARGETING_AUDIT_2026-09-08.md` — this report.

### Reproducing the numbers

```bash
cd Assets/Games/viitorx-vrm-avtar-unity/docs/audit/retarget_trace
python audit.py 2        # per-pose want-vs-got direction table for the shipping config
python audit.py sweep    # flipQuat 0..3 comparison
python exp.py            # isolating variants + L/R asymmetry + torso facing
python exp2.py           # hybrid, noise robustness, yaw gain, bend decay
python vrmrest.py <path-to.vrm>   # normalized rest directions in Unity model space
```

To capture the same trace live instead: tick **`kalidokitDirectionTrace`** on `AppBootstrap` in
`Bootstrap.unity` during Play. Each traced bone logs `want` / `got` / `errDeg` / `solverEulerDeg` every
`kalidokitDirectionTraceEveryFrames` applied frames. Turn it back **off** afterwards.
