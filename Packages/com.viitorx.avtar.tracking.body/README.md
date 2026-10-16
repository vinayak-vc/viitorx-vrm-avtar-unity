# Viitorx Tracking Body

A person in front of a camera drives a VRM avatar. The tracking engine (`viitorx-tracking body`) reads the camera and sends the whole-body pose over UDP (port 8899); this package
receives it, keeps it honest, and retargets it onto the avatar's normalized control rig: body, legs, fingers, wrist, and the avatar's walk and jump from the hip.

Windows only. Needs the tracking engine (see the sidecar package) **and its body model**: `rtmw3d-x-fp16.onnx` (or `rtmw3d-x.onnx`) in the engine's `models` folder. The engine's
`fetch-models` does not download it (it has no public URL); copy it there yourself, or point `VIITORX_MODELS_DIR` at the folder that holds it.

## Needs UniVRM (not on Unity's registry)

`com.vrmc.vrm` (and its `com.vrmc.gltf`) cannot be declared in a package's dependencies, so install it yourself. In `Packages/manifest.json`:

```json
"scopedRegistries": [
  { "name": "package.openupm.com", "url": "https://package.openupm.com", "scopes": [ "com.vrmc" ] }
],
"dependencies": { "com.vrmc.vrm": "0.126.0" }
```

Without it the package still installs: `VirtualMirror.Tracking` (the body wire receiver) compiles, but the avatar loader, the retargeting and `BodyBootstrap` are **not compiled**, and the Editor logs one warning saying so
(`VIITORX_UNIVRM` is defined by the `com.vrmc.vrm` version define). The package version it was built and tested with is 0.126.0. Animation Rigging (`com.unity.animation.rigging`) is a normal registry dependency.

## Getting started

1. Install this package (the packages it depends on, core and sidecar, come with it from a registry; from git URLs add each one), UniVRM, and the engine with its body model.
2. Add a **Body Bootstrap** (*Add Component > Body > Body Bootstrap*) to a GameObject, once. Give it an avatar: a VRM file renamed to `.bytes` as *Avatar Asset* (works in a player build), or *Avatar File* (an absolute path, or one relative to StreamingAssets).
3. Put a camera in front of where the avatar appears and press Play. The bootstrap turns the avatar 180 degrees about the vertical axis when it loads (*Following > Avatar Yaw Degrees*, as the Virtual Mirror application does): the avatar then looks toward -Z, so the camera goes at -Z looking along +Z. Do not change that to 0 unless you know why: the retargeting works in the avatar's model frame, and at 0 the arms cross the body. *Settings > Source* is `camera:0` (the first webcam); a clip, a LAN camera link or a stream also works. The *Body Avatar* sample is this, ready to open (Package Manager > this package > Samples).

`BodyBootstrap` starts the engine, listens on the wire, loads the VRM with its control rig, and drives it every frame; `StatusText` is a few lines for a status box (engine, stream, avatar, and the operating volume's
verdict). The C key takes the neutral again (the person stands upright and still; legacy Input Manager only, or call `Recalibrate()`).

## What is in it (four assemblies and a host)

| Assembly | What | Needs |
|---|---|---|
| `VirtualMirror.Tracking` | `OakDUdpPoseProvider` (the body wire: parser, timestamped pose buffer, stream health and the stale failsafe), `TrackingStreamHealth`, `JointFilterPipeline`/`OneEuroFilter`, fake providers, `BodySettings`, `BodyEngineArguments` | core, sidecar |
| `VirtualMirror.Retargeting` | Kalidokit whole-body solver and control-rig driver, the arm aim solver, limb/trunk/torso gates, the hand and expression retargeters | UniVRM |
| `VirtualMirror.Avatar` | `UniVrmAvatarLoader` (`GenerateControlRig`), `VrmAvatarInstance` | UniVRM |
| `VirtualMirror.IK` | `AnimationRiggingIkDriver` (the older FK/IK path) | Animation Rigging |
| `VirtualMirror.Body` | `BodyBootstrap`, `BodyAvatarDriver` (one frame from provider to avatar) | all of the above |

`BodyAvatarDriver` is the Kalidokit path of the Virtual Mirror application's `AppBootstrap`, with the same stages, order and defaults (stream failsafe, operating volume, humanized skeleton, Kalidokit solve, fingers and wrist, root follow).
The application keeps its own copy of that orchestration; the two are not shared yet.

Not in the package: the in-process MediaPipe and Sentis providers (they stay in the application, `VirtualMirror.Tracking.Inference`), the legacy FK retargeter path, face tracking for the avatar (the body stream carries none), any scene or UI.

## Limits to know

- **One reader per camera.** A webcam can be opened by one program. The body engine and the vision package's hands, face and cutout senders cannot share `camera:0`; use a clip, a LAN camera or a stream when both are wanted.
- **No restart watchdog.** If the engine exits, the stream goes stale, the avatar eases to its neutral pose and `StatusText` says the engine is not running. The Virtual Mirror application restarts it through its Python supervisor; the compiled `body` command has none.
- **Estimated depth.** From a webcam or a clip the depth is estimated (an assumed field of view and subject height, see `BodySettings`), not measured as the OAK-D's is.
- **A second `BodyBootstrap` is refused** (one UDP port, one reader). It is not a crowd: one person.

## Tests

`Tests/Editor/Tracking` (the wire health, the filter, the engine command line against fixtures that the engine's own parser also reads), `Tests/Editor/Retargeting` (the solvers and gates; need UniVRM) and
`Tests/Runtime` (Play mode: a real VRM and its control rig driven by a fake provider; `BodyBootstrap`'s lifecycle; and real body-wire datagrams sent over loopback UDP for the arms, the fingers and their sides, and the stale-stream failsafe and recovery). The `Tests/Runtime` engine test runs only when `VIITORX_TEST_ENGINE`, `VIITORX_TEST_CLIP` and `VIITORX_MODELS_DIR` name the engine, a clip and the models.
