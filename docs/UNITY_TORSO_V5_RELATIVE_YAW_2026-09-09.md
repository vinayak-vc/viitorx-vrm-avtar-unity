# Torso V5 — trunk gate + relative-yaw composition — 2026-09-09

**Two controlled changes, both in the torso path only:**

1. **A trunk validity gate** (`Runtime/Retargeting/TrunkGate.cs`, new) so a degenerate or discontinuous
   4-point trunk measurement can never reach the avatar. The specific defect it kills: an absent
   detection is `[0,0,0]`, and Kalidokit's `atan2(0,0)` plus its `+0.5` jump-fix resolves to **exactly
   +90° of commanded torso yaw**.
2. **A relative-yaw composition** replacing the two-absolute-yaws sum. Hips take the absolute hip yaw;
   Spine/Chest/**UpperChest** take only `shoulderYaw − hipYaw`, with weights summing to exactly 1.0.
   `UpperChest` is now bound — it was never written before, by omission rather than design.

`kalidokitTorsoYawScale` is **1** and is no longer a compensation for a broken composition.

**Verdict: CONDITIONAL PASS.** Every compositional and safety target is met exactly — rigid-turn gain
**1.000**, twist gain **1.000**, opposite-direction gain **1.000**, the +90° false command **eliminated**,
ARM V2 unchanged, and tracking error on the real OAK corpus down **49–61 %**. Two things hold it
short of PASS: the live OAK-D motion test was not run (this round was directed to video input), and
because V5 is now *faithful*, it also follows bad source depth more faithfully — over-twist frequency
rises on the poor-depth captures (§6). That is an upstream depth problem, not a torso one, but it is
real and is not hidden here.

---

## 1. V4 baseline

Carried forward from `UNITY_TORSO_YAW_V4_VALIDATION_2026-09-09.md`, all measured:

| finding | value |
|---|---|
| composition structure | `chain = 0.70·hipYaw + 0.70·shoulderYaw` — two **absolute** yaws summed |
| rigid turn (hip == shoulder) | over-rotated **1.4×** (measured 1.194 live on OAK, 1.230 held) |
| opposing hip/shoulder | **cancelled** — gain +0.116 |
| 52° twist | **inverted** — gain −2.944 |
| trunk validation | **none**; `CalcHipsAndSpine` reads `lm[11/12/23/24]` unconditionally |
| empty room at scale 0.75 | landmark confidence 0.71 → 40.46° source yaw → **avatar twisted 23.82°** |
| rest jitter at 0.75 | **5.4–7.6°** stdev while the subject is still |
| `UpperChest` | **0.000° at every scale** — never bound |

The conclusion V4 reached, and which this task acts on: *no scalar can fix a gain that ranges
−2.944 … +1.230*, because the fault is structural.

---

## 2. Trunk gate design

`TrunkGate` is **geometric and continuity-based, not confidence-based** — deliberately. V4 measured
landmark confidence of **0.71** accompanying a fully hallucinated person, so confidence does not
separate the populations. Semantic hallucination is explicitly out of scope (the brief says so); what
this gate guarantees is that a *degenerate or discontinuous* measurement is never converted into a
confident yaw command.

### 2a. Thresholds, measured rather than chosen

From `trunk_gate_thresholds.py` over **111,546 recorded frames across 12 captures**:

| quantity | degenerate population | real population | chosen | cost |
|---|---|---|---|---|
| shoulder span | p95 **0.054 m** | p01 **0.156 m** | `MinSpan = 0.10 m` | rejects **0.000 %** of real |
| hip span | p95 **0.091 m** | p01 **0.123 m** | `MinSpan = 0.10 m` | rejects **0.195 %** of real |
| span ceiling | — | p95 0.366 m, max 3.2 m | `MaxSpan = 1.0 m` | catches gross breakage |
| yaw rate | — | p99 **129 °/s**, p99.9 **435 °/s** | `MaxYawRate = 500 °/s` | rejects **0.078 %** |

The two span populations separate cleanly, with a gap between 0.091 and 0.123 m — that gap is what
makes a floor possible at all. The rate ceiling sits far above the conditioner's own 140 °/s slew on
purpose: **the conditioner smooths the plausible, the gate rejects the impossible**, and if the two
were close they would fight.

### 2b. Design decisions worth stating

* **Hip and shoulder are gated together, never independently.** The V5 composition consumes
  `shoulderYaw − hipYaw`; mixing a fresh value with a held one would fabricate a twist the human
  never made.
* **Span is the full 3-D separation, not `|Δx|`.** A subject turned ~90° has a tiny `Δx` but a
  full-width line in *depth*. Gating on `Δx` would reject exactly the turned poses this work exists
  to support. (Test: `EdgeOnTorso_IsNotMistakenForDegenerate`.)
* **Only a continuity rejection accumulates towards re-acquisition.** A degenerate frame carries no
  candidate to re-acquire *to*, so no number of degenerate frames may ever push the gate into
  adopting the +90° sentinel. (Test: `DegenerateFrames_DoNotDriveReacquisition`.)
* **Re-acquisition after `ReacquireFrames = 8`** (~0.3 s) so a genuine teleport — a different person
  stepping in — cannot freeze the torso forever.
* **Before the first valid frame the output is 0 (frontal), never the sentinel.**

---

## 3. Old vs new torso composition

The bones **nest**, so the rendered shoulder line is the *sum* of the chain. That is the whole bug:

```
OLD:  Hips        = 0.70 · hipYaw
      Spine+Chest = (0.45 + 0.25) · shoulderYaw
      => chain     = 0.70·hipYaw + 0.70·shoulderYaw          two ABSOLUTE yaws, added

NEW:  Hips                     = hipYaw                       gain 1.0, absolute
      Spine+Chest+UpperChest   = shoulderYaw − hipYaw         weights sum to EXACTLY 1.0
      => chain                  = hipYaw + (shoulderYaw − hipYaw) = shoulderYaw
```

The new chain equals `shoulderYaw` **identically, for every hip/shoulder ratio**. A rigid rotation is
therefore never multiplied, and a twist is reproduced as a twist.

**Weights** follow human axial rotation, which is mostly thoracic rather than lumbar (~5° lumbar vs
~35° thoracic): `Spine 0.20`, `Chest 0.40`, `UpperChest 0.40`. The sum **is** the gain, so
`NormaliseTwistWeights` renormalises to 1.0 on any rig missing Chest or UpperChest — a missing bone
hands its share to the ones that remain, never to nothing.

**`UpperChest` is now bound.** On this rig it exists (`spine.003`, the shoulders' direct parent) and
resolves to `Vrm10ControlBone:UpperChest`; a quarter of the trunk chain had been rigid purely because
`Bind()` never fetched it. Bend (pitch) distribution and roll are untouched — only the yaw channel
changed. The old per-bone dampeners are renamed `*RollDamp`, which is the only channel still using them.

---

## 4. Deterministic tests

`Tests/EditMode/TrunkGateTests.cs`, 15 tests, covering every case the brief listed:

| brief case | test |
|---|---|
| zero landmarks | `ZeroLandmarks_AreRejected_AndNeverCommandNinetyDegrees` |
| tiny shoulder span | `TinyShoulderSpan_IsRejected` |
| tiny hip span | `TinyHipSpan_IsRejected` |
| NaN / Inf | `NaNAndInf_AreRejected_OnEveryInput` (landmarks **and** solved yaw) |
| sudden impossible yaw jump | `ImpossibleYawJump_IsRejected_AndHolds` |
| valid → invalid → valid | `ValidThenInvalidThenValid_HoldsThroughTheGapAndResumes` |
| invalid on first frame | `InvalidOnFirstFrame_DrivesFrontal_NotTheSentinel` |
| no false +90° command | `NoFalseNinetyDegreeCommand_EvenAfterAValidPose` |
| valid behaviour preserved | `ValidTorso_PassesThroughUnchanged`, `PlausibleFastTurn_IsAccepted`, `EdgeOnTorso_IsNotMistakenForDegenerate` |
| (design guards) | `ImplausiblyLargeSpan_IsRejected`, `SustainedDisagreement_ReacquiresRatherThanLockingOutForever`, `DegenerateFrames_DoNotDriveReacquisition`, `Reset_ClearsHeldState` |

**Full EditMode suite: 94 passed, 0 failed** — including all 32 `ArmAimSolver` cases, so ARM V2 is
untouched. The MCP Test Runner closes this Editor (recorded pitfall), so the `[Test]` and `[TestCase]`
methods were invoked directly by reflection against the compiled assemblies; there are no
`SetUp`/`TearDown` fixtures in this suite, so direct invocation is faithful. This also clears the
"EditMode suite OWED" item carried since ARM V2.

**One test caught a fault in its own fixture, not in the gate.** The first version of
`ValidTorso_PassesThroughUnchanged` stepped 32° in one 1/30 s frame — **960 °/s** — and was correctly
rejected. The fixture was unphysical; it now steps within the measured human envelope.

---

## 5. Live results — video input (OAK-D live NOT run)

**TASK 5's live OAK-D motion list was not executed.** This round was directed to video input
(`Assets/Games/video/video.webm`) because no subject was available. The device itself is present and
working (§ V4 report), but slow/fast yaw, left-right reversal, ~45/90/180° turns with a human were
**not** run, and nothing here should be read as an OAK-D live result.

### 5a. The controlled torso probe — why video alone cannot answer this

`video.webm` **cannot test the composition**: its source shoulder yaw is p50 **4.18°**, and only
**2.7 % of frames clear the 22° dead-zone knee**. Almost every frame is deliberately zeroed by the
ADR-027 conditioner, so a gain measured there measures the *dead zone*, not the decomposition.

So `torso_probe_udp.py` streams a synthetic but fully valid 4-point trunk on the **real UDP wire**,
commanding hip and shoulder yaw **independently** and holding each long enough for the rate limiter
and low-pass to settle. Measured on the **actual skinned VRM bones**, scoring only the settled 60 % of
each hold:

| phase | cmd hip | cmd sh | cmd twist | Hips Y | trunk sum | chain | **gain** | arm err |
|---|---|---|---|---|---|---|---|---|
| rigid +45 | +45.0 | +45.0 | 0.0 | **45.00** | **0.00** | **45.00** | **1.000** | 0.015° |
| rigid −45 | −45.0 | −45.0 | 0.0 | **−45.00** | **0.00** | **−45.00** | **1.000** | 0.016° |
| twist +35 | 0.0 | +35.0 | +35.0 | **0.00** | **35.00** | **35.00** | **1.000** | 0.003° |
| twist −35 | 0.0 | −35.0 | −35.0 | **0.00** | **−35.00** | **−35.00** | **1.000** | 0.000° |
| **oppose** | +30.0 | −30.0 | −60.0 | **30.00** | **−60.00** | **−30.00** | **1.000** | 0.000° |
| rigid +90 | +90.0 | +90.0 | 0.0 | **90.00** | **0.00** | **90.00** | **1.000** | 0.007° |

**Exact to 0.01° in every phase.** Against what the old composition would have produced from its own
measured structure:

| phase | V5 chain | V5 gain | V4 chain | V4 gain |
|---|---|---|---|---|
| rigid +45 | **45.00** | **1.000** | 63.00 | 1.400 |
| rigid +90 | **90.00** | **1.000** | 126.00 | 1.400 |
| twist +35 | **35.00** | **1.000** | 24.50 | 0.700 |
| **oppose** | **−30.00** | **1.000** | **0.00** | **0.000** |

The `oppose` row is the clearest: the old form **cancelled to exactly zero** — a human twisting hips
one way and shoulders the other produced a perfectly frontal avatar.

**Trunk-gate rejections across the whole probe: 0 of 21,173 applied frames.** The gate does not
interfere with valid input.

### 5b. False-detection (ghost) behaviour — the headline safety result

`ghost_udp_sender.py` streams **real → all-zero landmarks → real** on a live wire, with confidence
0.75 still attached (higher than the 0.71 V4 measured on a real hallucination), so the gate cannot be
passing by relying on confidence.

| phase | n | source shoulder yaw | avatar chain yaw | sd | frames within 15° of ±90 |
|---|---|---|---|---|---|
| REAL (before) | 2320 | −20.00 … +20.00 | −18.74 … +23.34 | 10.50 | **0** |
| **GHOST (no person)** | 3588 | **+90.00 … +90.00** | **−18.79 … −18.74** | **0.00** | **0** |
| REAL (after) | 2938 | −20.00 … +17.93 | −18.79 … +18.73 | 10.31 | **0** |

* The source **is** the +90° sentinel throughout the ghost phase — that is the poisoned input.
* The avatar **holds the last valid yaw**, frozen to a **0.0044° stdev**.
* Gate reasons during ghost: `ShoulderSpan = 3587`, `YawJump = 1`.
* Tracking resumes **immediately** on the first real frame.

**The false +90° command is eliminated.**

---

## 6. Rest-jitter measurement

Both compositions driven from the **same recorded OAK-D yaw** through the **same verified `DampYaw`
port** (checked against the shipping C# to 6.7e-6°), with the V5 gate applied. "Rest" = frames whose
*source* yaw is inside the 8° dead-zone floor, i.e. the human is standing still.

| capture | composition | **rest jitter (sd)** | >45° | >90° | out max | **err mean** |
|---|---|---|---|---|---|---|
| `armv1` (589 s) | OLD V4 @0.75 | 4.915° | 0.23 % | 0.13 % | 135.68° | 7.18° |
| | **NEW V5 @1.00** | **3.873°** | 0.41 % | 0.16 % | 145.00° | **3.67°** |
| `armv1b` (245 s) | OLD V4 @0.75 | 7.558° | 1.23 % | 0.00 % | 58.98° | 9.65° |
| | **NEW V5 @1.00** | **0.366°** | 4.96 % | 0.00 % | 61.12° | **3.73°** |
| `p14` (228 s, poor depth) | OLD V4 @0.75 | 12.398° | 8.11 % | 6.82 % | 185.46° | 23.40° |
| | **NEW V5 @1.00** | **12.139°** | 15.71 % | 12.44 % | 179.61° | **9.80°** |

**Tracking error falls 49 %, 61 % and 58 %** — and that is at V5's *higher* gain of 1.0 against V4's
0.75, so it is not bought with attenuation.

**Rest jitter improves in two of three captures** — dramatically on `armv1b` (7.558° → **0.366°**,
a 95 % reduction) and by 21 % on `armv1` — and is essentially unchanged on `p14` (12.398° → 12.139°).

**Over-twist frequency rises, and this is stated plainly because it is the cost.** V5 at gain 1.0
reproduces the source faithfully; when the source itself is a depth artefact reaching ±174°, V5
follows it where the old attenuating composition partially suppressed it. On `p14` — whose large-yaw
frames have a mean measured-depth coverage of **12.2/33 against 20.9/33 for its calm frames** — the
time spent beyond 45° roughly doubles. **The composition is now correct, so the remaining error is
upstream.** That is the same conclusion V3 reached for the arms, arrived at again for the torso.

---

## 7. False-detection behaviour

Covered by §5b above; summarised against the brief's requirements:

| requirement | result |
|---|---|
| validate shoulder span | ✅ `MinSpan`/`MaxSpan`, 3-D separation |
| validate hip span | ✅ same |
| shoulder/hip vectors finite | ✅ landmarks **and** solved yaw checked |
| reject degenerate spans | ✅ 3587 rejections during the ghost phase |
| reject impossible jumps | ✅ 500 °/s ceiling, with 8-frame re-acquisition |
| HOLD last valid yaw when invalid | ✅ frozen to 0.0044° stdev |
| never convert invalid → +90° | ✅ **0 frames within 15° of ±90** |
| torso-specific, not the arm gate | ✅ separate 4-point geometry, hip+shoulder gated jointly |
| preserve valid yaw behaviour | ✅ 0 rejections in 21,173 valid probe frames |

---

## 8. Arm regression

ARM V2 must remain exact. Source → **actual skinned** bone direction, measured through
`Inverse(RigFrame())` exactly as the driver computes it:

| input | condition | p50 | p95 | p99 | max |
|---|---|---|---|---|---|
| torso probe (0→90° turns) | torso rotating hard | **0.0000°** | **0.1153°** | **0.2065°** | 57.79° ¹ |
| `video.webm` upper arm L | torso rotating | 0.3140° | 1.8967° | 3.6706° | 13.47° |
| `video.webm` upper arm R | torso rotating | 0.3208° | 1.6476° | 3.3493° | 6.67° |
| `video.webm` forearm L | torso rotating | 0.6046° | 3.7937° | 6.4164° | 14.21° |
| `video.webm` elbow angle err L | torso rotating | 0.3581° | 3.6944° | 6.2618° | 20.15° |

¹ The 57.79° max is **4 samples of 21,173 (0.019 %)**, all in the first four frames, decaying
57.79 → 28.90 → 14.45 → 7.22 — halving every frame. That is the pre-existing `lerpAmount = 0.5` slerp
converging from rest, the same signature V2 and V4 §4b both identified, not a regression.

The brief's five conditions:

| condition | result |
|---|---|
| 1. arms stationary, torso stationary | probe `zero` phases: arm err **0.000°** |
| 2. arms moving, torso stationary | video, scale-0 baseline: p50 0.265° (V4 §5g) |
| 3. torso rotating, arms stationary | probe rigid ±45/±90: arm err **0.003–0.016°** |
| 4. torso rotating + arms moving | video at scale 1: p50 **0.31°**, p95 1.90° |
| 5. body twist with asymmetric arms | probe `oppose`: arm err **0.000°** |

**Body-relative arm error** — the KPI, since it is what a viewer sees. On `video.webm`: p50 **9.00° /
9.45°**, p95 14.18° / 13.79°. Across the probe, which holds the arms rigid *in the shoulder frame* so
a correct rig must keep this constant, it varies by only **2.7° across a full 90° rigid turn**
(110.01° → 112.69°); the constant offset is an artefact of the synthetic probe's arm placement, so
only the *variation* is meaningful there.

**No arm regression.**

---

## 9. Visual result

The brief's f419 frame came from `annotated_p0.mp4`, which no longer exists in the repo; `video.webm`
is the same underlying footage, and its worst-yaw frames are the equivalent. V5 versus the V4 sweep on
**identical input**, on the actual skinned bones:

| frameIdx | source yaw | V4 @0 (frozen) | V4 @1 (old comp) | **V5 @1 (new comp)** |
|---|---|---|---|---|
| 375 | −23.48° | 0.00° | **−0.75°** | **−23.04°** |
| 369 | −24.27° | 0.00° | −13.23° | **−21.80°** |
| 364 | −25.44° | 0.00° | −12.44° | **−17.65°** |
| 363 | −25.05° | 0.00° | −9.63° | **−13.74°** |

Peak avatar shoulder yaw over the clip: V4 **17.06°**, V5 **23.04°**, against a source peak of 25.44° —
**91 % of the human's turn reproduced, versus 67 %**. Hand distance from the spine axis rises in step
(f375: 0.3152 → **0.4469 m**; f369: 0.3816 → **0.4322 m**), i.e. the hands move further out of the
torso, which was V3's original symptom.

Rendered evidence (`oak_v4_evidence/shots/`):

* `v5_6116765.png` — frontal rest.
* **`v5_6118005.png` — rigid +90°: the avatar is fully in profile, legs, torso and head turned as one
  coherent body, arms still anatomically attached.** The old composition would have driven this to
  126° — past profile, over-rotated by a third.
* **`v5_6117792.png` — `oppose` (hips +30°, shoulders −30°): a visible counter-twist**, legs turned one
  way and chest/head the other. The old composition produced **exactly 0.00°** here — a perfectly
  frontal avatar for a human who was twisting hard.

---

## 10. Final verdict

## CONDITIONAL PASS

**Against the brief's PASS conditions:**

| condition | result |
|---|---|
| false torso yaw prevented when the subject disappears | ✅ **0 frames within 15° of ±90**, held to 0.0044° sd |
| rigid body rotation has ~1× torso gain | ✅ **1.000**, exact to 0.01°, at ±45° and +90° |
| torso twist reproduced correctly | ✅ **1.000**, including the `oppose` case the old form zeroed |
| no significant new jitter/reversal | ✅ jitter improves 21 % / 95 %, unchanged on the worst capture; **0** output reversals |
| ARM V2 remains correct | ✅ p50 **0.0000°**, p95 0.1153° through 90° torso turns; 94/94 tests pass |
| rendered avatar visually follows body orientation | ✅ §9, numerically and in the renders |

**Why not PASS.** Two things, neither of which the evidence can wave away:

1. **The live OAK-D motion list was not run** (§5). This round was directed to video; the device works
   but no subject was available. Slow/fast yaw, reversal, ~45/90/180° on real depth remain unmeasured.
2. **V5's faithfulness cuts both ways.** Because gain is now 1.0 rather than an attenuating 0.7-ish
   mixture, bad source depth reaches the avatar more completely: time beyond 45° roughly doubles on
   the poor-depth `p14` capture (8.11 % → 15.71 %). Tracking error still falls 58 % there, so this is
   a faithful rendering of a bad signal rather than a new fault — but it means **upstream depth
   quality is now the binding constraint on torso realism**, and that should be established on real
   OAK-D before this is called done.

**FAIL was rejected** because nothing regressed: no reversals, no instability, no arm degradation, and
every compositional target hit exactly.

### Recommended next steps — not implemented

1. **Run the guided OAK-D motion capture** (harness exists: `oak_guided_v4.py`, `oak_guided_v4.ps1`).
2. **Then re-qualify the dead zone and rate limit** against real depth statistics — with the
   composition correct, the conditioner's constants are the next thing tuned against the wrong target.
3. **Upstream depth quality** for the trunk landmarks is now the dominant remaining error source, as
   §6 shows; that is sidecar work, out of scope here.

### Rollback

`kalidokitTorsoYawScale = 0` restores frontal-lock behaviour with the gate still active. Reverting
`TrunkGate.cs` and the composition block in `KalidokitControlRigDriver.cs` restores V4 exactly.

---

## 11. Honest limits

1. **No live OAK-D subject test** (§5). Every live number here is video or synthetic probe.
2. **The probe is synthetic.** It proves the *composition* exactly, on the real UDP wire and the real
   skinned rig, but it carries no depth noise — §6 supplies that from recorded OAK data.
3. **§6 is a replay, not 111k rendered frames.** The conditioner is the shipping method (port verified
   to 6.7e-6°) and the composition identity is arithmetic, but the avatar poses were computed rather
   than rendered per frame. §5a and §9 *are* live rig measurements.
4. **`video.webm` is a weak torso test** — p50 4.18°, only 2.7 % of frames above the dead-zone knee.
   It cannot measure gain; that is why the probe exists.
5. **The body-relative arm error in the probe carries a constant frame offset** from the synthetic arm
   placement; only its *variation* (2.7° across a 90° turn) is quoted as a result.
6. **Twist weights (0.20/0.40/0.40) are anatomically motivated, not measured on this rig.** Their
   *sum* is what the composition depends on and that is exactly 1.0 by construction and by test; the
   split between the three bones affects only how the twist is distributed visually.
7. **Bend (pitch) and roll are untouched**, including on the newly-bound `UpperChest`, which receives
   yaw only. Changing the pitch distribution would alter ADR-026 behaviour that is separately validated.
8. Head, hands, legs, feet, IK, proportions and mesh deformation were left alone as instructed.

---

## Artefacts

Under `python-sidecar~/oak_v4_evidence/`:
`v5_probe_analysis.txt` · `v5_analysis.txt` · `v5_rest_jitter.txt` · `trunk_gate_thresholds.txt` ·
`probe_trace.jsonl` · `v5_trace.jsonl` · `torso_probe_marks.json` ·
`shots/v5_6116765.png` (rest), `shots/v5_6118005.png` (rigid +90°), `shots/v5_6117792.png` (oppose).

Harness under `python-sidecar~/`: `trunk_gate_thresholds.py` · `torso_probe_udp.py` ·
`ghost_udp_sender.py` · `analyze_probe_v5.py` · `analyze_v5.py` · `v5_rest_jitter.py`.

Code: `Runtime/Retargeting/TrunkGate.cs` (new) · `Runtime/Retargeting/KalidokitControlRigDriver.cs`
(torso yaw block, `UpperChest` binding, `NormaliseTwistWeights`) ·
`Tests/EditMode/TrunkGateTests.cs` (new) · `Scenes/Bootstrap.unity` (`kalidokitTorsoYawScale: 1`).
