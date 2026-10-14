# HE-01 - Human Echo: the cutout becomes smoke, echo copies, a neon outline and GPU particles (2026-10-14)

Decision: **ADR-104**. Tasks: `tasks.md` HE-01. Showcase: `Showcase/HumanEcho/README.md`. Follows S-01 (`S01_CUTOUT_2026-10-13.md`, ADR-103).

The request: build, close to the three reference clips in `Ref/` (`Video-27845` a group with rainbow smoke ghosts, `Video-14008` a figure with red echoes and red spheres, `Video-11367` a dancer made of hue-cycling particles that pile on a floor), on top of the S-01 cutout stream and with the FluXY asset; validate the approach first (a spike), then build all three features in one scene with switchable looks, a Launcher row, tests and documents. Defaults kept: compute-shader particles (no VFX Graph), 1080p on an RTX 3060, not audio-reactive, no new model downloads.

## 1. What exists

```
S-01 cutout stream (picture + soft mask, UDP 8903; no Python change)
  -> PersonField (GPU, once per new mask)
        Field   picture rgb + mask a, upright, mirrored when asked
        Motion  normal flow of the mask edge (quarter-size blurred mask), PICTURE heights per second + confidence
  -> layers (IHumanEchoLayer), each on its own quad, back to front by render queue
        Smoke 3000      FluXY fluid (dye = picture + drifting hue, velocity = Motion); ink or dots; hue spread over the body
        Echo 3010       ring of past masks (Texture2DArray), copies: hue ramp / one colour, offset, wave, solid / stipple / outline
        Particles 3020  compute pool, born on the mask, carried by FluXY's velocity, gravity, floor pile
        Person 3030     the cutout (picture / grayscale / flat), flash, rim
        Outline 3040    neon edge, optional jitter, optionally the mask of N echo copies ago
  -> HDR orthographic camera + URP Bloom
```

A **look** (`HumanEchoSettings`, three assets: Rainbow Smoke, Red Echo, Particle Body) says which layers are on and how each is tuned; every layer reads the active look every frame, so the number keys switch instantly. Layer toggles (S E B P L), mirror (M), clear fluid (C), debug tiles (D) live on the controller, never on the assets. `HumanEchoUI` is the HUD and the key map (pure parts: `HumanEchoKeys`, `HumanEchoInputState`, `HumanEchoLayerRules`, `HumanEchoStatusFormatter`).

The Launcher lists it (**HUMAN ECHO**, SHOWCASES column, `VisionOnly`). Escape returns to the Launcher.

## 2. The spike (validated before the rest was built)

| # | Question | Result |
|---|---|---|
| S1 | Can the live mask drive FluXY dye + velocity? | Yes: a glowing coloured silhouette that trails movement and sheds wisps. Found and fixed: a shader used with `Graphics.Blit` must declare `_MainTex` in Properties (the first motion field was constant); normal flow on the full-resolution mask under-estimated 10x, so it runs on a quarter-size blurred mask |
| S2 | Can a compute shader sample FluXY's velocity on the GPU? | Yes: with flow influence 0 the mean particle speed was 0.0000, with 6 it was 0.18 screen heights/s upward (buoyant plume, orientation right) |
| S3 | `Graphics.RenderPrimitives` from a structured buffer in URP? | Yes: 200 000 slots drawn as lit spheres and dots |
| S4 | Cost | smoke only 0.92 ms GPU, smoke + particles 1.14 ms (Editor floor about 0.8 ms) |

## 3. Verification (2026-10-14, Unity 6000.3.9f1, RTX 3060, 1920 x 1080 game view)

| Check | Result |
|---|---|
| Compile | 0 errors; the seven shaders and the compute shader import with 0 messages; Console after Play clean |
| `HumanEcho.Tests` | **99 passed**, 0 failed (GPU stages on synthetic textures, pure logic, HUD and keys) |
| `VirtualMirror.Tests` | **608 passed**, 0 failed, 2 parameterised cases not run by the reflection runner (the new one: `SceneLauncherF35Tests.HumanEchoNeedsNoBodyEitherAndIsListedWithTheShowcases`) |
| `CutoutShowcase.Tests` | **11 passed** |
| Mutation checks | broken lines (flow sign, upright flip, mirror, pile cap, fluid mapping and scale, freshness window, LIVE rule, layer rule, blank reset, ring dedupe) each made at least one test fail and were restored; two tests were too weak (the blank-reset and the ring dedupe) and were tightened |
| Looks on video | all three looks on `video.webm` (one person, portrait) and `147_1080p.mp4` (two people, landscape), through the real sender |
| Cost (two-person clip, real sender on the same GPU, 300 frames) | all layers off 0.45 ms GPU; Rainbow Smoke 1.41 ms; Red Echo 1.27 ms; Particle Body 1.26 ms; display 175 to 200 fps; mask 22 to 24 per second (unchanged by the effects) |
| Stall | the sender killed: the person left every layer within about a second, the HUD read `WAITING FOR CAMERA` |
| Review | three read-only lenses (GPU, C#, scenarios) found 26 points; nearly all were fixed (see ADR-104) |

Tests were run by reflection in the open Editor (`Showcase > Human Echo > Run Self Tests`, and the same for the repository's assemblies): the Unity MCP `run_tests` closes this Editor.

## 4. Limits and what was NOT tested

- **Not tested: a person in front of a LIVE webcam**, a network camera, a built player (no sender ships), screens other than 1920 x 1080, the HUD beside the F6 panel, the Escape key press by hand, the controller's Dispose lifecycle.
- Mask limits of S-01 apply (dark clothes on dark, floor lights, thin gaps; a floor light is a stray blob in Particle Body on the two-person clip). The motion is a silhouette estimate, not limb tracking. No depth: the particle body's volume is faked by size and shading.
- FluXY runs slow-motion below about 31 fps at `maxSteps` 4. There is no run-time quality switch (resolution, supersampling, particle count are look settings). A tiny or perfectly still silhouette emits fewer particles than the rate says.
- 92 lines (code and attributes) of the package are over the `.editorconfig` 110 columns; the existing code has the same shape (about 3 to 4 % of lines). The comment lines were wrapped.
- Looks are close in spirit to the clips, not frame matches; the clips are phone recordings of a projection wall.

## 5. Where things are

`Showcase/HumanEcho/{Scripts,Shaders,Settings,Editor,Tests,Scenes,README.md}`; `Runtime/SkeletonShow/SceneLauncher.cs` and `Tests/EditMode/SceneLauncherF35Tests.cs` (Launcher row and test); `docs/decisions.md` ADR-104; the build settings entry lives in the (git-ignored) `ProjectSettings/EditorBuildSettings.asset` of the outer project. Work records of the overnight run: `AI_WORKLOG.md` and `.ai-work/` at the repository root (checkpoints, contracts, review findings).
