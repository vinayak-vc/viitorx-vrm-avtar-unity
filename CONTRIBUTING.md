# Contributing

Thanks for looking at Virtual Mirror. This project spans two repositories and a physical camera, so
a little orientation saves a lot of time.

## Repository layout

| | Repository | Contains |
|---|---|---|
| Unity | [`viitorx-vrm-avtar-unity`](https://github.com/vinayak-vc/viitorx-vrm-avtar-unity) | The UPM package: `Runtime/`, `Editor/`, `Tests/`, `Scenes/` |
| Sidecar | [`viitorx-vrm-model-python`](https://github.com/vinayak-vc/viitorx-vrm-model-python) | Python tracking sidecar, a submodule at `python-sidecar~` |

The `~` suffix is deliberate: it stops Unity importing 80-odd `.py` files and generating a `.meta`
for each. Do not rename it.

## Setup

See [README.md](README.md). In short: Windows, Unity 6000.3.9f1, Python **3.10 exactly** (depthai
ships `cp310` wheels only), an OAK-D camera, and `python-sidecar~/setup_sidecar.ps1`.

`setup_sidecar.ps1` verifies that onnxruntime's DirectML provider actually loaded. Do not skip it and
hand-roll the virtualenv — see *The one trap* below.

## Before you change anything

Read `AGENTS.md` at the Unity project root and `python-sidecar~/AGENTS.md`. They are the coding rules
and they take precedence over anything in `docs/`. The short version:

- `python-sidecar~`'s **root is the production path only** — the two entry points and the nine
  modules `wholebody_udp_sender.py` imports. A new harness goes in the matching `tools/` group, a
  new self-test in `tests/`. The Unity build copies the root verbatim, so a file left there ships to
  every player.
- Never build an evidence path by hand. Use `evidence_paths`:
  ```python
  import evidence_paths as EV
  EV.ensure_dir(EV.oak_v4("f21"))
  ```
  Paths assembled from a per-file `HERE`, or from the working directory, have silently broken
  harnesses twice.
- Do not change a tuned constant without a new measurement and an ADR.
- Do not break the UDP wire contract (`python-sidecar~/README.md`).

## The one trap

`onnxruntime` asks for DirectML and **falls back to CPU silently** when the provider cannot load.
Correctness is unaffected, so the only symptom is that everything is mysteriously slow — 183 ms per
frame instead of ~31 — and every timing number you report is wrong. Before trusting any throughput
figure:

```powershell
.venv\Scripts\python.exe -c "import onnxruntime as ort; print(ort.get_available_providers())"
```

`DmlExecutionProvider` must be listed. Never run the sidecar with `python` from `PATH`; that resolves
a different environment, which is exactly how this bites.

## Tests

Self-tests need no pytest. Each prints `N/N assertions passed` and exits non-zero on failure.

```powershell
.venv\Scripts\python.exe tests\test_target_ownership.py       # 75
.venv\Scripts\python.exe tests\test_kinematic_recovery.py     # 39
.venv\Scripts\python.exe tests\test_joint_tracker.py          # 37
.venv\Scripts\python.exe tests\test_surface_depth.py          # 32
.venv\Scripts\python.exe tests\test_pose_validation.py        # 22
.venv\Scripts\python.exe tools\diagnostics\f26_fidelity_analyze.py --selftest   # 28
.venv\Scripts\python.exe tools\diagnostics\f27_humanized_analyze.py --selftest  # 18
```

Unity EditMode tests must run in **batch mode with the editor closed** — running them over the
editor's own test runner has been observed to close it:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe" -batchmode -runTests `
  -projectPath "<your-unity-project>" -testPlatform EditMode `
  -testResults editmode-results.xml -logFile -
```

CI runs the sidecar suite on every push. It cannot run the Unity suite — that needs a licensed
editor — so run it locally before opening a PR that touches `Runtime/` or `Editor/`.

## Evidence and claims

This project keeps a measured record, and reports cite it. Two rules:

1. **Do not upgrade a result to PASS without a measurement**, and say plainly what was not tested.
   Several published findings have had to be retracted because an instrument was wrong; the
   self-tests exist because of that.
2. Raw captures are regenerable and git-ignored. The distilled `RESULTS.txt`-style files **are**
   tracked, because the reports and ADRs cite them. Follow the `.gitignore` pattern already in
   `docs/evidence/f26/` rather than adding blobs.

## Pull requests

- Branch from `main`.
- Keep the change and its evidence together: if you change a threshold, link the measurement.
- Update `CHANGELOG.md` under `## [Unreleased]`.
- Add an ADR to `docs/decisions.md` for a decision that a future reader would otherwise have to
  reverse-engineer. ADRs are numbered sequentially and are never deleted.
- Update `docs/tasks.md` and `docs/ai_handoff.md` — `AGENTS.md` §16 requires it, and the next person
  (or agent) picking this up starts there.

## Reporting bugs

Use the issue templates. For a tracking bug, the useful details are: OAK-D mounting orientation,
`ort.get_available_providers()` output, whether the sidecar was auto-started or run by hand, and the
`[SidecarLauncher]` lines from the Unity console.
