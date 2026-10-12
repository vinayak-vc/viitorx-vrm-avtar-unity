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

**Several people.** Set `Tracking (AppBootstrap)` > **Video Test Source** > `Video Max People` to 2 or more (or use the OAK-D
multi-person sender) and the scene draws **one silhouette and one set of fluid targets per tracked person**, up to
`ChromaticRainSettings.maxPeople` (default 4, read when the scene starts). Each person is keyed on the sidecar's track id, so
they keep their own slot, fade in and out on their own and nobody swaps. Each stands where the camera sees them (sideways
position from the sidecar's per-person root), so nobody jumps when the first person leaves. Hands exist for the first person only
(the sidecar sends hands at the root), so the others are drawn and stir the fluid from their body. With one person, or a
single-person sender, nothing changes. The status chip then reads `N people tracked`.

## The silhouette

The tracked person is a filled, rain-free human shape built from the landmarks: broad shoulders sloping into the neck, a chest that
narrows to the waist and widens to the pelvis, arms and legs that taper (a calf bulge, round elbows and knees), hands and feet. The
outline is a separate, optional thin edge along the outside of the filled mask. Every proportion follows the person's own measured
shoulder width.

**Skins.** `Settings/ChromaticRainSkins.asset` (a `SilhouetteSkinSet`, assigned to `Chromatic Rain Showcase > Skins`) is a list of
skins; each skin is the look (fill colour, edge softness, shape blend, outline on/off, colour, intensity, width) plus every body
proportion in metres. Six ship: **Anatomical**, **Slim**, **Athletic**, **Neon**, **Soft glow**, **Minimal** (no outline). Select the
asset and edit a skin (it applies live while the scene runs), add one with the `+` of the list, or right-click the asset header >
"Add a copy of the first skin" / "Add the built-in skins that are missing". `Start Skin` is the one shown first; **B** cycles the list
while the scene runs without changing the asset.

**Debug views (V).** Normal, then the filled mask as white on black, then the mask with the landmarks and bones over it, then the
contour only: use them to judge the shape before touching the rain.

## A person at a laptop (the camera cuts the body off)

A laptop webcam shows the head and shoulders, not the hips. With **Video Partial Body** on (the default, on the scene's AppBootstrap
under Video Test Source) the sidecar decides every frame what anchors the skeleton - the hips while they are in the picture, then the
shoulders, then the nose - so the showcase keeps working when the hips are not in the picture, and carries on if they are lost in the
middle of a session. What you see: the silhouette has the head, neck, shoulders, chest and arms as measured, and the torso continues
a little below the pelvis (about an upper thigh, narrowing toward its end) so the person reads as sitting out of shot. No leg is
drawn: nothing measured is invented. In every skin of `ChromaticRainSkins.asset`: **Extend Cut Off Body** turns the continuation off
(the torso then ends at the pelvis), **Cut Off Extension Metres** (0.45) sets its length and **Cut Off Taper** (0.6) how much it
narrows. It used to be a 3 m column and read as "a long waist" (ADR-097). The debug view (key `D`) says "hips not in the picture:
root estimated".

A seated person with a desk in the picture used to be carried up and down the screen: the model answers for feet that are not there,
and the floor estimate followed them. A foot now counts as the floor only with a measured root, its ankle, 0.3 m below the root,
for 10 frames in a row, and the figure holds still when no foot is being measured (ADR-097).

The hip is then the sender's ESTIMATE (6-10 cm off in the measurements), so the operating volume judges it against **Volume Partial Near
Metres** (0.35 m) instead of the stereo camera's 1.35 m; a measured hip is still held to 1.35 m. Set **Video Horizontal Fov Degrees** to the
laptop camera's field of view: the default 60 is a guess and scales every distance. **Video Partial Body** off restores the old rule (no
hips, no frame). Measured on recordings only, not on a physical webcam: see ADR-096.

Keys (Game view focused): `S` source · `N` Dark Silhouette / Negative Space · `B` silhouette skin · `V` silhouette debug view · `F` fluid layer · `M`
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
  SilhouetteBuilder.cs          landmarks -> trapezoids, tapered strokes + head ellipse for the mask shader (shapes from a skin)
  SilhouetteSkin.cs             a skin (look + proportions) and the SilhouetteSkinSet asset
  SilhouetteMaskRenderer.cs     one R8 mask texture (global _ChromaMaskTex) + the dark overlay quad
  RainRenderer.cs               full-screen procedural rain, one material
  FluidLayer.cs                 FluXY solver + container + 3 targets, driven from valid joints
  SimulatedPoseSource.cs        deterministic test pose in the live landmark format
  ShowcaseUI.cs / ShowcaseDebugOverlay.cs / ShowcasePerformanceProbe.cs
  ChromaticRainSettings.cs      every tunable (ScriptableObject)
Shaders/  Rain, SilhouetteMask, SilhouetteOverlay, FluidLayer
Settings/ ChromaticRainSettings.asset (edit this live), FluXY storage, post-processing profile
Editor/   ChromaticRainSceneBuilder (Showcase > Chromatic Rain > Build Showcase Scene; puts AppBootstrap in the scene)
Tests/    60 EditMode tests (Showcase > Chromatic Rain > Run Self Tests, or the Test Runner)
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
