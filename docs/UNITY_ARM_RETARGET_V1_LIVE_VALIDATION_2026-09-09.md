# Unity Arm Retargeting V1 — LIVE VALIDATION — 2026-09-09

Closes the outstanding item in `docs/UNITY_ARM_RETARGET_V1_2026-09-08.md` §12/§16.1: the on-camera
sign-off. Executes the §12 procedure with a real subject in front of the OAK-D, plus the live A/B
required by the brief (`kalidokitAimArms` on vs off).

**No code was changed for this task.** No C# file, no Python file, no scene asset, no constant, no
threshold. `git status` on the nested Unity repo shows only ` M python-sidecar~`; `Scenes/Bootstrap.unity`
is untouched. The only additions are three offline analysis scripts under `python-sidecar~/`
(`arm_v1_live_capture.py`, `analyze_arm_v1_live.py`, `forensics_arm_v1.py`). The runtime flags used
(`kalidokitDirectionTrace`, `pipelineLogDir`) were set on the in-memory scene instance only.

---

## 1. Environment

Raw analyser output is archived alongside the captures as
`python-sidecar~/arm_v1_evidence/analysis_full.txt` and `forensics_full.txt`.

| item | value |
|---|---|
| Unity | **6000.3.9f1** |
| Avatar | `Anime+Boy+V-tuber.vrm` (UniVRM, VRM 1.0 normalized control rig) |
| Unity repo commit | `6bd2dab` "Add retargeting scripts and utilities for humanoid bone manipulation" |
| Python sidecar commit | `c35e5e0` "Implement F-08 surface-aware depth sampling and diagnostics" |
| Camera | OAK-D-PRO-W (OV9782), RGB-aligned stereo depth, 640×400 |
| Model | RTMW3D-x ONNX, ONNX Runtime DirectML |
| Sidecar throughput | **23.02 fps** (session A, 549 s) / **21.67 fps** (session B, 194 s) |
| Unity `LateUpdate` rate | **196.8 fps** (A) / **247.7 fps** (B) |
| sidecar send → Unity recv | p50 **0.6 ms**, p95 1.6 ms, max 29.1 ms |
| sidecar send → avatar apply | p50 **24.0 ms**, p95 **45.0 ms** |
| `poseInterpolationDelayMs` | **40** |
| `limbConfidenceThreshold` | 0.3 |
| `kalidokitAimArms` | **true** (NEW) / **false** (OLD) — toggled live, mid-Play, nothing else changed |
| `kalidokitDirectionTrace` | **true**, `…EveryFrames` = **60** (briefed value); **6** for the dense pass |
| `useIkDriver` | false | 
| `wristRotationWeight` | 0 |
| `kalidokitBodyMirror` | false; `poseFlipY` the only active flip |
| P1-4 `--recovery` | **OFF** (shipping default; rejected 2026-09-08) |
| F-08 surface-aware depth | ON (shipping default) |
| Unity console errors during capture | **0** |

Rest basis, logged once per avatar load, both sessions identical:

```
[ARM-V1] rest basis measured:
  left (valid=True upper=( 1.00,-0.05,0.00) lower=( 1.00,-0.01,-0.07) hinge=( 0.05, 0.97,-0.24))
  right(valid=True upper=(-1.00,-0.05,0.00) lower=(-1.00,-0.01,-0.07) hinge=( 0.05,-0.97, 0.24))
```

Both arms valid, hinges exactly opposed — §12 step 3 satisfied.

### 1a. Method, and the two error measures kept apart

The brief's warning is load-bearing: `upperErrDeg`/`forearmErrDeg` compare the solver's **own intent**
to the achieved bone, so they cannot tell you whether the upstream landmark was right. They are
reported in §4 only, never as correctness.

The A/B metric is a **different** number that was already being logged and needs no new code:

```
[RETARGET-TRACE] frame=N bone=leftUpperArm want=(…) got=(…) errDeg=E
        want = MirrorX(KalidokitToRig(landmarks[to] - landmarks[from]))   ← the RAW landmarks,
                                                                            i.e. exactly what the
                                                                            green PoseDebugSkeleton draws
        got  = child.position - bone.position                             ← the avatar's real bone
```

`errDeg` is therefore literally *"how far the VRM bone is from the green skeleton"*. That is the
question the brief asks, and it is immune to how good or bad the tracking happened to be in a given
take. Every number in §2, §3 and §5 is built on it.

Captures: `python-sidecar~/pipeline_logs_armv1/` (session A) and `…_armv1b/` (session B), each with
`blocks.json` (per-motion epoch windows + which branch was live), `model_log.jsonl` (per render frame),
`sender_log.jsonl`, `recv_log.jsonl`, and `shots/` (timed Game-view PNGs).

> **CORRECTION, 2026-09-09** (found by the V2 forensics — see
> [`UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md`](UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md) §1a).
> Everywhere this document calls `model_log.jsonl` fields (`elbowAng`, `boneLen`, `llowF`/`rlowF`) the
> "real VRM humanoid bones", they are in fact **control-rig bones**. `AppBootstrap` resolves its
> `Animator` with `GetComponentInChildren<Animator>()`, and on a control-rig-enabled `Vrm10Instance`
> that Animator's humanoid map returns `Vrm10ControlBone:*`, not the skinned `metarig/.../upper_arm.L`.
> The skinned bones are reachable only through `Vrm10Instance.Humanoid`.
>
> **No conclusion in this report changes.** The A/B metric throughout is `[RETARGET-TRACE] errDeg`,
> which is source-vs-control-rig by construction and is described as such. Only the `model_log`
> labelling was wrong. The measurement that was actually missing — control rig vs *rendered* bone — is
> supplied in the V2 forensics §3, and it is **0.000000°**.

### 1b. Two honest caveats about the capture

**(i) An 88.40 s Editor stall destroyed part of session A, and I caused it.** Calling the MCP
`manage_camera` screenshot action refreshed Unity's AssetDatabase, freezing the Editor from
09:26:34.203 for 88.40 s. The sidecar was unaffected throughout (largest send gap in the same window:
**0.22 s**), so this was purely an Editor-side stall from my instrumentation, not a pipeline fault. It
consumed NEW motions 3, 4, 5 and 84 % of motion 2.

Those four motions were **recaptured for both branches** in session B (`pipeline_logs_armv1b`), with
screenshots taken by an in-Unity `ScreenCapture` timer instead. Session B's largest Unity gap was
**0.91 s** — the branch toggle itself. The analysis supersedes the ruined windows and prints exactly
which ones it replaced. Motions 1, 6, 7, 8, 9 are the original session-A capture for both branches.

**(ii) Tracking quality was not identical between the two takes of motions 4 and 5.** See §3.3. This
does not affect the A/B metric (which measures avatar-vs-skeleton, whatever the skeleton says), but it
does affect what the screenshots of those two motions look like, and it is called out where relevant.

---

## 2. Live test results

Avatar-bone vs raw-landmark angle, degrees. Lower is better. `n` = trace samples per motion
(4 arm bones × ~1 Hz at `traceEveryFrames = 60`).

| Motion | Old mean / p95 / max | New mean / p95 / max | Visual result | Trace result | Verdict |
|---|---|---|---|---|---|
| 1 relaxed arms | 35.0 / 52.3 / 55.3 | **0.2 / 0.7 / 7.1** | old: elbows bent, hands at the belly (cactus). new: matches the skeleton | old asym 22.1°; new 0.2° | **NEW WINS** |
| 2 one arm horizontal | 74.8 / 147.2 / 174.2 | **0.0 / 0.3 / 0.7** | old: raised arm ~straight while landmarks say 24° elbow. new: exact | old elbow diff **143.2°**; new 0.1° | **NEW WINS** |
| 3 both arms horizontal (T) | 14.3 / 36.2 / 53.7 | **0.1 / 0.3 / 1.6** | both takes limited by tracking, but new reproduces it exactly | old asym 38.3°; new 0.2° | **NEW WINS** |
| 4 one arm overhead | 33.1 / 50.7 / 136.6 | **2.0 / 0.7 / 148.7** | old refuses elevation the tracker supplied (86.9°→73.0°) | new max is a **P0 gate hold**, §3.1 | **NEW WINS** |
| 5 both arms overhead | 45.3 / 121.0 / 132.5 | **0.0 / 0.1 / 0.5** | new take: tracker never saw the arms up (§3.3) | new elbow diff **0.0°** | **NEW WINS** |
| 6 fast waving | 26.7 / 54.5 / 115.3 | **0.4 / 1.5 / 3.2** | new tracks the wave; old lags and compresses | 4 forearm pops >45°, §4 | **NEW WINS** |
| 7 arm circles | 23.9 / 49.2 / 75.1 | **0.5 / 1.3 / 9.7** | new follows the full circle | 4 forearm pops >45°, §4 | **NEW WINS** |
| 8 asymmetric poses | 34.4 / 80.0 / 134.7 | **0.2 / 0.7 / 3.6** | old couples the arms; new keeps them independent | old forearm asym max 123.6° | **NEW WINS** |
| 9 dance | 32.2 / 59.4 / 83.6 | **0.4 / 1.1 / 3.3** | new keeps up; old visibly damped | 4 forearm pops >45°, §4 | **NEW WINS** |
| **ALL** | **35.78 / 107.30 / 174.20** (n=2772) | **0.46 / 0.90 / 148.70** (n=2780) | | | **78× lower mean** |

Per bone, all motions:

| bone | OLD mean / p95 / max | NEW mean / p95 / max |
|---|---|---|
| leftUpperArm | 34.15 / 122.10 / 131.10 | **0.55 / 0.60 / 77.40** |
| leftLowerArm | 28.66 / 59.20 / 115.30 | **0.83 / 1.10 / 148.70** |
| rightUpperArm | 33.68 / 53.70 / 118.50 | **0.17 / 0.60 / 7.10** |
| rightLowerArm | 46.62 / 132.80 / 174.20 | **0.31 / 1.20 / 4.80** |

Both NEW maxima come from the single 0.73 s P0 gate hold in motion 4 (§3.1). Excluding it, NEW max is
**9.7°**.

### 2a. Within-run control — the legs

The change touches arms only. If the legs moved as much as the arms between the two passes, the A/B
would be measuring the subject rather than the code:

| | NEW | OLD |
|---|---|---|
| **arm** bones, mean errDeg | **0.46** | **35.78** |
| **leg** bones, mean errDeg | 10.51 | 9.91 |

Legs move **1.06×**; arms move **78×**. The effect is in the code under test, not in the subject.

### 2b. Elbow reproduction — the decisive number

From the same trace line, both elbow angles are reconstructible (180° = straight):

```
landmark elbow = 180 − angle(want_upper, want_lower)      ← what the green skeleton shows
avatar   elbow = 180 − angle(got_upper,  got_lower)       ← what the VRM does
```

| | reproduction error mean | p95 | max | n |
|---|---|---|---|---|
| **NEW** | **0.31°** | **1.03°** | 29.78° | 1390 |
| OLD | 21.98° | 123.77° | 173.60° | 1386 |
| dense pass | 0.24° | 0.84° | 3.31° | 2920 |

And the achieved elbow **range**, over every motion:

| | left elbow | right elbow |
|---|---|---|
| **NEW** | **28.6° … 179.9°** | **15.9° … 179.1°** |
| OLD | 97.9° … 175.1° | 97.7° … 174.7° |

The old branch's elbow **cannot go below ~98°** — the saturated `lowerArm.x` clamp of audit §3 fault 2,
visible in live data. Motion 2 is the clearest single case: the landmarks reported a **24.3°** left
elbow (sharply bent); the old avatar drew **167.6°** (essentially straight), a **143.2° mean** error.
The new branch reproduces the full human range.

---

## 3. Failure evidence

### 3.1 Motion 4, NEW — the 148.7° maximum is P0-1 doing its job, not a retarget fault

Eight of 336 arm samples (2.38 %) exceeded 10°, all on the **left** arm, all inside **0.73 s**
(09:40:17.436 → 09:40:18.164):

```
09:40:18.164 frame=18060 leftLowerArm errDeg=148.7 | bend=32.2 roll= 0.3 src=ElbowPlane | gate hLArm=243
09:40:17.895 frame=18000 leftLowerArm errDeg= 79.8 | bend=63.7 roll=35.4 src=ElbowPlane | gate hLArm=183
09:40:18.164 frame=18060 leftUpperArm errDeg= 77.4 | bend=32.2 roll= 0.3 src=ElbowPlane | gate hLArm=243
09:40:17.665 frame=17940 leftLowerArm errDeg= 77.2 | bend=62.7 roll=71.8 src=ElbowPlane | gate hLArm=123
09:40:17.437 frame=17880 leftLowerArm errDeg= 75.1 | bend=32.0 roll=79.0 src=ElbowPlane | gate hLArm= 63
```

`hLArm` is the LimbGate hold counter and it is **monotonically rising** (63 → 123 → 183 → 243). The
left arm was **HELD** by P0-1 through a low-confidence patch while the landmarks kept moving, so
"avatar ≠ landmark" is the gate deliberately freezing the limb. Motion 4 NEW is the only NEW motion
with a non-trivial arm hold (**6.52 %** of render frames; every other motion 0.00–1.29 %).

Classification: **P0 safety behaving as designed.** Not an arm-retargeting failure. The arm-retargeting
error in that window is the ARM-V1-TRACE `upperErrDeg`, which stayed at 1.6° mean.

### 3.2 The near-straight-elbow forearm pop — the one real remaining defect

31 single-render-frame steps of the forearm's **forward (roll) axis** exceeded 45° across NEW + dense
(100 890 steps → **0.031 %**, but ≈ **1 every 6.9 s** of arm motion). Causal signature:

| | |
|---|---|
| pops involving a LimbGate transition | **0 of 31 (0 %)** |
| pops at elbow bend **< 15°** | **21 of 31 (68 %)** |
| bend at the pops | min **7.0°**, **median 12.3°**, max 55.5° |

`BendSinMin = 0.2` corresponds to a **≈11.5°** bend threshold. The pops sit directly on it — 20 of 31
fall between 7.0° and 13.5°, and `normalSource` at the pops alternates between `Held` (13) and
`ElbowPlane` (18). This is threshold chatter: as the elbow crosses straight, `n = cross(u, f)` reverses
sign and the elbow-plane roll flips ~180°, while the roll **hold** engages and disengages around it.

Worked example — motion D2, *arms held straight out, deliberately still*:

```
09:34:50.886  D2 STRAIGHT ARMS HELD OUT  leftArm   step 67.2°  bend 12.9°  src=ElbowPlane  gate 0 → 0
09:34:43.433  D2 STRAIGHT ARMS HELD OUT  rightArm  step 64.6°  bend 11.4°  src=Held        gate 0 → 0
```

Important scoping: the **bone direction** is not what pops. In the same windows the direction error
stays at 0.0–0.2° and D2's whole-block direction p95 is 0.2°. What flips is the twist about the
forearm axis. With `wristRotationWeight: 0` and no hand landmarks the visible result is the forearm
and hand mesh rotating about their own axis, not the arm swinging.

This is exactly limitation §16.4 of the V1 document, now measured live rather than predicted.

### 3.3 Motions 4 and 5 — the tracker, not the retarget

Elevation of the upper-arm direction (+90° = straight up), from landmarks vs from the avatar:

| motion | branch | LANDMARK mean / p95 / max | AVATAR mean / p95 / max |
|---|---|---|---|
| 4 one arm overhead | OLD | 49.8 / 86.0 / **86.9** | 38.9 / 72.2 / **73.0** |
| 4 one arm overhead | NEW | −10.3 / 57.0 / **67.5** | −8.8 / 57.0 / **67.5** |
| 5 both arms overhead | OLD | −12.7 / 49.6 / **63.2** | −15.9 / 39.9 / **41.5** |
| 5 both arms overhead | NEW | 20.2 / 23.5 / **23.7** | 20.2 / 23.5 / **23.7** |

Read it row by row:

- **In every NEW row, landmark and avatar elevation agree to 0.0–1.5°.** Across all nine motions the
  two columns are identical to ≤0.1° except motion 4's gate hold.
- **In motion 4 OLD the tracker delivered a genuinely overhead arm (86.9°) and the old branch only
  reached 73.0°** — a 13.9° refusal, with the mean short by 10.9°. The old branch fails *even when
  tracking is good*. That is the cleanest proof available that the old failure was retargeting.
- **In motion 5 NEW the tracker never saw the arms go up at all** (max elevation 23.7°, while the
  subject held them straight overhead). The avatar faithfully drew that. The screenshot below shows the
  green skeleton with its arms down and the avatar matching it.

So the two takes of motions 4/5 were not equally well tracked. The A/B metric is unaffected — it asks
whether the avatar follows the skeleton — but a viewer watching motion 5 in the NEW take sees a wrong
pose, and its cause is upstream.

### 3.4 Screenshots

Timed Game-view captures, epoch-stamped and mapped to motion windows. Green = `PoseDebugSkeleton`
(raw landmarks), offset to the right of the avatar.

Selected frames are collected in `python-sidecar~/arm_v1_evidence/`; the full timed sets remain in
`pipeline_logs_armv1/shots/` (243 PNGs, 09:27:06–09:36:04) and `pipeline_logs_armv1b/shots/`
(09:39–09:43), named `t<epoch-ms>.png` so any frame can be mapped back to a motion via `blocks.json`.

| file (`arm_v1_evidence/`) | time | what it shows |
|---|---|---|
| `m3_TPOSE_old.png` | 09:41:36 | skeleton in a clean T; **old avatar draws a clean T** — the tracker was good in this window |
| `m3_TPOSE_new.png` | 09:39:51 | skeleton collapsed (no horizontal arms); **new avatar matches the collapsed skeleton** |
| `m5_OVERHEAD_old.png` | 09:42:24 | skeleton's right arm **straight up**; old avatar's hand only reaches the head — the refusal in §3.3 |
| `m5_OVERHEAD_new.png` | 09:40:39 | skeleton's arms **not up**; new avatar matches — upstream failure |
| `m1_RELAXED_old.png` | 09:30:22 | **cactus arms**: elbows bent, hands at the belly, more bent than the skeleton |
| `m7_CIRCLES_new.png` / `m7_CIRCLES_old.png` | 09:28:42 / 09:32:46 | new avatar's arms track the skeleton through the circle |

No screenshot exists for motion 1 NEW: it fell inside the 88.40 s Editor stall of §1b(i), and motion 1
was not part of the session-B re-run. Its numbers come from the trace, which was likewise interrupted —
motion 1 NEW carries n=220 samples against 244 for OLD.

`m5_OVERHEAD_old.png` and `m5_OVERHEAD_new.png` side by side are the single most informative pair: the
old avatar disagrees with a *correct* skeleton; the new avatar agrees with an *incorrect* one.

---

## 4. Jitter / roll analysis

`traceEveryFrames = 60` samples roll about **once per 0.25–0.4 s**. A "step" between samples that far
apart is not a jitter measurement, so the numbers below come from the **dense pass (≈40 Hz)**. This is
the one place I deviated from the briefed configuration, deliberately and additively: the nine-motion
A/B ran at the briefed 60, and a supplementary 45 s dense capture was appended afterwards purely so
§4 could be answered with real data.

`rollDeg` is an angle on a circle, so every step below is wrapped into (−180°, 180°]. Raw differencing
reports a −179° → +178° move as 357°; 14 such seam artefacts appear in the session-A trace and are
**not** flips.

**Roll continuity vs elbow bend** (dense pass, wrapped steps):

| elbow bend | n | mean | p95 | p99 | max | steps > 90° |
|---|---|---|---|---|---|---|
| **left arm** | | | | | | |
| bent > 40° | 575 | 1.63° | 5.00° | 10.20° | 23.8° | **0** |
| shallow 15–40° | 374 | 4.39° | 13.20° | 18.80° | 87.5° | **0** |
| straight < 15° | 510 | 2.97° | 7.50° | **130.90°** | 176.9° | **6** |
| **right arm** | | | | | | |
| bent > 40° | 674 | 1.39° | 4.30° | 7.30° | 13.1° | **0** |
| shallow 15–40° | 687 | 1.42° | 6.60° | 13.50° | 24.3° | **0** |
| straight < 15° | 98 | 9.73° | 70.70° | **134.10°** | 165.3° | **4** |

- **Maximum roll step:** 176.9° (left), 165.3° (right) — both in the straight-elbow bucket.
- **Sudden roll jumps:** p99 is **10.2° / 7.3°** when the elbow is bent past 40°, and **130.9° / 134.1°**
  when it is straighter than 15°. A ~13× discontinuity, entirely explained by bend.
- **Number of visible flips:** **10** wrapped steps > 90° in 45 s, **all** at bend < 15°, **zero** at
  bend > 15°.
- **Arm-direction error** (the thing a viewer actually sees as a wrong pose): dense pass mean **0.26°**,
  p95 0.90°, max 5.50°.
- **Behaviour through nearly-straight elbows:** the straight-arm roll hold does engage —
  `normalSource = Held` for **86 %** (left) and **65 %** (right) of straight-elbow samples, versus
  **0 %** at any larger bend. It catches most of the degenerate region but not the boundary, which is
  where the 10 flips and the 31 pops of §3.2 live.

**Single-frame avatar pops** (`model_log`, every render frame):

| | forearm steps | > 5° | > 20° | > 45° | max |
|---|---|---|---|---|---|
| NEW, 9 motions | 83 364 | 264 | 63 | **21** | 87.7° |
| OLD, 9 motions | 83 128 | 120 | 3 | **0** | 32.8° |
| dense, 3 motions | 17 526 | 107 | 26 | **10** | 85.4° |

OLD's zero must not be read as "smoother". OLD's elbow is clamped into a 98–175° band and its mean
direction error is 35.78° — it does not pop because it does not follow. Its own bone-length and
smoothness numbers are good for the same reason a frozen limb's are.

**Bone lengths were constant to the printed precision in both branches** — `upArm` spread
**0.00000 m** (0.5400 m throughout), `foreArm` spread **0.00000 m**. No squash, no stretch, in either
branch.

**Arm gate holds:** NEW 0.95 % of render frames, OLD 0.12 %. The NEW figure is dominated by motion 4's
6.52 % (§3.1); seven of nine NEW motions held 0.00 %.

---

## 5. A/B conclusion — which SR.mp4 failures are fixed

| SR.mp4 symptom (audit §14) | live measurement | status |
|---|---|---|
| **cactus arms** — standing, arms hanging, avatar's elbows bent, hands at the belly | old motion 1: elbow reproduction error 5.7° (L) / **13.9° (R)**, avatar 154°/136° vs landmarks 148°/122°. New: **0.1° / 0.4°** | **FIXED in the retarget.** Residual bend now comes from the landmarks, not the rig — see §6 |
| **one-arm-only** — subject raises both, avatar raises one | old L/R asymmetry **22.6° mean, p95 80.0°, max 101.4°**. New: **≤0.2° mean in every motion** except motion 4, whose 3.1° is the P0 gate hold of §3.1; new overall 0.5° mean | **FIXED** |
| **overhead reaches not reachable** | old motion 4: tracker gave 86.9°, avatar reached 73.0°. New: avatar reaches whatever the tracker gives, to ≤0.1° | **FIXED in the retarget** (§3.3) |
| **T-pose reads wrong** | old motion 3 asymmetry 38.3° mean; new 0.2°, errDeg 0.1 / 0.3 / 1.6 | **FIXED** |
| **backward-arm inversion** (162.8° in the offline harness) | new max direction error over all motions excluding the gate hold: **9.7°**. No sample above 10° outside that 0.73 s window | **FIXED** |
| **asymmetry** | see one-arm-only | **FIXED** |
| **fast waving / dance** | fast waving 26.7° → **0.4°**; dance 32.2° → **0.4°**; arm circles 23.9° → **0.5°** | **FIXED** |
| **hands / forearms stiff** | `wristRotationWeight: 0`, hand landmarks 17–22 never populated | **REMAINS** — out of scope, V1 §16.2 |
| **popping when the elbow is nearly straight** | 31 forearm roll pops > 45°, 68 % at bend < 15°, median bend 12.3° | **REMAINS, and is now quantified** — §3.2, §4 |
| overhead poses still look wrong on camera | motion 5 NEW: tracker's max arm elevation **23.7°** while the subject held them vertical | **REMAINS, but upstream** — §3.3, §6 |

---

## 6. Root-cause classification

| remaining failure | classification | evidence |
|---|---|---|
| Forearm twist pops when the elbow passes near-straight | **arm retargeting** | 31 pops, 68 % at bend < 15°, median bend **12.3°** against a `BendSinMin` threshold of ≈11.5°; 0 % coincide with a gate transition; 10 wrapped roll steps > 90°, all at bend < 15°, zero above it |
| Elbow reported bent (~120°) while the arm hangs straight | **upstream landmark/tracking** | motion 1 landmark elbow 121.7°/124.4°; avatar reproduces to **0.1°/0.4°**. The rig draws what it is given |
| Arms held overhead not seen by the tracker | **upstream landmark/tracking** | motion 5 NEW landmark elevation max **23.7°**; avatar matches to 0.0°. The same tracker reached 86.9° in motion 4 OLD, so it is **intermittent**, not systematic |
| T-pose skeleton collapsing in one take | **upstream landmark/tracking** | motion 3 NEW landmark elbow 116.0° (L) vs OLD 167.3° in the same pose; avatar reproduction 0.2° in both |
| Hands / fingers inert | **hand limitation** | landmarks 17–22 never populated (21 of 33 slots), `wristRotationWeight: 0` |
| Forearm pronation not recovered | **hand limitation** | three points give the bend plane, not the twist — V1 §16.3 |
| Torso does not follow a body turn | **torso interaction** | out of scope; audit §7, `torsoYawScale` |
| Avatar limb proportions ≠ subject's | **avatar rig limitation** | FK by construction; directions exact, elbow/wrist land at the avatar's own bone lengths |

**Nothing in this run classifies as pose-space conversion or as unknown.** Every large error was
attributed to a named cause with a number behind it.

### 6a. Does the VRM rig need replacing?

**No, and the data rules it out.** The rig reproduces the tracked pose to **0.31° mean / 1.03° p95**
elbow error and **≤0.1°** arm-elevation error, with bone lengths constant to 5 decimal places. A
custom rig cannot improve a 0.3° reproduction error, and it would reproduce the same wrong landmarks
just as faithfully — motion 5 is the proof: landmark elbow 56.3°, avatar elbow 56.3°, difference
**0.0°**, while the subject's arms were straight overhead. The rig is not the limitation.

### 6b. The next single highest-impact change

Ranked, and kept in separate subsystems:

1. **Highest impact overall — upstream 2D elbow/wrist localisation.** It now dominates the error
   budget: the retarget contributes 0.46° mean while the landmarks are wrong by tens of degrees
   (a 121° elbow on a straight hanging arm; arms overhead missed entirely for a 15 s window). This is
   F-08 territory — §8 of the F-08 implementation report already established that `depthQuality`
   cannot see this class of fault, because it is 2D-semantic, not depth.
2. **Highest impact within arm retargeting — `BendSinMin`.** The one defect this task can attribute to
   the arm code. The evidence points at a specific constant with a specific direction: pops cluster at
   7–13.5° against an 11.5° threshold, and there are **zero** flips above 15° of bend. Raising it
   should be a separate, controlled change validated the way P1-4 was.

Per the brief, **no code was changed on the basis of these observations.**

---

## 7. Regression

| item | status |
|---|---|
| C# runtime files | **unmodified** — `git status` clean apart from ` M python-sidecar~` |
| `Scenes/Bootstrap.unity` | **unmodified** |
| `KalidokitArmSolver.cs` (old branch) | **unmodified**, and demonstrated reachable live via `kalidokitAimArms = false` |
| Legs | **unchanged** — leg error 9.91° (OLD) vs 10.51° (NEW), 1.06× |
| Torso, head, hands, feet, IK | untouched |
| `flipQuat`, `eulerSigns`, `PoseSpaceConverter` | untouched |
| Python / OAK-D / RTMW3D / network protocol | untouched — sidecar at `c35e5e0` for both branches |
| P1-4 `--recovery` | **OFF** throughout |
| Bone-length constancy | `upArm` and `foreArm` spread **0.00000 m** in both branches |
| Unity console errors during capture | **0** |
| EditMode suite | **not re-run this session.** Repeated `run_tests` calls each forced a domain reload and destabilised the Editor, so I stopped. No C# changed since the 70/70 recorded in `UNITY_ARM_RETARGET_V1_2026-09-08.md` §15, so that result still stands; re-confirm it on the next Editor session |

---

## 8. Final verdict

**CONDITIONAL PASS.**

Arm **retargeting** is fixed, decisively and on camera:

- avatar-vs-skeleton error **35.78° → 0.46°** mean (**78×**), p95 107.30° → 0.90°
- elbow reproduction **21.98° → 0.31°** mean
- L/R asymmetry **22.6° → 0.5°** mean overall, and **≤0.2° in every motion** but the gate-held one
- elbow range **97.9–175.1° → 28.6–179.9°** — the saturated clamp is gone
- the old branch fails **even when tracking is good** (motion 4: 86.9° supplied, 73.0° drawn); the new
  branch matches the skeleton regardless of tracking quality
- legs, the within-run control, moved 1.06× while arms moved 78× — the effect is the code under test
- bone lengths constant, 0 console errors, latency unchanged (p50 24.0 ms camera-send → apply)

It is **not** an unqualified PASS, because the brief's bar is that *"the actual avatar visibly follows
the real subject and the debug skeleton"*. It provably follows the **debug skeleton** — to a tenth of a
degree. It does not always follow the **real subject**, because in this capture the skeleton itself
sometimes did not, and there is one measurable defect still attributable to the arm code:

1. **Forearm twist pops at near-straight elbows** — 31 events > 45°, ≈1 per 6.9 s of arm motion,
   median bend 12.3° against `BendSinMin`'s 11.5° threshold. Arm retargeting. Fixable, characterised,
   deliberately left unchanged.
2. **Upstream landmark failures** — a 121° elbow on a straight hanging arm; arms overhead missed for a
   full 15 s window. Not arm retargeting; the dominant remaining error source; F-08 territory.

Roll back at any time with `kalidokitAimArms = false`; the old branch is byte-for-byte intact and was
exercised live in this run.
