# Virtual Mirror

A real-time virtual mirror. An OAK-D depth camera tracks a person standing in front of a display,
and a VRM avatar mirrors their movement on screen.

Tracking runs in a **Python sidecar** (whole-body 3D pose via RTMW3D on the GPU) that streams poses
over UDP to Unity, which retargets them onto the avatar's humanoid rig. Keeping inference out of
Unity's process is deliberate: a native inference crash cannot take the editor or the app down with
it (ADR-016).

> **Status: v1. Not production-ready.** Several acceptance tests have never been run against a live
> camera, and there is a known visual defect. Read [Known limitations](#known-limitations) before
> demoing this to anyone.

---

## Prerequisites

| Requirement | Version / notes |
|---|---|
| Windows | 10 or 11. **Windows-only** — the GPU path uses DirectML and process teardown uses Win32 job objects. |
| GPU | DirectML-capable. Developed on an RTX 3060. |
| [Unity](https://unity.com/releases/editor/archive) | **6000.3.9f1** with URP 17.3.0 (editor only; not needed to run a build). |
| OAK-D camera | Luxonis OAK-D. Mounted in **portrait**, rotated counter-clockwise. |
| [Python](https://www.python.org/downloads/release/python-3100/) | **3.10 exactly.** `depthai` ships `cp310` wheels only; 3.11+ cannot resolve it. |
| `rtmw3d-x.onnx` | ~369 MB pose model. Not in git — see [Install](#install) step 2. |

---

## Install

### 1. Set up the Python sidecar

From `Assets/Games/viitorx-vrm-avtar-unity/python-sidecar~/` (or, in a build,
`<App>_Data/StreamingAssets/Sidecar/`):

```powershell
.\setup_sidecar.ps1
```

This creates the virtualenv, installs the pinned dependencies from `requirements.lock.txt`, and —
importantly — **verifies that the DirectML GPU provider actually loaded**. If it did not, the script
fails with instructions rather than letting you discover it later as unexplained slowness. See
[Troubleshooting](#troubleshooting).

Re-run with `-Force` to rebuild the venv from scratch. Pass `-PythonExe` if Python 3.10 is installed
somewhere unusual.

### 2. Install the pose model

`rtmw3d-x.onnx` is not committed (369 MB). Obtain it from the project's model store — ask the team
if you do not have it — then either pass it to the setup script:

```powershell
.\setup_sidecar.ps1 -ModelPath "D:\path\to\rtmw3d-x.onnx"
```

or copy it by hand:

* **Editor:** `Assets/SentisModel/rtmw3d-x.onnx`
* **Build:** `<App>_Data/StreamingAssets/Sidecar/models/rtmw3d-x.onnx`

### 3. Plug in the OAK-D

Mount it in portrait, rotated counter-clockwise. If yours is mounted clockwise, set
`sidecarPortraitDirection` to `cw` on the `AppBootstrap` component.

---

## Running it

### In the editor

1. Open `Scenes/Bootstrap.unity`.
2. On the `AppBootstrap` component, tick **Use Oak Udp Tracking**.
3. Press Play.

The sidecar starts automatically — you do not need a terminal. Unity spawns
`sidecar_supervisor.py`, streams its output into the Unity console prefixed `[SidecarLauncher]`, and
stops it when you exit Play mode. Expect roughly 15 seconds before poses arrive while the model
loads.

### From a build

Launch the executable. The sidecar starts the same way, resolved from `StreamingAssets/Sidecar/`.
The venv and the model must already be installed there (step 1 and 2 above).

### Running the sidecar by hand instead

Turn off **Auto Start Sidecar** on `AppBootstrap`, or simply start the supervisor yourself before
pressing Play — Unity detects an existing supervisor via its lock port (TCP 8897) and attaches to it
rather than spawning a second one. Two producers on one UDP port is a real failure mode and this
guard exists to prevent it.

```powershell
.venv\Scripts\python.exe sidecar_supervisor.py --model models\rtmw3d-x.onnx --portrait --portrait-dir ccw --subpixel-bits 3 --allow-port-listener
```

`--allow-port-listener` is required whenever Unity is in Play mode. The supervisor's port check binds
the destination port to detect a stale producer, which means it reads Unity's healthy listener as
"port in use" and refuses to start. This flag tells it that a listener is expected.

### Ports

| Port | Protocol | Purpose |
|---|---|---|
| 8899 | UDP | Pose stream, sidecar → Unity |
| 8897 | TCP | Supervisor single-instance lock |

---

## How it is packaged (ADR-064)

The sidecar **source** ships inside the build; its **runtime** does not.

A post-build step copies the 83 top-level `.py` files (~1.2 MB), `requirements.lock.txt` and
`setup_sidecar.ps1` into `StreamingAssets/Sidecar/`. `python-sidecar~` stays the single source of
truth — the trailing `~` keeps Unity from importing those files and generating `.meta` churn for
them.

Three things are deliberately excluded:

| Excluded | Size | Why |
|---|---|---|
| `.venv/` | 342 MB | **A Windows venv is not relocatable.** `pyvenv.cfg` pins an absolute `home` to the base interpreter and `Lib/` holds only `site-packages` — no stdlib. A copied venv breaks on any machine that lacks that exact Python at that exact path, and it breaks confusingly. `setup_sidecar.ps1` builds it on the target instead. |
| `rtmw3d-x.onnx` | 369 MB | Copying it into every build costs minutes for a file that changes roughly never. |
| `depthai_blazepose/` | 86 MB | The superseded Phase-1 BlazePose path. Named only in a docstring; never imported. |

**The trade-off:** the build stays small and every build is guaranteed to carry the same sidecar
revision the editor just ran, but the target machine needs a one-time setup pass and Python 3.10. It
is not a double-click install. Freezing the sidecar with PyInstaller would remove the Python
dependency entirely and is the natural v2 step; it was not attempted for v1 because
`onnxruntime-directml`, `depthai` and `opencv` are all awkward to freeze.

---

## Troubleshooting

**Everything is slow — roughly 5 fps.**
The sidecar is running on CPU. `onnxruntime` asks for DirectML and falls back silently when the
provider cannot load, so nothing reports an error; inference goes from ~31 ms/frame to ~183 ms.
Check it:

```powershell
.venv\Scripts\python.exe -c "import onnxruntime as ort; print(ort.get_available_providers())"
```

`DmlExecutionProvider` must be listed. If it is not, plain `onnxruntime` has usually been installed
over `onnxruntime-directml`:

```powershell
.venv\Scripts\python.exe -m pip uninstall -y onnxruntime onnxruntime-directml
.venv\Scripts\python.exe -m pip install -r requirements.lock.txt
```

Never work around a missing venv by running the sidecar with `python` on PATH. That is a different
environment and it is the cause of this exact symptom, which is why the launcher refuses to fall
back to it.

**"Sidecar: FAILED" on the HUD.**
The message names the missing file. Usually the venv (run `setup_sidecar.ps1`) or the model.

**Port 8899 is in use / a stranded python process.**
Should not happen — the child is assigned to a Win32 job object, so Windows kills the whole tree
whenever Unity exits, including on a crash. If you do find one, kill that PID specifically. Do not
run `taskkill /IM python.exe`; it will take down unrelated Python processes.

**The avatar freezes after editing a script while playing.**
A script recompile leaves Play mode looking alive while the C# services are gone — `AppBootstrap`'s
providers come back null and `manage_editor play` reports "Already in play mode" in that half-dead
state. Exit Play and re-enter.

---

## Known limitations

These are open. Do not describe them as done.

* **Open visual defect:** the forearm and hand clip into the hip when the arm hangs. The avatar's
  shoulders are only 6.4 cm wider than its hips, the clavicles are never driven, and VRM 1.0's
  normalised control rig is a T-pose, so there is no authored A-pose clearance. Nothing in the
  pipeline does body-volume avoidance. **Not fixed.**
* **The avatar is not proven faithful.** F-26 established that F-19's "avatar quality proven good"
  rested on four internal-consistency metrics, all of which a stably-wrong pose also satisfies.
* **F-21 live two-person acceptance has never been run.** ADR-061's drift budget is unmeasured, and
  single-person ownership is not a safety property on current evidence.
* **F-22 L2 hands-near-face** has not been run live.
* **F-27 (humanized skeleton) has never run on stereo.** Both measurements used the video path,
  which synthesises every joint's depth, so every protective stage was idle.
* **F-28 (skeleton show scene) has never run with a live camera.**
* **The Unity EditMode suite has not been re-run** since `kalidokitBodyTorsoRoll` changed 0 → 1.

---

## Documentation

Start at [`docs/README.md`](docs/README.md) for the full index. For agents picking up this project,
`AGENTS.md` at the parent project root is the rulebook, and
[`docs/ai_handoff.md`](docs/ai_handoff.md) holds the current state.
