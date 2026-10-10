# Chromatic Rain showcase

A dark, minimalist, interactive installation scene. Colourful rain falls over a near-black screen;
a tracked person is a rain-free, human-shaped silhouette; their movement stirs a layer of chromatic
fluid (FluXY) behind the rain.

Scene: `Scenes/ChromaticRain_Showcase.unity` - open it and press Play. It is independent of the
production scenes, is **not** in Build Settings and does not touch the app's startup behaviour.

## Run it

**Tracking is handled by the `AppBootstrap` in the scene** (`Tracking (AppBootstrap)`), the same component and
Inspector sections as `Bootstrap.unity`. It starts and supervises the Python sidecar and chooses the source;
the showcase only listens to the stream, like every experience scene. Nothing about sources is duplicated here.

| Goal | How |
|---|---|
| OAK-D camera | Open the scene, press Play. (`Use Oak Udp Tracking` + `Auto Start Sidecar` are on.) |
| **Video, webcam, phone, LAN camera** | Select `Tracking (AppBootstrap)` > **Video Test Source (F-49)**: tick `Track From Video`, set `Video Source` (a file path, `camera:0`, a phone IP-camera link, or a LAN-camera viewer link + PIN), press Play. Changing a field while playing restarts the sidecar for you. Status chip: `LIVE (VIDEO name)`. |
| Through the app | Play `Bootstrap.unity`, open **CHROMATIC RAIN** from the Launcher. The scene's own bootstrap destroys itself and the real one keeps tracking. **ESC** returns to the menu. |
| No camera at all | Select `Chromatic Rain Showcase`, set **Source = Simulated**. (**Auto**, the default, switches to the simulation after `autoFallbackSeconds` with no live data and says so in the chip.) |

The scene is registered in Build Settings by `Virtual Mirror > Register All Scenes in Build Settings`, and is a row in the Launcher.
If another scene or the mirror app already holds UDP 8899, the chip reads `TRACKING PORT IN USE`.
Use **Live** for an unattended installation: the sidecar sends nothing for an empty room, so Auto would fall back to the simulation.

Keys (Game view focused): `S` source · `N` Dark Silhouette / Negative Space · `F` fluid layer · `M`
mirror · `R` recenter/reset · `C` clear fluid · `D` debug view · `H` hide the overlay text. While
the debug view is up: `1-4` simulated arm pattern, `P` stop/start the simulated stream, `T`
still/travel, `K` hands on/off, `L` lose the left arm joints, `J` lose the right leg joints.

Status chip: `LIVE` only while accepted, fresh poses are arriving; `SIMULATED` (or `SIMULATED (no
live signal)` in Auto); `NO SIGNAL`; `TRACKING PORT IN USE`. The second half says `person tracked`
or `no person`; it never claims a person just because the scene runs. Note the sidecar sends NO
pose when nobody is in frame, so with Source = Live an empty room reads `NO SIGNAL`, and in Auto an
empty room falls back to the simulation after the timeout. Use **Live** for an unattended installation.

## What is where

```
Scripts/
  ChromaticRainShowcase.cs      controller: lifecycle, source choice, the order of a frame
  ShowcasePoseAdapter.cs        existing SkeletonPose/HandPose -> view-space ShowcaseBody (smoothing,
                                confidence/trust, joint grace + fade, snap)
  ScreenSpaceMapper.cs          world metres -> view metres (life-size, orthographic)
  ShowcaseStateMachine.cs       states A-F, debounce, silhouette alpha, fluid gate
  SilhouetteBuilder.cs          landmarks -> capsules + head ellipse for the mask shader
  SilhouetteMaskRenderer.cs     one R8 mask texture (global _ChromaMaskTex) + the dark overlay quad
  RainRenderer.cs               full-screen procedural rain, one material
  FluidLayer.cs                 FluXY solver + container + 3 targets, driven from valid joints
  SimulatedPoseSource.cs        deterministic test pose in the live landmark format
  ShowcaseUI.cs / ShowcaseDebugOverlay.cs / ShowcasePerformanceProbe.cs
  ChromaticRainSettings.cs      every tunable (ScriptableObject)
Shaders/  Rain, SilhouetteMask, SilhouetteOverlay, FluidLayer
Settings/ ChromaticRainSettings.asset (edit this live), FluXY storage, post-processing profile
Editor/   ChromaticRainSceneBuilder (Showcase > Chromatic Rain > Build Showcase Scene; puts AppBootstrap in the scene)
Tests/    28 EditMode tests (Showcase > Chromatic Rain > Run Self Tests, or the Test Runner)
```

Dependencies: the project's `VirtualMirror.*` assemblies and FluXY 1.4.1 (`Assets/FluXY`, outside this package, unmodified: only
its public components are used and `Assets/FluXY/Resources/Shaders/FluidUtils.hlsl` is
`#include`d read-only). If either is missing the `ChromaticRain` assembly does not compile.

## Notes worth knowing

* **The silhouette is built from landmarks**, not segmentation. The sidecar carries no mask and
  no image coordinates; position on screen comes from the measured mid-hip that the existing stage
  applies (`TrackedStage.FollowPosition`), scaled sideways by `lateralGain`. A source without a
  measured hip (a clip with no depth) keeps the body centred horizontally.
* **FluXY rounds its resolution DOWN to a power of two**: `fluidResolution` 192 gives 128. Use 128 /
  256 / 512. `fluidSupersampling` makes the dye buffer finer than the velocity buffer.
* Targets inject only while a hand moves (`rateOverDistance`, no per-step splat) and are DISABLED
  whenever a hand is not freshly measured, so there is no stale impulse and no jump impulse.
* Rain is seamless forever: the fall phase wraps at exactly 1 and every column's fall is a whole
  number of its own periods per wrap, so there is no precision loss after hours and no visible reset.
* Re-run `Build Showcase Scene` if the hierarchy or wiring is damaged; it rewrites the scene and the
  showcase's own materials (it never changes FluXY's files or Build Settings).
