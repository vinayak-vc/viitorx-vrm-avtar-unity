# F-11 — 2D Face/Nose Sign for Torso Yaw

**Verdict: NO-GO.**

The face signal does not measure torso yaw. It measures **head** yaw — and in the decoupling test a
**head-only turn produces a signal 2.3× larger (nose) or 7.7× larger (ears) than a genuine 45° torso
turn**. In a virtual mirror, where the user turns their body while keeping their face on the mirror,
that is the failure mode that matters most, and it is not marginal.

**No production behaviour was changed.** The only edit is diagnostic logging so the question became
answerable at all.

---

## 1. Objective

Determine whether a purely 2-D face/shoulder relationship can supply the **SIGN** of torso yaw, to pair
with F-10's already-validated 2-D foreshortening **magnitude**.

---

## 2. Existing F-10 Evidence

| quantity | value |
|---|---|
| `yaw2D` magnitude MAE | **5.90°** ✅ |
| production `yaw3D` MAE | 26.70° (61.08° signed) |
| depth sign, overall | 78.49 % ❌ |
| depth sign, negative turns | 189/374 (50.5 %) |
| depth-sign hybrid, signed MAE | 13.09° |

The magnitude was solved; only the sign was open.

---

## 3. Landmark Mapping

Read from `rtmw3d_pose.COCO17_TO_JOINTID`, not assumed:

| COCO index | keypoint | JointId |
|---|---|---|
| **0** | **nose** | 0 |
| 1 / 2 | left / right eye | 2 / 5 |
| 3 / 4 | left / right ear | 7 / 8 |
| **5 / 6** | **left / right shoulder** | 11 / 12 |
| 11 / 12 | left / right hip | 23 / 24 |

`AUDIT_JOINTS` began at index **5** — the entire face (0–4) was never logged.

---

## 4. Diagnostic Changes

### 4a. The F-10 archive was checked first and was insufficient

| source | size | face? |
|---|---|---|
| `pipeline_logs_f10_near/audit_log.jsonl` | 184 MB | **No** |
| `…/sender_log.jsonl` | 24 MB | **No** |
| `…/holds_log.jsonl` | 15 MB | **No** |
| RGB frames | — | not recorded |

`grep -l nose` over all three returns nothing, and no imagery was kept, so nothing could be re-derived.

### 4b. The change — diagnostic only

`python-sidecar~/wholebody_udp_sender.py`, 3 hunks, all inside `if audit_f is not None:`:

```python
+ AUDIT_FACE_JOINTS = (("nose", 0), ("L-eye", 1), ("R-eye", 2), ("L-ear", 3), ("R-ear", 4))
-   _rec = {..., "j": {}}
+   _rec = {..., "j": {}, "f": {}}
+   for _n, _i in AUDIT_FACE_JOINTS:
+       _rec["f"][_n] = {"c": ..., "u": ..., "v": ...}
```

Kept as a **separate tuple** from `AUDIT_JOINTS` on purpose: that tuple is also iterated by the periodic
`depthQuality` aggregate, so extending it would have moved a reported diagnostic number. This way that
aggregate is numerically identical.

### 4c. New capture

`pipeline_logs_f11` — 11 blocks, **1457 labelled frames**, hipZ ≈ 1.44 m, all blocks covered. Seven
static headings (0, ±45, ±90) plus **four head/torso decoupling blocks** added specifically to test the
failure mode this report predicted. `K` calibrated on the pure `h_0*` blocks = 88.8.

---

## 5. Candidate Signals

| candidate | definition |
|---|---|
| **A** | `noseX − shoulderMidX` (px) |
| **B** | `A / shoulderSpanPx` |
| C1/C2 | `noseX − L/R shoulderX` — **closed out**: the nose lies between the shoulders in every frame, so both hold a fixed sign and their difference is just the span |
| **Dn** | `(eyeCentreX − shoulderMidX) / span` |
| **E** | ear asymmetry, `conf(L-ear) − conf(R-ear)` |

Polarity for each candidate was **inferred independently** from the static blocks. This matters: the
nose offset and the ear asymmetry have **opposite** natural signs (`A/B/Dn = −1`, `earAsym = +1`), and an
earlier pass that forced one polarity on all of them scored `earAsym` at 2.51 % instead of 97.49 %.

---

## 6. Static Ground-Truth Results

| candidate | n | overall | positive turns | negative turns |
|---|---|---|---|---|
| **B** (nose, normalised) | 518 | **87.84 %** | **75.39 %** | 100.00 % |
| A (nose, px) | 518 | 87.84 % | 75.39 % | 100.00 % |
| Dn (eye centre) | 518 | 85.33 % | 70.31 % | 100.00 % |
| **E** (ear asymmetry) | 518 | **97.49 %** | 94.92 % | 100.00 % |

Per-block medians:

| block | torso GT | head GT | n | yaw2D | A px | B | earAsym | B sign ok |
|---|---|---|---|---|---|---|---|---|
| `h_0` | 0 | — | 136 | 0.0° | −1.40 | −0.0206 | −0.0049 | — |
| `h_p45` | +45 | — | 121 | 63.7° | −4.32 | −0.1406 | +0.0226 | **100.0 %** |
| `h_p90` | **+90** | — | 135 | 76.8° | −7.37 | −0.7498 | +0.0787 | **53.3 %** |
| `h_0b` | 0 | — | 90 | 4.2° | −0.94 | −0.0138 | −0.0101 | — |
| `h_m45` | −45 | — | 115 | 58.5° | +5.69 | +0.1539 | −0.0484 | **100.0 %** |
| `h_m90` | −90 | — | 147 | 81.1° | +9.79 | +0.8922 | −0.1051 | **100.0 %** |
| `h_0c` | 0 | — | 117 | 4.2° | +0.94 | +0.0140 | −0.0002 | — |

---

## 7. ±90° Results

| pose | n | B sign accuracy |
|---|---|---|
| \|torso\| = 45° | 236 | **100.00 %** |
| \|torso\| = 90° | 282 | 77.66 % |
| **torso = +90°** | 135 | **53.33 %** — chance |
| torso = −90° | 147 | 100.00 % |

The face remains **visible and confident** at ±90° (nose confidence stayed high throughout), so this is
not an occlusion failure. It is a **normalisation** failure: at ±90° the shoulder span collapses to a
few pixels, so `B = A/span` divides by a near-zero denominator and blows up to |B| ≈ 0.75–0.89 with
whatever sign the pixel noise happens to give. The raw offset `A` is only −7.37 px at +90°, i.e. the
numerator is small in absolute terms while the denominator is smaller still.

**±90° is where the depth sign already failed (F-10: 7.7° at true +90°, 180.0° at true −90°). The face
sign fails there too, on the same side.**

---

## 8. Motion/Reversal Results

Not captured — this run was `--focus sign` (static + decoupling) to keep the subject's time to ~3
minutes, on the reasoning that a sign source failing the static and decoupling tests cannot be rescued
by motion data. Video-derived stability from the earlier pass, for reference: nose-offset sign flips on
**6.45 %** of frames and is near-zero on **31.5 %**.

---

## 9. Sign Confusion Matrix

Candidate **B**, static turned frames (n = 518):

| | predicted + | predicted − |
|---|---|---|
| **true +** (n = 256) | **193 (75.39 %)** | 63 |
| **true −** (n = 262) | 0 | **262 (100.00 %)** |

A **systematic left/right asymmetry**, exactly the defect F-10 exposed in the depth sign — different
mechanism, same shape. All 63 errors are positive turns misread as negative, and they are concentrated
in `h_p90`.

---

## 10. Hybrid Face-Sign + 2D Magnitude

`yawHybridFace = sign(faceB) · |yaw2D|`, scored signed against torso ground truth:

| estimator | MAE | RMSE | p50 | p95 | max |
|---|---|---|---|---|---|
| **hybrid (face sign)** | **31.60°** | 59.21° | 13.52° | 166.82° | 169.36° |
| `yaw2D` magnitude alone (unsigned) | 13.18° | 13.79° | 13.14° | 20.34° | 22.76° |
| depth-sign hybrid (F-10) | 13.09° | 25.46° | — | 78.07° | — |
| production `yaw3D` (F-10) | 61.08° | 104.92° | — | 270.00° | — |

**The face sign makes the hybrid worse than the depth sign it was meant to replace** — 31.60° against
13.09° — because its errors land at ±90° where they cost ~180° each. The p95 of 166.82° is the
signature of that.

> The 13.18° magnitude MAE here is higher than F-10's 5.90° because this run's ±45° blocks read
> yaw2D ≈ 58–64° — the subject over-turned on the eyeballed halfway marks. That is **ground-truth
> error, not estimator error**, and it inflates all three rows equally, so the comparison between them
> holds.

---

## 11. Failure Cases

### 11a. The decisive one — the signal measures the HEAD, not the TORSO

| signal | genuine torso ±45°, face forward | **torso 0°, head ±45°** | ratio false/true |
|---|---|---|---|
| **B** (nose) | 0.1472 | **0.3369** | **2.3×** |
| **E** (ears) | 0.0355 | **0.2745** | **7.7×** |

Frontal reference (torso 0, head 0): B = 0.0141, E = 0.0043.

**Turning only the head, with the torso square to the camera, produces a larger signal than a real 45°
torso turn does.** No threshold separates them, because the false signal is on the wrong side of the
true one. This is not noise that more data would average away; it is the signal measuring a different
degree of freedom.

### 11b. What did work, and why it misled the earlier analysis

The other half of the decoupling test came out **well**:

| block | n | B p50 | sign correct |
|---|---|---|---|
| `dc_body45R_face0` — torso +45, face on camera | 148 | −0.0543 | **99.32 %** |
| `dc_body45L_face0` — torso −45, face on camera | 150 | +0.1300 | **100.00 %** |

So when the body turns and the face stays on the camera, the sign *is* recovered. The mechanism is not
the nose pointing anywhere — it is that the **shoulder midpoint shifts in the image** as the shoulders
foreshorten, leaving a stationary nose offset from it.

That is a real effect, and it is why the signal looked promising. But it means the measurement is a
*difference* between two things that both move independently. When the head moves instead (§11a), the
same difference appears with no torso rotation at all, and nothing in the signal distinguishes the two
cases.

### 11c. Ambiguity is not the problem

If ambiguity were the issue, a hold-on-uncertain rule would help. It is not: `|B|` is below 2× its
frontal value on only **3.7 %** of turned frames (E: 6.2 %). The signal is confident and wrong, which
is worse than uncertain.

---

## 12. Safety Analysis

* **No production behaviour changed** — verified by diff; the only edit is §4b, entirely inside the
  audit block.
* `depthQuality` aggregate numerically unchanged (face joints deliberately excluded from `AUDIT_JOINTS`).
* **No ML, no classifier, no state machine**, per the brief.
* **Ground truth used only for scoring and for the single `K` constant** (calibrated on the 0° blocks),
  never to pick a sign per frame.
* **`yaw3D` was never used to label the face sign** — F-09's circularity avoided.
* Audit-log growth ≈ 1 %.

---

## 13. Verdict

## NO-GO

Against the brief's NO-GO criteria — **four of six are met**:

| criterion | met? | evidence |
|---|---|---|
| < 90 % overall sign accuracy | ✅ | B = **87.84 %** |
| negative-turn accuracy near chance | ❌ | 100 % |
| **systematic sign bias** | ✅ | positive 75.39 % vs negative 100.00 %; all 63 errors one-directional |
| frequent wrong-way transitions | ✅ | 47 % wrong at true +90° |
| **±90° fundamentally ambiguous** | ✅ | **+90° = 53.33 %**, chance |
| **signal tracks something other than heading** | ✅ | **it tracks HEAD yaw: 2.3–7.7× larger on head-only motion** |

The ear asymmetry (97.49 % static) would clear the accuracy bars — and fails the decoupling test
**worst of all at 7.7×**. Higher static accuracy from a signal attached to the skull is not evidence it
measures the torso; it is evidence the protocol's head and torso happened to move together.

---

## 14. Recommendation

**Do not implement V6 with a face-derived sign.** Do not pursue a different face landmark — nose, eyes
and ears all sit on the same rigid body, and all three fail identically (§11a).

**What remains true:** F-10's magnitude result is untouched and still the best torso-yaw signal measured
— `yaw2D` MAE **5.90°** against production's **26.70°**. The problem is entirely the sign.

**Candidate sign sources that do NOT ride on the head**, in order:

1. **Temporal sign continuity seeded at a known-frontal pose.** `|yaw2D|` is accurate; its sign can only
   change by passing through zero. Track the zero crossings and the sign follows, with no dependence on
   head pose. Testable against the F-11 capture already recorded — the static blocks pass through 0°
   between every turn.
2. **Hip-line depth sign.** The hips do not rotate with the head. Their depth difference is noisy for
   *magnitude* (F-09), but sign is 1 bit and the hips are a larger, flatter target than the shoulders.
   Also testable on existing data.
3. **Shoulder-occlusion ordering** (F-10 §10) — which shoulder the model places further from the body
   centre at large yaw.

All three are offline-testable on captures already in hand. **No new subject time is needed to reach
the next decision.**

---

## 15. Files Changed

**Production behaviour: none.**

| file | change | kind |
|---|---|---|
| `python-sidecar~/wholebody_udp_sender.py` | `AUDIT_FACE_JOINTS`; `"f": {}`; 5-iteration face loop — all inside `if audit_f is not None:` | **diagnostic only** |
| `python-sidecar~/f10_gt_capture.py` | added `DECOUPLE` blocks and `--focus sign` | capture harness |

**New analysis files:** `f11_face_extract.py`, `f11_face_analyze.py`, `f11_gt_score.py`.
**Evidence:** `oak_v4_evidence/f11_gt_score.txt`, `f11_video_analysis.txt`, `f11_video_face.jsonl`,
`pipeline_logs_f11/`.

**Unchanged, verified:** torso composition, `TrunkGate`, `yaw3D`, `yaw2D`, `DampYaw`, retarget weights,
`ArmAimSolver`, Arm V2, P0/P1 tracking, scene defaults, thresholds, shipping configuration.

---

## Honest limits

1. **One subject, one distance (1.44 m), one room, one outfit.** Ear-confidence asymmetry in particular
   could be hairstyle-dependent.
2. **±45° ground truth was eyeballed** and this subject over-turned — yaw2D read 58–64° at the "45°"
   marks. It inflates §10's magnitude MAE, though equally across all three estimators.
3. **Motion and reversal blocks were not captured** in this run (§8); the conclusion rests on static and
   decoupling data.
4. **The decoupling blocks are self-reported.** "Body turned, face on camera" was the instruction; how
   precisely the subject held head and torso apart is not independently measured. The effect sizes
   (2.3× and 7.7×) are far too large to be explained by imperfect compliance, but the exact ratios
   should not be quoted to two significant figures.
5. **`h_p90` may be partly a protocol artefact**: yaw2D read 76.8° there against 81.1° at −90°,
   suggesting the subject turned less far to the right. That would not change the conclusion — the
   failure is at 53.33 %, far below anything a few degrees of under-turn explains.
6. **The earlier video-based section of this report** (nose offset matching projection physics to 2 % at
   20°) still stands as a physical result. It is simply not sufficient: a signal can be geometrically
   real and still be measuring the wrong body part.
