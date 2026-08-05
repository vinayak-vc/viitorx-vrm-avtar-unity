# Virtual Mirror
## Software Design Specification

Document ID: SDS-022  
Document Name: Milestones  
Version: 1.0  
Status: Active  

---

# Milestone M0 — Project Bootstrap

**Exit criteria**

- [ ] Folder scaffold + asmdefs per `02_ProjectStructure.md`  
- [ ] Bootstrap scene loads Mirror scene  
- [ ] Settings save/load roundtrip  
- [ ] Log file created on run  
- [ ] Docs index + agent handoff files present  

---

# Milestone M1 — Avatar Load

**Exit criteria**

- [ ] Load VRM from StreamingAssets  
- [ ] Load VRM from file picker  
- [ ] Swap avatar without restart  
- [ ] Last avatar restored next launch  
- [ ] Invalid VRM shows error; previous kept  

---

# Milestone M2 — Pose Tracking Vertical Slice

**Exit criteria**

- [ ] Webcam opens  
- [ ] MediaPipe Pose landmarks produced  
- [ ] Filter + retarget + IK move avatar arms/torso  
- [ ] ≥ 30 FPS min on mid PC; ≥ 60 FPS on recommended  
- [ ] Fake provider PlayMode test green  

---

# Milestone M3 — Face + Hands

**Exit criteria**

- [ ] Blink/smile/mouth open visible on VRM  
- [ ] Both hands finger motion  
- [ ] Per-modality smoothing UI  
- [ ] Toggle face/hands reduces CPU/GPU load  

---

# Milestone M4 — Calibration & UX

**Exit criteria**

- [ ] Calibration flow saves profile  
- [ ] Camera / smoothing / background panels complete  
- [ ] Diagnostics FPS + tracking status  
- [ ] Mirror mode correct  

---

# Milestone M5 — V1 Release Candidate

**Exit criteria**

- [ ] All Must FRs / NFRs  
- [ ] 2h soak pass  
- [ ] Performance budgets measured  
- [ ] ThirdPartyNotices.txt  
- [ ] 5–10 licensed built-in avatars  
- [ ] Windows EXE artifact  

---

# Milestone M6 — Phase 2 Kickoff (post-V1)

- Gallery / downloads  
- Depth provider spike  
- FinalIK optional path  
- Gesture prototype  

---

# Tracking

Update `tasks.md` and `ai_handoff.md` when a milestone checkbox flips.
