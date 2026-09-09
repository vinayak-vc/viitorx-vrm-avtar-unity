# Unity Arm Retargeting V2 — roll fix + end-to-end forensics — 2026-09-09

Two tasks, one document.

**Task 1 — fix the near-straight-elbow roll jitter.** Done: the single bend threshold in `ArmAimSolver`
is replaced by a hysteresis latch plus a continuity guard on the roll reference. Measured before/after
on byte-identical input: roll steps above 90° **6 → 0**, above 45° **10 → 3**, worst single-frame step
**145.56° → 65.29°**.

**Task 2 — find where the rendered avatar goes crooked.** Answer: **at no stage between the landmarks
and the rendered VRM bone.** Source → PoseFrame → solver → control rig → *actual skinned humanoid bone*
is exact — stage B (control rig → rendered VRM) measured **0.000000° maximum over 13 308 samples** of
video-driven motion, and 0.000° on all six deterministic poses. No fix was implemented for Task 2,
because the brief forbids one until a stage is proven bad and no stage was.

Scope held: the arm direction solve is untouched, and Python/OAK-D, legs, torso, head, hands, IK,
`PoseSpaceConverter`, `flipQuat`, `eulerSigns` and the P1 systems were not modified.

Tested against `Assets/Games/video/video.webm` driving the real Unity avatar, plus six deterministic
poses injected on the real UDP wire.

---

## 1. V1 baseline

From [`UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`](UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md),
measured live over nine motions:

| measure | value |
|---|---|
| avatar-vs-skeleton arm error, mean | **0.46°** |
| elbow reproduction error, mean | **0.31°** |
| dense-pass arm-direction error, mean | **0.26°** |
| L/R asymmetry | ≤0.2° per motion |
| **remaining defect** | forearm roll flips near a straight elbow — 31 single-frame twist pops > 45°, **68 % below 15° of elbow bend**, median bend **12.3°** |

Direction tracking was therefore already correct, and the roll was the one defect attributable to the
arm code. That is what Task 1 fixes.

### 1a. A correction to the V1 report

The V1 live-validation document describes `model_log.jsonl` (`elbowAng`, `boneLen`, `llowF`/`rlowF`) as
reading "the real VRM humanoid bones". **That is wrong, and this task found it.** `AppBootstrap` obtains
its `Animator` with `GetComponentInChildren<Animator>()`, and on a control-rig-enabled `Vrm10Instance`
that Animator's humanoid map resolves to the CONTROL bones:

```
AppBootstrap.boundAnimator  ->  AvatarRoot/VRM1
   GetBoneTransform(LeftUpperArm) -> Runtime Control Rig/.../Vrm10ControlBone:LeftUpperArm
Vrm10Instance.Humanoid
   GetBoneTransform(LeftUpperArm) -> metarig/spine/.../shoulder.L/upper_arm.L      <- the skinned bone
```

So every "real VRM bone" figure in the V1 report was in fact a control-bone figure. **The V1
conclusions are unaffected** — its A/B metric was `[RETARGET-TRACE] errDeg`, which is explicitly
source-vs-control-rig and was never claimed otherwise — but the labelling was inaccurate, and §3 below
supplies the measurement that was actually missing. The V1 document has been corrected.

---

## 2. Roll-jitter root cause, and the fix

### 2.1 Root cause, measured

Binning every wrapped roll step of the V1 dense live trace (~40 Hz) by elbow bend:

| bend | n | p90 | p99 | max | steps > 90° |
|---|---|---|---|---|---|
| 0–5° | 247 | 0.60 | 8.00 | 165.8 | 1 |
| 5–7.5° | 128 | 0.40 | 7.30 | 7.8 | **0** |
| 7.5–10° | 84 | 2.50 | 130.90 | 146.8 | 2 |
| **10–12.5°** | **79** | **63.80** | **165.30** | **176.9** | **7** |
| 12.5–15° | 70 | 11.50 | 24.80 | 70.7 | **0** |
| ≥ 15° | 2310 | ≤10.9 | ≤18.6 | 87.5 | **0** |

`BendSinMin = 0.2` is **11.54°**. Seven of the ten flips fall in the single 2.5°-wide bin that straddles
it, and **none occur anywhere above 12.5°**. The threshold was not merely too low — a single threshold
*anywhere* inside that band makes the solver alternate between `ElbowPlane` and `Held` frame to frame,
and each alternation can reverse `cross(upper, forearm)`, which is a 180° roll flip.

Dwell measurement confirms it is sustained rather than a one-off crossing: bend lingers in [11.5°, 20°)
for a median of 4 and a 95th-percentile of 17 consecutive dense samples (100–425 ms).

### 2.2 The design

Three changes, all confined to the roll reference. **The aim solve is untouched**: `LookRotation(dir,
normal)` maps forward onto `dir` whatever the normal is, so none of this can move a bone direction.

1. **Hysteresis.** `BendSinEnter = 0.35` (20.49°) to start trusting the elbow plane, `BendSinExit = 0.15`
   (8.63°) to stop. The dead band **strictly contains the 10.0–12.5° hot zone**, so an elbow hovering at
   the old threshold cannot toggle the state at all. Enter sits above the entire measured unreliable
   region; above it the live data has no >90° step and one >45° step in 2125 samples (0.05 %).
2. **Continuity guard.** A fresh candidate normal is rejected if it reverses the held normal
   (`dot < 0`) or implies more than `RollStepMaxDeg = 90°` of roll about the current bone axis. Both
   comparisons are made about the *current* axis, so swinging the arm is never mistaken for rolling it.
3. **Debounced, rate-bounded re-acquisition.** A rejected candidate that keeps being proposed coherently
   for `FlipConfirmFrames = 6` frames *while the elbow is clearly bent* is eventually believed — without
   this a genuine re-bend the other way would be locked out forever — and even then it is stepped in at
   `RollSlewMaxDeg = 30°` per frame rather than snapped.

Every constant is anchored to the table in §2.1, not chosen by feel.

**A bug this found in my own first draft.** The rate bound initially applied only to the confirmed-flip
branch. The ordinary accept path was then free to snap the remainder as soon as the held normal came
within 90° of the candidate — an offline replay measured a **60° single-frame jump** while walking a
reversal in. The bound now applies on **both** paths, which makes "no frame moves the roll by more than
`RollSlewMaxDeg`" an invariant of the solver rather than a property of one branch.

**A degeneracy the guard newly exposed.** V1 only ever held a normal at bend < 11.5°, where the forearm
is nearly parallel to the upper arm and a held normal is safely perpendicular to both. V2's `Guarded`
path can hold a normal at *any* bend, and at the 90° rejection boundary the held normal can land parallel
to the forearm — where `LookRotation` silently substitutes an arbitrary up vector, i.e. exactly the pop
being fixed. `Solve` now detects that (`sin < 5°`) and falls back, **for that bone only**, to the
roll-free carried reference, which has no roll degree of freedom and cannot helicopter. Not observed in
any capture (0/2000 adversarial frames), added because a silent arbitrary value is worse than a defined one.

### 2.3 Before / after — V1 vs V2 on byte-identical input

The two state machines were replayed over the **same recorded source arm directions**, logged straight
out of the running driver while the video drove the avatar (`ab_roll_v1_v2.py`, n = 8022 frames). A live
A/B could not do this — the subject moves differently in the two passes, which is precisely the confound
that muddied motions 4 and 5 of the V1 live report.

| | V1 (single threshold 0.20) | **V2 (hysteresis + guard)** |
|---|---|---|
| **roll steps > 90°** | **6** | **0** |
| roll steps > 45° | 10 | **3** |
| **worst single-frame step** | **145.56°** | **65.29°** |
| p95 / p99 step (left arm) | 1.15° / 8.19° | **0.96° / 7.66°** |
| **bend at the > 45° events** | min 10.5°, **p50 11.4°** | min 19.2°, **p50 35.6°** |
| roll source mix (left) | ElbowPlane 93.5 %, Held 6.5 % | ElbowPlane 88.1 %, Held 10.8 %, **Guarded 0.6 %, Slewed 0.1 %** |

The right arm had **0 → 0** on both variants: its bend distribution never entered the hot zone in this
clip, so there was nothing there to fix. The whole effect is on the left arm, and it is exactly the
events at 10.5–11.4° of bend that V1's threshold sat on.

Note the residual V2 events are no longer near-straight (p50 bend 35.6°) — they are the roll *metric*
moving because the reference itself swings with a fast-moving arm, not the state machine snapping.

**Cross-check against the shipped C#.** Unity's own live sampling of the same video, reading the running
solver, reports **0 roll steps > 90°** on both arms with a max of **63.31°** (left) / **34.86°** (right) —
matching the offline V2 model's 65.29° to within ~2°. The Python model of the state machine therefore
faithfully reproduces the C#, which is what licenses the A/B above.

**A measurement error I made and corrected.** The first version of this A/B referenced roll to a fixed
world axis projected onto the bone. That reference becomes degenerate whenever the arm swings near it,
and the fallback to a second axis injects a ~180° discontinuity *of the measurement*. It affected both
variants identically and reported a meaningless "max step 173.91° for both". The A/B now uses the
roll-free carried reference the solver itself uses. The numbers above are from the corrected metric.

### 2.4 Deterministic tests

Eight new tests in `Tests/EditMode/ArmAimSolverTests.cs`, covering the seven cases the brief names plus
two that pin the constants:

| # | test | asserts |
|---|---|---|
| 1 | `V2_ThresholdChatter_DoesNotFlipTheRoll` | bend oscillating 10.5/12.5° with the plane sign alternating never enters the plane, roll does not move; and a locked plane survives a drop to 15° |
| 2 | `V2_StraightToBendToStraight_KeepsRollContinuous` | through-straight reversal stays within the slew bound every frame |
| 3 | `V2_StraightToShallowBend_HoldsRatherThanEntering` | a 15° bend does not steal the roll; a 45° bend still drives it |
| 4 | `V2_ShallowBendNoisyCrossing_ProducesNoFlip` | 400 pseudo-random near-straight crossings, 0 flips |
| 5 | `V2_OppositeNormal_IsRejected_NotAcceptedImmediately` | a reversed plane is `Guarded` and the held normal survives; sustained, it is eventually `Slewed` in |
| 6 | `V2_NoRollStepAbove90Degrees_AcrossAnAdversarialSequence` | 2000 adversarial frames: 0 steps > 90°, and 0 above the 30° bound |
| 7 | `V2_DirectionsAreUnchangedByTheRollStateMachine` | both bone directions equal the mirrored source to < 0.05° on the same 2000 frames, including guarded/slewed ones |
| 8 | `V2_HysteresisThresholds_MatchTheMeasuredLiveDistribution`, `V2_GuardIsRateBounded_AtExactlyTheDocumentedAngles` | enter/exit bracket the measured hot zone; the slew advances by exactly `RollSlewMaxDeg`, pinning the cosine constants to the degree constants |

All seven scenarios were additionally validated against a faithful Python port of the state machine
(`arm_v2_evidence/sim_armaim_v2.py`) — which is how the 60°-snap bug in §2.2 was caught before Unity
ever compiled it. **The C# suite itself was not run: see §6.**

---

## 3. End-to-end bone trace

Six deterministic poses, injected on the real UDP wire by `pose_stage_udp.py` — exact landmark
geometry, no tracking noise, travelling the entire production path (provider → `PoseSpaceConverter` →
P1-3 pose buffer → P0 `LimbGate` → `ArmAimSolver` → control rig → UniVRM `Process()` → skinned bones).

The rendered bones are resolved through **`Vrm10Instance.Humanoid`**, not the Animator — see §1a.

| pose | arm | A source→control | B control→**rendered VRM** | C elbow src / VRM | D wrist-offset angle | E len ratio |
|---|---|---|---|---|---|---|
| 1 relaxed | left | 0.000° | **0.000°** | 180.00 / 179.97 | 0.000° | 1.0000 |
| 1 relaxed | right | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 2 T-pose | left | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 2 T-pose | right | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 3 one arm horizontal | left | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 3 one arm horizontal | right | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 4 elbow bent 90° | left | 0.000° | **0.000°** | **90.00 / 90.00** | 0.977° | 1.0000 |
| 4 elbow bent 90° | right | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 5 both overhead | left | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 5 both overhead | right | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |
| 6 arm behind back | left | 0.000° | **0.000°** | **114.20 / 114.20** | 0.632° | 1.0000 |
| 6 arm behind back | right | 0.000° | **0.000°** | 180.00 / 180.00 | 0.000° | 1.0000 |

Each pose was verified distinct by its logged source direction (e.g. T-pose left `[1,0,0]`, overhead left
`[0.148, 0.989, 0]`, behind left `[0.154, −0.873, 0.462]`); full per-stage records with world positions,
quaternions, local rotations, parents and bone lengths are in `arm_v2_evidence/stage_trace.jsonl`.

`D` is non-zero only where the elbow is bent, and only by ~1°: the avatar's limb proportions differ from
the source's, so under FK the wrist lands at the avatar's own bone lengths. That is expected and is
V1 §16.7, not a defect.

### 3.1 The same trace under real video motion

`video.webm` → RTMW3D → UDP → avatar, sampled every render frame for 25 s (n = 6654 arm-samples):

| stage | mean | p50 | p95 | p99 | max |
|---|---|---|---|---|---|
| A source→control, upper (left) | 0.2795° | 0.0485° | 1.2847° | 3.9477° | 28.90° |
| A source→control, forearm (left) | 0.5299° | 0.0485° | 2.6583° | 7.8827° | 44.14° |
| **B control→rendered VRM, upper** | **0.0000°** | 0.0000° | 0.0000° | 0.0000° | **0.0000°** |
| **B control→rendered VRM, forearm** | **0.0000°** | 0.0000° | 0.0000° | 0.0000° | **0.0000°** |
| C elbow \|src − VRM\| | 0.4430° | 0.0060° | 2.2420° | 7.3690° | 35.57° |
| E \|VRM length − control length\| | 0.000000 m | — | — | — | **0.000000 m** |

Right arm is equivalent (A 0.2545°/0.4732° mean, B 0.0000°, E 0.000000 m).

**Stage B is exactly zero across all 13 308 samples.** Stage A is non-zero only under motion, and its
cause is the pre-existing `lerpAmount = 0.5` per-frame slerp in `ApplyBoneRotation` — temporal lag, not a
geometric error: it is 0.0485° at the median (i.e. zero when the pose is not moving) and rises with
speed.

V2's roll machinery on this real input: `ElbowPlane` 90.1 %/93.4 %, `Held` 9.5 %/5.8 %,
**`Guarded` 0.2 %/0.5 %, `Slewed` 0.2 %/0.3 %** — the guard genuinely engages on real data, and there
were **0 roll steps above 90°** on either arm.

### 3.2 Screenshots

`arm_v2_evidence/video_frame_a.png`, `video_frame_b.png` — Game view, video-driven, green
`PoseDebugSkeleton` beside the rendered VRM in the same frame. The avatar's arm directions visibly match
the skeleton's in both.

The brief also asks for a separate control-rig render. There is none to make that would be informative:
§3 measures the control bone and the rendered bone as **identical to 0.000000°**, so a control-rig
overlay would be drawn on top of the VRM bones exactly. That identity is the finding, and it is stronger
than a picture of it.

---

## 4. Exact first-bad-stage

**There is no bad stage between the landmarks and the rendered avatar.**

```
RAW LANDMARKS ──0.000°──> PoseFrame direction ──0.000°──> ArmAimSolver target
              ──0.000°──> control UpperArm/LowerArm ──0.000000°──> rendered VRM upper_arm.L / forearm.L
              ──> visible wrist: direction exact, position at the avatar's own bone lengths (FK)
```

Classification of the "crooked avatar" symptom against the brief's list:

| candidate | verdict | evidence |
|---|---|---|
| **SOLVER** (`ArmAimSolver`) | **ruled out** | A = 0.000° on all six exact poses; 0.0485° median under video motion |
| **CONTROL** (rig composition) | **ruled out** | same measurement — the control bone reaches the source direction exactly |
| **VRM** (humanoid mapping / rest pose / deformation) | **ruled out** | B = **0.000000°** max over 13 308 video samples and 0.000° on all six poses; bone-length delta **0.000000 m** |
| **MESH** (skinning) | **not implicated, not fully excluded** | the bones are provably correct, so any residual would have to be skin weighting alone; nothing in the captures or screenshots suggests it |
| **SOURCE** (landmark / tracking) | **the remaining candidate** | already quantified in the V1 live report: a 121° elbow reported for a straight hanging arm, arms-overhead missed entirely for a 15 s window |
| torso interaction | not implicated | arm rotations are composed against the inverse parent, so a yawed chest cannot drag the arm; poses here had the torso at rest |

**Conclusion: the geometry is not where the avatar goes crooked.** Under exact input the rendered VRM
bone matches the human landmark to the numerical floor. The V1 live report already measured the source
stage as wrong by tens of degrees, and that remains the dominant error.

**Honest limit.** I could not reproduce a "crooked at the rig stage" failure at all — every stage
measured exact. If there is a specific frame or pose where the rendered avatar looks wrong *while the
green skeleton looks right*, the fastest way to close this is a screenshot or timestamp of that frame; I
can then snapshot every stage on that exact input. Without one, the measurement says the stages between
skeleton and render are not the problem.

---

## 5. Fix

**Task 1: implemented** — the roll state machine of §2.2, in `Runtime/Retargeting/ArmAimSolver.cs`.

**Task 2: no fix implemented, deliberately.** The brief says *"Do not fix anything until the stage is
identified"*, and no stage was found bad. Implementing something here would be guessing.

Files changed:

| file | change |
|---|---|
| `Runtime/Retargeting/ArmAimSolver.cs` | hysteresis (`BendSinEnter`/`BendSinExit` replace `BendSinMin`), the continuity guard + debounced rate-bounded re-acquisition (`ResolveNormal`, `Orthogonalise`), the forearm roll-degeneracy fallback, two new `ArmNormalSource` values, four new fields on `ArmRollState` |
| `Tests/EditMode/ArmAimSolverTests.cs` | +8 tests (§2.4) |
| `python-sidecar~/pose_stage_udp.py` | **new** — six-pose deterministic UDP driver |
| `python-sidecar~/video_udp_sender.py` | **new** — video → RTMW3D → UDP driver |
| `python-sidecar~/ab_roll_v1_v2.py` | **new** — V1/V2 replay on identical recorded input |

**Not changed:** the arm direction solve, Python/OAK-D/tracking, legs, torso, head, hands, IK,
`PoseSpaceConverter`, `flipQuat`, `eulerSigns`, P0/P1-1/P1-2/P1-3/F-08, `Scenes/Bootstrap.unity`.
The Task 2 forensics required **no** production code at all — every stage was reachable by reflection.

---

## 6. Regression

| item | status |
|---|---|
| Compilation | **0 errors, 0 warnings** (`validate_script` on both changed files, plus a forced full recompile with a clean console) |
| **EditMode suite** | **NOT RUN — see the note below.** |
| Offline scenario validation | **15/15 checks pass** against a faithful Python port of the state machine (`arm_v2_evidence/sim_armaim_v2.py`), covering all seven required scenarios |
| Direction error | **unchanged**: A = 0.000° on all six deterministic poses; 0.0485° median under video motion (slerp lag). Test 7 asserts < 0.05° across 2000 adversarial frames including guarded/slewed ones |
| Elbow error | 0.00–0.03° on the deterministic poses; 0.0060° median on video |
| Symmetry | both arms measured independently in §3; left/right stage errors agree to the numerical floor |
| **Allocations** | **0 bytes** — measured over 400 000 solves with `GC.GetTotalMemory` |
| **Performance** | **1.593 µs/solve**, i.e. **3.186 µs/frame for both arms**. Against a same-session inline re-implementation of V1's roll branch (diagnostics included in both, so the comparison is fair): V1 0.784 µs → V2 1.593 µs, **+0.809 µs/solve**. Still below the pre-V1 Kalidokit arm branch's 3.435 µs/frame recorded in the V1 doc §14. Removing an `acos` from the guard's hot path (comparing cosines against fixed bounds instead of angles) saved 9 % of that |
| Live validation | video-driven run, 25 s, 6654 arm-samples: 0 roll steps > 90°, guard engaging 0.2–0.5 %, B = 0.000000° |
| Bone lengths | **0.000000 m** delta, control vs rendered, every pose and every video sample |
| Unity console during the runs | **0 errors** |

### The EditMode suite was not run, and why

`run_tests` over the MCP bridge **closes this Unity Editor**. It did so twice, once before any of my
changes were made (with the tree untouched), so the crash is the Test Runner path on this project, not
these edits. I stopped calling it after the user reported the shutdown.

Substitute evidence used instead: a clean compile of both files, plus the Python port of the state
machine passing all seven required scenarios (which caught a real 60°-snap bug before Unity ever saw the
code). **This is weaker than running the suite and should not be treated as equivalent.**

To close it properly the suite should be run in **batch mode**, which is a separate headless process and
cannot touch a running Editor. It needs Unity closed for ~3 minutes:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe" -batchmode -runTests -projectPath "D:/Unity/viitorx-vrm-avtar-unity-base-project" -testPlatform EditMode -testResults "D:/Unity/viitorx-vrm-avtar-unity-base-project/editmode-results.xml" -logFile -
```

---

## 7. Observations recorded, not acted on

Three things this task surfaced that are outside its scope. None is fixed here.

1. **`AppBootstrap.boundAnimator` resolves to control bones, not the skinned skeleton** (§1a). It makes
   `model_log.jsonl` a control-rig log despite its comments. Harmless to behaviour — it is diagnostic
   only — but it mislabels every downstream analysis that trusts those field names.
2. **`rawLeftHand` / `rawRightHand` are control bones too**, contradicting their own comment ("Raw
   skeleton hands … applied AFTER Process"). `ApplyWrist` therefore writes to a control bone that
   `Process()` will overwrite. Currently inert because `wristRotationWeight = 0`, so nothing visible —
   but it would bite the moment palm work enables the wrist. Worth fixing *before* palm work starts.
3. **Restarting a sidecar mid-Play silently wedges tracking.** `PoseBuffer.Push` rejects any packet whose
   `seq` is below the newest held ("a late packet cannot be inserted mid-ring") — correct for a real-time
   buffer, but a driver restarting at seq 0 while Unity stays in Play has **every** packet dropped until
   it counts back up. Measured here: `ReceivedCount` climbed to 6847 with `ParseErrorCount` 0 while the
   applied pose sat **60 s stale**. It cost me two mislabelled captures before I found it. The test
   drivers now seed `seq` from the wall clock. If the production sidecar can ever be restarted without
   restarting Play, this deserves either a seq-reset detection or a documented "restart Play too".

---

## 8. Verdict

**Task 1 — roll jitter: FIXED, measured.** 6 → 0 roll steps above 90° and 145.56° → 65.29° worst step, on
byte-identical input, with the offline model cross-checked against the shipped C# to within 2°. Bone
directions provably untouched. 0 bytes allocated. Conditional only on the EditMode suite being run in
batch mode (§6).

**Task 2 — where the avatar goes crooked: ANSWERED, no fix warranted.** Not the solver, not the control
rig, and not the VRM humanoid mapping: the rendered skinned bone matches the source landmark direction to
**0.000000°** across 13 308 video samples and all six deterministic poses, with bone lengths constant to
six decimals. The remaining error lives upstream in the landmarks, exactly where the V1 live report put
it. Per the brief, nothing was changed on the strength of a visual impression.

The product requirement — *the rendered VRM avatar must visually and geometrically follow the human* — is
met **geometrically, with respect to the tracked skeleton**. It is not yet met with respect to the human,
and the gap is entirely upstream of Unity.
