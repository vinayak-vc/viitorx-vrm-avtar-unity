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
*(Superseded for the demonstration branch: multi-person tracking was built in F-32/F-33 — see
below. The rule still stands for the **mirror app**, which drives one avatar from one user.)*

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

F-19  does portrait survive the REAL pipeline and produce a credible avatar?  <-- CURRENT, CLOSED
      Integration: --portrait (default off) in wholebody_udp_sender.py, F-18 transform imported not
                 re-derived. git diff -- Runtime/ EMPTY. Equivalence re-verified 5/5 (0.000e+00 px
                 intrinsics, 0.0001 mm geometry, 0/9960 left-right identity).
      Live     : 134,959 rendered frames, 37 blocks, one rig, ~24 min of real camera + subject.
      AVATAR OK: square human -> square avatar, rendered yaw median 0.012 deg (range -2.59..0.30).
                 ZERO snaps > 10 deg in ALL 37 blocks; worst per-block frame-to-frame p95 0.503 deg
                 against a 5 deg target. Bone length 0.0011 %, lossyScale 1.000000 (attributed).
                 Zero left/right swaps in 80,835 frames. 29.9 fps, 31.8 ms capture->send, P1-2
                 freshness intact (stale-dropped p50 = 0).
      MOUNT    : F-18's derived 19-32 deg pitch was WRONG. Measured on the device's own BNO086:
                 ~6-7 deg before levelling, 5.89 deg after, roll +0.14 deg. Height still unmeasured.
      BLOCKER 1: shipped stereo config is NOT the validated one. Sub-pixel OFF gives 6 distinct
                 shoulder-dz values in 270 frames (235 on ONE 116 mm bin) = a 6.80 deg YAW QUANTUM
                 at 0.90 m, vs 0.85 deg with sub-pixel 1/8. A 6.80 deg quantum cannot express a
                 5 deg median. --subpixel-bits added (default -1 = no change); SHIPPING IT IS AN
                 OPEN DECISION and every F-19 number depends on it.
      BLOCKER 2: MULTI-USER FAILS. Second person present -> tracker switched to them at ~25 s and
                 NEVER returned (hip depth bimodal 1.25-1.40 vs 2.30-2.60 m; 0 % of frames in the
                 primary's band for the last 60 s). The avatar telemetry showed ZERO snaps and
                 7.8 mm max root steps - the lerp smoothed a total person switch into
                 perfect-looking output. RENDERED-BONE CONTINUITY CANNOT DETECT PERSON SWITCHING.
      BLOCKER 3: SIDECAR RESTART FREEZES THE AVATAR PERMANENTLY. seq restarts at 1, P1-3 rejects
                 everything as out-of-order (RejectedOutOfOrder 4948, LastSeqA stuck 22678,
                 packetAgeMs 208417) while IsRunning=True, ReceivedCount climbing, ParseErrors=0.
                 Pre-existing, not portrait. Recorded not fixed (section 1 forbids touching P1-3).
      F-20 IN  : both elbows fold to 177-180 deg (human max ~150) on 81-91 % of the hands-near-face
                 block, bend normal wandering 74.8/129.6 deg, robust to a 120 deg threshold, P0 gate
                 holding NOTHING. Knees clean. Whole-capture hinge spread WITHDRAWN as not robust.
      Decision : integration accepted; NOT ready for public use until blockers 1-3 are closed.
                                                                                [ADR-049]
```

### NEXT PATH — after the 2026-09-15 OFFLINE HAND-OFF-FIX PASS. F-21's silent wrong-person
### hand-off is FIXED (ADR-057) and verified offline; the live two-person session is now the only
### thing standing between F-21 and a verdict.

```text
F-21 SS30 (2026-09-15)  the SS27 silent hand-off is closed.
   WHAT CHANGED   reacquisition is PATH-consistent, not only position-consistent. A candidate
                  tracked walking in from outside the margin is refused. ~20 lines in
                  target_ownership.py, no new identity signal, no Runtime/ change.
   RESULT         123.webm wrong-person frames 102 (ownership off) -> 40 (F-21 shipped) -> 0 for
                  the hand-off segment. The hand-over becomes DECLARED (release + TARGET_SWITCH +
                  new epoch) instead of invisible.
   COST           96 frames (1.6 s) of the LEGITIMATE RETURNING OWNER withheld on that clip. A
                  returning owner and an intruder walking in are the SAME observation stream from
                  a hip position. The gate does not resolve the ambiguity - it resolves it towards
                  ANNOUNCING the change. RELEASE_TIMEOUT_S is the knob that prices it, left at 4.0 s
                  for the live session to arbitrate rather than tuned against one clip.
   NO REGRESSION  video.webm and 456.webm are bit-identical with the gate on and off.
   STILL OPEN     the rate at which a real room produces a refused returning owner. Only live data
                  can say. That is what WALK_IN_OWNER / WALK_IN_IMPOSTOR are for.
```


```text
STATUS
   V5 torso / V6 wrap guard  OK
   portrait in production    LANDED behind --portrait (default off)
   torso MEASUREMENT         OK in portrait at 0.90 m -- ONLY WITH SUB-PIXEL 1/8 (open decision)
   AVATAR QUALITY            STABLE, not VERIFIED-FAITHFUL. F-19 proved internal consistency
                             (0.012 deg median yaw, zero snaps, bone lengths to 0.0011 %, zero
                             swaps) which is NOT pose fidelity - all four are compatible with a
                             smoothly, stably WRONG pose. F-26 (2026-09-16) shows exactly that
                             on real footage: the debug skeleton follows a seated / arms-overhead
                             subject and the VRM does not.
                             THE RETARGET, NOT THE TRACKING, IS NOW THE DOMINANT ERROR SOURCE.
   POSE FIDELITY             MEASURED at last (ADR-062, F-26 section 11). The metric that had never
                             existed now exists, self-tests 22/22, and has been run against live
                             data on two clips through the production wire. FIRST BASELINE, video
                             path, subject at ~1.4 m:
                               arms   0.4-1.1 deg median, follow 1.00  -- they track
                               legs   9.2-15.1 deg median, follow 1.4-2.3x  -- OVER-DRIVEN
                               trunk  16.7 deg median, follow 0.27, 100 % of frames over 10 deg
                             CORRECTED 2026-09-16 (F-27 section 8.5): the trunk figures above were
                             measured against a RUNTIME kalidokitBodyTorsoRoll = 0. The scene ships
                             1. Re-measured on the identical clip from a freshly opened scene, the
                             trunk reads median 7.3 deg with follow 1.29 -- less than half the error
                             and a channel that FOLLOWS, not a dead one. The 8 s adaptive-baseline
                             high-pass on SAGITTAL lean is unaffected and still removes a sustained
                             lean, so "the avatar cannot sit" (F-26 4.2) stands. This also validates
                             the tasks.md item "torsoRoll 0 -> 1 applied, NOT validated".
                             Cross-checked in world space against PoseDebugSkeleton, which shares
                             none of the driver's conditioning: the two paths agree to 0.4 deg.
                             NOT a yaw result -- axial twist leaves the hip->shoulder line
                             unchanged, so this metric is structurally blind to it. NOT a sensor
                             result -- the video path has no stereo. NOTHING in the retarget was
                             changed on the strength of it; it is the BEFORE number.
   HUMANIZED SKELETON       IMPLEMENTED + MEASURED (F-27, ADR-063). A new layer between the
                             existing filtering and the existing retarget treats tracking output as
                             a SUGGESTION: fixed bone lengths, anatomical joint limits, teleport and
                             origin-collapse rejection, per-joint hold in the PARENT's frame, and a
                             0.15 s re-acquire blend. Nothing in the tracking, the filtering, P0-1,
                             P1-1/2/3, Arm V2 or Torso V5/V6 changed, and confidence passes through
                             untouched so P0-1 still sees an unobserved limb as unobserved.
                             MEASURED, both streams from the SAME frames in ONE pass:
                               bone-length deviation  median 0.0568 -> 0.0181 m  (-68 %)
                                                      worst  0.2526 -> 0.0520 m  (-79 %)
                               landmark jump          median 0.0123 -> 0.0119 m  (-3 %)
                                                      p99    0.0708 -> 0.0844 m  (+19 % WORSE)
                               poses no stateful stage touched          99.6 %
                             Tests 32/32 synthetic + fault injection, 18/18 analyser self-test.
                             TWO DEFECTS WERE FOUND BY THE NUMBERS after the suite was green: the
                             body-forward axis was ambiguous and the knee fix fired on 84 % of poses
                             (now 1.8 %, derived from the FEET), and the velocity clamp was measuring
                             its own output and fired on 80 % of poses (now 0.4 %).
                             NOT a sensor result -- video path only, no stereo.
                             AVATAR A/B NOW DONE (F-27 section 8). F-26's fidelity metric run with
                             the layer ON, OFF, and ON-with-bone-lengths-OFF, 4000+ live frames each,
                             reference = the RAW tracked pose in every arm:
                               ON vs OFF              0 bones better, 3 worse, 6 unchanged
                               ON-no-bone-len vs OFF  0 better, 0 worse, 9 unchanged
                             So the velocity clamp, the knee fix and the joint limits cost the avatar
                             NOTHING, and the entire ~1 deg arm cost is bone-length normalisation --
                             isolated by measurement, not inferred. That 1 deg IS the correction: ARM
                             V2 aims the bone at its input (follow 1.00 in every arm), so measured
                             against raw the error equals how far the layer moved the arm. Whether
                             that move is an improvement CANNOT be decided here, because deciding it
                             needs a reference better than the raw tracker and none exists.
                             F-27 does NOT fix the leg over-drive: follow stays 1.26-2.06 in all
                             three arms. That is the Kalidokit leg solve, not the input.
                             VERDICT: on CLEAN tracking the layer is neutral-to-slightly-negative for
                             avatar fidelity, and its value rests entirely on the fault cases this
                             clip does not contain (0 holds, 0 NaN, 0 collapses in either stream).
                             Do not claim it improves the avatar; do not switch it off on this either.
   SKELETON AS THE PRODUCT   NEW (F-28). Scenes/SkeletonShow.unity: three live-switchable modes
                             driven by the SAME production tracking and the F-27 layer, with the
                             retarget skipped entirely. 1 GLOW SKELETON (lines, cores, trails,
                             coloured by speed), 2 ENERGY BODY (a hologram skin built from the joint
                             positions -- no mesh, no skinning, no retarget, so the body cannot be in
                             a pose the tracker did not report), 3 MOTION EFFECTS (hand particles, a
                             beam, floor ripples). 114-118 fps in the editor. Driven from video only;
                             never seen with a person in front of an OAK-D.
   TRUST CHANNEL             NEW (F-29, ADR-066). The pipeline now PUBLISHES its own verdict: st
                             (P1-1/P1-4 state per joint), own (F-21 ownership), lat (measured
                             camera->payload latency). Optional and read-only -- nothing upstream
                             reads them back, a consumer that ignores them is unaffected. Drawn by
                             SkeletonShow mode 4, which is the first time P1-1/P1-4/F-21 have been
                             visible to anyone not reading a log file. Measured end to end: 53.4 ms.
   HANDS AND FEET DRAWN      NEW (F-29). Both were ALREADY on the wire and being discarded: feet as
                             JointId 29/30/31/32 inside lm (431/431 and 330/330 frames measured),
                             hands as 21 landmarks per hand reduced to five curls and dropped. Mode
                             5 draws the hands; every mode now draws feet as ankle-heel-toe.
                             TWO DEFECTS FIXED, both invisible until the feet were drawn: the figure
                             stood 153 mm INSIDE the floor in every mode since F-28, and every
                             floor-driven effect spawned 81-127 mm too high because the floor came
                             from the lowest ANKLE rather than the foot contacts.
                             MEASURED LIMITS, which bound what should be built: floor contact is
                             usable at ~1.4 m (21 mm smoothed) and NOT at ~2.9 m (81 mm -- and
                             smoothing makes it WORSE there, the signature of drift not noise).
                             Hands are 99.1% anatomically plausible at 1.4 m, 59.9% at 2.9 m.
   ATTRACT LOOP              NEW (F-29). Closes F-28 section 7.4: with nobody tracked every mode
                             previously drew NOTHING, so an empty room was indistinguishable from a
                             crashed machine. Attract is a synthetic pose SOURCE, not a separate
                             renderer, so it demonstrates the REAL modes and every future mode gets
                             it free. It never carries telemetry and is never scored.
   SEVEN EXPERIENCES         NEW (F-30, ADR-067). Footprints (stand still; the floor remembers,
                             built on F-29 planted-foot detection), Fluid (a real velocity field the
                             body stirs, which keeps swirling after you stop), ObjectPlay (bat, kick,
                             head, pinch-and-throw, hand-written physics because a tracked body is
                             not a rigidbody), BubblePop, PoseMatch, DepthReach, AirGraffiti. Each is
                             its own scene: a camera and one GameObject. All seven share TrackedStage
                             -- the socket-to-usable-body chain, owned once.
                             NOT RENDERED. Three Editors held the project lock all session, so this
                             is compile + headless logic + real-wire parsing only. No visual claim
                             about any of the seven has been checked. Tune on first run.
   TIME ECHO                 NEW (F-31, ADR-069). Four delayed copies of the tracked body trail
                             the live one. This is the one item that DEFEATS the project's hardest
                             limit rather than working around it: RTMW3D-x is single-person with no
                             detector and no track id, F-21's live two-person acceptance FAILED, and
                             multi-person is a MODEL change. Replaying the one person we can track
                             makes a crowd without touching any of that, and cannot fail in a new way
                             because everything shown is already-validated pose data. EchoBuffer is a
                             fixed clock-injected ring, 8/8 unit tests including the post-wrap case.
   SOUND                     NEW (F-31, ADR-068). Closes F-28 section 7.4, open since there were
                             three modes and the project contained no audio of any kind. Built as a
                             LAYER on ExperienceBase rather than a ninth scene, so all nine scenes
                             gained a drone (whole-body energy), a movement layer (extremity speed)
                             and arrival/departure cues without being edited; event cues went into
                             hooks that already existed. Synthesised at runtime -- no assets, no
                             import settings, scene stays a camera and one GameObject. Every pitch is
                             PENTATONIC and Play() takes a scale degree, not a frequency, so a random
                             human trigger source cannot produce a wrong note.
                             NEVER HEARD. AudioClip.Create is a native call, so none of the synthesis
                             runs headlessly. Retune the mix on first listen.
   MULTI-PERSON              NEW (F-32, ADR-070). The single-person limit is gone for the
                             demonstration branch. person-detection-retail-0013 on the OAK-D's
                             otherwise-idle VPU finds a median 7 of 7 dancers (the vendored BlazePose
                             blob managed 1-5) and costs NOTHING to the existing streams -- rgb
                             29.8 -> 29.7 fps, depth 29.6 -> 29.4. PersonTracker assigns stable ids
                             that are never reused, associating in 3-D using metric depth, which no
                             IoU-based tracker can: two people overlapping on screen at different
                             distances are trivially separable in Z. Assignment is OPTIMAL, not
                             greedy -- greedy is suboptimal on 54.6% of random 4x4 cost matrices and
                             its failure mode is exactly an ID swap between crossing people.
                             BACKWARD COMPATIBLE BY CONSTRUCTION: persons[0] is republished at the
                             payload root in the single-person shape, so the VRM mirror and all nine
                             experience scenes consume this sender unchanged and unaware. Verified
                             root lm == persons[0].lm on every packet and across 752 Unity frames.
                             Scenes/Bonds.unity is the first experience that NEEDS two people, and
                             was chosen first because it is robust to an ID switch -- nothing
                             accumulates per person, so a swap costs two people trading colours.
                             BUDGET IS REAL: RTMW3D is 20.7 ms with a FIXED batch of 1, so 2 people
                             run at 24 fps, 3 at 16, 4 at 12. --max-poses defaults to 3.
                             NEVER RUN WITH REAL PEOPLE. All evidence is recorded video.
   THE F-29 CORRECTION       456.webm contains SEVEN DANCERS. F-29 treated it as one subject at
                             2.9 m and drew range conclusions from it ("hands 59.9% plausible at
                             2.9 m", "floor 81 mm"). Those figures measure IDENTITY CONTAMINATION,
                             not distance: the crop migrates between dancers and the tracked body's
                             shoulder width ranges 0.068-0.455 m, a 30x pixel swing, with no
                             frame-to-frame jump above 172 mm. A genuine single subject at 1.96 m
                             gives 93.7% plausible hands. The real limit beyond ~2 m is UNTESTED.
                             Corrected in the F-29 report, both READMEs, both CHANGELOGs and every
                             code comment that cited it. The lesson is bigger than the numbers: the
                             single-person pipeline does not fail LOUDLY on multi-person input -- it
                             emits a plausible skeleton belonging to nobody, and it reached a report.
   PER-PERSON FILTERS        NEW (F-33, ADR-071). F-32 shipped multi-person with the entire signal
                             chain switched off and said so; this closes it. Every tracked person now
                             carries a full P0 + P1-1 + F-22 chain (P1-4 available and off, as in
                             production), pooled on their TRACK ID -- not on list position, because
                             the tracker re-sorts most-established-first every frame and an
                             index-keyed bank hands one person's One-Euro history to another.
                             THE ONE THING THAT IS NOT A STRAIGHT COPY IS SAMPLE RATE. One-Euro
                             derives velocity as delta * freq; three people cost three sequential
                             20.7 ms solves, so this loop runs at ~16 fps, and a filter told 30
                             over-estimates speed by 1.9x and OPENS UP when it should damp. Measured
                             at 10 fps the naive port is WORSE THAN NO FILTER AT ALL on the median
                             frame (35.6 mm vs 33.5) while costing 400 ms of lag. Each person now
                             measures their own cadence.
                             ADR-071: the FEET (WholeBody 17-22) and HEAD (0-4) were in no filter
                             group at all and produced the worst residual artefacts, for OPPOSITE
                             reasons -- feet fail with src=0 and need the bounded HOLD, the head
                             fails with src=1 and needs the displacement CAP. Implausible
                             single-frame steps over 2060 person-frames: 2158 unfiltered -> 630 with
                             the single-person grouping -> 77. That last step changed the distal,
                             trunk and hip figures by 0.0% and emitted exactly the same 40,200
                             joints, which is what a correct grouping fix looks like.
                             COST 0.88 ms per person-frame against 20.7 ms for the pose solve.
                             LAG IS 233 ms AT 30 fps and is NOT NEW -- it is the accepted
                             single-person tuning. Quote it with any jitter figure.
                             Unity needed NO change: the wire shape did not move.
   TRANSPORT ROBUSTNESS      FIXED (F-20A): sessions, stale watchdog, neutral failsafe. Reconnect
                             re-proven 2026-09-14 across the language boundary: three REAL producer
                             session ids -> the REAL C# TrackingStreamHealth (FirstSession once,
                             NewSession per restart, a dead producer's straggler -> OldSession).
   UNATTENDED OPERATION      PASS (F-20B), and a PRODUCTION-BLOCKING DEFECT WAS FOUND AND FIXED on
                             2026-09-14 (ADR-055): the sidecar's frames= heartbeat was printed only
                             AFTER a successful pose emission, so an EMPTY ROOM printed none at all,
                             the supervisor read that as a stuck startup, and it killed a healthy
                             sidecar every 45 s -> FAILED_PERMANENT in ~4 min. The original SS17
                             matrix passed only because a person stood at the camera throughout.
                             Fixed in the sidecar (idle liveness line). Automated suite 4/6 -> 6/6
                             WITH AN EMPTY ROOM. Deployment hardening 35/35.
   TARGET OWNERSHIP          CONDITIONAL (F-21), OFFLINE ENGINEERING COMPLETE. 46/46 unit, 63/63
                             across 17 deterministic multi-target scenarios, real 8-person footage
                             at the production-equivalent margin: 0 switches, 0 releases, 0
                             rejections. ONE REAL DEFECT FOUND AND FIXED (ADR-054: a returning owner
                             past REACQUIRE_WINDOW was rejected 51x as "not_owner", released, and
                             re-acquired with a spurious TARGET_SWITCH). ONE REAL SILENT
                             WRONG-PERSON HAND-OFF REPRODUCED AND IMAGED on real two-person footage
                             (ADR-056) -- documented, deliberately NOT fixed today.
   POSE VALIDATION           CONDITIONAL (F-22), OFFLINE ENGINEERING COMPLETE. 22/22 unit + 120/120
                             across 18 adversarial cases driven through the PRODUCTION insertion
                             point to what the consumer actually receives. Threshold table
                             RECOMPUTED in real degrees (the old one was a mixed-unit pixel space).
                             0 absolute-angle false rejections on legitimate single-person footage
                             (1 single-frame rate hold); all 12 real
                             absolute rejections came from multi-person footage and were each
                             classified against the raw image. Cost measured: 20.1 us/frame.
   multi-user                MEASURED, AND IT STILL FAILS. F-21 closes the M15-crop mechanism but
                             NOT the reacquire-path hand-off (ADR-056). Unchanged conclusion:
                             UNSUPPORTED for a public installation.

1. RUN A CLEAN LIVE F-21 TWO-PERSON SESSION.            <-- blocks "target ownership: PASS"
   f21_live_protocol.py now draws instructions ON the camera preview (fixed after attempt 1's UX
   failure). Launch through sidecar_supervisor.py if possible so a device crash (attempt 2's failure)
   recovers instead of ending the session -- needs --show/--cue-file passthrough added to the
   supervisor first. PRIORITY TARGET, now that it has a known-positive offline counterpart to compare
   against: the same-spot hand-off of ADR-056 -- have B walk into exactly where A was, within the
   reacquire window, and check whether target_id changes. Offline it does not.
   See docs/F21_SINGLE_PERSON_TARGET_OWNERSHIP_2026-09-14.md SS27.

2. RUN A LIVE F-22 L2 SESSION (hands near face).        <-- blocks "pose validation: PASS"
   The one test that reproduces F-19's own original defect against pose_validation.py's raw-geometry
   threshold. See docs/F22_HUMAN_POSE_VALIDATION_2026-09-14.md SS17/SS29. Watch it against the
   measured headroom, not the superseded one: the 160 deg elbow REJECT has only 9.9 deg of margin
   over legitimate single-person motion (SS25), not the 1-5 deg the old mixed-unit table implied.

3. DECIDE WHETHER SUB-PIXEL 1/8 SHIPS.                  <-- every F-18/F-19 number depends on it
   Shipped config has a 6.80 deg torso-yaw quantum at 0.90 m vs 0.85 deg with sub-pixel.
   --subpixel-bits exists and defaults to no change.

4. RE-RUN MULTI-USER AND BODY-SIZE GENERALISATION with more people, once F-21 reads PASS.

5. TAPE-MEASURE THE CAMERA HEIGHT (tilt 5.89 deg and roll +0.14 deg are now measured via the IMU).

6. TUNING FOLLOW-UP (not blocking): consider lowering --failed-permanent-retry (60s default) if
   camera outages on the real installation are expected to be brief -- it is now the dominant term
   in worst-case recovery time once a crash loop trips (F20B report SS13).

DECIDED BY F-18/F-19/F-20A/F-20B/F-21, do not revisit without new evidence:
   - Operating distance 0.90 m in portrait.
   - Supported envelope: full-body relaxed, arms-45, normal movement/reaching, stepping,
     entry/exit, SINGLE user. T-pose and crouch are outside it.
   - LIMITED: hands near face (impossible elbows), fast torso reversal (forearm snaps).
   - UNSUPPORTED: any multi-user environment; reliable +/-90 deg torso.
   - A dead stream ends in the NEUTRAL pose, never a frozen human one (F-20A).
   - Health = TrackingState, NOT ReceivedCount/IsRunning/ParseErrors (F-19 proved those lie).
   - Do not procure wider-baseline hardware (F-17).
   - The sidecar is supervised by a dedicated Python watchdog (sidecar_supervisor.py), NOT a
     Windows Service, NOT a bare .bat loop, NOT Task Scheduler alone (ADR-051). Do not re-litigate
     the mechanism choice without new evidence that the watchdog itself is insufficient.
   - `taskkill /T` is required on every sidecar kill - this venv's python.exe is a launcher whose
     Popen pid does NOT match the real interpreter's own os.getpid() (F-20B SS10).
   - Target ownership uses geometry only (position + scale continuity) - NO track id exists in this
     pipeline to use instead (RTMW3D-x is single-person top-down, verified from source, ADR-052).
     Do not invent an identity/ReID signal without a source change that actually provides one.
   - Pose validation (elbow/knee) is ANGLE-based, not bone-length-based (ADR-053) - P1-4's own
     rejected closeout found the length signal's natural variation overlaps real corruption on this
     hardware. Do not resurrect a bone-length-ratio rejection approach without new evidence the
     upstream measurement-quality problem P1-4's closeout named has actually been solved.
   - A LIVENESS heartbeat must report that the CAMERA LOOP is alive, NOT that a subject is present
     (ADR-055). Do not re-gate the sidecar's frames= line behind a successful pose emission, and do
     not "fix" a future variant of this in the supervisor - the supervisor's readiness contract was
     never the thing that was wrong.
   - TARGET_SWITCH == 0 is NOT evidence of correct ownership (ADR-056). It counts DECLARED
     hand-overs only; a silent wrong-person hand-off through the reacquire path is invisible to it,
     and one has now been reproduced and imaged on real footage. Quote silent wrong-person EMISSION
     (f21_adversarial.py's ground-truth-labelled metric) instead.
   - 123.webm is a TWO-PERSON clip (entry ~f626, exit shortly after), not the single-person stress
     video F-21 SS13 originally treated it as. video.webm is the only genuinely single-person clip
     of the three. Do not re-cite 123.webm as single-person evidence.

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

---

## 2026-09-15 (evening) — F-21 instrument ready; F-23 answers the milestone question

**The F-21 live two-person session DID NOT HAPPEN** — no second person was available. The
instrument was rebuilt and verified instead, and a separate milestone question was answered.

```text
F-21 INSTRUMENT     READY, NEVER RUN. 10 defects fixed before spending human time (report S32).
                    The worst: a cue.json write race that killed whole runs; a recorder that
                    captured 70.2 s of an 86 s session; composites silently built from the WRONG
                    WINDOW. Two NEW protocol cases - OWNER_OCCLUDED / OWNER_TURN - are the REAL
                    false-hold rate and no offline clip has ever exercised them.
F-23 RETARGET       PROVEN on real human footage: r = +0.714, 435/435 frames, parseErr = 0,
                    through the production landmark builder and wire. Retarget, arm solver, legs,
                    hands and pelvic roll all exercised.
PELVIC ROLL         FIXED (ADR-059). 25.1 deg p2p of hip tilt was being discarded by
                    kalidokitBodyTorsoRoll = 0. Image-plane derived, so it TRANSFERS to the OAK-D.
DAMPING             NOT the avatar/skeleton gap (ADR-060, measured negative result). Removing slerp
                    damping entirely buys 33 ms and is slightly worse. The gap is STRUCTURAL.
```

### The finding that should shape the next planning conversation

*"If the avatar matches the video, the OAK-D will follow"* is **false for the channel that matters**,
and this session demonstrated it rather than argued it:

```text
transfers to OAK-D      pelvic roll (image plane), arms, legs, hands, bone mapping, root policy
does NOT transfer       pelvic yaw, torso twist, waist bend - all built from shoulder/hip dZ
```

The dancer's real pelvic twist is **std 7.4 deg**. The OAK-D's torso-yaw REST jitter is **5.4-7.6
deg**. On the depth channel signal and noise are the same size, and the 8 deg deadzone that exists
to suppress the noise therefore zeroes **76.6 %** of the real motion. Even with perfect, noise-free
MONOCULAR depth, hip yaw was either unstable enough to swing the avatar into profile or - once
smoothed enough to be stable - below the gate. The hip line is ~0.21 m wide, so any depth error
becomes a large angle. **That is geometry, not tuning.**

**The lever is sub-pixel 1/8** (torso-yaw quantum 6.80 deg -> 0.85 deg at 0.90 m, +2 ms, no FPS
cost). Lower the noise floor and the deadzone can come down honestly. It is a decision, not research.

### NEXT, in order

```text
0. TWO REAL PEOPLE IN FRONT OF THE CAMERA <- the top item since F-32. Every multi-person number
                                            comes from recorded video or synthetic trajectories;
                                            nothing about identity through a REAL occlusion has
                                            been observed. Note this is a DIFFERENT session from
                                            item 1: that one tests whether single-person ownership
                                            refuses a hand-off, this one tests whether the
                                            multi-person tracker keeps two ids apart. Run them in
                                            the same booking - the same two people, ~45 min.
0b. OPEN THE SCENES AND LOOK AT THEM     <- still true for F-30/F-31/F-32/F-33. Nothing in the
                                            demonstration branch has EVER been rendered on a
                                            screen by a human. Every geometric and numeric claim
                                            is tested; no VISUAL claim is.
1. F-21 LIVE TWO-PERSON SESSION          <- blocks "target ownership: PASS". Instrument is ready.
                                            Needs TWO people, ~30 min. CLEAR CHAIRS FIRST: the
                                            tracker was seen locking an empty office chair at
                                            conf=0.38 vs a 0.30 floor, which voids a rep silently.
2. TRUNK-YAW NOISE FLOOR                 <- blocks the deadzone decision. ~30 s, ONE person, still,
                                            at 0.90 m. The 2026-09-15 attempt is INVALID (no
                                            subject was in frame) and its numbers are discarded.
3. RE-RUN UNITY EDITMODE 170/170         <- torsoRoll 0->1 is applied but NOT validated. Needs the
                                            editor CLOSED.
4. F-22 L2 hands-near-face live          <- blocks "pose validation: PASS"
5. SUB-PIXEL 1/8 DECISION                <- every F-18/F-19 number depends on it, and it is what
                                            unblocks item 2's follow-up.
```

Unchanged and not to be re-litigated: operating distance 0.90 m portrait; reliable +/-90 deg torso
not achievable with this sensor; do not procure wider-baseline hardware (F-17 - the LENS is the
lever).

**"single user only; multi-user UNSUPPORTED" is no longer true and is struck.** F-32 built a real
multi-person path (detector on the VPU, optimal 3-D assignment, stable ids) and F-33 gave each
person the full signal chain; see the section below. What replaces it: the **mirror app** still
drives one avatar from one user, the multi-person path has **never been run with real people**, and
the SINGLE-person sender must not be used where more than one person can appear - it does not fail
loudly there, it emits a plausible skeleton belonging to nobody.

---

## v1 PACKAGING (2026-09-16, ADR-064)

The last manual step is gone: Unity spawns `sidecar_supervisor.py` itself and kills it on exit, so
the user never opens a terminal. A build now carries the sidecar source in
`StreamingAssets/Sidecar/`.

**Shipping model:** sidecar `.py` source is bundled with the build; the virtualenv and the 369 MB
model are installed once on the target by `setup_sidecar.ps1`. The venv is deliberately NOT copied —
a Windows venv pins an absolute `home` to the base interpreter and carries no stdlib, so a copied one
works only on a machine with the identical Python at the identical path. Cost of this choice: the
target needs Python 3.10 and a one-time setup pass; it is not a double-click install. PyInstaller
removes that dependency and is the natural v2 step.

**The blocking item added by this work:**

```text
6. PLAY-MODE + BUILD ACCEPTANCE          <- the launcher's mechanism is verified directly (exact CLI,
                                            exact taskkill /F /T, 3/3 cycles, no orphan, 11/11 new
                                            tests) but NOT through Unity Play mode, and no build has
                                            ever been produced. Until both are run, "the sidecar
                                            starts automatically" is demonstrated for the mechanism
                                            and asserted for the integration.
```

Items 1-5 above are untouched by this work and remain open.

---

## MULTI-PERSON (2026-09-17, ADR-070 + ADR-071)

Reports: [`F32_MULTIPERSON_2026-09-17.md`](F32_MULTIPERSON_2026-09-17.md),
[`F33_PER_PERSON_FILTERS_2026-09-17.md`](F33_PER_PERSON_FILTERS_2026-09-17.md).
Evidence: `docs/evidence/f32/`, `docs/evidence/f33/`.

```text
what exists now       detector on the VPU -> stable ids -> N poses -> one datagram -> N bodies in
                      Unity -> Scenes/Bonds.unity, the first experience that needs two people
cost of the detector  none: rgb 29.8 -> 29.7 fps, depth 29.6 -> 29.4 (the VPU was idle)
the binding limit     RTMW3D 20.7 ms, FIXED batch of 1 -> 2 people 24 fps, 3 -> 16, 4 -> 12
signal quality        full P0 + P1-1 + F-22 per person; implausible steps 2158 -> 77 for 0.88 ms
compatibility         persons[0] republished at the root; the mirror app and all nine single-person
                      scenes run on the multi-person sender UNCHANGED
```

**What would move this from "built" to "trusted", in order:**

```text
1. TWO REAL PEOPLE. Nothing else on this list is worth doing first. Identity through a real
   occlusion is the one behaviour video cannot test, and metric depth is exactly what should make
   it work - which is a hypothesis, not a result.
2. RENDER IT. Bonds has never been seen.
3. PER-PERSON st IN UNITY. The wire now carries real per-joint tracking states PER PERSON (all -1
   before F-33). The root person's already reach the existing trust HUD; a crowd HUD needs
   PersonPose to carry them. Small, and it makes the HUD honest about people 2 and 3.
4. INFERENCE HEADROOM is now worth more than it was. It is no longer only about latency: it is the
   single number that decides how many people can be in the room at once.
5. A ReID / appearance model, IF AND ONLY IF the live session shows long occlusions are the
   dominant failure. Deferred deliberately in ADR-070 - a third network on a contended budget, and
   3-D position is a stronger signal than appearance at this resolution.
```

**Do not re-litigate:** the multi-person sender is a SECOND program, not a mode -
`wholebody_udp_sender.py` stays byte-identical and remains the mirror app's path. Do not generalise
F-21 ownership to N targets (it is a single-target state machine; this is an assignment problem).
Do not batch N crops into one RTMW3D call - the ONNX input is fixed at [1,3,384,288] with no batch
axis, so it would need a re-export.
