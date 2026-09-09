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

**OAK-D live validation was NOT EXECUTED** — no OAK-D device and no `depthai` module are present in this
environment (§5). A bench test of the shipping conditioner against the documented ±180° failure was run
instead, and it passes: flips shorter than **250 ms are rejected entirely**.

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

---

## 5. OAK-D validation

### 5a. Live test — NOT EXECUTED

No OAK-D is attached and the sidecar venv has no `depthai`:

```
depthai unavailable: ModuleNotFoundError No module named 'depthai'
enumerated cameras: Logi C270 HD WebCam (OK), Iriun Webcam (Error), EPSON L6270 (printer)
```

The brief's list — slow yaw, fast yaw, left/right turns, repeated turns, ~90°, ~180° — needs the device
and a subject in front of it. **None of it was run, and no claim is made about it.** This is the reason
the verdict below is CONDITIONAL rather than PASS.

No recorded log in the repo carries a usable OAK torso-yaw signal either, so a replay against real depth
noise was also impossible: `recv_log.jsonl` has shoulder and hip **z = 0.0** (and torso facing is derived
from exactly that depth separation), and `model_log.jsonl`'s `hipsY` is `hips.eulerAngles.y` of a
*control* bone — representation-ambiguous by construction, from a capture whose `torsoYawScale` is
unknown. Its 0/180 bimodality is **not** evidence of yaw flips and is not cited as such.

### 5b. What was run instead — the shipping conditioner on the bench

`DampYaw` was driven **by reflection, so it is the shipping code and not a reimplementation**, over
adversarial sequences whose amplitudes come from the figures ADR-027's own source comment cites as
pipeline-log-measured: *"rest yaw ~-10 deg, excursions to -119 deg, and ±180 deg AMBIGUITY FLIPS"*.

Shipping constants read back live: `maxRate = 140°/s`, `tau = 0.15 s`, `deadzone = 8..22°`; at 40 Hz that
is **3.500°/frame** maximum.

**±180° ambiguity flip on a −10° rest baseline** — the historical failure:

| flip duration | frames | peak output | leaked past the dead zone? |
|---|---|---|---|
| 25–200 ms | 1–8 | **−0.55°** | **no** |
| 300 ms | 12 | 16.69° | yes |
| 500 ms | 20 | 45.84° | yes |
| 750 ms | 30 | 80.55° | yes |
| 2000 ms | 80 | 179.73° | yes |

**Flips shorter than ~250 ms are rejected completely.** Anything sustained beyond ~300 ms is followed —
which is correct behaviour for a genuine turn and is the limit of what a rate limiter can distinguish.

**Sudden reversal −119° → +119°:** worst single-frame step **8.175°** (no snap), 1700 ms to cross to
+100°. No instantaneous torso reversal is possible.

**Jitter:** zero-mean noise of ±5° and ±10° at rest produces **0.000° output stdev** — the dead zone
removes it entirely; ±20° → 0.047°; ±40° → 1.017°. Mid-turn (45° baseline) the same noise passes at
~20 % of amplitude (±20° → 3.886° stdev, 8.925° peak).

**Genuine turns all reach their target exactly** (30/45/90/180° → 0.00° steady-state error), with lag
450 ms (30° at ≥180°/s) to 1275 ms (180°).

**Repeated left/right turns** — the brief's explicit case, and the finding that matters most:

| amplitude | period | tracked fraction |
|---|---|---|
| ±45° | 4 s | **1.00** |
| ±45° | 2 s | 0.96 |
| ±45° | 1 s | **0.36** |
| ±45° | 0.5 s | **0.15** |
| ±90° | 2 s | 0.62 |
| ±90° | 1 s | 0.24 |
| ±90° | 0.5 s | **0.07** |

At fast periods the output also becomes asymmetric (±45° at 1 s settles into −1.48…+31.19° rather than
±31°), i.e. the rate limiter cannot keep up and the tracked range drifts to one side. **This is the same
bandwidth limit §2d measures on the real clip**, arrived at independently.

So: **no ±180° instability, no jitter, no sudden reversal, no arm drag — and no bandwidth either.** The
conditioner is stable to the point of being over-damped for dance-speed motion.

---

## 6. Final verdict

## CONDITIONAL PASS

*works, but OAK-D validation is outstanding and tuning remains*

**What passes.**

* The single controlled change is **safe**: no arm regression (source → skinned arm direction
  **0.0000°** across 70 measurements while the torso rotated to 62.9°), no instability, no ±180°
  leakage below 250 ms, no jitter at rest, no sudden reversal.
* It **fixes the V3 symptom on slow or sustained turns**. Held f419, at the shipped 0.75 and verified
  from a cold start: body-relative arm error **51.22° → 7.16°**, shoulder yaw error
  **−50.84° → −3.64°**. At the measured unity-gain scale 0.813 the same frame reaches **5.38°**, which
  *is* the control frame's own residual (5.43°), with shoulder yaw error **0.01°**. Visually (`sweep_f419_visual.png`) the torso turns with
  the human and **both hands move out of the torso**, with the arm/body relationship matching the green
  skeleton — the brief's stated acceptance condition, met on that frame.
* Hip yaw tracking improves monotonically with scale (mean error 22.27° → 7.75°).

**What is conditional.**

* **OAK-D live validation was not executed** (§5a) — no device, no `depthai`, no subject. The brief's
  PASS wording explicitly requires it, so PASS is unavailable on evidence.
* **On real motion the change achieves almost nothing**: mean shoulder yaw error 7.40° → 7.21° over 376
  frames, maximum unchanged at 50.84°, 7.2 % of frames worse, effective gain 0.138 (§2d).
* **The existing implementation is insufficient, and the sweep demonstrates it** — so the brief's
  condition for touching the algorithm is now satisfied. Two independent defects, neither fixable by any
  scalar:
  1. **Composition.** Hips and shoulder yaw are two *absolute* signals each given 0.70 gain and then
     summed by the bone chain, so the gain depends on the pose's hip/shoulder ratio: measured
     **−2.944 to +1.230** across four frames, including a sign inversion on the 52°-twist frame.
     `UpperChest` is never written at all, so the trunk cannot represent shoulder-vs-hip twist.
  2. **Bandwidth.** The rate limit + 0.15 s low-pass + 8–22° dead zone track a 1 s-period ±45° reversal
     at 0.36 of amplitude and the real clip at 0.138, and delete every shoulder yaw below 8°
     (95 % of the clip's frames never exceed the 22° knee).

FAIL was rejected because nothing regressed and no instability was introduced — the mechanism is
over-damped, not unstable. PASS was rejected because the torso does not follow the human on real motion
and the OAK-D requirement is unmet.

### Recommended next step — not implemented

The next task may touch the algorithm, on this evidence. In priority order, each independently testable
against the harness already built here:

1. **Fix the composition before touching any gain.** Drive the trunk from the *relative* twist
   (shoulder yaw minus hip yaw) on Spine/Chest/UpperChest and the *absolute* hip yaw on Hips, so the
   shoulder line lands on the source's shoulder yaw by construction and the gain is 1.0 for every
   hip/shoulder ratio. That alone should remove the f339 sign inversion and the f237 cancellation.
2. **Then re-tune the conditioner bandwidth** against the §5b table, treating the dead zone and rate
   limit as the two knobs — but only once an OAK-D is available, because those constants exist to reject
   OAK depth noise and this report could not measure that noise.
3. **Re-run the §2d clip distribution** as the acceptance gate rather than held frames; held frames
   flatter the mechanism by ~9× and would have led to the wrong conclusion here.

### Rollback

One number: set `kalidokitTorsoYawScale` back to `0` in `Scenes/Bootstrap.unity`, or at runtime in the
inspector — it is pushed to the driver every frame and is live-tunable.

---

## 7. Honest limits

1. **OAK-D untested** (§5a). Every yaw figure here comes from a monocular RTMW3D estimate over
   `annotated_p0.mp4`, so it characterises *(that estimator + the shipping conditioner)*, not the OAK-D
   depth path.
2. **§2a's held frames measure steady state, not dance response**, and overstate the mechanism by ~9×
   in gain. §2d is the number to trust for production behaviour. Both are reported rather than only the
   flattering one.
3. **§2d applies a verified model, not a per-frame live capture.** `DampYaw` is the shipping method
   driven by reflection over the real 376-frame sequence, and the composition it feeds was verified
   against the live rig to **0.01°** at scale 0.813 — but the 376 avatar poses were not each rendered and
   measured in Unity. It substitutes measured source hip/shoulder yaw for `pose.Hips.y`/`pose.Spine.y`,
   which was validated on five frames (39.060 vs 39.06; 50.841 vs 50.84) and not beyond.
4. **My first dynamic test was invalid and is discarded**, with the evidence for why in §4c. Its numbers
   appear nowhere in the conclusions.
5. **Five held frames are not a distribution.** They were chosen in V3 to span 0–51° of yaw error, which
   makes the §2b gain analysis readable but makes §2a's means unrepresentative; §2d exists for that
   reason.
6. **The parent-motion transient's magnitude is real but its duration in §4b is inflated ~34×** by the
   sampler advancing one editor frame per call (`frameCount` deltas all 1 against ~0.86 s wall clock).
7. The MCP servers were `CONNECTION_CLOSED`; the editor bridge on `127.0.0.1:6401` was driven directly,
   as in V3.
8. Head, hands, proportions, feet, legs, mesh garment deformation and upstream elbow behaviour were left
   alone as instructed, and remain open from V3.

---

## Artefacts

Under `python-sidecar~/arm_v3_evidence/`: `mesh_trace.jsonl` (all 35 sweep cells plus V3),
`sweep_f419_visual.png` (source beside scale 0 / 0.5 / 0.813 / 1.0), `yaw{0,0p25,0p5,0p75,0p813,1,1p25}_f*.png`,
`slew_idx359_scale{0,1}*` (§4b), `dyn_arm_scale1*` (§4c, void — kept for the audit trail),
`sweep_yaw.py`, `dyn_arm.py`, `slew_test.py`, `dyn_light.cs.txt`, `dampyaw_bench.cs.txt`, `dist.cs.txt`,
`measure.cs.txt`, `ubridge.py`.
