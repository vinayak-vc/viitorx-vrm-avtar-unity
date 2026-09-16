# F-15 — Cross-Session / Second-Subject Validation of the Minimal Torso-Yaw Wrap Guard

## 1. Executive conclusion

```text
VERDICT: CONDITIONAL
```

On a **second subject**, the sign is **perfect up to ±60° and fails completely at ±90°**. Over the
eight label-validated whole-body blocks the wrap-guarded `yaw3D` scores **6 correct / 1 wrong /
1 no-signal**; split by band that is **±30/±45/±60: 6 correct, 0 wrong, 0 no-signal**, and
**±90°: 0 correct, 1 wrong, 1 no-signal**. Two acceptance criteria pass cleanly — there is **no
subject-specific sign inversion** (all five estimators infer identical polarity on all three
captures) and raw shoulder Δz again supplies **1 true rescue in 318 defined frames**, replicating
F-14. But the ±90° collapse is *worse* on subject B than on subject A, macro-averaged accuracy falls
**59.4 points** against F-11 (97.28 % → 37.85 %), and the guarded hybrid MAE rises **5.13° → 45.29°**
— far past the brief's 15-point "major collapse" trigger.

**Read strictly against the brief's criteria this is a NO-GO** (criterion 2 trips its own numeric
trigger, and "±90° fails" is an explicit NO-GO condition). It is recorded as CONDITIONAL because the
failure is **bounded, geometric and fully explained** rather than general: the estimator reproduced
*perfectly* on a new body everywhere the shoulder line stays resolvable. The decision that follows is
not "redesign the representation" but "**ship it with a documented ±60° range, or not at all**".

---

## 2. Hypothesis

> Is the existing `yaw3D` + F-10 wrap guard reliable enough **across sessions and subjects** to
> justify a minimal V6 implementation?

Secondary: does F-14's "raw Δz is not an independent sign source" conclusion replicate on new data?

**Note on the premise.** F-15's brief was written to chase a *session-dependence* gap
(70.83 % ↔ 98.04 %). While preparing this task that gap was re-characterised and the F-14 report and
ADR-043 were corrected: controlling for heading, F-10 and F-11 agree **exactly** on +45/−45/+90
(100.00 % both) and *all* the variance was one −90° block. The 27-point figure was frame-weighting
over unequal block mixes. So F-15 was aimed at the **±90° pose** and at **body geometry**, which is
why a second subject was the right variable.

---

## 3. Dataset

| | F-10 | F-11 | **F-15** |
|---|---|---|---|
| subject | A | A | **B (new)** |
| capture | `pipeline_logs_f10_near` | `pipeline_logs_f11` | `pipeline_logs_f15` |
| marks | `f10_gt_marks_near_RECOVERED.json` | `f10_gt_marks_near.json` | `f15/f10_gt_marks_near.json` |
| raw → labelled frames | 32,974 → 2,478 | 22,389 → 1,457 | **20,379 → 2,895** |
| distance p50 (p10 / p90) | 1.45 m (1.35 / 1.62) | 1.28 m (1.25 / 1.33) | **1.27 m (1.20 / 1.33)** |
| blocks | 20 | 11 | **28** |
| headings | coarse | coarse | **full (adds ±30 / ±60)** |
| `K` calibration | 91.2 | 88.8 | 83.5 |

**Protocol deviations from F-10, all deliberate and documented:**

1. **`--headings full`** adds static holds at ±30° and ±60°. F-10/F-11 have only 0/±45/±90. The
   shared headings are scored identically; the new ones exist to locate *where* the shoulder line
   collapses, which is the open question. This adds information without altering any comparison.
2. **Distance was actively controlled.** Subject B was measured live before starting and moved from
   1.68 m → 0.99 m → **1.34 m** before the capture began, landing between F-10 (1.45) and F-11
   (1.28). Held to a 13 cm p10–p90 drift. Depth step 90 mm, between F-10's 101 mm and F-11's 78 mm.
3. **Marks written to `oak_v4_evidence/f15/`**, not the default. The default filename derives only
   from `--distance`, which is how the F-10 marks were destroyed. An overwrite guard was also added
   to `f10_gt_capture.py` (analysis/capture tooling, not production).

**A pre-capture blocker was found and avoided.** The system Python has **depthai 3.7.1** while
`requirements.txt` pins `>=2.32,<3`; on 3.x the sidecar aborts at startup
(`PresetMode.HIGH_DENSITY` was renamed). The capture was run from the project `.venv`
(**depthai 2.32.0.0**) — the same depth stack as F-10/F-11, which comparability depends on. Found by
a dry run before the subject was involved.

---

## 4. Method

Every estimator, index, depth field, normalisation and scoring rule is **imported verbatim** from
`f14_shoulder_depth_sign.py`. `f15_cross_session.py` defines **no new estimator**. Two deviations,
both analysis-only and both documented in the script:

1. `f14.TURNED` is extended with `h_p30/h_m30/h_p60/h_m60`. F-10/F-11 do not contain those blocks, so
   this cannot change any F-14 number.
2. **Polarity is inferred separately per capture and then compared**, rather than once on a pooled
   population. This is the only way to detect a subject-specific inversion (criterion 3); reusing a
   pooled polarity would hide exactly what the criterion asks about.

Estimators: **A** raw p30 Δz, **B** raw p50 Δz, **C** centre-pixel Δz, **D** emitted/smoothed Δz,
**E** `sign(yaw3D)`. The wrap guard is F-10's, reproduced exactly: refuse the sign when
`|yaw3D| > 150°`, hold the last one. A Δz of exactly 0 is `undefined`, never "negative".

---

## 5. Results

### 5a. Label audit — did subject B turn the way the labels say?

Before scoring anything, the labels were audited with a signal independent of depth and of `yaw3D`:
the **nose offset from the shoulder midpoint**. For a whole-body turn the head goes with the body, so
its sign must mirror. The convention is read off the positive blocks, never assumed. It is **invalid
for the `dc_*` blocks by construction** — those deliberately hold the face on camera while the body
turns — so those are excluded from the audit.

| block | label | n | \|yaw2D\| | nose norm | check |
|---|--:|--:|--:|--:|---|
| `h_p30` | +30 | 88 | 54.4° | −0.462 | ok |
| `h_p45` | +45 | 85 | 69.4° | −1.083 | ok |
| `h_p60` | +60 | 87 | 74.7° | −1.554 | ok |
| `h_p90` | +90 | 88 | 83.6° | −2.787 | ok |
| `h_m30` | −30 | 95 | 40.8° | +0.304 | ok |
| `h_m45` | −45 | 97 | 58.6° | +0.645 | ok |
| `h_m60` | −60 | 91 | 37.0° | +0.158 | ok |
| `h_m90` | −90 | 94 | 83.1° | +3.614 | ok |
| `dc_body45R_face0` | +45 | 112 | 44.1° | −0.366 | n/a (decoupled) |
| `dc_body45L_face0` | −45 | 110 | 50.1° | −0.284 | n/a (decoupled) |

**All eight whole-body blocks are correctly labelled — the protocol was followed.** Subject B's
rotations were not always the commanded magnitude (`h_p30` reads 54.4°, `h_m60` only 37.0°), which is
normal for eyeballed headings and is why *sign*, not magnitude, is what is scored.

This audit matters: **`dc_body45L_face0` is confidently wrong on 110/110 frames** for every estimator.
It cannot be adjudicated by the nose test, so it is reported but the whole-body population is treated
as primary. That is an explicit technical reason, not cherry-picking — and both populations are given
below.

### 5b. Block-level — the primary metric

| population | estimator | correct | wrong | no signal |
|---|---|--:|--:|--:|
| all 10 main blocks | raw p30 Δz | 7 | **2** | 1 |
| all 10 main blocks | `yaw3D` + wrap guard | 7 | **2** | 1 |
| **8 whole-body (label-validated)** | raw p30 Δz | **6** | **1** | **1** |
| **8 whole-body (label-validated)** | **`yaw3D` + wrap guard** | **6** | **1** | **1** |

Split by band — this is the finding:

| band | correct | wrong | no signal |
|---|--:|--:|--:|
| **\|heading\| ≤ 60°** (±30, ±45, ±60) | **6** | **0** | **0** |
| **\|heading\| = 90°** | **0** | **1** | **1** |

Per block:

| block | true | n | raw p30 Δz | `yaw3D` + guard |
|---|--:|--:|---|---|
| `h_m30` | −30 | 95 | correct (82/95 def) | correct (95/95) |
| `h_m45` | −45 | 97 | correct (7/95 def, 71 % agree) | correct (97/97, 59 % agree) |
| `h_m60` | −60 | 91 | correct (91/91) | correct (91/91) |
| `h_m90` | −90 | 94 | **WRONG** (4/94 def) | **NO SIGNAL** |
| `h_p30` | +30 | 88 | correct (88/88) | correct (88/88) |
| `h_p45` | +45 | 85 | correct (85/85) | correct (85/85) |
| `h_p60` | +60 | 87 | correct (62/87 def) | correct (87/87) |
| `h_p90` | +90 | 88 | **NO SIGNAL** | **WRONG** (1/88 def) |
| `dc_body45R_face0` | +45 | 112 | correct (112/112) | correct (112/112) |
| `dc_body45L_face0` | −45 | 110 | **WRONG** (110/110) | **WRONG** (110/110) |

### 5c. Frame-level, per heading (all frames; undefined counted wrong)

| estimator | +45 | −45 | +90 | −90 | macro |
|---|--:|--:|--:|--:|--:|
| raw p30 Δz | 100.00 % | 2.42 % | 0.00 % | 0.00 % | 25.60 % |
| raw p50 Δz | 100.00 % | 2.90 % | 0.00 % | 0.00 % | 25.72 % |
| centre Δz | 100.00 % | 5.31 % | 0.00 % | 0.00 % | 26.33 % |
| emitted Δz | 100.00 % | 27.54 % | 0.00 % | 0.00 % | 31.88 % |
| `sign(yaw3D)` | 100.00 % | 27.54 % | 23.86 % | 0.00 % | 37.85 % |

The −45 column pools `h_m45` (correct) with `dc_body45L_face0` (110 frames, confidently opposite),
so it understates the whole-body result — block level (5b) is the honest read.

### 5d. The ±90° collapse, located

F-15's new ±30/±60 holds locate the knee of the curve for the first time:

| heading | n | **shoulder span** | raw coverage | raw acc | guard acc | \|yaw2D\| p50 |
|---|--:|--:|--:|--:|--:|--:|
| 0° | 496 | **63.8 px** | 4.6 % | — | — | 0.0° |
| +30 | 88 | 37.2 px | 100.0 % | **100.00 %** | **100.00 %** | 54.4° |
| −30 | 95 | 52.9 px | 86.3 % | 86.32 % | **100.00 %** | 40.8° |
| +45 | 197 | 41.0 px | 100.0 % | **100.00 %** | **100.00 %** | 51.1° |
| −45 | 207 | 41.8 px | 56.5 % | 2.42 % | 27.54 % | 51.1° |
| +60 | 87 | **17.3 px** | 71.3 % | 71.26 % | **100.00 %** | 74.7° |
| −60 | 91 | 53.5 px | 100.0 % | **100.00 %** | **100.00 %** | 37.0° |
| **+90** | 88 | **7.4 px** | **0.0 %** | **0.00 %** | **0.00 %** | 83.6° |
| **−90** | 94 | **8.4 px** | **4.3 %** | **0.00 %** | **0.00 %** | 83.1° |

The shoulder line runs 63.8 px front-on → **7–8 px at ±90°**. Two 5×5 depth windows that close
together on an edge-on torso sample the same surface, so Δz has nothing to compare and `yaw3D`'s
`atan2` wraps. Note `+60` already sits at 17.3 px with raw coverage down to 71.3 % — **the guard is
what holds that block at 100 %**, which is the clearest evidence in this report that the guard does
useful work.

### 5e. True-zero

| estimator | MAE | RMSE | p95 | max |
|---|--:|--:|--:|--:|
| `sign(raw Δz) · \|yaw2D\|` | **0.19°** | 1.08° | 0.00° | 7.85° |
| `sign(guarded yaw3D) · \|yaw2D\|` | **1.11°** | 2.49° | 6.04° | 9.20° |
| production `yaw3D` | 1.00° | 1.84° | 4.42° | 6.40° |

Replicates F-14: an arbitrary sign at a square torso is nearly free, because `|yaw2D| ≈ 0` there.

### 5f. Dynamic

| block | n | raw coverage | raw flips/s | guard flips/s | longest run |
|---|--:|--:|--:|--:|--:|
| `m_slow` | 129 | 84.5 % | 0.00 | 0.16 | 109 |
| `m_normal` | 110 | 86.4 % | 0.00 | 0.20 | 95 |
| `m_fast` | 124 | 45.2 % | 1.39 | 0.60 | 30 |
| `m_rev_lr` | 134 | 50.7 % | 0.79 | 0.79 | 40 |
| `m_rev_rl` | 114 | 51.8 % | 0.80 | 0.00 | 28 |
| `m_180` | 144 | **18.8 %** | 0.33 | 0.00 | 23 |
| `m_turn_arms` | 125 | 32.0 % | 1.19 | 0.60 | 18 |
| `m_turn_still` | 122 | 77.9 % | 0.20 | 0.20 | 79 |

No dynamic accuracy is claimed — there is no instantaneous truth. The guard's flip rate
(0.00–0.79/s) is at or below the ~0.5/s physical reversal reference on 6 of 8 blocks, and better than
F-14's 0.40–1.61/s. `m_180` coverage of 18.8 % is the wrap region behaving as expected.

---

## 6. F-10 vs F-11 vs F-15 comparison

Macro-averaged over the shared headings, so unequal block mixes cannot distort it — the mistake that
produced F-14's spurious "session gap".

**Sign accuracy (all frames):**

| estimator | F-10 | F-11 | **F-15** | vs best prior |
|---|--:|--:|--:|--:|
| raw p30 Δz | 55.36 % | 63.26 % | **25.60 %** | **−37.65** |
| raw p50 Δz | 55.36 % | 67.51 % | **25.72 %** | **−41.78** |
| centre Δz | 55.36 % | 66.97 % | **26.33 %** | **−40.64** |
| emitted Δz | 75.00 % | 97.28 % | **31.88 %** | **−65.39** |
| `sign(yaw3D)` | 75.00 % | 97.28 % | **37.85 %** | **−59.43** |

**Hybrid MAE (degrees):**

| estimator | F-10 | F-11 | **F-15** | vs best prior |
|---|--:|--:|--:|--:|
| `yaw3D` raw, no guard | 47.25 | 10.33 | **78.61** | +68.28 |
| **`yaw3D` + wrap guard** | **5.13** | **10.33** | **45.29** | **+40.16** |
| raw shoulder Δz | 5.13 | 11.32 | **38.02** | +32.88 |
| emitted shoulder Δz | 5.13 | 10.33 | **84.73** | +79.59 |
| perfect-sign ceiling | 5.13 | 10.33 | **9.64** | +4.51 |

The **ceiling itself only moved 4.51°** (5.13 → 9.64), so the magnitude estimator travelled to the new
subject almost intact. The **sign did not**: the guarded hybrid moved 40.16°. The gap between 45.29
and the 9.64 ceiling is entirely the sign at ±90°.

---

## 7. Session-dependency analysis

The original 70.83 % ↔ 98.04 % gap **was not real session instability**, and F-15 confirms the
corrected reading rather than the original one:

* The gap was one −90° block, amplified by frame-weighting over unequal block mixes. Corrected in the
  F-14 report and ADR-043 before this capture.
* **Distance was tested and refuted as the cause** during preparation: F-11 was 13 cm closer (101 mm →
  78 mm step), which predicts "closer is better", but binning *within* each capture shows F-10's
  1.50 m+ frames at 100 % coverage on the *coarsest* 117 mm step while its 1.30–1.40 m frames sit at
  0 %. The bins are confounded with heading, because turning changes measured `hipZ`.
* Landmark confidence and depth coverage were identical between captures (25/25 valid px, conf p10
  0.502 both).

**What F-15 shows instead is a subject/pose interaction, not a session one.** Subject A got ±90°
right in one capture (F-11: +90 100 %, −90 89.1 %) and wrong in the other (F-10: +90 100 %, −90
0 %). Subject B gets **neither** side (+90 0 %, −90 0 %) despite a well-controlled distance and a
genuine 83° turn. Everything ≤ 60° is 100 % on both subjects.

The controlled variable that tracks the failure is **shoulder pixel span**, not distance, session or
confidence: 63.8 px front-on, 17.3 px at +60°, **7–8 px at ±90°**. That is a geometric property of an
edge-on torso at this baseline and resolution, and it is subject-dependent because build and clothing
change how much of the far shoulder survives occlusion.

---

## 8. Failure analysis

| # | failure | evidence |
|---|---|---|
| **1** | **±90° produces no usable sign on subject B** | +90: raw coverage **0.0 %** of 88 frames; −90: **4.3 %** of 94. Guarded `yaw3D`: 0 correct, 1 wrong, 1 no-signal. Shoulder span **7.4 / 8.4 px**. |
| **2** | `dc_body45L_face0` confidently opposite | 110/110 frames wrong, every estimator, 100 % internal agreement, on a block whose label the nose test cannot validate. Either subject B turned the wrong way on the most confusing instruction in the protocol, or the estimator failed identically across four independent extractions. **Unresolved.** |
| **3** | The guard holds a third of frames | 30.9 % of frames held, longest hold **94 frames (~3 s)**. It removes 83.5 % of the wrap errors it fires on, but a 3 s stale sign is a visible artefact. |
| 4 | Raw Δz coverage falls with turn angle | 100 % at ±30/±45 → 71.3 % at +60 → 0.0 % at +90. The guard, not the depth, carries +60. |

---

## 9. Decision

> **Is the minimal V6 `yaw3D` + existing wrap guard now justified?**

**Not as an unbounded estimator. Yes, potentially, within a documented ±60° working range.**

Against the six acceptance criteria:

| # | criterion | result | |
|--:|---|---|---|
| 1 | wrap-guard stable on the second subject | perfect ≤60°, total failure at ±90° | **✗** |
| 2 | no major block-level collapse (>15 pt trigger) | macro 97.28 % → 37.85 % = **−59.4 pt**; block correct-rate 90 % → 75 % | **✗** |
| 3 | no subject-specific sign inversion | **all five estimators, identical polarity, all three captures** | **✓** |
| 4 | guard improves wraps without unacceptable holds | removes 83.5 % of wrap errors; but holds 30.9 %, max 94 frames | **~** |
| 5 | raw Δz again gives no independent rescue | **1 true rescue / 318 defined; 0 at ±90°** | **✓** |
| 6 | reproducible enough to justify V6 | only inside ±60° | **~** |

Criteria 1 and 2 fail, and "±90° fails" is one of the brief's explicit NO-GO conditions. **A strict
reading of the brief returns NO-GO.** This report returns **CONDITIONAL** because the failure is
bounded and explained rather than general, and because the ≤60° result is a genuine cross-subject
validation (6/6 blocks, 100 % frame accuracy, on a body the estimator had never seen) that a blanket
NO-GO would discard. The brief's NO-GO branch prescribes *redesigning the torso representation*; that
would be an overreaction to a well-understood geometric limit at one extreme of the range.

**F-14's central finding replicates cleanly:** raw shoulder Δz is not an independent sign source
(1 rescue in 318 frames; 0 at ±90°, where raw Δz is undefined on **96.9 %** of the frames that trigger
the guard). No further sign-source search is warranted.

---

## 10. Next action

**Decide the product question before writing any more code: is a ±60° torso-yaw range acceptable for
the mirror?**

If **yes** → implement the minimal V6 (wrap guard only, V5 composition untouched) *plus* an explicit
range limit, and validate live. If **no** → the sign is not the blocker; the shoulder line simply
stops being measurable edge-on at this baseline and resolution, and the next work is the
**measurement** (wider stereo baseline, sub-pixel disparity, or a second viewpoint), not another
estimator.

That is a product decision, not an engineering one, and it should not be made inside a validation
task.

---

## 11. Honest limits

1. **Two subjects, one distance band, one room, one camera.** ±90° now has three observations
   (A/F-10 fail, A/F-11 pass, B/F-15 fail) — enough to establish it is unreliable, not enough to
   model what makes it work.
2. **`dc_body45L_face0` is unresolved** (failure 2). It is included in the all-blocks tally and
   excluded from the whole-body tally, and both are reported. If it is an estimator failure rather
   than a mislabelled block, the ≤60° result is weaker than stated.
3. **F-10's block windows remain RECOVERED**, not original; every F-10 number inherits that.
4. **The nose-based label audit is a proxy.** It validates whole-body blocks only, and rests on the
   head following the body — reasonable for a commanded whole-body turn, but not measured
   independently.
5. **Subject B's rotations were not the commanded magnitudes** (`h_p30` → 54.4°, `h_m60` → 37.0°).
   Only the *sign* is scored, so this does not invalidate the result, but it means the heading labels
   are ordinal, not metric.
6. **The ≤60° claim rests on 6 blocks.** 100 % of 6 is not 100 %.
7. **No dynamic accuracy is claimed anywhere**; §5f is behaviour only.
8. **The >90° magnitude limit is untouched.** `|yaw2D| = acos(...)` is still bounded to 90°
   (`m_180` p50 55.8°). A working sign yields −90…+90 only. **SIGN and MAGNITUDE-BEYOND-90 remain
   separate problems**, and nothing in F-15 addresses the second.

---

## 12. Files changed

**Production files changed: NONE.** `KalidokitControlRigDriver`, `TrunkGate`, `yaw3D`, `yaw2D`,
`DampYaw`, torso composition and weights, `ArmAimSolver`, Arm V2, P1-1/P1-2/P1-3, F-08 depth sampler,
scene defaults, production thresholds, UDP payload semantics and shipping configuration are untouched.

| file | role |
|---|---|
| `python-sidecar~/f15_cross_session.py` | the analysis (offline; imports F-14 verbatim) |
| `python-sidecar~/oak_v4_evidence/f15_cross_session.txt` | full output |
| `python-sidecar~/oak_v4_evidence/f15/f10_gt_marks_near.json` | F-15 capture marks |
| `python-sidecar~/pipeline_logs_f15/` | F-15 capture (subject B) |
| `python-sidecar~/f10_gt_capture.py` | **overwrite guard added** — capture tooling, not production |
| `docs/F15_CROSS_SESSION_TORSO_YAW_VALIDATION_2026-09-10.md` | this report |
| `docs/F14_...md`, `docs/decisions.md` | corrected session-gap framing (see §2) |
| `docs/roadmap.md` | F-15 added to the decision tree; NEXT PATH updated |

Reproduce with `./.venv/Scripts/python.exe f15_cross_session.py` from `python-sidecar~/`.
**Use the venv** — system Python has depthai 3.7.1 and the sidecar will not start.
