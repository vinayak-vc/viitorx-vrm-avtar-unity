# Virtual Mirror
## Software Design Specification

Document ID: SDS-017  
Document Name: Performance  
Version: 1.0  
Status: Active  

---

# 1. Targets

| Metric | Target |
|--------|--------|
| FPS | ≥ 60 sustained |
| E2E latency | ≤ 50 ms ideal |
| Memory | < 2 GB typical |
| Avatar switch | ≤ 3 s |
| IK + retarget | < 1 ms |
| No main-thread hitch | < 33 ms steady state |

### Stage budgets (modern GPU, single person)

| Stage | ms |
|-------|-----|
| Camera capture | 1–2 |
| Pose GPU | 3–6 |
| Face | 2–4 |
| Hands | 2–4 |
| IK + retarget | < 1 |
| VRM render | 1–3 |

---

# 2. Quality Modes

| Mode | Tradeoff |
|------|----------|
| Quality | Full res, all modalities every frame |
| Balanced | Default; stagger hands |
| Performance | Lower camera res; reduce face/hands rate; disable spring bones; render scale 0.75 |

---

# 3. GC / Allocation Rules

Hot path (per frame) **must not**:

- LINQ  
- string concat  
- `new` arrays / lists  
- boxing  

Allowed: reuse filters, pooled buffers, struct frames.

---

# 4. Profiling Checklist

Unity Profiler markers:

- `VM.Capture`  
- `VM.Infer.Pose`  
- `VM.Infer.Face`  
- `VM.Infer.Hands`  
- `VM.Filter`  
- `VM.Retarget`  
- `VM.IK`  
- `VM.FaceApply`  
- `VM.HandApply`  

Validate on reference hardware before milestone signoff.

---

# 5. Soak Test

2-hour run:

- FPS average / 1% low  
- Working set slope ≈ 0  
- Camera still alive  
- No error spam  

---

# 6. GPU

Prefer MediaPipe GPU delegate.  
If CPU-only: force Performance mode defaults and warn user.

---

# 7. Agent Guidance

When optimizing, measure first. Do not “optimize” by adding threads without profiler evidence. Prefer reducing resolution / cadence over clever micro-opts.
