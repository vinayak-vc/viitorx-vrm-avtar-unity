# Installing the Viitorx tracking packages in a Unity project

Windows, Unity 6000.3. Three things are installed, in this order: the **packages** (C#), **UniVRM** (only if you want the body to drive a VRM avatar), and the
**engine** (the program that reads the camera). The packages do nothing until the engine is there.

> **Status of this guide.** Installing the packages from their git URLs (branch `package-v1` of this repository, 2026-10-17) was run in a fresh Unity 6000.3 project: all five packages resolved
> from GitHub, and the edit-mode suites (565 tests), the play-mode suites (47, with the real engine) and the samples (5) passed from those git-installed packages. The tag `v0.1.0` was installed the same way (2026-10-17: commit `aa9084d`, same results). Installing from a registry has not been run (none is published). The engine's `.exe` installer was built, never run.

## 1. The packages

Which ones do you need?

| You want | Install |
|---|---|
| hands, face, or the person cut out of the picture | `...vision` (hands, face); add `...cutout` for the cut-out person |
| a VRM avatar that copies a person | `...body` (needs UniVRM, step 2) |
| all of it, one name | `com.viitorx.avtar.tracking` (the umbrella) |

`...core` and `...sidecar` come with any of them. Package names are `com.viitorx.avtar.tracking.<core|sidecar|vision|cutout|body>`; they all share ONE version.

### From git (works without any registry)

Open `Packages/manifest.json` of your project and add every package you need **by its own URL**, with the tag of the release (until a tag exists, the branch name works the same way: `#package-v1`):

```json
"dependencies": {
  "com.viitorx.avtar.tracking.core":    "https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git?path=/Packages/com.viitorx.avtar.tracking.core#v0.1.0",
  "com.viitorx.avtar.tracking.sidecar": "https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git?path=/Packages/com.viitorx.avtar.tracking.sidecar#v0.1.0",
  "com.viitorx.avtar.tracking.vision":  "https://github.com/vinayak-vc/viitorx-vrm-avtar-unity.git?path=/Packages/com.viitorx.avtar.tracking.vision#v0.1.0"
}
```

Add `cutout` and `body` the same way when you want them. `python tools/release/git_install_block.py v0.1.0 vision cutout` prints these lines for the packages you name, with the packages they need added (or `--apply <manifest.json>` merges them into your manifest); it never includes the umbrella. List the dependencies yourself: Unity does not follow a git URL written inside a package's own `dependencies`, so installing `...vision` alone does not install `...core` and `...sidecar` for you.
**Measured:** a manifest with only the umbrella (`com.viitorx.avtar.tracking`) by git URL fails to resolve: *Package [com.viitorx.avtar.tracking.core@0.1.0] cannot be found*, and the same for each sibling. From git, do not install the umbrella; name the packages.

### From a registry

If the family is hosted on a Unity package registry (a scoped registry such as OpenUPM, or your own), add that registry to `scopedRegistries` with the scope
`com.viitorx`, then add `com.viitorx.avtar.tracking` (or the single packages); the others follow. No registry hosts these packages yet: see `docs/RELEASING.md`.

### Do not install the application's root package

The repository's root `package.json` (`cloud.viitor.virtual-mirror`) is the whole Virtual Mirror application and holds the same code as assets. Installing it beside these
packages defines every assembly twice.

## 2. UniVRM (only for the body package)

`com.vrmc.vrm` is not on Unity's registry, so a package cannot install it. Add the OpenUPM scoped registry and the package to `Packages/manifest.json`:

```json
"scopedRegistries": [ { "name": "package.openupm.com", "url": "https://package.openupm.com", "scopes": [ "com.vrmc" ] } ],
"dependencies": { "com.vrmc.vrm": "0.126.0" }
```

0.126.0 is the version the body package was built and tested with. Without UniVRM the body package installs and its avatar loader, retargeting and `BodyBootstrap` are not
compiled (the Editor says so once). The scoped-registry text above was not run here (UniVRM was a local copy in the tests, to avoid a download).

## 3. The engine

The engine is one folder, `viitorx-tracking\`, with `viitorx-tracking.exe`. The packages look for it in this order: the path in the bootstrap's settings, the environment variable
`VIITORX_TRACKING_ENGINE`, the machine-wide install (registry), `%LOCALAPPDATA%\Viitorx\TrackingEngine\<version>`, `StreamingAssets/Viitorx/TrackingEngine`.
Pick **one** of these ways to put it there:

**a) The installer** (`viitorx-tracking-setup-<version>.exe`, needs administrator rights once): installs under Program Files, records itself in the registry and the machine environment,
and can fetch the small models at the end. *Built, never run.*

**b) From Unity, for the current user, no administrator rights** (the engine's release file is next to its zip on the release page):
*Tools > Viitorx > Tracking Engine > Install From Release File...* and choose `viitorx-tracking-<version>-win-x64.release.json`. Unity downloads the zip named in it, checks its size
and SHA-256, unpacks it into `%LOCALAPPDATA%\Viitorx\TrackingEngine\<version>` (every entry checked, nothing written outside), starts the engine once with `version --json` to see that it
runs and is the version it says, and only then keeps it. A wrong or truncated download installs nothing. *Tested against a local server with the real zip; never against a real release,
because none exists yet.* Keep the `.release.json` in your project (or get it from somewhere you trust): the SHA-256 protects against a corrupt or swapped zip, not against a
release host whose zip and release file are both replaced. For a machine without network, save the zip beside the release file (same folder, the name the release file gives) and use the same menu item: it
uses that zip instead of downloading.

**c) By hand:** unzip the engine zip anywhere and put the folder's path in *Engine Path* of the bootstrap component.

### The models

The engine does not ship models. In the engine folder run `viitorx-tracking fetch-models` for the hands, face and cutout models (size and SHA-256 are checked). The **body model**
(`rtmw3d-x-fp16.onnx`, 185 MB) has no public URL: copy it into the engine's `models` folder. Check the licence of the weights you obtain.

## 4. Try it

Import a sample (*Package Manager > the package > Samples*): **Hands and Face** or **Cutout** (vision, cutout) or **Body Avatar** (body), open its scene and press Play. The engine opens
`camera:0`, the first webcam. A webcam can be opened by one program: the body engine and the vision senders cannot share it (use a video file, a LAN camera or a stream for both).

If something is missing, the status box in the sample says what (engine not found and where it looked, model missing, source error, or an engine that speaks another wire generation than these packages read: install one that matches).
