# F-23 — Driving the production avatar from an ordinary video, and what it can and cannot prove

**Date:** 2026-09-15 **Status:** EVIDENCE ONLY — production tracking untouched.
**Related:** ADR-058, ADR-059, ADR-060. Supersedes nothing.

---

## 1. Why this exists

A milestone review asked two questions: *how much work remains to a production-grade virtual
mirror*, and *does the avatar do what the person in a dance video does*. The second is answerable
today with no camera and no second person, so it was answered.

It is worth being precise about what it tests, because the obvious reading is wrong.

```text
REAL   RTMW3D at the production confidence gate; R.center_bbox / R.bbox_from_keypoints (the M15
       crop loop); build_body_landmarks() unmodified; the wire payload and UDP port; and everything
       downstream - P1-3 buffer, TrunkGate, V5 torso, V6 guard, Arm V2, VRM retarget.

NOT    THERE IS NO STEREO DEPTH. A webm has one camera. Every keypoint is UNMEASURED and placed by
       _fallback_point() from RTMW3D's own monocular root-relative z, so every `src` flag on the
       wire reads 0 and the payload declares its own provenance. The intrinsics of the source phone
       are unknown and estimated; the hip plane is chosen to make the subject a plausible height.
       Both only SCALE the pose - neither invents articulation.
```

So this exercises **the retarget and the avatar**. It does not exercise the torso-yaw signal, which
F-16/F-17/F-18 measured as the actual blocker. An avatar that looks right here says nothing about
whether the SENSOR can resolve torso yaw at a usable distance.

## 2. Tooling

```text
f23_video_to_unity.py    video -> RTMW3D -> production landmark builder -> UDP 8899
f23_dance_demo.py        records the Unity window while the clip drives it
f23_compose_sbs.py       side-by-side, with a game-view guard (see S6)
f23_followance.py        the objective follow metric (S4)
```

## 3. Three defects in the harness, found by a user watching the video

All three were in the NEW bridge, not in the product. Recorded because each produced a confident,
plausible-looking result that was wrong, and because two of them made the PRODUCT look broken.

| # | Defect | Symptom | Cause |
|---|---|---|---|
| 1 | `build_body_landmarks` called positionally | avatar's trunk completely rigid | its SIGNATURE default is `flatten_trunk=True`; **production's argparse default is `False`** since Milestone-2. Ten positional args silently took the wrong one, zeroing hip AND shoulder Z |
| 2 | `xyz` sent as `[0, 0, z]` | hip sway never reached Unity | production sends the measured mid-hip X/Y/Z as the avatar ROOT. Measured loss: **388 mm peak-to-peak** of real hip translation |
| 3 | no temporal filter on `zrel` | avatar swung into PROFILE | production runs `body_smoother.filter()` BEFORE `build_body_landmarks`. Milestone-2 could turn `--flatten-trunk` off *because* the trunk is depth-smoothed; skipping that stage recreated the exact defect ADR-023 describes |

Effect of fixing #3, with production's own trunk parameters (`min_cutoff 0.5 / beta 0.4`):

```text
hip-line dZ std         0.0282 m -> 0.0147 m
hip yaw std                7.4 deg -> 3.9 deg
frame-to-frame yaw p95     8.2 deg -> 1.6 deg
```

**The lesson worth keeping:** a harness that calls a production function positionally inherits that
function's signature defaults, which are not the same thing as the product's configured defaults.
Pass behaviour-changing arguments explicitly.

## 4. The follow metric, and the result

Judging "does the avatar do the same thing" by eye is exactly what this project's rules distrust, so
one scalar was defined identically on both sides and cross-correlated.

```text
SIGNAL   normalised arm span - wrist-to-wrist over shoulder width. Scale-free, so the avatar's
         proportions and the dancer's need not match; and it is the most active DOF in the clip.
INPUT    computed from the payloads actually sent on the wire (the tracker's opinion of the dancer)
OUTPUT   measured from the RECORDED PIXELS of the game view, by background-subtracting a static
         median plate. Deliberately from what was DRAWN: a bone log proves the retarget emitted
         something, not that a viewer saw it.
```

```text
RESULT   peak Pearson r = +0.714, avatar trailing by 9 frames (300 ms)
         435/435 frames delivered, Unity reported parseErr=0
         inference 29.6-36.7 fps on the DirectML path
```

A single clean correlation peak: the avatar tracks the dancer rather than moving plausibly but
independently. **~53 % of variance is unexplained** — arms diverge on the fastest moves, consistent
with the documented LIMITED case (fast torso reversal, forearm snaps).

**A first attempt at this metric returned a constant output signal and a meaningless correlation.**
It thresholded saturation on the assumption that the game view's checkerboard was neutral grey; it
is not (median S = 56). Replaced with background subtraction, the technique `f21_ground_truth.py`
already uses. The first number it produced looked plausible.

## 5. Why the hips looked dead — the product part

After the harness defects were fixed, pelvic motion was still largely absent. The pelvis has three
rotational axes and one of them is live:

```text
ApplyBone(hips, new Vector3(0f, hipsYaw, hipsRoll * HipsRollDamp))
  pitch  hardcoded 0f
  yaw    through the 8-22 deg SmoothStep deadzone
  roll   torsoRoll = 0 in Bootstrap.unity -> always zero
```

Measured against this dancer:

```text
pelvic obliquity (tilt)   25.1 deg p2p   -> DISCARDED ENTIRELY (torsoRoll = 0)
pelvic twist (yaw)        74.8 deg p2p, std 7.4 deg
    76.6 % of frames zeroed completely (|yaw| <= 8 deg)
     3.0 % reach full gain (>= 22 deg)
    33.9 % of the rotation present in the data actually reaches the avatar
hip sway                 388 mm p2p      -> was not being sent at all (defect 2)
```

Roll is re-enabled by ADR-059 (image-plane derived, therefore safe, and it transfers to the OAK-D).
The deadzone is NOT changed here: it is blocked on a live noise-floor measurement (S7).

## 6. A live finding, unrelated to the video path

Reviewing a recorded preview — a capability that did not exist before this session — the tracker was
seen **locked onto an empty office chair** at `conf = 0.38` against a `min_confidence` floor of
`0.30`, with a person standing clearly in frame. In 45 s that produced 3 epochs, 2 switches and 122
position-jump rejections.

This is upstream of F-21 (the detector emitting a low-confidence body on furniture), and no
threshold was changed in response. It matters operationally: **an F-21 rep whose ANCHOR lands on
furniture is marked valid and every classification in it is meaningless.** Clear chair-like objects
from the capture volume before an ownership session.

## 7. What this does NOT answer, stated plainly

The obvious inference — *make the avatar match the video and the OAK-D will follow* — is **false for
the channel that matters**, and this session produced the evidence:

| Channel | Derived from | Transfers to OAK-D? |
|---|---|---|
| pelvic roll / tilt | `Find2DAngle(a.x, a.y, b.x, b.y)` — image plane | **yes** |
| arms, legs, hands, bone mapping | 2D keypoints + solver | **yes** |
| pelvic yaw / torso twist | shoulder and hip **dZ** | **no** |
| waist bend | trunk Z | **no** |

The dancer's real pelvic twist has **std 7.4 deg**. The V4-era OAK-D torso-yaw REST JITTER — the
yaw reported while a subject stands still — is **5.4-7.6 deg**. On the depth channel the signal and
the noise are the same size. Tuning the video path to look perfect means removing the deadzone, and
on the OAK-D that re-admits the jitter the deadzone exists to suppress.

Worse, this was demonstrated rather than argued: with **perfect, noise-free monocular depth**, hip
yaw was either unstable enough to throw the avatar into profile, or — once smoothed enough to be
stable — mostly below the 8 deg gate. There is no setting that yields clean AND visible pelvic twist
from this depth source. The hip line is only ~0.21 m wide, so any depth error becomes a large angle.
That is geometry, not tuning.

**The lever is depth resolution, i.e. the sub-pixel 1/8 decision already on the roadmap** (torso-yaw
quantum 6.80 deg -> 0.85 deg at 0.90 m). Lower the noise floor and the deadzone can come down
honestly.

## 8. VERDICT

```text
F-23: EVIDENCE ONLY. No production tracking change.

PROVEN  the production retarget chain drives a VRM avatar from a real human in real footage at
        r = 0.714, 435/435 frames, parseErr = 0. Retarget, arm solver, legs, hands and pelvic roll
        all exercised.
FIXED   pelvic roll re-enabled (ADR-059) - 25.1 deg p2p that was being discarded by a config value.
MEASURED the avatar/skeleton gap is STRUCTURAL (gating + missing DOFs), not damping (ADR-060).
NOT PROVEN anything depth-derived. Torso and pelvic yaw remain gated by sensor resolution, and this
        path has no stereo at all.
NOT TESTED the live trunk-yaw noise floor. One attempt recorded 2565 frames of an empty room
        tracking something that was not the subject, and is discarded. It needs ~30 s of a person
        standing still at 0.90 m and is the last thing gating the deadzone decision.
```
