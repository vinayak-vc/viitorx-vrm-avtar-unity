# Virtual Mirror — Project Overview

**Status:** Milestone-2 — OAK-D depth path shipping. Stability program **P0 + P1-1 + P1-2 + P1-3 COMPLETE** (see `ai_handoff.md` for the status table).  
**Demonstration branch:** F-28 → F-33. The avatar is parked (F-26: the tracking is sound, the *retarget* is not), and the debug skeleton is being built into the product — one show scene with five modes plus **nine** interactive experience scenes, all driven from joint POSITIONS with the retarget skipped entirely. See `architecture.md` → *Demonstration branch*.
**Multi-person:** F-32 added person detection, identity tracking with stable ids, and `Scenes/Bonds.unity` — the first experience that needs two people. F-33 gave every tracked person the full signal chain. Both are **backward compatible**: the mirror app and all nine single-person scenes run on the multi-person sender unchanged. Neither has been run with real people.  
**Stack:** Unity 6 + **OAK-D (DepthAI) + RTMW3D Python sidecar** + Kalidokit-ported retarget + VRM 1.0 (UniVRM); MediaPipe Tasks + Animation Rigging retained as the fallback path.  
**Platform:** Windows x64 EXE  

---

## What It Is

A real-time **virtual mirror**: a webcam drives a VRM avatar’s body, face, and hands so the user sees a digital character mirror their motion.

```
USB Camera → MediaPipe (Pose/Face/Hands) → Filter → Retarget → IK → VRM → Mirror UI
```

---

## V1 Scope

- Runtime load any `.vrm` + 5–10 built-in avatars  
- MediaPipe body + face + hands  
- Retarget + Unity Animation Rigging IK  
- Calibration, smoothing, camera settings, background  
- ≥ 60 FPS target, ≤ 50 ms latency aspirational  
- Hot-swap avatar/camera without restart  

## Not V1

Multiplayer, VR/AR, cloth authoring, marketplace, lip-sync from mic, non-Windows.

> **"Multi-person" moved.** It was out of scope for the mirror app and still is — one avatar, one
> user. But the *demonstration branch* tracks several people as of F-32, so "multi-user
> UNSUPPORTED" is no longer true of the system as a whole. What remains true: the VRM mirror drives
> one avatar from one person, and the multi-person path has never run with real people.

---

## Doc Map

| Need | Read |
|------|------|
| Vision / success | `00_ProjectVision.md` |
| Requirements IDs | `01_ProductRequirements.md` |
| Folders / asmdefs | `02_ProjectStructure.md` |
| Architecture | `architecture.md`, `04_SystemArchitecture.md` |
| Milestones | `22_Milestones.md`, `roadmap.md` |
| Next work | `tasks.md`, `ai_handoff.md` |
| Experience scenes | `F30_EXPERIENCES_2026-09-16.md`, `F31` (time echo + sound) |
| Trust channel / hands / feet / attract | `F29_TRUST_HANDS_FEET_ATTRACT_2026-09-16.md` |
| Multi-person: detection, ids, the crowd wire | `F32_MULTIPERSON_2026-09-17.md` (ADR-070) |
| Per-person filter chains | `F33_PER_PERSON_FILTERS_2026-09-17.md` (ADR-071) |
| Coding law | `AGENTS.md` (parent), `23_CodingStandards.md` |

Full index: `README.md` in this folder.

---

## Avatar Workflow (users)

1. Create in **VRoid Studio** → Export **VRM 1.0**  
2. In app: Load Avatar → pick `.vrm`  
3. Or pick from built-in library  

---

## Engineering Principles

Modularity, replaceable providers, runtime hot-swap, real-time over perfect accuracy, AI-agent-friendly structure, data-oriented pipelines.
