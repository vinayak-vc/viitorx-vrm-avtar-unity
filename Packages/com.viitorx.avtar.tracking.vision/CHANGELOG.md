# Changelog

## 0.1.3 (2026-10-17)
- Bump package version to 0.1.3. No code changes.

## 0.1.2 (2026-10-17)
- Bump package version to 0.1.2.

## 0.1.1 (2026-10-17)
- Bump package version to 0.1.1.

## 0.1.0 (2026-10-17)
- `UnityDebugLogService` moved to the core package (namespace `VirtualMirror.Core`).
- `VisionBootstrap` (settings, host, tick, F6 panel in one component), `VisionHostRegistry` (AppBootstrap registers its host too), `VisionHostClient` (a component that asks the host for streams and lets go of them), `UnityDebugLogService`. Sample: Hands and Face. The F6 key is compiled only with the legacy Input Manager.
- `VisionHost` starts the compiled engine (`EnginePath`, `UsePythonProject`, `PythonProjectPath` in `VisionSettings`), ONE `run` process for several streams (`VisionSenderArguments.Run`), `EngineDescription` in the F6 panel. The single-stream command lines are byte-identical to before.
- Extracted from the application's `Runtime/Vision`, plus `VisionHost` and `VisionControlPanel` (they were in the AppBootstrap assembly). The assembly name `VirtualMirror.Vision` and every GUID are unchanged.
- The cutout's rendering (`CutoutView`, `CutoutLook`, the two shaders) moved to `com.viitorx.avtar.tracking.cutout`; the cutout wire, assembler, receiver and textures stay here.
- The assembly now references `VirtualMirror.Sidecar` (the host starts the engine through the sidecar launcher).
