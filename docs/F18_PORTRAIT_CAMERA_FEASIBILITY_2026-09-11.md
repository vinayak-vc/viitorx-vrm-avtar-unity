# F-18 — Portrait Camera Feasibility + Full-Body Interaction Envelope

```text
VERDICT: CONDITIONALLY FEASIBLE
```

**Date:** 2026-09-11
**Scope:** evidence only. `git diff -- Runtime/` shows only the V6 changes from the earlier task. All
F-18 code is new `f18_*.py` diagnostic tooling; all data is new under `oak_v4_evidence/f18/`.

---

## 1. Executive conclusion

**Portrait mounting removes the blocker that F-16 and F-17 could not.** For the first time in this
programme, full-body framing and acceptable torso yaw hold **simultaneously**, on the existing
camera, with no new hardware:

| at 0.90 m | measured | target | |
|---|--:|--:|:--|
| full body, arms relaxed | **full-body-SAFE** | full-body-safe | **✓** |
| square-stance median \|yaw\| | **1.15°** | ≤ 5° | **✓** |
| square-stance p95 \|yaw\| | **2.33°** | ≤ 10° | **✓** |
| frame-to-frame p95 | **1.98°** | ≤ 5° | **✓** |
| depth quality | 0.95 | — | ✓ |
| V6 guard on the square blocks | **Valid 100 %**, zero wrap, zero range, zero held | no new problems | **✓** |
| cost of the rotation | **0.26 ms/frame (0.78 % of a 33 ms budget)** | no regression | **✓** |

**All four tested distances pass** the torso criteria — 0.78 / 0.80 / 0.90 / 1.00 m — and 0.90 m is
the best of them. Compare F-16's landscape result, where **no** distance at or beyond the
full-body threshold of 1.24 m passed anything.

**Why "conditionally".** Three things, none of which portrait can fix by itself:

1. **Arm spread is the new binding constraint, exactly as predicted.** Rotating swaps which axis
   gets 96.7° and which gets 70.2°, but **the smaller FOV always binds the larger body dimension**.
   Your measured T-pose wrist span is **~1.18 m**, and horizontal coverage is only 1.12 m at 0.80 m.
   T-pose is **clamped at the frame edge at every distance up to 0.85 m**, fits tightly at 0.90 m,
   and is safe only at 1.00 m. Arms at 45° (span ~0.84 m) are safe from 0.78 m.
2. **Crouching loses the hands** — 80.1 % in-frame at 0.90 m, the one interaction block that fails.
3. **Two brief sections could not be performed**: §13 multi-user (no second person available) and
   §17 avatar human-quality (needs a live portrait sidecar and the Unity Editor, which was not
   running). They are reported as not-done, not estimated.

Per §20, this establishes only that *the current camera can satisfy the full-body + torso-yaw
geometry simultaneously*. It is not a production-readiness claim.

---

## 2. Physical setup

The camera was physically rotated 90°; the stereo pair rotated with it. No cropping was used —
cropping a landscape frame to a portrait shape keeps the same 70.2° vertical FOV and would deliver
none of what this experiment depends on.

| | |
|---|---|
| rotation direction | **CCW**, auto-detected from the imagery (see §3) |
| portrait frame | **400 × 640** |
| effective FOV | **H 70.2°, V 96.7°** (measured from the rotated intrinsics) |
| portrait intrinsics | fx 284.454, fy 284.627, cx 201.530, cy 319.789 |
| stereo config | sub-pixel 1/8 (`sub3`), the F-16 recommendation |
| camera height above floor | **~0.81 m, derived** (no tape measurement supplied) |
| camera tilt | **not level — pitched downward**; see below |
| camera roll | shoulder-line tilt median **−1.17°**, p95 +0.77° over 1,080 square frames — subject posture and mount roll combined, small either way |

**Tilt, stated as a gap.** Physical measurements were requested and not supplied, so height and tilt
were derived from the imagery: back-project the ankle keypoints and add 70 mm for the ankle-to-sole
offset. A level camera gives the same answer at every distance. It does not:

| range source | median implied height | spread | slope | implied tilt |
|---|--:|--:|--:|--:|
| measured hip depth | 0.67 m | 0.22 m | +0.622 m/m | ~32° |
| span-derived range | **0.81 m** | 0.11 m | +0.338 m/m | ~19° |

The span-derived version is flatter, so part of the drift is hip-depth error rather than tilt — but
both show a real downward pitch. **This does not invalidate the measurements below**: framing margins
were measured directly in pixels rather than predicted from FOV, and a pure pitch leaves a
world-horizontal shoulder line at equal camera-Z, so the square-stance yaw is unaffected. A tape
measurement of height and tilt is listed in §13 as outstanding.

---

## 3. Coordinate transformation

This is the step that silently invalidates everything if it is wrong, so it was verified **before**
the camera was touched.

Nothing changes inside the device: CAM_B and CAM_C are still horizontally separated *in the sensor
frame*, so rectification, disparity and depth are computed exactly as production does. Only the host
sees a sideways picture. The host rotates RGB **and depth together**, and rotates the **intrinsics**
with them:

```text
CCW:  u' = v              fx' = fy      cx' = cy
      v' = W-1-u          fy' = fx      cy' = W-1-cx
```

Back-projecting with the rotated intrinsics yields X = left-right, Y = down, Z = depth — the same
DepthAI convention the sidecar already expects. **Rotating the image alone would have rescaled every
back-projected X and corrupted the yaw triangle.**

| check | result |
|---|---|
| pixel rotation round-trips | 0 failures over corner and interior cases |
| 3-D geometry preserved (image-plane radius) | **max error 2.8 × 10⁻¹⁷ m** |
| `rotate_point` vs `cv2.rotate` on a real array | exact match, both directions |
| FOV after rotation | 96.7° V / 70.2° H — correctly swapped |

**Rotation direction was not assumed.** The tool runs the pose model on both rotations and keeps the
one giving higher body confidence *and* head-above-hips:

```text
cw   body_conf 0.576   nose_y 287.8   hip_y 197.9   upright False
ccw  body_conf 0.812   nose_y 344.3   hip_y 477.2   upright True    <- selected
```

Left/right identity, depth axis and yaw sign are all verified preserved in §11.

---

## 4. Framing envelope

Closed-loop HUD protocol (F-16/F-17), auto-triggered inside ±6 cm. 7 distances × 4 arm poses,
~130–160 frames each, 3,814 frames total.

**Methodological point that changes the answer.** A pose model *clamps* a keypoint to the border
rather than dropping it, so "0 ≤ u < width" is not evidence a hand is in frame — a T-pose whose
hands are really outside shows up as wrists at u=6 and u=393 of a 400 px frame. **Every in-frame test
below requires a keypoint to sit ≥10 px from the border**, and edge-adjacent frames are counted
separately. Without that rule the T-pose rows would all have read "100 % in frame".

| distance | relaxed | arms 45° | T-pose | arms overhead |
|---|---|---|---|---|
| 0.70 m | full-body-tight | partial | partial | partial (top margin 6 px) |
| 0.75 m | **SAFE** | tight | partial | **SAFE** |
| 0.78 m | tight | **SAFE** | partial | **SAFE** |
| 0.80 m | **SAFE** | **SAFE** | partial | **SAFE** |
| 0.85 m | **SAFE** | **SAFE** | partial | **SAFE** |
| 0.90 m | **SAFE** | **SAFE** | tight | **SAFE** |
| 1.00 m | **SAFE** | **SAFE** | **SAFE** | **SAFE** |

**Measured spans** (converted from pixels at the measured range — not assumed from proportions):

| distance | relaxed | arms 45° | T-pose | T-pose fits? |
|---|--:|--:|--:|:--|
| 0.70 m | 452 mm | 773 mm | 824 mm* | NO (clamped) |
| 0.78 m | 500 mm | 801 mm | 983 mm* | NO (clamped) |
| 0.80 m | 545 mm | 843 mm | 985 mm* | NO (clamped) |
| 0.85 m | 589 mm | 957 mm | 1092 mm* | NO (clamped) |
| 0.90 m | 535 mm | 901 mm | 1068 mm | **YES** |
| 1.00 m | 548 mm | 842 mm | **1175 mm** | **YES** |

\* clamped, so these are lower bounds. The 1.00 m figure is unclamped and is the best estimate of the
true span: **~1.18 m**.

---

## 5. Torso measurement

Rotation set at four distances, 8 square blocks and 24 turn blocks, 5,325 usable frames.

| distance | median \|yaw\| | p95 | max | ff p95 | ≤5° | shoulder span | depth step |
|---|--:|--:|--:|--:|--:|--:|--:|
| 0.78 m | 3.61° | 5.34° | — | 2.63° | 72–98 % | 109 px | 3.0 mm |
| 0.80 m | 2.19° | 3.45° | — | 1.84° | 98–100 % | 116 px | 3.0 mm |
| **0.90 m** | **1.15°** | **2.33°** | — | **1.98°** | **100 %** | 106 px | 4.0 mm |
| 1.00 m | 4.74° | 8.26° | — | 2.01° | 9–91 % | 94 px | 5.5 mm |

Best individual block: **`sq_b@090` at 0.73° median, 1.64° p95, 100 % within 1°.**

**Are the §8 thresholds appropriate?** Requested explicitly. They are met with large margin at
0.80–0.90 m, so they do not bind here — but they were proposed engineering thresholds in F-16 and
**no product tolerance for torso yaw is recorded in `decisions.md`**. At 1.15° median the avatar's
torso is effectively still; whether the *product* needs better than 5° is still an unanswered
product question, not an engineering one.

**Turned blocks behave as F-15/F-16 established and portrait does not change it:** magnitude
saturates around 30–40° for commanded 30/45/60, and ±90° collapses (shoulder span falls to 11–25 px,
|Δx| to 22–63 mm). Portrait neither fixes nor worsens this.

---

## 6. Shoulder occlusion — the F-17 open question

F-17 flagged this as unmeasured: in portrait the stereo baseline is **vertical in world terms**, so
the shoulder line is perpendicular to it instead of parallel, changing the silhouette occlusion
geometry. Same subject, same sub-pixel 1/8 config, same analysis:

| condition | Δz | median \|yaw\| | p95 | far-shoulder window | near-shoulder window | far wider % |
|---|--:|--:|--:|--:|--:|--:|
| landscape 0.80 m | 0.0 mm | **0.51°** | 1.21° | 7.0 mm | 7.0 mm | 23.2 % |
| portrait 0.80 m | −13.0 mm | 2.36° | 3.92° | 14.0 mm | 10.0 mm | 72.7 % |
| landscape 1.00 m | 46.0 mm | 7.87° | 9.76° | 18.0 mm | 16.0 mm | 85.9 % |
| portrait 1.00 m | −26.0 mm | **4.57°** | 8.52° | 32.0 mm | 13.0 mm | 97.5 % |

**The answer is C — no consistent improvement — with a distance-dependent split, and it is not the
answer the portrait case needed to win on.** Portrait is *worse* at 0.80 m (2.36° vs 0.51°) and
*better* at 1.00 m (4.57° vs 7.87°, the only one of the four that changes a pass into a fail).

More interesting: the **occlusion-edge signature gets stronger, not weaker**. "Far wider %" — the
share of frames where the farther-reading shoulder also carries the wider depth window — rises from
23.2 % to 72.7 % at 0.80 m and from 85.9 % to 97.5 % at 1.00 m, with far-shoulder window spreads
roughly doubling. So rotating the baseline **changes the character of the matching error without
removing it**. Portrait wins on *framing*, not by curing the F-16 defect.

---

## 7. Rotation sign validation

The question is whether rotating the camera inverted the yaw semantics. Commanded heading is not
angular truth (F-15 rule), so the test is what the geometry must satisfy:

| check | result |
|---|---|
| **(a) handedness preserved** | portrait square uL 264.2 > uR 163.0, signed Δx **−316.8 mm**. F-16 landscape: uL 360.9 > uR 289.8, Δx **−345.4 mm**. Same ordering, same sign — **no mirroring**. |
| **(b) no wrap introduced** | square \|yaw\| median across all 8 square blocks = **2.89°**, not ~180°. |
| **(c) left/right separate with opposite signs** | **10 of 12** distance×angle pairs separate correctly. |

The 2 failures are `±30` and `±45` at 0.78 m — the **first two turn blocks of the session**, where
the subject turned the same way twice before settling. From `left60@078` onward all 10 remaining
pairs separate cleanly.

**Observed polarity differs from F-16** — "left" gave negative yaw here, positive there. **That is
not the camera.** Handedness and Δx sign are identical in both sessions, and depth cannot invert
under an image rotation. What flipped is the subject's Δz for the same word: F-16 `left30` Δz =
**+231 mm**, F-18 `left30` Δz = **−87 mm**. The subject turned the other way for the same prompt —
precisely why F-15 forbids treating a commanded heading as truth.

---

## 8. Public interaction envelope

At 0.90 m, ~200 frames per action:

| action | head | feet | hands | verdict |
|---|--:|--:|--:|---|
| step forward / back | 100 % | 100 % | 100 % | **full-body-SAFE** |
| step left / right | 100 % | 100 % | 100 % | **full-body-SAFE** |
| bend forward | 100 % | 100 % | 100 % | **full-body-SAFE** |
| reach to one side | 100 % | 100 % | 100 % | **full-body-SAFE** |
| natural movement | 100 % | 100 % | 100 % | **full-body-SAFE** |
| **crouch** | 100 % | 100 % | **80.1 %** | **partial-body** |

Crouching is the single failing interaction: the hands drop toward the bottom border. Everything
else a mirror user would plausibly do stays fully framed.

---

## 9. V6 behaviour (observation only)

No threshold was modified. The Unity Editor was not running, so the shipped C# could not be invoked
as it was for V6's own validation. Instead `TorsoYawGuard.cs` was ported line-by-line and the port
was **verified rather than trusted** — replayed over the exact 20,783 frames V6 published figures
for:

```text
port      : held 896 (4.31 %), 11 runs, longest 314 frames = 12.31 s
V6 report : held 896 (4.31 %), 11 runs, longest          = 12.33 s
PORT VERIFIED
```

**Caveat, stated rather than buried:** F-18 logs the shoulder yaw but not a hip yaw (hip depths were
not retained), and TrunkGate's `Fresh` flag is not reproduced offline. The replay feeds
`hipYaw = shoulderYaw` and `trunkFresh = true`. That answers exactly what §16 asks — whether the
portrait yaw stream triggers new wrap/range guarding — but cannot measure TrunkGate-driven holds, so
hold figures are a **lower bound**.

| group | n | Valid | OutOfRange | WrapGuarded | max step | held |
|---|--:|--:|--:|--:|--:|--:|
| all portrait | 6,508 | 92.30 % | 3.75 % | 3.95 % | 120.0° | 3.95 % |
| at 0.78 m | 1,314 | 89.95 % | 5.48 % | 4.57 % | 120.0° | 4.57 % |
| at 0.80 m | 1,304 | 83.28 % | 10.35 % | 6.37 % | 120.0° | 6.37 % |
| at 0.90 m | 2,533 | 94.04 % | 1.46 % | 4.50 % | 120.0° | 4.50 % |
| at 1.00 m | 1,357 | **100 %** | 0 % | 0 % | 81.4° | 0 % |
| **square blocks** | 1,080 | **100 %** | **0 %** | **0 %** | **14.2°** | **0 %** |
| interaction @0.90 m | 1,183 | **100 %** | 0 % | 0 % | 55.0° | 0 % |

**Portrait creates no new wrap/guard problem.** The longest hold across the whole portrait stream is
**1.11 s** against V6's 12.33 s on the landscape corpus, and the square and interaction blocks
trigger the guard **zero times**. Wrap/range activity is confined to the ±90° turn blocks, which are
known-unsolved and out of the operating envelope by design.

---

## 10. Performance

| | |
|---|--:|
| **direct cost of the rotation** | **0.2584 ms/frame** (RGB 0.2291 + depth 0.0294) = **0.78 %** of a 33.3 ms budget |
| portrait capture harness | 21.6 fps, camera latency p50 133 ms |
| F-16 landscape capture harness, same sub3 config | latency p50 **153–154 ms** |
| F-16 landscape capture, `baseline` config | 30.0 fps, latency p50 14.7 ms |

**The apparent regression is not the rotation.** The often-quoted "31.2 fps / 24 ms" F-16 figure came
from `f16_config_sweep.py`, which reads **depth only and runs no pose model** — not a comparable
number for a capture harness. Against the like-for-like landscape capture at the same stereo config,
portrait is **21 ms faster**. The 133–154 ms figures are dominated by host-side queue backlog
(`maxSize=4` at ~21 fps against a 30 fps camera ≈ 130 ms), a property of the diagnostic harness, not
of orientation; production uses the P1-2 latest-frame policy precisely to avoid it.

Rotation is two `cv2.rotate` calls on a 640×400 frame. There is no pipeline change and no device
change.

---

## 11. Decision matrix

| test | 0.78 m | 0.80 m | 0.90 m | 1.00 m |
|---|--:|--:|--:|--:|
| full body (relaxed) | tight | **SAFE** | **SAFE** | **SAFE** |
| arms-45 in frame | yes | yes | yes | yes |
| T-pose in frame | no | no | tight | **yes** |
| square yaw median | 3.61° | 2.19° | **1.15°** | 4.74° |
| square yaw p95 | 5.34° | 3.45° | **2.33°** | 8.26° |
| shoulder span | 109 px | 116 px | 106 px | 94 px |
| depth quality | 0.96 | 0.97 | 0.95 | 0.93 |
| frame-to-frame p95 | 2.63° | 1.84° | **1.98°** | 2.01° |
| V6 held % | 4.57 % | 6.37 % | 4.50 % | 0 % |
| **overall** | **PASS** | **PASS** | **PASS** | **PASS** |

**Selected operating distance: 0.90 m.** Best torso accuracy of the four, full-body-SAFE relaxed,
T-pose at least tight, all interaction actions except crouch fully framed, and the largest
left/right zone of the passing distances (±0.63 m).

**1.00 m is the alternative** if T-pose must be fully safe: it is the only distance where T-pose is
SAFE, at the cost of torso median rising 1.15° → 4.74° and p95 to 8.26° — still inside target, but
with much less margin.

---

## 12. Installation recommendation

```text
                              SCREEN
                                 |
                                 |
      CAMERA (portrait)          |
      ~0.81 m above floor,       |
      pitched slightly down      |
            [|]                  |
             |                   |
             |<---- 0.90 m ----->|
             |                   |
                   USER          |
                     O           |
                    /|\          |
                    / \          |

   user zone at 0.90 m:  vertical coverage 2.02 m   horizontal 1.27 m (+-0.63 m)
   arms 45 deg           safe
   T-pose                tight - hands within ~10 px of the border
   crouch                hands leave frame
```

| | |
|---|--:|
| camera height | ~0.81 m (derived; **needs a tape measurement**) |
| camera orientation | portrait, CCW |
| user standing distance | **0.90 m** |
| floor interaction zone | ±0.63 m left/right, ±~0.10 m fore/aft before framing degrades |
| head clearance | 168 px ≈ 0.53 m above the topmost keypoint |
| foot margin | 69 px ≈ 0.22 m below the ankles |

---

## 13. Remaining engineering work

Per §20 — portrait establishes the geometry, not production readiness.

**Not performed in F-18, and not estimated:**
1. **§13 multi-user.** No second person was available. Whether portrait framing lets the system
   identify one intended participant is untested.
2. **§17 avatar human-quality.** Requires a live portrait sidecar and the Unity Editor, which was not
   running. No observation of torso twisting, arm distortion, avatar jitter or mirroring on the real
   VRM was made. **This is the most important gap** — everything here is measured at the landmark
   level, upstream of the rig.

**Measured gaps:**
3. **T-pose does not fit below 0.90 m** and is only tight at 0.90 m. If the product needs arms fully
   spread with margin, the distance must move to 1.00 m and torso accuracy gives up most of its
   margin.
4. **Crouch loses the hands** (80.1 % at 0.90 m).
5. **Camera is not level** — a downward pitch of roughly 19–32° is implied by the imagery. A tape
   measurement of height and tilt, and a re-run on a level mount, would tighten every geometric
   figure here.
6. **The portrait rotation is not in production.** The sidecar would need the rotation and the
   rotated intrinsics; F-18 changed nothing. That is its own task with its own live validation.
7. **±90° is unchanged** — still collapses (span 11–25 px). Portrait does not address it.
8. **One subject, one session.** Shoulder separation 333.1 mm, arm span ~1.18 m. A taller user, or
   one with a wider span, shifts every framing threshold; the §4 table should be re-measured against
   the largest user the installation must accept.
9. **The occlusion asymmetry got worse, not better** (§6) — portrait wins on framing, and the F-16
   matching error is still there underneath.

---

## Evidence

Under `oak_v4_evidence/f18/`:

| file | contents |
|---|---|
| `f18_frame_sweep.jsonl` | 7 distances × 4 arm poses, 3,814 frames, full COCO-17 keypoints |
| `f18_torso_near.jsonl`, `f18_torso_far.jsonl` | rotation set at 0.78/0.80 and 0.90/1.00 m, 5,325 usable frames |
| `f18_move_090.jsonl` | 6 interaction actions at 0.90 m, 1,184 frames |
| `f18_analysis_2.txt` / `.json` | framing envelope, torso, occlusion, signs, decision matrix |
| `v6_observation.txt` | V6 guard replay + port verification |
| `v6_replay_input.csv` | the 6,508-sample portrait yaw stream |
| `probe_landscape_control.jsonl` | rotation-detection control taken before the camera was moved |

Tooling (all new, all diagnostic): `f18_portrait.py`, `f18_capture.py`, `f18_analyze.py`,
`f18_v6_replay.py`.

---

```text
MEASUREMENT DECISION:
In portrait orientation the existing OAK-D-PRO-W-97 measures a square-standing subject's torso yaw
to 1.15 deg median / 2.33 deg p95 at 0.90 m while framing the whole body safely - the first
configuration in this programme where full-body framing and acceptable torso yaw hold at the same
time.

INSTALLATION DECISION:
Adopt portrait orientation at a 0.90 m user distance with the camera at ~0.8 m and levelled, a
+-0.63 m left/right user zone, and arms-45 as the supported interaction envelope; T-pose and crouch
are outside it.

NEXT ENGINEERING ACTION:
Port the rotation and rotated intrinsics into the sidecar behind a flag and run the live avatar
session that F-18 could not - the VRM human-quality check and multi-user behaviour are the two
things still entirely unmeasured, and both sit downstream of every number in this report.
```
