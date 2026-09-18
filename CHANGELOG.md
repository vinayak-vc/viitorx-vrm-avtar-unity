# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While the version is `0.x`, the public API may change in a minor release. See
[Known limitations](README.md#known-limitations) before depending on it.

## [Unreleased]

### Added

- **Every body now shows where it is STANDING — the depth cue.** The stream has always carried real
  depth (measured live: the wrist moves 0.79 m in Z against 1.14 m in X) and the scenes could not
  show it: a dead-on camera 3.2 m away turns that into roughly a 15% change in apparent size, which
  the eye does not read. Each body gets a dim glowing pool on the floor beneath its mid-hip, in both
  shared renderers — `BodyRenderer` (all nineteen experiences) and `GlowSkeletonMode` (the launcher
  and Skeleton Show). It reads BECAUSE the camera is low: at a grazing ~17° a metre of walking
  sweeps the pool a long way down the screen, across the converging floor grid.
  - **Not a colour and not a width**, deliberately. Colour already means speed in every mode and
    width already means confidence; `BodyRenderer` states plainly why a second meaning on one
    channel is unreadable. Depth gets its own object instead.
  - Anchored on the mid-hip, not the feet: the feet are the truer contact point and much the
    noisier, and a pool that jumps when an ankle drops out draws the eye to the failure.
  - Off for echoes, ghosts and recorded replays (`RenderRaw`) — a pool asserts "a person is here".
  - Pairs with `FollowPosition`: the body only started moving in Z this session, and the pool is
    what makes that movement legible.

- **Camera orientation is a per-sender setting, with a live toggle.** `multiperson_udp_sender.py` had
  no portrait support at all (the launcher's claim that it "handles portrait internally" was untrue),
  so the two senders produced different coordinate frames from the same camera. It now applies the
  F-18 transform — image, depth and intrinsics — and rotates the on-device detector's boxes to match.
  `AppBootstrap` exposes `sidecarSinglePersonPortrait` (on) and `sidecarMultiPersonPortrait` (off),
  because the mount genuinely differs: portrait puts more pixels on one standing body, landscape
  covers more room and keeps the detector reading upright people. **Press O in play mode** to flip
  whichever sender is running and restart it.


- **The scene chooses the sender.** Single-person experiences (and `Mirror`) are now tracked by
  `wholebody_udp_sender.py` — the dedicated single-person pipeline, RTMW3D whole-body with hands,
  F-21 ownership and the measured filter chain — while crowd scenes get `multiperson_udp_sender.py`.
  Previously one crowd sidecar served the whole session, so every one-person scene was tracked
  through a detector and a per-person crop it does not need. `SceneLauncher.TrackingNeedFor()` reads
  the same catalogue the menu draws, so the columns a person sees and the tracking they get cannot
  disagree, and `AppBootstrap.EnsureSidecarForScene` switches on scene load. See ADR-076.
  - **The switch costs ~15-20 s of no tracking** (one python process killed, another loading
    RTMW3D) and is paid ONLY when crossing between the single-person and crowd columns. Navigating
    within a column, and passing back through the Launcher, cost nothing — an unlisted scene is
    `Unspecified` and changes nothing rather than falling back to a default. The pause is logged.
  - **No experience scene changed.** `TrackedStage` already handles both wire shapes: a
    single-person sender leaves `Bodies` with exactly one entry.
  - **Not the BlazePose/MediaPipe sender.** `depthai_blazepose/udp_pose_sender.py` sends no hand
    landmarks, which would break the two pinch-driven experiences (AIR GRAFFITI, OBJECTS); it is the
    path ADR-018 replaced, and `OakDUdpPoseProvider` already warns against it by name.


- **Unified Bootstrap: Bootstrap -> Launcher -> any scene.** `Scenes/Bootstrap.unity`, at Build
  Settings index 1, starts the multi-person sidecar and opens `Launcher.unity`. The redundant
  `SidecarBoot.unity` scene has been retired and removed.
  - **`AppBootstrap` moved to `Mirror.unity`** with scene-scoped lifecycle (not `DontDestroyOnLoad`),
    so its UDP socket on port 8899 is released when returning to Launcher via ESC, eliminating
    the socket collision that previously prevented skeletons from tracking in experience scenes.
  - **Single sidecar process serves all scenes.** `multiperson_udp_sender.py` is started once by
    `Bootstrap.unity` and outlives scene loads via `DontDestroyOnLoad` on the process holder.
  - **Both paths have a watchdog** (corrected same day — see Fixed/Changed above and ADR-075). This
    shipped as a `DirectScript` mode with no watchdog on the demonstration path, and the product was
    moved onto it too; the supervisor now launches either sender, so both are supervised again. A
    unit test pins that an unconfigured launcher still gets the supervised single-person command.
  - **It owns the sidecar process and no UDP socket** — each scene binds 8899 for itself — and is
    `DontDestroyOnLoad` only so the process outlives scene switches.
  - **It never starts a second producer, in both directions.** It listens on the destination port
    for 400 ms first and attaches if anything is already sending; in the other direction the
    SUPERVISOR's own lock port does the work, so launching the avatar app from the menu's PRODUCTION
    column makes `AppBootstrap` see an owner and attach — `Bootstrap.unity` ships with
    `useOakUdpTracking: 1` and auto-start enabled, so that was a real defect. This shipped as a lock
    held by hand in Unity, which was only necessary while the sender was launched unsupervised;
    `SidecarSingleInstanceLock` remains as the shared probe both sides test with.
  - **`Bootstrap.unity` is unchanged and is still the product** — avatar, UI, retargeting, IK and its
    own supervised sidecar. The boot scene owns only the sidecar process; the two are not
    alternatives, and the launcher lists `Bootstrap` in its PRODUCTION column.
  - `AppBootstrap` is untouched, and a stray `mirrorSceneName` code default was reverted to
    `"Mirror"` — it did nothing (the serialized scene value wins) but would have made any newly added
    `AppBootstrap` load a scene with no `AvatarRoot`.
  - **Verified headless: 0 compile errors, 10/10 new tests, 191 passed suite-wide**, plus the
    spawned command executed end-to-end past argparse and provider selection. Nothing has been on a
    screen.

- **One launcher scene (F-35).** `Scenes/Launcher.unity` lists all twenty scenes in three columns
  and loads any of them; **ESC returns to it from any scene** (ADR-073). Click a row, or arrows plus
  Enter, or the number shown on it.
  - **Grouped by how many people a scene needs**, not by theme. Ten of the nineteen demonstrations do
    nothing on your own, and somebody standing alone in front of Chain concludes the installation is
    broken; the menu answers that before they choose.
  - **The launcher runs the real tracking while you choose**, so "the sidecar is not running" and
    "this is the SINGLE-PERSON sender" are visible BEFORE a scene is picked and blamed for it. The
    multi-person column replaces its description with that warning and names the sender to run.
  - **All 20 scenes are now registered in Build Settings**, which is what makes them loadable at
    runtime and means they are in a player build. They are camera-and-one-GameObject scenes, so the
    cost is negligible. **Index 0 is unchanged**, so a build's startup scene is not affected.
  - **A row whose scene is not registered is greyed out with the reason** rather than being a button
    that silently does nothing — Unity's own failure for that is a console error after the click.
  - **The UDP socket is released before the scene load**, not in `OnDestroy`, because every scene
    binds the same fixed port; both scene bases also skip the rest of the frame rather than running
    against a disposed provider.
  - **`Virtual Mirror > Register All Scenes in Build Settings`** (new Editor menu item), because
    `ProjectSettings/EditorBuildSettings.asset` is gitignored and the registration is therefore
    per-machine — a fresh clone opens the launcher with every row greyed out. It only ever appends,
    never reorders or removes, since index 0 is the startup scene of a build.
  - Known limits, recorded rather than fixed: ESC does not return from `Bootstrap` (the production
    app is not one of the two scene bases), the catalogue's titles are copied rather than read (the
    menu's assembly cannot see the experience types), and the launcher has no audio.
  - **Verified headless only: 0 compile errors, 10/10 new unit tests, 181 passed suite-wide, and a
    registry cross-check showing all 20 rows resolve to a registered scene.** Nothing has been on a
    screen; the Editor checks are in §5 of the report.

- **Nine crowd experiences (F-34).** Nine new scenes that need more than one person — Collective,
  Stillness, Traces, Eclipse, Chord, Chain, Pass, Podium and Mirror Each Other — each one
  `ExperienceBase` subclass plus a `.unity` that is a camera and one GameObject (ADR-072). The
  sidecar is unchanged: these are consumers of the crowd F-32 put on the wire and F-33 filtered.
  - **One rule decided which ideas exist.** An identity switch cannot be detected: the live
    two-person session measured an identity migrating between two humans at **0.015 m per frame**
    against a **0.35 m** association margin — 97 datagrams, **zero events logged**. There is no
    discontinuity to trigger on, so "detect a switch and freeze the score" cannot be written. The
    only defence is structural — **symmetric in the participants, reads only the current frame** —
    and that single test sorted the whole candidate list.
  - **No per-person accumulated score exists in any of them, on purpose.** Points, streaks and hold
    timers are exactly the state a silent switch corrupts, and the failure is maximally visible: a
    player watches their points appear on somebody else's counter. Accumulated state is allowed only
    where it belongs to something that is not a person — a rally, a circuit, a light's drift, a
    floor. The **three** places identity *is* read (colour, a pass count, a crown-change chime) are
    named in each class header with what a switch costs; all three are bounded.
  - **The budget is three people at 16 fps** — 20.43 ms per person at p50 over 400 warm inferences
    (p90 20.89, p99 21.69). The detector sees seven; the GPU affords three. Four costs 12 fps.
  - **`SkeletonPose.ResetHistory()` and a 12 m/s mid-hip guard**, which fixed a live latent bug:
    `TrackedStage` snaps its ground offset on first floor acquisition, moving the staging origin by
    up to a metre in one frame, so the next frame differenced two positions measured against
    different origins and reported a whole-body speed spike above every strike gate at once (0.9 m/s
    in Bubble Pop, 0.55 in Objects) while un-planting every planted foot. The guard does **not**
    catch the slow switch above — nothing can — and says so.
  - **`ExperienceBase` no longer wipes a crowd when somebody walks up.** `PresenceGate` answers "is
    there *a* person", so it never changes in a crowd; arrivals and departures now come from
    `CrowdRoster`, keyed on the track id. `ResetsOnNewVisitor` defaults to true, so the ten existing
    single-person scenes are behaviourally unchanged.
  - **The three movement thresholds are gathered into `ExperienceTuning`** with the caveat attached:
    they were tuned at ~21 fps and a crowd stream runs at ~16, and they have **not** been
    re-measured. The effect is one-sided — a crowd scene reads slower, so the gates get harder to
    pass, not easier.
  - **`BodyRenderer.Render(pose, tint)`** added as an overload, so a crowd scene can colour a body
    per person without dropping the speed palette and confidence-in-line-width the way `RenderRaw`
    does.
  - `FluidFieldExperience` and `FootprintsExperience` were **migrated** onto new shared `FluidField`
    and `FloorMarks` classes rather than having the logic copied — same grid, same decay, same merge
    radius, same fade. Two copies drift apart the first time either is tuned.
  - **Verified headless only: 0 compile errors, 29/29 new unit tests, 171 passed suite-wide.**
    Nothing in F-34 has been on a screen; the play-mode and hardware checks are listed in §8 of the
    report.

- **Per-person filter chains (F-33).** Every tracked person now carries the full measured
  single-person signal chain — P0 One-Euro smoothing with its displacement cap and bounded hold,
  P1-1 joint tracking, F-22 biomechanical validation — in one instance keyed to their **track id**
  (ADR-071). F-32 had shipped multi-person with all of it switched off.
  - **Keyed on identity, never on list position.** The tracker re-sorts people
    most-established-first every frame, so an index-keyed filter bank hands one person's One-Euro
    history to another. Unity's `TrackedStage.UpdateCrowd` already keyed `SkeletonPose` on the
    track id for the same reason; this is the sidecar half of that decision.
  - **Each person measures their own sample rate.** One-Euro derives velocity as `delta * freq`,
    so a filter told 30 fps while actually sampled at 16 over-estimates speed by 1.9×, inflates
    its adaptive cutoff and opens up exactly when it should damp. Measured at 10 fps, the naive
    port is *worse than no filter at all* on the median frame (35.6 mm vs 33.5 mm) while costing
    400 ms of lag; self-measured, it is 29.9 mm at 200 ms.
  - **The feet and the head joined the filter group**, which the single-person sender never put
    them in. They failed for opposite reasons — feet with no depth at all, the head with depth
    sampled from the wall behind it — so they needed the bounded hold and the displacement cap
    respectively. Implausible single-frame steps (over 300 mm in 33 ms) fell from 630 to **77**,
    while the distal, trunk and hip figures moved by 0.0% and exactly the same 40 200 joints were
    emitted. This is what `FootprintsExperience` reads, so it is visible, not bookkeeping.
  - **Measured:** implausible steps 2158 → 77 against the unfiltered baseline on 2060
    person-frames of identical input; worst single step 2091 mm → 960 mm; cost **0.88 ms per
    person-frame** against 20.7 ms for the pose solve it follows.
  - **Lag is 233 ms at 30 fps and is not new** — it is the accepted single-person tuning. At
    30 fps the multi-person chain *is* the single-person chain: same modules, same constants.
  - Unity needed **no change**: the wire shape is unaltered. The `st` trust field now carries real
    per-joint states per person instead of `-1`.

- **Multi-person detection and tracking (F-32).** `multiperson_udp_sender.py` detects every
  person in frame on the OAK-D's VPU, assigns each a **stable id** that is never reused, poses
  the most-established ones on the GPU, and streams them all in one datagram (ADR-070).
  - `assignment.py` — pure-numpy optimal assignment. Greedy is suboptimal on 54.6% of random
    4×4 cost matrices, and its failure mode is exactly an ID swap between crossing people.
  - `person_tracker.py` — associates in **3-D** using the OAK-D's metric depth, which no
    IoU-based tracker can: two people overlapping on screen at different distances are
    trivially separable in Z. 19 unit tests including the crossing case.
  - **Backward compatible by construction**: the most-established person is also published in
    the ordinary single-person shape, so the VRM mirror app and all nine existing experience
    scenes consume this sender unchanged. Verified across 752 Unity frames.
  - Unity side: `CrowdFrame`, `TrackedBody`, `OakDUdpPoseProvider.TryGetCrowd`,
    `TrackedStage.Bodies`.
  - **Measured budget:** pose is 20.7 ms per person with a fixed batch of 1, so 2 people run at
    ~24 fps, 3 at ~16 fps, 4 at ~12 fps. `--max-poses` defaults to 3.
- **`Scenes/Bonds.unity` (F-32)** — the first experience that needs two people. Everyone gets a
  colour, lines connect every pair and brighten as they close, and touching hands makes a bond
  flare. Chosen first because it is robust to an ID switch: nothing accumulates per person, so
  a swap costs two people trading colours rather than corrupting a score.

- **Time Echo (F-31).** `Scenes/TimeEcho.unity` — four delayed copies of the tracked body trail
  behind the live one. It exists to defeat the project's hardest limit: RTMW3D-x is single-person
  with no detector and no track id, so this makes a crowd from one person without touching the
  tracking. Everything shown is replay of already-validated pose data. `EchoBuffer` is a fixed,
  clock-injected ring with 8 unit tests including the post-wrap case (ADR-069).
- **Sound, as a layer rather than a scene (F-31).** `ExperienceAudio` is owned by `ExperienceBase`,
  so all nine scenes gained a drone that follows whole-body energy, a movement layer driven by
  extremity speed, and arrival/departure cues from the presence gate — without any of them being
  edited. Event cues were wired into hooks that already existed: bubble pops rise with the combo, a
  footprint petal plays a climbing note, an object hit is louder for a harder swing, plus grab,
  release, a pose-match chord and a stroke start. Closes F-28 §7.4. `M` mutes (ADR-068).
  - Every clip is **synthesised at runtime** — no audio assets, no import settings, no licence
    questions, and a scene stays a camera and one GameObject.
  - Every pitch comes from a **pentatonic scale**, and `Play` takes a scale degree rather than a
    frequency, so a caller can compose a rising run and cannot produce a wrong note. A moving person
    is a random trigger source; random semitones sound like a fault within about four notes.

- **Seven experience scenes (F-30).** Each is a camera and one GameObject; everything visible is
  built at runtime. `AirGraffiti` (pinch and draw in the air), `BubblePop` (timed, scored, combo),
  `PoseMatch` (copy a ghost pose, per-joint feedback), `DepthReach` (rings at three real distances -
  the demo that needs the depth camera), `Footprints` (stand still and the floor remembers, built on
  F-29's foot contact), `Fluid` (a real velocity field the body stirs, which keeps swirling after
  you stop) and `ObjectPlay` (bat, kick, head or pinch-and-throw objects with hand-written physics).
- **`TrackedStage`** — everything between the socket and a usable body, owned once: provider,
  converter, the F-27 humanized layer and its once-per-pose rule, world pose, both hands, trust
  channel, attract loop, presence gate and floor grounding. A plain class rather than a component,
  so the order of "advance the tracking" and "read the tracking" is in the code rather than in
  Unity's script execution order.
- **`ExperienceBase`** — shared staging, HUD and keys, plus `ScoringAllowed`, which stops any
  experience scoring the synthetic attract figure.
- **`ShowStage`**, **`BodyRenderer`**, and the `VirtualMirror.Experiences` assembly.
- **`SkeletonPose.Velocity`** — per-joint world velocity, for anything that reacts directionally to
  the body. Note that `|Velocity|` is NOT `Speed`: they agree exactly on straight-line motion but the
  smoothed vector partly cancels on reversing motion (measured mean ratio 0.678 on a dancing
  subject, max 0.999). Take direction from `Velocity` and magnitude from `Speed`.
- **F-29 trust channel.** The sidecar now publishes what it believes about the pose it just sent:
  `st` (P1-1/P1-4 tracking state per joint), `own` (F-21 ownership state) and `lat` (measured
  camera-to-payload latency, ms). All three are optional and read-only — a consumer that ignores
  them behaves exactly as before, and nothing upstream reads them back. Surfaced in Unity through
  `TrackingTelemetry` and `OakDUdpPoseProvider.TryGetTelemetry`.
- **SkeletonShow mode 4 — TRUST HUD.** Draws the per-joint tracking state that previously reached
  only a log file: green TRACKED, amber WEAK, blue PREDICTED, red LOST, violet RECOVERING, grey
  no-tracker, with a halo marking depth that was inferred rather than measured. Reports joint
  counts, depth coverage, ownership, end-to-end latency, floor and per-joint camera depth.
- **SkeletonShow mode 5 — HANDS.** Renders all 21 landmarks per hand, the palm plane, pinch state
  and finger count. The landmarks were already arriving and being discarded after the five finger
  curls were derived from them. Gesture measures are normalised by palm length so they cannot fire
  on subject distance, and a hand whose palm falls outside 50–160 mm is refused rather than drawn.
- **Attract loop.** With nobody tracked, a synthetic figure drives whichever mode is active through
  the ordinary pose path, so an empty room shows the real modes instead of a black screen.
  `PresenceGate` enters live after 0.4 s and returns to attract after 3.0 s.
- **Feet are drawn.** Heels (JointId 29/30) and big toes (31/32) have always been on the wire;
  each foot is now an ankle–heel–toe triangle rather than a single ankle-to-toe line.
- `RawHandFrame`, `HandPose`, `AttractSkeleton`, `PresenceGate`, and 23 EditMode tests.
- **Camera mount tilt compensation, so an angled camera is usable at all.** Nothing in the pipeline
  corrected for pitch: `PoseSpaceConverter` applied axis sign flips and no rotation, and neither
  sender knew the word. Because `SkeletonPose.FloorY` is a **single scalar** learned from foot
  contacts, a camera pitched by θ made the floor appear `tan(θ)` metres higher per metre of depth —
  **0.27 m/m at 15°, crossing the 0.08 m planted band within 30 cm of walking.** The figure sank as
  you approached and rose as you left. F-18 had already measured the effect without naming it, at
  +0.338 m/m (implying ~19°; `tan(19°) = 0.344`), and this session's `FollowPosition` made it
  visible by letting the body move in depth at all. Set `AppBootstrap.cameraTiltDegrees`, positive
  for nose-down. See ADR-079.
  - **One value in `CameraMount`, not a field in four components.** `flipX` is declared separately in
    `AppBootstrap`, `SkeletonShowBootstrap`, `ExperienceBase` and `SceneLauncher`, and turning it on
    cost twenty edited scene files. `AppBootstrap.Awake` publishes the tilt before the first scene
    loads instead.
  - **The magnitude is measured; the sign is yours.** `f19_level.py` writes `tilt_deg` to
    `mount.json` as magnitude only — the board's IMU-to-camera extrinsic is an identity placeholder
    that F-19 disproved. A wrong sign doubles the error, so the default is 0 and a guess is worse
    than nothing.
  - Portrait needs no special case: `f18_portrait` already undoes the physical 90° roll, leaving
    pitch as the only misalignment in both orientations. At 0° the path is bit-identical to before.
- **Multi-person went from 16 fps to camera-capped: RTMW3D now runs in FP16.** The GPU stage was
  **88.1%** of the 21.3 ms per person, so that is what changed. FP16 takes it 18.59 -> 6.25 ms
  (**2.97x**), and end to end per person 21.51 -> 8.99 ms: **three people go from 15.5 fps to 37.1**,
  which is past the camera's 30, and five people now run at 22.3 fps where three used to manage 15.5.
  `keep_io_types` leaves the graph taking and returning fp32, so the sender's blob and decode are
  untouched. See ADR-082.
  - **Accuracy measured on real people, not on noise.** SimCC is an argmax over an activation map,
    and the argmax of a near-flat random map flips under any perturbation - that measures nothing.
    Over 60 frames of `123.webm` (F-29's verified single subject at 1.96 m), confident joints move a
    **median of 0.000 px**, p95 2.652 px, with **0.090% moving more than 20 px** - about one joint in
    a thousand, which P0's One-Euro and the displacement caps already exist to absorb.
  - **Batching was built, proven correct, and rejected on measurement.** The model pins batch to 1;
    the rewritten dynamic graph returns bit-identical rows at batch 3, and still loses - a dynamic
    batch dimension costs **+42% at batch 1** because DirectML stops specialising on a fixed shape,
    and batch 3 wins only 7% back. A room usually holds one or two people.
  - **TensorRT is not needed.** At 37 fps for three people the camera is the limit, not the GPU; it
    would also mean replacing onnxruntime-directml with onnxruntime-gpu plus CUDA, cuDNN and
    TensorRT, none of which are installed here. Revisit only if `--max-poses` goes past 4.
  - Build it with `tools/model/f45_make_fp16.py`, which re-runs the accuracy check itself.
    `evidence_paths.DEFAULT_MODEL` prefers the fp16 file **when it exists** - the `.onnx` files are
    not in version control, so a hard default would break any machine that never converted - and both
    senders print the weights they loaded. Sidecar readiness also improved 13.8 s -> 6.8 s.
- **The working volume roughly tripled: the pipeline was running both 1280x800 sensors at
  640x400.** This was the biggest single gap in the system, and it was not the lens or the stereo
  baseline (F-17 closed both) - it was discarded resolution. `--rgb-isp 1/1` keeps the RGB sensor's
  native frame at **full FOV** (verified: `fy` 284.627 -> 569.254, FOV unchanged at H 70.22 /
  V 96.70), doubling the pixels on a body; `--mono-res 800p` doubles the mono focal length so `f.B`
  goes 21.54 -> 43.07 and **depth error halves at every range**. Both default on. See ADR-081.
  - **Measured before deciding** (`tools/capture/f44_resolution_ab.py`, on the device): **2x pixels
    on target and +5.6 pp valid depth for 2.6% of the frame rate** (30.0 -> 29.2 fps).
  - Nearly free for a structural reason: RTMW3D always resizes its crop to 288x384 and the detector
    always runs at 544x320, so **neither sees the source resolution** and the 20.4 ms/person pose
    solve is untouched. The cost is VPU and USB, not GPU.
  - The distance at which the pose model resolves a given level of detail **doubles** - 242 px on a
    body was 2.0 m, it is now 4.0 m. Depth at 4 m quantises at 46 mm against interaction tolerances
    of 130-450 mm, so pixels on target, not depth precision, is now what limits range.
  - **`build_rgbd_pipeline` now actually applies `mono_res`.** It was a declared parameter the body
    ignored in favour of a hard-coded `THE_400_P` while the banner reported the requested value -
    the same defect ADR-077 records for the sub-pixel banner, in a second place.
- **The IR dot projector is on** (`--ir-dot`, default 0.8, both senders, forwarded by the
  supervisor). It had never been switched on — DepthAI leaves the emitter off and nothing in this
  repo ever called the setter, so **every stereo measurement this project has taken was on an
  unassisted pair.** F-17 established that CAM_A is IR-cut colour while the mono pair is unfiltered,
  so the dots texture blank walls and plain clothing for the matcher and never appear in the RGB
  frame the pose model reads. Verified live: `IR dot projector: on (80%, driver LM3644)`. Where
  sub-pixel (ADR-077) made each depth reading finer, this makes more readings valid. See ADR-080.

### Changed

- **Both bootstraps launch the sidecar SUPERVISED again.** `sidecar_supervisor.py` now builds its
  child command from the sender it is given (`SENDER_BY_SCRIPT`), so the multi-person sender is
  supervised like the single-person one — same state machine, backoff, readiness and heartbeat.
  `multiperson_udp_sender.py` prints the `producer session id = ` banner to satisfy that contract.
  `SidecarLaunchOptions.SupervisedSenderScript` / `MaxPoses` replace `DirectScript` at both call
  sites; the single-person supervised command is unchanged byte for byte, pinned by a unit test.
- **The sidecar preview (`--show`) is OFF by default** in `Bootstrap.unity` and in code. It is a
  diagnostic window that takes keyboard focus, and the production path should not depend on one.
- `SidecarBootstrap` no longer holds the single-instance lock by hand — the supervisor binds its own
  lock port, and taking it in Unity would make the supervisor abort on its own mutex.
- The supervisor treats a missing person-detector blob as a TERMINAL failure with a fetch hint,
  rather than five silent crash-loop restarts.
- `tests/f20b_failure_tests.py` resolves its paths against the sidecar ROOT. They pointed at
  `tests/` after the "root is the production path" refactor, so the whole harness died on
  `CreateProcess` before running a scenario; D/E/F now pass 4/4.

### Fixed

- **A stalled detector looked perfectly healthy.** Measured live: the on-device person detector
  stopped emitting after 1457 frames and never resumed. The sender stayed alive at 30 fps for
  another 59,000 frames with `dets=0`, streaming nothing, while Unity sat on "WAITING for the
  sidecar" for half an hour — no DepthAI warning, no exception, no exit. It used to be caught by
  accident (no detections → no people → no stdout → the watchdog restarted it), and fixing the
  heartbeat so an empty room no longer triggers a restart removed that accident. There is now an
  explicit `--det-timeout` (8 s): the detector emits a packet per frame whether or not it finds
  anybody, so silence on that queue is never an empty room, and the sender exits rc=3 for the
  supervisor to rebuild the pipeline. The heartbeat line carries `detAge~Ns` so a stall is visible
  before it is fatal. **Root cause still unknown** — see F-42 for the A/B.
- **A late floor teleported the whole body.** `HasFloor` only becomes true once a foot landmark
  appears, so while the feet were untracked `TrackedStage.UpdateGrounding` returned early and
  `hasGroundOffset` stayed false. The first time a floor arrived it ran `groundOffset = target` as a
  one-frame snap — correct for somebody who has just walked up, a vertical teleport when it happens
  27 seconds in. The snap is now limited to a 2 s acquisition window and eases otherwise, and an
  empty room re-grounds so the next visitor still gets the snap.

- **Walking out of frame killed the sidecar.** The multi-person sender's periodic `frames=` line sat
  below `if not persons: continue`, so an empty room produced no stdout and the supervisor's
  heartbeat watchdog — which only started applying to this sender when it became supervised — killed
  it after 10 s. Measured: `HEARTBEAT TIMEOUT pid=41620 no stdout growth for 10s`, `rc=1 uptime=
  463.9s`. The heartbeat now runs before the empty-frame skip. See ADR-078.
- **ESC still killed the single-person sender.** The earlier fix covered only the multi-person one;
  `wholebody_udp_sender.py` had three more `waitKey(1) in (27, ord("q"))` sites. Measured in the same
  session as `SIDECAR EXIT rc=0 uptime=30.6s`. All three now take `q` only.
- **The scenes showed a photograph, not a mirror.** `flipX` was false in `ExperienceBase`,
  `SceneLauncher` and `SkeletonShowBootstrap` and `flipX: 0` in all 20 scene files, so X was never
  negated: your right hand drove the figure's left and two people appeared on each other's sides.
  Now on everywhere. `Mirror.unity` and the VRM avatar are deliberately untouched — ADR-023 still
  holds that reflecting the input twists a rotation retarget.
- **A single person's figure never moved.** `TrackedStage` pinned the staged body to the authored
  origin with only a grounding term on Y, so a crowd separated correctly while one person stayed put
  however far they walked. `FollowPosition` now drives X/Z from the measured mid-hip, relative to a
  neutral captured on first sight. Crowd pair geometry is unchanged — it translates the whole group.


- **Every crowd scene was measuring depth on whole-pixel disparity.** `multiperson_udp_sender.py`
  had no `--subpixel-bits` flag at all, so it took `STEREO_CONFIG`'s shipped default
  (`subpixel: False`) while `wholebody_udp_sender.py` acted on the `--subpixel-bits 3` Unity has
  always passed. Stereo depth error goes as `distance² × disparity_step / (focal_px × baseline)`,
  so the disparity step is a straight multiplier at every range: whole pixels → eighths is **8×
  better depth, everywhere**. Measured on this device (`baseline 7.5 cm`, `mono fx 287.16 px` at
  640×400), the quantisation floor at 3.5 m goes from ±57 cm to ±7 cm. The flag is now defined in
  both senders with identical semantics and forwarded by the supervisor. See ADR-077.
- **The multi-person sender never announced its stereo configuration.** Sub-pixel is invisible in
  the stream, so the only way to know it took effect is the startup banner the single-person sender
  has always printed. It now prints `[mp]   stereo: …` too — which is how this was found.


- **The person detector was being fed a cropped, 1.6x-stretched picture, and furniture read as
  people.** `cam.preview` was never sized, so it sat at DepthAI's default 300x300 with
  `keepAspectRatio` on — centre-cropping the 640x400 ISP to 400x400, discarding 120 px off each side
  — and `ImageManip.setResize(544, 320)` then stretched that square to 17:10. `detections_from`
  mapped the resulting boxes onto the full frame with no inverse, displacing every box outward from
  centre. The sender's own `letterbox()` helper documents the cost of exactly this (0.95 people per
  frame against 7.06 letterboxed) and was only ever called from the video harness. The live pipeline
  now letterboxes on the VPU (`setResizeThumbnail`) and un-letterboxes on the host through one shared
  `letterbox_mapping`. See ADR-075.
- **A single detector packet counted as three confirmations.** The detector runs at ~11 Hz against
  30 Hz RGB and the same `latest_dets` list was re-fed on every RGB frame, so `confirm_hits = 3` was
  satisfied by one detection and the tracker's "a flickering false positive never becomes a person"
  rule could never fire. The tracker is now updated once per detector packet.
- **Held (LOST) tracks were posed and streamed.** A track coasting on prediction with no detection
  behind it had RTMW3D run on a drifting box, which is what put a second skeleton on a single person.
  Only CONFIRMED tracks are posed and sent; LOST tracks stay in the tracker so an occlusion cannot
  destroy an identity.
- **ESC killed the sidecar with nothing to restart it.** The preview window bound ESC (27) to quit
  while the app's own hint reads "ESC menu", so the natural key ended the producer for the rest of
  the session. Only `q` closes the preview now, and it closes the window rather than the process.


- **Corrected a wrong causal claim in the F-29 report (F-32).** `456.webm` was treated as a single
  subject at 2.9 m; it contains **seven dancers**. Every degradation figure from that clip measures
  identity contamination, not distance — the tracked body's shoulder width ranges 0.068–0.455 m. A
  genuine single subject at 1.96 m gives 93.7% plausible hands, so hand tracking does not collapse
  at 2 m. The true limit beyond ~2 m is untested. Corrected in the F-29 report, the README and every
  code comment that cited those numbers.
- **The figure stood 153 mm inside the floor.** `bodyOrigin` fixes the mid-hip at a set height, so
  foot height depended entirely on the subject's proportions. With a measured floor available, the
  new `groundToFloor` staging correction settles the feet onto the grid (0.3 mm, no overshoot).
- **Floor-driven effects were 81–127 mm too high.** The floor was taken from the lowest *ankle*;
  it is now taken from the four foot contact points, which is where the body meets the ground.
- **Corrected an incorrect causal claim in the F-29 report (F-32).** `456.webm` was treated as a
  single subject at 2.9 m; it actually contains **seven dancers**, and the single-person crop
  migrates between them. Every degradation figure taken from that clip measures identity
  contamination, not range — the tracked body's shoulder width varies 0.068–0.455 m. A genuine
  single subject at 1.96 m gives 93.7% plausible hands, so hand tracking does not collapse at 2 m.
  Corrected in the F-29 report, the README, and the code comments that cited those numbers.

### Known limitations

- **Multi-person input produces a silent chimera.** The pipeline does not fail loudly when several
  people are in frame; it emits a plausible skeleton belonging to nobody. Do not deploy where a
  second person can enter frame. Evidence in `docs/evidence/f32/`.
- Floor contact is reliable to ~21 mm and hands to 99.1% on a clean single subject at 1.4 m, and
  hands remain 93.7% plausible at 1.96 m. Single-subject accuracy beyond ~2 m is untested.
- **Tilt compensation is verified in arithmetic, not against a real angled mount.** Eleven tests pin
  the geometry and the zero-tilt identity, but nobody has yet pitched the camera, set
  `cameraTiltDegrees` and walked toward it. Until that is done, treat a level mount as the supported
  configuration and an angled one as unproven.
- **The FP16 model has never been run with three real people, or watched on screen.** The 37.1 fps
  is a per-person measurement multiplied by three, not a crowd capture, and the ~1-in-1000 keypoint
  excursion should be invisible after P0 smoothing but has not been observed to be. Fall back by
  deleting `rtmw3d-x-fp16.onnx` or pointing `VIRTUAL_MIRROR_MODEL` at the fp32 file.
- **Full resolution has never been run with a person in frame, or with three people.**
  Single-person is confirmed at 29.3 fps through the supervisor with the real model; the valid-depth
  figures are an empty room and compare configurations rather than describing tracking. The "usable
  to ~4 m" claim is arithmetic from pixels-on-target, not a capture - nobody has stood at 4 m.
  Rollback is `--mono-res 400p --rgb-isp 1/2`.
- **The IR dot projector's benefit is unquantified.** The mechanism is established and it is
  confirmed running on the device, but no before/after valid-depth measurement was taken. The number
  to beat is the "~63% of keypoints get measured depth" recorded in `RETARGET_AUDIT_2026-08-11.md`.
- **The production mount is 5.89° out of level** against `f19_level.py`'s own 2° tolerance, and
  camera height has still never been tape-measured (`mount.json` reads `"height_m": null`; the
  0.81 m figure is derived from imagery taken on that same tilted mount).

## [0.1.0] — 2026-09-16

First packaged release. The engineering behind it predates this version; `0.1.0` marks the point at
which it became installable by someone other than its authors.

### Added

- **Automatic sidecar launch.** `SidecarProcessLauncher` starts `sidecar_supervisor.py` from
  `AppBootstrap` and stops it on quit, play-mode exit and domain reload, so the tracking sidecar no
  longer has to be started by hand in a terminal (ADR-064).
  - Uses the project's `NativeProcess` rather than `System.Diagnostics.Process`, which is stripped
    under IL2CPP.
  - The child is assigned to a Win32 job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so the
    kernel reaps the whole process tree even when Unity crashes or is force-killed. A stranded
    sidecar holding UDP 8899 used to block the next run.
  - An already-running supervisor is detected through its lock port (TCP 8897) and attached to,
    rather than double-started onto the same UDP port.
  - `autoStartSidecar`, `sidecarLockPort`, `sidecarModelPathOverride`, `sidecarPortraitDirection`
    and `sidecarSubpixelBits` are exposed on `AppBootstrap`.
- **Build packaging.** `SidecarBuildPostprocessor` copies the sidecar's production `.py` files into
  `StreamingAssets/Sidecar/` on a Windows build (ADR-064).
- **`setup_sidecar.ps1`** — one-time target setup that creates the virtualenv, installs pinned
  dependencies and verifies that onnxruntime's DirectML provider actually loaded.
- **`requirements.lock.txt`** — exact pins captured from the working environment.
- `SidecarPathsTests` — 11 EditMode tests covering editor/player path resolution and the
  refuse-to-fall-back-to-PATH contract.
- Package metadata for distribution: `package.json`, `LICENSE` (MIT), this changelog,
  `CONTRIBUTING.md`, `.editorconfig`, `.gitattributes` and CI.

### Changed

- **Sidecar layout** (ADR-065). The repository root now holds only the production path — the two
  entry points plus the nine modules `wholebody_udp_sender.py` imports. The 72 investigation
  harnesses moved into `tools/` and `tests/`, grouped by dependency cluster. Root went from 96 files
  to 18.
- **Evidence paths.** The four capture folders were unified under `evidence/`, and every path now
  resolves through `evidence_paths` instead of one of 115 scattered literals.
  `VIRTUAL_MIRROR_EVIDENCE_DIR` redirects the tree.
- The repository-root `README.md` is now a product README. The documentation index it previously
  duplicated lives at `docs/README.md`, where its relative links resolve.

### Fixed

- **Seven harnesses hardcoded an absolute `C:\Unity\...` path** to a checkout that exists on no
  machine — including this one, since the project moved to `D:`. They now resolve from
  `evidence_paths`.
- **Thirteen harnesses built evidence paths from a per-file `HERE`**, which silently pointed at a
  non-existent `tools/<group>/evidence/` after they were moved. Not covered by the self-tests
  because those harnesses need hardware.
- `requirements.txt` had `onnxruntime-directml` commented out while the working environment had it
  installed, so a setup performed from that file produced a sidecar with no inference at all.
- Three launcher scripts were broken: `run_p0_acceptance.bat` and `run_p12_ab.bat` had invoked
  deleted scripts since `29ec57e`, and `run_capture.bat` broke in the reorganisation. The first two
  are retained with a `DOES NOT RUN` banner because published reports cite them by name; the third
  is repointed.
- The sidecar README's layout section still described roughly a dozen scripts deleted in `29ec57e`,
  and its Tests section printed commands that no longer ran.

### Known limitations

Carried forward and **not** resolved in this release — see
[Known limitations](README.md#known-limitations) for the full list with evidence:

- The avatar's forearm and hand clip into the hip when the arm hangs. Nothing in the pipeline
  performs body-volume avoidance.
- Avatar fidelity is not proven; F-26 showed the earlier "proven good" claim rested on four
  internal-consistency metrics that a stably-wrong pose also satisfies.
- F-21 two-person acceptance, F-22 L2 hands-near-face, F-27 on stereo and F-28 with a live camera
  have never been run.
- The in-editor Play cycle and a produced build have not been exercised for the new launcher.

[Unreleased]: https://github.com/vinayak-vc/viitorx-vrm-avtar-unity/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/vinayak-vc/viitorx-vrm-avtar-unity/releases/tag/v0.1.0
