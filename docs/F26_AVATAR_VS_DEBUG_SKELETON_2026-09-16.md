# F-26 — Avatar vs Debug Skeleton: why the skeleton is right and the VRM is not

```text
VERDICT: OBSERVATION REPORT. No code change, no decision taken.
SOURCE:  Assets/Games/video/LatestScreenReording.mp4  (1918x1078, 30 fps, 129.1 s, 3873 frames)
STATUS:  the retarget — not the tracking — is the dominant error source for POSE FIDELITY
```

> **One-line summary.** The debug skeleton sets 33 joint **positions** directly from the tracker. The
> VRM avatar has to turn those same 33 points into ~16 bone **rotations** on a body with different
> proportions, through a solver, four gates, three conditioners and a dead-zone. The skeleton is
> right because it is not solving anything. The avatar is wrong because every stage of that
> conversion is lossy, and several stages are lossy **by design**.

---

## 1. What was examined, and what this report is not

Eleven frames sampled evenly across the recording (t = 8, 20, 32, 44, 56, 68, 80, 92, 104, 116, 124 s)
plus four detail frames, all preserved under `docs/evidence/F26_avatar_vs_skeleton/`. Every claim
below is either visible in those frames or read out of the code, and the two are kept apart.

**This is not a measurement report.** Nothing here has a number attached to a live capture, because
no instrument currently measures the thing in question (§6 explains why that is itself the finding).
It is an observation report and a code audit. Where it says "wrong", it means *demonstrably different
from the subject in the same frame*, judged by eye against the camera feed in the same screenshot.

---

## 2. Observations, frame by frame

| t (s) | Subject (camera feed) | Debug skeleton | VRM avatar | Agree? |
|---|---|---|---|---|
| 8 | standing, arms down | standing, arms down | standing, arms down | **yes** |
| 20 | bending / reaching | deep bend, legs wide | crouched, one arm up | partial |
| 32 | arms slightly out | arms out horizontal | arms slightly out | partial |
| 44 | standing at rest | standing, arms down | standing, arms down | **yes** |
| 56 | standing at rest | standing | standing | **yes** |
| 68 | **both arms raised overhead** | **arms up, elbows bent, hands above head** | **arms only reach ~horizontal** | **NO** |
| 80 | arms partly out | arms out | standing, arms near body | partial |
| 92 | standing at rest | standing, arms down | standing, arms down | **yes** |
| 104 | arms up near head | arms up | arms up near head | partial |
| 116 | **sitting in a chair** | **seated: thigh horizontal, knee bent** | **upright, running/lunging pose, floating** | **NO** |
| 124 | seated, leaning | seated / crouched | upright, arms bent up | **NO** |

**The pattern is unambiguous and it is monotonic:** the further the subject departs from a neutral
standing pose, the worse the avatar diverges, while the skeleton keeps tracking. At rest they agree.

Two frames carry the whole finding:

* **t = 65–68 — arms overhead.** The skeleton puts the hands above the head with bent elbows,
  matching the subject. The avatar's arms stop at roughly shoulder height. *Arm elevation is being
  lost.* (`t065_arms_overhead_avatar_horizontal.png`)
* **t = 115–124 — seated.** The skeleton lays out a seated figure. The avatar **stays upright**, in a
  lunging stance, floating above the floor. *The avatar never leaves a standing biped.*
  (`t115_seated_avatar_upright.png`)

A third, subtler one: at **t = 40** the subject's feet are apart; the avatar's legs splay wider than
the subject's with the feet rotated outward. Leg yaw is being over-applied or mis-axised.

---

## 3. The complete comparison

| | Debug skeleton | VRM avatar |
|---|---|---|
| **What is set** | joint **positions** | bone **rotations** (+ one root translation) |
| **Code that drives it** | `PoseDebugSkeleton.Render()` — `joints[i].localPosition = landmark.Position * scale` | `KalidokitControlRigDriver` (1106 lines) + `ArmAimSolver` + `KalidokitPoseSolver` + `HumanoidPoseRetargeter` |
| **Degrees of freedom** | **99** (33 landmarks × 3), every one independent | **~48** rotational (16 driven bones × 3) + 30 finger curls + **3** root translation |
| **Positional DOF** | 99 | **3** (root only). No joint can be *placed*; only aimed. |
| **Bone lengths** | none — dots connected by lines that stretch freely | **fixed by the mesh**, and deliberately held constant (F-19 measured 0.0011 % variation) |
| **Proportion handling** | irrelevant — no bones | the subject's limb ratios are **discarded**; the avatar's own are used |
| **Solver** | none | Kalidokit pose solve + a bespoke quaternion arm-aim solver |
| **Gates** | none | `LimbGate` (per-limb hold), `TrunkGate`, `TorsoYawGuard` (150° wrap), F-22 confidence zeroing upstream |
| **Conditioning** | none | yaw rate-limit → low-pass → **soft dead-zone 8°–22°**; spine-bend rate-limit 250 °/s + baseline high-pass; per-bone dampers (Hips 0.7 / Spine 0.45 / Chest 0.25) |
| **Temporal smoothing** | none | global slerp `kalidokitBodyLerp = 0.5` (≈33 ms lag, measured ADR-060) |
| **Behaviour on low confidence** | joint simply **hidden** | limb **frozen in its last good rotation** (`LimbGate.Held`) |
| **Failure appearance** | a missing dot — obviously absent | a **stale limb in a plausible-looking wrong pose** — not obviously wrong |
| **Root/world orientation** | n/a | **not driven.** `ApplyRootPosition()` *translates only*; the body cannot tilt or lie down |
| **Latency** | one frame | one frame + slerp + conditioners |

---

## 4. Where the avatar is failing, and the mechanism for each

### 4.1 Arm elevation is lost (t = 68)
The arm path is a *rotation aim*: the upper arm is rotated to point at the tracked elbow, the forearm
at the tracked wrist. Because the avatar's arm length is fixed and its shoulder pivot sits in a
different place from the subject's, an aim that is angularly correct still lands the hand somewhere
else — and any clamp in the shoulder's reachable cone truncates the top of the range first. The
**hand position is never used as a target**; it is only used to derive a direction.

### 4.2 The avatar cannot sit, lie, or leave vertical (t = 116, 124)
This is the largest failure and it is structural. `ApplyRootPosition()` only sets
`avatarRootTransform.position`. **Nothing drives the root's rotation.** Hips *yaw* is driven; a spine
*bend* is derived and applied as pitch to Spine/Chest — but it is passed through a 250 °/s rate limit,
a baseline high-pass that treats a sustained bend as drift to be corrected away (`spineBendBaselineTau
= 8 s`), and a low-pass. A person sitting down produces exactly a *sustained* trunk pitch, which is
the signal that high-pass is designed to remove. The avatar therefore returns to upright.

### 4.3 Legs splay and the feet rotate out (t = 40)
Legs are driven (`kalidokitBodyLegs = 1`) as `UpperLeg`/`LowerLeg`/`Foot` rotations from the
Kalidokit solve. There is no hip-width normalisation between subject and avatar, and no foot-contact
or floor constraint, so leg yaw error is expressed directly as splay.

### 4.4 A held limb looks worse than a missing one
`LimbGate` holds the last valid rotation when confidence drops. That is the *correct* engineering
choice (never emit a fabricated position), but visually a frozen arm in an old pose reads as "the
avatar is wrong", whereas the skeleton's response — hide the dot — reads as "no data". **The same
underlying event produces a small visual cost on the skeleton and a large one on the avatar.**

### 4.5 Torso rotation largely never arrives
The yaw dead-zone is `8°` (below → forced frontal) to `22°` (above → full). Previously measured: the
dead-zone **zeroes 76.6 % of frames**, and only **33.9 % of pelvic rotation reaches the avatar**.
That was a deliberate trade against sensor noise (ADR-027/ADR-037), not an oversight — but it means
the avatar is *designed* not to reproduce most torso rotation.

---

## 5. What previous data does **not** support

This is the uncomfortable section, and it is the most important one.

### 5.1 F-19's "AVATAR QUALITY: PROVEN GOOD" does not mean what it has been used to mean

F-19's actual wording:

> *"The rendered VRM is visually and physically plausible for the supported envelope — 0.012 deg
> median yaw on a square subject, zero snaps above 10 deg in all 37 blocks, bone lengths constant to
> 0.0011 % with zero scaling and zero left/right swaps over 85,395 labelled rendered frames."*

Every one of those metrics is a measure of **internal consistency**, not of **correspondence to the
subject**:

| F-19 metric | What it proves | What it does *not* prove |
|---|---|---|
| 0.012° median yaw | the avatar is **stable** | that the yaw is **correct** |
| zero snaps > 10° | it does not **jump** | that it is in the **right pose** |
| bone lengths constant to 0.0011 % | the retarget is **rotation-only, no scaling** | anything about pose fidelity |
| zero left/right swaps | the **mapping** is consistent | that the mapping is **accurate** |

**All four numbers are fully compatible with an avatar that is smoothly, stably and consistently in
the wrong pose** — which is precisely what the video shows. F-19 is not falsified; it measured what
it said it measured. What is wrong is the *inference* drawn from it in the roadmap
(`AVATAR QUALITY — PROVEN GOOD`), which has been carried forward as though pose fidelity were
established. It was not, anywhere.

### 5.2 The envelope caveat was load-bearing and got dropped

F-19 says "**for the supported envelope**" and "**on a square subject**". The supported envelope is a
person standing, square to the camera, at ~0.90 m. Everything in this video that fails —
arms overhead, seated, turned — is **outside** that envelope. The finding is not that F-19 was wrong;
it is that **no evidence has ever existed for the poses a real user actually adopts**, and the
qualifier stopped being repeated.

### 5.3 F-23's r = 0.714 is a single-channel correlation, not a pose match

F-23 drove the avatar from a video and measured peak Pearson **r = 0.714 at 300 ms lag**. That is one
scalar, correlated over time — it says the avatar *moves when the human moves*, with a lag. It does
**not** say the avatar adopts the same pose, and F-23's own §7 says the depth-derived channels do not
transfer at all. It is good evidence for "the pipeline is connected end to end" and weak evidence for
fidelity.

### 5.4 A constraint F-19 explicitly required was never built

F-19's `F-20 CONSTRAINT REQUIREMENTS` asked for *"an elbow flexion clamp at approximately 150 deg plus
a hinge-axis constraint on the elbow bend normal, with a secondary angular-velocity limit on forearm
roll."* Searching the retarget: **no elbow flexion clamp exists.** The only 150° in the code is
`TorsoYawGuard.WrapGuardDeg`, an unrelated torso wrap guard. The elbow problem was instead addressed
in F-22, *in the sidecar*, by zeroing confidence on biomechanically impossible angles — which
**holds** the joint rather than **clamping** it. That may be the better design, but the requirement
was closed by a different mechanism at a different layer and the substitution was never recorded as
such.

---

## 6. What we assumed or did wrong

1. **We measured stability and called it quality.** Every avatar-side metric to date — yaw jitter,
   snaps, bone-length constancy, swap count — is a *self-consistency* measure. None compares the
   avatar's pose to the subject's pose. The instrument that would have caught this does not exist.
   This is the same class of error as three findings already recorded this month (F-21 §30.6's
   labeller, §33.1's classifier, §34.4's gate-blind metric): **the number was true and the question
   was wrong.**

2. **We spent the effort on the tracking layer.** F-20A, F-20B, F-21 and F-22 are all *upstream* of
   the retarget and all are in good shape. The video says the dominant pose-fidelity error is
   *downstream*, in the retarget, which has had comparatively little adversarial attention.

3. **We assumed the debug skeleton and the avatar were two views of the same thing.** They are not,
   and `PoseDebugSkeleton`'s own docstring says so: *"if the skeleton tracks the user cleanly but the
   avatar does not, the retarget is at fault."* The tool was built to answer exactly this question and
   the answer it has been giving was not acted on.

4. **We treated Kalidokit as a given.** It was written for MediaPipe webcam input of a front-facing
   upright user. Seated, overhead and turned poses are outside what it was designed for. Every fix
   since (V2 arms, V5/V6 torso, ADR-027/037 conditioning) has been a patch on that base rather than a
   decision about whether the base fits.

5. **The conditioning was tuned against sensor noise without a fidelity counter-metric.** The 8°–22°
   dead-zone, the 8 s bend baseline, the dampers — each was a defensible response to measured noise.
   But with no fidelity metric on the other side of the scale, every one of those decisions could only
   ever move in the direction of *more suppression*. That is why 76.6 % of yaw frames are zeroed.

---

## 7. What is right, and concrete

Stated plainly, because most of this report is negative and the foundation is genuinely sound:

```text
TRACKING              the 33-point stream is good. The skeleton is that stream, and it tracks the
                      subject through every pose in this video, including seated and overhead.
TRANSPORT             F-20A sessions/stale-watchdog/failsafe, F-20B supervisor: PASS, well evidenced.
BONE-LENGTH INTEGRITY the retarget is genuinely rotation-only. 0.0011 % over 85,395 frames. The
                      avatar never stretches or scales. That is worth keeping.
NO SWAPS / NO SNAPS   the mapping is stable and does not flip or jump. Also worth keeping.
SAFETY LAYERS         F-21 ownership + F-22 biomechanical validation are upstream and unaffected by
                      any retarget decision. Whatever replaces the retarget inherits them intact.
THE DIAGNOSTIC ITSELF PoseDebugSkeleton was built for exactly this comparison and it worked.
```

**The tracking is not the problem. It has not been the problem for some time.**

---

## 8. How the avatar could be resolved

Four options, with what each costs. **No recommendation is made here** — that is the next
conversation.

### Option A — drive a rig directly from the points (what was asked for)
Build bones between the tracked joints and let them **stretch**. Pose becomes the data, with no
solve, no dead-zone, no gate.
*Gets:* exactly the fidelity of the green skeleton, in a rig.
*Costs:* not a character — a stick/volumetric figure. Stretching bones break mesh skinning.
*Effort:* low. Roughly a day.

### Option B — position-target IK instead of rotation-aim
Keep the VRM, but drive the hands/feet/head to the **tracked positions** as IK targets (Unity
Animation Rigging is already in the project — `AnimationRiggingIkDriver.cs` exists), instead of
aiming bone rotations.
*Gets:* the endpoint goes where the subject's endpoint is; arms-overhead works; sitting becomes
possible if the root/hips are also driven.
*Costs:* proportion mismatch becomes visible as reach limits; needs root rotation driven; IK jitter
needs its own conditioning.
*Effort:* medium. The largest single win per unit of work.

### Option C — fix the specific failures in the current retarget
Drive root rotation; stop the bend high-pass eating a sustained lean; add hip-width normalisation;
re-examine the 8°–22° dead-zone now that sub-pixel could lower the noise floor.
*Gets:* incremental improvement, keeps all existing validation.
*Costs:* patching a base that may not fit; each change needs its own evidence and ADR.
*Effort:* medium, spread over several passes.

### Option D — retarget proportions properly (bone-length normalisation)
Scale the incoming skeleton to the avatar's proportions before solving, so the solve is done on a
body the avatar can actually adopt.
*Gets:* addresses the root cause of endpoint error.
*Costs:* real work; interacts with everything above.
*Effort:* high.

**A and B are not exclusive.** A is a fast way to get a usable, honest mirror now; B is the path to
keeping a character.

---

## 9. Points not raised, that matter

1. ~~**There is still no fidelity metric, and that is the thing to fix first.**~~ **DONE, same day —
   see §11 and ADR-062.** The metric was built, self-tested (22/22), run against live data on two
   clips, and cross-checked against the debug skeleton in world space. It confirms §4.2/§4.5
   quantitatively and refutes nothing in this report. The original note read: *whatever option is
   chosen, without a measurement of avatar pose vs subject pose the next round will repeat this one.*
   That remains the reason it was built first, before any retarget change.

2. **`showDebugSkeleton` defaults to `false`.** The single most informative diagnostic in the project
   is off by default.

3. **The avatar's failures are not random — they are systematic and reproducible.** Arms overhead,
   seated, legs splayed. That means they are fixable and testable, not noise.

4. **The seated case may be out of product scope, and that should be decided rather than discovered.**
   A mirror installation may only ever see standing users. If so, §4.2 stops being the top defect. If
   not, it is the top defect by a wide margin. Nobody has stated which.

5. **This changes the priority order of the open work.** F-21's drift hole (ADR-061) and the sub-pixel
   decision are both real, but neither improves what the user sees in this video. The retarget does.

6. **Sub-pixel 1/8 interacts with §4.5.** The dead-zone exists because the noise floor is high. Sub-
   pixel lowers the floor 6.80° → 0.85°, which is the only honest route to shrinking the dead-zone.
   The two decisions are coupled and have been treated separately.

7. **Nothing in this report is committed.** Both repositories remain uncommitted working-tree state.

---

## 10. Evidence

```text
docs/evidence/F26_avatar_vs_skeleton/
    contact_sheet_11_samples.png                 11 Game-view samples, t=8..124 s
    t040_legs_splayed.png                        avatar legs wider than subject, feet rotated out
    t065_arms_overhead_avatar_horizontal.png     skeleton hands above head, avatar at shoulder height
    t090_neutral_both_agree.png                  the control: at rest, both are correct
    t115_seated_avatar_upright.png               subject seated, skeleton seated, avatar upright
source: Assets/Games/video/LatestScreenReording.mp4
```

---

## 11. The fidelity metric — built, validated, and its first numbers (2026-09-16, later same day)

```text
STATUS:  instrument ACCEPTED (ADR-062). Measurement is FIRST EVIDENCE: 2 clips, 1 avatar, video path.
CHANGED: nothing in the retarget. This section is the BEFORE number, not a fix.
```

§9.1 of this report said the fidelity metric was the thing to build first. It was built, and it was
run. Everything below is measured, not inferred from the recording.

### 11.1 What was built

| piece | what it does |
|---|---|
| `Runtime/Core/Models/BoneFidelity.cs` | one bone's result: error, min source confidence, validity, and each side's own inclination |
| `KalidokitControlRigDriver.SampleFidelity()` | 9 bones, read-only, allocation-free; cannot alter the pose it measures |
| `Runtime/Diagnostics/AvatarFidelityRecorder.cs` | one jsonl record per RENDERED frame, sampled on `Application.onBeforeRender`; self-terminating |
| `python-sidecar~/f26_fidelity_analyze.py` | per-bone distribution + the follow ratio; **22/22 self-test**; refuses to emit a combined score |

Sampling point matters: the retarget chain ends with UniVRM's `Vrm10Runtime.Process()`, so a
LateUpdate sample races it and can capture the pose one stage early — measuring a skeleton that was
never drawn. The recorder samples on `onBeforeRender` for that reason.

### 11.2 Run A — video.webm x4, subject at ~1.4 m (the primary evidence)

5929 frames recorded, 742 dropped as stale, **5187 analysed**, 0 excluded for low confidence.

| bone | median | p90 | max | >10° | >30° | follow | reading |
|---|---|---|---|---|---|---|---|
| leftUpperArm | 0.4° | 1.0° | 4.0° | 0.0 % | 0.0 % | 1.00 | follows |
| leftLowerArm | 1.0° | 3.8° | 59.3° | 0.7 % | 0.0 % | 1.00 | follows |
| rightUpperArm | 0.6° | 1.3° | 27.6° | 0.0 % | 0.0 % | 0.99 | follows |
| rightLowerArm | 1.1° | 3.4° | 71.9° | 1.0 % | 0.1 % | 1.00 | follows |
| leftUpperLeg | 9.3° | 20.1° | 31.4° | 46.2 % | 0.4 % | 1.50 | OVER-DRIVEN |
| leftLowerLeg | 14.8° | 28.9° | 42.1° | 70.5 % | 8.7 % | 2.32 | OVER-DRIVEN |
| rightUpperLeg | 9.2° | 20.6° | 31.6° | 47.7 % | 0.6 % | 1.40 | |
| rightLowerLeg | 15.1° | 35.8° | 45.5° | 71.0 % | 18.9 % | 2.25 | OVER-DRIVEN |
| **trunk** | **16.7°** | **19.3°** | **22.1°** | **100.0 %** | 0.0 % | **0.27** | **UNDER-DRIVEN** |

`follow` = the avatar's own inclination swing (p10..p90) divided by the subject's. It exists because
a large but nearly CONSTANT error is ambiguous in the error column alone: a fixed offset and a
completely frozen channel read identically. They do not read identically here.

### 11.3 Run B — 456.webm x5, subject at ~3 m (weak evidence, recorded as such)

5428 frames, **80–90 % of them below the confidence floor and excluded**. It replicates the trunk
result (median 15.2°, follow 0.41) and the leg over-drive (1.08–1.58). Its arm numbers are dominated
by source noise at 3 m and are NOT a retarget verdict.

### 11.4 The measurement was cross-checked, because the arms cannot validate it

`kalidokitAimArms` is ON, so the arm retarget AIMS the bone along the same landmark pair the metric
compares against. A coordinate-mapping error would cancel on both sides, and the near-zero arm error
would prove nothing. The channels showing error are exactly the non-aimed ones — a coincidence that
had to be ruled out rather than assumed away.

Cross-checked in WORLD space against `PoseDebugSkeleton`, which shares none of the driver's internal
conditioning and which §2 of this report already establishes as tracking correctly:

```text
L-ARM   skeleton(12->14) = (-0.095, -0.977,  0.192)    avatar = ( 0.111, -0.975,  0.192)
        angle after the known X mirror = 0.9°           -> the coordinate mapping is correct
TRUNK   skeleton         = ( 0.086,  0.989, -0.122)    avatar = ( 0.000,  0.989,  0.148)
        angle = 16.3°, and the X mirror changes nothing -> 16.7° confirmed independently
```

Two paths sharing no convention agree on the trunk to within 0.4°.

### 11.5 What the trunk number is, exactly

> ## ⚠ CORRECTED — this section measured a configuration the project does not ship
>
> Everything below ran against a **runtime** `kalidokitBodyTorsoRoll = 0`. The scene asset stores
> **1**; the long-running Play session sampled here had been set to 0 in the Inspector, and that was
> not noticed until F-27's fidelity A/B re-measured the same clip from a freshly opened scene.
>
> On the **identical clip**, with the **identical subject trunk motion** (`src` 6.1–13.1° both times):
>
> ```text
> here      (torsoRoll = 0):  trunk median 16.7 deg   avatar span 7.9..9.8 deg    follow 0.27
> F-27 §8   (torsoRoll = 1):  trunk median  7.3 deg   avatar span 1.3..10.3 deg   follow 1.29
> ```
>
> **The trunk is NOT a dead channel in the shipped configuration.** Its error is less than half what
> is reported below, and it follows the subject rather than sitting at its rest lean. The mechanism
> described below is still the correct explanation of what `torsoRoll = 0` does — it is simply not the
> value the project ships. The sagittal high-pass (`spineBendBaselineTau = 8 s`) is unaffected by this
> correction and still removes a sustained lean, so §4.2 "the avatar cannot sit" stands.
>
> Nothing else in F-26 depends on `torsoRoll`: the arms, the legs, the metric itself and the reasoning
> about rotation-driven rigs are all unaffected. See F-27 §8.5.

The bound VRM's REST hips→upperChest chain is itself `(0.0000, 0.9883, 0.1522)` — **8.76° off
vertical**. That is this channel's floor; a perfect retarget cannot report 0°. Across four snapshots
the avatar's trunk sat **0.00° / 0.23° / 0.40° / 1.20°** from that rest vector while the subject's
trunk moved through ±0.15 in both X and Z, and the avatar's lateral component was **identically
0.000** every time.

The trunk is not biased. Its LEAN channel does not move. Two mechanisms, both already in the code,
both deliberate, neither a broken line:

* **Lateral lean is multiplied by zero** — `spineRoll = pose.Spine.z * torsoRoll` with
  `kalidokitBodyTorsoRoll = 0`. (ADR-059 re-enabled *pelvic* roll; trunk roll stayed off.)
* **Sagittal lean is high-passed away** — `rawPitch = Atan2(trunk.z, vert)` is taken against an
  ADAPTIVE baseline with `spineBendBaselineTau = 8 s`, and only the residual is applied. A SUSTAINED
  lean decays to nothing as the baseline follows it; only fast transients survive.

That second mechanism **is** §4.2 of this report. "The avatar cannot sit" is not a missing feature —
a seated posture is precisely a sustained lean, and a sustained lean is precisely what an 8 s
high-pass removes. §4.2 described the symptom; this is the line of code.

### 11.6 A false finding the instrument caught on itself

The first run reported forearm maxima of **121.6°** and **155.4°** and a ~5 % catastrophic tail on
every arm. All of it was the release-to-rest tail: when the producer stops, `ReleaseToRest` walks the
avatar back to rest while the recorded target stays frozen at the last packet. With those 742 stale
frames dropped the same recording reads max 59.3 / 71.9° and 0.0–0.1 % over 30°.

Had that tail been reported it would have been the fourth wrong instrument this month (the labeller
×3, the pixel/metre soak, the protocol-guard smoke test). The drop rule is defensible rather than
convenient: a source value repeating for longer than `poseStaleSeconds` means no packet arrived, and
it cannot mean a motionless subject, because RTMW3D's own estimation noise changes the value on every
packet.

### 11.7 Four limits, stated so they are not forgotten

1. **Direction, not position.** Two poses can agree on every bone direction and still put the hands in
   different places, because the avatar's bone lengths are its own (§3). Endpoint error is a different
   measurement and is deliberately not conflated with this one.
2. **It cannot see trunk YAW.** Axial twist leaves the hip→shoulder line unchanged, so 16.7° is LEAN
   only and says nothing about the torso-yaw blocker of F-16/F-17/F-18. In the sampled frames the
   subject's own shoulder yaw varied only ~8° — inside the documented dead zone — so this clip cannot
   test yaw in either direction. The avatar's shoulder line held at exactly −180.0°, which is
   consistent with the gate working as designed and is not evidence either way.
3. **The video path has no stereo** (F-23): every joint's depth is synthesised. This exercises the
   RETARGET, not the sensor. It has not been run on an OAK-D session.
4. **"Frames" are RENDERED frames (~118 Hz), not independent samples.** 5187 rendered frames carry
   ~1300 pose packets at 30 Hz. The percentages are over what was drawn — which is what a viewer sees
   — but they are not 5187 independent observations.

### 11.8 What this changes in §8's options

Nothing is chosen, but the options are no longer equally blind:

| option | what the measurement now says |
|---|---|
| A — drive a rig directly from the points | still the only option that sidesteps rotation-driven limits entirely; the metric now gives it a target to beat |
| B — position-target IK | addresses the legs' 1.4–2.3× over-drive directly; does nothing for a trunk channel that is switched off |
| C — fix the specific failures | **now has two named, located causes** (§11.5) rather than symptoms. Cheapest by a wide margin |
| D — bone-length normalisation | unmeasured by this metric, which is direction-only; needs the endpoint metric of §11.7.1 first |

### 11.9 Evidence

```text
docs/evidence/f26/
    fidelityA_video.jsonl    Run A, 5929 records, video.webm x4
    fidelityB_456.jsonl      Run B, 5428 records, 456.webm x5
    wireA_video.jsonl        the wire payload for Run A, for cross-referencing what was sent
    wireB_456.jsonl          the wire payload for Run B
reproduce:
    .venv\Scripts\python.exe f23_video_to_unity.py --video ..\..\video\video.webm --loop 4
    .venv\Scripts\python.exe f26_fidelity_analyze.py --in ..\docs\evidence\f26\fidelityA_video.jsonl
```
