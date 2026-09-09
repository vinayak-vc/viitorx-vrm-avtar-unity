# Arm Retarget V3 — rendered-avatar mesh forensics — 2026-09-09

**Question asked:** *why does the avatar LOOK crooked when the green skeleton looks correct?*

**Answer: because the arms are aimed correctly in the world while the torso they hang from is not aimed
at all.** Torso yaw is multiplied by `torsoYawScale`, which is **0** in `Bootstrap.unity`, so the
avatar's shoulder line yaws by exactly **−0.00°** no matter what the human does. Over five real video
frames the human's shoulder line yawed up to **50.84°** and the hip line up to **42.22°**; the avatar
answered with **0.0000°**. The arm bones then reach source direction to **0.0000°** *in world space* —
and are wrong by up to **51.10°** *relative to the body*, which is the only frame a viewer can see them
in. At the worst frame both hands are pulled to **0.15 m** from the spine axis where they belong at
**0.26–0.44 m**, i.e. inside the torso, and the render shows both arms swallowed by the hoodie.

**The layer responsible: the retarget's TORSO stage.** Not tracking, not the arm solver, not the
control rig, not skinning, not the camera. It is a bone-transform failure — of the *torso* chain. Both
prior reports measured arm bones only, which is exactly the quantity that is already exact.

Mesh deformation is real but secondary (visible silhouette 10–20° off its bone, and a 7–9 cm elbow
shift on bend); avatar proportions are real and third (arms are **79.1 %** of proportional length,
per-segment scale spread **1.457×–3.356×**). Neither produces the reported symptom.

Nothing was changed. Per TASK 7 this is forensics only; the single recommendation is in §8.

---

## 0. Method, and how the editor was reached

The `UnityMCP` and `coplay-mcp` MCP servers both reported `CONNECTION_CLOSED` for this session, so the
normal tooling was unavailable. The Unity-side bridge host was still listening on `127.0.0.1:6401`
(PID = `Unity.exe`), so its wire protocol was spoken directly: ASCII handshake line
`WELCOME UNITY-MCP 1 FRAMING=1`, then 8-byte big-endian length-prefixed UTF-8 frames carrying
`{"type": <tool>, "params": {…}}` — read out of
`Library/PackageCache/com.coplaydev.unity-mcp@*/Editor/Services/Transport/`. Every measurement below
ran inside the live editor through that path. **No production C# or Python was modified.**

Harness (all new, diagnostic only):

| file | role |
|---|---|
| `ubridge.py` | direct client for the Unity editor bridge |
| `measure.cs.txt` | one-call snapshot of source + control + skinned bone + skinned-vertex mesh + torso + screen |
| `shot.cs.txt` | deterministic offscreen render of the production camera |
| `run_poses.py` | drives the six deterministic poses over the real UDP wire |
| `wire_record.py` | records the production sender's wire output verbatim; replays one frame as a static hold |
| `run_frames.py` | holds one recorded video frame and snapshots every layer |
| `analyze.py` | screen-space error + overlays |

Inputs: `pose_stage_udp.py`'s six exact poses on the real UDP wire, and
`Assets/Games/video/annotated_p0.mp4` (720×1280, 30 fps, 435 frames) through the unmodified
`video_udp_sender.py` → RTMW3D.

Live configuration read back out of the running driver by reflection, not assumed:

```
torsoYawScale = 0.0    spineBendScale = 1.0    useAimArms = true    lerpAmount = 0.5    mirrorX = false
```
and from `Bootstrap.unity`: `useOakUdpTracking 1`, `oakUdpPort 8899`, `kalidokitTorsoYawScale 0`,
`kalidokitBodyTorsoRoll 0`, `wristRotationWeight 0`, `trackLegs 1`, `showDebugSkeleton 1`,
`debugSkeletonOffset (1.4, 1.0, 0)`, `debugSkeletonScale 1.5`.

### 0a. Two things about the source path, stated once

* The video path is **monocular**: no OAK-D depth, no intrinsics. `video_udp_sender.py` builds an
  estimated metric signal (pixels × a frozen torso normalisation; z from the model's own `zrel`).
  Anything this report attributes to the SOURCE stage is a property of *(RTMW3D + that estimator)*,
  **not** of the OAK-D depth path. Same caveat the V2 report made.
* Frame identity is exact rather than inferred. The sender emits one packet per frame once calibrated
  (`435 frames, 376 sent` ⇒ first sent frame = 435 − 376 + 1 = 60, contiguous thereafter), so
  `video frame = 60 + (seq − seq₀)` and the burned-in annotation index is `f = frame − 1`. 370 of 376
  packets were captured (6 UDP drops); joining on `seq` makes the mapping immune to those drops.

---

## 1. TASK 1 — deterministic reproduction, all layers on one frame

Each of the six poses was streamed on the real UDP wire (`OakDUdpPoseProvider` confirmed as the live
provider), held static for 5 s so the P0 gate validated and the `lerpAmount = 0.5` per-frame slerp
fully converged, then captured. Because the pose is *held*, the measurement and the render are not a
moving target.

Captured per pose: `pose_<name>.png` (production camera), `pose_<name>_overlay.png` (all four chains
projected), and one line of `mesh_trace.jsonl` carrying source landmarks, control bones, skinned bones,
skinned-vertex mesh joints, the green debug skeleton's own joints, torso axes and every screen
projection.

**A zeroth result worth stating: with no tracking at all the avatar renders correctly** — a clean,
symmetric T-pose (`00_rest_no_tracking.png`). The defect is pose-dependent, not a rest-pose or import
fault.

### 1a. The green skeleton is not a like-for-like comparison object

`AppBootstrap.RenderDebugSkeleton` creates the skeleton at `avatarRootInitialPosition + (1.4, 1.0, 0)`
with `localScale = 1.5`, and `PoseDebugSkeleton` places joints at `landmark.Position * scale`. So the
green figure is drawn **beside** the avatar, at **1.5× raw landmark metres**, while the avatar is
**2.5006×** human scale (§5). It is also the *unmirrored* source, whereas the avatar is deliberately
mirrored. A human comparing them is comparing a small stick figure against a large mirrored chibi. This
is a real reason the two "look" different, and it is independent of every defect below.

---

## 2. TASK 2 — visible joint positions, from the skinned vertices

Visible joints are derived from the **rendered geometry**, not from Transforms, by evaluating linear
blend skinning directly:

```
worldVert = Σᵢ wᵢ · (bones[i].localToWorldMatrix · bindposes[i]) · meshVert
```

**Verified against Unity's own `SkinnedMeshRenderer.BakeMesh`: max delta 1e-06 m over 400 vertices.**
The mesh measurement is therefore trustworthy rather than assumed.

Joint definitions (weight bands, so each is where the mesh actually creases):

| visible joint | vertices used | count (left arm) |
|---|---|---|
| shoulder | `w[shoulder.L] > 0.05` **and** `w[upper_arm.L] > 0.05` | 423 |
| elbow | `w[upper_arm.L] > 0.05` **and** `w[forearm.L] > 0.05` | 123 |
| wrist | `w[forearm.L] > 0.05` **and** `w[hand.L] > 0.05` | 121 |
| upper-arm shell | dominant bone = `upper_arm.L` | 229 |
| forearm shell | dominant bone = `forearm.L` | 489 |

### 2a. A metric I had to correct

The first visible-axis metric was the dominant eigenvector of the whole limb shell. It reported the
upper-arm sleeve as **41.7°** off its bone. That is an artifact: a chibi sleeve is short and thick
enough that the cloud's dominant extent can be its *girth*, not its length. Replaced with a
**slice centerline** — slice the shell along the bone axis, drop the girth-dominated end caps
(`t ∉ [0.15, 0.85]`), take each slice's centroid, fit through the centroids. That gives **12.146°**,
and it is the number used everywhere below. The 41.7° figure is discarded.

### 2b. A–F, per the brief

At rest, and identically on both sides (**12.146° left, 12.146° right** — a symmetry that proves it is
an authoring property of the rig, not an error):

| quantity | left | right |
|---|---|---|
| A source shoulder→elbow direction | see §5/§6 tables | — |
| B skinned bone shoulder→elbow | **matches A to 0.0000°** in every pose and frame | same |
| C source elbow relative to shoulder | source lengths 0.28 / 0.26 m | same |
| D skinned elbow relative to shoulder | avatar lengths **0.5449 / 0.5236 m** | same |
| E source elbow angle | see §6 | — |
| F skinned elbow angle | **equals E to 0.000°** in every pose and frame | same |

Visible-mesh silhouette around the elbow and forearm, versus the bone:

| measure | rest | straight poses | `elbow90` (L) | `behind` (L) |
|---|---|---|---|---|
| centerline vs bone, upper arm | 12.146° | 2.797–13.804° | 13.657° | 5.644° |
| centerline vs bone, forearm | 7.933° | 9.538–9.550° | 4.389° | 1.939° |
| **visible elbow angle** | 6.806° | **10.263–21.504°** (bone: 0.000°) | **99.326°** (bone: 90.000°) | **70.162°** (bone: 65.796°) |
| visible elbow offset from the elbow bone, in the upper-arm frame | (−0.003, −0.016, **+0.086**) | constant ≈ (−0.005, −0.018, **+0.083**) | (**+0.089**, −0.017, +0.091) | (**+0.065**, −0.020, +0.103) |
| ‖offset‖ | 0.0849 m | 0.0849–0.0859 m | **0.1285 m** | **0.1229 m** |

Two readings, kept apart:

* **A fixed offset.** The visible elbow sits ~8.5 cm behind the elbow bone in every straight pose, to
  within 1 mm, on both sides. That is the sleeve's mass sitting behind the bone. It is a constant, so
  it cannot make a pose look wrong.
* **A pose-dependent part.** Bending the elbow moves the visible elbow **+9.4 cm** (`elbow90`) or
  **+7.0 cm** (`behind`) *along the upper arm* relative to the bone, and the visible limb reads **9.3°
  more bent** than the bones are. On a bone-straight arm the visible limb reads **10–21° bent**. This
  is genuine deformation, it is the largest mesh effect found, and §3 shows it is ordinary skinning
  rather than a rigging fault.

---

## 3. TASK 3 — is this skinning / deformation?

**Which renderers actually draw the arm.** The avatar (`Anime Boy`, VRM 1.0, `AvatarRoot/VRM1`) has 15
`SkinnedMeshRenderer`s, all sharing `bones = 89` and `rootBone = spine`. **There is no arm, body or
torso mesh.** The visible arm is a *garment*:

| renderer | verts | vertices dominated by `shoulder.L` / `upper_arm.L` / `forearm.L` / `hand.L` |
|---|---|---|
| **Plush** (the hoodie) | 2916 | **280 / 227 / 389 / 0** |
| **Hands** | 3196 | 0 / 0 / **100 / 28** |
| Head | 7240 | 212 / 2 / 0 / 0 (collar) |
| zipper Base | 246 | 18 / 0 / 0 / 0 |

So the entire visible upper arm and forearm are hoodie sleeve, which is why the mesh has more give than
the bone — and why the fixed 8.5 cm rear offset exists at all.

**Bindposes — is the rendered rest the bind pose?** If the rig is at bind, every per-bone skinning
matrix `Sᵢ = bones[i].localToWorldMatrix · bindposes[i]` is the same matrix; any spread *is* deformation
away from bind. Measured on `Plush`, `Hands` and `Head`:

| bone | Δposition | Δrotation | bindpose scale |
|---|---|---|---|
| `shoulder.L` / `.R` | **0.000000 m** | **0.0000°** | (1, 1, 1) |
| `upper_arm.L` / `.R` | **0.000000 m** | **0.0000°** | (1, 1, 1) |
| `forearm.L` / `.R` | **0.000000 m** | **0.0000°** | (1, 1, 1) |
| `hand.L` / `.R` | **0.000000 m** | **0.0000°** | (1, 1, 1) |
| `spine.002`, `spine.003` | **0.000000 m** | **0.0000°** | (1, 1, 1) |
| worst over all 89 bones | 1.065785 m | 42.4213° | — (`Shorts Laces R.002`) |

The only deviations anywhere are dangling accessory/spring bones (`Shorts Laces …`) — cloth, not arm.
**Every arm bone sits exactly at its bind pose.** Bindpose mismatch is ruled out.

**Weights at the elbow — any unexpected bone?**

| bone | Σw over the 123 band verts | max w | mean w |
|---|---|---|---|
| `forearm.L` | 64.594 | 0.948 | **0.525** |
| `upper_arm.L` | 58.055 | 0.950 | **0.472** |
| `shoulder.L` | 0.234 | 0.072 | 0.002 |
| `spine.002` | 0.071 | 0.045 | 0.001 |

Textbook two-bone elbow blend summing to 0.997, at most **4 simultaneous influences**, and bleed from
`shoulder.L`/`spine.002` of 0.002/0.001. **No unexpected bone influences the elbow.**

**Scaling.** Every bone in the arm chain and the whole torso chain has `localScale = (1,1,1)` and
`lossyScale = (1,1,1)`; `AvatarRoot` and `VRM1` are both unit scale; every bindpose scale is
(1, 1, 1). **No scaling occurs anywhere in the deformation path.**

**Parent contamination.** The arm's parents are `spine.003` (UpperChest, **rotated 0.0000°**),
`spine.002` (Chest, ≤5.46° of pure pitch) and `shoulder.L` (**0.0000°**) — see §6. No parent
rotation or scale can be contaminating the deformation, because five of those bones are never rotated
at all.

**Answer to "are the bones correct but the mesh deforms incorrectly?"** The bones are correct
(§5, §6: 0.0000°). The mesh deforms *differently from the bone* by 10–20° of visible silhouette and up
to 9.4 cm of visible elbow shift — but the rig is clean by every structural test above, so that is
**ordinary linear-blend skinning of a thick garment sleeve, not a rigging defect**, and §7 shows it is
not what produces the reported symptom.

---

## 4. TASK 4 — screen-space silhouette error

Everything is projected through the **production camera** — `(0.0004, 2.2513, −8.3983)`, identity
rotation, vertical FOV 60°, 1080×1920 — inside Unity via `Camera.WorldToScreenPoint`. The analysis
script's own projection was cross-checked against Unity's and agrees to **0.003 px**, so the numbers
and the drawn overlays cannot drift apart.

Four chains, drawn in `*_overlay.png`:

* **WHITE** — the skinned humanoid bones (what the rig does)
* **RED** — the visible mesh joints from §2 (what is drawn)
* **CYAN** — *expected*: the source direction anchored at the avatar's own shoulder, walked at the
  **avatar's own** bone lengths. This is precisely what a perfect direction-only FK retarget must
  produce, so any WHITE↔CYAN gap is a retarget direction error and nothing else.
* **MAGENTA** — *proportional*: the same source direction walked at the **source's** bone lengths
  scaled by one global body scale. So any CYAN↔MAGENTA gap is pure avatar proportion and nothing else.

| pose | arm | bone vs expected, upper | fore | elbow px | wrist px | mesh vs bone, upper | fore |
|---|---|---|---|---|---|---|---|
| relaxed | left | **+0.000°** | **+0.000°** | **0.00** | **0.00** | +6.94° | +0.15° |
| relaxed | right | **+0.000°** | **+0.000°** | **0.00** | **0.00** | −6.53° | +0.69° |
| tpose | left | **+0.000°** | **+0.000°** | **0.00** | **0.00** | −6.16° | +0.15° |
| tpose | right | **+0.000°** | **+0.000°** | **0.00** | **0.00** | +5.83° | +0.69° |
| onehoriz | left | **+0.000°** | **+0.000°** | **0.00** | **0.00** | −6.16° | +0.15° |
| onehoriz | right | **+0.000°** | **+0.000°** | **0.00** | **0.00** | −6.53° | +0.69° |
| elbow90 | left | **+0.000°** | **−0.000°** | **0.00** | **0.00** | −4.81° | **−20.50°** |
| elbow90 | right | **+0.000°** | **+0.000°** | **0.00** | **0.00** | −6.53° | +0.69° |
| overhead | left | **−0.000°** | **+0.000°** | **0.00** | **0.00** | **−12.13°** | −0.90° |
| overhead | right | **−0.000°** | **−0.000°** | **0.00** | **0.00** | **+12.80°** | −0.20° |
| behind | left | **+0.000°** | **−0.000°** | **0.00** | **0.00** | −1.55° | **+12.74°** |
| behind | right | **+0.000°** | **+0.000°** | **0.00** | **0.00** | −6.53° | +0.69° |

Same measure on the five real video frames: bone-vs-expected stays within **0.0112° / 0.0129 px** on
all ten arms, while mesh-vs-bone reaches **+17.27°** (upper) and **+14.41°** (forearm). The worst
bone-vs-expected figure anywhere is **0.0051° / 0.0033 px** across the deterministic poses and
**0.0112° / 0.0129 px** across the video frames — i.e. at the numerical floor, not literally zero.

**`screen error` between the expected projected skeleton segment and the avatar's bone segment is
≤0.013 px in every pose and every frame measured** — sub-hundredth-of-a-pixel on a 1080×1920 render.
At the render level the retarget contributes nothing.

Visible limb length on screen tells a different story: the avatar's upper arm projects **107.7 px**
where human proportions demand **138.4 px**.

**One blind spot, stated.** In `elbow90` the forearm points almost straight at the camera, so it
projects to ~15 px and its 90° bend is invisible to this camera. That pose cannot support a screen-space
forearm conclusion; `behind` and the video frames carry that instead.

---

## 5. TASK 5 — separating the four causes

### 5a. SOURCE — ruled out as the cause of *this* symptom

On `annotated_p0.mp4` the burned-in P0 annotation tracks the human's real limbs correctly in every
frame examined (`src_f83/236/332/338/418.jpg`) — the green skeleton *is* right, which is the premise of
the complaint and is confirmed rather than assumed. Source torso motion is large and smooth:
shoulder-vs-hip twist to **52.1°** (f339), body turn to **50.8°** (f419), lean to **19.1°** (f333).

### 5b. BONE (arms) — ruled out, twice, independently

Source → **skinned** humanoid bone (via `Vrm10Instance.Humanoid`, not the Animator):

| input | arms | src→bone upper | src→bone fore | src elbow | bone elbow |
|---|---|---|---|---|---|
| 6 deterministic poses | 12/12 | **0.0000°** | **0.0000°** | 0.000–90.000° | identical |
| video f419 | 2/2 | **0.0000°** | **0.0000°** | 142.934 / 89.657° | **142.934 / 89.657°** |
| video f339 | 2/2 | **0.0000°** | **0.0000°** | 105.611 / 114.882° | **105.611 / 114.882°** |
| video f333 | 2/2 | **0.0000°** | **0.0000°** | 113.103 / 92.868° | **113.103 / 92.868°** |
| video f237 | 2/2 | **0.0000°** | **0.0000°** | 37.058 / 37.853° | **37.058 / 37.853°** |
| video f084 | 2/2 | **0.0000°** | **0.0000°** | 42.379 / 88.505° | **42.379 / 88.505°** |

Across all **22** arm measurements (12 deterministic + 10 video) the worst source→skinned-bone
direction error is **0.0000°**, and every elbow angle reproduces to **±0.0000°** — including real noisy
video with elbows bent to 142.9°. (An earlier capture of f333, since superseded by the final run, read
0.0198° on one upper arm; that is the largest value observed at any point.) The arm retarget is exact.

### 5c. MESH — present, measurable, not the cause

§2b/§3: 10–20° of visible silhouette deviation and up to 9.4 cm of visible elbow shift, on a rig whose
bindposes, weights, influence counts and scales are all clean. It is real, it is ordinary garment
skinning, and it is an order of magnitude too small to explain a limb ending up inside the torso.

### 5d. PROPORTION — present, third-largest, structural

One global body scale from a measure both figures have (glenohumeral span):
**0.9002 m / 0.3600 m = 2.5006**. Per-segment scale against the same source segment — a proportionally
matched rig would give **one** number in this column:

| segment | avatar (m) | source (m) | scale |
|---|---|---|---|
| neck + head (midShoulder→head) | 0.4027 | 0.12 | **3.3562** |
| hip span | 0.5457 | 0.20 | 2.7287 |
| shoulder span | 0.9002 | 0.36 | 2.5006 |
| torso (hip→midShoulder) | 1.2183 | 0.50 | 2.4366 |
| clavicle | 0.3751 | 0.18 | 2.0839 |
| forearm | 0.5236 | 0.26 | 2.0138 |
| upper arm | 0.5449 | 0.28 | 1.9462 |
| thigh | 0.7023 | 0.45 | 1.5607 |
| shin | 0.6557 | 0.45 | 1.4571 |

**Spread 1.4571× → 3.3562×, a factor of 2.30.** The head is 2.3× larger relative to the shins than a
human's; from the head bone to the top of the rendered silhouette is **29.7 %** of total rendered
height (bounds include the hair spikes). This is a chibi rig, quantified.

Consequence under direction-only FK, from the deterministic poses (exact source lengths):

| quantity | value |
|---|---|
| arm length, avatar ÷ proportional | **0.7913** |
| elbow displacement from proportion alone | **0.1552 m** = **30.7 px** |
| wrist displacement from proportion alone | **0.2818 m** = **55.7 px** |

Visible directly in `pose_tpose_overlay.png`: the magenta proportional wrist lands **past the end of the
avatar's hand**. (On video frames this ratio wanders 0.68–1.00 because the *source's* own limb lengths
are a monocular estimate; 0.7913 from the exact fixtures is the figure to quote.)

### 5e. The cause the brief's four categories do not name

The arms are exact in world space and the mesh and proportions are too small. What remains is the frame
the arms are expressed in. Measured the same way the arms were — source axis versus rendered bone axis,
mirrored in X exactly as the arm path mirrors:

| video frame | source shoulder yaw | **avatar shoulder yaw** | yaw error | source hip yaw | **avatar hip yaw** | hip error | shoulder-axis error |
|---|---|---|---|---|---|---|---|
| f419 | −50.84° | **−0.00°** | **−50.84°** | −39.06° | **−0.00°** | **−39.06°** | **51.148°** |
| f237 | −26.86° | **−0.00°** | **−26.86°** | +22.39° | **−0.00°** | **+22.39°** | 29.182° |
| f339 | +9.92° | **−0.00°** | +9.92° | −42.22° | **−0.00°** | **−42.22°** | 10.129° |
| f333 | +7.21° | **+0.00°** | +7.21° | +7.66° | **−0.00°** | +7.66° | 8.912° |
| f084 | 0.00° | −0.00° | 0.00° | 0.00° | −0.00° | 0.00° | 5.416° |

**The avatar's torso never yaws. At all. In any frame.** Torso-up error is 7.7–15.5° besides.

Now the same arm measured twice — once in the world, once in each figure's **own torso frame** (right =
shoulder axis, up = hip→midShoulder, Gram-Schmidt orthonormalised, built identically for both figures).
A viewer never sees an arm's world direction; they see it **against the body**:

| video frame | arm | world-frame error | **body-frame error** | shoulder yaw error |
|---|---|---|---|---|
| f419 | left | **0.0000°** | **28.48° / 38.96°** | −50.84° |
| f419 | right | **0.0000°** | **23.13° / 51.10°** | −50.84° |
| f237 | left | **0.0000°** | 16.17° / 27.99° | −26.86° |
| f237 | right | **0.0000°** | 23.29° / 12.09° | −26.86° |
| f339 | left | **0.0000°** | 6.94° / 8.13° | +9.92° |
| f339 | right | **0.0000°** | 6.20° / 9.86° | +9.92° |
| f333 | left | **0.0000°** | 12.52° / 5.16° | +7.21° |
| f333 | right | **0.0000°** | 16.01° / 16.10° | +7.21° |
| **f084 (control)** | left | **0.0000°** | **6.15° / 4.51°** | **0.00°** |
| **f084 (control)** | right | **0.0000°** | **5.70° / 4.40°** | **0.00°** |

**The body-frame error tracks the shoulder yaw error almost one-for-one** (50.84 → 51.10; 26.86 →
27.99; 9.92 → 9.86; 7.21 → 16.10; 0.00 → 6.15), and the control frame with zero torso error has only
the 4–6° residual attributable to §5c/§5d. That is the causal chain, measured, not argued.

The physical consequence — distance of the wrist from the spine axis:

| frame | arm | avatar | where it belongs (source × 2.5006) | ratio |
|---|---|---|---|---|
| f419 | left | **0.1597 m** | 0.2631 m | **0.61** |
| f419 | right | **0.1531 m** | 0.4373 m | **0.35** |
| f237 | right | 0.4555 m | 0.2311 m | 1.97 |
| f339 | left | 0.7102 m | 0.7044 m | **1.01** |
| f084 | left | 0.7293 m | 0.7760 m | 0.94 |

At f419 both hands are pulled to ~0.15 m of the spine axis — **well inside the hoodie volume**. At f339,
where the shoulder yaw error is only 9.92°, the same measure is 1.01. The frames with a correct torso
put the hands in the right place; the frames with a 51° torso error put them inside the chest.

---

## 6. The mechanism, in the code

```csharp
// KalidokitControlRigDriver.Apply
float hipsYaw  = DampYaw(pose.Hips.y,  ref hipsYawRate,  ref hipsYawSmooth,  dt) * torsoYawScale;
float spineYaw = DampYaw(pose.Spine.y, ref spineYawRate, ref spineYawSmooth, dt) * torsoYawScale;
float spineRoll = pose.Spine.z * torsoRoll;
ApplyBone(spine, new Vector3(bend * spineShare, spineYaw * SpineYawDamp, spineRoll * SpineYawDamp));
if (chest != null)
    ApplyBone(chest, new Vector3(bend * 0.5f,   spineYaw * ChestYawDamp, spineRoll * ChestYawDamp));
```

`torsoYawScale = 0` (from `kalidokitTorsoYawScale: 0`) ⇒ `hipsYaw = spineYaw = 0` identically.
`torsoRoll = 0` (from `kalidokitBodyTorsoRoll: 0`) ⇒ both roll terms are 0 too. Only `bend` survives.

Control-bone rotation away from identity, and the rendered skinned bone beside it — **the two are equal
to the last decimal in every cell, which is stage B measured yet again**:

| frame | Hips | Spine | Chest | UpperChest | Neck | Head | L Shoulder | R Shoulder |
|---|---|---|---|---|---|---|---|---|
| f419 | **0.0000** | 3.8749 | 3.8749 | **0.0000** | **0.0000** | **0.0000** | **0.0000** | **0.0000** |
| f339 | **0.0000** | 3.6914 | 3.6914 | **0.0000** | **0.0000** | **0.0000** | **0.0000** | **0.0000** |
| f333 | **0.0000** | 5.4637 | 5.4637 | **0.0000** | **0.0000** | **0.0000** | **0.0000** | **0.0000** |
| f237 | **0.0000** | 1.5717 | 1.5717 | **0.0000** | **0.0000** | **0.0000** | **0.0000** | **0.0000** |
| f084 | **0.0000** | 2.0463 | 2.0463 | **0.0000** | **0.0000** | **0.0000** | **0.0000** | **0.0000** |

**Six of the eight bones that shape the torso and head are never rotated at all.** The two that are get
≤5.46° of pure forward pitch. The human is turning 51°, twisting 52° and leaning 19°.

Three further points, each measured rather than inferred:

1. **`UpperChest` is never written by design, not by configuration.** `Bind()` resolves
   `chest = controlRig.GetBoneTransform(HumanBodyBones.Chest)` and there is no `UpperChest` binding, so
   `spine.003` — a real bone in this rig, sitting between Chest and Neck — is rigid in every pose.
2. **`Neck`/`Head` are never written**, reproducing the V1 audit's finding on this build. On a rig whose
   head is 3.36× scale and ~30 % of rendered height, a permanently frontal head is a large part of why
   the figure reads wrong.
3. **`LeftShoulder`/`RightShoulder` are never written.** The clavicle is 0.3751 m — 26 % of the whole
   shoulder-to-hand chain — and is fixed.

### 6a. Why the workaround's own justification no longer holds

`torsoYawScale = 0` is a deliberate fallback. ADR-027's comment in the source says why: the OAK
depth-derived yaw estimate suffered ±180° ambiguity flips and noise, and *"the chest over-twists and
**DRAGS the arms** (the 'wrong pose' regression)."* Under the **old Euler arm path** that was true — the
arms inherited the chest.

Under ARM V1/V2 it is no longer true. `ApplyAimArm` divides the parent out explicitly:

```csharp
Quaternion parentRig = upperBone.parent != null
    ? Quaternion.Inverse(rigFrame) * upperBone.parent.rotation : Quaternion.identity;
ApplyBoneRotation(upperBone, Quaternion.Inverse(parentRig) * targetUpper);
```

The upper arm reaches `targetUpper` in the rig frame **whatever the chest does**, which is exactly why
§5b measures 0.0000° through a frozen torso. **A noisy chest can no longer rotate the arms; it can only
translate the shoulder.** So the failure mode the workaround was defending against has been removed by
the very work that made the arms exact — and the workaround is now the dominant remaining defect. This
is the user's own turn-1 phrasing, now measured: *"the previous workaround has become a current bug."*

---

## 7. TASK 6 — the exact frame

**Frame 419** (annotation `f=418`, `t = 13.93 s`, wire index 359). Both required conditions hold: the
green annotation tracks the human's limbs correctly, and the rendered avatar is visibly broken — square
to camera with **both arms folded flat into the hoodie**, the left sleeve merged into the chest as a
shapeless bulge, the right hand poking out at the waist.

Evidence: `sbs_vid_f419_idx359.png` (source beside render), `vid_f419_idx359.png`,
`vid_f419_idx359_overlay.png`, `src_f418.jpg`.

```text
frame            vid_f419_idx359          annotated_p0.mp4  f=418  t=13.93s  (wire index 359)
                                             AVATAR LEFT ARM            AVATAR RIGHT ARM
source direction, upper (mirrored)   [+0.0397,-0.7297,+0.6826]   [+0.0962,-0.9424,-0.3204]
control direction, upper             [+0.0397,-0.7298,+0.6825]   [+0.0962,-0.9424,-0.3205]
skinned bone direction, upper        [+0.0397,-0.7298,+0.6825]   [+0.0962,-0.9424,-0.3205]
source direction, forearm (mirrored) [-0.6339,+0.5621,-0.5312]   [+0.9628,+0.0000,+0.2703]
skinned bone direction, forearm      [-0.6339,+0.5622,-0.5311]   [+0.9628,-0.0000,+0.2703]
  source -> skinned bone error, upper              0.0000 deg                0.0000 deg
  source -> skinned bone error, forearm            0.0000 deg                0.0000 deg
source elbow angle                              142.934 deg               89.657 deg
skinned elbow angle                             142.934 deg               89.657 deg
  elbow angle error                               +0.0000 deg              +0.0000 deg
ARM DIRECTION RELATIVE TO THE BODY, upper / fore   28.48 / 38.96 deg    23.13 / 51.10 deg
source joint position   (raw landmarks, hip-relative m, Y up, NOT mirrored)
   shoulder / elbow / wrist   L [-0.1999,+0.4420,-0.3209] [-0.2135,+0.1917,-0.0868] [-0.0694,+0.3196,-0.2076]
                              R [+0.0830,+0.4936,+0.0264] [+0.0558,+0.2271,-0.0642] [-0.1863,+0.2271,+0.0038]
skinned joint position  (world m)
   shoulder / elbow / wrist   L [+0.4505,+2.7732,-0.0877] [+0.4721,+2.3755,+0.2842] [+0.1402,+2.6699,+0.0061]
                              R [-0.4497,+2.7732,-0.0877] [-0.3973,+2.2597,-0.2624] [+0.1068,+2.2597,-0.1209]
visible MESH joint position (skinned vertices, world m)
   shoulder / elbow / wrist   L [+0.3672,+2.8340,-0.0637] [+0.4296,+2.4548,+0.2063] [+0.1457,+2.6850,+0.0061]
                              R [-0.3720,+2.8385,-0.0759] [-0.3210,+2.3478,-0.2192] [+0.1080,+2.2669,-0.1066]
screen-space error, bone vs expected projected skeleton (same camera)
   upper / forearm / elbow px / wrist px   L +0.0003 / -0.0031 deg / 0.01 px / 0.00 px
                                           R -0.0001 / -0.0021 deg / 0.00 px / 0.00 px
screen-space error, visible mesh vs bone
   upper / forearm                         L  +7.02 / +2.67 deg
                                           R  +0.42 / -10.73 deg
TORSO  source shoulder yaw -50.84 deg   avatar shoulder yaw -0.00 deg   error -50.84 deg
TORSO  source hip yaw      -39.06 deg   avatar hip yaw      -0.00 deg   error -39.06 deg
WRIST radius from the spine axis   avatar  L 0.1597 m   R 0.1531 m
   source 0.1052 / 0.1749 m, x2.5006 body scale ->  L 0.2631 m   R 0.4373 m   (ratio 0.61 / 0.35)
```

That single frame contains the whole finding: **every arm number is exactly zero and the picture is
still wrong.** A second instance, `frame 237` (`f=236`, `t = 7.87 s`, yaw error −26.86°), shows both
arms swallowed by the hoodie in the same way; `frame 84` (`f=83`, yaw error 0.00°) is the control and
renders correctly.

---

## 8. TASK 7 — verdict, and the one recommendation

**Nothing was changed.** `git status` shows no modification to any `Runtime/`, `Tests/`, `Packages/`,
scene or Python file. All new files are the diagnostic harness, the captures under
`python-sidecar~/arm_v3_evidence/`, and this document.

### Why the avatar LOOKS crooked when the green skeleton looks correct

Against the brief's list:

| candidate | verdict |
|---|---|
| 1. tracking | **No.** The annotation tracks the human correctly in every frame examined; torso motion is present and smooth in the source. |
| 2. bone transform | **YES — but the TORSO chain, not the arms.** Hips, UpperChest, Neck, Head and both Shoulders are rotated **0.0000°** in every frame; Spine and Chest get ≤5.46° of pitch. Shoulder-line yaw error to **50.84°**, hip-line to **42.22°**. Arm bones are exact to **0.0000°** over 22 measurements. |
| 3. mesh deformation | **No, but real and measured.** 10–20° visible silhouette vs bone, up to 9.4 cm visible elbow shift on bend. Bindposes exact, weights clean, ≤4 influences, no unexpected bones, no scaling. Ordinary garment skinning; secondary. |
| 4. avatar proportions | **No, but real and measured.** Arms **79.1 %** of proportional length (wrist short by 0.2818 m = 55.7 px); per-segment scale spread **1.457×–3.356×**. Third-largest; makes the figure stubby, not crooked. |
| 5. camera / rendering artifact | **No.** Projection verified to 0.003 px; bone-vs-expected screen error is ≤0.013 px everywhere. |
| 6. unknown | **No.** |

**Exact layer responsible: the retarget's TORSO stage — specifically torso yaw, zeroed by
configuration (`kalidokitTorsoYawScale: 0` ⇒ `torsoYawScale = 0`), plus the never-written
`UpperChest`, `Neck`/`Head` and clavicles.**

The arms are aimed correctly in world space and are therefore *wrong relative to the body by exactly the
torso's error*, up to **51.10°** — which is the only frame a viewer perceives them in, and which drives
the hands to 0.35–0.61× of their proper distance from the spine, i.e. inside the torso. Both prior
reports measured world/rig-frame arm direction, the one quantity that is already exact, which is why
this went unfound.

### The single recommended next change — not implemented

**Restore torso yaw, then re-measure.** It is one already-existing serialized field
(`kalidokitTorsoYawScale`, currently `0`, live-tunable via `SetTorsoYawScale`), it needs no new code, and
§6a shows the reason it was zeroed no longer applies now that the arms divide out the parent rotation.
Measure `torso.shoulderYawErr_deg` and `bodyRelative.*.bodyRelErrUpper_deg` on the same five held
frames; the prediction is that the body-frame arm error collapses toward the f084 control's 4–6°.

Do not treat this as a settled fix. Two things must be checked in that task, because this report cannot
answer them:

* ADR-027's damping (rate limit 140°/s, dead zone 8–22°, low-pass 0.15 s, per-bone dampeners
  0.7/0.45/0.25) was tuned against **OAK-D depth** yaw, with ±180° ambiguity flips. This report's yaw
  came from a **monocular** video estimate and was smooth. The yaw signal must be re-qualified on the
  real OAK path before `torsoYawScale = 1` is trusted.
* The V1 audit's **1.40× torso-yaw over-twist** was in the same code path and is untested at
  `torsoYawScale = 1` on this build.

Recorded, not acted on, and deliberately out of scope here: `UpperChest` has no binding at all;
`Neck`/`Head` are never written on a rig whose head is ~30 % of rendered height; the clavicles are never
written; the per-segment proportion spread of 2.30× means direction-only FK can never place a hand where
the human's is; the green debug skeleton is drawn at 1.5× beside a 2.5× avatar, unmirrored, which makes
visual side-by-side comparison misleading in itself.

---

## 9. Honest limits of this report

1. **A held frame suppresses the spine bend.** `bend` is high-passed against a slow baseline
   (`spineBendBaselineTau = 8 s`), so holding a frame for 5 s lets the baseline absorb the pitch and the
   measured bend settles to ≤5.46°. Under live motion the bend term would be larger transiently.
   **Yaw and roll are zero by configuration and are unaffected by hold time** — the central finding does
   not depend on this.
2. **Monocular source.** §0a. No claim here about the OAK-D depth path's own accuracy.
3. **`elbow90`'s forearm is view-aligned**, so that pose carries no screen-space forearm conclusion (§4).
4. **The mesh joints are weight-band centroids**, not anatomical pivots, so the mesh chain carries a
   fixed bias (the constant 8.5 cm rear offset). Only the *pose-dependent change* in that offset is read
   as deformation; the invariance test in §2b is the bias-free measure.
5. **One metric was wrong and was replaced**: whole-shell PCA reported 41.7° where the slice centerline
   reports 12.146° (§2a). The PCA figure appears nowhere in the conclusions.
6. **Five video frames, not thousands.** They were chosen from the source's own torso statistics to span
   0.00°–50.84° of yaw error, including a zero-error control, which is what makes the correlation in §5e
   readable. This is not a distribution over the whole clip.
7. The MCP servers were down and the editor bridge was driven directly (§0). Every figure is from the
   live editor, but not through the normal tooling.

---

## Artefacts

Under `python-sidecar~/arm_v3_evidence/`:

`mesh_trace.jsonl` (every layer, every capture) · `screen_error.json` ·
`00_rest_no_tracking.png` · `pose_{relaxed,tpose,onehoriz,elbow90,overhead,behind}.png` and
`*_overlay.png` · `vid_f{084,237,333,339,419}_*.png` and `*_overlay.png` ·
`sbs_vid_f{084,237,419}_*.png` (source beside render) · `src_f{83,236,332,338,418}.jpg` ·
`zoom_tpose_leftarm{,_raw}.png`
