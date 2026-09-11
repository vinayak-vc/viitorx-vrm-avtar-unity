# Virtual Mirror — Roadmap (Agent Summary)

Detail: `21_Roadmap.md`, `22_Milestones.md`.

| Phase | Focus | Milestone |
|-------|--------|-----------|
| 0 | Scaffold, settings, scenes, docs | M0 |
| 1 | Webcam + Pose + IK + VRM load + basic UI | M1–M2 |
| 1b | Face + Hands + smoothing | M3 |
| 1c | Calibration + polish UX | M4 |
| 1 ship | Soak, notices, EXE, builtins | M5 |
| 2 | Gallery, depth spike, FinalIK optional, gestures | M6+ |

**Rule:** Do not start marketplace / multi-person before M5.

---

## Full-body stability program (Milestone-2, OAK-D path)

Driven by [`AUDIT_FBT_2026-09-07.md`](AUDIT_FBT_2026-09-07.md). Each stage was accepted only on
measured live evidence, not on code review. Status table lives in [`ai_handoff.md`](ai_handoff.md).

| Stage | Focus | Status | ADR |
|-------|-------|--------|-----|
| **P0** | Confidence-gated limb hold (LimbGate) + 0.35 m distal caps + legs into the hold set | **COMPLETE** (live human acceptance) | ADR-028 |
| **P1-1** | Per-joint temporal tracking + plausibility — catches *confident-but-wrong* landmarks | **COMPLETE** | ADR-029 |
| **P1-2** | Frame freshness / throughput — latest-frame queue policy | **COMPLETE** (live human A/B) | ADR-030 |
| **P1-3** | Unity timestamped pose buffer + interpolation | **COMPLETE** | ADR-031 |
| **P1-4** | Skeleton constraints + long-horizon recovery (sidecar-side) | **REJECTED** (live human validation 2026-09-08) — see [`P1_4_CLOSEOUT_2026-09-08.md`](P1_4_CLOSEOUT_2026-09-08.md) | ADR-032 |
| **F-08** | Surface-aware depth sampling + advisory `depthQuality` (sidecar-side) | **COMPLETE**, A/B validated — see [`F08_SURFACE_AWARE_DEPTH_IMPLEMENTATION_2026-09-08.md`](F08_SURFACE_AWARE_DEPTH_IMPLEMENTATION_2026-09-08.md) | ADR-P010 |
| **Arm retarget V1** | Replace the Kalidokit Euler arm branch with a quaternion aim solve (`ArmAimSolver`) | **COMPLETE**, live A/B validated 2026-09-09 — error 35.78° → 0.46° mean. See [`UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`](UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md) | ADR-033 |
| **Arm roll V2** | Hysteresis + continuity guard on the elbow-plane roll reference; end-to-end stage forensics down to the skinned bones | **COMPLETE** — roll steps > 90° **6 → 0**; rendered VRM proven identical to the control rig (0.000000°). See [`UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md`](UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md) | ADR-034, ADR-035 |
| **Torso yaw V4** | Restore `kalidokitTorsoYawScale` 0 → 0.75 (V3 named the frozen torso as the dominant defect) and validate on the real OAK-D path | **CONDITIONAL** — stable (±180° flips 2/55,408; 0 output reversals) and no arm regression (p50 0.057–0.171° live), but the trunk has **no confidence gate** (an empty room commands +90° yaw → avatar twisted 23.82° at 0.75) and rest jitter is 5.4–7.6°. See [`UNITY_TORSO_YAW_V4_VALIDATION_2026-09-09.md`](UNITY_TORSO_YAW_V4_VALIDATION_2026-09-09.md) | ADR-036 |
| **Torso V5** | Trunk validity gate + relative-yaw composition (Hips absolute, Spine/Chest/UpperChest take `shoulderYaw - hipYaw`); bind `UpperChest` | **CONDITIONAL PASS** — rigid-turn/twist/opposite gain all **1.000 exact**; false +90° command **eliminated** (0 frames near ±90); tracking error −49/−61/−58 % on the OAK corpus; ARM V2 p50 **0.0000°**; 94/94 EditMode tests. OAK-D live motion list still unrun. See [`UNITY_TORSO_V5_RELATIVE_YAW_2026-09-09.md`](UNITY_TORSO_V5_RELATIVE_YAW_2026-09-09.md) | ADR-037 |
| **F-09 torso yaw quality** | Can the torso landmark measurement be given a reliable quality score? (audit only, nothing changed) | **CONDITIONAL PASS** — `hipLenErr` AUC **0.897** is independent but unsafe as a gate (rejects **19.7 %** of genuine turns, recovery latency 13.9 s); F-08 signals carry no signal (`validFrac` AUC 0.482). Root cause: yaw is **under-resolved** — 1 disparity step = 6.3–11.3° and `dz` is exactly 0 on **60.1 %** of frames. See [`F09_TORSO_YAW_QUALITY_AUDIT_2026-09-10.md`](F09_TORSO_YAW_QUALITY_AUDIT_2026-09-10.md) | ADR-038 |
| **F-10 yaw ground truth** | Validate `hybrid = sign(yaw3D)*abs(yaw2D)` against ground truth (experiment only, nothing changed) | **NO-GO as specified.** Magnitude **validated 4.5×** (yaw2D MAE **5.90°** vs yaw3D **26.70°**); sign **refuted** (78.5 %, and **50.5 %** on left turns). yaw3D reads **7.7°** at true +90° and **180°** at true −90°. See [`F10_TORSO_YAW_GROUND_TRUTH_2026-09-10.md`](F10_TORSO_YAW_GROUND_TRUTH_2026-09-10.md) | ADR-039 |
| **F-11 face sign** | Can a 2-D face/shoulder relation supply the yaw SIGN? (forensics only) | **NO-GO** — it measures **head** yaw: a head-only turn gives **2.3×** (nose) / **7.7×** (ears) the signal of a real 45° torso turn. Static: nose **87.84 %** (+90° = **53.33 %**, chance); hybrid MAE **31.60°**, worse than the depth-sign hybrid's 13.09°. See [`F11_FACE_SIGN_TORSO_YAW_2026-09-10.md`](F11_FACE_SIGN_TORSO_YAW_2026-09-10.md) | ADR-040 |
| **F-12 temporal sign** | Can sign continuity + zero crossings supply the yaw SIGN? (offline only) | **NO-GO** — the information is absent: a reversal and a return produce **identical** `\|yaw2D\|`. Sweep swings **75.4 pts** on one debounce step (0.00 %–96.01 %). Also found: `\|yaw2D\|` is **bounded to 90°**, so 180° is ambiguous in the magnitude itself. See [`F12_TEMPORAL_SIGN_TORSO_YAW_2026-09-10.md`](F12_TEMPORAL_SIGN_TORSO_YAW_2026-09-10.md) | ADR-041 |
| **F-13 hip depth sign** | Can the hip-line depth ordering supply the yaw SIGN? (offline only) | **NO-GO** — **4 correct / 2 wrong / 4 NO-SIGNAL** over 10 held blocks; **+90° defines a sign on zero frames**. Hips are 130 mm wide against a 78–88 mm disparity step = **1.1 steps at 45°**, and at ±90° the far hip is *occluded* so `dz` collapses to 0 with zero spread. Hybrid MAE **72.46°** vs 8.34° unsigned. See [`F13_HIP_DEPTH_SIGN_TORSO_YAW_2026-09-10.md`](F13_HIP_DEPTH_SIGN_TORSO_YAW_2026-09-10.md) | ADR-042 |
| **F-14 shoulder depth sign** | Does RAW shoulder Δz carry a sign *independent of* the wrapping `atan2` composition? (offline only) | **CONDITIONAL** — hypothesis **refuted**: raw Δz rescues **0 frames** `yaw3D` gets wrong and is silent on **89.5 %** of wraps; a **wrap-guarded `yaw3D` matches it to the decimal (8.34° = the perfect-sign ceiling)**. ±45° separates at **AUC 0.000**; an arbitrary sign at true 0° costs only **1.60°** vs production's 8.72°. Blockers: **3.7 % coverage at −90°** and a **27-point session gap** (F-10 70.83 % vs F-11 98.04 %). See [`F14_SHOULDER_DEPTH_SIGN_TORSO_YAW_2026-09-10.md`](F14_SHOULDER_DEPTH_SIGN_TORSO_YAW_2026-09-10.md) | ADR-043 |
| **F-15 cross-session validation** | Does the `yaw3D` + wrap guard hold up on a SECOND SUBJECT? (offline; new capture) | **CONDITIONAL** — the sign is **perfect to ±60° (6 blocks correct, 0 wrong, 0 no-signal)** and **fails completely at ±90° (0 correct, 1 wrong, 1 no-signal)** on subject B. No sign inversion (identical polarity on all 3 captures) and raw Δz again gives **1 rescue in 318 frames**, replicating F-14. But macro accuracy falls **59.4 pts** vs F-11 and the guarded hybrid MAE **5.13° → 45.29°**, tripping the brief's 15-point collapse trigger. The controlled variable is **shoulder pixel span** — 63.8 px front-on, 17.3 px at 60°, **7–8 px at ±90°** — not distance or session. See [`F15_CROSS_SESSION_TORSO_YAW_VALIDATION_2026-09-10.md`](F15_CROSS_SESSION_TORSO_YAW_VALIDATION_2026-09-10.md) | ADR-044 |
| **V6 torso wrap guard** | Ship the F-10 wrap guard + an explicit ±60° operating envelope between the V5 gate and the V5 composition | **CONDITIONAL** — worst torso step **359.9° → 38.4°** and steps >30° **32 → 1** over 20,783 real F-15 frames; ±180° flip never reaches the avatar; **145/145 EditMode** (51 new). Held only **4.31 %** of frames and **0 %** during normal/fast/reversal motion — but **worst hold 12.33 s**, and the negative-side sign is weaker than F-15's block-majority metric implied (`h_m45` is 58.6 % at frame level because `yaw3D` reads −0.1° for a real 58.6° turn). See [`V6_TORSO_YAW_IMPLEMENTATION_VALIDATION_2026-09-10.md`](V6_TORSO_YAW_IMPLEMENTATION_VALIDATION_2026-09-10.md) | ADR-045 |
| **F-16 measurement feasibility** | Can the OAK-D stereo configuration physically resolve shoulder depth well enough for torso yaw? (evidence only; production untouched) | **CONDITIONALLY FEASIBLE** — quantisation is characterised exactly (observed/theory **0.94–1.26**) and **removable**: sub-pixel 1/8 shrinks the 1.33 m step **83.5 → 12.0 mm at +2 ms latency, zero FPS cost**, cutting frame-to-frame torso instability **15.90° → 3.59°**. But quantisation is **NOT** the square-stance error: a **28× finer ladder moves it 14.68° → 13.52°**, and all 8 configurations at 1.33 m land 10.55–15.61°. The residual is a **+0.75 px stereo MATCHING error** (median over 7 distances), whose cost scales as Z²: **0.51° at 0.80 m → 24.87° at 2.00 m**. **At 0.80 m four of five configurations pass ±5°/±10° — production included, at median 0.00° — and all five are within ±10° on 100 % of frames; at ≥1.00 m none passes.** Full-body framing needs **≥1.24 m** (measured V-FOV 70.2°) — **the two windows do not overlap**. See [`F16_OAKD_TORSO_DEPTH_RESOLUTION_2026-09-11.md`](F16_OAKD_TORSO_DEPTH_RESOLUTION_2026-09-11.md) | ADR-046 |
| **F-17 wider-baseline feasibility** | Can a wider stereo baseline give enough torso-depth resolution at the full-body distance (>=1.24 m)? (evidence only; production untouched) | **NOT FEASIBLE** for the baseline route. **No wider-baseline hardware is attached** — the board's only other baselines are 37.4/37.6 mm, and the half-baseline CAM_A–CAM_C pair proved **unusable** (bulk depth off 21–41 %, an IR-cut colour sensor against unfiltered mono; 5 checks locate the fault in the pair, not the rig). The decisive evidence is F-16's **three independent f·B doublings**: ratios **1.067 / 0.791 / 0.887**, mean **0.915** — doubling f·B moves the bias **−9 %**, not the −50 % a pixel-domain bias needs, so this is **Case B**. Even the ideal model gives a 150 mm head **4.66° at 1.24 m and FAIL from 1.33 m**. **The lens is the real lever**: `B_req ∝ 1/(h·tan(vfov/2))` and Z_min falls with FOV, so at 95° V-FOV Z_min = 0.80 m and B_req = **58 mm, below the 75 mm already fitted** — and F-16 **measured** this camera passing at 0.80 m. See [`F17_WIDER_STEREO_BASELINE_FEASIBILITY_2026-09-11.md`](F17_WIDER_STEREO_BASELINE_FEASIBILITY_2026-09-11.md) | ADR-047 |
| **F-18 portrait camera feasibility** | Can the EXISTING camera, rotated 90 deg, give full-body framing AND accurate torso yaw at the same time? (evidence only; production untouched) | **CONDITIONALLY FEASIBLE — the blocker is gone.** At **0.90 m**: square-stance yaw **1.15 deg median / 2.33 p95**, frame-to-frame **1.98 deg**, body **full-body-SAFE**, V6 guard **100 % Valid with zero wrap/range/hold** on the square blocks, and the rotation costs **0.26 ms/frame (0.78 %)**. **All four distances pass** (0.78/0.80/0.90/1.00) where landscape passed none at ≥1.24 m. Transformation verified to **2.8e-17 m** before the camera moved; handedness preserved (Δx −316.8 vs −345.4 mm), square reads 2.89 deg not 180, **10/12** left/right pairs separate. CONDITIONS: **T-pose (span ~1.18 m) does not fit below 0.90 m** — the smaller FOV always binds the larger body dimension; **crouch loses the hands** (80.1 %); the occlusion asymmetry got **worse** not better (far-wider 23→73 % at 0.80 m); and **§13 multi-user + §17 avatar quality were NOT performed**. See [`F18_PORTRAIT_CAMERA_FEASIBILITY_2026-09-11.md`](F18_PORTRAIT_CAMERA_FEASIBILITY_2026-09-11.md) | ADR-048 |

**Result so far:** camera→avatar **~162 ms → ~102 ms**; limb spikes **−67…−76%**; stutter **−53%**;
trunk *improved* 33–36%; no bone-scale deformation (measured constant).

---

## TORSO YAW INVESTIGATION — decision tree

Read this instead of the six reports. Each stage records **what was tested → result → what it
changed → the next decision**. Every result is measured and carries an ADR.

```text
F-09  measurement-quality audit
      Tested   : can bad torso yaw be scored and gated?
      Result   : a predictor exists (hipLenErr AUC 0.897) but costs 19.7% of genuine turns.
                 ROOT CAUSE found: yaw is UNDER-RESOLVED - 1 disparity step = 6.3-11.3 deg,
                 and dz is exactly 0 on 60.1% of frames.
      Changed  : abandoned the confidence-gate approach; fix the MEASUREMENT, not the score.
      Decision : get ground truth.                                              [ADR-038]

F-10  ground truth + hybrid
      Tested   : hybrid = sign(yaw3D) * abs(yaw2D) against a measured floor protractor.
      Result   : MAGNITUDE VALIDATED - yaw2D MAE 5.90 deg vs production yaw3D 26.70 (4.5x).
                 SIGN REFUTED - 78.49% overall, 50.5% on LEFT turns.
      Changed  : magnitude now comes from 2-D foreshortening. The SIGN became the open question.
      Decision : find a sign source.                                            [ADR-039]

F-11  face / nose sign
      Tested   : does a 2-D face-vs-shoulder relation carry the sign?
      Result   : NO-GO. It measures HEAD yaw - a head-only turn gives 2.3x (nose) / 7.7x (ears)
                 the signal of a real 45 deg torso turn. Hybrid MAE 31.60.
      Changed  : ruled out every face-derived sign.
      Decision : try temporal continuity.                                       [ADR-040]

F-12  temporal sign continuity
      Tested   : hold a sign, flip it at genuine zero crossings.
      Result   : NO-GO. The information is ABSENT - a reversal and a return produce IDENTICAL
                 |yaw2D|. Sweep swings 75.4 points on one debounce step.
                 SEPARATE FINDING: |yaw2D| = acos(...) is BOUNDED TO 90 deg.
      Changed  : ruled out recovering the sign from the magnitude itself, and opened a second,
                 independent problem (magnitude beyond 90 deg).
      Decision : try a different physical signal - the hips.                    [ADR-041]

F-13  hip-line depth sign
      Tested   : is the hip depth ordering a better directional source than the shoulders?
      Result   : NO-GO. 4 correct / 2 wrong / 4 NO-SIGNAL over 10 blocks; +90 never defines a
                 sign. Hips are 130 mm vs a 78-88 mm step = 1.1 steps at 45 deg; at +-90 the far
                 hip is OCCLUDED. Hybrid MAE 72.46 - worse than emitting no sign at all.
      Changed  : ruled out the hips. Raised raw shoulder Delta-z as an incidental lead (9/10).
      Decision : test that lead properly.                                       [ADR-042]

F-14  raw shoulder depth sign                                          <-- CURRENT, JUST CLOSED
      Tested   : does raw shoulder Delta-z carry a sign INDEPENDENT of the wrapping atan2?
      Result   : CONDITIONAL. The hypothesis is REFUTED - raw Delta-z rescues 0 of the frames
                 yaw3D gets wrong and is silent on 89.5% of wraps. It ABSTAINS where yaw3D errs,
                 which is not information. A WRAP-GUARDED yaw3D matches the depth sign to the
                 decimal (8.34 deg = the perfect-sign ceiling) on every population tested.
                 What DID hold: +-45 separates at AUC 0.000 (completely disjoint distributions),
                 and an arbitrary sign at a square torso costs only 1.60 deg of avatar error
                 against production's 8.72, because sign * ~0 is ~0.
                 Blockers: 3.7% coverage at -90 deg, and a 27-point gap between two captures of
                 the same subject at the same distance on the same day (70.83% vs 98.04%).
      Changed  : NO new sign source is needed. The smallest V6 is a WRAP GUARD on the existing
                 path, not a new signal. Session dependence is now the gating unknown.
      Decision : validate the wrap guard on a second subject.                [ADR-043]

F-15  cross-session / second-subject validation                        <-- CURRENT, JUST CLOSED
      Tested   : does yaw3D + the F-10 wrap guard hold up on a DIFFERENT BODY?
      Dataset  : subject B, new capture, 28 blocks / 2895 labelled frames, 1.27 m (13 cm drift),
                 --headings full so +-30 and +-60 are held for the first time.
      Result   : CONDITIONAL, and the failure is BOUNDED, not general.
                 |heading| <= 60 : 6 blocks CORRECT, 0 wrong, 0 no-signal  -- perfect on a new body
                 |heading| == 90 : 0 correct, 1 WRONG, 1 NO-SIGNAL         -- total failure
                 No sign inversion: all 5 estimators infer identical polarity on all 3 captures.
                 Raw dz again gives 1 true rescue in 318 defined frames (0 at +-90) -> F-14 replicates.
                 Macro accuracy 97.28% (F-11) -> 37.85%; guarded hybrid MAE 5.13 -> 45.29 deg.
                 The perfect-sign CEILING only moved 5.13 -> 9.64, so the MAGNITUDE travelled to the
                 new subject nearly intact -- the entire loss is the sign at +-90.
      Cause    : SHOULDER PIXEL SPAN, not distance / session / confidence. 63.8 px front-on,
                 17.3 px at 60 deg, 7-8 px at +-90. Two 5x5 depth windows that close together on an
                 edge-on torso sample ONE surface, so dz has nothing to compare and atan2 wraps.
                 Distance was explicitly tested and REFUTED as the cause during F-15 prep.
      Changed  : the sign question is CLOSED for |yaw| <= 60 and CLOSED-AS-UNSOLVABLE at +-90 with
                 this baseline/resolution. Stop looking for sign sources entirely.
      Decision : implement the minimal V6 within a documented envelope.       [ADR-044]

V6    minimal wrap guard + explicit operating range                    <-- CURRENT, IMPLEMENTED
      Shipped  : Runtime/Retargeting/TorsoYawGuard.cs (new) + 18 lines in the driver.
                 ONE new stage between the V5 gate and the V5 composition. Nothing else moved:
                 TrunkGate, V5 weights, DampYaw, ArmAimSolver, Arm V2, P1-*, F-08 all untouched.
                 Policy: |yaw| > 150 -> HOLD (F-10's own guard); |yaw| > 60 -> CLAMP (F-15's
                 envelope); states Valid / WrapGuarded / OutOfRange / NoValue.
      Result   : CONDITIONAL.
                 Continuity FIXED: worst step 359.9 -> 38.4 deg, steps >30 deg 32 -> 1, and that
                 38.4 is upstream of the existing 140 deg/s limiter so the avatar cannot snap.
                 Guard is INERT in normal motion: 0 holds on slow/normal/fast/reversal blocks.
                 145/145 EditMode tests (94 pre-existing + 51 new), no regression.
                 NOT FIXED: worst hold 12.33 s (4x what F-15 estimated); +-90 still wrong, just
                 stationary instead of flipping; the magnitude is still under-resolved (yaw3D
                 reads -18.6 for a -60 block, and -0.1 for a real -45 turn).
      Corrects : F-15's block-MAJORITY metric marked h_m45 CORRECT at 58.6% frame-level agreement.
                 The "+-60 validated, 6/6 blocks" claim holds on the POSITIVE side (100% every
                 block) and is weaker on the negative side.
      Decision : the estimator is done; take the question to the SENSOR.        [ADR-045]

F-16  can the SENSOR resolve shoulder depth at all?                    <-- CURRENT, CLOSED
      Tested   : 12 stereo configurations (sub-pixel 1/8 and 1/32, mono 1280x800, RGB 1280x800,
                 HIGH_ACCURACY, extended disparity, no-RGB-alignment) x 7 distances x a rotation
                 set, on the real device. Nothing in production was modified.
      CONFIRMED: the quantisation model is exact. EEPROM gives f*B = 21,224.6 mm*px against the
                 21,216 fitted in F-09..F-15 -- a ratio of 1.0004. Observed/theory 0.94-1.26 over
                 9 configurations x 8 range bands. Production resolves 3 DISTINCT DEPTH VALUES in
                 a 200 mm window at 1.2 m.
      REMOVABLE: sub-pixel 1/8 cuts the 1.33 m step 83.5 -> 12.0 mm (7x) at +2 ms latency and no
                 FPS cost, and cuts frame-to-frame torso instability 15.90 -> 3.59 deg.
      REFUTED  : quantisation is NOT the square-stance error. A 28x finer ladder (83.5 -> 3.0 mm)
                 moves the error 14.68 -> 13.52 deg. All 8 configurations at 1.33 m: 10.55-15.61.
                 Higher RGB resolution changes it by 0.9 deg. HIGH_ACCURACY does not touch the
                 ladder at all.
      FOUND    : a +0.75 px SYSTEMATIC DISPARITY MATCHING error between the two shoulder windows
                 (median over 7 distances, spread 0.38 px). Sub-pixel subdivides a match; it
                 cannot repair one that is a whole pixel wrong. Cost scales as Z^2.
                 NOT the subject's posture -- a 180 deg turn did not flip the sign, and the same
                 subject reads 0.00-5.10 deg at 0.80 m. NOT a left/right sensor bias -- the
                 farther shoulder kept its ANATOMICAL side while swapping IMAGE side. Consistent
                 with an occlusion-edge artefact (the farther shoulder carried the wider depth
                 window on 69.5 % / 75.2 % of frames in 2 of 3 datasets; the third split 50/50).
      DISTANCE : at 0.80 m FOUR OF FIVE configurations pass, production included at median
                 0.00 deg / p95 5.21, and ALL FIVE are within +-10 deg on 100 % of frames. The
                 fifth (RGB800+mono800+sub) misses the 5 deg median by 0.10. At 1.00 m and
                 beyond NO configuration passes.
      CONFLICT : torso-yaw accuracy needs Z <= 0.80 m. Full-body framing needs Z >= 1.24 m
                 (measured V-FOV 70.2 deg). THE TWO WINDOWS DO NOT OVERLAP ON THIS CAMERA.
      Decision : see NEXT PATH -- this is now a PRODUCT decision, not an engineering one.
                                                                                [ADR-046]

F-17  would a WIDER STEREO BASELINE rescue the full-body distance?     <-- CURRENT, CLOSED
      Blocked  : NO WIDER-BASELINE HARDWARE IS ATTACHED. One device only. The board's other
                 baselines are 37.36 and 37.64 mm -- narrower, not wider.
      Attempted: the half-baseline CAM_A-CAM_C pair, host-rectified from the device's own EEPROM
                 with ONE shared SGBM matcher, over 402 synchronised A/B/C frame sets at
                 1.24-2.00 m. host B-C tracks the device to 1-8 %; host A-C is wrong by 21-41 %
                 (a constant ~1.93 px disparity deficit). FIVE checks put the fault in the PAIR:
                 epipolar |dy| p50 = 0.00 px, cx1 = cx2 exactly, the same matcher is accurate on
                 B-C, 2x/3x upsampling does not fix it, and all three sockets use the same
                 distortion model. CAM_A is IR-CUT COLOUR against UNFILTERED MONO -- torso depth
                 coverage 42 % vs 88 %. This hardware cannot supply a second usable baseline.
      DECISIVE : F-16's 1.33 m sweep holds the subject square through three pairs that differ
                 ONLY by mono resolution -- three independent DOUBLINGS of f*B.
                     baseline    -> mono800        89.0 -> 95.0 mm   ratio 1.067  (WRONG WAY)
                     sub3        -> mono800_sub3   82.2 -> 65.0 mm   ratio 0.791
                     sub3_rgb800 -> best           97.0 -> 86.0 mm   ratio 0.887
                 mean 0.915. A PIXEL-domain bias would give 0.50; a DEPTH-domain bias 1.00.
                 THIS IS CASE B: the bias does not follow f*B. Quantisation halved in all three,
                 so f*B certainly changed -- the BIAS did not.
      Ideal    : even the optimistic model needs B = 140/161/205/295/364 mm at
                 1.24/1.33/1.50/1.80/2.00 m. A 150 mm head passes ONLY at 1.24 m (4.66 deg) with
                 0.34 deg of margin, and fails from 1.33 m (5.36) to 2.00 m (11.98).
                 Under the MEASURED scaling it needs ~161 m. The models disagree by orders of
                 magnitude and the settling measurement is the one this hardware cannot make.
      FOUND    : the LENS is the lever, not the baseline.
                     B_req = H_body^2 * dd / (2 * h_px * W * tan(eps) * tan(vfov/2))
                 FOV enters twice -- a wider lens lets the subject stand closer AND the
                 requirement falls as Z^2.
                     V-FOV 70.2 (this camera) -> Z_min 1.24 m -> B_req 141 mm
                     V-FOV 95                 -> Z_min 0.80 m -> B_req  58 mm  (< the 75 we have)
                 That 0.80 m row is not an extrapolation: F-16 MEASURED this camera passing there.
                 And the camera is already 96.7 H x 70.2 V -- mounted in PORTRAIT that becomes
                 96.7 VERTICAL, framing a 1.75 m body at 0.78 m with the stereo pipeline
                 unchanged. Cost: horizontal coverage falls to 1.10 m, so spread arms leave frame.
      Decision : do NOT procure a wider-baseline head. Trial the portrait orientation first.
                                                                                [ADR-047]

F-18  does PORTRAIT orientation give full body AND torso yaw together?  <-- CURRENT, CLOSED
      Setup    : camera PHYSICALLY rotated 90 deg (cropping would keep the same 70.2 deg vertical
                 FOV and prove nothing). Direction auto-detected from the imagery: CCW, body
                 confidence 0.81 vs 0.58 and head-above-hips only that way.
      Verified : the transformation FIRST, before the camera moved. Rotate RGB + depth together
                 AND the intrinsics with them (rotating the image alone rescales every
                 back-projected X and corrupts the yaw triangle). 3-D geometry preserved to
                 2.8e-17 m; pixel mapping matches cv2.rotate exactly; FOV swaps to 96.7 V/70.2 H.
      RESULT   : THE BLOCKER IS GONE. At 0.90 m -- square yaw 1.15 deg median / 2.33 p95,
                 frame-to-frame 1.98, body full-body-SAFE, depth quality 0.95. ALL FOUR tested
                 distances PASS (0.78/0.80/0.90/1.00) where landscape passed NOTHING at >=1.24 m.
      V6       : guard replayed in observation mode, port VERIFIED against V6's own published
                 numbers (896 held / 4.31 % / 12.31 s vs 12.33 s). Portrait square blocks:
                 100 % Valid, ZERO wrap, ZERO out-of-range, ZERO held. Longest hold anywhere in
                 the portrait stream 1.11 s vs V6's 12.33 s. No new guard problem.
      Cost     : the rotation is 0.2584 ms/frame = 0.78 % of a 33 ms budget. Against the
                 like-for-like landscape capture at the same stereo config, portrait is 21 ms
                 FASTER. (The old "24 ms" figure was a depth-only sweep with no pose model.)
      Geometry : no inversion. Handedness identical (dx -316.8 portrait vs -345.4 landscape),
                 square reads 2.89 deg not 180, 10/12 left/right pairs separate. The polarity
                 difference vs F-16 is the SUBJECT turning the other way for the same word
                 (left30 dz +231 -> -87 mm), not the camera -- the F-15 label rule again.
      CONDITIONS (why not simply FEASIBLE):
                 1. ARM SPREAD is the new binding constraint. Rotating swaps which axis gets
                    96.7 and which gets 70.2, but the SMALLER FOV always binds the LARGER body
                    dimension. Measured T-pose span ~1.18 m against 1.12 m of horizontal cover at
                    0.80 m: CLAMPED at every distance up to 0.85, tight at 0.90, safe only at 1.00.
                    Arms-45 (~0.84 m) is safe from 0.78 m.
                 2. CROUCH loses the hands (80.1 % in frame at 0.90 m).
                 3. The OCCLUSION ASYMMETRY GOT WORSE, not better: far-wider 23.2 -> 72.7 % at
                    0.80 m, 85.9 -> 97.5 % at 1.00 m. Portrait wins on FRAMING; the F-16 matching
                    error is still underneath.
                 4. NOT PERFORMED: multi-user (no second person) and avatar human-quality (needs a
                    live portrait sidecar + Unity). The avatar check is the most important gap.
                 5. The mount is NOT LEVEL -- a ~19-32 deg downward pitch is implied by the
                    imagery. Does not invalidate the numbers (margins measured directly; a pure
                    pitch leaves a world-horizontal shoulder line at equal camera-Z) but needs a
                    tape measurement.
      Decision : adopt portrait at 0.90 m; port the rotation into the sidecar behind a flag and
                 run the live avatar session.                                   [ADR-048]
```

### NEXT PATH — matches the F-18 CONDITIONALLY FEASIBLE result

```text
TORSO STATUS
   V5 torso composition      OK
   V6 wrap protection        OK
   torso MEASUREMENT         OK AT 0.90 m IN PORTRAIT  (1.15 deg median / 2.33 p95, measured)
   torso ACCURACY            OK AT 0.90 m IN PORTRAIT
   full body + torso yaw     SIMULTANEOUSLY SATISFIED for the first time
   NOT YET PROVEN            on the avatar, with more than one person, or over time

1. PORT THE ROTATION INTO THE SIDECAR, BEHIND A FLAG.   <-- the one production change to make
   Rotate RGB and depth together AND rotate the intrinsics with them. f18_portrait.py has the
   verified transformation; do not re-derive it. This is a PRODUCTION change and needs its own
   task, its own ADR and its own live validation. F-18 changed nothing.

2. THEN RUN THE LIVE AVATAR SESSION F-18 COULD NOT.     <-- the biggest remaining unknown
   Everything in F-18 is measured at the LANDMARK level, upstream of the rig. Nothing yet shows
   how the VRM actually looks in portrait: torso twisting, arm distortion, knee bending, body
   stretching, snapping, jitter, mirroring. Use the existing avatar and change no constraints.

3. AND MULTI-USER, which was not tested at all: observer outside the zone, someone crossing
   behind, someone standing beside. The question is only whether ONE intended participant can
   still be identified - not multi-person tracking.

4. THEN RE-MEASURE THE FRAMING TABLE AGAINST THE LARGEST USER THE INSTALLATION MUST ACCEPT.
   Every threshold in F-18 section 4 is one subject: shoulder separation 333.1 mm, arm span
   ~1.18 m. A taller or wider user moves all of them.

5. LEVEL THE MOUNT and tape-measure height and tilt, then re-run f18_capture.py. The current
   mount is pitched down ~19-32 deg by derivation.

DECIDED BY F-18, do not revisit without new evidence:
   - Operating distance 0.90 m (1.00 m only if a fully-safe T-pose is required, which costs most
     of the torso margin: median 1.15 -> 4.74 deg).
   - Supported interaction envelope is arms-45. T-pose and crouch are OUTSIDE it.
   - Do not procure wider-baseline hardware (F-17).
   - Enable sub-pixel 1/8 when the rotation lands - F-18 used it throughout (F-16).

ALSO OPEN, UNAFFECTED BY F-18:
   6. +-90 deg still collapses (span 11-25 px, |dx| 22-63 mm). Portrait does not address it.
   7. MAGNITUDE still saturates at 30-40 deg for commanded 30/45/60.
   8. The V6 |dx| COLLAPSE (154.5 mm) remains uncharacterised.
   9. TorsoYawGuard labelling bug: a !trunkFresh hold reports as `Valid`. Diagnostics only.
  10. Then continue: hands / feet / recovery.
```

**Separation rule:** `SIGN` and `MAGNITUDE > 90°` are two different problems. A working sign yields
−90…+90 only. Do not report a working sign as solving torso yaw.


### Next candidates (NOT started — pick one deliberately, do not batch)

| Candidate | Why it is next | Prerequisite met? |
|---|---|---|
| **Upstream 2D landmark quality (elbow / wrist localisation)** | **DO THIS FIRST.** Now the dominant error source, measured on the avatar 2026-09-09: the retarget contributes 0.46° mean while the landmarks report a **121° elbow on a straight hanging arm** and missed arms-overhead entirely for a 15 s window (max tracked elevation 23.7°). F-08 §8 already showed `depthQuality` cannot see this class — it is 2D-semantic, not depth | yes |
| ~~Upstream measurement-quality audit (F-08)~~ | **DONE.** Audit + surface-aware depth sampler shipped; `depthQuality` is advisory only | — |
| ~~`BendSinMin` (arm roll near a straight elbow)~~ | **DONE 2026-09-09.** Replaced by a hysteresis + continuity-guard state machine; roll steps > 90° **6 → 0** on identical input. See [`UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md`](UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md), ADR-034. Outstanding: run the EditMode suite in batch mode | — |
| **Fix `rawLeftHand`/`rawRightHand` before palm work** | They resolve to CONTROL bones, not the raw skeleton, so `ApplyWrist` writes somewhere `Process()` overwrites. Inert today only because `wristRotationWeight = 0`; it will bite the moment the wrist is enabled. ADR-035 | yes |
| ~~Recovery / short-gap prediction + blended reacquire~~ | Attempted as P1-4 and **REJECTED** on live evidence. Do not retry on the same signal | blocked on F-08 |
| **Palm / wrist rotation** | The L-palm rate limiter saturates (median = p95 = 14.98° against a 15°/frame cap); hands are also ~40 ms off the body timeline after P1-3 | yes |
| **Foot / ground constraint** | Audit F-10, never implemented; foot slide remains | yes |
| Confidence normalisation | Audit F-08 — the root of "confident-but-wrong"; P1-1 works around it kinematically rather than fixing the scale | yes, but larger scope |

**Rule:** do not raise prediction horizons, enable IK, add smoothing layers, or change the P0/P1-1/P1-2
mechanisms without a new ADR — each is load-bearing and was tuned against measured evidence.

## Project status (as of 2026-09-09)

```text
P0 safety                         OK
P1-1 temporal tracker             OK
P1-2 latest-frame freshness       OK
P1-3 Unity pose buffer            OK
P1-4 kinematic recovery           REJECTED
F-08 surface-aware depth          OK  (depthQuality advisory only)
Arm retarget V1 (ArmAimSolver)    OK  (live A/B 2026-09-09, 35.78 deg -> 0.46 deg)
Arm roll V2 (hysteresis+guard)    OK  (>90 deg roll steps 6 -> 0 on identical input)
Rendered VRM vs control rig       OK  (0.000000 deg over 13308 samples - rig is NOT the fault)
Torso yaw restored (0 -> 1.0)     OK  (V5 relative composition; gain 1.000 exact)
Trunk validity gate               OK  (false +90 deg command eliminated; 0 frames near +/-90)
UpperChest bound + driven         OK  (was never written; now takes 0.40 of the twist)
EditMode suite                    OK  (94/94, invoked directly - Test Runner still closes this Editor)
Torso yaw quality gate            NO-GO for now (F-09: AUC 0.897 predictor exists but costs 19.7% of
                                  genuine turns; F-08 signals do not transfer; ADR-038)
Torso yaw is UNDER-RESOLVED       ROOT CAUSE  (1 disparity step = 6.3-11.3 deg of yaw; dz is exactly
                                  0 on 60.1% of frames) - fix the measurement, not the score
Live torso-heading ground truth   DONE  (F-10, 2520 labelled frames at 1.44 m)
2-D yaw MAGNITUDE                 VALIDATED  (MAE 5.90 deg vs production 26.70 deg = 4.5x; ADR-039)
Depth yaw SIGN                    REFUTED  (78.5% overall, 50.5% on LEFT turns - a coin flip)
Face-derived yaw SIGN             NO-GO  (F-11: measures HEAD yaw - a head-only turn gives 2.3x
                                  (nose) / 7.7x (ears) the signal of a real 45 deg torso turn; ADR-040)
Temporal sign continuity          NO-GO  (F-12: the information is absent - reversal and return give
                                  IDENTICAL |yaw2D|; sweep swings 75.4 pts on one debounce step; ADR-041)
yaw2D ambiguous beyond 90 deg     KNOWN LIMIT  (acos is bounded to [0,90]: a 180 turn and a 90-and-back
                                  are the same trace - affects the MAGNITUDE, not just the sign)
Hip-line depth sign               NO-GO  (F-13: 4 correct / 2 wrong / 4 NO-SIGNAL over 10 held
                                  blocks; +90 defines a sign on ZERO frames; hybrid MAE 72.46 deg vs
                                  8.34 unsigned. Root cause: hips are 130 mm wide vs a 78-88 mm
                                  disparity step = 1.1 steps at 45 deg, and at +-90 the far hip is
                                  OCCLUDED so dz collapses to 0 with zero spread; ADR-042)
SHOULDER raw depth sign           NO-GO as a NEW SIGNAL  (F-14: rescues 0 frames that yaw3D gets
                                  wrong; silent on 89.5% of wraps; -90 coverage 3.7%. It ABSTAINS
                                  where yaw3D errs, and abstention is not information; ADR-043)
Wrap-guarded yaw3D sign           LEAD, NOT VALIDATED  (F-14: holding the last sign while |yaw3D|>150
                                  reaches the PERFECT-SIGN CEILING - 8.34 deg MAE, 0.00% wrong sign -
                                  identical to the depth sign on every population. This is the
                                  smallest possible V6. BLOCKED on the session gap below.)
+-90 deg pose                     UNSOLVABLE at this baseline  (F-15: subject B gets 0 correct /
                                  1 wrong / 1 no-signal at +-90 while scoring 6/6 PERFECT at
                                  <=60 deg. Shoulder span 63.8 px front-on -> 7-8 px at +-90, so both
                                  5x5 depth windows sample ONE surface. Geometric, not tunable;
                                  ADR-044)
Torso yaw sign, |yaw| <= 60 deg   VALIDATED ON TWO SUBJECTS  (F-15: 6 blocks correct, 0 wrong,
                                  0 no-signal on a body the estimator had never seen; no sign
                                  inversion across 3 captures)
Sign-source search                CLOSED  (five tested and rejected: yaw3D depth F-10, face F-11,
                                  temporal F-12, hips F-13, raw shoulder dz F-14 + replicated F-15.
                                  Do NOT write another one.)
Minimal V6 (wrap guard only)      IMPLEMENTED, CONDITIONAL  (TorsoYawGuard.cs + 18 driver lines;
                                  worst torso step 359.9 -> 38.4 deg, steps >30 deg 32 -> 1, guard
                                  INERT during normal motion, 145/145 EditMode; ADR-045)
Torso freeze at +-90              OPEN USABILITY DEFECT  (V6 measured worst hold 12.33 s - 4x the
                                  F-15 estimate. Deliberately NOT damped away; needs a product
                                  decision: accept inside a documented range, or decay to frontal.)
Live end-to-end V6 session        DONE  (266 s / 58,605 avatar frames, OAK-D + Play mode. Guard
                                  CONFIRMED: worst RENDERED torso step 2.45 deg, ZERO steps >10 deg
                                  vs V5's 359.9. Freeze confirmed at 10.7 s.)
TORSO READS ~50 deg AT REST       THE BLOCKER - DOMINANT VISIBLE DEFECT  (live: subject standing
                                  SQUARE, confirmed by a 66.9 px shoulder span, and the avatar torso
                                  sits at -49.6 deg stable to 0.2 deg. Cause: raw shoulder dz -101 mm
                                  vs an 89 mm disparity step = ONE RUNG becomes ~50 deg of yaw.
                                  NOT a V6 defect - it is inside the envelope and passes as Valid.
                                  F-09/ADR-038 root cause; ADR-045)
Torso-yaw ESTIMATOR work          STOP  (five sign sources closed F-10..F-15; the remaining failure is
                                  MAGNITUDE, not sign. V5 composition, the V6 guard and the +-60
                                  envelope are all correct and stay as they are.)
Torso-yaw MEASUREMENT             THE NEXT ACTION  (closer working distance, higher RGB/depth
                                  resolution, sub-pixel disparity, wider stereo baseline, or a second
                                  viewpoint - in rough cost order)
Torso yaw MAGNITUDE               STILL UNDER-RESOLVED  (F-09, untouched by V6: yaw3D reads -18.6 deg
                                  for a -60 block and -0.1 deg for a real -45 turn. V6 changed which
                                  yaw reaches the composition, not how well it is measured.)
Torso yaw working range           +/-45..60 deg  (both signals fail at +-90: the shoulder line collapses
                                  to 7 px and both 5x5 depth windows sample ONE surface, dsd = 0.0.
                                  Geometric, not tunable.)
True-zero sign is CHEAP           MEASURED  (F-14: an arbitrary sign at a square torso costs 1.60 deg
                                  of avatar error vs production's 8.72 - sign * ~0 is ~0. This
                                  vindicates the sign x magnitude decomposition.)
Guided OAK-D motion capture       OWED  (harness built; needs a subject - slow/fast yaw, 45/90/180)
Palm robustness                   LATER
Foot / ground locking             LATER
IK                                OFF
```
