# F-27 — The Humanized Skeleton: make the pose a body before the avatar sees it

```text
VERDICT: IMPLEMENTED AND MEASURED. Video path only; not yet run on an OAK-D stereo session.
PIPELINE: tracking -> existing filtering -> [ HumanizedSkeleton ] -> existing retarget -> avatar
CHANGED: nothing in the tracking, the filtering, P0-1, P1-1/2/3, Arm V2 or Torso V5/V6.
TESTS:   32/32 synthetic + fault-injection (C#), 18/18 analyser self-test (Python).
```

> **One-line summary.** Tracking output is now treated as a *suggestion*: a new layer between the
> existing filter and the existing retarget refuses impossible poses, holds bone lengths constant,
> rejects teleports and collapses, holds missing joints in the parent's frame and eases them back —
> and it does so on **99.6 % of poses without any stateful stage firing at all**, so it cannot have
> added lag to real movement.

---

## 1. Where it was injected, and why there

`AppBootstrap.UpdateTracking()`, immediately after the pose is filtered and **after**
`RenderDebugSkeleton(filtered)`, before anything retargets:

```csharp
RenderDebugSkeleton(filtered);          // the RAW pose - see below
PoseFrame humanized = filtered;
if (useHumanizedSkeleton) {
    humanized = humanizedSkeleton.Process(filtered, poseDelta);
}
... kalidokitControlRig.Apply(humanized);
```

**The debug skeleton still draws the RAW pose, permanently and deliberately.** Its whole diagnostic
value is that it shows what the *tracker* said, which is what makes "the skeleton is right and the
avatar is not" a meaningful sentence (F-26). Drawing the humanized pose there would make the skeleton
agree with the avatar by construction and destroy the only independent reference the project has.

**Confidence is passed through untouched.** A held joint keeps the confidence it arrived with, so
P0-1's `LimbGate` still sees an unobserved limb as unobserved and holds its rotation exactly as
before. What changes is the consumers that read landmarks with **no gate at all** — the Kalidokit
torso solve reads 11/12/23/24 without any validation — which now receive a possible body instead of a
zero-filled one. Fabricating confidence here would have silently disabled a protection that works.

## 2. What was implemented

| requirement | where | how |
|---|---|---|
| realistic joint rotation limits | `HumanizedSkeleton.ApplyAngularLimits` | elbow/knee interior-angle floors, hip and neck cones, torso-twist refusal, knee-bends-forwards reflection |
| fixed / consistent bone lengths | `BoneLengthCalibrator` | sliding-window **median** per bone, left/right averaged, rebuilt outward from the mid-hip |
| maximum movement between frames | `ClampVelocity` | per-joint-class speed ceilings with a persistence escape hatch |
| previous valid pose preservation | `heldLocalDir[]` | last good direction per bone, held in the **parent segment's frame** |
| missing-joint hold | same | a held forearm swings with its upper arm instead of freezing in world space |
| reject / clamp impossible values | `ClassifyValidity` | NaN, at-the-origin, outside-any-body, below the emit floor — four separate counters |
| smooth recovery | recovery blend | 0.15 s direction slerp on re-acquire only, never during normal tracking |
| prevent collapse toward origin | the rebuild | a joint is *placed* at one calibrated bone length from its parent, so collapse is unrepresentable rather than merely gated |
| no snapping / extreme bend / unnatural twist | the above combined | — |

**Deliberately NOT done:** no smoothing filter was added. The project already owns three (the
sidecar's One-Euro, P1-1's tracker, P1-3's interpolation); a fourth would be latency for its own sake.
Every stage here is either a per-frame geometric projection — which has no memory and cannot lag — or
fires only on a fault. The root translation channel is carried through untouched.

## 3. Before / after, measured

Both streams measured **from the same frame, in the same pass, by the same code**
(`HumanizedSkeletonRecorder` → `PoseMetrics`). Running the session twice with the layer off and on
would compare two different takes and credit the layer with the difference between them.

Video path, `video.webm`, 2462 poses, first 300 skipped as warm-up:

| measurement | BEFORE (raw) | AFTER (humanized) | change |
|---|---|---|---|
| **bone-length deviation, median** | 0.0568 m | **0.0181 m** | **−68 %** |
| bone-length deviation, p99 | 0.1744 m | 0.0486 m | −72 % |
| bone-length deviation, worst frame | 0.2526 m | 0.0520 m | −79 % |
| landmark jump, median | 0.0123 m | 0.0119 m | −3 % |
| landmark jump, worst frame | 0.6547 m | 0.5859 m | −11 % |
| landmark jump, p99 | 0.0708 m | 0.0844 m | **+19 % worse** |
| joint-frames below the anatomical floor | 0 | 0 | = |
| frames inside the collapse radius | 0 | 0 | = |
| NaN/Inf reaching the retarget | 0 | 0 | = |

What the layer actually did, over 2162 poses:

```text
bone length corrected     36680 joint-frames   100.0 % of poses
velocity clamped              8 joint-frames     0.4 % of poses
knee un-bent                 39 joint-frames     1.8 % of poses
joints held                   0                  0.0 %
elbow/knee opened up          0                  0.0 %
torso twist refused           0                  0.0 %
poses no STATEFUL stage touched ............... 99.6 %
```

**Reading it honestly.** On this clip the tracking was clean: no dropouts, no NaN, no collapses, no
impossible angles in *either* stream. So the fault-handling stages had almost nothing to do, and the
measured win is entirely the structural one — bone lengths, which improved by two thirds to four
fifths. The clip does not demonstrate the hold, the recovery or the rejection paths at all; those are
demonstrated by the fault-injection tests in §5, not by this recording.

**The p99 jump is genuinely 19 % worse and is not dismissed.** Holding a bone at a fixed length moves
its child whenever the tracker's length estimate wanders, and at the tail of the distribution that
costs more per-frame motion than it saves. The median and the worst frame both improved; the tail did
not. It is a real trade and it is the first thing to look at if the avatar reads as busier.

## 4. Two defects the measurement found, that testing did not

Both were found *by the numbers*, after 32/32 synthetic tests already passed. Neither would have been
visible by eye.

**(a) The body-forward axis was ambiguous, and the knee fix fired on 84 % of poses.** Forward was
derived as `cross(hipLine, up)`, whose handedness depends on whether this frame's landmark 23 sits at
+X or −X — and the mirror/swap conditioning happens *later*, inside the driver, so that is not
knowable at this stage. With the sign inverted, "the knee must be in front" becomes "the knee must be
behind" and a perfectly normal leg is reflected every frame. Fixed by deriving forward from the
**feet** (toes are anterior to heels on every human, and the vector is ~0.12 m, far above the noise),
falling back to the nose, and **standing every front/back test down** when neither is observed.
Measured: knee un-bent **84.0 % → 1.8 %** of poses.

**(b) The velocity clamp was measuring its own output, and fired on 80 % of poses.** `lastAccepted`
stored the *rebuilt* position, so the structural rebuild moved a joint, the next frame's step was
measured from the moved position, the clamp fired on the difference it had created itself, and the
joint lagged further every frame. The clamp is a statement about the *tracker*, so it now compares
tracker to tracker. Measured: velocity clamped **80.3 % → 0.4 %** of poses, and the median landmark
jump went from **+269 % worse** to −3 %.

A third, smaller one: the recorder's own bone-length metric borrowed `BoneLengthCalibrator`, whose
left/right mirror lookup indexes a *different* bone table — it was silently averaging the shoulder
span against a forearm and reporting ~13 cm of error on **both** streams. That is a fourth wrong
instrument this month; it was caught because the number was implausible on the raw stream too.

## 5. The test mode

`HumanizedSkeletonSelfTest.Run()` — headless, no Editor, no Play mode, no avatar, no camera.
`SyntheticPose` generates a body with exact bone lengths and exact joint angles, so every assertion is
against a known answer rather than an impression. **32 passed, 0 failed.**

Half the cases prove the layer FIXES bad data; the other half prove it LEAVES GOOD DATA ALONE:

```text
clean tracking is not moved                        max 0.000 m over 60 frames
a full arm sweep arrives with zero lag             max 0.000 m, 0 -> 180 deg in 3 s
an arm straight overhead is not truncated          wrist 0.000 m, elbow 0.000 m, cone=0
a 50 % stretched forearm is restored               0.2600 m (true 0.2600)
a confident wrist-at-the-origin keeps its forearm  0.2600 m, 0.573 m clear of the hip
a dropped elbow keeps its bone length while held   0.000 m drift over 15 frames
NaN never reaches the retarget                     all finite, 2 landmarks rejected
a one-frame wrist teleport is capped               0.328 m (ceiling 0.480 m)
a demand sustained in ONE direction is accepted    1 acceptance
a joint flickering between two places is never accepted
an elbow folded through itself is opened up        0.0 deg -> 25.0 deg
a knee bending forwards is reflected back          thigh and shin preserved to 0.01 m
150 deg of shoulder twist is brought inside a spine  34.0 deg
a returning joint eases in instead of snapping     max step 0.027 m, reaches truth in-window
an invalid frame stays invalid                     no body is fabricated
```

The arms-overhead case is a **permanent regression guard**. A 175° shoulder cone was implemented
first, and measured firing on exactly one pose in a full arm sweep — arms straight overhead — moving
the elbow 24 mm. That is the single pose F-26 §4.1 records as this project's worst avatar failure, so
the cone was truncating the one thing it most needed to leave alone. There is now **deliberately no
shoulder cone**: a cone about the trunk's down axis is the wrong shape for a joint that genuinely
reaches 180°.

## 6. What remains

1. **Never run on stereo.** Both recordings used the video path, which synthesises every joint's
   depth (F-23). The rejection, hold and recovery stages are the ones a real OAK-D session will
   actually exercise, and none of them has been seen firing on real depth.
2. **The p99 jump regression** (§3) is unexplained beyond "fixed lengths move children". It should be
   attributed to a specific bone before anyone decides whether it matters.
3. **Hyperextension is not observable** from three landmarks: the interior angle is direction-agnostic
   and capped at 180°. The elbow maxima in `HumanBodyModel` are used by the offline analyser to flag
   impossible angles in recorded data, not enforced live. Only the knee case, which has an
   unambiguous sign against the body's forward axis, is corrected.
4. **Torso twist has never fired.** The limit is 90°, past any human spine, so it is an impossibility
   rejector by design — but that means it is also untested against live data.
5. **No avatar-side number yet.** This measures the POSE. Whether a more human pose produces a more
   human avatar is F-26's fidelity metric's question, and the two are deliberately not conflated.
   Running F-26 §11's metric with the layer on and off is the obvious next measurement.

## 7. Evidence

```text
docs/evidence/f27/
    RESULTS.txt                  the before/after report above, verbatim
    postfix_all_stages.jsonl     2462 poses, both streams, all stages on  (gitignored, regenerable)
    seg_all_stages.jsonl         the PRE-fix recording, kept as the record of the two defects
reproduce:
    .venv\Scripts\python.exe f23_video_to_unity.py --video ..\..\video\video.webm --loop 4
    ... attach HumanizedSkeletonRecorder to AppBootstrap ...
    .venv\Scripts\python.exe f27_humanized_analyze.py --in ..\docs\evidence\f27\postfix_all_stages.jsonl
```

---

## 8. Did it help the AVATAR? The fidelity A/B — measured, and the answer is "no, and here is why"

§5 measured the POSE. This section runs F-26 §11's fidelity metric — *avatar bone direction vs the
subject's segment direction* — with the layer ON and OFF, which is the only measurement that says
whether any of it reaches what a viewer actually looks at.

### 8.1 The instrument had to be fixed first

`SampleFidelity` built its reference `want` from the driver's landmark buffer — which **is** the
humanized pose when the layer is on. That would have scored the avatar against its own input: the
retarget handed an easier pose and then marked on how well it followed it, winning by construction.

`KalidokitControlRigDriver.SetFidelityReference(raw)` now supplies the **RAW** tracked pose as the
reference in both arms, conditioned through the same `Condition()` helper the solver uses so the two
cannot drift apart.

**The reference is a handicap, and that is deliberate.** Raw is not the subject either — F-27 exists
precisely because the raw pose contains shapes a body cannot make — but it is the only *independent*
reference available. Every correction this layer makes moves the pose away from it and can only cost
points here. A tie means the humanized pose cost the avatar nothing; a win would mean it helped
despite the handicap.

### 8.2 Three arms, same clip, same wire, 4000+ live frames each

| bone | OFF med / p90 | ON med / p90 | change | ON, bone-len OFF |
|---|---|---|---|---|
| leftUpperArm | 0.4° / 0.9° | 1.2° / 2.8° | = | **0.4° / 1.0°** |
| leftLowerArm | 0.8° / 3.0° | 2.1° / 6.0° | **+1.3° WORSE** | **0.8° / 3.1°** |
| rightUpperArm | 0.5° / 1.3° | 1.5° / 3.1° | **+1.1° WORSE** | **0.5° / 1.4°** |
| rightLowerArm | 0.9° / 2.7° | 1.9° / 5.5° | **+1.0° WORSE** | **1.0° / 3.0°** |
| leftUpperLeg | 7.1° / 16.9° | 7.5° / 17.1° | = | 6.9° / 16.6° |
| leftLowerLeg | 12.3° / 26.2° | 12.7° / 26.3° | = | 11.9° / 26.3° |
| rightUpperLeg | 6.9° / 16.6° | 7.1° / 16.9° | = | 6.9° / 16.4° |
| rightLowerLeg | 12.1° / 32.2° | 12.1° / 31.8° | = | 12.2° / 31.8° |
| trunk | 7.3° / 8.9° | 7.1° / 8.8° | = | 7.1° / 8.8° |
| | | **0 better, 3 worse, 6 =** | | **0 better, 0 worse, 9 =** |

"=" means the median moved by less than 1° **and** less than 10 %. The two passes are separate
playbacks of the same clip, not the identical frames, so smaller moves are not evidence.

### 8.3 What it says

**With bone-length enforcement OFF, the humanized layer is indistinguishable from not running it at
all** — nine bones unchanged, the arms landing back on 0.4 / 0.8 / 0.5 / 1.0°, the same figures as the
OFF arm. So the velocity clamp, the knee fix and the joint limits cost the avatar **nothing**. That is
worth having on its own: the protections are free.

**The entire ~1° arm cost is bone-length normalisation**, isolated by measurement rather than inferred.
It is also the only stage that fired on this clip at all (100 % of poses; velocity 0.4 %, knee 1.8 %,
everything else 0 %).

**And that 1° is the correction itself, not a retarget failure.** ARM V2 *aims* the bone along its
input direction — the follow ratio is 1.00 in every arm, so the avatar tracks whatever it is given,
exactly. Measured against raw, the arm error therefore **is** the distance the layer moved the arm.
Whether that move is an improvement cannot be decided by this metric, because deciding it would
require a reference better than the raw tracker, and none exists.

**F-27 does not fix F-26's leg over-drive.** The legs' follow ratio is 1.26–2.06 in all three arms:
the avatar's legs still swing far wider than the subject's. That over-drive is a property of the
Kalidokit leg solve, not of the bone lengths fed into it, and a cleaner input does not touch it.

### 8.4 The honest verdict

**On clean tracking, F-27 is neutral-to-slightly-negative for avatar fidelity, and its whole value
rests on the fault cases this clip does not contain.** The clip has 0 holds, 0 NaN, 0 collapses and 0
impossible angles in either stream (§3), so there was nothing for the protective stages to protect
against — and they correctly did nothing, at zero cost. The next honest test is footage that actually
contains dropouts and occlusion, or a live OAK-D session, where those paths fire.

It does **not** follow that the layer should be switched off. It costs ~1° on aimed arms against a
reference that is itself wrong in exactly the ways the layer corrects, and it buys the protections at
no measured cost everywhere else. It does follow that nobody should claim it improves the avatar.

### 8.5 A correction to F-26 §11.5, found while running this

F-26 measured the trunk as a **dead channel**: 16.7° median error, follow ratio 0.27, the avatar's
trunk pinned to within 0.00–1.20° of its rest lean with a lateral component of identically 0.000, and
named `kalidokitBodyTorsoRoll = 0` as one of two causes.

**That measurement ran against a runtime value of 0 that does not match the saved project.** The scene
asset stores `kalidokitBodyTorsoRoll = 1`; the long-running Play session F-26 sampled had been set to
0 in the Inspector. On the identical clip, with the identical subject trunk motion (`src` 6.1–13.1°
both times), the saved configuration gives:

```text
F-26 run A (torsoRoll = 0):  trunk median 16.7 deg   rig span 7.9..9.8 deg    follow 0.27
this A/B   (torsoRoll = 1):  trunk median  7.3 deg   rig span 1.3..10.3 deg   follow 1.29
```

The trunk is **not** a dead channel in the shipped configuration. Its error is less than half what
F-26 reported and it follows the subject. The rest of F-26 stands — the arms, the legs, the metric
itself and the reasoning about rotation-driven rigs are all unaffected by `torsoRoll`.

This also incidentally discharges the open item in `tasks.md`: *"`kalidokitBodyTorsoRoll: 0 → 1`.
Applied, NOT validated."* It is now validated on this clip, and it more than halves the trunk error.

### 8.6 Evidence

```text
docs/evidence/f27/
    FIDELITY_AB.txt              both comparisons, verbatim
    fidelity_humanOFF.jsonl      4223 frames, 0 stale   (gitignored, regenerable)
    fidelity_humanON.jsonl       4390 frames, 0 stale
    fidelity_noBoneLen.jsonl     3995 frames, 0 stale
reproduce:
    .venv\Scripts\python.exe f23_video_to_unity.py --video ..\..\video\video.webm --loop 8
    ... attach AvatarFidelityRecorder, flipping useHumanizedSkeleton / humanizeBoneLengths ...
    .venv\Scripts\python.exe f26_fidelity_analyze.py --before <OFF>.jsonl --after <ON>.jsonl
```

**A note on sample size, because the first attempt at this was wrong.** An earlier run of this A/B
reported "3 bones better" — it had 366 live frames in the OFF arm against 1599 in the ON arm, because
the recorder had been attached at ~57 s into a 60 s producer and spent most of its window recording an
avatar releasing to rest. The stale filter caught it, the asymmetry was visible in the header, and the
run was discarded and redone with 4000+ live frames in every arm. The numbers above are the redone
ones; the discarded ones are not quoted anywhere.
