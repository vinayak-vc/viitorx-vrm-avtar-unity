# Viitorx Tracking Vision

Receives the hands (UDP 8901), face (8902) and cutout (8903) streams of the Viitorx tracking engine and gives them to Unity code on the main thread; `VisionHost` starts the
engine for the streams a scene wants, `VisionControlPanel` (F6) shows and changes the settings at run time. Also: `HandsGizmoView`, `FaceGizmoView`, `ArkitBlendshapeDriver`, `VisionDemoRig`.
The cutout's wire, receiver and textures are here (the host runs them); drawing the cutout is the `com.viitorx.avtar.tracking.cutout` package.

The long description, the code samples and the wire notes are in [`Runtime/README.md`](Runtime/README.md) (written when this folder was `Runtime/Vision` of the application, before it became this package;
paths in it that mention `python-sidecar~` and `scripts/` refer to that application). The wire itself is specified in the engine repository's `docs/WIRE.md`.

## Getting started in a project of your own

1. Install the packages (Package Manager, *Add package from git URL*, `...?path=/Packages/com.viitorx.avtar.tracking.vision#<tag>`; the dependencies core and sidecar come with it) and the tracking engine (`viitorx-tracking-setup-<version>.exe`).
2. Put a **Vision Bootstrap** (*Add Component > Vision > Vision Bootstrap*) on a GameObject. It owns every setting (engine path, camera source, each stream), starts the engine for the streams a scene asks for and adds the F6 panel. Nothing else is required: no AppBootstrap, no avatar, no log service.
3. Import the **Hands and Face** sample (Package Manager > this package > Samples) and open its scene, or write your own component: derive from `VisionHostClient`, return the streams you need from `Streams`, and subscribe to `host.HandsUpdated` / `FaceUpdated` / `CutoutUpdated` in `OnBound`.

Windows only. The F6 key and the sample's number keys use the legacy Input Manager (*Project Settings > Player > Active Input Handling*: Input Manager or Both); with the Input System package alone the panel opens with *Show Panel On Start* instead.

Assembly: `VirtualMirror.Vision`. Depends on `com.viitorx.avtar.tracking.core` and `.sidecar`. Windows only (the host starts the engine as a process).

**The engine:** `VisionHost` starts the compiled `viitorx-tracking.exe` (found as described in the sidecar package; the Engine section of `VisionSettings` and the F6 panel show which one). When two or more streams are wanted it starts ONE `viitorx-tracking run` process that reads the camera once and runs them all (a webcam cannot be opened twice); one stream alone gets its own process; a Python project is never given the combined command.

Status: extracted from the Virtual Mirror application. The golden datagrams (`Tests/Editor/VisionFixtures`) and the tests of the receivers, the host, the settings and the command lines are in this package (`Tests/Editor`, `Tests/Runtime`).
