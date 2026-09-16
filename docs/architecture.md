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

Two paths share the same Core interfaces. The **shipping** path is OAK-D (`useOakUdpTracking=1`,
`useKalidokitBody=1`, `useIkDriver=0`); the MediaPipe/webcam path remains as a fallback.

**Shipping path (OAK-D depth), as of ADR-028..031:**

```
OAK-D RGB + RGB-aligned stereo depth
  → latest-frame queue policy      (ADR-030: newest RGB, depth matched by timestamp)
  → RTMW3D inference (~21 ms)
  → depth sample 5x5/p30 + backproject
  → P0 smoother  (One-Euro + 0.35 m distal caps + bounded hold)     ADR-028
  → P1-1 JointTracker (TRACKED/WEAK/PREDICTED/LOST + plausibility)  ADR-029
  → UDP JSON  { lm[33], xyz, src, seq, t }
--------------------------------------------------------------- process boundary
  → OakDUdpPoseProvider (bg thread) → PoseSpaceConverter
  → P1-3 PoseBuffer  (ring of 16; render at now − poseInterpolationDelayMs)  ADR-031
  → KalidokitPoseSolver  (positions → per-bone euler)
  → P0 LimbGate  (confidence-gated hold — the FINAL safety layer)   ADR-028
  → KalidokitControlRigDriver → Vrm10 Runtime.Process → VRM avatar
```

**Fallback path (webcam/video):**

```
Camera → MediaPipe Pose/Face/Hands → JointFilter
      → Retarget (map + vectors) → IK → VRM bones
      → Face BlendShapes + Hand fingers → URP present
```

**Layering rule for the stability stack:** P1 improves the *signal*; P0 remains the *safety
mechanism*. A joint P1 marks LOST has its emit confidence zeroed, which is byte-identical to a real
occlusion, so the LimbGate still makes the final call. Never move safety out of P0.

**Measured budget (real OAK-D, subject present):** camera→UDP 62 ms + 40 ms presentation delay
≈ **102 ms** camera→avatar. Inference dominates the sidecar half.

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
