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
- [ ] **Fix z-depth decode (highest impact)** — `SentisPoseProvider.DecodeFrame` treats z like a pixel coord (÷`InputHeight`); RTMW3D z is a *root-relative depth* (own normalization; z bins 576). Implement the correct mmpose RTMW3D z decode + a separate `sentisDepthScale`. Flattened/​wrong z is the main cause of muted limb reach.
- [ ] **Reduce smoothing for the 3D path** — One-Euro is tuned for calm webcam; raise beta / lower min-cutoff (or per-provider filter profile) so fast motion isn't damped.
- [ ] **Person crop to 3:4** — top-down model wants a tight person bbox; centre-crop the frame (or add a light detector) instead of resizing the whole 16:9 frame → bigger, more accurate keypoints.
- [ ] **Per-axis sign + scale tune** — `poseFlipX/Y/Z` + `sentisMetreScale` were inherited from MediaPipe; live-tune for this provider (mirror + amplitude).
- [ ] **HUD label from active provider** — stop hardcoding "MediaPipe Pose (CPU)"; show the running provider name.
- [~] **Live-validate + tune** — ✅ **decode + live pipeline VALIDATED** (2026-08-06, video source): on a real frame both norm modes give anatomically-correct keypoints (ImageNet norm higher-conf → kept); live in-pipeline `SentisPoseProvider` emits valid hip-centred 3D (hips@origin, nose y+0.47, ankles y−0.50, Y-up, L/R x-split, conf 0.80–0.94) and the avatar is driven (posed, not T-pose), 0 errors. ⏳ **Still TODO:** frame-to-frame **motion** couldn't be confirmed headless (VideoPlayer doesn't advance under `EditorApplication.Step`; real-time run freezes when Unity unfocused) → needs a **real-time focused Play** by the user. Then tune: z-depth `sentisMetreScale` (~1.0 m span now, a bit short); mirror/axis via `poseFlipX/Y/Z` for the avatar convention; root/hip grounding (stop float); velocity outlier-reject (leg jitter). Fix stale HUD label ("MediaPipe Pose (CPU)" shown while Sentis is active).
Stack chosen: **Unity Sentis** `com.unity.ai.inference` 2.6.1 (INSTALLED ✅, 0 errors), model **RTMW3D** (RTMPose3D family, whole-body 3D incl. hands, Apache-2.0). POC → non-commercial licenses OK.
- [x] **Install Sentis** (`com.unity.ai.inference` 2.6.1) — namespace/assembly `Unity.InferenceEngine`. Build clean.
- [~] **RTMW3D feasibility spike (NEXT — resume here):** ONNX downloaded + verified (`Soykaf/RTMW3D-x`, 369 MB, 384×288). **Import into Sentis** (regular `Assets/` folder) and read the import log for unsupported operators. Clean → Sentis; rejected → **ONNX Runtime (DirectML)**. See ADR-015 for URL + decode.
- [ ] **Build `SentisPoseProvider : IBodyTrackingProvider`** — camera→`TextureConverter.ToTensor`(384×288)→`Worker.Schedule`(GPUCompute)→readback→**SimCC decode**→133 keypoints→our 33 `JointId` (Unity hips-centred m). Tracking asmdef + `Unity.InferenceEngine` ref. Wire behind `AppBootstrap.useSentis3dTracking` (default off) + MediaPipe fallback. Live-validate/tune (like `poseFlipX`).
- [ ] **Bonus from RTMW3D:** whole-body includes **hands + face** → could replace the separate MediaPipe hand/face providers and fix dead-hands-at-distance.
- [ ] **Also:** root/hip grounding (stop the avatar floating), velocity outlier-reject (leg jitter seen while standing).
- [ ] **Direct rotational retarget from 3D** — feed true 3D limb rotations, dropping most IK reconstruction (fixes head pitch, wrist, depth at the source, not as symptoms).
- [ ] **Per-joint finger + wrist from 3D hand** — replace the single mid-joint curl heuristic (SDS-011 curl) with per-joint angles + real wrist pose; supersedes the ADR-013 delta-from-neutral wrist hack.
- [ ] **(Optional, Phase 3)** depth camera provider (RealSense D455 / OAK-D) behind `ICameraCapture` / `IBodyTrackingProvider` — best scale + occlusion; hardware, ADR-007.

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
