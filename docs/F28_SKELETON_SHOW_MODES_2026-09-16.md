# F-28 — Skeleton Show: three modes that make the skeleton the product

```text
VERDICT: BUILT AND RUNNING. Three modes, live switching, driven by real tracking through the
         production wire and the F-27 humanized skeleton.
SCENE:   Assets/Games/viitorx-vrm-avtar-unity/Scenes/SkeletonShow.unity
MEASURED: 114-118 fps in the editor with the video feed running.
CHANGED: nothing in the mirror app. This is a separate scene with its own bootstrap.
```

> **Why this exists.** F-26 established that the debug skeleton tracks the user correctly while the
> VRM avatar does not, and that the difference is the retarget — a fixed-proportion mesh driven from
> ~16 bone *rotations* can only aim a limb, never place it. These three modes take the identical joint
> positions and **skip the retarget entirely**. The body cannot be in a pose the tracker did not
> report, because there is nothing between the tracker and the pixels.

---

## 1. The three modes

| key | mode | what it is |
|---|---|---|
| **1** | **GLOW SKELETON** | the skeleton as the character — bone lines, joint cores, hand/foot trails, all coloured by speed |
| **2** | **ENERGY BODY** | a hologram skin over the same joints — capsule volumes that merge into a body, with a scan band travelling up it |
| **3** | **MOTION EFFECTS** | the body as an input device — particles from the hands, a beam between them when raised, expanding rings on the floor under a fast hand |

**Switching:** `1` / `2` / `3` pick a mode, `TAB` cycles, and the on-screen buttons do the same.
`H` toggles the F-27 humanized skeleton live, which is the fastest way to see what that layer does.

## 2. The one rule that makes three modes look like one product

**Colour means SPEED, in every mode.** Cool blue at rest → cyan → magenta at speed, from a single
shared palette, driven by a single shared speed measurement in `SkeletonPose`. A viewer learns the
mapping in the first mode and it stays true in the other two. If each mode had computed its own
"movement" the switcher would feel like three unrelated toys.

Everything is HDR (colour values above 1.0) and the bootstrap creates its own Bloom + ACES tonemapping
volume. That is what makes the modes glow rather than merely brighten — and it is why the bootstrap
creates the volume rather than expecting the scene to: a colour at 0.9 produces no glow at all.

## 3. What is real tracking and what is presentation

**Real, and unchanged from the mirror app:** `OakDUdpPoseProvider` on UDP 8899, `PoseSpaceConverter`,
P1-3's 40 ms interpolation buffer, and the F-27 humanized skeleton. The sidecar is run exactly as
usual; nothing knows this is a different scene.

**Presentation only:** the palette, the bloom, the floor grid, the capsule volumes, the particle
rates. No mode invents a joint position, and no mode smooths one — `SkeletonPose` smooths *speed*
(for colour and emission rates) but never position.

## 4. Why this dodges the hard problem

A VRM must deform a fixed-proportion mesh from bone rotations; F-26 §3 lists what that costs — 99
positional degrees of freedom collapsed to ~48 rotational ones plus a root translation, the subject's
limb ratios discarded, and every stage of the conversion lossy by design. Mode 2 is the direct
answer: the visual is **built from the joint positions every frame**, so there is no skinning, no
proportion mismatch and no retarget. The trade is explicit — a stylised body instead of a
human-looking one, in exchange for that entire class of error disappearing.

Mode 3 is the one whose value does not depend on the tracking being beautiful. A ripple is convincing
if a hand's position is roughly right and its speed is roughly right, both of which F-26 §2 already
established. It also degrades best: a dropped joint stops emitting instead of drawing something wrong.

## 5. Implementation notes worth knowing

* **The scene asset is a camera and one GameObject.** Everything else — materials, particle systems,
  the bloom volume, the floor grid — is built at runtime by `SkeletonShowBootstrap`. A scene full of
  hand-placed particle systems is not reviewable in a diff and drifts from the code that drives it.
* **A mode owns everything it creates and destroys all of it on switch.** Verified live: switching
  leaves exactly one mode root (53 objects → 45 → 33). A mode leaking a trail renderer would make the
  *next* mode look wrong, and whoever debugged it would be reading the wrong file.
* **Separate assembly** (`VirtualMirror.SkeletonShow`), so the existing `VirtualMirror.Rendering`
  assembly keeps its deliberately minimal dependency surface.
* **IMGUI for the switcher**, on purpose: no canvas, no event system, no prefab. This is a show and
  diagnostic scene, not shipped UI.

## 6. Measured

```text
114-118 fps in the editor with the video feed running (smoothed).
An earlier reading of "3 fps" was the screenshot capture spiking the single frame the HUD sampled;
the fps readout is now smoothed over ~0.5 s so a one-frame spike cannot be misread as a problem.
Mode switching verified live: Mode_GlowSkeleton 53 objects, Mode_EnergyBody 45, Mode_MotionEffects 33
(2 particle systems, 29 line renderers, 2 trails), exactly one root at a time.
```

## 7. What remains

1. **Never run on a live camera.** Everything here was driven from `video.webm` through
   `f23_video_to_unity.py`. The modes have not been seen with a person standing in front of an OAK-D.
2. **Mode 2's joints still show seams** where a bright core capsule meets its neighbour at an
   extreme angle. The blob spheres cover most of it; a metaball-style shader would cover all of it,
   and would be the first thing to do if mode 2 is the direction chosen.
3. **Mode 3's floor is flat and infinite.** The ripples use the lowest ankle as the floor height,
   which is right for a standing subject and wrong for a seated one.
4. **No audio, no scene changes, no idle state.** With nobody in frame all three modes simply show
   nothing. A real installation needs an attract state.
5. **Nothing here has been shown to anyone.** Whether this reads as "intentional" rather than as a
   debug visualisation is a judgement about an audience, and no audience has seen it.

## 8. Evidence

```text
docs/evidence/f28/
    mode1_glow_skeleton.png       glowing lines + speed colour + hand trails
    mode2_energy_body.png         the hologram skin, scan band visible
    mode3_motion_effects.png      hand particles, beam, floor ripple
    mode3_particles_closeup.png   the particle burst at close framing
source: driven from Assets/Games/video/video.webm through the production wire
```
