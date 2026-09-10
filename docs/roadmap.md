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

**Result so far:** camera→avatar **~162 ms → ~102 ms**; limb spikes **−67…−76%**; stutter **−53%**;
trunk *improved* 33–36%; no bone-scale deformation (measured constant).

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
Hip-line depth sign               NEXT  (last untested candidate; hips do not rotate with the head;
                                  1 bit not a magnitude; offline-testable on existing captures)
Guided OAK-D motion capture       OWED  (harness built; needs a subject - slow/fast yaw, 45/90/180)
Palm robustness                   LATER
Foot / ground locking             LATER
IK                                OFF
```
