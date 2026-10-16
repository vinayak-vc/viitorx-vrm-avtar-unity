# Viitorx Tracking

One compiled engine reads a camera (a webcam, a LAN camera, a stream or a clip) and sends what it finds to Unity over UDP; these packages receive it. **Hands** (21 points each), **face** (landmarks, head pose, 52 blendshapes),
a **cut-out person** (picture plus mask) and a **whole body** that drives a VRM avatar. Nothing is inferred inside Unity: no model, no camera and no native plug-in runs in the player, so an inference crash cannot take the player down.

This package has **no code**. It names the five packages of the family at one version:

| Package | What | Needs besides the family |
|---|---|---|
| `com.viitorx.avtar.tracking.core` | pose, hand, face and crowd frames, the provider interfaces, the humanized skeleton, the Unity log service | nothing |
| `com.viitorx.avtar.tracking.sidecar` | finds, starts, watches and stops the engine | Newtonsoft Json |
| `com.viitorx.avtar.tracking.vision` | hands, face and cutout streams: parsers, receivers, `VisionBootstrap`, hand and face views, sample *Hands and Face* | |
| `com.viitorx.avtar.tracking.cutout` | the person cut out of the picture: `CutoutView`, its two shaders, sample *Cutout* | Universal Render Pipeline |
| `com.viitorx.avtar.tracking.body` | the body stream and the Kalidokit retargeting onto a VRM avatar: `BodyBootstrap`, sample *Body Avatar* | **UniVRM**, Animation Rigging |

Windows only. Unity 6000.3.

## Installing

1. **The packages.** From a registry that hosts the family, add `com.viitorx.avtar.tracking` and the rest follows. **From git do not add this umbrella: Unity then stops with "Package [com.viitorx.avtar.tracking.core@0.1.0] cannot be found" (measured).** From git, add each package to `Packages/manifest.json` by its own URL
   (`https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git?path=/Packages/com.viitorx.avtar.tracking.core#<tag>`, and the same for sidecar, vision, cutout and body). Unity does not follow a git URL written inside a package's own dependencies, so the umbrella cannot install the others from git. A `file:` reference to the folders works the same way.
2. **UniVRM** (only for the body package; the others do not need it). It is not on Unity's registry, so add the OpenUPM scoped registry and the package to the manifest:

   ```json
   "scopedRegistries": [ { "name": "package.openupm.com", "url": "https://package.openupm.com", "scopes": [ "com.vrmc" ] } ],
   "dependencies": { "com.vrmc.vrm": "0.126.0" }
   ```

   Without it the body package installs but its avatar loader, retargeting and `BodyBootstrap` are not compiled; the Editor says so once.
3. **The engine.** Run `viitorx-tracking-setup-<version>.exe` (it installs `viitorx-tracking.exe` and its models folder, and offers to fetch the small models), or unzip the engine folder anywhere and put its path in the bootstrap's *Engine Path*.
   The body model (`rtmw3d-x-fp16.onnx`, 185 MB) has no public URL: copy it into the engine's `models` folder. The sidecar package describes the order in which Unity looks for the engine.

## Using it

Each stream has its own bootstrap component, because each has its own camera reader and UDP port:

- **Vision Bootstrap** (hands, face, cutout; one engine process serves them all from one camera). Import the samples from the Package Manager.
- **Body Bootstrap** (the body and an avatar). One webcam can be opened by one program, so the body engine and the Vision Bootstrap cannot both use `camera:0`; a clip, a LAN camera or a stream can feed both.

Do not install the repository's root package `cloud.viitor.virtual-mirror` (the whole Virtual Mirror application) beside these: it holds the same code as assets and the assemblies would be defined twice.

## Status

Version 0.1.0, unreleased. Verified on Windows in a fresh Unity 6000.3 project with the packages as `file:` references: edit-mode and play-mode tests, the samples played with the compiled engine and a clip. Not verified: a clean machine, the installer run by a person,
a real webcam through the samples, installation from a registry or from git URLs, anything but Windows.
