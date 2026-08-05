# Virtual Mirror
## Software Design Specification

Document ID: SDS-013  
Document Name: UI System  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Provide a simple operator UI around the virtual mirror view without turning the first viewport into a dashboard.

---

# 2. Layout

```
+----------------------------------------+
|                                        |
|           Virtual Mirror               |
|             VRM Avatar                 |
|                                        |
+----------------------------------------+

[Load Avatar] [Camera Settings] [Smoothing] [Calibration] [Background]
```

- Primary surface = avatar composition  
- Controls = bottom or side toolbar / modal panels  
- Diagnostics = small corner readout (FPS, tracking state)  

---

# 3. Panels

| Panel | Functions |
|-------|-----------|
| Load Avatar | File picker (*.vrm); Avatar Library grid |
| Camera Settings | Device list, resolution, FPS, mirror toggle |
| Smoothing | Body / face / hands sliders; IK blend |
| Calibration | Start/cancel; pose instructions; save/reset |
| Background | Mode + color/image pick |
| Diagnostics | FPS, latency, confidence lights (dev or advanced) |

---

# 4. UX Rules

- One job per panel  
- Destructive actions (replace avatar) confirm if load > 1 s still in progress  
- Errors as toast + log, not silent fail  
- Keyboard: Esc closes panel; F11 fullscreen (optional)  

---

# 5. Tech

| Item | Choice |
|------|--------|
| Framework | UI Toolkit preferred for settings; uGUI acceptable if already in project |
| Scaling | Reference 1920×1080; scale with screen |
| Input | Input System for hotkeys |
| Localization | English V1; string table ready |

---

# 6. Command Binding

UI calls application commands only:

```
LoadAvatarCommand
SelectLibraryAvatarCommand
SetCameraCommand
SetSmoothingCommand
StartCalibrationCommand
SetBackgroundCommand
```

No MediaPipe / UniVRM calls from view classes.

---

# 7. States → UI

| App state | UI |
|-----------|-----|
| Initializing | Loading blocker |
| Tracking | Controls enabled |
| AvatarLoading | Progress on Load |
| Calibrating | Calibration panel exclusive |
| Error | Banner + retry |

---

# 8. Accessibility

- Buttons have clear labels  
- Critical status not color-only (icon + text)  
- Minimum click targets ~40×40 px  

---

# 9. Agent Notes

- Prefab the toolbar once; panels as separate documents/prefabs  
- Do not spawn full canvas per panel if avoidable  
- Keep mirror camera free of screen-space clutter in the avatar region
