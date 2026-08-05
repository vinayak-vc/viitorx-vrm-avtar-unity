# Virtual Mirror — AI Handoff

Last updated: 2026-08-05  
Purpose: next agent can continue without re-deriving context.

---

## Current state

- **Milestone:** **M0 done + play-verified. M1 core (avatar load/swap/persist) done + play-verified; UI/builtins remain.**
- **Code:** Runtime scaffold + infrastructure + avatar loader pipeline implemented and compiling (0 errors).
- **Packages pinned:** `com.unity.animation.rigging` 1.4.1, `com.vrmc.vrm` 0.126.0 (VRM 1.0 via OpenUPM) + `com.vrmc.gltf` transitive. See `19_ThirdParty.md §1a`.
- **Docs:** Full SDS `00`–`25` + handoff set. `decisions.md` has ADR-006 (Accepted: Unity Awaitable) + ADR-009 (assemblies).

### What runs today
`Bootstrap.unity` → `AppBootstrap` (composition root) constructs `PathProvider`, `LogService`, `SettingsStore`, `UniVrmAvatarLoader`, wraps them in `ServiceRegistry`, loads settings, additive-loads `Mirror.unity`, then on `sceneLoaded` finds `AvatarRoot`, builds `AvatarSessionController`, and auto-loads `avatar.lastPath`. Verified in play mode:
- Creates `Documents/MyMirror/{Avatars,Backgrounds,Calibration,Logs,Settings}`
- Writes `Settings/settings.json` (schema v1, matches SDS-015) + daily log with session header
- Avatar session wires up; auto-load is a clean no-op until `avatar.lastPath` is set (0 errors)

### Avatar pipeline (M1)
- `IAvatarLoader.LoadAsync(request, parent, ct) : Awaitable<AvatarLoadResult>` (Core) → `UniVrmAvatarLoader` (Avatar asmdef) reads bytes async, `Vrm10.LoadBytesAsync` with `RuntimeOnlyAwaitCaller`, parents under root, validates `animator.isHuman`, size gate (default 256 MB).
- `AvatarSessionController` (App): load-new-then-dispose-old swap, keeps old avatar on failure, persists `avatar.lastPath` + debounced save.
- **Not yet runtime-tested with a real `.vrm`** (no test file available).

---

## Understanding (do not re-litigate)

Product = webcam virtual mirror for VRM avatars.  
Stack = Unity 6 + MediaPipe (Pose/Face/Hands) + UniVRM + Animation Rigging.  
Architecture = layered providers, hot-swap, filter → retarget → IK → VRM.  
V1 = single user Windows EXE; no marketplace yet.

---

## Assemblies (per ADR-009)

`VirtualMirror.Core` (interfaces/models) ← `.IO` ← `.Settings` ← `.App` (composition root).  
Feature asmdefs (`.Tracking`, `.Retargeting`, `.IK`, `.Avatar`, `.UI`, `.Editor`, `.Tests`) not created yet — add with their first code.

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

## MediaPipe pose — LIVE (M2 core done)

Plugin at `Assets/MediaPipeUnity` (homuler). `MediaPipePoseProvider` (`Runtime/Tracking/MediaPipe/`, Tracking asmdef refs `Mediapipe.Runtime`):
- CPU delegate, `RunningMode.VIDEO`, full model via **`BaseOptions.modelAssetBuffer`** (reads `StreamingAssets/MediaPipe/pose_landmarker_full.bytes`, 9.4 MB, Google official) — no ResourceManager/AssetLoader/GpuManager needed. Global init = `Protobuf.SetLogHandler` + `Glog.Initialize` (once, guarded).
- Per `Tick`: `TextureFramePool` → `TextureFrame.ReadTextureOnCPU(capture.CurrentTexture, false, true)` → `BuildCPUImage` → `TryDetectForVideo(image, tsMs, ipo, ref result)` → `result.poseWorldLandmarks[0].landmarks` → `ToUnitySpace(x,-y,-z)` → `PoseFrame`.
- Wired via `AppBootstrap.useMediaPipeTracking` (true); **falls back to `FakeBodyTrackingProvider`** if start fails.
- **Verified:** play → "MediaPipe pose provider started (CPU, VIDEO, full model)", avatar arm bones change frame-to-frame with the sample-video person, 0 errors.

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

## Next recommended task

1. **`AnimationRiggingIkDriver`** — hand/foot IK targets for planted/reach accuracy (ADR-002).
3. **Perf:** full-model CPU `TryDetectForVideo` runs synchronously in `LateUpdate` (tens of ms). Move inference to a worker thread (publish immutable `PoseFrame`), or use the lite model / GPU delegate.
4. **ConfidenceGate with hysteresis + stale-hold** (SDS-025 §4/§7); current gate is a plain per-segment threshold.
5. **Camera framing** (`MirrorCameraController` — frame the avatar; matters more for small avatars), **built-in avatars**, **file browser**, **URP renderer asset**.
6. Swap video → webcam: `AppBootstrap.useVideoSource=false`, then finalize `poseFlipX`.

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
