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
Upstream 2D landmark quality      NEXT  (now the dominant error source)
EditMode suite in batch mode      OWED  (MCP Test Runner closes this Editor)
Palm robustness                   LATER
Foot / ground locking             LATER
IK                                OFF
```
