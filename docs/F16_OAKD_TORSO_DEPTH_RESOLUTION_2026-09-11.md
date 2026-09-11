# F-16 — OAK-D Torso Depth Resolution / Measurement Feasibility

```text
VERDICT: CONDITIONALLY FEASIBLE
```

**Date:** 2026-09-11
**Scope:** evidence only. No production file was modified. `git status` for `Runtime/` is unchanged
from the V6 state; every F-16 artefact is a new `f16_*.py` diagnostic or a new file under
`oak_v4_evidence/f16/`.

---

## 1. Executive conclusion

**The sensor can measure torso yaw correctly — but only at ≤ 0.80 m, and that distance cannot frame
a standing body on this lens.**

Three things were established, each by measurement:

1. **Depth quantisation is real, fully characterised, and removable.** The production configuration
   resolves only **3 distinct depth values in a 200 mm window** at 1.2 m. Enabling sub-pixel
   disparity shrinks the ladder at 1.33 m from **83.5 mm to 3.0 mm (28×)** at **no FPS or latency
   cost**. Observation matched the analytic prediction `Z²·Δd/(f·B)` to **0.94–1.26×** across 9
   configurations and 8 range bands.

2. **Quantisation is NOT what puts the torso at ~14° when the user stands square.** That was the
   working hypothesis going in, and the data refutes it. Shrinking the ladder 28× moved the
   square-stance error from **14.68° to 13.52°**. All eight configurations at 1.33 m land between
   **10.55° and 15.61°**. The residual is a **+0.75 px systematic disparity error** between the two
   shoulder windows — a *matching* error, not a *rounding* error, and sub-pixel refinement cannot
   repair a match that is a whole pixel wrong.

3. **That residual is a fixed pixel error, so its angular cost grows as Z².** Measured square-stance
   error with sub-pixel enabled: **0.51° at 0.80 m → 14.76° at 1.33 m → 24.87° at 2.00 m.** At
   0.80 m **four of the five configurations tested pass** the ±5°/±10° acceptance — including
   **unmodified production**, at median 0.00° — and **all five are within ±10° on 100 % of frames**.
   (The fifth, `best`, misses the median threshold by 0.10°.) At ≥ 1.00 m **none** does.

**The conflict.** Torso-yaw accuracy requires **Z ≤ 0.80 m**. Full-body framing on this camera's
measured 70.2° vertical FOV requires **Z ≥ 1.24 m**. *These windows do not overlap.* The verdict is
CONDITIONALLY FEASIBLE because the condition — give up full-body framing and build an upper-body
mirror at 0.8 m — is a product decision, not a physical impossibility.

> **What sub-pixel does buy, and it is not nothing.** It cuts frame-to-frame torso instability by
> **4–6×** (p95 15.90° → 3.59° at 1.33 m; 5.21° → 1.69° at 0.80 m) for free. It converts a torso
> that *snaps* into one that *drifts*. It does not make the reading correct.

---

## 2. Current failure reproduction

Requested: reproduce the ~1.33 m / square / ~50° error. **Partially reproduced, and the difference
matters — it is reported rather than smoothed over.**

Production configuration, 1.33 m, subject square, 256 frames:

| metric | value |
|---|--:|
| median \|torso yaw\| | **14.68°** |
| p95 \|torso yaw\| | **29.36°** |
| max \|torso yaw\| | 29.60° |
| frame-to-frame p95 | **15.90°** |
| frames within ±5° | **26.2 %** |
| shoulder Δz | **89.0 mm** |
| observed depth step | **83.5 mm** (theory 88.5) |

The **mechanism** reproduced exactly: Δz lands on one disparity rung, and the error is bimodal —
0° when both shoulders share a rung, a full step-angle when they straddle one. The production
distance sweep shows this cleanly: median yaw of **0.00° at 1.00 m and 2.00 m** (shared rung) against
**11.62 / 14.68 / 18.74 / 26.05°** at 1.20 / 1.33 / 1.50 / 1.80 m (straddled rung). *Where a square
user's torso sits is decided by luck.*

**The magnitude did not reproduce.** V6 measured 49.6°; F-16 measures 14.7° at the same distance and
configuration. The difference is the yaw triangle's baseline `|Δx|`:

| | V6 live session | F-16 |
|---|--:|--:|
| `\|Δx\|` (landmark shoulder separation) | **154.5 mm** (p50, whole session) | **320–361 mm** |
| implied sensitivity | 0.35 °/mm | 0.17 °/mm |

Since `yaw = atan(Δz / |Δx|)`, halving `|Δx|` roughly doubles the error for the same Δz. In V6 the
emitted shoulder landmarks sat at a mean Z of 238 mm against a hip Z of 1521 mm — a regime where the
back-projection `x = (u−cx)·Z/fx` collapses the baseline. **F-16 never entered that regime**
(`|Δx|` stayed at the true shoulder width across all seven distances, as theory requires).

**This is left open.** The V6 log's time base could not be aligned to the Unity trace closely enough
to isolate the square window, so the cause of the `|Δx|` collapse is not established here. It is a
separate defect from the one F-16 characterises, and it is listed in §10.

---

## 3. Hardware and configuration

Read from the device, not from datasheets (`oak_v4_evidence/f16/device_probe.txt`).

| | |
|---|---|
| device | **OAK-D-PRO-W-97**, board DM9098 rev R8M2E8, mxid 14442C10F143D3D200 |
| stereo pair | CAM_B (left) / CAM_C (right), OV9282, native 1280×800 |
| colour | CAM_A, OV9782, native 1280×800, ISP 1/2 → **640×400** full FOV |
| **stereo baseline** | **75.0 mm** (EEPROM `getBaselineDistance`) |
| fx, CAM_C @ 640×400 | 282.995 px → **f·B = 21,224.6 mm·px** |
| fx, CAM_A @ 640×400 | 284.627 px (depth is reprojected into CAM_A) |
| **measured FOV** | **H 96.7°, V 70.2°** |
| preset | `HIGH_DENSITY` |
| left/right check | on, threshold 10 |
| **sub-pixel** | **OFF** (`subpixelFractionalBits` defaults to 3 = 1/8 px when enabled) |
| extended disparity | off |
| confidence threshold | 245 |
| depth align | `CAM_A`; depth unit mm |

**The empirical fit is confirmed.** F-09…F-15 used a fitted `f·B = 21,216 mm·px` derived from
observed quantisation. The EEPROM gives **21,224.6** — a ratio of **1.0004**. Every quantisation
conclusion in those investigations rests on a correct constant.

---

## 4. Distance sweep

Seven distances, subject square and still, ≥ 12 s each. The first attempt was discarded: the subject
cannot judge 0.80 m from 1.33 m and **cannot see the capture console at all**, so positions drifted
(measured 1.06 → 1.45 m against a commanded 0.80 → 2.00 m). The sweep was rebuilt as a closed loop —
range estimated from **shoulder pixel span** (an RGB/pose quantity, independent of the stereo depth
under test), displayed on a full-screen HUD, recording triggered automatically once the subject was
inside ±6 cm and steady. All seven targets then landed.

`oak_v4_evidence/f16/autosweep_baseline_1.jsonl`, `autosweep_sub3.jsonl`

| target | span px | **production** step | Δz | med \|yaw\| | p95 | **sub-pixel** step | med \|yaw\| | p95 | verdict |
|---|--:|--:|--:|--:|--:|--:|--:|--:|:--|
| **0.80 m** | 117.1 | 29.0 | 28 | 5.06° | 5.34° | **4.0** | **0.51°** | **1.21°** | **PASS** |
| 1.00 m | 94.8 | 48.0 | 0 | 0.00° | 7.88° | 6.0 | 7.87° | 9.76° | FAIL |
| 1.20 m | 79.4 | 69.0 | 69 | 11.62° | 11.69° | 10.0 | 15.08° | 16.62° | FAIL |
| 1.33 m | 71.7 | 89.0 | 89 | 14.68° | 14.80° | 11.5 | 14.76° | 16.83° | FAIL |
| 1.50 m | 63.4 | 116.0 | 116 | 18.74° | 19.02° | 15.0 | 10.33° | 14.47° | FAIL |
| 1.80 m | 52.9 | 177.0 | 161 | 26.05° | 26.26° | 21.0 | 17.04° | 20.53° | FAIL |
| 2.00 m | 47.1 | 236.0 | 0 | 0.00° | 0.00° | 25.0 | 24.87° | 28.90° | FAIL |

Depth **coverage was 100 % measured at every distance** under every configuration — availability is
not the problem.

The two production `0.00°` rows are **not passes**. They are frames where both shoulders happened to
land on the same rung; the same configuration reads 26.05° two rows up. Sub-pixel removes the
coin-flip and exposes the underlying error, which is why its numbers look *worse* at 1.00 m and
2.00 m. That is the measurement improving, not the system degrading.

---

## 5. Configuration sweep

Nine configurations, static scene, 10 s each (`config_sweep_scene1.txt`), plus eight re-measured with
the subject square at 1.33 m and five at 0.80 m (`cfgsweep_133.jsonl`, `cfgsweep_080.jsonl`).

### Cost and resolution

| config | depth step @1.3 m | FPS | latency p50 | coverage |
|---|--:|--:|--:|--:|
| **baseline (production)** | 65.5 mm | 31.1 | **22 ms** | 98.1 % |
| **sub-pixel 1/8, 400p** | **10.0 mm** | 31.2 | **24 ms** | 97.7 % |
| sub-pixel 1/32, 400p | 3.0 mm | 31.3 | 23 ms | 93.4 % |
| mono 1280×800 | 41.5 mm | 31.0 | 57 ms | 94.4 % |
| mono 800 + sub-pixel 1/8 | 5.0 mm | 31.1 | **215 ms** | 93.7 % |
| mono 800 + sub-pixel 1/32 | 1.0 mm | 31.1 | 215 ms | 85.4 % |
| + extended disparity | 5.0 mm | **15.5** | **530 ms** | 85.5 % |
| HIGH_ACCURACY preset | 65.5 mm | 31.1 | 22 ms | 86.8 % |
| no RGB alignment (diag) | 65.5 mm | 31.1 | 19 ms | 98.3 % |
| RGB 1280×800 | 95.0 mm | 30.1 | 28 ms | 98.4 % |
| RGB 800 + sub-pixel 1/8 | 12.0 mm | 30.0 | **30 ms** | 98.2 % |
| RGB 800 + mono 800 + sub 1/8 | 6.0 mm | 29.7 | 209 ms | 95.9 % |

Notes that matter:
- **`HIGH_ACCURACY` does not change the ladder at all** — it is a confidence/filter preset, and it
  costs 11 points of coverage.
- **RGB↔depth alignment does not smear quantisation** (`noalign` is identical to `baseline`), so the
  reprojection is not a suspect.
- **Mono 1280×800 costs 158–191 ms of latency** for a 2× ladder improvement that sub-pixel delivers
  8× better at 2 ms. Mono 800 is the wrong lever.
- **Extended disparity is unusable**: half the frame rate, 530 ms latency.

### With the subject square at **1.33 m** — the decisive table

| config | step mm | Δz mm | med \|yaw\| | p95 | frame-to-frame p95 | verdict |
|---|--:|--:|--:|--:|--:|:--|
| baseline | 83.50 | 89.0 | 14.68° | 29.36° | 15.90° | FAIL |
| sub-pixel 1/8 | 12.00 | 82.2 | 13.42° | 16.55° | **3.59°** | FAIL |
| sub-pixel 1/32 | **3.00** | 84.0 | 13.52° | 15.51° | 3.70° | FAIL |
| mono 800 | 47.50 | 95.0 | 15.24° | 15.35° | 7.64° | FAIL |
| mono 800 + sub 1/8 | 6.00 | 65.0 | **10.55°** | **12.40°** | **2.68°** | FAIL |
| RGB 800 | 95.00 | 101.0 | 15.61° | 15.83° | 15.50° | FAIL |
| RGB 800 + sub 1/8 | 12.00 | 97.0 | 15.22° | 17.20° | 5.28° | FAIL |
| RGB 800 + mono 800 + sub 1/8 | 6.00 | 86.0 | 13.57° | 15.41° | 2.98° | FAIL |

**The ladder falls 28×; the error moves 1.1°.** That single row-pair is the finding of this
investigation.

### With the subject square at **0.80 m**

| config | step mm | med \|yaw\| | p95 | frame-to-frame p95 | ≤10° | verdict |
|---|--:|--:|--:|--:|--:|:--|
| **baseline** | 29.00 | **0.00°** | 5.21° | 5.21° | **100 %** | **PASS** |
| **sub-pixel 1/8** | 4.00 | 1.86° | **4.27°** | **1.69°** | 100 % | **PASS** |
| **sub-pixel 1/32** | 1.00 | 2.45° | 3.97° | 1.91° | 100 % | **PASS** |
| **mono 800 + sub 1/8** | 2.00 | 3.83° | 5.20° | 1.90° | 100 % | **PASS** |
| RGB 800 + mono 800 + sub 1/8 | 2.00 | 5.10° | 5.91° | 1.36° | 100 % | FAIL (median 5.10 > 5.00) |

Every configuration is within ±10° on **100 %** of frames. Distance, not configuration, is the
variable that decides pass or fail.

---

## 6. Theoretical vs observed depth resolution — **mandatory section**

The model is `Z_quantised = f·B / d`, so one increment of disparity `Δd` costs

```text
step(Z) = Z² · Δd / (f·B)        Δd = 1 px integer,  2⁻ᵇ with b sub-pixel bits
f·B = 21,224.6 mm·px  (EEPROM: fx 282.995 px @640×400 × B 75.0 mm)
```

**Static scene, 9 configurations × 8 range bands, observed ÷ theory:**

| config | 0.8 m | 1.0 m | 1.2 m | 1.33 m | 1.5 m | 1.8 m | 2.0 m | 2.5 m |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| baseline | 0.96 | 0.98 | 0.97 | — | — | — | — | — |
| sub 1/8 | 1.06 | 1.02 | 0.94 | 0.97 | 0.98 | 1.00 | 1.00 | 1.01 |
| sub 1/32 | 1.06 | 0.68 | 0.94 | 1.16 | 0.91 | 1.05 | 1.02 | 0.98 |
| mono 800 | 0.96 | 0.98 | 1.00 | 1.00 | 0.98 | — | — | 1.00 |
| mono 800 + sub 1/8 | 1.06 | 1.02 | 0.94 | 0.97 | 1.06 | 1.00 | 1.02 | 0.98 |

**With the subject in frame, measured at the shoulder windows:**

| block | observed step | theory | ratio |
|---|--:|--:|--:|
| 0.80 m baseline | 29.00 | 29.11 | **1.00** |
| 1.00 m baseline | 48.00 | 48.16 | **1.00** |
| 1.80 m baseline | 177.00 | 160.99 | 1.10 |
| 1.33 m baseline | 83.50 | 88.49 | 0.94 |
| 1.33 m sub 1/8 | 12.00 | 11.54 | 1.04 |
| 1.33 m sub 1/32 | 3.00 | 2.97 | **1.01** |
| 1.33 m mono800+sub | 6.00 | 5.90 | **1.02** |

**Observed quantisation matches theory.** Where sub-pixel bottoms out at 1–2 mm the ratio drifts
(1.15–1.26) purely because the depth map is integer millimetres — the sensor has hit the output
format's floor, not a pipeline defect. There is no anomaly to chase: the limitation is imposed by the
stereo geometry, exactly as configured.

---

## 7. Torso yaw impact — translating depth error into degrees

The shipped Kalidokit y-channel was reduced to closed form and **validated against 11,477 live
frames from the V6 session at a maximum residual of 0.000000°**:

```text
yaw3D = atan2(Δx, Δz) + 90°          Δx, Δz = right shoulder − left shoulder
⇒  d(yaw)/d(Δz) = |Δx| / (Δx² + Δz²)  ≈  1/|Δx|  radians per mm near square
```

**`|Δx|` is the true shoulder width and does not change with distance** — confirmed across the sweep
(320.6, 331.6, 345.6, 338.0, 343.6, 332.9, 326.6 mm from 0.8 m to 2.0 m). So:

```text
yaw_error_per_depth_step = atan( step(Z) / |Δx| ) = atan( Z² · Δd / (f·B·W) )
```

which depends only on range. Measured against predicted, production configuration:

| Z | depth step | **deg per step** | Δz budget for ±5° |
|---|--:|--:|--:|
| 0.80 m | 29.1 mm | **4.9°** | 29.1 mm |
| 1.00 m | 47.1 mm | 7.9° | 29.0 mm |
| 1.20 m | 69.4 mm | 11.5° | 29.4 mm |
| 1.33 m | 88.5 mm | **14.6°** | 30.5 mm |
| 1.50 m | 116.7 mm | 18.8° | 30.4 mm |
| 1.80 m | 161.0 mm | 25.3° | 29.3 mm |
| 2.00 m | 212.2 mm | **31.4°** | 29.0 mm |

**The accuracy budget is ~29 mm of shoulder Δz, at every distance.** Production delivers a 29 mm step
only at 0.80 m and a 212 mm step at 2.00 m.

### The residual — what is left after quantisation is removed

With sub-pixel on, quantisation is ≤ 25 mm everywhere, yet Δz persists. Fitting a **constant
disparity error** `Δz = Z²·δd/(f·B)`:

| block | shoulder Z | Δz | yaw | step | **implied δd** |
|---|--:|--:|--:|--:|--:|
| 0.80 m | 792 | 0.0 | 0.00° | 4.0 | 0.000 px |
| 1.00 m | 985 | 46.0 | 7.87° | 6.0 | 1.006 px |
| 1.20 m | 1260 | 93.0 | 15.08° | 10.0 | 1.244 px |
| 1.33 m | 1370 | 89.0 | 14.76° | 11.5 | 1.006 px |
| 1.50 m | 1580 | 62.0 | 10.33° | 15.0 | 0.527 px |
| 1.80 m | 1867 | 102.0 | 17.04° | 21.0 | 0.621 px |
| 2.00 m | 2073 | 152.0 | 24.87° | 25.0 | 0.751 px |

**Implied disparity error: median +0.75 px, mean +0.74 px, spread 0.38 px.** Sub-pixel refinement
subdivides a match; it cannot repair one that is a whole pixel wrong. That is precisely why all eight
configurations at 1.33 m reproduce the same offset.

### Where the 0.75 px comes from

Two candidate causes, and the data separates them.

**Not the subject's posture.** The subject was turned **180°** at the same spot and the sign of Δz
did not flip (`sq_a/sq_b/sq_c` = +35 / +66 / +45 mm facing; `back` = +19 mm). A genuine postural
asymmetry reverses when the body reverses. More conclusively, the *same subject* reads **0.00° to
−5.10° at 0.80 m** — a real posture would be present there too.

**Not a left/right sensor bias either.** Across that 180° turn the "farther" shoulder stayed on the
same **anatomical** side while swapping **image** sides, so it is not tied to the sensor's left/right.

**Consistent with an occlusion-edge matching artefact.** The shoulder keypoints sit on the body's
silhouette edges, where the two mono cameras see different amounts of background. Tested on data
already captured (`edge_check.txt`): the shoulder reading **farther** had the **wider depth window**
in **69.5 %** and **75.2 %** of frames on two of three datasets (median window spread 31 vs 17 mm, and
35 vs 13 mm) and carried lower F-08 depth quality (0.925 vs 0.955). The third dataset split 50/50.
**Support, not proof** — flagged as such in §10.

---

## 8. Best achievable configuration

**`sub3` — sub-pixel 1/8 at the existing mono 400p.**

| | production | **sub3** | change |
|---|--:|--:|--:|
| depth step @1.33 m | 83.5 mm | **12.0 mm** | **7×** better |
| frame-to-frame yaw p95 @1.33 m | 15.90° | **3.59°** | **4.4×** better |
| frame-to-frame yaw p95 @0.80 m | 5.21° | **1.69°** | **3.1×** better |
| median \|yaw\| @0.80 m | 0.00° | 1.86° | — |
| p95 \|yaw\| @0.80 m | 5.21° | **4.27°** | better |
| FPS | 31.1 | **31.2** | none |
| latency | 22 ms | **24 ms** | +2 ms |
| coverage | 98.1 % | 97.7 % | −0.4 pt |

`mono800_sub3` scores marginally better on accuracy at 1.33 m (10.55° vs 13.42°) but costs
**191 ms of added latency** — fatal for a mirror. `sub5` adds nothing over `sub3` (13.52° vs 13.42°)
and costs 4 points of coverage.

**Nothing in the configuration space fixes the square-stance error.** The best any configuration
achieves at 1.33 m is 10.55°, against a 5° requirement.

---

## 9. Product feasibility

Against the §12 proposed thresholds — median \|yaw\| ≤ 5° and p95 ≤ 10° for a genuinely square
subject. *(No pre-existing product tolerance for torso yaw is recorded in `decisions.md`; these are
proposed engineering thresholds and should be confirmed against the actual product requirement.)*

| criterion | result | |
|---|---|:--|
| Zero-yaw stability **at 0.80 m** | median 0.00–3.83°, p95 3.97–5.21°, **100 % within ±10°**, all configs | **✓** |
| Zero-yaw stability **at ≥ 1.00 m** | median 7.87–15.61°, p95 9.76–29.36°, no configuration passes | **✗** |
| Quantisation not worth tens of degrees | production = **31.4°/step at 2.00 m**, 14.6° at 1.33 m. With sub-pixel: **2.0°/step at 1.33 m** | **✓ once sub-pixel is on** |
| Depth coverage through the torso range | **100 % measured** at every distance and configuration | **✓** |
| Repeatability | 5 independent standing attempts at 1.33 m: 5.73 / 7.49 / 10.92 / 13.42 / 14.76°, **all the same sign**, std 3.43° | **✓ (repeatable, and repeatably wrong)** |
| Compatible with full-body framing | accuracy needs ≤ 0.80 m, framing needs ≥ 1.24 m | **✗ no overlap** |

### The framing constraint

From this device's own intrinsics (V 70.2°), vertical coverage = `2·Z·tan(35.1°)`:

| Z | coverage | fits a 1.75 m body? |
|---|--:|:--|
| 0.80 m | 1.12 m | no |
| 1.00 m | 1.41 m | no |
| 1.20 m | 1.69 m | no |
| **1.245 m** | **1.75 m** | **yes — the threshold** |
| 1.33 m | 1.87 m | yes |
| 2.00 m | 2.81 m | yes |

**Full-body framing requires Z ≥ 1.24 m. Torso-yaw accuracy requires Z ≤ 0.80 m.**

---

## 10. Remaining limitations

1. **±90° is not solved and gets worse, not better.** At 1.33 m with sub-pixel: shoulder span
   collapses from 71 px to **16.9 / 13.8 px** and `|Δx|` from 345 mm to **55.8 / 48.7 mm**.
   `left90` reads +28.6° with a **standard deviation of 50.6°** and a 180° wrap; `right90` reads
   **+7.5° — the wrong sign**. Sign was correct on **6/6** of the ±30/±45/±60 blocks and **1/2** at
   ±90°. This confirms F-15 exactly and sub-pixel does not move it.
2. **Magnitude saturates and does not discriminate.** Commanded −30 / −45 / −60 produced
   **+40.2 / +40.2 / +48.1°**; commanded +30 / +45 / +60 produced **−25.3 / −40.0 / −40.4°**. The
   sign is trustworthy inside ±60°; the magnitude is not. Sub-pixel fixed the *stability* of these
   readings (std 0.93–4.18°) without fixing their *accuracy*.
3. **The occlusion-edge mechanism is supported, not proven.** Two of three datasets show the farther
   shoulder carrying the wider window at 69.5 % / 75.2 %; the third splits 50/50. A decisive test
   would sample depth inboard of the silhouette, which is a *sampler* change and therefore outside
   F-16's remit (§1).
4. **The V6 `|Δx|` collapse is unexplained.** V6's 49.6° needed a 154.5 mm baseline; F-16 never
   reproduced a baseline below 320 mm. That is a second, separate defect and it is not characterised
   here.
5. **One subject, one room, one session.** Shoulder separation 333.1 mm. Repeatability was measured
   across five standing attempts but not across subjects or days. F-15's second subject is not
   re-tested here.
6. **Commanded headings are not angular ground truth**, per §7 of the brief. Only the square (0°) and
   reversed (180°) cases carry strong semantic truth, and those are the ones the conclusions rest on.
7. **Interaction distance.** 0.80 m puts the user close enough that the 96.7° horizontal FOV covers
   only 1.43 m of width — arms spread wide will leave frame.
8. **Processing cost is not free at the top end.** Anything involving mono 1280×800 costs 191–209 ms
   of latency; extended disparity halves the frame rate. Only the 400p sub-pixel options are
   latency-neutral.
9. **Sub-pixel costs a little coverage**: 98.1 % → 97.7 % at 1/8, → 93.4 % at 1/32.

---

## 11. Recommendation

**Exactly one: `B. Change working distance`** — to **≤ 0.80 m**, paired with `D` as a required
companion. Ranked, with the measured basis for each:

| rank | option | measured basis |
|---|---|---|
| **1** | **B — change working distance to ≤0.80 m** | The **only** change that makes a square subject pass. All five configurations tested at 0.80 m are within ±10° on **100 %** of frames; none of eight at 1.33 m passes. |
| **2** | **D — enable sub-pixel disparity (1/8)** | Necessary but **not sufficient**. Cuts instability 3–4× at **+2 ms latency, zero FPS cost**. Does not fix the offset (13.42° vs 14.68° at 1.33 m). Adopt it with B, not instead of B. |
| 3 | F — wider-baseline stereo | Untested. The verified model says error scales as `1/(f·B)`, so doubling the 75 mm baseline would halve a 0.75 px error's metric cost — 14.76° → ~7.5° at 1.33 m. **Predicted, not measured.** The only hardware lever left if full-body framing at ≥1.24 m is non-negotiable. |
| 4 | G — second viewpoint | Untested, highest cost, and the only option that also addresses the ±90° collapse in §10.1. |
| 5 | E — increase resolution | **Measured and rejected.** RGB 1280×800 doubles shoulder span (70.7 → 141.7 px) and changes the error by 0.9° (14.68° → 15.61°). Mono 1280×800 buys 2× ladder for 191 ms latency. |
| 6 | C — change stereo/depth configuration | **Measured and rejected.** `HIGH_ACCURACY` does not alter the ladder and costs 11 points of coverage; extended disparity halves FPS. |
| 7 | A — accept the limitation | Rejected: a square user's torso reads 14.7° at 1.33 m and 24.9° at 2.00 m. |
| 8 | H — unsolvable with this camera | Rejected: **0.80 m demonstrably works**, on unmodified production configuration. |

**The decision this forces onto the product.** If the mirror must show a full standing body, it must
stand the user at ≥1.24 m, and at that range this camera cannot measure torso yaw to better than
~15°. If the mirror can be an upper-body mirror at 0.8 m, torso yaw works today. That trade is a
product call, not an engineering one, and it is the reason this report says CONDITIONALLY FEASIBLE
rather than FEASIBLE or NOT FEASIBLE.

---

## Evidence

All under `oak_v4_evidence/f16/` (no F-10/F-11/F-14/F-15 file was touched; every run has a unique
filename with overwrite protection):

| file | contents |
|---|---|
| `device_probe.txt` | EEPROM calibration, SDK capability enumeration, theoretical ladder |
| `config_sweep_scene1.txt` / `.json` | 9 configurations × 8 range bands, static scene |
| `config_sweep_rgbcfg.txt` | cost of the three RGB-800 configurations |
| `wall_probe_scene1.txt`, `wall_probe_flat_test.txt` | subject-free shoulder-pair noise floor |
| `dist_baseline.jsonl` | the **discarded** first sweep (kept — it is why the protocol changed) |
| `autosweep_baseline_1.jsonl` | 7-distance sweep, production configuration |
| `autosweep_sub3.jsonl` | 7-distance sweep, sub-pixel 1/8 |
| `pose_sub3_133.jsonl` | rotation set + the 180° bias test at 1.33 m |
| `cfgsweep_133.jsonl`, `cfgsweep_080.jsonl` | 8 and 5 configurations with the subject square |
| `sweep_baseline.txt`, `sweep_sub3.txt`, `cfg_133.txt`, `cfg_080.txt`, `pose_sub3.txt` | per-capture analyses |
| `consolidated.txt` | §1/§2/§3/§6/§11/§12 tables |
| `edge_check.txt` | occlusion-edge test |

Tools (all new, all diagnostic): `f16_configs.py`, `f16_probe_device.py`, `f16_config_sweep.py`,
`f16_wall_probe.py`, `f16_capture.py`, `f16_autosweep.py`, `f16_pose_protocol.py`,
`f16_sweep_configs.py`, `f16_analyze.py`, `f16_consolidate.py`, `f16_edge_check.py`.

---

```text
MEASUREMENT DECISION:
The OAK-D-PRO-W-97 can measure torso yaw to within ±5° only at ≤0.80 m, because a systematic
+0.75 px stereo matching error at the shoulder silhouettes costs Z²-scaling depth error that no
available stereo configuration removes, and 0.80 m cannot frame a standing body on this 70.2°
vertical FOV.

NEXT ENGINEERING ACTION:
Put the working-distance decision to the product owner — upper-body mirror at 0.80 m (torso yaw
works today, and should ship with sub-pixel 1/8 enabled for its 3-4x stability gain at +2 ms), or
full-body at >=1.24 m (torso yaw stays broken and the only untested lever left is a wider stereo
baseline).
```
