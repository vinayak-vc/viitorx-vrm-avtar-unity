# F-19 — Portrait Production Integration + Live VRM Validation

```text
VERDICT: CONDITIONAL
```

Live session run 2026-09-14 on the machine with the OAK-D-PRO-W-97 (`14442C10F143D3D200`) attached,
Unity Editor in Play mode, one subject plus a second person for §19. This supersedes the earlier
replay-only draft of this report, which could not execute §8, §10–§12, §17–§19 for want of hardware.
Where a replay figure has been replaced by a live one, the live one is used and the replay figure is
named as such.

---

## 1. Executive conclusion

Portrait input support is integrated in the production sidecar behind a default-off flag, is
byte-for-byte equivalent to the F-18 diagnostic transform, and **ran live end-to-end into the real
VRM avatar for ~24 minutes across 134,959 rendered frames, 85,395 of them inside labelled
protocol blocks.** The torso answer is unambiguous and good.
Three defects are blocking, and one of them is not caused by portrait at all.

**What passed.**

* **§10 — a physically square human produces a visually square avatar.** Rendered shoulder-line yaw
  median **0.012°**, range −2.59…0.30°, frame-to-frame p95 **0.0064°**, **zero** snaps > 10°.
* **§11/§20 continuity — zero snaps > 10° in every one of 37 protocol blocks**, worst per-block
  frame-to-frame p95 **0.503°** against a 5° target, across a 120° range of followed torso yaw.
* **§14 skeleton — worst bone-length deviation 0.0011 %**, worst |lossyScale − 1| **0.000002**
  (no bone scaling), on every capture.
* **§12D — zero left/right swaps** in 80,835 analysed frames.
* **§18 — graceful loss and recovery.** Walking out of view holds the last valid pose (P0 gate held
  76.4 % of that block, longest 7.6 s); walking back in re-acquires with **zero** snaps.
* **§12 performance — 29.9 fps, capture→send 31.8 ms p50, camera→host 17.3 ms p50**, and P1-2's
  freshness is intact (stale-dropped p50 = 0), which §20 explicitly required not be sacrificed.

**What failed, and must gate any public deployment.**

1. **§19 person switching — FAIL.** With a second person in frame the tracker moved to them after
   ~25 s and **never came back**, through the walk-through, the occlusion and after they left.
2. **Restarting the sidecar permanently freezes the avatar** — silently, with healthy receive
   counters. Pre-existing, not caused by portrait, and §1 forbade me from fixing it.
3. **§12A — both elbows fold to 177–180°** (human maximum ≈ 150°) whenever the hands come near the
   face, on 81–91 % of that block's frames, with the P0 gate holding nothing.

**And one integration finding that changes what "validated" means.** Every F-18 portrait number was
measured with sub-pixel 1/8 (capture config `sub3`); **production ships `setSubpixel(False)`**.
F-18's roadmap entry did flag this ("enable sub-pixel 1/8 when the rotation lands"), so the gap was
known — what is new is that it had **not** been done, that the F-19 startup banner **falsely claimed
it was already on** (§2c), and what it actually costs: at 0.90 m the shipped configuration has a
**6.80° torso-yaw quantum** against 0.85° with sub-pixel, which no ≤5° median can survive.
**F-18's numbers therefore do not describe the shipped configuration.**

---

## 2. Production changes (§2, §24)

### 2a. Unity — zero modifications to existing production files

```text
?? Runtime/Diagnostics/F19BoneRecorder.cs
?? Runtime/Diagnostics/F19BoneRecorder.cs.meta
```

`git diff -- Runtime/` is **empty**. The only addition is the §13 rendered-bone recorder, which the
brief explicitly asked for. It is referenced by nothing in the product, does nothing unless a
GameObject carrying it is created deliberately, reads transforms and never writes one, finds the
driver by reflection so the Diagnostics assembly gains no dependency edge, and self-terminates
(unsubscribes in `OnDisable`, closes in `OnDestroy`, destroys itself after `maxSeconds`).

### 2b. Sidecar — two flags, both defaulting to current behaviour

| flag | default | effect at default |
|---|---|---|
| `--portrait` / `--portrait-dir` | off, `ccw` | none — landscape is unchanged and remains the rollback path |
| `--subpixel-bits` | `-1` | none — production stereo settings untouched |

Rollback for either is deleting the flag from the launch command.

`--subpixel-bits` is a **second** flag beyond the "portrait behind a feature flag" that §24 names as
the only intended change, so it is called out rather than buried. It exists because §8 measured that
the shipped stereo configuration cannot meet §20's own measurement criterion (see §4b); without it
the live avatar test could only have been run on a configuration that provably fails, which would
make every avatar number below uninterpretable. It changes nothing unless passed.

### 2c. A configuration log that was lying

The earlier F-19 startup banner printed:

```text
[wb]   stereo: subpixel=on(1/8) LR-check=on align=CAM_A  surfaceDepth=... kwin=...
```

while `oak_depth.build_rgbd_pipeline` built `stereo.setSubpixel(False)`. The string was hand-written
and had drifted from the code. §4 requires the startup log to state the sub-pixel configuration, so a
banner that can disagree with the pipeline is worse than no banner, because it is trusted.

Fixed by declaring the settings once in `oak_depth.STEREO_CONFIG`, which both the pipeline builder
and the banner now read. Changing a value there changes both. The banner now reads:

```text
[wb] CameraOrientation = PORTRAIT_CCW
[wb]   rgb 400x640   depth 400x640
[wb]   intrinsics  fx=284.454 fy=284.627 cx=201.530 cy=319.789
[wb]   intrinsics(landscape, pre-rotation)  fx=284.627 fy=284.454 cx=319.211 cy=201.530
[wb]   FOV  H 70.22 deg  V 96.70 deg
[wb]   stereo: preset=HIGH_DENSITY subpixel=on(1/8) LR-check=on align=CAM_A mono=400p
[wb]   depth sampling: surfaceDepth=True kwin=5 (F-08)
[wb]   working-distance target: 0.90 m (F-18 preferred)
[wb]   udp -> ('127.0.0.1', 8899)
```

### 2d. Insertion point (§3, §6)

Two places, both minimal. At start-up the intrinsics are rotated **against the depth-frame
dimensions**, because `read_rgb_intrinsics` returns them in depth pixels and `backproject` scales
`uv` into that frame; rotating against the RGB dimensions would silently corrupt every
back-projected X. Per frame, RGB and depth are rotated **identically** — depth is aligned to CAM_A,
so any mismatch samples the wrong surface for every keypoint.

Downstream semantics are untouched: back-projection in the rotated frame with rotated intrinsics
still yields X = left/right, Y = down, Z = depth, the UDP payload is unchanged, and Unity cannot tell
which orientation produced it. Portrait is a camera transformation, not a pose protocol.

---

## 3. Portrait transformation equivalence (§5)

`f19_equivalence.py`, re-run on this machine: **5 / 5 PASS.**

| check | result |
|---|---|
| intrinsics rotation vs 4 recorded F-18 captures | worst component error **0.000e+00 px** |
| pixel mapping, both directions, 7,410 probes | image-vs-formula **0 px**, round-trip **0 px** |
| semantics — same metric point through rotated and unrotated paths | worst \|dXYZ\| **0.0001 mm**, \|dZ\| **0.0000 mm** |
| 10,322 real recorded F-18 frames | **0** yaw values outside the storage-rounding box; worst dx **0.0401 mm** |
| left/right identity, 9,960 confident frames | **0** disagreements (a mirrored rotation would give ~100 %) |

The transform is imported from `f18_portrait.py`, not re-derived, exactly as §2 required.

Live confirmation: the sidecar's rotated intrinsics printed at start-up
(`fx=284.454 fy=284.627 cx=201.530 cy=319.789`, H 70.22° / V 96.70°) are **identical to the values
F-18 recorded** in its capture metadata.

---

## 4. Camera preflight (§8) — EXECUTED

### 4a. Geometry, orientation and the mount (§7)

§7's tilt and roll were measured from the device's own **BNO086 IMU** rather than estimated from
imagery. One caveat had to be settled first: `getImuToCameraExtrinsics(CAM_A)` reports an **identity
rotation**, and that is a placeholder, not a measured extrinsic — a raw frame shows the subject's
head at the +X edge (world-up = camera −X) while the IMU simultaneously reports gravity along its own
+Y, so IMU X/Y do **not** coincide with camera X/Y. What *is* established empirically: rotating the
camera 90° about the optical axis changed the IMU X and Y components completely while leaving Z
unchanged (−1.129 → −1.126), and only the roll axis is invariant under a roll, so **the IMU's Z axis
is the optical axis** and tilt is trustworthy. Roll is anchored to two raw frames whose orientation
was read directly off the image.

| item | measured |
|---|---|
| orientation | **PORTRAIT_CCW**, detected from the image, not assumed |
| optical-axis tilt from horizontal | **5.89°** (accepted; as level as this mount allows) |
| roll deviation from exact portrait | **+0.14°** |
| camera height above floor | **NOT MEASURED** — still needs a tape |
| portrait frame / depth | 400 × 640 / 400 × 640 |
| intrinsics | fx 284.4542, fy 284.6272, cx 201.5303, cy 319.7891 |
| FOV | H **70.22°**, V **96.70°** |
| depth validity | **93–95 %** of frame pixels; shoulder depthQuality p50 **0.975–1.000** |
| left/right identity | **100 %** agreement, every frame of every run |
| ±180° inversion | **0** occurrences |
| hips measured | 282/282 frames |
| COCO-17 in frame (≥10 px from border) | **17 / 17** |
| camera→host frame age | p50 **31.5 ms**, p95 46.3 ms |
| RGB/depth sync | p50 **12.09 ms** |

**F-18's derived 19–32° downward pitch was wrong.** The mount was at ~6–7° before levelling and
**5.89°** after. That estimate has been carried through two reports and is now corrected by
measurement.

A process note worth recording: the camera was **physically rotated between my first two preflight
runs**, which I detected from the IMU rather than being told. The first run was therefore a landscape
mount measured through a portrait transform and its numbers are void. Everything reported here is
from runs taken after levelling, on a mount verified unchanged between them (IMU agreement to 0.03°).

### 4b. Square-stance torso yaw — the measurement gate

Both configurations measured back to back on the same unchanged mount:

| configuration | n | yaw median | yaw p95 | frame-to-frame p95 | §20 gate |
|---|--:|--:|--:|--:|---|
| **A — production as shipped (sub-pixel OFF)** | 270 | **20.02°** | 20.13° | 3.672° | **FAIL** |
| **B — sub-pixel 1/8 (what F-18 validated)** | 268 | **6.94°** | 20.23° | 2.284° | **FAIL** |

Everything that must not move between them did not: shoulder separation 339.4 vs 331.5 mm, L/R
identity 100 % vs 100 %, inversions 0 vs 0, depth validity 93.1 vs 94.0 %, frame age 31.2 vs 33.6 ms.

**Why A fails is structural, not noise.** Across 270 frames config A produced **6 distinct shoulder
Δz values, 235 of them on a single 116 mm bin** — the signal is locked in one quantisation step. Config
B produced **79 distinct values**, median gap 1.0 mm.

```text
depth quantum at 0.90 m, 1 px     38.17 mm  ->  torso-yaw quantum 6.80 deg
depth quantum at 0.90 m, 1/8 px    4.77 mm  ->  torso-yaw quantum 0.85 deg
```

A 6.80° quantum cannot express a ≤5° median except by landing in the zero bin. **The shipped
configuration cannot meet §20's measurement criterion at the intended working distance.** F-18's
roadmap already called for enabling sub-pixel; this quantifies why it is not optional.

### 4c. Was the 20° a measurement bias or the subject's actual stance? — the 180° test

A camera-fixed artefact keeps the sign of shoulder Δz through a 180° turn; a genuinely rotated torso
reverses it.

| block | n | yaw median | Δz median | Δx median | uL > uR |
|---|--:|--:|--:|--:|--:|
| FACE CAMERA | 271 | **+0.90°** (p95 7.12°) | −5.0 mm | +319.8 mm | 100 % |
| TURN AROUND | 271 | **+170.94°** | −52.0 mm | −324.9 mm | **0 %** |
| FACE CAMERA | 268 | −7.79° (p95 11.21°) | +43.0 mm | +314.6 mm | 100 % |

The turn is tracked correctly — 170.94°, with Δx correctly reversing — so there is **no inversion
bug**. And on the block where the subject genuinely squared up, the path delivered **0.90° median /
7.12° p95: a clean §20 pass**. The Δz-sign test itself is **inconclusive** here and is reported as
such: block 1's Δz is −5.0 mm, statistically indistinguishable from zero, so its sign carries no
information.

**Conclusion.** The measurement path is *capable* of the gate (0.90° / 7.12°) but only with sub-pixel
on, and the reading is dominated by how square the subject actually stands — across four square-stance
blocks the median ranged 0.90° → 6.94°. Since the path correctly reports genuine torso rotation, and
this experiment had no independent reference for "square", **measurement error and subject posture
cannot be fully separated from these data.** That limitation is real and is not argued away.

Per §8's own stop rule the shipped configuration's failure was recorded before proceeding; the live
avatar work below was then run on configuration **B**, the one F-18 validated, and is labelled as
such throughout.

---

## 5. Live pipeline (§9)

```text
LIVE: EXECUTED - 134,959 rendered frames (85,395 labelled), real camera, real subject,
      full production path
```

`OakDUdpPoseProvider` → P1-3 pose buffer → `TrunkGate` → `TorsoYawGuard` (V6) → V5 composition →
`ArmAimSolver` (Arm V2) → control rig → UniVRM `Process()` → rendered bones. Read back live from the
running scene: `useOakUdpTracking=True`, `oakUdpPort=8899`, `kalidokitTorsoYawScale=1`,
`kalidokitAimArms=True`, `trackLegs=True`, `limbConfidenceThreshold=0.3`. No safety layer disabled.

Sidecar: `--portrait --subpixel-bits 3`, banner as §2c. Rig `VRM1` throughout — **no avatar swap
occurred in this session**, so the mid-session rig change the replay draft flagged is not reproduced
here and its cross-run bone-length caveat does not apply.

---

## 6. Rendered-bone results (§13)

The recorder samples on `Application.onBeforeRender` — after every Update/LateUpdate and immediately
before rendering — so what is recorded is what is drawn. A LateUpdate hook would race the driver's
own call to `Vrm10Runtime.Process()` and could capture the pose one stage early.

Per record: frame index, timestamp, unscaled dt, rig name, protocol block, gate states, root
position, and for each of 20 humanoid bones the world position, **world rotation, local rotation**
and lossyScale. §13's angular velocity, max angular step, joint range, bone length and end-effector
position are **derived** from these rather than stored, so a stored summary can never disagree with
its own samples.

| capture | frames | mean fps | blocks |
|---|--:|--:|--:|
| `bones_square` (§10) | 7,306 | 228 | 1 |
| `bones_motion` (§11) | 59,246 | 217 | 19 |
| `bones_public` (§17/§18) | 27,463 | 210 | 8 |
| `bones_entry2` (§18) | 19,816 | 220 | 4 |
| `bones_multi` (§19) | 21,128 | 217 | 5 |

Unity renders at ~217 fps against a ~30 fps source, so ~7 rendered frames share each source pose.
Two consequences are stated rather than left to be misread: per-rendered-frame steps are naturally
small, and the "most frozen bone" figure (Hips, 67–85 % of frame pairs) is **oversampling, not a stuck
joint**.

---

## 7. Human-quality audit (§12)

### §12A — impossible joint configurations: **FAIL, localised and attributable**

This is the finding F-20 exists for, and unlike the replay draft it *is* attributable: the sidecar
measured all 33 landmarks live with real stereo depth, so distal joints are no longer sitting on an
assumed plane.

| block | L elbow max flex | L over-fold frames | R elbow max flex | R over-fold frames |
|---|--:|--:|--:|--:|
| **HANDS NEAR YOUR FACE** | **177.5°** | **1,434 / 1,770** | **179.6°** | **1,614 / 1,770** |
| BACK TO CENTRE | 175.4° | 303 | 42.4° | 0 |
| ROTATE TORSO RIGHT | 124.3° | 0 | 168.9° | 33 |
| REACH LEFT, THEN RIGHT | 124.0° | 0 | 157.5° | 22 |
| BEND FORWARD | 34.2° | 0 | 163.1° | 1 |
| *all 14 other blocks* | ≤ 118° | **0** | ≤ 131° | **0** |

A human elbow reaches ~150°. In `HANDS NEAR YOUR FACE` the **median** flex was 167.8° (L) and 169.4°
(R) — this is not an occasional spike, it is the whole block — and the bend normal wandered
**74.8° / 129.6°**, i.e. the joint bends in directions a hinge cannot.

**That wander survives the robustness check and the whole-capture figure does not.** Raising the
minimum-bend threshold, a real hinge defect must persist:

| joint | thr 15° | thr 30° | thr 60° | verdict |
|---|--:|--:|--:|---|
| L elbow, whole capture | 67.9° | **0.1°** | 12.6° | **not robust — not reported as a defect** |
| R elbow, whole capture | 27.3° | 21.3° | 61.7° | not robust |
| L elbow, hands-near-face | 74.8° | 74.8° | 74.8° (and at 120°) | **robust — real** |
| R elbow, hands-near-face | 129.6° | 129.6° | 129.6° (and at 120°) | **robust — real** |
| L / R knee | 2.9 / 2.2° | 0.7 / 0.8° | 0.6 / 6.4° | clean hinges |

Every frame of that block is bent past 120°, so the normal is perfectly well-conditioned there. The
whole-capture number was dominated by near-straight frames where the cross product vanishes, and is
**withdrawn**. Knees are clean throughout; the right knee's 30 over-fold frames (max 176°) during
walking are the only other instance and are an order of magnitude rarer.

**The P0 LimbGate held nothing during that block (0.00 %).** The impossible poses travelled the full
path un-suppressed.

### §12B — mesh deformation: **no gross artefact observed, at the flex I could capture**

A rendered capture at **150.8°** left-elbow flex shows dark wedges at both elbows. A control capture
at 45° / 36° was taken specifically to test whether those are deformation — **they are the hoodie's
sleeve bands, present at both flex levels.** My first reading was wrong and the control disproved it.
At 150.8° the skinning holds up: no tearing, no collapse, no limb passing through the torso.

**Not claimed:** the 177–180° cases were never captured as an image, only as telemetry. §12B is
therefore answered only up to ~151° of elbow flex, and the worst measured poses remain visually
unexamined.

### §12C — temporal defects: **pass within blocks**

Per-block, frame-to-frame rendered yaw p95 ≤ **0.503°** and **zero** snaps > 10° in **all 37 blocks**.
The 6 snaps and 1,124 bone-snaps that appear in whole-capture aggregates are **artefacts of my own
concatenation across block boundaries** (18 boundaries, 6 of which exceed 10°) and are not defects;
every individual block is clean.

Real within-block bone snaps do occur during fast motion — `TURN LEFT-RIGHT AT NORMAL SPEED` 295,
`REACH LEFT, THEN RIGHT` 139, worst bone `RightLowerArm` p95 2.9–4.1° — concentrated in the forearms
(wrist/forearm roll). These are below the §20 threshold at p95 but are the second most likely F-20
target after the elbows.

### §12D — cross-body errors: **PASS**

**0** laterally crossed hands in **80,835** analysed frames. Zero left/right swaps anywhere.

---

## 8. Bone-length stability (§14) — PASS

| capture | worst chain deviation | worst \|lossyScale − 1\| |
|---|--:|--:|
| §10 square | **0.0003 %** | 0.000001 |
| §11 motion | 0.0011 % (neck→head) | 0.000002 |
| §17/§18 public | 0.0010 % | 0.000002 |
| §18 entry/exit | 0.0011 % | 0.000002 |
| §19 multi-user | 0.0011 % | 0.000002 |

Bone lengths are constant and **the cause is attributed, not assumed**: `lossyScale` is 1.000000 to
six decimals on every tracked bone of every frame, so the residual is joint motion, not scaling. The
avatar never squashes its skeleton. One rig (`VRM1`) throughout, so these are directly comparable.

---

## 9. Joint-angle distributions (§15)

Collected, not enforced, as §15 requires. Whole-capture elbow/knee flexion and hinge-normal spread are
in §7 and in `*_live_analysis.json`; the per-block breakdown is the useful artefact and is what §14's
F-20 table is built from.

P0 LimbGate engagement, by block — this is new information, and it **contradicts the replay draft's
"gates never held"**:

| block | gate held | longest hold |
|---|--:|--:|
| WALK OUT OF VIEW | **76.4 %** | **7.6 s** |
| CROUCH DOWN | 68.3 % | 1.95 s |
| BEND FORWARD | 47.8 % | 1.87 s |
| TURN SIDE-ON | 47.2 % | 4.51 s |
| HOLD ONE HAND AGAINST YOUR TORSO | 35.0 % | 1.96 s |
| PUT ONE HAND BEHIND YOUR BACK | 34.1 % | 1.14 s |
| ROTATE TORSO LEFT | 25.8 % | 0.89 s |
| square / still / arms blocks | 0.00 % | 0 |

The counter's cadence was verified rather than assumed: the maximum counter value equals the maximum
run of consecutive rendered frames in the held state for all four limbs (ratio 1.00), so holds are in
**rendered** frames and the longest was **7.6 s**. A 7.6 s held limb is a visibly frozen limb — but in
the block where it happens (subject out of frame) that is the *correct* behaviour, and it is what
makes §18 pass.

---

## 10. Public-user tests (§17, §18)

### §18 single-user follow / lose / re-enter — **PASS**

| block | yaw range | step p95 | snaps > 10° | gate held | longest hold |
|---|--:|--:|--:|--:|--:|
| stand centred and still | 0.9° | 0.006° | 0 | 1.1 % | 0.13 s |
| **walk out of view** | 0.0° | 0.000° | 0 | **76.4 %** | **7.6 s** |
| **walk back in and stop** | 105.4° | 0.317° | **0** | 13.6 % | 2.30 s |
| **walk out, re-enter twice** | 119.7° | 0.503° | **0** | 32.8 % | 3.72 s |

The system follows correctly, **loses the user gracefully by holding the last valid pose**, does not
jump to the background, and re-acquires without a snap. Position within the zone is also clean:
centred / slightly left / slightly right / edge-of-view all ran with zero snaps and bone deviation
≤ 0.0010 %.

Awkward poses (arms crossed, hand behind back, hand against torso, side-on) produced no snaps, but did
drive gate holds of 1.1–4.5 s — the limb goes still rather than wrong.

### §17 body-size and clothing generalisation — **NOT EXECUTED**

Two people were available. Height, build and clothing variation were not tested, so nothing about
generalisation across visitors can be claimed. One subject and one second person is not a public
sample.

---

## 11. Multi-user tests (§19) — **EXECUTED, FAIL**

The protocol ran with the primary asked to stand still throughout, so any avatar motion could only
come from the second person.

**At the avatar level everything looked perfect**, and that is the trap:

| block | yaw range | step p95 | snaps > 10° | max root step |
|---|--:|--:|--:|--:|
| second person behind | 80.4° | 0.231° | **0** | 5.0 mm |
| second person beside | 60.0° | 0.177° | **0** | 7.8 mm |
| walks through frame | 52.2° | 0.100° | **0** | 6.9 mm |
| briefly blocks primary | 41.0° | 0.157° | **0** | 6.3 mm |
| second person leaves | 35.3° | 0.191° | **0** | 7.0 mm |

**The source data shows a complete person switch.** Hip depth over the same window is strongly
bimodal — 1.25–1.40 m (primary) and 2.30–2.60 m (second person) — with a one-way transition:

```text
t =  0-20 s    hipZ 1.18-1.29 m     79-100 % of frames in the primary's depth band
t = 20-30 s    hipZ 1.88 -> 2.31 m  transition
t = 30-90 s    hipZ 2.31-2.70 m          0 % of frames in the primary's depth band
```

Measured landmark coverage held at ~21/33 the whole time, so the tracker was confidently locked onto
the **second** person — through the walk-through, through the occlusion, and still after they left.
It never returned. Shoulder Δx sign flipped on 604 of 2,472 source frames (24.4 %) during the window.

**The methodological point matters as much as the result.** The retarget's lerp smoothed a total
person switch into rendered telemetry with zero snaps and 7.8 mm maximum root steps. **Rendered-bone
continuity metrics cannot detect person switching.** Any future acceptance test for this must read the
source depth stream; an avatar-level check will pass a switched avatar every time.

For a mall or airport installation this is disqualifying as it stands: the mirror will follow whoever
the pose model prefers, and will not hand back.

---

## 12. Performance (§12) — PASS

Production sidecar, portrait, sub-pixel 1/8, **headless** (measured without the preview window's
cost), 22,678 frames over 759 s:

| metric | p50 | p95 | max |
|---|--:|--:|--:|
| send rate | **29.9 fps** | 27.9 fps (p05) | — |
| camera→host frame age | **17.3 ms** | 33.7 ms | 50.5 ms |
| RGB/depth sync | 12.09 ms | 12.09 ms | — |
| capture→pose | 26.4 ms | 28.0 ms | — |
| pose→depth | 3.9 ms | 6.0 ms | — |
| **capture→send total** | **31.8 ms** | 34.9 ms | 84.1 ms |
| P1-1 tracker cost | 0.183 ms | 0.263 ms | — |
| P1-2 stale frames dropped | **0** | 0 | — |

**P1-2's freshness improvements are intact** — stale-dropped p50 = 0 and frame age 17.3 ms, against
the ~131 ms the FIFO policy used to cost. §20's requirement not to sacrifice them is met. Unity
rendered at 205–228 fps across every block. F-18 measured the rotation itself at 0.2584 ms/frame (0.78 %);
that cost is inside capture→send above and is not separable here, which is the honest way to state it.

---

## 13. Failure cases

### 13a. Sidecar restart permanently freezes the avatar — **BLOCKING, pre-existing**

Discovered because I restarted the sidecar mid-session. The next capture was **13,410 frames of
perfect stasis** — 0.0000 % bone deviation, hips frozen 100 %, every joint angle identical to six
decimals. Diagnosed live from the running scene:

```text
provider.IsRunning     = True          <- looks healthy
provider.ReceivedCount = 22860         <- still receiving
provider.ParseErrors   = 0             <- parsing fine
PoseBuffer.RejectedOutOfOrder = 4948   <- every new frame rejected
PoseBuffer.LastSeqA/B  = 22678         <- stuck on the last frame of the PREVIOUS run
buffer packetAgeMs     = 208417        <- rendering a 3.5-minute-old pose
```

The sidecar restarts its `seq` counter at 1; P1-3 rejects everything below the sequence it already
holds; the avatar freezes **forever**, silently, while every health counter reads normal. The system
computes `packetAgeMs = 208417` and does nothing with it. Recovery required restarting Play mode.

Not caused by portrait. §1 forbids modifying P1-3, so this is recorded, not fixed. For an unattended
public installation any sidecar crash, watchdog restart or USB re-enumeration bricks the mirror until
Unity is restarted — and nothing in the running system reports a fault.

### 13b. Person switching — §11 above.

### 13c. Elbow hyperextension near the face — §7 §12A above.

### 13d. Forearm roll snaps during fast motion — §7 §12C above. Below threshold at p95; noted.

---

## 14. F-20 constraint requirements (§23)

| Failure observed | Joint | Frequency | Severity | Constraint needed |
|---|---|--:|---|---|
| Elbow folds to 177–180° (human max ≈150°) whenever hands approach the face | Left + Right elbow | **81 % / 91 %** of the hands-near-face block; **0 %** of 14 other blocks | **High** — grossly non-human, un-gated | Hard flexion clamp ≈150°, applied before the control rig |
| Elbow bend normal wanders 74.8° / 129.6° at >120° flex | Left + Right elbow | same block, robust at every threshold | **High** | Hinge-axis constraint: project the bend onto the elbow's anatomical axis |
| Elbow/knee over-fold during reach, torso rotation, bend | R elbow, R knee | 22–33 frames per block; R knee 30 total (max 176°) | Medium | Same clamp, one threshold covers both |
| Forearm roll snaps in fast motion (p95 2.9–4.1°, isolated large steps) | Left + Right lowerArm | 295 / 139 per fast block | Medium | Angular-velocity limit on forearm roll |
| Tracker switches to a second person and never returns | *not a joint* | 1 of 1 trials, permanent | **Blocking** | **Not an F-20 constraint** — needs subject identity/locking in the sidecar |
| Sidecar restart freezes avatar permanently | *not a joint* | deterministic | **Blocking** | **Not an F-20 constraint** — needs seq-reset tolerance + a staleness fault in P1-3 |

The last two rows are deliberately marked as out of scope for a biomechanical layer. Clamping joints
would hide neither, and F-20 must not be aimed at them.

**Not offered as F-20 input:** the whole-capture hinge-spread figures (withdrawn in §7 as not robust),
and anything from the §13a frozen capture.

---

## 15. Production readiness assessment

**Portrait integration: correct, and now proven on hardware.** Equivalent to F-18 at 0.000e+00 px on
intrinsics and 0.0001 mm on geometry, flagged off by default, one-flag rollback, zero Unity
`Runtime/` modifications, and it ran live for 24 minutes at 29.9 fps with P1-2 freshness intact.

**Unity retarget + VRM under live portrait input: good, within a bounded envelope.** Square stance is
excellent (0.012° median rendered yaw). Continuity is excellent (zero snaps > 10° in all 37 blocks,
worst p95 0.503°). The skeleton is rigid (0.0011 %, no scaling) and left/right never swaps. Loss and
recovery are graceful.

**Not ready for a public installation**, for reasons that are mostly not about portrait:

1. §19 person switching — the avatar follows the wrong person and does not hand back.
2. A sidecar restart freezes the mirror silently and permanently.
3. Elbows reach anatomically impossible angles in an everyday gesture.
4. **The shipped stereo configuration cannot meet the measurement gate** — enabling sub-pixel 1/8 is
   a production decision that has not been taken, and every live number above depends on it.
5. §17 generalisation across body sizes and clothing is untested.
6. Camera height is still not tape-measured.

```text
SUPPORTED:    full-body relaxed, arms 45 deg, normal movement, normal reaching, stepping,
              entry/exit, single user
LIMITED:      hands near face (impossible elbows), fast torso reversal (forearm snaps),
              T-pose and crouching (F-18 framing limits, not re-validated here)
UNSUPPORTED:  any multi-user environment; unattended operation without sidecar-restart recovery;
              reliable +/-90 deg torso orientation
```

### What the next task needs, in order

1. **Decide the sub-pixel question.** It is the difference between a 6.80° and a 0.85° yaw quantum.
   Either adopt `--subpixel-bits 3` in the shipping launch configuration with its own ADR and a
   re-measured performance budget, or accept that torso yaw cannot meet §20 at 0.90 m.
2. **Fix the P1-3 sequence-reset freeze** and surface `packetAgeMs` as a fault. One-line class of bug,
   installation-ending consequence.
3. **F-20 biomechanical layer** against the table in §14 — elbows first.
4. **Subject locking** for §19. This is a sidecar-side identity problem, not a retarget problem.
5. Re-run §19 and §17 with more people once 4 is in place, reading the **source depth stream**, not
   avatar telemetry.
6. Tape-measure the camera height.

---

```text
INTEGRATION DECISION:
Portrait input support is accepted - proved equivalent to the F-18 transform to 0.000e+00 px and
0.0001 mm, integrated behind a default-off flag with zero Unity Runtime modifications and one-flag
rollback, and validated live for 24 minutes at 29.9 fps with P1-2 freshness intact - but it runs on a
stereo configuration that production does not currently ship, and that gap must be closed by an
explicit decision rather than by a diagnostic flag.

AVATAR QUALITY DECISION:
The rendered VRM is visually and physically plausible for the supported envelope - 0.012 deg median
yaw on a square subject, zero snaps above 10 deg in all 37 blocks, bone lengths constant to 0.0011 %
with zero scaling and zero left/right swaps over 85,395 labelled rendered frames - with one attributable
defect: both elbows fold to 177-180 deg whenever the hands approach the face.

F-20 CONSTRAINT REQUIREMENTS:
An elbow flexion clamp at approximately 150 deg plus a hinge-axis constraint on the elbow bend
normal, with a secondary angular-velocity limit on forearm roll; the person-switching and
sidecar-restart failures are explicitly NOT constraint problems and F-20 must not be designed to mask
them.

NEXT ENGINEERING ACTION:
Decide whether sub-pixel 1/8 ships - every measurement in this report depends on it and the shipped
configuration has a 6.80 deg torso-yaw quantum at 0.90 m - then fix the P1-3 sequence-reset freeze
before any unattended deployment, and only then build F-20 against the failure table in section 14.
```
