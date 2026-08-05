# Virtual Mirror
## Software Design Specification

Document ID: SDS-011  
Document Name: IK Pipeline  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Convert retargeted targets into stable avatar poses using IK rather than noisy per-bone FK.

**Recommended V1:** Unity Animation Rigging Package  
**Optional premium:** FinalIK (RootMotion)

---

# 2. Why IK

- Preserves avatar bone lengths  
- Smoother elbows / knees with pole vectors  
- Easier to absorb landmark noise at end effectors  
- Cleaner integration with Animator  

---

# 3. Animation Rigging Layout

```
AvatarRoot
└── VRM Instance (Animator)
    └── RigBuilder
        └── Rig (weight 1)
            ├── TwoBoneIK_LeftArm   (hand target + elbow hint)
            ├── TwoBoneIK_RightArm
            ├── TwoBoneIK_LeftLeg   (optional V1+)
            ├── TwoBoneIK_RightLeg
            └── MultiAim / Chain   (spine / head as needed)
```

`AnimationRiggingIkDriver` each frame:

1. Reads `RetargetResult`  
2. Writes target Transform positions/rotations  
3. Writes hint positions  
4. Adjusts rig weights from confidence  

---

# 4. Target Sources

| Effector | Source |
|----------|--------|
| Hand | Wrist landmark (calibrated) |
| Elbow hint | Elbow landmark |
| Foot | Ankle landmark |
| Knee hint | Knee landmark |
| Head | Face pose / nose–ear plane |

Root / hips: controlled by hip mid-point + grounded policy (keep avatar on floor plane).

---

# 5. FinalIK Path (Optional)

If FinalIK licensed:

- `FinalIkDriver : IIkSolver`  
- Use VRIK or LimbIK set  
- Same `RetargetResult` input  

Settings flag: `IkBackend = AnimationRigging | FinalIK`.  
Never reference FinalIK types from Core.

---

# 6. Weighting & Soft Fail

```
rigWeight = confidenceMapped * userIkBlend
```

When tracking lost: blend rig weight → 0 over ~200–400 ms toward idle pose (optional idle breathing later).

---

# 7. Update Order

1. Animator (idle/base if any)  
2. Our scripts set targets (Update)  
3. RigBuilder evaluates  
4. Finger / BlendShape late apply if needed  

Do not set bone localRotation on IK-controlled bones after RigBuilder unless intentional override layer.

---

# 8. Feet & Grounding (V1 policy)

V1 may prioritize upper body + head if lower-body webcam angle is poor.  
Settings: `EnableLegIk` default true when full body visible; auto-disable if ankle confidence low.

---

# 9. Performance

IK + retarget budget: **< 1 ms** on recommended hardware.  
Avoid allocating new Transforms every frame — reuse target/hint transforms under `AvatarRoot/IKTargets`.

---

# 10. Implementation Steps for Agents

1. Create IK target hierarchy once per avatar load  
2. Bind TwoBoneIK references by humanoid bones  
3. Feed targets from RetargetPipeline  
4. Tune hint offsets  
5. Expose weights in Smoothing / Debug UI  
6. PlayMode test: T-pose, wave, walk-in-place
