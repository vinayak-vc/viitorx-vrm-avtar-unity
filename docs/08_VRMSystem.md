# Virtual Mirror
## Software Design Specification

Document ID: SDS-008  
Document Name: VRM System  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Runtime-load and drive VRM 1.0 avatars as the visual body of the virtual mirror.

---

# 2. Avatar Sources (Product)

```
Virtual Mirror
├── Load VRM          → user picks any .vrm
├── Avatar Library    → built-in StreamingAssets avatars
└── Download More     → Phase 2 online gallery
```

### How users create avatars

| Option | Pipeline | V1 support |
|--------|----------|------------|
| 1. User-made (recommended) | VRoid Studio → Export VRM 1.0 → Load in app | Yes |
| 2. Studio-made presets | Blender → FBX → Unity+UniVRM → Export VRM → bundle | Yes (ship 5–10) |
| 3. AI generation | Selfie → RPM / Avatar SDK / Meshy → VRM | Phase 2 |

**Important:** `.vroid` is editable in VRoid; `.vrm` is the runtime export. Users do not edit `.vrm` inside VRoid.

---

# 3. Runtime Load Sequence

```
Select File / Library item
    ↓
Read .vrm (async)
    ↓
UniVRM instantiate (async)
    ↓
Parent under AvatarRoot
    ↓
Validate Humanoid Avatar
    ↓
Attach / rebuild Animator + IK rig
    ↓
Bind BlendShape + Hand drivers
    ↓
Reconnect body tracking
    ↓
Done (no app restart)
```

Switching avatars:

```
Current Avatar → Load → Destroy Old → Instantiate New → Retarget Skeleton → Done
```

Target: ≤ 3 seconds for typical models (`FR-AV-02`, `NFR-03`).

---

# 4. UniVRM Responsibilities

| Concern | Implementation |
|---------|----------------|
| Parse VRM 1.0 | UniVRM runtime API |
| Materials / textures | As imported |
| Expressions | VRM Expression / BlendShape proxy |
| Spring Bone | Enable by default; settings toggle |
| First-person / meta | Read-only for V1 (licensing display later) |
| Humanoid | Required for retarget |

Loader abstraction: `IAvatarLoader` → `UniVrmAvatarLoader`.

---

# 5. Face → VRM BlendShapes

```
MediaPipe Face Mesh
    → Eye / Mouth / Eyebrows / Head rotation
    → VRM BlendShapes / Expressions
```

Minimum expression set:

| Feature | VRM target (typical) |
|---------|----------------------|
| Blink L/R | `blinkLeft` / `blinkRight` or combined blink |
| Smile | `happy` / mouth smile shapes |
| Angry | `angry` |
| Surprised | `surprised` |
| Mouth open | `aa` / jaw open |
| Eye direction | lookUp/Down/Left/Right or bone aim |

Missing shapes on a model: skip silently; log once at Warning.

---

# 6. Hands → VRM

MediaPipe Hands provides 21 joints; drive finger bones for curl and spread.  
Wrist position/rotation feeds arm IK target.

If a VRM lacks finger bones, disable hand driver for that instance and notify diagnostics.

---

# 7. Body

Body motion is **not** applied as mocap clip playback.  
Flow: filtered joints → retarget → IK → humanoid bones.

---

# 8. Storage

```
Documents/MyMirror/Avatars/
    Alice.vrm
    Bob.vrm
    MyAvatar.vrm
```

Built-ins: `StreamingAssets/Avatars/*.vrm`  
Last selection saved in settings.

---

# 9. Validation Checklist (on load)

- [ ] File readable  
- [ ] VRM 1.0 (or supported)  
- [ ] Humanoid mapped  
- [ ] Has hips / spine / head / upper arms / lower arms / hands  
- [ ] Within max texture / mesh budget (configurable)  

Fail → keep previous avatar + error toast.

---

# 10. Phase Progression

**Phase 1:** 5–10 built-ins + disk load + remember last  
**Phase 2:** Launch VRoid / AI service links; marketplace; in-app clothing customization  

---

# 11. Agent Implementation Notes

- Destroy previous instance completely (meshes, materials, spring bones) to avoid leaks  
- Do not load VRM on the main thread for large files — await then Instantiate on main  
- Rebind IK **after** humanoid is ready  
- Never assume expression names; use a configurable remap asset per avatar or VRM standard names
