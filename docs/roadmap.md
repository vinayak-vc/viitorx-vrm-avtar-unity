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

**Result so far:** camera→avatar **~162 ms → ~102 ms**; limb spikes **−67…−76%**; stutter **−53%**;
trunk *improved* 33–36%; no bone-scale deformation (measured constant).

### Next candidates (NOT started — pick one deliberately, do not batch)

| Candidate | Why it is next | Prerequisite met? |
|---|---|---|
| **Upstream measurement-quality audit (F-08)** | **DO THIS FIRST.** P1-4 showed skeleton-level reconstruction cannot be made reliable on the present landmark geometry — the bone-length signal overlaps its own noise floor. Audit only, no implementation | yes |
| ~~Recovery / short-gap prediction + blended reacquire~~ | Attempted as P1-4 and **REJECTED** on live evidence. Do not retry on the same signal | blocked on F-08 |
| **Palm / wrist rotation** | The L-palm rate limiter saturates (median = p95 = 14.98° against a 15°/frame cap); hands are also ~40 ms off the body timeline after P1-3 | yes |
| **Foot / ground constraint** | Audit F-10, never implemented; foot slide remains | yes |
| Confidence normalisation | Audit F-08 — the root of "confident-but-wrong"; P1-1 works around it kinematically rather than fixing the scale | yes, but larger scope |

**Rule:** do not raise prediction horizons, enable IK, add smoothing layers, or change the P0/P1-1/P1-2
mechanisms without a new ADR — each is load-bearing and was tuned against measured evidence.

## Project status (as of 2026-09-08)

```text
P0 safety                         OK
P1-1 temporal tracker             OK
P1-2 latest-frame freshness       OK
P1-3 Unity pose buffer            OK
P1-4 kinematic recovery           REJECTED
Palm robustness                   NEXT
Foot / ground locking             LATER
Confidence normalization          IMPORTANT
IK                                OFF
```
