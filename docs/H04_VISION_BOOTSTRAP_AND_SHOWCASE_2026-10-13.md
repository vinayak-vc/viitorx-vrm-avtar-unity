# H-04 - Hands and Face in AppBootstrap, a control panel, and two sample scenes (2026-10-13)

Decision: **ADR-102**. Tasks: `tasks.md` H-04. Sidecar repo: `docs/tasks.md` H-04. Follows H-03 (the receivers). The user's request: "wire the all setting in AppBootstrap, full control panel from there and create dedicated 2 (face, hands) sample scene to showcase this".

## 1. What was built

| Piece | Where | What it is |
|---|---|---|
| Settings | `Runtime/Vision/Host/VisionSettings.cs` | One serializable object with 35 settings (senders, source, hands, face, receiver) and their tooltips; enums `VisionSenderMode` (Off / Auto / Always), `VisionProvider`, `HandSidesMode`, `FaceLandmarkMode`. |
| Command lines | `Runtime/Vision/Host/VisionSenderArguments.cs` | Pure mapping from a setting to a flag of `hands_udp_sender.py` / `face_udp_sender.py`, with clamping and quoting. |
| Inspector section | `Runtime/Bootstrap/AppBootstrap.Vision.cs` | "Hands and Face (H-03)" on AppBootstrap, order 25 (between Tracking Source and Video Test Source). Independent of the body sidecar: both can run together. |
| Host | `Runtime/Bootstrap/VisionHost.cs` | Owns both receivers, the Python senders, and the per-frame hand-over (`HandsUpdated`, `FaceUpdated`). A scene asks for a stream (`AddDemand`); in Auto mode that starts the sender and leaving the scene stops it. A sender somebody else runs is left alone; a failed launch is one status line, not a retry loop; a changed command line restarts the sender after a second of quiet. |
| Control panel | `Runtime/Bootstrap/VisionControlPanel.cs` | **F6**, an IMGUI window: Status (per stream: sender state, rate, counters, subjects in view, the command line each sender runs with, Restart), Source, Hands, Face, Receiver tabs, Apply and Reset. Live, not saved. |
| Launcher support | `SidecarProcessLauncher` (`DirectSenderOnly`), `SidecarPaths.ValidateSenderOnly` | Reuses the launcher's kill-on-close job object and process-tree kill for the vision senders: no supervisor, no lock port, no `--model`, and no body pose model required. Default behaviour of every existing launch is unchanged. |
| Views | `HandsGizmoView.Show/Hide/SetAnchor`, `FaceGizmoView.Show/Hide/SetAnchor` | The H-03 gizmo views can now be driven by whoever owns the data (the host) instead of only by a receiver behaviour with a socket of its own. |
| Sample scenes | `Showcase/HandsFace/` | `HandsShowcase.unity`, `FaceShowcase.unity`, assembly `HandsFaceShowcase`, README. Hands: skeletons, fingertip trails, gesture labels, pinch and curl bars, three orbs to pick up by pinching. Face: a cartoon face from primitives driven by the head pose and 52 blendshapes, strongest-blendshape bars, expression tags, the 478 landmarks. |

One webcam or LAN-camera viewer can be read by one program at a time, so each scene runs only its own sender (`Auto`), and the panel warns when both are `Always`.

## 2. Verified (headless, as in H-03)

| Check | Result |
|---|---|
| C# tests | **105 pass, 0 fail** (H-03's 86, plus `VisionSenderArgumentsTests` 8 and `VisionHostTests` 11). The host tests use real UDP sockets on loopback with sender auto-start OFF: receivers exist only for streams not Off, one datagram is one event, a changed port rebinds, a taken port is reported once, demand is counted per stream, the sender-wanted rule (truth table), the message rate, Dispose frees both ports. |
| Mutation check | **24 of 24 injected bugs caught** in `VisionHost` and `VisionSenderArguments` (22 in the first round; the two survivors, an Off stream still getting a receiver and the rate never measured, got tests that kill them). |
| Python/C# command-line contract | `tests/test_vision_cli_contract.py`: **20/20**. The four expected command lines (`Tests/EditMode/VisionFixtures/args_*.txt`) are asserted byte-for-byte by the C# tests and parsed by the senders' own argparse parsers, so a flag the host emits that a sender rejects (Python would exit before the camera opens) fails in one of the two suites. |
| Compile | `VirtualMirror.App` (with the new files, the launcher change and the Vision reference) and `HandsFaceShowcase` built with `dotnet build` against Unity 6000.3.9f1's assemblies, 0 errors. The Editor compiled the first revision of `Runtime/Vision` with 0 errors (Editor.log); it has not been brought to the foreground since, so it has not compiled these changes. |
| Regression | H-03 suites unchanged; Python `test_vision_fixtures` 45/45, `test_hand_wire` 34/34, `test_face_wire` 36/36. |

## 3. NOT TESTED

- **Play mode.** Nothing here has run in the Unity Editor: `VisionControlPanel` (IMGUI), `HandsShowcase` / `FaceShowcase` (primitives, trails, line renderers, `OnGUI`), the two scene files, and `AppBootstrap` carrying a `VisionSettings` in the Inspector. The scene files were written by hand from the structure of `Launcher.unity`; if Unity cannot read one, recreate it with the same components (README in `Showcase/HandsFace`).
- **The senders started from Unity.** `VisionHost` launching and restarting the Python senders (the process, the command line against a real camera, the grace window, stopping on scene exit) is not covered by any test: it needs the sidecar venv, the downloaded models and a camera.
- **A live camera**, as in H-01/H-02: the hand and face senders have still not seen a live frame. Which eye is which on a live face is the first thing to check (`M` swaps it).
- **Build players.** `tools/` is not shipped by `SidecarBuildPostprocessor` (root `*.py` only), so a built player has no hands or face sender; the receivers and scenes would wait for one started elsewhere.
- The scenes are in the Launcher (a new SHOWCASES column, see s.6) but not in Build Settings: the Launcher greys a row whose scene is not registered.

## 4. Traps found on the way

- A nested `[Serializable]` settings object inside AppBootstrap is the right shape here (35 fields would be a wall of private fields), but each field is a property over a private serialized field, to keep `AGENTS.md`'s no-public-fields rule. Adding a setting means a field, a property, a flag in `VisionSenderArguments` (if a sender takes it), a control in the panel, and a line in the command-line fixtures.
- `SidecarLaunchOptions` direct launch always appended `--host --port --model`; the vision senders accept none of `--model`. `DirectSenderOnly` skips them. The launcher also probes the supervisor's lock port, which would have made it "attach" to the body sidecar instead of starting; the vision launch skips that probe.
- Two programs cannot read one webcam: `Auto` per scene, not `Always` for both, is the default for that reason.
- A host that retried a failed UDP bind or a failed launch on every reconcile would log four times a second; both are remembered and retried only when the settings change or the user presses Apply.
- The scene's own `AppBootstrap` must switch the body path off (`useOakUdpTracking` 0, no Mirror scene), otherwise it would start the body sidecar and take the OAK-D port.
- The Editor only imports and compiles when it is in the foreground; a background edit is not compiled until then.

## 5. Next

1. Open the project, let it import and compile, open `HandsShowcase`, press Play, read the banner, press F6. Then `FaceShowcase`.
2. Register the scenes (**Virtual Mirror > Register All Scenes in Build Settings**); the Launcher rows are done (s.6).
3. A combined hands + face sender on one camera, so both can run at once.

## 6. Launcher (follow-up, same day)

The Launcher listed 21 scenes (10 one-person, 10 two-or-more, 1 production) and the number-key shortcut stops at 10 rows per column, so there was no room. It now has a fourth column, **SHOWCASES**: Fluid Disruption (which had no row for exactly that reason), Hands Only and Face Only. New `TrackingNeed.VisionOnly` for the last two: they need no body sidecar, so entering or leaving them does not start, stop or restart it (`AppBootstrap.EnsureSidecarForScene`, `DemoDirector` switch allowance). Fluid Disruption counts as single-person. The two sample scenes return to the Launcher on ESC. Left/right arrows wrap across four columns. Covered by `SceneLauncherF35Tests` (16 pass headless, with the rest: 121 total). NOT TESTED: the layout on screen (four columns on a narrow window), and the new rows greyed until the scenes are in Build Settings.
