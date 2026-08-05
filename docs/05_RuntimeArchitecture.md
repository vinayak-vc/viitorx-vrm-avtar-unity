# Virtual Mirror
## Software Design Specification

Document ID: SDS-005  
Document Name: Runtime Architecture  
Version: 1.0  
Status: Active  

---

# 1. Application Lifetime

```
Process Start
    → Unity Player init
    → Bootstrap.Awake / Start
    → Register services
    → Load settings
    → Init camera (async)
    → Init MediaPipe session (async)
    → Load last avatar or default library avatar
    → Enter Tracking loop
    → OnQuit: dispose native sessions, flush logs
```

Startup must remain interactive: show splash / loading UI while camera + models init.

---

# 2. Hot-Swap Operations

All must work **without process restart**:

| Operation | Steps |
|-----------|-------|
| Change avatar | Unwire drivers → Destroy instance → Load VRM → Rebuild IK rig → Rewire → Resume |
| Change camera | Stop capture → Open new device → Restart tracking |
| Change resolution | Recreate capture + rebuild MediaPipe input size if required |
| Toggle face/hands | Enable flags on orchestrator; skip inference cost when off |
| Change smoothing | Update filter parameters live |
| Recalibrate | Enter Calibrating; write profile; apply offsets |

---

# 3. Frame Loop (conceptual)

**Capture / inference thread (or async)**

1. Grab camera frame  
2. Run Pose / Face / Hands (may stagger: e.g. hands every N frames)  
3. Write results into back buffer; flip  

**Main thread (Update / LateUpdate)**

1. Read latest committed frames  
2. Filter  
3. Retarget → IK targets  
4. Apply BlendShapes / fingers  
5. Unity Animation Rigging evaluates  
6. Render  

**Rule:** Never call MediaPipe or file IO synchronously on the main thread in steady state.

---

# 4. Timing Model

| Clock | Use |
|-------|-----|
| Capture timestamp | Latency metrics; drop stale frames |
| Unity `Time.deltaTime` | Filter dt; UI animation |
| FixedUpdate | Avoid for tracking apply (use Update/LateUpdate) |

Stale frame policy: if `now - timestamp > StaleThresholdMs`, do not apply; hold last pose.

---

# 5. Service Registry

Simple composition root (no heavy DI framework required for V1):

```
ServiceRegistry
    .Register<IBodyTrackingProvider>(mediaPipePose)
    .Register<IFaceTrackingProvider>(mediaPipeFace)
    .Register<IHandTrackingProvider>(mediaPipeHands)
    .Register<IAvatarLoader>(uniVrmLoader)
    .Register<IIkSolver>(animRigging)
    .Register<SettingsStore>(settings)
```

MonoBehaviours resolve via registry injected at bootstrap — avoid `FindObjectOfType` in hot paths.

---

# 6. Avatar Session

`AvatarSessionController` owns:

- Current `VrmAvatarInstance`  
- Active bone map  
- IK rig instance  
- Face / hand drivers  

On load:

```
Read bytes/path
→ UniVRM instantiate
→ Parent under AvatarRoot
→ Ensure Animator + Humanoid
→ Build / bind Animation Rigging
→ Bind BlendShape + Hand drivers
→ Apply calibration profile
→ Enable tracking apply
```

On unload: reverse order; dispose textures/meshes UniVRM allocated.

---

# 7. Calibration Runtime

1. User selects Calibration  
2. Prompt T-pose (or A-pose) for N seconds  
3. Capture average shoulder width, hip height, arm lengths  
4. Compute scale + offset vs avatar  
5. Save `CalibrationProfile`  
6. Retarget uses profile every frame  

Recalibration invalidates only the profile, not the avatar asset.

---

# 8. Error Recovery

| Event | Recovery |
|-------|----------|
| Domain reload (Editor) | Re-bootstrap registry |
| MediaPipe native crash | Catch, disable provider, show error, keep app alive if possible |
| Out of memory on huge VRM | Fail load; suggest smaller model |

---

# 9. Shutdown Order

1. Stop applying tracking  
2. Stop MediaPipe  
3. Stop camera  
4. Unload avatar  
5. Flush settings + logs  
6. Dispose native handles  

Never leave webcam locked after exit.
