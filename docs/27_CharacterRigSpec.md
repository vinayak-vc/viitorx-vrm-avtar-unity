# 27 — Character Rig Specification (VRM 1.0)

**Status:** Active (2026-08-10)
**Audience:** character artists building avatars for Virtual Mirror, in Blender or any DCC that exports VRM 1.0.
**Purpose:** the canonical rig contract. An avatar built to this spec tracks in the app on first load with **zero per-avatar tuning**.

This spec is **reverse-engineered from the pipeline**, not invented:

| The app reads… | …so the model must provide |
|----------------|-----------------------------|
| Unity Humanoid bones (`HumanBodyBones`) bound by the Kalidokit control-rig driver + IK driver | The full humanoid bone set below, in a clean T-pose |
| VRM 1.0 expression presets written by `VrmExpressionRetargeter` | The required blendshape presets below |
| VRM 1.0 spring bones | Optional secondary-motion chains |

Everything else an artist adds (mesh, textures, hair, materials) is free — the retarget never looks at it. See ADR-017 (rotational retarget), ADR-022/023 (Kalidokit control rig), ADR-013/021 (wrist).

---

## 1. Hard rules (the model is rejected or mistracks without these)

1. **Format: VRM 1.0.** Not VRM 0.x. The loader uses `Vrm10.LoadBytesAsync`; a 0.x file will not load correctly. (Blender: VRM Add-on → spec version 1.0.)
2. **Rest pose = T-pose.** Arms straight out horizontally, palms down, legs straight, feet flat, fingers straight. The T-pose is the retarget's neutral; an A-pose rest shifts every arm by the A-pose angle.
3. **Facing.** In Blender the character faces **−Y** (front view); the VRM addon exports this as VRM's **+Z-forward, +Y-up**. Authoring facing the wrong way makes the avatar track backwards. (Left/right is absorbed by the app's live mirror toggle, but facing is not.)
4. **Uniform scale, transforms applied.** No non-uniform or unapplied object/bone scale. Single armature root.
5. **Humanoid chain intact.** Do not splice extra bones *between* two humanoid bones (e.g. a bone between `LowerArm` and `Hand`). Extra **child/deform** bones (twist bones, helpers) are fine — the humanoid retarget ignores them.
6. **Required bones + required expressions present** (§2, §3).

---

## 2. Skeleton

Units: meters, roughly human proportions (~1.6–1.8 m). The rotational retarget is proportion-agnostic, but position/reach and finger tracking behave best near human ratios; extreme chibi proportions have caused reach problems historically.

### 2.1 Required — core + limbs (17)
Retarget/IK bind these directly; absence breaks tracking.

```
Hips  Spine  Chest  Neck  Head
LeftUpperArm  LeftLowerArm  LeftHand
RightUpperArm RightLowerArm RightHand
LeftUpperLeg  LeftLowerLeg  LeftFoot
RightUpperLeg RightLowerLeg RightFoot
```

### 2.2 Required — fingers (30)
OAK-D 21-point hands and the Kalidokit finger-curl driver need all 30. **VRM 1.0 thumb naming differs** — thumb has *Metacarpal / Proximal / Distal* (no Intermediate); the other four fingers have *Proximal / Intermediate / Distal*.

```
per hand (×2):
  Thumb:  Metacarpal, Proximal, Distal
  Index:  Proximal, Intermediate, Distal
  Middle: Proximal, Intermediate, Distal
  Ring:   Proximal, Intermediate, Distal
  Little: Proximal, Intermediate, Distal
```

> UniVRM maps VRM `thumbMetacarpal → thumbProximal → thumbDistal` onto Unity `HumanBodyBones.LeftThumbProximal/Intermediate/Distal`. You author VRM names; Unity sees its own. Nothing to do — just don't skip the metacarpal.

### 2.3 Strongly recommended — optional (7)
The pipeline uses these when present and degrades without them.

```
UpperChest        smoother spine bend / better torso
LeftShoulder RightShoulder   clavicle motion, shoulder line
LeftToes RightToes           foot roll
LeftEye RightEye             VRM lookAt (future eye tracking)
```

### 2.4 Symmetry note (important)
Author left/right mirrored as usual. But be aware: at runtime UniVRM's **normalized** left/right finger bones are **parallel, not mirrored**. The driver already flips per-hand (`sideSign`, left −1 / right +1 — ADR-023 fix). Keep standard mirrored authoring; the app handles the flip.

---

## 3. Expressions (blendshapes)

`VrmExpressionRetargeter` maps MediaPipe face output onto these **VRM 1.0 preset expressions**. Bind each to a mesh shape key (morph target).

### 3.1 Required (face tracking dead without them)
| VRM preset | Driven by |
|------------|-----------|
| `blinkLeft`  | left eye blink |
| `blinkRight` | right eye blink |
| `aa`         | jaw open / mouth open |
| `happy`      | smile |
| `angry`      | brow-down |
| `surprised`  | brow-up / eye-wide |

### 3.2 Recommended (future lip-sync; not required today)
`ih`, `ou`, `ee`, `oh` — the remaining A-I-U-E-O visemes.

---

## 4. Spring bones (optional)

Hair, skirt, tails, accessories → VRM 1.0 spring-bone joint chains (+ colliders on the body to stop clipping). Purely cosmetic secondary motion; the tracking pipeline ignores them. Set up in the VRM addon's Spring Bone panel. Leave empty if the model is rigid.

---

## 5. Blender workflow

### 5.1 Fastest — start from the generated template
The repo ships a headless generator that produces a contract-complete rig:

```bash
"C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" --background --python tools/blender/build_rig.py
```

Outputs to `tools/blender/output/`:
- `VirtualMirrorRigTemplate.blend` — the editable armature + placeholder mesh + expression shape-key slots + humanoid mapping already assigned.
- `VirtualMirrorRigTemplate.vrm` — an export-ready, validated VRM 1.0.

Open the `.blend`, **replace the placeholder block mesh with your character mesh**, skin it to the existing bones, sculpt the expression shape keys (the named slots already exist), add spring bones for hair/cloth, then export VRM (below). The skeleton, humanoid mapping and expression list are already correct.

### 5.2 From scratch (existing character)
1. Install the **VRM Add-on for Blender** (saturday06, MIT).
2. Pose the armature to the exact bone set in §2, rest = T-pose (§1.2), facing −Y (§1.3).
3. Skin the mesh (weights to humanoid bones; extra deform bones OK).
4. VRM panel → set **spec version 1.0**, fill **Meta** (name, author, license).
5. VRM panel → **Humanoid** → assign every required bone (§2.1–2.2) + optionals (§2.3). "Auto Bone Assignment" works if your names are conventional; verify each slot.
6. VRM panel → **Expressions** → create the presets in §3 and bind each to a shape key.
7. VRM panel → **Spring Bones** → optional hair/cloth chains + colliders.

### 5.3 Export
VRM panel → **Export VRM** (VRM 1.0). Or headless: `bpy.ops.export_scene.vrm(filepath=...)`.

---

## 6. Validate before shipping the avatar

Two independent checkers, both reading the actual VRM (`VRMC_vrm`) data UniVRM will load:

**In Unity** — `Editor/VrmRigValidator.cs`:
- Menu **Virtual Mirror → Validate VRM Rig (Pick File)…**, or
- select a `.vrm` in the Project window → **Virtual Mirror → Validate Selected VRM**.

Reports PASS/FAIL with the exact missing bones/expressions. Run it on every new avatar before it enters `StreamingAssets/Avatars/`.

**Contract checked:** VRM 1.0 spec version · 17 core/limb bones · 30 finger bones · 6 required expressions bound · optionals + spring bones reported as info.

---

## 7. Pipeline limits (set artist expectations)

- The app reads **nothing beyond** VRM humanoid bones + expressions + spring bones. Twist/deform bones, extra materials, custom shaders — cosmetic only, invisible to tracking.
- **Head/neck** currently rest forward (undriven on the FK path); face-pose head drive is future work (ADR-012).
- **Fingers/wrist** track only with a close hand source (OAK-D 21-pt hands or a close webcam) — small distant hands give no data (ADR-021/023).
- **Body turn (yaw past ~side)** is frontal-locked on a single camera (ADR-019) — not a rig issue.

---

## 8. Reference: what the generated template contains (verified 2026-08-10)

54 humanoid bones (6 core + 5 left arm/eye + 15 left fingers + 4 left leg, mirrored right), VRM spec 1.0, and 13 expression presets bound (6 required + 4 visemes + sad/relaxed/blink). Regenerate any time with §5.1.
