# H-03 - Unity receivers for the hands and face wires (2026-10-13)

Decision: **ADR-101**. Tasks: `tasks.md` H-03. Sidecar repo: `docs/tasks.md` H-03. Usage guide: [`Runtime/Vision/README.md`](../Runtime/Vision/README.md).
Follows H-01 (hands wire, UDP 8901) and H-02 (face wire, UDP 8902). The user's request: "build the Unity receivers for hands and face".

## 1. What was built

A new assembly `VirtualMirror.Vision` (`Runtime/Vision`, references `VirtualMirror.Core` only) that listens for the two wires and gives Unity code the data three ways.

| Layer | Files | What it is |
|---|---|---|
| Wire | `Wire/WireScanner.cs`, `VisionWireParser.cs`, `HandsWireParser.cs`, `FaceWireParser.cs`, `WireKeys.cs`, `WireEnums.cs` | A pull scanner over the UTF-8 bytes (no `string`, no `JsonUtility`) and two parsers. Version and kind are checked as soon as they are seen. A datagram that does not parse returns a `WireParseResult`, never throws. |
| Data | `HandData`, `HandsMessage`, `FaceData`, `FaceMessage`, `VisionMessage`, `ArkitBlendshapes`, `VisionMath` | Reusable mutable messages (capacity fixed at construction, default 4), `CopyFrom`, the 52 names as constants with `IndexOf` / `Mirror`. `VisionMath` converts wire camera space (y down) to Unity (y up) and builds the head and palm rotations with its own managed maths. |
| Receiving | `Receiving/UdpVisionReceiver.cs` (generic), `HandsReceiver` (8901), `FaceReceiver` (8902) | A socket on a background thread; parse per datagram; lock + `CopyFrom` double buffer; sequence and session handling; `State` (NoStream / Live / Stale) from an injectable clock; counters by reason. |
| Bridges | `Providers/HandFrameMapper`, `FaceFrameMapper`, `UdpHandTrackingProvider`, `UdpFaceTrackingProvider` | The existing `HandFrame` / `FaceFrame` and `IHandTrackingProvider` / `IFaceTrackingProvider`, so the humanoid hand retargeter and `VrmExpressionRetargeter` run from a hands-only or face-only camera. Expression formulas are the same as `MediaPipeFaceProvider`. |
| Scene | `Behaviours/*` (`HandsReceiverBehaviour`, `FaceReceiverBehaviour`, `HandsGizmoView`, `FaceGizmoView`, `VisionStatusHud`, `VisionDemoRig`, `VisionDebugLog`), `Rigging/ArkitBlendshapeMap` + `ArkitBlendshapeDriver` | A drop-in demo (Vision > Demo Rig) and a driver that puts the 52 scores on any mesh with ARKit-named blendshapes by name, plus an optional head bone. |

## 2. The contract with Python

The wire is documented in the docstrings of `hand_wire.py` and `face_wire.py` and is pinned both ways:

- `python-sidecar~/tools/video/make_vision_fixtures.py` writes 22 golden datagrams (`Tests/EditMode/VisionFixtures`) with the PRODUCTION Python encoders from synthetic hands and faces of known shape; `--real` captures what the real networks emitted once (`real_hands.json`, `real_face.json`).
- The C# tests parse those files and assert the values the Python side put in them. `python-sidecar~/tests/test_vision_fixtures.py` (45 assertions) fails if the checked-in files drift from the encoders' output, and checks that the positives parse and the negatives are refused on the Python side too.
- A wire change therefore has to change the encoder, regenerate the fixtures, and keep both test suites green; it needs an ADR in both repos.

## 3. Verified

Headless: Unity 6000.3.9f1's own assemblies compiled with `dotnet build` (Vision and Core netstandard2.1, tests net9.0) and the NUnit tests run by a reflection runner, because the Editor held the project lock and `run_tests` closes it.

| Check | Result |
|---|---|
| C# tests (`VisionWireScannerTests` 17, `VisionParserTests` 32, `VisionReceiverTests` 14, `VisionBridgeTests` 23) | **86 passed, 0 failed.** The receiver tests use a real UDP socket on an ephemeral loopback port. |
| Mutation check, 36 injected bugs in the scanner, parsers, maths, receiver, bridges, map, copy | **36 of 36 caught.** First round: 28 caught, 8 survived; each survivor got a test that kills it (missing required fields, a landmark list of the wrong length, rejected datagrams published, `surprised` ignoring the eyes, a blendshape name inside a longer word, a held hand preferred to a tracked one, `CopyFrom` dropping landmarks), then re-run: 8 of 8 caught. One mutant (a fourth number accepted in a point) made the scanner loop forever; the hang is the proof it is caught and is now a test. |
| Allocation per parse (net9, `GC.GetAllocatedBytesForCurrentThread`, 2000 parses after 200 warm-up) | **0 bytes** for `hands_two` (2.4 KB), `hands_four_gestures` (5 KB), `face_full` (13 KB), `face_two` (25 KB). Measured on the .NET runtime, not on Unity's Mono. |
| Python regression | `test_vision_fixtures` 45/45, `test_hand_wire` 34/34, `test_face_wire` 36/36, `test_hand_tracking` 52/52, `test_face_tracking` 46/46, `test_landmark_filters` 11/11, `test_video_source` 157/157. |

## 4. NOT TESTED

- **The Unity Editor has not compiled or run any of this.** Everything above is headless against the engine DLLs. The MonoBehaviours, the gizmo views, the HUD, `VisionDemoRig`, and `ArkitBlendshapeDriver` (anything that touches the scene, `OnGUI`, `SkinnedMeshRenderer`, or Unity serialisation) are written to the documented API and were not exercised. The first Play-mode run is the first thing to do.
- **No live camera.** The senders (H-01, H-02) have not been run on a webcam, phone, or LAN camera either, so the end-to-end path camera to character has not been seen.
- **No rigged character** has been driven by `ArkitBlendshapeDriver` or the providers.
- Head pose and position are the sidecar's ESTIMATES (assumed field of view, assumed 0.09 m eye width, average hand); the receiver forwards them and does not make them measurements.
- The allocation figure is the parser only, on .NET 9. The receiver thread allocates for the socket and the log; Mono's allocator and the Editor were not measured.
- Mouth, brow, and gaze blendshape quality on a real face is H-02's open item and is unchanged.

## 5. Traps found on the way

- Unity's `Quaternion.LookRotation`, `Matrix4x4`, and friends are native calls (ECalls) that cannot run outside the Editor; `VisionMath` has managed versions (`LookRotation`, `FromMatrix`, `Rotate`) so the maths is testable headless. Keep them managed.
- The wire is y DOWN, Unity is y UP. Position, world landmarks and head rotation are converted in the parser; image landmarks are NOT (they stay in image space). The head rotation is conjugated by the y reflection, not just negated.
- A datagram rejected by the parser must not reach the sequence logic: the parser fills the worker message before it fails, so publishing it would move the sequence on. A test pins this.
- `MissingField` is a real result: a hand without `lm`, `wl`, `fing`, `ext` or `side` is refused, not zero-filled.
- Never index the blendshapes by ARKit order: 52 = `_neutral` + 51, no `tongueOut`. Use `ArkitBlendshapes`.

## 6. Next options

1. Play-mode check: start `run_hands_demo.bat` / `run_face_demo.bat`, add Vision > Demo Rig, read the HUD (NoStream / Stale / rejected).
2. Drive a rigged character from the face: `ArkitBlendshapeDriver` on a mesh with ARKit names, or `UdpFaceTrackingProvider` into `VrmExpressionRetargeter`.
3. Extract the sidecar's source handling to a root module so the senders can live beside the other sidecar code.
