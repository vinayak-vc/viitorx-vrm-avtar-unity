# Virtual Mirror
## Software Design Specification

Document ID: SDS-024  
Document Name: Risks  
Version: 1.0  
Status: Active  

---

# 1. Technical Risks

| ID | Risk | Impact | Mitigation |
|----|------|--------|------------|
| R1 | MediaPipe Unity plugin maturity / GPU path flaky | High | Abstract provider; CPU fallback; pin version; spike early in M2 |
| R2 | Landmark noise → jittery avatar | High | One-Euro + confidence hold + IK |
| R3 | Webcam FOV / user distance breaks proportions | Med | Calibration profile; UI guidance |
| R4 | VRM shader / URP mismatch | Med | Validate MToon/URP on first avatars |
| R5 | Main-thread hitch on VRM load | Med | Async bytes + progress UI; size gate |
| R6 | Memory leak on avatar swap | Med | Explicit dispose; PlayMode leak test |
| R7 | Hand/face cost blows budget on laptops | Med | Quality modes; stagger inference |
| R8 | Coordinate / mirror double-flip bugs | Med | Single mirror flag; unit tests |
| R9 | FinalIK vs Animation Rigging divergence | Low | Shared `RetargetResult`; feature flag |
| R10 | Depth camera scope creep | Med | Keep behind interface; Phase 3 |

---

# 2. Product Risks

| ID | Risk | Mitigation |
|----|------|------------|
| P1 | Users expect Kinect-level tracking from webcam | Set expectations; good lighting tips in UI |
| P2 | Avatar license violations on bundled characters | Legal review checklist before M5 |
| P3 | Scope expands to marketplace before core works | Phase gate: M5 before Phase 2 gallery |
| P4 | VRoid users confuse `.vroid` vs `.vrm` | Explicit UI copy: export VRM 1.0 |

---

# 3. Schedule Risks

| ID | Risk | Mitigation |
|----|------|------------|
| S1 | Tracking polish infinite loop | Timebox; ship body first (M2) then face/hands |
| S2 | Plugin integration unknown | Spike spike spike in first week of M2 |

---

# 4. Security / Privacy

| ID | Risk | Mitigation |
|----|------|------------|
| V1 | Camera always on perception | Clear indicator when tracking active |
| V2 | Avatar paths in logs | Filename only in privacy mode |
| V3 | Future cloud gallery | No credentials in repo; secure storage later |

---

# 5. Risk Review Cadence

Revisit this list at each milestone exit. New risks → add row + owner in `tasks.md`.
