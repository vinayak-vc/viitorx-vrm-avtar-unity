# Virtual Mirror
## Software Design Specification

Document ID: SDS-015  
Document Name: Settings  
Version: 1.0  
Status: Active  

---

# 1. Schema (V1)

```json
{
  "schemaVersion": 1,
  "avatar": {
    "lastPath": "",
    "libraryId": "builtin_01"
  },
  "camera": {
    "deviceName": "",
    "width": 1280,
    "height": 720,
    "fps": 60,
    "mirrorHorizontal": true
  },
  "tracking": {
    "enablePose": true,
    "enableFace": true,
    "enableHands": true,
    "qualityMode": "Balanced"
  },
  "smoothing": {
    "body": 0.5,
    "face": 0.4,
    "hands": 0.35,
    "ikBlend": 1.0
  },
  "ik": {
    "backend": "AnimationRigging",
    "enableLegs": true
  },
  "background": {
    "mode": "Solid",
    "color": "#202020",
    "imagePath": ""
  },
  "display": {
    "fullscreen": false,
    "vsync": true,
    "showDiagnostics": false
  },
  "calibrationProfileId": "default"
}
```

---

# 2. Defaults

Ship sensible defaults for Brio/C920-class cameras and Balanced tracking.  
First launch: load built-in avatar `builtin_01`.

---

# 3. Persistence

| Concern | Rule |
|---------|------|
| Store | `SettingsStore` → JSON via `PathProvider` |
| Debounce | 300–500 ms after last change |
| Corrupt file | Rename to `.bak`, recreate defaults, Warning |
| Migration | `schemaVersion` switch in loader |

---

# 4. Runtime Apply

Changing settings must hot-apply when safe:

| Field | Apply |
|-------|-------|
| Smoothing | Immediate to filters |
| Camera device | Restart capture |
| Quality mode | Update orchestrator schedule |
| Avatar path | Only on explicit Load / startup |
| Mirror flag | Update capture + retarget together |

---

# 5. Calibration Link

`calibrationProfileId` points to `Calibration/default.json` (or named profiles later).

---

# 6. Agent Rules

- Strongly typed `AppSettings` class — do not scatter PlayerPrefs keys  
- UI binds to `AppSettings` view-model  
- Never block main thread on save
