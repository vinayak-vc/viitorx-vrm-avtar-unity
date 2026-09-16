# Virtual Mirror — Project Overview

**Status:** Milestone-2 — OAK-D depth path shipping. Stability program **P0 + P1-1 + P1-2 + P1-3 COMPLETE** (see `ai_handoff.md` for the status table).  
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
