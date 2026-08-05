# Virtual Mirror
## Software Design Specification

Document ID: SDS-020  
Document Name: Test Plan  
Version: 1.0  
Status: Active  

---

# 1. Strategy

| Layer | Framework | Focus |
|-------|-----------|-------|
| EditMode unit | Unity Test Framework | Filters, vector rotations, settings migrate, bone map |
| PlayMode | UTF PlayMode | Avatar load, camera mock, session state |
| Manual | Checklist | Tracking quality, UX |
| Soak | Manual / scripted | 2h stability |

---

# 2. Unit Tests (must)

- One-Euro filter converges / no NaN  
- `RotationFromVectors` known axes  
- Confidence gate hold behavior  
- Settings schema v1 load + corrupt fallback  
- Humanoid map completeness for required joints  
- Mirror X transform single application  

---

# 3. PlayMode Tests

- Load built-in VRM from StreamingAssets  
- Replace avatar twice without leak markers (GO count)  
- Session transitions Ready → Tracking → AvatarLoading → Tracking  
- Calibration profile save/load  

Camera / MediaPipe: use **fake providers** injecting recorded `PoseFrame` sequences so CI does not need a webcam.

---

# 4. Manual Tracking Checklist

- [ ] T-pose detected  
- [ ] Wave left/right arms  
- [ ] Walk in place (if legs enabled)  
- [ ] Blink / smile / mouth open  
- [ ] Finger curl both hands  
- [ ] Occlusion recovery (hand behind back)  
- [ ] Mirror mode feels correct  
- [ ] Camera hot-swap  

---

# 5. Performance Tests

- Marker averages within budgets (`17_Performance.md`)  
- Performance mode improves FPS on low GPU  
- Memory flat over 30 min smoke  

---

# 6. Acceptance Gate (V1)

All Must requirements in `01_ProductRequirements.md` verified.  
No Sev-1 open bugs. Soak pass.

---

# 7. Test Data

```
Tests/Testdata/
  poses/wave.json
  faces/smile.json
  hands/fist.json
  avatars/minimal.vrm
```

Keep minimal VRM tiny for CI.

---

# 8. Agent Rule

New pipeline math → unit test in same PR.  
New provider → fake implementation for PlayMode.
