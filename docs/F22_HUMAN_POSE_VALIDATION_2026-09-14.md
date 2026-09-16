# F-22 — Human Pose Validation / Biomechanical Validation Layer

```text
VERDICT: CONDITIONAL
STATUS:  OFFLINE ENGINEERING COMPLETE - LIVE HANDS-NEAR-FACE ACCEPTANCE PENDING
```

> **2026-09-14 offline completion pass — §23–§29 supersede parts of what follows.**
> The headline change: **§15's distribution table was computed in a mixed-unit pixel space and its
> "degrees" were not real degrees.** It has been recomputed with a proper metric lift (§25) and the
> old table is marked superseded rather than deleted. The second change is better news — the
> absolute-angle REJECT path, which §20.3 correctly listed as never having been exercised against a
> positive case outside unit tests, **has now fired on real footage**, and every instance has been
> classified rather than counted (§26).

The validator is implemented, unit-proven (22/22 plus 120/120 adversarial, §24), and produces real,
honest evidence from offline replay against three real clips: on legitimate single-person motion it
produced **zero absolute-angle rejections** with 9.9°/16.5° of headroom to the 160° REJECT line, and
exactly one single-frame rate-based hold that recovered on the very next frame (§25 — these figures
supersede the mixed-unit ones originally quoted here). **No live human was available this session**
(stated explicitly by the user) — this is why the verdict is CONDITIONAL, not PASS: the brief's own
§29 rules require replay *and* live evidence to agree, and specifically a live L2 (hands-near-face)
reproduction of F-19's own defect, which has not yet happened against this validator.

---

## 1. Problem statement

F-19 found the last unmitigated live defect in the tracking→retargeting path: with hands near the
face, the rendered VRM elbow bone reached a **median** of 167.8°/169.4° (L/R) and a max of
177.5°/179.6° — anatomically impossible (a real elbow's flexion ceiling is ~145-150°) — while P0
LimbGate held **0.00%** of that block, because the tracking *confidence* was high even though the
*angle* was not. F-19's own §14 named this as future work: "hard flexion clamp ≈150°, applied before
the control rig." Nothing between P1-1 and the UDP send (or anywhere in Unity) currently checks
whether a measurement is a physically plausible human pose. F-22 closes that gap.

## 2. Evidence motivating F-22

F-19's finding is real, live, production-camera evidence (not a synthetic/replay artifact) — the
brief's own instruction to trust it is followed directly rather than re-litigated. What is separated
explicitly, per the brief's §2 requirement:

- **TRUE HUMAN-POSE VIOLATION**: a real elbow physically cannot reach 177-180° bend. This part is
  unambiguous regardless of root cause.
- **TRACKING/DEPTH ARTIFACT, not a human-pose violation on its own**: F-19's own report notes this
  was the block's *median*, not an occasional spike — "hands near the face" is a configuration where
  a hand occludes itself against the head, which very plausibly degrades this specific tracker's
  depth/keypoint quality rather than reflecting the tracker faithfully reporting normal human
  variation. F-22 does not need to resolve *which* (that is a separate investigation); an
  anatomically-impossible-looking angle should be suppressed regardless of root cause, and that is
  exactly what an angle-based validator does without needing to diagnose the cause.
- **TEMPORARY OBSERVATION UNCERTAINTY**: this offline replay's own rate-check trips (§16 below) are a
  concrete instance — three single-frame angular-velocity spikes that recovered immediately, most
  plausibly attributable to the replay's z-proxy (no real depth for an arbitrary video) being noisier
  than production's real OAK-D depth, not a true human-pose violation.

## 3. Current architecture audit (verified from source)

```text
CURRENT VALIDATION LAYERS:
  P0 LimbGate (Runtime/Retargeting/LimbGate.cs) - Valid/Held, PURELY confidence-threshold gated
    (Step(), L102-126). Zero geometry anywhere in the file.
  P1-1 JointTracker (python-sidecar~/joint_tracker.py) - PER-JOINT Cartesian plausibility only
    (residual/velocity/acceleration vs. prediction + a lightweight parent-distance/segment-length
    check, _suspicion() L355-437). Zero angle checks anywhere - confirmed, zero hits for "angle".
  P1-3 PoseBuffer (Runtime/Core/PoseBuffer.cs) - pure temporal interpolation (position + confidence
    lerp only). Zero angle math.
  Arm V2 / ArmAimSolver (Runtime/Retargeting/ArmAimSolver.cs) - ALREADY COMPUTES the elbow bend angle
    every frame (BendDeg = Vector3.Angle(upper, fore), L289, control-rig rest frame) but only as a
    diagnostic - never read back to gate or clamp anything. This is F-19 SS14's own named insertion
    point.
  Torso V5/V6 (TrunkGate.cs / TorsoYawGuard.cs) - geometric, but scoped entirely to torso YAW
    composition. No elbow/knee logic.
  P1-4 KinematicRecovery - REJECTED for production (docs/P1_4_CLOSEOUT_2026-09-08.md). Root cause:
    its BONE-LENGTH signal's natural variation (p99=1.79x) overlaps the corruption it tries to
    detect on this hardware - the safe threshold band is empty. Its ANGLE-only component was flagged
    as comparatively trustworthy in the closeout - a partial precedent FOR an angle-based approach.
  F-08 depthQuality - measured, explicitly ADVISORY, consumed by nothing today.

CURRENT FAILURE PROTECTION: confidence gating + per-joint Cartesian plausibility. Neither is a
  biomechanical/angle check - F-19's impossible elbow travelled the full path un-suppressed.

CURRENT GAPS: no angle/flexion validation anywhere; no angular-velocity/temporal-angle check; no
  chain-level (shoulder-elbow-wrist) consistency check.

PROPOSED F-22 INSERTION POINT: python-sidecar~/wholebody_udp_sender.py, between P1-1's skel.update()
  and build_body_landmarks() - confirmed empty by the audit. Reuses P1-1's OWN proven contract for
  signalling an invalid joint (conf_emit[joint] = 0.0), which P0 LimbGate already correctly consumes.
```

## 4. Insertion point

Implemented exactly as audited: `wholebody_udp_sender.py`, immediately after the P1-1/P1-4 block
(final `xyz_cam`/`measured`/`conf_emit`, i.e. whatever is actually about to be sent) and before
`build_body_landmarks()`. Zero `Runtime/` changes, zero UDP contract changes — the same minimal-
footprint pattern F-21 used successfully.

## 5. Pose representation

Raw camera-space metres (`xyz_cam`), COCO-WholeBody indices: shoulder 5/6, elbow 7/8, wrist 9/10,
hip 11/12, knee 13/14, ankle 15/16 (L/R). `measured[]`/`conf_emit[]` carry per-joint validity,
already established by P1-1.

## 6. Human-pose model

`python-sidecar~/pose_validation.py` — pure, clock-injected, no I/O (same shape as
`joint_tracker.py`/`target_ownership.py`). One `ChainValidator` per chain family (elbow, knee), one
instance per side. Bend-angle convention matches `ArmAimSolver.BendDeg` exactly (0° = straight,
→180° = folded back), so F-19's own already-cited threshold language applies to this raw signal
unchanged.

## 7. Joint validation

Per chain, per frame, in order: (1) all three chain points measured — else `TRACK_LOST`; (2) all
finite — else `NONFINITE`; (3) the hinge joint's own confidence ≥ 0.3 (reuses the existing `--conf`
gate) — else `INVALID_CONFIDENCE`; (4) segment lengths non-degenerate (near-zero → `NONFINITE`,
doubling as the "segment continuity" check); (5) absolute bend angle ≤ REJECT — else `ANGLE_LIMIT`;
(6) angular rate ≤ IMPOSSIBLE — else `ANGLE_RATE_IMPOSSIBLE`. Every non-pass carries one of these five
explicit reasons — never collapsed into a single "low confidence" bucket (brief §21).

## 8. Chain validation

The bend angle itself **is** the shoulder-elbow-wrist (or hip-knee-ankle) chain-consistency check — a
single bad depth sample dragging the wrist into a geometrically impossible position manifests
directly as an implausible bend angle. No separate chain-geometry check is layered on top (unit test
`t11`, a synthetic bad-wrist-depth-sample, confirms this is caught). No additional constraint was
invented beyond what F-19's evidence supports (brief §2's explicit instruction).

## 9. Temporal validation

Two-tier, both independent of how smoothly a bad value was reached (brief §15's explicit warning):
**absolute** plausibility (the angle itself, unconditional) and **rate** plausibility (angular
velocity between consecutive accepted frames). A slow, smooth 1°/frame ramp into an impossible angle
is still rejected the instant it crosses the absolute threshold (unit test `t12`) — temporal
smoothness alone never establishes validity. `WARN`-band crossings (angle > 150° but ≤160°, or rate >
900°/s but ≤1800°/s) are logged as diagnostic `CHAIN_WARN` events but do **not** block emission — this
is deliberately less granular than the brief's suggested four-tier NORMAL/FAST_BUT_PLAUSIBLE/
SUSPICIOUS/IMPOSSIBLE rate ladder; a true SUSPICIOUS-only downgrade tier was not implemented because
the bounded `HELD` window (§10) already provides the "continuous degradation" a separate tier would —
stated as a deliberate simplification, not hidden.

## 10. Biomechanical validation

| chain | WARN | REJECT | source |
|---|--:|--:|---|
| elbow | 150° | 160° | F-19 §14's own words, adopted directly: "a human elbow reaches ~150°"; REJECT is a 10° margin above it |
| knee | 165° | 178° | F-19 itself measured a LEGITIMATE max of 176° during ordinary walking (rare, 30/85k frames) — REJECT must sit above that observed legitimate value or it would false-reject real walking; set 2° above it |
| rate (both) | 900°/s | 1800°/s | starting values, reasoned, then checked against §16's own real distribution below |

**States**: `VALID / HELD / REJECTED` — matching P0 LimbGate's own downstream vocabulary (F-22's
output feeds the identical consumption path LimbGate already has), not P1-1's TRACKED/WEAK/PREDICTED/
LOST (a different, position-scoped vocabulary — reusing it verbatim would blur which layer made which
call). `HELD` is bounded (8 frames, mirrors P1-1's own reacquire-frame convention); past that, a
still-failing joint becomes `REJECTED`. Both zero `conf_emit` identically for the sender — the
distinction is diagnostic severity, not sender behaviour. Recovery to `VALID` is immediate on the next
passing frame (no confirm-frames needed — unlike F-21's identity re-lock, there is no "wrong person"
analogue here to guard against).

## 11. Calibration decision

**Option A (no calibration) was chosen.** All thresholds are normalized angles and rates — no
absolute body-scale assumption, no per-user calibration pose, matching brief §8/§9's explicit
preference against hardcoding one person's measurements or requiring a T-pose from arbitrary public
visitors. Bone-LENGTH-ratio calibration (Option B) was deliberately not pursued, consistent with §5's
own audit finding that P1-4's bone-length signal sits inside this hardware's noise floor.

## 12. Threshold derivation

Elbow/knee absolute thresholds: adopted directly from F-19's own already-published, already-vetted
live evidence (§10 table) — not re-derived from scratch, since re-deriving a number the project has
already measured live would be redundant, not more rigorous. Rate thresholds: reasoned starting
values, checked against this session's own real distribution (§16) rather than left as an unchecked
guess — and the honest limit of that check is stated in §16, per brief §22's explicit instruction to
report when data cannot cleanly separate normal from bad behaviour.

## 13. Replay dataset

One clip used this session: `video.webm` (720×1280, 30 fps, 14.4 s, 436 frames), an energetic
full-body solo dance — legitimate arm/leg motion including fast reaches, turns, and quick direction
changes, but no "hands near face" segment. This gives strong evidence on the false-positive question
(§16/§17) and on ordinary-motion elbow/knee ranges, but **not** a direct reproduction of F-19's own
hands-near-face defect — that remains a live-test-only gap (§20).

## 14. Baseline results

"Baseline" here is the same as "F-22 disabled": a frame counts as baseline-impossible if its
*absolute* angle alone exceeds REJECT, independent of whether F-22 actually suppressed it. Measured:
**0 baseline-impossible frames** across 431 owned frames on this footage — this specific clip never
produced an absolute-angle violation, so the baseline/F-22-enabled comparison the brief's §23 asks for
is, on this clip, "0 vs. 3," and the 3 are rate-based, not absolute-angle-based (§16).

## 15. F-22 results — ⚠️ SUPERSEDED BY §25, kept for traceability

> **These numbers are not in real degrees.** `f22_video_replay.py` places each keypoint at
> `(image_u, image_v, zrel * 200)` — two pixel axes and a third in arbitrarily-scaled metres. An
> angle computed in a mixed-unit space is not an angle. The table below is internally consistent but
> not comparable to the shipped thresholds, which are stated in real degrees. §25 recomputes it with
> a proper pinhole metric lift; **use §25 for any threshold reasoning.** This table stays because it
> was cited in the original verdict block, and deleting it would hide a correction rather than make
> one.

| chain | n | bend p50 | bend p95 | bend max | rate p50 (°/s) | rate p95 (°/s) | rate max (°/s) | suppressed |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| left_elbow | 431 | 36.0° | 150.9° | 155.3° | 95 | 521 | 1814 | 1 |
| right_elbow | 431 | 37.2° | 147.9° | 158.8° | 95 | 731 | 2151 | 2 |
| left_knee | 431 | 17.0° | 38.7° | 46.8° | 66 | 296 | 476 | 0 |
| right_knee | 431 | 18.7° | 35.6° | 47.9° | 74 | 286 | 426 | 0 |

The original reading — "elbow max 155.3°/158.8° sits within 1-5° of the 160° REJECT line, so the
threshold is exercised but never crossed" — does **not** survive §25. Recomputed in real degrees the
same clip's elbow maxima are 150.1°/143.5°, i.e. **9.9°/16.5° of headroom**, and it contains no
absolute violation anywhere. The qualitative half does survive, and more strongly: knees are clean
and nowhere near their threshold (real max 49.0° against a 178° REJECT).

## 16. False-positive results

**All 3 suppressed frames were rate-based (`ANGLE_RATE_IMPOSSIBLE`), none were absolute-angle
rejections.** Each recovered on the very next frame (`CHAIN_RECOVERED` one frame later in every
case) — a single 33 ms hold, imperceptible to P0 LimbGate's own hold-last-valid-rotation behaviour.
0.7% of owned frames touched (3/431), zero of those were sustained.

**Honest caveat, stated plainly per brief §22:** this replay has no real depth channel — z comes from
the model's own root-relative `zrel` (see `f22_video_replay.py`'s module docstring), which is noisier
than production's real OAK-D-measured depth. The three rate spikes (1814-2151°/s) most plausibly come
from that z-proxy's own noise on fast frames, not from genuinely implausible human motion or from
production's real signal — this cannot be fully separated from "legitimate very fast motion" using
this dataset alone. The data also shows the `WARN`-tier rate threshold (900°/s) triggers *frequently*
during ordinary energetic dancing (dozens of `CHAIN_WARN` events) — confirming that a stricter
SUSPICIOUS-downgrades-to-HELD tier, had it been implemented, would have been measurably too aggressive
on legitimate fast human motion; keeping WARN strictly non-blocking (§9) is validated by this data,
not just a design preference.

## 17. Live validation

**Not performed this session — no human was available (explicit user instruction).** All of L1-L7
remain open, most importantly **L2 (hands-near-face)**, since that is the only test that can directly
confirm the raw-geometry threshold (calibrated from F-19's *rendered* evidence, §12) produces the same
correct behaviour on the *raw* pre-retargeting signal this module actually consumes.

## 18. Performance

No `Runtime/` change, no UDP change — F-19/F-20A/F-20B/F-21 baselines are unaffected by construction.
Sidecar-side cost is four `ChainValidator.update()` calls per frame (closed-form trig, no I/O) plus an
occasional small JSON line — the same order of magnitude as F-21's own per-frame overhead, which
showed no measurable fps/latency change. Not independently re-measured this session; recommended
alongside the next live session (§20).

## 19. Regression tests

None found. `--no-pose-validation` restores the exact pre-F-22 path (present, not yet A/B-timed
against the enabled path this session). No `Runtime/` file changed, so P0/P1-1/P1-2/P1-3/F-08/Arm V2/
Torso V5/V6/F-20A/F-20B/F-21 are untouched by construction — same argument F-21's own report used
successfully.

## 20. Remaining risks

1. **No live L1-L7 validation** — the reason for CONDITIONAL. L2 (hands-near-face) is the specific,
   named next action: it is the only test that closes the loop back to F-19's original evidence.
2. **The replay's z-proxy (model `zrel`, not real depth) is noisier than production** — the 3 rate-
   based holds observed (§16) may not recur, or may recur differently, against real OAK-D depth.
3. **Only one clip, no "hands near face" segment, was available offline** — the absolute-angle REJECT
   path has real evidence it does NOT over-trigger (0 baseline-impossible frames on legitimate
   footage) but has not yet been exercised against a *positive* case (a real impossible pose) outside
   of unit tests.
4. **Rate thresholds are reasoned, not statistically separated from a matched bad population** — per
   §22's own honesty requirement, stated directly rather than implied to be more rigorous.
5. **CLI threshold overrides were not added this pass** (unlike F-20A/F-20B/F-21's `--*` flag
   pattern) — thresholds live only in `pose_validation.py`'s module constants for now, a scope
   reduction made for time, not hidden.

## 21. Production recommendation

Ship with `--pose-validation` **ON by default** (already the default) — every executed test shows it
either does nothing (no absolute violations in legitimate footage) or recovers within one frame
(the 3 rate-based holds), with zero regressions. Do **not** yet certify it as having closed F-19's own
defect for a public installation until a live L2 (hands-near-face) session confirms the raw-geometry
threshold behaves the way the rendered-geometry evidence predicts it should.

## 22. Final decision

CONDITIONAL: the validator is correctly built, evidence-derived, unit-proven, and shows zero harmful
false positives on ~14 s of real energetic human motion — but it has not yet been exercised against
either a live human or a direct reproduction of the specific defect (hands-near-face) it exists to fix,
which the brief's own §29 rules require before PASS.

---

## 23. Offline completion pass (2026-09-14) — scope

No physical person was available (explicit user instruction). Everything here is deterministic,
replay, or measurement work, aimed at removing every remaining *offline* unknown so the next live
session is acceptance testing rather than development.

```text
new (python-sidecar~/):
  f22_adversarial.py           18 adversarial cases derived from REAL frames, driven through the
                               PRODUCTION insertion point and the REAL downstream builder
  f22_threshold_analysis.py    real-video distributions in real degrees, across all three clips,
                               reported strictly separately from synthetic evidence
  f22_inspect_rejections.py    classifies every absolute-angle rejection found on real footage, and
                               writes annotated frames so the classification is checkable
  f22_perf_ab.py               validation OFF vs ON on identical input - closes SS18's own gap
```

## 24. Adversarial perturbation suite — 120/120 PASS across 18 cases

The gap this closes: `test_pose_validation.py` exercises the validator in **isolation** on synthetic
limbs, and `f22_video_replay.py` exercises real footage that never violates a threshold. Neither
drives the thing that matters — the **production insertion point** and **what the consumer actually
receives** when a chain is suppressed.

Every case proves four stages as one unit, failing if any disagrees:

```text
1 RAW OBSERVATION  the bend angle actually present in the perturbed skeleton, measured back out of
                   the geometry - never assumed from the value that was requested
2 F-22             pose_validation.PoseValidator, the SAME class the sender constructs, unmodified
3 conf_emit        wholebody_udp_sender.py L880-884 reproduced exactly:
                     if _state != PV.VALID: conf_emit[_j] = 0.0
4 DOWNSTREAM       wholebody_udp_sender.build_body_landmarks - the REAL function, imported from the
                   real module - asserting the consumer receives lm[joint] == [0,0,0,0], src == 0,
                   which is what makes Unity's P0 LimbGate hold
```

Base geometry is real: frames from the actual clip through the actual RTMW3D model, lifted to metric
camera space by pinhole backprojection with per-joint depth from the model's own root-relative z
about a nominal 2.0 m hip plane. Perturbation rotates the distal joint about the hinge **in the
limb's own existing bend plane, preserving that frame's real segment length** — only the angle
changes. The one synthetic element is the nominal hip depth standing in for a stereo measurement a
plain video cannot provide, and it is stated rather than buried.

| case | raw bend | F-22 verdict | conf_emit | consumer vis / src |
|---|--:|---|--:|---|
| elbow @150° | 150.0 | VALID | 0.843 | 0.843 / 1 |
| elbow @155° | 155.0 | VALID | 0.843 | 0.843 / 1 |
| elbow @160° | 160.0 | VALID (boundary is inclusive) | 0.843 | 0.843 / 1 |
| elbow @165° | 165.0 | HELD / `ANGLE_LIMIT` | 0.000 | 0.000 / 0 |
| elbow @170° | 170.0 | HELD / `ANGLE_LIMIT` | 0.000 | 0.000 / 0 |
| elbow @175° | 175.0 | HELD / `ANGLE_LIMIT` | 0.000 | 0.000 / 0 |
| elbow @179° | 179.0 | HELD / `ANGLE_LIMIT` | 0.000 | 0.000 / 0 |
| knee @176° | 176.0 | VALID **by design** | 0.833 | 0.833 / 1 |
| knee @178° | 178.0 | VALID **by design** | 0.833 | 0.833 / 1 |
| knee @179° | 179.0 | HELD / `ANGLE_LIMIT` | 0.000 | 0.000 / 0 |
| single-frame spike | 175.0 | HELD (never escalated) | 0.000 | 0.000 / 0 |
| multi-frame violation | 175.0 | HELD ×8 → REJECTED | 0.000 | 0.000 / 0 |
| angular-rate spike | 155.0 | HELD / `ANGLE_RATE_IMPOSSIBLE` | 0.000 | 0.000 / 0 |
| missing elbow | n/a | HELD / `TRACK_LOST` | 0.000 | 0.000 / 0 |
| missing wrist | n/a | HELD / `TRACK_LOST` | 0.000 | 0.000 / 0 |
| invalid confidence | n/a | HELD / `INVALID_CONFIDENCE` | 0.000 | 0.000 / 0 |
| degenerate segment | n/a | HELD / `NONFINITE` | 0.000 | 0.000 / 0 |
| non-finite coordinate | n/a | HELD / `NONFINITE` | 0.000 | 0.000 / 0 |

**No threshold was changed to make anything pass.** The five VALID rows are asserted as VALID because
that is what the shipped thresholds say: 150/155/160 are at or under the elbow REJECT line, and knee
176°/178° are deliberately accepted because F-19 measured 176° as a *legitimate* walking maximum —
rejecting them would be a false positive, not a catch.

Two behaviours worth recording because neither is obvious from the code:

- **`ANGLE_LIMIT` takes precedence over the rate check** (the absolute test short-circuits first), so
  any frame past REJECT reports `ANGLE_LIMIT` regardless of how fast it got there. The rate case is
  therefore only meaningful *below* REJECT, which is how it is constructed here: 20° → 155° in one
  33 ms frame = 4050 °/s, still under the 160° line, caught purely on rate.
- **Recovery from a rate rejection is bounded but not single-frame.** `last_valid_t` advances only on
  an accepted frame, so while a joint is held the measured rate decays as Δ/dt with dt growing one
  frame at a time; it self-heals in `ceil(Δ / rate_limit / DT)` frames with no special-case logic.
  Measured: **2 frames (67 ms)** for a 135° displacement, against an arithmetic bound of 3. Recovery
  from every other rejection type is immediate on the first good frame, as §10 claims.

## 25. Threshold analysis — REAL VIDEO and SYNTHETIC ADVERSARIAL, kept apart

The two populations answer different questions and are never averaged. A combined "accuracy" figure
is **not** computed, on purpose:

- **REAL VIDEO** is entirely legitimate motion, so every rejection is a candidate *false* rejection.
  It can measure false-positive rate and band occupancy. It cannot measure true-positive rate.
- **SYNTHETIC ADVERSARIAL** has a known correct answer by construction. It measures true-positive
  behaviour. It cannot measure false-positive rate.

### REAL VIDEO EVIDENCE — 3 clips, 1,347 owned frames, 4,997 chain evaluations, real degrees

| clip | chain | p50 | p95 | max | headroom to REJECT | warn-band % | reject-band % | rate max °/s |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| video.webm (1 person) | left_elbow | 55.8 | 140.0 | 150.1 | **+9.9** | 0.232 | **0** | 1484 |
| video.webm | right_elbow | 54.6 | 134.3 | 143.5 | **+16.5** | 0 | **0** | 2241 |
| video.webm | left_knee | 21.0 | 40.6 | 49.0 | +129.0 | 0 | 0 | 490 |
| video.webm | right_knee | 23.4 | 40.5 | 50.7 | +127.3 | 0 | 0 | 598 |
| 123.webm (2 people) | left_elbow | 102.2 | 127.5 | 147.8 | +12.2 | 0 | 0 | 3052 |
| 123.webm | right_elbow | 101.9 | 131.3 | **174.4** | **−14.4** | 0 | 0.385 | 3384 |
| 123.webm | left_knee | 39.1 | 94.5 | 117.9 | +60.1 | 0 | 0 | 3620 |
| 123.webm | right_knee | 36.3 | 91.2 | 136.7 | +41.3 | 0 | 0 | 5981 |
| 456.webm (8 people) | left_elbow | 80.9 | 146.2 | **163.5** | **−3.5** | 2.373 | 1.017 | 2977 |
| 456.webm | right_elbow | 80.5 | 147.1 | **171.3** | **−11.3** | 1.190 | 2.778 | 2726 |
| 456.webm | left_knee | 31.2 | 60.7 | 102.8 | +75.2 | 0 | 0 | 1811 |
| 456.webm | right_knee | 28.8 | 63.5 | 92.5 | +85.5 | 0 | 0 | 2559 |

**The single most important row is the first clip.** `video.webm` is the only genuinely single-person
footage available, and across 435 frames it produced **zero absolute-angle rejections** — with 9.9°
and 16.5° of headroom on the elbows. Every absolute violation in the whole dataset came from
multi-person footage.

Total suppression is 459 of 4,997 chain evaluations (9.19 %), and that number needs decomposing
because it flatters F-22's activity enormously:

```text
TRACK_LOST               391  (85.2%)  joints not measured at all - a pre-existing confidence-gate
                                        condition F-22 merely reports. These joints were already
                                        being dropped by build_body_landmarks' own conf threshold.
ANGLE_RATE_IMPOSSIBLE     56  (12.2%)  F-22's own contribution
ANGLE_LIMIT               12   (2.6%)  F-22's own contribution
```

F-22's *own* suppression on real footage is therefore **68 / 4,997 = 1.36 %**, not 9.19 %. Hold
episodes: `video.webm` 1 episode of 1 frame (33 ms); `123.webm` 56 episodes, p50 2 frames, max 103
frames (1.72 s); `456.webm` 76 episodes, p50 1 frame, max 13 frames (433 ms). The long holds are all
`TRACK_LOST` runs in crowded frames, not angle judgements.

### SYNTHETIC ADVERSARIAL EVIDENCE — 18 constructed cases, 120/120 assertions

```text
correctly SUPPRESSED : 13   ANGLE_LIMIT 7, NONFINITE 2, TRACK_LOST 2, INVALID_CONFIDENCE 1,
                            ANGLE_RATE_IMPOSSIBLE 1
correctly PASSED     :  5   elbow 150/155/160, knee 176/178 - accepted because the shipped
                            thresholds say so, not because a threshold was moved
```

## 26. Every real-video rejection, classified — not counted

§20.3 of this report correctly said the absolute-angle REJECT path "has not yet been exercised against
a *positive* case outside of unit tests". It now has been, 12 times, and a bare count would have been
worthless — so each one was classified with a measurement and checked against a picture.

**The discriminator.** The offline lift takes depth from the model's `zrel`; production measures depth
with real stereo. So an "impossible" 3D angle can arise two ways, and they separate cleanly: if the
**2D image-plane** bend is also extreme, the forearm really is folded back against the upper arm in
the raw picture, independent of any depth channel — production would see it too. If only the 3D angle
is extreme, the fold exists purely along z and is evidence about this harness, not about the
production signal.

```text
total absolute-angle rejections on real footage : 12
  impossible in the 2D IMAGE too                :  9   genuine bad pose; would be caught with real
                                                       stereo depth as well
  extreme only after the offline z lift         :  3   attributable to the zrel proxy, not to
                                                       production's measured depth
```

**All 12 were checked visually** (`oak_v4_evidence/f22/rejections/*.png`), and the cause is the same
in every case and is not what was expected: **multi-person interference, not single-person tracking
noise.**

- `123.webm` f626-627, right elbow 174.5°/174.3° — the skeleton is a **chimera stitched across two
  humans**: right shoulder on the man, right wrist on the woman, a "forearm" spanning the whole
  frame. Not an elbow at all.
- `456.webm` f293-352, right/left elbow 160-171° — the arm chain **collapses to a near-degenerate
  segment** inside a tight cluster of eight dancers under heavy mutual occlusion.

Two conclusions follow, and they pull in opposite directions, which is why both are stated:

1. **F-22 has zero measured absolute-angle false rejections on legitimate single-person motion** —
   the envelope it actually ships into. Precisely: `video.webm`, 435 frames, 0 absolute-angle
   rejections and exactly **1 single-frame rate-based hold** (33 ms, recovered on the next frame).
   Every absolute rejection in the whole dataset came from footage containing people this system
   explicitly does not support tracking (roadmap: "UNSUPPORTED: any multi-user environment").
2. **F-22 incidentally catches a class of F-21 failure that F-21 itself cannot see.** F-21's identity
   signal is mid-hip position plus torso span; a partial cross-person keypoint fusion leaves the hip
   exactly where it was and passes ownership untouched, while the *limbs* belong to someone else.
   F-22 caught that as an impossible elbow. This is a genuine architectural finding: **nothing before
   F-22 constrains the limbs to belong to the same body as the hips.** It is not a reason to widen
   F-22's scope; it is a reason F-21's own §27 hand-off finding deserves the priority it now has.

## 27. Performance A/B — closing §18's own gap

§18 said performance "was not independently re-measured this session". It now has been, in two
measurements rather than one, because a single end-to-end number would actively mislead here.

**Measurement 1 — isolated insertion-point cost.** Real per-frame geometry extracted once and cached,
so both arms replay byte-identical input with no model inference in the loop; only the sender's F-22
block and its `build_body_landmarks` call are timed. 435 frames × 40 repeats = 17,400 iterations per
arm.

| | OFF | ON | delta |
|---|--:|--:|--:|
| mean | 26.5 µs | 46.6 µs | **+20.1 µs** |
| p50 | 26.4 | 46.1 | +19.7 |
| p95 | 27.1 | 49.0 | +21.9 |
| p99 | 32.6 | 61.7 | +29.1 |
| max | 64.7 | 124.0 | +59.3 |

F-22 block alone: mean **18.3 µs**, p95 20.2 µs, max 59.8 µs — **0.060 % of a 33.3 ms production
frame**.

**Measurement 2 — end-to-end replay throughput**, with inference in the loop: 37.10 fps OFF vs
36.91 fps ON (−0.51 %, 26.95 → 27.09 ms/frame). RTMW3D dominates this by three orders of magnitude,
so "indistinguishable" here is **not** evidence F-22 is free — only that it is far below this
measurement's noise floor. Measurement 1 carries the claim.

**Frame age and capture-to-send latency are deliberately not reported.** Both are properties of the
live OAK-D capture path — a real sensor timestamp and a real socket send — and neither exists in a
video replay. What measurement 1 *does* honestly bound is how much F-22 can add to capture-to-send
latency once that path is live: 20.1 µs.

## 28. Regression status after this pass

```text
test_pose_validation.py             22/22 PASS
f22_adversarial.py                 120/120 PASS   18 cases, full 4-stage chain each
f22_video_replay.py                 PASS          unchanged, superseded for threshold reasoning
f22_threshold_analysis.py           PASS          3 clips, 4,997 chain evaluations
Unity EditMode (reflection runner)  170/170 PASS   no Runtime/ change, re-run anyway
```

## 28b. OFFLINE REPLAY SOAK — F-21 + F-22 together, 16 min, 29,595 frames

```text
LABEL: OFFLINE REPLAY SOAK
```

**Not equivalent to a hardware soak, and not to be quoted as one.** It exercises every pure-software
stage — the M15 crop loop, F-21 ownership, F-22 validation, and the real `build_body_landmarks` —
over tens of thousands of consecutive frames, which is enough to expose unbounded growth,
accumulating state, leaked events and exception paths. It touches no OAK-D, no USB, no stereo depth
and no real socket send, so every failure mode living in those is untouched and remains a live
question. It loops all three clips rather than repeating one, so each cycle is a total change of
person and scene and the ownership machine is driven through its lifecycle repeatedly instead of
sitting in `LOCKED` for 16 minutes.

| measure | result |
|---|---|
| duration / frames | 16.0 min, 19 clip cycles, **29,595 frames** |
| throughput | **30.83 fps** sustained (34.2 fps first sample → 30.9 fps last; no decay after warm-up) |
| **exceptions** | **0** |
| owned frames (F-21 emitted) | 25,582 (86.4 %) |
| resident memory | **flat** — first-half mean 875.2 MB vs second-half mean 878.6 MB, **+3.5 MB** over 29,595 frames |
| state-machine oscillation | 76 `TEMP_LOST` episodes, **76 reacquired**, 0 releases |
| ownership changes | 1 epoch for the whole run |
| validator suppression | 8.60 % of chain evaluations — `TRACK_LOST` 7,474 / `ANGLE_RATE_IMPOSSIBLE` 1,101 / `ANGLE_LIMIT` 221 |
| recovery behaviour | **2,581 hold episodes, 2,581 recoveries, 0 unrecovered holds at exit** |

Three honest caveats on these numbers rather than a clean bill of health:

1. **The memory figure needed a fix before it meant anything.** The first 16-minute run reported
   "0.0 MB, +0.0000 MB growth" — a completely fabricated-looking clean result. The cause was
   `ctypes.windll.psapi.GetProcessMemoryInfo` called without declared `argtypes`, which silently
   narrows the 64-bit process HANDLE, returns 0 and leaves the struct zeroed **without raising**. The
   soak was re-run with the call properly declared (and now raising rather than returning a plausible
   zero). Working-set readings oscillate between ~818 and ~902 MB from Windows trimming, which is why
   the half-means are quoted rather than an endpoint difference.
2. **The soak did NOT exercise `TARGET_RELEASED`.** Clip transitions are a single-frame gap in the
   simulated clock, so every person change resolved as `TEMP_LOST → REACQUIRED` well inside the 4 s
   release timeout — hence one epoch across 19 cycles. Release/re-acquire is covered deterministically
   in F-21 §24 instead; this run covers sustained operation, not the release path.
3. **The 8.60 % suppression rate is dominated by `TRACK_LOST`** (86.9 % of it), which is the
   confidence gate reporting unmeasured joints, not an F-22 angle judgement — the same decomposition
   as §25. F-22's own contribution is 1,322 / 102,328 chain evaluations ≈ 1.3 %.

## 29. Updated remaining risks

Replacing §20 where it has moved:

1. **No live L2 (hands-near-face) reproduction** — unchanged, and still the reason for CONDITIONAL.
   §26 strengthens the case for running it: the absolute path is now known to fire correctly on real
   bad geometry, but never yet on the *specific* geometry F-19 found.
2. **The replay's z-proxy remains noisier than production** — now quantified rather than asserted:
   3 of 12 absolute rejections (25 %) were attributable to it alone (§26). Expect a somewhat lower
   absolute-rejection rate against real stereo depth.
3. **Rate thresholds are still reasoned, not population-separated.** Real footage reaches
   3,000-6,000 °/s in crowded/occluded frames against a 1,800 °/s reject line, but those frames are
   corrupt anyway, so they do not constitute a clean "legitimate fast motion" population either.
   Unchanged from §20.4.
4. **CLI threshold overrides still not added** — unchanged from §20.5.
5. **New:** F-22's elbow REJECT of 160° has only **9.9°** of headroom on legitimate single-person
   footage (`video.webm` left elbow, §25). Not a defect — the threshold is meant to be close — but it
   is a much tighter margin than the superseded §15 table implied, and it is the number live L2
   testing should be watched against.

## 30. F-21's path-consistency gate — measured apart from F-22 (2026-09-15)

F-21 §30 fixed the silent wrong-person hand-off. That matters here for one reason only, and it is a
reason to be careful rather than pleased: **cross-person keypoint contamination is often
biomechanically impossible**, so an F-22 suppression can easily be mistaken for F-21 working, or
vice versa. §26 of this report already found that nothing before F-22 constrains the limbs to belong
to the same body as the hips.

The combined replay therefore ran a fourth arm with the validator switched **off entirely**, so
neither layer can be credited with the other's work:

```text
123.webm, whole chain     wrong-person frames   F-21 path rejections   F-22 joint suppressions
F-21 gate ON,  F-22 ON                      2                     30                      110
F-21 gate ON,  F-22 OFF                     2                     30                        0
```

**Every wrong-person frame eliminated was eliminated by F-21.** None of it is owed to F-22, and this
report does not claim any of it.

What F-22 *does* show is the mirror image, and it corroborates §26 rather than adding a new claim —
the suppression RATE per emitted frame roughly halves as ownership gets stricter:

```text
ownership off     366 / 775 emitted = 47.2 %
F-21 as shipped   144 / 500 emitted = 28.8 %
F-21 + §30 gate   110 / 404 emitted = 27.2 %
```

F-22 is not getting better. It is being handed less contaminated input, because F-21 stopped emitting
frames whose limbs and hips came from different humans. **F-22's own thresholds and behaviour are
unchanged by that pass** — `test_pose_validation.py` 22/22 and `f22_adversarial.py` 120/120 were
re-run against it, untouched.

Full detail, including the ground-truth labelling method and its measured error rate, is in the F-21
report §30.8b — kept there rather than duplicated here, so there is one copy of the numbers.

---

```text
F-22 VERDICT:
CONDITIONAL - OFFLINE ENGINEERING COMPLETE, LIVE HANDS-NEAR-FACE ACCEPTANCE PENDING. Validator built,
evidence-derived, and now proven end to end through the production insertion point to what the
consumer receives (120/120 across 18 adversarial cases); zero absolute-angle false rejections on
legitimate single-person footage; cost measured at 20.1 us/frame. No live human / no L2 reproduction available.

VALIDATION LAYER:
python-sidecar~/pose_validation.py, inserted between P1-1 and the UDP build - pure, per-chain,
per-frame, reuses P1-1's existing conf_emit=0 "invalid joint" contract. Zero Runtime/ or UDP changes.

JOINTS COVERED:
Left/right elbow, left/right knee - exactly F-19's evidence scope, no more.

CHAIN CHECKS:
Shoulder-elbow-wrist / hip-knee-ankle bend angle (the chain-consistency check itself), track/finite/
confidence gating, degenerate-segment detection.

TEMPORAL CHECKS:
Absolute angle (unconditional) + angular rate (between accepted frames), both independent - a smooth
ramp into an impossible angle is still rejected (unit-proven).

BIOMECHANICAL CHECKS:
Elbow reject 160 deg (F-19's own 150 deg + margin), knee reject 178 deg (2 deg above F-19's own
measured legitimate walking max of 176 deg) - both cited, not guessed.

KNOWN LIVE FAILURE ADDRESSED:
F-19's hands-near-face elbow hyperextension (167.8-179.6 deg rendered) - addressed in design and
unit-tested directly against F-19's own measured numbers; NOT yet confirmed live.

FALSE-POSITIVE RATE:
0 absolute-angle false rejections on 431 owned real-video frames; 3/431 (0.7%) momentary rate-based
holds, each recovering within one frame.

REJECTED/HOLD RATE:
3/431 owned frames (0.7%) on this footage; 0 baseline-impossible (absolute-angle) frames.

RECOVERY:
Immediate on the next passing frame, no confirm-window - all 3 real holds this session recovered in
exactly 1 frame.

PERFORMANCE IMPACT:
No Runtime/ or UDP change; sidecar cost is four closed-form trig calls/frame - not independently
re-measured this session.

AUTOMATED TESTS:
22/22 unit assertions (test_pose_validation.py), offline video replay (f22_video_replay.py) against
video.webm with real derived statistics.

LIVE TEST:
None this session - no human available (explicit user instruction). L1-L7 all open, L2 is the
specific next action.

REGRESSIONS:
None. No Runtime/ change; --no-pose-validation preserves the exact pre-F-22 path.

REMAINING RISKS:
No live validation yet; replay's z-proxy is noisier than real depth; only one non-"hands-near-face"
clip available; rate thresholds are reasoned, not population-separated; no CLI threshold overrides
added this pass.

NEXT ACTION:
Run a live L2 (hands-near-face) session against the real OAK-D to confirm this raw-geometry threshold
reproduces F-19's own finding and correctly suppresses it, then upgrade this report's verdict to PASS.
```
