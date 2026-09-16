# F-10 — torso yaw ground-truth experiment — 2026-09-10

**Nothing was changed.** No production C#, no Python pipeline, no scene, no threshold. Per TASK 8 this
is a measurement experiment only.

**Verdict: NO-GO for `hybrid = sign(yaw3D) · |yaw2D|` as specified — but the magnitude half is
strongly validated and should be pursued with a different sign source.**

The ground-truth capture was executed. It splits the proposal cleanly in two:

| half of the proposal | result | evidence |
|---|---|---|
| **2-D foreshortening → yaw MAGNITUDE** | **VALIDATED, decisively** | MAE **5.90°** vs production's **26.70°** — **4.5× better** |
| **depth → yaw SIGN** | **REFUTED** | **78.49 %** sign accuracy (89.52 % excluding the ±180° wrap) |

The composite therefore fails decision criteria 2 and 4, and the brief's own NO-GO wording is the right
one: *keep V5 and investigate another measurement source* — where the source to replace is the **sign**,
not the magnitude.

---

## 1. Camera / setup geometry

| quantity | value |
|---|---|
| capture | `pipeline_logs_f10_near`, 2520 labelled frames, 20/20 blocks covered |
| subject distance | commanded 1.5 m; measured hipZ p50 **1.44 m** |
| face-on shoulder pixel span | **60–64 px** (measured at the three 0° blocks) |
| calibration constant `K = fx·S` | **91.2**, from the 0° blocks |
| RGB stream | 640×400, fx ≈ 284.6, cx ≈ 319.2 |
| sidecar | shipping `wholebody_udp_sender.py`, unmodified, `--audit-log` on |

**No floor marks were used.** The headings were established from physically self-evident poses:

* **0°** — square to the lens.
* **±90°** — shoulders edge-on, one shoulder pointing at the lens and the other away. This is
  self-verifying and it is what makes the run usable as ground truth with no setup. The measured
  shoulder pixel span at these blocks — **1.4 px** at −90° and **10.4 px** at +90°, against 60–64 px
  face-on — independently confirms the subject really was edge-on.
* **±45°** — eyeballed halfway. Recorded as **estimated**, carrying perhaps ±5–10° of their own error;
  no conclusion below rests on them alone.

---

## 2. Ground-truth methodology

`f10_gt_capture.py` drove the subject; `f10_gt_analyze.py` labelled every frame from the block windows.

* Text-only, large block font (there is no speaker on this machine, and the subject stands ~1.5 m from
  the screen). Each block draws a **bird's-eye diagram** of the required pose.
* **Self-paced**: the protocol shows every position up front and waits for ENTER, so reading time is
  the subject's to spend, not the script's to allocate.
* Each block's first and last **25 %** is trimmed, so a mistimed step costs a block's edges only.
* **`K` calibrated from the 0° blocks**, where `cos(heading) = 1` by construction — the guess F-09 had
  to make with a p98 heuristic is now measured.
* **Ground truth never touches `yaw3D`**, per the brief.

### 2a. Two operator faults on the first attempt, and what they cost

Disclosed because they cost a full run:

1. **`--audit-log` was not passed**, so no pixel coordinates were recorded and `yaw2D` — the entire
   point of the experiment — could not be computed at all. All 7 static blocks were captured and all 7
   were unusable.
2. **The sidecar was started before the subject had finished reading**, and its 520 s window expired
   156 s before the protocol ended, losing 11 of 20 blocks.

Both were fixed for the second attempt (audit log verified writing *before* the run, 1200 s window,
self-paced start). A third fault was caught before it could bite: the block font used `U+2588`, which
the Windows console cannot encode and which would have crashed the capture mid-run; it is now ASCII.

---

## 3. yaw3D accuracy — the production estimate

**MAE 26.70°, RMSE 43.26°, p50 5.28°, p95 90.00°, max 93.99°** over 1226 static-heading frames.

The per-block medians show it is not noise — it is a structured failure:

| block | true heading | yaw3D p10 | **yaw3D p50** | yaw3D p90 | shoulder pixel span |
|---|---|---|---|---|---|
| `h_0` | 0° | 21.0 | **21.1** | 21.2 | 60.2 px |
| `h_p45` | +45° | 47.7 | **48.1** | 50.5 | 42.6 px |
| **`h_p90`** | **+90°** | 1.4 | **7.7** | 44.0 | 10.4 px |
| `h_0b` | 0° | 0.1 | **1.5** | 9.0 | 64.3 px |
| `h_m45` | −45° | −40.7 | **−40.5** | −40.1 | 49.9 px |
| **`h_m90`** | **−90°** | 0.0 | **180.0** | 180.0 | 1.4 px |
| `h_0c` | 0° | −1.1 | **−0.2** | 0.0 | 63.4 px |

* **At ±45° it is fine** — 48.1° and −40.5° against 45°.
* **At ±90° it collapses**: **7.7°** when the truth is +90°, and **180.0°** when the truth is −90°.
  Those are the two poses where the shoulders occlude each other and the depth difference is exactly
  what stereo cannot resolve. This is F-09's quantisation finding, now with ground truth attached.
* **The first 0° block reads 21.1°** — a large static offset that the two later 0° blocks (1.5°, −0.2°)
  do not show, i.e. the estimate is not even repeatable at the same pose.

Bias by heading: **+37.20°** at −90°, **−72.70°** at +90°, **+9.25°** at 0°.

---

## 4. yaw2D accuracy — the 2-D foreshortening estimate

**MAE 5.90°, RMSE 9.28°, p50 4.67°, p95 17.37°, max 45.00°** on the same 1226 frames.

| true heading | **yaw2D p50** | signed bias | note |
|---|---|---|---|
| 0° (×3 blocks) | **0.0 / 4.2 / 0.0** | +2.68 | |
| +45° | **45.0** | +0.41 | eyeballed truth |
| −45° | **39.3** | −3.10 | eyeballed truth |
| +90° | **80.7** | −10.33 | |
| −90° | **88.5** | −2.58 | |

**4.5× more accurate than the production estimate** (5.90° vs 26.70° MAE), and the failure mode is
benign: it under-reads slightly at ±90° rather than collapsing or wrapping. The underlying pixel span
behaves exactly as the projection model predicts — 60–64 px face-on, 42.6 px at +45°, collapsing to
10.4 px and **1.4 px** at ±90°.

**Criterion 1 (resolution) and criterion 3 (substantially lower MAE/RMSE) are met.**

---

## 5. Hybrid accuracy

`yawHybrid = sign(yaw3D) · |yaw2D|`, scored **signed** against ground truth over the 860 non-zero
heading frames:

| estimator | MAE | RMSE | p95 |
|---|---|---|---|
| production `yaw3D` | 61.08° | 104.92° | 270.00° |
| **hybrid, with a wrap guard** ¹ | **13.09°** | **25.46°** | 78.07° |

¹ holding the previous sign whenever `|yaw3D| > 150°`, i.e. refusing the ±180° wrap.

A 4.7× improvement — but **13.09° of signed error is still an order of magnitude worse than the 5.90°
the magnitude alone achieves**, and the entire difference is the sign.

---

## 6. Sign accuracy — where it fails

| population | frames | sign accuracy |
|---|---|---|
| all non-zero headings | 860 | **78.49 %** |
| excluding the ±180° wrap (`|yaw3D| ≤ 150`) | 754 (87.7 %) | **89.52 %** |
| `|heading| ≥ 90°` | 290 | **63.45 %** |

Confusion matrix (rows = truth):

| | predicted + | predicted − |
|---|---|---|
| **true +** | **486** | 0 |
| **true −** | **185** | 189 |

The asymmetry is the whole story: **positive headings are called correctly 486/486, negative headings
only 189/374 (50.5 %)** — for a left turn the depth sign is no better than a coin flip. `yaw3D` reports
positive on 78 % of all frames regardless of the truth, so it behaves like a biased constant, not a
sign detector.

**Even at its best — 89.52 %, with the ±180° wrap excluded — that is a wrong-way torso on roughly one
frame in ten.** Criteria 2 and 4 fail.

---

## 7. Distance sensitivity

**NOT MEASURED.** Only the 1.44 m capture was run. Once the sign was shown to fail, a second distance
could not change the verdict — it would refine a recommendation that is already NO-GO as specified —
so the subject was not asked to repeat the protocol. `f10_gt_analyze.py` accepts repeated
`--dir`/`--marks` pairs and will produce the distance table whenever a second run exists.

---

## 8. Motion / jitter results

Per-block, over the eight motion blocks (no ground-truth heading in these, so this is jitter and step
size only):

| block | yaw3D sd | **yaw2D sd** | yaw3D p95 step | **yaw2D p95 step** |
|---|---|---|---|---|
| slow turn | 21.12° | 21.78° | 3.91° | 10.62° |
| normal turn | 49.07° | **24.45°** | 12.57° | 10.65° |
| fast turn | 52.35° | **24.51°** | 14.86° | **10.19°** |
| reversal L→R | 79.87° | **25.66°** | 16.82° | **11.02°** |
| reversal R→L | 104.06° | **25.34°** | 27.64° | **17.01°** |
| ~180° turn | 111.62° | **26.36°** | 22.18° | **14.14°** |
| turn + arms moving | 55.42° | **23.23°** | 23.91° | **12.25°** |
| turn, arms still | 70.22° | **21.84°** | 19.07° | **14.94°** |

`yaw3D`'s spread balloons to 79–112° on reversals and the 180° turn — the signature of wrapping —
while `yaw2D` stays at a consistent 22–26° across every motion, which is the range the motion itself
spans. On the slow turn `yaw2D`'s step is larger (10.62° vs 3.91°), consistent with it being unfiltered
while the emitted depth passes through the one-euro filter first. **No filter tuning was done**, per
TASK 6.

---

## 9. Torso-twist results

| block | commanded | hipYaw3D | shoulderYaw3D | relative |
|---|---|---|---|---|
| `tw_rigid_p45` | rigid, both to +45° | 4.97° | 42.76° | 37.79° |
| `tw_sh_p45` | shoulders +45°, hips square | −8.31° | 40.88° | 49.18° |
| `tw_sh_m45` | shoulders −45°, hips square | −16.27° | −11.63° | 4.64° |
| `tw_opp_a` | hips left, chest right | 3.70° | −43.09° | −46.78° |
| `tw_opp_b` | hips right, chest left | −7.35° | 16.11° | 23.46° |

**These blocks are only weakly usable and are reported as such.** The subject said afterwards they were
unsure whether to move their feet, and how to counter-rotate hips against chest — and the numbers show
it: `tw_rigid_p45` should have hips ≈ shoulders ≈ 45° but the hips read 4.97°, and `tw_sh_m45` shows
the hips moving *more* than the shoulders. That is a protocol-comprehension failure, not a measurement
finding, and no conclusion is drawn from this section.

What it does *not* threaten: V5's composition was already proven exact on all three cases — rigid,
twist and opposite reproduce with **gain 1.000 to 0.01°** (ADR-037 §5a). This section was testing
whether the *input* to that composition is trustworthy, and the static blocks (§3) already answer that.

---

## 10. Recommendation

**Do not implement `sign(yaw3D) · |yaw2D|`.** It inherits a sign that is right 78 % of the time overall
and 50 % of the time for left turns — an avatar that turns the wrong way on one frame in ten is worse
than one that under-rotates predictably, which is what V5 does today.

**Do pursue the magnitude half.** 5.90° against 26.70° is not a marginal result, and the failure modes
are qualitatively different: `yaw2D` under-reads gracefully, `yaw3D` collapses to 7.7° or wraps to 180°
at exactly the poses a mirror application cares about.

### The next measurement source to try — for the SIGN only

**The nose, or any face keypoint, relative to the shoulder midpoint in the image.** When a subject
turns right, the nose shifts toward the left shoulder in pixel space; the sign of that offset is a 2-D
quantity that needs no depth at all, and RTMW3D already produces the face keypoints. It is
1 bit of information from a signal that does not suffer disparity quantisation.

**It could not be tested here**: `audit_log.jsonl` records the four torso joints plus limbs, and
`sender_log.jsonl` records shoulders/elbows/wrists/hips/knees/ankles. Neither logs the nose. Adding it
to the audit log is a one-line diagnostic change to the sidecar and would make this testable against
**the capture already recorded** — no new subject time.

Second candidate, weaker: **occlusion ordering** — at ±90° one shoulder is in front of the other, and
which keypoint the model places further from the body centre carries the sign.

### Where this leaves V5

Unchanged and still correct. V5 reproduces whatever yaw it is given with gain 1.000; §3 now shows,
against ground truth, that what it is given is wrong by 26.70° on average and catastrophically wrong at
±90°. **The torso problem is entirely upstream of the retarget**, which is what F-09 concluded and this
capture confirms with truth attached.

---

## Final verdict

## NO-GO — retain V5 and pursue another source

Against the brief's seven criteria:

| # | criterion | result |
|---|---|---|
| 1 | 2-D magnitude has materially better angular resolution | ✅ **MAE 5.90° vs 26.70°, 4.5×** |
| 2 | depth sign remains reliable | ❌ **78.49 % (89.52 % excl. wrap); 50.5 % on left turns** |
| 3 | hybrid reduces MAE/RMSE substantially | ✅ 61.08° → 13.09° signed |
| 4 | hybrid introduces no left/right sign flips | ❌ **inherits the failure in 2** |
| 5 | performance remains real-time | ✅ trivial arithmetic, no new model |
| 6 | V5 torso composition remains valid | ✅ untouched; independently exact (ADR-037) |
| 7 | ARM V2 remains unaffected | ✅ untouched |

Criteria 2 and 4 fail, so the brief's rule applies: **NO-GO — keep V5 and investigate another
measurement source.** The magnitude finding is recorded as validated so the next attempt starts from
it rather than re-deriving it.

---

## Honest limits

1. **One distance only** (1.44 m). §7 is unmeasured, and quantisation scales with distance, so none of
   these numbers generalise to other subject ranges.
2. **±45° ground truth was eyeballed**, worth perhaps ±5–10°. The 0° and ±90° blocks are physically
   anchored and carry the conclusions; §4's ±45° rows are supporting, not load-bearing.
3. **The twist blocks (§9) are compromised** by protocol confusion and no conclusion is drawn from them.
4. **One subject, one room, one lighting condition, one outfit.** Shoulder width `S` is a per-subject
   constant here; a production system would have to estimate it per person, and that estimation error
   feeds directly into `yaw2D`. **This capture cannot bound that error** because there is only one
   subject.
5. **`yaw2D` is measured unfiltered** while `yaw3D` passes through the one-euro depth filter first, so
   §8's step comparison is not like-for-like in favour of `yaw3D`.
6. **The sign result may understate the achievable case.** It is measured on raw `yaw3D`; a sign taken
   from temporal continuity, or from the nose offset above, was not tested. The NO-GO applies to the
   hybrid **as specified**, not to every possible sign source.
7. **Only 1226 static frames across 7 blocks**, one hold each. Block-to-block variation (the 0° blocks
   read 21.1°, 1.5°, −0.2° on `yaw3D`) suggests per-hold effects that more repetitions would separate.

---

## Artefacts

`python-sidecar~/oak_v4_evidence/`: `f10_near_analysis.txt` (the full per-block output) ·
`f10_gt_marks_near.json` (block windows + ground-truth headings) · `f10_precapture.txt` (the
resolution and sign-stability analysis done before the capture).
Raw logs: `python-sidecar~/pipeline_logs_f10_near/` (`sender_log.jsonl`, `audit_log.jsonl`).

Harness: `f10_gt_capture.py` (self-paced, large-text, bird's-eye diagrams per pose) ·
`f10_gt_analyze.py` (labels frames, calibrates `K` from the 0° blocks, scores all three estimators,
sign confusion, twist, motion, distance) · `f10_resolution_sign.py` (pre-capture analysis).
