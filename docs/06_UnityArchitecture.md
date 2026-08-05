# Virtual Mirror
## Software Design Specification

Document ID: SDS-006  
Document Name: Unity Architecture  
Version: 1.0  
Status: Active  

---

# 1. Design Rules

- MonoBehaviours are **thin**: lifecycle + serialization + view hooks  
- Logic lives in plain C# services / pipelines  
- Prefer composition over inheritance  
- No heavy work in `Update` beyond reading latest frames and applying results  
- Cache component references; no per-frame `GetComponent`  

Coding style law: parent `AGENTS.md` + `23_CodingStandards.md`.

---

# 2. Scene Graph (Mirror)

```
Bootstrap
└── ServiceRoot (DontDestroyOnLoad)
    ├── CameraService
    ├── TrackingOrchestrator
    ├── SettingsStore
    └── AvatarSessionController

MirrorScene
├── MirrorCamera
├── Lights
├── AvatarRoot
│   └── (Runtime VRM instance)
│       └── RigBuilder / IK constraints
├── BackgroundQuad / Sky
└── UI Canvas
    ├── Virtual Mirror frame (optional chrome)
    ├── Load Avatar
    ├── Camera Settings
    ├── Smoothing
    ├── Calibration
    ├── Background
    └── Diagnostics
```

---

# 3. Component Responsibilities

| Component | Type | Does | Does not |
|-----------|------|------|----------|
| `AppBootstrap` | MB | Wire registry, load scene | Tracking math |
| `WebcamCaptureService` | MB/service | Device open, texture | Inference |
| `TrackingOrchestrator` | MB | Tick providers, publish frames | Bone rotation |
| `RetargetPipeline` | Plain C# | Map + rotations | Rendering |
| `AnimationRiggingIkDriver` | MB | Set IK targets | MediaPipe |
| `UniVrmAvatarLoader` | Plain C# | Load VRM | UI |
| `VrmBlendShapeDriver` | MB | Apply expression weights | Camera |
| `MirrorHud` | MB | Buttons → commands | File parsing |

---

# 4. Animator & Rig

- VRM humanoid Animator remains the animation authority  
- Animation Rigging `RigBuilder` layered for IK after Animator  
- Do not fight Animator with direct `transform.rotation` on the same bones unless IK-owned  
- Fingers may be applied as late overrides if not under IK  

---

# 5. Prefab Strategy

| Prefab | Contents |
|--------|----------|
| `AvatarRoot` | Anchor, scale, RigBuilder placeholder, layer |
| `TrackingDebugOverlay` | Line renderers for joints |
| `MirrorCanvas` | Full HUD |

Runtime VRM is **not** a prefab; it is instantiated from bytes.

---

# 6. Script Execution Order

Suggested order (Project Settings → Script Execution Order):

1. `WebcamCaptureService` (early)  
2. `TrackingOrchestrator`  
3. Default time  
4. `AvatarSessionController` / drivers  
5. `DiagnosticOverlay` (late)  

IK evaluation follows Animation Rigging’s own update.

---

# 7. Editor vs Runtime

| Editor-only | Runtime |
|-------------|---------|
| VRM validation window | UniVRM runtime import |
| Landmark gizmo toggles | Same via diagnostics flag |
| Bake calibration helper | User calibration UI |

Keep Editor scripts under `Editor/` asmdef.

---

# 8. Addressables / Loading

V1 may load built-in avatars via `StreamingAssets` paths.  
Addressables optional for Phase 2 gallery downloads.

---

# 9. Layers & Tags

| Layer | Use |
|-------|-----|
| `Avatar` | Avatar meshes |
| `UI` | HUD |
| `Background` | Backdrop |
| `DebugDraw` | Optional overlays |

Mirror camera culling masks must exclude UI if UI is Screen Space Overlay; use Camera for World Space UI if needed.
