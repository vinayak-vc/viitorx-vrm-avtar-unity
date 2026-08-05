# Virtual Mirror
## Software Design Specification

Document ID: SDS-016  
Document Name: Threading  
Version: 1.0  
Status: Active  

---

# 1. Thread Model

| Thread | Work |
|--------|------|
| Unity Main | Instantiate, Animator, IK targets, UI, apply BlendShapes |
| Capture / Inference | Camera copy (if needed), MediaPipe detect |
| IO Pool | Read VRM bytes, save settings/logs |

If the MediaPipe Unity plugin **requires** main-thread calls, document that exception and minimize cost (copy texture → native on main, process async if plugin allows).

---

# 2. Hard Rules

1. No file reads of VRM on main thread  
2. No unbounded locks held across frames  
3. No Unity API from pure background threads (`Transform`, `GameObject`, etc.)  
4. Publish frames via double buffer or `ConcurrentQueue` of immutable structs  
5. `SynchronizationContext` / `Awaitable` / UniTask — pick one async style and stick to it (ADR)

---

# 3. Frame Handoff

```
Inference:
  build PoseFrame in local memory
  Interlocked exchange into published slot

Main:
  read published slot once per Update
  copy needed fields into filter state
```

Prefer struct arrays pooled/reused to cut GC.

---

# 4. Staggered Inference

Hands / Face may run at lower rate. Orchestrator timestamps each modality independently. Retarget uses latest of each; does not block waiting for hands.

---

# 5. Shutdown

`CancellationToken` through async loads and inference loops.  
Join/cancel before disposing native MediaPipe session.

---

# 6. Editor Play Mode

Domain reload must cancel tasks; use playmode teardown hooks to release camera.

---

# 7. Diagnostics

Track:

- inference queue lag  
- main-thread apply time  
- dropped frames  

Expose in diagnostics when enabled.
