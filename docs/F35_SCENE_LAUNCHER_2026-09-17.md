# F-35 — One launcher scene, split by how many people a scene needs

Date: 2026-09-17
Decision: **ADR-073**
Evidence: [`docs/evidence/f35/`](evidence/f35/)
Depends on: F-28 (`SkeletonShowBootstrap`), F-30 (`ExperienceBase`, `TrackedStage`), F-34 (nine crowd
scenes)

---

## 1. What shipped

`Scenes/Launcher.unity` — a camera and one GameObject, like every other scene here. It lists all
twenty launchable scenes in three columns and loads any of them.

> **Since F-36, press Play on `Scenes/SidecarBoot.unity` instead.** It starts the multi-person
> sidecar and then opens this launcher, so the whole set works without a terminal. Opening
> `Launcher.unity` directly still works and still expects a sidecar you started yourself.

```text
Runtime/SkeletonShow/SceneLauncher.cs      NEW  the menu
Runtime/SkeletonShow/LauncherScene.cs      NEW  "back to the menu", shared by both scene bases
Runtime/Experiences/ExperienceBase.cs      +    ESC returns to the menu (all 18 experience scenes)
Runtime/SkeletonShow/SkeletonShowBootstrap.cs  +  the same, for the one scene that is not an
                                                   ExperienceBase
Editor/SceneRegistrar.cs                   NEW  one-click re-registration (see §3)
Tests/EditMode/SceneLauncherF35Tests.cs    NEW  10/10
Scenes/Launcher.unity                      NEW
ProjectSettings/EditorBuildSettings.asset  +    20 scenes registered
```

| Column | Scenes |
|---|---|
| **ONE PERSON** | Skeleton Show, Pose Match, Bubble Pop, Objects, Air Graffiti, Depth Reach, Fluid, Footprints, Time Echo |
| **TWO OR MORE** | Bonds, Collective, Stillness, Traces, Eclipse, Chord, Chain, Pass, Podium, Mirror Each Other |
| **PRODUCTION** | Virtual Mirror (`Bootstrap`) — the full avatar app, not a demonstration |

Controls: click a row, or arrows + Enter, or the number shown on the row (1–9 then 0, within the
highlighted column). **ESC returns to the menu from any scene.**

`Mirror.unity` is deliberately absent: `Bootstrap` loads it additively after wiring the settings,
the avatar and the UI, so opening it on its own gives an unwired scene. Launching `Bootstrap` is the
correct way to get the mirror.

---

## 2. The grouping is the feature

Nineteen scenes in one list is a list nobody reads, and the grouping that matters is not theme —
it is **how many people have to be in front of the camera**. Ten of these do nothing on your own:
Bonds needs somebody to bond with, Chain cannot be a chain of one, Mirror Each Other has nobody to
agree with. Picking one of those alone and watching it sit there is the single most likely way for
somebody to conclude the installation is broken.

So the menu answers that question before you choose. It also answers the one behind it:

**The launcher runs the real tracking while you choose.** Same `TrackedStage` every experience uses,
same UDP port, bodies drawn behind the menu. That is not decoration — it is where you find out that

* the sidecar is not running at all (`WAITING for the sidecar on UDP 8899`), or
* it is running in **single-person** mode, in which case the multi-person column says so in place of
  its description: *"SINGLE-PERSON SENDER upstream — these will only ever show one person. Run
  multiperson_udp_sender.py."*

Both of those were previously discovered *inside* a scene, where they look like a fault in the
scene rather than in what is feeding it.

---

## 3. A scene can only be loaded if it is in Build Settings

This is the part with a consequence worth stating. `SceneManager.LoadScene` fails at runtime for a
scene that is not registered, and Unity's failure is an error in the console *after* the click — so
the symptom is "the button does nothing".

Two things follow:

1. **All 20 scenes are now in `EditorBuildSettings.asset`**, which means they are in a player build.
   They are camera-and-one-GameObject scenes, so the size cost is negligible, but it is a real change
   to what ships. See ADR-073.
2. **The launcher checks anyway.** Every row's availability is resolved once at startup by walking
   the build list, and a missing scene is drawn greyed with *"NOT IN BUILD SETTINGS — add
   Scenes/&lt;name&gt;.unity"* instead of being a button that silently fails.

**Index 0 is still `License-Verifier`, so the startup scene of a build is unchanged.** If you want a
build to open on the menu, move `Launcher` to index 0 — that is a deliberate decision and has not
been made here.

### The registration does not travel with the repo

`ProjectSettings/EditorBuildSettings.asset` is **gitignored** (`.gitignore` line 10 of the Unity
project, which is the outer repo, not this package). The scene list is therefore per-machine: it is
registered here and a fresh clone will not have it. On that machine the launcher opens with every row
greyed out saying `NOT IN BUILD SETTINGS` — honest, and not a fix.

So **`Virtual Mirror ▸ Register All Scenes in Build Settings`** exists, in
[`Editor/SceneRegistrar.cs`](../Editor/SceneRegistrar.cs). It appends every scene under `Scenes/`
that is not already there and **never reorders, removes or disables anything**, because index 0 is
the startup scene of a build and a convenience must not be able to change what the product does on
launch. `Mirror.unity` is excluded: `Bootstrap` loads it additively, so registering it would invite
somebody to open it alone and find an unwired scene. There is also
`Virtual Mirror ▸ List Scenes in Build Settings`, which changes nothing and is the first thing to run
when a row is greyed out.

---

## 4. Returning to the menu

`ESC`, from every scene, via `LauncherScene`. Three details that are not obvious:

* **It lives in the SkeletonShow assembly, not Experiences.** Two different bases run these scenes —
  `ExperienceBase` (Experiences) and `SkeletonShowBootstrap` (SkeletonShow) — and Experiences
  references SkeletonShow, not the reverse. Anything both must call has to live in the lower one.
* **It does nothing when there is no launcher**, and says so once rather than once per keypress.
  Opening a single scene on its own in the Editor is a normal way to work and must keep working
  exactly as before; the key help line only advertises `ESC menu` when a launcher actually exists.
* **The UDP socket is released before the load, not in `OnDestroy`.** Every scene binds the same
  fixed port, so the order matters; both bases now stop tracking on the way out and skip the rest of
  the frame (`leaving`) rather than running Update and OnGUI against a disposed provider.

---

## 5. What was verified, and what was not

**Compile — 0 errors** across `VirtualMirror.SkeletonShow`, `VirtualMirror.Experiences` and
`VirtualMirror.Tests`.

**Unit tests — 10/10 new, 181 passed overall.** The six remaining failures are the pre-existing
`SidecarPathsTests`, which need `Application.dataPath` and are unrelated.

The launcher tests pin the invariants whose failure mode is *silent*, because every way a menu breaks
is silent: a typo in a scene name is a dead button, a duplicated row is a scene you can never reach,
and an eleventh row in a column is a scene the number keys cannot select. Covered: no duplicates,
columns disjoint, every row has a title and a description, the launcher does not list itself, no
column exceeds the ten rows the shortcut can address, and the path→name parsing including the cases
that would throw (`null`, empty, a dot in a folder name).

**Scene registry cross-check** — [`evidence/f35/scene_registry.txt`](evidence/f35/scene_registry.txt).
Every one of the 20 rows resolves to a file on disk *and* an entry in Build Settings: 0 problems.
That check needs the filesystem and the project settings, so it cannot run in a headless unit test;
it is a script whose output is kept instead.

### NOT verified — this needs the Editor

**Nothing here has been on a screen**, which is the same state F-34 is in.

1. **Open `Scenes/Launcher.unity` and press Play.** Check the three columns lay out at your
   resolution — the layout is computed from `Screen.width`, but it has never been rendered.
2. **Click one row in each column**, then press **ESC** to come back. The round trip is the thing
   most likely to be wrong, because it rebinds a UDP port on a fixed number.
3. **Launch `Bootstrap` from the menu.** It starts the whole app; confirm it still wires up, and note
   that ESC does *not* return from it (AppBootstrap is not one of the two bases).
4. **Watch the multi-person column's note** with the single-person sidecar running, then with
   `multiperson_udp_sender.py`. That line is the most useful thing the menu says and it is untested.

---

## 6. Known limits

* **ESC does not return from `Bootstrap`.** The production app has its own lifecycle and is not an
  `ExperienceBase`; wiring a demo key into it was out of scope and would need its own decision.
* **The catalogue titles are copied, not read.** The menu is in SkeletonShow and the experiences are
  in an assembly that references it, so it cannot see their types. A unit test pins the column
  membership; it cannot pin that a blurb still matches the scene's own `Instruction`. **If a title
  here ever disagrees with the scene it launches, the scene is right.**
* **No audio in the launcher.** `ExperienceAudio` lives in the Experiences assembly, which
  SkeletonShow cannot reference. A menu click is silent.
* **`EditorBuildSettings.asset` is gitignored**, so a clone needs one menu click before the launcher
  works — §3. Un-ignoring it would make the scene list travel, but it also carries the startup scene
  and anything anyone else has added locally, so that is a repo decision rather than one this change
  should make.
