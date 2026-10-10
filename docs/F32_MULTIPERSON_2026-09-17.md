# F-32 — Multi-person detection, tracking, and the first experience that needs two people

```text
VERDICT: BUILT AND WORKING END TO END, on recorded video through the real device.
         Detect N -> track N with stable ids -> pose N -> one wire -> N bodies in Unity.
TESTED:  19/19 tracker unit tests; 7/7 Unity acceptance checks driving the real provider and
         TrackedStage with real multi-person packets; 31/31 existing Unity tests and 192/192
         sidecar assertions still pass; 7/7 assemblies compile.
NOT RUN: never with two real people in front of the camera. Nothing has been rendered on screen.
```

> **This report opens with a correction to an earlier one.** See §1. It matters more than the
> feature, because it was wrong in a way that looked right.
>
> **Superseded in one respect:** this report's biggest stated gap — that multi-person output is
> RAWER because no per-joint filter runs per track — was closed by **F-33**
> ([report](F33_PER_PERSON_FILTERS_2026-09-17.md), ADR-071).

---

## 1. The correction: `456.webm` has SEVEN people in it

F-29 treated `456.webm` as *a single subject at 2.9 m* and drew range conclusions from it:
"hands 59.9% plausible at 2.9 m", "floor contact 81 mm at 2.9 m". Both were attributed to distance.

**The clip contains seven dancers.** There is no single subject in it, and the "2.9 m" hip depth is
an artifact — hip depth on the video path is estimated from apparent body span, so a crop landing on
a smaller or further dancer reports "farther".

The tracked body in that clip is **not any one person**:

| clip | people | shoulder-width CV | range | verdict |
|---|---|---|---|---|
| `video.webm` | 1 | 4.0% | 0.302–0.370 m | one person + noise |
| `456.webm` | **7** | 22.8% | **0.068–0.455 m** | a chimera |

A 68 mm shoulder is anatomically impossible; 455 mm is a large adult. In pixels the tracked shoulder
width swings **30.6×**. The single-person crop migrates between dancers — smoothly, with no
frame-to-frame jump over 172 mm, which is precisely the F-21 silent-hand-off failure already
documented in ADR-061.

**So the range claims were wrong in their cause.** A genuine single subject at 1.96 m (`123.webm`)
gives **93.7% plausible hands** and a 90 mm mean palm — close to the 1.4 m figures. Hand tracking
does not collapse at 2 m. The real single-subject limit beyond ~2 m is **untested**.

This has been corrected in the F-29 report, the README, the CHANGELOG and every code comment that
cited those numbers. Evidence: `docs/evidence/f32/identity_contamination.txt`.

**Why it matters beyond the correction:** the single-person pipeline does not fail loudly on
multi-person input. It emits a plausible-looking skeleton that belongs to nobody, and those numbers
reached a report as if they measured one person. That is the strongest argument for this feature.

---

## 2. What was measured before anything was built

### 2.1 The detector

Both candidates were run on the OAK-D VPU over all 356 frames of the seven-dancer clip.

| detector | thresh | mean | median | max | character |
|---|---|---|---|---|---|
| BlazePose (already on disk) | 0.50 | 1.35 | 1 | 3 | misses 5 of 7 |
| BlazePose | 0.15 | 5.75 | 5 | 12 | recall OK, many false positives |
| **retail-0013** | 0.40 | 5.35 | 5 | 10 | tight boxes, a few misses |
| **retail-0013** | 0.20 | 7.06 | **7** | 14 | median exactly the ground truth |

`person-detection-retail-0013` wins clearly — tight, upright, well-separated boxes (0.99/0.96/0.94 on
clear dancers) where BlazePose gives sloppy overlapping ones. BlazePose is a *single prominent
subject* detector and its 224×224 input leaves each dancer ~18 px wide.

> **A method warning, recorded because it produced a wrong conclusion first.** An initial retail run
> measured **0.95** people per frame. That was a preprocessing bug: a 1080×1920 portrait frame
> squashed into the model's 544×320 landscape input, distorting every person ~6×. Letterboxing took
> the same detector from 0.95 to 7.06. **The first number measured my code, not the detector.**

### 2.2 The detector is free; the host GPU is the constraint

| configuration | depth fps | rgb fps | detector fps |
|---|---|---|---|
| baseline (production stereo only) | 29.6 | 29.8 | — |
| + ImageManip resize | 29.6 | 29.8 | 29.8 |
| + resize + retail-0013 | 29.4 | **29.7** | **11.4** |

RGB and depth are untouched. 11.4 fps detection is ample — detection is an **enrolment** source, not
a per-frame need; tracks carry identity between detections, which is MediaPipe's own
detect-once-then-track design.

**Critical, and not optional:** `nn.input.setBlocking(False)` + `setQueueSize(1)`. Without them a
blocking NN input back-pressures the ImageManip and hence `ColorCamera.preview`, which the production
RGB output also consumes, and RGB collapses 29.8 → 12.3 fps. The detector must be allowed to drop
frames.

RTMW3D measured **20.7 ms p50** with a **fixed batch of 1** — no dynamic batch axis, so N people cost
N sequential inferences:

| people | pose ms | max fps | verdict |
|---|---|---|---|
| 2 | 41.4 | 24.2 | usable |
| 3 | 62.1 | 16.1 | usable |
| 4 | 82.8 | 12.1 | marginal |
| 5+ | 103+ | <10 | too slow |

**Design target: 2–3 people at full rate, 4 degraded.** `--max-poses` defaults to 3. The tracker
emits most-established-first, so the cap drops the people least likely to still be there.

---

## 3. The architecture

```
OAK-D VPU : stereo depth (30 fps) + person detection (11.4 fps)   <- free, the VPU was idle
host      : PersonTracker assigns persistent ids                  <- pure numpy, microseconds
host GPU  : RTMW3D per tracked person, N <= 3                     <- 20.7 ms each, the budget
wire      : every person in one datagram, backward compatible
Unity     : CrowdFrame -> TrackedStage.Bodies -> experiences
```

### 3.1 The tracker

`person_tracker.py` + `assignment.py`. Three choices, each forced by what this system actually has:

* **Association is 3-D, not IoU.** Every published tracker (SORT, ByteTrack, OC-SORT) associates on
  image-plane IoU because that is all a webcam gives. This rig has metric depth per detection. Two
  people overlapping on screen at different distances have near-perfect IoU and are trivially
  separable in Z. That is this system's biggest advantage over an off-the-shelf tracker.
* **Optimal assignment, not greedy.** Greedy is **suboptimal on 54.6% of random 4×4 cost matrices**
  (measured), and its failure mode is exactly an ID swap between crossing people. `assignment.py` is
  a pure-numpy O(n³) shortest-augmenting-path solver — scipy is not in the venv and is not worth
  ~30 MB for one function. Verified optimal against brute force on 200 random rectangular matrices.
* **Constant velocity, no Kalman filter.** The gate here is a fixed physical plausibility bound, not
  an adaptive covariance. Damped constant velocity predicts; the residual gate rejects. This mirrors
  what `joint_tracker.py` already does per *joint*, one level up.

Birth and death are asymmetric on purpose: confirm over 3 frames so a flickering false positive never
becomes a person, but hold a lost track for 1.2 s so an occlusion does not destroy an identity.

### 3.2 The wire, and why nothing broke

The multi-person sender publishes a `persons` array **and** republishes the most-established person
at the payload **root** in the exact single-person shape. So:

* the VRM mirror app, `OakDUdpPoseProvider`, the P1-3 buffer and all nine existing experience scenes
  consume it **unchanged and unaware**, seeing one person as they always have;
* a multi-person consumer reads `persons`.

Cost: one duplicated person, ~6 KB of a ~9.3 KB payload (max measured 10.2 KB, against a 65 KB UDP
limit). `--no-legacy-primary` drops it. Verified: `root lm == persons[0].lm` on every packet.

The crowd is **not** run through P1-3 interpolation. That buffer reconstructs one body at a
presentation time and has no concept of identity; interpolating a crowd would mean matching people
across two buffered frames, which is the tracker's job and is already done upstream.

---

## 4. Measured results

### 4.1 Detect → track, on the seven-dancer clip (no depth — the harder path)

```text
tracked people per frame   mean 6.90, median 7      (ground truth 7)
distinct ids issued        11 for 7 people over 11.8 s
BORN 32 -> CONFIRMED 11    21 phantom detections refused before reaching the wire
LOST 134 / REACQUIRED 129  the occlusion cycle working - hidden, then recovered WITH THE SAME ID
RELEASED 3                 only 3 identities genuinely given up
ids alive >50% of clip     6   (three of them 99.4%)
```

### 4.2 Through the real Unity path

```text
render frames 856   live 752   with crowd 752
  PASS  the single-person path still works              752 live frames
  PASS  the crowd channel is populated                  752 frames
  PASS  more than one person tracked at once            max 3 bodies (3p:720, 2p:31, 1p:1)
  PASS  frames with 2+ people                           751
  PASS  tracked people occupy DIFFERENT places          mean pair gap 0.98 m, max 3.01 m
  PASS  root primary == Bodies[0]                       752 frames - old and new consumers agree
  PASS  at least two ids persist across the clip        #4:90%  #2:52%  #43:42%  #44:40%
```

> **A bug this test caught.** The first run failed on *"tracked people occupy different places —
> mean pair gap 0.00 m"*. Landmarks are hip-relative, so staging every person at the same origin drew
> the entire crowd exactly on top of each other — three bodies, one silhouette, across 850 frames.
> Each person is now offset by their own measured mid-hip relative to the primary's. Y is
> deliberately not offset: vertical placement belongs to the grounding loop.

---

## 5. BONDS — the first experience that needs two people

`Scenes/Bonds.unity`. Everyone gets their own colour; a line is drawn between every pair and
brightens as they close; touching hands makes it flare and ring.

**Why this one first, out of everything two people unlock.** Two properties decide whether a
multi-person experience survives real tracking, and this has both:

1. **It is robust to an ID switch.** Every richer idea — a score per player, a drawing per person, a
   duel — is *ruined* when the tracker swaps two identities, because accumulated state follows the
   wrong body. ID switches **will** happen — the seven-dancer clip costs a few identities to long
   occlusions. Here a switch costs two people trading colours for a moment. Nothing accumulates, so
   nothing is corrupted.
2. **It is self-evident.** Nobody needs the concept explained: a line appears between two people and
   responds to them. It demonstrates in one glance the thing that was impossible before — that the
   system knows these are two *different* people and is relating them.

Everything is a **relative** measure, so it needs no calibration: two people 1.2 m apart read as
1.2 m apart even if both are 20 cm off in absolute depth, because the error is common-mode.

With a single-person sender it degrades honestly to one body and a line of text saying so.

---

## 6. What changed

```text
python-sidecar~/
  assignment.py                     NEW  pure-numpy optimal assignment (Hungarian)
  person_tracker.py                 NEW  detections -> persistent ids
  multiperson_udp_sender.py         NEW  the multi-person sidecar
  tools/video/f32_multiperson_video.py  NEW  the testable video path
  tests/test_person_tracker.py      NEW  19 assertions
  depthai_blazepose/models/person-detection-retail-0013_*.blob   NEW (fetched, not committed)

Runtime/Core/Models/CrowdFrame.cs   NEW  PersonPose + CrowdFrame
Runtime/Tracking/OakD/OakDUdpPoseProvider.cs   + ParseCrowd, TryGetCrowd
Runtime/SkeletonShow/TrackedBody.cs NEW  id + pose + state
Runtime/SkeletonShow/TrackedStage.cs + Bodies, HasCrowd, DetectedCount, TrackedCount, placement
Runtime/Experiences/CrowdBondsExperience.cs   NEW  experience 9
Scenes/Bonds.unity                  NEW
```

**Nothing in the single-person path changed.** `wholebody_udp_sender.py` is byte-identical.
`VirtualMirror.App` compiles; 31/31 Unity tests and 192/192 sidecar assertions still pass.

---

## 7. What this does NOT prove — read before demoing

1. **Never run with two real people in front of the camera.** Everything is recorded video through
   the real device. The detector, the tracker and the wire are exercised; *two humans in a room* are
   not.
2. **Nothing has been rendered on a screen.** Same Editor-lock situation as F-29/F-30/F-31. Every
   geometric and numeric claim is tested; no visual claim is.
3. ~~**Multi-person output is RAWER than single-person output.**~~ **CLOSED by F-33** — every
   tracked person now carries a full P0 + P1-1 + F-22 chain keyed to their track id, and P1-4 is
   available and off exactly as in the single-person sender. See
   `F33_PER_PERSON_FILTERS_2026-09-17.md` and **ADR-071**.
4. **Identities are not perfect.** Dancers occluded long enough are released and re-enrolled under a
   new id. That is the case metric depth fixes, and video cannot test it. *(The figure originally
   given here, "11 ids for 7 people", was machine-dependent: this harness drove the tracker from the
   wall clock, so its constant-velocity prediction changed with host speed. On the video's own clock
   it is **9 ids across two passes**. Fixed in F-33 §7.)*
5. **The detector blob is not committed** (2.3 MB binary). `setup_sidecar.ps1` does not yet fetch it;
   the command is in `multiperson_udp_sender.py`'s error message.
6. **A latent bug was found and left alone:** `mediapipe_utils.non_max_suppression` raises
   `IndexError` on OpenCV 4.x (`cv2.dnn.NMSBoxes` returns a flat array now). It is dead vendored
   code that production never calls; this work does not use it. Fix it if anything ever does.

## 8. Evidence

```text
docs/evidence/f32/
    identity_contamination.txt      the F-29 correction, with the bone-length measurements
    detector_feasibility.txt        why BlazePose is not enough
    architecture_measured.txt       detector cost, coexistence, the N-person pose budget
    tracking_results.txt            detect -> track on the 7-dancer clip
    acceptance_unity_crowd.txt      7/7 through the real Unity provider + TrackedStage
    unit_tests_tracker.txt          19/19
    456_seven_dancers.jpg           what is actually in the clip
    tracked_dancer_frame035/249.jpg the single-person crop on two different dancers
    retail_detector_letterboxed.jpg tight per-person boxes
    tracked_ids_seven_dancers.jpg   six stable coloured ids
    *.py.txt                        every harness, for reproduction
```
