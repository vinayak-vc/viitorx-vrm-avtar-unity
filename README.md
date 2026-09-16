# Virtual Mirror

A real-time virtual mirror. An OAK-D depth camera tracks a person standing in front of a display,
and a VRM avatar mirrors their movement on screen.

Tracking runs in a **Python sidecar** (whole-body 3D pose via RTMW3D on the GPU) that streams poses
over UDP to Unity, which retargets them onto the avatar's humanoid rig. Keeping inference out of
Unity's process is deliberate: a native inference crash cannot take the editor or the app down with
it (ADR-016).

> **Status: `0.1.0` — pre-release, not production-ready.** Several acceptance tests have never been
> run against a live camera, and there is a known visual defect. The version is `0.x` deliberately:
> the API may change in a minor release. Read [Known limitations](#known-limitations) before shipping
> this to anyone.

[![CI](https://github.com/vinayak-vc/viitorx-vrm-avtar-unity/actions/workflows/ci.yml/badge.svg)](https://github.com/vinayak-vc/viitorx-vrm-avtar-unity/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Unity 6000.3](https://img.shields.io/badge/Unity-6000.3-black.svg)](https://unity.com/releases/editor/archive)

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

### 0. Add the Unity package

This is a UPM package (`cloud.viitor.virtual-mirror`). Two of its dependencies do **not** resolve
from Unity's default registry, so add them first or the import will fail.

**a. Register the OpenUPM scope** for UniVRM, in your project's `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.vrmc"]
    }
  ]
}
```

**b. Install the MediaPipe Unity plugin.** `com.github.homuler.mediapipe` is not on any registry;
follow [homuler/MediaPipeUnityPlugin](https://github.com/homuler/MediaPipeUnityPlugin) and place it
under your project's `Packages/`.

**c. Add this package**, via *Window → Package Manager → + → Add package from git URL*:

```
https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git
```

or by adding it to `manifest.json` directly:

```json
"cloud.viitor.virtual-mirror": "https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git"
```

Pin a release rather than tracking the default branch — append `#v0.1.0`.

> The Python sidecar is a **git submodule** at `python-sidecar~`. Unity's Package Manager does not
> fetch submodules, so if you installed by git URL you must clone the repository yourself and use a
> local path (`file:` URL) to get a working tracking pipeline. Steps 1 and 2 below are required
> either way.

Remaining dependencies (`com.unity.render-pipelines.universal`, `com.unity.animation.rigging`,
`com.unity.nuget.newtonsoft-json`, `com.unity.ai.inference`) resolve automatically.

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

### Experience scenes (F-30)

Eight demonstration scenes under `Scenes/`, each a camera and one GameObject with everything built
at runtime. All of them have sound: a drone that follows whole-body energy, a movement layer, and
event cues — synthesised at runtime, so there are no audio assets to install. They all take the same sidecar on the same port — open one and press Play. With nobody
in front of the camera each shows a synthetic attract figure rather than a black screen.

| Scene | What a visitor does | Needs hands? |
|---|---|---|
| `Footprints` | Stand still; the floor grows a mark that remembers how long you stayed | no |
| `Fluid` | Move; the space swirls and keeps swirling after you stop | no |
| `ObjectPlay` | Bat, kick and head objects; pinch to pick one up and throw it | optional |
| `BubblePop` | Swing at bubbles. 60 s, score, combo | no |
| `PoseMatch` | Copy a ghost pose; your joints turn green as they land | no |
| `DepthReach` | Reach *into* rings at three real distances — the demo that needs the depth camera | no |
| `TimeEcho` | Move; four copies of you from seconds ago trail behind | no |
| `AirGraffiti` | Pinch thumb and finger together and draw in the air | **yes** |
| `SkeletonShow` | The five-mode show scene, including the trust HUD (F-28/F-29) | mode 5 only |

Sound is on by default in every experience scene; `M` mutes.

Shared keys in every experience: `R` reset, `H` humanized skeleton, `A` attract loop, `G` ground to
floor, `M` mute. Some add their own — `C` clears in Air Graffiti, Footprints and Time Echo, `N` skips
a pose in Pose Match.

**Hand-driven experiences need the subject close to the camera.** At ~1.4 m 99.1% of hand
observations are anatomically plausible; at ~2.9 m only 59.9% are. Air Graffiti and Object Play's
grabbing say so on screen rather than failing silently, but they will frustrate at three metres.
Everything else runs on body joints and is unaffected.

---

## How it is packaged (ADR-064)

The sidecar **source** ships inside the build; its **runtime** does not.

A post-build step copies the sidecar's root `.py` files, `requirements.lock.txt` and
`setup_sidecar.ps1` into `StreamingAssets/Sidecar/`. Since ADR-065 that root holds exactly the
production path — the two entry points plus the nine modules `wholebody_udp_sender.py` imports — so
the build ships what a player needs and none of the 70-odd development harnesses under `tools/` and
`tests/`. `python-sidecar~` stays the single source of truth; the trailing `~` keeps Unity from
importing those files and generating `.meta` churn for them.

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
* **Multi-person is UNSUPPORTED, and single-person ownership fails live.** The live two-person
  session *has* now been run (2026-09-16) and it found a second silent wrong-person route: while
  `LOCKED`, the ownership reference updates every accepted frame, so an observation migrating slower
  than the switch margin slides from one human to another without failing a single test. Measured at
  **0.015 m/frame against a 0.35 m margin — 23x under it**; 97 datagrams emitted, `target_id`
  unchanged, zero ownership events logged (ADR-061). A mechanism exists but
  **`drift_budget_m` defaults to `None` = disabled**, so production behaviour is unchanged; the
  budget is deliberately unset because it is unmeasured.

  Note what this layer is and is not. RTMW3D-x is a single-person top-down model — one inference,
  one skeleton, no detector, no track id — so the pipeline cannot track several people at once and
  F-21 does not attempt to. It exists only to hold **one** person and refuse to hand off silently.
  Offline it is strong (75/75 unit, 63/63 across 17 scenarios, 8-person footage with 0 switches);
  live it is not yet a safety property. Do not deploy this where a second person can enter frame.
* **F-22 L2 hands-near-face** has not been run live.
* **F-27 (humanized skeleton) has never run on stereo.** Both measurements used the video path,
  which synthesises every joint's depth, so every protective stage was idle.
* **F-28/F-29 (skeleton show scene) has never run with a live camera**, and F-29 has never been
  rendered on a screen by a human — it is verified by compilation, 23 EditMode tests and a headless
  harness that drives the real provider over a real socket with recorded packets. Every geometric
  and numeric claim is tested; no **visual** claim is. The trust HUD's depth row has consequently
  only ever shown `0/33 MEASURED`, because a video file has no stereo pair.
* **Floor contact degrades with distance, and is not usable far away.** Measured on the F-29 clips:
  the smoothed floor estimate is stable to 21 mm at ~1.4 m but only 81 mm at ~2.9 m, where
  smoothing makes it *worse* because the error is a slow drift rather than noise. Hand tracking
  fails the same way — 99.1% of hands are anatomically plausible at 1.4 m, 59.9% at 2.9 m, with
  palms reported up to 362 mm. Both are gated and reported rather than hidden, but neither should
  be built on at that range. See `docs/evidence/f29/measurements.txt`.
* **F-29 hand gesture thresholds are uncalibrated.** Pinch and finger-extension thresholds are
  derived from the distribution of ordinary hand poses in two dance clips, not from a subject
  performing each gesture on cue. They separate open from closed on that data; they have not been
  validated against intent.
* **The F-31 sound layer has never been heard.** It compiles and the synthesis arithmetic is
  checked, but `AudioClip.Create` is a Unity native call that cannot run outside a player, so every
  judgement about how it actually sounds — levels, whether the drone masks the cues, whether it is
  bearable for an hour — is unmade. Expect to retune; the mix constants are all named.
* **The eight experience scenes have never been rendered.** They compile, their shared
  tracking core is verified over a real socket with recorded packets, and F-29's 23 tests still
  pass — but no experience's own visuals or feel have been seen. Expect to tune constants on first
  run; they are all named, with the reasoning attached. The scene files were generated rather than
  authored in the Editor, so if a component shows as missing, the script `.meta` GUID and the
  scene's `m_Script` guid have diverged.
* **`SkeletonShowBootstrap` still carries its own copy of the tracking chain.** `TrackedStage` was
  extracted for the experiences; the F-28/F-29 scene was left alone because it is the one scene
  carrying verified evidence and the refactor could not be visually checked. Two copies of that
  chain will drift — this is the top follow-up.
* **The Unity EditMode suite has not been re-run** since `kalidokitBodyTorsoRoll` changed 0 → 1.
  F-29's own 23 tests were run outside the Editor (23/23) because the project lock was held.

---

## Documentation

[`CONTRIBUTING.md`](CONTRIBUTING.md) covers the development workflow and the coding rules.
[`CHANGELOG.md`](CHANGELOG.md) records what changed per release.
Start at [`docs/README.md`](docs/README.md) for the full index. For agents picking up this project,
`AGENTS.md` at the parent project root is the rulebook, and
[`docs/ai_handoff.md`](docs/ai_handoff.md) holds the current state.
