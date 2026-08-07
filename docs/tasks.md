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
