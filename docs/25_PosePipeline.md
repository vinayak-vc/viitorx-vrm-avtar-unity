# Virtual Mirror
## Software Design Specification

Document ID: SDS-025  
Document Name: Pose Pipeline  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Define coordinate conversion, joint mapping inputs, smoothing, confidence handling, and calibration math between MediaPipe and the avatar.

Related: `09_MediaPipeSystem.md`, `10_Retargeting.md`, `11_IKPipeline.md`.

---

# 2. Stages

```
Raw landmarks (MediaPipe image / world)
    → Normalize / space convert
    → Confidence gate
    → Temporal filter (One-Euro / EMA)
    → Calibration transform (scale, offset, mirror)
    → Logical JointId positions
    → Retarget (vectors / IK targets)
```

---

# 3. Coordinate Spaces

| Space | Description |
|-------|-------------|
| Image | x,y normalized 0–1; origin top-left (MediaPipe typical) |
| MediaPipe World | Metric-ish world landmarks if enabled |
| Unity World | Left-handed; Y up; avatar rooted at AvatarRoot |
| Avatar Local | After calibration |

**Conversion rules (V1 RGB):**

1. Prefer Pose **world landmarks** when stable for 3D structure  
2. Else lift image landmarks with assumed plane depth / weak perspective  
3. Flip Y from image to Unity  
4. Apply optional mirror: `x = 1 - x` **once** before Unity convert  
5. Transform into AvatarRoot space  

Document the exact matrix in code comments at the converter — this is a common bug farm.

---

# 4. Joint Filter

### Confidence gate

```
if (confidence < minEnter) → ignore update (hold)
if (confidence < minExit)  → increase smoothing
else → normal smoothing
```

Hysteresis avoids flicker at threshold.

### One-Euro filter

Per-axis on positions (and optionally on quaternion via swing-twist).  
Parameters exposed as Smoothing UI 0..1 mapped to mincutoff / beta.

### Outlier reject

If joint jumps > maxMetersPerSecond * dt → clamp or reject.

---

# 5. Calibration

Capture while user holds T-pose (or A-pose):

| Measurement | Use |
|-------------|-----|
| Shoulder width | Scale X arms |
| Hip height | Vertical placement |
| Ankle–head | Overall scale |
| Mid-hip position | Root offset |

Store in `CalibrationProfile`. Apply:

```
pAvatar = rootOffset + scale * R * pTracked
```

Reset calibration = identity scale/offset.

---

# 6. Latency vs Smoothness

Latency budget prefers responsiveness (`00_ProjectVision.md`).  
Default smoothing moderate; “cinematic” preset higher lag OK for demos.

---

# 7. Stale Data

If `now - timestamp > StaleMs` (e.g. 100–150 ms): hold; decay IK weight.

---

# 8. Debug Visualization

Dev overlay:

- Raw vs filtered joints  
- IK targets  
- Confidence colors (green/yellow/red)  

Toggle via diagnostics — off by default in ship builds.

---

# 9. Implementation Types

| Type | Role |
|------|------|
| `PoseSpaceConverter` | MediaPipe → Unity |
| `ConfidenceGate` | Hold / pass |
| `OneEuroFilter` | Temporal |
| `JointFilterPipeline` | Orchestrates above |
| `PoseCalibrator` | Profile bake |
| `MediaPipeToHumanoidMapper` | Indices → JointId |

---

# 10. Agent Acceptance

- Unit tests for Y-flip and mirror  
- No NaNs when landmark missing  
- Visual debug proves filter lag acceptable  
- Calibration improves hand reach on different height users
