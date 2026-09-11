# F-13 — Hip-Line Depth Sign for Torso Yaw

**Verdict: NO-GO.** The hip-line depth ordering does not carry a usable torso-yaw sign. On 10 held
ground-truth blocks it is **correct on 4, wrong on 2, and produces no signal at all on 4** — including
**both +90° blocks**, where the depth difference is never non-zero on a single frame. The hypothetical
`sign(hipΔz)·|yaw2D|` hybrid scores **MAE 72.46°** against **8.34°** for the unsigned magnitude alone:
attaching this sign makes the estimate eight times worse than not signing it.

**One incidental finding is more consequential than the verdict**, and it is not a hip result. The same
harness, run on the **shoulder** line for the mandated comparison (§10), gets the sign right on
**9 of 10 blocks with 0 wrong**. F-10 rejected "depth sign" at 78.49 % — but F-10 tested
`sign(yaw3D)`, an `atan2` composition that **wraps** when the shoulder line goes edge-on, not the
depth difference itself. **The sign appears to have been destroyed by the composition, not missing
from the measurement.** That is a lead, not a result: it rests on 10 held blocks from one subject at
one distance in one session. It is written up in §10 and §17 and recommended as F-14. **No production
code was changed and no V6 was implemented.**

---

## 1. Objective

Determine whether the hip-line stereo depth relationship can supply a reliable left/right **sign** to
pair with the already-validated 2-D shoulder-foreshortening magnitude, giving

```text
yawCandidate = sign(hipDepthSignal) · abs(yaw2D)
```

Offline evidence only. The output is a decision, not an estimator.

---

## 2. Evidence Used

| source | role |
|---|---|
| `pipeline_logs_f10_near/` | F-10 ground-truth capture, 27,363 frames → **2,478 labelled** |
| `pipeline_logs_f11/` | F-11 ground-truth capture, 22,380 frames → **1,457 labelled** |
| `oak_v4_evidence/f10_gt_marks_near_RECOVERED.json` | F-10 block windows — **RECOVERED, see §19** |
| `oak_v4_evidence/f10_gt_marks_near.json` | F-11 block windows (the name is a trap — see §19) |
| `docs/F09…F12`, `UNITY_TORSO_V5…`, `decisions.md`, `roadmap.md` | prior evidence, read before starting |

Both captures are at ~1.5 m. **No new human capture was requested**; the existing recordings contain
the required poses.

**Scored population.** 1,320 turned frames and 1,000 true-zero frames. **561 twist frames are excluded
from every headline number**: `tw_sh_p45`/`tw_sh_m45` turn the shoulders while the hips stay square, so
their heading label is not hip truth, and the subject reported confusion during that protocol in F-10.
They are reported separately in §14.

Blocks are assigned hip truth by what they constrain **the hips** to do, not by their heading label:

| block | hips are | use |
|---|---|---|
| `h_p45` `h_p90` `h_m45` `h_m90` | turned to the heading | scored |
| `dc_body45R_face0` `dc_body45L_face0` | turned ±45 (face on camera) | scored |
| `h_0` `h_0b` `h_0c` `dc_body0_head45R` `dc_body0_head45L` | square | zero case |
| `tw_*` | ambiguous / compromised | excluded |

---

## 3. Hip Landmark Mapping

Confirmed from the sidecar source, not assumed:

| layer | left hip | right hip | evidence |
|---|---|---|---|
| COCO-WholeBody (model output) | **11** | **12** | `wholebody_udp_sender.py:84` `AUDIT_JOINTS` |
| JointId (33-slot UDP payload) | **23** | **24** | `wholebody_udp_sender.py:155` `for h in (23, 24): # hips` |
| audit log key | `j["L-hip"]` | `j["R-hip"]` | same tuple |
| sender log | `hip[0]` | `hip[1]` | `f09_torso_features.py:129` |

The brief's `leftHip = 23 / rightHip = 24` is the **JointId** layer and is correct; the audit log this
analysis reads is indexed at the **COCO-WholeBody** layer, 11/12. Both name the same two joints.

---

## 4. Coordinate / Signal Definition

The audit record is written at `wholebody_udp_sender.py:460–509`, which is **before** the one-euro /
depth-outlier smoother (`:517`) and before hip-centring (`:540`). So:

| field | what it is | frame |
|---|---|---|
| `j["L-hip"]["p30"]`, `["p50"]` | percentiles of the valid pixels in a **5×5 depth window** at the joint's pixel | **raw stereo depth, mm, camera Z, unsmoothed** |
| `j["L-hip"]["ctr"]` | the single centre pixel's depth | raw, mm |
| `sender.hip[0][2]` | emitted landmark Z | **after** one-euro smoothing, hip-relative metres |

`kwin = 5` (`:190`), and the hips are ~65 px apart in RGB, so **the two windows do not overlap** —
window overlap is not a confound.

The emitted hip landmarks are exactly antisymmetric (`hipL = −hipR`) because they are hip-centred:
`p = xyz_cam[i] − mid_hip` with `mid_hip = (L+R)/2`. No information is lost by that; the difference
`hipL.z − hipR.z` is preserved and is carried as candidate C.

**`--flatten-trunk` was OFF** for both captures (Milestone-2 default). Had it been on, `lm[23|24][2]`
would be identically zero (`:155–157`) and candidate C would be vacuous. The raw audit depths are
unaffected either way.

---

## 5. Candidate Signals

| id | signal | definition |
|---|---|---|
| **A** | `dzP30` | `p30(L-hip) − p30(R-hip)`, mm — raw |
| **A2** | `dzP50` | `p50(L-hip) − p50(R-hip)`, mm — raw |
| **B** | `hipAng` | `atan2(Δz, horizontalSep)` in degrees; `horizontalSep = uSpanHip · Z / fx`, guarded to > 20 mm |
| **C** | `dzEmit` | `(hip[0].z − hip[1].z)·1000`, mm — post-smoothing |
| **D** | `dzCtr` | `ctr(L-hip) − ctr(R-hip)`, mm — centre pixel only, no window statistics |

`fx` is not logged; it is recovered from the pipeline's own back-projection as
`fx = uSpanHip · Z / |hipL.x − hipR.x|`, giving **288.5 px (F-10)** and **291.1 px (F-11)**.

**A, A2, B and D are the same one bit wherever all are defined.** B divides A by a strictly positive
separation, which cannot change a sign. They differ only in which depth statistic is read, and once a
threshold is applied (§11). This is stated up front so the four rows in §6 are not mistaken for four
independent chances.

**Sign convention is inferred empirically per candidate** (majority vote over the scored turned
frames), never assumed. All five inferred to **−1**.

A depth difference of **exactly zero carries no sign** and is treated as a third state, `undefined` —
not silently folded into "negative". Doing that was a real bug in the first run of this analysis: it
made an all-zero block look like a perfectly stable sign with zero flips.

---

## 6. Static Ground-Truth Results

Accuracy **conditioned on a sign existing**, with the coverage that conditioning hides:

| id | signal | defined | coverage | overall | positive | negative | +90 | −90 | **all frames** |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|
| A | `dzP30` | 434 | 32.9 % | 70.51 % | 63.21 % | 86.67 % | **none** | 10.00 % | **23.18 %** |
| A2 | `dzP50` | 435 | 33.0 % | 72.87 % | 61.13 % | 91.18 % | **none** | 11.76 % | **24.02 %** |
| B | `hipAng` | 434 | 36.0 % | 70.51 % | 63.21 % | 86.67 % | **none** | 10.00 % | **25.35 %** |
| C | `dzEmit` | 745 | 56.4 % | 71.95 % | 65.08 % | 79.02 % | 0.00 % | 24.51 % | **40.61 %** |
| D | `dzCtr` | 443 | 33.6 % | 74.04 % | 62.55 % | 91.48 % | 100.00 % | 16.67 % | **24.87 %** |

* **coverage** — turned frames on which the candidate produces any sign at all.
* **overall** — accuracy given a sign exists. Not a shippable number.
* **all frames** — accuracy over every turned frame, an undefined sign counted wrong, because a
  production estimator must emit something every frame. **This is the honest column, and its best
  value is 40.61 %.**

Per-heading, where the pooled table hides empty cells:

| signal | +45 cov / acc | −45 cov / acc | +90 cov / acc | −90 cov / acc |
|---|---|---|---|---|
| HIP raw `dzP30` | 78.7 % / 63.21 % | 31.0 % / 100.00 % | **0.0 % / none** | 6.8 % / **10.00 %** |
| HIP emitted `dzEmit` | 96.6 % / 67.03 % | 71.4 % / 100.00 % | 4.0 % / **0.00 %** | 34.7 % / 24.51 % |

### The block is the honest unit of evidence

Inside one held pose consecutive frames are near-duplicates, so 1,320 frames are **not** 1,320
independent observations. Per held block, majority sign:

| capture | block | true | HIP raw Δz | SHOULDER emitted Δz |
|---|---|--:|---|---|
| F-10 | `h_m45` | −45 | **no signal** | correct (106/106) |
| F-10 | `h_m90` | −90 | **no signal** | **no signal** |
| F-10 | `h_p45` | +45 | correct (41/111) | correct (111/111) |
| F-10 | `h_p90` | +90 | **no signal** | correct (140/140) |
| F-11 | `dc_body45L_face0` | −45 | correct (1/150) | correct (150/150) |
| F-11 | `dc_body45R_face0` | +45 | correct (148/148) | correct (148/148) |
| F-11 | `h_m45` | −45 | correct (114/115) | correct (115/115) |
| F-11 | `h_m90` | −90 | **WRONG** (18/147) | correct (131/147) |
| F-11 | `h_p45` | +45 | **WRONG** (110/121) | correct (121/121) |
| F-11 | `h_p90` | +90 | **no signal** | correct (135/135) |
| | | | **4 ✓ / 2 ✗ / 4 none** | **9 ✓ / 0 ✗ / 1 none** |

Two of the four hip "correct" blocks are correct on a razor-thin majority (41/111 and 1/150 frames
carrying the winning sign). **The hip signal is a coin flip that abstains more often than it votes.**

---

## 7. Raw Distribution Analysis

Raw p30 window depth, mm:

| population | n | zL p50 | zR p50 | Δz p10 | Δz p50 | Δz p90 | Δz sd | \|Δz\| p50 |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| true 0 | 1000 | 1326 | 1326 | 0 | 0 | 0 | 22.8 | 0 |
| true +45 | 380 | 1326 | 1326 | −147 | **0** | +147 | 102.6 | 78 |
| true −45 | 371 | 1326 | 1248 | 0 | **0** | +78 | 44.0 | 0 |
| true +90 | 275 | 1415 | 1415 | 0 | **0** | 0 | **0.0** | 0 |
| true −90 | 294 | 1326 | 1326 | 0 | **0** | 0 | 19.0 | 0 |

The +45 row is the finding in one line: the distribution is **symmetric about zero**
(p10 = −147, p50 = 0, p90 = +147). A pose with an unambiguous physical direction produces a depth
difference with no preferred direction.

**Overlap between mirrored poses** (rank AUC; 0.500 = indistinguishable):

| pair | AUC | separability |
|---|--:|--:|
| +45 vs −45 | 0.336 | 66.4 % |
| **+90 vs −90** | **0.527** | **52.7 %** — chance |

**Quantisation.** Δz is **exactly 0 mm on 886 / 1,320 turned frames (67.1 %)**. The non-zero levels are
not continuous: 16, 62, 78, 81, 101, 131, 133, 147, 147, 197 mm.

### Why — the mechanism

**(i) Depth is quantised to integer disparity.** The levels actually used near the hips are
1179, 1248, 1326, 1415, 1516, 1632, 1768 mm. Fitting `z·d` over them gives **f·B = 21,216 mm·px**,
constant to < 1 %. At the subject's 1326 mm the disparity is **d = 16**, and the neighbouring rungs are
1248 mm and 1414 mm — **one step is 78–88 mm**.

**(ii) The hips are too narrow to clear one step.** Measured hip separation is **130 mm**, so a turn of
θ puts them `130·sin θ` mm apart:

| θ | true Δz | in disparity steps |
|--:|--:|--:|
| 15° | 33.7 mm | 0.41 |
| 30° | 65.2 mm | 0.78 |
| **45°** | **92.2 mm** | **1.11** |
| 60° | 112.9 mm | 1.36 |
| 90° | 130.4 mm | 1.57 |

A 45° turn is worth **1.1 quantisation steps**. The sign is a comparison of two numbers that each move
in 83 mm jumps, so both hips land on the same rung most of the time — hence the 67.1 % of exact zeros.

**(iii) At ±90° the far hip is occluded by the near one.**

| heading | n | uSpan px | \|Δz\| p50 | Δz sd | measured | conf p50 |
|---|--:|--:|--:|--:|--:|--:|
| 0 | 1000 | 43.9 | 0.0 | 22.8 | 100 % | 0.655 |
| +45 | 380 | 30.1 | 78.0 | 102.6 | 100 % | 0.621 |
| −45 | 371 | 30.8 | 0.0 | 44.0 | 100 % | 0.635 |
| **+90** | 275 | **14.6** | 0.0 | **0.0** | 100 % | 0.491 |
| **−90** | 294 | **10.6** | 0.0 | 19.0 | 100 % | 0.461 |

At ±90° the hips project 10–15 px apart and the far one is behind the near one, so both 5×5 windows
land on the same body surface. Δz collapses to zero **with zero spread** — not noisy, absent. The pose
that most needs a sign is the pose that cannot produce one, and the depth pipeline still reports the
joints as `measured` 100 % of the time.

---

## 8. Zero / Frontal Safety Analysis

| capture | block | n | \|Δz\| p50 | \|Δz\| p95 | defined | flips/s |
|---|---|--:|--:|--:|--:|--:|
| F-10 | `h_0` | 139 | 0.0 | 0.0 | 0.0 % | 0.00 |
| F-10 | `h_0b` | 111 | 0.0 | 0.0 | 0.0 % | 0.00 |
| F-10 | `h_0c` | 109 | 0.0 | 101.0 | 16.5 % | 0.00 |
| F-11 | `h_0` | 136 | 0.0 | 78.0 | 19.9 % | 0.00 |
| F-11 | `h_0b` | 90 | 62.4 | 78.0 | **56.7 %** | 0.00 |
| F-11 | `h_0c` | 117 | 0.0 | 0.0 | 0.0 % | 0.00 |
| F-11 | `dc_body0_head45R` | 147 | 0.0 | 0.0 | 0.0 % | 0.00 |
| F-11 | `dc_body0_head45L` | 151 | 0.0 | 0.0 | 0.0 % | 0.00 |

Pooled true-zero |Δz|: p50 0.0, p90 0.0, p95 78.0, max 101.0 mm. **9.6 %** of true-zero frames emit a
sign, and **7.5 %** show |Δz| ≥ one full disparity step. One block (`F-11 h_0b`) emits a confident sign
on **56.7 %** of frames with the torso square.

The threshold basis is stated rather than invented: **78 mm is one measured disparity step at the
subject's distance** (§7 (i)), not a tuned constant.

The 0.00 flips/s is not stability — it is the near-total absence of a defined sign. This is exactly the
artefact that the three-valued sign in §5 was introduced to expose.

---

## 9. Dynamic Motion Analysis

No instantaneous ground truth exists inside the motion blocks, so no dynamic accuracy is reported.
Behaviour only:

| block | n | flips | flips/s | longest run | defined | \|Δz\| p50 |
|---|--:|--:|--:|--:|--:|--:|
| `m_slow` | 172 | 3 | 0.50 | 8 | 12.8 % | 0.0 |
| `m_normal` | 137 | 6 | 1.21 | 5 | 17.5 % | 0.0 |
| `m_fast` | 130 | 11 | **2.22** | 10 | 32.3 % | 0.0 |
| `m_rev_lr` | 113 | 5 | 1.00 | 6 | 15.9 % | 0.0 |
| `m_rev_rl` | 114 | 5 | 1.01 | 16 | 28.9 % | 0.0 |
| `m_180` | 139 | 2 | 0.33 | 6 | 9.4 % | 0.0 |
| `m_turn_arms` | 124 | 5 | 1.00 | 12 | 20.2 % | 0.0 |
| `m_turn_still` | 124 | 1 | 0.20 | 18 | 15.3 % | 0.0 |

The sign is **defined on 9–32 % of moving frames** and flips up to **2.22 times per second** against a
subject who physically reverses at most about 0.5 times per second. The longest unbroken run of one
sign is 5–18 frames — a fifth of a second. This behaves like noisy depth ordering, not a direction
indicator.

---

## 10. Comparison With Shoulder Depth Sign

This is the mandated comparison, and it is where the significant result is.

| source | defined | coverage | overall | positive | negative | +90 | −90 | all frames |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| HIP raw Δz (p30) | 434 | 32.9 % | 70.51 % | 63.21 % | 86.67 % | none | 10.00 % | 23.18 % |
| HIP emitted Δz | 745 | 56.4 % | 71.95 % | 65.08 % | 79.02 % | 0.00 % | 24.51 % | 40.61 % |
| **SHOULDER raw Δz (p30)** | 854 | 64.7 % | 99.88 % | 99.79 % | 100.00 % | 98.92 % | 100.00 % | **64.62 %** |
| **SHOULDER emitted Δz** | 1157 | 87.7 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % | **87.65 %** |

Block level: **shoulder 9 correct, 0 wrong, 1 no-signal** out of 10.

**The hip line is not a better directional source than the shoulders. It is dramatically worse**, and
the reason is geometric, not incidental: the shoulders are ~3× wider than the hips, so the same turn
produces ~3× the depth difference against the same 83 mm quantisation step. Choosing the hips traded
away the only thing that made the measurement work.

### Why F-10 rejected a signal that measures 100 % here

F-10's 78.49 % was **`sign(yaw3D)`**, not the depth difference. `yaw3D` is an `atan2(Δx, Δz)`
composition over the shoulder line, and it **wraps** when that line goes edge-on:

| \|heading\| | shoulder pixel span |
|---|--:|
| ±45° | 40.5 px |
| ±90° | **9.6 px** |

At ±90° the shoulder line is 9.6 px across, `Δx → 0`, and `atan2` becomes unstable and wraps through
±180°. F-10's own report already recorded this without naming the cause: *"excluding the ±180° wrap
(|yaw3D| ≤ 150): 89.52 %"*, and *"`|heading| ≥ 90°`: 63.45 %"*. **A raw depth difference does not
wrap.** The evidence is consistent with the sign having been destroyed by the composition rather than
missing from the measurement.

### What the shoulder signal does *not* yet have

| check | result |
|---|---|
| motion flips/s | 0.40 – **1.61** (defined 100 %) — still above the ~0.5/s physical bound |
| true-zero behaviour | emits a **confident, non-flipping** sign; \|Δz\| p50 up to **136 mm**, comparable to a real 45° turn |
| −90° raw coverage | **3.7 %** — only the *smoothed* variant has coverage there |
| independent evidence | **10 held blocks, 1 subject, 1 distance, 1 session** |

The true-zero behaviour is not automatically a defect for a **sign-only** use: `sign · |yaw2D|` is near
zero when the magnitude is near zero, so an arbitrary sign at a square torso costs little. But the
magnitude of this signal plainly carries no yaw information, and **it has not been verified that the
product stays small in practice.** That verification is F-14's job, not a claim of this report.

---

## 11. Threshold / Coverage Sweep

On |Δz| (p30, mm). The threshold ladder is meaningful only in units of the 78–88 mm disparity step.

| \|Δz\| > | n kept | coverage | overall | positive | negative | +90 / −90 | 0° defined |
|--:|--:|--:|--:|--:|--:|---|--:|
| 0 | 434 | 32.9 % | 70.51 % | 63.21 % | 86.67 % | none / 10.0 | 9.6 % |
| 10 | 434 | 32.9 % | 70.51 % | 63.21 % | 86.67 % | none / 10.0 | 9.6 % |
| 20 | 430 | 32.6 % | 70.70 % | 63.21 % | 87.79 % | none / 11.1 | 9.6 % |
| 30 | 430 | 32.6 % | 70.70 % | 63.21 % | 87.79 % | none / 11.1 | 9.2 % |
| 50 | 430 | 32.6 % | 70.70 % | 63.21 % | 87.79 % | none / 11.1 | 9.1 % |
| 75 | 427 | 32.3 % | 71.19 % | 63.85 % | 87.79 % | none / 11.1 | 7.5 % |
| 100 | 157 | 11.9 % | 64.33 % | 61.64 % | 100.00 % | none / none | 1.2 % |
| 150 | 22 | **1.7 %** | **4.55 %** | 0.00 % | 100.00 % | none / none | 0.0 % |

**Raising the threshold does not buy accuracy.** The best "overall" anywhere in the sweep is 71.19 % at
75 mm, on 32.3 % coverage — and at 150 mm accuracy *collapses to 4.55 %*, because the only frames
surviving are the ones where quantisation error, not rotation, produced a large difference. There is no
accuracy-for-coverage trade to make here: **both are bad everywhere, and above 100 mm they are
anti-correlated with truth.** No threshold is recommended, because none works.

---

## 12. Hypothetical Hip-Sign Hybrid

`sign(hipΔz) · |yaw2D|` on the static turned population. **Not implemented.**

| estimator | MAE | RMSE | p50 | p95 | max | wrong sign |
|---|--:|--:|--:|--:|--:|--:|
| hip-sign hybrid (hold last sign on Δz = 0) | **72.46°** | 100.48° | 83.50° | 178.85° | 180.00° | **50.61 %** |
| hip-sign hybrid (no hold) | **69.66°** | 96.45° | 18.55° | 178.85° | 180.00° | 49.85 % |
| `yaw2D` magnitude alone | **8.34°** | 9.96° | 7.29° | 18.55° | 22.76° | n/a |

The "hold" variant is the most favourable reading a shipping estimator could take, and it is the worse
of the two. **The wrong-sign rate is 50 %** — a fair coin.

Against the published references (all on their own static populations):

| estimator | MAE |
|---|--:|
| production `yaw3D`, signed | 61.08° |
| **hip-sign hybrid (F-13)** | **72.46°** |
| F-11 face-sign hybrid | 31.60° |
| F-10 depth-sign hybrid | 13.09° |
| F-12 temporal hybrid | 9.92° (unstable — 75-point swing on one parameter step) |
| `yaw2D` magnitude alone | 5.90° (F-10) / **8.34°** (this population) |

The hip-sign hybrid is **worse than the production estimator it would replace**, and worse than
emitting no sign at all. The 8.34° vs F-10's 5.90° for the magnitude is a population difference — this
report pools F-10 and F-11 and includes the `dc_*` blocks — not a regression.

---

## 13. >90° Limitation

`|yaw2D| = acos(clamp(uSpan·Z / K))` is bounded to **[0°, 90°]**. Measured on the `m_180` block:
p50 18.2°, p95 74.2°, **max 83.7°** over 139 frames — it never exceeds 90°, as the algebra requires.

A 120° turn and a 60° turn foreshorten the shoulders identically. **No sign source can separate them**,
because the ambiguity is in the magnitude, not the sign. If the shoulder sign of §10 is confirmed, it
solves the **sign half only**; a torso that turns past 90° still needs a different magnitude
representation. These are two separate problems and must not be conflated.

---

## 14. Failure Cases

The falsification tests, all of which the hypothesis was supposed to survive:

| case | test | result |
|---|---|---|
| **1** | true +45° but Δz is 0 or points the wrong way | **191/380 = 50.3 %** — fires |
| 2 | true −45°, wrong sign, hip confidence ≥ 0.60 | 0/371 = 0.0 % — does not fire |
| **3** | true 0° but \|Δz\| ≥ one disparity step | **75/1000 = 7.5 %** — fires |
| **4** | +90° and −90° give the same ordering | **+90 defines a sign on 0 frames**, −90 on 20, of 569 — fires completely |
| **5** | sign changes with no physical turn | **38 flips over 42 s = 0.91/s** — fires |

Case 4 is the decisive one and it is stronger than "the same ordering": at +90° there is **no ordering
at all** on any frame, so +90° and −90° are not merely confusable, they are **literally
indistinguishable** — neither produces a bit to compare.

Case 2 not firing is not a point in the hypothesis's favour: at −45° the signal is defined on only
31.0 % of frames, so there are few confident frames available to be wrong.

**Twist blocks** (excluded from all headline numbers, reported for completeness):

| block | n | heading label | Δz p50 | \|Δz\| p50 |
|---|--:|---|--:|--:|
| `tw_rigid_p45` | 102 | +45 | 0.0 | 0.0 |
| `tw_sh_p45` | 112 | +45 | 0.0 | 0.0 |
| `tw_sh_m45` | 114 | −45 | 0.0 | 0.0 |
| `tw_opp_a` | 116 | — | 0.0 | 0.0 |
| `tw_opp_b` | 112 | — | 0.0 | 0.0 |

Every twist block produces a hip Δz median of exactly zero, including the rigid ±45 turn.

---

## 15. Safety Analysis

**Is a wrong hip sign associated with low confidence?** Forensic only — no gate is proposed.

| population | n | conf p50 | conf p10 | depth-q p50 | dsd p50 |
|---|--:|--:|--:|--:|--:|
| sign CORRECT | 306 | 0.625 | 0.608 | 1.000 | 28.9 |
| sign WRONG | 128 | 0.610 | 0.450 | 0.690 | 34.5 |

**81 of 128 wrong-sign frames (63.3 %) carry hip confidence ≥ 0.60** — the sign is wrong while the
landmark looks healthy. There is a mild association (depth-quality p50 1.000 → 0.690, dsd 28.9 → 34.5),
but it is nowhere near separating. **Confidence is not validity**, consistent with F-08 and F-09.

At ±90° the depth pipeline reports both hips as `measured` on **100 %** of frames while emitting a
depth difference of exactly zero with zero spread. **A confidence gate would not catch this**, because
nothing in the measurement reports itself as unhealthy. Any future sign gate must be geometric
(is the line resolvable?), not confidence-based.

---

## 16. Verdict

# NO-GO

Against the stated acceptance criteria:

| # | criterion | required | measured | |
|--:|---|---|---|---|
| 1 | overall sign accuracy | ≥ 95 % | 70.51 % conditioned / **23.18 % over all frames** | ✗ |
| 2 | positive-turn accuracy | ≥ 95 % | 63.21 % | ✗ |
| 3 | negative-turn accuracy | ≥ 95 % | 86.67 % (on 31 % coverage) | ✗ |
| 4 | +90° accuracy | ≥ 95 % | **no signal exists** | ✗ |
| 5 | −90° accuracy | ≥ 95 % | **10.00 %** | ✗ |
| 6 | no severe left/right asymmetry | — | +45 63.21 % vs −45 100 %; +90 none vs −90 10 % | ✗ |
| 7 | 0° does not generate frequent confident sign | — | 9.6 % overall, **56.7 % in one block** | ✗ |
| 8 | sign useful during movement | — | defined 9–32 %, up to 2.22 flips/s | ✗ |
| 9 | materially improves the signed hybrid | — | **MAE 72.46° vs 8.34° unsigned** | ✗ |
| 10 | improvement not from discarding frames | — | best coverage 32.9 %, and accuracy *falls* with threshold | ✗ |

**Ten of ten criteria fail.** The NO-GO conditions are met on every count: < 90 % overall, one direction
near chance, ±90° arbitrary, frequent confident sign at true 0°, behaviour indistinguishable from noisy
depth ordering, and no better than the shoulder-depth sign it was meant to improve on — it is
**far worse**.

---

## 17. Recommendation

**1. Close the hip-line depth sign. Do not revisit it at this baseline.** The failure is geometric and
quantitative: a 130 mm hip separation against an 83 mm disparity step gives 1.1 steps at 45°, and zero
steps at ±90° because of self-occlusion. No filtering, threshold or gate can recover a bit the sensor
never resolved. It would only become viable with a materially different depth front end (sub-pixel
disparity, wider baseline, or a much closer subject) — and even then the shoulders would still be the
better line.

**2. Do NOT yet apply the brief's final rule.** That rule — *stop searching for ways to sign `yaw2D`,
redesign the representation* — is predicated on **all** cheap sign sources being unreliable. §10 puts
that premise in doubt: the shoulder depth difference got the sign right on **9 of 10 blocks with 0
wrong**, and F-10's rejection is explained by `atan2` wrap in the `yaw3D` composition rather than by
the depth. Redesigning the representation before checking this would discard a signal that may already
be present in the pipeline.

**3. Next task — F-14: validate the shoulder depth-difference sign properly.** Offline first, on
existing captures, with the specific weaknesses §10 lists as the test targets:

* dynamic behaviour (flip rate is 0.40–1.61/s, above the physical bound);
* the true-zero regime — confirm `sign · |yaw2D|` actually stays small when the sign is arbitrary;
* the −90° coverage gap (3.7 % raw; only the smoothed variant covers it) and how much of the smoothed
  variant's coverage is real information versus one-euro interpolation across disparity rungs;
* whether 9/10 blocks from one subject at one distance survives a second subject or distance — **this
  is the point where a new human capture is justified**, and not before.

**4. Keep the two problems separate.** Even a perfect sign leaves `|yaw2D|` saturated at 90° (§13).
Sign and >90° magnitude are independent work items.

**5. Nothing ships from F-13.** No V6, no threshold, no gate, no production change.

---

## 18. Files Changed

**Production files changed: NONE.**

Unchanged and untouched: `KalidokitControlRigDriver`, `TrunkGate`, `yaw3D`, `yaw2D`, `DampYaw`, torso
weights, `ArmAimSolver`, Arm V2, P0 / P1-1 / P1-2 / P1-3, scene defaults, production thresholds, UDP
payload semantics, shipping configuration.

**Diagnostic logging changed: NONE.** F-11 already added the face keypoints to the audit record; the
hip depth this analysis needs was already logged by the F-08 audit (`AUDIT_JOINTS`). No new logging was
required, so `wholebody_udp_sender.py` was not touched.

Added — offline analysis only:

| file | role |
|---|---|
| `python-sidecar~/f13_hip_depth_sign.py` | the analysis |
| `python-sidecar~/oak_v4_evidence/f13_hip_sign.txt` | its full output, 277 lines |
| `docs/F13_HIP_DEPTH_SIGN_TORSO_YAW_2026-09-10.md` | this report |

Reproduce with `python f13_hip_depth_sign.py` from `python-sidecar~/`.

---

## 19. Honest Limits

1. **The F-10 block windows are RECOVERED, not original.** The F-11 capture was run with
   `--distance near` and overwrote `f10_gt_marks_near.json`. The windows were reconstructed by
   `f12_recover_f10_marks.py` (offset 100.75 s, stretch 1.000, all seven static blocks within 3.8° of
   the medians published in the F-10 report) and the file is flagged `"RECOVERED": true`. **Every F-10
   number here inherits that reconstruction.** F-11's windows are original.

2. **The two marks files are confusingly named.** `f10_gt_marks_near.json` describes the **F-11**
   capture; the F-10 windows are in `..._RECOVERED.json`. This is documented in
   `oak_v4_evidence/README.md` and both files are backed up.

3. **One subject, one distance (~1.5 m), one session, two captures on the same day.**
   **Distance sensitivity: NOT MEASURED** — only "near" exists, and per the brief no new capture was
   requested to fill the table. The archived logs contain no second-distance ground-truth capture.

4. **The block, not the frame, is the unit of evidence.** Ten held blocks. Every frame-level percentage
   in this report is over ~130 near-duplicate frames per block and overstates the independent evidence
   accordingly. This cuts against the shoulder finding in §10 as much as it does against the hips —
   9/10 blocks is a strong hint, not a validated result.

5. **The shoulder result is incidental and unreplicated.** It came out of the mandated comparison, was
   not the object of this task, and has not been tested for the failure modes §10 lists. It must not be
   implemented on the strength of this report.

6. **No dynamic accuracy is claimed anywhere.** The motion blocks carry no instantaneous ground truth,
   so §9 reports behaviour only. A low flip rate there is not evidence of correctness.

7. **`fx` is recovered, not logged** (288.5 / 291.1 px). It is self-consistent with the pipeline's own
   back-projection, which is all candidate B requires — and since B's denominator is strictly positive,
   an error in `fx` could not change any sign or any verdict in this report.

8. **The `tw_*` blocks are compromised**, as recorded in F-10; nothing is concluded from them.

9. **A methodological error was made and corrected mid-analysis.** The first run treated a Δz of
   exactly zero as a negative sign, which made all-zero blocks appear as perfectly stable signs with
   zero flips, and reported a 70.51 % accuracy while silently dropping 67 % of frames. The three-valued
   sign in §5 and the coverage columns throughout are the fix. Any earlier numbers from this task are
   superseded by this document.
