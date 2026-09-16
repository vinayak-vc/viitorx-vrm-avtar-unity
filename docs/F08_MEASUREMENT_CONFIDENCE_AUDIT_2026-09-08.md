# F-08 MEASUREMENT + CONFIDENCE AUDIT — 2026-09-08

Evidence-first forensic audit. **No production behaviour was changed.** All instrumentation added
for this audit is `--audit-log`, default **OFF**.

Captures used: `pipeline_logs_p13`, `pipeline_logs_p14`, `pipeline_logs_rollback` (archived), plus a
new instrumented capture `pipeline_logs_f08` — **5780 frames, 12 joints, 10 blocks, human subject**,
carrying per-frame/per-joint raw confidence, 2D pixel, depth validity, full depth-window statistics,
and an 11×11 raw depth crop every 3rd frame (1926 crop frames).

---

## 1. Executive verdict

## **ROOT CAUSE IDENTIFIED**

Two distinct defects, both established with code-level and numerical evidence:

**(A) There is no depth-quality signal anywhere in the pipeline.** The 3D point is
`2D landmark ⊗ independently-sampled OAK depth`. Confidence describes **only** the 2D factor. The
sole depth check is `min_valid = 6 of 25`, which passes **98.1–98.5%** of the time and does not
measure whether the sampled pixels belong to the same surface. **15–32% of depth windows straddle a
>150 mm depth discontinuity**, and those produce frame-to-frame depth jumps **10.4× larger at p99**
(3945 mm vs 379 mm). This is why bone lengths fluctuate, and therefore why P1-4 failed.

**(B) A physically occluded limb produces a clean, high-confidence, depth-valid measurement.** During
a sustained hand-behind-torso hold the hidden wrist held median confidence **0.575**, was below the
0.3 gate only **2.10%** of frames, had valid depth **99.89%** of frames, and a **perfectly uniform**
depth window (spread p50 = 0 mm). The model hallucinates the wrist onto the torso; the depth sampler
faithfully measures the torso. Every quality signal in the system reports "good".

**(B) is not fixed by (A).** They need different answers.

---

## 2. Measurement pipeline trace

| step | class / function | file | timestamp source | frame / units | failure behaviour |
|---|---|---|---|---|---|
| RGB frame selection | `q_rgb.get()` + `tryGetAll()`, keep newest | `wholebody_udp_sender.py:341` | `dai` device clock | 640×400 BGR | blocks on `get()`, guarantees liveness |
| depth packet | `q_depth.get()` + `tryGetAll()` | `:344` | `dai` device clock | 640×400 uint16 mm | — |
| **RGB/depth pairing** | `min(cands, key=abs(d.ts − rgb.ts))` | `:355` | device clock | — | falls back to newest on exception |
| pose inference | `RTMW3D.infer` → `_preprocess` → `session.run` → `_decode` | `rtmw3d_pose.py:141` | host `t_pose` | 288×384 input | — |
| 2D landmark | `_decode`: `argmax` over SimCC x/y, `/SIMCC_SPLIT`, un-warp | `rtmw3d_pose.py:120` | — | RGB frame px | always emits 133 points |
| **confidence** | `min(simcc_x.max(1), simcc_y.max(1))`, clamped ≥0 | `rtmw3d_pose.py:131` | — | **raw activation, unitless** | never absent |
| depth alignment | `stereo.setDepthAlign(CAM_A)` | `oak_depth.py:40` | on-device | RGB-aligned | — |
| depth sampling | `sample_depth_mm(depth,u,v,k=5,min_valid=6,percentile=30)` | `oak_depth.py:67` | — | mm | **returns 0.0 if <6 valid** |
| validity check | `window[window>0].size < min_valid` | `oak_depth.py:86` | — | count | binary only |
| pixel → camera | `backproject`: `x=(u_d−cx)z/fx`, `y=(v_d−cy)z/fy` | `oak_depth.py:91` | — | metres, X right / Y down / Z fwd | hole → `measured=False`, xyz=0 |
| 3D point | `xyz_cam[i]` | `wholebody_udp_sender.py:400` | — | camera-space m | — |
| hip-relative | `p = xyz_cam[i] − mid_hip` | `wholebody_udp_sender.py:97` | — | hip-relative m | conf < 0.3 → joint dropped |
| P1-1 input | `skel.update(pos_in, cnf_in, time.time(), depth_valid=dv_in)` | `wholebody_udp_sender.py:447` | host `time.time()` | hip-relative m | — |

**One thing checked and cleared:** `backproject` scales `uv` into *depth* pixels (`sx = dw/rgb_w`)
while using `cx,cy,fx,fy`. Those intrinsics are read as
`read_rgb_intrinsics(device, dw, dh)` (`wholebody_udp_sender.py:270`) — i.e. **at depth resolution**.
Units are consistent. This is **not** a bug.

---

## 3. Confidence semantics

```python
conf = np.maximum(np.minimum(simcc_x.max(axis=1), simcc_y.max(axis=1)), 0.0)
```

- **Source:** the raw SimCC classification peak, per joint.
- **Definition:** the **minimum of the x-heatmap peak and the y-heatmap peak**. It measures how
  *sharp* the model's 2D belief is.
- **Per-joint:** yes, all 133 keypoints.
- **Range:** unbounded below 0 by construction (clamped); **max observed 0.9420** over 69,360
  joint-frames. Empirically inside [0,1], but that is an observation, not a guarantee.
- **Calibrated:** **no.** It is an activation magnitude, not a probability. The code says so
  explicitly (`rtmw3d_pose.py:128`).
- **Critically — `simcc_z` is excluded.** The depth axis output exists and is decoded into `z`, but
  contributes **nothing** to `conf`. Confidence is a purely 2D quantity.
- It therefore **cannot** describe the OAK depth sample, which is drawn later and independently in
  `backproject`.

**`confidence = P(3D landmark is correct)` is false, and nothing in the implementation claims it.**

---

## 4. Confidence vs correctness

**Confidence is a good 2D signal.** Median frame-to-frame 2D step, low- vs high-confidence:

| joint | conf<0.5 | conf>0.8 | ratio |
|---|---|---|---|
| R-shoulder | 9.13 px | 0.45 px | **20.25×** |
| R-knee | 10.37 px | 0.57 px | **18.07×** |
| R-ankle | 9.05 px | 0.50 px | **18.01×** |
| L-wrist | 3.90 px | 0.73 px | 5.31× |

Every joint 5.3×–20.3×. Confidence genuinely predicts 2D localisation stability.

**Confidence is a near-useless depth signal.** Rate at which the depth window is MIXED
(spans >150 mm), by confidence bucket:

| joint | conf<0.5 | conf 0.5–0.8 | conf>0.8 |
|---|---|---|---|
| **L-wrist** | 24.96% | 28.86% | **29.47%** ← higher at high confidence |
| **L-ankle** | 35.20% | 31.14% | **32.23%** ← flat |
| R-ankle | 31.09% | 30.05% | 22.74% |
| R-shoulder | 54.32% | 17.82% | 38.36% |
| L-knee | 34.66% | 13.37% | 12.46% |

For wrists and ankles, **high confidence provides no protection whatsoever** against a contaminated
depth sample. That is the informational gap.

Distribution (5780 frames/joint): median confidence 0.60–0.84; only **3.0–5.6%** of frames fall below
the shipping 0.3 gate; depth reported valid **95.8–98.5%** of frames.

---

## 5. Depth-quality findings

| joint | mean valid /25 | <6 valid | spread p50 | p95 | p99 | **MIXED %** |
|---|---|---|---|---|---|---|
| L-shoulder | 24.3 | 1.52% | 0 mm | 193 | 2122 | 26.09% |
| L-wrist | 24.3 | 1.70% | 0 mm | 505 | 2315 | **28.32%** |
| L-ankle | 24.3 | 1.91% | 0 mm | 388 | 842 | **31.69%** |
| R-ankle | 24.0 | 4.16% | 0 mm | 505 | 842 | 25.32% |
| L-knee | 24.5 | 1.70% | 0 mm | 217 | 590 | 14.13% |

**The `min_valid` check is not measuring anything useful.** Windows are essentially always full
(24.0–24.5 valid of 25) — so the check passes — while up to a third of them straddle a depth
discontinuity of more than 150 mm.

**Consequence, corrected to tail statistics** (the median `|dz|` is 0 mm in both arms, so a
median-ratio test is meaningless):

| joint | CLEAN p99 | MIXED p99 | ratio |
|---|---|---|---|
| L-elbow | 252 mm | 7579 mm | **30.08×** |
| R-shoulder | 236 mm | 7074 mm | **29.97×** |
| L-knee | 236 mm | 5826 mm | **24.69×** |
| **ALL JOINTS** | **379 mm** | **3945 mm** | **10.41×** |

**Alternative estimators** (temporal sd, mm, static block, same raw crops, n=107):

| joint | center | 5×5 p30 *(production)* | 5×5 min | 9×9 p30 |
|---|---|---|---|---|
| R-hip | 319.0 | 229.0 | 61.8 | **40.0** |
| L-knee | 56.3 | 47.6 | **34.1** | 35.8 |
| R-ankle | 75.6 | 75.4 | **53.7** | 79.7 |
| L-shoulder | 227.7 | 227.7 | 229.3 | 228.5 |
| R-elbow | 279.6 | 280.4 | 278.9 | 278.2 |

A clean split: for **hips/knees/ankles** the estimator matters a great deal (R-hip **8× steadier**
with a 9×9 window). For **shoulders/elbows/wrists** every estimator lands at 180–280 mm — the
sampler is not the problem there, the 2D landmark is moving between frames so the window samples a
different place each time.

---

## 6. RGB/depth timing findings

| capture | min | p50 | p95 | p99 | max |
|---|---|---|---|---|---|
| p13 | 12.07 | 12.08 | 12.08 | 12.09 | 21.25 |
| rollback | 12.18 | 12.19 | 12.19 | — | 21.14 |

Spread above the floor is **0.02 ms**. The ~12 ms is a **fixed systematic device offset, not
jitter** — the pairing logic (nearest-timestamp) is working and has essentially no variance to
exploit. `depthWaitMs` p99 = 0.00 ms; `queueDepth` p50 = 1.

**Conclusion:** the residual ~12 ms is **not** a material contributor to joint instability. At the
measured joint speeds (wrist p99 ≈ 1.35 m/s) 12 ms accounts for ≈16 mm — two orders of magnitude
below the 3945 mm p99 depth jumps seen on mixed windows. **Do not spend effort here.**

---

## 7. Joint-specific findings

Per-joint 3D speed, dt-normalised, and the depth share of that motion (p13 capture):

| joint | speed p50 | p95 | p99 | \|vz\| p99 | vz share of speed (p50) |
|---|---|---|---|---|---|
| wrist | 0.108 | 0.851 | 1.352 | 0.649 | 0.587 |
| ankle | 0.073 | 0.573 | 1.066 | 0.564 | 0.532 |
| elbow | 0.085 | 0.559 | 0.952 | 0.609 | 0.615 |
| **knee** | 0.080 | 0.423 | 0.797 | 0.499 | **0.762** |
| shoulder | 0.052 | 0.349 | 0.629 | 0.576 | 0.512 |
| hip | 0.013 | 0.151 | 0.327 | 0.291 | 0.577 |

**Worst three joints, and why:**

1. **L-ankle / R-ankle — 31.7% / 25.3% MIXED, highest contamination rate.** Thin limb, at the frame
   edge, and adjacent to the floor plane, which sits close in depth and defeats the "person is in
   front" assumption behind the 30th-percentile rule. R-ankle also has the worst `<6 valid` rate
   (4.16%).
2. **L-wrist — 28.3% MIXED, and confidence is *inversely* related to contamination.** Small, fast,
   frequently self-occluded against the torso. This is also the joint in the §8 case study.
3. **Shoulders/elbows — MIXED 15–26% and estimator-invariant (180–280 mm sd).** Distinct failure:
   the 2D landmark itself is unstable, so the depth window lands somewhere different each frame.
   No depth-sampling change will help these.

**Static-noise evidence (p13 block 1, subject demonstrably still — `hipZ` sd 1.3 mm, range 8 mm):**
R-elbow sd(z) **0.187 m**, L-knee sd(z) **0.175 m**, R-shoulder sd(z) **0.108 m** against trunk depth
sd of **0.0013 m**. Same sampler, same depth map, same instant: **134× worse on limbs than trunk.**

---

## 8. Confident-but-wrong case study

Block 10, sustained hand behind torso, **952 frames**:

| joint | conf p50 | conf min | <0.3 % | depth OK % | window spread p50 |
|---|---|---|---|---|---|
| **L-wrist (hidden)** | **0.575** | 0.037 | **2.10%** | **99.89%** | **0 mm** |
| R-wrist (visible) | 0.679 | 0.061 | 1.26% | 100.00% | 78 mm |
| L-elbow | 0.843 | 0.042 | 1.16% | 99.79% | 0 mm |

**Earliest stage at which the measurement became incorrect: the 2D landmark (`_decode`).**

The chain: the hand is physically behind the torso, so no correct 2D location exists in the image.
RTMW3D still emits a peak — on the torso — and because that peak is sharp, `conf` stays at 0.575.
`backproject` then samples the **torso**, a large flat surface: window full, spread 0 mm, depth
valid. The resulting 3D point is a *well-measured point on the wrong object*. P1-1 sees a smooth,
confident, depth-valid joint and correctly declines to intervene.

Notably the occluded wrist's window is **cleaner** than the visible wrist's (0 mm vs 78 mm spread).
**Depth-window quality is anti-correlated with correctness in this case.** No downstream test built
on depth or geometry can catch this, which is precisely why P1-4 could not.

---

## 9. High-confidence injected fault

| injection | conf before | conf after | window p30 before | after |
|---|---|---|---|---|
| drift 0.85 m | 0.764 | 0.806 | 2122 mm | 1929 mm |
| teleport 0.85 m | 0.803 | 0.806 | 1929 mm | 1929 mm |

Neither signal moved.

**Limitation of my own test, stated plainly:** the harness injects into `xyz_cam` *after*
`backproject`, so it is analytically guaranteed that neither 2D confidence nor the depth window can
observe it. This result therefore tests **downstream** detection (P1-1/P1-4), not upstream signal
quality. It confirms the layer that should catch a corrupted 3D point is the one that currently
cannot; it says nothing about whether an upstream fault would be visible. **A faithful upstream test
would perturb `uv` or the depth map, and has not been run.**

---

## 10. RGB-only vs fused

Attempted via bone-length decomposition on the archived captures: `L_full = √(dx²+dy²+dz²)` vs
`L_lateral = √(dx²+dy²)`. Result: **p99 ratio 1.02×** — no separation.

**This test is inconclusive, and I am not claiming otherwise.** Because `x = (u−cx)·z/fx`, the
lateral components are themselves scaled by each endpoint's depth, so a *common-mode* depth error is
invisible to the comparison. The static-block analysis in §7 substitutes for it and does separate the
two, because at rest all variation is noise by construction.

A true RGB-only comparison needs `replay_video.py`'s `uv/zrel/conf` cache against a recorded clip;
**no `.npz` cache exists and no clip was recorded**. Not established.

---

## 11. Candidate signals

| signal | correlation with bad measurement | false positives on clean motion | false negatives | cost | production-suitable? |
|---|---|---|---|---|---|
| **depth-window spread (MIXED)** | **strong — 10.4× p99 dz separation** | 15–32% base rate; must be a *quality weight*, not a veto | **blind to §8 hallucination** (spread 0 mm) | ~0 (already computed) | **most promising** |
| 2D landmark stability | strong — 5.3–20.3× vs confidence | needs a temporal window | misses static-wrong | low | promising, complements above |
| RTMW3D confidence | good for 2D, **near-zero for depth** | low | **fails §8: 0.575 while wrong** | free | keep for 2D only |
| depth validity (`min_valid`) | **almost none** — passes 98.1–98.5% | — | ~everything | free | **no** |
| RGB/depth timestamp delta | none — fixed 12 ms, 0.02 ms spread | — | — | free | **no** |
| 3D temporal residual | already used by P1-1 | tuned | misses slow drift | 0.175 ms | already in place |
| **segment geometry** | **refuted** — natural p99 1.791 > 1.50 fault | catastrophic (P1-4: 25.3%) | missed the live injection | 0.09 ms | **no — see ADR-P009** |
| joint-specific reliability | strong priors exist (ankles 31.7% vs knees 14.1% MIXED) | n/a | n/a | free | as a **prior**, not a gate |

---

## 12. Root cause

**Smallest defensible statement:**

> The pipeline computes a 3D joint as a 2D landmark combined with an independently sampled depth
> value, and carries a quality signal for the first factor only. `conf` is the SimCC x/y peak and
> excludes `simcc_z` by construction; the only depth check counts valid pixels (passing 98%+) and
> never tests whether they lie on one surface. Because 15–32% of 5×5 windows span a >150 mm
> discontinuity — producing 10.4× larger depth jumps at p99 — metric quantities built on depth
> (bone length above all) inherit a noise floor larger than the faults we need to detect.

And separately:

> When a limb is genuinely occluded the model relocates it onto the body rather than lowering
> confidence. The resulting point is well-measured on the wrong object, so confidence, depth
> validity, window homogeneity and temporal smoothness all report healthy simultaneously.

The first explains the P1-4 failure. The second is the more dangerous defect and is **not** addressed
by fixing the first.

---

## 13. Recommended next implementation — exactly ONE

**Make `sample_depth_mm` surface-aware, and have it return a per-joint depth-quality scalar.**

One function, `oak_depth.py:67`. Instead of a blind 30th percentile over "any valid pixel", cluster
the window's valid pixels and select the cluster consistent with the body, returning both the depth
and a quality value derived from the window's spread and cluster occupancy.

Why this one:
- It is the **only** signal in §11 with a large, measured, confidence-independent separation
  (10.4× p99, up to 30× per joint).
- The estimator study already shows headroom on exactly the joints that need it (R-hip **8×**,
  L-knee 1.6×) and correctly predicts no gain for shoulders/elbows.
- It fixes the **measurement**, rather than adding a downstream gate — which is what P1-4 proved to
  be the wrong shape of answer.
- Zero new heuristic layers; `min_valid` is replaced, not supplemented.

**Do not gate on the quality value in the same change.** Emit it, then validate it against the
avatar exactly as P1-4 was validated, before anything consumes it.

**This does not solve §8.** The hallucinated-limb case needs a different signal — most likely 2D
self-occlusion reasoning or a calibrated per-joint uncertainty — and should be a separate,
later investigation.

---

## 14. Rejected approaches

| approach | why the evidence rejects it |
|---|---|
| Any bone-length / segment threshold | ADR-P009. Natural p99 = 1.791 exceeds the 1.50 fault signal. The band is empty. |
| Tuning the 0.3 confidence gate | Only 3.0–5.6% of frames are below it, and the §8 wrist sat at 0.575 while physically wrong. Moving it trades misses for false drops without touching the defect. |
| Treating `conf` as depth or 3D quality | It is the SimCC x/y peak; `simcc_z` is excluded by construction. |
| Using `measured` / `min_valid` as a quality signal | Passes 98.1–98.5%; windows are 24.4/25 valid even when contaminated. |
| Chasing the ~12 ms RGB/depth offset | Fixed offset, 0.02 ms spread, ≈16 mm of motion — 240× smaller than the effect being explained. |
| Another smoothing stage | The failure is a wrong value, not a noisy one. Smoothing a hallucinated wrist yields a smooth hallucinated wrist. |
| Stacking confidence + length + angle + velocity gates | Explicitly proven harmful (P1-4: limb-holds 0.47% → 17.83%). |

---

## 15. Baseline integrity

| item | state |
|---|---|
| P1-1 tracker | **unchanged** — `--tracker` default True; tests **37/37** |
| P1-2 latest-frame | **unchanged** — `--latest-frame` default True |
| P1-3 PoseBuffer | **unchanged** — `poseInterpolationDelayMs: 40` (`Scenes/Bootstrap.unity:173`) |
| P1-4 recovery | **disabled** — `--recovery` default False |
| IK | **off** — `useIkDriver: 0` |
| `limbConfidenceThreshold` | **0.3**, unchanged |
| Unity | **47/47** EditMode, 0 compile errors; no Unity runtime file modified in this phase |
| New instrumentation | `--audit-log`, default **OFF** |

**Diagnostic cost (Part 14):** audit logging ON vs OFF — fps **21.18 vs 21.32** (−0.7%),
`trackerMs` p50 **0.176 vs 0.175 ms**, camera latency p50 **31.4 vs 31.2 ms**. Negligible, and off in
production regardless.

---

## 16. Final recommendation

The audit reached a root cause, so neither "more evidence" nor a hardware investigation is the next
step for the primary finding. **The defect is in software — `sample_depth_mm` — and §13 states the
single change.**

Two things do require more evidence before anything is built on them:

1. **The §8 hallucination path.** No ground truth was available; "physically incorrect" is inferred
   from the physical impossibility of the pose, which is sound for that case but is not a measured
   error. Quantifying it needs either a reference (mocap/checkerboard) or a deliberate
   occlusion protocol with known hand position.
2. **§9's upstream injection.** The current harness injects downstream of `backproject` and therefore
   cannot test upstream detectability. A `uv`/depth-level injection should be added before any
   claim is made about what upstream signals could catch.

Hardware is **not** implicated: `rgbDepthSyncMs` spread is 0.02 ms, `depthWaitMs` p99 is 0.00 ms,
packet loss is 0/5780, and depth returns 24.4/25 valid pixels. The sensor is delivering what it
should; the pipeline is choosing the wrong pixels out of it.

---

## Reproduce

```bash
python audit_f08_offline.py
python audit_f08_capture.py --dir pipeline_logs_f08
```

Instrumented capture: `python visual_p14_capture.py --pass rollback --audit`
(adds `--audit-log` and block 10, the sustained-occlusion block).

Artefacts: `python-sidecar~/pipeline_logs_f08/` — `audit_log.jsonl` (28 MB, 5780 frames),
`sender_log.jsonl`, `recv_log.jsonl`, `model_log.jsonl`, `holds_log.jsonl`, `blocks.json`.
