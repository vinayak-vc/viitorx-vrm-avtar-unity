# F-09 — torso yaw quality audit — 2026-09-10

**Nothing was changed.** No production C#, no Python, no scene, no threshold. This is an evidence-only
audit, per TASK 7.

**Verdict: CONDITIONAL PASS** — a genuinely independent predictor exists (`hipLenErr`, AUC **0.897**),
but when replayed as an actual gate it **fails the safety test**: at its own ROC-optimal threshold it
rejects **19.7 % of image-confirmed genuine turns** to catch 75.9 % of bad frames, makes rest jitter
*worse* in 3 of 5 captures, and stretches p95 recovery latency to **13.9 s**. The physically correct
candidate — a 2-D/3-D consistency check — cannot be validated on this corpus **because it is the same
rule used to define the label**, so it needs a live ground-truth session before anyone implements it.

**The finding that reframes the question:** the torso yaw is not noisy, it is **under-resolved**. One
stereo disparity step is **6.3–11.3° of commanded yaw**, and on **60.1 % of frames both shoulders land
on the identical quantised depth level**, forcing the yaw to exactly 0 regardless of the true pose.
A quality score cannot recover information the measurement never had.

---

## 1. Current V5 baseline

From `UNITY_TORSO_V5_RELATIVE_YAW_2026-09-09.md` (ADR-037), unchanged and re-confirmed here:

| property | value |
|---|---|
| rigid-turn / twist / opposite gain | **1.000**, exact to 0.01° |
| false +90° sentinel | **blocked** — 0 frames within 15° of ±90 |
| ARM V2 | p50 **0.0000°**, p95 0.1153° through 90° torso turns |
| remaining problem | poor-depth captures still produce excessive torso rotation |

**How much of the problem V5's existing `TrunkGate` already solves**, measured here for the first
time. Of the 553 frames this audit labels BAD:

| disposition | frames | share of BAD |
|---|---|---|
| already rejected by the **span** check | 216 | **39.1 %** |
| already rejected by the **jump** check | 95 | **17.2 %** |
| **survives the shipped gate (the residual)** | **242** | **43.8 %** |

The residual is **1.086 % of all frames** (242 of 22,277). Its signature is stark: **|yaw3D| p50 =
169.0°, p95 179.4°** — near-±180° readings that pass the span and jump checks because they arrive
gradually or persist past re-acquisition. That is the classic ±180° ambiguity ADR-027 was written
about, not a general quality problem.

---

## 2. Torso measurement pipeline

Traced through the shipping code, not assumed.

```
RTMW3D  ->  uv[133] + conf              rtmw3d_pose.py         2-D pixels, per keypoint
        ->  sample_depth_surface(u,v)   oak_depth.py           5x5 window, surface-clustered, p30
        ->  backproject()               oak_depth.py           x = (u-cx)*z/fx,  y = (v-cy)*z/fy,  z = depth
        ->  build_body_landmarks()      wholebody_udp_sender   p = xyz_cam - mid_hip   (hip-relative m)
        ->  UDP  {"lm":[[x,y,z,vis]]}
        ->  CalcHipsAndSpine()          KalidokitPoseSolver    yaw = atan2(dx, dz) over the PAIR
        ->  TrunkGate.Evaluate()        TrunkGate.cs           span + finite + continuity, else HOLD
        ->  DampYaw + V5 composition    KalidokitControlRigDriver
```

Answering each question the brief asked:

| question | answer |
|---|---|
| **which coordinates determine yaw** | **x and z only.** `RollPitchYaw2(a,b).y = atan2(b.x−a.x, b.z−a.z)`. `y` is unused for yaw. |
| **which coordinates determine depth** | `z`, from the stereo depth map. `x` and `y` are *derived from* `z`: `x = (u−cx)·z/fx`. **A depth error therefore corrupts x as well**, which is why the 3-D segment length is a usable consistency test. |
| **independently sampled or model-derived?** | **Independently sampled** when a depth window is usable (`m = 1`): p30 of a surface-clustered 5×5 window. When there is no usable window, `_fallback_point` substitutes the *model's* root-relative `zrel` at **halved confidence** (M16). So the stream **mixes two different depth sources**, and the emitted landmark does not say which. |
| **same surface for shoulders and hips?** | **No — four independent 5×5 windows**, one per joint, each clustered separately (`SURFACE_GAP_MM = 100`). Nothing constrains them to the same body surface. |
| **can left/right depths belong to different surfaces?** | **Yes.** Each window picks the cluster nearest its own keypoint pixel. Measured: `rangeShMax` exceeds the 100 mm discontinuity threshold on **9,627 of 22,277 frames (43 %)**. |
| **2-D, 3-D or mixed geometry?** | **Mixed, and asymmetrically so.** `dx` carries pixel resolution scaled by depth; `dz` carries only disparity resolution. The yaw is `atan2` of a well-resolved quantity over a coarsely-resolved one. |

### 2a. The structural fact: the yaw is under-resolved

| capture | median depth step | median shoulder span | **one step = degrees of yaw** |
|---|---|---|---|
| `pipeline_logs` | 69.0 mm | 0.346 m | **11.3°** |
| `ab_legacy` | 42.0 mm | 0.350 m | **6.8°** |
| `f08` | 47.2 mm | 0.353 m | **7.6°** |
| `surface` | 38.6 mm | 0.351 m | **6.3°** |

And the raw shoulder depth difference across all 22,727 frames:

* **exactly 0 mm on 13,657 frames (60.1 %)** — both shoulders on the same quantised level, so the
  commanded yaw is exactly 0 whatever the subject is doing;
* p75 = 161 mm → **27.4°** of yaw; p90 = 193 mm → **33.5°**; p95 = 217 mm → **38.3°**.

So at the raw level the signal is close to ternary: **0°, or roughly ±27–38°**. The emitted yaw
histogram looks continuous (37.5 % in 0–5°, decaying tail) only because the one-euro depth filter
interpolates *temporally* between levels — smoothing hides the staircase, it does not add information.

---

## 3. Good vs bad yaw — how "bad" was defined without ground truth

There is no recorded ground-truth torso yaw, and a label built from any depth-difference feature would
be circular, because the depth difference *is* what produces the yaw. The label therefore uses the one
signal independent of the per-shoulder depth difference: **the shoulder separation in pixels**.

A rigid shoulder segment of width `S` at yaw `t` projects to `S·cos(t)`; in pixels at distance `Z`,
`uSpan = fx·S·cos(t)/Z`, so `cos(t) = uSpan·Z/K` with `K = fx·S` a single per-capture constant,
calibrated as the p98 of `uSpan·Z` (the most face-on frames). `Z` is `hipZ` — a robust trunk distance,
**not** the per-shoulder depth under test.

```
BAD  :  |yaw3D| − yaw2D > 20°      the depth claims a turn the image denies
```

The estimate is insensitive near t = 0 (cos is flat) but **decisive at large claimed yaw** — a genuine
60° turn must narrow the shoulders to 50 % of their face-on pixel width. So the label is strongest
exactly where it matters. Two sanity checks confirm it is not simply flagging motion:

* image-confirmed **standing still** (453 frames): BAD rate **0.00 %**
* image-confirmed **rapid legitimate turn** (156 frames, |yaw3D| p50 44.75°): BAD rate **0.00 %**

**Corpus:** 22,727 frames across the 5 captures that carry per-joint depth diagnostics
(`audit_log.jsonl` joined to `sender_log.jsonl` on `seq`; 122 frames dropped for having no usable
depth window at all). **553 BAD (2.43 %).**

> `armv1`, `armv1b` and `p14` — the captures the brief named — have `sender_log.jsonl` but **no
> `audit_log.jsonl`**, so they carry no per-joint depth-window diagnostics, no pixel coordinates, and
> hence neither the candidate predictors nor the label. They cannot support this analysis and are
> excluded rather than partially used.

---

## 4. Candidate predictors

Every feature the brief listed, scored by rank AUC against the label (0.5 = useless). Reference
segment lengths are estimated **online** (rolling median, 600-frame memory, 90-frame warm-up), so
every operating point is one a live gate could actually reach.

| predictor | AUC | thr | TPR | FPR | independence of the label |
|---|---|---|---|---|---|
| **`hipLenErrOnline`** | **0.897** | 0.170 | 0.759 | 0.088 | **INDEPENDENT** — hip u/z; label uses shoulder u + hipZ |
| `shLenErrOnline` | 0.856 | 0.125 | 0.759 | 0.142 | **PARTIAL** — shares shoulder u/z with the label |
| `dYaw` | 0.822 | 1.76° | 0.622 | 0.104 | INDEPENDENT (temporal only) |
| `dYawRate` | 0.821 | 37.8 °/s | 0.613 | 0.097 | INDEPENDENT (temporal only) |
| `confMin` | 0.773 | 0.588 | 0.609 | 0.196 | INDEPENDENT (model confidence) |
| `dsdShMax` | 0.595 | 97.3 mm | 0.260 | 0.054 | INDEPENDENT (F-08 window spread) |
| `rangeShMax` | 0.585 | 295 mm | 0.257 | 0.039 | INDEPENDENT (F-08 window range) |
| `surfCrossSh` | 0.516 | — | 0.441 | 0.317 | INDEPENDENT (surface discontinuity flag) |
| `validFracMin` | **0.482** | — | 0.949 | 0.818 | INDEPENDENT (valid-pixel count) |
| `_absTrunkDz` | **0.412** | — | 0.137 | 0.034 | INDEPENDENT (shoulder-vs-hip depth disagreement) |

**Negative results worth as much as the positive ones:**

* **`validFracMin` AUC 0.482 — no signal at all.** This independently reproduces F-08's own conclusion
  that counting valid pixels is not depth quality, now specifically for torso yaw.
* **`surfCrossSh` AUC 0.516 — the F-08 surface-discontinuity flag does not predict bad torso yaw**,
  even though it fires on 43 % of frames. The F-08 machinery was built for limb depth and does not
  transfer to this failure.
* **`_absTrunkDz` AUC 0.412 — *inverted*.** The intuition that a trunk should be vertical, so a large
  shoulder-vs-hip depth disagreement signals error, is **wrong on this data**: bad frames have
  *smaller* trunk depth disagreement than good ones.
* **Combining made it worse.** `max(hipLenErr, shLenErr) > 0.20` catches 77.4 % of BAD while rejecting
  12.66 % of GOOD; `hipLenErr` alone at 0.17 catches 75.9 % while rejecting only 8.8 %. The brief's
  instruction not to combine automatically was correct.

---

## 5. Correlation / separation — and the test that disqualifies the gate

A gate is only useful if it can separate a **fast legitimate turn** from a **bad measurement**.
Separating BAD from a mostly-static background is not enough, because rejecting BAD would then reject
genuine turning at a similar rate.

**BAD (n=553) vs image-confirmed LEGITIMATE turns (n=2157):**

| feature | AUC | legit p50 | legit p95 | bad p50 | bad p95 |
|---|---|---|---|---|---|
| `hipLenErrOnline` | 0.830 | 0.0546 | **0.8068** | 0.4388 | 2.2713 |
| `dYaw` / `dYawRate` | 0.801 | 0.30° | 4.54° | 2.82° | 53.74° |
| `shLenErrOnline` | 0.788 | 0.0660 | 0.7033 | 0.4067 | 3.7543 |
| `dsdShMax` | 0.554 | 27.4 | 292.3 | 37.7 | 842.2 |
| `validFracMin` | 0.492 | 1.000 | 1.000 | 1.000 | 1.000 |

`hipLenErr` does separate them (AUC 0.830) **in the medians** — but look at the tails: **legit p95 =
0.807 sits well above bad p50 = 0.439.** Genuine turning itself degrades the measurement (foreshortened
shoulders, one shoulder partly self-occluded, depth windows straddling the torso edge), so the two
populations overlap exactly where a gate would have to act.

**The collateral damage, measured:**

| threshold | catches BAD | **rejects GENUINE turns** | rejects STILL frames |
|---|---|---|---|
| 0.10 | 83.4 % | **30.9 %** | 30.2 % |
| 0.15 | 77.4 % | **22.8 %** | 20.3 % |
| **0.17** (ROC-optimal) | **75.9 %** | **19.7 %** | 17.4 % |
| 0.25 | 68.2 % | 13.7 % | 7.3 % |
| 0.40 | 55.2 % | 8.7 % | 1.5 % |

At every operating point the gate throws away a large fraction of real turning. There is no threshold
at which it is clearly worth it.

---

## 6. Counterfactual replay

Recorded yaw pushed through the shipping `DampYaw` (port verified to 6.7e-6°) and the V5 relative
composition. **A** = V5 as shipped; **B** = V5 + `hipLenErr > 0.17` hold.

| capture | variant | err mean | err p95 | >45° | >90° | **rest sd** | rejects | **gap p95** |
|---|---|---|---|---|---|---|---|---|
| `pipeline_logs` | A | 4.79 | 11.45 | 1.04 % | 0.30 % | 3.471 | 1.19 % | 0.98 s |
| | **B** | 4.43 | 11.29 | 0.40 % | 0.00 % | **0.462** | 5.26 % | 2.15 s |
| `ab_legacy` | A | 5.61 | 11.72 | 7.20 % | 2.37 % | 17.510 | 2.77 % | 1.60 s |
| | **B** | 4.65 | 11.00 | 3.98 % | 0.93 % | 14.864 | 12.17 % | 2.74 s |
| `f08` | A | 5.77 | 11.89 | 9.12 % | 5.26 % | 6.719 | 10.08 % | 7.09 s |
| | **B** | 5.64 | 11.59 | 8.33 % | 1.78 % | **18.424** | 22.88 % | **13.87 s** |
| `surface` | A | 4.25 | 11.20 | 2.00 % | 0.97 % | 7.382 | 1.25 % | 0.94 s |
| | **B** | 4.23 | 11.12 | 1.50 % | 1.05 % | **13.281** | 6.13 % | 2.02 s |

**The gate does not pay for itself:**

* **Error barely moves** — mean 4.79 → 4.43, 5.77 → 5.64, 4.25 → 4.23.
* **Rest jitter gets WORSE in 3 of 5 captures**, badly so on `f08` (6.72 → **18.42**) and `surface`
  (7.38 → 13.28). Holding a stale value and then releasing it produces larger discontinuities than
  following the noisy signal did.
* **Rejection rate balloons** — `f08` 10.1 % → 22.9 %.
* **Recovery latency becomes a product defect** — p95 gap 7.1 s → **13.9 s** on `f08`. A torso frozen
  for fourteen seconds is worse than a torso that wanders.

Only `>90°` events improve consistently (5.26 % → 1.78 % on `f08`), which is the one thing the gate
genuinely helps with — and those are largely the ±180° residual that a *targeted* check would catch
far more cheaply.

---

## 7. Adversarial conditions

| # | condition | frames | \|yaw3D\| p50 | \|yaw3D\| p95 | BAD rate |
|---|---|---|---|---|---|
| 3 | standing still | 453 | 3.58° | 8.96° | **0.00 %** |
| 10 | rapid legitimate turn | 156 | 44.75° | 90.00° | **0.00 %** |
| 1/2 | turn or twist (any) | 21,296 | 9.05° | 46.13° | 2.50 % |
| 5 | one shoulder low-confidence | 316 | **89.77°** | 152.45° | 19.62 % |
| 6 | poor depth (window spread > 200 mm) | 594 | 20.78° | 166.04° | 17.68 % |
| 8 | partial person (coverage < 12/33) | 178 | **90.00°** | 151.09° | 20.79 % |
| 9 | depth discontinuity | 9,627 | 12.54° | 56.68° | 2.92 % |

Conditions 5 and 8 sit at **|yaw3D| p50 ≈ 90°** — that is the `atan2(0,0)` sentinel, which **V5's
existing `TrunkGate` already blocks** (span check, 39.1 % of all BAD). Conditions **4 (partial torso
occlusion)** and **7 (empty room)** have no ground truth in these recordings; the empty-room case was
already proven closed in the V5 report (§5b, 0 frames within 15° of ±90).

**The system can already distinguish "no measurement" from motion. What it cannot distinguish is a
degraded measurement from fast legitimate movement** — §5 shows that directly, and no available
feature changes it.

---

## 8. Recommended torso quality gate

**Do not ship one. Not on this evidence.** Specifically:

* **Do not gate on `hipLenErr`** — §5/§6 show it costs 19.7 % of genuine turns, worsens rest jitter on
  3 of 5 captures, and pushes recovery latency to 13.9 s, for a mean-error gain under 1°.
* **Do not gate on the F-08 signals** — `validFracMin` (AUC 0.482), `surfCrossSh` (0.516) and
  `_absTrunkDz` (0.412) carry no usable information about torso yaw.
* **Do not gate on confidence** — AUC 0.773 looks tempting, but V4 already measured 0.71 confidence
  accompanying a fully hallucinated person, and the brief rules it out for that reason.

### What the evidence *does* support, in priority order

1. **A targeted ±180° consistency check, validated live first.** The residual that survives V5's gate
   is 242 frames (**1.086 %**) with **|yaw3D| p50 = 169°** — a reading that is geometrically impossible
   while the shoulders still span their full pixel width. The check is
   `reject when |yaw3D| is large AND uSpan·Z/K ≈ 1`, using only pixel coordinates and `hipZ`, both
   already computed in the sidecar.
   **This report cannot validate that rule, because it is the rule used to define the label** — doing so
   would be circular. It needs a live session with known torso orientation. That single caveat is the
   reason this verdict is CONDITIONAL rather than PASS.
2. **Fix the measurement rather than scoring it.** §2a is the real finding: yaw magnitude is derived
   from a quantity quantised at 6.3–11.3° per step, which is exactly zero 60 % of the time. Meanwhile
   the *pixel* shoulder span resolves foreshortening far more finely and needs no depth at all. The
   architecturally correct move is to take **yaw magnitude from 2-D foreshortening** and use depth only
   for the **sign** — a 1-bit decision, which is what disparity resolution can actually support. That
   is sidecar work and outside this brief's scope; it is recorded, not started.
3. **Reduce the quantisation** — closer subject, wider baseline, or sub-pixel disparity — which shrinks
   the step directly.

---

## 9. Regression against V5

**None. This task modified no code.**

The working tree does show `M Runtime/Retargeting/KalidokitControlRigDriver.cs`,
`M Scenes/Bootstrap.unity` and the new `TrunkGate.cs` / `TrunkGateTests.cs` — **those are the accepted
V5 changes from ADR-037**, still uncommitted from the previous task. F-09 added nothing to them. Its
only additions are:

* `python-sidecar~/f09_*.py` (the audit harness) and `oak_v4_evidence/f09_*` (its output),
* this document, and ADR-038 + the roadmap row.

V5's measured behaviour — gain 1.000, sentinel blocked, ARM V2 p50 0.0000°, 94/94 EditMode tests — is
untouched, and §1 re-confirms it from the same recordings.

---

## Final verdict

## CONDITIONAL PASS

*A predictor exists and is measurably independent, but the gate built from it fails its safety test;
the physically correct candidate cannot be validated on this corpus by construction and needs a live
ground-truth session.*

* **PASS was rejected** because no gate was validated as safe. `hipLenErr` reaches AUC 0.897 and still
  costs 19.7 % of genuine turns, degrades rest jitter on 3 of 5 captures, and takes recovery latency to
  13.9 s.
* **NO-GO was rejected** because a real, independent predictor *was* found, the residual failure is now
  precisely characterised (1.086 % of frames, |yaw3D| p50 169°), and a specific, physically-grounded
  and cheap check is identified — it simply cannot be scored against a label it defines.

### The one experiment that would settle it

A live OAK-D session with **known torso orientation** — the subject holding marked headings (0/±45/±90°)
on a floor protractor, which is the ground truth this corpus lacks. That makes the 2-D/3-D consistency
rule scoreable against truth rather than against itself, and would convert this to PASS or NO-GO in a
single capture. The guided-capture harness already exists (`oak_guided_v4.py`, `oak_guided_v4.ps1`).

---

## Honest limits

1. **No ground truth.** Every "BAD" label is the 2-D/3-D disagreement rule of §3. It is physically
   motivated and passes two sanity checks (0.00 % BAD on standing-still and on rapid legitimate turns),
   but it is a proxy, and the rule it most directly supports is the one rule it cannot score.
2. **`shLenErr` is only partially independent** of the label (shared shoulder u/z) and is reported as
   such; `hipLenErr` and the temporal features share nothing with it.
3. **`armv1`/`armv1b`/`p14` are excluded** — no `audit_log.jsonl`, so no pixel coordinates and no
   depth-window diagnostics. The brief named them, but they cannot carry this analysis.
4. **The corpus is 5 captures / 22,727 frames from one room, one camera pose, one subject distance
   band** (hipZ p50 ≈ 1.7 m). Quantisation scales with Z², so the step sizes here do not generalise to
   other distances.
5. **§6 is a replay**, not 22k rendered Unity frames: the conditioner is the shipping method and the V5
   composition identity is arithmetic, but the avatar poses were computed rather than rendered.
6. **`K` is calibrated per capture** as the p98 of `uSpan·Z`. If a subject never faces the camera in a
   capture, `K` is underestimated and `yaw2D` is biased low, which would *over*-report BAD. The
   0.00 % BAD rate on standing-still frames argues against that having happened here.
7. The **one-euro depth filter** sits between the raw p30 values and the emitted `z`, so §2a's
   quantisation is measured on the raw windows while the yaw histogram is measured post-smoothing.
   Both are labelled as such.

---

## Artefacts

Under `python-sidecar~/oak_v4_evidence/`: `f09_features.txt` · `f09_roc.txt` · `f09_replay.txt` ·
`f09_adversarial.txt` · `f09_features.jsonl` (22,727 rows, every feature per frame).

Harness under `python-sidecar~/`: `f09_torso_features.py` (join + label + features) ·
`f09_quantisation_roc.py` (quantisation, online ROC) · `f09_replay.py` (genuine-turn test +
counterfactual) · `f09_adversarial.py` (BAD-vs-LEGIT separation, adversarial conditions).
