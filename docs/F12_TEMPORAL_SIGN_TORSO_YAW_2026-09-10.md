# F-12 — Temporal Sign Continuity for Torso Yaw

**Verdict: NO-GO.**

Temporal continuity cannot supply the sign, because **the information is not in `|yaw2D|` to begin
with**. At a zero crossing the magnitude trace for "the subject reversed" and "the subject returned to
the same side" is identical, so the decision is made by a parameter rather than by the data — and the
parameter sweep proves it: static accuracy swings **75.4 percentage points from a single ±1 step in the
debounce count**, and reaches 0.00 % at one setting and 96.01 % at another.

**No production behaviour was changed.** No V6 estimator was implemented.

---

## 1. Objective

Determine whether the validated 2-D magnitude `|yaw2D|` can be given a left/right sign by tracking sign
continuity and zero crossings, without face landmarks, depth sign, head orientation or ML.

---

## 2. Evidence Used

| source | role |
|---|---|
| F-10 (`pipeline_logs_f10_near`) | 20 blocks — static headings, twist, **and the 8 motion blocks** |
| F-11 (`pipeline_logs_f11`) | 11 blocks — static headings + head/torso decoupling |

Established previously and **not re-tested**: `yaw2D` magnitude MAE **5.90°** vs production `yaw3D`
**26.70°** (F-10); depth sign unsuitable (F-10); face sign NO-GO — it measures head yaw (F-11).

### 2a. The F-10 marks were destroyed, and recovered

The F-11 capture was run with `--distance near` and **overwrote `f10_gt_marks_near.json`**, destroying
the F-10 block windows — including every motion block this task needs. That was an operator error in
the previous task.

The frame data survived and the protocol was deterministic, so the boundaries were **reconstructed**
rather than recaptured: a two-parameter search (start offset, clock stretch) maximising a contrast
objective that uses only the *structure* of the static blocks — "do the wide-shoulder frames land where
0° is expected and the narrow-shoulder frames where ±90° is expected" — never their heading values.

**Accepted only against an independent check the search does not optimise for**: reproducing the
per-block medians already published in `f10_near_analysis.txt`.

| block | published | recovered | Δ |
|---|---|---|---|
| `h_0` | 0.0° | 1.8° | 1.8 |
| `h_p45` | 45.0° | 48.8° | 3.8 |
| `h_p90` | 80.7° | 82.4° | 1.7 |
| `h_0b` | 4.2° | 3.8° | 0.4 |
| `h_m45` | 39.3° | 39.2° | 0.1 |
| `h_m90` | 88.5° | 87.3° | 1.2 |
| `h_0c` | 0.0° | 0.0° | 0.0 |

Alignment: offset 100.75 s, stretch **1.000**, all seven blocks within 3.8°. **RECOVERY ACCEPTED.**
Marks written to `f10_gt_marks_near_RECOVERED.json` and flagged `"RECOVERED": true`.

---

## 3. Available Signals

Per frame, recoverable from the archives with no new logging: `seq`, `t`, shoulder pixel coordinates
(`audit_log.j`), `hipZ` (`sender_log`), hence shoulder span and `|yaw2D| = acos(span·Z / K)` with `K`
calibrated on the 0° blocks. Δt and Δ|yaw2D| are therefore available frame to frame. F-10: 32,974
frames, 2,478 labelled. F-11: 22,389 frames, 1,457 labelled.

**No diagnostic change was required.**

---

## 4. Temporal Sign Algorithms

All four operate on `|yaw2D|` alone.

| id | rule |
|---|---|
| **A** | sign hold; flip once the magnitude has entered a near-zero band and re-emerged |
| **B** | A, with the band `Z` as an explicit parameter |
| **C** | B plus an `N`-frame debounce before a flip is accepted |
| **D** | continuity cost — pick `+|yaw2D|` or `−|yaw2D|`, whichever is closer to the previous signed state |

---

## 5. Parameter Sweep

Z ∈ {3, 5, 8, 10, 15}°, N ∈ {1…6}. Static sign accuracy, algorithm C:

| Z | N=1 | N=2 | N=3 | N=4 | N=5 | N=6 | range | worst ±1 jump |
|---|---|---|---|---|---|---|---|---|
| 3° | 62.44 | 38.23 | 62.80 | **0.00** | 62.44 | 38.23 | 62.8 | 62.8 |
| 5° | 75.42 | 56.75 | 62.80 | 38.01 | 43.25 | 19.34 | 56.1 | 24.8 |
| **8°** | 60.96 | 4.13 | 79.48 | **96.01** | 57.86 | 59.93 | **91.9** | **75.4** |
| 10° | 60.96 | 23.32 | 58.08 | 41.25 | 58.23 | 60.22 | 37.6 | 37.6 |
| 15° | 42.29 | 79.41 | 41.11 | 41.03 | 40.96 | 40.89 | 38.5 | 38.3 |

Algorithm D (continuity cost): **53.58 %** overall — positive turns 100.00 %, negative **0.00 %**. It
never flips, so it simply reports the initial sign forever.

**This table is the central result.** A single ±1 change in the debounce count moves accuracy by up to
**75.4 points**, and one setting scores **0.00 %**. That is not a method with a tuning optimum; it is
the flip *count* accidentally aligning with the protocol's own alternation. The 96.01 % peak is
surrounded by 79.48 % and 57.86 %.

Per the brief: no configuration was cherry-picked. The Pareto front does not exist — the surface is
noise.

---

## 6. Static Ground-Truth Results

Best configuration by static accuracy (C, Z=8°, N=4), pooled F-10 + F-11:

| metric | value |
|---|---|
| overall static sign | 96.01 % |
| positive turns | 100.00 % |
| negative turns | 91.41 % |
| +90° | 100.0 % |
| −90° | 100.0 % |
| hybrid MAE | 9.92° |
| hybrid RMSE | 21.14° |
| wrong-sign frames | 2.63 % |

**These numbers should not be believed**, for the reason in §5 and §8: they are produced by a
configuration whose neighbours score 79 % and 58 %, on a protocol where every designed zero visit is
followed by a reversal.

---

## 7. Dynamic / Reversal Results

Best configuration, F-10 motion blocks:

| block | n | \|yaw2D\| p50 | max | sign flips | **flips/second** |
|---|---|---|---|---|---|
| `m_slow` | 172 | 45.9° | 71.1° | 5 | **0.84** |
| `m_normal` | 138 | 54.7° | 83.8° | 3 | 0.60 |
| `m_fast` | 130 | 56.2° | 89.6° | 3 | 0.60 |
| `m_rev_lr` | 113 | 58.7° | 88.3° | 2 | 0.40 |
| `m_rev_rl` | 114 | 53.7° | 89.6° | 5 | **1.01** |
| `m_180` | 139 | 18.2° | 83.7° | 4 | 0.67 |
| `m_turn_arms` | 124 | 50.3° | 78.6° | 4 | 0.80 |
| `m_turn_still` | 124 | 57.3° | 83.1° | 4 | 0.81 |

The subject was reversing at most about once every two seconds. The algorithm flips **0.40–1.01 times
per second** — i.e. at roughly the rate the magnitude wanders, not the rate the body reverses. On
`m_rev_rl` it produced 5 flips in ~5 s.

Instantaneous ground truth does not exist inside a motion block, so these are reported as **transition
counts, not accuracies**. No frame-by-frame truth was invented.

---

## 8. Zero-Crossing Analysis

**The structural finding, and the reason for the verdict.**

The recorded protocol contains exactly **two designed zero visits** (`h_p90 → h_0b → h_m45`, and
`h_m90 → h_0c → tw_rigid_p45`), and **both are reversals**. The case that would discriminate — magnitude
returns to zero and then goes back to the **same** side — was **never recorded, in any capture**.

That matters more than it first appears:

* A rule as trivial as *"flip the sign every time the magnitude dips near zero"* scores near-perfectly on
  this data **by construction**.
* So this dataset cannot separate a working method from that trivial rule — and no parameter search over
  it means anything.

And the deeper point does not depend on the dataset at all: for a subject at +45° who returns to 0°,
`|yaw2D|` traces `45 → 0`. Whether they then go to −45° or back to +45°, the magnitude traces `0 → 45`
**identically**. The two futures are the same signal. **No causal filter over `|yaw2D|` can distinguish
them**, because the distinguishing information was destroyed when the sign was discarded.

**False crossings are *not* the problem.** During blocks held at ±45°/±90°, the magnitude dips into the
zero band on only:

| Z | frames inside the band while held away from 0 |
|---|---|
| 3° | 28 / 1355 (2.07 %) |
| 5° | 28 / 1355 (2.07 %) |
| 8° | 30 / 1355 (2.21 %) |
| 10° | 33 / 1355 (2.44 %) |
| 15° | 34 / 1355 (2.51 %) |

So the failure is not noise sneaking across the band. It is genuine ambiguity at genuine crossings.

---

## 9. 180° Analysis

`|yaw2D| = acos(clamp(span·Z / K))` is **bounded to [0°, 90°]** by construction. The shoulder span
narrows from 0° to 90° and then **widens again** from 90° to 180°.

Measured on `m_180`: `|yaw2D|` p50 **18.2°**, p95 74.2°, **max 83.7°** — it never exceeds 90°, as the
arithmetic requires.

**A 180° turn and a 90°-and-back produce the same magnitude trace: `0 → 90 → 0`.** The information is
lost in the *magnitude estimator*, before any sign logic runs. Temporal continuity cannot recover it,
and neither could a perfect sign source — this is a limit of the 2-D foreshortening measurement itself,
and it applies to F-10's validated magnitude too.

---

## 10. Head-Independence Check

Passes, by construction: `|yaw2D|` is computed from the **shoulder** pixel span and `hipZ` only, so head
motion cannot enter it. Confirmed on the F-11 decoupling blocks (torso 0°, head ±45°):

| block | n | \|yaw2D\| p50 | p95 | sign flips |
|---|---|---|---|---|
| `dc_body0_head45R` | 147 | 4.5° | 10.0° | 2 |
| `dc_body0_head45L` | 151 | 9.9° | 12.0° | 4 |

The magnitude correctly stays small when only the head moves — the F-11 failure does not recur.

**But note what those flip counts mean**: with the torso genuinely at 0°, the magnitude sits inside the
zero band, and the algorithm flipped **2 and 4 times in ~10 seconds**. A user standing still facing the
mirror — the single most common pose — would have their avatar's torso sign oscillating. That is the
brief's explicit NO-GO condition *"sign becomes arbitrary when magnitude is small"*, observed directly.

---

## 11. Signed Hybrid Results

| estimator | MAE | RMSE | p95 | wrong-sign frames |
|---|---|---|---|---|
| production `yaw3D` (F-10) | 61.08° | 104.92° | 270.00° | 21.5 % |
| depth-sign hybrid (F-10) | 13.09° | 25.46° | 78.07° | — |
| face-sign hybrid (F-11) | 31.60° | 59.21° | 166.82° | — |
| **temporal hybrid, best config** | **9.92°** | 21.14° | — | 2.63 % |
| temporal hybrid, Z=8 N=3 | 17.39° | 32.34° | — | 13.51 % |
| temporal hybrid, Z=8 N=5 | 41.95° | 74.95° | — | 27.76 % |
| `yaw2D` magnitude alone, unsigned (F-10) | 5.90° | 9.28° | 17.37° | n/a |

The best row beats everything — and its two immediate neighbours are 17.39° and 41.95°. **Quoting
9.92° as the method's performance would be selecting the winner after seeing the answers**, which §15
of the brief explicitly forbids. The honest summary of the sweep is that the hybrid MAE ranges from
**9.92° to 84.21°** depending on a parameter the data cannot determine.

---

## 12. Failure Cases

1. **Reversal vs return is undecidable** (§8). The defining case. Structural, not a tuning problem.
2. **Sign is arbitrary at small magnitude** (§10). 2–4 flips per 10 s with the torso genuinely at 0° —
   the commonest pose in the target application.
3. **180° is ambiguous in the magnitude itself** (§9), before the sign is even considered.
4. **Chaotic parameter sensitivity** (§5). 75.4-point swing from N → N+1; 0.00 % at one setting.
5. **Flip rate tracks magnitude jitter, not body reversals** (§7): 0.40–1.01 flips/s against a subject
   reversing at most ~0.5/s.

Explicitly tested and **not** a problem: false crossings from noise (2.07–2.51 %, §8) and head
dependence (§10).

---

## 13. Safety Analysis

* **No production files changed.** Offline analysis only; no diagnostic logging was needed either.
* **`yaw3D` was never used to determine the correct sign**; F-09's circularity avoided.
* **Face signals were not used**, per the brief.
* **Ground truth used only for scoring** and for the single `K` constant (calibrated on the 0° blocks).
* **No per-frame label tuning**, no ML, no classifier.
* The F-10 marks recovery (§2a) is validated against previously-published numbers it did not optimise
  for, and is flagged `"RECOVERED": true` in the file so it can never be mistaken for original data.

---

## 14. Verdict

## NO-GO

Against the brief's NO-GO conditions — **five of six are met**:

| condition | met? | evidence |
|---|---|---|
| sign cannot be maintained through genuine reversals | ✅ | reversal vs return is undecidable from `\|yaw2D\|` (§8) |
| false reversals occur frequently | ✅ | 0.40–1.01 flips/s in motion (§7) |
| **sign becomes arbitrary when magnitude is small** | ✅ | 2–4 flips per 10 s at true 0° (§10) |
| **±180° remains fundamentally ambiguous** | ✅ | magnitude saturates at 90° by construction (§9) |
| continuity creates unacceptable lag or wrong-direction persistence | ➖ | not the limiting factor |
| **cannot distinguish a true reversal from magnitude noise** | ✅ | 75.4-point swing from one debounce step (§5) |

The 96.01 % / 9.92 ° best case is not evidence of a working method. It is one point on a chaotic
surface, measured on a protocol in which every zero visit happens to be a reversal.

---

## 15. Recommendation

**Do not implement V6 with a temporally-derived sign.** Do not tune it further — the sweep in §5 shows
there is nothing to tune toward.

**Three sign sources have now been tested and refused**: depth (F-10, 78.5 %), face (F-11, measures head
yaw), temporal continuity (F-12, information absent). What they have in common is that each tried to
recover the sign *after* the magnitude estimator had discarded it.

**The remaining candidate — and the one that does not share that flaw — is the hip-line depth sign**
(F-11 §14, still untested). The hips do not rotate with the head, and sign is 1 bit rather than a
magnitude, so disparity quantisation is far less damaging than F-09 showed it to be for magnitude. It
is testable offline on the F-10 and F-11 captures already in hand — **no new subject time**.

**Also worth recording, because it outlives the sign question**: §9 shows the 2-D magnitude estimator
itself is ambiguous beyond 90°. Even a perfect sign source would leave `yaw2D` unable to distinguish
a 120° turn from a 60° one. If the product needs beyond-90° torso tracking, the magnitude estimator
needs revisiting too — which is a larger question than the sign.

---

## 16. Files Changed

**Production behaviour: none. No production or diagnostic runtime file was modified.**

| file | role |
|---|---|
| `python-sidecar~/f12_recover_f10_marks.py` | reconstructs the overwritten F-10 block windows, with validation |
| `python-sidecar~/f12_temporal_sign.py` | the four algorithms, sweep, dynamic, zero-crossing, 180°, head-independence |
| `oak_v4_evidence/f10_gt_marks_near_RECOVERED.json` | recovered F-10 marks, flagged as recovered |
| `oak_v4_evidence/f12_recovery.txt`, `f12_temporal.txt`, `f12_stability.txt` | output |

**Unchanged, verified:** `KalidokitControlRigDriver`, `TrunkGate`, `yaw3D`, `yaw2D`, `DampYaw`, torso
weights, `ArmAimSolver`, Arm V2, P0/P1-1/P1-2/P1-3, scene defaults, production thresholds, UDP payload
semantics, shipping configuration.

---

## Honest limits

1. **The decisive case was never recorded.** No capture contains "magnitude returns to zero, subject
   goes back to the same side". §8's conclusion for that case rests on a structural argument — the two
   futures produce identical signals — not on a measurement. The argument is sound but it is an
   argument.
2. **The F-10 marks are reconstructed** (§2a), not original. They validate to within 3.8° on all seven
   static blocks, but every F-10 number in this report inherits that reconstruction.
3. **Motion blocks have no instantaneous ground truth**, so §7 reports transition counts only.
4. **One subject, one distance, one room.**
5. **`|yaw2D|` here is unfiltered**, whereas production `yaw3D` passes through the one-euro filter, so
   §7's flip rates are not a like-for-like comparison with production jitter.
6. **Algorithm D was tested at a single Z**, since it does not use the band; its 53.58 % (never flipping)
   makes further sweeping pointless, but that is an inference, not an exhaustive search.
