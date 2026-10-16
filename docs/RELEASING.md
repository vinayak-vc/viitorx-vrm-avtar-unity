# Releasing the Viitorx tracking packages and engine

Two things are released, from two repositories, with two version numbers:

| What | Repository | Version | Tag | Artifacts |
|---|---|---|---|---|
| the six Unity packages (`com.viitorx.avtar.tracking`, `.core`, `.sidecar`, `.vision`, `.cutout`, `.body`) | this one (`Packages/`) | ONE number for all six (`package.json`), e.g. `0.1.0` | `v0.1.0` | the repository at the tag (Unity installs from a git URL); optionally tarballs for a registry |
| the compiled engine | `python-sidecar~` (its own repository) | `viitorx_tracking.__version__`, e.g. `0.1.0` | `engine-v0.1.0` | zip, `.sha256`, `.release.json`, installer |

The two numbers are independent. What ties them together is the **wire specification** (`docs/WIRE.md`, version in `viitorx_tracking.WIRE_SPEC_VERSION`, now 1.0): a package family that reads
wire spec 1.x works with any engine that sends 1.x. The sidecar package checks it before it starts an engine (`EngineCompatibility`: it asks the exe once, `version --json`, and refuses an engine whose wire spec has another major number, with a message; an exe that cannot answer is not blocked, starting it says what is wrong). Raise `WIRE_SPEC_VERSION`'s major number only for an incompatible wire change, and `EngineCompatibility.SupportedWireSpecMajor` with it.

## The gate, before anything is tagged

```text
python tools/release/test_release_check.py          # the gate's own tests (38)
python tools/release/test_bump_version.py           # the version tool's tests (18)
python tools/release/release_check.py --release     # the packages; with --release a changelog that still says "(unreleased)" fails
```

`release_check.py` reads the six packages the way Unity and a reviewer would: one version everywhere, dependencies exactly that version, the umbrella names exactly the other five, README /
CHANGELOG (with the version's heading) / LICENSE (equal to the repository's) / Third Party Notices in every package, a `.meta` for everything Unity imports and none without a file, no GUID twice, every
assembly reference resolves to the package itself or a package it declares, UniVRM only under a define constraint, no reference to the application, no leftover `.orig`, no path of a developer's machine.
It changes nothing. The tests that decide whether the *code* is releasable are the Unity suites (see `docs/ai_handoff.md` for the commands used so far): edit mode, play mode with the engine, the samples.

## Cutting the packages' release

1. `python tools/release/bump_version.py bump 0.2.0` (only when the version changes: moves every `package.json` and every dependency between the six, and opens a `## 0.2.0 (unreleased)` section in every changelog). Fill the changelogs in.
2. Run the Unity suites and the gate.
3. `python tools/release/bump_version.py finalize 2026-10-17` replaces `(unreleased)` by the date in the current version's headings. Run `release_check.py --release`: it must be silent.
4. Commit; tag `v0.2.0`; push (the person who owns the repository does this: nothing here pushes).
5. In a **fresh** Unity project, install from the tag (see `docs/INSTALL.md`), import a sample, press Play. This step has never been run: it is the first thing to do after the first push.

## Cutting the engine's release

1. Set `viitorx_tracking.__version__`. Tests: `tests/test_build.py`, `tests/test_engine.py`, the contract and wire tests.
2. Build: `build/build_engine.py all --out <dir> --jobs 6 --base-url https://github.com/<owner>/<engine-repo>/releases/download/engine-v0.2.0` (the base URL is where the zip WILL be; it is written into the `.release.json`).
   Verify the real folder: `tests/verify_build.py <dir>/viitorx-tracking --clip <video>` (needs a GPU and the models).
3. Tag `engine-v0.2.0` and push: `.github/workflows/engine-release.yml` builds on a clean runner, checks that the tag names the version, and attaches the zip, its `.sha256`, the `.release.json` and the installer to the release.
   **That workflow has never run on GitHub.** The first run is expected to need adjustments. Until it works, attach the four files from a local build by hand.
4. The `.release.json` is what the Unity side trusts: publish it where people get it from, and tell them to keep a copy in their project (`docs/INSTALL.md`).
5. The installer and the exe are **not code-signed**; SmartScreen shows "unknown publisher". Signing needs a certificate: a release decision.

## Where the packages live: a decision

| Option | What it takes | What the user types | Honest status |
|---|---|---|---|
| **A. git URLs** | push + tag; nothing else | one `Packages/manifest.json` line **per package**, with `?path=/Packages/<name>#v0.2.0` | works with the layout as it is; not tested (nothing is pushed). The umbrella then only pins versions |
| **B. OpenUPM** | a public repository, a pull request to openupm.com with the package names | add the OpenUPM scoped registry once, then one name | needs the repository to be public; the packages' layout (one repo, `Packages/<name>`) is the kind OpenUPM builds from |
| **C. your own registry** (Verdaccio, a Unity-compatible registry) | host it, publish six tarballs (`npm pack` of each package folder) | add the registry once, then one name (`com.viitorx.avtar.tracking`) | what makes the umbrella do its job; the hosting and its access are yours to run |

None is chosen. A is enough to start. UniVRM must be installed from OpenUPM by the user in every case (it is not on Unity's registry).

## Not done

- **A release has never been made.** No tag, no GitHub release, no registry entry exists; no install from git or a registry has been run.
- **Engine and packages are matched by wire spec major only.** A change inside one major (a new optional field) is not checked; the engine's own version is shown in the status, not compared.
- **The CI workflow for the packages**: none. The Unity suites need a licensed Unity and a GPU for some tests; there is no runner for them. The gate (`release_check.py`) needs only Python and could run on any runner.
- **Code signing** of the exe and the installer.
- **The installer has never been run** (it needs administrator rights), and the engine's `fetch-models` was never run in the frozen exe over the network.
- **Unreal**: no plugin. The wire is specified (`docs/WIRE.md`) and the engine is a separate program, so nothing in the engine blocks it.
