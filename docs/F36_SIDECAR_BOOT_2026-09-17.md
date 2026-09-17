# F-36 — Press Play once: the sidecar starts, then the menu opens

Date: 2026-09-17
Decision: **ADR-074**
Evidence: [`docs/evidence/f36/`](evidence/f36/)
Depends on: F-35 (the launcher), ADR-064 (Unity launches the sidecar itself)

---

## 1. What shipped

`Scenes/SidecarBoot.unity` at **Build Settings index 1**. It starts the multi-person sidecar, then
opens the launcher. Index 0 (`License-Verifier`) is untouched.

```text
Runtime/SkeletonShow/SidecarBootstrap.cs        NEW  starts the sidecar, then loads Launcher
Runtime/Tracking/OakD/SidecarProcessLauncher.cs +    DirectScript / DirectArguments
Runtime/SkeletonShow/SceneLauncher.cs           +    sidecar status in the footer
Runtime/Bootstrap/AppBootstrap.cs               ~    mirrorSceneName default back to "Mirror"
Editor/SceneRegistrar.cs                        +    puts SidecarBoot at index 1 on a fresh clone
Tests/EditMode/SidecarDirectLaunchF36Tests.cs   NEW  6/6
Scenes/SidecarBoot.unity                        NEW
ProjectSettings/EditorBuildSettings.asset       +    SidecarBoot at index 1
```

New build order: `License-Verifier → SidecarBoot → Bootstrap → Mirror → Launcher → …`

---

## 2. Why the supervisor could not be used

This is the finding that shaped the whole change.

`SidecarProcessLauncher` runs `sidecar_supervisor.py`, and the supervisor builds its child command
from a **fixed** flag set ([sidecar_supervisor.py:245-252](../python-sidecar~/sidecar_supervisor.py)):

```
--host --port --model --portrait --portrait-dir --subpixel-bits --seconds 0
```

`multiperson_udp_sender.py` accepts `--model --host --port` and **none** of the rest — it handles
portrait internally. Pointing `--script` at it exits on unrecognised arguments before the camera is
ever opened, and even with matching flags the supervisor waits on a readiness/heartbeat contract only
`wholebody_udp_sender.py` emits, so it would declare the child unready and restart-loop.

So the auto-start that already existed could only ever produce the **single-person** stream. Wiring
the launcher to it would have given a working ONE PERSON column and a dead TWO OR MORE column.

**`multiperson_udp_sender.py` is backward compatible** — it publishes the most-established person in
the ordinary single-person shape, including the `lh`/`rh` hand landmarks Air Graffiti and Objects
need. So one sender serves the single-person scenes, the crowd scenes and the avatar app alike, and
the boot scene runs that one, **directly**.

### The cost, stated plainly

The direct route has **no watchdog**. The supervised path restarts a dead sidecar, detects crash
loops and validates the environment (F-20B, 35/35); this does not. If the sidecar dies, it stays dead
until you leave play mode and press Play again.

That is an acceptable trade for a demonstration launcher and an unacceptable one for the product,
which is why **`AppBootstrap` is untouched and still supervised**. The change to
`SidecarProcessLauncher` is additive: `DirectScript` defaults to empty, so every existing caller gets
the supervisor exactly as before. A unit test pins that.

---

## 3. Why a separate scene and not `Bootstrap.unity`

Pointing `AppBootstrap.mirrorSceneName` at `Launcher` looks like the one-line version of this and
does not work:

* It loads that scene **additively** and then builds the avatar session against an `AvatarRoot`
  inside it. `Mirror.unity` has one; `Launcher.unity` does not.
* It **binds UDP 8899 itself** for its own tracking. Every scene the launcher then opens binds the
  same port, so none of them would receive anything.
* It survives scene loads with its camera and UI, which would fight `ShowStage.BuildCamera` — that
  takes `Camera.main` and repositions it.

`AppBootstrap` is the avatar application, not a router.

**`SidecarBootstrap` owns exactly one thing: the sidecar process.** No camera, no UI, no avatar, and
above all **no UDP socket** — each scene binds 8899 for itself, which is the whole point. It is
`DontDestroyOnLoad` purely so the sidecar outlives every scene switch.

A code-default change to `mirrorSceneName` was also found in the working tree and **reverted to
`"Mirror"`**. It had no effect (the value is `[SerializeField]`, so `Bootstrap.unity`'s serialized
`Mirror` wins) but it was a live trap: any newly added `AppBootstrap` would have defaulted to loading
a scene with no `AvatarRoot`.

---

## 4. It never starts a second producer

Two producers on one UDP port interleave poses from different sessions, which reads as violent jitter
rather than as a configuration error. Before spawning anything, `SidecarBootstrap` **listens on the
destination port for 400 ms**: if datagrams are already arriving — a sidecar started by hand, another
Unity instance — it attaches instead.

That tests the thing that actually matters (is somebody producing?) rather than a proxy like a lock
file, which a hand-started sender never creates. The supervisor's lock port is still set as well, so
if the *supervised* production path is already up, this attaches to that too.

The process itself is killed with Unity by the Win32 job object `SidecarProcessLauncher` already
sets up, plus a `taskkill /F /T` on the normal path — a stranded python holds the camera and blocks
the next run.

---

## 5. What was verified, and what was not

**Compile — 0 errors** across `VirtualMirror.Tracking`, `SkeletonShow`, `Editor` and `Tests`.

**Unit tests — 6/6 new, 187 passed overall.** The six failures are the pre-existing
`SidecarPathsTests`, which need `Application.dataPath`.

The new tests deliberately spend most of their effort on the **old** path: that `DirectScript`
defaults to empty and that the production defaults (host, port, lock port, portrait, subpixel) are
unchanged. A regression there would silently take the avatar application's watchdog out.

**The spawned command was executed** — [`evidence/f36/sender_args.txt`](evidence/f36/sender_args.txt).
Run with a deliberately bogus `--model`, it gets past argparse, past the interpreter check and past
provider selection (`DmlExecutionProvider` — the venv reaches the GPU) and fails only on the missing
model. That is the proof the flag set is accepted; the supervisor's set would not have been.

### NOT verified — this needs the camera

1. **Press Play on `Scenes/SidecarBoot.unity`.** Expect `[SidecarBoot] launching: …` in the console,
   the launcher within a frame, and poses after ~15 s while the model loads.
2. **Check the launcher footer** shows `sidecar: …` — the status line is new and has never rendered.
3. **Launch a scene from each column and ESC back**, confirming the sidecar survives the round trip
   and no scene fails to bind 8899.
4. **Start a sender by hand first, then press Play**, and confirm the console says *attaching to it
   instead of starting a second one* rather than spawning a rival producer.
5. **Exit play mode and confirm no orphan `python.exe`** is left holding the camera.

---

## 6. Known limits

* **No watchdog on this path** — §2.
* **ESC does not return from `Bootstrap`**, unchanged from F-35. Launching the avatar app from the
  launcher is a one-way trip.
* **`SidecarBoot` is not on the launcher's menu**, deliberately: selecting it would reload the
  launcher and re-probe the port for nothing.
* **The 400 ms probe delays boot by that much** when no producer is running. It is paid once.
* **`EditorBuildSettings.asset` is still gitignored**, so a clone needs
  `Virtual Mirror ▸ Register All Scenes in Build Settings` once — which now places `SidecarBoot` at
  index 1 rather than appending it.
