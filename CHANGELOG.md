# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While the version is `0.x`, the public API may change in a minor release. See
[Known limitations](README.md#known-limitations) before depending on it.

## [Unreleased]

### Added

- **Press Play once (F-36).** `Scenes/SidecarBoot.unity`, at Build Settings index 1, starts the
  multi-person sidecar and then opens the launcher — so the whole demonstration set works without a
  terminal (ADR-074). Index 0 is unchanged.
  - **It runs `multiperson_udp_sender.py` directly, without the supervisor.** `sidecar_supervisor.py`
    builds its child command from `--portrait`, `--portrait-dir`, `--subpixel-bits` and `--seconds`,
    which only `wholebody_udp_sender.py` accepts, and waits on a readiness contract only that sender
    emits — so the pre-existing auto-start could only ever produce the single-person stream. The
    multi-person sender is backward compatible, so one sender now serves the single-person scenes,
    the crowd scenes and the avatar app alike.
  - **The demonstration path has no watchdog; the product still has one.**
    `SidecarProcessLauncher` gained an additive `DirectScript` mode that defaults to empty, so
    `AppBootstrap` goes through the supervisor exactly as before. A unit test pins that default,
    because a regression there would silently remove restart-on-crash from the product.
  - **It owns the sidecar process and no UDP socket** — each scene binds 8899 for itself — and is
    `DontDestroyOnLoad` only so the process outlives scene switches.
  - **It never starts a second producer:** it listens on the destination port for 400 ms first and
    attaches if anything is already sending, rather than interleaving poses from two sessions.
  - `AppBootstrap` is untouched, and a stray `mirrorSceneName` code default was reverted to
    `"Mirror"` — it did nothing (the serialized scene value wins) but would have made any newly added
    `AppBootstrap` load a scene with no `AvatarRoot`.
  - **Verified headless: 0 compile errors, 6/6 new tests, 187 passed suite-wide**, plus the spawned
    command executed end-to-end past argparse and provider selection. Nothing has been on a screen.

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

### Fixed

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

### Known

- **Multi-person input produces a silent chimera.** The pipeline does not fail loudly when several
  people are in frame; it emits a plausible skeleton belonging to nobody. Do not deploy where a
  second person can enter frame. Evidence in `docs/evidence/f32/`.
- Floor contact is reliable to ~21 mm and hands to 99.1% on a clean single subject at 1.4 m, and
  hands remain 93.7% plausible at 1.96 m. Single-subject accuracy beyond ~2 m is untested.

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
