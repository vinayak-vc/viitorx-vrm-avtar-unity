# F-17 — Wider Stereo Baseline Feasibility for Full-Body Torso Yaw

```text
VERDICT: NOT FEASIBLE
```

**Date:** 2026-09-11
**Scope:** evidence only. No production file was modified — `git diff -- Runtime/` shows only the V6
changes from the earlier task. All F-17 code is new `f17_*.py` diagnostic tooling; all data is new
under `oak_v4_evidence/f17/`.

**Read this first.** The verdict is scoped precisely: *a wider stereo baseline alone is not a viable
route to full-body torso yaw at ≥1.24 m.* **No wider-baseline camera was available to measure**, and
that is stated up front rather than buried — §15 lists exactly what would overturn this.

---

## 1. Executive conclusion

Three findings, in order of how much they should change the plan.

1. **The wider-baseline hypothesis fails its own best available test.** F-16's 1.33 m configuration
   sweep contains **three independent doublings of f·B** on the same subject in one continuous hold.
   If the +0.75 px bias were a fixed *pixel* offset — the assumption behind "double the baseline,
   halve the error" — each doubling would cut Δz by 50 %. Measured ratios: **1.067, 0.791, 0.887**
   — mean **0.915**, and **one of the three moved the wrong way**. Doubling f·B moves the bias by
   **−9 % on average**, which is indistinguishable from no effect. Quantisation fell 2× in every one
   of those pairs, so f·B certainly changed; **the bias simply does not follow it.**

2. **Even the optimistic model barely clears the bar, and only at the very bottom of the range.** Under
   the ideal `Δz ∝ 1/(f·B)` law, a **150 mm** head (double the current 75 mm, and the widest common
   stock stereo product) gives **4.66° at 1.24 m — pass — then 5.36° at 1.33 m and 11.98° at 2.00 m**.
   One passing distance with **0.34° of margin** is not a product. Reaching ≤5° at 2.00 m needs
   **364 mm**; under the measured scaling it needs ~161 m, i.e. never.

3. **The lens, not the baseline, is the cheap lever — and it is already measured.** The required
   baseline has a closed form, and the field of view enters it twice:

   ```text
   B_required = H_body² · δd / ( 2 · h_px · W_shoulder · tan(ε) · tan(vfov/2) )
   ```

   Because a wider lens lets the subject stand closer for the same full-body framing, and the
   requirement falls as Z², **widening the lens beats widening the baseline**:

   | vertical FOV | Z_min for a 1.75 m body | yaw at the current 75 mm | B required for ≤5° |
   |---|--:|--:|--:|
   | 50° | 1.88 m | 20.48° | 320 mm |
   | 60° | 1.52 m | 13.70° | 209 mm |
   | **70.2° (this camera)** | **1.24 m** | **9.34°** | **141 mm** |
   | 80° | 1.04 m | 6.58° | 99 mm |
   | **95°** | **0.80 m** | **3.90°** | **58 mm — below the 75 mm we already have** |

   The 95° row is not an extrapolation into the unknown: **F-16 measured the current camera at
   0.80 m and it passed on 4 of 5 configurations, with 100 % of frames within ±10°.**

**So the answer to "can a wider baseline make full-body torso yaw work at ≥1.24 m" is no — and the
question was the wrong one.** The constraint that forces 1.24 m is the **70.2° vertical FOV**, and
that is the cheaper thing to change.

---

## 2. F-16 baseline reference

Reproduced, not replaced (brief §3). Production geometry, sub-pixel 1/8, subject square, from the
F-17 capture (`raw/sweep1`, 402 frame sets, HUD-guided closed loop):

| | reference expected | **F-17 measured** |
|---|---|--:|
| baseline B | 75 mm | **74.992 mm** (EEPROM) |
| f·B | ≈21,224.6 mm·px | **21,222.4 mm·px** |
| depth step @1.33 m | ≈12 mm | **11.5 mm** |
| square yaw error @1.33 m | 13–15° | **12.88°** |

| Z | Δz measured | med \|yaw\| | p95 \|yaw\| |
|---|--:|--:|--:|
| 1.24 m | 75.0 mm | 12.70° | 15.67° |
| 1.33 m | 78.0 mm | 12.88° | 14.93° |
| 1.50 m | 88.0 mm | 14.26° | 16.55° |
| 1.80 m | 129.0 mm | 20.12° | 23.42° |
| 2.00 m | 138.0 mm | 21.54° | 28.62° |

The reference condition reproduces cleanly. Depth coverage was **100 %** at every distance.

---

## 3. Candidate hardware

**Inventory result: exactly one stereo device is attached.**

```text
OAK-D-PRO-W-97   board DM9098 rev R8M2E8   mxid 14442C10F143D3D200
```

No OAK-D-LR, no OAK-D-W, no second camera, no custom synchronized pair. **The brief's candidate list
could not be populated**, so §7's per-candidate metrics, §11's rotation validation on a candidate and
§13's per-candidate thermal/CPU run have no subject and are marked not-applicable rather than
invented.

What the board *does* offer is three cameras and therefore three real baselines, read from its own
EEPROM (`inventory.txt`):

| pair | baseline | fx @640×400 | f·B | vs production | horizontal fraction |
|---|--:|--:|--:|--:|--:|
| CAM_A–CAM_B | 37.363 mm | 287.157 | 10,729 | 0.506× | 0.9999 |
| CAM_A–CAM_C | 37.641 mm | 282.995 | 10,652 | 0.502× | 0.9999 |
| **CAM_B–CAM_C (production)** | **74.992 mm** | 282.995 | **21,222** | 1.000× | 1.0000 |

Halving the baseline tests the scaling law exactly as well as doubling it, so this was attempted as
the substitute experiment. **It failed — see §7.**

---

## 4. Theoretical prediction

`step(Z) = Z²·Δd/(f·B)` and `yaw = atan(Δz / W)`, with the F-16 measured bias `δd = 0.75 px` and the
measured shoulder separation `W = 333.1 mm`.

**Baseline required to reach the target, ideal model:**

| Z | B for ≤5° | B for ≤3° | × current |
|---|--:|--:|--:|
| 1.24 m | **139.8 mm** | 233.4 mm | 1.86× / 3.11× |
| 1.33 m | 160.9 mm | 268.5 mm | 2.15× / 3.58× |
| 1.50 m | 204.6 mm | 341.6 mm | 2.73× / 4.55× |
| 1.80 m | 294.6 mm | 491.9 mm | 3.93× / 6.56× |
| 2.00 m | 363.8 mm | 607.3 mm | 4.85× / 8.10× |

**What a 150 mm head would deliver, ideal model:**

| Z | Δz | yaw | ≤5°? |
|---|--:|--:|:--|
| 1.24 m | 27.2 mm | **4.66°** | **PASS** |
| 1.33 m | 31.3 mm | 5.36° | FAIL |
| 1.50 m | 39.8 mm | 6.81° | FAIL |
| 1.80 m | 57.2 mm | 9.75° | FAIL |
| 2.00 m | 70.7 mm | 11.98° | FAIL |

The ideal model reproduces the *current* baseline to within 1–3° (§2 vs the fitted curve) — which is
expected, since δd was fitted on it. **It says nothing about how δd behaves when the baseline
changes.** That is what §7 had to measure.

---

## 5. Square-stance results

Captured with the F-16 closed-loop HUD protocol (brief §6): range estimated from shoulder pixel span,
displayed full-screen, recording auto-triggered inside ±7 cm and steady. The discarded console
protocol was not reused. 80–81 frame sets per distance at 8 Hz, 5 distances, plus the device's own
sub-pixel depth and raw CAM_A/CAM_B/CAM_C frames for offline re-analysis.

Measured values are in §2. `|Δx|` held at **333–356 mm** across the whole range, confirming again
that the yaw triangle's baseline is the true shoulder width and does not vary with distance.

---

## 6. Distance results

All five full-body distances **FAIL** the §14 acceptance (median ≤5°, p95 ≤10°) on the production
geometry — 12.70° at 1.24 m rising to 21.54° at 2.00 m. Nothing at or beyond the full-body framing
threshold passes. This is F-16's result reproduced on a fresh capture.

---

## 7. Systematic disparity bias — the decisive section

### 7a. The direct test was attempted and it failed as an instrument

402 frame sets were processed offline through host rectification (`cv2.stereoRectify` from the
device's own EEPROM) and a **single shared SGBM matcher**, so the baseline was the only variable:

| source | baseline | Z at 1.24 m | ratio vs device |
|---|--:|--:|--:|
| device B–C | 74.99 mm | 1254 mm | 1.000 |
| **host B–C** | 74.99 mm | **1236 mm** | **0.986** |
| host A–C | 37.64 mm | 1696 mm | **1.353** |

**host B–C tracks the device to within 1–8 %** — the rig, the rectification and the matcher are
sound. **host A–C is wrong by 21–41 %**, equivalent to a constant **≈1.93 px disparity deficit**.
That corrupts Δz, which is a few tens of mm, so the A–C pair cannot measure what F-17 needs.

Five checks locate the fault in the *pair*, not the rig:

| check | result |
|---|---|
| epipolar alignment after rectification | **\|dy\| p50 = 0.00 px**, p95 2.07 / 2.49 px — geometry correct |
| rectified principal points | **cx₁ = cx₂ exactly** on both pairs — no built-in disparity offset |
| same matcher on B–C | accurate (0.986–1.079) — matcher and code are fine |
| upsampled matching ×2, ×3 | ratio stays 1.21–1.41 — **not** a low-disparity limit |
| distortion models | all three sockets `Perspective`, 14 coefficients — no model mismatch |

**Conclusion: CAM_A is an IR-cut colour sensor while CAM_B/CAM_C are unfiltered mono, and that
spectral mismatch defeats block matching between them.** Torso depth coverage was **42 % for A–C
against 88 % for B–C** under the best of five tuned matcher configurations. This hardware cannot
supply a second usable baseline.

### 7b. The best remaining probe says the bias does not scale with f·B

F-16's 1.33 m sweep held the subject square through all eight configurations in one continuous
session, giving three pairs that differ **only** by mono resolution — three independent doublings of
f·B:

| comparison (f·B ×2) | Δz before | Δz after | ratio |
|---|--:|--:|--:|
| baseline → mono800 | 89.0 mm | 95.0 mm | **1.067** |
| sub3 → mono800_sub3 | 82.2 mm | 65.0 mm | 0.791 |
| sub3_rgb800 → best | 97.0 mm | 86.0 mm | 0.887 |

```text
pure PIXEL-domain bias  -> ratio 0.50 per doubling   (Case A: wider baseline fixes it)
pure DEPTH-domain bias  -> ratio 1.00 per doubling   (Case B: wider baseline buys nothing)
MEASURED                -> ratio 0.915 mean, 0.887 median, spread 0.115
```

**This is Case B.** Doubling f·B moves the bias **−9 % on average**, with one comparison moving the
wrong way by +6.7 %. Projecting that scaling onto baseline:

| B | Δz @1.33 m | yaw | ≤5°? |
|---|--:|--:|:--|
| 75 mm | 78.0 mm | 13.18° | FAIL |
| 150 mm | 71.4 mm | 12.09° | FAIL |
| 300 mm | 65.3 mm | 11.09° | FAIL |
| 600 mm | 59.7 mm | 10.17° | FAIL |

Reaching 5° would need **≈161 metres** of baseline. **The two models disagree by orders of
magnitude, and the measurement that would settle it is precisely the one this hardware cannot
make.** Given a choice between a model fitted to one operating point and a model tested against
three changes to that operating point, the tested one governs.

**Stated plainly (§15 repeats it): these doublings come from RESOLUTION, not BASELINE.** They are the
closest available probe of how the bias responds to f·B, not a substitute for measuring a wider
baseline. The physical mechanism argues the same way, though: F-16 attributed δd to occlusion at the
shoulder silhouettes, and a **wider** baseline makes silhouette occlusion **worse**, partly cancelling
whatever geometric gain it buys.

---

## 8. Shoulder matching / occlusion analysis

Median shoulder-window spread (larger = the window is straddling an edge):

| Z | device B–C | host B–C | host A–C |
|---|--:|--:|--:|
| 1.24 m | 19.0 mm | 49.3 mm | 47.4 mm |
| 1.33 m | 35.0 mm | 66.9 mm | 68.0 mm |
| 1.50 m | 62.0 mm | 52.8 mm | 219.3 mm |
| 1.80 m | 71.0 mm | 140.8 mm | 784.9 mm |
| 2.00 m | 59.0 mm | 166.6 mm | 580.4 mm |

The shoulder windows get **worse** with distance on every source, and the half-baseline pair degrades
catastrophically. The device's tuned matcher is markedly better than host SGBM at the shoulders
(19–71 mm vs 49–167 mm) — worth noting, because it means F-16's production numbers already reflect
the *best* matcher available here, not a weak one.

No candidate hardware existed, so the brief's question — whether better stereo geometry alone
improves the shoulder measurement — could not be answered directly. The A–C direction (narrower)
got worse, but is confounded by the spectral mismatch and carries no weight either way.

---

## 9. Rotation results

**Not applicable.** §11 asks for a rotation set on a candidate wider-baseline system. No candidate
exists. The rotation behaviour of the *current* system was measured in F-16 (sign correct on 6/6 of
the ±30/±45/±60 blocks, wrong at ±90°, magnitude saturating at 40–48°) and F-17 changes none of it.

---

## 10. Full-body framing

Measured from this device's own intrinsics: **H 96.7°, V 70.2°**. Vertical coverage `= 2·Z·tan(V/2)`;
a 1.75 m body needs **Z ≥ 1.24 m**.

**The finding that matters is the coupling.** Because Z_min is set by the lens and the baseline
requirement grows as Z², field of view is the stronger lever — see the §1 table. And there is a
**zero-cost** version of it: this camera is 96.7° horizontal × 70.2° vertical. **Mounted in portrait,
that becomes 96.7° vertical**, giving

```text
Z_min = 1750 / (2 · tan(48.35°)) = 0.78 m
```

The stereo pair rotates with the body, so rectification and the device pipeline are unchanged — only
the images need rotating before the pose model. And **0.78–0.80 m is precisely where F-16 measured
the current hardware to pass** (median 0.00–3.83°, 100 % of frames within ±10°, on production
configuration).

**Cost of that orientation, and it is real:** horizontal coverage falls to `2 × 0.78 × tan(35.1°) =
1.10 m`. An adult arm span is ~1.75 m, so **fully spread arms would leave frame**. Whether that is
acceptable is a product decision of the same kind F-16 surfaced.

---

## 11. FPS / latency / processing cost

No candidate hardware to profile. What was measured:

| | |
|---|--:|
| device pipeline, sub-pixel 1/8 (F-16) | 31.2 fps, 24 ms latency |
| host rectify + SGBM, **both** pairs, offline | 28.7 ms/frame (34.8 fps ceiling) |
| depth coverage, device vs host B–C vs host A–C | 87.8 % / 57.6 % / 2.2 % (static scene) |
| torso coverage, host B–C vs host A–C (subject) | 88 % / 42 % |

The host rig is offline analysis tooling and was never intended to ship. The F-16 latency ceilings
still stand: 191–209 ms configurations rejected, extended disparity at 530 ms rejected.

---

## 12. Current vs wider-baseline comparison

| Hardware | Baseline | Distance | Depth step | Implied δd | Median yaw | p95 yaw | FF p95 | FPS | Latency | Verdict |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|:--|
| **Current OAK-D, sub-pixel 1/8** | 75 mm | 1.33 m | 12 mm | ~0.75 px | **13.42°** | 16.55° | 3.59° | 31.2 | 24 ms | **FAIL** |
| Current OAK-D (F-17 recapture) | 75 mm | 1.33 m | 11.5 mm | 0.88 px | 12.88° | 14.93° | — | 31.2 | 24 ms | FAIL |
| Current OAK-D, **0.80 m** (F-16) | 75 mm | 0.80 m | 4 mm | ~0.0 px | **0.51°** | **1.21°** | 1.69° | 31.2 | 24 ms | **PASS** |
| host B–C (rig control) | 75 mm | 1.33 m | — | 0.56 px | 9.17° | 11.26° | — | 34.8 | offline | FAIL |
| host A–C (half baseline) | 37.6 mm | 1.33 m | — | 0.93 px | *38.98°* | *41.00°* | — | 34.8 | offline | **INVALID** — §7a |
| 150 mm head *(predicted, ideal)* | 150 mm | 1.24 m | — | 0.75 px assumed | *4.66°* | — | — | — | — | *predicted PASS* |
| 150 mm head *(predicted, measured scaling)* | 150 mm | 1.33 m | — | — | *12.09°* | — | — | — | — | *predicted FAIL* |

*Italic rows are predictions or invalid measurements and are labelled as such.*

---

## 13. Product feasibility

Against §14:

| criterion | wider baseline (150 mm, best case) | current camera in portrait at 0.78 m |
|---|---|---|
| median \|yaw\| ≤ 5° at 1.24–1.50 m | 4.66° at 1.24 m only; fails from 1.33 m | **measured 0.00–3.83° at 0.80 m** |
| p95 \|yaw\| ≤ 10° | not measured | **measured 3.97–5.91°** |
| frame-to-frame p95 ≤ 5° | not measured | **measured 1.36–1.91°** |
| coverage ≥ 95 % | not measured | **measured 100 %** |
| latency compatible | unknown hardware | **unchanged — same camera** |
| full-body framing | yes, by construction | **yes at 0.78 m** |
| arm-spread coverage | yes | **no — 1.10 m against a ~1.75 m span** |
| evidence status | **predicted only** | **measured, except the orientation change itself** |

---

## 14. Recommendation

**Do not procure a wider-baseline stereo head.** Ranked:

| rank | action | basis |
|---|---|---|
| **1** | **Trial the existing camera in portrait orientation at ~0.78 m.** Zero hardware cost; the stereo pipeline is untouched; only the image needs rotating before the pose model. | Full-body framing at 0.78 m follows from the **measured** 96.7° horizontal FOV, and F-16 **measured** the current hardware passing at 0.80 m. The only unmeasured step is the rotation itself. |
| 2 | If arm-spread coverage kills portrait, source a **wider-lens** (not wider-baseline) stereo head and re-run `f17_raw_capture.py`. | B_required falls as `1/tan(vfov/2)` **and** as Z², so FOV is the stronger and cheaper lever (§1 table). |
| 3 | Only if 1 and 2 both fail: trial a wider-baseline head, with a **pre-committed decision rule** — it must deliver ≤5° median at ≥1.24 m, not merely "better". | Both models agree a 150 mm head passes at most one distance; the tested model says it passes none. |
| 4 | Second viewpoint. | Highest cost. Also the only option that addresses the ±90° collapse (F-16 §10.1), which none of the above touches. |

---

## 15. Honest limitations

1. **The primary question was not measured.** No wider-baseline camera was available. Everything in
   §4 about 150 mm and above is prediction. The verdict rests on (a) the ideal model clearing the bar
   at only one distance with 0.34° of margin, (b) three measured f·B doublings that show the bias
   does not scale, and (c) the occlusion mechanism arguing the wrong way. **A measured
   counter-example from real wider-baseline hardware would overturn this**, and the acceptance test
   is already written: ≤5° median and ≤10° p95 at ≥1.24 m, square stance, closed-loop HUD protocol.
2. **The substitute experiment failed.** The half-baseline A–C pair is not a usable stereo pair on
   this hardware (§7a). Five checks locate the fault in the colour/mono spectral mismatch rather than
   the rig, but that diagnosis is inference from elimination, not a direct spectral measurement.
3. **The scaling evidence comes from resolution, not baseline.** Three doublings of f·B via mono
   resolution are the closest available probe. A baseline change also alters occlusion geometry,
   which resolution does not — and that difference could cut either way, though the occlusion
   argument says it cuts against a wider baseline.
4. **The portrait recommendation has one unmeasured step.** Rotating the camera puts the stereo
   baseline vertical in world coordinates, so the shoulder line becomes perpendicular to the baseline
   rather than parallel. That changes the occlusion geometry at the shoulders in a way this
   investigation has not measured — it could help or hurt. It is a trial, not a conclusion.
5. **One subject, one session, one room.** W = 333.1 mm. No second subject and no repeat day.
6. **δd = 0.75 px is a median over 7 distances with a spread of 0.38 px.** The F-17 recapture gives
   0.63–1.01 px over its five distances. Required-baseline figures inherit that ±50 % uncertainty.
7. **§7's per-candidate metrics, §11's rotation validation and §13's thermal/CPU run were not
   performed** because they require candidate hardware. They are marked not-applicable, not
   estimated.

---

## Evidence

Under `oak_v4_evidence/f17/` (overwrite-protected; no F-10…F-16 file was touched):

| file | contents |
|---|---|
| `inventory.txt` | device enumeration + EEPROM extrinsics for all three camera pairs |
| `rig_validation.txt` | host rig vs device on a static scene |
| `tuning.txt` | five matcher configurations × both pairs, coverage |
| `raw/sweep1/` | **402 synchronized CAM_A/B/C + device-depth frame sets**, 5 distances, + `calib.json`, `manifest.jsonl` |
| `multibaseline.txt` / `.json` | per-distance, per-source Δz / \|Δx\| / yaw / implied δd |
| `model.txt` | required-baseline tables, both models, and the FOV coupling |

Tooling (all new, all diagnostic): `f17_inventory.py`, `f17_stereo_rig.py`, `f17_validate_rig.py`,
`f17_raw_capture.py`, `f17_tune.py`, `f17_analyze.py`, `f17_model.py`.

---

```text
MEASUREMENT DECISION:
The +0.75 px shoulder matching bias does not scale with f*B - three independent doublings moved it
-9% on average against the -50% a pixel-domain bias requires - so widening the stereo baseline
cannot deliver the ≤5° square-stance accuracy at full-body distance.

HARDWARE DECISION:
WIDER BASELINE INSUFFICIENT

NEXT ENGINEERING ACTION:
Trial the existing camera mounted in portrait orientation, which turns its measured 96.7° horizontal
field of view into the vertical one and frames a full body at 0.78 m - the range where F-16 already
measured this hardware passing - and verify arm-spread coverage and the rotated-baseline occlusion
behaviour before anything is procured.
```
