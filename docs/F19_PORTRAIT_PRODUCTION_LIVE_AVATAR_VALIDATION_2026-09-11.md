# F-19 — Portrait Production Integration + Live VRM Validation

```text
VERDICT: CONDITIONAL
```

**Date:** 2026-09-11
**Scope of the verdict.** CONDITIONAL, not PASS, and not NO-GO.

* Portrait integration is **done**, behind a default-off flag, with **zero Unity `Runtime/` changes**,
  and proved equivalent to the F-18 transform over **10,322 real frames** (§3).
* The complete Unity pipeline **was** exercised — P1-3 → TrunkGate → V5 → V6 → Arm V2 → VRM — by
  replaying **22,029 rendered frames** of real recorded portrait capture, with per-frame rendered-bone
  telemetry (§6). The torso half of that replay runs on **measured stereo depth** and passes every
  §20 criterion it can reach.
* It is not PASS because the live half — camera, subject, second person — **could not be run at all**,
  and because the distal limbs in the replay run on assumed depth, so no arm or leg finding is
  attributable.

---

## 1. Executive conclusion

**What was delivered, and is finished:**

| F-19 section | status |
|---|---|
| §2–§4 production portrait integration behind a feature flag | **DONE** |
| §5 production-vs-diagnostic equivalence | **DONE — PASS, 5/5 checks, 10,322 real frames** |
| §6 downstream semantics unchanged | **DONE — proved, not asserted** |
| §13–§15 rendered-bone telemetry / bone-length / joint-angle instruments | **BUILT AND RUN — 22,029 rendered frames** |
| §10 square stance → square avatar | **DONE — PASS on measured depth** |
| §11 torso rotate left/right/centre; step; crouch; bend; reach | **DONE from recorded capture** |
| §14 bone-length stability | **DONE — PASS** |
| §12C/§12D temporal + cross-body | **DONE** |
| §24 production safety diff | **DONE — zero Unity `Runtime/` changes; one Python file, +42/−2** |

**What could not be run, and why — this is the whole of the verdict:**

| requirement | blocker |
|---|---|
| §7 physical camera levelling, height/tilt/roll measurement | no physical access to the rig |
| §8 camera preflight (portrait detect, FOV, depth, FPS, sync) | **no OAK-D attached** and **no `depthai` module installed** |
| §10 first live avatar test — square stance | needs the camera **and a human subject** |
| §11 whole-body motion battery | same |
| §12 human-quality audit on live motion | same |
| §17 public-user scenarios (heights, widths, clothing) | needs several people |
| §18 single-user follow/lose/re-enter | needs a person |
| §19 multi-user person-switching | needs **two** people |
| §20 acceptance criteria | every criterion is defined on live data |

Checked directly rather than assumed:

```
depthai:  ModuleNotFoundError: No module named 'depthai'   (sidecar .venv, Python 3.10.11)
USB:      no Movidius / Luxonis / MyriadX / OAK device present
           (only "Logi C270 HD WebCam", "Iriun Webcam" (Error), "EPSON L6270")
Unity:    Editor was not running at task start; once launched it stopped on a modal
          "Recovering Scene Backups" dialog and never bound its bridge
```

F-18 already flagged this exact gap as its largest untested area, and F-19 was written to close it.
**It is still open.** No number in this report is a substitute for it, and none is presented as one.

**The honest one-line summary:** the plumbing is built and proved; the experiment F-19 exists to run
has not been run.

---

## 2. Production changes

Exactly one production file changed.

```
$ git diff --stat -- Runtime/
(empty — no Unity production change at all)

$ git diff --stat            # python-sidecar~
 wholebody_udp_sender.py | 44 ++++++++++++++++++++++++++++++++++++++++++--
 1 file changed, 42 insertions(+), 2 deletions(-)
```

Four insertions, smallest viable footprint (§3 "prefer the smallest possible insertion point"):

1. **Import the verified transform** — `import f18_portrait as PORTRAIT`. Per §2 the F-18
   implementation is *used*, not re-derived. There is no second copy of the rotation maths anywhere.
2. **The feature flag** — `--portrait` (`BooleanOptionalAction`, **default `False`**) and
   `--portrait-dir` (`ccw`|`cw`, default `ccw`, the direction F-18 measured on this mount).
   Landscape remains the default and the rollback path; rollback is removing one flag.
3. **Setup** — rotate the intrinsics, swap the frame dimensions, print the orientation banner.
4. **Per frame** — rotate RGB and depth **identically**, immediately after they are pulled from the
   queues and before anything consumes them.

```python
frame = _rgb_pkt.getCvFrame()
depth = _depth_pkt.getFrame()
if args.portrait:
    frame = PORTRAIT.rotate_image(frame, args.portrait_dir)
    depth = PORTRAIT.rotate_image(depth, args.portrait_dir)
```

### 2a. One subtlety that would have silently corrupted every X

`read_rgb_intrinsics(device, dw, dh)` returns intrinsics in **depth-frame** pixels — `backproject`
scales `uv` into the depth frame and applies them there. So the intrinsics must be rotated against
`(dw, dh)`, **not** against the RGB dimensions. On this device both are 640×400 so the two give the
same numbers, but rotating against the wrong frame is a silent error that no visual check catches,
so production does it explicitly against the depth dimensions with a comment saying why.

### 2b. Startup log (§4)

```
[wb] ======================================================================
[wb] CameraOrientation = PORTRAIT_CCW          <- or LANDSCAPE
[wb]   rgb 400x640   depth 400x640
[wb]   intrinsics  fx=284.454 fy=284.627 cx=201.530 cy=319.789
[wb]   intrinsics(landscape, pre-rotation)  fx=284.627 fy=284.454 cx=319.211 cy=201.530
[wb]   FOV  H 70.22 deg  V 96.70 deg
[wb]   stereo: subpixel=on(1/8) LR-check=on align=CAM_A  surfaceDepth=True kwin=5
[wb]   working-distance target: 0.90 m (F-18 preferred)
[wb]   udp -> ('127.0.0.1', 8899)
[wb] ======================================================================
```

Discoverable (`--help`), logged at startup, off by default, and impossible to enable accidentally —
it requires an explicit command-line flag with no environment-variable or config-file path in.

**Not verified on hardware:** this banner's *values* come from the code path plus F-18's recorded
intrinsics. The banner has never been printed by a running device in this session, because there is
no device.

---

## 3. Portrait transformation equivalence (§5)

`f19_equivalence.py`, run against the transform code and the four F-18 captures on disk.

```
OVERALL: PASS
  PASS  intrinsics rotation
  PASS  pixel mapping
  PASS  semantics
  PASS  real recorded frames
  PASS  left/right identity
```

| check | result |
|---|---|
| **1 intrinsics** — production's rotation of the recorded landscape intrinsics vs the recorded portrait intrinsics, 4 captures | **worst component error 0.000e+00 px** |
| **2 pixels** — `rotate_image` vs `rotate_point`, and `unrotate_point` as its inverse, both directions, 7,410 probes | **0 px** mapping error, **0 px** round-trip |
| **3 semantics** — back-project a synthetic scene through the rotated frame + rotated intrinsics vs the unrotated path | **worst \|dXYZ\| 0.0001 mm**, **\|dZ\| 0.0000 mm**, both directions |
| **4 real data** — shoulder X/Z reconstructed from 10,322 recorded frames through the rotated intrinsics | **worst dx 0.0401 mm, dz 0.0000 mm; 0 frames** outside the storage-rounding box |
| **5 identity** — pixel ordering vs metric ordering of the two shoulders | **0 disagreements in 9,960 frames** (a mirrored rotation would give ~100 %) |

Acceptance from the brief was "position residual ≈ numerical precision, yaw residual < 0.01°". The
position residual is **0.04 mm**, at the capture's own storage quantisation. The yaw is reported
differently from the brief's phrasing, deliberately — see §3b.

### 3a. Why check 3 is the one that matters for §6

Rotating the image *alone* would leave every back-projected X wrong while still looking perfectly
correct on screen. Check 3 proves the composite: through the rotated frame **and** the rotated
intrinsics, the back-projector returns the same metric point, with **Z bit-identical** and X/Y rotated
with the camera exactly as a physical rotation about the optical axis demands. That is the §6 claim —
*portrait is a camera/input transformation, not a new pose protocol* — reduced to 0.0001 mm.

The UDP payload is untouched. Unity has no portrait flag, no new field, and no way to tell.

### 3b. Two corrections made to this test, recorded rather than quietly fixed

1. **The first version compared the wrong yaw convention** and reported a spurious ~90° residual.
   `line_yaw_deg` is not a plain `atan2`: it is `atan2(dx, dz)` followed by Kalidokit's
   `RollPitchYaw2` normalisation (÷π, wrap, +0.5) — the exact y-channel that reaches the avatar. The
   test now calls the shipped function. Verified by hand on one frame: dx = −290.62, dz = 19.0 →
   3.71°, against a recorded 3.740°.
2. **The first identity test was wrong too.** It asserted `uL > uR`, which fails legitimately whenever
   the subject turns far enough side-on for the shoulders to cross in the image — 2.67 % of frames,
   which is a real pose, not a defect. The test now compares *pixel ordering against metric ordering*,
   which is what a mirrored rotation would actually break. That gives 0/9,960.

After both corrections, the residual yaw is 0.0015–0.055°. That is **not** transform error: it is the
capture's own 0.01 px / 0.1 mm storage rounding, amplified where the shoulders are nearly coincident
(at |dx| ≈ 7 mm the yaw sensitivity reaches ~3° per mm of dx). The test now bounds each frame by the
rounding box the stored values could have come from and requires the recorded yaw to fall inside it —
**0 frames out of 10,322 fall outside**.

---

## 4. Camera preflight (§8)

```text
NOT EXECUTED
```

Every item in §8 — portrait detection, orientation, frame dimensions, intrinsics, FOV, depth validity,
left/right identity, torso-yaw sign, FPS, frame age, RGB/depth sync, Python processing time, UDP rate,
shoulder separation, square-stance yaw, ±180° inversion — requires the device.

Per §8's own instruction ("If preflight fails, stop there and record the failure. Do not continue to
avatar acceptance while the measurement path is invalid"), **no live avatar acceptance is claimed
below.** The preflight did not fail; it could not be started.

§7's physical work — measuring camera height, tilt and roll with a tape and levelling the mount — is
likewise not done. F-18 derived a **19–32° downward pitch** from imagery and explicitly asked for a
tape measurement. That request is still outstanding and is now blocking F-19 as well.

---

## 5. Live pipeline

```text
LIVE RUN: NOT EXECUTED — no camera, no subject
REPLAY  : EXECUTED — 24,047 rendered frames through the complete production Unity path
```

The pipeline was driven from the **recorded F-18 portrait captures** rather than a camera
(`f19_replay_portrait.py`). The payload is the production UDP contract, so everything downstream of
the wire ran exactly as in production: `OakDUdpPoseProvider` → P1-3 pose buffer → `TrunkGate` →
`TorsoYawGuard` (V6) → V5 relative composition → `ArmAimSolver` (Arm V2) → control rig → UniVRM
`Process()` → rendered bones. Configuration read back from the running driver:
`useOakUdpTracking=True`, `oakUdpPort=8899`, `kalidokitTorsoYawScale=1`, `kalidokitAimArms=True`,
`trackLegs=True`, `limbConfidenceThreshold=0.3`.

### 5a. What is real in this replay and what is not — the single most important caveat

F-18 stored keypoint **pixels**, the **shoulder depths** (`zL`, `zR`) and the **hip depth** (`hipZ`).
It did not store the depth frame, so per-joint depth for elbow, wrist, knee and ankle is
**unrecoverable**. The replay places those joints on the nearest measured trunk depth plane.

| layer | driven by | conclusions attributable? |
|---|---|---|
| torso (shoulder line, hip line, spine, chest, upperChest) | **measured stereo depth** | **yes** |
| bone length, bone scale, left/right identity, temporal continuity | avatar/retarget properties, independent of source depth | **yes** |
| elbow / knee joint angles, hinge plausibility, distal limb accuracy | **assumed planar depth** | **no** |

Every arm and leg number below is therefore reported as *not attributable*, and none of it is offered
as an F-20 input.

---

## 6. Rendered-bone results (§13)

### 6a. The instrument

A per-frame rendered-bone recorder, installed from `execute_code` as a closure on
`EditorApplication.update`, lifetime-controlled by a sentinel GameObject (`F19Recorder`). It adds
**nothing to `Runtime/`**.

It exists in this shape for a measured reason: an `execute_code` round trip blocks Unity's main
thread, so polling from outside advances the app **exactly one frame per sample** (measured in V4 as
`frameCount` deltas of 1 against ~0.86 s of wall clock). Every temporal metric §13 asks for would be
meaningless at that rate.

Recorded per rendered frame for all 20 humanoid bones: world position, local rotation, world
rotation, `lossyScale`, the driving source frame's trunk landmarks with confidences, the P0 limb-gate
states, `Time.frameCount`, `Time.time`, `Time.deltaTime`. Sampled via `Vrm10Instance.Humanoid` — the
**rendered skinned bones**, not the Animator, which on a control-rig instance resolves to the control
bones (the V2 report's §1a correction).

### 6b. Capture integrity

| run | source block | frames | fps | frame gaps | P0 gate held |
|---|---|---|---|---|---|
| `sq090` | `sq_a@090` ×3 (square stance, 0.90 m) | 2,434 | 149.9 | **1..1** | **0 %** |
| `torso_turns` | full `f18_torso_far` (sq, ±30/45/60/90 at 0.90 & 1.00 m) | 14,181 | 151.8 | **1..1** | **0 %** |
| `motion090` | full `f18_move_090` (step fwd/side, crouch, bend, reach, natural) | 5,414 | 127.3 | **1..1** | **0 %** |
| `armposes` | full `f18_frame_sweep` (relax / arms45 / tpose / overhead at 0.85/0.90/1.00 m) | 2,018 | 131.8 | **1..1** | **0 %** |

`frame gaps 1..1` means **every rendered frame was captured, none duplicated**. The P0 `LimbGate`
never held on any limb in any run, so nothing below was suppressed by the safety layer — every value
is what the full path actually produced.

---

## 7. Human-quality audit (§12)

### §12A impossible joint configurations — NOT ATTRIBUTABLE

Measured, and recorded only so the numbers are not lost: on `motion090` the left elbow reached
166.0° of fold with 13 frames past 155°, and the left knee reached 168.1° with 20 frames past 155°.
A human elbow tops out near 150–160° and a knee near 150°, so these *would* be impossible — but the
elbow and knee geometry in this replay comes from **assumed planar depth** (§5a). They are far more
likely to be artifacts of that assumption than of the retarget, and they are **not** carried into the
F-20 table.

The right knee never left 11.28° across 14,181 frames while the left knee moved. That asymmetry is
also an artifact: the replay drops keypoints below confidence 0.3, so a weakly-detected right leg
never forms a valid triangle and the solver leaves it at rest.

### §12B mesh deformation — NOT EXECUTED

Requires visual inspection of a live subject driving the avatar.

### §12C temporal defects — clean where attributable

Torso, across the three continuous captures (22,029 frames):

| run | avatar shoulder-line yaw range | step p95 | max | **snaps > 10°** |
|---|---|---|---|---|
| `sq090` | −0.00 … 0.00° | 0.000° | 0.001° | **0** |
| `torso_turns` | **−37.38 … +39.45°** | **0.364°** | **2.484°** | **0** |
| `motion090` | **−34.61 … +50.99°** | **0.564°** | **2.713°** | **0** |
| `armposes` | −25.24 … +0.58° | 0.004° | 13.136° | 1 — see below |

**Zero snaps above 10° in 22,029 frames of continuous capture**, against §20's "no visible snap > 10°"
and its preferred "p95 frame-to-frame torso rotation < 5°" (measured: **0.564°** worst).

The single 13.136° step in `armposes` is **an artifact of the replay, not a defect**, and was traced
rather than assumed. At that frame the *source* shoulder yaw jumps **−26.2° → +6.6° in one frame** —
a 32.8° discontinuity, because that run concatenates separate capture blocks and the subject was
re-posed between them. The avatar's response to that step input is a well-damped slew:
13.1 → 6.5 → 3.2 → 1.5 → 0.7 → 0.2°, converging in ~6 frames, which is exactly `lerpAmount = 0.5`.
Bone length is unchanged through it (0.005 %), so no rig event occurred.

### §12D cross-body errors — PASS

**0 laterally-crossed hands in 22,029 frames** across all runs. No left/right swap, no limb attaching
to the wrong side.

---

## 8. Bone-length stability (§14)

| run | worst deviation | worst \|lossyScale − 1\| |
|---|---|---|
| `sq090` | **0.0019 %** | **0.000000** |
| `torso_turns` | **0.0022 %** | **0.000000** |
| `motion090` | **0.0023 %** | **0.000000** |
| `armposes` | **0.0074 %** | **0.000000** |

Worst single chain anywhere: **0.0155 %** (`chest→upperChest`). Every limb chain — shoulder→elbow,
elbow→wrist, hip→knee, knee→ankle — holds to **≤0.0074 %**.

**PASS.** The skeleton never squashes, and the cause is attributed rather than assumed: `lossyScale`
is exactly 1.000000 on every tracked bone in every frame of every run, so the residual is float noise
in world-position differencing — not scale, not IK, not skinning, not parent rotation.

### 8a. An observation that needs an explanation before any public deployment

The avatar **changed between runs**. `sq090`, `torso_turns` and `motion090` all ran on a rig whose
left upper arm is **0.544948 m** (the "Anime Boy" chibi); `armposes` ran on one at **0.159846 m**; and
a check afterwards found a third state — `Vrm10Instance` meta **"Kory model"**, upper arm
**0.201021 m**, hips 0.836 m, head 1.272 m.

Within each run the length is constant to ≤0.0074 %, so **this is not skeleton squashing** and §14
still passes. But the loaded avatar changed at least twice inside one Play session.
`AvatarSessionController.LoadAsync` supports a runtime swap, so the capability is by design — what is
*not* established is what triggered it here. **No claim is made that it happened unprompted**; it was
not observed directly. For a mall/airport installation an avatar that can change mid-session without
an operator action would be a product risk, so this needs explaining before deployment. It also makes
cross-run length comparison invalid; per-run comparison is the valid one, and that is what is reported.

---

## 9. Joint-angle distributions (§15)

Collected; per §16 enforced nowhere. Attributable only for the torso.

Square stance, 0.90 m (`sq090`, 2,434 frames) — the relaxed baseline:

| joint | flexion p50 | p95 | max |
|---|---|---|---|
| L elbow | 7.53° | 8.25° | 8.79° |
| R elbow | 10.42° | 10.95° | 11.81° |
| L / R knee | 11.28° | 11.28° | 11.28° |

(0° = straight.) Sane relaxed-standing values; no joint folded past the 155° threshold.

**A correction made to this instrument rather than shipped.** The first version computed
`flex = 180 − angle(proximal, distal)`, which returns **180° for a straight limb** — so it reported
all 2,434 frames of a perfectly normal relaxed stance as "folded past 160°". It also measured the
hinge bend-normal on straight limbs, where `cross(u, f) → 0` and its direction is pure noise, giving a
meaningless ~115° "spread". Both are fixed: flexion is now `angle(proximal, distal)` with 0 = straight,
and the bend normal is measured only on frames bent past 15° — the same degeneracy `ArmAimSolver`
guards with `BendSinMin`.

---

## 10. Public-user tests (§17, §18)

```text
NOT EXECUTED — requires subjects of differing height, build and clothing
```

The replay contains **one** subject, so nothing about body-size generalisation, clothing, or
entry/exit behaviour can be claimed.

---

## 11. Multi-user tests (§19)

```text
NOT EXECUTED — requires a second person
```

No recorded capture contains two people, so person-switching cannot be touched offline. This remains
the single highest-risk unknown for a public installation, now unanswered across F-18 and F-19.

---

## 12. Performance

```text
LIVE: NOT EXECUTED
```

Unity rendered at **127–152 fps** while consuming the replayed 30 Hz stream, with every frame captured
and the P0 gate never holding. That is a Unity-side result only: the retarget and render keep up
comfortably. It says nothing about camera latency, RGB/depth sync or sidecar processing time, none of
which existed in this run, so no claim is made about P1-2 freshness.

The only portrait cost figure remains F-18's measured **0.26 ms/frame (0.78 % of a 33 ms budget)**.
F-19 adds no per-frame work beyond that same rotation.

---

## 13. Failure cases

**Observed in the replay:**

| # | observation | attributable? |
|---|---|---|
| 1 | avatar changed rig at least twice inside one Play session (upper arm 0.544948 → 0.159846 → 0.201021 m, meta "Anime Boy" → "Kory model") | **yes — needs explanation** (§8a) |
| 2 | left elbow folded to 166.0°, left knee to 168.1° — both past human range | **no** — assumed planar depth (§5a) |
| 3 | right knee frozen at 11.28° for 14,181 frames while the left moved | **no** — replay drops sub-0.3-confidence keypoints |
| 4 | one 13.136° torso yaw step | **no** — replay block-boundary step input; response is a correct damped slew (§12C) |

**Process failures, recorded because both cost real time:**

| failure | cause | fix |
|---|---|---|
| the live half of F-19 could not run | hardware and subjects assumed available; neither was | confirm device enumeration and subject availability **before** scheduling a live task |
| `depthai` missing from the sidecar `.venv` | the environment that ran F-18 no longer has it (`.venv` is Python 3.10.11 with numpy/onnxruntime/opencv only) | pin `depthai` in `requirements.txt`; verify `import depthai` in preflight |
| Unity Editor unreachable at task start | a modal "Recovering Scene Backups" dialog from a previous unclean shutdown blocks the main thread, so the bridge never binds | close the Editor cleanly at the end of a driven session |

**Two instrument bugs found and fixed here, not shipped:** the flexion sign (§9) and the yaw-convention
and identity tests in the equivalence harness (§3b). Both would have reported large false failures.

---

## 14. F-20 constraint requirements

The brief asks for a failure table to drive F-20. **F-19 produced no attributable joint failure**, so
the table stays empty — deliberately.

| Failure observed | Joint | Frequency | Severity | Constraint needed |
| ---------------- | ----- | --------: | -------- | ----------------- |
| *(none attributable — every out-of-range joint angle measured here traces to assumed planar depth, not to the retarget)* | — | — | — | — |

**F-20 must not start on this table.** A biomechanical constraint layer designed against artifacts of a
depth assumption would clamp joints that were never wrong and miss the ones that are. The prerequisite
is a live F-19 run with real per-joint depth.

What F-19 *does* hand F-20 is the instrument: `rec_start.cs.txt` + `f19_analyze_bones.py` already
produce exactly the §13–§15 telemetry F-20 needs, validated over 24,047 frames, and the hinge measure
is convention-free.

---

## 15. Production readiness assessment

**Portrait integration: correct, and untested on hardware.** In, flagged off, reversible by deleting
one flag, equivalent to F-18 to 0.000e+00 px / 0.0001 mm. It has never executed against a camera.

**Unity retarget + VRM under portrait-derived input: better than expected, within its limits.** Over
22,029 continuous rendered frames the torso follows real recorded turns across a **77° range** with
**zero snaps > 10°**, **p95 frame-to-frame 0.564°**, bone lengths constant to **0.0074 %**, zero bone
scaling, and **zero** left/right swaps. V5 + V6 + TrunkGate + Arm V2 behaved correctly on every frame
they were given, and the P0 gate never had to intervene.

**Avatar human-quality in portrait: still unknown**, and it is the thing F-19 existed to settle.

Standing F-18 limitations carried forward, **not** re-validated here:

```text
SUPPORTED:    full-body relaxed, arms 45 deg, normal movement, normal reaching, stepping
LIMITED:      T-pose (wrist span ~1.18 m vs 1.12 m coverage at 0.80 m), crouching
              (hands 80.1 % in frame at 0.90 m), extreme edge-of-frame
UNSUPPORTED:  reliable +/-90 deg torso orientation
```

### What the next attempt needs, in order

1. Attach the OAK-D and `pip install depthai` into `python-sidecar~/.venv` (pin it in
   `requirements.txt`).
2. Do §7 physically: tape-measure height, tilt and roll, and level the mount. F-18's derived 19–32°
   downward pitch has been an open request for two tasks.
3. `python f19_equivalence.py` — needs no camera, should still be 5/5.
4. Run the sidecar with `--portrait` and read the §2b banner back before anything else.
5. Do §8 preflight. Stop if it fails.
6. Arm the recorder (`rec_start.cs.txt`), run §10/§11 with a subject, then `f19_analyze_bones.py`.
   The torso thresholds this report already meets become the regression baseline.
7. Explain the §8a avatar swap before any public-facing claim.
8. Recruit a second person for §19.

---

```text
INTEGRATION DECISION:
Portrait input support is implemented behind a default-off flag with zero Unity Runtime changes and is
proved equivalent to the F-18 transform to 0.000e+00 px on intrinsics, 0 px on pixel mapping,
0.0001 mm on back-projected geometry and 0/9,960 on left/right identity, so the integration is
accepted as correct and remains untested on hardware.

AVATAR QUALITY DECISION:
Torso quality is ACCEPTED on recorded portrait data — 77 deg of followed turn, zero snaps above
10 deg, p95 frame-to-frame 0.564 deg, bone lengths constant to 0.0074 % with zero scaling and zero
left/right swaps over 22,029 rendered frames — while arm, leg and mesh quality remain UNDETERMINED
because no live subject and no per-joint depth were available.

F-20 CONSTRAINT REQUIREMENTS:
None may be specified yet: every out-of-range joint angle measured here traces to the replay's assumed
distal depth rather than to the retarget, so the failure table is empty by necessity and F-20 must not
be designed against it.

NEXT ENGINEERING ACTION:
Attach the OAK-D, install depthai into the sidecar venv, level and tape-measure the mount, then re-run
F-19 from the §8 preflight using the instruments built and validated here, treating this report's
torso numbers as the regression baseline.
```
