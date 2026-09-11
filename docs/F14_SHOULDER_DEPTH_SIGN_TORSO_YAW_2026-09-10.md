# F-14 — Shoulder Depth-Difference Sign for Torso Yaw

**Verdict: CONDITIONAL.** The central hypothesis is **refuted**, but the practical sign question comes
back positive — via a cheaper route than the one this task set out to validate.

**The hypothesis fails.** F-14 asked whether raw shoulder Δz carries a sign *independent of* the
`atan2(Δx, Δz)` composition that F-10 rejected. It does not. Over 854 frames where raw Δz is defined,
it is right where `yaw3D` is right and **rescues exactly 0 frames** where `yaw3D` is wrong. Where
`yaw3D` wraps, raw Δz is **also silent on 89.5 %** of those frames. It is not an independent signal —
it is the same information, minus coverage.

**The decisive test.** Section 14 of the brief compares a *held* depth sign against an *unheld*
`yaw3D`, which is not a fair test — F-10 itself proposed a wrap guard. Scoring that guard on the same
frames:

| estimator | MAE | RMSE | wrong sign |
|---|--:|--:|--:|
| `sign(EMITTED Δz)`, hold | **8.34°** | **9.96°** | **0.00 %** |
| `sign(yaw3D)` + F-10 wrap guard | **8.34°** | **9.96°** | **0.00 %** |
| perfect-sign ceiling (`\|yaw2D\|`) | **8.34°** | **9.96°** | — |

Identical to the decimal, on all three populations tested, and both already at the theoretical ceiling.
**No new sign source is needed. The existing production signal plus a wrap guard is equivalent.**

**Central answer: NO** — the raw depth does not contain a sign independent of the failing composition.
**But the sign is nonetheless solvable on this data**, and the correct V6 is far smaller than the one
the brief anticipated: add a wrap guard, do not add a signal.

**Why CONDITIONAL and not PASS:** raw Δz coverage collapses to **3.7 % at −90°**, and the two captures
differ by **27 points** on the same estimator (70.83 % vs 98.04 %).

> ⚠️ **See the CORRECTION at the end of this report.** That 27-point figure is frame-weighted over two
> different block mixes and overstates the problem. Controlling for heading, +45°, −45° and +90° are
> **100.00 % in both captures** and *all* the variance is **one −90° block** (0.0 % vs 89.1 %).
> The remaining uncertainty is the **−90° pose**, not session dependence. Distance and landmark
> confidence were tested as causes and refuted.

**No production code was changed and no V6 was implemented.**

---

## 1. Objective

Determine whether the raw shoulder depth difference can supply the left/right sign of torso yaw, with
the validated 2-D foreshortening supplying the magnitude:

```text
yawCandidate = sign(rawShoulderDepthDelta) · abs(yaw2D)
```

Polarity inferred from ground truth, never assumed. Offline evidence only.

---

## 2. Prior Evidence

| task | result | what it changed |
|---|---|---|
| F-09 | depth yaw is under-resolved; 1 step = 6.3–11.3° of yaw | do not build a confidence gate |
| F-10 | 2-D magnitude **validated** (MAE 5.90°); `sign(yaw3D)` **78.49 %**, 50.5 % on left turns | magnitude from 2-D, sign unresolved |
| F-11 | face sign measures **head** yaw | reject face sign |
| F-12 | sign information absent from `\|yaw2D\|`; `\|yaw2D\|` bounded to 90° | reject temporal sign |
| F-13 | hip sign 4 ✓ / 2 ✗ / 4 no-signal over 10 blocks | reject hip sign; **raised this task** |

F-13's incidental observation — shoulder Δz correct on 9/10 blocks — is what F-14 exists to test
properly. It was explicitly recorded there as *"a lead, NOT a result"*.

---

## 3. Exact Shoulder Signal Definition

Traced from source, not from prior reports:

| layer | left | right | evidence |
|---|---|---|---|
| COCO-WholeBody (model output) | **5** | **6** | `wholebody_udp_sender.py:46, :83` |
| JointId (33-slot UDP payload) | **11** | **12** | `rtmw3d_pose.py:52` |
| audit log | `j["L-shoulder"]` | `j["R-shoulder"]` | `AUDIT_JOINTS` |
| sender log | `sh[0]` | `sh[1]` | `f09_torso_features.py:129` |

The audit record is written at `wholebody_udp_sender.py:460–509`, which is **before** the one-euro /
depth-outlier smoother (`:517`) and **before** hip-centring (`:540`):

| field | statistic | units / frame | smoothing |
|---|---|---|---|
| `j["L-shoulder"]["p30"]` / `["p50"]` | 30th / 50th percentile of valid pixels in a **5×5** window at the joint's pixel | mm, **camera Z**, no root-centring | **none — raw** |
| `j["L-shoulder"]["ctr"]` | single centre pixel | mm, camera Z | none |
| `sh[0][2]` | emitted landmark Z | metres, **hip-relative** | **one-euro + depth-outlier gate** |

`kwin = 5` (`:190`). `--flatten-trunk` was **OFF** for both captures (Milestone-2 default); had it been
on, `lm[11|12][2]` would be zeroed at `:139` and the emitted candidate would be vacuous. Raw audit
depths are unaffected either way.

`yaw3D` is reproduced exactly as `line_yaw_deg(sh[0], sh[1])` from `f09_torso_features.py:65` —
`atan2(shR.x − shL.x, shR.z − shL.z)` through the Kalidokit normalisation — so candidate E is the
same quantity F-10 scored, not a re-derivation.

---

## 4. Candidate Signals

| id | signal |
|---|---|
| **A** | `p30(L-sh) − p30(R-sh)`, mm — **raw, primary** |
| **B** | `p50(L-sh) − p50(R-sh)`, mm — raw |
| **C** | `ctr(L-sh) − ctr(R-sh)`, mm — raw centre pixel |
| **D** | `(sh[0].z − sh[1].z)·1000`, mm — emitted, post-smoothing (comparison) |
| **E** | `sign(yaw3D)` — the F-10 baseline |

Polarity inferred per candidate from the main group: A/B/C/D = −1, E = +1.
A Δz of exactly 0 is a **third state, `undefined`** — never "negative" (the F-13 bug).

**Populations.** 1,320 frames in the main group (whole-body and body-turned blocks), 1,000 true-zero
frames, and **333 frames in a separate twist group**. Unlike F-13, `tw_sh_p45`/`tw_sh_m45` *do* turn
the shoulders, so they are on-target here — but the subject reported confusion during that protocol,
so they are scored as a **robustness group and never pooled into a headline**.

---

## 5. Static Ground-Truth Results

| id | signal | defined | coverage | overall | positive | negative | +90 | −90 | **all frames** |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|
| A | raw p30 Δz | 854 | 64.7 % | 99.88 % | 99.79 % | 100.00 % | 98.92 % | 100.00 % | **64.62 %** |
| B | raw p50 Δz | 879 | 66.6 % | 99.77 % | 99.59 % | 100.00 % | 98.18 % | 100.00 % | **66.44 %** |
| C | raw centre Δz | 876 | 66.4 % | 99.66 % | 99.39 % | 100.00 % | 97.25 % | 100.00 % | **66.19 %** |
| D | emitted Δz | 1157 | 87.7 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % | **87.65 %** |
| E | `sign(yaw3D)` | 1229 | 93.1 % | 94.14 % | 100.00 % | 87.46 % | 100.00 % | **64.53 %** | **87.65 %** |

**Accuracy where defined is excellent — coverage is the problem.** The all-frames column (undefined
counted wrong) puts raw Δz at **64.62 %**, *below* `sign(yaw3D)`'s 87.65 %. Candidates D and E land on
the identical 87.65 %: D trades coverage for precision, E trades precision for coverage, and the
product is the same.

Per-heading, where the pooled row hides the failure:

| signal | +45 cov / acc | −45 cov / acc | +90 cov / acc | **−90 cov / acc** |
|---|---|---|---|---|
| raw p30 Δz | 100.0 % / 100 % | 99.7 % / 100 % | 33.8 % / 98.92 % | **3.7 % / 100 %** |
| emitted Δz | 100.0 % / 100 % | 100.0 % / 100 % | 100.0 % / 100 % | **44.6 % / 100 %** |
| `sign(yaw3D)` | 100.0 % / 100 % | 100.0 % / 100 % | 100.0 % / 100 % | 100.0 % / **64.53 %** |

**At −90° the raw signal answers on 11 frames out of 294.** Its 100 % there is 11 frames.

**Twist robustness group** (compromised protocol, separate):

| id | n | coverage | overall | all frames |
|---|--:|--:|--:|--:|
| A raw p30 | 333 | 91.9 % | **81.70 %** | 75.08 % |
| D emitted | 333 | 100.0 % | **79.58 %** | 79.58 % |
| E `yaw3D` | 333 | 100.0 % | **79.58 %** | 79.58 % |

Accuracy drops ~18 points the moment the pose stops being a whole-body turn. The label may be at fault
— but that is exactly the uncertainty that stops this being a PASS.

---

## 6. Block-Level Evidence

The primary unit. 1,320 frames are ~130 near-duplicates × 10 held poses.

| cap | block | true | grp | raw p30 Δz | emitted Δz |
|---|---|--:|---|---|---|
| F-10 | `h_m45` | −45 | main | correct, 106/106 def | correct, 106/106 |
| F-10 | `h_m90` | −90 | main | **NO SIGNAL** (0 def) | **NO SIGNAL** (0 def) |
| F-10 | `h_p45` | +45 | main | correct, 111/111 def | correct, 111/111 |
| F-10 | `h_p90` | +90 | main | correct, **30/140 def** | correct, 140/140 |
| F-11 | `dc_body45L_face0` | −45 | main | correct, 150/150 def | correct, 150/150 |
| F-11 | `dc_body45R_face0` | +45 | main | correct, 148/148 def | correct, 148/148 |
| F-11 | `h_m45` | −45 | main | correct, 114/115 def | correct, 115/115 |
| F-11 | `h_m90` | −90 | main | correct, **11/147 def** | correct, 131/147 |
| F-11 | `h_p45` | +45 | main | correct, 121/121 def | correct, 121/121 |
| F-11 | `h_p90` | +90 | main | correct, **63/135 def**, 98 % agree | correct, 135/135 |
| F-10 | `tw_rigid_p45` | +45 | twist | correct, 105/105 def | correct, 105/105 |
| F-10 | `tw_sh_p45` | +45 | twist | correct, 114/114 def | correct, 114/114 |
| F-10 | `tw_sh_m45` | −45 | twist | **WRONG**, 64 % agree | **WRONG**, 60 % agree |

| group | | correct | wrong | no signal |
|---|---|--:|--:|--:|
| main | raw p30 Δz | **9** | **0** | 1 |
| main | emitted Δz | **9** | **0** | 1 |
| main | `sign(yaw3D)` | **9** | **1** | 0 |
| twist | raw p30 Δz | 2 | 1 | 0 |

F-13's 9/10 replicates exactly. **But `sign(yaw3D)` also scores 9 correct at block level** — the wrap
costs it frames, not blocks. At the block level the two signals are indistinguishable.

---

## 7. Raw Depth Distributions

Raw p30, mm:

| heading | n | zL p50 | zR p50 | Δz p10 | p25 | p50 | p75 | p90 | sd | **zero %** |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 0 | 1000 | 1415 | 1516 | −136 | −101 | **−89** | 0 | 0 | 57.1 | 46.8 % |
| +45 | 380 | 1326 | 1516 | −252 | −252 | −190 | −167 | −78 | 58.2 | **0.0 %** |
| −45 | 371 | 1516 | 1326 | +89 | +89 | +190 | +217 | +217 | 52.9 | **0.3 %** |
| +90 | 275 | 1415 | 1415 | −101 | −78 | **0** | 0 | 0 | 40.9 | **66.2 %** |
| −90 | 294 | 1326 | 1248 | 0 | 0 | **0** | 0 | 0 | 14.2 | **96.3 %** |

**Separability (rank AUC, 0.500 = indistinguishable):**

| pair | AUC | separability |
|---|--:|--:|
| **+45 vs −45** | **0.000** | **100.0 %** |
| +90 vs −90 | 0.322 | **67.8 %** |

±45° is a textbook result: the two distributions are **completely disjoint** (+45 spans −252…−78,
−45 spans +89…+217) with 0.0 % / 0.3 % zeros. ±90° is near chance.

Note also the **standing offset at true 0°**: Δz p50 = −89 mm with only 46.8 % zeros. The signal claims
a direction at a square torso more than half the time — addressed in §10.

---

## 8. Quantisation / Geometry Analysis

Re-derived from the data: the depth rungs near the torso are 1248, 1326, 1415, 1516, 1632, 1768,
1929 mm; fitting `z·d` gives **f·B = 21,216 mm·px**, matching F-13. At 1326 mm, `d = 16` and
**one step is 78–88 mm (mean 83)**.

Effective shoulder width measured front-on (where `cos(yaw) = 1`): **359 mm**.

| θ | Δz = W·sin θ | disparity steps | vs hips (F-13) |
|--:|--:|--:|--:|
| 15° | 92.9 mm | 1.12 | 2.76× |
| 30° | 179.5 mm | 2.16 | 2.76× |
| **45°** | **253.8 mm** | **3.05** | 2.76× |
| 60° | 310.8 mm | 3.74 | 2.76× |
| 90° | 358.9 mm | **4.31** | 2.76× |

The shoulders clear **3.05 steps at 45°** against the hips' 1.11 — the 2.76× baseline advantage is real
and it is exactly why ±45° works perfectly here and failed for the hips.

**But the geometry predicts 4.31 steps at 90°, and the measurement delivers 96.3 % zeros at −90°.**
The model is right about the quantisation and wrong about ±90°, because it assumes both shoulders
remain independently visible. They do not — see §9.

---

## 9. ±90° Analysis

| heading | n | uSpan px | \|yaw3D\| | raw cov | raw acc | emit cov | emit acc | measured |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| 0 | 1000 | 67.6 | 9.6 | 53.2 % | — | 69.6 % | — | 100 % |
| +45 | 380 | 39.8 | 47.7 | 100.0 % | 100.00 % | 100.0 % | 100.00 % | 100 % |
| −45 | 371 | 46.5 | 38.5 | 99.7 % | 100.00 % | 100.0 % | 100.00 % | 100 % |
| +90 | 275 | **12.9** | 38.3 | **33.8 %** | 98.92 % | 100.0 % | 100.00 % | 100 % |
| −90 | 294 | **7.0** | 1.6 | **3.7 %** | 100.00 % | 44.6 % | 100.00 % | 100 % |

Occlusion evidence:

| heading | conf p50 | conf p10 | valid px p50 | **dsd p50** |
|---|--:|--:|--:|--:|
| +45 | 0.709 | 0.587 | 25 | 41.5 |
| −45 | 0.729 | 0.627 | 25 | 32.8 |
| +90 | 0.532 | 0.494 | 25 | 32.6 |
| −90 | 0.508 | 0.493 | 25 | **0.0** |

The shoulder line collapses from 67.6 px front-on to **7.0 px at −90°**. Two 5×5 windows 7 px apart on
a body edge-on to the camera sample the same surface — and `dsd = 0.0` at −90° confirms it: the depth
window is **perfectly uniform**, so both shoulders return the identical rung and Δz is exactly 0. The
failure at ±90° is **self-occlusion and window collision, not quantisation**.

**The critical question — does raw Δz retain a sign where `yaw3D` wraps?**

> **NO.** 143 frames show the wrap signature (`|yaw3D| > 150`), all at ±90°. On **128 of them
> (89.5 %) raw Δz is also undefined.** Raw Δz does not rescue the wrap; it goes silent in the same
> place, for the same physical reason.

Note the pipeline still reports both shoulders as `measured` on **100 %** of frames at ±90°, with
confidence merely dropping from 0.71 to 0.51. **A confidence gate would not catch this either.**

---

## 10. True-Zero Safety

| cap | block | n | defined | \|Δz\| p50 | \|Δz\| p95 | flips/s | \|yaw2D\| p50 |
|---|---|--:|--:|--:|--:|--:|--:|
| F-10 | `h_0` | 139 | **100.0 %** | 136.0 | 136.0 | 0.00 | 1.8 |
| F-10 | `h_0b` | 111 | 18.9 % | 0.0 | 116.0 | 0.00 | 3.8 |
| F-10 | `h_0c` | 109 | 9.2 % | 0.0 | 92.8 | 0.00 | 0.0 |
| F-11 | `h_0` | 136 | **100.0 %** | 101.0 | 101.0 | 0.00 | 0.0 |
| F-11 | `h_0b` | 90 | **100.0 %** | 101.0 | 101.0 | 0.00 | 4.2 |
| F-11 | `h_0c` | 117 | 0.0 % | 0.0 | 0.0 | 0.00 | 4.2 |
| F-11 | `dc_body0_head45R` | 147 | 92.5 % | 89.0 | 89.0 | 0.00 | 4.5 |
| F-11 | `dc_body0_head45L` | 151 | 0.0 % | 0.0 | 0.0 | 0.00 | 9.9 |

Read alone this looks alarming: a square torso emits a confident, non-flipping sign on up to 100 % of
frames with |Δz| up to 136 mm — comparable to a real 45° turn.

**But the brief asks the right question: does it move the avatar?** The resulting signed angle at true
0°:

| estimator | MAE | RMSE | p50 | p95 | max |
|---|--:|--:|--:|--:|--:|
| `sign(RAW Δz) · \|yaw2D\|` | **1.60°** | 3.31° | 0.00° | 7.68° | 12.15° |
| `sign(EMITTED Δz) · \|yaw2D\|` | **2.03°** | 3.67° | 0.00° | 7.94° | 12.15° |
| production `yaw3D` | **8.72°** | 12.24° | 9.64° | 21.15° | 21.24° |

**An arbitrary sign at a square torso costs 1.60° of avatar error**, because `|yaw2D|` is 0–4° there
and `sign × ~0 ≈ 0`. That is **5.4× better than what production does today**. This is the strongest
result in F-14 and it vindicates the `sign × magnitude` decomposition: the sign only has to be right
where the magnitude is large.

---

## 11. Dynamic Behaviour

No instantaneous ground truth, so behaviour only.

| block | n | raw cov | raw flips/s | emit flips/s | longest run | raw Δz sd | emit Δz sd |
|---|--:|--:|--:|--:|--:|--:|--:|
| `m_slow` | 172 | 80.2 % | 1.01 | 0.84 | 29 | 158.8 | 86.5 |
| `m_normal` | 138 | 76.8 % | 1.41 | 1.41 | 31 | 148.9 | 82.5 |
| `m_fast` | 130 | 66.9 % | 0.60 | 0.60 | 35 | 149.1 | 108.3 |
| `m_rev_lr` | 113 | 78.8 % | 0.40 | 0.40 | 40 | 160.4 | 99.6 |
| `m_rev_rl` | 114 | 75.4 % | 1.61 | 1.01 | 27 | 159.5 | 61.9 |
| `m_180` | 139 | **36.0 %** | 0.67 | 0.67 | 12 | 101.8 | 52.1 |
| `m_turn_arms` | 124 | 80.6 % | **2.21** | 1.61 | 13 | 222.5 | 69.3 |
| `m_turn_still` | 124 | 91.9 % | **1.82** | 0.81 | 27 | 252.3 | 137.0 |

Coverage in motion (67–92 %) is far better than the hips' 9–32 %. Flip rate is 0.40–2.21/s against a
reference of ~0.5 physical reversals/s (F-12) — **still above it**, and `m_turn_arms` at 2.21/s is
where arm motion perturbs the shoulder landmarks. Smoothing reduces flips on 4 of 8 blocks and never
increases them.

---

## 12. Raw vs Smoothed Sign

| observation | value |
|---|--:|
| where raw is defined (854 frames), emitted agrees with raw | **99.88 %** |
| where raw is **undefined** (466 frames), emitted supplies a sign on | 303 (65.0 %) |
| and that supplied sign is correct on | **100.00 %** |
| raw correct but smoothing makes it wrong | **0 / 854 = 0.0 %** |

The one-euro filter **adds real coverage, not artefacts**: it recovers sub-rung information by averaging
a quantised signal across a held pose — classic dithering, and legitimate while the pose is static.
It never inverted a correct raw sign.

That verdict is scoped to static poses. In motion the same filter lags; §11's flip columns are the
cost, and the smoothed variant's 44.6 % coverage at −90° shows dithering cannot manufacture information
where the raw signal is uniformly zero for 96.3 % of a block.

---

## 13. Comparison Against `yaw3D`

**All turned headings** (n = 854, frames where raw Δz is defined):

| | `yaw3D` correct | `yaw3D` wrong |
|---|--:|--:|
| **raw Δz correct** | **853** | **0** |
| **raw Δz wrong** | 1 | 0 |

**Isolated to \|heading\| = 90** (n = 104):

| | `yaw3D` correct | `yaw3D` wrong |
|---|--:|--:|
| **raw Δz correct** | 103 | **0** |
| **raw Δz wrong** | 1 | 0 |

* raw Δz right where `yaw3D` is wrong: **0 frames**
* raw Δz wrong where `yaw3D` is right: **1 frame**
* at ±90° specifically: **0 rescued, 1 lost**

**The "composition destroys the sign" hypothesis is refuted.** There is no frame anywhere in the
scored data where the raw depth knows the sign and `yaw3D` does not. Raw Δz appears better in §5 only
because it **abstains** exactly where `yaw3D` errs — 89.5 % of wrapped frames have raw Δz undefined.
Abstention is not information.

---

## 14. Hypothetical Shoulder-Sign Hybrid

Section 14 of the brief compares a *held* depth sign against an *unheld* `yaw3D`. That is not a fair
test, because F-10 itself proposed the wrap guard. **Giving every estimator the same hold** is the
experiment that decides F-14:

| population | estimator | MAE | RMSE | wrong sign |
|---|---|--:|--:|--:|
| main | `sign(RAW Δz)`, hold | 8.96° | 14.50° | 0.38 % |
| main | `sign(EMITTED Δz)`, hold | **8.34°** | **9.96°** | **0.00 %** |
| main | `sign(yaw3D)` + F-10 wrap guard | **8.34°** | **9.96°** | **0.00 %** |
| main | **perfect-sign ceiling** | **8.34°** | **9.96°** | — |
| main + twist | `sign(EMITTED Δz)`, hold | 11.87° | 19.71° | 4.11 % |
| main + twist | `sign(yaw3D)` + wrap guard | **11.87°** | **19.71°** | **4.11 %** |
| main + twist | perfect-sign ceiling | 9.30° | 12.34° | — |
| twist only | `sign(EMITTED Δz)`, hold | 25.85° | 39.17° | 20.42 % |
| twist only | `sign(yaw3D)` + wrap guard | **25.85°** | **39.17°** | **20.42 %** |
| twist only | perfect-sign ceiling | **13.09°** | 19.04° | — |

**Identical to the decimal on every population.** They are the same signal.

Against the unguarded baseline and the published references:

| estimator | MAE | RMSE | wrong sign |
|---|--:|--:|--:|
| production `yaw3D`, signed, no guard | **45.78°** | 78.45° | — |
| shoulder raw Δz, hold | 8.96° | 14.50° | 0.38 % |
| **shoulder smoothed Δz, hold** | **8.34°** | **9.96°** | **0.00 %** |
| **`sign(yaw3D)` + wrap guard** | **8.34°** | **9.96°** | **0.00 %** |
| `yaw2D` magnitude alone | 8.34° | 9.96° | — |
| F-10 depth-sign hybrid (own population) | 13.09° | — | — |
| F-11 face-sign / F-12 temporal / F-13 hip-sign | 31.60° / 9.92° / 72.46° | — | — |

The twist-only ceiling of **13.09°** is exactly F-10's published guarded-hybrid figure — F-10's
population was twist-heavy, which is why its guard appeared to fall short of its own 5.90° magnitude.
On clean whole-body turns the guard reaches the ceiling.

---

## 15. >90° Magnitude Limitation

Unchanged and untouched by anything here. `|yaw2D| = acos(clamp(uSpan·Z/K))` is bounded to **[0°, 90°]**;
measured on `m_180`: p50 18.2°, p95 74.2°, **max 83.7°**.

```text
SIGN                  = potentially solved, by a wrap guard, for |yaw| <= 45 (see §17)
MAGNITUDE beyond 90   = STILL UNSOLVED
```

A working sign yields −90…+90 only. `acos` cannot represent 120°. **Separate roadmap items; do not
conflate them.**

---

## 16. Falsification Results

| case | test | result | fires? |
|---|---|---|---|
| A | true +45° but raw Δz undefined or wrong | **0 / 380 = 0.0 %** | no |
| B | true −45° but raw Δz undefined or wrong | **1 / 371 = 0.3 %** | no |
| C | ±90° same sign or no signal | +90 defined 93, −90 defined **11**; signs **DISTINCT** | **partly** — coverage |
| D | true 0° with \|Δz\| ≥ one disparity step | **525 / 1000 = 52.5 %** | **yes** (but costs 1.60°, §10) |
| E | raw sign changes during a stable pose | **0 flips over 5 held blocks** | no |
| F | raw correct but smoothing makes it wrong | **0 / 854 = 0.0 %** | no |
| G | `yaw3D` and raw Δz disagree at edge-on poses | **0 frames rescued at ±90°** | **yes — fatal to the hypothesis** |
| H | same heading, different block-level sign | **0 of 4 headings disagree** | no |

The hypothesis survives A, B, E, F and H cleanly. **It dies on G**, which was the whole premise, and is
badly wounded on C (coverage).

---

## 17. Verdict

# CONDITIONAL

Against the acceptance criteria:

| # | criterion | required | measured (raw Δz) | |
|--:|---|---|---|---|
| 1 | overall sign accuracy | ≥ 95 % | 99.88 % where defined / **64.62 % all frames** | ~ |
| 2 | positive-turn accuracy | ≥ 95 % | 99.79 % | ✓ |
| 3 | negative-turn accuracy | ≥ 95 % | 100.00 % | ✓ |
| 4 | +90° accuracy | ≥ 95 % | 98.92 % **on 33.8 % coverage** | ~ |
| 5 | −90° accuracy | ≥ 95 % | 100.00 % **on 3.7 % coverage (11 frames)** | ~ |
| 6 | block-level ≥ 90 % correct, no directional bias | — | **9 ✓ / 0 ✗ / 1 none**, no bias | ✓ |
| 7 | **raw Δz retains sign where `yaw3D` wraps** | — | **0 frames rescued; silent on 89.5 % of wraps** | **✗** |
| 8 | coverage high enough for practical use | — | **3.7 % at −90°**, 64.7 % overall | **✗** |
| 9 | true-zero false sign does not create signed-angle error | — | **1.60° MAE, vs production's 8.72°** | ✓ |
| 10 | no pathological dynamic flipping | — | 0.40–2.21/s vs ~0.5/s reference | ~ |
| 11 | materially improves the signed hybrid | — | **8.34° vs 8.34° for a guarded `yaw3D` — no improvement** | **✗** |
| 12 | not dependent on one session | — | **F-10 70.83 % vs F-11 98.04 %** on the same estimator | **✗** |

Four criteria fail outright. This is not a PASS. It is also not a NO-GO: criteria 2, 3, 6 and 9 pass
convincingly, ±45° separates at **AUC 0.000**, and the sign question turns out to be **answerable on
this data** — just not by the signal F-14 proposed.

### Central engineering question

> **Does the current OAK-D shoulder measurement contain a usable left/right torso-yaw sign in raw
> depth, independent of the failing `atan2(Δx, Δz)` composition?**

## NO

Not *independent of* it. The raw depth and `yaw3D` carry the **same** sign information: 853/854
agreement, **zero** frames rescued, and a guarded `yaw3D` matches the depth sign to the decimal on
every population. Where the composition fails, the measurement is silent for the same physical
reason — the shoulder line collapses to 7 px and both depth windows sample one surface.

**The useful corollary:** the sign is nevertheless recoverable at ≤ 45° — and the fix is a **wrap guard
on the existing signal**, not a new signal. That is a much smaller change than the V6 this brief
anticipated.

---

## 18. Recommendation

**1. Do not adopt raw shoulder Δz as a sign source.** It is not independent information, it has 3.7 %
coverage at −90°, and it is 0.38 % worse than the smoothed variant. Criterion 11 is decisive: it buys
nothing a wrap guard does not already deliver.

**2. The minimum next experiment (the CONDITIONAL requirement).** Two specific gaps, in order:

* **(a) Session dependence — the blocker.** F-10 and F-11 differ by 27 points on the same estimator
  (70.83 % vs 98.04 %), same subject, same distance, same day. Until that is explained, no number here
  can be trusted as a property of the rig. **This is the point at which a new capture is justified** —
  the first time across F-09…F-14 that I would ask for one. Minimum: **one capture, second subject,
  same protocol, same 1.5 m**, scored with this script unchanged. If the gap is subject- or
  session-specific, the sign is not production-ready at any yaw.
* **(b) ±90° coverage.** Both signals fail there and it is a geometric limit, not a tuning one.
  Either accept a documented ±45–60° working range, or change the measurement (wider baseline,
  sub-pixel disparity, closer subject). Do not attempt to filter it away.

**3. If (a) comes back clean, the smallest possible V6 is a wrap guard**, not a new signal:
hold the last sign while `|yaw3D| > 150°`. Keep the V5 composition untouched. This is a
handful of lines on a path that already exists, and it reached the perfect-sign ceiling on both
captures independently.

**4. Keep the two problems separate.** Even a perfect sign leaves `|yaw2D|` saturated at 90° (§15).

**5. Nothing ships from F-14.** No V6, no threshold, no gate, no production change.

---

## 19. Roadmap Changes

`docs/roadmap.md` has been restructured into an evidence-driven decision tree — what was tested, the
result, what it changed, and the next decision — for F-09 through F-14, followed by an explicit
**NEXT PATH** matching the CONDITIONAL outcome, and the preserved remaining workstreams (upstream
elbow/wrist localisation, palm/wrist rotation, foot/ground constraint, short-gap recovery, >90°
magnitude, final human acceptance), none of which are marked complete.

---

## 20. Files Changed

**Production files changed: NONE.** `KalidokitControlRigDriver`, `TrunkGate`, `yaw3D`, `yaw2D`,
`DampYaw`, torso composition and weights, `UpperChest` binding, `ArmAimSolver`, Arm V2,
P0 / P1-1 / P1-2 / P1-3, pose interpolation, scene defaults, production thresholds, UDP payload
semantics and shipping configuration are all untouched. Verified with `git diff` / `git status` (§26 of
the brief).

**Diagnostic logging changed: NONE.** The shoulder depth was already logged by the F-08 audit
(`AUDIT_JOINTS`), so `wholebody_udp_sender.py` was not touched.

| file | role |
|---|---|
| `python-sidecar~/f14_shoulder_depth_sign.py` | the analysis (offline) |
| `python-sidecar~/oak_v4_evidence/f14_shoulder_sign.txt` | full output, 283 lines |
| `docs/F14_SHOULDER_DEPTH_SIGN_TORSO_YAW_2026-09-10.md` | this report |
| `docs/roadmap.md` | restructured as a decision tree |
| `docs/decisions.md` | ADR-043 |

Reproduce with `python f14_shoulder_depth_sign.py` from `python-sidecar~/`.

---

## 21. Honest Limits

1. **The F-10 block windows are RECOVERED, not original** (F-11 overwrote them; reconstructed by
   `f12_recover_f10_marks.py`, validated to within 3.8° of published medians, flagged
   `"RECOVERED": true`). Every F-10 number inherits that.

2. **Ten held blocks, one subject, one distance (~1.5 m), one session, two captures on the same day.**
   Every frame-level percentage averages ~130 near-duplicates. **Distance sensitivity: NOT MEASURED.**

3. **The session gap is unexplained and is the main reason this is not a PASS.** F-10 70.83 % vs F-11
   98.04 % on `sign(yaw3D)`; raw-Δz coverage 49.0 % vs 74.4 %. Something differed between the two runs
   — position, lighting, or clothing — and this report does not know what.

4. **±45° ground truth is eyeballed**, as in F-10/F-11. The 0° and ±90° cases carry more weight, and
   ±90° is where the signal fails.

5. **`sign(yaw3D)` measures 94.14 % here against F-10's published 78.49 %** — different populations
   (this report pools two captures, excludes twist from the headline, and applies the confidence and
   trim filters). On the F-10 capture alone it is **70.83 %**, which brackets the published figure.
   The two are not directly comparable and no claim rests on the difference.

6. **The twist group is compromised** (subject-reported confusion) and never enters a headline. It is
   nonetheless the only evidence here of shoulder-only rotation, and accuracy drops ~18 points on it.
   Whether that is the signal or the label is **unknown**.

7. **No dynamic accuracy is claimed.** §11 is behaviour only.

8. **`fx` is recovered, not logged** (258.0 / 248.3 px). It affects only the reported effective
   shoulder width in §8; it cannot change any sign, because it enters as a strictly positive scale.
   Note it differs from F-13's hip-derived 288.5 px — the two recover `fx` from different joint pairs
   and the gap reflects landmark placement bias, not a camera change.

9. **The "wrap guard reaches the ceiling" result is measured on 10 blocks**, and its own live
   behaviour has never been tested. It is a strong lead for the smallest V6, not a validated fix.

---

## CORRECTION (added during F-15 preparation, 2026-09-10)

**The "27-point session gap" reported above is mis-framed.** Controlling for heading — which this
report did not do — the two captures agree exactly on three of four headings:

| heading | F-10 | F-11 | delta |
|---|--:|--:|--:|
| +45° | **100.0 %** | **100.0 %** | 0.0 |
| −45° | **100.0 %** | **100.0 %** | 0.0 |
| +90° | **100.0 %** | **100.0 %** | 0.0 |
| **−90°** | **0.0 %** | **89.1 %** | **+89.1** |

All of the variance is **one block: `F-10 h_m90`**. The 70.83 % vs 98.04 % figures in §14c are
frame-weighted over two different block mixes — F-10's main group is **56.9 %** ±90° frames against
F-11's **34.6 %** — so a single-pose failure was amplified into what looked like session-wide
instability. Macro-averaged over the four headings the captures read **75.00 %** and **97.28 %**, and
the entire difference is still that one block.

Two hypotheses were tested and **refuted** while preparing F-15:

* **Distance.** F-11 was recorded ~13 cm closer (1.284 m vs 1.415 m), which moves the shoulders from
  disparity rung 15 to 17 and shrinks the quantisation step from 101 mm to 78 mm. That predicts
  "closer is better" — but binning by distance *within* each capture shows the opposite: F-10's
  1.50 m+ frames give **100 %** coverage and **100 %** accuracy on the *coarsest* 117 mm step, while
  its 1.30–1.40 m frames give **0 %**. The distance bins are confounded with heading, because turning
  changes the measured `hipZ`. **Distance is not the driver.**
* **Landmark confidence / depth coverage.** Both captures show identical valid-pixel counts (25/25)
  and a shoulder-confidence p10 of 0.502. Neither separates them.

**What this changes for F-15.** The open question is *not* "does the estimator generalise across
sessions" — three headings are perfectly reproducible across two sessions. It is narrower and harder:
**what happens at −90°, and does it depend on the subject's build?** −90° is the pose where the
shoulder line collapses to 7.0 px, both 5×5 depth windows land on one surface (`dsd = 0.0`), and
`yaw3D` wraps. F-15's capture should be read primarily as a test of that pose.

Nothing else in this report changes: the central answer (raw Δz is not an independent sign source;
0 frames rescued; a wrap-guarded `yaw3D` matches it to the decimal) stands, as does the CONDITIONAL
verdict. Only the *characterisation of the remaining uncertainty* is corrected — from "session
dependence" to "the −90° pose".
