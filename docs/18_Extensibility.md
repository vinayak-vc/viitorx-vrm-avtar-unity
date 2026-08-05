# Virtual Mirror
## Software Design Specification

Document ID: SDS-018  
Document Name: Extensibility  
Version: 1.0  
Status: Active  

---

# 1. Principle

Every external capability is behind an interface. Concrete SDKs are adapters.

Incorrect: `AvatarController` → `MediaPipe`  
Correct: `AvatarController` → `IBodyTrackingProvider` → `MediaPipePoseProvider`

---

# 2. Extension Points (V1)

| Interface | Swap examples |
|-----------|---------------|
| `IBodyTrackingProvider` | MediaPipe, MoveNet, depth fusion |
| `IFaceTrackingProvider` | MediaPipe Face, future ARKit (non-Windows) |
| `IHandTrackingProvider` | MediaPipe Hands, gloves (future) |
| `IAvatarLoader` | UniVRM, future GLB humanoid |
| `IIkSolver` | Animation Rigging, FinalIK |
| `IAvatarLibrary` | Local files, Phase 2 remote gallery |
| `ICameraCapture` | WebcamTexture, native SDK, RealSense |

---

# 3. Registration

Bootstrap / settings select implementation:

```
if (settings.Ik.Backend == FinalIK) register FinalIkDriver
else register AnimationRiggingIkDriver
```

Feature flags in settings — not `#if` soup across domain code.

---

# 4. Plugin Vision (Phase 2+)

```
Plugins/
  MyGesturePack.dll → registers IGestureRecognizer
```

V1: compile-time modules only. Design registry so runtime plugins can appear later.

---

# 5. Data Compatibility

New providers must emit the **same** `PoseFrame` / `FaceFrame` / `HandFrame` contracts.  
If a provider has richer data, extend with optional fields + schema version — do not break consumers.

---

# 6. UI Extensibility

Settings panels read a descriptors list later; V1 panels are fixed but call shared commands.

---

# 7. Forbidden Couplings

- UI → MediaPipe  
- UniVRM → MediaPipe  
- Retargeter → concrete webcam class  
- FinalIK types in Core asmdef  

---

# 8. Future Features Enabled by This Design

- Clothing preview  
- Gesture recognition  
- AI-driven idle animations  
- Multi-person  
- Integration with existing Unity tooling / digital twin stacks
