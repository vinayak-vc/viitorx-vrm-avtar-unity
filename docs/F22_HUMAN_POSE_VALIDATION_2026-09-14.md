# F-22 — Human Pose Validation / Biomechanical Validation Layer

```text
VERDICT: CONDITIONAL
```

The validator is implemented, unit-proven (22/22), and produces real, honest evidence from an offline
replay against `video.webm`: on ~14.4 s of energetic real dance motion, elbow bend angles reached
within 1-5° of the REJECT threshold without ever exceeding it (max 155.3°/158.8° vs. a 160° reject
line), and exactly 3 of 431 owned frames were momentarily suppressed — each recovering on the very
next frame. **No live human was available this session** (stated explicitly by the user) — this is
why the verdict is CONDITIONAL, not PASS: the brief's own §29 rules require replay *and* live evidence
to agree, and specifically a live L2 (hands-near-face) reproduction of F-19's own defect, which has
not yet happened against this validator.

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

## 15. F-22 results

| chain | n | bend p50 | bend p95 | bend max | rate p50 (°/s) | rate p95 (°/s) | rate max (°/s) | suppressed |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| left_elbow | 431 | 36.0° | 150.9° | 155.3° | 95 | 521 | 1814 | 1 |
| right_elbow | 431 | 37.2° | 147.9° | 158.8° | 95 | 731 | 2151 | 2 |
| left_knee | 431 | 17.0° | 38.7° | 46.8° | 66 | 296 | 476 | 0 |
| right_knee | 431 | 18.7° | 35.6° | 47.9° | 74 | 286 | 426 | 0 |

Elbow max bend (155.3°/158.8°) sits within 1-5° of REJECT (160°) on legitimate footage — the
threshold is exercised, not slack, without ever being crossed on the absolute check. Knee bends stay
well inside NORMAL (max 47.9° vs. REJECT 178°) — consistent with F-19's own "knees are clean" finding
and this task's brief's own instruction to start conservative on knees.

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

```text
F-22 VERDICT:
CONDITIONAL - validator built, evidence-derived, unit-proven, zero-regression by construction; no
live human/no L2 hands-near-face reproduction available this session.

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
