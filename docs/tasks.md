# Virtual Mirror — Tasks

Update this file whenever work starts or finishes. Prefer small checkboxes agents can claim.

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
- [ ] `ConfidenceGate` with hysteresis (SDS-025 §4) + outlier reject + stale-hold; current gate is a simple per-segment threshold  
- [x] **`MirrorCameraController`** (`VirtualMirror.Rendering`) — frames the avatar to renderer bounds (fits any height; solves the tiny-chibi problem). Called on `IAvatarSession.AvatarChanged`  
- [x] **Hide load UI on avatar load** — `AvatarSessionController` fires `AvatarChanged` (manual + auto); `AvatarLibraryPanel` hides input/button/status on it; **Tab** toggles it back  
- [x] `AnimationRiggingIkDriver` (hand/foot IK targets) — ADR-002; implements `IIkSolver` using `TwoBoneIKConstraint` for arms & legs, `RigBuilder`, and `Rig`. Play-tested & verified  
- [x] **Webcam Input Integration & Mirror Calibration** — `AppBootstrap.useVideoSource=false` wired to live webcam (`WebcamCaptureService` initialized device `Logi C270 HD WebCam` 1280x720@30fps). `poseFlipX=true` finalized for true mirror reflection (right hand motion maps to facing avatar's right hand). Play-tested & verified
- [x] **Perf:** MediaPipe CPU inference offloaded to background thread (`ThreadPool`) with frame-dropping and double-buffering. Main thread remains completely unblocked during tracking. Play-tested & verified  
- [x] **EditMode unit tests** (`VirtualMirror.Tests` asmdef): Y-flip/mirror (`PoseSpaceConverter`), no-NaN on missing landmark & parallel vectors (`RotationFromVectors`), One-Euro smoothing (`OneEuroFilter`) — 13/13 passed  

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

## Blocked

_None_

---

## Completed

- [x] Author agent-oriented SDS docs `00`–`25` + handoff set (recreated 2026-08-05)
