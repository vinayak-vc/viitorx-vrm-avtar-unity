# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While the version is `0.x`, the public API may change in a minor release. See
[Known limitations](README.md#known-limitations) before depending on it.

## [Unreleased]

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
