# Virtual Mirror — Architecture Decision Records

Add a new ADR for every major choice. Do not silently contradict Accepted ADRs.

---

## ADR-001 — Engine and stack

- **Status:** Accepted  
- **Date:** 2026-08-05  
- **Decision:** Unity 6 (2022 LTS fallback) + MediaPipe Tasks + VRM 1.0 (UniVRM) + URP  
- **Why:** Mature, cost-effective, VRoid-compatible avatars, single CV ecosystem for pose/face/hands  
- **Alternatives:** Unreal; MoveNet-only; custom avatar format  

---

## ADR-002 — IK backend

- **Status:** Accepted (V1)  
- **Decision:** Unity Animation Rigging as default `IIkSolver`; FinalIK optional later  
- **Why:** No mandatory commercial dependency for V1; interface allows upgrade  

---

## ADR-003 — Provider interfaces

- **Status:** Accepted  
- **Decision:** All tracking/avatar/IK/camera behind interfaces in Core  
- **Why:** Replaceability (vision §3.2); test fakes; depth cameras later  

---

## ADR-004 — Platform

- **Status:** Accepted  
- **Decision:** Windows x64 EXE only for V1  
- **Why:** Webcam + native MediaPipe story clearest; scope control  

---

## ADR-005 — Avatar format

- **Status:** Accepted  
- **Decision:** VRM 1.0 runtime load; ship built-ins + user file pick  
- **Why:** VTuber/VRoid ecosystem; hot-swap without rebuild  

---

## ADR-006 — Async / threading style

- **Status:** Proposed  
- **Decision:** TBD — UniTask vs Unity Awaitable vs Task  
- **Action:** Decide during M0/M1 before widespread async code  

---

## ADR-007 — Azure Kinect

- **Status:** Accepted  
- **Decision:** Do not design V1 around Azure Kinect  
- **Why:** Discontinued hardware; keep optional depth behind interface for RealSense/OAK-D later  

---

## ADR-008 — OpenCV

- **Status:** Accepted  
- **Decision:** Do not take OpenCV dependency unless calibration/preprocess requires it  
- **Why:** Reduce package surface; MediaPipe + Unity enough for V1
