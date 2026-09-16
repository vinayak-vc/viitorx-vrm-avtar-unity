# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While the version is `0.x`, the public API may change in a minor release. See
[Known limitations](README.md#known-limitations) before depending on it.

## [Unreleased]

### Added

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

- **The figure stood 153 mm inside the floor.** `bodyOrigin` fixes the mid-hip at a set height, so
  foot height depended entirely on the subject's proportions. With a measured floor available, the
  new `groundToFloor` staging correction settles the feet onto the grid (0.3 mm, no overshoot).
- **Floor-driven effects were 81–127 mm too high.** The floor was taken from the lowest *ankle*;
  it is now taken from the four foot contact points, which is where the body meets the ground.

### Known

- Floor contact is reliable at ~1.4 m (21 mm smoothed) but **not** at ~2.9 m (81 mm, where
  smoothing makes it worse because the error is drift rather than noise). Hand tracking is likewise
  99.1% plausible at 1.4 m and 59.9% at 2.9 m. Measurements in `docs/evidence/f29/`.

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
