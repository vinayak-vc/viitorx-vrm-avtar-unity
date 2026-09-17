# F-33 — Per-person filters: the measured single-person signal chain, once per identity

```text
VERDICT: BUILT AND MEASURED. Every tracked person now carries a full P0 + P1-1 + F-22 chain keyed
         to their track id. At 30 fps the multi-person chain IS the single-person chain, same
         modules and same constants; below 30 fps it is better, because it knows its own rate.
TESTED:  46/46 new unit tests (270/270 across the sidecar suite); a four-way A/B on the
         seven-dancer clip through the real device, all four runs on provably identical input;
         a ground-truth bench that measures LAG as well as jitter.
COST:    0.88 ms per person-frame, against 20.7 ms for the pose solve it follows.
NOT RUN: never with real people in front of the camera. Nothing has been rendered on screen.
         The stereo depth in the video A/B is synthetic - see §6.
```

F-32 shipped multi-person with a hole it named out loud: *"Multi-person output is RAWER than
single-person: none of the per-joint temporal filters run per track yet. That is the top
follow-up."* This closes it.

---

## 1. Why the filters could not simply be turned on

Every stage in the single-person chain is a **temporal** filter. P0's One-Euro carries a velocity
estimate per coordinate, its displacement cap carries the last accepted position, its hold carries a
countdown. P1-1 carries a state machine per joint. F-22 carries a bend history per chain. All of it
is *per person*, and none of it is in the frame.

`PersonTracker.update()` returns people sorted most-established-first. A person's **index in that
list changes** whenever somebody else gains a hit. So a filter bank addressed by index hands person
A's One-Euro history to person B on that frame: every joint appears to teleport, and the
displacement cap then spends several frames slewing the two bodies into each other. The pool is
therefore keyed on **track id**, which is stable and never reused — the one property a temporal
filter needs. `tests/test_person_filters.py` drives that exact re-ordering.

Unity already made the same choice independently: `TrackedStage.UpdateCrowd` keys `SkeletonPose` on
track id for exactly this reason. F-33 is the sidecar half of a decision already taken downstream.

---

## 2. The one thing that is not a straight copy: sample rate

`KeypointSmoother` takes `freq` once at construction, and the single-person sender leaves it at the
default **30.0**. That is true there — that loop runs at camera rate.

It is **not true here**. Three people cost three sequential 20.7 ms pose solves, so the loop runs at
~16 fps, and a person below the `--max-poses` cap is updated rarer still.

One-Euro derives velocity as `delta * freq`. A filter told 30 while actually sampled at 16
over-estimates speed by **1.9×**, which inflates its adaptive cutoff, which makes it **open up**
exactly when it should be damping. So the naive port is worse on *both* axes at once — not the usual
smoothing trade-off:

| actual rate | told `freq=30` (the naive port) | told the truth |
|---|---|---|
| 30 fps | 13.5 mm jitter / 167 ms lag | 13.5 mm / 167 ms — identical, nothing to fix |
| 16 fps | 32.8 mm jitter / 250 ms lag | 25.1 mm / 188 ms |
| 10 fps | 59.7 mm jitter / 300 ms lag | 39.1 mm / 200 ms |

Each person therefore **measures their own update cadence** and retunes their own filters from it —
the median of the last 15 gaps, so one skipped frame cannot move the estimate. That handles the
`--max-poses` case for free: a person solved every third frame simply reports a third of the rate.

`smoothing.py` gained a `set_freq()` method to make this possible. It is **purely additive** — the
diff contains zero deleted lines, and the single-person sender never calls it.

*Evidence: `docs/evidence/f33/freq_probe.txt`.*

---

## 3. What it buys, measured against known ground truth

A recorded clip has no truth, so "the output moved less" cannot distinguish better tracking from
over-smoothing: **any** filter can drive jitter to zero by refusing to move, and a filter that adds
300 ms of lag scores beautifully and is unusable. `tools/diagnostics/f33_filter_bench.py` synthesises
two people whose exact position is known at every instant — one standing at 2.2 m reaching, one
walking at 3.4 m — with a modelled sensor: depth quantisation growing as z² (19 mm at 2.2 m, 45 mm
at 3.4 m), 6% of keypoint-frames losing depth, 0.4% taking a 1.5 m spike.

That model assumes 1/8-pixel subpixel. **Production stereo has subpixel OFF**
(`oak_depth.STEREO_CONFIG`), so the real quantisation step is up to 8× coarser — partly recovered by
the HIGH_DENSITY preset and the F-08 percentile window, by an amount nobody has measured. The bench
therefore **understates** the noise the filter faces. That shifts every arm of the A/B together,
which is what matters for comparing them, but it means the absolute millimetres below are optimistic.

The people are synthetic. **The filters are the production modules, unmodified.** These numbers
describe signal processing, never the camera and never RTMW3D.

| 16 fps (three people — the real multi-person case) | jitter sd | jitter median | lag | rms |
|---|---|---|---|---|
| no filters (raw F-32) | 131.4 mm | 32.4 mm | 0 ms | 37.0 mm |
| F-33, rate pinned at 30 | 11.4 mm | 21.5 mm | 312 ms | 52.8 mm |
| **F-33, adaptive rate** | **10.8 mm** | **19.3 mm** | **250 ms** | **40.7 mm** |

| 10 fps (a person starved by `--max-poses`) | jitter sd | jitter median | lag | rms |
|---|---|---|---|---|
| no filters | 141.5 mm | 33.5 mm | 0 ms | 33.6 mm |
| F-33, rate pinned at 30 | 18.0 mm | **35.6 mm** | 400 ms | 63.5 mm |
| **F-33, adaptive rate** | 17.0 mm | 29.9 mm | **200 ms** | 42.9 mm |

**Read the 10 fps pinned row.** Its jitter *median* is 35.6 mm — **worse than not filtering at all**
(33.5 mm) — while costing 400 ms of lag. That is the naive port, and it is the single strongest
argument for measuring the rate rather than assuming it. Adaptive takes the same case to 29.9 mm at
half the lag.

Two figures are reported honestly rather than buried:

- **Lag is 233 ms at 30 fps.** That is not introduced by F-33 — it is the accepted single-person
  tuning (`--min-cutoff 0.5`, `--beta 0.4`) doing what it was tuned to do. At 30 fps the
  multi-person chain *is* the single-person chain: same modules, same constants, same result.
- **RMS barely improves** (34.1 → 35.9 mm at 30 fps). The noise is zero-mean, so smoothing does not
  reduce mean absolute error much when the subject is moving. What it removes is the *shake*, which
  is what a viewer sees: jitter falls 20× on the sd and 3× on the median.

*Evidence: `docs/evidence/f33/filter_bench.txt`.*

---

## 4. The feet and the head were in no filter group at all (ADR-071)

The chain is a copy of the single-person order, with **one deliberate difference**, and it was found
by looking at what the first filtered run still got wrong rather than by reasoning about it.

The single-person sender's limb set is `{5..16} ∪ {91..132}` — trunk, arms, legs, hands. It does not
contain the **feet** (WholeBody 17–22) or the **head** (0–4). Those slots still get the light
image-plane One-Euro, but no heavy depth cutoff, no bounded hold, and no tightened displacement cap.
Both then produced the worst artefacts in the filtered run, **for opposite reasons**:

| | fails with | why | what fixes it |
|---|---|---|---|
| feet | `src=0` | no depth that frame, no hold, so the point is rebuilt from the model's raw monocular z every frame | the bounded hold |
| head | `src=1` | depth *was* sampled — from a window straddling the edge of the head, catching the wall behind it | the displacement cap |

Counting single-frame steps over 300 mm — a joint crossing 300 mm in 33 ms is moving at 9 m/s and is
not a person — over 2060 person-frames of identical input:

| | steps > 300 mm | worst single step |
|---|---|---|
| no filters (raw F-32) | 2158 | 2091 mm |
| filters, single-person grouping | 630 | 2018 mm (a heel) |
| **filters + feet + head** | **77** | **960 mm** |

The last step changed the distal, trunk and hip figures by **0.0% to the decimal** and emitted
**exactly the same 40 200 joints**. It moved only the joints it targets, which is what a correct
grouping fix should look like and is the reason to believe it rather than a tuning accident.

This matters beyond tidiness. F-29 put the feet on screen and `FootprintsExperience` reads heel and
toe directly, so an unfiltered heel is not a number in a log — it is a footprint appearing two metres
from the person who made it.

Both groups have off-switches (`--no-filter-feet`, `--no-filter-head`) that restore the
single-person grouping exactly, which is how the table above was produced.

---

## 5. The end-to-end A/B on the seven-dancer clip

`tools/video/f32_multiperson_video.py`, two passes over `456.webm`: 710 payloads, 2060
person-frames, real VPU detector, real tracker, real RTMW3D.

| | median step | p95 step | worst |
|---|---|---|---|
| all joints, no filters | 45.3 mm | 332.1 mm | 2091 mm |
| all joints, F-33 | **10.4 mm** | **65.3 mm** | **960 mm** |
| distal (wrists/ankles/elbows/knees) | 56.1 → 13.4 mm | 363.5 → **67.6 mm** | 1576 → **447 mm** |
| hip origin | 72.5 → 12.6 mm | 420.0 → 77.7 mm | 1439 → 511 mm |

Cost: **1.1 percentage points** of emitted joints (60.2% → 59.1%) — that is the chain *refusing* to
send a joint it does not believe, which is the contract: a refused joint arrives as `[0,0,0,0]`,
byte-identical to a real occlusion, and Unity's P0 LimbGate makes the final call.

**These are dancers, so much of the raw step is real motion**, and the table cannot on its own
separate that from noise. The step-over-300 mm count and the ground-truth bench are the two figures
that can.

*Evidence: `docs/evidence/f33/video_ab_456.txt`.*

---

## 6. What is not true here, stated plainly

- **Never run with real people.** All evidence is recorded video and synthetic trajectories.
- **The video A/B's depth is synthetic.** A clip has no stereo, and without depth the smoother has
  nothing to smooth and P1-1 starves every tracked joint to LOST within a few frames. So the harness
  paints each tracked person's box with their apparent-height distance plus a stereo-like noise
  model, and reads it back through the real `oak_depth.backproject` and F-08 surface sampler. A
  `src=1` flag in that run means *"sampled the synthetic depth frame"*, never *"measured by stereo"*.
- **Nothing has been rendered on screen.** Still true from F-30 onward.
- **A slow, confident drift is still not caught.** 850 mm injected over 25 frames is followed to
  847 mm. No single 34 mm step trips the displacement cap and P1-1's residual gate adapts to the
  joint's own scale, so it looks like motion to every layer that is running. P1-4 catches it
  (264 mm with `--filter-recovery`) and P1-4 is **rejected for production**. Multi-person inherits
  the single-person blind spot, deliberately. This is pinned by a test that asserts the *limitation*,
  because a green tick hiding it would be worse than a red one.
- **P1-1's horizons are counted in frames, and this loop is slower.** A 6-frame prediction horizon
  spans ~375 ms here against ~200 ms in the single-person loop. The P0 smoother adapts to the real
  rate; P1-1 does not, and is more forgiving here as a result.

---

## 7. A harness defect found on the way

`f32_multiperson_video.py` drove `PersonTracker` from the **wall clock**. A file replay that reads
the wall clock is not reproducible — the tracker's constant-velocity prediction is scaled by `dt`, so
the same clip yields different identities on a faster machine, or simply on a second run. It showed
up as an A/B whose two halves disagreed about which ids existed.

Both the tracker and the filters now run on the video's own timeline. Consequences:

- The four A/B runs agree **to the packet** on how many people existed, which ids they had, and
  which frames they appeared in. That is printed in the evidence file and is what makes the
  differences attributable to the filters.
- **F-32's "11 ids for 7 people" video figure was machine-dependent** and is superseded: the
  deterministic harness gives **9 ids across two passes**. The live sender is unaffected — there the
  wall clock *is* the frame clock.

---

## 8. Verification

| | |
|---|---|
| new unit tests | **46/46** (`tests/test_person_filters.py`) |
| whole sidecar suite | **270/270** assertions across 7 files |
| Unity assemblies | Core, Tracking, SkeletonShow, Experiences — **0 errors** |
| `wholebody_udp_sender.py` | **byte-identical** (`git diff` empty) |
| `smoothing.py` | additive only — **zero deleted lines** |
| Unity runtime | **no change required**; the wire shape is unaltered |

The new tests cover AGENTS.md §8's required list — steady state, legitimate fast motion, an isolated
spike, a high-confidence wrong value, a short gap, a long gap, recovery — plus the pool lifecycle,
the re-ordering trap, the per-person M11 hold, and a **config-drift guard** that reads
`wholebody_udp_sender.py`'s source and fails if any shared default stops matching `FilterConfig`.

One test caught a real defect during development: the pool released a person's chain while they were
still being tracked, because the idle timer and the rate estimate shared one clock, and a frame whose
pose failed advanced neither. They are now two clocks — `last_seen_at` for lifecycle, `last_update_at`
for the rate — and the reason is written where the fields are declared.

---

## 9. Files

**New:** `person_filters.py`, `tests/test_person_filters.py`,
`tools/diagnostics/f33_filter_bench.py`.
**Changed:** `smoothing.py` (additive `set_freq`), `multiperson_udp_sender.py`,
`tools/video/f32_multiperson_video.py`.
**Unchanged:** `wholebody_udp_sender.py`, and every file under `Runtime/`.

New flags, all with safe defaults and an off-switch for A/B (AGENTS.md §10): `--filters`,
`--adaptive-rate`, `--filter-feet`, `--filter-head`, `--filter-recovery`, and on the video harness
`--synth-depth` and `--seed`.

---

## 10. What to do next

1. **Two real people in front of the camera.** Everything above is video and synthesis. This is the
   top item and has been since F-32.
2. **Render a scene.** Still nothing on screen since F-30.
3. **Per-person `st` in Unity.** The wire now carries real per-joint tracking states *per person* —
   it was all `-1` before F-33. The root person's states already reach the existing Trust HUD
   unchanged; a crowd HUD would need `PersonPose` to carry them.
4. **Consider the hip origin.** The 77 residual implausible steps are dominated by whole-body origin
   shifts of ~230 mm, where the hip moved and every hip-relative landmark moved with it. Hips sit on
   the global 1.5 m displacement cap, deliberately, in both senders. Three events over 2060
   person-frames is not enough evidence to retune a constant tuned on live data (AGENTS.md §10).
