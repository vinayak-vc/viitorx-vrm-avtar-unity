# Virtual Mirror
## Software Design Specification

Document ID: SDS-010  
Document Name: Retargeting  
Version: 1.0  
Status: Active  

---

# 1. Problem

MediaPipe joints are **not** Unity Humanoid bones.  
Directly copying landmark positions onto bones produces broken avatars.

You must:

1. Map landmark indices → humanoid bones  
2. Compute **rotations from vectors between joints**  
3. Prefer **IK targets** over full FK for limbs  

---

# 2. Mapping Examples

| MediaPipe | Unity Humanoid |
|-----------|----------------|
| Left shoulder | LeftUpperArm (parent aim) |
| Left elbow | LeftLowerArm |
| Left wrist | LeftHand |
| Right shoulder | RightUpperArm |
| Right elbow | RightLowerArm |
| Right wrist | RightHand |
| Left hip | LeftUpperLeg |
| Left knee | LeftLowerLeg |
| Left ankle | LeftFoot |
| Hips center | Hips |
| Spine approx | Spine / Chest |
| Nose / ears | Head hint |

Maintain the map in `HumanoidBoneMap` / ScriptableObject — data-driven, not hardcoded magic numbers in solvers.

---

# 3. Rotation From Vectors

For a simple two-bone limb:

```
upperDir = normalize(elbow - shoulder)
lowerDir = normalize(wrist - elbow)
```

Build rotation that aims the bone’s bind axis along `upperDir` / `lowerDir`, preserving twist via:

- plane from shoulder–elbow–wrist, or  
- IK pole vector from MediaPipe mid-joint  

Pseudo-policy:

1. Compute aim rotation from bind pose forward to target direction  
2. Apply twist correction from calibration / pole  
3. Convert to local space of parent bone  

Implementation type: `RotationFromVectors`.

---

# 4. Pipeline Stages

```
Filtered PoseFrame
    → Landmark → logical JointId
    → Apply calibration scale/offset
    → Build world-space IK targets (hands, feet, head)
    → Build pole vectors
    → Optional FK fallback for spine
    → Output RetargetResult
```

`RetargetResult` feeds `IIkSolver` and optional direct spine/head helpers.

---

# 5. Why IK Instead of Pure FK

Pure FK from noisy landmarks jitters and breaks bone lengths.  
IK with fixed avatar bone lengths keeps the character on-model and smoother.

See `11_IKPipeline.md`.

---

# 6. Mirror Mode

If the product shows a **mirror**, either:

- Mirror landmark X before retarget, or  
- Mirror root scale X on avatar  

Pick one in settings and document in calibration; do not double-mirror.

---

# 7. Scale & Proportions

User limb lengths ≠ avatar limb lengths.  
Calibration profile stores:

- shoulder width ratio  
- hip height  
- arm span ratio  
- vertical offset  

Retarget applies these so hands reach plausible positions on the avatar.

---

# 8. Confidence-Aware Retarget

| Confidence | Behavior |
|------------|----------|
| High | Full update |
| Medium | Heavier smoothing |
| Low | Hold last rotation / IK target |
| Invalid | Skip limb; keep previous |

Never slam to T-pose on a single bad frame.

---

# 9. Face & Hands Outside Body Retarget

- Face expressions → BlendShape driver (not Humanoid muscle API)  
- Finger curls → dedicated hand retarget (`VrmHandDriver`)  
- Wrist → body IK hand target  

---

# 10. Agent Checklist

- [ ] Map table covered for all used joints  
- [ ] No MediaPipe enum leaked into Avatar assembly  
- [ ] Unit tests for vector→rotation with known right angles  
- [ ] Mirror mode single-path verified  
- [ ] Calibration applied before IK targets published
