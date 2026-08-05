# Virtual Mirror
## Software Design Specification

Document ID: SDS-002  
Document Name: Project Structure  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Define the Unity project scaffold so any agent creates files in the correct place and avoids cyclic dependencies.

---

# 2. Repository Layout

This package lives under the Unity base project:

```
viitorx-vrm-avtar-unity-base-project/
├── AGENTS.md
├── Assets/
│   └── Games/
│       └── viitorx-vrm-avtar-unity/     ← this repo / package root
│           ├── docs/                   ← this documentation
│           ├── Runtime/                ← shipping code
│           ├── Editor/                 ← editor-only tools
│           ├── Tests/                  ← EditMode + PlayMode
│           ├── Samples~/               ← optional sample scenes/avatars
│           ├── package.json            ← if UPM package
│           └── README.md
└── Packages/
    └── manifest.json                   ← Unity packages (MediaPipe, UniVRM, etc.)
```

If implemented as a flat Unity project instead of a UPM package, mirror the same logical folders under `Assets/VirtualMirror/`.

---

# 3. Runtime Folder Scaffold

```
Runtime/
├── Bootstrap/
│   ├── AppBootstrap.cs
│   └── ServiceRegistry.cs
├── Core/
│   ├── Interfaces/
│   │   ├── IBodyTrackingProvider.cs
│   │   ├── IFaceTrackingProvider.cs
│   │   ├── IHandTrackingProvider.cs
│   │   ├── IAvatarLoader.cs
│   │   ├── IRetargeter.cs
│   │   └── IIkSolver.cs
│   ├── Models/
│   │   ├── PoseFrame.cs
│   │   ├── FaceFrame.cs
│   │   ├── HandFrame.cs
│   │   ├── JointId.cs
│   │   └── TrackingConfidence.cs
│   └── Events/
│       └── TrackingEvents.cs
├── Camera/
│   ├── WebcamCaptureService.cs
│   ├── CameraDeviceInfo.cs
│   └── CameraSettings.cs
├── Tracking/
│   ├── MediaPipe/
│   │   ├── MediaPipePoseProvider.cs
│   │   ├── MediaPipeFaceProvider.cs
│   │   ├── MediaPipeHandsProvider.cs
│   │   └── MediaPipeSession.cs
│   ├── Filtering/
│   │   ├── OneEuroFilter.cs
│   │   ├── JointFilterPipeline.cs
│   │   └── ConfidenceGate.cs
│   └── Calibration/
│       ├── PoseCalibrator.cs
│       └── CalibrationProfile.cs
├── Retargeting/
│   ├── HumanoidBoneMap.cs
│   ├── MediaPipeToHumanoidMapper.cs
│   ├── RotationFromVectors.cs
│   └── RetargetPipeline.cs
├── IK/
│   ├── AnimationRiggingIkDriver.cs
│   └── FinalIkDriver.cs              ← optional commercial path
├── Avatar/
│   ├── Vrm/
│   │   ├── UniVrmAvatarLoader.cs
│   │   ├── VrmAvatarInstance.cs
│   │   ├── VrmBlendShapeDriver.cs
│   │   └── VrmHandDriver.cs
│   └── AvatarSessionController.cs
├── Rendering/
│   ├── MirrorCameraController.cs
│   ├── BackgroundController.cs
│   └── DiagnosticOverlay.cs
├── UI/
│   ├── MirrorHud.cs
│   ├── AvatarLibraryPanel.cs
│   ├── CameraSettingsPanel.cs
│   ├── SmoothingPanel.cs
│   ├── CalibrationPanel.cs
│   └── BackgroundPanel.cs
├── Settings/
│   ├── AppSettings.cs
│   ├── SettingsStore.cs
│   └── SettingsSchema.cs
├── IO/
│   ├── AvatarFileService.cs
│   ├── PathProvider.cs
│   └── LogService.cs
└── Diagnostics/
    ├── FrameTimingSampler.cs
    └── PerformanceBudget.cs
```

---

# 4. Assembly Definitions

Prefer asmdefs to enforce dependency direction:

| Assembly | References | May not reference |
|----------|------------|-------------------|
| `VirtualMirror.Core` | UnityEngine | Tracking, UI, Avatar concretes |
| `VirtualMirror.Tracking` | Core | Avatar, UI |
| `VirtualMirror.Avatar` | Core, UniVRM | Tracking concretes |
| `VirtualMirror.Retargeting` | Core | MediaPipe, UI |
| `VirtualMirror.IK` | Core, Animation Rigging | MediaPipe |
| `VirtualMirror.UI` | Core | MediaPipe internals |
| `VirtualMirror.App` | All feature asmdefs | — (composition root) |
| `VirtualMirror.Editor` | App + Editor | — |
| `VirtualMirror.Tests` | App | — |

**Rule:** Concretes depend on interfaces in `Core`. Composition root (`App` / Bootstrap) wires implementations.

---

# 5. Scenes

| Scene | Role |
|-------|------|
| `Bootstrap.unity` | Loads services, then Additive-loads Mirror |
| `Mirror.unity` | Main mirror view + UI |
| `Dev_TrackingDebug.unity` | Landmark overlays, no full UI |
| `Dev_AvatarLoad.unity` | Isolated VRM load tests |

---

# 6. Prefabs

```
Prefabs/
├── Avatar/
│   └── AvatarRoot.prefab           ← empty root + IK rig hooks
├── UI/
│   ├── MirrorCanvas.prefab
│   └── DiagnosticHud.prefab
└── Tracking/
    └── TrackingRig.prefab          ← Animation Rigging constraints
```

---

# 7. StreamingAssets / Resources Policy

| Asset type | Location | Notes |
|------------|----------|-------|
| Built-in VRM | `StreamingAssets/Avatars/` | Runtime `File.Read` / UniVRM |
| MediaPipe models | `StreamingAssets/MediaPipe/` | `.task` / model files |
| UI sprites | Addressables or Resources/UI | Prefer Addressables long-term |
| Never put large VRM in Resources | — | Memory spike on load |

---

# 8. User Data Paths (runtime)

```
%USERPROFILE%/Documents/MyMirror/
├── Avatars/           ← user-imported .vrm
├── Settings/
│   └── settings.json
├── Calibration/
│   └── default.json
└── Logs/
    └── virtual-mirror-YYYYMMDD.log
```

Implemented by `PathProvider` — never hardcode absolute paths in feature code.

---

# 9. Naming Conventions (folders & types)

- Namespaces: `VirtualMirror.<Area>` e.g. `VirtualMirror.Tracking.MediaPipe`
- Files: one public type per file, PascalCase matching type name
- Interfaces: `I` prefix
- Providers: `*Provider`
- MonoBehaviours: thin; suffix `Controller` / `View` / `Driver` as appropriate

---

# 10. What Agents Must Not Do

- Do not put MediaPipe API calls inside Avatar or UI scripts  
- Do not put UniVRM load logic inside Tracking  
- Do not create new top-level folders outside this scaffold without an ADR  
- Do not add `var` / expression-bodied members (see AGENTS.md / SDS-023)
