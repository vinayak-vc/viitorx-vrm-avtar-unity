# V6 — Minimal Torso-Yaw Wrap Guard + Explicit Operating Range

```text
VERDICT: CONDITIONAL
```

**The guard does its job, confirmed live.** Over a 266 s OAK-D session in Play mode (58,605 avatar
frames, guard evaluated 58,604 times), the worst frame-to-frame change in the **rendered** torso yaw
is **2.45°**, with **zero** steps above 10° — against a V5 baseline of **359.9°** and 32 steps over
30°. The ±180° flip never reaches the avatar, the ±60° envelope holds, and **145/145 EditMode tests
pass** (51 new). Turning past ±90° produces an explicit, stationary hold instead of a flip.

**But the live session answered the product question, and the answer is no — for a reason that is not
V6's.** With the subject standing **square to the camera** — confirmed independently by a 66.9 px
shoulder span against a ~64 px front-on value — the avatar's torso sits at **−49.6° and is rock
steady** (0.2° range over 150 consecutive frames). The cause, measured on the same frames: raw
shoulder Δz is **−101 mm** while **one disparity step at 1.33 m is 89 mm**. The two shoulders land on
adjacent disparity rungs, the smallest non-zero difference the sensor can express, and that single
step becomes ~50° of commanded yaw. It is not noise and no filter can remove it.

**−49.6° is inside the ±60° envelope, so V6 correctly passes it through as `Valid`.** V6 governs the
wrap and the range; it does not touch how the yaw is measured. This is the F-09 under-resolution root
cause (ADR-038), and it is now the dominant visible defect — far more so than the freeze.

Two further measured defects: the **worst hold is 10.7 s** live (offline replay predicted 12.33 s),
and a **diagnostic labelling bug** in the guard's state enum (§10.9). Neither is fixed by more
implementation.

> **Read this if you read nothing else.** V6 is a correct, minimal, well-tested change that does
> exactly what F-10/F-14/F-15 predicted. It is not what stands between this system and a usable
> mirror. The measurement is.


---

## 1. Exact production files changed

| file | change |
|---|---|
| `Runtime/Retargeting/TorsoYawGuard.cs` | **new**, 190 lines — the guard, the envelope, the state enum, hold statistics |
| `Runtime/Retargeting/TorsoYawGuard.cs.meta` | new |
| `Runtime/Retargeting/KalidokitControlRigDriver.cs` | **+18 lines, 2 hunks** — 4 fields, 1 call site, reset |
| `Tests/EditMode/TorsoYawGuardTests.cs` (+`.meta`) | **new**, 51 tests |

**Unchanged, verified by `git diff`:** `TrunkGate`, V5 composition and weights, `UpperChest` binding,
`DampYaw` and every ADR-027 constant, `ArmAimSolver`, Arm V2, P1-1/P1-2/P1-3, F-08 depth sampler,
scene defaults, UDP payload semantics, shipping configuration, the sidecar.

---

## 2. Exact logic changed

One new stage, between the V5 gate and the V5 composition — nothing else moved:

```text
landmarks → TrunkGate (V5, unchanged) → TorsoYawGuard (V6, NEW) → DampYaw (unchanged)
          → V5 relative composition (unchanged) → avatar
```

```csharp
TorsoYawGuardResult torsoYaw = TorsoYawGuard.Evaluate(
    trunkYaw.HipYaw, trunkYaw.ShoulderYaw, trunkYaw.Fresh, ref torsoYawGuard);
float hipsYawAbs     = DampYaw(torsoYaw.HipYaw,      ref hipsYawRate,  ref hipsYawSmooth,  dt);
float shoulderYawAbs = DampYaw(torsoYaw.ShoulderYaw, ref spineYawRate, ref spineYawSmooth, dt);
```

The two lines below are the *only* production behaviour change — previously they read
`trunkYaw.HipYaw` / `trunkYaw.ShoulderYaw` directly.

**The policy**, with both thresholds inherited rather than invented:

| condition | state | output |
|---|---|---|
| `\|hipYaw\| > 150°` **or** `\|shoulderYaw\| > 150°`, or non-finite | `WrapGuarded` | hold the last valid pair |
| `TrunkGate.Fresh == false` | `Valid` (held) | hold the last valid pair |
| nothing valid yet | `NoValue` | 0 — drive frontal |
| fresh, `\|yaw\| > 60°` | `OutOfRange` | **clamp** to ±60° |
| otherwise | `Valid` | pass through unchanged |

Three deliberate design choices:

* **Clamp, not hold, out of range.** Clamping is continuous — no boundary snap — deterministic, and
  it never extrapolates through the unreliable region. It keeps the sign (F-15 validated) and drops
  only the magnitude (F-15 did not validate it past 60°). Holding would freeze the avatar mid-turn.
* **The held value is stored UNCLAMPED**, so returning into the envelope restores the true angle on
  the next frame rather than easing back from the boundary.
* **Hip and shoulder are guarded together**, exactly as `TrunkGate` does, because V5 composes
  `shoulderYaw − hipYaw`; holding one while following the other would fabricate a twist.

`WrapGuardDeg = 150f` is F-10's own guard (scored in F-14, replicated in F-15).
`ValidatedRangeDeg = 60f` is the F-15 measured envelope. A test asserts both, so changing either
breaks the build rather than silently invalidating the evidence.

---

## 3. Unit-test results

**145 passed, 0 failed** — the 94-test V5/Arm baseline intact, plus **51 new**.

Run by reflection inside the Editor; `run_tests` closes this Unity instance (pre-existing issue).
An initial run showed 2 failures that were a **harness artefact**, not a regression:
`ArmAimSolverTests` has methods carrying both a bare `[Test]` and `[TestCase]`s, and the runner
invented a zero-argument call. Fixed in the runner; the product code was never at fault.

| group | cases | covers |
|---|--:|---|
| basic validity | 7 | 0°, ±30°, ±45°, ±60° pass through unchanged |
| boundary | 10 + 1 | 59/60/61/75/90 both signs; **continuity sweep 50→70° proves no snap** |
| wrap | 8 + 2 | 149/150/151/179 both signs; holds last valid; **wrap never enters the held state** |
| safety | 3 | no output ever ±180°; non-finite holds; **first-frame-invalid drives frontal, not +90°** |
| recovery | 5 | same sign, opposite sign, repeated invalid, 200-frame invalid run, out-of-range ≠ hold |
| transitions (§5) | 11 | ±45/±60 → centre → back; through edge-on; through wrap; left→centre→right |
| composition safety | 2 | hip+shoulder guarded together; twist preserved in range |
| statistics / reset | 2 | hold %, longest, average; `Reset()` clears |
| thresholds | 1 | 150 and 60 are the inherited constants |

The two that matter most: `Wrap_IsNeverAcceptedIntoTheHeldState` (20 wrapped frames must not displace
a held +45°) and `FirstFrameInvalid_DrivesFrontal_NotNinety` (the V4 `atan2(0,0) → +90°` sentinel).

---

## 4. Live validation results

Two independent validations were run.

**(a) Offline replay of the shipped guard** — `TorsoYawGuard.Evaluate` invoked inside the Editor over
the **20,783 real F-15 frames** (subject B, 28 ground-truth-labelled blocks). This is the only one of
the two with ground-truth labels, so all per-block sign/error numbers below come from it.

**(b) Live end-to-end session** — OAK-D + sidecar + **Play mode**, subject A at 1.33 m, **266 s,
58,605 avatar frames**, sampled at 220 Hz directly off the rendered bone transforms
(`hips + spine + chest + upperChest` local Y). This exercises the full shipping path: camera →
sidecar → UDP → pose buffer → Kalidokit → TrunkGate → **TorsoYawGuard** → V5 composition → VRM rig.

| guard state | live frames | share |
|---|--:|--:|
| `Valid` | 52,350 | **89.33 %** |
| `OutOfRange` | 4,877 | **8.32 %** |
| `WrapGuarded` | 1,378 | **2.35 %** |

Protocol: square baseline, ±45°, ±60°, past ±90° both directions with ~10 s holds, then slow and fast
reversals. Evidence: `oak_v4_evidence/v6_live_session.txt` and `v6_live_trace.jsonl`.

**Both agree**, which is the point: the replay predicted a 12.33 s worst-case hold and the live
session measured 10.7 s.

---

## 5. Static results

Guard output per block; sign is the meaningful metric (see the MAE caveat below).

| block | gt | n | sign ok | held % | longest hold | max step |
|---|--:|--:|--:|--:|--:|--:|
| `h_0` | 0 | 96 | — | 0.0 % | 0 | 1.3° |
| `h_p30` | +30 | 90 | **100.0 %** | 0.0 % | 0 | 0.2° |
| `h_p45` | +45 | 87 | **100.0 %** | 0.0 % | 0 | 2.2° |
| `h_p60` | +60 | 89 | **100.0 %** | 0.0 % | 0 | 6.6° |
| `h_p90` | +90 | 90 | 100.0 % | **100.0 %** | **210** | 0.0° |
| `h_0b` | 0 | 73 | — | 0.0 % | 0 | 1.6° |
| `h_m30` | −30 | 97 | **100.0 %** | 0.0 % | 0 | 1.3° |
| `h_m45` | −45 | 99 | **27.3 %** | 0.0 % | 0 | 2.0° |
| `h_m60` | −60 | 93 | **100.0 %** | 0.0 % | 0 | 1.3° |
| `h_m90` | −90 | 96 | **0.0 %** | **100.0 %** | **169** | 0.0° |
| `h_0c` | 0 | 74 | — | 0.0 % | 0 | 1.4° |
| `dc_body45R_face0` | +45 | 115 | **100.0 %** | 0.0 % | 0 | 1.6° |
| `dc_body45L_face0` | −45 | 112 | **0.0 %** | 0.0 % | 0 | 0.9° |
| `dc_body0_head45R` | 0 | 130 | — | 0.0 % | 0 | 0.0° |
| `dc_body0_head45L` | 0 | 133 | — | 0.0 % | 0 | 0.0° |

**The MAE column is deliberately omitted here because it is a weak metric on this capture.** F-15
measured subject B's *actual* rotations as different from the commanded labels (`h_p30` → 54.4°,
`h_m60` → 37.0° of real `|yaw2D|`), so error-vs-label largely measures the subject, not the
estimator. The full numbers are in `oak_v4_evidence/f15/v6_guard_replay.txt`.

### Why `h_m45` and `dc_body45L` fail — and why it is not a V6 defect

Raw Kalidokit `yaw3D` per block, guard not involved:

| block | gt | frame sign ok | yaw p50 | yaw p90 |
|---|--:|--:|--:|--:|
| `h_m45` | −45 | 58.6 % | **−0.1°** | +1.4° |
| `h_m90` | −90 | 0.0 % | **180.0°** | 180.0° |
| `dc_body45L_face0` | −45 | 0.0 % | **+47.3°** | +53.5° |

* **`h_m45`: `yaw3D` reads −0.1° while F-15 measured `|yaw2D| = 58.6°`.** The depth-derived yaw does
  not register that turn *at all*. That is the F-09 under-resolution root cause. **V6 cannot invent a
  sign for a measurement that reads zero**, and it correctly does not try.
* **`h_m90`: `yaw3D` reads exactly 180.0°** — the wrap. The guard catches it on 100 % of frames,
  which is precisely its job.
* **`dc_body45L_face0`: `yaw3D` reads +47.3° and the nose offset reads −0.284** — the same side as
  every +45° block — while `|yaw2D|` confirms a genuine ~50° turn. **Two independent signals say the
  subject turned right during a block labelled left.** This strengthens the mislabelled-block reading
  F-15 left open, but it is still not proof, and the block is included in every table above.

### A correction to F-15

F-15 scored blocks by **majority sign**, which marks a block CORRECT whenever >50 % of its frames
agree. `h_m45` is **58.6 % at frame level — a coin flip — and F-15 recorded it as CORRECT.**
**F-15's "±60° validated, 6/6 blocks" should be read with that caveat**: the positive side is solid
(100 % at frame level on every block), the negative side is not uniformly so.

---

## 6. Dynamic results

| block | n | held % | longest hold | max step |
|---|--:|--:|--:|--:|
| `m_slow` | 132 | 0.0 % | 0 | 3.7° |
| `m_normal` | 113 | 0.0 % | 0 | 2.1° |
| `m_fast` | 126 | 0.0 % | 0 | 5.0° |
| `m_rev_lr` | 136 | 3.7 % | 5 | **13.9°** |
| `m_rev_rl` | 117 | 0.0 % | 0 | 7.1° |
| `m_180` | 147 | **100.0 %** | **173** | 0.0° |
| `m_turn_arms` | 128 | 0.0 % | 0 | 6.1° |
| `m_turn_still` | 124 | 0.0 % | 0 | 3.9° |

Normal, fast and reversal motion runs with **zero holds** and steps ≤ 13.9°. The guard is inert
during ordinary movement — it fires only where the geometry actually collapses.

Whole-capture continuity, **guard output** (offline replay):

| | V5 (no guard) | **V6** |
|---|--:|--:|
| max frame-to-frame step | **359.9°** | **38.4°** |
| steps > 30° | 32 | **1** |
| sign flips | 142 (0.17/s) | 120 (0.15/s) |

The 38.4° figure is upstream of the ADR-027 conditioner (140 °/s). **The live session measured what
the viewer actually sees** — the rendered sum of the four nested torso bones, 58,605 samples:

| rendered torso yaw, frame-to-frame | value |
|---|--:|
| p50 | **0.007°** |
| p95 | **0.201°** |
| p99.9 | **1.157°** |
| **max** | **2.45°** |
| steps > 10° | **0** |
| steps > 30° | **0** |

**No snap is reachable by the viewer.** Max rendered |yaw| was 61.2°; the 1.2° over the envelope is
Slerp/lerp dynamics across the four nested bones, not a guard failure — the guard clamps its own
output to exactly 60°.

---

## 7. Wrap / edge-on behaviour

The guard fired on **896 frames = 4.31 %**, in **11 runs**, and every one is at ±90° or in the 180°
turn — `h_p90` (100 %), `h_m90` (100 %), `m_180` (100 %), plus 3.7 % of `m_rev_lr`. During those runs
the torso holds its last valid yaw with a max step of **0.0°**.

`h_m90` is the honest failure: the guard held, but what it held was already the wrong sign, so the
avatar faces the wrong way for the whole block. **The guard prevents the ±180° flip; it does not make
±90° work.** That is the documented limit, not a bug.

---

## 8. Hold statistics

**Offline replay** (F-15 frames): 896 held = 4.31 %, 11 runs, average 3.19 s, **longest 12.33 s**.

**Live session** (58,604 guard evaluations at 220.6 Hz):

| metric | value |
|---|--:|
| hold runs | 94 |
| total held | 7,780 = **13.3 %** |
| median run | 7 frames = **0.03 s** |
| p95 run | 483 frames = **2.2 s** |
| **longest run** | **2,356 frames = 10.7 s** |

**Flagged explicitly, per §10 of the brief.** The median hold is 0.03 s — invisible — and the guard is
completely inert during ordinary movement. But turning past ±90° and staying there freezes the
avatar's torso for as long as the pose is held: **10.7 s measured live**, and the offline replay
independently predicted 12.33 s.

**It was measured, not smoothed away** (§6 of the brief). No damping was added, because damping hides
staleness rather than fixing it, and the correct fix is a policy decision — decay toward frontal, or
accept the freeze inside a documented range — not a filter constant.

---

## 9. Regressions

**None.** 145/145 EditMode tests pass, including the full pre-existing V5 / TrunkGate / ArmAimSolver /
Arm V2 suite (94 tests, all previously passing, all still passing). `git diff` confirms no production
file outside the two listed in §1 was modified.

---

## 10. Known limitations

1. **The torso reads ~50° while the user stands still.** The dominant defect, and the reason the
   product answer is no. Measured live at **−49.6° median** with the subject independently confirmed
   square (66.9 px shoulder span vs ~64 px front-on), stable to 0.2° over 150 frames. Cause: raw
   shoulder Δz **−101 mm** against an **89 mm** disparity step at 1.33 m — one rung. **Not a V6
   defect**: −49.6° is inside the envelope and V6 correctly passes it through. This is F-09 /
   ADR-038, untouched by V6 and untouchable by it.
2. **Worst hold 10.7 s live** (§8). Unresolved by design; needs a policy decision.
3. **±90° is not solved and is not claimed to be.** The guard converts a wrong-and-flipping torso into
   a wrong-and-stationary one. `h_m90` holds an incorrect sign for its whole block.
4. **The magnitude is under-resolved in both directions.** `yaw3D` reads −18.6° for a −60° block and
   **−0.1° for a real 58.6° turn** (`h_m45`), while producing ~50° on a square stance. V6 changed
   *which* yaw reaches the composition, not how well it is measured.
5. **The ±60° envelope rests on F-15's 6 blocks**, and F-15's block-majority metric hid frame-level
   instability on `h_m45` (§5). The positive side is solid; the negative side is weaker than F-15
   stated.
6. **`dc_body45L_face0` remains formally unresolved** (§5), though two independent signals now favour
   the mislabelled-block reading.
7. **`>90°` magnitude is untouched.** `|yaw2D| = acos(...)` is bounded to 90° (ADR-041).
8. **Two subjects, one room, one distance band.** The live session is subject A at 1.33 m; the
   labelled replay is subject B at 1.27 m.
9. **Diagnostic labelling bug, found by the live session.** A frame held because `TrunkGate` reported
   `!Fresh` is reported as state `Valid` with a growing `HoldStreak` (the `!trunkFresh` branch of
   `TorsoYawGuard.Evaluate`). The live timeline shows 2,356-frame holds sitting under a "Valid 100 %"
   label. **Behaviour is correct** — the yaw is held either way — but the enum should distinguish
   "held because the trunk gate rejected" from "fresh and in range". Diagnostics only; fix in a
   follow-up.

---

## 11. Final verdict

```text
VERDICT: CONDITIONAL
```

Against the §10 acceptance criteria:

| criterion | result | |
|---|---|---|
| **Accuracy** — no systematic sign inversion in range | positive side 100 % on every block; **negative side 27–100 %**, driven by `yaw3D` reading ~0 at `h_m45` | **~** |
| **Accuracy** — no unexpected ±180° jumps | **none**, live or replayed | **✓** |
| **Accuracy** — no regression from V5 | 145/145 tests; V5 composition byte-identical; strictly better continuity | **✓** |
| **Continuity** — no large visible snaps | **live max rendered step 2.45°, zero > 10°** (V5: 359.9°) | **✓** |
| **Safety** — no arbitrary ±90°/±180° jump outside range | enforced by clamp + guard; max rendered 61.2° | **✓** |
| **Safety** — no opposite-direction flip | wrap never enters the held state (tested + live) | **✓** |
| **Safety** — behaviour explicitly controlled | 4 states, counted and exposed (labelling caveat §10.9) | **✓** |
| **Hold behaviour** — reported, not hidden | 13.3 % held, median 0.03 s, **worst 10.7 s — flagged** | **flagged** |

**V6 as a change: correct, minimal, well-tested, and it does exactly what F-10/F-14/F-15 predicted.**
CONDITIONAL only because of the 10.7 s freeze and the weaker-than-stated negative-side sign.

**V6 as an answer to the brief's closing question — "can the validated ±60° solution behave cleanly
enough in the actual mirror product?" — the answer is NO**, and the blocker is not V6. A user standing
still in front of the mirror sees a torso twisted ~50°, because one disparity rung of shoulder depth
noise is worth ~50° of commanded yaw at this baseline and resolution. Guarding the wrap and clamping
the range cannot help with that, and neither can any other estimator: five sign sources are already
closed (F-10…F-15), and this is a *magnitude* failure, not a sign one.

**Recommended next step, exactly one:** stop work on the torso-yaw *estimator* and take the
**measurement** to the F-09 root cause. The concrete options, in cost order, are a closer working
distance, higher RGB/depth resolution, sub-pixel disparity, a wider stereo baseline, or a second
viewpoint. Everything downstream — the V5 composition, the V6 guard, the ±60° envelope — is already
correct and can stay exactly as it is.
