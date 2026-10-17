# Changelog

## 0.1.3 (2026-10-17)
- Bump package version to 0.1.3; compatible with Viitorx Tracking Engine 0.1.3. No code changes.

## 0.1.2 (2026-10-17)
- Bump package version to 0.1.2; compatible with Viitorx Tracking Engine 0.1.2.

## 0.1.1 (2026-10-17)
- Bump package version to 0.1.1; compatible with Viitorx Tracking Engine 0.1.1.

## 0.1.0 (2026-10-17)
- `EngineRelease` (the engine's `.release.json`, parsed as untrusted input), `EngineInstaller` (download or take a zip, check size and SHA-256, unpack safely, run `version --json`, move into `%LOCALAPPDATA%\Viitorx\TrackingEngine\<version>`), and the Editor menu *Tools > Viitorx > Tracking Engine > Install From Release File...* (assembly `VirtualMirror.Sidecar.Editor`).
- `EngineLocator`, `EngineLocation`, `EngineRegistry` (raw advapi32: `Microsoft.Win32.Registry` is not in the default .NET Standard 2.1 profile) and `SidecarLocator.ResolveEngine`; `SidecarLaunchOptions.EngineExecutable` / `EngineCommand`; the launcher starts the compiled engine with `--parent-pid`. The Python paths are unchanged.
- Extracted from `VirtualMirror.Tracking` (`Runtime/Tracking/OakD`): `Sidecar*` and `VideoSourceSpec`. Code unchanged; the namespace `VirtualMirror.Tracking.OakD` is kept.
- Vendored `NativeProcess` (from `Assets/Modules/Utility`) under `VirtualMirror.Sidecar.Native`; the launcher uses it. The package no longer references `Utility`.
