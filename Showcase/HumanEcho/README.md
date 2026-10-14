# Human Echo showcase

The live person, cut out of the camera picture by the Python sidecar, turned into a stage effect in one scene: **smoke** that rises off them (a real FluXY fluid simulation fed by their silhouette and its motion), **echo copies** of where they were a moment ago, a **neon outline**, the cutout itself with a **flash** and a coloured **rim**, and **GPU particles** that fall off the body and pile up on a floor line. Three looks, one per reference clip, switch live with the number keys. It combines the three planned features of `AI_WORKLOG.md` (F1 smoke + outline, F2 echo + person + flash, F3 particles) in one scene.

It is not audio-reactive and it does not use the VRM avatar or skeleton tracking. RGB only: there is no depth, so the "volume" of the particle body is faked.

Scene: `Scenes/HumanEcho_Showcase.unity`. Open it (`Showcase > Human Echo > Open Showcase Scene`) and press Play. Rebuild it from code with `Showcase > Human Echo > Build Showcase Scene` (safe to repeat: it rewrites the scene only and keeps your tuned looks). The Launcher lists it as **HUMAN ECHO** in the SHOWCASES column (`Tracking need: VisionOnly`: no body tracking, only the cutout stream).

## Run it

1. First time only, the sidecar's two selfie networks (0.46 MB each) and, for one crop per person, the person detector:
   `python-sidecar~/.venv/Scripts/python.exe python-sidecar~/tools/model/fetch_cutout_models.py` and `python-sidecar~/tools/model/fetch_yolox_s.py`. The models are not in version control.
2. Open the scene and press Play. The scene's own `Tracking (AppBootstrap)` has the body path off and only the **cutout** stream on (`Auto`); it starts the cutout sender on **`camera:0`**. The first frame takes a few seconds (the networks load). Until a frame arrives the HUD reads `WAITING FOR CAMERA`.
3. Another source (a second webcam, a phone, a LAN camera link, a video file): select `Tracking (AppBootstrap)` > **Hands and Face (H-03)** > **Source** and type `camera:1`, a file path (for example `Assets/Games/video/147_1080p.mp4`) or a link, then press Play; or change the source at run time in the F6 vision control panel (its **Source** tab). As in the Cutout showcase, the sender restarts on the new source a second later. A video file loops (`Loop File`) and the HUD says `VIDEO FILE`, never `LIVE`; with Mirror `Auto` a file is not flipped, a camera is. Do not save the scene with a test source in it: the committed scene keeps `camera:0`.
4. From the Launcher: pick **HUMAN ECHO**. On another machine run `Virtual Mirror > Register All Scenes in Build Settings` once so the Launcher can load it (`ProjectSettings/EditorBuildSettings.asset` is not in version control).

One webcam can be read by one program at a time: do not run this beside the Hands, Face or Cutout scene on the same camera (each stream is `Auto`, so only the open scene runs its sender).

## On screen and the keys

The panel at the top left (about 330 x 262 points, scaled up on tall screens) shows: the title; **one input line**; the active look (`LOOK 2/3   Red Echo`) with what it is meant to resemble; frames per second, mask frames per second and what the mirror is really doing; one status line from the controller (source, people, foreground percent, latency); the five layers (green = being drawn: switched on and used by the look; grey = not drawn; a fluid that runs only to carry particles counts as not drawn); and the key list. While the F6 panel is open the HUD moves to the right edge.

The input line says `LIVE` only while the cutout texture set holds a frame whose upload counter changed in the last 2 seconds. A video file says `VIDEO FILE`. Anything else says `WAITING FOR CAMERA` (with how long the silence has lasted once frames have been seen before). It never says live because a sender was started. When the stream stops for a second the person is cleared from every layer (the HUD does not freeze on the last frame).

| Key | Does |
|---|---|
| `1` - `9` (also the keypad) | Select look 1 to 9 (a number with no look behind it does nothing) |
| `Tab` | Next look |
| `M` | Mirror: Auto, On, Off |
| `C` | Clear the fluid |
| `D` | Debug tiles (the intermediate textures the layers read) |
| `S` `E` `B` `P` `L` | Turn a layer on or off: Smoke, Echo, Body (the person), Particles, outLine |
| `H` | Hide or show the whole HUD |
| `F6` | The vision control panel (the bootstrap owns it) |
| `Esc` | Back to the Launcher (does nothing when the scene was opened directly) |
| `O` | Left to the AppBootstrap |

The layer keys work on top of what the active look's own settings say: a look that does not use a layer keeps it off.
While the F6 panel is open and its **Source** box has been clicked, letters go into the box and the hotkeys wait; close the panel with `F6` to get them back.

## The three looks

Assets in `Settings/`, in this order. The values were tuned by eye against the clips with the two test videos (see **What was checked**); the assets are the product, and `Editor/HumanEchoPresetBuilder.cs` holds the same values as code. Each look's `notes` field says what it is for.

| # | Asset | Follows | Layers |
|---|---|---|---|
| 1 | `HumanEchoLook_RainbowSmoke` | `Ref/Video-27845.mp4`, a group dance: true-colour cutouts, rainbow dotted smoke ghosts, white-hot flashes, jagged electric outlines | Smoke on (Dots, hue drift, hue spread over the body height 0.55); Echo on (6 copies, hue ramp, stipple); Outline on (hue cycle, jitter 3); Person in picture colours with a little flash; Particles off |
| 2 | `HumanEchoLook_RedEcho` | `Ref/Video-14008.mp4`, a black stage: a white figure with a red rim, flat red copies trailing sideways, storms of red spheres and dots | Person grayscale with a red rim; Echo on (8 solid red copies, offset sideways, wavy); Particles on (solid red, half spheres, size 3 to 20 thousandths of a screen height, no floor); the fluid runs undrawn (it only carries the particles); Outline off |
| 3 | `HumanEchoLook_ParticleBody` | `Ref/Video-11367.mp4`, a single dancer made of hue-cycling dots and spheres that fall and pile on a floor | Particles on (hue over time, emit from inside and edge, floor with a pile); Outline on (thin white); Person almost invisible (opacity 0.08); the fluid runs undrawn; Echo off |

The reference clips are 720 x 720 phone recordings of a projection wall, in `Ref/` at the Unity project root (outside this repository).

`Showcase > Human Echo > Reset Looks To Defaults` writes these code values over the three assets after a confirmation dialog (a warning in the Console says tuning was discarded). The scene builder never does that: it only creates a look that does not exist yet.

## How it works

```
camera / stream / video file
  -> Python sidecar (cutout_udp_sender.py): one soft person mask + the picture, UDP 8903     [no Python change]
  -> AppBootstrap vision host: CutoutReceiver -> CutoutTextures (picture + mask)
  -> HumanEchoController: PersonField, once per new mask frame
        Field   RGBA: picture + mask, upright, mirrored when asked
        Motion  silhouette velocity in PICTURE heights per second + confidence (a quarter of the size)
  -> five layers, each a quad drawn in a fixed order (render queue, back to front), all reading the ACTIVE look every frame
        3000 Smoke      FluXY fluid: the silhouette injects dye (Field) and velocity (Motion); drawn as ink or dots
        3010 Echo       a ring of past masks, redrawn as copies (hue ramp or one colour, offset, wave, solid / stipple / outline)
        3020 Particles  GPU particles born on the mask, carried by the fluid's velocity, gravity, floor pile
        3030 Person     the cutout itself (picture, grayscale or flat; flash; rim)
        3040 Outline    the neon edge of the mask (optionally the mask of N echo copies ago)
  -> orthographic HDR camera + URP Bloom
  -> HumanEchoUI (keys, status) reads the controller; it draws and calls, it holds no state of the effect
```

The motion field is an estimate of how the **silhouette** moved (the normal flow of the mask edge on a small, pre-blurred copy), not a measurement of limbs, and it is only as good as the mask edge. It is computed on a quarter-size blurred mask on purpose: the full-size mask under-estimated the speed about ten times.

The particles are a compute-shader pool (a ring: the oldest slot is reused), drawn by `Graphics.RenderPrimitives` from a structured buffer. They read FluXY's velocity atlas directly on the GPU (nothing is read back). The floor pile is a column histogram with a fixed-point counter.

## Files

```
Scripts/  HumanEchoController            conductor: frame order, looks, layer toggles, mirror, debug tiles, status line, Esc
          PersonField                    picture + mask -> Field and Motion (GPU); Blank() empties it
          HumanEchoLayer                 IHumanEchoLayer and FrameContext, the contract every layer follows
          IFluidFlow                     where a layer reads the fluid's velocity
          LayerQuad / LayerResources     the quad every layer draws on (render queues, contain-fit); object-lifetime helpers
          SmokeLayer                     FluXY smoke (also the IFluidFlow the particles read)
          EchoLayer / EchoRing / EchoSchedule   echo copies, the shared ring of past masks, the pure capture-timing rule
          OutlineLayer                   neon outline
          PersonLayer                    the cutout itself
          ParticleLayer                  GPU particles and the floor pile
          HumanEchoSettings              one look; PersonSettings, SmokeSettings, EchoSettings, OutlineSettings, ParticleSettings
          HumanEchoTypes                 MirrorMode, HumanEchoLayerId
          HumanEchoUI                    the HUD and the keys (IMGUI)
          HumanEchoKeys                  key -> command map (pure)
          HumanEchoInputState            when the input may be called LIVE (pure)
          HumanEchoLayerRules            when a layer counts as drawn: switched on AND enabled by the look (pure)
          HumanEchoStatusFormatter       the HUD's text (pure)
          HumanEchoAssemblyInfo          InternalsVisibleTo the tests
Shaders/  HumanEcho_PersonField, HumanEcho_Smoke, HumanEcho_SmokeInject, HumanEcho_Echo, HumanEcho_Outline, HumanEcho_Person,
          HumanEcho_ParticleDraw, HumanEcho_Particles.compute
Settings/ HumanEchoLook_RainbowSmoke / _RedEcho / _ParticleBody, HumanEchoPostProcess (Bloom)
Editor/   HumanEchoSceneBuilder          Showcase > Human Echo > Build Showcase Scene / Open Showcase Scene
          HumanEchoPresetBuilder         creates the looks; Showcase > Human Echo > Reset Looks To Defaults
Tests/    99 EditMode tests in HumanEcho.Tests (see below); HumanEchoTestRunner = Showcase > Human Echo > Run Self Tests
Scenes/   HumanEcho_Showcase.unity
```

The scene builder finds each shader by its file in `Shaders/`, then by its declared name, and warns (naming the field) about anything it cannot find: the scene still builds and that layer stays off.

## Tests

`HumanEcho.Tests` holds 99 EditMode tests: the GPU stages driven with synthetic textures on the real GPU (`PersonFieldTests` orientation, mirror, motion direction and size, blank; `ParticleLayerTests` spawn, flow, pile, caps; `EchoRingTests` shared-ring capture), the pure logic (`EchoScheduleTests`, `EchoColorTests`) and the HUD and keys (`HumanEchoUITests`). Unity's **MCP `run_tests` closes this Editor**, so they were run in the open Editor through `Showcase > Human Echo > Run Self Tests` (reflection over the same NUnit methods; the report is `Temp/HumanEchoTestResults.txt`) and the Unity Test Runner (Window > General > Test Runner > EditMode) runs them too. With the Editor closed, `Unity -batchmode -runTests -testPlatform EditMode -assemblyNames HumanEcho.Tests` is the standard command-line route (not run for this package: the Editor was open throughout). Nothing in `HumanEcho.Tests` needs a camera or the sidecar.

## Tuning

Edit the three assets while the scene plays: every layer re-reads the active look each frame. (As with any ScriptableObject in the Editor, a value changed in Play mode stays changed afterwards.) What to reach for first:

| To get closer to... | Change |
|---|---|
| more or fewer ghosts | `echo.copies`, `echo.spacingSeconds` (seconds between copies), `echo.opacityNewest` / `opacityOldest` |
| ghosts trailing sideways | `echo.offsetPerCopy` (screen heights), `echo.waveAmount` |
| denser or finer dots | `smoke.dotsPerScreenHeight`, `echo.dotsPerScreenHeight`, `particles.sizeMin/MaxThousandths` |
| more rainbow in the smoke | `smoke.hueSpread` (hue range over the body height), `smoke.hueSpeed` |
| more flash | `person.flash` (HDR, so Bloom flares it); Bloom itself is in `Settings/HumanEchoPostProcess.asset` |
| a more jagged outline | `outline.jitterThousandths`, `jitterFrequency`, `jitterSpeed` |
| a bigger particle storm | `particles.spawnPerSecond`, `motionBoost`, `sphereFraction`, `maxParticles` |
| a taller or lower pile | `particles.floorHeight`, `pileMaxHeight`, `pileSeconds` |
| a lighter load | `smoke.resolution` (FluXY rounds down to a power of two) and `smoke.supersampling`, `particles.maxParticles`, `echo.copies` |

Source, camera size, mask size and every segmentation option are the AppBootstrap "Hands and Face" section or the F6 panel, not this package. There is no run-time quality switch: the load knobs above are look settings.

## Dependencies

- The Python sidecar's **cutout stream on UDP 8903** (`python-sidecar~/tools/cutout/cutout_udp_sender.py`, started by the scene's AppBootstrap) and the two selfie ONNX models it needs. **No Python change was needed** for this showcase.
- **FluXY 1.4.1** in `Assets/FluXY` (outside this repository; it carries the local patches of ADR-092 and this package never edits it).
- **URP 17.3** with a global Volume carrying **Bloom**; Unity 6000.3.9f1.
- Assemblies: `HumanEcho` uses `VirtualMirror.Core`, `VirtualMirror.App`, `VirtualMirror.SkeletonShow`, `VirtualMirror.Vision`, `fluxy` and the URP runtime; `HumanEcho.Editor` also uses `ChromaticRain.Editor` (`ShowcaseBuilderUtil`).
- Compute shaders for the particles (no VFX Graph package: it is not installed).

## What was checked (2026-10-14, Unity 6000.3.9f1, RTX 3060, game view 1920 x 1080)

- **Compile and Console:** the whole project compiles with 0 errors; the seven Human Echo shaders and the particle compute shader import with 0 messages; the Console after Play shows no errors or warnings from this package.
- **Tests:** `HumanEcho.Tests` 99 passed, 0 failed. The repository's own `VirtualMirror.Tests` 608 passed, 0 failed (two parameterised cases are not run by the reflection runner; the new Launcher test is `SceneLauncherF35Tests.HumanEchoNeedsNoBodyEitherAndIsListedWithTheShowcases`). `CutoutShowcase.Tests` 11 passed. The GPU and UI tests were mutation-checked: a deliberately broken line (flow sign, upright flip, mirror, pile cap, fluid mapping, freshness window, LIVE rule, layer rule, blank, ring dedupe) made at least one test fail each time, and was restored.
- **Looks against the clips:** all three looks were seen in Play on `Assets/Games/video/video.webm` (one person, portrait) and `Assets/Games/video/147_1080p.mp4` (two people, landscape), through the real sender. Rainbow Smoke reads as a true-colour cutout with a jagged hue-cycling outline, stippled hue-stepped echoes and dotted multi-hue smoke; Red Echo as a grey figure with red rim in front of flat red copies and a red sphere storm; Particle Body as a hue-cycling dotted body, a white outline and a pile on the floor. They are close in spirit to the clips, not frame matches.
- **Cost (Editor, two-person clip through the real sender on the same GPU, FrameTimingManager, 300 frames):** all layers off 0.45 ms GPU; Rainbow Smoke 1.41 ms; Red Echo 1.27 ms; Particle Body 1.26 ms. Display 175 to 200 fps in the Editor; mask 22 to 24 frames per second, unchanged by the effects (the sidecar sets it).
- **Stall:** killing the sender made the person disappear from every layer within about a second and the HUD read `WAITING FOR CAMERA`.
- **Launcher:** the row exists, the scene is in this machine's Build Settings, and the Launcher unit test passes.

## Limits

- The mask comes from a small selfie network applied once per person crop. Dark clothes on a dark background, bright floor lights and thin gaps between an arm and the body can be kept or cut wrongly, and the edge is soft by design: the outline and the echo copies show exactly that. On the two-person clip a floor light survives as a stray blob in Particle Body. See `Showcase/Cutout/README.md`.
- FluXY runs fewer solver steps when the frame rate drops (`maxSteps` 4 in these looks): below about 31 fps the smoke moves in slow motion. The 1080p numbers above are well above that on the development PC.
- Particle emission is per probed pixel (32 probes per slot): a tiny or perfectly still silhouette emits fewer particles than `spawnPerSecond` says.
- A built player has no sender (`tools/` is not shipped): start `cutout_udp_sender.py` by hand.
- The three looks are starting points, not a finished grade. They are tuned by eye against clips filmed off a projection wall, not against the cutout itself.
- Everything is drawn from RGB and a mask: no depth, no sense of the body's volume, no audio.
- The HUD cannot see whether a layer built successfully: a layer whose shader is missing still shows green when switched on and used by the look. The Console carries its error.

## NOT TESTED

- **A person standing in front of a live webcam in this scene.** The cutout has only been seen on video files (through the real sender) and, once, on a webcam that saw nobody (numbers only). Live latency, a mirror that matters, and a real room are untested.
- A network (LAN / RTSP) camera as the source.
- A player build. Nothing was measured outside the Editor, and a player cannot start the sender.
- Screens other than 1920 x 1080, and the HUD beside the F6 panel (it moves to the right edge while the panel is open).
- `Esc` back to the Launcher by hand: the logic is simple and the Launcher entry is tested, but the key press itself was not exercised.
- The controller's and layers' `Dispose` / scene-unload lifecycle has no EditMode test (the layers were seen to build and free in Play, with no Console messages).
- A portrait phone held upright as a live source: portrait is covered only by `video.webm`.
