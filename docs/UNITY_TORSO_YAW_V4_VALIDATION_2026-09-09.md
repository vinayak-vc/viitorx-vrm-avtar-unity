# Torso yaw V4 — re-enable, sweep, validate — 2026-09-09

**The one controlled change made:** `Scenes/Bootstrap.unity`, `kalidokitTorsoYawScale: 0` → **`0.75`**.
One line. Nothing else in the repo was modified — no torso algorithm, no `ArmAimSolver`, no roll
hysteresis, no Python/OAK-D, no legs, IK, head, hands, feet, `PoseSpaceConverter`, `flipQuat`,
`eulerSigns`, or P1/F-08.

**Verdict: CONDITIONAL PASS.** The change is safe and fixes the V3 symptom on slow or sustained turns.
Verified end-to-end from a cold start at the shipped value, on held frame f419: shoulder yaw error
**−50.84° → −3.64°**, worst body-relative arm error **51.22° → 7.16°** (the control frame's own residual
is 5.43°), and the hands move from **0.61/0.35** of their proper distance from the spine axis to
**0.92/0.81** — i.e. out of the torso. The arm path does not regress: source → skinned arm direction
stays at **0.0000°** at every scale while the torso rotates up to 62.9°.

**But it does not deliver on real motion, and the sweep proves the existing implementation is
insufficient** — the brief's escape clause is now satisfied. Two independent measurements agree:
replayed over the whole 376-frame clip at its own 30 fps through the *real shipping* `DampYaw`, the
mean shoulder-yaw error improves only **7.40° → 7.21°** (2.6 %), the maximum is **unchanged at 50.84°**,
7.2 % of frames get *worse*, and the effective gain on frames above the dead zone is **0.138** — not the
1.230 a held frame shows. The ADR-027 conditioner's bandwidth is far below human dance motion.

**§5 has been REPLACED — the OAK-D path has now been exercised, and the original §5a was wrong.** It
claimed the device could not be tested because `depthai` was missing and no recorded log carried a
torso-yaw signal. **`depthai 2.32.0.0` is installed** (the original check used a different interpreter),
the device enumerates and streams at 30 fps, and **111,119 recorded frames across 11 captures do carry
the signal** — `sender_log.jsonl` logs the shoulder landmarks after `build_body_landmarks`, and
`--flatten-trunk` defaults to FALSE in Milestone-2, so shoulder Z is measured stereo depth.

**What that evidence adds, and why the verdict stays CONDITIONAL:**

* **Stability holds on the real depth path.** ±180° ambiguity flips: **2 in 55,408 valid frames**.
  Output sign reversals: **0 at every scale in every capture**. Live device in good conditions: max
  frame step **2.11°**, zero flips. The rate limiter works.
* **No arm regression.** Source → *actual skinned* bone direction stays at **p50 0.057–0.171°, max
  ≤0.78°** across the live scale sweep, and p50/p95 are flat or better on video as the torso rotates.
* **The 1.4× over-gain is confirmed live**: measured gain **1.194** at scale 1.0 on the OAK path,
  against §2b's held-frame 1.230, arrived at independently.
* **A new, higher-impact defect: the torso path has no confidence gate.** `CalcHipsAndSpine` reads
  `lm[11/12/23/24]` unconditionally, and an absent detection makes `atan2(0,0)` command **exactly +90°
  of yaw**. Caught live on an empty room at the shipped 0.75: landmark confidence **0.71** (passing
  every existing gate), hallucinated depth → 40.46° source yaw → **avatar torso twisted 23.82°**. At
  `torsoYawScale = 0` this was multiplied away; restoring the scalar arms it.
* **Rest jitter.** At 0.75 the avatar's torso wanders with a **5.4–7.6° standard deviation while the
  subject is standing still**, against exactly 0.000° at scale 0.
* **The brief's guided motion list was NOT completed** (§5f) — two attempts failed for operator
  reasons, so whether a deliberate 180° turn flips on this device is still unanswered.

---

## 1. Baseline — `torsoYawScale = 0`

Reproduced from V3, on the same five recorded frames, before any change. Live values read back out of
the running driver by reflection, not assumed:

```
torsoYawScale = 0.0   spineBendScale = 1.0   useAimArms = true   lerpAmount = 0.5   mirrorX = false
```

| frame | source shoulder yaw | avatar shoulder yaw | shoulder yaw err | source hip yaw | avatar hip yaw | hip yaw err | worst body-rel arm err |
|---|---|---|---|---|---|---|---|
| f084 (control) | 0.00° | **−0.00°** | 0.00° | 0.00° | **−0.00°** | 0.00° | **5.43°** |
| f237 | −26.86° | **−0.00°** | −26.86° | +22.39° | **−0.00°** | +22.39° | 26.75° |
| f333 | +7.21° | **−0.00°** | +7.21° | +7.66° | **−0.00°** | +7.66° | 16.85° |
| f339 | +9.92° | **−0.00°** | +9.92° | −42.22° | **−0.00°** | −42.22° | 13.60° |
| f419 | −50.84° | **−0.00°** | −50.84° | −39.06° | **−0.00°** | −39.06° | **51.22°** |

Per-bone control-bone yaw at baseline: Hips, Spine, Chest, UpperChest all **0.0000°**. Mean body-relative
arm error **22.77°**, max **51.22°**.

---

## 2. Scale sweep

Method: the scale was set at **runtime** by reflecting `AppBootstrap.kalidokitTorsoYawScale`, which
`AppBootstrap` pushes into the driver every frame (`SetTorsoYawScale(kalidokitTorsoYawScale)`), so the
whole sweep ran without touching disk. Each cell calls `Recalibrate()` first so the ADR-027 conditioner
re-seeds and the cell does not inherit the previous cell's slew state, then holds the recorded frame on
the real UDP wire for 5 s before measuring. 7 scales × 5 frames = 35 cells, plus renders.

### 2a. The table the brief asked for

`effective gain` = measured avatar shoulder-line yaw ÷ source shoulder-line yaw, **measured, not
configured**. `arm body error` = worst body-relative arm direction error over both arms.

| scale | shoulder yaw error (mean / max) | hip yaw error (mean / max) | effective gain, f419 rigid turn | arm body error (mean / max) | visual result |
|---|---|---|---|---|---|
| **0.00** | 18.97° / 50.84° | 22.27° / 42.22° | **0.000** | 22.77° / **51.22°** | torso frontal; both arms folded inside the hoodie (the V3 symptom) |
| **0.25** | 17.15° / 35.22° | 18.64° / 34.83° | **0.307** | 20.72° / 35.66° | slight turn; arms still largely buried |
| **0.50** | 15.32° / 25.30° | 15.01° / 27.44° | **0.615** | 18.85° / **25.94°** | torso clearly turned; hands emerging from the torso |
| **0.75** | 13.50° / 31.82° | 11.38° / 20.05° | **0.923** | **17.39°** / 32.49° | torso follows; both hands clearly out of the torso |
| **0.813** | **13.04°** / 33.66° | 10.47° / 18.19° | **1.000** | 17.46° / 34.22° | as 0.75; arm/body relationship matches the source |
| **1.00** | 16.35° / 39.12° | **7.75°** / **12.67°** | **1.230** | 19.97° / 39.39° | torso over-rotates past the human (yaw err +11.68°) |
| 1.25 | 16.36° / 39.12° | 7.75° / 12.67° | 1.231 | 19.95° / 39.39° | identical to 1.00 — see §2c |

### 2b. THE 1.40× GAIN — measured, and explained

The brief was right to insist on measuring this. The configured scalar is **not** the gain, and the gain
is **not a constant of the mechanism**.

Backing the common damped signal out of each bone independently (dividing each bone's measured twist by
its own Kalidokit dampener) gives, at scale 1.0 on f419:

| bone | dampener | measured local yaw | ⇒ implied signal |
|---|---|---|---|
| Hips | 0.70 | 27.342° | **39.060°** |
| Spine | 0.45 | 22.879° | **50.841°** |
| Chest | 0.25 | 12.710° | **50.841°** |

`Spine` and `Chest` back out to the *same* number in every one of the 35 cells, confirming they share
one signal. And those two numbers are exactly the source's own yaws: source hip yaw **−39.06°**, source
shoulder yaw **−50.84°**. So:

```
avatarShoulderYaw = clamp01(scale) · [ 0.70·DampYaw(hipYaw) + 0.45·DampYaw(shYaw) + 0.25·DampYaw(shYaw) ]
                  = clamp01(scale) · [ 0.70·DampYaw(hipYaw) + 0.70·DampYaw(shYaw) ]
```

Check: 0.70·39.060 + 0.70·50.841 = 62.93; measured bone-twist sum **62.931**, measured shoulder-line yaw
**62.53**.

**This was verified by prediction, not by fitting.** The model says unity gain needs
scale = 50.84 / 62.93 = 0.813. That scale was then run: measured gain **1.000**, shoulder yaw error
**0.01°**.

So the 1.40× the earlier audit found is real, and this is what it is: **it is not one signal spread over
three bones (0.70 + 0.45 + 0.25 = 1.40); it is 0.70 applied to *each of two absolute yaw signals* — hips
and shoulders — which are then summed by the bone chain.** The 1.40× appears exactly when the body turns
rigidly so hip yaw = shoulder yaw. `torsoYawScale = 1.0` therefore over-rotates a rigid turn by 23 %
(measured gain 1.230, because f419's hip and shoulder yaws are 39° and 51° rather than equal).

Three consequences, all measured:

| frame | source hip / shoulder yaw | gain at scale 1.0 | why |
|---|---|---|---|
| f419 | −39.06° / −50.84° | **+1.230** | near-rigid turn — the two signals add |
| f237 | **+22.39°** / **−26.86°** | **+0.116** | opposite signs — the two signals cancel |
| f339 | **−42.22°** / **+9.92°** | **−2.944** | 52° of twist — the hips signal dominates and **inverts the result** |
| f333 | +7.66° / +7.21° | **0.000** | both below the 8° dead-zone floor — deleted entirely |

**No scalar can fix a gain that ranges from −2.944 to +1.230 across four frames.** f339's avatar torso
turns the *wrong way* and nearly 3× too far at scale 1.0, and it degrades monotonically with scale
(13.60° → 39.39° of body-relative arm error). f333 is unreachable at any scale: 7.21° of shoulder yaw
sits under `yawDeadzoneLoDeg = 8`, so it is zeroed at every scale, exactly as designed.

### 2c. `scale > 1.0` is impossible

`SetTorsoYawScale` is `torsoYawScale = Mathf.Clamp01(scale)`. Scales 1.0 and 1.25 produce byte-identical
per-bone decompositions (hips 27.342 / spine 22.879 / chest 12.710 in both). The brief's requested range
is therefore the entire achievable range, and the 0.813 unity point is reachable while any compensation
above 1.0 is not.

### 2d. The measurement that overturns §2a — the real clip, not held frames

Every number above comes from a frame **held static for 5 s**, which lets the rate limiter and the
0.15 s low-pass fully converge. A dance does not. Replaying all **376** recorded frames of
`annotated_p0.mp4` in temporal order at the clip's own 30 fps, through the **real shipping `DampYaw`**
driven by reflection, and applying the §2b composition (verified to 0.01°):

| scale | mean \|shoulder yaw err\| | p50 | p90 | p95 | max | frames worse than scale 0 | mean gain, \|shYaw\| > 22° |
|---|---|---|---|---|---|---|---|
| 0.000 | 7.40° | 5.49° | 17.79° | 22.81° | **50.84°** | 0 / 376 | 0.000 |
| 0.250 | 7.34° | 5.49° | 17.66° | 21.88° | **50.84°** | 25 / 376 (6.6 %) | 0.035 |
| 0.500 | 7.29° | 5.49° | 17.33° | 21.51° | **50.84°** | 27 / 376 (7.2 %) | 0.069 |
| 0.750 | 7.25° | 5.55° | 17.00° | 20.60° | **50.84°** | 27 / 376 (7.2 %) | 0.104 |
| 0.813 | 7.24° | 5.49° | 17.00° | 20.60° | **50.84°** | 27 / 376 (7.2 %) | 0.112 |
| 1.000 | **7.21°** | 5.44° | **16.84°** | **20.53°** | **50.84°** | 27 / 376 (7.2 %) | **0.138** |

**On real motion the mechanism tracks 13.8 % of the yaw, and the worst frame is not improved at all.**
Mean error falls by 0.19° (2.6 %) for a full-scale change; 7.2 % of frames are made worse.

This is not a contradiction of §2a — it is the difference between the conditioner's steady-state and its
dynamic response, and it is independently corroborated by the bench in §5.4/§5.5: a ±45° reversal with a
1 s period is tracked at 0.36 of amplitude, and at 0.5 s period at 0.15. The clip turns faster than that.
Only **5.3 %** of the clip's frames exceed the 22° dead-zone knee at all.

### 2e. End-to-end verification of the shipped value

The edit was verified from a **cold start** — editor out of Play, scene reloaded from disk, Play
re-entered — rather than by trusting the runtime reflection used for the sweep:

```
serialized kalidokitTorsoYawScale = 0.75      (read from the loaded scene)
driver     torsoYawScale          = 0.75      (read from the live driver, pose streaming)
```

Holding f419 under that shipping configuration:

| measure | baseline (scale 0) | shipped (scale 0.75) |
|---|---|---|
| source shoulder yaw | −50.84° | −50.84° |
| **avatar shoulder yaw** | **−0.00°** | **−47.20°** |
| shoulder yaw error | **−50.84°** | **−3.64°** |
| hip yaw error | −39.06° | −18.55° |
| arm world direction error, L / R | 0.0000° / 0.0000° | **0.0000° / 0.0000°** |
| worst body-relative arm error | **51.22°** | **7.16°** |
| wrist radius from the spine axis, L / R | 0.1597 / 0.1531 m | **0.2430 / 0.3550 m** |
| …as a fraction of where it belongs (0.2631 / 0.4373 m) | **0.61 / 0.35** | **0.92 / 0.81** |

The last row is the brief's visual acceptance condition expressed numerically: **the hands move out of
the torso**, from 61 %/35 % of their proper distance from the spine to 92 %/81 %.

**One behaviour worth recording.** `SetTorsoYawScale` is only called inside
`if (!IsPoseStale(filtered))`, so until a live pose arrives the driver keeps `torsoYawScale`'s C# field
initialiser of **1f**, not the scene's value — observed directly above (driver reported `1` before the
sender started, `0.75` after). Harmless, because with no pose the torso is not written at all, but it
means the scene value is not in force during the idle period after entering Play.

---

## 3. Best value

Chosen on measured torso tracking, per the brief — not on the renders.

**`kalidokitTorsoYawScale = 0.75`**, because:

* it is within 8 % of **0.813**, the *measured* unity-gain point for a rigid turn (§2b), so slow and
  sustained turns are reproduced nearly exactly (f419 shoulder yaw error **3.94°**, versus 50.84° at
  baseline and +11.68° of over-rotation at 1.0);
* it minimises the stated KPI, mean body-relative arm error, over the five frames (**17.39°**);
* on the 376-frame distribution it is within **0.04°** of the best achievable mean (7.25 vs 7.21), so
  nothing measurable is given up against scale 1.0;
* it cuts the worst-case body-relative arm error from **51.22° to 32.49°**.

Values rejected, with the reason:

| scale | why not |
|---|---|
| 1.00 | over-rotates a rigid turn by 23 % (gain 1.230) and gives the worst f339 (39.39°); only 0.04° better than 0.75 on the clip |
| 0.813 | marginally better mean shoulder yaw (13.04° vs 13.50°) but a worse max (33.66° vs 31.82°), and it is not a round tunable |
| 0.50 | best worst-case (25.94°) but leaves the reported symptom half-uncorrected (f419 arm error 20.29°) |
| 0.25 | barely moves anything (gain 0.307 held, 0.035 on the clip) |

**0.50 is the defensible alternative** if worst-case is later judged more important than the mean; the
whole decision is one number and is reversible.

---

## 4. Arm regression — ARM V2 while the torso rotates

### 4a. The decisive test: parent division, isolated

With the source frame **held static** and only `torsoYawScale` changed, the torso rotates by up to
**62.9°** while the arms' input never changes. Any change in the arm's world direction would be parent
contamination.

| scale | torso bone-twist sum | source → skinned arm direction error, all 5 frames × 2 arms |
|---|---|---|
| 0.00 | 0.000° | **0.0000°** |
| 0.25 | 15.73° | **0.0000°** |
| 0.50 | 31.26° | **0.0000°** |
| 0.75 | 46.91° | **0.0000°** (one 0.0198° reading, the V3 noise floor) |
| 0.813 | 51.16° | **0.0000°** |
| 1.00 | 62.93° | **0.0000°** |
| 1.25 | 62.93° | **0.0000°** |

**The arm solver's parent division is exact.** `ApplyAimArm` computes
`Quaternion.Inverse(parentRig) * targetUpper`, so the arm reaches its world target whatever the chest
does — and it measurably does, across 70 arm measurements spanning a 62.9° torso rotation. Elbow angles
also reproduce to ±0.0000° at every scale. **No regression.**

### 4b. Parent in motion — a bounded transient, pre-existing

`ApplyBone(hips, …)` runs *after* the arms are written in the same `Apply()` call, so within one frame
the arm's parent division uses the previous frame's hips rotation. Measured by freezing the source and
letting `Recalibrate()` force the torso to slew:

| condition | arm world error |
|---|---|
| parent settled (torso span 0.04°) | 1.95° → **0.034°**, halving each applied frame |
| parent unwinding 31.4° | peak **22.57°**, halving each applied frame |

The error is proportional to parent angular velocity and decays by half per applied frame — the
pre-existing `lerpAmount = 0.5` slerp that V2 already identified for source motion, here shown to apply
to parent motion too. At production's 40 Hz it falls below 0.1° in ~8 applied frames (**~200 ms**). It is
not a systematic inheritance error, and §4a shows it vanishes entirely once the parent stops.

### 4c. A dynamic test I ran, and why its numbers are void

The four motion cases were first run by replaying the wire and sampling the avatar as fast as the editor
round-trip allowed. Those numbers (arm world error mean 24–52°, max 155°) are **a harness artifact, not a
result**, and are discarded. Two things prove it:

1. **The error anticorrelated with torso motion.** Case 1 (torso static, 7.77° of avatar yaw span) had
   the *worst* error, 51.97° mean; case 2 (torso rotating 46.75°, arms static) had the *best*, 24.40°
   mean. Arm drag would give the opposite ordering.
2. **The sampler starves the main loop.** `frameCount` deltas between consecutive samples are
   `[1,1,1,1,1,1,1,1,1]` against 0.83–0.89 s of wall clock — the editor advances **exactly one frame per
   measurement call**. With the wire at 30 Hz the provider advanced ~26 source frames per applied frame,
   so the comparison was against a source jumping 26 frames at a time. It measured temporal lag at 34×
   its production duration.

§4a is the valid test for parent inheritance because it removes time from the question entirely.

### 4d. The dynamic test done properly — in motion, on the live OAK and on video

§4c's numbers were void because the sampler advanced the editor one frame per measurement call. The
replacement samples from **inside `EditorApplication.update`**, and records `Time.frameCount` so the
same defect would be visible if it recurred: it reads **1 frame per sample against a 0.0038 s wall dt
(263 fps)**, i.e. the editor is running normally. Source → **actual skinned** bone direction, in the rig
frame the driver itself uses (`Inverse(RigFrame()) * (child.position − bone.position)`):

| input | condition | p50 | p95 | max |
|---|---|---|---|---|
| live OAK-D, scale 0 → 1.0 | torso rotating | **0.057 – 0.171°** | — | **≤0.78°** |
| `video.webm`, scale 0.00 | **torso frozen** | 0.265° | 2.065° | **5.049°** |
| `video.webm`, scale 0.25 | torso moving | 0.317° | 1.710° | 11.633° |
| `video.webm`, scale 0.50 | torso moving | 0.318° | 1.871° | 11.556° |
| `video.webm`, scale 0.75 | torso moving | 0.317° | 1.776° | 11.754° |
| `video.webm`, scale 1.00 | torso moving | **0.329°** | **1.871°** | 11.303° |

**Median and p95 are flat — or better — as the torso rotates.** Only the extreme tail roughly doubles,
from 5.05° with the torso frozen to 11.75° with it moving, and that is §4b's bounded parent-motion
transient rather than systematic inheritance: it is the `lerpAmount = 0.5` slerp catching up, and it
halves every applied frame. The 5.05° floor at scale 0 — where the torso cannot move at all — is the
same slerp responding to fast *source* motion, and bounds how much of the 11.75° can possibly be
attributable to the parent.

On the live OAK path the only readings above 5° were **8 of 20,278 measurements (0.039 %)**, all in the
first four samples after the sampler started, decaying **61.21° → 30.81° → 15.29° → 7.61°** — the
slerp converging from rest, halving each frame, exactly as §4b describes. After those four, nothing.

**No arm regression.**

---

## 5. OAK-D validation

**§5a as first written was wrong on both of its claims, and this section replaces it.** It said the
OAK-D path could not be exercised because `depthai` was missing and because no recorded log carried a
usable torso-yaw signal. Both were checked again and neither holds:

| original claim | what is actually true |
|---|---|
| *"depthai unavailable: ModuleNotFoundError"* | **`depthai 2.32.0.0` is installed** in `python-sidecar~/.venv`. The original check ran against a different interpreter. The device (`14442C10F143D3D200`) enumerates and the shipping sidecar boots and streams at **30 fps**. |
| *"`recv_log.jsonl` has shoulder and hip z = 0.0"* | True of the **hips only**. `sender_log.jsonl` logs `sh = [lm[11][:3], lm[12][:3]]` *after* `build_body_landmarks`, and `--flatten-trunk` **defaults to FALSE** in Milestone-2 ("keep the measured trunk Z so the model can BEND at the waist and TURN"). Shoulder Z is measured stereo depth. **111,119 recorded frames across 11 captures carry the signal.** |

Everything below therefore comes from the real depth path, not from the monocular video estimator.

### 5a. Method, and the one thing that is modelled

The conditioner used offline is a **line-by-line port of `KalidokitControlRigDriver.DampYaw`**, checked
against the shipping C# driven by reflection in the live editor over an adversarial 20-step sequence
(0 → 40° → ±180° → 90° → 0):

```
MAX |python - shipping C#| = 0.000007 deg over 20 steps      -> PORT EXACT
live constants: yawMaxRateDeg=140  yawSmoothTau=0.15  yawDeadzone=8..22
```

Source yaw is Unity's own derivation, ported from `KMath.RollPitchYaw2` + `CalcHipsAndSpine`:
`yaw = normalize(atan2(b.x−a.x, b.z−a.z))/π`, `−2 if >0.5`, `+0.5`, `×π`. Verified against the frontal
case (0.000°) and a constructed 26.57° turn.

What is **measured** rather than modelled: the live-device numbers in §5d and §5e come from the actual
skinned VRM bones via `Vrm10Instance.Humanoid`. What is **modelled**: the §5c replay applies the
verified conditioner and the §2b composition to recorded source yaw; the 111k avatar poses were not
each rendered.

### 5b. The failure mode this found: the torso path has NO confidence gate

`CalcHipsAndSpine` reads `lm[11]`, `lm[12]`, `lm[23]`, `lm[24]` **unconditionally**. P0-1 `LimbGate`
gates arms and legs; nothing gates the trunk. When the sidecar has no detection it emits `[0,0,0]`, and
then:

```
atan2(0, 0) = 0  ->  normalize 0  ->  +0.5  ->  ×π  =  +90.000 deg      (verified numerically)
```

**An absent or dropped subject commands exactly +90° of torso yaw.** At `torsoYawScale = 0` that is
multiplied away, which is why it has never been seen. At 0.75 it is a live command. In the recorded
corpus the sentinel appears in **53,351 frames** (52,800 of them one empty-room capture, plus 205 in
`rollback`, 176 in `f08`, 156 in `p14`, 14 in `armv1b`), with **148 transitions into the degenerate
state while the stream was otherwise healthy**.

**Caught live, with the camera pointed at an empty room** (Play, scale 0.75, sidecar reporting
`measured_body=1/33`):

| quantity | value |
|---|---|
| landmark confidence, `lm[11]` / `lm[12]` | **0.710 / 0.707** — comfortably above the retarget gate |
| source shoulder yaw from the hallucinated depth | **+40.46°** |
| **avatar skinned shoulder-line yaw** | **+23.82°** |
| avatar Hips / Spine / Chest local Y | −2.59° / −13.65° / −7.58° |

RTMW3D is a top-down model: it always returns a plausible-looking person with plausible confidence, so
a confidence threshold alone does not catch this. The trunk yaw is derived from exactly the depth that
is least reliable when nobody is there.

### 5c. The recorded corpus — the failure modes the brief names

11 captures, 111,119 frames, 55,408 valid after excluding degenerate frames. Steps are measured only
between temporally adjacent valid frames, so a detection drop cannot masquerade as a flip.

| symptom | measured |
|---|---|
| **±180° yaw flips** (adjacent-frame wrapped step > 150°) | **2 in 55,408 valid frames** |
| **sudden torso reversal** (sign change while \|yaw\| > 20°) | **32** |
| steps above the 140°/s rate limit | 0.4 – 1.5 % of frames per capture |
| **detection drops while streaming** | **148** |

So the historical ±180° *ambiguity flip* is essentially **absent** from the valid signal — it shows up
as **degeneracy** (§5b) instead. That is a materially different failure from the one ADR-027 was
written against, and the rate limiter does not address it because the sentinel is a *sustained*
wrong value, not a fast one.

Pushing the recorded yaw through the verified conditioner and the §2b composition, on `pipeline_logs_armv1`
(13,517 valid frames, 589 s of a real subject):

| scale | err mean | err p95 | out p95 | out max | effective gain |
|---|---|---|---|---|---|
| 0.00 | 11.10° | 30.23° | 0.00° | 0.00° | 0.000 |
| 0.50 | 8.44° | 19.61° | 11.08° | 85.82° | 0.370 |
| **0.75** | **7.23°** | **14.91°** | **16.63°** | **128.73°** | **0.556** |
| 1.00 | 6.29° | 13.43° | 22.17° | 171.64° | 0.741 |

Tracking genuinely improves — mean error **11.10° → 7.23°**, a 35 % reduction, far better than the
2.6 % the monocular clip suggested in §2d. But the cost is in the last two columns.

### 5d. The cost: rest jitter and over-twist, on the real depth path

Same replay, measuring what the torso does rather than how well it tracks. "Rest jitter" is the stdev
of the avatar's commanded yaw over frames whose **source** yaw is below the 8° dead-zone floor — i.e.
what the torso does while the human is standing still.

| capture | scale | rest jitter | \|twist\| > 45° | longest such episode | out max | output reversals |
|---|---|---|---|---|---|---|
| `armv1` (589 s) | 0.50 | 3.61° | 0.18 % | 1.13 s | 85.8° | 0 |
| `armv1` | **0.75** | **5.42°** | 0.25 % | 1.55 s | 128.7° | 0 |
| `armv1` | 1.00 | 7.23° | 1.12 % | 3.64 s | 171.6° | 0 |
| `armv1b` (245 s) | 0.50 | 5.04° | 0.17 % | 0.36 s | 54.1° | 0 |
| `armv1b` | **0.75** | **7.56°** | 1.66 % | 1.56 s | 81.2° | 0 |
| `p14` (228 s, poor depth) | **0.75** | **11.90°** | 14.13 % | **7.74 s** | 185.5° | 0 |

Three readings, kept apart:

* **No instability.** Output sign reversals are **0 at every scale in every capture** — the rate
  limiter does prevent the sudden-reversal failure. That part of ADR-027 works.
* **Rest jitter is real and scales linearly with the scalar.** At 0.75 the avatar's torso wanders with
  a **5.4–7.6° standard deviation while the subject is still**, and it is exactly **0.000°** at scale 0.
  The dead zone does not remove it, because the composition sums *two* damped signals and the hip yaw
  often clears the dead zone when the shoulder line does not.
* **Over-twist is input-quality dependent.** On the two good captures it is rare (0.25–1.66 % of the
  time). On `p14` it reaches **14 % of the time with a 7.7 s episode**, and that capture's large-yaw
  frames have a mean measured-depth coverage of **12.2/33 against 20.9/33 for its calm frames** —
  the same split appears in `armv1b` (10.2 vs 19.3). **In two of the three captures the large yaw
  excursions coincide with poor depth**, i.e. they are artefacts being faithfully followed. In `armv1`
  the coverage is identical either way (20.9 vs 20.9), so this is a tendency, not a law.

### 5e. Live device — end to end, measured on the skinned bones

Sidecar streaming live to Unity in Play, scale swept in 8 s blocks, sampled from inside
`EditorApplication.update`. The harness defect that voided §4c is checked directly: `Time.frameCount`
advanced **1 per sample against a 0.0038 s wall dt (263 fps)**, so the editor was running normally,
not one frame per round trip.

| scale | avatar shoulder-line yaw, sd | **effective gain** | Hips Y | Spine Y | Chest Y | UpperChest Y |
|---|---|---|---|---|---|---|
| 0.00 | **0.14°** | **−0.000** | 0.00° | −0.00° | −0.00° | **0.00°** |
| 0.25 | 0.82° | 0.201 | −0.66° | −2.30° | −1.28° | **0.00°** |
| 0.50 | 0.92° | 0.380 | −0.84° | −4.60° | −2.55° | **0.00°** |
| 0.75 | 3.01° | 0.522 | −3.71° | −3.52° | −1.96° | **0.00°** |
| 1.00 | 9.92° | **1.194** | −13.23° | −6.18° | −3.43° | **0.00°** |

The **1.4× over-gain is confirmed on the real depth path**: measured gain **1.194 at scale 1.0**,
against V4's held-frame 1.230 — arrived at independently, on a different input, through the live rig.
`UpperChest` is **0.000° at every scale**, reproducing §6's finding that it is never written.

A 15 s live capture with a cooperative static scene gives the other half of the picture: max
frame-to-frame step **2.11°**, **zero** flips, **zero** reversals, **0 %** of steps above the rate
limit. **In good conditions the OAK yaw signal is clean and the conditioner is not stressed.** The
problems in §5b/§5d are conditions problems, not signal-processing problems.

### 5f. What was NOT run

**The brief's guided motion list — slow yaw, fast yaw, left/right turns, repeated turns, ~90°, ~180° —
was not completed with a human subject.** Two attempts were made with the device live. The first
recorded 156 s but the subject could not read prompts printed to a console from 2 m away, so the
motions are absent from the data (89 % of frames under 15° of shoulder yaw, 2 % above 22°); a second,
spoken-prompt attempt was stopped because no speaker was available. No claim in this report rests on
that capture, and the specific question it would answer — *does a deliberate 180° turn produce an
ambiguity flip on this device* — **remains open**. §5c answers it only for the motions those 11
recorded captures happen to contain.

Two operator faults occurred during those attempts and are recorded because they cost the run:
a sampler was left subscribed to `EditorApplication.update` after Play mode exited, throwing once per
editor frame and stalling the editor; and two sidecar processes were started with `--show`, competing
for the camera. The sampler now unregisters itself if `isPlaying` goes false or on 20 consecutive
errors.

### 5g. Controlled scale sweep on `video.webm`, in the live rig

Because the live motion list could not be completed, the sweep was repeated on the one input that can
be replayed exactly: **five passes of `video.webm`, one per scale, 376 packets each over the real UDP
wire**, measured on the skinned bones. Source reproducibility across passes was **0.093° mean /
3.882° max**, which is what licenses the comparison.

| scale | avatar yaw sd | avatar yaw max | gain | err p50 | err p95 | err max | arm L p50 / p95 / max | hand radius |
|---|---|---|---|---|---|---|---|---|
| 0.00 | 0.00° | 0.00° | 0.000 | 4.18° | 18.58° | 25.44° | 0.265 / 2.065 / **5.049°** | 0.6744 m |
| 0.25 | 0.93° | 4.28° | 0.056 | 3.97° | 17.30° | 24.53° | 0.317 / 1.710 / 11.633° | 0.6765 m |
| 0.50 | 1.86° | 8.55° | 0.111 | 3.92° | 16.45° | 24.22° | 0.318 / 1.871 / 11.556° | 0.6774 m |
| **0.75** | 2.78° | 12.84° | **0.167** | 4.01° | 16.00° | 23.91° | 0.317 / 1.776 / 11.754° | 0.6780 m |
| 1.00 | 3.74° | 17.06° | 0.228 | 4.03° | 15.44° | 23.34° | 0.329 / 1.871 / 11.303° | 0.6788 m |

**This clip barely exercises torso yaw**: source \|yaw\| is p50 **4.18°**, p95 18.58°, max **25.44°**,
and only **2.7 % of frames clear the 22° dead-zone knee**. The low gain here is therefore mostly the
dead zone doing its job on an input that has little to track — not evidence that the mechanism is
broken. It is, however, evidence about the product: on footage like this, restoring torso yaw moves
mean error 4.18° → 4.03° and the hand's distance from the spine axis 0.6744 → 0.6788 m. **Visually,
almost nothing changes.**

Note the gain is **0.167 at scale 0.75 here versus 0.522 on the live OAK and 0.923 held**: the same
scalar produces a 5.5× spread of effective gain depending only on how fast the subject moves. That is
§2b's point, measured a third independent way.

## 6. Final verdict

## CONDITIONAL PASS

*the change is safe and the mechanism is stable, but it under-delivers on motion and the OAK-D path
carries an ungated failure mode that `torsoYawScale = 0` was silently masking*

**What passes, now on the real depth path rather than by inference.**

* **No instability.** Output sign reversals: **0 at every scale, in every one of the 11 recorded
  captures**. ±180° ambiguity flips in the valid signal: **2 in 55,408 frames**. Live device, good
  conditions: max frame step **2.11°**, zero flips, zero reversals, 0 % of steps above the rate limit.
  The ADR-027 rate limiter does what it was written to do.
* **No arm regression.** Source → **actual skinned** bone direction, while the torso rotates:

  | input | p50 | p95 | max |
  |---|---|---|---|
  | live OAK-D, all scales | **0.057 – 0.171°** | — | **≤0.78°** |
  | `video.webm`, scale 0 (torso frozen) | 0.265° | 2.065° | 5.049° |
  | `video.webm`, scale 1.0 (torso moving) | **0.329°** | **1.871°** | 11.754° |

  Median and p95 are **flat or better** as the torso rotates. Only the extreme tail doubles
  (5.0° → 11.8°), and that is the bounded parent-motion transient §4b already identified — the
  `lerpAmount = 0.5` slerp catching up, halving every applied frame, not systematic inheritance.
  ARM V2's parent division holds.
* **The 1.4× over-gain is confirmed independently**: measured **1.194** at scale 1.0 on the live OAK
  path, against §2b's held-frame 1.230. `UpperChest` is **0.000° at every scale**, never written.

**What is conditional.**

1. **The torso path has no confidence gate, and restoring the scalar arms it (§5b).** `CalcHipsAndSpine`
   reads `lm[11/12/23/24]` unconditionally; an absent detection yields **exactly +90° of commanded
   yaw**. Caught live on an empty room: landmark confidence 0.71 (passing every existing gate),
   hallucinated depth → 40.46° source yaw → **avatar torso twisted 23.82°**. This is not a regression
   introduced by the scale change — it is a latent defect the change *un-masks*, and it is the single
   highest-impact item found in this task.
2. **Rest jitter (§5d).** At 0.75 the avatar's torso wanders with a **5.4–7.6° standard deviation while
   the subject is standing still**, against exactly **0.000°** at scale 0. It scales linearly with the
   scalar, and the dead zone does not remove it because the hip and shoulder signals are summed.
3. **It under-delivers on motion.** The same scalar yields an effective gain of **0.167** on
   `video.webm`, **0.522** on the live OAK, and **0.923** held — a 5.5× spread driven only by how fast
   the subject moves. On `video.webm` the whole change moves mean error 4.18° → 4.03° and the hand's
   distance from the spine axis by 4 mm. §2b's conclusion stands and is now measured three ways:
   **no single scalar can be correct**, because the gain depends on the hip/shoulder ratio and on
   motion bandwidth.
4. **The brief's guided OAK-D motion list was not completed (§5f).** Whether a deliberate 180° turn
   produces an ambiguity flip on this device is still unanswered.

**FAIL was rejected** because nothing regressed: no instability, no reversals, no arm degradation in
p50 or p95. **PASS was rejected** because the brief's live motion list is incomplete, and because
shipping 0.75 with an ungated trunk means an empty room or a dropped detection twists the avatar.

### Best value

On measured torso tracking, and holding to the brief's instruction to choose on measurement:

* **`0.75` remains defensible if — and only if — the confidence gate in §5b is added first.** It is
  within 8 % of the measured unity-gain point (0.813) for a sustained turn, and gives the best
  measured error on the OAK corpus (mean 11.10° → 7.23°).
* **`0.50` is the better value if it ships without a gate.** It roughly **halves both costs** — rest
  jitter 5.42° → 3.61° and worst-case output 128.7° → 85.8° on `armv1` — while giving up little
  tracking (mean error 7.23° → 8.44°, and on `video.webm` p95 16.00° → 16.45°).

The change is one number and is reversible either way, so this is a low-stakes decision that should
follow the gate decision rather than precede it.

### Recommended next steps — not implemented

Deliberately not implemented: the brief scoped this task to **one** controlled change plus validation,
and that change (`0` → `0.75`) was already made. In priority order:

1. **Gate the trunk on landmark confidence and plausibility** — reject the yaw when the shoulder or hip
   line is degenerate (near-zero span), and hold the last good value instead. Small, local, and it
   removes the §5b failure entirely. A counterfactual replay with such a gate is already in
   `oak_yaw_v4.py` (`gate_degenerate=True`) and costs nothing in tracking accuracy.
2. **Fix the composition before touching any gain** (unchanged from the original §6): drive the trunk
   from *relative* twist on Spine/Chest and *absolute* hip yaw on Hips, so the gain is 1.0 for every
   hip/shoulder ratio.
3. **Re-qualify the dead zone and rate limit** against §5c/§5d now that real OAK statistics exist.
4. **Complete the guided motion capture** (§5f) — the harness is built and committed
   (`oak_guided_v4.py`, `oak_guided_v4.ps1`); it needs a subject and a way to signal them.

### Rollback

One number: set `kalidokitTorsoYawScale` back to `0` in `Scenes/Bootstrap.unity`, or at runtime in the
inspector — it is pushed to the driver every frame and is live-tunable.

## 7. Honest limits

1. **The brief's guided OAK-D motion list was not completed** (§5f). Slow/fast yaw, left-right turns,
   repeated turns, ~90° and ~180° with a live subject remain unrun, so the ±180° ambiguity question is
   answered only for whatever motions the 11 recorded captures happen to contain.
2. **§5c/§5d are a replay, not 111k rendered frames.** The conditioner is the shipping method (port
   verified to 6.7e-6°) and the composition was verified live to 0.01°, but the avatar poses were
   computed from that model rather than measured per frame. §5e and §5g *are* live rig measurements.
3. **§2a's held frames measure steady state, not dance response**, and overstate the mechanism by ~9×
   in gain. §5g and §2d are the numbers to trust for motion. Both are reported.
4. **`video.webm` is a weak torso test**: source |yaw| p50 4.18°, max 25.44°, only 2.7 % of frames above
   the 22° dead-zone knee. Conclusions about *how much* the change helps are specific to that footage.
   V3's `annotated_p0.mp4`, which reached 50.84°, no longer exists in the repo.
5. **The depth-coverage correlation in §5d holds in 2 of 3 captures examined** (`armv1b`, `p14`), and
   is absent in `armv1` (20.9 vs 20.9). It is a tendency, not a law, and is reported as such.
6. **My first dynamic test was invalid and is discarded**, with the evidence in §4c. The replacement
   sampler records `Time.frameCount` so the same defect would be visible; it reads 1 frame per sample
   against a 0.0038 s dt (§5e).
7. **Two operator faults during the live attempts are disclosed in §5f** (an orphaned editor callback
   that stalled Unity; two competing sidecars). Neither contaminated a reported number — the affected
   captures are excluded, not corrected.
8. **Sign convention**: `mirrorSagittal` negates X, flipping the sign of both yaws but no magnitude,
   step size or stability property. Every metric here is sign-symmetric or reported as |·|.
9. Head, hands, proportions, feet, legs, mesh garment deformation and upstream elbow behaviour were
   left alone as instructed, and remain open from V3.


## Artefacts

Under `python-sidecar~/oak_v4_evidence/` (this task):

`oak_yaw_analysis.txt` (11 recorded OAK captures, degeneracy-classified) · `oak_safety.txt` (rest
jitter, over-twist, reversals) · `live_analysis.txt` + `live_trace.jsonl` (live device, scale blocks) ·
`video_sweep.txt` + `video_trace.jsonl` (5-pass controlled sweep, 49,318 samples) ·
`guided2/`, `smoke_noperson/` (raw sidecar logs).

Harness, under `python-sidecar~/`: `oak_yaw_v4.py` (Unity's yaw derivation + verified `DampYaw` port) ·
`oak_safety_v4.py` · `analyze_live_v4.py` · `analyze_video_sweep_v4.py` · `oak_guided_v4.py` /
`oak_guided_v4.ps1` (guided motion capture, built and unused — see §5f).

Under `python-sidecar~/arm_v3_evidence/` (the original V4 sweep): `mesh_trace.jsonl` (all 35 sweep cells
plus V3), `sweep_f419_visual.png`, `yaw{0,0p25,0p5,0p75,0p813,1,1p25}_f*.png`, `slew_idx359_scale{0,1}*`,
`dyn_arm_scale1*` (§4c, void — kept for the audit trail), `sweep_yaw.py`, `dyn_arm.py`, `slew_test.py`,
`dyn_light.cs.txt`, `dampyaw_bench.cs.txt`, `dist.cs.txt`, `measure.cs.txt`, `ubridge.py`.
