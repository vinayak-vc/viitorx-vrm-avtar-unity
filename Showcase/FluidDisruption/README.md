# Fluid Disruption showcase

A dark, minimal interactive installation. A luminous, multicoloured liquid surface (a real FluXY
fluid simulation) flows calmly on its own. When a person walks into the camera's tracking area, their
movement disturbs it: the hands leave wakes and swirls, the torso pushes broad ripples, fast gestures make
turbulence, and when they stand still or leave the surface settles back to the same calm baseline.

Scene: `Scenes/FluidDisruption_Showcase.unity` - open it (`Showcase > Fluid Disruption > Open Showcase Scene`) and press
Play. It is independent of the production scenes and does not touch the app's startup behaviour.

**Not in the Launcher yet, on purpose.** The one-person column already holds the 10 rows that
`SceneLauncherF35Tests.EveryColumnFitsTheNumberKeyShortcut` allows (1-9 and 0), and an unlisted scene gets
`TrackingNeed.Unspecified`, so the Launcher would not choose a sender for it. Adding a row needs a decision about that
cap, so it was left alone. The scene runs on its own: its `AppBootstrap` starts the sidecar exactly as `Bootstrap.unity`
does. To put it in the menu: add a `new Entry("FluidDisruption_Showcase", ...)` to `SceneLauncher.SinglePerson` and make room
(or raise `MaxShortcutRows` and update that test), then run `Virtual Mirror > Register All Scenes in Build Settings`.

## Run it

**Tracking is handled by the `AppBootstrap` in the scene** (`Tracking (AppBootstrap)`), the same component and
Inspector sections as `Bootstrap.unity`. It starts and supervises the Python sidecar and chooses the source
(OAK-D, video file, webcam, phone, LAN camera); the showcase only listens, through the existing `TrackedStage`,
like every experience scene. This package has no UDP listener, no process code and no source selector of its own.

| Goal | How |
|---|---|
| OAK-D camera | Open the scene, press Play. |
| **Video, webcam, phone, LAN camera** | Select `Tracking (AppBootstrap)` > **Video Test Source (F-49)**: tick `Track From Video`, set `Video Source`, press Play. |
| Through the app | Once it has a Launcher row (see above): play `Bootstrap.unity`, open **FLUID DISRUPTION**; the scene's own bootstrap destroys itself and the real one keeps tracking; **ESC** returns to the menu (already wired). |
| No camera at all | Select `Fluid Disruption Showcase`, set **Source = Simulated** (the demo programme plays). **Auto**, the default, switches to the simulation after `autoFallbackSeconds` with no live data and says so. |

Keys (Game view focused): `S` source (Live / Simulated / Auto) · `M` mirror · `Q` quality (Performance / Quality) ·
`E` shader enhancements · `F` FluXY simulation on/off (diagnosis) · `C` reset the fluid to the baseline ·
`R` recentre / restart the demo · `D` debug view · `H` hide the overlay text.
With the debug view up: `Space` pause the demo, `,` `.` previous / next demo segment (held), `/` run the programme again,
`P` cut the simulated stream (stale-data test), `L` / `K` lose the left / right hand landmarks.

On screen: the title, one input line (`LIVE`, `SIMULATED`, `SIMULATED (no live signal)`, `NO SIGNAL`, `TRACKING PORT IN USE`),
one person line (`PERSON DETECTED` only while a confirmed person has a torso measured this frame, otherwise
`WAITING FOR PERSON`), the instruction, and two tiny controls (`reset`, `debug`). `LIVE` appears only while fresh,
accepted tracking data arrives; the simulation is never labelled live.

The sidecar sends NO pose when nobody is in frame, so with Source = Live an empty room reads `NO SIGNAL`; in Auto the
simulation takes over after the timeout (visibly). Use **Live** for an unattended installation.

## How it works

```
AppBootstrap (sidecar + source)  ->  existing TrackedStage  ->  ShowcaseTrackingSource (shared, ChromaticRain assembly)
        or the demo programme -> SimulatedPoseSource (same landmark format, same SkeletonPose / HandPose code)
  -> ShowcasePoseAdapter (view-space body, confidence/trust, joint grace)         [shared]
  -> FluidDisruptionStateController (A-F, injection gate, detection pulse)
  -> InteractionPointTracker -> MovementImpulseEstimator x3 (left hand, right hand, torso)
  -> FluidDisruptionTargetDriver (3 pooled FluxyTargets)   +   FluidBaselineSources (1 field + N flow targets)
  -> FluXY solver (its own LateUpdate)  ->  FluidDisruption/Fluid shader
```

**States.** A Calm (nobody) - B Detected (one gentle swirl at the body centre) - C Active (a hand or the torso moves) -
D Interacting (both hands move) - E Settling (standing still: no new impulses) - F Lost (nothing injects, the fluid
dissipates). Entering or leaving only opens or closes an eased injection gate; the solver is never reset.

**Baseline (calm).** A texture-seeded field means the very first frame is already a populated liquid. Then the field
target (`FluidBaselineSources`) splats that same pattern over the whole area ten times a second with a small weight:
every pass pulls the dye a little toward it, so the baseline is always recognisable and the fluid settles back to it
after a disturbance. Five flow targets (wide bands, alternating directions, slow orbits) bend the bands into waves. None of
it reads the tracking state. Dye dissipation is low (0.02/s), viscosity 0.15.

**Interaction.** Each interaction point has a `MovementImpulseEstimator`: velocity from consecutive valid positions and
the real elapsed time, speed clamped, glitch jumps ignored, a dead zone and a smooth ramp, and a short start ramp when a point
is (re)acquired. The strength multiplies the target's dye weight, velocity weight, velocity scale and curl turbulence, and
targets only splat by distance travelled, so a still hand injects nothing and cannot act as an eraser. A point with no valid
measurement has its target disabled where it is (never sent to the origin); re-enabling re-seeds FluXY's old position, so
there is no jump. The torso is broad and low-density, the hands are small, bright and fast.

**Rendering.** `Shaders/FluidDisruption_Fluid.shader` draws the whole screen from FluXY's own dye and velocity textures
(deep midnight background; the dye as a liquid sheet): layered translucent contours through a gradient ramp, flow-advected
surface detail (FluXY's `tex2D_flowmap`), relief and a thin specular crest from the dye-thickness gradient, streaks and a
chromatic split along fast flow, and motion glow. `E` (or the Inspector switch) turns every extra off, leaving the plain dye.

## Files

```
Scripts/  FluidDisruptionController      lifecycle, input choice, the order of a frame
          FluidDisruptionSettings        every tunable (ScriptableObject) - edit it live
          FluidDisruptionFluid           FluXY solver/container/material, quality, baseline pattern
          FluidBaselineSources           the calm baseline (field + flow targets)
          FluidDisruptionTargetDriver    hands + torso targets (+ detection swirl)
          FluidTargets                   neutral FluxyTarget configuration, soft splat shape
          MovementImpulseEstimator       speed -> 0..1 disturbance strength
          InteractionPointTracker        body -> left hand / right hand / torso tracks
          FluidDisruptionStateController states A-F, gate, pulse
          FluidDisruptionDemoPoseSource  the deterministic demo programme
          FluidDisruptionUI / FluidDisruptionDebugView / FluidDisruptionTypes
Shaders/  FluidDisruption_Fluid
Settings/ FluidDisruptionSettings.asset, FluidDisruptionFluxyStorage.asset, FluidDisruptionPostProcess.asset
Editor/   FluidDisruptionSceneBuilder    Showcase > Fluid Disruption > Build Showcase Scene / Reset Settings To Defaults
Tests/    FluidDisruptionTests           Showcase > Fluid Disruption > Run Self Tests (report: Temp/FluidDisruptionTestResults.txt)
```

Shared with the Chromatic Rain showcase (assembly `ChromaticRain`, not duplicated): `ShowcaseTrackingSource` (live /
simulated / auto selection and the truthful status), `ShowcasePoseAdapter`, `ScreenSpaceMapper`, `ShowcaseBody`,
`SimulatedPoseSource`, `ShowcasePerformanceProbe`, `ShowcaseBuilderUtil`, and the `ITrackingTuning` interface each
showcase's settings implement. This package therefore depends on `Showcase/ChromaticRain` and on FluXY 1.4.1
(`Assets/FluXY`, outside this repo, unmodified).

## Tuning

Everything is in `Settings/FluidDisruptionSettings.asset`, re-read every frame, so edit it while playing: palette, the
baseline (`fieldWeight`, `fieldRate` = how quickly it is restored; the flow sources), viscosity / turbulence / dissipation,
the dead zone and full-speed of the hand and torso movement, the densities and stir strengths, the A-F timings and the
rendering extras. Quality levels (`performanceProfile` 256 x1, `qualityProfile` 512 x2) are on the asset; the active one is
the controller's `Quality`. FluXY rounds its resolution DOWN to a power of two. Resolution changes re-seed the baseline.

## Limits

- Never run on a physical OAK-D / camera. The live path was exercised with the real sidecar on a recorded video (see
  `docs/ai_handoff.md`).
- One person (the existing single-person tracking path); the hands and torso of that person drive the fluid.
- The detection swirl, hand wakes and torso push are tuned on the demo programme and one clip; adjust on the venue's screen.
- Measured numbers are Editor numbers, not a player build.
