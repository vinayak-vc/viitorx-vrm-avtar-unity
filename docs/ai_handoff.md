# Virtual Mirror — AI Handoff

Last updated: 2026-08-06  
Purpose: next agent can continue without re-deriving context.

---

## Current state

- **Milestone:** **M0 ✅. M1 ✅ (VRM load/swap/persist + UI). M2 ✅ core (capture → MediaPipe pose → filter → torso FK + limb IK → avatar; live). M3 ✅ face + hands REAL (MediaPipe Face/Hand Landmarker → VRM expressions / finger curls). M4 ✅ calibration/HUD. M5 ✅ ship (built-ins, file picker, notices).**
- **IK + Face audit (7 fixes) — DONE + play-verified 2026-08-06.** Arms/legs = IK; FK = torso (hips/spine) + neck only (mutually exclusive). IK targets scale-normalized; position-only IK. Real `MediaPipeFaceProvider`. Expression guard + reset-on-stop. `FaceFrame` reusable buffer. See `tasks.md` "IK + Face audit — FIXED".
- **Code:** Full runtime pipeline implemented + compiling (0 errors). All feature assemblies created.
- **Packages pinned:** `com.unity.animation.rigging` 1.4.1, `com.vrmc.vrm` 0.126.0 (+ `com.vrmc.gltf`); homuler MediaPipeUnityPlugin under `Assets/MediaPipeUnity`; pose model at `Assets/StreamingAssets/MediaPipe/pose_landmarker_full.bytes` (9.4 MB). See `19_ThirdParty.md §1a`.
- **Docs:** SDS `00`–`25` + handoff. `decisions.md`: ADR-006 (Unity Awaitable), ADR-009 (assemblies), ADR-010 (MediaPipe = homuler).

### What runs today (live, play-verified)
`Bootstrap` → `AppBootstrap` wires services + capture (`VideoFileCaptureService` on `sample video.mp4`, or `WebcamCaptureService` — toggle `useVideoSource`) + `MediaPipePoseProvider` + `JointFilterPipeline` + `HumanoidPoseRetargeter`, additive-loads `Mirror`, auto-loads `avatar.lastPath`, hides the load UI on load, and frames the avatar (`MirrorCameraController`). Live chain:
`capture → MediaPipe PoseLandmarker (full, CPU, background worker) → world landmarks → PoseSpaceConverter → One-Euro filter → full-body FK retarget → VRM`. Verified: avatar tracks the sample video, torso straight, 0 errors.

**Retarget (`HumanoidPoseRetargeter`)**: Hips + Spine driven by calibration-relative bases (hip line / shoulder line, shared torso-up) → torso straight at neutral, no waist twist. Arms/legs/neck are roll-constrained FK using per-frame body-forward. All parts hysteresis-gated (enter/exit). `C` key recalibrates.

### Parallel session (present, NOT audited by this agent)
A second session added: a full HUD + `Settings & Calibration` panel (webcam device picker, IK/face/hand toggles, smoothing sliders), an **IK solver** (Animation Rigging — needs the Animator enabled), and a **face provider + VRM expression retargeter** (M3). Audit these against the same coordinate/handedness conventions that caused the waist-twist bug.

### Audit + fixes (this agent, 2026-08-05)
1. **Waist twist** — Hips (basis) vs Spine (world-locked direction) disagreed on facing. Fixed: torso is now dual-basis + calibration-relative.
2. **Threading race** in `MediaPipePoseProvider` — pool get/release + Image build/dispose were split across main/worker. Fixed: worker does only `TryDetectForVideo`; main owns the `TextureFramePool`+`Image` (in-flight, released next tick).
3. **Per-frame GC** — `PoseFrame` is now a reusable mutable buffer; fake provider, filter, and the (double-buffered) MediaPipe provider fill in place → zero per-frame `PoseFrame`/array allocs.
4. **Limb roll** — roll reference switched world-forward → per-frame body-forward (twist follows body facing).
5. **Confidence hysteresis + stale-hold** — retarget gate has enter/exit thresholds (`retargetMinConfidence`/`retargetExitConfidence`); `AppBootstrap.IsPoseStale` holds the pose if frames stop arriving (`poseStaleSeconds`).
6. **Animator.enabled** — left ENABLED on purpose (the IK path needs it); FK writes in LateUpdate, no AnimatorController so nothing overwrites, IK layers on top.

### IK + Face fixes (this agent, 2026-08-06) — play-verified
Design locked: **IK owns arms+legs; FK owns torso (hips/spine) + neck** (mutually exclusive per bone).
1. **FK/IK exclusion** — `HumanoidPoseRetargeter.SetArmsLegsDrivenExternally(bool)` + per-segment `IsLimb` (Neck=false). `AppBootstrap.UpdateTracking` sets it from `ikActive = useIkDriver && ikSolver.IsBound` and calls `IIkSolver.SetActive(useIkDriver)` (new interface member; driver zeros constraint weights when disabled so the toggle works both ways).
2. **IK target scale** — `AnimationRiggingIkDriver` measures per-side avatar limb lengths at `Bind` and scales each landmark offset by `avatarLen/trackedLen` (`ComputeScale`) → reachable targets on any-scale avatar.
3. **Position-only IK** — `targetRotationWeight = 0` on all four constraints.
4. **Real face** — new `MediaPipeFaceProvider` (homuler FaceLandmarker, blendshapes) + new refcounted `MediaPipeGlobalInit` (both pose & face now share Glog init/shutdown — prevents double-init crash). Wired behind `AppBootstrap.useMediaPipeFace` with fake fallback. Model at `StreamingAssets/MediaPipe/face_landmarker.bytes`.
5. **Expression guard** — `VrmExpressionRetargeter` snapshots the VRM's `ExpressionKeys` at Bind; guards every `SetWeight`; warns once per missing key.
6. **Reset on stop/invalid** — resets mapped weights to 0 on Unbind, invalid frame, and face-toggle-off.
7. **`FaceFrame` reusable buffer** — mutable `SetExpressions`/`SetMeta`/`MarkInvalid` like `PoseFrame`; providers fill in place.

Verified live (video → StrawberryPrincess): 0 compile/runtime errors; waist straight (hips↔spine 0.0°, hips→spine 2.0° off up); arm IK weight=1 with targets 0.43/0.60 m vs 0.46 m avatar arm; all constraints rotW=0; `faceProvider=MediaPipeFaceProvider` with valid frames; expression apply 1.00→invalid 0.00.

**Modified/created:** `Runtime/Core/Models/FaceFrame.cs` (rewrite), `Runtime/Core/Interfaces/IIkSolver.cs` (+SetActive), `Runtime/IK/AnimationRiggingIkDriver.cs`, `Runtime/Retargeting/HumanoidPoseRetargeter.cs`, `Runtime/Retargeting/VrmExpressionRetargeter.cs` (rewrite), `Runtime/Tracking/FakeFaceTrackingProvider.cs`, `Runtime/Tracking/MediaPipe/MediaPipeFaceProvider.cs` (new), `Runtime/Tracking/MediaPipe/MediaPipeGlobalInit.cs` (new), `Runtime/Tracking/MediaPipe/MediaPipePoseProvider.cs` (use shared init), `Runtime/Bootstrap/AppBootstrap.cs`, `Tests/EditMode/VrmExpressionRetargeterTests.cs`, `StreamingAssets/MediaPipe/face_landmarker.bytes` (new, 3.67 MB).

### Real hand tracking (this agent, 2026-08-06) — play-verified
- **`MediaPipeHandProvider`** (new, `Runtime/Tracking/MediaPipe/`) — homuler `HandLandmarker`, CPU/VIDEO, `numHands:2`, `modelAssetBuffer`; mirrors pose/face threading exactly (worker `TryDetectForVideo`; main-thread `TextureFramePool`+`Image`; double-buffered reusable `HandFrame`; shared `MediaPipeGlobalInit`). Per-finger curl = bend angle at the finger's middle joint over the 21 world landmarks (`Vector3.Angle(mid-prox, tip-mid)/160°`, clamped); left/right by MediaPipe handedness label. Wired behind `AppBootstrap.useMediaPipeHand` (fake fallback). Model `StreamingAssets/MediaPipe/hand_landmarker.bytes` (7.6 MB float16, Google official).
- **`HandFrame`** now a reusable mutable buffer (`SetCurls`/`SetMeta`/`MarkInvalid`) like `FaceFrame`/`PoseFrame`; fake + real fill in place; old 12-arg ctor removed; `HumanoidHandRetargeterTests` updated.
- **Status:** compiles clean (0 errors) and **play-verified** (video source): `handProvider=MediaPipeHandProvider`, `IsTracking=True`, retargeter bound (15 joints/hand); valid frames carry real per-finger curls that vary frame-to-frame and map to the correct L/R by MediaPipe handedness; pose+face+hand landmarkers run together (shared `MediaPipeGlobalInit`) with 0 runtime errors. Handedness mirror-swap and the `angle/160°` curl scaling may still want fine-tuning against a webcam.
- **Open notes:** hand curl mapping is a heuristic (single mid-joint angle); good enough for open/curl but not per-joint fidelity. Consider `HandFrame` reset-to-open on invalid/stop (currently holds last pose, like the pre-existing behavior).

### IK facing + calibration fixes (this agent, 2026-08-06) — play-verified
Reported on a webcam run with the **Skull** avatar: "hands behind the body" + "body horizontal, non-human". Root causes + fixes:
- **Hands behind body** — IK targets were `hips.position + landmarkOffset` in **world axes**, ignoring the avatar's rest facing. Skull's rest hips face **Y=180°** (avatar faces −Z), so a camera-space forward offset landed *behind* it. Fix: `AnimationRiggingIkDriver` captures `hipsRestRotation` at Bind and places targets/hints as `origin + hipsRestRotation * (offset * scale)`. Verified: hand targets moved from hips-local z ≈ **−0.78 (behind)** to **+0.28/+0.31 (front)**; live screenshot shows arms in front.
- **Body horizontal** — the calibration-relative torso locks its neutral basis on the first confident frame; a webcam/OBS warm-up frame (hips not yet in view → weak/degenerate basis) locked a bad neutral, tilting the torso ~90° forever. Fix: `HumanoidPoseRetargeter.ApplyTorso` now captures neutral only when the torso up **and** each right vector exceed `MinTorsoVectorMagnitude` (0.08 m) **and** the basis has been strong for `NeutralWarmupRequired` (8) consecutive frames; until then the torso stays at rest (upright). `Recalibrate` (C key) resets the warmup. Verified upright on the video (torso 6° from vertical).
- Offset uses **rest** hips rotation (not live), decoupling arm placement from torso lean; left/right sign still follows `poseFlipX` and may want webcam tuning. **If a webcam run still looks tilted, press C** while standing upright + centered.
- **Modified:** `Runtime/IK/AnimationRiggingIkDriver.cs`, `Runtime/Retargeting/HumanoidPoseRetargeter.cs`.

### Open / still to do
- **`useVideoSource`** is committed **false** (webcam, `Logi C270`). For the 2026-08-06 IK/face verification it was flipped to `true` in-memory (reflection, edit-mode only, never saved) to drive tracking off `sampleVRMFiles/sample video.mp4`, then restored to false. To re-verify off the video, flip `AppBootstrap.useVideoSource` (or set it in Bootstrap.unity).
- Audit the parallel session's **calibration** code (same coordinate-convention risk as the waist-twist bug).
- Unused `VRM10` reference in `VirtualMirror.Retargeting.asmdef` — actually now USED by `VrmExpressionRetargeter` (keep it).
- Finalize mirror sign (`poseFlipX`, committed true) once a real user is on webcam.

> ⚠️ Scheduling as a **cloud** routine won't work end-to-end: cloud agents have no Unity Editor / MCP → code edits + PR only (no compile/play-verify), and local uncommitted work must be pushed to the repo first. Verification needs a local Unity session.

---

## Understanding (do not re-litigate)

Product = webcam virtual mirror for VRM avatars.  
Stack = Unity 6 + MediaPipe (Pose/Face/Hands) + UniVRM + Animation Rigging.  
Architecture = layered providers, hot-swap, filter → retarget → IK → VRM.  
V1 = single user Windows EXE; no marketplace yet.

---

## Assemblies (per ADR-009)

`VirtualMirror.Core` (interfaces/models) ← `.IO` ← `.Settings`; feature asmdefs `.Camera`, `.Tracking` (refs `Mediapipe.Runtime`), `.Retargeting`, `.Rendering`, `.Avatar` (refs `VRM10`/`UniGLTF`), `.UI` (refs `UnityEngine.UI`, `Unity.TextMeshPro`) — all ← `.App` (composition root). `.Editor`/`.Tests` still pending. Concretes never flow up into Core.

---

## Modified / created this session

- Folder scaffold under `Runtime/` (Bootstrap, Core/{Interfaces,Models,Events}, Camera, Tracking/{MediaPipe,Filtering,Calibration}, Retargeting, IK, Avatar/Vrm, Rendering, UI, Settings, IO, Diagnostics) + `Editor/`, `Tests/`, `Scenes/`, `Prefabs/{Avatar,UI,Tracking}`
- asmdefs: `VirtualMirror.Core`, `VirtualMirror.IO`, `VirtualMirror.Settings`, `VirtualMirror.App`
- `Runtime/Core/Models/LogLevel.cs`, `Core/Interfaces/ILogService.cs`, `Core/Interfaces/IPathProvider.cs`
- `Runtime/IO/PathProvider.cs`, `Runtime/IO/LogService.cs`
- `Runtime/Settings/AppSettings.cs`, `SettingsSchema.cs`, `SettingsStore.cs`
- `Runtime/Bootstrap/ServiceRegistry.cs`, `AppBootstrap.cs`
- `Scenes/Bootstrap.unity`, `Scenes/Mirror.unity` (both added to Build Settings: Bootstrap[0], Mirror[1])
- `docs/tasks.md`, `docs/decisions.md` (ADR-009), `docs/ai_handoff.md`
- Tooling: fixed `~/.claude/scripts/check-unity-mcp.ps1` to include port **6400** (Unity MCP default)

---

## Avatar load UI (M1) — built + LIVE-TESTED

`Mirror.unity` has `MirrorCanvas` (Screen Space Overlay) + `EventSystem` (StandaloneInputModule; project `activeInputHandler=2` Both) with **TextMeshPro** widgets: `PathInputField` (`TMP_InputField`, built via `TMP_DefaultControls.CreateInputField`), `LoadButton` (uGUI Button + TMP label), `StatusText` (`TextMeshProUGUI`). `AvatarLibraryPanel` (fields are `TMP_InputField`/`Button`/`TMP_Text`; asmdef refs `Unity.TextMeshPro`) sits on `MirrorCanvas`, refs wired via `execute_code`. `AppBootstrap` finds it on `sceneLoaded` → `Initialize(session, log)`.

**Live test PASSED (2026-08-05):** set `avatar.lastPath` to `sampleVRMFiles/StrawberryPrincess.vrm` → auto-load on play → UniVRM parsed, humanoid validated, parented under `AvatarRoot`, rendered. Log: "Avatar loaded: StrawberryPrincess.vrm", 0 errors. Screenshot showed avatar (A-pose rest) + TMP UI. `avatar.lastPath` currently still points at that sample (auto-loads each run; clear it in `settings.json` to stop).

Sample VRMs (VRoid Hub): `C:\Unity\viitorx-vrm-avtar-unity-base-project\sampleVRMFiles\` — StrawberryPrincess, VIPE_Hero__2421, Skull, 2 numbered.

## M2 tracking pipeline — built + live-verified

Chain (all decision-independent, MediaPipe not yet installed):
`ICameraCapture` (video/webcam) → `IBodyTrackingProvider` (fake) → `JointFilterPipeline` (One-Euro ×33) → `HumanoidArmRetargeter` (vector FK, arms) → avatar bones.

- **Capture:** `VideoFileCaptureService` plays `sample video.mp4` into a RenderTexture (shown in `CameraPreview` corner). `WebcamCaptureService` ready; toggle `AppBootstrap.useVideoSource`.
- **Tracking:** `FakeBodyTrackingProvider` emits a waving-arms `PoseFrame` each `Tick`. `AppBootstrap.LateUpdate` runs Tick → filter → retarget.
- **Retarget:** `HumanoidArmRetargeter.Bind(animator)` captures rest rotations/dirs and **disables the Animator** (so it does not fight bone writes); `Apply(frame)` sets arm bone world rotations via `RotationFromVectors` (FromToRotation).
- **Loader change:** VRM now loaded with `ControlRigGenerationOption.None` so humanoid bones are directly drivable.
- **Verified:** play → `animatorEnabled=False`, arm bones driven (non-rest euler), 0 errors. Motion is subtle on the StrawberryPrincess (big dress) but bone data confirms it.

## MediaPipe pose — LIVE & Async Perf (M2 core done)

Plugin at `Assets/MediaPipeUnity` (homuler). `MediaPipePoseProvider` (`Runtime/Tracking/MediaPipe/`, Tracking asmdef refs `Mediapipe.Runtime`):
- CPU delegate, `RunningMode.VIDEO`, full model via **`BaseOptions.modelAssetBuffer`** (reads `StreamingAssets/MediaPipe/pose_landmarker_full.bytes`, 9.4 MB, Google official) — no ResourceManager/AssetLoader/GpuManager needed. Global init = `Protobuf.SetLogHandler` + `Glog.Initialize` (once, guarded).
- **Async ThreadPool Offloading (Perf):** `ReadTextureOnCPU` & `BuildCPUImage` run on the main thread; inference `TryDetectForVideo` is queued on `ThreadPool.QueueUserWorkItem` with double-buffering (`pendingFrame`/`latest`) and frame dropping when busy. Main thread render performance remains silky smooth.
- Wired via `AppBootstrap.useMediaPipeTracking` (true); **falls back to `FakeBodyTrackingProvider`** if start fails.
- **Verified:** play → "MediaPipe pose provider started (CPU, VIDEO, full model, async worker)", avatar arm bones driven smoothly, 0 console errors.

## Retarget — full body + configurable mapping (done)

- `PoseSpaceConverter` (Core): per-axis flip; MediaPipe world (X right, Y down, Z toward camera, metres) → Unity. Wired via `AppBootstrap.poseFlipX/Y/Z` (defaults `false/true/true`). **Mirror sign = flipX** — finalize live once webcam is up.
- `HumanoidPoseRetargeter` (replaces the arm-only one): generic segment list (spine, neck, arms, legs); sets **world** rotation per bone via `RotationFromVectors` (FromToRotation of rest-dir → tracked-dir), so limbs stay correct as parents move. **Confidence gate**: a segment is held if any endpoint landmark < `retargetMinConfidence` (0.5).
- Verified live: on the upper-body sample video, arms track the person while spine/legs are correctly held (their landmarks are low-confidence). Avatar is small (StrawberryPrincess chibi) so it renders small in the default camera.

## Retarget: roll + hips (done)

- **Roll pinned:** `RotationFromVectors.Delta(restDir, targetDir, referenceUp)` builds full orientations via `LookRotation` with a shared reference axis (world forward) and returns the delta — twist no longer arbitrary. `SafeUp` guards the parallel-degenerate case.
- **Hips driven:** `HumanoidPoseRetargeter.BindHips`/`ApplyHips` — rest basis from upper-leg line + (neck−hips) up; target basis from (LeftHip−RightHip) + (midShoulder−midHip); `hips.rotation = targetBasis·inverse(restBasis)·restHipsRotation`. Applied before limbs. **Position not driven** (world landmarks are hip-centred → no absolute translation; avatar stays in place — fine for a mirror).
- Verified: hips rotation tracks the person, legs gate correctly on the upper-body video, no NaN. Visual quality is hard to judge on the chibi StrawberryPrincess — assess with webcam + normal avatar + `MirrorCameraController`.

## UI hide-on-load + camera framing (done, verified)

- `IAvatarSession.AvatarChanged` event fires on every successful load (manual + auto). `AvatarLibraryPanel` hides `PathInputField`/`LoadButton`/`StatusText` on it; **Tab** toggles them back.
- `MirrorCameraController` (`VirtualMirror.Rendering`, component on Main Camera in Mirror.unity) frames the avatar to renderer bounds on `AvatarChanged`. Verified: UI hidden after load, camera repositioned `(0,1,-10)→(0,0.76,-4.59)` for the chibi. Note: it fits *full* bounds, so a wide-dress avatar sits back — bias/padding is tunable in `MirrorCameraController` (`paddingFactor`, `lookHeightFraction`).

## AnimationRigging IK Driver (done, verified)

- `IIkSolver` interface (`VirtualMirror.Core`): `Bind`, `Apply`, `Unbind`, `IsBound`.
- `AnimationRiggingIkDriver` (`VirtualMirror.IK` asmdef): builds `Rig` + `RigBuilder` with `TwoBoneIKConstraint` for LeftArm, RightArm, LeftLeg, RightLeg. Checks bone transforms before component attachment, and maintains `animator.enabled = true` to allow PlayableGraph evaluation.
- `Glog` lifecycle fix in `MediaPipePoseProvider.cs`: paired `Glog.Initialize` on start with `Glog.Shutdown` in `Dispose()` guarded by `globalInitialized` flag, preventing double-initialization process crashes across domain reloads.
- Verified in play mode: 0 console errors, clean execution.

## EditMode Unit Tests (done, 13/13 passed)

- `VirtualMirror.Tests` asmdef created under `Tests/EditMode/`.
- Test suites: `PoseSpaceConverterTests` (4 tests), `OneEuroFilterTests` (4 tests), `RotationFromVectorsTests` (5 tests).
- Automated test run targeting `VirtualMirror.Tests` passed 13/13 tests cleanly in 0.62s.

## Webcam Input Integration & Mirror Calibration (done, verified)

- `AppBootstrap.useVideoSource` set to `false` (switched from video player to live webcam).
- `WebcamCaptureService` initialized live hardware device (`Logi C270 HD WebCam` @ 1280x720 30fps).
- `poseFlipX` set to `true` on `AppBootstrap` instance in `Bootstrap.unity` scene to finalize mirror reflection calibration.
- Verified in Play mode: webcam feed live, MediaPipe pose provider tracking live, 0 console errors.

## Milestone M4: Calibration & UX (done, verified)

- **Diagnostics HUD**: Created `PerformanceMonitor` and `DiagnosticsHudPanel`. Renders real-time FPS, frame time (ms), active tracking provider, webcam hardware info, and loaded avatar name.
- **Calibration Settings Panel**: Created `CalibrationSettingsPanel` with uGUI/TMP controls for WebCam device selection dropdown (`WebCamTexture.devices`), mirror flip toggle, IK toggle, face/hand toggles, and filter cutoff/beta smoothing sliders.
- **Automated Tests**: Added `PerformanceMonitorTests`. All 21 EditMode unit tests passed 100% cleanly (21/21 passed in 0.07s).
- **Play Mode Verification**: Confirmed `"Diagnostics HUD wired."` and `"Calibration settings panel wired."` logs with 0 console errors.

## Next recommended task (M5: Release Candidate)

1. **Built-in Avatar Library** (5–10 licensed `.vrm` avatars under `StreamingAssets/Avatars/`).
2. **Standalone Windows EXE packaging** & ThirdPartyNotices.txt.

## Decisions locked (this session)

- **ADR-006 async:** Unity Awaitable (Accepted).
- **UI toolkit:** uGUI (legacy `UnityEngine.UI`).
- **File input:** absolute path typed into the Load field for now; native file browser deferred to the user.

---

## Constraints for implementers

- Follow AGENTS.md + `.editorconfig`: no `var`, no target-typed `new()`, no expression-bodied members, K&R braces, **no `this.` qualifier except to disambiguate a ctor param from a field**, `[SerializeField] private` fields camelCase, separate using-groups (blank line between `System`, `UnityEngine`, `VirtualMirror`).
- Settings DTOs use `[SerializeField] private` camelCase backing fields + public PascalCase properties so JSON keys stay camelCase (JsonUtility maps field names).
- Do not couple UI to MediaPipe / UniVRM. Concretes depend on Core interfaces; App wires them.
- Update `tasks.md` + this file after each meaningful chunk.

---

## Environment notes

- **Documents is OneDrive-redirected:** `PathProvider` resolves `Environment.SpecialFolder.MyDocuments` to `C:\Users\<user>\OneDrive - Viitorcloud Technologies Pvt Ltd\Documents\MyMirror`. Expected on this machine; do not hardcode.
- Unity MCP bridge runs on port **6400**.

---

## Open questions

- Exact MediaPipe Unity plugin package / repo to pin
- IL2CPP vs Mono for shipping player
- UniTask vs Unity Awaitable (ADR-006)
- License sources for the 5–10 built-in VRM avatars
