# Virtual Mirror — Architecture (Agent Summary)

Canonical detail lives in SDS `04`–`11`, `16`, `18`. This file is the **agent entry** summary.

---

## Layers

```
UI / Mirror Camera
    ↓ commands
Application (session, settings)
    ↓
Domain: Retarget, IK, Calibration, Avatar session
    ↓ interfaces
Providers: MediaPipe, UniVRM, Webcam, Animation Rigging
    ↓
Infrastructure: threads, files, logs
```

---

## Pipeline

```
Camera → MediaPipe Pose/Face/Hands → JointFilter
      → Retarget (map + vectors) → IK → VRM bones
      → Face BlendShapes + Hand fingers
      → URP present
```

---

## Key Interfaces

- `IBodyTrackingProvider` / `IFaceTrackingProvider` / `IHandTrackingProvider`  
- `IAvatarLoader`  
- `IIkSolver`  
- `ICameraCapture`  
- `IAvatarLibrary`  

Composition root: `AppBootstrap` + `ServiceRegistry`.

---

## Assemblies

`Core` ← `Tracking` | `Retargeting` | `IK` | `Avatar` | `UI` ← `App`

Concretes never flow upward into Core.

---

## Threading

Inference off main thread when plugin allows; main thread applies immutable frames via double buffer. No VRM file IO on main thread.

---

## Hot Swap

Avatar, camera, smoothing, calibration, modality toggles — all without process restart.

---

## Extension

New tracker = new provider. New IK = new `IIkSolver`. Do not couple UI to MediaPipe or UniVRM.
