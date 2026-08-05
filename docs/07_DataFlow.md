# Virtual Mirror
## Software Design Specification

Document ID: SDS-007  
Document Name: Data Flow  
Version: 1.0  
Status: Active  

---

# 1. End-to-End Flow

```
USB Camera
    ↓
WebcamCaptureService  →  Texture / CPU bytes
    ↓
MediaPipe Session
    ├── Pose Landmarker   → PoseFrame
    ├── Face Landmarker   → FaceFrame
    └── Hand Landmarker   → HandFrame×2
    ↓
JointFilterPipeline
    ↓
RetargetPipeline  →  bone aims / world IK targets / expression weights
    ↓
├── IkDriver          → upper/lower body
├── VrmBlendShapeDriver → face
└── VrmHandDriver       → fingers
    ↓
Animator + Animation Rigging
    ↓
URP Render → Mirror display
```

---

# 2. Double-Buffer Publish

```
Inference thread          Main thread
─────────────────         ─────────────────
write Buffer[back]
flip index  ───────────►  read Buffer[front]
```

- Writers never mutate front while readers use it  
- Readers treat frame as immutable  
- If inference slower than display, main thread reuses last frame (hold)

---

# 3. Pose Path Detail

1. Landmarks in image space (x, y normalized, z relative)  
2. Convert to Unity space (see `25_PosePipeline.md`)  
3. Confidence gate per joint  
4. One-Euro filter per axis  
5. Build bone direction vectors (e.g. shoulder→elbow→wrist)  
6. Map to Humanoid bones  
7. Set IK targets (hand, foot, head) and hint rotations  

---

# 4. Face Path Detail

```
Camera → Face Mesh landmarks
    → Feature extract (eye aspect, mouth open, brow, smile)
    → Map to VRM expression weights [0..1]
    → Head rotation from face pose
    → Apply BlendShapes + neck/head bone (or IK head)
```

Outputs for V1 expressions:

- blinking (L/R)  
- smile  
- angry  
- surprised  
- mouth open  
- eye direction  

---

# 5. Hand Path Detail

```
Hands → 21 joints each
    → filter
    → finger bone rotations (curl / spread)
    → wrist pose feeds arm IK target
```

---

# 6. Control Flow (commands)

```
UI Button
  → Application command (LoadAvatar, SetCamera, …)
    → Domain service
      → side effects (file IO async, recreate session)
        → UI status event
```

Data plane events must not trigger heavy control operations without debouncing.

---

# 7. Settings Flow

```
UI change → AppSettings (in memory)
         → SettingsStore.SaveAsync (debounce 300–500 ms)
Launch    → SettingsStore.Load → apply before tracking start
```

---

# 8. Logging Flow

| Level | Examples |
|-------|----------|
| Info | Avatar loaded, camera opened |
| Warning | Low confidence sustained, stale frames |
| Error | Load fail, native init fail |

Never log every joint every frame at Info.

---

# 9. Metrics Flow

`FrameTimingSampler` records:

- capture dt  
- inference dt  
- main apply dt  
- FPS  

Exposed to Diagnostic overlay and optional CSV for soak tests.
