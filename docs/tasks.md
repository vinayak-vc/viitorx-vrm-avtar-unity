# Virtual Mirror — Tasks

Update this file whenever work starts or finishes. Prefer small checkboxes agents can claim.

---

## HE-01 - Human Echo: the cutout becomes smoke, echo copies, a neon outline and GPU particles (2026-10-14)

Report: [`HE01_HUMAN_ECHO_2026-10-14.md`](HE01_HUMAN_ECHO_2026-10-14.md). Decision: **ADR-104**. Showcase: `Showcase/HumanEcho/README.md`. Follows S-01.

- [x] **Spike validated first:** the live mask drives FluXY dye + velocity; a compute shader samples FluXY's velocity atlas on the GPU; `Graphics.RenderPrimitives` draws 200 000 particles from a structured buffer in URP; cost measured (report s.2).
- [x] **Built and seen working in Play mode on two video files (one person portrait, two people landscape):** `Showcase/HumanEcho` - `PersonField` (Field + Motion), five layers (FluXY smoke, echo copies from a shared ring, neon outline, the person itself with flash and rim, GPU particles with a floor pile), three looks (Rainbow Smoke, Red Echo, Particle Body) switched with the number keys, HUD + keys, scene builder and look builder, README. No Python and no wire change. Launcher row **HUMAN ECHO** (SHOWCASES, `VisionOnly`), Escape returns to the Launcher.
- [x] Verified: `HumanEcho.Tests` 99/99 (GPU stages on synthetic textures, pure logic, HUD and keys; mutation-checked), `VirtualMirror.Tests` 608 passed / 0 failed (+1 Launcher test), `CutoutShowcase.Tests` 11/11, compile 0 errors, shaders 0 messages; cost 1.26-1.41 ms GPU per look in the Editor (floor 0.45 ms) with the mask rate unchanged; a killed sender clears the person and the HUD says WAITING FOR CAMERA; three-lens review, 26 findings, nearly all fixed.
- [ ] **NOT TESTED: a person in front of a LIVE webcam**; a network / LAN camera; a built player (no sender ships); screens other than 1920 x 1080; the HUD beside the F6 panel; the Escape key by hand; the controller's Dispose lifecycle (no EditMode test).
- [ ] Known limits: the S-01 mask limits (a floor light is a stray blob on the two-person clip); the motion is a silhouette estimate; no depth (the particle body's volume is faked); FluXY slow-motion below ~31 fps at `maxSteps` 4; no run-time quality switch; 92 code / attribute lines over 110 columns.
- [x] AppBootstrap Inspector: **Simple / Advanced** view (`Editor/AppBootstrapEditor.cs`, editor-only, no serialization change). Simple = one input choice (webcam, video file, network/LAN link, OAK-D) written to both the body path's Video Source and the vision streams' Source, the three streams, people, two switches; Advanced = the old sections plus a note on which camera section is for what. Compiles, Inspector drew with no Console errors; NOT clicked through by hand.
- [x] AppBootstrap audit and fixes (2026-10-14): split into partials (`TrackingFrame`, `Sidecar`, `ModelLog`) with `UpdateTracking` broken into stage methods; one shared `SidecarGuards` (producer probe + LAN PIN check) replaces two probes and two PIN checks; one `FindInScene<T>`; editing video fields no longer restarts the OAK-D sidecar; duplicate-instance and static status-provider leaks fixed; machine-specific paths removed; vision source errors debounced; misleading setting names explained in tooltips (renaming would lose saved scene values). 6 new tests, `VirtualMirror.Tests` 614 / 0. NOT done: the three duplicated camera/source setting sets still exist in serialized data (the Simple Inspector keeps them in step).
- [ ] Next: stand in front of a webcam and tune the three looks against yourself (the clips are phones filming a projection wall); decide whether to ship a quality switch; run `Virtual Mirror > Register All Scenes in Build Settings` on other machines; the S-01 "designed, not built" transition effects can now reuse `PersonField`.

---

## S-01 - Real-time human cutout: the person in a camera picture, isolated, shown in Unity (2026-10-13)

Report: [`S01_CUTOUT_2026-10-13.md`](S01_CUTOUT_2026-10-13.md). Decision: **ADR-103**. Sidecar repo: `docs/tasks.md` S-01. Showcase: `Showcase/Cutout/README.md`. Follows H-04.

- [x] **Built, tested, and seen working in Play mode with two video files:** Python finds each person (YOLOX-s + tracker), cuts a crop per person with the other people painted out, segments it (MediaPipe selfie segmenter, ONNX, DirectML), mattes it, and sends the picture + soft mask as one frame over a NEW chunked binary wire (UDP 8903). Unity (`Runtime/Vision`) reassembles it, uploads two textures (`CutoutTextures`) and draws them (`CutoutView`, `VirtualMirror/Cutout` shader): Original / Person (transparent or a checker / colour / gradient / image backdrop) / Silhouette (red first), glow outline, mirror, fit. No segmentation in Unity; independent of the VRM and of skeleton tracking.
- [x] AppBootstrap hosts it as the third vision stream (Off / Auto / Always, 20 settings, a Cutout tab in the F6 panel); `CutoutStreamBinder` puts it in any scene; `Showcase/Cutout/Scenes/CutoutShowcase.unity` + a Launcher row (HUMAN CUTOUT, SHOWCASES column); `scripts/run_cutout_demo.bat` without Unity.
- [x] Verified: 100 new C# tests; the whole `VirtualMirror.Tests` assembly passes (607 in 38 classes, 0 fail; `InspectorGroupingTests` fixed for H-04's tenth section) plus 11 in `CutoutShowcase.Tests`; Python 100 + 66 + 35 + 93 + 36 assertions; 16 of 16 Python and 15 of 15 C# injected bugs caught; the Python senders' argparse parses the command lines Unity builds (`args_cutout_*.txt`); the sender started by Unity and stopped on leaving Play.
- [ ] **NOT TESTED: a person on a live webcam** (the one 11 s webcam run, numbers only, found nobody: 29.3 fps, 0 datagrams lost, sender latency 12.5-19.7 ms); a phone / LAN camera; a network between two machines (JPEG, deflate, 1 200-byte chunks); a built player (no sender ships); a long soak.
- [ ] **Designed, not built:** particle / voxel silhouettes, camera-to-silhouette transition, disperse and reform, motion echo, edge trails (they read `VisionHost.CutoutTextures`; s.9 of the report).
- [ ] Known limits (report s.6): dark clothes on a dark background, thin gaps filled, floor lights kept, two people who touch, ~2 % flicker, 18-30 fps depending on the clip and the GPU clock.
- [ ] Next: stand in front of a webcam and look; register the scene on other machines (**Virtual Mirror > Register All Scenes in Build Settings**); build the transition.

---

## P-01 - Tracking Engine and Unity packages: PLAN ONLY (2026-10-13)

Plan: [`PLAN_TRACKING_ENGINE_AND_PACKAGES_2026-10-13.md`](PLAN_TRACKING_ENGINE_AND_PACKAGES_2026-10-13.md). **Nothing implemented.** Sidecar repo: `docs/tasks.md` P-01.

- [x] Phase 0 (2026-10-16): reuse of both repos confirmed (same account); annotated tag `pre-packaging` and branch `package-v1` created in BOTH repos. Python repo renamed on GitHub to `viitorx-tracking-engine` by the user; local submodule remote and `.gitmodules` updated.
- [ ] Phase 0 remaining: push `pre-packaging` tag and `package-v1` branch to origin (outward-facing: wait for user's go); user to revoke any old pasted tokens on github.com/settings/tokens.
- [x] Phase 1 (2026-10-16): **GO**. Nuitka standalone works: 0 `.py` shipped, 330 MB, starts in under a second, DirectML works frozen, no Python needed, outputs match the source version (plan section 10). Open from it: real WebRTC session, clean-machine test, face cost on a real face, size trimming.
- [x] Phase 2 (2026-10-16): wire spec `python-sidecar~/docs/WIRE.md` v1.0 (four wires, versioning rules, conformance checklist), `conformance.json` for all 48 golden files, `tests/test_wire_spec.py` 64/64, 11/11 mutants caught (plan section 11).
- [x] Phase 3 (2026-10-16): engine package `python-sidecar~/viitorx_tracking/` (CLI, `run --streams` one-camera mode, model registry + `fetch-models`, `manifest.json` check, `--parent-pid`, `VTSTATUS` lines, exit codes), `docs/ENGINE.md`, `tests/test_engine.py` 113/113, 40 of 41 mutants caught (plan section 12). Built as a layer over the unchanged modules, not a physical move.
- [x] Phase 4 (2026-10-16): compiled build + installer. `python-sidecar~/build/` (`build_engine.py`, `installer.iss`, `requirements-build.txt`), `tests/test_build.py` 99/99 (20 of 21 mutants caught, 1 equivalent), `tests/verify_build.py` 65/65 on the real folder (345 MB folder, 125 MB zip, 85.7 MB installer; file check, exit codes, real runs of hands/face/cutout/run/body, parity with the source within the source's own run-to-run noise). Plan section 13.
- [ ] Phase 4 open: RUN the installer (admin; user), `fetch-models` in the compiled exe over the network, clean-machine test, real LAN WebRTC session, webcam, size trim, CI workflow untested, pip layout deferred.
- [x] Phase 5a (2026-10-16): `Packages/com.viitorx.avtar.tracking.{core,sidecar,vision,cutout}` extracted with every GUID kept (796 before, 0 lost); `VirtualMirror.Sidecar` and `VirtualMirror.Cutout` are new assemblies; `NativeProcess` vendored; tests split per package (18 / 96 / 186 / 20); headless minimal runner identical before and after (517 / 97 env / 2); **320/320 in a real Unity batch run in a fresh project**. Plan section 15.
- [x] Phase 5b (2026-10-16): engine locator (setting, env, registry, per-user, StreamingAssets, Python tree), `viitorx-tracking.exe` launch with `--parent-pid`, ONE `run` process for several streams (engine: `--flip` per stream), host + panel say which engine; **368/368 in real Unity incl. the real compiled engine**; mutation 9/9 engine flip, 12/13 locator+launcher, 7/7 host group. Plan section 16.
- [x] Phase 5c (2026-10-16): `VisionBootstrap` + `VisionHostRegistry` + `VisionHostClient`; samples Hands and Face and Cutout (`Samples~`, scenes built in the fresh project from the imported samples); Play-mode tests 9/9; **the imported sample scenes played with the real compiled engine 3/3**; mutation 9/9. Plan section 17.
- [x] Phase 5d (2026-10-16): `com.viitorx.avtar.tracking.body` (provider, retargeting, avatar, IK, `BodyBootstrap`, `BodyAvatarDriver`, sample Body Avatar), `VirtualMirror.Tracking.Inference` (application-side), the umbrella `com.viitorx.avtar.tracking`; UniVRM optional through a define constraint; real Unity with and without UniVRM, Play mode 27/27 including the real engine. Plan section 18, ADR-106.
- [x] Body datagram tests (2026-10-16): real wire datagrams over UDP to a `BodyBootstrap` with a real VRM: arms, hands/fingers and sides, `TrackHands` off, the stale-stream failsafe, recovery, one log line per stall; Body Play tests 35/35, mutation caught.
- [x] Engine rebuilt with `build_parser()` (verify_build 65/65); application-vs-package equivalence test (0.00 degrees, static phases); FIXED: the package now turns the avatar 180 degrees before binding (arms crossed without it).
- [x] Release preparation, first part (2026-10-17): release descriptor in the engine build; `EngineInstaller` + Editor menu (tested with the real zip); `EngineCompatibility` (wire spec major) in the vision and body hosts; per-package notices; `tools/release/release_check.py` and `bump_version.py`; `docs/INSTALL.md`, `docs/RELEASING.md`; current docs' moved paths rewritten. Plan section 19.
- [x] First install from git (2026-10-17, branch `package-v1`): five packages resolved from GitHub, edit 565/565, play 47/47, samples 5/5; the umbrella alone from git fails (measured); `tools/release/git_install_block.py`. Plan section 20.
- [x] Tag install (2026-10-17): `v0.1.0` installed from GitHub in a fresh project, 565 + 47 + 5 pass. The tag's changelogs still say unreleased. The engine workflow's first run FAILED in the tests step (no depthai on the runner): fixed in tests/test_engine.py. Plan section 21.
- [x] Version bump and release 0.1.1 (2026-10-17): Option 2 executed. Engine test fix committed, `buildenv` virtualenv directory skipped in `build_engine.py` module_index; engine tagged `engine-v0.1.1`. Unity package family bumped to 0.1.1, changelogs finalized (`release_check.py --release` passes 6/6), submodule pointer updated.
- [ ] Next: verify GitHub workflow run for `engine-v0.1.1` and release artifacts; host choice (git / OpenUPM / own registry); real webcam runs; signing. The merge of `BodyAvatarDriver` with `AppBootstrap` was skipped by decision.
- [ ] Phases 5-7 (rest): Unity packages `com.viitorx.avtar.tracking.*` (the locator also reads the registry value `HKLM\Software\Viitorx\TrackingEngine\InstallDir`), dogfood, release.

---

## H-04 - Hands and Face in AppBootstrap, a control panel, two sample scenes (2026-10-13)

Report: [`H04_VISION_BOOTSTRAP_AND_SHOWCASE_2026-10-13.md`](H04_VISION_BOOTSTRAP_AND_SHOWCASE_2026-10-13.md). Decision: **ADR-102**. Sidecar repo: `docs/tasks.md` H-04. Follows H-03.

- [x] **Done and verified headless:** `VisionSettings` (35 settings) as the AppBootstrap "Hands and Face (H-03)" section; `VisionHost` (receivers, Python senders, demand, hand-over); `VisionControlPanel` (F6); `DirectSenderOnly` launch mode; `Showcase/HandsFace` (`HandsShowcase.unity`, `FaceShowcase.unity`). 105 C# tests pass; 24 of 24 injected bugs caught; Python command-line contract 20/20.
- [ ] **NOT TESTED: Play mode** for the panel, both scenes and the Inspector section; the host launching the senders from Unity; a live camera. The scene files were written by hand. *(S-01 update: the host starting a sender through `DirectSenderOnly`, the demand rule, stopping it on leaving Play and the AppBootstrap Inspector section WERE exercised in Play mode, through the cutout stream. The F6 panel's drawing, the Hands and Face scenes and a live camera are still untested.)*
- [x] Launcher: a fourth column SHOWCASES (Fluid Disruption, Hands Only, Face Only) and `TrackingNeed.VisionOnly`; 121 C# tests pass headless.
- [ ] Next: open the project, let it compile, Play `HandsShowcase` then `FaceShowcase`; register the scenes in Build Settings (Virtual Mirror > Register All Scenes); a combined hands + face sender for one camera.

---

## H-03 - Unity receivers for the hands and face wires (2026-10-13)

Report: [`H03_UNITY_RECEIVERS_2026-10-13.md`](H03_UNITY_RECEIVERS_2026-10-13.md). Decision: **ADR-101**. Sidecar repo: `docs/tasks.md` H-03. Usage: `Runtime/Vision/README.md`. Follows H-01 and H-02.

- [x] **Done and verified headless:** assembly `VirtualMirror.Vision`; allocation-free scanner and parsers; reusable messages; `UdpVisionReceiver` (hands 8901, face 8902); `UdpHandTrackingProvider` / `UdpFaceTrackingProvider` onto the existing provider interfaces; `ArkitBlendshapeMap` + `ArkitBlendshapeDriver`; drop-in `VisionDemoRig`. 22 golden fixtures from the Python encoders. 86 C# tests pass; 36 of 36 injected bugs caught; 0 bytes allocated per parse (net9); Python `test_vision_fixtures` 45/45.
- [ ] **NOT TESTED: the Unity Editor compile and Play mode** for every MonoBehaviour, the gizmo views, the HUD and the driver; a live camera; a rigged character. This is the first thing to do.
- [ ] Next: Play-mode check with `run_hands_demo.bat` / `run_face_demo.bat`; drive a VRM from the face (`UdpFaceTrackingProvider` into `VrmExpressionRetargeter`, or `ArkitBlendshapeDriver`); extract the sidecar source handling to a root module.

---

## H-02 - Face only: a face, 52 blendshapes, head pose and position, with no body in the picture (2026-10-13)

Report: [`H02_FACE_ONLY_2026-10-13.md`](H02_FACE_ONLY_2026-10-13.md). Decision: **ADR-100**. Sidecar repo: `docs/tasks.md` H-02. Follows H-01 (the user's order: hands first, then face).

- [x] Requirements (from H-01): webcam and LAN camera sources; 52 blendshapes; a new sender in THIS repo; licensing "carry what is current". User approved the downloads (3 ONNX models, the converter's NOTICE and script) in chat.
- [x] Model route: community ONNX conversions of the TFLite models in `face_landmarker.task` (fernandotonon / QtMeshEditor-models on Hugging Face, Apache-2.0 with a NOTICE), fetched by size + SHA-256 (`tools/model/fetch_face_models.py`). The exact contract (anchors, ROI rule, the 146 landmark indices, the 52 names, the pixel-coordinate blendshape input) was read from the converter's script itself, not from a summary. No new pip package.
- [x] `face_landmarker.py`, `face_tracking.py`, `face_geometry.py`, `face_wire.py`, `face_overlay.py`, plus the shared `roi_geometry.py` and `landmark_filters.py` (sidecar root); `tools/face/{face_udp_sender,face_udp_receiver,face_eval}.py`; `tools/video/vision_sender.py` (the frame loop both senders share); `scripts/run_face_demo.bat`.
- [x] Wire: `{ver:1, kind:"face"}` on UDP 8902, documented in `face_wire.py`'s docstring. 52 scores by `BLENDSHAPE_NAMES` (index 0 `_neutral`, 1-51 ARKit, no tongueOut).
- [x] Tests 183 new (`test_face_landmarker` 68, `test_face_geometry` 22, `test_face_tracking` 46, `test_face_wire` 36, `test_landmark_filters` 11); hands' 207 pass unchanged through the extraction. 42 of 42 injected bugs caught (3 survivors each got the test that kills them).
- [x] Run end to end on a real clip: sender (three real networks) -> UDP -> reference receiver, 29-30 msg/s, `bad=0 seq_gaps=0`; 435/435 frames, one id, 7.9 ms a frame on CPU.
- [x] Bug found by running it, not by the tests: a file's slow first frame (DirectML warm-up, 250 ms) made the pacer drop frames and the face was lost 14 times in the first second. Fixed in `vision_sender` (pacer restarts after the first frame); a test-visible regression guard is the run's `det_runs=1`.
- [x] The blendshape panel moved to a strip beside the picture (it covered the face on a narrow frame).
- [x] `ThirdPartyNotices.txt` entry 14 (and it says these are a third party's conversion).
- [ ] **NOT TESTED: a live webcam, a phone, a LAN camera; each expression channel on a face that blinks / opens its mouth / raises its brows; which of Left/Right moves when YOU close your left eye.**
- [ ] **NOT TESTED: the head angles against a real head (uncalibrated neutral pitch), `pos` against a ruler (`--hfov`, `--face-scale` assumed), the conversion's parity with Google's models (needs a throwaway venv with `mediapipe`).**
- [ ] Unity receivers for `kind:"hands"` and `kind:"face"` and an ARKit-names -> VRM-expressions mapping. Not started.
- [ ] Extract the video sender's camera / stream / LAN readers to a root module so the senders can ship in a Unity build. One process for hands + face if two senders on one camera is too heavy.

---

## H-01 - Hands only: find, track and read hands with no body in the picture (2026-10-13)

Report: [`H01_HANDS_ONLY_2026-10-13.md`](H01_HANDS_ONLY_2026-10-13.md). Decision: **ADR-099**. Sidecar repo: `docs/tasks.md` H-01. Priority order the user set: **hands first, then face only (H-02)**.

- [x] Requirements settled with the user: sources = webcam and LAN camera (url); 3D position AND finger poses AND gestures; later 52 face blendshapes; a new sender in THIS repo; licensing "carry what is current".
- [x] Model route: the OpenCV Zoo ONNX conversions of MediaPipe's palm detector + hand-landmark network, on the existing onnxruntime-directml. No new pip package (`mediapipe` would pull `opencv-contrib-python` next to `opencv-python`). User approved the download; GitHub's LFS host was reset by the network, so `tools/model/fetch_hand_models.py` uses the Hugging Face mirror, size + SHA-256 verified.
- [x] `hand_landmarker.py`, `hand_geometry.py`, `hand_gestures.py`, `hand_tracking.py`, `hand_wire.py`, `hand_overlay.py` (sidecar root); `tools/hands/hands_udp_sender.py|hands_udp_receiver.py|hands_eval.py`; `scripts/run_hands_demo.bat`.
- [x] Wire: `{ver:1, kind:"hands"}` on UDP 8901, documented in `hand_wire.py`'s docstring; the body datagram is untouched.
- [x] Tests 207: `test_hand_landmarker` 61, `test_hand_geometry` 24, `test_hand_gestures` 36, `test_hand_tracking` 52, `test_hand_wire` 34. 25 of 25 injected bugs caught (2 survivors each got the test that kills them).
- [x] Run end to end: sender (real networks, real clip) -> UDP -> reference receiver, 30 msg/s, `bad=0`, `seq_gaps=0`. 431 of 435 frames with a hand, 6.7 ms a frame.
- [x] `ThirdPartyNotices.txt` entry 13 for the models.
- [ ] **NOT TESTED: a live webcam, a phone, a LAN camera.** Nothing in H-01 has seen a camera frame. Run `scripts/run_hands_demo.bat` and `--video <LAN link> --show`.
- [ ] **NOT TESTED: depth against a ruler** (0.3 / 0.5 / 0.8 m) and the right `--hfov` for the laptop and the phone. **NOT TESTED: the L/R letter** against a known hand (raise your right hand).
- [ ] Unity receiver: a `kind:"hands"` UDP provider behind `IHandTrackingProvider` and a scene that draws it. Not started.
- [x] **H-02: face only, 52 blendshapes (built, see above).** Same pattern: a face landmark + blendshape network on ONNX, `kind:"face"` on port 8902, the same source handling. Not started.
- [ ] Extract the video sender's camera / stream / LAN readers to a sidecar root module so the hands sender can move to the root and ship in a Unity build.
- [ ] Crossing hands can lock both crops onto one hand (ids may swap); a detection-driven mode while two hands are close would fix it. Known limit, measured as an invariant only.

---

## F-62 - Several people in Fluid Disruption, and the people logic moved into one shared module (2026-10-12)

- [x] Why Fluid Disruption followed one person: the TRACKING SOURCE was shared (it already published the whole crowd) but the crowd
  handling (a slot per track id, a placement per person, release) lived inside `ChromaticRainShowcase`, and Fluid Disruption read only
  `stage.Pose`. Recorded as an open follow-up under F-58.
- [x] New assembly `Showcase.Shared` (namespace `ShowcaseShared`): the pose path moved out of Chromatic Rain's assembly (`ShowcaseTrackingSource`,
  `ShowcasePoseAdapter`, `ShowcaseBody`, `ScreenSpaceMapper`, `ITrackingTuning`, `SimulatedPoseSource`, `Joint`, `ShowcaseSource`, `SignalStatus`) and the crowd
  logic extracted from the Chromatic controller into `ShowcaseCrowd<T>` + `TrackedPerson` + `PersonSlots` + `ICrowdStepper<T>` + `ICrowdSource`.
  Fluid Disruption no longer references Chromatic Rain at runtime. `Showcase/Shared/README.md` says how to build a showcase on it.
- [x] Fluid Disruption: `DisruptionPerson` (state controller + movement tracks), `DisruptionPersonStepper`, a pooled `FluidDisruptionTargetDriver`
  (three targets per slot, created at start and added to the container), `maxPeople` in the settings, debug view and status text for several people.
- [x] Tests: `ShowcaseCrowdTests` 15 (new assembly + runner), Fluid Disruption 39 (+6), Chromatic Rain 63 (unchanged), project 393; 9 mutants caught,
  one redundant line found by a surviving mutant and removed. Live: two dancers -> two people, six targets (Fluid Disruption); two people (Chromatic Rain).
- [ ] The Chromatic Rain controller still has its own `StepPerson` (it needs the skin and the silhouette builder); a third showcase should copy the
  Fluid Disruption pattern (`DisruptionPersonStepper`), not that one.
- [ ] Observation, NOT investigated: in the open Fluid Disruption scene with `FluidDisruptionSettings_Rainbow.asset` the calm baseline fades to black
  within ~14 s, also with `maxPeople` 1 (so not caused by this change). Check before the venue.

---

## F-61 - Seated person: the figure no longer drifts up and down, the cut-off torso is not a pole, a lost limb keeps its shape (2026-10-12)

- [x] Measured on two screen recordings of a seated user (`Build/SR1.webm`, `SR2.webm`) replayed through the sidecar and the real scene: the floor
  estimate took invented feet and the grounding loop ran away (live: -8 m then +8 m). Fixed at three levels (ADR-097): sidecar masks the whole lower body
  below tier 0; `SkeletonPose` takes a floor only from a measured root + ankle + 0.3 m below the root + 10 frames; `TrackedStage` grounds only while the
  floor is re-measured, clamped to 0.75 m.
- [x] Chromatic Rain: the cut-off torso is a tapered upper-thigh stub (skin `cutOffExtensionMetres` 0.45, `cutOffTaper` 0.6), not a 3 m column; the adapter
  ignores the lower body of an estimated root and a joint that drops out keeps its last offset from its parent (a lost wrist travels with its elbow).
- [x] Tests: sidecar `test_framing.py` 78; Unity `PartialBodyF60Tests` 23, Chromatic Rain 63; project 393 / Fluid Disruption 33; 8 Unity mutants caught.
- [ ] Whole-body loss (every joint gone) still holds the last pose and fades; no extrapolation of the root. Not changed.
- [ ] A desk painted inside the picture with the model's hips at tier 0 is still not detected; the floor gate removes its effect, not the hips.

---

## F-60 - A body the camera cuts off: hips, then shoulders, then the nose (2026-10-12)

- [x] Sidecar `framing.py`: per-person anchor tier (hips / shoulders / nose) decided every frame with hysteresis, learned shoulder-frame hip offset and body widths, depth cascade (height -> shoulders -> face -> 2.0 m), joints outside the image sent as absent. `--partial-body` (default on) / `--no-partial-body` in `video_udp_sender.py`, `host_multiperson.py` (per person), `sidecar_supervisor.py --video-partial-body`. Wire: additive `fr`.
- [x] Measured on RTMW3D crops of real footage (`tools/video/f60_partial_body_eval.py`), live on the wire, and the auto-orient decision re-measured on upper-body crops (ADR-096).
- [x] Unity: `PoseAnchor` carried end to end; relaxed operating-volume near limit for an estimated root only; Chromatic Rain and Fluid Disruption treat the estimated hip centre as the hip; cut-off torso continued below the screen (skin `extendCutOffBody`); `Video Partial Body` bool.
- [x] Tests: sidecar `test_framing.py` 72/72 (18 mutations caught); Unity `PartialBodyF60Tests` 16 (incl. 2 over UDP), 9 cut-off tests in Chromatic Rain (60 total), 1 in Fluid Disruption (33 total); project suite 382 pass.
- [ ] Try it on a real laptop webcam with a seated user; set `Video Horizontal Fov Degrees` for that camera; tune `Volume Partial Near Metres`.
- [ ] Fluid Disruption in Play mode with a cut-off body; the VRM mirror app on a hip-less pose.

---

## F-59 - Chromatic Rain silhouette: anatomical filled body, skins, debug views (2026-10-12)

- [x] Anatomical filled silhouette (rounded-trapezoid chest and pelvis, tapered limb strokes, calf, neck, sloped shoulders, hands, feet), separate optional outline, mask debug views (V), 16 new shape and skin tests.
- [x] `SilhouetteSkinSet` with six built-in skins (Anatomical, Slim, Athletic, Neon, Soft glow, Minimal), editable live, B cycles.
- [ ] Try the skins on the venue screen with a live camera; add the venue's own skin to the set.
- [ ] Fluid Disruption: no silhouette there; unchanged.

---

## F-58 - Chromatic Rain shows several people (2026-10-12)

- [x] One silhouette + one set of fluid targets per tracked person (`ShowcasePerson`, `PersonSlots`, MAX-blended shared mask, `FluidLayer` slots), placed by each person's own sideways position. 35 self tests, Play mode with two dancers.
- [ ] Fluid Disruption: the same change (it still follows only the first person).
- [ ] Try it on a live phone / webcam with three people.

---

## F-57 - Auto orient: portrait / landscape / sideways from a LAN camera, webcam or video (2026-10-12)

```text
VERDICT: BUILT, TESTED ON RECORDINGS AND THROUGH UNITY PLAY MODE. NOT RUN ON A LIVE PHONE.
```

- [x] `frame_orientation.py`: four-rotation score (detector x pose x upright geometry), vote logic, size-change re-probe, stall self-check, long-axis focal length, detector-seeded crop. 55 Python assertions, 12 mutations caught.
- [x] Sender flags `--auto-orient` / `--hfov-axis`; supervisor `--video-auto-orient` / `--video-hfov-axis`; Unity bool `videoAutoOrient` (default ON) -> `SidecarLaunchOptions.VideoAutoOrient`; live edits restart the sidecar like the other video fields. 3 C# tests.
- [x] Decision measured on real clips (96/96) and end to end through the sender and through Unity Play mode.
- [ ] Run it on the real phone via the LAN camera server and a real webcam, turning the device between portrait and landscape.
- [ ] Set the phone's real FOV in `Video Horizontal Fov Degrees` (the default 60 across the long side is an assumption).
- [ ] Optional: the same auto rotation for the OAK-D senders (they use the known mount, `--portrait`, today).

---

## F-56 - FluXY 1.4.1 local fixes (2026-10-11)

- [x] `DisposeFramebuffer` releases `tileID`; readback textures only on demand, reused, destroyed; `GetCurrentLOD` null-camera crash; per-solver `_TileData` / `_TileID` binding and per-container property block. Patch: `docs/fluxy-1.4.1-local-fixes.patch`.
- [x] Each defect reproduced on the original code and gone on the fixed code (leak counts, NRE, two-solver tile corruption).
- [ ] Re-apply the patch if FluXY is updated or re-imported; consider reporting the defects upstream.

---

## F-55 - Fluid Disruption showcase (2026-10-11)

```text
VERDICT: BUILT, COMPILING, TESTED IN THE EDITOR. NOT RUN ON A PHYSICAL CAMERA.
```

- [x] Scene, FluXY baseline (seeded field + relaxation + flow sources), interaction targets, state machine A-F, movement estimator, demo programme, UI, debug view, shader, scene builder, 31 EditMode tests (`Showcase/FluidDisruption`).
- [x] Tracking handled by the scene's `AppBootstrap`; shared source / status / adapter code extracted from Chromatic Rain instead of copied (`ShowcaseTrackingSource`, `ITrackingTuning`, `ShowcaseBuilderUtil`); the 28 Chromatic Rain tests still pass.
- [ ] DECISION: a Launcher row (and Build Settings registration). The one-person column is full (10-row cap pinned by `SceneLauncherF35Tests.EveryColumnFitsTheNumberKeyShortcut`); not done on purpose.
- [x] Verified in Play mode (see `ai_handoff.md` F-55): calm baseline from frame 1, hand independence, torso travel, stillness settles, hand-landmark loss and restore without a jump, stream cut and restore, person leaves and returns, quality levels, shader extras off, FluXY off, and the live path through the real sidecar on a recorded video.
- [ ] Run on the physical OAK-D / camera. Tune the visuals on the venue screen.
- [ ] Measure a player build (the numbers are the Editor).

---

## F-54 - Chromatic Rain showcase (2026-10-11)

```text
VERDICT: BUILT, COMPILING, TESTED IN THE EDITOR. NOT RUN ON A PHYSICAL CAMERA.
```

- [x] Scene, shaders, silhouette, rain, FluXY layer, UI, debug view, simulated pose, scene builder, 28 EditMode tests (`Showcase/ChromaticRain`).
- [x] Tracking handled by the scene's `AppBootstrap` (video / webcam / phone / OAK-D sources exactly as in `Bootstrap.unity`); the showcase only listens.
- [x] Launcher row + ESC to menu + Build Settings registration (`SceneLauncher`, `SceneRegistrar`).
- [x] Verified: bootstrap-launched video sidecar -> LIVE; Launcher round trip; port-in-use is reported, not fatal; stream loss fades the silhouette and stops the fluid targets; mirror flip; mutation-checked tests.
- [ ] Run on the physical OAK-D / camera. Tune the visuals on the venue screen.
- [ ] Measure a player build (the 4 ms figure is the Editor).

---

## PER-PERSON FILTERS (F-33, 2026-09-17) — multi-person gets the measured single-person chain

```text
BUILT AND MEASURED. Every tracked person carries a full P0 + P1-1 + F-22 chain keyed to their
track id. At 30 fps the multi-person chain IS the single-person chain; below 30 fps it is better,
because it knows its own rate. 46/46 new tests, 270/270 across the sidecar suite.
Report: F33_PER_PERSON_FILTERS_2026-09-17.md   ADR-071   Evidence: docs/evidence/f33/
```

### BUILT
- [x] **`person_filters.py`** — `PersonFilters` (one chain) + `PersonFilterPool` (keyed on TRACK ID,
      never on list position: the tracker re-sorts every frame, so an index-keyed bank hands one
      person's One-Euro history to another). Lifecycle is idle-based so a caller cannot leak it.
- [x] **Each person measures their OWN sample rate.** One-Euro derives velocity as `delta * freq`,
      so a filter told 30 while sampled at 16 over-estimates speed by 1.9x and OPENS UP when it
      should damp. Median of the last 15 gaps; `smoothing.py` gained an additive `set_freq()`.
- [x] **ADR-071: the feet and the head joined the filter group.** They were in none — not trunk, arm,
      leg or hand — and produced the worst artefacts left in the filtered run, for opposite reasons
      (feet fail with `src=0` and need the HOLD; the head fails with `src=1` and needs the CAP).
- [x] **`tools/diagnostics/f33_filter_bench.py`** — ground truth, so LAG is measured next to jitter.
      Without it "the output moved less" cannot be told from over-smoothing.
- [x] **`tests/test_person_filters.py`** — 46/46, covering AGENTS.md §8's required list plus the
      re-ordering trap, the per-person M11 hold, and a config-drift guard that reads the
      single-person sender's SOURCE and fails if any shared default stops matching.
- [x] Harness defect fixed: `f32_multiperson_video.py` drove the tracker from the WALL CLOCK, so a
      replay was not reproducible. Both it and the filters now run on the video's own timeline.

### MEASURED
- [x] **Implausible single-frame steps (>300 mm) over 2060 person-frames of identical input:**
      no filters **2158** → single-person grouping **630** → +feet +head **77**. Worst step
      2091 mm → 960 mm.
- [x] **Against known ground truth at 16 fps** (three people): jitter median 32.4 → 19.3 mm, lag
      250 ms. At 10 fps the NAIVE port (rate pinned at 30) is **worse than no filter at all** on the
      median frame — 35.6 mm vs 33.5 mm — while costing 400 ms of lag.
- [x] **Cost 0.88 ms per person-frame**, against 20.7 ms for the pose solve it follows (~4%).
- [x] Joints emitted 60.2% → 59.1%: the chain refusing what it does not believe, as `[0,0,0,0]`.
- [x] `wholebody_udp_sender.py` byte-identical; `smoothing.py` diff has zero deleted lines.

### ⚠ BEFORE DEMOING F-33
- [ ] **Never run with real people.** All evidence is recorded video and synthetic trajectories.
- [ ] **The video A/B's depth is SYNTHETIC.** A clip has no stereo, and with no depth the smoother
      has nothing to smooth and P1-1 starves every joint to LOST. `src=1` in that run means
      "sampled the synthetic depth frame", never "measured by stereo".
- [ ] **Lag is 233 ms at 30 fps.** Not new — it is the accepted single-person tuning. Say so with
      any jitter figure; a jitter number without a lag number means nothing.
- [ ] **A slow confident drift is still not caught** (847 mm of an injected 850 mm). P1-4 catches it
      and P1-4 is rejected for production. Inherited blind spot, pinned by a test.
- [ ] F-32's "11 ids for 7 people" video figure was machine-dependent and is superseded: the
      deterministic harness gives **9 ids across two passes**.

### NEXT
- [ ] **Per-person `st` in Unity.** The wire now carries real per-joint states per person (all `-1`
      before F-33). The root person's already reach the existing Trust HUD; a crowd HUD needs
      `PersonPose` to carry them.
- [ ] The 77 residual implausible steps are dominated by whole-body ORIGIN shifts (~230 mm), where
      the hip moved and every hip-relative landmark moved with it. Hips sit on the global 1.5 m cap
      in both senders; three events is not enough to retune a constant tuned on live data.

---

## MULTI-PERSON (F-32, 2026-09-17) — detection, tracking, and the first two-person experience

```text
BUILT AND WORKING END TO END on recorded video through the real OAK-D.
19/19 tracker unit tests. 7/7 Unity acceptance checks. Nothing regressed.
Report: F32_MULTIPERSON_2026-09-17.md   ADR-070   Evidence: docs/evidence/f32/
```

### CORRECTION FIRST - this invalidated part of F-29
- [x] **`456.webm` contains SEVEN dancers.** F-29 treated it as one subject at 2.9 m and drew range
      conclusions from it. The tracked body there has a shoulder width ranging 0.068-0.455 m (CV
      22.8%, a 30x pixel swing) - a chimera assembled from whichever dancers fell in the migrating
      crop. Every degradation figure from that clip measures IDENTITY CONTAMINATION, not distance.
- [x] A genuine single subject at 1.96 m gives **93.7% plausible hands**, close to the 1.4 m figures.
      Hand tracking does NOT collapse at 2 m.
- [ ] **The real single-subject limit beyond ~2 m is still UNTESTED.** Do not assume it holds or
      that it fails. This needs one clean clip of one person at 3 m.
- [x] Corrected in the F-29 report, README, CHANGELOG and every code comment citing those numbers.

### BUILT
- [x] **`assignment.py`** - pure-numpy optimal assignment. Greedy is suboptimal on **54.6% of random
      4x4 matrices** (measured) and its failure mode is exactly an ID swap between crossing people.
      Verified optimal against brute force on 200 random rectangular matrices.
- [x] **`person_tracker.py`** - detections to persistent ids. Associates in **3-D**, which no
      off-the-shelf tracker can: two people overlapping on screen at different depths have perfect
      IoU and are trivially separable in Z. 19/19 unit tests, including the crossing case.
- [x] **`multiperson_udp_sender.py`** - detector on the VPU, tracker on the host, RTMW3D per person.
      A NEW file: the production sender carries the whole measured single-person stack and was left
      byte-identical.
- [x] **`tools/video/f32_multiperson_video.py`** - the testable path while nobody is in the room.
- [x] **Unity:** `CrowdFrame`, `TrackedBody`, provider `ParseCrowd`/`TryGetCrowd`,
      `TrackedStage.Bodies`.
- [x] **`Scenes/Bonds.unity`** - experience 9, the first that needs two people.
- [x] **Backward compatibility verified:** `root lm == persons[0].lm` on every packet; `Pose` and
      `Bodies[0]` are the same person across 752 Unity frames. The mirror app and all nine existing
      scenes consume the multi-person sender unchanged.

### MEASURED
- [x] Detector `retail-0013` beats the vendored BlazePose blob: median **7 of 7** people vs 1-5.
- [x] Detector is **free** on the VPU: rgb 29.8 -> 29.7 fps, depth 29.6 -> 29.4, detector 11.4 fps.
- [x] RTMW3D **20.7 ms p50, fixed batch of 1** -> 2 people 24 fps, 3 people 16 fps, 4 people 12 fps.
- [x] Tracking the 7-dancer clip with NO depth: median 7 tracked, 6 ids alive >50% of the clip.

### ⚠ BEFORE DEMOING MULTI-PERSON
- [ ] **Never run with two real people in front of the camera.** Everything is recorded video.
- [x] ~~**Multi-person output is RAWER than single-person**: no P0 smoother, no P1-1 joint tracker,
      no P1-4 recovery, no F-22 validation per person.~~ **DONE in F-33** — see the section above.
- [ ] Nothing has been rendered on screen - no visual claim about Bonds has been checked.
- [ ] Long occlusions still cost an identity. Metric depth should fix it; video cannot test that.
      (The "11 ids for 7 people" figure was machine-dependent — the harness read the wall clock. The
      deterministic harness gives **9 ids across two passes**. F-33 §7.)
- [ ] The detector blob is NOT committed (2.3 MB). `setup_sidecar.ps1` does not yet fetch it.
- [ ] Latent bug left alone: `mediapipe_utils.non_max_suppression` raises IndexError on OpenCV 4.x.
      Dead vendored code that production never calls; fix it if anything ever does.

## ✅ F-31 TIME ECHO + SOUND (2026-09-16)

```text
BUILT AND COMPILING. 8/8 EchoBuffer tests. Nothing has been SEEN or HEARD — see below.
ADR-068 (sound), ADR-069 (time echo).
```

- [x] **Time Echo** (`Scenes/TimeEcho.unity`) — four delayed copies of you trailing behind. Turns the
      project's hardest limit into the subject: RTMW3D-x is single-person with no detector and no
      track id, and F-21's live two-person acceptance failed, so this makes a crowd from one person
      without touching any of that. Replay of already-validated pose data, so it cannot fail in a new
      way; body joints only, so it works where the hands do not.
- [x] **`EchoBuffer`** — a fixed, clock-injected ring, extracted to a top-level class **because a
      mis-indexed ring shows a plausible body at slightly the wrong time and no viewer can tell**.
      8 unit tests including the post-wrap case.
- [x] **Sound as a shared layer** — `ExperienceAudio` on `ExperienceBase`, so all nine scenes gained
      a drone that follows body energy, a movement layer, and arrival/departure cues without being
      edited. Closes F-28 §7.4, open since there were three modes.
- [x] **Synthesised at runtime** — no `.wav` assets, no import settings, no licence questions; the
      scene stays a camera and one GameObject. Drone partials snap to whole-cycle frequencies so the
      loop does not click.
- [x] **Every pitch is pentatonic**, and `Play` takes a scale DEGREE rather than a frequency, so a
      caller can compose a rising run and is structurally unable to produce a wrong note. This is what
      decides whether the room is bearable after an hour of strangers triggering it.
- [x] **Cues wired into the events that already existed**: bubble pops rise with the combo, a
      footprint petal plays a climbing note, an object hit is louder for a harder swing, grab/release,
      a pose match chord, a stroke starting in Air Graffiti.
- [x] `M` mutes in every scene.

### ⚠ Before demoing F-31
- [ ] **Nobody has heard any of it.** `AudioClip.Create` is a native call, so the synthesis cannot
      run headlessly — it compiles and the arithmetic is right, but every judgement about how it
      SOUNDS is unmade. Expect to retune levels; the mix constants are all named.
- [ ] **Nobody has seen Time Echo.** Same Editor-lock caveat as the other eight scenes.
- [ ] Check the drone does not mask the cues in a real room; they are two octaves apart on paper.

## ✅ F-30 EXPERIENCES (2026-09-16) — seven scenes on one shared tracking core

```text
BUILT AND COMPILING. Not rendered — see the caveat block below before demoing.
Report: F30_EXPERIENCES_2026-09-16.md    ADR-067    Evidence: docs/evidence/f30/
```

- [x] **`TrackedStage`** — everything between the socket and a usable body, owned once: provider,
      converter, the F-27 humanized layer *and its once-per-pose rule*, world pose, both hands, trust
      channel, attract loop, presence gate, floor grounding. A plain class, not a MonoBehaviour, so
      the order of "advance the tracking" and "read the tracking" lives in the code rather than in
      Unity's script execution order (ADR-067).
- [x] **`ExperienceBase`** — shared staging, HUD, keys (`R` reset / `H` humanized / `A` attract /
      `G` ground), and `ScoringAllowed`, which stops any experience scoring the synthetic attract
      figure.
- [x] **Seven scenes**, each a camera and one GameObject: `Footprints`, `Fluid`, `ObjectPlay`,
      `BubblePop`, `PoseMatch`, `DepthReach`, `AirGraffiti`.
- [x] **`SkeletonPose.Velocity`** — per-joint world velocity. **`|Velocity|` is NOT `Speed`**: equal
      on straight-line motion (1.000 vs 1.000 m/s), but the smoothed vector partly cancels on
      reversing motion (mean ratio 0.678 on a dancer, max 0.999). Take direction from `Velocity`,
      magnitude from `Speed`. Documented on the field; both the fluid and the object strikes comply.
- [x] **Verified:** 6/6 assemblies compile; `TrackedStage` driven over a real socket with recorded
      production packets, 7/7 checks; 3 velocity checks; F-29's 23 EditMode tests still pass.

### ⚠ Before demoing F-30
- [ ] **Nothing has been rendered on a screen.** Three Editors held the project lock all session.
      Every geometric and numeric claim is tested; **no visual claim is** — colours, sizes, bloom,
      whether a bubble is reachable, whether the fluid reads as fluid. Expect to tune constants on
      first run; they are all named with the reasoning attached.
- [ ] **Scene files were generated, not authored in the Editor.** Each is the F-28 scene with its
      component swapped; script GUIDs are md5 of the asset path. If a component shows as missing, the
      `.meta` guid and the scene's `m_Script` guid have diverged.
- [ ] **`SkeletonShowBootstrap` still has its own copy of the tracking chain.** `TrackedStage` was
      extracted for the experiences; F-28/F-29's scene was left alone because it carries the verified
      evidence and the refactor could not be visually checked. **Top follow-up** — two copies drift.
- [ ] Air Graffiti and Object Play's *grabbing* need the subject close in (hands are 99.1% plausible
      at 1.4 m, 59.9% at 2.9 m). Both say so on screen; both will frustrate at three metres.

## ✅ F-29 TRUST HUD / HANDS / FEET / ATTRACT (2026-09-16)

```text
BUILT AND VERIFIED HEADLESSLY. 23/23 EditMode tests, 29/29 acceptance checks against the real
provider over a real socket. Report: F29_TRUST_HANDS_FEET_ATTRACT_2026-09-16.md   ADR-066
```

- [x] **Trust channel on the wire** — `st` (P1-1/P1-4 state per joint), `own` (F-21 ownership),
      `lat` (measured camera-to-payload latency). All optional and read-only; nothing upstream reads
      them back. `st` captured AFTER P1-4 so it matches the geometry actually emitted.
- [x] **Mode 4 TRUST HUD** — the per-joint state that previously reached only a log file, drawn.
      Colour means TRUST here, deliberately breaking the show's speed rule; legend permanently on
      screen for that reason.
- [x] **Mode 5 HANDS** — all 21 landmarks per hand, palm plane, pinch, finger count. Gesture
      measures normalised by palm length so they cannot fire on subject distance.
- [x] **Attract loop** — a synthetic pose SOURCE, not a separate renderer, so it demonstrates the
      real modes. Closes F-28 §7.4.
- [x] **Feet drawn** — heels 29/30 and toes 31/32 were always on the wire (431/431 and 330/330
      frames measured); each foot is now an ankle–heel–toe triangle.
- [x] **Fixed: the figure stood 153 mm inside the floor** in every mode since F-28, invisible until
      the feet were drawn. `groundToFloor` settles it to 0.3 mm with no overshoot.
- [x] **Fixed: floor-driven effects were 81–127 mm too high** — the floor came from the lowest
      *ankle*; it now comes from the four foot contact points.
- [x] **Fixed: latency mixed two clocks** — `UtcNow − arrivalStopwatchTime` reported 1.79 × 10¹² ms.
      Real figure: **53.4 ms end-to-end**.

### ⚠ Measured limits that bound what should be built on this
- [ ] **Floor contact is usable at ~1.4 m (21 mm smoothed), NOT at ~2.9 m (81 mm)** — and smoothing
      makes it *worse* at distance, which is the signature of drift rather than noise. Do not build
      true contact detection at three metres.
- [ ] **Hands: 99.1% plausible at 1.4 m, 59.9% at 2.9 m**, with palms reported up to 362 mm. Gated
      and reported rather than hidden, but do not depend on hands at range.
- [ ] **Hand gesture thresholds are uncalibrated** — derived from the distribution of ordinary hand
      poses in two dance clips, not from a subject performing gestures on cue.
- [ ] The trust HUD's depth row has only ever shown `0/33 MEASURED`; the MEASURED path is correct by
      construction and **has not been seen**, because no live OAK-D session has been run.

## ⚠ F-26 AVATAR vs DEBUG SKELETON (2026-09-16) — observation report, no decision taken

Screen recording reviewed (`LatestScreenReording.mp4`, 129 s). **The tracking is not the problem; the
retarget is.** The debug skeleton follows the subject through every pose in the video — including
seated and arms-overhead — while the VRM avatar diverges, monotonically, the further the subject gets
from neutral standing.

- [ ] **Full report: [F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md](F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md)**
      — frame-by-frame table, complete avatar/skeleton comparison, failure mechanisms, four resolution
      options. Evidence in `docs/evidence/F26_avatar_vs_skeleton/`.
- [ ] **⚠ "AVATAR QUALITY — PROVEN GOOD" does not mean pose fidelity.** Every F-19 avatar metric
      (0.012° median yaw, zero snaps, bone lengths to 0.0011 %, zero swaps) measures **internal
      consistency**, not correspondence to the subject. All four are compatible with an avatar that is
      smoothly and stably in the WRONG pose — which is what the video shows. F-19 is not falsified; the
      inference carried into roadmap.md is. See F-26 §5.1.
- [ ] **⚠ No fidelity metric has ever existed.** Nothing compares avatar pose to subject pose. That
      absence is why this went unnoticed, and it is the first thing to fix whatever else is decided.
      Per-joint angular error between the skeleton's bone directions and the avatar's is computable
      today, offline, with no new hardware. F-26 §9.1.
- [ ] **⚠ F-19's requested elbow flexion clamp (~150°) was never built.** The only 150° in the
      retarget is `TorsoYawGuard.WrapGuardDeg`, unrelated. The elbow case was closed instead by F-22
      zeroing confidence in the sidecar — a hold, not a clamp, at a different layer. Possibly the
      better design; never recorded as a substitution. F-26 §5.4.
- [ ] **Top avatar defects, by size:** (1) the avatar cannot leave vertical — nothing drives root
      rotation and the spine-bend high-pass treats a sustained lean as drift, so it cannot sit;
      (2) arm elevation is lost — the hand is used as a direction, never as a target;
      (3) legs splay, no hip-width normalisation; (4) a held limb reads worse than a missing one.
- [ ] **`showDebugSkeleton` defaults to false** — the most informative diagnostic in the project is
      off by default.

## ✅ OFFLINE HAND-OFF-FIX PASS (2026-09-15) — the §27 silent hand-off is closed

```text
F-21   CONDITIONAL - OFFLINE FIX VERIFIED, LIVE ACCEPTANCE PENDING
F-22   OFFLINE ENGINEERING COMPLETE      LIVE HANDS-NEAR-FACE ACCEPTANCE PENDING (L2)
F-20A  PASS
F-20B  PASS, unchanged - the §31 supervisor passthrough is additive (35/35 still)
```

- [x] **F-21 path-consistency gate (ADR-057)** — a reacquire must now be *path*-consistent, not only
      position-consistent: a candidate tracked walking in from outside the margin is refused
      (`reason="path_walked_in"`). Sidecar-only, ~20 lines, no new identity signal, no Runtime/
      change. Reuses `SWITCH_MARGIN_M` as the continuity distance rather than adding a tunable —
      measured separation: largest real single-frame body displacement **126.1 px** vs a 141 px
      margin vs the three real body-to-body jumps of **303.5, 323.0 and 687.5 px** (2.15× clear of
      the smallest of them).
- [x] **The number that matters, on the clip that showed the bug** — `123.webm`, whole production
      chain, production config, against offline ground-truth labels:
      **ownership off 102 → F-21 as shipped 40 → F-21 + gate 2** wrong-person frames, and those
      last 2 are a *verified labeller artefact* during the subject's entry, present identically in
      all three arms. **The hand-off segment itself is 0.** The hand-over that does occur is now
      **declared** — `TARGET_RELEASED` + `TARGET_SWITCH` + a new epoch.
- [x] **The cost, measured rather than assumed** — 96 frames (1.60 s) of the *legitimate returning
      owner* withheld, one contiguous block. A returning owner and an intruder walking in are the
      **same observation stream** from a hip position; the gate does not resolve that ambiguity, it
      resolves it towards *announcing* the change. Brief §7 and ADR-057 both say that is the right
      way round. ~2.5 frames of the right person withheld per frame of the wrong person suppressed —
      a property of this clip, **not** an installation rate.
- [x] **No regression where the gate should never fire** — `video.webm` (single person) and
      `456.webm` (eight dancers, real crossing/occlusion) are **bit-identical** with the gate on and
      off: 435/431/4 and 356/347/9, zero releases, zero switches, zero path rejections in both.
- [x] **Tests** — unit **46/46 → 61/61**; deterministic adversarial **63/63 → 101/101** with the
      brief's P1–P11 added and the original 17 scenarios untouched. **P11 is the control**: the same
      input with the gate off reproduces the silent hand-off exactly (43 wrong-person frames,
      `switches=0`, `epochs=1`), which is what makes the claim falsifiable. It is excluded by name
      from the suite aggregate so a deliberately-disabled run can never read as production behaviour.
- [x] **The measuring instrument was wrong three times, and that is in the report** — median hue →
      largest share → "absence of green means the other person". The last one anchored an epoch to
      the wrong person and reported **429** wrong-person frames. Two further filters were built and
      **removed** because they deleted the man from every two-person frame. Now validated against 41
      eye-checked frames: **36/41, 4 conservative (excluded from scoring, cannot invent a
      wrong-person frame), 1 dangerous and proven unreachable.** See F-21 §30.6.
- [x] **Performance** — isolated A/B over 775 real observations × 200 reps: **+0.204 µs/frame**,
      +14.5 % of the ownership layer, **+0.0006 % of a 30 fps frame budget**. Three scalars of state,
      no buffer, no allocation.
- [x] **⚠ PRODUCTION DEFECT FOUND: `--cue-file` had never worked (§31.4)** —
      `wholebody_udp_sender._read_cue()` calls `io.open` in a module that **never imported `io`**, and
      its own `except Exception: return None` swallowed the `NameError` on every frame. `_read_cue` is
      the only user of `io` in that file, so nothing else surfaced it. **The on-screen cue banner has
      been silently dead since it was written** — the banner REV 2 added specifically because the
      first live attempt was inconclusive when the operator couldn't read console instructions at the
      camera. Tomorrow's session would have repeated that failure with a report saying it was fixed.
      One-line fix, verified by actually rendering the banner (imaged). Found by *testing* the
      feature, not by reading the code — an absent banner looks like operator error, not a bug.
- [x] **Supervisor + protocol integration, real OAK-D, empty room (§15/§31.3)** —
      `f21_supervisor_integration.py` **11/11**: supervisor starts the sidecar, both flags reach the
      running process (read from its argv, not inferred), the preview window is enumerated from
      user32, the cue round-trips through the real reader and renders, the ownership log lands where
      the protocol reads it, `SidecarExit` is detected and restarted, and never more than one sidecar
      tree is alive. Two initial "failures" were *this test* being wrong, not the product — recorded
      in §31.3, because a test wrong in the optimistic direction is the dangerous kind.
- [x] **Dead-producer abort path TESTED, not just written** —
      `f21_live_protocol_abort_test.py` **9/9**: a stub producer is killed mid-phase and the protocol
      returns non-zero, records rc / phase / wall-clock / last ownership state / completed phases, and
      **does not run the phases after the death**.
- [x] **Offline replay soak, twice, and a harness bug found doing it (§30.13)** — 17 min / 21 cycles
      / **31,851 frames / 0 exceptions**, memory ends *below* warm-up, 80 TEMP_LOST all reacquired,
      2,770 holds / 2,770 recoveries / 0 unrecovered. But it logged **0 path-gate rejections** across
      21 replays of the hand-off clip, which did not add up: `f2x_replay_soak.py` fed ownership
      **metres** while setting `switch_margin_m` from a **pixel** diagonal — a ~141 *metre* gate, so
      nothing could ever be rejected. Pre-existing harness defect, not production. **Every soak to
      date therefore exercised F-21's plumbing and never its discrimination**, and its clean "0
      releases" line read as correctness when it was an artefact. Fixed to production constants and
      re-run against `123.webm` with per-cycle reset: **600 `path_walked_in` rejections, 20 declared
      TARGET_SWITCHes, 15,662 frames, 0 exceptions, no memory growth, 749/749 recoveries.**
- [x] **Live tooling fixed before the session (§31)** — `f21_live_protocol.py` now polls `proc.poll()`
      during every wait and aborts with return code / phase / timestamp / last F-21 state instead of
      scoring phases against a dead producer; `sidecar_supervisor.py` forwards `--show` and
      `--cue-file` **explicitly** (not a generic `--extra-args`, which could also forward
      `--no-ownership` and change what is under test). New phases `WALK_IN_IMPOSTOR` /
      `WALK_IN_OWNER` target this pass's finding directly.

## ✅ OFFLINE COMPLETION PASS (2026-09-14) — no physical person available

Everything provable without someone standing at the camera is done. Two live acceptance gates remain
and **only** those; the next hardware session is validation, not development.

```text
F-21   OFFLINE ENGINEERING COMPLETE      LIVE ACCEPTANCE PENDING (two-person matrix)
F-22   OFFLINE ENGINEERING COMPLETE      LIVE HANDS-NEAR-FACE ACCEPTANCE PENDING (L2)
F-20A  PASS, reconnect re-verified end to end against the real C# consumer
F-20B  PASS, restored - a production-blocking empty-room defect was found and fixed (ADR-055)
```

- [x] **F-21 deterministic multi-target suite** — `f21_adversarial.py`, all 17 brief scenarios,
      **63/63**. Ground-truth labels make it measure *silent wrong-person emission*, which the
      `TARGET_SWITCH` counter structurally cannot see. 0 false releases, 0 false reacquisitions,
      0 wrong-person frames outside the deliberately-provoked identical-build crossing.
- [x] **F-21 defect found + fixed (ADR-054)** — a returning owner past `REACQUIRE_WINDOW` was
      rejected 51× as `not_owner`, released, and re-acquired with a spurious `TARGET_SWITCH`.
      Unit suite **36/36 → 46/46** with two regression guards.
- [x] **F-21 real multi-person replay** — `f21_multiperson_replay.py` on 8-dancer footage, margin
      sweep incl. the production-equivalent 141 px: **0 switches, 0 releases at every margin**;
      false rejections begin at ~126 px, i.e. ~11 % headroom.
- [x] **⚠ F-21 silent wrong-person hand-off REPRODUCED AND IMAGED (ADR-056)** — on real two-person
      footage F-21 refused the intruder for 54 frames, then re-locked onto them as the *same*
      `target_id` when they walked into the owner's vacated spot. `TARGET_SWITCH = 0`,
      `TARGET_RELEASED = 0` — invisible to the consumer. **Now FIXED — see the 2026-09-15 block
      below (ADR-057).** ADR-056's premise that only live data could price the fix turned out to be
      wrong: offline ground-truth labelling measured that cost directly.
- [x] **F-22 adversarial suite** — `f22_adversarial.py`, **120/120** across 18 cases derived from
      real frames, each proving raw observation → F-22 → `conf_emit` → the **real**
      `build_body_landmarks` output the consumer receives. No threshold was changed to pass anything.
- [x] **F-22 threshold analysis recomputed in real degrees** — the old §15 table was a mixed-unit
      pixel space. Real vs synthetic evidence reported strictly separately; no combined accuracy
      figure. **0 absolute-angle false rejections on legitimate single-person footage** (435
      frames; 1 single-frame rate-based hold, recovered next frame).
- [x] **F-22 real rejections classified, not counted** — all 12 absolute-angle rejections checked
      against the raw image; cause is multi-person interference (cross-person keypoint fusion /
      occlusion collapse), not single-person noise. Finding: **nothing before F-22 constrains the
      limbs to belong to the same body as the hips.**
- [x] **F-22 performance A/B** — closes §18's own gap: **20.1 µs/frame** (p95 20.2, max 59.8),
      0.060 % of a 33.3 ms frame. End-to-end replay 37.10 → 36.91 fps, reported with its limitation.
- [x] **F-20B empty-room defect found + fixed (ADR-055)** — `frames=` heartbeat was gated behind a
      successful pose emission, so an empty room read as a stuck startup and the supervisor killed a
      healthy sidecar every 45 s → `FAILED_PERMANENT` in ~4 min. Suite **4/6 → 6/6 with no person
      present**.
- [x] **F-20B deployment hardening 35/35** — `f20b_deployment_verify.py`: `run_supervisor.bat`,
      logon-registration procedure (verified statically, deliberately not executed), duplicate guard,
      post-reboot diagnostics freshness, no duplicate sidecars, real session ids.
- [x] **Regression** — Python 176/176 across 5 suites; Unity EditMode **170/170** via a reflection
      runner in the live Editor (the "124-test" figure was an undercount: 124 `[Test]` + 46
      `[TestCase]` = 170).
- [x] **Offline replay soak** — `f2x_replay_soak.py`, labelled `OFFLINE REPLAY SOAK`, explicitly not
      equivalent to a hardware soak. 16 min / 19 clip cycles / **29,595 frames / 0 exceptions**;
      30.83 fps sustained; **memory flat** (+3.5 MB half-mean over the run); 76 `TEMP_LOST` episodes
      all reacquired; **2,581 validator holds, 2,581 recoveries, 0 unrecovered**. Note: the first run
      reported a fake-clean "0.0 MB / +0.0000 MB growth" — an undeclared `ctypes` Win32 call failing
      silently; fixed and re-run before quoting. Detail in F-22 §28b.

---

## ⏳ F-21 — Single-Person Lock / Target Ownership (2026-09-14) — CONDITIONAL

See [`F21_SINGLE_PERSON_TARGET_OWNERSHIP_2026-09-14.md`](F21_SINGLE_PERSON_TARGET_OWNERSHIP_2026-09-14.md)
and ADR-052. `python-sidecar~/target_ownership.py` gates M15's person-box loop by identity (position +
scale continuity — no track id exists in this pipeline, verified from source) so a second person
entering frame cannot silently steal tracking (the F-19 failure).
- [x] State machine + M15 identity-gating integration, `target_events.jsonl` evidence, on-screen HUD
      (state/owner/timers/switch count) on the `--show` preview — `target_ownership.py` +
      `wholebody_udp_sender.py`.
- [x] Unit tests **46/46 PASS** (`test_target_ownership.py`, was 36/36 — two ADR-054 regression
      guards added 2026-09-14) — covers 12/15 of the brief's adversarial cases directly (crossing,
      rapid A/B/A, flicker-never-locks, scale mismatch, boundary timing).
- [x] Real-hardware: clean acquire/lock/emit; **5/5 PASS** sidecar-restart-with-person-present (fresh
      epoch, new SID, zero stale-identity carry-over — `f21_restart_interaction_test.py`);
      `--no-ownership` bit-identical regression check.
- [~] ~~Single-person video regression (`f21_video_replay.py`, 775 frames / ~13s energetic real dance
      motion): **PASS**, zero `TARGET_SWITCH`, zero `TARGET_RELEASED`.~~ **WITHDRAWN 2026-09-14** —
      that clip (`123.webm`) is not single-person; the flat `target_id` across its real person change
      is a silent hand-off, not stability (ADR-056). The single-person claim is now carried by
      `video.webm` (435 frames) and `456.webm`.
- [ ] **Live two-person matrix (blocks PASS)** — two attempts both failed for reasons unrelated to
      correctness: attempt 1 hit a test-script UX defect (instructions unreadable while coordinating
      two people — fixed with an on-screen cue banner); attempt 2 hit a real OAK-D device crash.
      Rerun `python-sidecar~/f21_live_protocol.py` with two people; specifically test the named
      "well-timed handoff" limitation (B stands exactly where A was the instant A leaves).
- [ ] **Follow-up (not blocking):** `f21_live_protocol.py` doesn't detect a dead sidecar subprocess
      and will narrate a full protocol against a crashed process — add a `proc.poll()` check.
- [ ] **Follow-up (not blocking):** `sidecar_supervisor.py` doesn't forward `--show`/`--cue-file` —
      needed to run the next live F-21 session through the supervisor for crash resilience.

---

## ⏳ F-22 — Human Pose Validation / Biomechanical Validation Layer (2026-09-14) — CONDITIONAL

See [`F22_HUMAN_POSE_VALIDATION_2026-09-14.md`](F22_HUMAN_POSE_VALIDATION_2026-09-14.md) and ADR-053.
`python-sidecar~/pose_validation.py` gates elbow/knee bend angle + angular rate (reuses P1-1's
existing `conf_emit=0` "invalid joint" contract, zero `Runtime/`/UDP changes) against F-19's live
evidence (left elbow ~177.5°, right elbow ~179.6° rendered, hands-near-face block).
- [x] Validator (`pose_validation.py`) + sender integration, per-chain localized gating, evidence
      logging (`oak_v4_evidence/f22/`), `--pose-validation`/`--no-pose-validation` (default ON).
- [x] Unit tests **22/22 PASS** (`test_pose_validation.py`) — includes F-19's own measured numbers
      fed directly as fixtures (168° → rejected; knee 176° legitimate-walking max → NOT rejected).
- [~] Offline replay (`f22_video_replay.py`, composes F-21 + F-22 in production order) against
      `video.webm`, 431 owned frames: **0 false absolute-angle rejections**; ~~elbow max
      (155.3°/158.8°) sat within 1–5° of REJECT (160°)~~; 3 momentary rate-based holds (0.7%), each
      recovered within one frame. **The angle figures are SUPERSEDED (2026-09-14)** — that harness
      computes angles in a mixed-unit pixel space. Recomputed in real degrees the same clip's elbow
      maxima are 150.1°/143.5°, i.e. **9.9°/16.5° of headroom**. See `f22_threshold_analysis.py`.
- [ ] **Live L2 session — hands near face (blocks PASS)** — no human was available this session
      (explicit instruction). This is the one test that reproduces F-19's own original defect
      against this validator's raw-geometry threshold. L1/L3–L7 also open, L2 is the named priority.
- [ ] **Follow-up (not blocking):** no CLI threshold-override flags added this pass (unlike
      F-20A/F-20B/F-21's `--*` pattern) — thresholds live only in `pose_validation.py`'s constants.
- [x] ~~**Follow-up (not blocking):** performance impact (fps/latency) not independently re-measured~~
      **DONE 2026-09-14** (`f22_perf_ab.py`): **20.1 µs/frame** added cost (p95 20.2, max 59.8) =
      0.060 % of a 33.3 ms frame; end-to-end replay 37.10 → 36.91 fps, reported with the caveat that
      RTMW3D dominates that second measurement and it does not carry the claim.

---

## ✅ F-20B — Sidecar Supervisor / Watchdog (2026-09-14) — PASS

See [`F20B_SIDECAR_SUPERVISOR_WATCHDOG_2026-09-14.md`](F20B_SIDECAR_SUPERVISOR_WATCHDOG_2026-09-14.md)
and ADR-051. Producer-side complement to F-20A: `python-sidecar~/sidecar_supervisor.py` restarts the
sidecar with backoff/crash-loop protection when it dies (crash, forced kill, camera unplug).
- [x] Supervisor state machine, readiness detection, backoff, crash-loop guard, single-instance lock,
      `supervisor_state.json` diagnostics — `sidecar_supervisor.py` + `run_supervisor.bat`.
- [x] Automated test harness, 6/6 PASS: normal start, forced-kill ×2 (real camera, new SID both
      times), duplicate-supervisor guard, crash-loop → `FAILED_PERMANENT` (throttled, never dies),
      static dependency-failure → clean terminal exit. `f20b_failure_tests.py`.
- [x] **Live session, 2026-09-14** — real USB unplug (sidecar HUNG, not crashed — caught by the
      stdout-heartbeat check, not process-exit detection), camera-absent-at-startup (recovered 36s,
      never hit crash-loop), and a Unity restart while the supervisor/sidecar stayed alive
      (`RestartCount` stayed 0). Avatar recovered live in every case, no Unity restart, no scene
      reload. `python-sidecar~/f20b_usb_test.py --mode live` / `--mode absent`.
- [ ] **Follow-up (not blocking):** consider lowering `--failed-permanent-retry` (60s default) — it is
      now the dominant term in worst-case recovery time once a crash loop trips (report §13).

---

## ✅ Full-body stability program — P0 → P1-3 (2026-09-07 → 2026-09-08) — ALL COMPLETE

Driven by [`AUDIT_FBT_2026-09-07.md`](AUDIT_FBT_2026-09-07.md). ADRs **028–031**. Every stage was
accepted on **measured live evidence**, never on code review alone.

### P0 — confidence-gated stability (ADR-028) — COMPLETE
- [x] **P0-1 `LimbGate`** — per-limb VALID/HELD gate at application time; holds last valid rotation, never substitutes zero. `Runtime/Retargeting/LimbGate.cs`.
- [x] **P0-2** — knees/ankles into the depth-smoothing + hold set; `--arm-max-jump` / `--leg-max-jump` = **0.35 m** for wrist/elbow/knee/ankle (slewed, not dropped).
- [x] **Diagnostic instrumentation** — `recv_log` gained knees/ankles + per-limb confidence + epoch clocks; `model_log` gained gate state, leg bones and **bone lengths**; sidecar gained per-stage timings. All marked `DIAG-ONLY`, additive only.
- [x] **Scripted-occlusion proof** — `inject_occlusion.py` + `verify_gate.py`: **20/20**, 0 zero-rotations across 2765 held frames.
- [x] **Live human acceptance** — gate fired **14×** across all 4 limbs; **0 origin collapse** (250 held rotations); re-acquire jumps **max 15.7°**; wrist/elbow peaks **−67…−76%**; trunk **improved 33–36%**.
- [x] **Squash question settled by measurement** — femur/shin/upArm/foreArm spread **0.000000 m** over 102 968 frames. Correct term is **LIMB ROTATION INSTABILITY**.
- [x] `limbConfidenceThreshold` serialized into `Scenes/Bootstrap.unity` (was relying on the C# initializer).

### P1-1 — per-joint temporal tracking + plausibility (ADR-029) — COMPLETE
- [x] `python-sidecar~/joint_tracker.py` — one reusable `JointTracker` per joint; TRACKED/WEAK/PREDICTED/LOST.
- [x] **FROZEN detector** — the check that actually catches the observed failure (a stuck joint has near-zero residual, speed *and* acceleration).
- [x] 37/37 unit assertions (`test_joint_tracker.py`); **6/6 adversarial cases detected** (`evaluate_p1.py`).
- [x] Cost **0.085 ms** median for 12 joints — 8× under the 1 ms budget.
- [x] Sidecar integration behind `--tracker` / `--no-tracker`.

### P1-2 — frame freshness / throughput (ADR-030) — COMPLETE
- [x] Root cause proven arithmetically: FIFO `get()` + `maxSize=4` at 30 fps = **133 ms** staleness (measured 131.4 ms).
- [x] Latest-frame drain + **timestamp-matched depth** selection.
- [x] Permanent diagnostics: `frameAgeMs`, `queueDepth`, `staleDropped`, `rgbDepthSyncMs`.
- [x] **Live human A/B** — frame age **131.51 → 31.36 ms**; camera→UDP **161.81 → 62.03 ms**; fps and compute unchanged.
- [x] RGB/depth pairing **improved** (audit F-09): max sync error 54.63 → 21.24 ms.
- [x] 180 s soak — **no freshness accumulation**.
- [x] Compute spikes root-caused to **ONNX/DirectML warm-up at frame 0–2**, not queue drain or GC.

### P1-3 — Unity timestamped pose buffer + interpolation (ADR-031) — COMPLETE
- [x] `Runtime/Core/PoseBuffer.cs` — bounded ring of 16; render at `now − poseInterpolationDelayMs`.
- [x] Duplicate / out-of-order / **backwards-timestamp** rejection; **no extrapolation**.
- [x] **Safety rule:** never position-interpolate across an invalid endpoint (would recreate the F-01 origin collapse). Explicitly tested.
- [x] 14 new EditMode tests → **47/47 total**.
- [x] Measured with identical deterministic input (`stream_motion.py`): stutter CoV **2.286 → 1.070 (−53%)**, frozen render frames **41.5% → 23.6%**.
- [x] Delay **40 ms** chosen on evidence (55 ms bought 3 more points for 15 ms more latency).

### Tooling added (reusable)
`inject_occlusion.py`, `verify_gate.py`, `analyze_capture.py`, `guided_capture.py`, `evaluate_p1.py`,
`compare_p12.py`, `stream_motion.py`, `run_p0_acceptance.bat`, `run_p12_ab.bat`.
Audit baseline logs preserved in `python-sidecar~/pipeline_logs_baseline_audit/`.

### Gotchas for the next agent
- **Do NOT run the unfiltered EditMode suite** — it aborts the Editor via a MediaPipe native
  `CHECK failed: 1 == ChannelSize()` (`image_frame.cc:362`) on the prebuild domain reload. Scope runs to
  the `VirtualMirror.Tests` assembly. Not a product defect.
- **`model_log` does not start until the VRM avatar binds (~7 s after Play).** Injecting data before
  that yields *no observations*, which is not a failure. **Absence of observation ≠ evidence of failure.**
- `--inject-load-ms` is a **test-only** instrument; never set it in production.

### Open / next (NOT started)
- [ ] **Recovery: short-gap prediction + blended reacquire** — P1-1's known weak spot (sustained high-confidence teleport → LOST → slow recovery during fast motion).
- [ ] **Palm / wrist rotation** — the L-palm rate limiter saturates (median = p95 = 14.98° vs a 15°/frame cap); hands also sit ~40 ms off the body timeline after P1-3.
- [ ] **Foot / ground constraint** — audit F-10, never implemented.
- [ ] **Confidence normalisation** — audit F-08; P1-1 works around it kinematically rather than fixing the scale.

---

## ⚠️ Full-codebase audit fix backlog (2026-08-07)

A 5-track read-only audit found the recurring bugs stem from **duplicated responsibility across the
Unity app and the Python sidecar** (mirror ×2, smoothing ×2, uprightness ×3, config ×2) plus a dead IK
path. Full report + `file:line` + fixes: [`AUDIT_2026-08-07.md`](AUDIT_2026-08-07.md). Verification
plan: [`AUDIT_TESTPLAN_2026-08-07.md`](AUDIT_TESTPLAN_2026-08-07.md). Tracked as session tasks
(H1–H7, M-CONSOLIDATE, M1/M4/M5/M7–M18, LOW-A..D, TEST).

Fix order: (1) single-owner consolidation of mirror/smoothing/uprightness → (2) H7+M12 finger twitch →
(3) H6 leg gate → (4) H3+M4+M8 hot-swap stability → (5) H1+H2 diagnostics/persistence →
(6) H4/M5/M9/M10 leak/race/teardown → (7) LOW batches via the consolidation refactors.

**Progress:** ✅ **ALL AUDIT CODE FIXES COMPLETE (2026-08-07 session 2, Unity MCP on port 6400).** Every
H/M/LOW item is implemented + **compile-verified 0 errors/0 warnings + EditMode 21/21 + sidecar
`py_compile` clean + M16 bench**. Session-1 wrote the Unity C# blind (no MCP); session-2 fixed the
`SharedUp` compile blocker, then finished the REMAINING items (H2 config single-source via dead-schema
removal, H5 IK toggle, M1 lazy face/hand toggles, M10 filter-dt gate, M16 zrel fallback, and the
LOW-A/C/D remainders), re-verifying compile + tests after each batch. Mirror single-owner is
resolved-by-design (Unity owns; sidecar `--mirror` off). **Only PLAY verification (OAK-D + eyes on the
avatar) remains — the user's, per `AUDIT_TESTPLAN_2026-08-07.md` §B PLAY + §C.**

**Post-audit test-video fixes (2026-08-07):** user PLAY test surfaced 3 issues, all fixed + logged as
ADRs so they don't recur — **waist twist** (spine yaw now from the hip line, ADR-019), **jitter**
(sidecar smoothing strengthened, single-owner kept, ADR-020), **wrist** (re-enabled on OAK with
smoothing + live weight, ADR-021). Round 2: **wrist roll-free** (ADR-021 amend), **beta up for
responsiveness** (ADR-020 amend), back-facing twist = single-camera limit (ADR-019). Compile 0/0 +
EditMode 21/21 + sidecar py_compile clean.

**Kalidokit port — the 70%→95% path (2026-08-07, ADR-022):** the persistent forearm spin is vector-FK's
guessed limb ROLL (not the hand, not the engine). Decision (user-chosen): stay on Unity, replace the
retarget math with a C# port of **Kalidokit (MIT)** which derives roll from limb geometry. **WHOLE BODY ported** (arms + hips/spine +
legs) and applied via UniVRM's **normalized control rig** so ONE global convention works for every bone
(no per-bone guessing). Behind **`useKalidokitBody`** (set before Play; FK/IK/hand-curl bypassed when on).
Compiles 0/0, EditMode 27/27 (6 KMath tests). ⏳ USER live check: set `useKalidokitBody` before Play →
confirm the avatar moves (if T-pose, it's UniVRM Process ordering, fixable) → tune the one convention
(`kalidokitBodyFlipQuat` 0..3 + `kalidokitBodyEulerSigns`, Inspector-live). Next: HandSolver → control-rig
fingers. See ADR-022 + `ai_handoff.md`.

---

## Character rig template + spec + validator (2026-08-10) — DELIVERED

Reverse-engineered the VRM 1.0 rig contract from the pipeline so artists build avatars that track with zero
per-avatar tuning. See [`ai_handoff.md`](ai_handoff.md) "🎭 Canonical VRM 1.0 rig template".

- [x] **Spec doc** [`docs/27_CharacterRigSpec.md`](27_CharacterRigSpec.md) — VRM 1.0 + T-pose + facing rules,
  bone tables (17 core/limb + 30 fingers + 7 optional), required expressions, Blender workflow, validation,
  limits. Indexed in `README.md` (rows 26+27).
- [x] **Blender generator** `tools/blender/build_rig.py` — headless; builds 54-bone T-pose skeleton +
  placeholder skinned mesh + expression shape-key slots + humanoid mapping + expression binds → exports
  VRM 1.0 to `tools/blender/output/`. Installed VRM Add-on for Blender v4.5.0 (MIT) headless into Blender 4.5
  (release CDN TLS-blocked → installed from `git clone` of the source).
- [x] **Import validator** `Editor/VrmRigValidator.cs` (+ `VirtualMirror.Editor.asmdef`) — Menu
  "Virtual Mirror → Validate VRM Rig"; parses the `.vrm` glb JSON (VRMC_vrm) → PASS/FAIL + missing
  bones/expressions. ⏳ needs a Unity compile-check (MCP unbound this session).
- [x] **Verified** generated VRM by glb-JSON parse: spec 1.0, 54 bones (0 missing, all 30 fingers, all
  optionals), 6 required expressions bound. Copied placeholder to `StreamingAssets/Avatars/VirtualMirrorRigTemplate.vrm`.
- [ ] **In-app load-test** — pending Unity MCP (restart to bind) OR manual Play → Tab → pick the template.

---

## Now (M0)

- [x] Create Runtime/Editor/Tests folder scaffold per `02_ProjectStructure.md`  
- [x] Add asmdefs (`VirtualMirror.Core`, `.IO`, `.Settings`, `.App`) — see ADR-009  
- [x] Create `Bootstrap.unity` + `Mirror.unity` (Mirror = Camera + Directional Light + AvatarRoot; Bootstrap = AppBootstrap). Both in Build Settings (Bootstrap[0], Mirror[1])  
- [x] Implement `PathProvider` + `SettingsStore` (schema v1) — verified: writes `Documents/MyMirror/Settings/settings.json`  
- [x] Implement `LogService` rolling daily file — verified: `Logs/virtual-mirror-YYYYMMDD.log` with session header  
- [x] URP present in base project (`com.unity.render-pipelines.universal` 17.3.0) — renderer-asset assignment still to confirm in Graphics settings  
- [x] Pin UniVRM (`com.vrmc.vrm` 0.126.0 via OpenUPM) + Animation Rigging (`com.unity.animation.rigging` 1.4.1) in manifest — recorded in `19_ThirdParty.md §1a`. MediaPipe pin deferred to M2.  

**M0 verified:** Play-tested — `AppBootstrap` wires services, loads settings, additive-loads Mirror. 0 compile errors, 0 runtime errors.  
**Feature asmdefs deferred:** `.Tracking`, `.Retargeting`, `.IK`, `.Avatar`, `.UI`, `.Editor`, `.Tests` — add when their code lands (avoid empty assemblies).  

---

## Now (M1 — in progress)

- [x] `IAvatarLoader` + `UniVrmAvatarLoader` (VRM 1.0, `Vrm10.LoadBytesAsync`, humanoid validation, size gate) — compiles clean  
- [x] Core avatar contracts: `IAvatarInstance`, `AvatarLoadRequest/Result/Status` + `VrmAvatarInstance`  
- [x] Avatar swap without restart + persist last path — `AvatarSessionController` (load-new-then-dispose-old, keep-old-on-fail, persists `avatar.lastPath`)  
- [x] Bootstrap wiring: loader in `ServiceRegistry`; session built on Mirror `sceneLoaded`; auto-loads last avatar. Play-verified (no-op when no last path, 0 errors)  
- [x] Avatar UI code (uGUI + **TextMeshPro**, path-input for now): `IAvatarSession` (Core) + `AvatarLibraryPanel` (`VirtualMirror.UI` asmdef, refs `Unity.TextMeshPro`) + bootstrap wiring — compiles clean  
- [x] **Canvas built in Mirror.unity** (MirrorCanvas + EventSystem + TMP `PathInputField` + `LoadButton` w/ TMP label + TMP `StatusText`), `AvatarLibraryPanel` refs wired. Play-verified: "Avatar UI wired."  
- [x] **Live load test PASSED** with `sampleVRMFiles/StrawberryPrincess.vrm`: auto-load → UniVRM parse → humanoid validated → parented under AvatarRoot → rendered. "Avatar loaded: StrawberryPrincess.vrm", 0 errors. Screenshot confirmed avatar + TMP UI.  

Sample VRMs live in `C:\Unity\viitorx-vrm-avtar-unity-base-project\sampleVRMFiles\` (from hub.vroid.com): StrawberryPrincess, VIPE_Hero, Skull, + 2 numbered.  
- [ ] File browser: swap the path field for a native picker later (StandaloneFileBrowser or Win32) behind a small interface  
- [ ] Built-in avatar slots under StreamingAssets (blocked: need licensed `.vrm` files)  

Decisions locked: async = **Unity Awaitable** (ADR-006); UI = **uGUI**; file input = **absolute path for now**.  
New assemblies: `VirtualMirror.Avatar` (Core + `VRM10` + `UniGLTF`), `VirtualMirror.UI` (Core + `UnityEngine.UI`). App refs both.  

---

## Now (M2 — in progress)

- [x] `ICameraCapture` (Core) + `CameraDeviceInfo`/`CameraCaptureRequest` + `VirtualMirror.Camera` asmdef  
- [x] `WebcamCaptureService` (WebCamTexture) — for later; `useVideoSource=false` toggle  
- [x] `VideoFileCaptureService` (VideoPlayer → RenderTexture) — **current source** = `sampleVRMFiles/sample video.mp4`. Play-verified: video renders in `CameraPreview` corner, 0 errors  
- [x] `CameraPreviewController` (RawImage) + bootstrap wiring (`useVideoSource` picks video/webcam)  
- [x] Core tracking contracts: `IBodyTrackingProvider`, `PoseFrame`, `JointId` (33 BlazePose), `PoseLandmark`  
- [x] `OneEuroFilter` + `JointFilterPipeline` (per-axis, 33 joints) — `VirtualMirror.Tracking`  
- [x] `FakeBodyTrackingProvider` (synthetic waving arms) — drives the pipeline now  
- [x] `RotationFromVectors` + `HumanoidArmRetargeter` (vector FK, arms) — `VirtualMirror.Retargeting`  
- [x] **Pipeline verified live:** fake pose → filter → retarget → avatar arm bones. `animatorEnabled=False` (bound), arm bones driven, 0 errors. Loader now uses `ControlRigGenerationOption.None` so bones are directly drivable  
- [x] **MediaPipe Pose provider LIVE** — `MediaPipePoseProvider : IBodyTrackingProvider` (homuler, ADR-010). Full model, CPU, VIDEO mode, `modelAssetBuffer` (no ResourceManager), `TextureFramePool` → `TryDetectForVideo` → `poseWorldLandmarks` → Unity space. Play-verified: bones driven frame-to-frame by the sample-video person, 0 errors. Model at `Assets/StreamingAssets/MediaPipe/pose_landmarker_full.bytes` (9.4 MB, Google official). Swap via `AppBootstrap.useMediaPipeTracking`; falls back to fake if start fails  
- [x] **Configurable axis mapping** — `PoseSpaceConverter` (Core, per-axis flip), wired via `AppBootstrap.poseFlipX/Y/Z`. Defaults `flipX=false, flipY=true, flipZ=true` (mirror-correct by reasoning + matches the mapping that visibly tracked). Final mirror sign = live Inspector toggle  
- [x] **Full-body retarget** — `HumanoidPoseRetargeter` (spine, neck, arms, legs; generic segment list; sets world rotation so limbs stay correct as parents move) + **confidence gate** (segment held if any endpoint < `retargetMinConfidence` 0.5). Verified: arms driven by MediaPipe, spine/legs correctly held on the upper-body sample video, 0 errors  
- [x] **Roll fixed** — `RotationFromVectors.Delta` now builds rest+target orientations via `LookRotation(dir, referenceUp)` (referenceUp = world forward) and returns their delta, pinning twist. `SafeUp` handles the degenerate parallel case  
- [x] **Hips driven** — `HumanoidPoseRetargeter` rotates the Hips root from a basis (hip line `LeftHip-RightHip` + torso up `midShoulder-midHip`), delta vs the avatar's rest basis (upper-leg line + neck-up). Verified: hips rotation tracks the person; applied before limbs. Legs correctly inherit/gate on the upper-body video (leg world rot == hips rot). No NaN/explosion  
- [ ] **Hips position** NOT driven — MediaPipe world landmarks are hip-centred (origin at mid-hip) so give no absolute translation; add later from image landmarks / depth (avatar stays in place = fine for a mirror)  
- [x] **Confidence hysteresis + stale-hold** — retarget gate has enter/exit thresholds (`retargetMinConfidence` 0.5 / `retargetExitConfidence` 0.3) per segment + torso; `AppBootstrap.IsPoseStale` holds the pose if frames stop arriving (`poseStaleSeconds` 0.5). Outlier reject still TODO  
- [x] **`MirrorCameraController`** (`VirtualMirror.Rendering`) — frames the avatar to renderer bounds (fits any height; solves the tiny-chibi problem). Called on `IAvatarSession.AvatarChanged`  
- [x] **Hide load UI on avatar load** — `AvatarSessionController` fires `AvatarChanged` (manual + auto); `AvatarLibraryPanel` hides input/button/status on it; **Tab** toggles it back  
- [x] `AnimationRiggingIkDriver` (hand/foot IK targets) — ADR-002; implements `IIkSolver` using `TwoBoneIKConstraint` for arms & legs, `RigBuilder`, and `Rig`. Play-tested & verified  
- [x] **Webcam Input Integration & Mirror Calibration** — `AppBootstrap.useVideoSource=false` wired to live webcam (`WebcamCaptureService` initialized device `Logi C270 HD WebCam` 1280x720@30fps). `poseFlipX=true` finalized for true mirror reflection (right hand motion maps to facing avatar's right hand). Play-tested & verified
- [x] **Perf:** MediaPipe CPU inference offloaded to background thread (`ThreadPool`) with frame-dropping and double-buffering. Main thread remains completely unblocked during tracking. Play-tested & verified  
- [x] **EditMode unit tests** (`VirtualMirror.Tests` asmdef): Y-flip/mirror (`PoseSpaceConverter`), no-NaN on missing landmark & parallel vectors (`RotationFromVectors`), One-Euro smoothing (`OneEuroFilter`) — 13/13 passed  

### Audit fixes (2026-08-05)
- **Waist twist FIXED** — Hips + Spine now dual-basis + **calibration-relative** (tracked neutral captured on first confident frame; `C` recalibrates). Torso straight at neutral regardless of handedness. Supersedes the earlier avatar-geometry hips basis + world-forward roll noted above.
- **Threading race FIXED** — `MediaPipePoseProvider`: worker does only `TryDetectForVideo`; main thread owns `TextureFramePool`+`Image` (in-flight, released next tick when worker idle).
- **Per-frame GC removed** — `PoseFrame` is a reusable mutable buffer; fake provider, `JointFilterPipeline`, and the (double-buffered) MediaPipe provider fill in place → zero per-frame `PoseFrame`/array allocs.
- **Limb roll → body-forward** — roll reference is per-frame body-forward (from the torso basis), so arm/leg twist follows the body facing.
- **Animator kept enabled** — the IK path (Animation Rigging) requires it; FK writes in LateUpdate with no AnimatorController so nothing overwrites, IK layers on top.

### IK + Face audit — FIXED + play-verified (2026-08-06, 7 tasks)
Audited the parallel session's IK + face code, then fixed all 7. **Design decision locked & implemented:** arms & legs are IK-driven; the FK retargeter drives ONLY torso (hips/spine) + neck — mutually exclusive, never both on one bone. Verified live (sample video → StrawberryPrincess auto-load, 0 compile/runtime errors, waist straight: hips↔spine 0.0°, hips→spine 2.0° off world-up).

**IK — `Runtime/IK/AnimationRiggingIkDriver.cs` (+ `AppBootstrap.UpdateTracking`, `HumanoidPoseRetargeter`, `IIkSolver`):**
- [x] **1. FK/IK conflict** — FK now skips arm/leg segments when IK owns them. `HumanoidPoseRetargeter.SetArmsLegsDrivenExternally(bool)` + per-segment `IsLimb` flag (Neck stays FK). `AppBootstrap` computes `ikActive = useIkDriver && ikSolver.IsBound`, sets the FK flag, and `IIkSolver.SetActive(useIkDriver)` (zeros constraint weights when off so the runtime IK toggle actually works both ways). **Verified:** `FK.armsLegsDrivenExternally=True`, `IK.active=True`, left/right arm constraint `weight=1.00`.
- [x] **2. IK target scale** — `AnimationRiggingIkDriver` measures avatar arm/leg lengths at `Bind` (per side, `UpperArm→LowerArm→Hand` etc.) and each frame scales the landmark offset by `avatarLen / trackedLen` before `origin + offset`. **Verified:** avatar left-arm length 0.462 m; hand-target distances from hips 0.435 / 0.603 m (avatar-scaled, reachable — not raw ~1.7 m human metres).
- [x] **3. `targetRotationWeight = 1` → 0** on all four TwoBoneIKConstraints (position-only IK; wrist/ankle orientation not tracked). **Verified:** all 4 constraints report `rotW=0.0, posW=1.0`.

**Face — `Runtime/Tracking/MediaPipe/MediaPipeFaceProvider.cs` (new), `MediaPipeGlobalInit.cs` (new), `VrmExpressionRetargeter.cs`, `Core/Models/FaceFrame.cs`, `FakeFaceTrackingProvider.cs`, `AppBootstrap`:**
- [x] **4. Real face tracking** — `MediaPipeFaceProvider : IFaceTrackingProvider` (homuler `FaceLandmarker`, CPU/VIDEO, `modelAssetBuffer`, `outputFaceBlendshapes:true`), mirrors `MediaPipePoseProvider` threading exactly (background worker does only `TryDetectForVideo`; main thread owns `TextureFramePool`+`Image`; double-buffered reusable `FaceFrame`). Blendshape → FaceFrame map: `eyeBlinkLeft/Right→BlinkLeft/Right`, `jawOpen→MouthOpen`, `max(mouthSmileLeft,Right)→Smile`, `max(browDownLeft,Right)→Angry`, `max(browInnerUp,eyeWideLeft,Right)→Surprised`. Wired behind `AppBootstrap.useMediaPipeFace` (fake fallback if model/init fails). Model `StreamingAssets/MediaPipe/face_landmarker.bytes` (3.67 MB float16, Google official). New shared `MediaPipeGlobalInit` (refcounted Glog init/shutdown) prevents the double-init native crash now that two providers init MediaPipe. **Verified:** `faceProvider=MediaPipeFaceProvider`, `faceFrame(valid=True, blinkL=0.01…)` — real face detected in the video, buffer filled in place.
- [x] **5. Missing-expression guard** — `VrmExpressionRetargeter` captures the VRM's registered `ExpressionKeys` at `Bind` and guards every `SetWeight`; missing keys skipped + one Warning logged per missing key (SDS-008 §5). **Verified:** error-free (StrawberryPrincess has all 6 mapped keys: blinkLeft/Right, aa, happy, angry, surprised).
- [x] **6. Reset on stop/invalid** — weights reset to 0 (neutral) on `Unbind`, on invalid/`!IsValid` face frame, and when `useFaceTracking` is toggled off (`AppBootstrap` calls `Apply(null)`). **Verified:** applied 1.00 → invalid frame → 0.00.
- [x] **7. `FaceFrame` per-frame GC** — `FaceFrame` is now a reusable mutable buffer (`SetExpressions`/`SetMeta`/`MarkInvalid`, mirroring `PoseFrame`); fake + real providers fill in place, zero per-frame allocation. Old 8-arg ctor removed; `VrmExpressionRetargeterTests` updated.

- [x] **8. Real hand tracking** — `MediaPipeHandProvider : IHandTrackingProvider` (homuler `HandLandmarker`, CPU/VIDEO, `numHands:2`, `modelAssetBuffer`), mirrors the pose/face providers exactly (background worker `TryDetectForVideo`; main-thread `TextureFramePool`+`Image`; double-buffered reusable `HandFrame`; shared `MediaPipeGlobalInit`). Reduces the 21 world landmarks/hand to per-finger curl (0..1) from the bend angle at each finger's middle joint (`angle/160°`), mapped to left/right by MediaPipe handedness label. Wired behind `AppBootstrap.useMediaPipeHand` with fake fallback. Model `StreamingAssets/MediaPipe/hand_landmarker.bytes` (7.6 MB float16, Google official). `HandFrame` made a reusable buffer (`SetCurls`/`SetMeta`/`MarkInvalid`); fake + real fill in place; `HumanoidHandRetargeterTests` updated. **Verified live** (video → StrawberryPrincess): `handProvider=MediaPipeHandProvider`, retargeter bound (15 joints/hand), valid frames with real per-finger curls that vary frame-to-frame (e.g. R=[0.14,0.45,0.54,0.45,0.41], L=[idx 0.15, mid 0.25]) mapped to correct L/R by handedness; 3 MediaPipe landmarkers (pose+face+hand) coexist via the shared refcounted `MediaPipeGlobalInit` with no double-init crash; 0 runtime errors.

Capture source swaps via `AppBootstrap.useVideoSource` (video now, webcam later). Tracking provider swaps via `StartTracking` (fake now, MediaPipe after plugin).  
New assemblies: `VirtualMirror.Tracking` + `VirtualMirror.Retargeting` (both ref Core). App refs both.  

---

## Now (M3 — completed)

## Now (M4 — completed)

- [x] **Diagnostics HUD** (`PerformanceMonitor`, `DiagnosticsHudPanel`) — real-time FPS counter, frame time, tracking mode, active webcam device, and avatar status. Play-tested & verified
- [x] **Calibration & Settings UX** (`CalibrationSettingsPanel`) — uGUI/TMP controls for WebCam device selection, mirror flip toggle, IK toggle, face/hand tracking toggles, and filter cutoff/beta smoothing sliders. Play-tested & verified
- [x] **Unit tests** (`VirtualMirror.Tests` asmdef) — `PerformanceMonitorTests` added and verified (21/21 EditMode unit tests passed)

---

## Now (M5 — Release Candidate)

- [x] **Built-in Avatar Library (`StreamingAssets/Avatars/`)** — 5 sample `.vrm` avatars (`StrawberryPrincess`, `VIPE_Hero`, `Skull`, `4897454821417733844`, `5227260540573173135`) copied into `StreamingAssets/Avatars/` and dynamically enumerated in `AvatarLibraryPanel` dropdown.
- [x] **Native Windows File Picker (`NativeFileDialog`)** — Win32 `GetOpenFileName` open file dialog integrated with `Browse...` button for 1-click custom `.vrm` avatar selection.
- [x] **Dance Video Test Benchmark (`StreamingAssets/Videos/sample video.mp4`)** — dance test video integrated into `StreamingAssets/Videos/` for automated full-body pose retargeting play-testing.
- [x] **ThirdPartyNotices.txt** — License notices compiled for UniVRM, MediaPipe, homuler, Animation Rigging, and OneEuroFilter.

---

## Quality & Accuracy (post-M5)

Root cause of the recurring head-pitch / wrist / folded-leg / jitter issues: MediaPipe is **monocular 2D + weak-Z** (no reliable depth or true limb rotation), so rotation is reconstructed from vectors. Stack (Unity 6 + homuler MediaPipe + UniVRM + Animation Rigging + URP) is correct for V1; the accuracy ceiling is the tracking model. NOTE: MediaPipe runs **CPU** here — the RTX 3060 is idle, and homuler's GPU delegate on Windows is unreliable, so accuracy gains come from changing the **model**, not a flag.

### Accuracy strategy — ADR-014 (staged: cheap wins → GPU 3D). Triggered by a live webcam test showing ~20–30% replication (see ADR-014 / ai_handoff "Live webcam accuracy test").

### Cheap wins (current stack, high ROI)
- [x] **Pose model Full → Heavy** — ✅ 2026-08-06: downloaded `pose_landmarker_heavy.bytes` (29 MB, Google official) to `StreamingAssets/MediaPipe/`; `AppBootstrap.poseModelFileName` + `Bootstrap.unity` point at it. NOTE: heavier CPU inference → may lower tracking framerate; compare vs `pose_landmarker_full.bytes` if it feels laggier.
- [x] **Filter live-tunable + responsive default** — ✅ 2026-08-06: `OneEuroFilter.SetParameters` + `JointFilterPipeline.SetParameters`; calibration sliders now retune the live filter (they were dead before); default `filterBeta` 0.02 → 0.2 to cut lag. Raise Beta slider for more responsiveness, lower for less jitter.
- [ ] **⭐ FRAMING (free, biggest win) — user stands closer / upper-body.** At full-body distance the hands are too small for the Hand Landmarker (dead fingers/wrist) and body landmarks are noisy. Closest single improvement; no code.
- [ ] **Implement calibration (T-pose scale/offset)** — SDS-025 §5 / SDS-005 §7, still TODO (no `Runtime/Tracking/Calibration/` code exists yet). Deferred behind the framing+model+filter re-test (ADR-014): the live-test failures are dominated by distance-noise + lag, not proportion, so re-test first before building this.
- [ ] **Camera 1080p60** — Logi C270 caps at 720p30, so this needs a better webcam; raise `cameraWidth`/`cameraHeight`/`cameraFps` once hardware allows.
- [ ] **Outlier rejection** — velocity-based reject (SDS-025 §4 TODO) to cut the leg/limb jitter seen while standing still.
- [~] **Play-verify head/legs/arms (ADR-011/012)** — ✅ DONE 2026-08-06 (Unity MCP, port 6400): 0 errors; neck undriven (head forward, up-vector ≈ world-up); all 4 IK constraints `rotW=0/posW=1`; arms follow the waving-arms fake pose from hip level to `+0.81` above the shoulder (not collapsed). See `ai_handoff.md` "Live play-verification".
- [ ] **Tune `wristRotationWeight` (ADR-013) live** — still pending: needs a webcam pass with hands in upper-body framing (real MediaPipe hand detection). Default 0.7; **C** recalibrates neutral upright; 0 disables (fingers still curl). Not exercisable from the full-body dance video or the fake provider.

### Higher accuracy — GPU 3D-pose path (uses the RTX 3060, the real jump) — ADR-015, IN PROGRESS
- [x] **Sentis feasibility spike** — installed `com.unity.ai.inference` 2.6.1; RTMW3D-x imports as `ModelAsset` with 0 warnings (opset 17, all-standard ops); I/O confirmed (input 1×3×384×288; SimCC x576/y768/z576). Sentis path confirmed, no ORT fallback. See ADR-015.
- [x] **Build `SentisPoseProvider : IBodyTrackingProvider`** — DONE + compiles clean (0 errors). `Runtime/Tracking/Sentis/SentisPoseProvider.cs`; Tracking+App asmdefs +`Unity.InferenceEngine`. GPU inference on `BackendType.GPUCompute`, **async readback** (`ReadbackRequest`/`IsReadbackRequestDone` + frame-drop, no main-thread stall), SimCC argmax÷2.0 decode, 133→33 `JointId` (COCO-WholeBody body+feet subset), hips-centred via `PoseSpaceConverter`, double-buffered `PoseFrame`. Wired `AppBootstrap.useSentis3dTracking` (off) + `sentisModel`/`sentisImageNetNorm`/`sentisMetreScale` + MediaPipe fallback.
#### Sentis 3D — tuning backlog (Run-5 findings: live-tracks but motion under-responsive)
- [x] **Fix z-depth decode (highest impact)** — ✅ DONE 2026-08-06: got the exact mmpose `SimCC3DLabel` decode (see ADR-015). `DecodeFrame` now decodes z as ROOT-RELATIVE METRIC depth `z_metric = (zIndex/(ZBins/2) − 1) × ZRange` (ZRange=2.1744869), centred at 0 — not a pixel. z magnitude routed via a separate **`sentisDepthScale`** (default 0.4), independent of `sentisMetreScale`.
- [x] **Reduce smoothing for the 3D path** — ✅ DONE: when the body provider is `SentisPoseProvider`, `AppBootstrap` applies a looser One-Euro profile (`sentisFilterMinCutoff` 1.5 / `sentisFilterBeta` 0.6) so fast dance isn't damped. Still slider-tunable live.
- [x] **Person crop to 3:4** — ✅ DONE: `SentisPoseProvider.BuildInput` centre-crops the largest 3:4 region (UV scale/offset on the blit) when `sentisPersonCrop` (default true), so the top-down model gets a tight, undistorted person box instead of a stretched 16:9 frame.
- [~] **Per-axis sign + scale tune** — knobs in place (`poseFlipX/Y/Z` + `sentisMetreScale` + `sentisDepthScale`). Run-7: default `sentisDepthScale` raised **0.4 → 0.8** to fix "elbow doesn't bend" (depth-compressed elbow collapses onto the shoulder→wrist line → rigid straight arm; confirmed via dancer-vs-avatar zoom it's the pipeline, not the video). `sentisMetreScale`/`sentisDepthScale` are now **live-tunable during Play** (`SentisPoseProvider.SetTuning` pushed from `AppBootstrap`). Still needs a live tuning pass (elbow depth 0.8→1.0+ if shallow; mirror via `poseFlipX/Z`).
- [x] **HUD label from active provider** — ✅ DONE: `AppBootstrap.DescribeActiveTracking()` shows "Sentis RTMW3D (GPU)" / "MediaPipe Pose (CPU)" / "Fake Tracking" from the actual `bodyProvider` type.
- [x] **Person-box tracking (Run-8, top-down bbox stage)** — ✅ webcam tracked poorly (full-body model + upper-body framing + arm-clipping centre-crop → gated to rest). Added keypoint-driven person-box tracker in `SentisPoseProvider` (`ComputeCropRect`/`UpdatePersonBox`): crop follows the person from the previous frame's keypoints (+margin 0.35, smoothed), adapts to any framing (upper/full body), no extra detector NN. Compiles clean. ⏳ needs live webcam validation; tune `sentisMetreScale` DOWN (person fills frame now), flip `poseFlipZ` if limbs bend behind. A detector NN is the fallback only for multi-person / hard re-acquisition.
- [x] **`sentisDepthScale` default 0.4→0.8 + live-tunable (Run-7)** — elbow-bend fix; `sentisMetreScale`/`sentisDepthScale` pushed live from `AppBootstrap` (Inspector-tunable during Play).
- [x] **Waist-bend rigid tilt → FIXED (Run-9) + ✅ USER-CONFIRMED WORKING** — Hips + Spine shared `up=midShoulder−midHip` → forward bend tilted both together. `BasisBone.StableUp`: Hips now use vertical up (yaw/side-lean only, stay upright), Spine keeps torso-up (carries bend). `HumanoidPoseRetargeter.cs`. Applies to the FK torso path (both MediaPipe & Sentis).
- [~] **Hands behind body (Sentis/MediaPipe RGB)** — **CLOSED as won't-fix-on-RGB (2026-08-07):** this is the inherent monocular front/back depth ambiguity of a single camera; no RGB-only tweak resolves it (toggling `poseFlipZ` doesn't — it's not a constant sign error), only palliatives (`sentisDepthScale`↓, temporal z-smoothing/outlier clamp). The real fix is the depth camera — that is exactly what the OAK-D B2 path (ADR-016) + Phase-2 measured depth ([`docs/26_OakDDepthPhase2.md`](26_OakDDepthPhase2.md)) address. Superseded by the OAK-D task; do not spend more effort on the RGB paths here.
- [ ] **Close-framing weakness** — top-down model + hip-centring need the hips in frame; guide users to half/full-body framing. Person-box can't recover an out-of-frame pelvis.
- [ ] **⏳ LIVE-VERIFY (blocked on user real-time Play):** the Run-5 fixes compile clean (0 errors) but **motion can't be confirmed headless** (VideoPlayer doesn't advance under `EditorApplication.Step`; real-time freezes when Unity unfocused). Needs a **focused real-time Play** (Sentis on, dance video or webcam): confirm bigger arm/leg reach + depth. Then live-tune `sentisDepthScale` (raise if depth still muted), `sentisMetreScale` (overall amplitude), `poseFlipX/Y/Z` (mirror/handedness), and the smoothing sliders.
- [~] **Live-validate + tune** — ✅ **decode + live pipeline VALIDATED** (2026-08-06, video source): on a real frame both norm modes give anatomically-correct keypoints (ImageNet norm higher-conf → kept); live in-pipeline `SentisPoseProvider` emits valid hip-centred 3D (hips@origin, nose y+0.47, ankles y−0.50, Y-up, L/R x-split, conf 0.80–0.94) and the avatar is driven (posed, not T-pose), 0 errors. ⏳ **Still TODO:** frame-to-frame **motion** couldn't be confirmed headless (VideoPlayer doesn't advance under `EditorApplication.Step`; real-time run freezes when Unity unfocused) → needs a **real-time focused Play** by the user. Then tune: z-depth `sentisMetreScale` (~1.0 m span now, a bit short); mirror/axis via `poseFlipX/Y/Z` for the avatar convention; root/hip grounding (stop float); velocity outlier-reject (leg jitter). Fix stale HUD label ("MediaPipe Pose (CPU)" shown while Sentis is active).
Stack chosen: **Unity Sentis** `com.unity.ai.inference` 2.6.1 (INSTALLED ✅, 0 errors), model **RTMW3D** (RTMPose3D family, whole-body 3D incl. hands, Apache-2.0). POC → non-commercial licenses OK.
- [x] **Install Sentis** (`com.unity.ai.inference` 2.6.1) — namespace/assembly `Unity.InferenceEngine`. Build clean.
- [~] **RTMW3D feasibility spike (NEXT — resume here):** ONNX downloaded + verified (`Soykaf/RTMW3D-x`, 369 MB, 384×288). **Import into Sentis** (regular `Assets/` folder) and read the import log for unsupported operators. Clean → Sentis; rejected → **ONNX Runtime (DirectML)**. See ADR-015 for URL + decode.
- [ ] **Build `SentisPoseProvider : IBodyTrackingProvider`** — camera→`TextureConverter.ToTensor`(384×288)→`Worker.Schedule`(GPUCompute)→readback→**SimCC decode**→133 keypoints→our 33 `JointId` (Unity hips-centred m). Tracking asmdef + `Unity.InferenceEngine` ref. Wire behind `AppBootstrap.useSentis3dTracking` (default off) + MediaPipe fallback. Live-validate/tune (like `poseFlipX`).
- [ ] **Bonus from RTMW3D:** whole-body includes **hands + face** → could replace the separate MediaPipe hand/face providers and fix dead-hands-at-distance.
- [ ] **Also:** root/hip grounding (stop the avatar floating), velocity outlier-reject (leg jitter seen while standing).
- [ ] **Direct rotational retarget from 3D** — feed true 3D limb rotations, dropping most IK reconstruction (fixes head pitch, wrist, depth at the source, not as symptoms).
- [ ] **Per-joint finger + wrist from 3D hand** — replace the single mid-joint curl heuristic (SDS-011 curl) with per-joint angles + real wrist pose; supersedes the ADR-013 delta-from-neutral wrist hack.
- [~] **⭐ OAK-D depth-camera — B1 (in-Unity plugin) ABANDONED (too crash-prone), pivoted to B2 (Python sidecar over UDP) — BUILT, awaiting user test (ADR-016).** B2: `oak_sidecar/venv` (depthai 2.32) + `depthai_blazepose` + `udp_pose_sender.py` (BlazePose on OAK → 33 landmarks over UDP:8899); Unity `OakDUdpPoseProvider` (no native DLL → can't crash Unity) behind `AppBootstrap.useOakUdpTracking`. Phase-1 = GHUM depth (≈MediaPipe, stable); Phase-2 = per-keypoint depth-map sampling (the real front/back fix, hardware-in-the-loop). **✅ 2026-08-07: B2 COMPILE-VERIFIED in Unity 6.3 (0 CS errors) + reviewed AGENTS.md-clean; B1 dead code REMOVED** (deleted `OakDPoseProvider.cs` + all `AppBootstrap` B1 wiring/fields; 81 MB native plugins already gone). **Also 2026-08-07: B2 provider hardened** (lock-safe `TryGetLatestFrame`, `ReceivedCount`/`ParseErrorCount` + periodic `rx=…/parseErr=…` console line for live bring-up) and **Phase-2 fully spec'd → [`docs/26_OakDDepthPhase2.md`](26_OakDDepthPhase2.md)**. **✅ 2026-08-07: PHASE-1 PROVEN LIVE ON HARDWARE (OAK-D attached).** Repaired the broken sidecar venv (missing `Scripts/` → recreated in place with Python 3.10, site-packages intact); device `14442C10F143D3D200` detected; sidecar streams BlazePose over UDP (default `--frame_height 200`; 400 errors on this OV9782 — comment fixed); Unity `useOakUdpTracking=true` saved, Play-verified `OAK-D UDP pose provider listening…` + `rx=1/2/3 parseErr=0` (datagrams received + parsed clean) → full chain OAK→sidecar→UDP→provider→PoseFrame confirmed, 0 errors, HUD "OAK-D 3D (UDP sidecar)". **✅ 2026-08-07 ITER-2 (device attached, many user iterations):** FOV crop FIXED (device = OAK-D-PRO-W / OV9782 1280×800; geaxgx assumed 1920×1080 → invalid ISP scale → centre-crop; sidecar now uses native `--color_res 800p --color_scale 1/2` → 640×400 FULL FOV). Sidecar `--show` cv2 preview added. Retarget overhaul (compiles clean): legs held straight when occluded (`SetTrackLegs`, `trackLegs=false`), hips upright-only (no tilt / no C needed), **rotational FK retarget now default** (`useIkDriver=false`, = webcam-tool/Kalidokit approach), **live mirror toggle** (`PoseSpaceConverter.SetFlipX/Y/Z`), and **position tracking** (avatar walks/jumps with user — `PoseFrame.RootPosition` from the OAK's measured hip `xyz` → avatar root, `trackPosition`/`positionScale`/`positionSmoothing`). Scene defaults saved (`useIkDriver=false, poseFlipX=false, poseFlipZ=false, trackLegs=false, trackPosition=true`). ⏳ NONE user-confirmed yet — needs a focused real-time Play. KNOWN LIMITS: face+fingers need a real USB webcam (Quest 3 virtual cam fails `Could not connect pins`); legs occluded by desk; `xyz` can spike (add outlier reject); `--lm heavy`≈7fps vs `full`≈18fps. Phase-2 (measured per-keypoint depth, doc 26) still open. **✅ ITER-3 (2026-08-07): position vertical inversion fixed (`ToUnityPosition`, OAK xyz is Y-UP); front-facing auto-calibration (no C key); `trackLegs=true` → USER-CONFIRMED "legs are fine now". Retarget-architecture decision recorded in ADR-017 (rotational FK default over IK).** Next: verify jump-up/walk-position live + tune `positionScale`; confirm mirror-X via live checkbox; get a USB webcam for face/fingers; then Phase-2 depth. Old details below (B1, now removed). ✅ 2026-08-06: `luxonis/depthai-unity` minimal Windows subset imported (compiles on Unity 6.3, 0 errors); `OakDPoseProvider` written (native `[DllImport]`, ABI-verbatim structs, MoveNet-17 real-3D → `PoseFrame`), wired OAK-first in `AppBootstrap` (`useOakDTracking=true` saved) + HUD label. ⏳ **NEXT: user live-test with OAK-D plugged in** (Play → HUD "OAK-D 3D (on-device)"); then tune coordinate signs + offload the native call off the main thread if it stalls FPS. Details below + ADR-016. On-device BlazePose (33 landmarks = our `JointId` topology) + stereo depth → metric 3D from the camera; removes the "hands behind" front/back ambiguity + scale guesswork; drops ~1:1 into `IBodyTrackingProvider`. **Integration fork pending:** B1 `luxonis/depthai-unity` native plugin (all-Unity, precompiled, has a Pose pipeline; risk = Unity-6.3 compat of a 2024-era plugin) vs B2 `geaxgx/depthai_blazepose` Python sidecar + UDP/socket to Unity (robust, decoupled; +Python process). Plan: verify B1 import first, else B2. New `Runtime/Tracking/OakD/OakDPoseProvider.cs` + `AppBootstrap.useOakDTracking` + fallback. NEEDS hardware plugged in + SDK/plugin install + user live-testing (can't validate device-less).

---

### Head-forward + folded-legs bugfix (2026-08-06, iter 2) — ADR-012
Webcam (seated, upper-body) run after iter 1: arms track ✅, but head still up, legs folded by default, wrist static.
- [x] **Head still up** — ear-midpoint aim (iter 1) still tilted back under the tracking Z convention. **Fix:** removed the Neck FK segment entirely (`HumanoidPoseRetargeter.BuildDefinitions`) — head rests forward. Head pose to come later from the face landmarker (SDS-007 §4).
- [x] **Legs folded by default** — seated webcam → occluded lower body → MediaPipe emits collapsed leg landmarks that leg IK folded onto. **Fix:** `AnimationRiggingIkDriver.UpdateLeg` gates on ankle sitting ≥ `MinLegDropMetres` (0.35 m) below the hip (torso-up axis); else leg weight 0 = rest straight (SDS-011 §8).
- [x] **Wrist/hand orientation** — added palm basis from MediaPipe Hand world landmarks (`PalmRotation`: wrist→middleMcp forward, palm-normal up), carried on `HandFrame`, applied **delta-from-neutral** to the Hand bone in `HumanoidHandRetargeter.ApplyWrist` (slerp by `wristRotationWeight`, default 0.7; C recalibrates neutral; 0 disables). Provider now takes the shared `PoseSpaceConverter`. See ADR-013. **Needs live weight/axis tuning** (convention-sensitive).
- [x] **Play-verify** — ✅ 2026-08-06 (Unity MCP, new machine): head forward (neck undriven), legs not folded (tracked plausibly on full-body video; gate-off/seated case by inspection), arms track (fake waving-arms: hand hip→above-head). 0 runtime errors. Wrist still pending a live webcam pass.
- ⚠️ **Regression fixed same session:** the fake-arms test left `useMediaPipeTracking=0` persisted in `Bootstrap.unity` (body ran on the fake provider → user saw "only face tracking"); restored to `1` on the real scene instance + saved. Do not leave that flag off on disk.
- Files: `Runtime/Retargeting/HumanoidPoseRetargeter.cs`, `Runtime/IK/AnimationRiggingIkDriver.cs`.

### IK arm-placement + head-up bugfix (2026-08-06)
Reported from a full-body dance-video run (VRM1 `Anime-Boy+V-tuber`, IK Arm/Leg Driver ON): arms hang at the hips (don't follow raised arms), head pitched up. See ADR-011.
- [x] **IK targets collapse to hips** — `AnimationRiggingIkDriver.UpdateArm/UpdateLeg` scaled the whole hip-centred landmark vector by the limb ratio, shrinking the torso→shoulder span too → hand/foot target sank to hip level on sub-human-scale avatars. **Fix:** anchor at the avatar `UpperArm`/`UpperLeg` bone and scale only the limb-local offset (`(wrist-shoulder)` / `(ankle-hip)`). IK now reaches correctly with IK ON.
- [x] **Head looks up** — neck FK segment aimed `midShoulder → Nose`; the nose's forward offset + tuned Z sign tilted the head back. **Fix:** aim `midShoulder → midEar` (centred, near-vertical) → head level/forward (SDS-011 §4 nose–ear plane). Trade-off: neck nod dropped for V1.
- [x] **Play-verify in Unity** — ✅ 2026-08-06 (Unity MCP connected, bridge port 6400): arm IK anchors correctly (hands follow raised-arm input up past the head, not pinned at the hips); head forward; 0 console errors.
- Files: `Runtime/IK/AnimationRiggingIkDriver.cs`, `Runtime/Retargeting/HumanoidPoseRetargeter.cs`.
- Note (not a code bug): fingers won't animate from a full-body dance video — hands are too small for the MediaPipe Hand Landmarker; needs upper-body framing. Legs having only UpperLeg→LowerLeg→Foot→Toes is the VRM Humanoid spec, not a defect.

---

## Blocked

_None_

---

## Completed

- [x] Author agent-oriented SDS docs `00`–`25` + handoff set (recreated 2026-08-05)

---

## 2026-09-15 (evening) — F-21 instrument rebuild + F-23 video-driven validation

### DONE
- [x] F-21 live instrument rebuilt; **10 defects fixed** before spending human time (F-21 report §32)
- [x] Cue legibility measured and fixed: 13 px / 4.1–6.2 arcmin → 60 px / 20.5–25.7 arcmin (ADR-058)
- [x] `f21_walkin_protocol.py` + `f21_walkin_score.py` + test (**22/22**); adds the two cases that
      are the REAL false-hold rate (`OWNER_OCCLUDED`, `OWNER_TURN`)
- [x] `cue.json` write race fixed in BOTH protocols — it was killing runs mid-session
- [x] F-23: avatar driven from real video through the production chain, **r = +0.714**
- [x] Pelvic roll re-enabled (ADR-059) — 25.1° p2p was being discarded by a config value
- [x] Slerp A/B measured — **negative result**, damping is not the lever (ADR-060)
- [x] Wire probe + trunk-jitter harness built and self-tested

### BLOCKING — needs people
- [ ] **F-21 LIVE TWO-PERSON SESSION.** Instrument ready, never run. Needs TWO people, ~30 min.
      Clear chair-like objects from the capture volume first (§32.4). Tape a cross at 0.90 m.
- [ ] **Trunk-yaw noise floor.** ~30 s of ONE person standing still at 0.90 m. Last thing gating
      the deadzone decision. The 2026-09-15 attempt is INVALID — no subject was in frame.
- [ ] F-22 L2 hands-near-face live.

### BLOCKING — needs the editor closed
- [ ] Re-run Unity EditMode **170/170** after `kalidokitBodyTorsoRoll: 0 → 1`. The VALUE is now
      validated on live data (F-27 section 8.5: trunk error more than halves), but the EditMode suite
      has still not been re-run against it.

### DECISION, not research
- [ ] Sub-pixel 1/8: torso-yaw quantum **6.80° → 0.85°** at 0.90 m, +2 ms, no FPS cost. This is the
      lever that would let the 8° deadzone come down honestly.

---

## 2026-09-16 — F-26 avatar-vs-skeleton report + the POSE FIDELITY metric

### DONE
- [x] F-26 observation report from `LatestScreenReording.mp4` — full avatar/skeleton comparison,
      failure mechanisms, what prior data does and does not support
      (`docs/F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md`)
- [x] **POSE FIDELITY metric built** — the measurement that had never existed. `BoneFidelity`,
      `KalidokitControlRigDriver.SampleFidelity()` (9 bones, read-only, allocation-free),
      `AvatarFidelityRecorder` (samples on `Application.onBeforeRender`, self-terminating),
      `f26_fidelity_analyze.py` (**22/22 self-test**, refuses to emit a combined score)
- [x] **Validated against live data**, two clips through the production wire (ADR-062, F-26 §11).
      Arms follow to 0.4–1.1° median; legs OVER-DRIVEN 1.4–2.3×; trunk median **16.7°**, follow 0.27
- [x] Cross-checked in world space against `PoseDebugSkeleton` — the aimed arms cannot validate the
      metric's coordinate mapping, so an independent path did. Two paths agree on the trunk to 0.4°
- [x] Trunk failure LOCATED, not just measured: lateral lean is `* torsoRoll` with
      `kalidokitBodyTorsoRoll = 0`; sagittal lean is high-passed by an 8 s adaptive baseline. The
      second is the mechanism behind "the avatar cannot sit" (F-26 §4.2)
- [x] Instrument caught a false finding on itself: forearm maxima of 121.6°/155.4° were entirely the
      release-to-rest tail. Stale-frame drop rule added, with tests

### NEXT — decision, not research
- [ ] **Choose a retarget direction** from F-26 §8 / §11.8. The metric is the before-number; option C
      ("fix the specific failures") now has two named, located causes rather than symptoms.
      **Nothing should change in the retarget until the direction is chosen** — that is what spending
      the measurement on an unvalidated fix would waste.
- [ ] Decide whether SEATED use is in product scope (F-26 §9.4). If it is, the trunk high-pass is the
      top defect. If not, it is not a defect at all. Nobody has stated which.

### NEXT — cheap, and unblocks a real claim
- [ ] Run the fidelity metric on an **OAK-D stereo session**. Both runs so far used the video path,
      which has no stereo and synthesises every joint's depth — it exercises the RETARGET, not the
      sensor (F-26 §11.7.3).
- [ ] **ENDPOINT error metric.** The direction metric is blind to it by construction: two poses can
      agree on every bone direction and still put the hands elsewhere, because the avatar's bone
      lengths are its own. Option D (bone-length normalisation) cannot be judged without it.

### STILL BLOCKING — unchanged, needs people
- [ ] F-21 live two-person session; trunk-yaw noise floor; F-22 L2 hands-near-face live.
- [ ] Re-run Unity EditMode **170/170** with the editor closed.

---

## 2026-09-16 (later) — F-27 humanized skeleton + F-28 skeleton-as-the-product

### DONE — F-27 HUMANIZED SKELETON (ADR-063)
- [x] New layer between the existing filtering and the existing retarget. Tracking output is treated
      as a SUGGESTION: fixed bone lengths, anatomical joint limits, teleport and origin-collapse
      rejection, per-joint hold in the PARENT's frame, 0.15 s re-acquire blend
- [x] **Nothing existing was rewritten** — tracking, filtering, P0-1, P1-1/2/3, Arm V2 and Torso
      V5/V6 are untouched, and confidence passes through so P0-1 still gates exactly as before
- [x] Test mode: `HumanizedSkeletonSelfTest` with synthetic bodies + injected faults (dropped joint,
      confident collapse to origin, NaN, teleport, flicker, impossible elbow fold, knee bending
      forwards, 150 deg torso twist). **32/32**, headless, no Editor or avatar needed
- [x] Before/after recorded from the SAME frames in ONE pass; analyser **18/18** self-test
- [x] **Two defects found by the numbers after the suite was green**: the body-forward axis was
      ambiguous (knee fix fired on 84 % of poses -> 1.8 %), and the velocity clamp was measuring its
      own output (80 % -> 0.4 %). Median jump went from +269 % WORSE to -3 %
- [x] Measured: bone-length deviation **-68 % median / -79 % worst**; **99.6 % of poses touched by no
      stateful stage**, so no added latency

### DONE — F-28 SKELETON SHOW
- [x] `Scenes/SkeletonShow.unity` — three live-switchable modes (1/2/3, TAB, on-screen buttons),
      driven by the SAME production tracking and the F-27 layer, retarget skipped entirely
- [x] Mode 1 glow skeleton, mode 2 energy body (no mesh, no skinning), mode 3 motion effects
- [x] `H` toggles the humanized layer live — the fastest A/B of F-27 there is
- [x] 114-118 fps in the editor; mode switching verified to leave exactly one mode root

### NEXT — cheap and it closes a real question
- [x] **DONE — F-26's fidelity metric run with the layer ON, OFF, and ON-with-bone-lengths-OFF**
      (F-27 section 8, ADR-063 addendum). 4000+ live frames per arm, reference = the RAW pose in every
      arm. Result: the protections cost the avatar NOTHING (9/9 unchanged with bone lengths off); the
      whole ~1 deg arm cost is bone-length normalisation; F-27 does NOT fix the leg over-drive.
      On CLEAN tracking the layer is neutral-to-slightly-negative for avatar fidelity.
- [x] **DONE — `kalidokitBodyTorsoRoll: 0 -> 1` validated**, incidentally: F-26 section 11.5's
      "the trunk lean channel is dead" was measured against a RUNTIME 0 while the scene ships 1.
      Re-measured: trunk median 16.7 -> 7.3 deg, follow 0.27 -> 1.29. F-26 section 11.5 is corrected
      in place.
- [ ] **Run F-27 on an OAK-D stereo session.** Both recordings used the video path, which has no
      stereo — so the rejection, hold and recovery stages had NOTHING to do (0 holds, 0 NaN, 0
      collapses in either stream). They are proven by fault injection, not by live data.
- [ ] Attribute the **p99 jump regression (+19 %)** to a specific bone before deciding if it matters.

### NEXT — product direction, not research
- [ ] **Decide between the avatar and the skeleton.** F-28 exists because F-26 located every avatar
      failure in the retarget; mode 2 sidesteps that entire class of error by building the visual
      from joint positions. That is a product choice, not an engineering one.
- [ ] Show F-28 to somebody. Whether it reads as intentional rather than as a debug view is a
      judgement about an audience, and no audience has seen it.

---

## v1 PACKAGING — sidecar auto-launch and build packaging (2026-09-16, ADR-064)

### DONE
- [x] **`SidecarProcessLauncher`** spawns `sidecar_supervisor.py` from `AppBootstrap.StartTracking()`.
      Output goes to `ILogService`; teardown is first in `TeardownServices()` (already idempotent and
      already wired to both `OnApplicationQuit` and `OnDestroy`, so domain reload is covered).
- [x] **Orphan prevention via Win32 job object** (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`), so the
      kernel kills the tree even when Unity crashes and no managed teardown runs. Measured: the live
      tree is supervisor -> sender -> a third process, so `taskkill /F /T` is load-bearing.
- [x] **Attach, never double-spawn.** Launcher probes lock port 8897 before spawning; held means an
      existing supervisor, so it reports `AttachedExternal` and leaves it alone.
- [x] **`--allow-port-listener` passed unconditionally** — Unity in Play mode holds 8899 and the
      supervisor's probe would otherwise read that healthy listener as a stale producer.
- [x] **Serialized `autoStartSidecar`** on `AppBootstrap` (default on), plus lock port, model
      override, portrait direction and subpixel bits.
- [x] **Build packaging decided and implemented** — `.py` source into `StreamingAssets/Sidecar/` via
      a post-build step; `.venv` (342 MB, NOT relocatable), the 369 MB model and the superseded
      86 MB `depthai_blazepose/` all excluded. Trade-off recorded in ADR-064 and README.md.
- [x] **`setup_sidecar.ps1`** — one-time target setup that verifies `DmlExecutionProvider` and fails
      at install time rather than letting the 5 fps CPU fallback reach production.
- [x] **`requirements.lock.txt`** — exact `pip freeze`. `requirements.txt` had
      `onnxruntime-directml` COMMENTED OUT while the working venv had it installed, so anyone
      setting up from it got a sidecar with no inference at all.
- [x] **Product `README.md`** at the repo root. The previous root copy was a byte-identical
      duplicate of `docs/README.md` whose links only resolve from `docs/`.
- [x] **11 EditMode tests** (`SidecarPathsTests`) covering editor/player path resolution and the
      hard-fail-on-missing-venv contract. 11/11 pass.

### OPEN — required before v1 can be called done
- [ ] **Press Play and demonstrate the cycle three times.** The mechanism is verified directly
      (exact CLI, exact `taskkill /F /T`, 3/3 cycles, no orphan) but NOT through Unity Play mode.
- [ ] **Produce and run a Windows build.** `SidecarBuildPostprocessor` has never executed.
- [ ] **Re-run the full EditMode suite (170 tests) in batch mode** with the editor closed. Still
      outstanding from the `kalidokitBodyTorsoRoll: 0 -> 1` change.
- [ ] **Surface `SidecarProcessLauncher.StatusLine` in `DiagnosticsHudPanel`.** The launcher exposes
      it; nothing displays it yet, so a sidecar failure is currently console-only.
- [ ] Confirm Build Settings scene list and order, and decide whether `SkeletonShow.unity` ships.
- [ ] Cleanup pass (docs/evidence blobs, one-off harnesses) — NOT started; needs the user's call on
      what to delete, since several "test scripts" are the only instrument protection this project
      has.

---

## v1 CLEANUP + SIDECAR REORGANISATION (2026-09-16, ADR-065)

### DONE — cleanup (~2.72 GB freed, nothing tracked was deleted)
- [x] **Citation chain committed FIRST, before any deletion.** `docs/evidence/` was entirely
      untracked, so the evidence behind ADR-062/063 and F-26/27/28 existed on one disk only.
      33 files / 3.5 MB now committed: 9 cited PNGs, RESULTS.txt, FIDELITY_AB.txt, the .gitignore
      rules. Verified 0 .jsonl staged.
- [x] `oak_v4_evidence/` 1,818 MB -> 26.9 MB. Deleted 1,976 untracked files (1,701 PNG, 20 MKV,
      8 MP4, 101 jsonl); kept all 118 tracked summary files. Overlap between the delete list and
      the tracked list was checked to be 0 before executing.
- [x] `pipeline_logs*/` (5 dirs, 759 MB), `__pycache__/`, `docs/evidence/**/*.jsonl` (39 MB),
      `LatestScreenReording.mp4` (130 MB) — all removed.
- [x] **Images KEPT, deliberately.** All 9 PNGs in `docs/evidence/` are cited by
      F26_AVATAR_VS_DEBUG_SKELETON or F28_SKELETON_SHOW_MODES; `t065_arms_overhead_avatar_horizontal.png`
      is the visual record of the failure ADR-063 reasons about. Deleting 3.4 MB would have broken
      the citation chain for no meaningful saving. The real image bulk was the 1,701 untracked
      frame captures inside `oak_v4_evidence`, which are gone.
- [x] **The repo was never bloated by any of this** — it was all untracked, and the f26/f27
      .gitignore rules already excluded the jsonl. This freed disk, not repository size.

### DONE — sidecar reorganisation (ADR-065)
- [x] Root reduced 83 -> 12 .py files: the two entry points, the nine modules
      `wholebody_udp_sender` imports, and the `_sidecar_path` shim. The nine were NOT moved, on
      purpose — relocating them means editing the shipping frame loop's imports.
- [x] 72 files moved into `tests/` (8) and `tools/` (capture 23, ownership 13, diagnostics 11,
      deployment 7, validation 6, video 4), grouped by dependency cluster.
- [x] 39 files given the `_sidecar_path` prelude. It walks UP to find the shim rather than counting
      dirname() calls — a fixed depth was right for tools/<group>/ and wrong for tests/.
- [x] **Dedup measured, not assumed:** 0 whole-file pairs >= 55% similar; exactly 1 duplicated
      function (13-line `load()`), hoisted to `f16_configs.load_rows()`. The similar-looking
      f21_/f22_ names are different subsystems and were correctly left alone.
- [x] `SidecarBuildPostprocessor` now ships 12 files instead of 83; AGENTS.md §9 records that the
      root is the production path and a file left there reaches every player.
- [x] Sidecar README `## Layout` rewritten — it still documented ~12 scripts deleted back in
      `29ec57e`, and the Tests/Device-free sections gave commands that no longer ran.

### Verified
```text
self-tests before move   75/75, 39/39, 37/37, 22/22, f26 28, f27 18
self-tests after  move   identical, + test_surface_depth 32/32
production chain         all 10 modules import clean; compileall exit 0
supervisor end-to-end    3/3 start/stop cycles, READY ~13 s, no orphan, ports released
duplicate scan after     0 pairs, 0 duplicated functions
```

### DONE — root tidy-up (follow-up to ADR-065)
- [x] The first pass sorted only `.py` and left **13 loose non-.py files** at the sidecar root, plus
      **9 .py scripts buried inside `arm_v*_evidence/`** that a root-only scan never saw.
      Root: 96 -> 18 files.
- [x] `scripts/` ← 4 `.bat` + `oak_guided_v4.ps1`. Every `.bat` did `cd /d "%~dp0"` to reach the
      repo root; from `scripts/` that is one level short, so all are re-based to `%~dp0..`.
      Verified the venv interpreter, the model, the supervisor and compare_logs all resolve.
- [x] `docs/` ← `p0_human_report.txt`;  `tools/armaim/` ← the 9 arm-aiming analysis scripts.
- [x] **Three broken launchers found.** `run_p0_acceptance.bat` and `run_p12_ab.bat` have been dead
      since `29ec57e` (they invoke deleted scripts); `run_capture.bat` was broken by the ADR-065
      move itself and is now repointed. The two dead ones are KEPT — P0_ACCEPTANCE and
      P1_2_FRESHNESS cite them by name — but carry a DOES NOT RUN banner and exit 1.
- [x] `oak_v4_evidence/` deliberately left at the root: script defaults hardcode that path.
- [x] Removed `pose_stage_marks.json` (orphan; one unfinished mark, `tEnd 0.0`).

---

## SDK DISTRIBUTION STANDARDS (2026-09-16)

- [x] **MIT `LICENSE`** in both repos. The sidecar README had promised "see repository license"
      while no LICENSE existed. MIT matches the dependency stack (UniVRM, MediaPipe Unity plugin,
      Kalidokit, depthai, vendored depthai_blazepose are all MIT).
- [x] **`package.json`** — UPM manifest, `cloud.viitor.virtual-mirror` `0.1.0`, Unity `6000.3`.
      `0.x` on purpose: the §7 items are still open and the API may move in a minor release.
- [x] **`CHANGELOG.md`** in both repos (Keep a Changelog / semver).
- [x] **`CONTRIBUTING.md`** — repo layout, the DirectML trap, test commands, the evidence rules.
- [x] **`.gitattributes`** in both repos — LF normalisation, CRLF for `.bat`/`.cmd`/`.ps1`,
      binary markers for `.onnx`/`.blob`/media, linguist hints for vendored and generated trees.
      Also ends the constant CRLF warning noise on every commit.
- [x] **`.editorconfig`** — matches the existing C# and Python style (110 cols, same-line braces).
- [x] **`pyproject.toml`** for the sidecar — PEP 621 metadata, console entry points,
      `requires-python = "==3.10.*"` (depthai is cp310-only), ruff/black config. `py-modules` is
      explicit so a harness under `tools/` can never accidentally ship as an importable module.
- [x] **CI** (`.github/workflows/ci.yml`) — validates package metadata and that CHANGELOG documents
      the shipped version; runs the sidecar self-tests on `windows-latest` + Python 3.10; asserts
      `DmlExecutionProvider` is present; guards against absolute machine paths returning.
- [x] **Issue templates** — the bug template requires the ONNX provider list, because "everything is
      slow" is nearly always the silent CPU fallback.
- [x] **`ThirdPartyNotices.txt` extended** with the whole Python stack, which was entirely absent:
      vendored depthai_blazepose (MIT, licence file verified present), RTMW3D/MMPose weights
      (Apache-2.0 code, weights carry their own terms — flagged), depthai, onnxruntime, OpenCV, NumPy.
- [x] **README** — UPM install section. Documents that `com.vrmc.vrm` needs the OpenUPM scoped
      registry and `com.github.homuler.mediapipe` is not on any registry, so neither resolves for a
      consumer automatically; and that UPM does not fetch the `python-sidecar~` submodule.

### Open before a public release
- [ ] Confirm the MIT copyright holder string. `LICENSE` currently reads "ViitorCloud" — inferred
      from the domain, not verified against the legal entity name.
- [ ] Verify the licence attached to the specific `rtmw3d-x.onnx` weights in use. MMPose's CODE is
      Apache-2.0, but released weights can carry dataset-derived restrictions, and the model is a
      hard runtime requirement.
- [ ] Decide whether `evidence/` (~27 MB of tracked measurement data) belongs in a distributed SDK.
      It is the citation chain for the ADRs, but it is development material, not product.
- [ ] Unity EditMode tests are not in CI — that needs a licensed editor (GameCI + secrets).
- [ ] Tag `v0.1.0` once the Play-mode and build acceptance items above are actually run.

---

## F-34 — NINE CROWD EXPERIENCES (2026-09-17)

Report: [`F34_CROWD_EXPERIENCES_2026-09-17.md`](F34_CROWD_EXPERIENCES_2026-09-17.md) · Decision:
**ADR-072** · Evidence: [`evidence/f34/`](evidence/f34/)

### DONE — the four prerequisites
- [x] **`SkeletonPose.ResetHistory()` + a 12 m/s mid-hip guard.** Expressed as a speed times `dt`, so
      it holds at 16 fps and at 60. Catches a grounding snap (a live latent bug: the ground-offset
      snap moved the staging origin by up to a metre in one frame and produced a whole-body speed
      spike above every strike gate at once) and a gross identity jump. It does **not** catch a slow
      switch — 0.015 m/frame — and says so. The floor is deliberately kept across a reset.
- [x] **`ExperienceBase.ResetsOnNewVisitor`** (default **true**, so the ten existing scenes are
      untouched) plus `OnPersonArrived` / `OnPersonLeft`, driven by `CrowdRoster` keyed on track id.
      `PresenceGate` answers "is there *a* person" and so never changes in a crowd.
- [x] **`ExperienceTuning`** — the three movement thresholds (0.9 / 0.55 / 0.35) in one place with
      the frame-rate caveat attached. `BubblePopExperience` and `ObjectPlayExperience` now reference
      it; values unchanged.
- [x] **`BodyRenderer.Render(pose, tint)`** as an overload. The existing signature is unchanged.

### DONE — the nine scenes
- [x] `CollectiveFluid` · `StillnessTug` · `CrowdFootprints` (TRACES) · `Eclipse` · `Chord` ·
      `Chain` · `Pass` · `Podium` · `MirrorEachOther`. Each is one `ExperienceBase` subclass plus a
      `.unity` that is a camera and one GameObject.
- [x] **No per-person accumulated score in any of them**, per ADR-072. The three places identity IS
      read — colour, a pass count, a crown-change chime — are named in each class header with what a
      switch costs.
- [x] Shared: `CrowdPalette`, `CrowdRoster`, `CrowdMath`, `CrowdVoices`, `ExperienceTuning`,
      `FluidField`, `FloorMarks`.
- [x] `CrowdBondsExperience`'s private colour table moved to `CrowdPalette` so a person keeps their
      colour across every crowd scene. Same colours, same order, same eviction rule.

### DONE — verification (headless; the Editor holds the project lock)
```text
compile                  VirtualMirror.{Experiences,SkeletonShow,Tests}  0 errors
unit tests (new)         CrowdExperiencesF34Tests                        29/29, 0 skipped
unit tests (whole suite) 171 passed, 6 failed, 21 skipped
                         the 6 are SidecarPathsTests, which need Application.dataPath
                         (empty outside a player) and are untouched by this work
```
- [x] `SkeletonShowF29Tests.Planted_RequiresBothProximityAndStillness` — its **input** was changed,
      not its assertion: it stepped 0.4 m per 60 Hz frame (24 m/s, three times a sprinter's hip),
      which the new guard correctly absorbs. Now 0.1 m/frame = 6 m/s, still far above the 0.35 m/s
      planted gate. A new companion test pins the other side.
- [x] Two behaviour-neutral changes in `FloorMarks` for testability: `Create` is pure with the
      visuals in `BuildMarkVisuals` (the runtime refuses to JIT a whole method referencing a native
      ECall, even on an untaken branch), and `hasVisuals` is a bool rather than a `Transform == null`
      comparison (`UnityEngine.Object` overloads `==` with a native call).

### OPEN — needs the camera and a person. NOTHING IN F-34 HAS BEEN ON A SCREEN.
- [ ] **Play-mode smoke test `Fluid.unity` and `Footprints.unity`** — the two MIGRATED scenes.
      Highest priority: they worked before this change.
- [ ] Play-mode smoke test the nine new scenes with the sidecar running: attract first (empty room),
      then one person, then two.
- [ ] **Listen to `Chord.unity`.** It is mostly the sound and the voice bank has never produced a
      sample. Check for clicks at the four-second loop wrap and that `M` mutes it.
- [ ] **`Chain.unity` with two people actually holding hands.** Its risk is the DETECTOR merging two
      boxes at that distance, which has never been measured. The HUD flags a suspected merge as a
      known limit rather than letting it read as a bug.
- [ ] **`Eclipse.unity` with two people at different depths.** If the HUD says "NO MEASURED DEPTH"
      the sender is publishing no root position and the scene cannot work — that is the honest
      failure, not a bug in the scene.
- [ ] **Re-measure the three movement thresholds at ~16 fps** with three people (`ExperienceTuning`).
      They were tuned at ~21 fps. The effect is one-sided: a crowd scene reads slower, so the gates
      get harder to pass.
- [ ] **Re-check the frame rate with three people in `Collective`** — the heaviest of the nine (one
      grid, 1100 particles, up to 8 skeletons).

---

## F-35 — THE SCENE LAUNCHER (2026-09-17)

Report: [`F35_SCENE_LAUNCHER_2026-09-17.md`](F35_SCENE_LAUNCHER_2026-09-17.md) · Decision:
**ADR-073** · Evidence: [`evidence/f35/`](evidence/f35/)

### DONE
- [x] **`Scenes/Launcher.unity`** — a camera and one GameObject, like every other scene here. Three
      columns: **ONE PERSON** (9), **TWO OR MORE** (10), **PRODUCTION** (Bootstrap).
      Click a row, or arrows + Enter, or the number shown (1-9 then 0, within the highlighted column).
- [x] **Grouped by how many people a scene needs**, not by theme — ten of them do nothing on your
      own, and somebody standing alone in front of Chain concludes the installation is broken.
- [x] **The launcher runs the real `TrackedStage` while you choose**, so "the sidecar is not running"
      and "this is the SINGLE-PERSON sender" are visible BEFORE a scene is picked and blamed. The
      multi-person column swaps its description for that warning when `HasCrowd` is false.
- [x] **All 20 scenes registered in `EditorBuildSettings.asset`.** Required: `LoadScene` cannot load
      an unregistered scene and fails *after* the click. **Index 0 is still `License-Verifier`**, so
      a build's startup scene is unchanged.
- [x] **Availability checked anyway**, once at startup, by walking the build list. A missing scene is
      drawn greyed with "NOT IN BUILD SETTINGS - add Scenes/&lt;name&gt;.unity" rather than being a
      silent dead button.
- [x] **ESC returns to the menu from every scene**, via `LauncherScene` in the SkeletonShow assembly
      — Experiences references SkeletonShow and not the reverse, so anything both bases call has to
      live in the lower one. No-op with one warning when no launcher exists, so opening a single
      scene on its own behaves exactly as before.
- [x] **The UDP socket is released before the load, not in `OnDestroy`**, and both bases skip the rest
      of the frame (`leaving`) instead of running Update and OnGUI against a disposed provider.
      Every scene binds the same fixed port, so the order matters.
- [x] `Mirror.unity` deliberately NOT on the menu: `Bootstrap` loads it additively after wiring
      settings, avatar and UI, so opening it alone gives an unwired scene.
- [x] **`Virtual Mirror > Register All Scenes in Build Settings`** (`Editor/SceneRegistrar.cs`),
      because `ProjectSettings/EditorBuildSettings.asset` is **gitignored** — the registration is
      per-machine and a clone opens the launcher with every row greyed out. The menu item only ever
      APPENDS (never reorders, removes or disables), since index 0 is the startup scene of a build.
      `Virtual Mirror > List Scenes in Build Settings` reports without changing anything.

### DONE — verification (headless)
```text
compile                  VirtualMirror.{SkeletonShow,Experiences,Tests}  0 errors
unit tests (new)         SceneLauncherF35Tests                           10/10
unit tests (whole suite) 181 passed, 6 failed, 21 skipped
                         the 6 are SidecarPathsTests (need Application.dataPath), unrelated
scene registry           20/20 rows resolve to a file AND a Build Settings entry - 0 problems
```
- [x] The launcher tests pin the invariants whose failure mode is **silent**: no duplicate rows,
      columns disjoint, every row has a title and a description, the launcher does not list itself,
      no column longer than the ten rows the number shortcut can address, and the path→name parsing
      including `null`, empty and a dot in a folder name.

### OPEN — needs the Editor. NOTHING IN F-35 HAS BEEN ON A SCREEN.
- [ ] **Open `Scenes/Launcher.unity` and press Play.** The three-column layout is computed from
      `Screen.width` and has never been rendered.
- [ ] **Click one row in each column, then ESC back.** The round trip is the most likely thing to be
      wrong, because it rebinds a UDP socket on a fixed port.
- [ ] **Launch `Bootstrap` from the menu** and confirm the app still wires up. Note ESC does NOT
      return from it — it is not one of the two bases (known limit).
- [ ] **Watch the multi-person column's note** with the single-person sidecar, then with
      `multiperson_udp_sender.py`. That line is the most useful thing the menu says and is untested.
- [ ] Decide whether a build should open on `Launcher` (move it to index 0). Not done: it changes
      what the product does on launch.

---

## F-36 — PRESS PLAY ONCE (2026-09-17)

Report: [`F36_SIDECAR_BOOT_2026-09-17.md`](F36_SIDECAR_BOOT_2026-09-17.md) · Decision: **ADR-074** ·
Evidence: [`evidence/f36/`](evidence/f36/)

> **Partly superseded the same day by F-37 / ADR-075.** The scene topology below stands. Everything
> below about running the sender DIRECTLY because the supervisor cannot, and about the demo path
> having no watchdog, is no longer true — the supervisor now launches either sender and both
> bootstraps are supervised. Read F-37 before acting on this section.

### DONE
- [x] **`Scenes/SidecarBoot.unity` at Build Settings index 1** (index 0 `License-Verifier` untouched).
      Starts the sidecar, then loads `Launcher`. Order is now
      `License-Verifier -> SidecarBoot -> Bootstrap -> Mirror -> Launcher -> ...`
- [x] **It runs `multiperson_udp_sender.py` DIRECTLY.** The supervisor cannot: it builds its child
      command from `--portrait --portrait-dir --subpixel-bits --seconds`, which only
      `wholebody_udp_sender.py` accepts, and waits on a readiness contract only that sender emits.
      The multi-person sender is backward compatible, so ONE sender serves every scene and the app.
- [x] **The product keeps its watchdog.** `SidecarProcessLauncher.DirectScript` defaults to empty, so
      `AppBootstrap` still goes through the supervisor. Pinned by a test.
- [x] **It binds no UDP socket** - every scene binds 8899 for itself - and is `DontDestroyOnLoad`
      only so the sidecar process outlives scene switches.
- [x] **Never a second producer, in BOTH directions:** listens on the port for 400 ms first and
      attaches if anything is already sending; and HOLDS the supervisor's single-instance lock while
      it owns a sidecar. The second half fixes a real defect - `Bootstrap.unity` ships with
      `useOakUdpTracking: 1` and auto-start on, so launching the avatar app from the menu's
      PRODUCTION column would otherwise have spawned a second, supervised sidecar on top of it.
      The lock became `SidecarSingleInstanceLock`, one definition used by both sides; 4 tests.
- [x] **`Bootstrap.unity` is NOT superseded.** It is the product: avatar, UI, retargeting, IK, its
      own supervised sidecar. `SidecarBoot` owns only the sidecar process. Both are needed and the
      menu lists `Bootstrap` in its PRODUCTION column.
- [x] **`AppBootstrap` untouched**, and its `mirrorSceneName` code default reverted to `"Mirror"`.
      It was a live trap: `[SerializeField]` means the scene's value wins, so the change did nothing
      today but would have made any NEWLY added AppBootstrap load a scene with no `AvatarRoot`.
- [x] `SceneRegistrar` now places `SidecarBoot` at index 1 the first time it adds it; still never
      touches index 0 and never moves an entry that is already registered.

### DONE — verification (headless)
```text
compile     VirtualMirror.{Tracking,SkeletonShow,Editor,Tests}   0 errors
unit tests  SidecarDirectLaunchF36Tests                          10/10
            whole suite                                          191 passed, 6 failed, 21 skipped
            (the 6 are SidecarPathsTests, which need Application.dataPath)
spawned cmd executed with a bogus --model: past argparse, past provider selection
            (DmlExecutionProvider), failing only on the missing model  -> evidence/f36/sender_args.txt
```

### OPEN — needs the camera. NOTHING IN F-36 HAS BEEN ON A SCREEN.
- [ ] **Press Play on `Scenes/SidecarBoot.unity`.** Expect `[SidecarBoot] launching: ...`, the
      launcher within a frame, poses after ~15 s while the model loads.
- [ ] **Check the launcher footer shows `sidecar: ...`** - that status line has never rendered.
- [ ] **Launch a scene from each column and ESC back**, confirming no scene fails to bind 8899.
- [ ] **Launch `Bootstrap` from the PRODUCTION column** and confirm the console says *attaching to
      it instead of starting a second one* - the lock fix is unit-tested but has never run in Unity.
- [ ] **Start a sender by hand first, then press Play**, and confirm the same message.
- [ ] **Exit play mode and confirm no orphan `python.exe`** is left holding the camera.

---

## F-37 — TRACKING QUALITY REGRESSION + THE SIDECAR THAT COULD NOT COME BACK (2026-09-17)

Decision: **ADR-075** · Evidence: `Assets/Games/video/SR1.mp4`, `SR2.mp4` (operator recordings)

Reported as "it keeps tracking the chair as a person" and "when I press esc the sidecar is killed
and does not revive - no supervisor reboot". Both confirmed from the recordings: one person in the
room, the sender's own HUD reading `tracks=4 dets=2 posed=2`, two boxes on the same body (1.7 m and
2.4 m) each with a skeleton, and a third on a stack of cartons at 3.5 m.

### DONE — the detector was fed a bad picture
- [x] **`cam.preview` was never sized**, so it sat at DepthAI's default 300x300 with
      `keepAspectRatio` ON. Measured on depthai 2.32.0.0: centre-crops the 640x400 ISP to 400x400
      (120 px discarded off each side), and `setResize(544, 320)` then stretched the square to 17:10.
- [x] **`detections_from` mapped those boxes onto the FULL frame** with no inverse, displacing every
      box outward from centre by 640/400.
- [x] **Fixed:** `setPreviewSize(isp)` + `setPreviewKeepAspectRatio(False)` + `setResizeThumbnail`
      (the VPU's own letterbox), inverted on the host through `letterbox_mapping` — one definition
      now shared with `tools/video/f32_multiperson_video.py`, which had always done it correctly.
      **That is why the F-32 measurement disagreed with the live behaviour: it came from the harness,
      not from the path Unity runs.**

### DONE — the tracker was fed at the wrong rate
- [x] **One detection counted as three confirmations.** Detector ~11 Hz vs 30 Hz RGB, and the same
      `latest_dets` was re-fed every RGB frame, so `confirm_hits = 3` was met by a single packet and
      the "a flickering false positive never becomes a person" rule could never fire. The tracker is
      now updated once per detector packet.
- [x] **Held (LOST) tracks were posed and streamed.** RTMW3D was run on boxes drifting off any body.
      Only CONFIRMED tracks are posed and sent; LOST tracks stay in the tracker for occlusions.
- [x] The preview marks held tracks grey and labels them `held`, so "why are there two boxes on one
      person" is answerable from the window itself.

### DONE — restart-on-crash is back
- [x] **ESC no longer quits.** `cv2.waitKey(1) in (27, ord("q"))` bound ESC to killing the producer,
      while the app's own hint reads "ESC menu". Only `q` now, and it closes the window, not the
      process.
- [x] **`sidecar_supervisor.py` supervises either sender** (`SENDER_BY_SCRIPT` → `build_command`);
      `multiperson_udp_sender.py` prints the `producer session id = ` banner for the readiness
      contract. A missing detector blob is now a TERMINAL failure with a fetch hint.
- [x] **Both bootstraps launch supervised** via `SupervisedSenderScript` / `MaxPoses`.
      `SidecarBootstrap` no longer holds the lock by hand (the supervisor would abort on its own
      mutex). `--show` defaults OFF.
- [x] **`tests/f20b_failure_tests.py` paths repaired** — they resolved against `tests/` after the
      ADR-065 refactor, so the harness died on `CreateProcess` before running a single scenario.

### DONE — verification (headless; the Editor holds the project lock)
```text
compile      VirtualMirror.{Tracking,SkeletonShow,App,Tests}       0 errors
unit tests   SidecarDirectLaunchF36Tests                           12/12 (2 new, supervised sender)
             whole EditMode suite                                  192 passed, 9 failed, 19 skipped
             (6 SidecarPathsTests need Application.dataPath; 2 are [TestCase]-parameterised and
              1 needs UnityEngine.AnimationModule - all runner artifacts, not failures)
python       test_person_tracker 19/19 · test_person_filters 46/46
             test_joint_tracker 37/37 · test_pose_validation 22/22
supervisor   f20b_failure_tests.py d,e,f                           4/4
             (duplicate guard, crash loop -> FAILED_PERMANENT + keeps retrying, terminal dep failure)
geometry     letterbox_mapping(640,400) -> scale 0.80, pad 16 px each side, 0 vertical
             full-frame box round-trips to exactly (0,0)-(1,1); centre stays centre
command      supervised single-person command unchanged byte for byte; multi-person command carries
             only --host/--port/--model/--max-poses[/--show]
```

### OPEN — needs the camera. THE FIX HAS NOT BEEN ON A SCREEN.
- [ ] **Press Play on `Scenes/Bootstrap.unity` with the OAK-D connected** and confirm the cartons and
      chairs no longer acquire tracks. This is the claim the whole change rests on and it is
      currently supported only by arithmetic.
- [ ] **Confirm `tracks` == `dets` with one person present** (the HUD now separates `held`).
- [ ] **Re-measure people-per-frame** against the F-32 figure (7.06 letterboxed vs 0.95 squashed) to
      put a number on the recovery.
- [ ] **Check the FOV**: people in the outer ~120 px of frame were invisible to the detector before
      and should now be found.
- [ ] **Kill the sender with Task Manager** and confirm the supervisor restarts it with a new session
      id — the A/B scenarios of `f20b_failure_tests.py` need the camera and have not been run.
- [ ] **Press ESC in Unity** and confirm tracking survives it.
- [ ] **Exit play mode and confirm no orphan `python.exe`** is left holding the camera.

---

## F-38 — THE SCENE CHOOSES THE SENDER (2026-09-17)

Decision: **ADR-076** · Follows F-37

Reported as "for single person experiences I need the sidecar to use [the single-person path] for
best result (like earlier it was) ... I go from Bootstrap -> launcher and any single person exp then
multi person tracking gets started and that is not accurate for 1 person".

### DONE
- [x] **`SceneLauncher.TrackingNeedFor(sceneName)`** classifies from the catalogue the menu already
      draws — `SinglePerson[]` and `Production[]` → single, `MultiPerson[]` → crowd, anything else →
      `Unspecified`. The columns a person sees and the tracking they get cannot disagree.
- [x] **`Unspecified` changes nothing.** Load-bearing, not tidiness: `Launcher` is passed through on
      every navigation, so resolving it either way would restart the sidecar every time somebody
      backed out of a scene.
- [x] **`AppBootstrap.EnsureSidecarForScene`** on every scene load (and on the starting scene, which
      `sceneLoaded` never fires for). Restarts only when the requirement actually changes; logs the
      pause when it does.
- [x] **Never restarts a sidecar it did not spawn** — `AttachedExternal` is left alone.
- [x] **Two serialized fields** (`sidecarSinglePersonSender` = `wholebody_udp_sender.py`,
      `sidecarMultiPersonSender` = `multiperson_udp_sender.py`) replace the single one, in code and
      in `Bootstrap.unity`.
- [x] **NOT the BlazePose/MediaPipe sender**, which is what was literally asked for.
      `depthai_blazepose/udp_pose_sender.py` sends no `lh`/`rh`, so AIR GRAFFITI and OBJECTS — both
      pinch-driven — would stop working. `OakDUdpPoseProvider:319` already warns against it by name.
- [x] **No experience scene changed.** `TrackedStage` documents and implements the fallback: no
      `persons` array in the datagram → `Bodies` gets exactly one entry from the root-shape pose.

### DONE — verification (headless; the Editor holds the project lock)
```text
compile      VirtualMirror.{SkeletonShow,App,Tests}          0 errors
unit tests   SceneLauncherF35Tests                           15/15 (5 new)
             whole EditMode suite                            197 passed (was 192)
             (same 9 runner artifacts as F-37: 6 need Application.dataPath,
              2 are [TestCase]-parameterised, 1 needs UnityEngine.AnimationModule)
commands     supervisor builds, per sender:
               wholebody   -> --host --port --model --portrait --portrait-dir ccw
                              --subpixel-bits 3 --seconds 0
               multiperson -> --host --port --model --max-poses 3
             both senders run PAST argparse and provider selection with those exact flags,
             failing only on a deliberately bogus --model (NO_SUCHFILE) - so neither is
             handed a flag its argparse does not define
```

### OPEN — needs the camera
- [ ] **Launch a single-person experience from the Launcher** and confirm the console names
      `wholebody_udp_sender.py`, and that tracking quality is back to what it was.
- [ ] **Confirm pinch works in AIR GRAFFITI and OBJECTS** — the reason the BlazePose sender was
      refused.
- [ ] **Cross the columns once** (single → crowd → single) and time the restart. The ~15-20 s figure
      is the model-load estimate, not a measurement.
- [ ] **Confirm navigating WITHIN a column never restarts** (Launcher → PoseMatch → Launcher →
      AirGraffiti should log no sender change).
- [ ] **Start a sidecar by hand, then launch scenes**, and confirm it is never killed.

---

## F-39 — SUB-PIXEL DISPARITY FOR THE MULTI-PERSON SENDER (2026-09-17)

Decision: **ADR-077** · Follows F-38

Found while explaining the remaining accuracy gap: `multiperson_udp_sender.py` had **no
`--subpixel-bits` flag at all**, so every crowd scene inherited `STEREO_CONFIG`'s shipped
`subpixel: False` while the single-person sender acted on the `--subpixel-bits 3` Unity has always
passed. Depth error is `z² × Δd / (f × B)`, so the disparity step multiplies error at every range.

### DONE
- [x] **`--subpixel-bits` added to `multiperson_udp_sender.py`**, same name, semantics and `-1`
      default as the single-person sender, applied to the shared `STEREO_CONFIG` before
      `build_pipeline` reads it.
- [x] **Supervisor forwards it** in the multi-person branch, as it already did for single-person.
- [x] **Startup banner** — the multi-person sender now prints `[mp]   stereo: …`. It never did, which
      is precisely why a configuration that silently did not apply went unnoticed.

### DONE — verification (headless; no camera)
```text
device calibration   baseline 7.5 cm, mono fx 287.16 px @ 640x400  -> f*B = 21.54
wiring               real main() with the model and camera stubbed, spying on what
                     build_pipeline was actually handed:
                       no flag           -> subpixel=OFF     (production untouched)
                       --subpixel-bits 3 -> subpixel=on(1/8)
                       --subpixel-bits 0 -> subpixel=OFF     (explicit off)
supervisor cmd       wholebody   ... --portrait --portrait-dir ccw --subpixel-bits 3 --seconds 0
                     multiperson ... --max-poses 3 --subpixel-bits 3
python tests         tracker 19/19 - filters 46/46 - joints 37/37
                     validation 22/22 - surface depth 32/32
supervisor tests     f20b_failure_tests.py d,e,f  4/4
```

Predicted quantisation floor from the device's own calibration:

| Distance | shipped (whole px) | with 1/8 px |
|---|---|---|
| 2.0 m | ±18.6 cm | ±2.3 cm |
| 3.5 m | ±56.9 cm | ±7.1 cm |
| 4.5 m | ±94.0 cm | ±11.8 cm |

### OPEN — needs the camera
- [ ] **Confirm the banner reads `subpixel=on(1/8)`** when a crowd scene starts. This is the whole
      point: the previous state was a setting that silently did not apply.
- [ ] **Measure the fps cost.** Sub-pixel is VPU work on top of the left-right check already on. If
      it costs frames, that is the trade — it reverts with one flag.
- [ ] **Measure depth jitter on a stationary person** at 2.0 / 3.5 / 4.5 m, before and after. The
      table above is the quantisation floor, not a measurement; real jitter includes matching noise.
- [ ] **Re-check the ~2 m guidance** from ADR-018. If the numbers hold, the usable zone is a room
      rather than a spot, and that guidance can be relaxed.

### OPEN — the other half of the depth story
- [ ] **Mono 400p -> 800p** doubles `f` for another 2× (±3.6 cm at 3.5 m). Deliberately NOT bundled
      here so the fps cost of each can be attributed separately. `THE_800_P` is available on this
      device.

---

## F-40 — SR4 FINDINGS: ORIENTATION, MIRROR, WALKING, AND TWO WATCHDOG BUGS (2026-09-17)

Decision: **ADR-078** · Evidence: `Assets/Games/video/SR4.mp4`, Unity `Editor.log`,
`python-sidecar~/evidence/oak_v4/f20b/logs/run_001.txt`

### DONE — what the log confirmed already worked
- [x] Supervised path live: `sidecar supervisor started`, `SIDECAR CMD … --subpixel-bits 3`.
- [x] **`[mp]   stereo: … subpixel=on(1/8) …`** — ADR-077's 8× depth fix confirmed on hardware.
- [x] **Scene-driven sender switch** — `Scene 'Chain' needs multiperson_udp_sender.py (running:
      wholebody_udp_sender.py)`, and eight single-person scenes in a row with **zero** restarts.
- [x] **The supervisor restarted a dead sidecar** (`rc=0` → `RESTART delay=1s` → `run=2`).
- [x] `tracks=2 (held 1) dets=2 posed=1` with the held track greyed and not sent — ADR-075's
      phantom fix visible in the preview.

### DONE — bugs the same log exposed
- [x] **Walking out of frame killed the sidecar.** `HEARTBEAT TIMEOUT … no stdout growth for 10s`,
      `rc=1 uptime=463.9s`. The `frames=` heartbeat sat below `if not persons: continue`, so an empty
      room went silent and the watchdog — new to this sender as of ADR-075 — shot it. Heartbeat now
      runs first and reports the empty case honestly.
- [x] **ESC still killed the single-person sender** (`rc=0 uptime=30.6s`). ADR-075 fixed only the
      multi-person one; `wholebody_udp_sender.py` had three more `waitKey(1) in (27, …)` sites. All
      three now take `q` only.

### DONE — the four things reported
- [x] **1. Orientation unified.** `multiperson_udp_sender.py` gains real `--portrait` /
      `--portrait-dir` via `f18_portrait` (image + depth + intrinsics), detector boxes rotated with
      `rotate_box_normalised`. Supervisor forwards portrait to both senders. Unity exposes it PER
      SENDER (`sidecarSinglePersonPortrait` on, `sidecarMultiPersonPortrait` off) plus **O** in play
      mode to flip the running one and restart.
- [x] **2. The body walks.** `TrackedStage.FollowPosition` drives X/Z from the measured mid-hip,
      relative to a neutral captured on first sight and reset when the room empties. Y stays with
      the grounding loop. Crowd pair geometry unchanged — follow translates the whole group.
- [x] **3./4. The view is a mirror.** `flipX` true in `ExperienceBase`, `SceneLauncher`,
      `SkeletonShowBootstrap` and all **20** scene files.

### DONE — verification (headless)
```text
compile      SkeletonShow, Experiences, App, Tests            0 errors
unit tests   whole EditMode suite                             197 passed
             (same 9 runner artifacts as F-37/38/39)
box rotation rotate_box_normalised vs a REAL rotated image, both directions:
             ccw max err 0.0000 - cw max err 0.0000
supervisor   wholebody   … --portrait --portrait-dir ccw --subpixel-bits 3 --seconds 0
             multiperson … --max-poses 3 --subpixel-bits 3 --portrait --portrait-dir cw
             and --no-portrait on both when off
python tests tracker 19/19 - filters 46/46 - validation 22/22 - f20b d,e,f 4/4
```

### OPEN — needs the camera
- [ ] **Confirm the mirror is the right way round** — raise your right hand, the figure's hand should
      be on the same side of the screen as yours. Two people should now appear on the sides you see
      them on.
- [ ] **Walk left/right and near/far** and confirm the figure follows. Watch for hip-depth noise
      showing as drift; `FollowScale` and `FollowTau` (0.12 s) are the knobs.
- [ ] **Stand out of frame for 30 s** and confirm the sidecar is NOT restarted — this is the bug that
      froze PODIUM.
- [ ] **Press O** in play mode; confirm the log names the new orientation and the sidecar comes back
      with the matching `PORTRAIT_CCW` / `LANDSCAPE` banner.
- [ ] **Decide the real mount per column.** The intent is portrait for single-person and landscape
      for crowd; both previews in SR4 looked upright, which the code says should not be possible for
      both, so the physical mount needs confirming against the new banner.

### OPEN — known and deliberate
- [ ] **`PalmRotation` is handedness-inverted while `flipX` is on** (a reflection is not a rotation).
      Nothing reads it today; an experience that starts using palm orientation must fix it
      output-side, not by turning the mirror off. See ADR-023.
- [ ] **Portrait + multi-person degrades detection** — the detector runs on the unrotated stream and
      would see sideways people. Boxes are correct; recall is not. On-device rotation is the fix and
      needs the camera to validate.

---

## F-41 — THE SCENES NOW SHOW DEPTH (2026-09-17)

Follows F-40 · Reported as "experience in unity does not show depth, do something for it on all".

### DONE — first, confirmed the data was never the problem
```text
Unity's own recv_log.jsonl, 977 received frames:
    wrist    X range 1.138 m    Y range 0.616 m    Z range 0.792 m
    ankle    X range 1.352 m    Y range 0.749 m    Z range 0.955 m
```
Depth is present and swings nearly as far as left/right. It was a LEGIBILITY problem: `ShowStage`
puts the camera dead-on (2° tilt) 3.2 m away at 46° FOV, so a 0.79 m depth move is ~15% of apparent
size — real, rendered, and below what the eye reads.

### DONE
- [x] **`ShowStage.GroundPool` / `PlaceGroundPool`** — a dim glowing disc on the floor under a body.
- [x] **Wired into BOTH shared renderers**: `BodyRenderer` (all 19 experiences) and
      `GlowSkeletonMode` (launcher + Skeleton Show). That is what "on all" required — there are two.
- [x] **Not colour, not width.** Both are taken (speed, confidence) and `BodyRenderer`'s own header
      explains why doubling up on a channel is unreadable. Depth gets its own object.
- [x] **Mid-hip anchored**, floor-plane aware (`pose.FloorY`), hidden when the hips are not present
      and for `RenderRaw` bodies (echoes/replays).

### DONE — verification (headless)
```text
compile    SkeletonShow, Experiences, App, Tests    0 errors
unit tests whole EditMode suite                     197 passed (same 9 runner artifacts)
```

### OPEN — needs the camera, and needs an EYE
- [ ] **Does it actually read?** This is an aesthetic change and nothing headless can judge it. Walk
      toward and away from the camera and see whether the pool makes that obvious.
- [ ] **Is it too bright / too big?** `GroundPoolRadius` (0.26 m) and `GroundPoolDim` (0.25) are
      first guesses, duplicated in both renderers — if they need tuning, they should move to the
      palette rather than being edited twice.
- [ ] **Check the crowd scenes** — one pool per person, and they should not merge into a smear when
      two people stand close.
- [ ] **If it is still not enough**, the stronger lever is the camera: `ShowStage.BuildCamera` is one
      function used by every scene, and a modest yaw or a closer/wider setup would map Z onto screen
      X. Deliberately NOT done here — it changes the authored framing of all 20 scenes and the
      head-on mirror feel.

---

## F-42 — SR5: THE DETECTOR STALLS SILENTLY, AND A LATE FLOOR TELEPORTS THE BODY (2026-09-17)

Evidence: `Assets/Games/video/SR5.mp4`, Unity `Editor.log`,
`python-sidecar~/evidence/oak_v4/f20b/logs/run_001.txt` (110 KB, 18:12–18:46)

### DONE — "the sidecar closed and never came back" — it never closed
The process was alive the whole time at a healthy 30 fps. What died was the DETECTOR:

```text
frames=1457 sent=857 tracks=2 dets=2 posed=2 fps~14.9      <- last detection, ~97 s in
frames=1500..60714  sent=872 tracks=0 dets=0 posed=0 fps~30 <- 59,000 frames of nothing
```

`dets=0` for 33 minutes. No DepthAI warning, no exception, no exit. Unity sat on "WAITING for the
sidecar on UDP 8899" because `sent` stopped growing. fps rose 15 → 30 precisely because nothing was
being posed any more.

- [x] **This used to be caught BY ACCIDENT and F-40 removed the accident.** No detections meant no
      people meant no stdout, and the supervisor's heartbeat killed and restarted it. Fixing the
      heartbeat so an empty room does not trigger a restart (F-40, correct in itself) also made a
      stalled detector look perfectly healthy.
- [x] **`--det-timeout` (default 8 s).** The detector emits a packet per input frame whether or not
      it finds anybody, so silence on that queue is NEVER an empty room — it is always a stall. On
      timeout the sender prints `FATAL detector silent for …` and exits rc=3; the supervisor rebuilds
      the whole pipeline, which is the only thing known to clear it.
- [x] **`detAge~Ns` added to the heartbeat line**, so a stall is visible before it is fatal.

### DONE — "the leg was not recognized, and when found the skeleton jumped at 00:27"
**These are one bug, and the report describes the causal chain exactly.**

`SkeletonPose.HasFloor` only becomes true once a FOOT contact (heels/toes, landmarks 29-32) is
present. `TrackedStage.UpdateGrounding` returns early while `!HasFloor`, so `hasGroundOffset` stays
false — and the first time a floor DOES arrive it runs `groundOffset = target` as a single-frame
SNAP. The snap's own comment justifies it for a person who has just walked up; the condition is
really "the first frame WITH A FLOOR", which was 27 seconds in. Result: a whole-body vertical
teleport in front of somebody standing still.

- [x] **Snap only inside a 2 s acquisition window** (`GroundSnapSeconds`); ease over `GroundingTau`
      when the floor arrives late. A slide is the lesser evil once the scene has been on screen.
- [x] **An empty room now re-grounds as well as re-centres** — a new visitor is a fresh acquisition
      and gets the snap, not a slide.

### DONE — verification (headless)
```text
compile     SkeletonShow, Experiences, Tests    0 errors
unit tests  whole EditMode suite                197 passed (same 9 runner artifacts)
python      multiperson compiles - f20b d,e,f 4/4
```

### OPEN — the detector stall's ROOT CAUSE is not known
The fix makes it recoverable and visible; it does not explain it. It ran 97 s and then stopped
forever, with nothing logged. Two things new to this pipeline since it last ran for a long time,
both A/B-able from the Inspector:
- [ ] **Sub-pixel stereo** (`sidecarSubpixelBits` 3 → 0). Newest VPU load on this path.
- [ ] **`cam.preview` at 640x400** instead of DepthAI's default 300x300 — 2.8x more pixels through
      the preview → ImageManip → NN chain. Needed for FOV correctness (ADR-075), but it is load.
- [ ] Watch for `detAge~` climbing in the heartbeat before the timeout fires — that will show
      whether it degrades or stops dead.

### OPEN — why the legs were not recognised
NOT root-caused. The sidecar preview clearly HAS both feet (green landmarks on each) while Unity
drew one long straight line from the left hip and a stub on the right. So the data was there and
something downstream gated or mangled it. The subject was in a wide lunge at hip 1.35 m — very
close, strongly foreshortened legs.
- [ ] Reproduce in SKELETON SHOW and switch to the **trust HUD** (F-29 per-joint states), which
      reports which joints are rejected and why. That is the tool for this and it has never been
      pointed at a leg problem.

---

## F-43 — THE MOUNT: TILT COMPENSATION AND THE IR DOT PROJECTOR (2026-09-18)

Prompted by a setup question — camera height, subject distance, lighting, and "what if the camera is
tilted 15–30° like a game camera?" Answering it honestly required checking what the code actually
does about tilt. It did nothing, and the projector had never been switched on.

### DONE — a tilted camera shears the floor, and nothing corrected it

`PoseSpaceConverter` applied three axis SIGN FLIPS and no rotation. Neither sender contained the
string `tilt` or `pitch`. `SkeletonPose.FloorY` is a **single scalar**. So a pitch of θ made the
floor appear `tan(θ)` metres higher per metre of depth, and one scalar cannot describe a slope:
**0.27 m/m at 15°, which crosses the 0.08 m planted band in 30 cm of walking.**

F-18 §3 had already measured this without naming it — camera height derived from imagery sloped at
**+0.338 m/m**, implying ~19°, and `tan(19°) = 0.344`. F-40's `FollowPosition` then made it visible,
because the figure now moves in depth.

- [x] **`PoseSpaceConverter` rotates about X, before the mirror signs** (ADR-079). Applied to both
      entry points — the per-joint landmarks and the mid-hip `xyz` — because both come from one
      back-projection in the sender and splitting them would tear the root off the body.
- [x] **`CameraMount.TiltDegrees`: one static, not a serialized field in four components.** `flipX`
      is declared four times and F-40 paid for that across twenty scene files. `AppBootstrap.Awake`
      publishes it before the first scene loads, so a scene-launched converter cannot disagree.
- [x] **`AppBootstrap.cameraTiltDegrees`**, defaulting to 0 — a level mount is bit-identical to
      pre-F-43 behaviour.
- [x] **11 tests** (`PoseSpaceConverterTiltF43Tests`), including the one that matters: a real floor
      sampled at 1.5 m and 2.5 m through a 20°-pitched camera must resolve to ONE height, and the
      uncorrected samples must genuinely disagree first or the test proves nothing.

The **sign is deliberately not automated.** `mount.json` records magnitude only and says so; F-19 §7
disproved the board's identity IMU-to-camera extrinsic directly. A wrong sign doubles the error.

### DONE — the IR dot projector had never been turned on

DepthAI leaves the emitter off by default and nothing in this repo ever called the setter, so every
stereo measurement taken by this project has been on an unassisted pair. F-17 §8's finding is what
makes it free: **CAM_A is IR-cut colour, CAM_B/CAM_C are unfiltered mono** — the dots land in the
images that do the matching and never in the RGB frame the pose model reads.

- [x] **`oak_depth.enable_ir_dot_projector`**, default 0.8, guarded by `getIrDrivers()` + the
      setter's bool + try/except so a non-PRO board is unaffected.
- [x] **`--ir-dot` in both senders and forwarded by the supervisor**, so the two columns cannot
      disagree about the same room (the F-39 lesson).
- [x] **Declared in `f20b_fake_sidecar.py` too.** That stub uses strict `parse_args` precisely to
      catch the supervisor forwarding a flag the real sender would reject — and it caught this one.
- [x] **Verified live**: `[wb] IR dot projector: on (80%, driver LM3644)` in
      `evidence/oak_v4/f20b/tests/a_normal_start/logs/run_001.txt`, F-20B 6/6 PASS with the camera
      connected.

### The setup numbers this produced

Derived from the device's own calibration (`f·B` = 21.54, mono `fx` = 287.157) and cross-checked
against F-18's measured framing — the model reproduces F-18's "1.12 m horizontal coverage at 0.80 m"
and its ~19° implied tilt exactly, so the extrapolations are anchored, not guessed.

| | single person (portrait) | multi-person (landscape) |
|---|---|---|
| camera height | 0.80–0.85 m (F-18 used 0.81 m) | 1.1–1.2 m — **1.5 m cuts the feet off at 2 m** |
| subject distance | **0.90 m** (1.00 m if T-pose must fit) | **2.0–2.5 m** |
| depth precision there | 4.7 mm | 2.3–3.6 cm |
| body height in pixels | 537 px | 242–194 px |
| tilt | level within 2°, or set `cameraTiltDegrees` | same |

Light must come from **behind the camera**, diffuse, never in frame and never behind the subject.
Backlight turns the subject into a silhouette, which loses limb keypoints before it loses the person
— dark trousers against a bright floor is the classic missing-legs case. Sunlight is the worst
offender because it is IR-rich enough to swamp the projector as well.

### NOT DONE — open

- [ ] **Nothing here is camera-verified beyond the boot banner.** The tilt correction has been tested
      in arithmetic, not against a real angled mount. Setting `cameraTiltDegrees` and walking toward
      and away from the camera is the acceptance test, and it has not been run.
- [ ] **The projector's win is unquantified.** No before/after hole-rate or valid-depth-percentage
      measurement was taken. `RETARGET_AUDIT_2026-08-11.md` recorded "~63% of keypoints get measured
      depth" — that is the number to re-measure, and it is the obvious A/B.
- [ ] **Camera height is still not tape-measured.** F-18 and F-19 both list it as outstanding and
      `mount.json` still reads `"height_m": null`. The 0.81 m figure is derived from imagery using
      the same tilted mount that biases it.
- [ ] **The mount is 5.89° out** against `f19_level.py`'s own 2° tolerance. Either level it or set
      the tilt; right now neither has been done.
- [ ] **Live tilt tuning.** The value is read at converter construction, so changing it needs a
      restart. `PoseSpaceConverter.SetTiltDegrees` exists and is live-safe if a knob is ever wanted.

---

## F-44 — THE WORKING VOLUME: THE PIPELINE WAS THROWING AWAY HALF THE SENSOR (2026-09-18)

Working volume was the largest gap in the system's own self-assessment — excellent pose quality
inside about a one-metre box and nothing outside it. It was not the lens and not the baseline
(F-17 closed both). **Both OV9782 sensors are 1280×800 and the pipeline ran them at 640×400.**

### DONE — two independent 2× levers, neither of which costs GPU time

- [x] **`--rgb-isp 1/1`** — native 1280×800 at FULL FOV. Verified: `fy` 284.627 → **569.254**
      (exactly 2×) with **FOV unchanged at H 70.22° / V 96.70°**, so nothing is cropped and no
      framing minimum moves. Doubles the pixels on a body, which is what sets the distance at which
      the pose model can still resolve one.
- [x] **`--mono-res 800p`** — mono `fx` 287 → 574, so `f·B` doubles 21.54 → 43.07 and **depth error
      halves at every range.**
- [x] **`build_rgbd_pipeline` now APPLIES `mono_res`.** It was a declared parameter that the body
      ignored in favour of a hard-coded `THE_400_P`, while `stereo_config_str()` reported
      `STEREO_CONFIG["monoRes"]` — so a caller could ask for 800p, be told it got 800p, and run at
      400p. Same defect class as ADR-077's sub-pixel banner, in a second place, undetected.
- [x] Forwarded by the supervisor to **both** senders; Unity fields `sidecarMonoResolution` /
      `sidecarRgbIspScale`; declared in `f20b_fake_sidecar.py` so the strict stub keeps catching
      flag drift.

### MEASURED — `tools/capture/f44_resolution_ab.py`, on the device

Empty room, sub-pixel 1/8, IR dot on, 150 frames per config after a 30-frame warm-up:

| config | rgb/depth | fps | valid depth | px on a 1.7 m body @2 m | @4 m |
|---|---|--:|--:|--:|--:|
| 400p + ISP 1/2 (as shipped) | 640×400 | 30.0 | 14.3% | 242 | 121 |
| 800p + ISP 1/2 | 640×400 | 29.1 | 18.3% | 242 | 121 |
| 400p + ISP 1/1 | 1280×800 | 30.0 | 13.5% | 484 | 242 |
| **800p + ISP 1/1 (new default)** | **1280×800** | **29.2** | **19.9%** | **484** | **242** |

**2× pixels on target and +5.6 pp valid depth for 2.6% of the frame rate.** Nearly free for a
structural reason, not a lucky one: RTMW3D always resizes its crop to 288×384 and the detector always
runs at 544×320, so **neither sees the source resolution** and the 20.4 ms/person pose solve — the
thing that caps crowds at 16 fps — is untouched. The cost is VPU matcher time and USB bandwidth.

Confirmed through the supervisor with the real model loaded, and F-20B **6/6 PASS** on the new
defaults with the camera connected.

### What this buys

The distance at which the pose model sees a given level of detail **doubles**: 242 px was 2.0 m, it
is now 4.0 m. Usable band widens from roughly 1.4–2.5 m to **1.4–4.5 m**, and because width grows
with distance, floor area grows **~3–4×**. Depth at 4 m now quantises at 46 mm against interaction
tolerances of 130–450 mm in the experiences — **depth precision is no longer what limits range;
pixels on target are.**

### NOT DONE — open

- [ ] **Never measured with a person in frame at full resolution.** The valid-depth column is an
      empty room, so it compares configurations rather than describing tracking. The number to
      re-measure is `RETARGET_AUDIT_2026-08-11.md`'s "~63% of keypoints get measured depth".
- [ ] **Never run with three people at full resolution.** Single-person is confirmed at 29.3 fps; the
      crowd case adds a saturated GPU to a 4× depth map and has not been tried.
- [ ] **The range claim is arithmetic, not a capture.** "Good at 4 m" follows from pixels-on-target
      matching today's 2 m, which is measured good (93.7% plausible hands at 1.96 m, F-29 corrected).
      Nobody has stood at 4 m.
- [ ] Rollback if any of the above disappoints: `--mono-res 400p --rgb-isp 1/2`.

---

## F-45 — MULTI-PERSON FRAME RATE: FP16, AFTER BATCHING AND TENSORRT WERE RULED OUT (2026-09-18)

Multi-person ran at **16 fps with three people**, and F-34 records the consequence: the crowd speed
constants were tuned at ~21 fps and have been running at 16. Frame rate was the largest single term
in that feature's quality.

### DONE — profile first, because both candidates only help the GPU

| stage | time | share |
|---|--:|--:|
| preprocess (CPU) | 2.42 ms | 11.4% |
| **`session.run` (GPU)** | **18.75 ms** | **88.1%** |
| decode (CPU) | 0.11 ms | 0.5% |

15.7 fps for three people, which reproduces F-34's measured 16 — the profile describes the real
system, and 88% on the GPU is worth attacking.

### DONE — batching: built, correct, and REJECTED on measurement

The model pins batch to 1 (`[1,3,384,288]`), so this was a graph change. The rewritten graph is
**verified correct** — every row of a batch-3 run equals the batch-1 result — and still loses:

| case | ms/person | vs production |
|---|--:|--:|
| static batch=1 (production) | **18.50** | — |
| dynamic batch=1 | 26.26 | **+42%** |
| dynamic batch=2 | 19.53 | +6% |
| dynamic batch=3 | 17.30 | −7% |

A dynamic batch dim costs 42% at batch 1 because DirectML stops specialising on a fixed shape. The
room usually holds one or two people. Rejected. **Beware short warm-ups** — the first run of this,
at 8 iterations, reported batch-2 at +129%, wrong in the other direction.

### DONE — TensorRT: assessed, NOT NEEDED

Requires swapping `onnxruntime-directml` for `onnxruntime-gpu` (they cannot coexist) plus CUDA,
cuDNN and TensorRT — `nvcc` is not on PATH, no cuDNN present, no `tensorrt` package. After FP16 it
buys nothing visible: three people run at 37 fps against a **30 fps camera**. Revisit only if
`--max-poses` goes past 4.

### DONE — FP16 is the win

| | fp32 | fp16 |
|---|--:|--:|
| GPU stage | 18.59 ms | **6.25 ms (2.97×)** |
| per person, end to end | 21.51 ms | **8.99 ms** |
| 1 person | 46.5 fps | 111.3 fps |
| **3 people** | **15.5 fps** | **37.1 fps** |
| 5 people | 9.3 fps | 22.3 fps |

Accuracy measured on real people (60 frames of `123.webm`, same crop box both models), because
argmax over a random activation map measures noise rather than the model:

```
confident joints (conf > 0.3), 7,740 samples
  median 0.000 px | p95 2.652 px | max 78.772 px
  > 2 px: 5.620%   > 5 px: 0.426%   > 20 px: 0.090%  (7 frames of 60, median 1 joint of 133)
```

The median joint does not move. The tail is ~1 joint in 1000, which P0's One-Euro and the
`--arm-max-jump` / `--leg-max-jump` caps already exist to absorb.

- [x] `tools/model/f45_make_fp16.py` — converter **and** the accuracy check, re-runnable.
- [x] `evidence_paths.DEFAULT_MODEL` prefers the fp16 file **when present** — the `.onnx` files are
      not in version control, so a hard default would break any machine that never converted.
- [x] Both senders print the weights loaded: `model: rtmw3d-x-fp16.onnx (185 MB)`.
- [x] F-20B **6/6 PASS** on the real camera; readiness 13.8 s → **6.8 s** (185 MB loads faster).
      5/5 python unit suites pass.

### NOT DONE — open

- [ ] **Never run with three real people.** The 37.1 fps is a per-person measurement multiplied by
      three, not a crowd capture. Nobody has stood in front of it.
- [ ] **The FP16 tail has not been seen on screen.** ~1 joint in 1000 making a >20 px excursion
      should be invisible after P0, but "should be" is not "was observed to be".
- [ ] **`--max-poses` is still 3** though the GPU now affords 4–5 at camera rate. F-34's experiences
      were designed around three; raising it is a design decision, not a budget one.
- [ ] **numpy hazard, recorded.** Installing `onnx` + `onnxconverter-common` upgraded numpy
      1.26.4 → 2.2.6 under a system whose measured evidence base was produced on numpy 1.x. Pinned
      back; the converter still runs on 1.26.4 and `ml-dtypes`' warning can be ignored. Do not let
      model tooling move numpy under the capture path.

---

## F-46 — SHOOT SUPPORT: PREFLIGHT, DIRECTOR, RUN OF SHOW (2026-09-18)

A demonstration video is wanted as proof and as a closing note, shot **solo**. Two constraints shaped
what was built: one person cannot perform at 0.9–4 m and operate a keyboard at the same time, and
everything F-43/44/45 changed is still unverified on camera — so **the shoot is the verification
pass**, and the tooling has to make a wrong rig visible before filming rather than after.

### DONE

- [x] **`tools/capture/f46_preflight.py`** — twelve checks, one verdict. Interpreter (the CPU-fallback
      trap), model precision, sub-pixel, resolution, UDP 8899, crowd footage, camera, IR projector,
      frame size, throughput, valid depth, mount tilt from the IMU. It reads the **supervisor's**
      `--subpixel-bits` default rather than the bare module default, because the module ships
      sub-pixel off and every production launch turns it on — checking the default would report a
      configuration nothing runs.
- [x] **`Runtime/SkeletonShow/DemoDirector.cs`** — walks the run of show on a timer, loads scenes,
      switches SKELETON SHOW modes, and captions what each shot proves. Installed by
      `[RuntimeInitializeOnLoadMethod]` and **dormant until F9/F10/F12**, so no scene contains it and
      no Inspector configures it — the F-40 mirror problem is not repeated for a demo aid. Two
      sequences, not one: crossing the single/crowd columns swaps senders (15–20 s of no tracking),
      and with the crowd half driven from a recorded file an automatic swap would kill the process
      feeding it.
- [x] **`docs/F46_DEMO_RUN_OF_SHOW.md`** — printable. Six acts, exact scenes and keys, what each
      shot proves, and what to do when something looks wrong instead of quietly retaking.

### FOUND — the preflight earned itself on its first run

Mount tilt read **88.0°**: gravity has moved from the IMU's Y axis (where `mount.json` recorded it at
5.89° on 2026-09-11) to its Z axis, and F-19 §7 established that **Z is the optical axis**. The
camera is lying flat facing up, not aimed at a room. 490 clean samples, |g| = 9.659.

That also exposed a flaw in the tool, since fixed: it advised setting `cameraTiltDegrees` to 88,
which is obediently wrong. Above 45° it now says the camera is not mounted rather than offering to
compensate a non-tilt.

### NOT DONE — for the shoot itself

- [ ] The camera has to be mounted and the preflight has to say READY.
- [ ] **Act 1 is the tilt test**, Act 2's DEPTH REACH beat is the **4 m range test**, and every act
      exercises FP16. None of those have been confirmed.
- [ ] Act 4 runs from `456.webm` because the shoot is solo — **no stereo depth from a video file**,
      which the captions state on screen. Live crowd tracking stays unverified.
- [ ] Act 5 (kill the sidecar on camera, watch it recover) is the one shot that has never been filmed
      and is the strongest production-readiness claim available.

---

## F-47 — SR6: THE SIDECAR TRACKS, UNITY DOES NOT, AND F-21 IS WHY (2026-09-18)

Evidence: `Assets/Games/video/SR6.mp4` (6m21s, one person). Decision: **ADR-083**.
Status: **DIAGNOSED, NOT FIXED.** A shoot-time workaround exists; the production fix is specified
and deliberately deferred.

### The report
1. tracking quality degrades over a session
2. **Unity does not react while the sidecar preview tracks perfectly**
3. the sidecar sometimes loses the subject completely

### DONE — root-caused, all three from one mechanism

(2) is not a tracking failure. `wholebody_udp_sender.py` draws `_preview(...)` **inside** the
`if not should_emit:` branch and skips the `sendto` — so a withheld frame looks like a perfect
preview and a frozen Unity, which is precisely what was reported.

Measured from SR6: `switches=12` by 5:20 and `13` by 6:15, repeated `F-21 NO TARGET`, and Unity
reading `packetAgeMs=994.0 clampNewest=2048` — **thirteen ownership switches with one person.**

The trap, in `target_ownership.py`: `LOCKED` rejects an observation whose hip moved more than
`SWITCH_MARGIN_M = 0.35` from the owner (`position_jump`), **and does not update `owner_pos` on
rejection.** The subject is then permanently outside their own margin, every later frame is rejected
identically, and `RELEASE_TIMEOUT_S = 4.0` eventually releases and re-acquires them as a new owner.
Positive feedback, not a merely-tight threshold.

(3) is the same state: `NO_TARGET`/`RELEASED` resets the crop to `R.center_bbox(...)`, so an
off-centre subject drops out of the pose crop and the **preview** loses them too. (1) is these
accumulating.

**It surfaced now because of our own work:** F-40 added `FollowPosition`, F-44 took the usable band
to ~1.4–4.5 m, and F-46's run of show instructs the subject in writing to walk toward and away from
the camera. F-21 assumes a subject standing at 0.90 m and not moving.

### DONE — the shoot is unblocked

- [x] Start the sender by hand with `--no-ownership` and let `SidecarBootstrap` attach (the
      supervisor deliberately refuses to forward `--ownership`; uncheck `Auto Start Sidecar` first
      so Unity cannot race it). Exact command in ADR-083.
- [x] Middle path recorded: `--ownership-switch-margin 1.2` keeps the protection and tolerates a
      step.

### NOT DONE — the production fix

- [ ] **Capture the reason before tuning.** The sender already logs `[wb] TARGET ... reason=...` and
      `--ownership-log-dir` writes it per event. ADR-083 establishes the MECHANISM; it does not
      establish whether `position_jump` or `scale_mismatch` dominates live.
- [ ] **Close the feedback trap** — update or decay `owner_pos` toward a rejected-but-plausible
      observation so one fast movement cannot strand the owner outside its own margin. Smallest
      correct change; does not widen the gate.
- [ ] **Consider a velocity-aware margin** instead of a fixed metre count.
- [ ] **Do NOT simply raise `SWITCH_MARGIN_M`** — ADR-061 measured a real identity migration at
      0.015 m/frame, 23x under the existing margin.
- [ ] Nothing filmed with `--no-ownership` demonstrates target ownership. Say so if the footage is
      ever used as evidence for F-21.

---

## F-48 — SR7: THE TRANSPORT IS FIXED, THE INPUT IS OUT OF RANGE, AND THE CALIBRATION LEARNED FROM IT (2026-10-09)

Evidence: `Assets/Games/video/SR7.mp4` (200.6 s, one person, sender run with `--no-ownership`).
Decision: **ADR-084**. Status: **IMPLEMENTED AND UNIT/INTEGRATION-TESTED; NOT YET FILMED.**

**Provenance of the SR7 figures below:** they are the analysis handed to this task (frames extracted
with `ffmpeg` at ~1100 px). They were not re-extracted in the session that wrote F-48; the code and
tests were built against them.

### What SR7 established

SR6's failure (F-47, ADR-083) was transport starvation caused by F-21 gating the UDP send. SR7 ran
`--no-ownership`, so it proves the workaround bypasses F-21 and **says nothing about whether F-21 is
fixed** (Unity's TRUST HUD read `ownership not running (--no-ownership)`; the sidecar HUD had no
`F21` line). With F-21 out of the way the transport was healthy: `packetAgeMs` 8-31 ms (SR6: 994),
`clampNewest` 20-49 (SR6: 1177-1750), ~40 ms end to end, `parseErr=0`, sender 29.0-29.6 fps, Unity
163-194 fps. **Do not regress this** — nothing in F-48 touches the receive path.

What was wrong was the INPUT. The subject spent minutes at **0.91, 1.07-1.28 and finally 0.66 m**
hip depth, against F-44's usable band of ~1.4-4.5 m. Landmarks dropped out of the preview, the TRUST HUD
read 17-21 of 33 joints measured, and everything downstream still ran on it:

| symptom | cause |
|---|---|
| debug skeleton collapses to a vertical line (~104-112 s) | inferred-depth joints fed the body |
| avatar driven toward the camera (~192 s, hip 0.66 m) | `FollowPosition` / `ApplyRootPosition` obeyed the root |
| stretched bones in still frames | `LearnBoneLengths` accepted any pair that was merely *valid*, and the global band was 0.01-0.80 m |
| left palm 147 mm but thumb-index 2273 mm (~48 s) | the palm gate checks one segment; one fingertip can be metres away |
| "OUT OF RANGE - step closer" | one message for every hand refusal, including a hand that was too close |
| `maxCorr` / bone error missing from the capture | a single ~220-character HUD line ran off an 1100 px frame |

### DONE — Stage A: the operating volume

- [x] **`OperatingVolume`** (static, like `CameraMount`): near **1.35 m**, far **4.5 m**, hold **0.75 s**,
      settle **6 poses**, hysteresis 5 cm. Published from `AppBootstrap`'s Inspector
      (`Operating Volume (F-48)` header), so it is inspectable and tunable — not a constant.
- [x] **`OperatingVolumeGate`**, one per consumer. Judges `|root.z|` — the figure the TRUST HUD already
      prints — into `InVolume / TooNear / TooFar / RootInvalid / NoRoot`, and into an admission:
      **Outside** (hold), **Settling** (draw, don't learn), **Trusted**.
- [x] **Wired into all three consumers** that run the humanize chain: `TrackedStage` (every experience),
      `SkeletonShowBootstrap`, `AppBootstrap` (avatar). Outside the volume the last good body is HELD for
      0.75 s, the walk-follow does not move and its neutral is never captured, and calibration learns
      nothing; after the hold the stage withholds the body and says why. **No clamping** — see ADR-084.
- [x] **A person standing too close is not an empty room**: `TrackedStage` skips the attract figure when
      `live` is true, so the synthetic loop never appears over somebody who is merely out of range.
- [x] Receive telemetry is untouched. The gate counts **poses, not render frames**, so a 190 fps loop
      cannot report a rejection rate that belongs to the display.

### DONE — Stage B: calibration only learns from trustworthy evidence

- [x] **Measured-depth provenance** reaches the humanize layer through the existing trust channel —
      `HumanizedSkeleton.Process(frame, dt, TrackingTelemetry, PoseAdmission)` reads
      `TrackingTelemetry.DepthMeasured(joint)`. No parallel source of truth; the old two-argument
      `Process` is kept and means "provenance unknown".
- [x] A bone is observed only if both endpoints are valid **and** both measured **and** the pose is
      `Trusted`. Inferred joints are still output, rebuilt and held exactly as before — the rule gates
      learning, not output.
- [x] **Per-bone anatomical bands** replace the 0.01-0.80 m global one (`HumanBodyModel.MinBoneLengthM` /
      `MaxBoneLengthM`, one band per bone, adult 1st-99th percentile widened for landmark-vs-joint
      offsets). A 55 cm forearm used to pass.
- [x] **A bad side cannot pollute the good side**: left/right are averaged only when their medians agree
      to 20 %; otherwise each keeps its own. One validated side still serves an occluded other.
- [x] **Re-entry**: a held gap drops the temporal memory (`DropTemporalMemory`), so a subject who walks
      back in is not velocity-clamped against where they stood seconds earlier (mutation-tested: without
      it a walk back in is clamped 10 times as a "teleport").

### DONE — Stage C: the correction layer is legible

- [x] New counters: `CalibSamplesAccepted`, `CalibRejectedUnmeasured`, `CalibRejectedVolume`,
      `CalibRejectedImplausible`, `FramesHeldOutOfVolume`. Unit = one bone sample.
- [x] `HumanizedStats.ToHud(...)`: five short lines (calibration `n/24 bones ready`, trunk+head length,
      `maxCorrection`, `maxBoneError`, every rejection reason). Worst case with 8-digit counters is
      **<= 130 characters per line** (asserted).
- [x] `TrunkAndHeadLength()` is now shown. It earns its place as a tape-measure check: it is the sum of
      two bands, so a value outside ~0.42-1.10 m says calibration converged on something that is not a person.
- [x] HUD layout: every label is the **screen's** width, the SkeletonShow block is anchored to the bottom
      and its height is counted, and the mode panel is clamped to the room left of the buttons.
      `ExperienceBase` gains a `VOLUME ...` line and a large TOO CLOSE / TOO FAR warning.
- [x] The avatar app logs and reports (`DiagnosticsHudPanel`) every volume transition.

### DONE — Stage D: hand safety

- [x] **Per-landmark validation** in `HandPose`: every landmark within 2.8 palms and 0.30 m of the hand's own
      wrist, thumb-index within 3.0 palms, non-finite refused. The SR7 hand (palm 147 mm, thumb-index
      2273 mm = 15.5 palms) is refused; **each hand is judged on its own landmarks**, so a bad left leaves a
      good right usable.
- [x] `HandRange` (`NotTracked / Ok / TooFar / TooCloseOrUnreliable`), plus `RejectedPalmFrames`,
      `RejectedLandmarkFrames` and `WorstReachPalms` per hand, counted per datagram.
- [x] `HandRangeAdvice`: only a palm too SMALL says "move closer". A palm too large, or a fingertip that has left
      the hand, says "TOO CLOSE or depth unreliable - hold your hand further from the camera". A too-near
      body outranks any hand and says "step back". Air Graffiti and the HANDS HUD use it.

### MEASURED — verification, and what it does and does not prove

Run headlessly (the Editor holds the project lock; no batch mode):

| check | result |
|---|---|
| `dotnet build VirtualMirror.Tests.csproj` / `VirtualMirror.App.csproj` | 0 errors |
| Unity's own recompile (Editor open) | `Library/ScriptAssemblies` refreshed, no `error CS` in `Editor.log` |
| `NearFieldSafetyF48Tests` (60 tests) on real UnityEngine | **47 pass, 13 fail** — the 13 all reach `Quaternion.LookRotation` inside `HumanizedSkeleton.Process`, a native ECall |
| same 60 under a managed stand-in for `Vector3/Quaternion/Mathf` | **60 / 60 pass**, including two end-to-end `TrackedStage` tests over a real UDP socket |
| existing `HumanizedSkeletonSelfTest` (F-27) under the stand-in | **32 / 32 pass** — the layer's old behaviour is intact |
| full suite on real UnityEngine | 261 pass, 33 fail = **13 mine (above) + 19 ECall + 1 missing `AnimationModule`** — the last 20 are pre-existing, in code F-48 does not touch |
| mutation: disable the measured-depth rule | 3 tests fail (`Expected 30, was 90`) |
| mutation: let near-field frames drive the follow position | the end-to-end test fails with the figure at **z = -1.28 m**, ~1.9 m toward the lens — SR7's camera-rush, reproduced |
| mutation: keep temporal memory across the gap | the recovery test fails (`VelocityClamped` 0 -> 10) |

The 13 that need the stand-in are **real tests** that will run in the Editor Test Runner as written;
they cannot run on the bare .NET runner for the same reason the project's own `ArmAimSolverTests` cannot.
The stand-in is a scratch tool, not part of the project, and self-checks its quaternion algebra
(`LookRotation`, `AngleAxis`, `Inverse`, `RotateTowards`, `Slerp`) before any result is read.

The baseline you quoted (~208 passing, 9 known infrastructure failures) came from a different runner; the
absolute counts here are not comparable to it. What is comparable is the **set**: every failure outside
`NearFieldSafetyF48Tests` is an `ECall` or a missing engine module.

### NOT DONE — open, and said plainly

- [ ] **The on-camera acceptance recording was not made.** It needs a person and the camera. Nothing above
      is camera-verified. Protocol is in `F46_DEMO_RUN_OF_SHOW.md` §6; the four things to read off it are
      the VOLUME line, the `rejected unmeasured-depth / out-of-volume` counters, `calibration n/24`, and
      whether the figure rushes the lens at the boundary.
- [ ] **1.35 m and 4.5 m are derived, not measured.** Near comes from F-44's 1.4 m edge and SR7's
      degradation; far is F-44's pixels-on-target arithmetic, which F-44 itself lists as not a capture. The
      3-4 m take is what confirms or moves them. They are Inspector fields precisely so that is cheap.
- [ ] **This changes the product at 0.90 m.** F-43's setup table and F-46's run of show both say a single person
      stands at 0.90 m. That is now `TOO CLOSE` and the avatar holds, then stops being driven. If 0.90 m is
      still wanted as the Mirror's operating point, F-44's 800p change needs a capture AT 0.90 m, and the near
      limit becomes a per-scene setting. **This is a product decision, not a bug**, and it is flagged rather
      than decided.
- [ ] **Crowd bodies are not volume-gated.** Only the primary person's root is judged; the crowd path
      (`UpdateCrowd`) is unchanged. A crowd member too close is not held.
- [ ] **The avatar's own finger curls and palm rotation are not covered by Stage D.** `OakDUdpPoseProvider.ParseHands`
      still feeds `HandFrame` (curls, `PalmRotation`) from the raw `lh`/`rh`. Curls are angle-based and the palm is
      already rate-limited, but a blown-up fingertip can still reach them. Stage D protects `HandPose`
      (experiences, SkeletonShow), not the avatar retarget.
- [ ] **HUD layout is verified by counting characters, not on a screen.** IMGUI wrap behaviour at 1100 px has not
      been looked at in the Editor.
- [ ] **`AppBootstrap` integration is verified by compile only.** It needs the avatar scene and a camera.
- [ ] **A hand is not checked against the body's wrist.** `HandPose` bounds landmarks against the hand's own wrist;
      comparing that wrist to landmark 15/16 would also catch a whole hand placed wrongly.
- [ ] **Stage E / ADR-083 / F-21 is untouched and still open.** `target_ownership.py` was not edited and nothing here
      tests ownership. SR7 does not show it fixed.

---

## F-49 — RUN THE SAME TRACKING ON A VIDEO: A LINK AND A TICKBOX (2026-10-09)

Decision: **ADR-085**. Status: **IMPLEMENTED; verified end to end headlessly; NOT yet seen in the Editor.**

Wanted: give Unity a video link and tick a boolean, and have everything — the skeleton show, every
experience, the VRM avatar — run exactly as it does live, with inference on the video instead of the
OAK-D feed.

### DONE

- [x] **`tools/video/video_udp_sender.py`** — the production single-person pipeline (RTMW3D, production
      landmark builders, P1-1 states) on a video, sending the **identical datagram**, so nothing in Unity
      changes. Source may be a local path, a **direct** http(s) link (downloaded once, cached, atomic via
      `.part`, 2 GiB cap) or a page via `yt-dlp` **if installed** (it is not in the venv). Loops forever by
      default; after a finite `--loop` it idles instead of exiting, because the supervisor would restart it.
      Every frame is paced to the clip's own clock — including skipped ones, which the first run showed
      racing ahead on a clip that opens on an empty room.
- [x] **Supervised, not direct.** `sidecar_supervisor.py` gained a `video` sender kind (`SENDER_BY_SCRIPT`),
      with its own flags; none of the camera flags are forwarded to it (and `--show` is not, it has no
      window). The sender prints the supervisor's readiness markers and a 2 s heartbeat, **including while
      downloading and loading the model**, or the 10 s watchdog would kill a slow download.
- [x] **`AppBootstrap > Video Test Source (F-49)`**: `Track From Video` (the boolean), `Video Source`
      (link, absolute path, or project-relative `Assets/Games/video/SR7.mp4`), `Video Loop`,
      `Video Assume Measured Depth`, `Video Subject Height Metres`, `Video Horizontal Fov Degrees`,
      `Video Clamp Depth` + range. Applied at boot **and live**: edit a field in Play mode and the sidecar
      restarts a second after the typing stops (the model reloads, ~10 s). A sidecar Unity did not start
      is never restarted.
- [x] **`VideoSourceSpec`** validates the Inspector text in Unity first — http/https or an existing file, no
      quotes, no control characters, bounded length — so a typo is one clear console line, not a Python
      traceback in a log. The sender re-validates independently.
- [x] One sender for every scene: crossing the single/crowd columns does **not** trigger the 15–20 s sender
      swap, because there is no crowd video sender.
- [x] The HUD/launcher footer names the source: `VIDEO SR7.mp4 (UDP sidecar, no stereo)`.

### MEASURED

| check | result |
|---|---|
| `tests/test_video_source.py` (classification, loopback-HTTP download, cache, `.part` cleanup, size cap, yt-dlp-absent message, supervisor command vs the sender's own argparse) | **46 / 46** |
| `VideoSourceF49Tests` (Inspector rules + the C#-built command line, incl. a `de-DE` culture check) | **13 / 13** |
| full C# suite under the managed stand-in | **276 pass, 31 fail** — the same 31 stand-in-gap failures as before F-49 (`Color`, `Vector2`, `MonoBehaviour`…); +13 is exactly the new fixture |
| real RTMW3D on `SR7.mp4`, DirectML, through the real supervisor | wire correct: monotonic `seq`, one `sid`, `lm lh rh xyz src st lat`; every `src` = 0 |
| **full chain**: supervisor → video sender → Unity's real `OakDUdpPoseProvider` + `TrackedStage` over UDP, 85 s | 1930 datagrams, **parseErr 0**; body live on 2234/2994 ticks (the first ~19 s of SR7 is an empty room); hands read; F-48 gate `TRUSTED` |
| same chain, default (depth labelled inferred) | `measured 0/33`, calibration **0/24**, 40 128 `unmeasured-depth` refusals — the F-48 rule working as designed on a source with no stereo |
| same chain, `--assume-measured` | `measured 33/33`, calibration **18/24 bones**, trunk+head **0.73 m** (plausible), `impossible-length` 2133 refusals (the per-bone band doing its job on monocular noise) |
| hand gate on the video stream | both hands `ok` most of the time, refused at moments: L palm 278 / landmark 111, R palm 314 / landmark 154 refusals over 85 s — **monocular hand depth is noisy and F-48's gate is catching it**, which is a finding about the source, not a defect |

### NOT DONE — open

- [ ] **Not run inside the Editor.** Everything above ran on the real Unity assemblies plus a managed stand-in for
      `Vector3/Quaternion/Mathf`, driving `TrackedStage` directly. `AppBootstrap`'s new fields, the
      live-restart watcher and the launcher flow were compiled (`dotnet build VirtualMirror.App.csproj`, 0 errors) but
      not exercised, and Unity's own recompile of the new `Tracking` file had not happened yet when this was written.
- [ ] **No crowd.** The multi-person detector is an OAK-D VPU blob; there is no host-side copy. A crowd scene fed from
      video sees one person. A host-side detector (ONNX) would be the next step if that is wanted.
- [ ] **No stereo, so no real depth.** Hip depth is `subject height × fy / pixel span`. The default clamp (1.6–4.0 m)
      keeps a clip inside the F-48 volume; SR7's raw estimate was 3.7–6 m and would have read TOO FAR. Untick the
      clamp to test the volume against a clip.
- [ ] **A page link (YouTube etc.) is untested** — yt-dlp is not installed, by design. The code path and its
      absent-yt-dlp message are tested; the happy path is not.
- [ ] **A direct link was tested against a loopback server, not the internet.** Redirects, auth and CDNs are as
      `urllib` handles them.
- [ ] A finite loop cannot be told to "stop and show the last frame"; it goes stale. Deliberate (ADR-085) but unconfirmed as the
      behaviour you want.
- [ ] The scratch tools used to verify this (the Unity stand-in, the reflection runner, `StageRun`) are not in the repo.

### ADDED 2026-10-09 — PHONE / WEBCAM / LIVE STREAM SOURCES (same field, same sender)

`Video Source` now also takes **`camera:N`** (a webcam; a phone becomes one when an app such as DroidCam, Iriun or
Camo exposes it to Windows), **`rtsp://…`**, **`http://192.168.x.x:PORT/video`** (a phone "IP camera" app over Wi-Fi;
an http link to a LAN address is a camera, not a page) and **`stream:<url>`** to force it. New fields:
`Video Rotate Degrees`, `Video Flip Horizontal`, `Video Camera Size`.

- [x] **`LatestFrameReader`**: a reader thread keeps only the NEWEST frame and counts what it dropped, so a slow GPU cannot
      build lag behind a live source (a 100 ms-late phone would otherwise be seconds late after minutes). It also
      **reconnects with backoff forever**, so a phone that leaves Wi-Fi comes back without a restart. `lat` on the wire is
      measured from the frame's GRAB time, so queueing is counted.
- [x] **Rotate / mirror before inference**, because the model reads the image: a portrait phone arrives sideways and a
      front camera arrives mirrored. Measured: a stream served 90° sideways reads a **96.7°** trunk lean uncorrected and
      **5.6°** with `--rotate 270`, against **5.3°** for the same clip upright.
- [x] `--list-cameras` prints which indices open and at what size, for finding the phone.
- [x] Credentials in a link are **never printed** (sender, supervisor log/events, Unity log and HUD all redact
      `user:pass@`). The scheme allowlist is http, https, rtsp, rtsps; `stream:` cannot smuggle another scheme.
- [x] Webcam open tries DirectShow, Media Foundation, then OpenCV's default, each guarded (a virtual-camera driver on this
      machine **raises** instead of returning False), requests MJPG at 1280×720×30 and buffer size 1.

| check | result |
|---|---|
| `tests/test_video_source.py` (adds camera/stream classification, redaction, orientation, reader with a fake capture incl. drop-to-newest and reconnect, real MJPEG server) | **95 / 95** |
| `VideoSourceF49Tests` (adds camera, stream, LAN detection, redaction, rotate/flip/size flags) | **23 / 23** |
| real RTMW3D on a live MJPEG stream of `123.webm` (portrait phone clip, 60 fps source) | 925 packets, monotonic `seq`, ~40 fps; **100 stale frames dropped, not queued** |
| full chain: supervisor → live sideways stream (`--video-rotate 270`) → Unity's real `TrackedStage` | 1497 datagrams, **parseErr 0**, live 1305/1369 ticks, hands read, F-48 gate `TRUSTED` |

**NOT DONE / UNTESTED for this addition:**
- [ ] **No real phone, webcam or phone app was used.** This machine exposes no usable camera (a virtual device fails to open).
      The camera path (`open_camera`) is exercised only through a fake capture; the stream path was tested against a local
      MJPEG server speaking the same protocol, not against IP Webcam/DroidCam/Iriun/Camo themselves.
- [ ] Latency of a real phone app (typically 100–300 ms; the OAK-D is ~40 ms) is **unmeasured**.
- [ ] **SR7.mp4 is a screen recording** (Unity on the left, the sidecar preview with the person in a corner on the right). It is a poor
      test clip; my earlier SR7 numbers in this section tracked it only by luck of the crop. `123.webm` (portrait, single dancer)
      is the better regression clip. The F-49 numbers above that used SR7 describe plumbing, not tracking quality.
- [ ] Live sources are not paced, so a 60 fps source is processed at inference speed (~40 fps here) and sent at that rate.

### FINDING — logged 2026-10-09 (user observation in the Editor; NOT measured by an agent)

**Single-person tracking from VIDEO looks excellent — noticeably better than the live OAK-D feed.** The same
Unity scenes, the same RTMW3D model and the same pose-xy back-projection are used in both, so the difference is
upstream of Unity. With several people in the clip only ONE skeleton appears and it glitches.

* **The OAK-D is not involved in the video path.** `video_udp_sender.py` never creates a `dai.Device` or pipeline. It
  imports the `depthai` *library* (via `wholebody_udp_sender`, for the landmark builders) but touches no hardware;
  the supervisor's start-up check is `import depthai`, also no device.
* **Multi-person glitching is the expected behaviour of the single-person sender, not a new bug.** It keeps ONE crop
  (`bbox_from_keypoints` blended 0.7/0.3 into the previous box, falling back to `center_bbox` when the body
  confidence drops), so with several people the crop hops between them. Crowds need the multi-person sender's
  detector, which is an OAK-D VPU blob (ADR-085). Consistent with the code; not measured.
* **Why video may beat live — HYPOTHESES, ranked by how directly the code supports them, none yet tested:**
  1. **Depth source.** Video places every joint from the model's own root-relative z (`_fallback_point`, `zrel`) against
     ONE hip depth per frame: anatomically coherent by construction. Live samples stereo depth per joint, and the
     repo has already documented that this produces holes (~63 % of keypoints measured, RETARGET_AUDIT 2026-08-11),
     quantisation (`Δz ≈ z²/f·B`), surface bleed onto background and clothing, and — SR7 — stretched bones from
     inferred/measured mixing (ADR-084). One measured-per-joint z beside an inferred one is a bone-length error.
  2. **Fewer stages after the model.** Live runs P0 `KeypointSmoother` on 3-D xyz, P1-1 with geometry write-back, F-22
     pose validation and F-21 ownership; the video sender runs the model, a One-Euro on z, and P1-1 for *states only*.
     More stages is more places to add lag or a wrong correction (F-21 alone withheld frames in SR6).
  3. **Image and framing.** A phone clip is ~1918×1078, well exposed, subject fully in frame at a comfortable
     distance. The live feed is a 1280×800 global-shutter sensor at 0.9–2 m, in the room's lighting — and SR7's live
     take sat below the 1.4 m usable band.
  4. **Frame delivery.** A file is decoded in order with no dropped or stale frames; the live path has a camera queue,
     staleness handling and an interpolation delay.
* **THE DECISIVE TEST (not run): same pixels, different depth.** Record the live RGB frames (and the live wire) in one
  session, replay those exact frames through `video_udp_sender.py`, and compare the two outputs. Anything that differs is
  caused by the depth/stage path and nothing else, which separates hypothesis 1–2 from 3–4. The live sender cannot
  currently save its RGB stream; adding that is the first step.
* **A cheap live experiment that already exists:** `wholebody_udp_sender.py` has `--zrel-fallback` (default on), which only
  governs depth *holes*. Forcing every joint onto the model-z path in the live sender would be a new flag, but it is
  small, and it directly tests "model z + OAK hip root" against "stereo per joint".
* **Direction (proposed, NOT decided):** if the A/B confirms it, the live pipeline should take limb/torso geometry from
  the model's z and use stereo only for the root position, the floor and as a sanity check — which is also what F-48
  already enforces for calibration. Do not change the live path on this finding alone; it is one observer, one clip.


---

## F-50 — A PHONE OVER WEBRTC: PASTE THE LAN CAMERA VIEWER LINK, TYPE THE PIN (2026-10-10)

Decision: **ADR-086**. Status: **IMPLEMENTED; verified end to end against the real Node server and the real model; NOT yet
seen in the Editor; NEVER tried with a real phone.**

Wanted: run the LAN Camera Streaming server, open its sender page on the phone, paste the **viewer link** into
`AppBootstrap > Video Source`, press start, and have the sidecar join over WebRTC and track the phone like a webcam.

### DONE

- [x] **`python-sidecar~/tools/video/lan_camera.py`** — a UTILITY (frames in, frames out), with the same surface as
      `LatestFrameReader` (`start/stop/read`, `frames_read/dropped/reconnects/connected`). It speaks the server's own
      protocol: `GET /api/config` (ICE servers) -> Socket.IO -> `room:join` on **every** connect (the server drops
      membership on disconnect) -> a NEW `RTCPeerConnection` per `webrtc:offer` -> `webrtc:answer`; trickled candidates are
      queued until the offer is applied; `peer:left` closes the connection and waits for the next offer.
      Lazy imports, so every other source works without `aiortc`.
- [x] **`video_udp_sender.py`**: a new source kind `lan` (`http(s)://host/viewer?room=ID` or `lan:<url>`), classified BEFORE
      the generic http rule (which would call it an MJPEG stream or a yt-dlp page). The frame loop is otherwise untouched,
      plus two guards: a **fatal** LAN error exits 2 with one line, and a **frame-size change** re-derives the crop box,
      intrinsics and per-person state (a WebRTC sender adapts resolution to the bandwidth).
- [x] **The PIN is a separate field and never touches a command line.** `AppBootstrap > Lan Camera Pin` (masked in the
      Inspector by the new `[MaskedString]` attribute + `MaskedStringDrawer`) -> `SidecarLaunchOptions.LanCameraPin` ->
      set in the child's **environment** as `CAMERA_PIN` for exactly the duration of `NativeProcess.Start()`, then restored.
      Only a LAN camera source is handed one. A link that carries `pin=` is **refused** (Unity and Python both).
      Empty field + `CAMERA_PIN` already set on the machine also works; the field wins.
- [x] **`VideoSourceSpec`**: `VideoSourceKind.LanCamera`, `IsLanCameraLink`, `TryParseLanCamera`; the HUD names it
      `LIVE lan camera <host>/<room>`. The rule exists twice (Unity and Python) and both test suites pin the SAME examples.
- [x] **No PIN, no sidecar.** A LAN source with an empty field and no `CAMERA_PIN` is one console error at Play, not a
      sidecar that exits and is restarted by the watchdog forever.
- [x] Editing the PIN or the link while playing restarts the sidecar after a second, like every other video field.
- [x] `requirements.txt` + `requirements.lock.txt` (27 exact pins). **`pip freeze` was diffed before/after: nothing existing
      moved** (numpy 1.26.4, opencv 4.11.0.86, onnxruntime-directml 1.23.0, protobuf, DirectML provider all unchanged).

### MEASURED

| check | result |
|---|---|
| `tests/test_lan_camera.py`, no server (link rules, PIN rules, candidate parsing/queueing, newest-frame slot, offer->answer against a real aiortc peer with candidates arriving **before** the offer, new peer connection per offer, malformed offers ignored) | **78 / 78** |
| same, with `CAMERA_SERVER_DIR` = the real `lan-camera-streaming` Node server + a fake phone on aiortc | **103 / 103** (100/100 on two earlier runs, before the resolution-change phase was added) |
| - viewer joins first / phone joins first | >30 frames, BGR `(480,640,3)` uint8 both ways |
| - phone leaves and rejoins | fresh offer, new peer connection, frames resume, `reconnects` = 1 |
| - wrong PIN | `LanCameraError` "Invalid access PIN" in 0.0 s, **PIN never in the message** |
| - a second viewer in an occupied room | not fatal; retried; gets in and streams when the first leaves |
| - consumer 12x slower than the phone | max frame age **45 ms**, 90 frames counted as dropped, none queued |
| - `stop()` | the phone receives `peer:left` (room:leave was sent) |
| **full chain**: real `video_udp_sender.py`, viewer link in `--video`, PIN only in the environment, fake phone playing `123.webm` | 40 production datagrams (33 landmarks, `sid`, `seq`), `src` all 0, `[lan] joined room` + `frames=` in the log, **PIN absent from all output** |
| same chain, phone switches 360x640 -> 288x512 mid-run | "frame size changed" logged, 40 more datagrams, **one** `sid` (no restart) |
| same chain **through the real `sidecar_supervisor.py`** (PIN only in the supervisor's environment) | `SIDECAR READY`; PIN absent from the command line, supervisor log and events; 400 datagrams |
| sender's own latency on that chain (`lat`: frame published -> datagram sent) | mean **23 ms**, median 22, p95 32, max 48; packet age at the receiver 0.2 ms |
| `tests/test_video_source.py` (existing) | still **95 / 95** |
| `VideoSourceF49Tests` | **30 / 30** (the previous 23 + 7 new: viewer links, PIN refusal, PIN scoped to a LAN source, PIN not on the command line) |
| `dotnet build VirtualMirror.Tests.csproj` and `VirtualMirror.Editor.csproj` | 0 errors |
| **NativeProcess really passes the environment** (a probe against the project's own `StartExternalProcess.cs`) | child sees the PIN during `Start()`, not before, not after; Unity's own value restored |
| mutation checks (break it, watch it fail, restore) | Python: drop queued candidates, skip LAN classification, accept a PIN in a link, queue frames instead of newest-only, disable the resize guard — **all caught**. C#: PIN to a non-LAN child, PIN in a link, LAN rule after the generic one — **all caught** |

### FOUND ALONG THE WAY

- **aiortc answers a garbage SDP with an empty answer** instead of failing, so offers are shape-checked first.
- **`RTCPeerConnection.close()` can hang.** Reproduced three times in one test: with ICE still `checking` and DTLS still `new`
  it did not return in >30 s (aiortc 1.15.0 / aioice 0.10.2, Windows Proactor loop); a simplified replica never hung, so the
  trigger is timing. Unbounded it would sit inside the offer lock and freeze the receiver on every later offer — exactly the
  state a phone that cannot reach us leaves behind. Now bounded at 3 s (`PEER_CLOSE_TIMEOUT_SECONDS`); **root cause not found.**
- The server emits `peer:joined` only to whoever was already in the room, so a sender that joins second learns the viewer is
  there from `hasPeer` in its join ack. (My first fake phone got this wrong; the real sender page does it correctly.)

### NOT DONE — open

- [ ] **No real phone, browser or network was involved.** The "phone" is an aiortc process on this machine, on loopback.
      Real Chrome/Safari sender pages, real Wi-Fi, router client isolation, the firewall prompt for `python.exe`, and a zrok
      tunnel are all **untested**. WebRTC media is peer-to-peer on the LAN; through zrok only the signaling goes via the
      tunnel, and a phone on another network needs a TURN entry on the server.
- [ ] **Orientation of a real portrait phone is unknown.** aiortc ignores the video-orientation header, so a phone may
      arrive sideways; `Video Rotate Degrees` 90/270 is the existing answer. Not seen with a real device.
- [ ] **End-to-end latency is unmeasured.** The 23 ms above starts when the frame reaches the sidecar and excludes the
      phone's capture, its encode, the network and aiortc's decode. A real phone will be slower; expect the 100-300 ms of
      the other phone sources as a guess, not a number.
- [ ] **Throughput:** the chain ran at **~20 datagrams/s**, but the fake phone's VP8 encoder, Node and the model shared
      this CPU, so that is a loopback artefact, not the sidecar's ceiling (~31 ms/frame DirectML).
- [ ] **Not run inside the Editor.** `AppBootstrap`'s new field, the masked drawer, the start-up PIN check and the live
      restart were compiled (`dotnet build`, 0 errors) but not exercised. `MaskedStringAttribute.cs` and
      `MaskedStringDrawer.cs` have no `.meta` yet — Unity creates them when the Editor next refreshes; commit them with the
      `.cs` files.
- [ ] The effect of the resize guard on pose QUALITY is not measured; the test proves it is noticed and the stream
      continues, not that the output improves.
- [ ] No MJPEG / tunnel-relay fallback when WebRTC cannot connect (the standalone receiver had them). An MJPEG route for a
      LAN phone exists via `stream:`; the relay modes spend zrok quota and were left out until needed.
- [ ] The PIN field is masked but still saved into the scene as plain text. For a PIN that must not live in the project,
      leave it empty and set `CAMERA_PIN` on the machine.
- [ ] `D:\Unity\cam-receiver` (the standalone receiver this was ported from, 54 tests) still exists and is now redundant;
      delete it once you agree.


---

## F-51 — STATUS LIGHTS: SEE THAT THE SIDECAR, ITS WATCHDOG, THE SOURCE, THE MODEL AND THE TRACKING ARE UP (2026-10-10)

Decision: **ADR-087**. Status: **IMPLEMENTED; the rules are tested and ran on a real supervisor state file; the on-screen drawing has NOT been seen in the Editor.**

Reported by the user after F-50: the phone worked, but Unity showed nothing — the sidecar has no window (deliberately), so a healthy
system and a silently failed one looked identical, and there was no way to know whether the supervisor, the watcher or the inference
had started. Wanted: keep the sidecar windowless and put lights in the Unity scene.

### DONE

- [x] **Five lights, top right, in every scene (F8 hides):** `SIDECAR` (did Unity start the process, is it alive), `WATCHDOG` (is the
      supervisor running and still reporting; restarts, crash loops), `SOURCE` (camera/phone connected), `MODEL` (loaded and consuming
      frames), `TRACKING` (a person is being found and sent). Green = healthy, pulsing amber = in progress, red = needs a person, blue =
      an external sidecar, grey = off. Each change of a light is also logged once to the Unity console (`[SidecarStatus] MODEL: running`).
- [x] **`SidecarStatusModel`** (`Runtime/Tracking/OakD/`, pure and Unity-free like `VideoSourceSpec`): the launcher state plus the
      supervisor's state file in, five lights out. A counter counts as **moving** if it changed within 6 s and needs TWO observations
      (a stalled sender's last heartbeat must not read as healthy); the first seconds after attaching read `checking...`, never
      `idle`. A state file not rewritten for 10 s means the supervisor is gone; the previous run's leftover file is ignored when nothing
      of ours is running.
- [x] **`SidecarStatusLights`** (`Runtime/SkeletonShow/`): polls once a second, draws with `OnGUI`, survives scene loads
      (`DontDestroyOnLoad`), created by `SidecarBootstrap` and `AppBootstrap` (which attach their launcher). It listens on **no UDP
      port** (every scene binds 8899 for itself; ADR-075).
- [x] **Supervisor change (sidecar repo):** `supervisor_state.json` gained a `Child` object — the sender's latest heartbeat
      (`Frames`, `Sent`, `Skipped`, `Live`, `Note`, `LineAgeSeconds`) — parsed from the end of the sender's log every ~2 s, for all
      three senders. It changes nothing the supervisor decides, and cannot raise (`Child: null` when there is no log or heartbeat).
- [x] **The signal that distinguishes the cases that looked the same:** frames moving while `sent` stands still is `TRACKING: no person
      found yet` (exactly the earlier phone run: ~2000 frames, `sent=4`); `Live=n` is `SOURCE: waiting for the camera / phone`;
      `Frames=0` with `loading RTMW3D` is `MODEL: loading`.

### MEASURED

| check | result |
|---|---|
| `tests/test_supervisor_state.py` (heartbeat parsing for the video, wholebody and multiperson formats; latest line wins; truncated tail; no log / empty / banner-only / vanished log -> `Child: null`; existing keys untouched; large log read from the tail) | **32 / 32** |
| `SidecarStatusModelF51Tests` (parser incl. a null `Child`, garbage and half-written JSON; state-file path; every light in every situation: nothing started, starting, loading, healthy, no person, person lost, phone not connected, silent sender, restart, each launcher state, crash loop, stale file, stopped) | **23 / 23** |
| a REAL `supervisor_state.json` captured 5 s apart from a live chain (fake phone + real model + real supervisor), fed through the real built C# model | 177/171 -> 296/210 frames/sent: first poll `MODEL: idle` (one observation), second poll all five **green** |
| mutation checks | C#: first sighting counts as movement, stale file trusted, "person tracked" claimed on frames alone, a silent sender ignored — **all caught** |
| `dotnet build VirtualMirror.Tests.csproj` (pulls in Tracking and SkeletonShow, so the `OnGUI` component compiles) | 0 errors |
| existing suites | `test_video_source.py` 95/95, `test_lan_camera.py` 103/103 (unchanged), `VideoSourceF49Tests` 30/30 |

### FOUND ALONG THE WAY

- **`SidecarReady` does not mean the model is loaded.** The supervisor marks the sender READY at its first `frames=` line, which the
  senders print *before* loading the model (a real run: READY at 1.5 s, "loading RTMW3D" on the line before). The old state file therefore
  could not answer "has inference started"; the heartbeat can.
- With the phone, the log showed the whole system healthy and **no person ever found** — which no light existed to say.

### NOT DONE — open

- [ ] **Never seen on screen.** The `OnGUI` layout, readability at other resolutions, overlap with a scene's own HUD, the pulse, the F8
      toggle and survival across scene loads are untested. Compiles and passes the model tests only.
- [ ] **Does not prove Unity RECEIVES the poses.** The lights report what the SENDER says it sent. A sixth light for "Unity is
      receiving datagrams" needs a hook in the pose provider (a static counter), because the lights must not bind the port.
- [ ] **The real OAK senders were not run for this.** Their heartbeat formats are covered by parser tests built from their print
      statements, not by a live OAK-D run. `wholebody_udp_sender.py` prints a `frames=` line only in some states (idle lines keep the
      marker alive); the light degrades to `-`/`checking` rather than guessing.
- [ ] **A player build is assumed to lay the evidence folder out like the Editor** (`<sidecar root>/evidence/oak_v4/f20b`). Unverified;
      `VIRTUAL_MIRROR_EVIDENCE_DIR` is honoured by both ends.
- [ ] `SidecarStatusModel.cs`, `SidecarStatusLights.cs` and their test have no `.meta` yet (Unity creates them on refresh); commit them
      with the sources.


---

## F-52 — MULTI-PERSON FOR A PHONE, WEBCAM OR VIDEO: A HOST-SIDE DETECTOR IN FRONT OF THE F-32 TRACKER (2026-10-10)

Decision: **ADR-088**. Status: **BUILT, TESTED, MEASURED ON FILES AND THROUGH THE REAL SUPERVISOR WITH A FAKE PHONE; WIRED INTO UNITY
(`AppBootstrap > Video Max People`); NOT SEEN IN THE EDITOR; NO REAL PHONE, USB CAMERA OR REAL TWO-/THREE-PERSON RECORDING.**

Asked after the phone worked for ONE person: how to move on to several people, using the same route as before (a video, then the phone /
a USB RGB camera, no OAK-D). Target stated by the user: **2-3 people**, a high-end USB camera screwed to a wall (so it does not move
and latency matters less), the phone only for testing. The test clip was `Assets/Games/video/456.webm` (8 dancers, 1080x1920).

### BASELINE — what the single-person sender did on that clip (measured first, nothing changed)

354 of 356 frames posed at 29.3 fps, latency median 14.6 ms — and it followed ONE dancer (the front-centre one, because the crop starts at
the frame centre) for the whole clip and ignored the other seven. One hip jump over 100 px. (The earlier note that the crop "hops between
people" did not happen here; it follows whoever the crop starts on, and the pick is luck of position, not design.)

### DONE (sidecar repo, `python-sidecar~`)

- [x] `tools/video/host_person_detector.py`: YOLOX-s ONNX (Apache-2.0) on DirectML. The released file is a RAW export (`[1, 8400, 85]`),
      so the grid and strides are applied here and any other output shape is refused. Letterbox is top-left on 114 grey, as trained.
- [x] `tools/video/host_multiperson.py`: detector -> unchanged `PersonTracker` -> RTMW3D per CONFIRMED person (never a held/LOST one), each with
      their own z filter and P1-1 tracker; the F-32 `persons[]` datagram plus `persons[0]` at the root with `--sway-gain`.
- [x] `video_udp_sender.py`: `--max-poses N` (default 1 = unchanged path), `--detector`, `--det-conf`; detector modules imported only when N > 1.
- [x] `tools/model/fetch_yolox_s.py`: pinned size + SHA-256 (`c5c2d13e...`), atomic, refuses a truncated or altered file. Weights live in
      `python-sidecar~/models/` (not in git, invisible to Unity).
- [x] `tests/test_host_multiperson.py` **71/71**; 15 mutations caught. Existing: `test_video_source` 95/95, `test_supervisor_state` 32/32,
      `test_person_tracker` 19/19, `test_person_filters` 46/46, `test_lan_camera` **103/103** with the real Node server (the default path
      is intact end to end).

### WIRED INTO UNITY (same day, asked for so the phone can be tried)

- [x] **`AppBootstrap > Video Test Source > Video Max People`** (1-8, default 1). 1 is the old single-person path and sends nothing new.
      Edits during Play restart the sidecar like the other video fields.
- [x] **A separate flag, `--video-max-poses`, not `--max-poses`.** Unity ALWAYS sends `--max-poses` (the OAK multi-person cap, default 3) and the
      video sender ignored it; forwarding it would have switched multi-person on for every video session (a real launch line from the
      Editor console shows `--max-poses 3` on a one-person phone session). Supervisor: `--video-max-poses N` (> 1) -> the sender's `--max-poses N`, clamped to 8.
      Unity: `SidecarLaunchOptions.VideoMaxPeople`, `VideoSourceSpec.ClampPeople / MaxPeople / DetectorWeightsPath`.
- [x] **A start-up check, like the PIN one:** with more than one person and no detector weights, Unity logs one error with the fetch command
      and does NOT start the sidecar (instead of a watchdog restart loop). `VIRTUAL_MIRROR_DETECTOR` is honoured by both ends.
- [x] Tests: `VideoSourceF49Tests` **35/35** (+5; 6 mutations all caught), `test_video_source.py` **105/105** (+10 command-shape cases, incl. "the OAK
      `--max-poses` never reaches the video sender"), `test_host_multiperson.py` **73/73**. The Unity Editor console showed no errors or warnings after recompiling.
- [x] **Through the real supervisor** (fake aiortc phone -> real Node LAN server -> real supervisor with `--video-max-poses` -> sender -> UDP):
      the command line carried `--max-poses 3` and not the OAK `5` that was also passed; `SIDECAR READY`; `Child.Note` stayed empty; PIN absent from
      output, log and events. 8-dancer clip at 360x640 (3 people): 19.1 datagrams/s with the fake phone's encoder on the same CPU, 3 persons on 392 of 459
      datagrams, sender `lat` median 57 ms, but **12 distinct ids in 24 s** (small people fragment). 2-person composite at 540x480 (2 people): 28.0 datagrams/s,
      2 persons on 456 of 681, `lat` median 39 ms; the right-hand person kept ONE id for 681 of 701 datagrams (the others are the left clip's people entering
      and leaving, and the clip looping).

### MEASURED (RTX 3060, DirectML, RTMW3D fp16)

| check | result |
|---|---|
| detector alone | 8-12 ms per frame; **finds a mean 6.26 of the 8 dancers** (min 4, max 9); exactly 1 of 1 on `video.webm` (435/435 frames) |
| `--max-poses` 2 / 3 / 5 / 8 on `456.webm`, decode excluded | **35.5 / 26.3 / 17.5 / 14.8 fps** (28.2 / 38.1 / 57.5 / 66.8 ms per frame) |
| distinct ids posed (of those with >= 30 frames) | 4 (3) / 6 (4) / 10 (8) / 12 (10) — ids fragment under occlusion |
| one-person clip `video.webm`, single path vs multi path | datagrams 435 vs 433; shoulder width 333 vs 334 mm; jitter 13.9 vs 14.1 mm; p95 71.9 vs 71.5 mm |
| `123.webm` (a person who walks out of frame) | datagrams 570 vs 550; jitter 16.3 vs 10.7 mm (the 20 fewer are probably detector misses; not isolated) |
| side-by-side composite of `123` and `video` (ground truth by construction) | the right-hand person kept ONE id for 385 of 387 frames, 0 switches |
| a FILE through the sender | ~17 fps: decoding the 1080x1920 webm costs ~13 ms (spikes 97 ms) on the loop's own thread. A live source decodes on a reader thread. |
| resolution A/B (single path, same clip, 1080x1920 -> 288x512) | jitter 25.4 -> 28.4 mm (+12 %), 354 -> 352 datagrams: for a ~640 px dancer the crop is cut from the original pixels, so resolution mattered little |

### FOUND ALONG THE WAY

- **Pose input is a fixed 288x384 crop per person.** The model never sees the whole frame; what matters is how many real pixels the PERSON
  occupies before the crop is resized (>~384 px: more resolution buys nothing; <~200 px: the crop is stretched). The LAN phone sender asks the
  phone for its maximum resolution, then the browser's WebRTC encoder scales it down (360x640 and 288x512 were seen); a USB camera has no such step.
- **F-33's synthetic depth must NOT be used here.** It paints every box with one distance, so every joint reads "measured" and takes that flat
  z, discarding the model's coherent root-relative z — the thing that made single-person video tracking good.
- My first detector timing (108 ms) was an artefact: seeking and decoding between calls let the GPU idle down. Back-to-back it is 8-12 ms.

### NOT DONE — open

- [ ] **Never seen in the Editor.** The field, the missing-weights error and which scenes show several people have not been tried. A single-person
      scene shows the first person only; the crowd scenes (F-34) fed by a host detector have never been on a screen.
- [ ] **No real source.** No real phone, USB camera or browser sender page was run with several people (a synthetic phone was); no recording of 2-3 real
      people from the target camera. Small people at phone resolution fragment ids (12 ids for 8 dancers at 360x640).
- [ ] **3 people is 26.3 fps, under the camera's 30.** Candidates (unmeasured): detect every other frame, an FP16 detector, a cheaper pose preprocess.
- [ ] **Hip depth for a hidden-ankle person is the 2.0 m default** (nose-to-ankle span); the detector's box height could be a second estimate.
- [ ] **Identity is only counted, not labelled.** 12 ids for 8 dancers at cap 8 is fragmentation, but real swaps vs new births were not separated.
- [ ] `--det-conf 0.45` is the OAK path's value, not tuned for YOLOX-s. Re-measure phantom people before changing it.
- [ ] The crowd scenes (F-34) fed by this have never been seen on a screen.


---

## F-53 — THE APPBOOTSTRAP INSPECTOR AS COLLAPSIBLE SECTIONS, AND ITS SETTINGS IN NAMED FILES (2026-10-10)

Decision: **ADR-089**. Status: **DONE; compiles in the Editor with no errors or warnings; the drawing itself has not been looked at on screen.**

Asked because the `AppBootstrap` Inspector had become one flat list of 75 visible fields (105 serialized in all, 1,966 lines of script).

- [x] **`[InspectorGroup(title, Order, ExpandedByDefault)]`** (`Runtime/Core/InspectorGroupAttribute.cs`) and the pure `InspectorGrouping.Build(Type)`
      (`Runtime/Core/InspectorGrouping.cs`): which visible field belongs to which section. A plain `Attribute`, NOT a `PropertyAttribute`, so it cannot
      clash with `[Range]`, `[Tooltip]` or `[MaskedString]` on the same field.
- [x] **`GroupedInspectorEditor`** + **`AppBootstrapEditor`** (`Editor/`): collapsible sections, "Expand all / Collapse all", each section's open state remembered
      for the editor session, field count in the title, and any visible field no section claims is drawn under "Other" rather than dropped. Fields are still
      drawn with `PropertyField`, so tooltips, ranges and the masked PIN behave as before. `VirtualMirror.Editor.asmdef` now references `VirtualMirror.App`.
- [x] **`AppBootstrap` is a `partial class`**, its serialized declarations moved verbatim into one file per section: `AppBootstrap.TrackingSource.cs`,
      `.VideoSource.cs` (with the video runtime state), `.KalidokitBody.cs`, `.HumanizedSkeleton.cs`, `.OperatingVolume.cs`, `.PoseMapping.cs`, `.Features.cs`,
      `.Debug.cs`. `AppBootstrap.cs` keeps the plumbing and the behaviour (1,966 -> 1,689 lines). Nine sections: General, Tracking Source, Video Test Source,
      Kalidokit Body, Humanized Skeleton, Operating Volume, Pose Mapping, Features, Debug; General, Tracking Source and Video Test Source start open.
- [x] **No serialization change.** Field names, types and defaults are untouched, so `Bootstrap.unity` keeps every saved value. Proven two ways: the script that
      moved the text asserted the multiset of the 105 `[SerializeField]` declaration lines is identical before and after, and in the Editor a temporary
      component's `SerializedObject` showed 75 visible properties, all 75 in a section, none missing, none unclaimed.
- [x] Tests: `InspectorGroupingTests` **9/9** (the rules on stand-in types, and the real `AppBootstrap`: nine sections in order, every visible field in exactly one,
      key settings in the section a user would look in); 8 mutations all caught. `VideoSourceF49Tests` 35/35 and `SidecarStatusModelF51Tests` 23/23 unchanged.

**Found along the way:** the first version counted `HumanizeTap` (a public delegate) as visible; Unity never serializes a delegate, and the real-`SerializedObject`
check caught it. Now excluded, with a test.

**NOT DONE:** never seen drawn (layout of the section boxes, the buttons). A search box over 75 fields was not built. Only `AppBootstrap` uses the editor; any other
long component opts in by adding `[InspectorGroup]` to its fields and a one-line `CustomEditor` subclass. The new files' `.meta` files exist and must be committed.


---

## F-52 FOLLOW-UP — THE FIRST REAL TWO-PERSON CLIP (`147.webm`), LIVE IN UNITY (2026-10-10)

Status: **offline result good; the live run exposed three problems, all fixed; what the SCENE shows was watched by the user, not by me.**

The clip: `Assets/Games/video/147.webm`, two dancers, full body, landscape **3840x2160**, 1,935 frames at 29.97 fps. `AppBootstrap` was already set to it with `Video Max People` = 2.

### Offline (the multi-person path, 2 people)

| check | result |
|---|---|
| frames with both people posed | **1931 of 1934**; exactly **2 ids**; **0 id changes**; the left/right order of the two ids never flipped; one one-frame dropout (frame 1621) |
| native 4K vs max side 1920 vs 1280 (110 frames) | processing 29.6 / 29.2 / 29.4 ms; joints 21.0 / 21.0 / 21.0; jitter 8.5 / 8.4 / 8.5 mm — **downscaling a 4K source buys nothing and loses nothing** |
| decoding the 4K file | median 33 ms, mean 41 ms, p95 ~140 ms: **decode alone tops out at ~24 fps**; FFmpeg thread options changed nothing |

### Found live, fixed

- [x] **The first launch was killed by the watchdog.** `HeartbeatTimeout` 11.2 s in: the supervisor kills a child silent for 10 s, the blocking model load printed nothing, and ADR-082 had measured the FP32 model at 13.8 s to ready. The second launch worked only because the files were then in the OS cache.
      Fix: `LoadTicker` in `video_udp_sender.py` repeats the `frames=0 sent=0 loading ...` line every 2 s under both blocking loads (pose model, person detector). Tests: 6 mutations caught.
- [x] **Unity was running the slow FP32 model.** ADR-082 made FP16 the default (2.97x faster on the GPU stage, 6.8 s vs 13.8 s to ready) and the sidecar's own default prefers it, but Unity passes an explicit `--model`, which bypasses that. `SidecarPaths.PreferFp16` + `SidecarLocator` now pick `rtmw3d-x-fp16.onnx` when it sits beside the FP32 file (an Inspector override is used exactly as typed; a machine without the FP16 file is unchanged).
      Measured live: per-person pose 25 ms -> 14.7-16 ms with Unity rendering (9 ms standalone). `SidecarModelPreferenceTests` 6/6, 5 mutations caught.
- [x] **A 4K FILE ran at under half speed** because decode and inference added (13.4 fps through the sender). `PrefetchCapture` decodes ahead on its own thread (automatic above 2.5 MP, `--prefetch / --no-prefetch`): **13.4 -> 21.8 fps**, wall time 148 s -> 92 s, identical output (1,932 datagrams). It cannot make a 4K file real-time (decode caps at ~24 fps); a 1080p copy would be.
      Tests: 9 mutations, 8 caught, the 9th (end-of-clip sticky flag) equivalent. `test_video_source.py` **129/129**.
- **Live in Unity after the fixes:** `RestartCount` 0, ~19 fps, 2 people on every sampled frame. The clip plays at roughly 0.65x because it is 4K.

### NOT DONE

- [ ] What the scene draws (and whether the single-person scene shows only the first person) was not inspected by me.
- [ ] A 1080p copy of the clip would show real-time behaviour; the Inspector's source was left as the user set it.
- [ ] GPU contention with Unity's own rendering is real (9 ms -> ~15 ms per person); not tuned.


### F-52 FOLLOW-UP 2 — "THE SKELETON IS SLOW AGAINST THE VIDEO" (2026-10-10)

Reported while watching `147.webm` in Unity: the skeleton lagged the video it was meant to mirror. **It was right**: the sidecar steps through a file frame by frame however long that takes, and the 4K clip
needed ~54 ms a frame against a 33 ms period, so it advanced at **0.62x** (18.6 fps against 29.97 in the sender's own log). After 10 s of real time the skeleton was ~4 s behind a video playing at 1x.

- [x] **File playback is held to the clip's own clock.** `pace_decision` + `--drop-late` (default on): a frame more than 1.5 frame periods behind is dropped, so the skeleton stays in step with the picture at a lower frame rate. The clock is re-anchored after
      the first frame (the one-off GPU warm-up is not lag). The heartbeat reports `speed=` (1.00 is real time) and `late=` (frames dropped); the supervisor strips both from the note.
- [x] **A first version posed 13 frames out of 1,934 on the 4K file.** Dropping saves the time spent POSING a frame, never the time spent DECODING it, so when decoding is the bottleneck every frame arrives late and a late-drops-all rule drops everything. Found by running it,
      not by the tests. Now `can_catch_up_by_dropping`: drop only when the next frame is already decoded (prefetching reader: `pending() > 0`) or decoding is clearly cheaper than a frame period (plain reader). When it cannot help, nothing is dropped and the sender prints one
      `WARNING the clip is advancing at 0.69x real time ... use a lower-resolution copy (e.g. 1080p)`.
- [x] **Measured through the real sender (standalone):** 1080p copy **speed 1.00**, 29.2-29.8 fps, 7-46 late drops, 67.8 s wall for a 64.5 s clip; original 4K **0.77** (decode-bound; 1,557 poses; the warning fires).
- [x] **Measured live in Unity** (real GPU load, same clip): 4K original `speed=0.68-0.69` with the warning; after a live switch of `Video Source` to the 1080p copy `speed=0.99`, ~25 fps with 2 people, no restarts.
- [x] `Assets/Games/video/147_1080p.mp4` (32.9 MB; 1080p H.264, decodes in 3.3 ms against 33 ms for the 4K VP9 original). `Assets/Games/*` is git-ignored, so it is not committed. **Point `Video Source` at it to see the skeleton in step.**
- [x] Tests: `test_video_source.py` **150/150** (the pacing rules, a simulation of the decode-bound and the pose-bound cases, `PrefetchCapture.pending`); 11 mutations all caught, including the 13-of-1,934 rule.

**Why 4K cannot be real time here:** decoding alone tops out at ~24 fps on this machine (more FFmpeg threads changed nothing). A 4K phone or USB camera stream would hit the same wall; a lower capture resolution is the fix, and the pose stage loses nothing (a ~800 px dancer is cut to a 384 px crop).
**NOT DONE:** hardware (GPU) decoding; an automatic lower-resolution proxy; the runtime edit of `Video Source` used for the live check is not saved, so the scene still points at the 4K file until you change it.
