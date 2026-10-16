# Viitorx Tracking Sidecar

Runs the tracking engine for a Unity host: `SidecarProcessLauncher` (start, watch, stop; a Windows job object ends the engine with Unity; a heartbeat file and the
status model tell a stalled engine from a dead one), `SidecarLocator` and `SidecarPaths` (where the engine is), `SidecarSingleInstanceLock`, `SidecarGuards`,
`SidecarStatusModel` and `VideoSourceSpec` (a webcam, a LAN camera, a URL or a file as the source).

Assembly: `VirtualMirror.Sidecar` (namespace `VirtualMirror.Tracking.OakD`, kept from where the code came from). It carries its own copy of `NativeProcess`
(`VirtualMirror.Sidecar.Native`), so it needs nothing from the project it is installed into.

Windows only.

**Finding the engine** (`EngineLocator`, `SidecarLocator.ResolveEngine`), in this order: the path in the settings (the exe or its folder; a path that does not hold it is an error,
not a reason to look further), the environment variable `VIITORX_TRACKING_ENGINE`, the machine-wide install (registry `HKLM\Software\Viitorx\TrackingEngine\InstallDir`, written by the
installer), `%LOCALAPPDATA%\Viitorx\TrackingEngine\<version>` (the highest version, or the pinned one exactly), `StreamingAssets/Viitorx/TrackingEngine`, and last the Python source tree
(`python-sidecar~`, what the application always used). **Starting it:** `SidecarLaunchOptions.EngineExecutable` + `EngineCommand` (`hands`, `face`, `cutout`, `body`, `run`) make the launcher
start `viitorx-tracking.exe <command> <options> --parent-pid <Unity's pid>`; the engine finds its models beside itself.

**Installing the engine** (`EngineInstaller`, `EngineRelease`, Editor menu *Tools > Viitorx > Tracking Engine > Install From Release File...*): reads the `viitorx-tracking-<version>-win-x64.release.json` that the engine's
build writes beside its zip, downloads the zip (https only; plain http only to the loopback host), and installs it for the current user into `%LOCALAPPDATA%\Viitorx\TrackingEngine\<version>`, where the locator above finds it, with no administrator
rights. Order: size and SHA-256 are checked on the downloaded file BEFORE anything is unpacked; the unpack goes to a temporary folder (every entry must sit under the release's top folder and inside it, within the release's size
and a path-length limit); the engine is started once with `version --json` and must be the version the release file names; only then does the folder get its final name. Failure or cancel leaves nothing behind. A zip saved beside
the release file is used instead of downloading. What the SHA-256 protects against is a corrupt, truncated or swapped zip; it does not protect against a publisher whose release file AND zip are both replaced: keep the release file
in your project or get it from a source you trust. Not done: signature checking (the engine is not code-signed), resuming a download.

Status: extracted from the Virtual Mirror application. Not done: a release to download (none is published yet).
