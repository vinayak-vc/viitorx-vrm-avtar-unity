# Virtual Mirror
## Software Design Specification

Document ID: SDS-009  
Document Name: MediaPipe System  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Provide real-time pose, face, and hand landmarks from RGB camera frames using MediaPipe Tasks, behind replaceable provider interfaces.

---

# 2. Tasks Used

| Task | Output | Consumer |
|------|--------|----------|
| Pose Landmarker | Body landmarks + world landmarks if available | Retarget / IK |
| Face Landmarker | Face landmarks + blendshape-like scores if available | VRM face |
| Hand Landmarker | 21 landmarks × detected hands | VRM hands + wrist IK |

Optional later: Holistic — only if it simplifies threading; not required for V1.

---

# 3. Component Diagram

```
WebcamCaptureService
        │ frames
        ▼
MediaPipeSession (native / plugin lifecycle)
        │
        ├── MediaPipePoseProvider  : IBodyTrackingProvider
        ├── MediaPipeFaceProvider  : IFaceTrackingProvider
        └── MediaPipeHandsProvider : IHandTrackingProvider
                │
                ▼
        TrackingOrchestrator → PoseFrame / FaceFrame / HandFrame
```

---

# 4. Session Lifecycle

1. Load model files from `StreamingAssets/MediaPipe/`  
2. Create Tasks with GPU delegate when available  
3. On each frame: `Detect` / `DetectAsync`  
4. On dispose: release native graphs before app quit  

Failed GPU init → CPU delegate + lower input resolution + Warning log.

---

# 5. Input Configuration

| Setting | Default | Notes |
|---------|---------|-------|
| Input size | 640×480 or 1280×720 | Trade accuracy vs cost |
| Pose model complexity | Full / Heavy selectable | Settings UI |
| Max hands | 2 | |
| Face | 1 | Single user V1 |
| Mirror input | Optional | If UI mirror on, keep math consistent |

---

# 6. Scheduling

To protect frame budget:

| Mode | Behavior |
|------|----------|
| Quality | Pose + Face + Hands every frame |
| Balanced (default) | Pose every frame; Face every frame; Hands every 2nd frame |
| Performance | Lower res; Hands every 3rd; Face every 2nd |

Orchestrator owns the schedule; providers stay dumb.

---

# 7. Provider Interface Contracts

Providers must:

- Accept camera frame reference / bytes + timestamp  
- Return structured frames with confidences  
- Be callable from background thread if plugin allows; otherwise orchestrator marshals  
- Not reference UnityEngine Transform APIs inside native callback beyond copying data  

---

# 8. Landmark Sets (reference)

### Pose (MediaPipe Pose)

Use standard 33 landmarks (BlazePose topology). Critical for V1:

- Shoulders, elbows, wrists  
- Hips, knees, ankles  
- Nose / ears (head hint)  

### Hands

21 landmarks per hand (wrist + fingers).  

### Face

Face Landmarker topology per chosen model; derive expression features rather than driving every vertex.

---

# 9. Coordinate Notes

MediaPipe image landmarks: x,y in `[0,1]` (or pixel — normalize consistently), z relative.  
Conversion to Unity is owned by Pose Pipeline (`25_PosePipeline.md`), not by the provider.

Providers should expose **raw MediaPipe space**; converters live in Retargeting / PosePipeline.

---

# 10. Failure Modes

| Mode | Handling |
|------|----------|
| No person | `PoseFrame.IsValid = false` |
| Partial occlusion | Low confidence joints gated |
| Plugin missing | App runs; tracking disabled; clear UI error |
| Model file missing | Init fail with path in log |

---

# 11. Alternatives Behind Same Interfaces

| Provider | Use when |
|----------|----------|
| MoveNet | Pose-only light path |
| BlazePose TFLite standalone | Experiments |
| RealSense / OAK-D depth fusion | Better scale / occlusion |

Never hardcode MediaPipe types into Retargeter.
