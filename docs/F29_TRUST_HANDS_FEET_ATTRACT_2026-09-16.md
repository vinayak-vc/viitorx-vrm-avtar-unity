# F-29 — Trust HUD, hands, feet and an attract loop

```text
VERDICT: BUILT AND VERIFIED HEADLESSLY. Two new modes, the sidecar's own reasoning on the wire,
         hands and feet drawn for the first time, and an attract loop so an empty room is not a
         black screen.
SCENE:   Assets/Games/viitorx-vrm-avtar-unity/Scenes/SkeletonShow.unity  (unchanged asset)
TESTED:  23/23 EditMode unit tests; 29/29 headless acceptance checks driving the REAL
         OakDUdpPoseProvider over a REAL socket with recorded production packets.
NOT RUN: never on a live OAK-D, and never rendered on screen by a human. See §8.
```

> **Sequencing note.** The live-camera pass F-28 §7.1 called for was explicitly skipped: the
> skeleton is driven from `transform.position` and has been exercised many times, so the marginal
> value of another live run was judged lower than the cost. Everything here is therefore verified
> against recorded video through the production wire, and §8 says plainly what that does not cover.

---

## 1. What was actually missing, and what was only hidden

Two of the four items turned out to need **no new sensing at all**. That is the most useful finding
in this report, because it means the gap was presentation, not capability.

| item | expected work | actual work |
|---|---|---|
| **Feet** | "send the existing `WB_FOOT` data through UDP" | **already on the wire.** `build_body_landmarks` has always emitted `R.FOOT_TO_JOINTID` — heels to JointId 29/30, big toes to 31/32 — alongside the COCO-17 body. Measured 431/431 and 330/330 frames. No sidecar change was made or needed; Unity simply never drew those four slots. |
| **Hands** | render 21 landmarks | **already parsed.** `OakDUdpPoseProvider.ReadHand` has always read all 21 points per hand, derived five curl scalars for the avatar's finger bones, and discarded the positions. The work was publishing them, not acquiring them. |
| **Trust HUD** | expose tracker state | genuinely new on the wire — three fields. |
| **Attract** | idle state | genuinely new, and the gap F-28 §7.4 named. |

---

## 2. The trust channel (sidecar → Unity)

Three **optional, read-only** fields, added the same way `sid` was (F-20A), so a consumer that
ignores them behaves exactly as before and nothing upstream reads them back.

```text
"st":  [-1..4 × 33]   P1-1/P1-4 state per JointId.
                      -1 NO_TRACKER, 0 TRACKED, 1 WEAK, 2 PREDICTED, 3 LOST, 4 RECOVERING
"own": "LOCKED"       F-21 ownership state. ABSENT under --no-ownership.
"lat": 41.3           measured camera-timestamp → payload-built latency, ms
```

**`-1` is not padding, it is a claim.** Only the 12 joints in `joint_tracker.DEFAULT_TRACKED` have a
tracker. Reporting the other 21 as TRACKED would overstate what the system knows about them; the
HUD draws them grey and says "no tracker". The acceptance harness asserts exactly 21 per frame.

**`own` absent ≠ not locked.** Under `--no-ownership` the field is omitted entirely, so the HUD can
distinguish "identity tracking is not running" from "it is running and has not locked". Those mean
opposite things to an operator and one must never be rendered as the other.

**The values are captured AFTER P1-4.** Reading them before recovery would report LOST for a joint
that was reconstructed and sent — the HUD would contradict the skeleton drawn next to it.

### The video harness now runs the real trackers

`tools/video/f23_video_to_unity.py` previously ran neither P1-1 nor F-21, so the only testable path
had no states to report. It now runs both, with two limits stated in the file and repeated here
because they bound what a green run proves:

* **Geometry is not written back.** Production feeds P1-1's corrected position into `xyz_cam` and
  sets `measured=True`. Doing that here would flip every `src` flag to 1 and claim a stereo
  measurement that does not exist. States-only keeps this harness's emitted geometry
  **byte-identical to before**, so no earlier measurement taken from it is invalidated.
* **`depth_valid=True` is passed** although nothing was measured. That input feeds only P1-1's
  suspicion score; passing False for all 133 keypoints would drive every joint permanently WEAK, and
  a HUD reading "nothing is trustworthy" at all times conveys less than no information. The honest
  signal is `src`, which stays 0 everywhere and which the HUD shows.

So the video path exercises P1-1's **temporal/geometric** validation and F-21's machine for real. It
does **not** exercise the depth-hole path.

---

## 3. Mode 4 — TRUST HUD

**Colour means TRUST here, not speed.** This deliberately breaks the rule F-28 §2 established for
modes 1–3, because the entire subject of this mode is what the pipeline believes. Two meanings on one
channel would be unreadable, so speed is dropped rather than encoded somewhere else and half-noticed.
The legend is permanently on screen for exactly that reason.

```text
green TRACKED   amber WEAK   blue PREDICTED   red LOST   violet RECOVERING   grey no tracker
brown halo = depth inferred, not measured   (a separate axis: a joint can be TRACKED on a guess)
```

A bone takes the **worse** of its two endpoints — a bone is only as good as its weaker end, and an
average would hide the joint worth seeing.

The panel reports joint-state counts, valid joints, depth coverage, ownership, latency, floor and
per-joint camera depth for both wrists and both hips.

**How to demonstrate it:** have the subject occlude one arm. The joints go WEAK → PREDICTED as P1-1
extrapolates → LOST if the occlusion outlasts the prediction horizon, then RECOVERING as P1-4 blends
them back. That sequence is the most expensive engineering in the project and was, until now,
entirely invisible.

### Latency is reported honestly, in two halves

`sidecar (lat) + wire/present (age) = end to end`. Measured **53.4 ms mean** on fresh frames.

Two traps were hit and fixed while building this:

1. **The first version mixed two clocks.** Age was computed as `UtcNow − arrivalStopwatchTime`,
   and the provider's arrival stamp is a Stopwatch since start, not an epoch. The result was
   1.79 × 10¹² ms — the epoch itself. `SendEpochSeconds` and `ArrivalSeconds` are now separate,
   separately named fields with the trap documented on both.
2. **A stalled stream made latency climb forever.** Past 500 ms — the provider's own StaleHold
   threshold, reused rather than reinvented — the HUD reports *"STREAM STALE, newest pose is N s
   old, the body on screen is being held, not tracked"* instead of a growing number that reads as
   "the pipeline got slow" when the truth is "the pipeline stopped".

---

## 4. Mode 5 — HANDS

All 21 landmarks per hand, the palm plane as a disc, the thumb–index link, and per-hand pinch and
finger count. The body stays drawn and dimmed behind, or the hands float with no indication of whose
they are.

**Every gesture measure is scale-invariant, and that is not stylistic.** Hand landmark depth inherits
the body's depth estimate, so apparent hand SIZE varies with how well that estimate is doing:

```text
1.4 m subject   palm  84 mm mean        a real hand
2.9 m subject   palm 165 mm mean, p95 362 mm       not a hand at all
```

A pinch threshold in absolute millimetres would therefore fire on **distance**, not intent. Dividing
by the hand's own palm length cancels the error that matters.

**The plausibility gate is the honest quality signal.** A palm outside 50–160 mm is not a hand shape
whatever the model reported, and the fraction passing is the cleanest statement of whether hand
tracking is working at all:

```text
1.4 m   854/862 = 99.1% plausible
2.9 m   296/494 = 59.9% plausible
```

When a hand fails the gate the mode draws a **rejection marker instead of the hand**. A confidently
rendered 36 cm hand is worse than none: it invites a viewer to conclude the tracking is broken in
some interesting way, rather than that the subject is out of range.

Thresholds are derived from the measured distributions, not chosen:

```text
pinch enter 0.35 / exit 0.48 palms    measured median of a non-pinching hand is 0.52, p5 is 0.25
finger extend enter 1.45 / exit 1.30  measured open-hand medians 1.48-1.71, p5s 1.09-1.30
```

> **Calibration caveat.** These come from the *distribution of ordinary hand poses* in two dance
> clips, not from a recording of someone deliberately pinching on cue. They separate "closed" from
> "open" correctly on that data. A proper calibration pass is the right way to tighten them and has
> not been done. The little finger is the weakest separated and will miscount first.

---

## 5. Feet, and whether floor contact is actually reliable

The four contact points are now drawn, and each foot is a triangle (ankle–heel–toe) rather than a
single ankle-to-toe line, so it has a direction on the floor and can be seen to roll.

```text                        1.4 m subject      2.9 m subject
per-frame lowest foot          sd 36.0 mm          sd 66.0 mm
2 s rolling floor              sd 21.0 mm          sd 80.6 mm
ankle → true contact          +127 mm             +81 mm
```

**The answer to "is it reliable": at close range yes, at distance no.**

* At 1.4 m the smoothed floor is stable to **21 mm**, which is comfortably good enough for footstep
  ripples, floor effects and standing a figure on a surface.
* At 2.9 m it degrades to **81 mm**, and — the diagnostic detail — **smoothing makes it worse**
  (66 → 81 mm). A longer window helping is the signature of noise; a longer window hurting is the
  signature of *drift*. At 2.9 m the error is a slow wander, not jitter, so no amount of filtering
  recovers it. Do not build true contact detection at that range.
* Caveat: no stereo on this path, so vertical position comes from pixels scaled by an estimated hip
  plane. Real measured depth will likely do better; that has not been tested.

**Mode 3's ripples have been spawning 81–127 mm above the ground the whole time**, because the floor
was taken from the lowest ankle. `SkeletonPose.FootContacts` deliberately excludes the ankles.

The estimator is **asymmetric on purpose** — 0.05 s falling, 0.6 s rising. The floor is where the
lowest foot has *recently been*, not where the feet are now; a symmetric filter would let a lifted
foot drag the floor up with it.

### The 153 mm sink nobody could see

With a real floor measurement, a pre-existing bug became visible: `bodyOrigin` fixes the mid-hip at
0.95 m, so where the feet land depends entirely on the subject's hip-to-floor length. Measured on the
regression clip, **the figure's feet finish 153 mm below the floor grid** — it has been standing *in*
the floor in every mode since F-28, invisible because the feet were not drawn.

`groundToFloor` (default on, `G` toggles) corrects it by sliding the staging **origin**, never the
pose — moving a joint to meet the floor would be inventing tracking data; moving the figure is a
camera decision. It settles to **0.3 mm with no overshoot**, verified by reproducing the loop in a
test. The 1.5 s time constant is the design: it is more than 2× slower than the slowest part of the
floor estimator it feeds back through, so the two cannot interact.

---

## 6. Attract loop

**Attract is a synthetic pose SOURCE, not a separate renderer.** It emits an ordinary `PoseFrame`
through the identical path a tracked person's takes. Consequences, all of which are the reason for
the choice: the attract loop demonstrates the **real modes** rather than a screensaver that would
drift away from them; every mode gets attract behaviour for free, including modes not yet written;
and a visitor sees exactly what they are about to control.

It must never be mistakable for a tracked person, so: it is labelled `ATTRACT - synthetic figure,
nobody tracked` in the HUD, it moves on three slow incommensurate periods so the loop never visibly
repeats, it carries **no telemetry** (nothing to report about a body that was never tracked), and it
leaves `HasRootPosition` false so no depth readout invents a distance for it. It fills exactly the 17
joints it models and leaves the other 16 explicitly unobserved — emitting them at the origin with
confidence is the exact fault F-27 exists to reject.

`PresenceGate`'s two delays are deliberately very different:

* **enter 0.4 s** — a visitor who steps up and waits concludes it is broken. A false positive costs
  a moment of flicker; hesitation costs the visitor.
* **leave 3.0 s** — the stream drops poses for ordinary reasons, and dumping a present visitor back
  to attract mid-interaction is the worst failure this class can produce. Deliberately longer than
  the provider's own 2 s stale failsafe so this never fires first and pre-empts handling the
  tracking layer already does. A test asserts that ordering.

---

## 7. What changed

```text
python-sidecar~/
  wholebody_udp_sender.py          + build_joint_states(), + st/own/lat on the wire, docstring
  tools/video/f23_video_to_unity.py + real SkeletonTracker + TargetOwnership, + trust fields

Runtime/Core/Models/
  TrackingTelemetry.cs             NEW   the trust channel, consumer side
  RawHandFrame.cs                  NEW   21 landmarks per hand, as positions

Runtime/Tracking/OakD/
  OakDUdpPoseProvider.cs           + ParseTelemetry, + PublishRawHands,
                                   + TryGetTelemetry / TryGetRawHands

Runtime/SkeletonShow/
  TrustHudMode.cs                  NEW   mode 4
  HandsMode.cs                     NEW   mode 5
  HandPose.cs                      NEW   scale-invariant gesture layer
  AttractState.cs                  NEW   AttractSkeleton + PresenceGate
  SkeletonPose.cs                  + foot bones, floor estimator, camera depth, telemetry
  SkeletonShowBootstrap.cs         + modes 4/5, attract wiring, grounding, panels, keys

Tests/EditMode/
  SkeletonShowF29Tests.cs          NEW   23 tests
  VirtualMirror.Tests.asmdef       + VirtualMirror.SkeletonShow reference
```

**Controls:** `1`–`5` modes, `TAB` cycles, `H` humanized skeleton, `A` attract, `G` ground-to-floor.

**Nothing in the mirror app changed.** `VirtualMirror.App`, `.Retargeting` and `.Diagnostics` all
still compile, and the two new Core types are additive — neither `PoseFrame` nor `HandFrame` grew a
field, so no existing consumer sees a different contract.

---

## 8. What this does NOT prove

1. **Never run on a live OAK-D.** Everything is recorded video through the production wire. The
   trust HUD's depth row has therefore only ever shown `0/33 MEASURED`; the MEASURED path is
   correct by construction and **has not been seen**.
2. **Nothing here has been rendered on a screen by a human.** Three Unity Editors were open and
   holding the project lock, so verification is compile + headless logic + real-wire parsing. Every
   geometric and numeric claim is tested; **no visual claim is** — colours, sizes, bloom
   interaction, panel legibility and whether the attract figure reads as intentional are all
   unverified.
3. **Single person only, and that is unchanged.** RTMW3D-x is single-person top-down with no
   detector and no track id. F-21's live two-person acceptance failed on 2026-09-16. Nothing here
   improves that and nothing here should be deployed where a second person can enter frame.
4. **Hand gesture thresholds are uncalibrated** in the sense of §4 — derived from pose
   distributions, not from cued gestures.
5. **Foot contact at ≥ 2.9 m is not usable** for true contact detection, per §5.

## 9. Evidence

```text
docs/evidence/f29/
    measurements.txt             per-clip states, feet coverage, floor stability, hand plausibility
    acceptance_headless.txt      29/29 checks, real provider, real socket, recorded packets
    unit_tests.txt               23/23 EditMode tests
    F29AcceptanceHarness.cs.txt  the harness source, for reproduction
    replay.py.txt                deterministic packet replay (also strips hands - see below)
```

**Reproducing the acceptance run.** The provider's *pre-existing* hand path calls
`Quaternion.Slerp/Angle/LookRotation`, which are Unity native (ECall) and cannot execute outside a
player — so the socket test replays packets with `--no-hands`, and hand landmarks are validated
separately by running `HandPose` over the same recordings. That split is a property of code this
work did not write, and is why hands appear in two places in the evidence rather than one.
