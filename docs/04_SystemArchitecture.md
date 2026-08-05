# Virtual Mirror
## Software Design Specification

Document ID: SDS-004  
Document Name: System Architecture  
Version: 1.0  
Status: Active  

---

# 1. High-Level Pipeline

```
USB Camera
    │
    ▼
Camera Capture Service
    │
    ▼
Body Tracking AI (MediaPipe Pose / Face / Hands)
    │
    ▼
Joint / Landmark Frames (+ confidence)
    │
    ▼
Joint Filter (One-Euro / EMA + confidence gate)
    │
    ▼
Retargeting System (MediaPipe → Humanoid map)
    │
    ▼
IK Solver (Animation Rigging / FinalIK)
    │
    ▼
VRM Avatar (bones + BlendShapes + hands)
    │
    ▼
Unity Render (Mirror view + UI)
```

---

# 2. Logical Layers

```
┌─────────────────────────────────────────────┐
│ Presentation (UI, Mirror Camera, Overlay)   │
├─────────────────────────────────────────────┤
│ Application (Session, Settings, Commands)   │
├─────────────────────────────────────────────┤
│ Domain Services                             │
│  Avatar │ Retarget │ IK │ Calibration       │
├─────────────────────────────────────────────┤
│ Providers (interfaces)                      │
│  IBody / IFace / IHand / IAvatarLoader      │
├─────────────────────────────────────────────┤
│ Adapters                                    │
│  MediaPipe │ UniVRM │ Webcam │ File IO      │
├─────────────────────────────────────────────┤
│ Infrastructure                              │
│  Threads │ Logs │ Paths │ Native plugins    │
└─────────────────────────────────────────────┘
```

Dependencies point **downward only**. UI never talks to MediaPipe directly.

---

# 3. Core Components

| Component | Responsibility |
|-----------|----------------|
| `AppBootstrap` | Create registry, start services, load scenes |
| `CameraCaptureService` | Frames + device lifecycle |
| `TrackingOrchestrator` | Schedule pose/face/hands; emit frames |
| `JointFilterPipeline` | Temporal smoothing + confidence |
| `RetargetPipeline` | Landmark → bone aims / targets |
| `IkDriver` | Solve and apply to rig |
| `AvatarSessionController` | Load/unload VRM; wire drivers |
| `VrmBlendShapeDriver` | Face → expressions |
| `VrmHandDriver` | Hand landmarks → finger bones |
| `SettingsStore` | Load/save JSON |
| `MirrorHud` | User commands |

---

# 4. Provider Pattern

```
IBodyTrackingProvider
    ├── MediaPipePoseProvider
    ├── MoveNetProvider          (future)
    └── DepthCameraPoseProvider  (future)

IFaceTrackingProvider
    └── MediaPipeFaceProvider

IHandTrackingProvider
    └── MediaPipeHandsProvider

IAvatarLoader
    └── UniVrmAvatarLoader

IIkSolver
    ├── AnimationRiggingIkDriver
    └── FinalIkDriver
```

Swapping a provider must not require changes to Retarget or UI — only Bootstrap / Settings.

---

# 5. Session State Machine

```
Uninitialized
    → Initializing (services + camera)
    → Ready (idle avatar or last avatar)
    → Tracking (camera + inference + apply)
    → Calibrating
    → AvatarLoading
    → Error (recoverable)
    → ShuttingDown
```

Invalid transitions are logged and ignored.

---

# 6. Data Contracts (conceptual)

```csharp
struct PoseFrame {
    long TimestampMs;
    Vector3[] Positions;      // normalized or world-mapped
    float[] Confidences;
    bool IsValid;
}

struct FaceFrame {
    long TimestampMs;
    // landmarks or derived expression weights
    float BlinkL, BlinkR, MouthOpen, Smile, Angry, Surprised;
    Vector2 EyeLook;
    Quaternion HeadRotation;
}

struct HandFrame {
    long TimestampMs;
    bool IsLeft;
    Vector3[] Joints21;
    float[] Confidences;
}
```

Frames are immutable after publish. Consumers copy what they need.

---

# 7. Control Plane vs Data Plane

| Plane | Examples |
|-------|----------|
| Control | Load avatar, change camera, start calibration, set smoothing |
| Data | PoseFrame stream every capture tick |

Control uses explicit methods / UI commands. Data uses events or a lock-free double buffer.

---

# 8. Failure Isolation

| Failure | Behavior |
|---------|----------|
| Camera lost | Tracking pauses; UI warning; retry |
| Inference timeout | Keep last good pose; decay confidence |
| Bad VRM | Abort load; keep previous avatar |
| Low confidence | Hold / damp motion; do not snap |

---

# 9. Extensibility Hooks

- New tracking backend → implement provider interfaces  
- New avatar format → `IAvatarLoader` + bone driver adapters  
- New IK → `IIkSolver`  
- Plugins (Phase 2+) → load assemblies that register into `ServiceRegistry`

See `18_Extensibility.md`.
