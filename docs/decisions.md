# Virtual Mirror — Architecture Decision Records

Add a new ADR for every major choice. Do not silently contradict Accepted ADRs.

---

## ADR-001 — Engine and stack

- **Status:** Accepted  
- **Date:** 2026-08-05  
- **Decision:** Unity 6 (2022 LTS fallback) + MediaPipe Tasks + VRM 1.0 (UniVRM) + URP  
- **Why:** Mature, cost-effective, VRoid-compatible avatars, single CV ecosystem for pose/face/hands  
- **Alternatives:** Unreal; MoveNet-only; custom avatar format  

---

## ADR-002 — IK backend

- **Status:** Accepted (V1)  
- **Decision:** Unity Animation Rigging as default `IIkSolver`; FinalIK optional later  
- **Why:** No mandatory commercial dependency for V1; interface allows upgrade  

---

## ADR-003 — Provider interfaces

- **Status:** Accepted  
- **Decision:** All tracking/avatar/IK/camera behind interfaces in Core  
- **Why:** Replaceability (vision §3.2); test fakes; depth cameras later  

---

## ADR-004 — Platform

- **Status:** Accepted  
- **Decision:** Windows x64 EXE only for V1  
- **Why:** Webcam + native MediaPipe story clearest; scope control  

---

## ADR-005 — Avatar format

- **Status:** Accepted  
- **Decision:** VRM 1.0 runtime load; ship built-ins + user file pick  
- **Why:** VTuber/VRoid ecosystem; hot-swap without rebuild  

---

## ADR-006 — Async / threading style

- **Status:** Proposed  
- **Decision:** TBD — UniTask vs Unity Awaitable vs Task  
- **Action:** Decide during M0/M1 before widespread async code  

---

## ADR-007 — Azure Kinect

- **Status:** Accepted  
- **Decision:** Do not design V1 around Azure Kinect  
- **Why:** Discontinued hardware; keep optional depth behind interface for RealSense/OAK-D later  

---

## ADR-008 — OpenCV

- **Status:** Accepted  
- **Decision:** Do not take OpenCV dependency unless calibration/preprocess requires it  
- **Why:** Reduce package surface; MediaPipe + Unity enough for V1

---

## ADR-009 — Assembly layout refinement (M0)

- **Status:** Accepted  
- **Date:** 2026-08-05  
- **Decision:** Split infrastructure out of the `Core`/`App` pair from SDS-002 §4 into two thin low-level assemblies so feature code can depend on them without a cycle:
  - `VirtualMirror.Core` — interfaces + models + events only (refs UnityEngine). Holds `IPathProvider`, `ILogService`, `LogLevel`.
  - `VirtualMirror.IO` (refs Core) — `PathProvider`, `LogService`.
  - `VirtualMirror.Settings` (refs Core, IO) — `AppSettings` (+ nested groups), `SettingsSchema`, `SettingsStore`.
  - `VirtualMirror.App` (refs Core, IO, Settings) — composition root: `AppBootstrap`, `ServiceRegistry`.
- **Why:** `SettingsStore`/`LogService` are infrastructure needed by future feature assemblies (Tracking, Avatar, UI). Placing them in `App` (which references features) would invert the dependency. They cannot live in `Core` (Core must stay interface/model-only, and `AppSettings` would drag concretes in). Two dedicated infra assemblies keep the dependency graph acyclic and match the layer diagram (Infrastructure = files/logs at the bottom).
- **Consequence:** Feature asmdefs (`.Tracking`, `.Retargeting`, `.IK`, `.Avatar`, `.UI`, `.Editor`, `.Tests`) are created lazily as their code lands, to avoid empty assemblies.

---

## ADR-006 — Async / threading style

- **Status:** Accepted (2026-08-05, at start of M1)  
- **Decision:** Use **Unity Awaitable** (`UnityEngine.Awaitable` / `Awaitable<T>`) as the public async return type across the codebase. Await `Task`-based third-party APIs (e.g. UniVRM `Vrm10.LoadPathAsync`) directly inside `async Awaitable` methods.  
- **Why:** Native to Unity 6 (no extra dependency, unlike UniTask), low-allocation (fits AGENTS perf rules), first-class `CancellationToken` support, main-thread friendly. Keeps V1 free of another pinned package.  
- **Consequence:** `IAvatarLoader.LoadAsync` returns `Awaitable<AvatarLoadResult>`. UniTask stays optional (SDS-019 §2) and is not taken unless a concrete need appears.  
- **M0 note:** `SettingsStore` remains synchronous atomic write + debounce `Tick` — no async needed there.

---

## ADR-010 — MediaPipe backend

- **Status:** Accepted (2026-08-05, M2 kickoff)  
- **Decision:** Use **homuler MediaPipeUnityPlugin** (`com.github.homuler.mediapipe`) as the MediaPipe Tasks backend for Pose (and later Face/Hands). Real provider = `MediaPipePoseProvider : IBodyTrackingProvider`, consuming `ICameraCapture` frames.  
- **Why:** It is the de-facto "MediaPipe Tasks" for Unity — full PoseLandmarker (33 BlazePose landmarks + world landmarks) and one ecosystem that also covers Face + Hands (M3). Matches SDS-003/009/025 directly. Alternative (Unity Sentis + BlazePose ONNX) needs a full custom re-implementation of the MediaPipe graph.  
- **Cost / consequence:** Heavy native plugin (~hundreds MB), installed via the GitHub-release `.unitypackage` (not clean UPM). The plugin is imported manually by the user; the provider is written against its `PoseLandmarker` API. Until it is installed, the pipeline runs on `FakeBodyTrackingProvider`.  
- **Isolation:** Only `MediaPipePoseProvider` (Tracking asmdef) references the plugin. `IBodyTrackingProvider`, `PoseFrame`, retarget, and IK never reference MediaPipe types (SDS-009 §11).

---

## ADR-011 — IK target anchoring + neck aim source (M2 bugfix)

- **Status:** Accepted (2026-08-06)
- **Context:** On a full-body dance-video run (VRM1 `Anime-Boy+V-tuber`, IK Arm/Leg Driver ON) the avatar's arms hung at the hips instead of following the raised arms, and the head pitched up instead of forward.
- **Decision 1 — IK targets anchor at the avatar limb-root bone, scaling only the limb-local offset.** `AnimationRiggingIkDriver.UpdateArm/UpdateLeg` now place `target = avatarLimbRoot.position + hipsRestRotation * ((tip - rootJoint) * scale)` where `avatarLimbRoot` is the `UpperArm`/`UpperLeg` bone and `rootJoint` is the tracked shoulder/hip landmark. Previously the target was `hips.position + hipsRestRotation * (tipLandmark * scale)` — scaling the whole **hip-centred** landmark vector by the arm/leg ratio also shrank the torso→shoulder span, collapsing the hand/foot target down to hip level on any avatar smaller than the tracked human (all chibis).
- **Decision 2 — the neck FK segment aims `midShoulder → midEar`, not `midShoulder → Nose`.** The nose sits forward of the shoulder line; with the tuned tracking Z sign that forward offset entered the neck direction as a backward tilt, pitching the head up. The ear midpoint is centred and near-vertical (≈ no forward bias), so the head stays level/forward. Matches SDS-011 §4 ("Head ← nose–ear plane") and SDS-007 §4. Trade-off: neck pitch/nod is dropped; acceptable for V1 (head-forward correctness > nod fidelity). Head yaw/nod can later come from face-pose (SDS-007 §4) without reintroducing the bias.
- **Consequence:** IK arm/leg placement is now correct with IK ON (the documented FK/IK split stands — IK owns arms+legs, FK owns torso+neck). `hipsRestRotation` (ADR facing fix, Skull Y=180) is retained; only the anchor point and the scaled span changed. Superseded lines in `ai_handoff.md` (hips-anchored IK offset) noted there.
- **Files:** `Runtime/IK/AnimationRiggingIkDriver.cs`, `Runtime/Retargeting/HumanoidPoseRetargeter.cs`.
- **Verification:** ✅ **VERIFIED 2026-08-06** (Unity MCP, bridge port 6400). Arm IK targets no longer collapse to the hips: on an arms-down frame the hands sit at hip level, and under the waving-arms fake pose the hand rises to `+0.81` above the shoulder (`+2.02` above the hips) — following the input, not pinned. All 4 constraints `rotW=0/posW=1`. See `ai_handoff.md` "Live play-verification".

---

## ADR-012 — Neck undriven + leg-IK lower-body gate (M2 bugfix, iter 2)

- **Status:** Accepted (2026-08-06). **Supersedes ADR-011 Decision 2.**
- **Context:** After ADR-011 shipped and compiled, a webcam (seated, upper-body) run showed: arms track (ADR-011 Decision 1 confirmed), but (a) the head still craned **up**, (b) the legs sat **folded** by default, (c) wrist orientation didn't move.
- **Decision 1 — do not drive the neck from pose landmarks.** The ear-midpoint aim (ADR-011 Decision 2) still carried a small backward pitch under the tuned tracking Z convention; vector-aiming a near-vertical neck bone cannot reliably reproduce head pitch. `HumanoidPoseRetargeter.BuildDefinitions` no longer emits a Neck segment — the head rests forward. Head/neck orientation will be revisited from the **face landmarker head pose** (SDS-007 §4) with explicit axis handling, not from pose landmarks.
- **Decision 2 — gate leg IK on lower-body visibility.** `AnimationRiggingIkDriver.UpdateLeg` now requires the tracked ankle to sit at least `MinLegDropMetres` (0.35 m, MediaPipe scale) below the hip along the torso-up axis; otherwise the leg constraint weight is 0 and the leg rests straight. Rationale: on a seated/upper-body webcam the lower body is occluded, but MediaPipe still emits collapsed leg world-landmarks with pass-through confidence, which the leg IK folded onto. Implements SDS-011 §8 ("auto-disable leg IK when the lower body is not confidently visible") without a manual toggle.
- **Open (not yet done) — wrist/hand orientation.** IK stays position-only (`targetRotationWeight = 0`), so the Hand bone keeps its rest rotation while the arm moves ("wrist doesn't move"). Driving wrist rotation needs a palm basis from the MediaPipe **Hand** landmarks (wrist, index-MCP, pinky-MCP) written to the Hand bone (or as an IK target rotation) in the hand retarget path — deferred pending decision + local play-verify.
- **Files:** `Runtime/Retargeting/HumanoidPoseRetargeter.cs`, `Runtime/IK/AnimationRiggingIkDriver.cs`.
- **Verification:** ✅ **VERIFIED 2026-08-06** (Unity MCP, bridge port 6400). D1: `neck localEuler=(0,0,0)` (undriven) and head up-vector ≈ world-up → head rests forward/level, not craned. D2: leg gate passes on a full-body clip (legs track, not folded); the seated/occluded OFF case is by inspection of the `MinLegDropMetres` gate. 0 runtime errors.

---

## ADR-013 — Wrist/palm orientation from Hand landmarks (M3 enhancement)

- **Status:** Accepted (2026-08-06). Implements the open item in ADR-012.
- **Context:** Position-only arm IK left the Hand bone at rest rotation — the arm reached but the wrist never tilted/rolled ("hand moves, wrist doesn't").
- **Decision:** Derive a **palm orientation** from the MediaPipe Hand **world** landmarks and drive the avatar Hand bone with it **delta-from-neutral** (the same convention-robust technique that fixed the waist twist), not absolutely.
  - `MediaPipeHandProvider.PalmRotation`: `forward = wrist(0)→middleMcp(9)` (finger direction), `up = forward × (indexMcp(5)→pinkyMcp(17))` (palm normal) → `Quaternion.LookRotation`. Points run through the **same `PoseSpaceConverter`** as the body (so mirror/axis match); the provider now takes a converter (parity with `MediaPipePoseProvider`).
  - `HandFrame` carries `Left/RightWristRotation` + `Left/RightWristTracked` (reusable buffer; fake provider leaves them untracked → ignored).
  - `HumanoidHandRetargeter.ApplyWrist`: captures a neutral palm per hand on the first tracked frame; each frame applies `worldDelta = palm · inv(neutral)` to `hand.rotation`, slerped by `wristWeight`. Runs after IK in LateUpdate so it survives the rig. `Recalibrate()` re-captures neutral (bound to the existing **C** key alongside torso recalibration).
- **Why delta-from-neutral:** the absolute map from hand-landmark axes to a model-specific Hand-bone axis is convention-sensitive (the same class of bug that craned the head up twice). A relative delta cancels the constant offset, so only wrist *motion* shows and no per-avatar axis calibration is needed.
- **Tuning / safety:** `AppBootstrap.wristRotationWeight` (default 0.7, serialized) blends the effect; **0 disables** (fingers still curl). Slerp-clamped, hand-bone only → no explosion risk. Expect one live tuning pass on the weight (and possibly the palm `forward/up` choice) against a webcam, like `poseFlipX`.
- **Files:** `Runtime/Core/Models/HandFrame.cs`, `Runtime/Tracking/MediaPipe/MediaPipeHandProvider.cs`, `Runtime/Retargeting/HumanoidHandRetargeter.cs`, `Runtime/Bootstrap/AppBootstrap.cs`.
- **Verification:** pending local Unity play-test (webcam, hands in frame) once the Unity MCP bridge reconnects. Limitations: world-space apply may partly double-count forearm roll when the arm is far from the neutral pose; per-joint finger fidelity unchanged (still single-angle curl).

---

## ADR-014 — Accuracy strategy: staged cheap-wins then GPU 3D-pose (post-M5)

- **Status:** Accepted (2026-08-06). Directed by the user after a live webcam full-body test.
- **Context:** On a real webcam run (user standing far, full body in frame, pressed **C** to calibrate) the avatar replicated only ~20–30% of the user's motion — arms lagged and frequently landed in wrong poses, fingers/wrist were dead, legs jittered while the user stood still, and the body floated. HUD confirmed `MediaPipe Pose (CPU)` was active, so this is the **real pipeline's** quality, not the fake-provider regression. Diagnosis (agent watched the recording frame-by-frame): dominant causes are **(1) monocular MediaPipe 2D + weak Z** (rotations reconstructed from noisy vectors — the documented ceiling) and **(2) the user standing far from the camera** (hands too small for the Hand Landmarker → dead; body landmarks noisy), compounded by over-aggressive smoothing (`beta=0.02`) adding lag and confidence-gating damping the extremes.
- **Decision:** Pursue accuracy in two stages rather than immediately rewriting the tracking backend.
  - **Stage 1 — cheap wins on the current stack (this session, in progress):** (a) pose model **Full → Heavy** (`pose_landmarker_heavy.bytes`, 29 MB, wired via `poseModelFileName`); (b) One-Euro filter made **live-tunable** (`OneEuroFilter.SetParameters` / `JointFilterPipeline.SetParameters`, wired to the calibration sliders) with a more responsive **default `beta` 0.02 → 0.2** to cut lag; (c) **framing guidance** — the single biggest free win is the user standing **closer / upper-body** so hands are resolvable. T-pose calibration (SDS-025 §5) remains a Stage-1 candidate but was deferred pending the re-test, since the video's dominant errors are distance-noise + lag, not proportion.
  - **Stage 2 — GPU 3D-pose provider (the real fix, if Stage 1 is insufficient):** a new `IBodyTrackingProvider` backed by **Unity Sentis or ONNX Runtime (DirectML)** on the idle RTX 3060, emitting true 3D limb rotations + depth (RTMPose+MotionBERT lifting, or BlazePose-GHUM-3D-heavy / Sapiens). The architecture already isolates this behind `IBodyTrackingProvider` (ADR-003), so avatar/IK/UI are untouched. Will get its own ADR when started.
- **Why staged:** Stage-1 is minutes of work and closer framing alone may lift upper-body use to acceptable; if it does not, the 3D provider is justified with evidence and Stage-1 effort (esp. calibration) is not wasted on a backend we're replacing. Honest expectation set with the user: **monocular-2D-at-distance has a hard ceiling; "very accurate" full-body ultimately needs the 3D path (and possibly a depth camera, ADR-007).**
- **Files (Stage 1):** `Runtime/Tracking/Filtering/OneEuroFilter.cs`, `Runtime/Tracking/Filtering/JointFilterPipeline.cs`, `Runtime/Bootstrap/AppBootstrap.cs`, `Scenes/Bootstrap.unity` (serialized `poseModelFileName`, `filterBeta`), `StreamingAssets/MediaPipe/pose_landmarker_heavy.bytes` (new).

---

## ADR-015 — GPU 3D-pose provider: Unity Sentis + modern RTMW3D (RTMPose3D family) whole-body model

- **Status:** In progress (2026-08-06). Runtime = **Sentis (CONFIRMED — no ORT fallback needed)**. Model = **RTMW3D** (POC/non-commercial).
- **✅ FEASIBILITY SPIKE PASSED (2026-08-06):** `rtmw3d-x.onnx` (369 MB) placed at `Assets/SentisModel/`, Sentis `com.unity.ai.inference` 2.6.1 installed on this machine, reimport produced `assetType = Unity.InferenceEngine.ModelAsset` with **0 errors / 0 warnings** — no unsupported operators. Offline ONNX check corroborates: opset 17, 25 unique ops, all standard (Conv, Resize, DepthToSpace, MatMul, HardSigmoid, GlobalAveragePool, Slice/Split/Reshape/Transpose…), no exotic/custom ops. **Confirmed I/O** (both ONNX proto + Sentis `ModelLoader.Load`): input `input` (1,3,384,288) NCHW RGB; 3 SimCC outputs `output` [1,133,576]=**x** (288·2), `1554` [1,133,768]=**y** (384·2), `1556` [1,133,576]=**z**. Decode = per-keypoint argmax over last dim ÷ 2.0 (simcc split ratio) → x∈[0,288), y∈[0,384), z depth (scale TBD, tune); then 133→33 `JointId` (body subset). Runtime input normalization = ImageNet mean/std (RTMPose default) — must be applied before inference (verify vs a possibly-baked normalization in this export; op list has Mul/Add/Div but no explicit Sub — confirm live).
- **✅ Live GPU inference verified (2026-08-06, this machine, RTX 3060):** `new Worker(model, BackendType.GPUCompute)` + zero input → runtime outputs match (x(1,133,576)/y(1,133,768)/z(1,133,576)). **Warm end-to-end latency ~33–38 ms/frame** (schedule + readback all 3 outputs, synchronous); first run ~2.8 s (shader compile + 369 MB upload). → RTMW3D-**x** is real-time viable (~30 FPS) on the 3060; the provider should still use **async readback + frame-drop** (poll `IsReadbackRequestDone`) so the 35 ms doesn't stall the main thread. RTMW3D-**L** remains a later optimization (needs MMDeploy export from the `.pth`) only if more headroom is required.
- **RESOLUTION (2026-08-06, later — supersedes the "model OPEN" analysis below):** User clarified this is a **POC with no commercial use → non-commercial licenses are acceptable**, and asked for a **modern, accurate** model (ThreeDPose is 2020/dated). Chosen model: **RTMW3D** (OpenMMLab RTMPose3D family, 2024–2025) — real-time **whole-body 3D: 133 keypoints = body 17 + feet 6 + face 68 + hands 42** (directly addresses the dead-hands problem), Apache-2.0. Ready ONNX export: **`Soykaf/RTMW3D-x`** on HuggingFace, file `rtmw3d-x_8xb64_cocktail14-384x288-b0a0eab7_20240626.onnx` (~369 MB, opset from PyTorch-2.1 export, input 384×288). Download URL: `https://huggingface.co/Soykaf/RTMW3D-x/resolve/main/onnx/rtmw3d-x_8xb64_cocktail14-384x288-b0a0eab7_20240626.onnx?download=true`. **Runtime caveat:** modern models often exceed Unity Sentis's supported ONNX operators; if RTMW3D fails to import into Sentis, switch the runtime to **ONNX Runtime (DirectML)** (there's a proven Unity ORT integration by asus4) — this is the one open runtime risk. **Feasibility spike (the immediate next task):** import the ONNX into Sentis (put under `Assets/`), read the import log for unsupported-operator warnings; if clean → build the provider on Sentis; if not → ONNX Runtime. Model file was downloaded to the session scratchpad (not committed — re-download via the URL above; ~369 MB, keep OUT of `Assets/StreamingAssets`, it must be a regular `Assets/` ONNX so Sentis imports it → ModelAsset). **RTMW3D-x is the "extra-large" variant (~369 MB); for real-time on the RTX 3060, exporting/using RTMW3D-L may be faster (only `.pth` on HF `rbarac/rtmpose3d` → needs MMDeploy export).**
- **Decode note (for the provider):** RTMPose/RTMW use a **SimCC** representation — model outputs per-keypoint 1D coordinate classification vectors (x and y bins) plus a z/depth output; decode = argmax over the SimCC bins → (x,y) in the 384×288 input space, plus the z coordinate, then map the 133 keypoints → our 33 `JointId` slots (body subset) in Unity space (hips-centred metres). The heavy decode is post-processing outside the model (good — fewer exotic ops for Sentis).
- **EXACT z decode (mmpose `SimCC3DLabel`, confirmed 2026-08-06 — Run-6 fix):** x/y are pixels: `argmax ÷ simcc_split_ratio(2.0)` → x∈[0,288], y∈[0,384]. **z is NOT a pixel — it is root-relative metric depth:** `keypoints[...,2] = (z_coord / (input_size[-1]/2) − 1) × z_range` where `z_coord = zIndex/2`, `input_size[-1]=288`, `z_range=2.1744869`. Simplified: `z_metric = (zIndex/(ZBins/2) − 1) × 2.1744869`, `ZBins=576` → z centred at 0, ≈[−2.17,+2.17]. `root_index=(11,12)` (hips) → depth is already pelvis-relative, so no extra root subtraction needed. Run-5 bug was decoding z like a pixel (`z/InputHeight`, positive-only) → flat depth → muted limb reach. **Fixed** in `SentisPoseProvider.DecodeFrame`; z magnitude scaled by a separate serialized `sentisDepthScale` (independent of `sentisMetreScale`) since the metric↔pixel scale ratio has no closed form without camera intrinsics → live-tune. Also added: 3:4 centre-crop (`sentisPersonCrop`), looser 3D One-Euro profile (`sentisFilterMinCutoff`/`sentisFilterBeta`), HUD provider label. Source: mmpose `projects/rtmpose3d/rtmpose3d/simcc_3d_label.py`. **Compiles clean; motion pending a real-time Play.**

### Original analysis (pre-resolution) — kept for the reasoning trail:
- **Context:** Stage 2 of ADR-014. User directed "start the GPU 3D provider" and chose the **Sentis + single-stage 3D** stack.
- **Runtime decision (Accepted): Unity Sentis**, package **`com.unity.ai.inference` 2.6.1** (formerly `com.unity.sentis`; display name reverted to "Sentis" in 2.4). Installed and build-verified (0 compile errors). Namespace/assembly **`Unity.InferenceEngine`** (`Worker`, `ModelLoader`, `ModelAsset`, `TextureConverter`, `BackendType.GPUCompute`). Chosen over ONNX Runtime + DirectML to avoid native-DLL integration risk given limited in-editor play-verify. Runs on the RTX 3060 via compute.
- **Provider design (planned):** `SentisPoseProvider : IBodyTrackingProvider` in `Runtime/Tracking/Sentis/` (Tracking asmdef + `Unity.InferenceEngine` ref), mirroring `MediaPipePoseProvider`: read `ICameraCapture.CurrentTexture` → `TextureConverter.ToTensor` → `Worker.Schedule` on `GPUCompute` → readback → **decode → 33 `JointId` slots in Unity space** (reusable double-buffered `PoseFrame`). Wire behind `AppBootstrap.useSentis3dTracking` (default off) with MediaPipe fallback. The decode is **model-specific** and must be written against the actual model's inspected input/output tensors (cannot be guessed), then tuned live (like `poseFlipX`).
- **Model decision — OPEN, blocked on licensing (this is the crux):** Unity's own Sentis pose sample is **BlazePose = the same GHUM weak-Z** we already run on CPU → no accuracy gain, so a genuine improvement needs a dedicated single-image 3D model. Landscape:
  - **ThreeDPose (Digital-Standard `Resnet34_3inputs_448x448`)** — purpose-built for VRM webcam mocap, true 3D, community Sentis/Barracuda decode exists, ~180 MB, 3-frame temporal input. **License: the repo README states "Non-commercial use only"** (hobby/research) with no commercial pathway offered → **NOT usable in a commercial Viitorcloud product**. (An earlier "$500 commercial" figure came from an unreliable search snippet and was wrong — the authoritative README is non-commercial-only.) Project is unmaintained; bundled videos are not copyright-free. **Ruled out for commercial use.**
  - **Research 3D models (MotionBERT / VideoPose3D / many single-image 3D nets)** — often **non-commercial licenses (CC-BY-NC)** → unusable in a commercial product without clearance.
  - **Permissive free 3D (e.g. MobileHumanPose, MIT)** — free for commercial but lighter/older; accuracy uncertain and needs full custom decode + validation.
  - **BlazePose in Sentis (free/Apache)** — no accuracy gain over current; only moves inference to GPU.
- **Why blocked here:** picking/downloading/integrating a model has **license + cost implications for the organization** (org policy: do not make commitments on the org's behalf; highlight risks) and **cannot be validated blind** (needs the model imported to inspect tensors + a live webcam validation loop). Escalated to the user.
- **Done this session:** Sentis installed + verified; API confirmed; landscape + licensing analyzed. **Next:** user picks the model/license path → download + import → inspect tensor I/O → implement decode → wire + live-validate. Also planned regardless: root/hip grounding (stop the float) + velocity outlier-reject (leg jitter).
- **Files so far:** `Packages/manifest.json` (+`com.unity.ai.inference: 2.6.1`).

---

## ADR-016 — Depth camera (OAK-D) provider for real metric depth (Accepted — Option B, on-device 3D)

- **Status:** Accepted (2026-08-06) — user chose **Option B (on-device 3D pose)**. Realizes the depth-camera option ADR-007 reserved. User has an **OAK-D** in hand. **B1 (in-Unity native plugin) built + tried, then ABANDONED as too fragile on Unity 6.3** — 3 distinct native failures (depth-config crash → main-thread freeze → crash-on-stop native teardown) + no keypoints, each crashing/hanging Unity; the crash-on-stop is in the precompiled plugin's native lifecycle and isn't fixable from managed C#. `useOakDTracking=0` (app stable on RGB fallback). **Pivoting to B2 (Python sidecar)** so the DepthAI native code runs OUT of the Unity process (can't crash Unity): `geaxgx/depthai_blazepose` (33 BlazePose landmarks = our `JointId`, real stereo 3D, edge mode) → UDP JSON → a socket-reading `IBodyTrackingProvider` in Unity (no native DLL in-process). Needs Python + `pip install depthai` + a tiny UDP protocol. Awaiting user go-ahead.
- **Update (2026-08-07):** B2 built and **compile-verified in Unity 6.3 (0 CS errors)** — `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs` reads the sidecar UDP stream (33 BlazePose landmarks → `PoseSpaceConverter` → double-buffered `PoseFrame`), wired in `AppBootstrap` behind `useOakUdpTracking`/`oakUdpPort` (top priority), HUD "OAK-D 3D (UDP sidecar)". **Still NOT live-verified** (no OAK-D attached this session). **B1 code fully REMOVED this session:** deleted `OakDPoseProvider.cs` and all `AppBootstrap` B1 wiring (`useOakDTracking` + the `oakModel*/oakDeviceNum/oakLandmarkThreshold` fields, the StartTracking block, the on-device HUD branch); the 81 MB native plugins were already gone. B2 is the sole OAK path going forward. Phase 2 (measured per-keypoint depth vs the current GHUM `landmarks_world`) remains open and is hardware-in-the-loop.
- **Update (2026-08-07, later — OAK-D attached):** **Phase-1 B2 now LIVE-PROVEN end-to-end on hardware.** Repaired the broken sidecar venv (missing `Scripts/` regenerated with Python 3.10, site-packages intact), device `14442C10F143D3D200` detected, sidecar streams BlazePose over UDP (`--frame_height 200`; 400 errors on this OV9782 unit → ISP HW-ratio limit), Unity `useOakUdpTracking=true` saved + Play-verified: `OAK-D UDP pose provider listening…`, `rx=… parseErr=0` (datagrams received + parsed clean), 0 errors, HUD "OAK-D 3D (UDP sidecar)". The GHUM-depth Phase-1 pipeline is stable on the real camera. **Remaining:** user visual confirm + `poseFlipX/Y/Z` tuning (not live-tunable), then Phase-2 measured depth ([doc 26](26_OakDDepthPhase2.md)); geaxgx already exposes `body.landmarks` in video-px and builds RGB-aligned stereo depth, so Phase-2 = add a depth `XLinkOut` + host-side back-projection.
- **Update (2026-08-07, iter-2/3 — device live, extensive user iteration):** Device identified as **OAK-D-PRO-W** (wide ~120° lens, **OV9782 1280×800** color). Landed on the live device: **(1) FOV** — geaxgx assumed a 1920×1080 IMX378 → invalid ISP scale on the OV9782 → centre-crop (very narrow view; user's phone at the same spot showed full body). Fixed by adding native color modes to the sidecar (`--color_res 800p --color_scale 1/2` → **640×400 full FOV**, geometry computed from the real sensor). **(2) Position tracking** — the OAK's measured mid-hip `xyz` now drives the **avatar root** position (walk/jump follow the user) — the depth camera's signature win over mono webcams. `xyz` is **Y-UP** (opposite the GHUM landmarks' Y-down) → dedicated `PoseSpaceConverter.ToUnityPosition`; neutral-relative + smoothed. **(3) Legs** — track only when visible; held straight (bind pose) when occluded, because BlazePose hallucinates occluded legs behind the desk. `trackLegs` flag; ✅ **user-confirmed OK**. **(4) Auto-calibration** — neutral facing captured only when the user is front-facing → **no C keypress needed** (production has no keyboard). **(5) Retarget architecture change → see ADR-017.** Face + fingers remain blocked on a working RGB webcam (the `Meta Quest 3` virtual cam won't open — `Could not connect pins`); the OAK RGB is not fed into Unity in B2. Phase-2 measured per-keypoint depth (doc 26) still open. Full modified-files list in `ai_handoff.md`.
- **Chosen approach — on-device BlazePose 3D:** run the pose NN on the OAK-D's Myriad X VPU and fuse the stereo depth on-device → **metric 3D body landmarks straight from the camera** (offloads the host). The reference is `geaxgx/depthai_blazepose` (edge mode): **33 BlazePose landmarks = the SAME topology as our `PoseFrame`/`JointId`**, with real 3D from stereo. So the provider maps ~1:1 into our contract and the retarget/IK/avatar path is untouched — and measured depth removes the monocular front/back ambiguity (the "hands behind" defect) + the `sentisDepthScale`/`metreScale` guesswork.
- **Two integration mechanisms (the open fork):**
  - **B1 — `luxonis/depthai-unity` native plugin (all-Unity):** official, MIT, precompiled C#↔C++ (depthai-core) libs, has a high-level **Pose** pipeline that already returns 3D landmarks in Unity; URP-supported. Supports Windows "Unity 2021.2+" but last significant update ~2024-01 → **Unity 6.3 (2026) import/compat is the main risk**, and the pose C# API is undocumented (must read `src/`). Cleanest IF it imports.
  - **B2 — `geaxgx/depthai_blazepose` Python sidecar + IPC:** run the well-maintained Python app (edge mode, on-device) and stream the 33 3D landmarks to Unity over a local UDP/socket; `OakDPoseProvider` reads the socket. Decoupled from Unity-plugin compat; more robust to get working, but adds a Python process + a small IPC protocol.
- **Plan:** verify B1 import into Unity 6.3 first (quick feasibility); if it imports + exposes pose landmarks, build `OakDPoseProvider : IBodyTrackingProvider` on it; else fall back to B2 (sidecar). Either way: new `Runtime/Tracking/OakD/OakDPoseProvider.cs`, `AppBootstrap.useOakDTracking` flag with Sentis/MediaPipe fallback, 33→33 `JointId` map, landmarks through a `PoseSpaceConverter` for axis/mirror parity. **Needs the user for hardware (plug in OAK-D) + SDK/plugin install + live testing — I cannot validate without the device.**
- **Dependency/cost:** native DepthAI dependency (B1 precompiled libs, or B2 Python+depthai). Windows+USB (ADR-004). Isolation per SDS-009 §11: only the OAK-D provider references DepthAI; `PoseFrame`/retarget/IK/UI unchanged. RGB Sentis/MediaPipe paths remain as no-hardware fallbacks via the interface.
- **B1 concrete recipe (inspected the `luxonis/depthai-unity` source 2026-08-06 — clone at `D:/du/depthai-unity`):**
  - **Model = MoveNet single-pose (17 COCO keypoints)**, NOT BlazePose 33. Blob `movenet_singlepose_lightning_3.blob` (8.8 MB, present). 17 = nose/eyes/ears/shoulders/elbows/wrists/hips/knees/ankles → covers every joint the retarget/IK drive (arms/legs/torso). No feet/hands/face from OAK (keep those on MediaPipe). COCO-17 → our `JointId` maps 1:1 for the body subset (same order as the RTMW3D mapping's first 17).
  - **API:** native `depthai-unity.dll` via `[DllImport]`: `InitBodyPose(PipelineConfig)` + `BodyPoseResults(...)` returns a **JSON string**; parse `landmarks[]` where each has `index`, `location.x/y/z` (**millimetres, real 3D from stereo** when `useSpatialLocator=true`+depth), `xpos/ypos` (pixels). C# ref: `OAKForUnity/URP/Assets/Plugins/OAKForUnity/Scripts/Predefined/BodyPose/DaiBodyPose.cs` (landmark = `new Vector3(x,y,z)/1000f`). Uses SimpleJSON.
  - **Native libs present & REAL (committed, not LFS) for Windows:** `depthai-unity.dll`, `depthai-core.dll`, `depthai-opencv.dll`, `libusb-1.0.dll`, `opencv_world454.dll` under `OAKForUnity/URP/Assets/Plugins/OAKForUnity/NativePlugin/Windows/`. Plus `Netly` (Byter.dll/Netly.dll — for the python-unity bridge, optional) and the `OAKForUnity/Scripts` framework (`PredefinedBase`, `Device`, `PipelineConfig`, structs).
  - **Import step (feasibility gate):** copy the `OAKForUnity` plugin folder (Scripts + NativePlugin/Windows + Models + Editor + SimpleJSON + Netly deps) into our `Assets/Plugins/`; refresh; **check it compiles on Unity 6.3** (the risk — plugin targets Unity 2021.2/2022). If clean → write `OakDPoseProvider` calling the native `InitBodyPose`/`BodyPoseResults` directly (parse JSON → 17 landmarks → mm→m → PoseSpaceConverter → hip-centred `PoseFrame`), avoiding their MonoBehaviour/Device scene framework. If it won't compile on 6.3 → **B2 Python sidecar**.
  - **Coordinate note:** DepthAI spatial coords are camera-space (X right, Y down, Z forward/away); convert to Unity via a `PoseSpaceConverter` (Y-up, mirror) + hip-centre, same pattern as MediaPipe/Sentis.
  - **Runtime needs the OAK-D plugged in** — the import/compile check does not, but nothing past it can be validated device-less.
  - **✅ B1 COMPILE-FEASIBILITY PASSED (2026-08-06, Unity 6.3).** Imported the minimal Windows subset (81 MB: 5 native DLLs incl. 63 MB `opencv_world454.dll` + 12 MB `depthai-core.dll`, the `Scripts` framework + Netly + the MoveNet blob) to `Assets/Plugins/OAKForUnity/` + `Assets/Plugins/Netly/`. **0 errors / 0 warnings.** `OAKForUnity.DaiBodyPose` + `OAKForUnity.OAKDevice` load in **`Assembly-CSharp-firstpass`** (Plugins default assembly); `depthai-unity.dll` imported. Native DLL runtime-load + device detection NOT yet tested (needs a play run with the OAK-D).
  - **⚠️ asmdef constraint for the provider:** the plugin has no asmdef → its classes live in `Assembly-CSharp-firstpass`, which an asmdef (our `VirtualMirror.Tracking`) CANNOT reference. So `OakDPoseProvider` (in Tracking, so `AppBootstrap`/App can construct it) must NOT use the plugin's C# classes — it must call the **native `depthai-unity.dll` directly via its own `[DllImport]`** (P/Invoke works from any assembly) with **ABI-matched `PipelineConfig`/`FrameInfo` structs + enums copied verbatim from the plugin source** (`OAKForUnity/Scripts/**` — `OAKDevice.cs`/structs), and parse the JSON result (Newtonsoft, already in the project). Alternative: add an `OAKForUnity.asmdef` to the plugin + reference it from Tracking (reuses their structs, but must satisfy Netly/UI refs). DllImport path chosen for isolation. This is the next build step; runtime validation is with the user + camera.
  - **⚠️ Footprint: the full `OAKForUnity` folder is 665 MB** (all-OS native libs + every model blob + example scenes/3D-models/textures). Do NOT copy it wholesale into the repo. **Minimal Windows subset for the compile gate:** `Scripts/` (framework: `OAKDevice`, `Predefined/BodyPose/*`, base classes, `Extra/SimpleJSON.cs`) + `NativePlugin/Windows/` (the 5 DLLs; `depthai-core.dll` + `opencv_world454.dll` are the big ones) + `Models/movenet_singlepose_lightning_3.blob` (8.8 MB, runtime only). **Skip:** `UnityBridge/` (pulls the Netly dep — only for the python bridge, not needed for B1), Example Scenes, Playground, 3D Models, Textures, Materials, non-Windows `NativePlugin/{Linux,macOS}`, and the other model blobs. Note: the plugin has **no asmdef** → its scripts compile into `Assembly-CSharp` (watch for name clashes / Unity-6.3 API breaks; the compile gate will surface these). Clone available at `D:/du/depthai-unity` this session (ephemeral).
- **Context:** After the Sentis RTMW3D 3D path landed (arms/elbows/waist-bend all working), the last significant defect is **"hands sometimes go behind the body,"** which is the classic **monocular front/back depth ambiguity** — a single RGB camera cannot reliably tell whether a limb is in front of or behind the torso, so the model's z occasionally flips (toggling `poseFlipZ` doesn't fix it because it's not a constant sign error). It also forces hand-tuned `sentisDepthScale`/`sentisMetreScale` because monocular z has no true metric scale.
- **Decision (proposed):** Add a **Luxonis OAK-D** stereo-depth provider behind `IBodyTrackingProvider` so depth is **measured, not inferred**. This resolves front/back at the source, gives true metric scale, and stabilizes Z.
  - **Option A (recommended first, incremental):** keep the existing 2D keypoints (MediaPipe or RTMW3D x/y) and **sample OAK-D's depth-aligned frame at each keypoint pixel to get the real Z**, replacing the model's guessed z. Smallest change; reuses the current retarget/IK. New `OakDDepthProvider`/depth-fusion in the Tracking asmdef.
  - **Option B (cleaner, larger):** run pose **on-device** on the OAK-D Myriad X VPU with DepthAI `spatial` detection → metric 3D landmarks straight from the camera, offloading the host.
- **Cost / dependency:** DepthAI SDK + a Unity integration (Luxonis **DepthAI-Unity** plugin or a native bridge) — a native dependency comparable to the homuler MediaPipe plugin (ADR-010); Windows + USB. Consistent with ADR-004 (Windows-only) and ADR-007. Isolation per SDS-009 §11: only the OAK-D provider references DepthAI types; `PoseFrame`/retarget/IK/avatar/UI unchanged.
- **Caveats:** depth min-range (a few cm) + edge noise / occlusion holes; person must be within depth range; finger detail still needs close framing (depth fixes wrist/arm front-back + scale, not per-finger). Adds a hardware requirement (but the RGB/Sentis path remains as a no-hardware fallback via the provider interface).
- **Why it's the right fix:** monocular front/back ambiguity is a hard limit of one camera — tuning only reduces it. Measured depth removes it. The architecture was built for this swap (ADR-003/007), so it's additive, not a rewrite.
- **Files (when built):** new `Runtime/Tracking/OakD/OakDDepthProvider.cs` (+ Tracking asmdef ref to DepthAI), `AppBootstrap` wiring behind a `useOakDTracking` flag with fallback to Sentis/MediaPipe.

---

## ADR-017 — Retarget: direct rotational (FK) mapping as the default, over IK reconstruction (Accepted 2026-08-07)

- **Status:** Accepted. Applies to the OAK-D path; toggleable for all providers via `AppBootstrap.useIkDriver` / the "IK Arm/Leg Driver" panel checkbox.
- **Context:** The IK-reconstruction path (`AnimationRiggingIkDriver`) accumulated many heuristic gates across ADR-011/012/013/014 — per-joint confidence gates, a leg-drop plausibility gate, neutral calibration, and limb scale-normalization. On the **live** OAK feed these fought the data: arms dropped out (weight→0) when the user was small in the wide FOV, legs folded from hallucinated occluded landmarks, and the overall result was worse than free webcam VTuber tools (VSeeFace / Warudo / WebcamMotionCapture) that the user rightly compared against. Those tools use a **direct landmark→bone-rotation mapping** (Kalidokit) plus smoothing — simpler and more robust. Crucially, the camera was never the bottleneck: the OAK gives clean landmarks **with real depth**; the retarget was.
- **Decision:** Default the OAK path to the **already-present rotational FK retarget** — `HumanoidPoseRetargeter.BuildDefinitions` drives all four limbs via `RotationFromVectors` — i.e. `useIkDriver=false`. Keep the IK driver intact behind the toggle for comparison / other providers.
- **Supporting changes (2026-08-07):**
  - **Hips upright-only** — `ApplyTorso` projects the hip line to horizontal for the Hips basis → yaw/facing only, cannot roll → no whole-body tilt, and uprightness no longer depends on calibration.
  - **Legs gated by visibility** — `SetTrackLegs(bool)` (retargeter) + `SetLegTracking(bool)` (IK driver, `IIkSolver`): rotational when visible, pinned to bind (straight) when occluded.
  - **Front-facing auto-calibration** — neutral captured only when the shoulder line is X-dominant (facing camera) → no C keypress (production has no keyboard).
  - **Live mirror** — `PoseSpaceConverter` signs mutable (`SetFlipX/Y/Z`); panel checkbox flips left/right instantly.
  - **Position tracking** — avatar root driven from the OAK measured hip `xyz` (`PoseFrame.RootPositionMetres`, `ToUnityPosition`).
- **Consequences:** Robust upper-body tracking without gate dropout; IK reach / scale-normalization no longer used by default (acceptable — rotational mirrors the reference tools). Open follow-ups: derive wrist + neck rotations from landmarks (currently head rests forward, wrist/fingers need a webcam); add position `xyz` outlier rejection; Phase-2 measured per-keypoint depth (doc 26) to fix front/back at the source. **User-confirmed 2026-08-07: legs OK on this path; broader accuracy still being tuned live.**

---

## ADR-018 — Whole-body GPU model (RTMW3D) fused with OAK-D measured depth, in the Python sidecar (Accepted 2026-08-07)

- **Status:** Accepted. Stage B + C **built and hardware-verified 2026-08-07**. Supersedes the OAK Phase-1 BlazePose stream (ADR-016) as the primary OAK path; Phase-1 kept as a fallback.
- **Context:** User wants (a) proper wrist/finger tracking (BlazePose-33 gives no fingers) and (b) exact depth so limbs stop going behind the body. The two most capable pieces already existed but on separate paths: RTMW3D whole-body (ADR-015, 133 kpts incl. hands, GPU) and the OAK-D depth camera (ADR-016). ADR-018 fuses them.
- **Decision:** Run the **RTMW3D-x whole-body model on the host GPU inside the Python sidecar** (ONNX Runtime **DirectML** on the RTX 3060), on the OAK's RGB, and **fuse measured OAK stereo depth per keypoint** (host-side back-projection through the RGB intrinsics, doc 26 Option A). Stream body (33 JointId) + both hands (21 each) as hip-relative metres + the measured mid-hip root over the existing UDP contract (extended with `lh`/`rh`/`src`). **Face expression stays on MediaPipe** (blendshapes; RTMW3D face points are not blendshapes). Model host = sidecar (not Unity Sentis) so the OAK RGB + aligned depth stay in one process and no OAK RGB needs piping into Unity.
- **Why sidecar over Unity Sentis:** the OAK RGB + aligned depth are already in the sidecar; running the model there avoids piping RGB into Unity (a known blocker) and keeps GPU/native code out of the editor. The Sentis RTMW3D path (ADR-015) remains for the no-OAK RGB case.
- **Verified (live, this machine):** DirectML GPU active; I/O per ADR-015; ~40 ms/inference; end-to-end `wholebody_udp_sender.py` at **~30 fps**, 551 valid datagrams / 0 parse errors against a Unity-parser stand-in; back-projected XYZ anatomically correct (X L/R, Y up/down, Z forward with a reached-forward hand measured ~0.4 m closer); ~100 % depth coverage at range. Caveat: at ~3.5 m depth quantizes coarsely (~0.7 m steps) → guide users to ~2 m.
- **Files:** submodule `python-sidecar~/` — `rtmw3d_pose.py` (inference + SimCC decode + WholeBody→JointId maps), `oak_depth.py` (RGB-D pipeline + per-keypoint back-projection), `wholebody_udp_sender.py` (primary sender), `validate_rtmw3d.py` / `validate_depth.py` (HITL checks). Model at `Assets/SentisModel/rtmw3d-x.onnx` (git-ignored, 369 MB).
- **Unity finger consumption — CODE DONE 2026-08-07 (pending in-editor verify; MCP not bound this session):** one datagram carries body + hands, so `OakDUdpPoseProvider` now also parses `lh`/`rh` into a double-buffered `HandFrame` (per-finger curl via the same angle/160° math as `MediaPipeHandProvider`; palm rotation through the shared `PoseSpaceConverter`) exposed via `TryGetHandFrame`. New `OakDUdpHandProvider : IHandTrackingProvider` facade forwards it (no own socket). `AppBootstrap` uses the OAK hand facade whenever `bodyProvider is OakDUdpPoseProvider` (so fingers work without a MediaPipe RGB webcam); `HumanoidHandRetargeter` drives the finger bones + wrist as before. Files: `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs`, `OakDUdpHandProvider.cs`, `Bootstrap/AppBootstrap.cs`. **Verify in-editor:** `useOakUdpTracking` + `useHandTracking` on, run `wholebody_udp_sender.py`, confirm fingers curl. Notes: depth-hole hands fall back to 2D curls (coplanar z=0) — fine for open/close.
- **Post-test fixes 2026-08-07 (first live OAK whole-body run):** (1) **wrist rotation DISABLED** on the OAK path — the palm basis from RTMW3D hand landmarks at a distance was too noisy and spun the avatar wrist continuously; `OakDUdpPoseProvider.ParseHands` now passes `tracked=false` to `SetWristRotations` (fingers still curl; re-enable when hands are close/stable). (2) **Torso forward-hunch FIXED** via sender `--flatten-trunk` (default on) — RTMW3D+stereo measures the upper body ~0.5-0.7 m farther than the hips, tipping the near-vertical spine; zeroing the Z of the 4 trunk joints keeps the trunk vertical while limbs/hands keep measured depth. (3) **Exact mirror** via sender `--mirror` (default on) with Unity `poseFlipX=0`.
- **Second live run (2026-08-07): trunk-flatten confirmed (avatar upright), but two new issues → fixed sender-side:** (a) **Jitter** (shakes even when still) — RTMW3D+stereo is noisy and Unity's light One-Euro (mincutoff 1, beta 0.2) passes the coarse depth spikes. Added source-side smoothing `smoothing.py` (per-keypoint One-Euro + a jump gate); flags `--smooth`/`--min-cutoff`/`--beta`/`--max-jump`. **⚠️ First attempt (hold-on-outlier, max_jump 0.5) FROZE limbs** — at 2.5-3.5 m the depth-quantization step (~0.7 m) exceeds 0.5 m, so keypoints were flagged outliers every frame and held their first value forever → contorted/frozen poses ("completely ruined"). **Fixed:** the gate now **rate-limits** the step (always keeps moving, never freezes) and defaults to a lenient `max_jump=1.5` (above the quantization step) so only gross garbage spikes are caught; normal motion + quantization pass straight to One-Euro. Bench: still-jitter −8×, realistic motion tracks with modest lag, a 6 m spike damped to ~3.3 m. (b) **Mirror inverted the arms** (crossing → opening) — negate-X ALONE crosses limbs on a rotational retarget with anatomical bone mapping; the true reflection must **negate X AND swap left↔right** (`MIRROR_PAIRS`, and swap `lh`/`rh`). `--mirror` did both — but a THIRD live run showed the negate-X **reflection twists the torso/limbs**: the FK retarget builds bone orientations with `LookRotation`, and a reflected (improper) skeleton flips the roll/up reference → waist twist + limb twist. **Conclusion: do NOT mirror the input skeleton for a rotation-based retarget.** `--mirror` now defaults **OFF** (clean "copy" retarget, no twist). A true mirror must be done on the Unity/avatar side (e.g., render/scale flip) — TODO, not via input reflection. Sequence of mirror attempts for the record: poseFlipX-only (copy, "not a mirror") → negate-X (arms cross) → negate-X+swap (torso twist) → reverted to copy.
- **Open follow-ups:** MediaPipe face (needs a working RGB webcam); live axis/mirror + depth-scale tuning; xyz outlier rejection; consider RTMW3D-L for more FPS headroom.

---

## ADR-019 — Torso yaw is driven by the HIP line, not the shoulder line (waist-twist fix, Accepted 2026-08-07)

- **Status:** Accepted (2026-08-07). Applies to all providers (FK torso path). **Refines ADR-017's "hips upright-only"** by extending the same principle to the Spine's yaw.
- **Context:** A user test video (OAK-D whole-body path, 2026-08-07) showed a persistent **waist twist**: standing straight and facing the camera, the avatar's chest/shoulders were yaw-rotated 30–45° relative to its hips/legs, and the twist **changed as the arms moved** (worst with an arm raised). Frame-by-frame comparison of the sidecar preview (real pose, upright) vs the avatar confirmed it was a retarget artifact, not the user's pose. This is a DISTINCT cause from the forward-hunch (fixed by sidecar `--flatten-trunk`, ADR-018) and the rigid whole-body tilt (fixed by Hips `StableUp`, ADR-017): those were pitch/roll; this is **yaw**.
- **Root cause:** `HumanoidPoseRetargeter` built the **Spine** basis from the **shoulder line** (`LeftShoulder→RightShoulder`) for its "right"/yaw. When an arm is raised, the shoulder keypoints shift, the shoulder line rotates, and the Spine yaws relative to the (hip-derived) Hips → visible twist. The Hips already yawed stably from the horizontally-projected hip line.
- **Decision:** Drive **both** torso bones' yaw from the **horizontally-projected HIP line**. Hips keep the vertical up (facing/yaw only, upright); the Spine keeps the live torso-up (`midShoulder−midHip`) so it still carries forward/side **bend**, but its **yaw now comes from the hips**, so it can no longer twist relative to them. Implemented via a `BasisBone.ProjectRightHorizontal` flag; the Spine's right source changed from the shoulder line to the hip line.
- **Trade-off (accepted):** real **chest-vs-pelvis axial twist** is no longer reproduced. It is unreliable from monocular/coarse-depth tracking and is exactly what produced the false twist — the same philosophy as Hips-`StableUp` (ADR-017) and neck-undriven (ADR-012). For a mirror, upright-no-false-twist > axial-twist fidelity.
- **DO NOT REVERT to a shoulder-line spine basis** to "recover chest twist" — that reintroduces the arm-raise waist twist. If chest twist is ever wanted, derive it from a dedicated, smoothed chest signal, not the raw shoulder line.
- **Files:** `Runtime/Retargeting/HumanoidPoseRetargeter.cs` (`BasisBone.ProjectRightHorizontal`, `BindBasisBones`, `ApplyTorso`).
- **Verification:** compiles clean (0/0) + EditMode 21/21. **Live test 2026-08-07:** normal whole-body rotation no longer twists (fix confirmed by the user); a real forward/side lean should still bend the spine.
- **KNOWN LIMITATION — back-facing:** when the user turns to face AWAY from the camera (~180°), the avatar can still twist. This is the inherent **single-camera front/back ambiguity** — RTMW3D cannot reliably resolve a person facing away (left/right keypoints and hip-line facing become ambiguous / flip), and the front-facing auto-calibration gate (ADR-017) can't detect it because the sidecar `--flatten-trunk` zeros the depth the gate would need. This is **out of scope for a front-facing mirror** and is NOT worth chasing on one camera; a real fix needs multi-view or the measured-depth Phase-2 (doc 26) to disambiguate facing. Do not add speculative back-facing heuristics.

---

## ADR-020 — Smoothing is single-owned by the sidecar on the OAK path; do not re-add a Unity stage (Accepted 2026-08-07)

- **Status:** Accepted (2026-08-07). Confirms + tunes the audit's M3/M18 single-owner decision.
- **Context:** After the audit made the OAK path **bypass Unity's `JointFilterPipeline`** (`UpdateTracking`: `bodyProvider is OakDUdpPoseProvider ? frame : jointFilter.Filter(...)`) to remove the double-smoothing lag, a user test showed jitter had returned ("smoothing is gone"). Before the audit, the OAK path went through Unity's filter *and* the sidecar's, so the residual sidecar jitter was masked by the second (laggy) stage.
- **Decision:** **Keep smoothing single-owned by the sidecar** for OAK — do **NOT** re-introduce a Unity smoothing stage for the OAK path (that recreates the double-filter lag the audit removed and the two-owner problem that caused the recurring bugs). Instead make the single owner adequate: the sidecar's One-Euro **default `--min-cutoff` is lowered 1.0 → 0.5** (`wholebody_udp_sender.py`) so the OAK stream is smooth out of the box. `--beta` unchanged (0.02) keeps fast motion responsive; the `smoothing.py` rate-limit gate (ADR-018, `--max-jump 1.5`) still catches gross depth spikes.
- **Tuning (no recompile — sidecar flags):** still jittery at range → `--min-cutoff 0.3`; feels laggy → raise toward `1.0`. **Also stand ~2 m** from the camera: OAK stereo depth quantizes coarsely past ~2 m, which is the dominant jitter source at distance (the user in the test stood ~2.4–3.0 m).
- **DO NOT** "fix jitter" by adding `jointFilter.Filter` back for OAK — tune the sidecar One-Euro instead.
- **AMENDMENT (2026-08-07, after the first live test) — REACTION SPEED:** min-cutoff 0.5 alone felt **too slow/laggy**. The two One-Euro knobs do different jobs: **`--min-cutoff` = stillness** (how steady when you hold still), **`--beta` = reaction speed** (how fast it opens up during motion). The old `--beta 0.02` was far too low for metric-scale keypoints → sluggish. New defaults: **`--min-cutoff 0.7`, `--beta 0.4`** (steady when still, snappy on motion). Both are live-tunable per sidecar run (rerun in seconds, no Unity recompile): laggy → raise `--beta`; fast moves jittery → lower `--beta`; jittery when still → lower `--min-cutoff`.
- **Files:** `python-sidecar~/wholebody_udp_sender.py` (`--min-cutoff`, `--beta` defaults), `smoothing.py`.
- **AMENDMENT (2026-08-10) — per-limb DEPTH smoothing + hold-on-dropout (still single-owner in the sidecar).** Pipeline logs proved the residual jitter is **depth (z) on the limbs**: the wrist's z jitters ~5× its image-plane x,y (measured: |dz|=0.06 vs |dx|=0.015 m/frame) — invisible in the 2D Python preview but applied in Unity (this is *why* "stable in Python, unstable in Unity"). Extended `smoothing.KeypointSmoother`: (1) **limb depth (z) gets a heavier One-Euro** (`--depth-min-cutoff 0.3`, `--depth-beta 0.1`) than x/y — arms (WholeBody 7-10) + both hands (91-132) only; (2) **hold-on-dropout** — a limb keypoint with no measured depth holds its last-good smoothed value for up to `--max-hold-frames 8`, reported as measured so `build` uses it instead of the noisy zrel fallback (the source of the 8-12 m spikes), **bounded so a genuinely-gone limb still drops (never a freeze — ADR-018 rule)**. Legs are excluded (must drop when occluded). **Unit-tested:** 0.12 m/frame z-jitter → 0.006; a 12-frame dropout holds exactly 8 then releases; non-limbs still reset. **Tune per run** (no recompile): depth still jittery → lower `--depth-min-cutoff`; reaching toward/away feels laggy → raise `--depth-beta`; spikes persist through longer dropouts → raise `--max-hold-frames`. Files: `smoothing.py`, `wholebody_udp_sender.py`. **NOTE:** this is a sidecar improvement — the real depth fix is still **~2 m framing + a close-range hand source** (ADR-024); the wrist stays off (`wristRotationWeight=0`) for the POC.

---

## ADR-021 — Wrist rotation RE-ENABLED on the OAK path with temporal smoothing + live weight (Accepted 2026-08-07, supersedes ADR-018's wrist disable)

- **Status:** Accepted (2026-08-07). **Supersedes** the ADR-018 post-test decision that disabled wrist rotation on the OAK path.
- **Context:** ADR-013 added wrist/palm rotation; ADR-018 then **disabled it on the OAK path** because the raw palm basis from RTMW3D hand landmarks at a distance was noisy and **spun the avatar wrist continuously** — so the avatar wrist only followed the forearm direction ("directional only"), which the user reported as "wrist movement not happening." The blanket disable also meant every session that wanted wrists re-litigated it.
- **Decision:** Re-enable wrist rotation on the OAK path, but make it **stable and self-serviceable** so it doesn't churn:
  1. **Temporal smoothing** — `OakDUdpPoseProvider.ParseHands` slerp-smooths the palm quaternion per hand (`WristSmoothing = 0.35`) before streaming, so the wrist is responsive without the spin. (The underlying hand *points* are already One-Euro-smoothed in the sidecar; this damps the derived *quaternion*.)
  2. **Live-tunable weight** — `AppBootstrap.UpdateTracking` pushes `wristRotationWeight` to the retargeter every frame, so the serialized value is adjustable **during Play** (0 disables; fingers still curl). This breaks the stop→edit→Play recompile loop that made the wrist a recurring task.
  3. Applied **delta-from-neutral** as before (ADR-013), so no per-avatar axis calibration.
- **Expectation / fallback:** works best with **hands close / ~2 m framing** (small distant hands are inherently noisy). If it still misbehaves at distance, set `wristRotationWeight = 0` live — no code change, no re-disable in source. **DO NOT** hard-disable it in code again; tune the weight/`WristSmoothing` instead.
- **Files:** `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs` (smoothing + `SetWristRotations(..., lTracked, ..., rTracked)`), `Runtime/Bootstrap/AppBootstrap.cs` (live `SetWristWeight`).
- **AMENDMENT (2026-08-07, after the first live test) — ROLL-FREE wrist:** the re-enable above (full-orientation delta + slerp smoothing) **still spun the wrist like a helicopter** in the user's live test. Root cause: the palm ROLL (pronation) axis is derived from the noisy index/pinky span and drifts continuously; a full-orientation delta feeds that drift into a continuous roll, and slerp-smoothing a *spinning* target only lags the spin, it doesn't stop it. **Fix:** `HumanoidHandRetargeter.ApplyWrist` now drives a **roll-free swing** — `Quaternion.FromToRotation(neutralForward, currentForward)` using ONLY the palm's forward axis (wrist→middle-MCP direction). `FromToRotation` has no roll degree of freedom, so the wrist bends toward where the hand points but is **structurally incapable of spinning**. The provider-side quaternion smoothing + live `wristRotationWeight` (default 0.7, 0 disables) are retained. Trade-off: pronation/supination (palm-up↔palm-down roll) is not reproduced — it is the exact unreliable axis, same rationale as ADR-019 dropping chest axial twist. **DO NOT** restore a full-orientation wrist delta (it re-introduces the helicopter); if pronation is ever wanted, derive it from a dedicated stabilized roll signal, not the palm span. Re-verified: compile 0/0 + EditMode 21/21.
- **PIPELINE-LOG DIAGNOSIS + wrist-jitter fix (2026-08-10).** Built 3-stage frame-aligned logging (sender/recv/model, `seq`-aligned; `compare_logs.py`) to answer "stable in the Python window, unstable in Unity." A real OAK capture (1986 send / 1760 recv / 9549 model frames) localized it: **body + facing are STABLE** — shoulders 0.7 cm/frame, hips 0.25 cm/frame, **hips-yaw mean 0.17°/frame, p95 0°** (the frontal-lock flatten worked). **The wrist jitter/"spin" originates at the SOURCE:** the hand landmarks/depth are noisy — send R-wrist 5.7 cm/frame (8.6 m spike), recv palm quaternion **~10°/frame with spikes to 150°** → passed through to the avatar hand (~11.6°/frame). The **forearm is stable (1.7°/frame)** → NOT a helicopter; the roll-free wrist holds. So Unity faithfully renders noisy hand data (the 2D Python preview cannot show the depth noise). **Fix:** stronger palm smoothing in `OakDUdpPoseProvider` (`WristSmoothing` 0.35→0.15) + a per-frame rate-limit (`WristMaxStepDeg` 15°, **rate-limited not held** so it can never freeze — ADR-018 lesson) via `SmoothWristQuat`. The `recv_log` records the smoothed palm, so a re-capture quantifies it (expect palm jitter ~10°→~3°/frame). **Deeper source noise (sidecar follow-ups, not yet done):** the depth-hole fallback emits garbage hand/wrist points (the 8.6 m spike), and the person-box loses keypoints at the frame edge (obs 1 — rare shoulder 0.48 m / yaw 90° spikes). Both are sidecar robustness (hold-last-good on low confidence; require measured depth before emitting a hand). **DO NOT** add a Unity-side landmark smoothing stage for OAK (ADR-020 single-owner) — fix source noise in the sidecar. Files: `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs`; logging: `python-sidecar~/wholebody_udp_sender.py` + `compare_logs.py`, `AppBootstrap.cs`.
- **Verification:** **PLAY-verify (user):** hands ~2 m, bend/point the wrist → avatar wrist follows without spinning; if the bend is too strong/twitchy, lower `wristRotationWeight` live.

---

## ADR-022 — Kalidokit-ported arm/hand solver replaces vector-FK for limb roll (Accepted 2026-08-07)

- **Status:** Accepted (2026-08-07). Arms shipped behind a toggle; hand/spine/legs to follow. User chose "Port Kalidokit into Unity" after the vector-FK forearm spin persisted through ADR-019/021.
- **Context — the real 70% ceiling:** the custom retarget converts joint POSITIONS → bone ROTATIONS with `RotationFromVectors` (aim the bone along the joint direction, then reconstruct ROLL from a global body-forward reference). A single direction pins only 2 of a rotation's 3 axes; the 3rd (roll) is guessed, and when a limb points toward/away from the camera the direction aligns with the reference and the roll becomes undefined → the forearm "helicopters" (and it contributed to the twist). This is a math limitation of vector FK, **not** the engine or the tracking data (the OAK sidecar's skeleton is clean). Confirmed: a roll-free hand (ADR-021) didn't stop the spin because the spin is the FOREARM, upstream of the hand.
- **Decision — DO NOT leave Unity; replace the retarget math.** Switching engines would rebuild the identical landmark→rotation step and reproduce the bug; Unity + UniVRM is the right VRM stack. Instead, port **Kalidokit** (yeemachine/kalidokit, **MIT**) — the proven solver behind webcam VTuber apps — which derives each limb's rotation (incl. roll) from the limb GEOMETRY (elbow bend + shoulder/wrist plane via `angleBetween3DCoords` / `rollPitchYaw`), so roll is measured, not guessed. Our OAK gives real 3D, which makes this *more* robust than the webcam tools it comes from.
- **What was built (this session):** faithful C# port — `Runtime/Retargeting/Kalidokit/KMath.cs` (Vector math: findRotation, angleBetween3DCoords, rollPitchYaw, normalizers, clamp — verbatim formulas), `KalidokitArmSolver.cs` (`calcArms` + `rigArm`, verbatim scale/clamp constants). Application layer `Runtime/Retargeting/KalidokitRetargeter.cs` drives UpperArm/LowerArm/Hand as `bone.localRotation = rest * Remap(euler)` slerped. `HumanoidPoseRetargeter` gains `SetArmsExternallyDriven` + `IsArm` so FK yields ONLY the arms (torso + legs stay on FK). Wired in `AppBootstrap` behind **`useKalidokitArms`** (default OFF for A/B) with live-tunable `kalidokitAxisSigns` / `kalidokitLerp` / `kalidokitDriveHand`. **Compiles 0/0; EditMode 27/27** (6 new `KMathTests`).
- **The one thing that needs a live tune — the axis remap.** Kalidokit outputs Euler in the three-vrm normalized-bone convention (right-handed, Y-up); Unity bones are left-handed. `kalidokitAxisSigns` (default `(-1,-1,1)`) maps the Euler onto Unity bone-local degrees and is applied relative to the captured T-pose rest. This is the SAME class of convention tuning as `poseFlipX` — expect one live pass. **All knobs are Inspector-live during Play**, so tune without a recompile: tick `useKalidokitArms`, then adjust `kalidokitAxisSigns` (try the 8 sign combos of ±1) + `kalidokitLerp` until the arms track cleanly and the forearm no longer spins.
- **Input convention:** `KalidokitRetargeter` adapts our PoseFrame (Unity Y-up) to Kalidokit's (Y-down) by flipping Y before solving; `JointId` indices already equal MediaPipe pose indices, so `calcArms` maps 1:1.
- **License:** Kalidokit is MIT — ported (not bundled) with attribution; recorded in `ThirdPartyNotices.txt`.
- **Roadmap:** once the arm axis convention is dialled in live, extend the port to the **hand solver** (per-finger + wrist, `HandSolver`), then **spine/legs** (`calcHips`/`calcLegs`) to move the whole body onto the proven solver. Keep the vector-FK path behind the toggle as a fallback until Kalidokit is fully validated.
- **DO NOT** revert to vector-FK roll for limbs once Kalidokit is tuned — that reintroduces the helicopter. If Kalidokit arms look wrong on first run, it's the `kalidokitAxisSigns` convention (tune live), not the solver math (unit-tested).

- **AMENDMENT (2026-08-07) — WHOLE-BODY control-rig path (the recommended direction).** The arms-only raw-bone path above still needs per-bone axis tuning because each avatar's raw bone axes differ. The fix: apply the WHOLE Kalidokit pose to UniVRM's **normalized control rig** (`Vrm10Runtime.ControlRig`, verified via reflection to implement `INormalizedPoseApplicable` + `GetBoneTransform(HumanBodyBones)`), which exposes the SAME VRM1.0 normalized bones three-vrm uses — so Kalidokit's numbers apply with a SINGLE global three→Unity handedness conversion for every bone (no per-bone guessing). Ported the rest of Kalidokit: `calcHips` (rotation), `calcLegs`, spherical-coord + `rollPitchYaw2` helpers (`KMath`, `KalidokitPoseSolver`). New `KalidokitControlRigDriver` drives Hips/Spine/Chest/arms/hands/legs on the control rig. Loader gains `GenerateControlRig` (load-time; the FK/IK path keeps it OFF because a generated rig would force the T-pose over raw-bone writes). Wired behind **`useKalidokitBody`** (set BEFORE Play): when on, FK+IK+hand-curl are fully bypassed (control rig owns the skeleton), face blendshapes still run. **Compiles 0/0; EditMode 27/27.**
  - **Enable + tune (user):** set `useKalidokitBody = true` on AppBootstrap **before** pressing Play (it decides whether the avatar loads with the control rig), Play. Then dial the ONE global convention: `kalidokitBodyFlipQuat` (0..3) + `kalidokitBodyEulerSigns` (Inspector-live) until the whole body orients correctly; `kalidokitBodyLerp` = smoothing. Because it's one convention for all bones, tuning is a few tries, not per-bone.
  - **✅ PROCESS ORDER FIXED + VERIFIED ON VIDEO (2026-08-07).** First live test = avatar stuck in T-pose: UniVRM's auto `Process()` ran BEFORE our LateUpdate control-rig writes and applied the rest pose. Fix: `KalidokitControlRigDriver.Bind` sets `vrm.UpdateType = UpdateTypes.None` (manual) and `AppBootstrap` calls `ProcessRuntime()` (→ `vrm.Runtime.Process()`) ONCE at the end of `UpdateTracking`, AFTER body pose + face expressions are written. **Headless-verified on the VIDEO source** (OAK not connected): `useKalidokitBody=true`+`useVideoSource=true`+MediaPipe → `ControlRig=True`, `UpdateType=None`, control-rig bones NON-identity and CHANGING frame-to-frame (tracking the video), 0 exceptions, no T-pose. End-to-end logic proven (MediaPipe → Kalidokit port → normalized control rig → skeleton).
  - **✅ CONVENTION SOLVED + DATA-VERIFIED (2026-08-07): `kalidokitBodyFlipQuat=2`, `kalidokitBodyEulerSigns=(1,1,1)`.** User feedback: flipQuat 0 = arms inside-out, 2 = correct orientation but mirrored. Verified headlessly by data (not screenshots — the Game view freezes while Unity is unfocused, so live screenshots are unreliable): stepped the sim on the video and compared, in the same frame, the tracked person's raised arm vs the avatar's raised arm. Hand/head world-Y are anatomically sane at flipQuat=2 (no inside-out). Added a `kalidokitBodyMirror` toggle (negate input X + swap L/R = a true reflection); **verified it flips cleanly**: person-RIGHT-arm-up → mirror=true gives avatar-RIGHT-up (COPY, avatar does your same anatomical side), mirror=false gives avatar-LEFT-up (true mirror reflection, same visual side as you). Set **`kalidokitBodyMirror=true`** (copy / un-mirrored) per the user's "fix the mirroring". **For a real-mirror reflection instead, set `kalidokitBodyMirror=false`** — one flag, documented. 0 exceptions across the run.
  - **Scene left on the VIDEO test config** (`useVideoSource=true`, MediaPipe, OAK/Sentis off) so it runs without the OAK. For OAK later: `useVideoSource=false`, `useOakUdpTracking=true` (keep `useKalidokitBody=true`, `flipQuat=2`).
  - **⚠️ MCP Play caveat reconfirmed:** driving Play over MCP froze/crashed Unity (a MediaPipe Glog "InitGoogleLogging twice" abort from repeated play/stop/recompile cycles). Verify via a SINGLE clean Play + data reads (or a non-MediaPipe provider), not repeated Play cycling. Live screenshots are unreliable headless (Game view freezes unfocused) — use data reads (`Step()` + read bones/landmarks).
  - **✅ RIGHTWARD-LEAN FIXED + DATA-VERIFIED (2026-08-07).** User: "the model is leaned right all the time." Root cause (found by reading control-rig euler on the video): the spine euler was applied to BOTH the Spine AND Chest bones, DOUBLING the torso rotation (Spine=Chest=(0,22.7,349.6) → ~45° yaw + ~20° roll). Fix: (1) DISTRIBUTE the spine rotation across Spine + Chest (0.5 each) instead of full-to-both; (2) add `kalidokitBodyTorsoRoll` (default 0) that scales the roll (z) of hips + spine — 0 = upright (no lean, matching the ADR-019 no-unreliable-roll precedent), raise toward 1 for side-lean tracking. Verified after fix: Hips/Spine/Chest roll = 0 (no lean), Spine=Chest=(0,6.1,0) distributed (not doubled). `KalidokitControlRigDriver`.
  - **✅ FINGERS on the control rig (curl-based) + PLUMBING VERIFIED (2026-08-07).** Chose curl-driven fingers (reuse the HandFrame per-finger curls from any hand provider) over the full 21-landmark Kalidokit HandSolver, because the latter needs a close hand source to be worthwhile and the dance video's hands are too small (MediaPipe Hand returns no valid frame on it). `KalidokitControlRigDriver` binds the 30 normalized finger bones and rotates them by the curl about a single tunable `kalidokitFingerCurlAxis` (default (0,0,-1)); driven in `AppBootstrap` on the Kalidokit body path before `ProcessRuntime`. **Verified end-to-end** by injecting a synthetic 0.8 curl: control-rig LeftIndexProximal (0,0,0)→(0,0,-20°) AND the raw skeleton bone followed → reaches the mesh. **⏳ needs a close hand source (webcam close-up / OAK) for real finger data**, and possibly a `kalidokitFingerCurlAxis` tune for anatomical flexion direction. Full per-joint Kalidokit HandSolver (21 landmarks → HandFrame surgery) remains a future refinement.
  - **Fingers:** not yet driven on the control rig (they rest open during the body check) — porting `HandSolver` onto the control-rig fingers is the next step once the body convention is confirmed.
  - **Scene note:** `Bootstrap.unity` saved to the video test config (`useKalidokitBody=true`, `useVideoSource=true`, `useOakUdpTracking=false`, `useSentis3dTracking=false`, MediaPipe on) — revert `useVideoSource`/`useOakUdpTracking` for the OAK path later.

---

## ADR-023 — Kalidokit body path: the mapping is sound; the twist was the input-skeleton mirror, not the convention (2026-08-10)

- **Status:** In progress (2026-08-10). **Amends the mirror + "convention solved" claims of ADR-022.**
- **Context:** the user's 2026-08-09 recording (video test config: `useKalidokitBody=1`, MediaPipe pose, sample dance video) showed a persistent forearm "helicopter" + arms not tracking — even though ADR-022 marked the convention "SOLVED + DATA-VERIFIED."
- **Finding 1 — the prior verification was a false pass.** ADR-022 "data-verified" only that control-rig bones were non-identity and changed frame-to-frame — never that the avatar *reproduced* the input pose. New reliable method (2026-08-10): inject canonical poses via reflection on the live `KalidokitControlRigDriver.Apply` and read RAW-skeleton world positions — reliable because `execute_code` is synchronous (reads back before the next frame). **Screenshots are UNRELIABLE for injected poses:** the Awaitable update loop (ADR-006) re-applies the live pose on render even with `component.enabled=false`.
- **Finding 2 — the euler→normalized-bone mapping is SOUND.** Clean canonical input tracks correctly (T-pose clean; arm UP→above head, FORWARD→+Z, SIDE→out) at `flipQuat=2` / `eulerSigns(1,1,1)`. **DO NOT keep hunting flipQuat/sign combos — the mapping is not the bug.**
- **Decision — the twist is `kalidokitBodyMirror`'s input-skeleton reflection.** `Apply` negates X + swaps L/R on the landmarks BEFORE the Kalidokit solve; on an asymmetric pose that yields **~36° spurious forearm roll + 0.5 m hand error** vs a clean reflection (measured: mirror-ON vs the clean reflection of mirror-OFF). This is precisely ADR-018's rule ("do NOT mirror the input skeleton for a rotation-based retarget" — cross-product/plane-normal roll references flip on an improper skeleton). **Fix: `kalidokitBodyMirror=false` (persisted in `Bootstrap.unity`) — no input reflection.** **DO NOT re-enable the input-reflecting mirror.** A true visual L/R flip, if wanted, must be done OUTPUT-side (swap the target bones + sagittal-reflect each rotation), NOT by reflecting input landmarks — pending refactor.
- **Finding 3 — residual poor accuracy on that recording is the mono-video INPUT ceiling (ADR-014), not the retarget.** Distant dancer → weak/noisy MediaPipe Z. Clean-input tests prove the retarget tracks when given good directions. Real accuracy win = OAK-D depth / closer framing.
- **Audit outcome (2026-08-10):** a 9-agent adversarial port audit (KMath / ArmSolver / PoseSolver / Driver vs the original Kalidokit JS, each finding then adversarially verified) found the **arm / hips / legs / KMath port FAITHFUL — no solver bugs** (the KMath `NormalizeRadians` "suspicious" branch and the rigArm/rigLeg constants all match the original). This corroborates Finding 2: **the helicopter is not solver code.** It confirmed ONE real bug, in the FINGER driver: **A1 — finger curl used one shared axis+sign for both hands** (the normalized L/R finger bones are parallel, not mirrored) → one hand curls into the palm, the other hyperextends backward. **FIXED:** `ApplyHandFingers` now takes a per-hand `sideSign` (left −1, right +1), matching Kalidokit's `invert = side==Right?1:-1`; the global `kalidokitFingerCurlAxis` still tunes both hands together (flip it if both hyperextend). A2 (the thumb sharing the four-finger flexion axis) is noted as a low-priority, rig-dependent limitation — needs a close hand source to tune, deferred.
- **Follow-ups:** (a) if a "copy same-side" mode is ever wanted, reimplement `mirrorX` as an OUTPUT-side bone swap (the input-reflection form is deprecated — do not re-enable it); (b) A2 thumb axis; (c) final Play visual verify on a REAL input (OAK-D / close webcam) — the mono dance video is input-limited (ADR-014) and cannot judge the retarget.
- **Files:** `Scenes/Bootstrap.unity` (`kalidokitBodyMirror` 1→0); `Runtime/Retargeting/KalidokitControlRigDriver.cs` (A1 per-hand finger `sideSign`).
- **AMENDMENT (2026-08-10) — LIVE OAK-D run diagnosed + 2 more fixes.** On the OAK-D the body tracks (T-pose reproduced, depth ~2.4 m — confirming input was the mono-video ceiling), but the user reported: hands curve even with straight arms + wrist doesn't track; forearms bend forward; "body rotation gone." Diagnosed live (data reads + a debug line-skeleton of the raw landmarks, per the user's idea — the raw skeleton itself showed forward-bent arms, so it is NOT purely the VRM):
  - **Hand curve = Kalidokit hand-euler fed ZERO body-hand points.** The OAK whole-body stream leaves the body-pose hand points (JointId 17-22) at exactly `(0,0,0)`, so `FindRotation(wrist, origin)` produced a garbage FIXED rotation on the Hand bone (the persistent curve that never tracks). **FIX (verified):** `KalidokitControlRigDriver.Apply` now gates each Hand bone on valid hand points — drives it to REST (identity) when 17-22 are zero. Geometry-verified: zero-pts → Hand.local=(0,0,0); populated-pts → driven (±14°). Hands rest straight instead of curling. **This stops the garbage; it does NOT make the wrist TRACK** — real wrist bend needs the separate 21-pt OAK hand stream (`TryGetHandFrame`) wired onto the control-rig path (stabilized/roll-free per ADR-021). Open follow-up.
  - **Forearm forward-bend + "body rotation gone" = sidecar `--flatten-trunk`.** It zeroed ONLY the 4 trunk joints' Z (shoulders 11/12, hips 23/24), leaving elbows/wrists at measured Z (→ the upper arm gained ~0.5 m of spurious forward Z = the false bend) AND destroying shoulder/hip Z-separation (→ when the user turned, the lines collapsed → torso yaw lost). **FIX (py_compile clean):** `wholebody_udp_sender.py` now shifts the WHOLE upper body (JointId 0-22) back by the mid-shoulder Z instead of zeroing per-joint — the trunk stays vertical, every limb keeps its shape relative to its shoulder (no false bend), and Z-separation is preserved (yaw/rotation survives). **DO NOT go back to per-joint Z-zeroing.**
  - **Files:** `Runtime/Retargeting/KalidokitControlRigDriver.cs` (hand-point gate), `python-sidecar~/wholebody_udp_sender.py` (`build_body_landmarks` flatten). **Still open:** wrist TRACKING (bend) on the OAK path (wire the hand-stream wrist next).
- **AMENDMENT (2026-08-10, iter-2) — the "shift whole upper body" flatten REGRESSED facing into profile; corrected. The debug line-skeleton proved it's the retarget side.** A follow-up OAK run + the new debug skeleton (drawn beside the avatar, ADR-023 tooling) showed the **skeleton frontal and matching the user, but the avatar in ~90° PROFILE** — confirming (the user's own diagnosis) that tracking is good and the retarget yaw is wrong. **Root cause proven in Unity** by injecting a frontal pose into the live driver: hip-line Z=0 → avatar frontal (hips yaw 0°); hip-Z ±0.10 m → yaw 40°; ±0.25 m → yaw 64°. The Kalidokit hips/spine yaw is hypersensitive to hip-line Z, and the previous "shift the whole upper body (0-22) by mid-shoulder Z" flatten had left the hip Z **measured** → noisy OAK hip depth swung the avatar into profile. **Corrected flatten:** zero the shoulders' AND hips' Z (both lines horizontal → upright trunk + stable FRONTAL facing), but SHIFT each arm's joints by its shoulder's Z *before* zeroing the shoulder, which preserves the arm segment vectors exactly (keeps the no-false-bend fix). **DO NOT** leave the hip Z measured (re-introduces the profile swing) and **DO NOT** zero the shoulder without re-basing its arm (re-introduces the forward arm-bend). **Trade-off:** facing is frontal-locked (turning not tracked) — the single-front-camera limit (ADR-019 precedent); a stable frontal avatar >> a profile one. Supersedes the earlier "rotation restored when turning" note. py_compile clean.
- **WRIST BEND wired on the Kalidokit control-rig path (2026-08-10, closes the ADR-021 OAK gap).** On the control-rig path the Hand bone was only driven by Kalidokit's hand-euler (now gated to REST, since the OAK body-hand points 17-22 are zero). Now the real wrist bend is applied: `KalidokitControlRigDriver.ApplyWrist(HandFrame)` runs the ADR-021 **roll-free** swing (`FromToRotation` of the palm's forward axis vs a captured neutral — no roll DOF, structurally cannot helicopter) on the **RAW** Hand bones, called from `AppBootstrap` **AFTER** `ProcessRuntime` (a control-rig write would be overwritten by Process). Palm data comes from the OAK 21-pt hand stream (`OakDUdpPoseProvider.PalmRotation`, temporally smoothed) forwarded by the `OakDUdpHandProvider` facade. Live-weighted by `wristRotationWeight` (0 disables; fingers still curl); the **C key** re-captures the neutral palm (`Recalibrate`). **Verified in Unity:** neutral palm → hand at rest; a 35° palm tilt → the raw Hand rotates 24.4° (= 35° × weight 0.7), roll-free. Best with hands ~2 m; if twitchy at distance, lower `wristRotationWeight` live (do NOT restore a full-orientation wrist — it re-introduces the helicopter, ADR-021). Files: `Runtime/Retargeting/KalidokitControlRigDriver.cs`, `Runtime/Bootstrap/AppBootstrap.cs`.

---

## ADR-024 — POC scope: wrist-orientation deferred (source hand data too noisy at range); ship body/arms/fingers/face (2026-08-10)

- **Status:** Accepted (2026-08-10). Decision made after the ADR-023 pipeline logging + two live OAK captures **proved the wrist instability is source-side, not a Unity bug.**
- **Context:** the user reported the wrist "spinning"/jitter blocks even a POC and asked for a decision. Three-stage frame-aligned logs (sender/recv/model) localized it across two captures:
  - **Body + facing STABLE:** shoulders 0.7–0.9 cm/frame, hips-yaw mean ~0.15°/frame, **p95 0°** (frontal-lock works).
  - **Wrist:** recv palm quaternion **7.7–10°/frame steady + 174° snaps on every hand re-acquisition**; model right-hand **38°/frame (p95 172°)**. The **forearm is stable (1.1–1.7°/frame)** → NOT a helicopter; the roll-free wrist apply is correct.
  - **Source:** send R-wrist **5.7–7 cm/frame with 8.6–12 m depth spikes** → the RTMW3D hand landmarks at 2–3 m on the 640×400 OAK RGB are too small/noisy and drop out often.
- **Decision:** for the POC, **`wristRotationWeight = 0`** (wrist orientation OFF; finger **curls** still driven). A stable hand > a snapping one, and it's ONE live slider to re-enable. **POC scope = body pose + arms + finger curls + face expressions + position/root, frontal-facing.** Persisted in `Bootstrap.unity`.
- **Why not keep fixing it in Unity:** source garbage can't be smoothed into a stable orientation without unacceptable lag, and the re-acquire snaps remain (ADR-020: do not add more Unity-side smoothing). The retarget math is verified correct (roll-free; forearm stable). **It is an INPUT problem.**
- **Path to full wrist (post-POC):** a **close-range hand source** — a dedicated webcam running MediaPipe Hands up close, the user much nearer the OAK, or a hi-res OAK RGB crop around the hands. Then re-enable `wristRotationWeight` (the ADR-021/023 roll-free apply already works). **DO NOT** re-enable it on the current distant-hand input for the POC.
- **Also deferred (single-front-camera limits):** body turn-tracking (frontal-locked, ADR-023) and wrist pronation/roll (ADR-021).
- **Files:** `Scenes/Bootstrap.unity` (`wristRotationWeight` 0.7→0).

---

## ADR-025 — Milestone-2: un-flatten trunk to restore waist-bend + body-turn; physical movement on both paths (2026-08-11)

- **Status:** Accepted (2026-08-11). Makes the ADR-023/024 flatten/frontal-lock an **opt-in fallback**, not the default.
- **Context:** Milestone-1 confirmed live: at **2.23 m** the wrist depth jitter fell to 0.016 m/frame, palm to 3.8°/frame, and the model hand/forearm direction to **0.3°/frame (p95 1.3°) — stable**; facing frontal-locked. The user confirmed **the debug skeleton is perfect** and wants the MODEL to match it: waist bend, 360° turn, less still-jitter, physical depth movement, + a ProBuilder room to verify.
- **Root cause of missing bend/turn:** the sidecar `--flatten-trunk` (ADR-023, added for the 4 m profile bug) zeroes shoulder+hip Z → kills spine bend AND hip-line yaw; and the Kalidokit port zeroes spine pitch entirely (webcam origin) → no forward bend even un-flattened.
- **Decisions / changes:**
  1. **Un-flatten by default** (`--flatten-trunk` now default OFF): keep measured trunk Z so the hip-line yaw drives **TURN** and the trunk carries **BEND**. The old profile bug was 4 m depth noise; at ~2 m + with the **trunk now in the depth-smoothing set** (shoulders 5/6 + hips 11/12 added to `limb_idx`) the trunk Z is stable → turn without the profile swing. `--flatten-trunk` restores the frontal-lock fallback for noisy/far setups.
  2. **Spine forward-pitch added** (`KalidokitControlRigDriver`): the waist bend is derived from the measured mid-hip→mid-shoulder tilt (`atan2(trunk.z,-trunk.y)`), **neutral-subtracted** (removes the systematic RTMW3D "farther upper body" hunch; captured on first frame / C key) + **live-scaled** (`kalidokitSpineBendScale`, signed, 0=off), split across Spine+Chest. **Geometry-verified:** forward bend → spine/chest 0°→15° + head leans +0.23 m; upright → 0°.
  3. **Physical movement on BOTH paths:** `ApplyRootPosition` extracted and called on the Kalidokit path too (was FK-else-only → the Kalidokit body path never translated). Avatar now walks/jumps with the measured hip xyz. Geometry-verified turn = hips 0°→56° for a rotated hip line.
  4. **Still-jitter:** sidecar `--min-cutoff` 0.7→0.5 + trunk in the heavier depth-smoothing.
  5. **ProBuilder sample room:** `Scenes/Mirror.unity` → `SampleRoom` (floor + 3 walls, ProBuilder cubes) with `Materials/RoomChecker.mat` (generated checker) to verify depth movement.
- **Verify (user, live):** un-flatten can re-introduce the profile swing IF the setup is noisy/far — then `--flatten-trunk` restores frontal-lock, or lower `--depth-min-cutoff`. Tune `kalidokitSpineBendScale` (flip sign if the bend goes backward) and press **C** standing upright to zero the bend neutral. **Un-flatten also naturally removes the false arm-bend** (shoulder+elbow both measured again → no discontinuity).
- **Files:** `python-sidecar~/wholebody_udp_sender.py` (flatten default off, trunk in `limb_idx`, min-cutoff 0.5), `Runtime/Retargeting/KalidokitControlRigDriver.cs` (spine pitch + `SetSpineBend` + `Recalibrate`), `Runtime/Bootstrap/AppBootstrap.cs` (`kalidokitSpineBendScale` + `ApplyRootPosition` on both paths), `Scenes/Mirror.unity` (`SampleRoom`), `Materials/RoomChecker.*`.

---

## ADR-026 — Waist-bend: adaptive baseline (high-pass) instead of a single fixed neutral (2026-08-11)

- **Status:** Accepted (2026-08-11). **Refines ADR-025 decision #2** (the spine forward-pitch). Same feature, more robust neutral + jitter handling. **Convention-sensitive — do not revert without reading the data below.**
- **Context:** the user's live Milestone-2 OAK run (this session) reported three things: **(1) the avatar does not bend at the waist though the debug skeleton does; (2) it still jitters; (3) standing upright the avatar is slightly leaned forward ("leaned down").** Diagnosed from the run's own pipeline logs (`pipeline_logs/`, 5337 send/recv frames, 2026-08-11 11:32) — no guessing.
- **Data (measured from that capture's `recv_log`/`sender_log`):**
  - **The bend signal IS present** — reconstructing the driver's trunk pitch (`atan2(midShoulder−midHip depth, vertical)`) gives, at **<2 m**: upright p50 ≈ **+5.5°**, forward-bend p95 ≈ **+19°** (a real ~14° dynamic range). So the data carries the bend; the retarget was mis-using it.
  - **The old FIXED neutral was captured on the FIRST frame** — which in this run was the **4.24 m, all-zero-Z, 18 %-coverage startup garbage** → `neutralSpinePitch = 0°`. With the true upright baseline ≈ **+5.5°** at 2 m, the avatar carried a constant **~+5.5° forward lean at rest** (obs 3), and the actual bend rode on top of that lean + noise (obs 1).
  - **The upright baseline DRIFTS with distance** (upright reads **+5.5° at 2 m, −0.6° at ~3 m**) because the RTMW3D "upper body farther" systematic offset scales with range. So **no single fixed neutral can hold while the user walks** (and M2 added walking).
  - **Noise:** per-frame trunk-pitch std **~6°** with **35°/frame spikes** → the jitter/lurch (obs 2); the ~14° bend was comparable to the noise, so it read as "no bend."
  - **Framing:** distance **median 2.65 m, 58 % of frames > 2.5 m** (a large ~4 m cluster from walking), **depth coverage only ~21 %** — at that range the bend/turn signal is largely depth quantization. **Distance is still the dominant lever** (ADR-020/025): stand **~2 m**.
- **Decision — subtract a SLOW ADAPTIVE BASELINE (high-pass), not a fixed neutral, then rate-limit + smooth:**
  1. **Adaptive baseline** — a slow EMA of the raw trunk pitch (`spineBendBaselineTau`, default **8 s**) is the neutral; the bend is the *fast residual* above it. This removes the drifting/distance/systematic offset (upright → ~0 at **any** distance) while **keeping** the faster intentional bend. Seeded from the **median of the first 45 frames** (robust to the startup outlier); **no bend applied during warm-up**.
  2. **Spike reject** — rate-limit the bend change to **250°/s** (a 35°/frame lurch cannot get through).
  3. **Light low-pass** — `spineBendSmoothTau` 0.12 s on the output kills residual pitch jitter.
  - **Degenerate trunks** (vertical extent < 0.05 m, e.g. all-zero-Z frames) are skipped, so the baseline never seeds from garbage. **C re-seeds** the baseline (press standing upright).
- **Verified OFFLINE on the user's actual recorded logs** (the exact C# algorithm re-simulated frame-by-frame): pitch **jitter 0.56 → 0.36 °/frame (−36 %)**; **per-distance bend median ≈ 0 at every distance bin** (was +5.5° at 2 m); real bends of **±11–12°** still pass through. Unity **compile 0/0**. Final by-eye is the user's live re-run (injected-pose screenshots are unreliable, ADR-023).
- **Trade-off (accepted):** a bend **HELD longer than ~`baselineTau` (8 s)** slowly relaxes toward neutral as the baseline catches up. Fine for a mirror (bends are transient). To hold a sustained bend longer, **raise `kalidokitSpineBendBaselineTau`**; set it very large for effectively-fixed-neutral behaviour. This is the ADR-019/024 philosophy (prefer a stable pose over chasing an unreliable DOF), applied as a time-domain split.
- **Tuning (live, no recompile):** `kalidokitSpineBendScale` (visibility; **flip its sign if the bend goes backward**), `kalidokitSpineBendBaselineTau` (hold-a-bend ↔ correct-drift). The rate-limit/smooth constants are driver fields with good defaults.
- **DO NOT** revert to the single first-frame fixed neutral — it re-introduces the rest lean and cannot track the walking-distance drift. If a bend must hold indefinitely, raise the tau; do not go back to a fixed neutral.
- **Also fixed:** `run_capture.bat` forced `--min-cutoff 0.7`, **overriding ADR-025's 0.5** still-jitter default → restored to **0.5** (steadier when still). This alone reduces obs-2 jitter on the next capture.
- **Files:** `Runtime/Retargeting/KalidokitControlRigDriver.cs` (adaptive baseline + rate-limit + smooth + `Median` + `SetSpineBendDynamics` + `Recalibrate` reset), `Runtime/Bootstrap/AppBootstrap.cs` (`kalidokitSpineBendBaselineTau` + `SetSpineBendDynamics` call), `python-sidecar~/run_capture.bat` (`--min-cutoff` 0.7→0.5).

---

## ADR-027 — Torso-yaw conditioner (rate-limit + dead-zone) + restored Kalidokit dampeners; keep damped 360-turn (2026-08-11)

- **Status:** Accepted (2026-08-11). Implements the user's decision ("keep 360° turning, but damp it") from [`RETARGET_AUDIT_2026-08-11.md`](RETARGET_AUDIT_2026-08-11.md). Addresses that audit's R1 (primary) + R2. **Convention-sensitive facing change — read the audit before touching.**
- **Context:** the user's screen recording showed the **debug skeleton correct but the avatar arms/legs/torso wrong** (twisted waist, wrong facing, arms dragged/asymmetric). The audit localized it (same `filtered` frame feeds skeleton + Kalidokit, so it's the retarget): Kalidokit derives torso facing from the **depth separation of the shoulder/hip line** (2-point `rollPitchYaw`), which is **hypersensitive to OAK depth noise at range** — pipeline-log measured on the run's own frames: rest spine-yaw euler **−10°**, excursions to **−119°**, **±180° ambiguity flips** (from the `calcHips` jump-fix branches). **ADR-025 un-flattened the trunk** (for turn+bend), feeding that noise straight in → the chest over-twists and, since the arms are its children, **drags the arms into wrong/asymmetric world positions**. M1 avoided this because the sidecar **flattened** the trunk (yaw input ≈ 0). NOTE: an interim claim that our port over-rotated via a stray `×π` was **wrong** — Kalidokit's `rigHips` does `×π` too; our `KalidokitPoseSolver` lines 84–85 are faithful.
- **Decision:**
  1. **Torso-yaw conditioner** `KalidokitControlRigDriver.DampYaw` on hips + spine yaw: **(a) rate-limit** to `yawMaxRateDeg` (140°/s) — a spurious ±180° flip bounces back before the slew follows it (**rejected**), while a genuine gradual turn is **followed**; **(b)** light low-pass (`yawSmoothTau` 0.15 s); **(c)** a **soft dead-zone** (`yawDeadzoneLoDeg` 8° → `HiDeg` 22°, `SmoothStep` gate) so small/noisy yaw snaps to **frontal**. Self-seeds per signal (NaN sentinel → no startup slew). Live-scaled by **`kalidokitTorsoYawScale`** (0 = frontal-lock fallback, 1 = full damped turn).
  2. **Restored Kalidokit per-bone dampeners** — Hips **0.7**, Spine **0.45**, Chest **0.25** (the reference demo values), replacing our path's Hips ×1.0 + a 0.5/0.5 Spine/Chest split (which over-twisted ~1.4×, chest 2×). The **waist bend (ADR-026) keeps its own** Spine/Chest split (spineShare + 0.5 = 1.0), applied to the pitch only — yaw/roll use the dampeners.
- **Verified OFFLINE on the run's real logs** (exact C# algorithm re-simulated): rest spine-yaw **−10° → −1°**, **±180° flips 2 → 0**, jitter **1.4 → 0.86 °/frame (−39 %)**; applied spine-bone twist tail **p5 −119° → −75° total** (with dampeners). Unity **compile 0/0**. Final by-eye is the user's live re-run.
- **HONEST LIMIT (do not oversell):** **sustained** mis-estimation at range (e.g. an arm occluding a shoulder for a stretch) *looks like a real turn* to any conditioner, so the rate-limit **follows** it → a residual large-yaw tail remains (~−75° twist on ~5 % of frames in this capture). This is the **single-front-camera facing limit** (ADR-019/023/024). The escape hatch is **`kalidokitTorsoYawScale` → lower / 0 (frontal-lock)**; and **stand ~2 m** (this capture was 2.2 m avg but only ~63 % depth coverage).
- **DO NOT:** revert the dampeners to ×1.0 / 0.5-0.5 (re-introduces the over-twist); remove the rate-limit (the ±180° flips return); **assume this fixes the ARMS if they are independently wrong** — see the open item.
- **R4 RESOLVED — no arm bug (live injection, 2026-08-11).** Ran the asymmetric-pose injection on the live bound driver (both-up / left-up-only / right-up-only / both-down, converged over repeated `Apply`, read raw hand height above each shoulder): **both arms raise correctly when both are raised; each side responds independently in the right direction.** So the arm solver + `flipQuat` conversion are **sound** (only a minor ~25% side-to-side magnitude asymmetry, not worth chasing). The video's "flex both arms → one avatar arm up" was the **torso twist dragging the arms** (R1), which ADR-027 targets. **DO NOT touch `kalidokitBodyFlipQuat`.**
- **Tuning (live):** `kalidokitTorsoYawScale` (main: 0 frontal-lock ↔ 1 full turn); internal `yawMaxRateDeg` / `yawSmoothTau` / `yawDeadzoneLo/HiDeg` for the conditioner shape.
- **Files:** `Runtime/Retargeting/KalidokitControlRigDriver.cs` (`DampYaw`, torso-yaw state, per-bone dampeners, `SetTorsoYawScale`, `Recalibrate` reset), `Runtime/Bootstrap/AppBootstrap.cs` (`kalidokitTorsoYawScale` + push).
- **AMENDMENT (2026-08-11) — FRONTAL-LOCK is now the DEFAULT (`kalidokitTorsoYawScale = 0`).** The user's next capture (frame-analyzed from `Screen Recording …173203.mp4` + logs) confirmed the conditioner **removed the flips/jitter** (hips-yaw frame-to-frame **max 90° → 2.27°**, STABLE) and **arms/bend/position now track correctly at ~1.5–1.8 m** (both-arms-up symmetric — the old failure is gone). BUT a residual **"hands go backwards"** remained: the avatar **smoothly turns to face away** when the user walks back. **Root cause = DISTANCE**, measured from that capture: median |spine-yaw euler| is **22° at <1.8 m but 64° at 2.1–2.4 m**, and the big false-turns (>60°) cluster at mean **2.14 m** vs 1.78 m overall; at 2.66 m even the raw debug skeleton folds up. So the conditioner correctly follows a *sustained* yaw, but past ~2 m that "sustained yaw" is depth garbage, not a real turn — and the user walks to 2.5 m+ in the room (M2). **The user chose frontal-lock** (a mirror is frontal; you don't turn your back to a mirror). **`kalidokitTorsoYawScale` default 1 → 0**, applied live too. Everything else (arms, waist bend, walking, fingers) is unaffected (the scale multiplies **only** the torso yaw). **To re-enable body-turn:** raise `kalidokitTorsoYawScale` toward 1 **while standing < ~1.8 m** (reliable there). **Deferred option (not built):** a **distance-gated** yaw — turn when close, auto-freeze frontal past ~2 m — is the proper way to keep walk-around turning; needs the hip distance (`frame.RootPositionMetres`) plumbed to the conditioner + threshold calibration. **DO NOT** raise the default back to 1 without the distance gate — it re-introduces the face-away at range.

---

## ADR-028 — P0 stability: confidence-gated limb hold (LimbGate) + tightened distal-limb caps (Accepted 2026-09-07)

- **Status:** Accepted. Closes audit finding **F-01** (CRITICAL) and **F-03/F-04**. Evidence: [`AUDIT_FBT_2026-09-07.md`](AUDIT_FBT_2026-09-07.md); acceptance: [`P0_ACCEPTANCE_2026-09-07.md`](P0_ACCEPTANCE_2026-09-07.md).
- **Context:** per-joint confidence was gated only at the sidecar emit stage and never reached the Unity solve, so an invalid/occluded joint (emitted as `[0,0,0,0]`) drove a real bone rotation **aimed at the origin** → limb collapse. Separately the One-Euro `--max-jump 1.5 m` sat *above* the observed ~0.5–0.9 m limb spikes, so they passed, and knees/ankles were excluded from depth-smoothing and hold entirely.
- **Decision:**
  1. **P0-1 `LimbGate`** (`Runtime/Retargeting/LimbGate.cs`) — a two-state (VALID/HELD) gate at **application** time, not inside the solver. A limb whose weakest driving joint is below `limbConfidenceThreshold` **holds its last valid rotation**; it never substitutes zero. Gates on the Kalidokit cross-map (left bones ← right landmarks).
  2. **P0-2** — knees/ankles (WB 13–16) added to the depth-smoothing + bounded-hold set, and per-index displacement caps `--arm-max-jump` / `--leg-max-jump` (**0.35 m**) for wrists/elbows/knees/ankles, rate-limited (slewed) not dropped. Trunk and hands keep the global cap.
- **Verified live (human subject, real OAK-D):** the Unity gate fired **14 times across all four limbs**, **250 held rotations with 0 zero-rotations** (no origin collapse), re-acquisition jumps **max 15.7°** (no teleport/flip), peak displacement **−67…−76%** on wrists/elbows, **trunk improved 33–36%** (no regression). Scripted-occlusion proof: **20/20**.
- **Bone-scale deformation is measured constant** (`spread 0.000000 m` over 102 968 applied frames). **Do not call the failure mode "squashing" — it is LIMB ROTATION INSTABILITY.**
- **HONEST LIMIT:** RTMW3D does **not** lower confidence for a limb hidden behind the torso — it infers a plausible position at ~0.63 confidence (measured: **0 frames below the 0.3 threshold** across a 45 s hand-behind-back block). P0-1 therefore protects against **total joint loss**, not against confident-but-wrong data. That gap is what ADR-029 exists for.
- **DO NOT:** move safety out of the LimbGate; raise the 0.35 m cap without live evidence (peak *legitimate* fast-arm motion measured **0.0746 m** — 4.7× headroom).
- **Files:** `Runtime/Retargeting/LimbGate.cs`, `KalidokitControlRigDriver.cs`, `AppBootstrap.cs`, `python-sidecar~/smoothing.py`, `wholebody_udp_sender.py`.

---

## ADR-029 — P1-1: per-joint temporal tracking + plausibility (confident-but-wrong detection) (Accepted 2026-09-08)

- **Status:** Accepted. Report: [`P1_1_TRACKER_2026-09-08.md`](P1_1_TRACKER_2026-09-08.md).
- **Context:** ADR-028's honest limit — confidence alone is not validity. A hidden wrist held a **wrong** position for 840 frames at ~0.63 confidence, so no confidence gate could fire.
- **Decision:** one reusable `JointTracker` per joint (`python-sidecar~/joint_tracker.py`), states **TRACKED / WEAK / PREDICTED / LOST**, placed **after** the P0 smoother and **before** PoseFrame. It does not replace the LimbGate: a `LOST` joint has its **emit confidence zeroed**, which is byte-identical to a real occlusion, so the P0 gate still makes the final call.
- **Seven plausibility signals:** confidence, residual vs kinematic prediction (against an *adaptive* per-joint scale), speed, acceleration, depth consistency (existing 5×5/p30 result, unchanged), **FROZEN** (pinned <4 mm for ≥12 frames while the parent moved ≥5 cm), and segment length vs a running median.
- **The FROZEN check is the one that matters** — a stuck joint has near-zero residual, speed *and* acceleration, so no conventional test can see it. It is the actual observed failure.
- **Causal only, by choice.** An N−2…N+2 comparison needs lookahead, and lookahead is latency. A suspicious sample is down-weighted instead; consecutive samples that agree with each other promote back to TRACKED, so `A→B→C→D→E` (real motion) survives while `A→B→X→B` (spike) is damped, at zero added latency.
- **Verified:** 37/37 unit assertions, **6/6 adversarial cases detected** (an 0.8 m error at confidence 0.95 leaves a **2 mm** trace; the frozen-wrist case recovers with **0.0000 m** residual). Legitimate dancing: median/p95 unchanged, peak −18…−27% on four joints. Cost **0.085 ms** median for 12 joints.
- **Two calibration lessons (measured, not guessed):** the neighbour/segment check must be **corroborating evidence, capped at 0.5**, never a veto — uncapped it caused 3170 false rejections; and the output step limiter must budget from **measurement-to-measurement**, not from `lastValid`, or the WEAK offset inflates its own budget and the limiter never binds.
- **DO NOT:** raise prediction horizons (bounded 6 frames / 0.30 m by design); add smoothing here — the tracker adds **memory, not lag**.
- **KNOWN WEAK SPOT:** a sustained high-confidence teleport longer than the prediction window ends in LOST with slow recovery during fast motion. Needs short-gap prediction + blended recovery (future work).

---

## ADR-030 — P1-2: latest-frame queue policy (frame freshness over frame completeness) (Accepted 2026-09-08)

- **Status:** Accepted. Report: [`P1_2_FRESHNESS_2026-09-08.md`](P1_2_FRESHNESS_2026-09-08.md).
- **Context:** live measurement showed camera→host latency of **131 ms**. `DataOutputQueue.get()` returns the **OLDEST** packet; with inference (~21 ms) slower than the 30 fps sensor the host queue sat full at `maxSize=4` → **4 × 33.3 = 133 ms** of pure staleness. Nothing was slow; the system was working on old frames.
- **Decision:** for an interactive avatar, **freshness beats completeness**. One blocking `get()` (liveness), then `tryGetAll()` keeping only the **newest** RGB packet; stale intermediates are **discarded, not processed**. Queue size unchanged (no large buffer added). Toggle `--latest-frame` (default ON) / `--no-latest-frame`.
- **RGB/depth pairing:** depth is chosen by **closest timestamp** to the selected RGB frame — never newest-RGB + oldest-depth. This **improved** pairing (audit **F-09**), which had been mis-associating depth by **54.56 ms ≈ 1.6 frames**: max sync error 54.63 → **21.24 ms**.
- **Verified live (human subject):** frame age **131.51 → 31.36 ms**, camera→UDP **161.81 → 62.03 ms**, fps and compute unchanged (20.85 vs 20.70 ms — proving the win was queue wait, not processing). 180 s soak: **no accumulation**. Under load 38.6% of captured frames are deliberately dropped — frames the estimator could never have consumed in time.
- **Compute spikes >80 ms occur only at frame index 0–2** — ONNX Runtime/DirectML warm-up, per *process*, not per frame (a 3378-frame soak spiked only at frames 0 and 1).
- **DO NOT:** enlarge the queue (that trades latency for frames nobody sees); set `--inject-load-ms` in production (test-only instrument).
- **Device-side `XLinkOut` queues left at DepthAI defaults** — measured as non-contributing; revisit only if frame age exceeds sensor transfer plus one frame on a slower host.

---

## ADR-031 — P1-3: Unity timestamped pose buffer + interpolation (Accepted 2026-09-08)

- **Status:** Accepted. Report: [`P1_3_POSE_BUFFER_2026-09-08.md`](P1_3_POSE_BUFFER_2026-09-08.md).
- **Context:** Unity applied **latest-wins** and re-applied the same pose every render frame with a fixed slerp; `seq`/`t` were parsed but used only for log alignment. Measured **17.8 applies per packet** and **41.5% of render frames with literally zero bone movement** — the stutter signature.
- **Decision:** `Runtime/Core/PoseBuffer.cs`, a bounded ring of 16 timestamped poses. Unity renders at `now − poseInterpolationDelayMs` and interpolates between the bracketing pair. Buffer axis is Unity's **receive epoch** (no sender-skew assumption). Duplicate, out-of-order and **backwards-timestamp** packets are rejected. **No extrapolation** — past the newest sample it clamps.
- **SAFETY RULE (preserves ADR-028):** a landmark is **NEVER position-interpolated across an invalid endpoint**. The sidecar sends a dropped joint as `[0,0,0,0]`, so lerping valid→zero would place it half-way to the **origin** — the exact F-01 collapse. The valid position is carried and confidence becomes `min(a,b)` = 0, so the joint still reaches the LimbGate as invalid and the gate holds. Explicitly tested.
- **Verified with identical deterministic input** (`stream_motion.py` — a human cannot repeat a performance closely enough to measure interpolation quality): stutter (delta CoV) **2.286 → 1.070 (−53%)**, frozen render frames **41.5% → 23.6%**. 47/47 EditMode tests.
- **Delay = 40 ms, chosen on evidence:** 55 ms bought only 3 further points of smoothness for 15 ms more latency. `poseInterpolationDelayMs = 0` restores the original path exactly.
- **Ordering mattered:** P1-2 removed ~100 ms of staleness; P1-3 spends 40 ms of it. Net **~162 ms → ~102 ms** and smoother. Done in the other order, the buffer would have pushed an already-162 ms pipeline to ~202 ms.
- **DO NOT:** treat this as a smoothing stage (it is temporal reconstruction; P0/P1-1 own stability); add extrapolation without a separate decision.
- **KNOWN GAP:** hand/palm quaternions still use the existing rate-limited slerp path, so body and hands sit on timelines ~40 ms apart. `PoseBuffer.SlerpRotation` exists and is tested for when hands are wired in.

---

## Capture source note (M2)

Development input is a **video file** (`VideoFileCaptureService`, `sampleVRMFiles/sample video.mp4`) rather than a live webcam, selected via `AppBootstrap.useVideoSource`. `WebcamCaptureService` exists and is swapped in by flipping that flag. Both implement `ICameraCapture`; tracking providers are agnostic to the source.

---

## ADR-032 — P1-4 kinematic recovery REJECTED (live avatar evidence)

- **Status:** Accepted 2026-09-08. Sidecar counterpart: ADR-P009. Full report:
  [`P1_4_CLOSEOUT_2026-09-08.md`](P1_4_CLOSEOUT_2026-09-08.md).
- **Context:** P1-4 runs in the Python sidecar, but its failure is only visible **on the avatar**, so
  the decision was made from Unity-side measurement: `model_log.jsonl` limb-held state, per-pose bone
  rotation steps, and knee/elbow interior angles, correlated against the sidecar's event log.
- **Decision:** P1-4 is **rejected**. No Unity code changed — the Unity pipeline
  (`P1-3 PoseBuffer → Kalidokit → P0 LimbGate → VRM`) was correct throughout and is unmodified.
  Only the sidecar default flipped (`--recovery` → off).
- **Unity-side evidence:**
  - frames with at least one limb **held by the P0 LimbGate**: **0.47% → 17.83%** under P1-4,
    restored to **0.14%** after rollback. The gate behaved correctly; P1-4 gave it far too much to hold.
  - avatar snaps > 45° per pose: 15 → 27 → **13** after rollback.
  - `kneeAng.l` minimum 75.0° → **12.5°** (anatomically implausible) under P1-4; 0 implausible frames
    after rollback.
  - bone-length CV stayed **0.00000** in all three runs — the retarget never deformed the rig, so the
    artefacts were rotation, never scale.
- **Diagnostics added (DIAG-ONLY, read-only, retained):** `AppBootstrap.WriteModelLog` now also emits
  `kneeAng`/`elbowAng` (interior angles from world positions) and `luplegF`/`ruplegF`/`llowlegF`/
  `rlowlegF` (wrap-free bone forwards). Euler angles wrap at 0/360, so frame-to-frame deltas taken off
  them produce phantom 300° spikes; these fields make joint inversion and rotation spikes measurable.
- **Consequence:** do not enable sidecar `--recovery`. Do not begin palm/foot work with it on.
  Next step is the upstream F-08 measurement-quality audit, not another downstream layer.

---

## ADR-033 — Arm retargeting V1 (`ArmAimSolver`) confirmed live; kept ON as the default

- **Status:** Accepted 2026-09-09. Implementation ADR context:
  [`UNITY_ARM_RETARGET_V1_2026-09-08.md`](UNITY_ARM_RETARGET_V1_2026-09-08.md). Live report:
  [`UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md`](UNITY_ARM_RETARGET_V1_LIVE_VALIDATION_2026-09-09.md).
- **Context:** the V1 arm solver was accepted on offline maths but its §12 on-camera sign-off was
  outstanding, so `kalidokitAimArms = true` was shipping on deterministic evidence alone. The brief
  required a live A/B over nine arm motions with a real subject, and explicitly warned that
  `upperErrDeg`/`forearmErrDeg` measure only the solve→rig→bone path and must **not** be read as
  proof that tracking is correct.
- **Decision:** keep `kalidokitAimArms = true`. **No code changed** to reach this decision — the run
  was evidence-only, as briefed.
- **Method:** the A/B metric is `[RETARGET-TRACE] errDeg`, whose `want` is the raw landmark direction
  (exactly what `PoseDebugSkeleton` draws) and whose `got` is the achieved avatar bone. That answers
  "does the VRM follow the green skeleton" independently of how good the tracking was in a given take,
  which the solver's own self-check cannot. Both branches ran inside one Play session with the branch
  toggled mid-Play, so camera, model warm-up and subject were shared.
- **Evidence (nine motions, both branches):**
  - avatar-vs-landmark error **35.78° → 0.46°** mean (78×), p95 **107.30° → 0.90°**
  - elbow reproduction error **21.98° → 0.31°** mean; achieved elbow range **97.9–175.1° → 28.6–179.9°**,
    i.e. the saturated `lowerArm.x` clamp is gone
  - L/R asymmetry **22.6° → 0.5°** mean overall, **≤0.2° in every motion** except the one containing
    a P0 gate hold — the "avatar raises one arm" failure
  - **within-run control:** leg bones (untouched by the change) moved **1.06×** between branches while
    arms moved **78×**, so the effect is the code under test, not the subject
  - the old branch fails **even when tracking is good**: motion 4 landmarks supplied an 86.9° arm
    elevation and the old avatar reached only 73.0°
  - bone-length spread **0.00000 m** in both branches; 0 Unity console errors; latency unchanged
    (p50 24.0 ms sidecar-send → avatar-apply)
- **Residuals, and where they belong:**
  - **arm retargeting:** 31 single-frame forearm *twist* pops > 45°, 68 % at elbow bend < 15°, median
    bend 12.3° against `BendSinMin`'s ≈11.5° threshold; 10 roll steps > 90° in 45 s of dense trace, all
    below 15° of bend and **zero** above it. Bone *direction* is unaffected. Left unchanged by design.
  - **upstream landmark/tracking:** now the dominant error source — a 121° elbow reported for a
    straight hanging arm, and arms held overhead missed entirely for one 15 s window (max tracked
    elevation 23.7°). The avatar reproduced both faithfully. This is F-08 territory, not retargeting.
- **Rejected alternative — a custom rig.** Ruled out by measurement: the VRM rig reproduces the tracked
  pose to **0.31° mean** elbow error and **≤0.1°** arm elevation, with bone lengths constant to five
  decimals. A new rig cannot improve a 0.3° reproduction error and would reproduce the same wrong
  landmarks just as faithfully.
- **Consequence:** next change inside arm scope is `BendSinMin`, validated as its own controlled task.
  The higher-impact work is upstream 2D elbow/wrist localisation. Roll back at any time with
  `kalidokitAimArms = false`; the old branch is byte-for-byte intact and was exercised live.

---

## ADR-034 — Arm roll V2: hysteresis + continuity guard on the elbow-plane reference

- **Status:** Accepted 2026-09-09. Report:
  [`UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md`](UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md).
  Supersedes the `BendSinMin` limitation recorded as ADR-033 / V1 §16.4.
- **Context:** V1's live validation left exactly one defect attributable to the arm code — forearm twist
  pops near a straight elbow. Binning every wrapped roll step of the dense live trace by elbow bend put
  **7 of the 10 steps above 90° inside the single 10.0–12.5° bin** that straddles `BendSinMin`'s 11.54°,
  with **none above 12.5°**. The fault was the SINGLE THRESHOLD itself, not its value: any single
  threshold in that band makes the roll source alternate between `ElbowPlane` and `Held` frame to frame,
  and each alternation can reverse `cross(upper, forearm)` — a 180° flip.
- **Decision:** replace the single threshold with a state machine confined to the roll reference:
  hysteresis (`BendSinEnter` 0.35 / `BendSinExit` 0.15, a dead band that strictly contains the measured
  hot zone), a continuity guard (reject a reversed candidate, or one implying >90° of roll in a frame),
  and debounced re-acquisition (6 coherent frames at a clear bend) stepped in at 30°/frame. Bone
  DIRECTIONS are untouched by construction: `LookRotation(dir, normal)` maps forward onto `dir`
  whatever the normal is.
- **Evidence — V1 vs V2 replayed on BYTE-IDENTICAL recorded video input** (a live A/B cannot do this;
  the subject moves differently in the two passes):
  - roll steps > 90°: **6 → 0**; > 45°: **10 → 3**; worst single-frame step **145.56° → 65.29°**
  - bend at the > 45° events: p50 **11.4° → 35.6°** — the residuals are no longer near-straight
  - the offline model was cross-checked against the shipped C# (Unity live max 63.31° vs model 65.29°)
  - live video: guard engages 0.2–0.5 % of frames, 0 steps > 90° on either arm
  - 0 bytes allocated; 1.593 µs/solve, still under the pre-V1 Kalidokit branch's 3.435 µs/frame
- **Bug caught before shipping:** the rate bound initially applied only to the confirmed-flip branch, so
  the ordinary accept path snapped the remainder once within 90° — an offline replay measured a 60°
  single-frame jump. The bound now applies on both paths, making "no frame moves the roll more than
  `RollSlewMaxDeg`" an invariant rather than a branch property.
- **Consequence:** `BendSinMin` no longer exists. Eight deterministic tests pin the behaviour. The
  EditMode suite still needs a batch-mode run (the MCP Test Runner closes this Editor — see ADR-035).

---

## ADR-035 — The rendered VRM is NOT where the arm goes crooked

- **Status:** Accepted 2026-09-09. Report:
  [`UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md`](UNITY_ARM_RETARGET_V2_FORENSICS_2026-09-09.md) §3/§4.
- **Context:** the avatar was reported to look crooked even when the green `PoseDebugSkeleton` looked
  right, and V1's near-zero direction error was suspected of measuring the wrong thing.
- **It was measuring the control rig.** `AppBootstrap`'s `GetComponentInChildren<Animator>()` resolves,
  on a control-rig-enabled `Vrm10Instance`, to `Vrm10ControlBone:*` — NOT the skinned
  `metarig/.../upper_arm.L`. The skinned bones are reachable only via `Vrm10Instance.Humanoid`. So a
  naive "control vs VRM" check reads 0.000 by construction and proves nothing.
- **Decision / finding:** measured against the ACTUAL skinned bones, the pipeline is exact.
  **Stage control-rig → rendered VRM bone = 0.000000° maximum across 13 308 video-driven samples** and
  0.000° on all six deterministic poses; bone-length delta **0.000000 m**. Source → control is 0.000° on
  exact input and 0.0485° median under video motion (the pre-existing `lerpAmount = 0.5` slerp lag).
- **Classification:** SOLVER, CONTROL and VRM-mapping are ruled out by measurement; MESH is not
  implicated; **SOURCE (upstream landmarks) is the remaining candidate**, consistent with ADR-033.
- **Consequence:** no fix was implemented — the brief forbids one until a stage is proven bad, and none
  was. Do not look for the crookedness in the rig. Two latent issues were recorded and NOT fixed:
  `rawLeftHand`/`rawRightHand` also resolve to control bones (inert only because
  `wristRotationWeight = 0` — fix before palm work), and `PoseBuffer.Push` silently drops every packet
  from a sidecar that restarts its `seq` while Unity stays in Play.

## ADR-036 — Torso yaw re-enabled at 0.75 and validated on the real OAK-D path (Conditional)

- **Context:** ADR-027 zeroed `kalidokitTorsoYawScale` because the OAK depth-derived yaw over-twisted the
  chest and dragged the arms under the old Euler arm path. V3 measured that workaround as the dominant
  remaining defect (shoulder-yaw error to 50.84°, arms wrong *relative to the body* by up to 51.10°,
  hands pulled inside the torso), and ARM V1/V2 removed the arm-drag mechanism by dividing the parent
  rotation out. The scale was restored to **0.75** and swept; the original validation could not exercise
  the OAK-D path and recorded that as the reason for a CONDITIONAL verdict.
- **Decision:** keep `kalidokitTorsoYawScale = 0.75`, and treat the OAK-D evidence below as the gate on
  promoting it to a settled default. Do not change the torso algorithm on this evidence alone.
- **Evidence (this task, real depth path — the earlier "OAK-D not testable" finding was wrong on both
  counts: `depthai 2.32.0.0` is installed, and 111,119 recorded frames across 11 captures carry the
  signal because `--flatten-trunk` defaults to FALSE):**
  - **Stable.** ±180° ambiguity flips **2 in 55,408 valid frames**; output sign reversals **0 at every
    scale in every capture**; live device max frame step **2.11°** with zero flips. The ADR-027 rate
    limiter does its job.
  - **No arm regression.** Source → *actual skinned* bone direction: live OAK **p50 0.057–0.171°, max
    ≤0.78°**; on video, p50/p95 **flat or better** as the torso rotates (0.265°/2.065° frozen →
    0.329°/1.871° at scale 1.0). Only the tail doubles (5.05° → 11.75°), which is the bounded
    `lerpAmount = 0.5` parent transient, halving per applied frame.
  - **The 1.4× over-gain is confirmed live**: measured gain **1.194** at scale 1.0 on the OAK path
    versus the held-frame 1.230, arrived at independently.
  - **Gain is not a property of the scalar.** The same 0.75 yields **0.167** on `video.webm`, **0.522**
    live on OAK, **0.923** held — a 5.5× spread driven only by motion bandwidth.
- **Consequence — two costs, both new and both measured:**
  1. **The torso path has NO confidence gate.** `CalcHipsAndSpine` reads `lm[11/12/23/24]`
     unconditionally (P0-1 `LimbGate` covers arms and legs only), so an absent detection makes
     `atan2(0,0)` command **exactly +90° of yaw**. Caught live on an empty room at 0.75: landmark
     confidence **0.71**, hallucinated depth → 40.46° source yaw → **avatar torso twisted 23.82°**.
     `torsoYawScale = 0` was masking this; restoring the scalar arms it. **Fix this before shipping
     0.75** — gate the trunk on landmark plausibility and hold the last good value.
  2. **Rest jitter of 5.4–7.6° stdev while the subject is still**, against exactly 0.000° at scale 0;
     it scales linearly with the scalar. **0.50 roughly halves both costs** (jitter 5.42° → 3.61°,
     worst output 128.7° → 85.8°) for little tracking loss, and is the better value if 0.75 ships
     without the gate.
- **Still open:** the guided OAK-D motion list (slow/fast yaw, left-right turns, ~90°, ~180°) was NOT
  completed with a live subject, so whether a deliberate 180° turn produces an ambiguity flip on this
  device is unanswered. Harness is built and committed (`oak_guided_v4.py`, `oak_guided_v4.ps1`).
- **Evidence:** `docs/UNITY_TORSO_YAW_V4_VALIDATION_2026-09-09.md` §5 (rewritten), §4d;
  `python-sidecar~/oak_v4_evidence/`.

## ADR-037 — Torso V5: trunk validity gate + relative-yaw composition (Accepted, conditional)

- **Context:** ADR-036 restored `kalidokitTorsoYawScale` but recorded two structural faults it could not
  fix with a scalar. (1) The composition summed **two absolute yaws** — `0.70·hipYaw` at the Hips and
  `(0.45+0.25)·shoulderYaw` across Spine+Chest — and because the bones nest, the rendered shoulder line
  came out as their sum. Measured consequences: a rigid turn over-rotated **1.4×**, opposing hip/shoulder
  yaw **cancelled** (gain +0.116), and a 52° twist **inverted** the torso (gain −2.944). No scalar can fix
  a gain ranging −2.944…+1.230. (2) `CalcHipsAndSpine` read `lm[11/12/23/24]` with **no validation**, so an
  absent detection made `atan2(0,0)` command **exactly +90° of yaw** — caught live on an empty room with
  landmark confidence 0.71 twisting the avatar 23.82°.
- **Decision:**
  1. Add `TrunkGate` — a **geometric + continuity** validity layer for the 4-point trunk. Not
     confidence-based: 0.71 confidence was measured accompanying a hallucination, so confidence does not
     separate the populations. Thresholds measured over 111,546 frames: span floor **0.10 m** (degenerate
     p95 0.054/0.091 vs real p01 0.156/0.123; costs 0.000 %/0.195 % of real frames), span ceiling 1.0 m,
     jump ceiling **500 °/s** (real p99.9 is 435 °/s; costs 0.078 %). Hip and shoulder are gated
     **together** because the composition consumes their difference. Invalid → HOLD; never valid yet → frontal.
  2. Replace the composition: **Hips ← absolute hip yaw** (gain 1.0); **Spine/Chest/UpperChest ← relative
     twist `shoulderYaw − hipYaw`** with weights **0.20/0.40/0.40 summing to exactly 1.0**, renormalised
     when a rig lacks a bone. The chain then equals `shoulderYaw` identically for every hip/shoulder ratio.
  3. **Bind `UpperChest`** — it was never fetched in `Bind()`, so a quarter of the trunk was rigid by omission.
  4. `kalidokitTorsoYawScale = 1`; it is no longer a compensation for a broken composition, only a
     frontal-lock fallback at 0.
- **Evidence:** controlled torso probe on the real UDP wire, measured on the **actual skinned VRM bones**:
  rigid ±45° and +90°, twist ±35°, and hips +30/shoulders −30 all reproduce with **gain 1.000, exact to
  0.01°** (the old form gave 1.400 / 0.700 / **0.000** on the same inputs). Ghost test (real → all-zero
  landmarks → real, confidence 0.75 on the wire): **0 frames within 15° of ±90°**, avatar held to a
  **0.0044° stdev**, 3587 `ShoulderSpan` rejections, immediate resume. Trunk gate rejected **0 of 21,173**
  valid probe frames. On the recorded OAK corpus, tracking error falls **49 %/61 %/58 %** and rest jitter
  improves **21 %** and **95 %** on two captures (unchanged on the third). ARM V2 unchanged: p50
  **0.0000°**, p95 0.1153° through 90° torso turns. **Full EditMode suite 94/94 pass** (invoked directly;
  the MCP Test Runner closes this Editor).
- **Consequence — the cost, stated:** V5 at gain 1.0 is *faithful*, so bad source depth now reaches the
  avatar more completely. On the poor-depth `p14` capture the time beyond 45° roughly doubles
  (8.11 % → 15.71 %) even though tracking error falls 58 %. **Upstream trunk-landmark depth quality is now
  the binding constraint on torso realism**, which is sidecar work.
- **Still open:** the guided **OAK-D live motion list** (slow/fast yaw, reversal, ~45/90/180°) was NOT run —
  this round was directed to video input. Verdict is therefore CONDITIONAL PASS.
- **Evidence:** `docs/UNITY_TORSO_V5_RELATIVE_YAW_2026-09-09.md`; `python-sidecar~/oak_v4_evidence/`.

## ADR-038 — F-09: no torso-yaw quality gate on this evidence; the measurement is under-resolved

- **Context:** ADR-037 (V5) left one problem open — bad shoulder/torso depth produces a *plausible but
  incorrect* torso yaw, and V5, being faithful, reproduces it. F-09 asked whether that measurement can
  be assigned a reliable quality score. 22,727 frames across the 5 captures carrying per-joint depth
  diagnostics (`audit_log.jsonl` joined to `sender_log.jsonl`); label = the 2-D/3-D disagreement rule
  (shoulder pixel span is independent of the per-shoulder depth difference that produces the yaw).
- **Decision:** **do NOT add a torso quality gate.** Nothing was changed.
- **Root cause, measured:** the yaw is **under-resolved, not merely noisy**. `yaw = atan2(dx, dz)`, and
  `dz` carries only stereo disparity resolution: one depth step is **6.3–11.3° of commanded yaw**, and on
  **60.1 % of frames both shoulders land on the same quantised level**, forcing yaw to exactly 0. p75 of
  the raw shoulder depth difference is 161 mm = **27.4°**. The signal is close to ternary; the one-euro
  depth filter hides the staircase temporally without adding information.
- **Evidence against each candidate gate:**
  - `hipLenErr` (3-D hip segment length error) is a genuinely independent predictor, **AUC 0.897**, and
    still **unsafe**: at its ROC-optimal 0.17 it rejects **19.7 % of image-confirmed genuine turns** to
    catch 75.9 % of BAD; in counterfactual replay it worsens **rest jitter on 3 of 5 captures**
    (`f08` 6.72 → 18.42), triples the rejection rate, and pushes p95 recovery latency to **13.9 s**, all
    for under 1° of mean-error gain.
  - **F-08's signals do not transfer to torso yaw**: `validFracMin` AUC **0.482**, `surfCrossSh` 0.516,
    `_absTrunkDz` 0.412 (*inverted* — bad frames have SMALLER shoulder-vs-hip depth disagreement).
  - Confidence reaches AUC 0.773 but is ruled out: V4 measured 0.71 confidence on a hallucinated person.
  - Combining features made it worse than the best single feature.
- **What V5 already handles:** of 553 BAD frames, the shipped `TrunkGate` rejects **56.3 %** (39.1 % span
  + 17.2 % jump). The residual is **242 frames = 1.086 % of all frames**, with **|yaw3D| p50 = 169°** —
  the ±180° ambiguity, not a general quality problem.
- **Consequence / next:** (1) a targeted ±180° 2-D/3-D consistency check is the right candidate, but this
  report **cannot validate it because it is the rule used to define the label** — it needs a live session
  with known torso headings. (2) Architecturally, take yaw **magnitude from 2-D foreshortening** (pixel
  resolution) and only the **sign** from depth (1 bit, which disparity can support); that is sidecar work.
  (3) Reducing quantisation (closer subject, wider baseline, sub-pixel disparity) attacks the cause.
- **Verdict:** CONDITIONAL PASS — predictor found, gate not safe, correct candidate needs live ground truth.
- **Evidence:** `docs/F09_TORSO_YAW_QUALITY_AUDIT_2026-09-10.md`; `python-sidecar~/oak_v4_evidence/f09_*`.

## ADR-039 — F-10: hybrid yaw REFUTED on the sign; the 2-D magnitude is validated

- **Context:** F-09 (ADR-038) found the torso yaw under-resolved and proposed taking MAGNITUDE from 2-D
  shoulder foreshortening and SIGN from depth. F-10 tested that against ground truth: a real subject at
  physically self-evident headings (0 deg square to the lens; +-90 deg shoulders edge-on), 2520 labelled
  frames at 1.44 m, shipping sidecar unmodified.
- **Decision:** **NO-GO for `hybrid = sign(yaw3D) * abs(yaw2D)` as specified. Nothing implemented.**
  Retain V5. The magnitude half is recorded as VALIDATED so the next attempt starts from it.
- **Evidence — the proposal splits cleanly in two:**
  - **MAGNITUDE, validated 4.5x.** yaw2D **MAE 5.90 deg** vs production yaw3D **26.70 deg** (RMSE 9.28 vs
    43.26). Per-block yaw3D medians show a structured collapse, not noise: **7.7 deg when the truth is
    +90**, **180.0 deg when the truth is -90**, and **21.1 deg at the first 0 deg block**. The shoulder
    pixel span independently confirms the poses (60-64 px face-on -> 10.4 px at +90 -> **1.4 px** at -90).
  - **SIGN, refuted.** **78.49 %** accuracy over 860 frames; **89.52 %** excluding the +-180 wrap;
    **63.45 %** at |heading| >= 90. Confusion: true+ 486/486 correct, **true- only 189/374 (50.5 %)** --
    for a left turn the depth sign is a coin flip. yaw3D reports positive on 78 % of frames regardless of
    truth, i.e. a biased constant rather than a sign detector.
  - Hybrid with a wrap guard: signed MAE 13.09 deg vs production 61.08 deg -- a 4.7x gain, but still 2.2x
    worse than the magnitude alone, and every bit of that gap is the sign.
- **Criteria:** 1 (resolution) PASS, 2 (sign reliable) **FAIL**, 3 (MAE/RMSE) PASS, 4 (no sign flips)
  **FAIL**, 5 (real-time) PASS, 6/7 (V5, ARM V2) untouched. The brief's rule on 2/4 failing is NO-GO.
- **Next source to try, for the SIGN only:** the **nose (or any face keypoint) relative to the shoulder
  midpoint in the image** -- when a subject turns right the nose shifts toward the left shoulder, which is
  1 bit from a 2-D signal with no disparity quantisation. **Untestable here only because neither
  `audit_log.jsonl` nor `sender_log.jsonl` records the nose**; adding it is a one-line diagnostic change
  and would make this testable against THE CAPTURE ALREADY RECORDED, with no new subject time.
- **Consequence for V5:** unchanged and still correct. V5 reproduces its input with gain 1.000; F-10 now
  shows with ground truth that the input is wrong by 26.70 deg on average and catastrophic at +-90 deg.
  **The torso problem is entirely upstream of the retarget.**
- **Limits:** one distance (1.44 m), one subject, one room; +-45 deg headings were eyeballed; the
  torso-twist blocks were compromised by protocol confusion and carry no conclusion.
- **Evidence:** `docs/F10_TORSO_YAW_GROUND_TRUTH_2026-09-10.md`; `oak_v4_evidence/f10_near_analysis.txt`.

## ADR-040 — F-11: the 2-D face sign measures HEAD yaw, not torso yaw — NO-GO

- **Context:** F-10 (ADR-039) validated the 2-D foreshortening MAGNITUDE (MAE 5.90 deg vs production
  26.70 deg) and refuted the depth SIGN. F-11 tested whether a 2-D face/shoulder relation supplies it.
- **Decision:** **NO-GO. V6 not implemented. No production behaviour changed.** Do not pursue any other
  face landmark either -- nose, eyes and ears sit on the same rigid body and fail identically.
- **The F-10 archive could not answer it** (`AUDIT_JOINTS` starts at COCO 5; face is 0-4; no RGB kept),
  so a diagnostic-only logging addition was made -- `AUDIT_FACE_JOINTS` plus a 5-entry loop inside
  `if audit_f is not None:`, kept OUT of `AUDIT_JOINTS` so the depthQuality aggregate is unchanged --
  and a new 11-block capture recorded (1457 labelled frames, 4 of them head/torso DECOUPLING blocks
  added specifically to test the predicted failure mode).
- **Result — the decoupling test is decisive.** A head-only turn (torso square, head +-45) produces a
  LARGER signal than a genuine 45 deg torso turn:
  | signal | true torso +-45 | false: head-only | ratio |
  | nose offset B | 0.1472 | **0.3369** | **2.3x** |
  | ear asymmetry | 0.0355 | **0.2745** | **7.7x** |
  No threshold separates them: the false signal is on the wrong side of the true one. And ambiguity is
  not the issue -- |B| is below 2x its frontal value on only 3.7 % of turned frames. **The signal is
  confident and wrong**, which is worse than uncertain.
- **Static accuracy, for the record:** nose B **87.84 %** overall (positive **75.39 %**, negative 100 %);
  ear asymmetry **97.49 %**. At +-45 deg B is 100 %; at **true +90 deg it is 53.33 % -- chance** (a
  normalisation failure: the shoulder span collapses to a few px so B = A/span divides by ~0).
  Confusion is systematically one-directional: all 63 errors are positive turns read as negative.
- **Hybrid is worse than what it replaces:** `sign(faceB)*|yaw2D|` signed MAE **31.60 deg** vs the
  depth-sign hybrid's 13.09 deg, because its errors land at +-90 where each costs ~180 deg (p95 166.8).
- **What did work, and why it misled:** with the body turned and the face on the camera the sign IS
  recovered (99.3 % / 100 %) -- because the SHOULDER MIDPOINT shifts under foreshortening, not because
  the nose points anywhere. The measurement is a difference between two independently-moving things.
- **Next, all offline-testable on captures already in hand (no new subject time):** (1) temporal sign
  continuity seeded at a known-frontal pose -- |yaw2D| is accurate and its sign can only change through
  zero; (2) hip-line depth sign -- the hips do not rotate with the head; (3) shoulder-occlusion ordering.
- **Evidence:** `docs/F11_FACE_SIGN_TORSO_YAW_2026-09-10.md`; `oak_v4_evidence/f11_gt_score.txt`.

## ADR-041 — F-12: temporal sign continuity cannot supply the torso-yaw sign — NO-GO

- **Context:** F-10 validated the 2-D yaw MAGNITUDE (MAE 5.90 deg vs 26.70) and refuted the depth SIGN;
  F-11 (ADR-040) refuted the face SIGN (it measures head yaw). F-12 tested temporal continuity: seed a
  sign at a known-frontal pose, hold it, and flip only at genuine zero crossings.
- **Decision:** **NO-GO. Nothing implemented. No production or diagnostic file changed.**
- **Root reason — the information is absent, not merely noisy.** For a subject at +45 returning to 0,
  `|yaw2D|` traces 45 -> 0; whether they then go to -45 or back to +45, it traces 0 -> 45 IDENTICALLY.
  The two futures are the same signal, so no causal filter over `|yaw2D|` can separate them.
- **The parameter sweep proves it empirically.** Static sign accuracy for algorithm C swings by up to
  **75.4 percentage points from a single +-1 step in the debounce count**, hitting **0.00 %** at
  (Z=3, N=4) and **96.01 %** at (Z=8, N=4) whose neighbours read 79.48 % and 57.86 %. That is the flip
  COUNT accidentally aligning with the protocol's alternation, not a tuning optimum.
- **A methodological trap, recorded so it is not repeated:** every designed zero visit in every capture
  is followed by a REVERSAL. So the trivial rule "flip at every zero dip" scores ~100 % on this data by
  construction, and no parameter search over it means anything. The discriminating case -- zero, then
  back to the SAME side -- has never been recorded.
- **Other measured failures:** sign flips **2-4 times per 10 s with the torso genuinely at 0 deg** (the
  commonest pose in a mirror app); flip rate in motion is **0.40-1.01/s** against a subject reversing at
  most ~0.5/s. Explicitly NOT the problem: false crossings from noise (2.07-2.51 % of held frames) and
  head dependence (|yaw2D| uses only the shoulder span, confirmed on the F-11 decoupling blocks).
- **A separate finding that outlives the sign question:** `|yaw2D| = acos(...)` is **bounded to [0,90]**,
  so a 180 deg turn and a 90-and-back produce the SAME magnitude trace (m_180 measured max 83.7 deg).
  **The magnitude estimator itself is ambiguous beyond 90 deg** -- even a perfect sign source could not
  distinguish a 120 deg turn from a 60 deg one. If beyond-90 tracking is needed, F-10's magnitude needs
  revisiting too.
- **Operator error, disclosed:** the F-11 capture ran with `--distance near` and OVERWROTE
  `f10_gt_marks_near.json`, destroying the F-10 block windows. They were reconstructed by a two-parameter
  alignment search validated against previously-published per-block medians (all 7 static blocks within
  3.8 deg, stretch 1.000) and written to `f10_gt_marks_near_RECOVERED.json`, flagged `"RECOVERED": true`.
- **Next:** the **hip-line depth sign** is the one remaining untested candidate and the only one that does
  not try to recover the sign after the magnitude estimator discarded it -- the hips do not rotate with
  the head, and sign is 1 bit rather than a magnitude. Offline-testable on captures already in hand.
- **Evidence:** `docs/F12_TEMPORAL_SIGN_TORSO_YAW_2026-09-10.md`; `oak_v4_evidence/f12_*.txt`.

---

## ADR-042 — F-13: the hip-line depth sign is a coin flip — NO-GO; and the SHOULDER sign was rejected for the wrong reason

- **Context:** F-10 validated the 2-D yaw MAGNITUDE and refuted `sign(yaw3D)`; ADR-040 refuted the face
  sign; ADR-041 refuted temporal continuity. F-13 tested the last cheap candidate: the hips, which do
  not rotate with the head. Offline only, on the existing F-10 + F-11 ground-truth captures.
- **Decision:** **NO-GO on the hips. Nothing implemented. No production file and no diagnostic file
  changed** (the hip depth was already logged by the F-08 audit, so no new logging was needed).
- **All ten acceptance criteria fail.** Over ALL turned frames the best candidate scores **40.61 %**;
  conditioned on a sign existing it reaches only 70.51 % on **32.9 % coverage**. Per direction:
  +45 **63.21 %**, -45 86.67 %, **+90 no signal on any frame**, -90 **10.00 %**.
- **The block, not the frame, is the unit of evidence.** Over 10 held blocks the hip sign is
  **4 correct / 2 wrong / 4 no-signal**, and two of the four "correct" win on 41/111 and 1/150 frames.
- **Root reason is geometric and quantitative.** Depth is quantised to integer disparity with
  **f*B = 21,216 mm.px**; at the subject's 1326 mm one step is **78-88 mm**. The measured hip separation
  is **130 mm**, so a 45 deg turn is worth **1.1 disparity steps** and dz is **exactly 0 on 67.1 %** of
  turned frames. At +-90 the far hip is **occluded by the near one** (pixel span collapses 43.9 -> 10.6)
  and dz collapses to 0 **with zero spread** -- while the pipeline still reports both joints `measured`
  100 % of the time. **A confidence gate cannot catch this; any future sign gate must be geometric.**
- **The hybrid is worse than emitting no sign at all:** `sign(hipDz)*|yaw2D|` gives **MAE 72.46 deg**
  (wrong sign 50.61 %) against **8.34 deg** for the unsigned magnitude on the same population, and worse
  than production `yaw3D` (61.08). No threshold helps -- accuracy *falls* to 4.55 % at |dz| > 150 mm.
- **THE CONSEQUENTIAL FINDING, from the mandated hip-vs-shoulder comparison.** The same harness on the
  **shoulder** line gets the sign right on **9 of 10 blocks, 0 wrong** (frame level 100.00 % where
  defined, 87.65 % over all turned frames). F-10 rejected "depth sign" at 78.49 %, but F-10 tested
  **`sign(yaw3D)`** -- an `atan2(dx, dz)` composition that **WRAPS** when the shoulder line goes edge-on
  (pixel span 40.5 -> **9.6** px at +-90). A raw depth difference does not wrap. F-10's own report
  already recorded the symptom without naming the cause: "excluding the +-180 wrap: 89.52 %".
  **The evidence is that the sign was destroyed by the composition, not missing from the measurement.**
  The shoulders are ~3x wider than the hips, so the same turn clears ~3x the quantisation step --
  choosing the hips traded away the only thing making the measurement work.
- **This is a lead, NOT a result. Do not implement it.** It rests on 10 held blocks, one subject, one
  distance, one session; motion flip rate is still 0.40-1.61/s against a ~0.5/s physical bound; -90 raw
  coverage is 3.7 % (only the smoothed variant covers it); and at a true 0 it emits a confident
  non-flipping sign with |dz| p50 up to 136 mm. Validating it is **F-14**.
- **Consequence for the F-13 brief's "final rule".** That rule (stop signing `yaw2D`, redesign the
  representation) is predicated on *all* cheap sign sources being unreliable. **That premise is now in
  doubt** and the redesign should not start until F-14 settles the shoulder sign.
- **Unchanged limit:** `|yaw2D|` is still bounded to [0,90] (ADR-041), so even a perfect sign solves
  only the sign half. Sign and beyond-90 magnitude are separate work items.
- **Methodological error, disclosed:** the first run of this analysis treated a dz of exactly zero as a
  NEGATIVE sign, which made all-zero blocks look like perfectly stable signs with zero flips and quoted
  70.51 % while silently dropping 67 % of frames. Fixed with a three-valued sign (+1/-1/undefined) and
  explicit coverage columns throughout. Evidence: `oak_v4_evidence/f13_hip_sign.txt`,
  `docs/F13_HIP_DEPTH_SIGN_TORSO_YAW_2026-09-10.md`.

---

## ADR-043 — F-14: raw shoulder Δz is NOT an independent sign source; a wrap guard on the existing yaw3D is equivalent — CONDITIONAL

- **Context:** F-13 (ADR-042) closed the hip sign and raised an incidental lead: raw shoulder depth
  difference was correct on 9/10 held blocks while F-10 had rejected "depth sign" at 78.49 %. F-14 tested
  whether the sign survives in the raw depth **independent of** `yaw3D = atan2(dx, dz)`, which wraps as
  the shoulder line goes edge-on. Offline only, on the existing F-10 + F-11 ground-truth captures.
- **Decision:** **CONDITIONAL. Nothing implemented. No production file and no diagnostic file changed.**
- **THE HYPOTHESIS IS REFUTED.** Over the 854 frames where raw dz is defined, it rescues **0 frames**
  that `yaw3D` gets wrong (confusion table 853/0/1/0), and where `yaw3D` wraps raw dz is **also silent on
  89.5 %** of those frames. Raw dz only *appears* better because it **ABSTAINS** exactly where `yaw3D`
  errs. Abstention is not information.
- **The decisive experiment.** Brief §14 compares a HELD depth sign against an UNHELD `yaw3D`, which is
  unfair -- F-10 itself proposed a wrap guard. Giving every estimator the same hold:
  `sign(emitted dz)` = **8.34 deg MAE / 9.96 RMSE / 0.00 % wrong**, `sign(yaw3D)+wrap guard` = **8.34 /
  9.96 / 0.00 %**, perfect-sign ceiling = **8.34 / 9.96**. Identical to the decimal on main, main+twist
  and twist-only populations. **They are the same signal, and both already sit at the ceiling.**
- **CENTRAL ANSWER: NO** -- the OAK-D shoulder measurement does not contain a usable sign *independent
  of* the failing composition. **Corollary: no new sign source is needed.** The smallest possible V6 is a
  **wrap guard** (hold the last sign while `|yaw3D| > 150`), not a new signal.
- **What DID hold, and matters:** +-45 separates at **AUC 0.000** -- the +45 and -45 raw-dz distributions
  are completely disjoint (-252..-78 vs +89..+217) with 0.0 %/0.3 % zeros. Block level 9 correct / 0 wrong
  / 1 no-signal, replicating F-13. Smoothing never inverted a correct raw sign (0/854) and its added
  coverage was **100 %** correct -- legitimate dithering across a held pose, not an artefact.
- **The true-zero result vindicates the sign x magnitude decomposition.** A square torso emits a
  confident sign on up to 100 % of frames with |dz| up to 136 mm, but the resulting avatar error is only
  **1.60 deg MAE** (vs production `yaw3D`'s **8.72**), because `|yaw2D|` is 0-4 deg there and
  `sign * ~0 ~ 0`. **A wrong sign only costs where the magnitude is large.**
- **Why CONDITIONAL, not PASS.** Four acceptance criteria fail: (7) raw dz does not retain a sign where
  `yaw3D` wraps; (8) coverage is **3.7 % at -90** (11 frames of 294); (11) it does not improve on a
  guarded `yaw3D`; (12) **session dependence** -- two captures of the same subject, same distance, same
  day differ by **27 points** on the same estimator (F-10 70.83 % vs F-11 98.04 %; raw-dz coverage 49.0 %
  vs 74.4 %). That gap is unexplained and is the gating unknown.
- **+-90 fails for a GEOMETRIC reason.** The shoulder line collapses from 67.6 px front-on to **7.0 px at
  -90**; two 5x5 depth windows 7 px apart on an edge-on body sample the same surface, and **dsd = 0.0**
  confirms a perfectly uniform window -- 96.3 % of -90 frames give dz exactly 0. The geometry predicts
  4.31 disparity steps at 90 deg (shoulder width 359 mm vs an 83 mm step, 2.76x the hips) and the
  measurement delivers nothing, because the model assumes both shoulders stay independently visible.
  The pipeline still reports both as `measured` 100 % of the time -- **a confidence gate cannot catch
  this; any sign gate must be geometric.**
- **NEXT ACTION -- the first genuinely justified new capture since F-09:** one capture, a **second
  subject**, same protocol, same ~1.5 m, scored with `f14_shoulder_depth_sign.py` unchanged. If the
  session gap is subject-specific the path closes; if clean, implement the wrap guard only.
- **Unchanged limit:** `|yaw2D|` is still bounded to [0,90] (ADR-041). A working sign yields -90..+90
  only. SIGN and MAGNITUDE-BEYOND-90 remain separate roadmap items.
- Evidence: `oak_v4_evidence/f14_shoulder_sign.txt`,
  `docs/F14_SHOULDER_DEPTH_SIGN_TORSO_YAW_2026-09-10.md`. Roadmap restructured as an evidence-driven
  decision tree with an explicit NEXT PATH.

- **CORRECTION (F-15 preparation, 2026-09-10) — the "session dependence" blocker is mis-stated above.**
  Controlling for heading, the two captures agree EXACTLY on three of four headings: +45 **100.0 %**
  vs **100.0 %**, -45 **100.0 %** vs **100.0 %**, +90 **100.0 %** vs **100.0 %**, and -90 **0.0 %** vs
  **89.1 %**. All the variance is ONE BLOCK (`F-10 h_m90`). The 70.83/98.04 pair was frame-weighted
  over different block mixes (F-10 is 56.9 % +-90 frames, F-11 34.6 %), which amplified a single-pose
  failure into an apparent session-wide effect. Two candidate causes were tested and REFUTED:
  **distance** (F-11 was 13 cm closer, 101 mm -> 78 mm step, but within-capture binning shows FARTHER
  frames scoring better -- the bins are confounded with heading because turning changes `hipZ`) and
  **confidence/depth coverage** (identical: 25/25 valid px, conf p10 0.502 both). The remaining
  uncertainty is therefore **the -90 pose**, not the session. F-15 is retargeted accordingly; the
  CONDITIONAL verdict and the central "not an independent sign source" finding are unchanged.

---

## ADR-044 — F-15: the torso-yaw sign is validated to +-60 deg on a second subject and unsolvable at +-90 — CONDITIONAL

- **Context:** ADR-043 left the wrap-guarded `yaw3D` as the smallest possible V6, blocked on one
  uncertainty. That uncertainty was re-characterised during F-15 prep (see the CORRECTION appended to
  ADR-043): it was never session dependence, it was **the -90 pose**. F-15 captured a **second
  subject** to test whether it depends on body geometry. New capture, 28 blocks, 2895 labelled frames,
  1.27 m held to a 13 cm drift, `--headings full` so +-30 and +-60 are held for the first time.
- **Decision:** **CONDITIONAL. Nothing implemented. No production file changed.**
- **THE RESULT IS BOUNDED, NOT GENERAL.** On the eight label-validated whole-body blocks:
  **|heading| <= 60 -> 6 correct, 0 wrong, 0 no-signal** (perfect on a body the estimator had never
  seen); **|heading| == 90 -> 0 correct, 1 WRONG, 1 NO-SIGNAL**.
- **Two criteria pass cleanly.** No subject-specific **sign inversion** (all five estimators infer
  identical polarity on all three captures), and raw shoulder dz again supplies **1 true rescue in 318
  defined frames, 0 at +-90** -- F-14's "not an independent sign source" **replicates**.
- **Two criteria fail.** Macro accuracy **97.28 % (F-11) -> 37.85 %**, and the guarded hybrid MAE
  **5.13 -> 45.29 deg** -- past the brief's 15-point collapse trigger. A strict reading of the brief
  returns NO-GO; this ADR records CONDITIONAL because the failure is bounded, fully explained and
  confined to one extreme of the range, and the brief's NO-GO branch (redesign the representation)
  would over-react to a geometric limit.
- **The magnitude travelled; the sign did not.** The perfect-sign CEILING moved only **5.13 -> 9.64
  deg** across subjects, so the 2-D foreshortening magnitude generalises. The entire loss is the sign
  at +-90.
- **Cause identified: SHOULDER PIXEL SPAN.** 63.8 px front-on, 41 px at +-45, **17.3 px at +60**,
  **7-8 px at +-90**. Two 5x5 depth windows that close together on an edge-on torso sample ONE
  surface, so dz has nothing to compare and `atan2` wraps. **Distance was explicitly tested and
  REFUTED** as the cause during prep (within-capture binning shows FARTHER frames scoring better; the
  bins are confounded with heading because turning changes `hipZ`). Confidence and depth coverage were
  identical between captures.
- **+-90 is now a three-observation failure**: subject A fails it in F-10, passes in F-11, subject B
  fails both sides in F-15 despite a controlled distance and a genuine 83 deg turn.
- **The sign-source search is CLOSED.** Five sources tested and rejected: depth-via-`yaw3D` (F-10),
  face (F-11), temporal (F-12), hips (F-13), raw shoulder dz (F-14, replicated here). **Do not write
  another one.** If +-90 is required, the fix is the MEASUREMENT (closer distance, higher resolution,
  sub-pixel disparity, wider baseline, second viewpoint), not another estimator.
- **The guard does real work but is not free:** it removes **83.5 %** of the wrap errors it fires on,
  and carries the +60 block to 100 % where raw coverage has already fallen to 71.3 %. But it held
  **30.9 %** of frames with a longest hold of **94 frames (~3 s)**. A 3-second stale sign is a visible
  artefact and likely needs a decay before shipping.
- **True-zero replicates F-14:** an arbitrary sign at a square torso costs **0.19-1.11 deg**, because
  `|yaw2D| ~ 0` there. The `sign x magnitude` decomposition remains sound.
- **NEXT ACTION is a PRODUCT decision, not an engineering one:** *is a +-60 deg torso-yaw range
  acceptable for the mirror?* If yes, implement the wrap guard alone plus an explicit documented range
  limit, then validate live. If no, the work is on the measurement, not the estimator.
- **Unresolved and disclosed:** `dc_body45L_face0` is confidently opposite on 110/110 frames for every
  estimator, and the nose-based label audit cannot adjudicate it (that block deliberately decouples
  head from body). Either subject B turned the wrong way on the protocol's most confusing instruction,
  or four independent extractions failed identically. Both populations (with and without it) are
  reported.
- **Unchanged limit:** `|yaw2D|` is still bounded to [0,90] (ADR-041). SIGN and MAGNITUDE-BEYOND-90
  remain separate problems; F-15 addresses only the first.
- Evidence: `oak_v4_evidence/f15_cross_session.txt`, `pipeline_logs_f15/`,
  `oak_v4_evidence/f15/f10_gt_marks_near.json`,
  `docs/F15_CROSS_SESSION_TORSO_YAW_VALIDATION_2026-09-10.md`.
- **Tooling note:** system Python carries **depthai 3.7.1** against a `>=2.32,<3` pin, and the sidecar
  aborts at startup on 3.x (`PresetMode.HIGH_DENSITY` renamed). F-15 was captured from the project
  `.venv` (2.32.0.0) -- the same depth stack as F-10/F-11. An overwrite guard was added to
  `f10_gt_capture.py` so a repeat capture can no longer destroy an earlier run's marks.

---

## ADR-045 — V6: ship the wrap guard and an explicit ±60° envelope; the +-90 freeze is now the open defect — CONDITIONAL

- **Context:** ADR-044 closed the sign-source search and authorised the smallest possible V6 within a
  documented envelope. V6 implements exactly that and nothing else.
- **Decision:** **IMPLEMENTED, CONDITIONAL.** One new file (`Runtime/Retargeting/TorsoYawGuard.cs`)
  plus **18 lines** in `KalidokitControlRigDriver`: four fields, one call site, one reset. `TrunkGate`,
  the V5 composition and weights, `UpperChest`, `DampYaw` and every ADR-027 constant, `ArmAimSolver`,
  Arm V2, P1-1/P1-2/P1-3, F-08, scene defaults and UDP semantics are **unchanged** (`git diff` verified).
- **Policy, both thresholds INHERITED not invented.** `|yaw| > 150` -> HOLD the last valid pair
  (F-10's own guard, scored in F-14, replicated in F-15). `|yaw| > 60` -> **CLAMP** (F-15's measured
  envelope). States: `Valid` / `WrapGuarded` / `OutOfRange` / `NoValue`. A test asserts both constants,
  so changing either breaks the build rather than silently invalidating the evidence.
- **Clamp rather than hold out of range**, deliberately: clamping is continuous (measured max step
  across the boundary 0.5 deg on a 0.5 deg sweep), deterministic, never extrapolates through the
  unreliable region, and keeps the SIGN that F-15 validated while dropping only the MAGNITUDE it did
  not. The held value is stored UNCLAMPED so returning into range is instant. Hip and shoulder are
  guarded TOGETHER, as in TrunkGate, because V5 composes their difference.
- **What it fixes, measured on 20,783 real F-15 frames through the shipped C#:** worst frame-to-frame
  torso step **359.9 -> 38.4 deg**; steps > 30 deg **32 -> 1**; the +-180 flip never reaches the
  avatar. The 38.4 deg is upstream of the existing 140 deg/s conditioner (5.5 deg/frame at 25.5 fps),
  so the avatar cannot snap. The guard is **INERT during normal movement** -- 0 holds on the slow,
  normal, fast and reversal blocks -- firing only at +-90 and in the 180 deg turn.
- **Tests: 145/145 EditMode, 51 new.** The 94-test V5/Arm baseline is intact. An initial run showed 2
  failures that were a **harness artefact** (methods carrying both a bare `[Test]` and `[TestCase]`s;
  the reflection runner invented a zero-arg call) -- product code was never at fault.
- **WHY CONDITIONAL -- two measured defects, neither fixed by more implementation.**
  **(1) Worst hold 12.33 s** (314 frames), against the ~3 s flagged in F-15 -- 4x worse. Hold rate is
  only 4.31 % and the average run 3.19 s, but a subject who turns past +-90 and stays there freezes
  the avatar's torso for as long as they hold it. **Deliberately NOT damped away** (V6 brief section
  6): damping would hide staleness rather than fix it, and the fix is a policy choice.
  **(2) The negative-side sign is weaker than F-15 claimed.** F-15 scored blocks by MAJORITY, which
  marks a block CORRECT at >50 % agreement. `h_m45` is **58.6 % at frame level** -- a coin flip -- and
  F-15 recorded it CORRECT. **This ADR corrects that:** the +-60 envelope is solid on the POSITIVE
  side (100 % every block, every frame) and weaker on the negative side.
- **The root cause of (2) is NOT V6.** On `h_m45` the raw Kalidokit `yaw3D` reads **-0.1 deg** while
  F-15 measured `|yaw2D| = 58.6 deg`: the depth-derived yaw does not register that turn at all -- the
  F-09 under-resolution root cause. **V6 cannot invent a sign for a measurement that reads zero**, and
  correctly does not try.
- **+-90 is not solved and is not claimed to be.** The guard converts a wrong-and-flipping torso into
  a wrong-and-stationary one; `h_m90` holds an incorrect sign for its whole block. That is the
  documented limit.
- **`dc_body45L_face0` update.** F-15 left it unresolved. V6's replay adds a second independent
  signal: raw `yaw3D` reads **+47.3 deg** for a block labelled -45, agreeing with the nose offset
  (-0.284, the same side as every +45 block) while `|yaw2D|` confirms a genuine ~50 deg turn. Two
  independent signals now say the subject turned RIGHT during a block labelled LEFT. Still not proof;
  the block remains in every table.
- **Validation method, disclosed:** V6 was validated by replaying the **shipped** `TorsoYawGuard` over
  the **real** F-15 frames with ground-truth labels -- stronger than a fresh live session for
  measurement, but it stops at the guard's output. **A live end-to-end Play-mode session is OWED** and
  is the next action; watch specifically for the 12.33 s freeze.
- **Unchanged limits:** the magnitude is still under-resolved (F-09), and `|yaw2D|` is still bounded
  to [0,90] (ADR-041). V6 changed WHICH yaw reaches the composition, not how well it is measured, and
  it does not address beyond-90 magnitude.
- Evidence: `oak_v4_evidence/f15/v6_guard_replay.txt`,
  `docs/V6_TORSO_YAW_IMPLEMENTATION_VALIDATION_2026-09-10.md`.

- **LIVE SESSION RESULT (added same day, OAK-D + Play mode, subject A at 1.33 m, 266 s / 58,605
  avatar frames sampled off the rendered bones at 220 Hz).**
  **The guard is confirmed:** worst frame-to-frame change in the RENDERED torso yaw is **2.45 deg**
  with **ZERO** steps above 10 deg, against a V5 baseline of 359.9 deg and 32 steps over 30 deg. No
  snap is reachable by the viewer. States: Valid 89.33 %, OutOfRange 8.32 %, WrapGuarded 2.35 %.
  Worst hold **10.7 s** live, independently predicting-and-confirming the offline replay's 12.33 s.
- **THE LIVE SESSION ALSO ANSWERED THE PRODUCT QUESTION, AND THE ANSWER IS NO.** With the subject
  standing SQUARE -- confirmed independently by a 66.9 px shoulder span against a ~64 px front-on
  value -- the avatar's torso sits at **-49.6 deg, stable to 0.2 deg over 150 consecutive frames**.
  Measured cause on the same frames: raw shoulder dz **-101 mm** against an **89 mm** disparity step
  at 1.33 m. The two shoulders land on ADJACENT DISPARITY RUNGS -- the smallest non-zero difference
  the sensor can express -- and that single step becomes ~50 deg of commanded yaw. It is not noise
  and no filter can remove it.
- **This is NOT a V6 defect and V6 cannot fix it.** -49.6 deg is INSIDE the +-60 envelope, so the
  guard correctly passes it through as `Valid`. V6 governs the WRAP and the RANGE, not how the yaw is
  MEASURED. This is the F-09 under-resolution root cause (ADR-038), now shown to be the DOMINANT
  VISIBLE DEFECT -- far more so than the freeze this task was worried about. It also explains the
  replay oddity of `yaw3D` reading -0.1 deg for a real 58.6 deg turn: the magnitude is decoupled from
  the truth in BOTH directions.
- **CONSEQUENCE FOR THE ROADMAP: stop work on the torso-yaw ESTIMATOR.** Five sign sources are closed
  (F-10..F-15) and this is a MAGNITUDE failure, not a sign one. Everything downstream -- V5
  composition, the V6 guard, the +-60 envelope -- is correct and can stay as it is. The next work is
  the MEASUREMENT: closer working distance, higher RGB/depth resolution, sub-pixel disparity, wider
  stereo baseline, or a second viewpoint.
- **Diagnostic defect found by the session (labelling only, no behaviour change):** a frame held
  because `TrunkGate` reported `!Fresh` is reported as state `Valid` with a growing `HoldStreak`, so
  the live timeline shows 2,356-frame holds under a "Valid 100 %" label. The yaw is held correctly
  either way; the enum should distinguish it. Fix in a follow-up.
- Live evidence: `oak_v4_evidence/v6_live_session.txt`, `oak_v4_evidence/v6_live_trace.jsonl`.

## ADR-046 — F-16: the OAK-D can measure torso yaw, but only at 0.80 m (2026-09-11)

**Status:** accepted (investigation closed). **Evidence only — no production file was modified.**

**Question.** V6 established that the downstream guard is correct while the upstream measurement can
be catastrophically wrong. F-16 asked whether the current OAK-D stereo configuration can physically
resolve shoulder depth well enough for production torso yaw at the intended distance.

**Method.** 12 stereo configurations (sub-pixel 1/8 and 1/32, mono 1280x800, RGB 1280x800,
HIGH_ACCURACY, extended disparity, unaligned depth) measured on a static scene and with a subject
square at 7 distances from 0.80 m to 2.00 m, plus a rotation set and a 180-degree bias test. The
first distance sweep was DISCARDED and re-run: the subject cannot judge 0.80 m from 1.33 m and
cannot see the capture console, so positions drifted to 1.06-1.45 m. The rebuilt protocol estimates
range from SHOULDER PIXEL SPAN (an RGB/pose quantity, independent of the depth under test), shows it
on a full-screen HUD, and triggers recording automatically inside +-6 cm.

**Confirmed.**
- The quantisation model is exact. EEPROM gives baseline **75.0 mm** and fx 282.995 px, so
  **f*B = 21,224.6 mm*px** against the **21,216** empirically fitted through F-09..F-15 — a ratio of
  **1.0004**. Observed/theory ran **0.94-1.26** across 9 configurations x 8 range bands.
- Production resolves **3 distinct depth values in a 200 mm window** at 1.2 m.
- **Sub-pixel 1/8 is close to free and worth having on its own merits:** the 1.33 m step falls
  **83.5 -> 12.0 mm** and frame-to-frame torso instability **15.90 -> 3.59 deg**, at **+2 ms latency
  and no FPS cost**. At 0.80 m it takes instability **5.21 -> 1.69 deg**.

**Refuted — and this was the working hypothesis going in.** Quantisation is NOT what puts a square
user's torso at ~14 deg. A **28x finer ladder (83.5 -> 3.0 mm) moved the error 14.68 -> 13.52 deg**,
and all eight configurations at 1.33 m land between **10.55 and 15.61 deg**. Higher RGB resolution
changes it by 0.9 deg; HIGH_ACCURACY does not alter the ladder at all and costs 11 points of
coverage; extended disparity halves the frame rate for no gain.

**Found instead.** A **+0.75 px systematic disparity MATCHING error** between the two shoulder
windows (median over 7 distances, spread 0.38 px). Sub-pixel subdivides a match; it cannot repair
one that is a whole pixel wrong — which is exactly why every sub-pixel configuration reproduces the
same offset. Because it is a fixed PIXEL error its metric cost scales as Z^2, so the angular cost
runs **0.51 deg at 0.80 m -> 14.76 at 1.33 m -> 24.87 at 2.00 m**.
It is **not the subject's posture** (a 180-degree turn did not flip the sign, and the same subject
reads 0.00-5.10 deg at 0.80 m) and **not a left/right sensor bias** (the farther shoulder kept its
ANATOMICAL side while swapping IMAGE side). It is consistent with an **occlusion-edge artefact**: the
farther-reading shoulder carried the wider depth window on **69.5 % / 75.2 %** of frames in two of
three datasets — support, not proof; the third split 50/50.

**Decision.** **CONDITIONALLY FEASIBLE.** At **0.80 m four of the five configurations tested pass**
the proposed +-5 deg median / +-10 deg p95 acceptance — **production included, at median 0.00 deg /
p95 5.21** — and **all five are within +-10 deg on 100 % of frames**; the fifth (RGB 1280x800 +
mono 1280x800 + sub-pixel) misses the 5 deg median by 0.10 deg. At **1.00 m and beyond none does**. Recommendation **B (change working
distance)**, with **D (sub-pixel 1/8)** as a required companion rather than an alternative; E and C
are measured and rejected; F (wider baseline) is ranked third and is PREDICTED, not measured.

**The constraint that makes this a product decision, not an engineering one.** Torso-yaw accuracy
requires **Z <= 0.80 m**. Full-body framing requires **Z >= 1.24 m** (from this device's measured
70.2 deg vertical FOV against a 1.75 m subject). **The two windows do not overlap on this camera.**

**Not changed.** V5 composition, V6 `TorsoYawGuard`, `TrunkGate`, `DampYaw`, Arm V2, P1-1/2/3, the
F-08 sampler, UDP semantics, and the shipping stereo configuration are all untouched. Enabling
sub-pixel in production is a separate task requiring its own live validation.

**Also measured.** +-90 degrees gets **worse** with a better measurement, not better: shoulder span
collapses 71 -> 13.8-16.9 px and the yaw baseline |dx| 345 -> 48.7-55.8 mm, so `left90` reads
+28.6 deg with a **50.6 deg standard deviation** and `right90` reads **+7.5 deg, the wrong sign**.
Sign was correct on **6/6** of the +-30/+-45/+-60 blocks. Magnitude saturates: commanded
-30/-45/-60 read **+40.2 / +40.2 / +48.1 deg**.

**Open.** V6's 49.6 deg needed a **154.5 mm** yaw baseline; F-16 never observed below **320 mm**, so
the `|dx|` collapse seen in the V6 session is a separate, uncharacterised defect.

**Evidence:** `docs/F16_OAKD_TORSO_DEPTH_RESOLUTION_2026-09-11.md`; raw data and analyses under
`oak_v4_evidence/f16/` (`device_probe.txt`, `config_sweep_scene1.txt`, `autosweep_baseline_1.jsonl`,
`autosweep_sub3.jsonl`, `pose_sub3_133.jsonl`, `cfgsweep_133.jsonl`, `cfgsweep_080.jsonl`,
`consolidated.txt`, `edge_check.txt`).

## ADR-047 — F-17: a wider stereo baseline will not rescue full-body torso yaw (2026-09-11)

**Status:** accepted (investigation closed). **Evidence only — `git diff -- Runtime/` shows only the
V6 changes.**

**Question.** F-16 ended with a wider stereo baseline as the last untested hardware lever, predicting
that doubling the 75 mm baseline would take the 14.76 deg square-stance error at 1.33 m to ~7.5 deg.
F-17 asked whether that is actually achievable.

**Blocked at the inventory, and this is stated rather than worked around.** Exactly one stereo device
is attached. The board's other two baselines are **37.36 mm and 37.64 mm — narrower, not wider**. The
brief's candidate list (OAK-D-LR, OAK-D-W, custom pair, second camera) could not be populated, so the
per-candidate rotation set, thermal run and CPU profile are marked not-applicable rather than
invented.

**Substitute experiment attempted, and it failed as an instrument.** 402 synchronised CAM_A/B/C frame
sets were captured at 1.24-2.00 m with the F-16 closed-loop HUD protocol and processed offline
through host rectification built from the device's own EEPROM, with ONE shared SGBM matcher so the
baseline was the only variable. **host B-C tracks the device to within 1-8 %**; **host A-C is wrong
by 21-41 %**, a constant ~1.93 px disparity deficit that swamps the few-tens-of-mm dz under test.
Five checks put the fault in the PAIR and not the rig: epipolar |dy| p50 = **0.00 px**; rectified
**cx1 = cx2 exactly**; the same matcher is accurate on B-C; 2x and 3x upsampled matching does not fix
it; all three sockets use the same `Perspective` distortion model. CAM_A is an **IR-cut colour**
sensor against **unfiltered mono** — torso depth coverage **42 % against 88 %**. This hardware cannot
supply a second usable baseline.

**Decisive evidence, from data already in hand.** F-16's 1.33 m configuration sweep held the subject
square through all eight configurations in one continuous session, giving three pairs that differ
**only** by mono resolution — three independent **doublings of f*B**:

| comparison | dz before | dz after | ratio |
|---|--:|--:|--:|
| baseline -> mono800 | 89.0 mm | 95.0 mm | **1.067** (wrong way) |
| sub3 -> mono800_sub3 | 82.2 mm | 65.0 mm | 0.791 |
| sub3_rgb800 -> best | 97.0 mm | 86.0 mm | 0.887 |

Mean **0.915**. A pure PIXEL-domain bias would give **0.50** per doubling; a pure DEPTH-domain bias
**1.00**. **This is Case B: the bias does not follow f*B.** Quantisation halved in all three pairs,
so f*B certainly changed — the bias did not. Projected onto baseline, reaching 5 deg at 1.33 m would
need ~161 m. Even the IDEAL model needs **140 / 161 / 205 / 295 / 364 mm** at 1.24 / 1.33 / 1.50 /
1.80 / 2.00 m, and gives a 150 mm head **4.66 deg at 1.24 m with 0.34 deg of margin, failing from
1.33 m onward**.

**Decision.** **NOT FEASIBLE** for the wider-baseline route; hardware decision **WIDER BASELINE
INSUFFICIENT**. Do not procure a wider-baseline stereo head.

**What F-17 found instead — the lens, not the baseline.** The requirement has a closed form in which
field of view enters twice, because a wider lens lets the subject stand closer AND the requirement
falls as Z^2:

```text
B_required = H_body^2 * dd / ( 2 * h_px * W_shoulder * tan(eps) * tan(vfov/2) )
```

    V-FOV 70.2 deg (this camera) -> Z_min 1.24 m -> B_req 141 mm
    V-FOV 95   deg               -> Z_min 0.80 m -> B_req  58 mm   (BELOW the 75 mm already fitted)

That 0.80 m row is **not** an extrapolation: F-16 measured this camera passing there (median
0.00-3.83 deg, 100 % of frames within +-10 deg). And the camera is already **96.7 deg horizontal x
70.2 deg vertical** — **mounted in portrait that becomes 96.7 deg VERTICAL**, framing a 1.75 m body at
**0.78 m** with the stereo pipeline untouched, since the pair rotates with the body and only the
image needs rotating before the pose model.

**Cost of the portrait orientation, and it is real:** horizontal coverage falls to **1.10 m** against
a ~1.75 m adult arm span, so fully spread arms leave frame.

**Not changed.** V5 composition, V6 `TorsoYawGuard`, `TrunkGate`, `DampYaw`, Arm V2, P1-1/2/3, F-08,
UDP semantics, production thresholds and the shipping stereo configuration are all untouched.

**Honest limitations.** The primary question was **not measured** — no wider-baseline camera existed.
The verdict rests on the ideal model clearing the bar at one distance with 0.34 deg of margin, on
three measured f*B doublings showing the bias does not scale, and on the occlusion mechanism (a wider
baseline makes silhouette occlusion worse) arguing the same way. **A measured counter-example from
real wider-baseline hardware would overturn it**, and the acceptance test is pre-written: <=5 deg
median and <=10 deg p95 at >=1.24 m, square stance, closed-loop HUD protocol. The scaling evidence
comes from RESOLUTION, not BASELINE — the closest available probe, not a substitute. The portrait
recommendation also carries one unmeasured step: rotating the camera puts the stereo baseline
vertical in world terms, so the shoulder line becomes perpendicular to it rather than parallel, and
that changes the shoulder occlusion geometry in a way nothing here has measured.

**Evidence:** `docs/F17_WIDER_STEREO_BASELINE_FEASIBILITY_2026-09-11.md`; data under
`oak_v4_evidence/f17/` (`inventory.txt`, `rig_validation.txt`, `tuning.txt`, `raw/sweep1/` with 402
frame sets, `multibaseline.txt`, `model.txt`).

## ADR-048 — F-18: portrait orientation solves the full-body / torso-yaw conflict (2026-09-11)

**Status:** accepted (investigation closed). **Evidence only — `git diff -- Runtime/` shows only the
V6 changes.**

**Question.** F-16 measured that torso yaw needs <= 0.80 m while full-body framing needs >= 1.24 m,
with no overlap. F-17 measured that a wider stereo baseline cannot close that gap and found the lens,
not the baseline, to be the lever. F-18 tested the cheapest form of that: rotate the EXISTING camera
90 degrees so its 96.7 deg horizontal field of view becomes the vertical one.

**Setup.** The camera was PHYSICALLY rotated; cropping a landscape frame was rejected because it
keeps the same 70.2 deg vertical FOV and would prove nothing. Direction was auto-detected from the
imagery rather than assumed: CCW, body confidence 0.81 vs 0.58, head-above-hips only that way.

**The transformation was verified BEFORE the camera was touched.** Rotating the image alone would
rescale every back-projected X and silently corrupt the yaw triangle; the intrinsics must rotate with
it. Verified: 3-D geometry preserved to **2.8e-17 m**, pixel mapping identical to `cv2.rotate`, FOV
correctly swapping to 96.7 V / 70.2 H.

**Result — the blocker is gone.** At **0.90 m**: square-stance yaw **1.15 deg median, 2.33 deg p95,
frame-to-frame 1.98 deg**, body **full-body-SAFE**, depth quality 0.95. **All four tested distances
pass** (0.78 / 0.80 / 0.90 / 1.00 m) where landscape passed nothing at or beyond 1.24 m. Best single
block 0.73 deg median / 1.64 deg p95.

**V6, observation only, no threshold changed.** The Editor was not running, so `TorsoYawGuard.cs` was
ported line-by-line and the port **verified against V6's own published numbers** before use (896
held, 4.31 %, 11 runs, longest 12.31 s vs the report's 12.33 s). Portrait square blocks: **100 %
Valid, zero wrap, zero out-of-range, zero held.** Longest hold anywhere in the 6,508-sample portrait
stream is **1.11 s** against V6's 12.33 s. Caveat: hipYaw = shoulderYaw and trunkFresh = true, so
hold figures are a lower bound.

**Cost.** The rotation is **0.2584 ms/frame = 0.78 %** of a 33 ms budget. Against the like-for-like
landscape capture at the same stereo config, portrait measured **21 ms faster**; the frequently
quoted "31.2 fps / 24 ms" was a depth-only sweep with no pose model and is not comparable.

**No geometric inversion.** Handedness identical (signed dx **-316.8 mm** portrait vs **-345.4 mm**
landscape, person-left at the larger u in both), square reads **2.89 deg** not 180, and **10 of 12**
left/right pairs separate with opposite signs. The observed polarity difference from F-16 is the
SUBJECT turning the other way for the same prompt (left30 dz **+231 -> -87 mm**), not the camera —
the F-15 rule that commanded heading is not angular truth, again.

**Decision: CONDITIONALLY FEASIBLE. Adopt portrait at a 0.90 m user distance.** Conditions, all
measured:

1. **Arm spread is the new binding constraint.** Rotating swaps which axis gets 96.7 deg and which
   gets 70.2, but the SMALLER FOV always binds the LARGER body dimension. Measured T-pose span
   **~1.18 m** against 1.12 m of horizontal coverage at 0.80 m: clamped at the border at every
   distance up to 0.85 m, tight at 0.90 m, safe only at 1.00 m. Arms-45 (~0.84 m) is safe from
   0.78 m. **The supported interaction envelope is arms-45; T-pose and crouch are outside it.**
2. **Crouch loses the hands** (80.1 % in frame at 0.90 m) — the one failing interaction action.
3. **The occlusion asymmetry got WORSE, not better** — the F-17 open question. Far-wider rose
   23.2 -> 72.7 % at 0.80 m and 85.9 -> 97.5 % at 1.00 m, with far-shoulder window spreads roughly
   doubling. Portrait wins on FRAMING; the F-16 matching error is still underneath. Portrait is
   worse than landscape at 0.80 m (2.36 vs 0.51 deg) and better at 1.00 m (4.57 vs 7.87).
4. **Two sections were NOT performed and are not estimated**: multi-user (no second person) and
   avatar human-quality (needs a live portrait sidecar and the Unity Editor). **The avatar check is
   the most important gap** — every F-18 number is at the landmark level, upstream of the rig.
5. **The mount is not level** — a ~19-32 deg downward pitch is implied by the imagery (height derived
   at ~0.81 m; no tape measurement was supplied). This does not invalidate the results: framing
   margins were measured directly in pixels, and a pure pitch leaves a world-horizontal shoulder line
   at equal camera-Z so the square-stance yaw is unaffected.

**Methodological note worth keeping.** A pose model CLAMPS a keypoint to the image border rather than
dropping it, so "0 <= u < width" is not evidence a hand is in frame. Every in-frame test in F-18
requires a keypoint >= 10 px from the border. Without that rule every T-pose row would have read
"100 % in frame" and the arm-spread limit would have been missed entirely.

**Not changed.** V5 composition, V6 `TorsoYawGuard`, `TrunkGate`, Arm V2, P1-1/2/3, F-08, UDP
semantics, avatar rig and production thresholds are all untouched. **Porting the rotation into the
sidecar is a production change and needs its own task, ADR and live validation.**

**Per section 20, this is not a production-readiness claim.** It establishes only that the current
camera can satisfy the full-body and torso-yaw geometry simultaneously.

**Evidence:** `docs/F18_PORTRAIT_CAMERA_FEASIBILITY_2026-09-11.md`; data under
`oak_v4_evidence/f18/` (`f18_frame_sweep.jsonl`, `f18_torso_near.jsonl`, `f18_torso_far.jsonl`,
`f18_move_090.jsonl`, `f18_analysis_2.txt`, `v6_observation.txt`, `v6_replay_input.csv`).

---

## ADR-049 — F-19: portrait runs live end-to-end, and the live session found three blockers (2026-09-14)

**Status:** accepted (integration landed behind flags; three defects recorded, none fixed here).

**Question.** F-18 proved the camera can measure torso yaw in portrait. F-19 asked whether those
measurements survive the real production pipeline and produce a credible VRM avatar.

**Integration.** Portrait is in `wholebody_udp_sender.py` behind `--portrait` (default off), using the
F-18 transform imported rather than re-derived. `git diff -- Runtime/` is **empty**; the only Unity
addition is a diagnostic bone recorder referenced by nothing in the product. Equivalence to the F-18
transform re-verified on this machine: **5/5 PASS**, 0.000e+00 px on intrinsics, 0.0001 mm on
geometry, 0/9,960 on left/right identity.

**The avatar answer is good.** 134,959 rendered frames, 37 protocol blocks, one rig throughout.
A physically square human gives a visually square avatar: rendered yaw median **0.012 deg**, range
-2.59..0.30, **zero snaps > 10 deg in all 37 blocks**, worst per-block frame-to-frame p95 **0.503 deg**
against a 5 deg target. Bone lengths constant to **0.0011 %** with lossyScale 1.000000 (attributed, not
assumed). **Zero left/right swaps in 80,835 frames.** Performance: 29.9 fps, capture->send 31.8 ms p50,
and P1-2 freshness intact (stale-dropped p50 = 0).

**Decision 1 — the shipped stereo configuration is not the validated one.** Every F-18 portrait number
used sub-pixel 1/8; production ships `setSubpixel(False)`. Measured back to back on one unchanged
mount: shipped config gives **6 distinct shoulder-dz values in 270 frames, 235 on a single 116 mm bin**
— a **6.80 deg torso-yaw quantum** at 0.90 m, against 0.85 deg with sub-pixel. **A 6.80 deg quantum
cannot express a 5 deg median.** `--subpixel-bits` was added (default -1 = no change) so the live test
could run on the validated configuration; **whether it ships is an open decision, and every F-19
number depends on it.** A hand-written startup banner claiming `subpixel=on(1/8)` while the pipeline
built `False` was found and fixed by making both read one constant.

**Decision 2 — §19 multi-user FAILS.** With a second person present the tracker moved to them at ~25 s
and **never returned**, through the walk-through, the occlusion, and after they left (hip depth
bimodal 1.25-1.40 m vs 2.30-2.60 m; 0 % of frames in the primary's band for the last 60 s). Critically,
**the avatar-level telemetry showed zero snaps and 7.8 mm maximum root steps** — the retarget's lerp
smoothed a total person switch into perfect-looking output. **Rendered-bone continuity cannot detect
person switching; only the source depth stream can.** Any future acceptance test must read the source.

**Decision 3 — a sidecar restart freezes the avatar permanently.** The sidecar restarts `seq` at 1;
P1-3 rejects every frame as out-of-order (`RejectedOutOfOrder = 4948`, `LastSeqA` stuck at 22678,
`packetAgeMs = 208417`) while `IsRunning=True`, `ReceivedCount` climbing and `ParseErrors=0`. The
avatar renders a 3.5-minute-old pose forever, with no fault reported. Pre-existing, not caused by
portrait; §1 forbade touching P1-3, so it is recorded, not fixed.

**Decision 4 — elbows are the F-20 target.** Both elbows fold to **177-180 deg** (human max ~150) on
**81-91 %** of the hands-near-face block, median flex 167.8/169.4, bend normal wandering 74.8/129.6 deg
— robust at every threshold up to 120 deg, and the P0 gate held nothing. Knees are clean. The
whole-capture hinge-spread figures were **withdrawn** as not robust (L elbow 67.9 -> 0.1 -> 12.6 deg as
the threshold rises), so they are not offered as F-20 input.

**Corrections to earlier work.** F-18's derived 19-32 deg downward mount pitch is **wrong**: measured
from the device's own BNO086, it was ~6-7 deg and is **5.89 deg** after levelling, roll +0.14 deg. The
replay-based F-19 draft's "gates never held" does not hold live — the P0 LimbGate held a limb on up to
**76.4 %** of a block, longest **7.6 s**. `getImuToCameraExtrinsics` reports an identity rotation that
is a **placeholder**, disproved directly against a raw frame; only the IMU's Z axis was shown to be the
optical axis, so only tilt is claimed from it.

**Not done.** §17 body-size/clothing generalisation (only two people available), camera height
(still no tape measure), and §12B above ~151 deg of elbow flex (telemetry only, no image).

---

## ADR-050 — F-20A: producer sessions and a stale-pose failsafe for the tracking transport (2026-09-14)

**Status:** accepted (implemented; live-validated). **VERDICT: PASS.**

**Question.** F-19 found that restarting the Python sidecar froze the avatar permanently while every
health counter looked fine. Make sidecar restart, camera reconnect and stale input self-recovering.

**Root cause was TWO defects, not one.** (1) `seq` restarts at 1 and `PoseBuffer` correctly rejects
`seq < newestSeq`, so after a restart every packet is rejected forever. (2) `AppBootstrap.IsPoseStale`
ALREADY detected staleness at 0.5 s, and its response was to stop calling `Apply` - which IS the
freeze. Fixing only (1) would still have left a frozen human pose on any real outage. Useful
precision: the buffer pushes on Unity's own `recvEpoch`, so the timestamp axis was already monotonic
across a restart and only the sequence rule blocked recovery; no timestamp handling was changed.

**Decision 1 - identify producer sessions explicitly.** The sidecar stamps
`sid = uuid4().hex[:12]`, generated once per process, on every datagram. A `seq` reset is deliberately
NOT the identifier: it is the same signature a duplicated sender or wrapped counter produces, and
trusting it would amount to disabling ordering. Backward compatible (unknown field, ~22 bytes); a
bounded legacy rule (>=8 sustained regressions AND >=0.25 s quiet) covers senders without `sid`.

**Decision 2 - flush at the boundary, never interpolate across it.** On a new session the provider
calls the EXISTING `PoseBuffer.Clear()` before the push. P1-3 itself is unchanged; its duplicate,
out-of-order and backward-timestamp rules stand exactly as they were.

**Decision 3 - a dead stream ends in a NEUTRAL pose, not a frozen human one.** New states
NoStream/Connecting/Live/StaleHold/StaleFailsafe/Reconnecting/Recovering, thresholds derived from
measured timing (150 ms warn / 500 ms hold - equal to the existing `poseStaleSeconds` so there is one
definition of stale / 2 s failsafe, against F-19's observed 208 s). At failsafe the driver eases the
control rig to identity local rotation - exact, because the VRM control rig is NORMALIZED, so rest is
identity by definition and no rest capture can go stale.

**Evidence.** Two live sidecar restarts (forced kill and clean exit) plus a REAL USB unplug:
`rejectedOutOfOrder = 0` against F-19's 4948; both restarts accepted the new stream's `seq 1` within
44 ms and 63 ms; measured bone angle from rest went to **0.00 deg** in every outage phase and held
**19.43/52.95 deg unchanged** during the short-stale phase, demonstrating the full
hold-then-neutral policy. Unity restarted under a live sidecar rejoined mid-session at `seq 692` with
`transitions = 0`. Regression suite **124/124**, including PoseBuffer 14, TorsoYawGuard 18,
TrunkGate 15, ArmAimSolver 19. No performance regression (fps +0.2, capture->send -1.8 ms).

**A bug F-20A's own injection test found.** The first run reported `rejectedOldSession = 0`: packets
from a PREVIOUS session were adopted as a new one, flushing a healthy buffer. Fixed with a bounded
set of retired session ids; a retired sid now returns `OldSession` and the packet is dropped. Re-run
live: `rejectedOldSession = 10`, exactly the ten sent.

**Known limitations, recorded not hidden.** (a) NOTHING RESTARTS THE SIDECAR - a USB unplug kills the
process (`rc=1`) and an unattended installation needs a supervisor; this is the next action. (b)
Recovery from neutral necessarily exceeds 10 deg on the first rendered step when the user is far from
rest (measured 13.378 deg, then a damped convergence settling in ~36 ms); permitted by the acceptance
criteria and a deliberate trade against showing a stale pose. (c) The receive clock has 1 ms
resolution, capping the accepted rate near 1 kHz - 60 back-to-back packets produced 28 out-of-order
rejections. Pre-existing P1-3 behaviour, irrelevant at 30 fps (`ooo = 0` on real runs), reported
rather than changed because section 1 forbids modifying P1-3.

---

## ADR-051 — F-20B: a dedicated Python process supervises the sidecar, not a Windows Service or a
bare restart loop (2026-09-14)

**Status:** accepted (implemented; full §17 test matrix A-H passes, including a live session with a
real USB unplug). **VERDICT: PASS.**

**Question.** F-20A made Unity recover once its producer comes back, but explicitly could not make
the producer come back: a USB unplug kills the sidecar process (`rc=1`) and nothing restarts it. An
unattended installation needs that closed.

**Decision - mechanism.** A dedicated Python watchdog (`sidecar_supervisor.py`), not a Windows
Service (needs an interactive session for the depthai/OpenCV preview; install/debug overhead far
exceeds "start, monitor, restart, backoff"), not a bare `.bat` restart loop (cannot cleanly hold a
backoff table, a crash-loop time window, or a machine-readable diagnostics file), and not Task
Scheduler alone (no readiness detection, no custom backoff curve). It extends the exact
`subprocess.Popen` + `taskkill /F /T /PID` process-lifecycle pattern this repo already trusts in
`f20a_failure_injection.py`/`f20a_usb_test.py` into a persistent state machine
(`STOPPED -> STARTING -> RUNNING -> EXITED -> BACKOFF -> STARTING`, with a `FAILED_PERMANENT` crash-loop
floor that keeps retrying rather than ever truly giving up).

**Decision - static vs. self-healing failures.** A missing python.exe/script/model or a failed
`import depthai, cv2, numpy` is terminal (`FAILED_PERMANENT`, the supervisor process exits, rc=2) -
these cannot self-heal by retrying. A camera-not-found / DepthAI init failure is NOT terminal - it
goes through the ordinary backoff/restart path, because plugging the camera back in *does* self-heal
it. Conflating these would either loop forever on a broken venv or refuse to recover from a routine
unplug; keeping them apart is the whole point of the distinction the brief's own §7 and §17-test-F
draw.

**Decision - readiness, not process-alive.** Never called healthy from "the process started" -
F-19 measured every health counter looking perfect while the avatar was 208 s stale. Readiness is the
same two-marker check `f20a_failure_injection.py` already proved live (SID banner, then the first
`frames=` line), plus an ongoing stdout-growth heartbeat once ready, so a hung-but-alive process still
gets restarted.

**Evidence.** 6/6 automated scenarios pass against the REAL supervisor process (not a mock): two
independent forced kills against the real sidecar + camera recovered with a new session id in 7.0 s
and 8.0 s; a synthetic 5-crash loop degraded to `FAILED_PERMANENT` at 20.5 s (matching the 1/2/5/10 s
backoff arithmetic) and kept retrying rather than dying; the single-instance lock correctly refused a
second supervisor while leaving the first completely undisturbed; a missing python.exe produced a
clean terminal failure with zero restart attempts. Full detail and the three bugs this task's own
testing found in its own design (a diagnostics-write race that could crash the watchdog itself; the
degraded state being invisible to an external reader; a test-harness evidence-directory hygiene bug):
`docs/F20B_SIDECAR_SUPERVISOR_WATCHDOG_2026-09-14.md`.

**Live session (2026-09-14, real OAK-D, real USB unplug).** A first attempt found the operator missing
a too-short (12 s) unplug cue rather than any supervisor defect - widened to 25 s and re-run. The
re-run found a real production behaviour F-20A did not observe: on this unplug the sidecar HUNG rather
than crashing, and it was the supervisor's stdout-heartbeat check - not process-exit detection - that
caught it and forced the restart. Five subsequent device-open failures (5.9-12.2 s each, no camera
present) correctly tripped `FAILED_PERMANENT`; recovery landed on the next 60 s-spaced retry (~119 s
total). A cold start with the camera already absent (SS17 test F) recovered in 36 s on the identical
code path when the replug happened to land inside an ordinary backoff gap instead - confirming
`--failed-permanent-retry` (60 s default) is now the dominant term in worst-case recovery time.
Separately, Unity was stopped and restarted while the supervisor/sidecar stayed up (SS17 test H):
`RestartCount` stayed 0 for the whole window and the avatar reconnected mid-session via F-20A's
existing logic, confirming the two systems stay decoupled as SS16 requires. The avatar recovered live
in every case, with no Unity restart and no scene reload.

**Known limitation, recorded not hidden.** This project's venv `python.exe` is a launcher - the pid
`subprocess.Popen` reports is not the real interpreter's own `os.getpid()` (confirmed directly:
31108 vs. 19000 for the same invocation). `taskkill /T` (tree-kill) makes this transparent for every
kill this supervisor performs; anyone extending it must keep `/T` and never assume `SidecarPid` in the
diagnostics file is the pid actually holding the OAK-D handle.

**Tuning note carried forward.** `--failed-permanent-retry` (60 s default) is now the dominant
recovery-time variable once an outage is long enough to trip the crash loop - see the report §13.

---

## ADR-052 — F-21: geometry-only target ownership (position + scale continuity), no track ID
invented (2026-09-14)

**Status:** accepted (implemented; unit + real-hardware single-person evidence PASS; live two-person
matrix unverified). **VERDICT: CONDITIONAL.**

**Question.** F-19 found the tracker silently switches to a second person who enters frame, with
Unity rendering the hand-off as smooth continuous motion. Fix WHO is tracked, without touching pose
estimation, retargeting, or F-20A/F-20B.

**Decision - verify before designing.** Read `rtmw3d_pose.py` and the M15 loop in
`wholebody_udp_sender.py` before writing any ownership code. Confirmed: RTMW3D-x is single-person
top-down (one inference, one keypoint set per frame, no detector, no track id, no multi-person output
anywhere in this codebase). "Candidate count" is therefore honestly 0 or 1, never N - the design does
not pretend otherwise.

**Decision - identity proxy, not identity.** No track ID exists, so ownership is built from the only
real signals available: raw (pre-smoothing) camera-space hip-position continuity as the primary
signal, confidence as a validity gate, torso-scale as a corroborator. Position is read BEFORE P0's
smoother rate-limits it (`--max-jump` 1.5 m/frame) - reading after would blur a real person-swap into
what looks like fast continuous motion over a couple of frames.

**Decision - M15 becomes identity-gated.** The crop-follow loop only refines toward an observation
F-21 currently accepts; it holds steady (never drifts toward a rejected candidate) while
`TEMPORARILY_LOST`, and only fully re-opens (`center_bbox()`) once genuinely `RELEASED`. Confirmed
necessary before implementing: without this, F-21 could detect a switch but could never recover the
true owner, since the model's own crop would already have moved on.

**Decision - no UDP contract change.** Ownership gates whether the UDP packet is built and sent at
all, reusing the sender's own existing no-op path (the same one already used when `mid_hip` can't be
computed). F-20A's stale watchdog turns a withheld frame into hold-then-neutral with zero new Unity
code; a later acquisition rides the same sidecar session id, so "same session ≠ same person" falls
out for free rather than needing new wire semantics.

**Evidence.** 36/36 unit assertions (state machine, offline, deterministic - crossing, rapid A/B/A,
flicker-never-locks, gross scale mismatch, temporal boundary cases). Real hardware: clean acquire/
lock/emit; 5/5 sidecar-restart-with-person-present (fresh epoch, new SID, zero stale-identity
carry-over); `--no-ownership` bit-identical to the pre-F-21 path. A 775-frame / ~13 s real video
stress test (energetic dance motion, 2x production frame rate) showed zero `TARGET_SWITCH` and zero
`TARGET_RELEASED`. **Zero wrong-person switches in every test that produced usable data** - the
brief's own critical acceptance metric.

**Not yet done, and this is why the verdict is CONDITIONAL, not PASS.** The live two-person scenario
matrix (crossing, release-then-hand-off, simultaneous entry) did not produce usable evidence: attempt
1 was blocked by a test-script UX defect (instructions printed to a terminal the operator couldn't
read while coordinating two people and watching the camera - fixed with an on-screen cue banner
drawn on the same preview window as the F-21 HUD); attempt 2 was blocked by a real OAK-D device
crash unrelated to F-21's own code. Full detail: `docs/F21_SINGLE_PERSON_TARGET_OWNERSHIP_2026-09-14.md`.

**Named limitation, stated up front, not hidden.** If a second person occupies the first person's
just-vacated position within one observation cycle, position alone cannot distinguish them - a
property of a geometry-only signal set with no appearance/ReID available, not a defect introduced
here. This is exactly why release is timeout-gated (4 s, longer than F-20A's own 2 s transport
failsafe) rather than instantaneous, and it remains a named-but-unmeasured risk until a live test
specifically targets it.

---

## ADR-053 — F-22: angle-based (not bone-length-based) biomechanical validation, reusing P1-1's
existing invalid-joint contract (2026-09-14)

**Status:** accepted (implemented; unit-proven; offline replay evidence PASS; live evidence pending).
**VERDICT: CONDITIONAL.**

**Question.** F-19 found a live, unmitigated defect: with hands near the face, the rendered elbow
reached 167.8-179.6° (anatomically impossible; a real elbow's ceiling is ~145-150°), while P0 LimbGate
held 0.00% of that block because confidence was high even though the angle was not. Nothing in the
pipeline checks whether a measurement is a physically plausible human pose.

**Decision - audit before designing.** Read LimbGate, PoseBuffer, ArmAimSolver, TrunkGate/
TorsoYawGuard, JointTracker, and P1-4's closeout before writing any validation code. Confirmed: no
angle/flexion check exists anywhere in the live path; `ArmAimSolver` already computes the exact
needed angle (`BendDeg`, L289) every frame but discards it; P1-1 already does Cartesian
position/segment-length checks (not duplicated here); P1-4's bone-length-ratio rejection is formally
REJECTED for this hardware (its length signal's natural variation overlaps real corruption - the safe
threshold band is empty) - its own closeout flagged the ANGLE-only portion as comparatively
trustworthy, a precedent FOR this approach.

**Decision - angle, not length.** `pose_validation.py` validates elbow/knee bend angle (absolute +
angular rate) from RAW camera-space geometry, computed in the identical convention `ArmAimSolver`
already uses, so F-19's own rendered-evidence thresholds (~150° adopted directly, not re-derived)
apply to this pre-retargeting signal unchanged. No bone-length-ratio check was added, consistent with
P1-4's own rejected precedent.

**Decision - reuse P1-1's contract, not a new one.** A rejected/held joint zeros only that joint's own
`conf_emit` slot - the exact mechanism P1-1 already uses for a LOST joint ("LOST -> drop -> P0
LimbGate holds"), which Unity's P0 LimbGate already correctly consumes. Zero `Runtime/` changes, zero
UDP changes - the same minimal-footprint pattern ADR-052 (F-21) used successfully.

**Decision - localized, not global, failure.** Each chain (left/right elbow, left/right knee) is
validated independently; a bad elbow never suppresses the wrist, the other arm, torso, or legs. A
"global" failure is not a special code path - it falls out naturally of every chain failing on its
own merits (unit-tested directly, ADR's report §16).

**Evidence.** 22/22 unit assertions, including F-19's own measured numbers fed directly as test
fixtures (168° -> rejected, 176° knee -> NOT rejected, matching F-19's own legitimate-walking
finding). Offline replay against a real 431-frame energetic dance video: 0 absolute-angle false
rejections, elbow bend max (155.3°/158.8°) sat within 1-5° of REJECT (160°) without crossing it, and
exactly 3 momentary rate-based holds (0.7% of frames), each recovering within one frame. Full detail:
`docs/F22_HUMAN_POSE_VALIDATION_2026-09-14.md`.

**Not yet done, and this is why the verdict is CONDITIONAL.** No live human was available this
session (explicit instruction). The specific test that closes the loop back to F-19 - a live L2
(hands-near-face) reproduction confirming this raw-geometry threshold suppresses the same defect F-19
measured in rendered geometry - has not run.

---

## ADR-054 — F-21: the frozen-reference match runs for the whole release budget, not only inside
## REACQUIRE_WINDOW (2026-09-14)

**Context.** F-21's §9 describes `REACQUIRE_WINDOW` (2.0 s) as "the faster path within that budget",
language that presupposes a slower path also existing inside `RELEASE_TIMEOUT` (4.0 s). The code did
not implement one: it gated the frozen-reference match itself on `loss_elapsed <=
reacquire_window_s`, so past 2.0 s matching was not slowed down, it was switched off.

**Evidence (offline, deterministic, `f21_adversarial.py` scenario s08b).** An owner who stepped away
for 2.33 s and returned to their exact original position at full confidence was rejected **51
consecutive times** as `not_owner`, held un-emitted for 1.87 s, `TARGET_RELEASED`, then re-acquired
with a `TARGET_SWITCH` logged — against a person who never moved. `TARGET_SWITCH` is this report's
single headline safety metric, so the defect also corrupted the metric used to judge it.

**Decision.** Attempt the frozen-reference match for the whole release budget. `REACQUIRE_WINDOW`
keeps its documented meaning as the selector for the confirm count (fast path inside, full
acquisition standard outside). Both counts default to 5, so default numeric behaviour is unchanged —
what changes is that the owner can be matched at all.

**Why this was done without live evidence**, when F-21's thresholds are otherwise explicitly pending
live tuning: it is **strictly safer than the behaviour it replaces**. The alternative the old code
forced — release, then fresh acquisition — accepts *any* body at *any* position with *no*
frozen-reference check whatsoever. Requiring the position+scale gate against the frozen owner is a
strictly stronger admission test than the release-and-reacquire path it avoids. This is a logic fix,
not a threshold change; no threshold value was altered.

**Also changed:** `TARGET_REJECTED_CANDIDATE` now reports the real discriminator (`position_jump` /
`scale_mismatch` / `no_reference`) instead of a flat `not_owner` that hid which gate fired.

**Guarded by** `test_target_ownership.py` `t17` (the returning owner is re-locked with no release, no
switch, no rejection) and `t18` (a *different* body arriving in the same widened interval is still
rejected on position and still cannot acquire without waiting out the full release). 36/36 → 46/46.

---

## ADR-055 — A liveness heartbeat reports that the CAMERA LOOP is alive, not that a SUBJECT is
## present (2026-09-14)

**Context.** F-20B's supervisor detects readiness from two markers the sidecar already prints: the
session-id banner, then the first `frames=` line. That contract is sound. The sidecar broke it by
printing `frames=` only *after* `build_body_landmarks` and the UDP send — so every `continue` above
skipped it: the "no hip depth" path, and (since F-21) any frame ownership withholds.

**Evidence (offline, real hardware, empty room).** With the OAK-D attached and healthy and nobody in
front of it, a fully-booted sidecar printed **zero** `frames=` lines. The supervisor logged
`STARTUP STUCK pid=24460 not ready after 45s - killing for restart` and restarted it, repeatedly;
5 such restarts trip `FAILED_PERMANENT` in ~4 minutes, after which each 60 s retry fails identically.
**An empty room is the normal state of an unattended public installation between visitors.**

F-20B's live §17 matrix passed originally because a person was standing at the camera throughout
every run in it. The defect requires the *absence* of a person to reproduce.

**Decision.** Fix it in the **sidecar**, not the watchdog — the watchdog's contract was never wrong.
`wholebody_udp_sender.py` now emits an idle liveness line when the detailed status line has been
starved for 3 s (longer than its own 2 s cadence, so it never doubles up during normal tracking):

```text
[wb] frames=76 sent=0 idle - camera loop alive, no pose emitted (no subject / withheld) fps~25.3 ...
```

It carries `frames=`, satisfying the existing readiness contract unchanged, and is strictly more
informative than before: an operator can now distinguish *camera healthy, nobody there* from *camera
dead*, which was previously impossible from the log.

**General principle, worth applying beyond this instance:** a liveness signal must not be gated on
the presence of the thing whose absence it needs to survive. Conflating "is the pipeline alive" with
"is a person being tracked" made an empty room indistinguishable from a dead camera.

**Evidence of the fix**, same conditions: `SIDECAR READY sid=1a61dbe970d3 elapsed=6.777s`,
`RestartCount=0`, 72.7 s continuous uptime with nobody present. `f20b_failure_tests.py` went from
**4/6 to 6/6 with an empty room** — a state in which it previously could not pass at all.

---

## ADR-056 — F-21's silent wrong-person hand-off is documented and left unfixed today, deliberately

> **SUPERSEDED IN PART by [ADR-057](#adr-057) (2026-09-15).** The hand-off is now fixed. The
> reasoning below still stands as the record of why it was deferred, and one of its premises
> turned out to be wrong in an informative way: it assumed only live data could price the
> false-rejection cost of a path-consistency rule. Offline ground-truth labelling measured that
> cost directly (96 frames of a legitimate returning owner on `123.webm`), which is what made
> the decision possible today. The **named candidate fix** below is the one that was built.
## (2026-09-14)

**Context.** Replaying `123.webm` (which, contrary to F-21 §13's original premise, contains two
people) through the real ownership path reproduced a **silent wrong-person hand-off**: F-21 correctly
refused the intruder for 54 frames, but the second person then walked to within the switch margin of
the first person's frozen last-known position, passed the frozen-reference match, and was re-locked
as the **same `target_id`** — `TARGET_SWITCH = 0`, `TARGET_RELEASED = 0`. Confirmed frame by frame
with images. This is the F-19 defect occurring inside the layer built to prevent it, via the
reacquire path rather than the M15 crop path.

**Decision: document and measure it now; do not fix it today.** Three reasons, in order of weight:

1. Closing it properly needs either an identity signal this pipeline does not have — and inventing
   one without evidence is explicitly out of scope — or new admission logic, which is development,
   not the validation the next session is meant to be.
2. The fix has a real false-rejection cost that only live evidence can price. Any path-consistency
   rule tight enough to catch this hand-off will also reject some genuine occlusion recoveries, and
   there is no offline data that distinguishes the two populations.
3. It is a *known, named* limitation (F-21 §11/§16) being promoted from "not ruled out" to
   "reproduced and imaged", which is itself the deliverable.

**Named candidate for when it is fixed**, so the next session has something concrete to arbitrate
rather than a blank page: require a reacquire to be **path**-consistent as well as
position-consistent. A candidate observed far outside the margin during the loss, which then walks
back inside it, is the exact signature — and it is derivable from positions the module already holds,
with no new identity signal. What is unknown, and needs live data, is how often a genuine owner
produces that same signature by walking back into frame from the side.

**Do not** meanwhile quote `TARGET_SWITCH = 0` as evidence of correct ownership. It counts declared
hand-overs only; the silent ones are precisely the ones it cannot see.

## ADR-057 — F-21: reacquisition is PATH-consistent, not only position-consistent; the ambiguous
## case resolves towards a DECLARED hand-over

**Status:** Accepted (2026-09-15). **Supersedes the "not today" half of [ADR-056](#adr-056).**
**Scope:** `python-sidecar~/target_ownership.py` only. No Runtime/ change, no F-22 change, no new
identity signal.

**Context.** ADR-056 recorded a silent wrong-person hand-off reproduced on real footage, left it
unfixed, and named the candidate fix: require a reacquire to be path-consistent as well as
position-consistent. It also named the thing that was unknown and that supposedly needed live data:
*how often a genuine owner produces the same signature by walking back into frame.*

That question turned out to be answerable offline, and the answer is what makes this decision
possible. It is not "rarely". On `123.webm` the returning owner produces **exactly** the impostor
signature, and the two are not merely similar — from a hip position they are **the same observation
stream**. So the choice was never "catch impostors without touching genuine returns". The real choice
is what to do with an ambiguity that cannot be resolved with the signals this pipeline has.

**Decision.** Track the candidate's own observed path during a loss episode:

> `_chain_walked_in` is true iff the candidate currently being observed has, at any point in an
> unbroken observation chain during this loss episode, been seen outside `SWITCH_MARGIN_M` of the
> frozen owner position.

A matched candidate carrying that flag is refused (`reason="path_walked_in"`). It then releases
normally at `RELEASE_TIMEOUT_S` and re-acquires as a **new epoch with `TARGET_SWITCH` logged**.

The point is not that the gate identifies anybody — it cannot, and does not claim to. The point is
that it converts a hand-over the consumer was **never told about** into one it **is** told about.

Two supporting choices, both deliberate:

* **A chain break that lands inside the gate clears the flag.** A body appearing at the owner's spot
  with no observed approach is indistinguishable from the owner stepping back out from behind an
  obstruction, so it stays admissible. Only the distinguishable case is refused.
* **The continuity distance reuses `SWITCH_MARGIN_M`** rather than adding a tunable. It is already
  the module's "a one-frame move further than this means a different body" quantity. Measured across
  all three clips: largest single-frame displacement of a continuous body 126.1 px against a 141 px
  margin; the three real body-to-body jumps 303.5, 323.0 and 687.5 px - so 2.15x below the
  SMALLEST of them, not merely below the largest.

**Consequences, including the one that costs something.**

```text
123.webm   silent wrong-person emission   38 frames (0.63 s)  ->  0
           the hand-over becomes DECLARED: release + TARGET_SWITCH + new epoch
           COST: 96 frames (1.60 s) of the legitimate owner withheld, one contiguous block,
                 because she walked back in
video.webm PATH_OFF and PATH_ON identical - the gate never fires on single-person motion
cost       +0.204 us/frame (+0.00061 % of a 30 fps budget); three scalars of state.
           Measured with no model in the loop, so it does not depend on which execution
           provider the harness happened to get.
```

Roughly 2.5 frames of the right person withheld per frame of the wrong person suppressed, **on this
clip**. That ratio is a property of a clip in which the owner leaves and returns once; it is not an
installation rate and must not be quoted as one.

**Why this direction and not the other.** Brief §7, and this module's own §14, already decide it: a
false temporary hold is acceptable, a silent wrong-person hand-off is not. F-20A already shows
neutral during a hold, so the visible cost of holding is bounded and understood, whereas the cost of
a silent hand-off is an avatar driven by the wrong human with nothing in any log to say so.

**`RELEASE_TIMEOUT_S` is the knob that prices this**, trading blackout duration against epoch churn.
It is deliberately left at 4.0 s and flagged for the live session rather than tuned against one clip.

**Rejected:** direction-of-arrival matching (overfits `123.webm`, where impostor and owner both
arrive from the left — brief §5 forbids it, and scenarios P4/P7 keep it forbidden); owner-velocity
extrapolation (a person about to be occluded has a noisy, low-information velocity); releasing
immediately on a path-inconsistent candidate instead of holding (equal wrong-person safety and better
availability, but it destroys the frozen reference the moment any passer-by crosses, losing a
genuinely occluded owner's epoch to a stranger); tightening `SWITCH_MARGIN_M` (trades a measured
margin for an unmeasured one and does not address a candidate reaching the exact frozen position);
ReID / appearance / face / detector (forbidden, and absent from this pipeline).

**`path_consistency=False` restores the previous behaviour exactly**, and is kept as the control that
makes the claim falsifiable — scenario P11 asserts that the same input silently emits the wrong person
with the gate off and does not with it on. It is excluded by name from the suite aggregate so a
deliberately-disabled run can never be read as production behaviour.

**Still not closed by this**, and not to be claimed: the gate is verified against replayed footage
with no hip depth and no live two-person data. F-21's verdict stays **CONDITIONAL**.

---

## ADR-058 — The live-session cue must be sized for the SUBJECT at the camera, not the operator at
## the desk; and it is drawn by the producer, in one window

**Status:** Accepted (2026-09-15). **Scope:** `f21_cue_display.py` (new), `wholebody_udp_sender.py`
`--cue-panel` (opt-in, preview-only), `sidecar_supervisor.py` flag forwarding. No tracking change.

**Context.** F-21 S31.4 fixed `--cue-file` (a missing `import io` had made the banner dead since it
was written) and the banner was then imaged and declared working. It had still never been read by a
human at the camera, which is the only place it matters: in a two-person protocol NOBODY is at the
keyboard.

**The measurement that decided it.** The preview is the camera frame — 640x400, 400x640 under
`--portrait` — shown through `cv2.imshow` with no `namedWindow`, i.e. WINDOW_AUTOSIZE, i.e. a 400 px
window at native size. `_draw_cue`'s instruction line is FONT_HERSHEY_SIMPLEX scale 0.55 = **13 px
cap-height, 1 px stroke**. On a 24" 1080p monitor that is 3.6 mm; at the 2-3 m a subject stands from
it that subtends **4.1-6.2 arcmin**, against ~5 arcmin for the 20/20 threshold of RESOLUTION and
15-25 arcmin for glanceable reading. Correctly sized for the desk; ~4x too small at the camera.

**Decision.** A separate renderer (`f21_cue_display.py`) draws a large cue PANEL, and the sidecar
composites it beside the feed in ONE window under `--cue-panel`. Measured: instruction **60 px
cap-height = 20.5-25.7 arcmin at 2.5 m**. Three redundant channels, because text is the first thing
to fail at distance: a colour field down both edges, a ~300 px actor glyph (A/B), and a top-down
diagram of camera/floor-cross/movement. The sentence is the fourth channel, not the only one.

**Why in the producer and not a second window.** One 1920x1080 monitor: a fullscreen cue covers the
preview, and the operator and the screen recording both lose the F-21 HUD. `--cue-panel` is
preview-only, opt-in (default path byte-identical), and imports the renderer lazily so the
live-critical import graph is unchanged under the default.

**Cost, measured, because the composite is 7.4x the pixels.** Naive composite 4.29 ms/frame vs
0.62 ms for the small banner (+11 % of a 30 fps budget). With the panel cached on the fields that
change what is drawn (rebuilt ~1x/s, not ~30x/s) and the canvas preallocated: **0.38 ms median,
p95 0.48, max 3.93** — cheaper than the banner it replaces, and no per-frame allocation.

**DO NOT**
* do not judge cue legibility by looking at a screenshot on the desk — it is a visual-angle
  question and the answer is a number
* do not make the cue fullscreen on a single-monitor rig
* do not add a generic `--extra-args` passthrough to the supervisor (S31.2); `--cue-panel` is a
  third NAMED flag for the same reason

---

## ADR-059 — Pelvic roll is re-enabled: it is an IMAGE-PLANE signal and was switched off as
## collateral of a bug that had already been fixed separately

**Status:** Accepted (2026-09-15), **applied but NOT yet validated by the Unity EditMode suite.**
**Scope:** `Scenes/Bootstrap.unity` `kalidokitBodyTorsoRoll: 0 -> 1`. No code change.

**Context.** A user watching the F-23 dance replay reported that the dancer moves her hips and the
avatar does not. Three independent causes were found; this ADR covers the one that is a product
decision rather than a harness defect (the other two were bugs in `f23_video_to_unity.py` — see the
F-23 report).

**What was measured.** The pelvis has three rotational axes and, in the shipped configuration, one
of them is live:

```text
ApplyBone(hips, new Vector3(0f, hipsYaw, hipsRoll * HipsRollDamp))
  pitch  hardcoded 0f
  yaw    survives, but through the 8-22 deg deadzone -> 76.6 % of this dancer's frames zeroed
  roll   hipsRoll = pose.Hips.z * torsoRoll, and torsoRoll = 0 -> ALWAYS ZERO
```

The dancer's pelvic obliquity is **25.1 deg peak-to-peak** and none of it reached the avatar.

**Why it is safe, which is the whole argument.** `torsoRoll` was set to 0 on 2026-08-07 alongside
the real fix for a "leaned right all the time" complaint — the spine euler was applied to BOTH Spine
and Chest, doubling to ~45 deg yaw + ~20 deg roll. **That doubling was fixed independently** by the
0.5/0.5 Spine/Chest split. The roll was zeroed as a second, belt-and-braces measure citing ADR-019's
no-unreliable-roll precedent. But ADR-019 is about axes that are unreliable FROM DEPTH, and roll is
not one:

```text
KMath.RollPitchYaw2(a, b).z = Find2DAngle(a.x, a.y, b.x, b.y)      X and Y only - never Z
```

Pelvic roll is derived purely from the image plane, which is the most reliable thing RTMW3D
produces. Two protections that were inert at `torsoRoll = 0` become live again and are kept:
`hips.z *= 1 - turnHips` fades roll out as the person turns, and `HipsRollDamp = 0.7`.

**Consequence for the OAK-D.** Because it is image-plane derived, this gain **transfers** to the
live sensor path unchanged — unlike anything on the depth-derived yaw channel.

**DO NOT**
* do not read this as permission to re-enable depth-derived roll or chest axial twist — ADR-019
  stands for those
* do not claim this validated until Unity EditMode 170/170 has been re-run; the suite needs the
  editor closed and that had not happened when this was written

---

## ADR-060 — Global slerp damping is NOT what separates the avatar from the debug skeleton
## (a measured negative result)

**Status:** Accepted (2026-09-15). **Scope:** none — `kalidokitBodyLerp` stays at its validated 0.5.
Recorded because the hypothesis was plausible, acted on, and wrong.

**Context.** `PoseDebugSkeleton` draws RAW landmark positions; its own docstring says that if the
skeleton tracks cleanly and the avatar does not, the retarget is at fault. A user observed exactly
that. `ApplyBone` slerps EVERY bone toward its target with `lerpAmount = 0.5` every frame, with no
frame-rate compensation, so it was hypothesised to be the dominant sluggishness term.

**The A/B, on the F-23 dance clip, everything else identical.** Metric: peak Pearson correlation
between the commanded arm-span and the avatar's rendered width, measured from pixels.

```text
lerp 0.5 (shipped)        best r +0.714   lag 300 ms
lerp 1.0 (no damping)     best r +0.691   lag 267 ms
```

Removing the damping ENTIRELY buys **one frame (33 ms)** and makes correlation slightly WORSE.

**Why, in hindsight, and the arithmetic that should have come first.** `Slerp(cur, tgt, 0.5)`
converges 50 % per frame; at 30 fps its time constant is ~1 frame (~33 ms) — precisely what the
measurement returned. The 300 ms decomposes instead as One-Euro `yawSmoothTau = 0.15 s` (~150 ms,
trunk yaw only) + P1-3 `interpDelayMs = 40` + slerp 33 ms + inference/transport/render.

**The result that matters more than the lag.** `r` barely moved. **Responsiveness is not what
separates the avatar from the skeleton.** The ~0.7 ceiling is structural: the deadzone zeroing
76.6 % of hip yaw, the hardcoded hips pitch, and the arm solver. Damping was the wrong target.

**Also checked and rejected:** driving the hardcoded hips pitch. `RollPitchYaw2`'s pitch is
`Find2DAngle(a.z, a.y, ...)` — depth-derived — and the pitch of the HIP LINE is not forward bend
anyway (that is the spine `bend` term). The `0f` looks deliberate, not accidental.

**DO NOT**
* do not raise `kalidokitBodyLerp` to chase responsiveness — it is measured at 33 ms
* do not attribute the avatar/skeleton gap to smoothing without re-running this A/B

## ADR-061 — F-21's owner reference DRIFTS while LOCKED, and that is a second silent wrong-person
## route which ADR-057's gate is structurally unable to see (live evidence, 2026-09-16)

**Status:** Accepted as a FINDING (2026-09-16). The drift is now **MEASURED** and a mechanism exists
**behind a flag that defaults to OFF**, so production behaviour is byte-identical to before. The
budget itself is deliberately **not chosen** — see *Mechanism* below. **Scope:**
`python-sidecar~/target_ownership.py`, the `LOCKED` branch.

**Context.** ADR-057 closed the *reacquisition* route to a silent hand-off: a candidate tracked
walking in from outside the margin is refused. That fix stands and was re-confirmed live on
2026-09-16 (`IMPOSTOR_WALKIN` 0/2 silent, 2/2 declared).

The first two-person session then produced a silent wrong-person emission **anyway**, in a case
nobody was watching.

**The measurement.** `OWNER_OCCLUDED` rep 7. A stands on the cross; B walks between A and the camera.
Emitted hip depth, read off the recorded preview at 0.5 s intervals — `hip` is `float(mid_hip[2])`,
printed only on the emitting path:

```text
sent=5740  hip 1.61m  LOCKED   A
sent=5768  hip 1.34m  LOCKED     migrating
sent=5781  hip 1.16m  LOCKED   B      <- skeleton visibly on B
sent=5796  hip 1.19m  LOCKED   B
sent=5837  hip 1.58m  LOCKED   A
```

97 datagrams emitted. `target_id` 13 throughout, `switches` 12 throughout, **zero ownership events
logged for the entire manoeuvre**. Imaged in `oak_v4_evidence/f21/walkin/s34_evidence/`.

**Root cause.** The `LOCKED` branch updates the reference on every accepted frame:

```python
if ok:
    self.owner_pos = obs.pos
```

`SWITCH_MARGIN_M` is therefore tested against the **previous accepted frame**, not against the
position the epoch locked onto. The reference is frozen only *after* a loss is declared. While
`LOCKED`, an observation that migrates by less than the margin per frame is accepted step by step and
the reference slides from one human to another, never failing a single test. ADR-057's gate lives in
the `TEMPORARILY_LOST`/`REACQUIRING` branch and is never reached, because the state never leaves
`LOCKED`. **The gate is behaving exactly as specified; the specification does not cover this path.**

**Why every previous test missed it.** All of §30's offline evidence is built on `123.webm`, where the
observation *teleports* between bodies — the three real body-to-body jumps there are 303.5, 323.0 and
687.5 px. The margin was designed against jumps and validated against jumps. Rep 7 moved
1.61 -> 1.16 m over about a second: **a few millimetres per frame.** A slow drift is a different
failure mode and nothing in the module looks for it. The deterministic suite could not have caught it
either — `steps_path()` builds walk-ins that start OUTSIDE the margin, which is the reacquisition
case, not this one.

**Honest limit of this measurement.** The emitted hip was sampled from the preview at 2 Hz, not
logged per frame; there was no wire recording for this run. The migration and the continuous emission
are certain (the `sent` counter increments monotonically and the status string distinguishes
emitting from withholding), but **the exact wrong-person frame count is not measured** and is not
claimed. Closing that is the first task in §34.5.

**Two of the four options are RULED OUT by measurement, not by argument.** Taken from the live trace
where the emitted hip walked from a person at 1.61 m onto a person at 1.16 m:

```text
per-frame distance   the migration moved 0.015 m PER FRAME against a 0.35 m margin - 23x under.
                     No per-frame threshold catches that without forbidding ordinary motion.
scale gate           torso span scales as 1/Z, so B's apparent span was 1.39x A's = +39 %,
                     inside the +/-45 % ADR-052 set wide on purpose. It cannot see this either.
```

Only the **cumulative displacement from the anchor** separates them, and only because the anchor
stops moving.

**Mechanism, implemented 2026-09-16, OFF by default:**

```text
_anchor_pos       recorded at _lock(), and NOT updated while locked - unlike owner_pos, whose
                  per-frame update is the line that lets a migration through.
owner_drift_m     distance from the anchor, measured every accepted frame. DIAG-ONLY.
                  Surfaced on snapshot() so the live HUD shows it - which is how the next
                  session gets the distribution a budget could honestly be set from.
drift_budget_m    default None = DISABLED = exactly today's behaviour. Verified: the unit suite
                  and the 101/101 adversarial suite are unchanged with the default.
                  When SET, exceeding it emits TARGET_DRIFT_EXCEEDED and drops into
                  TEMPORARILY_LOST with reason "drift_budget", routing the candidate through
                  the normal reacquisition path INCLUDING ADR-057's path gate.
```

**Honest limit of the mechanism, pinned by test t25:** a reacquire RE-ANCHORS, so the budget is a
**ratchet, not a refusal** — a long migration is announced once per budget-length rather than
forbidden. It converts silence into a repeating announcement; it does not by itself change identity.
That is a deliberate stopping point: refusing outright is the decision that needs the false-rejection
cost priced against real walking owners, and no such measurement exists yet.

**The budget remains UNSET because it is UNMEASURED.** Choosing a number here would be exactly the
mistake this ADR documents. The next live session should run with the HUD drift readout and record
the distribution for an owner who walks, turns, and is occluded.

**Tests:** `test_target_ownership.py` 61/61 -> **75/75**. t23 reproduces the live failure in a unit
test (30/30 frames emitted, never leaves LOCKED, 0.450 m of drift); t24 asserts the per-frame
measurement that rules out a per-frame fix; t25/t26 pin the opt-in mechanism and that an owner
standing still never trips it.

**Options for the eventual behavioural fix, still none chosen:**

| Option | Cost |
|---|---|
| Freeze `owner_pos` at lock and test every frame against it | breaks a legitimate owner who walks anywhere; the margin becomes a leash, not a jump detector |
| Keep the rolling reference, add a **cumulative-displacement budget** from the lock anchor | needs a budget number nothing has measured yet, and a legitimate walking owner still exhausts it |
| Test scale against the **original** lock rather than the rolling one | scale is a weak discriminator (±45 % by design, ADR-052) and two adults of similar build defeat it |
| Require re-confirmation after N metres of accumulated drift | converts a silent hand-off into a declared one without forbidding movement; cost is churn for an active owner |

**DO NOT** in the meantime:
- do not quote "false holds = 0/4" as evidence that occlusion is handled. It is true and it is blind
  to this; see §34.4.
- do not treat `TARGET_SWITCH = 0` as evidence of correct ownership. That is the *third* time this
  report has had to say so (§30.2, §33.1, here).
- do not ship single-person ownership as a SAFETY property on the current evidence. The occlusion
  case costs either 4.19 s of blackout or a silent wrong person depending on which way the detector
  falls, and both were observed in the same 8-rep session.

---

## ADR-062 — The avatar now has a POSE FIDELITY metric, and its first measurement says the trunk
## LEAN channel is dead while the aimed arms track the subject to ~1 degree

**Date:** 2026-09-16  **Status:** ACCEPTED (instrument); measurement is FIRST EVIDENCE, two clips,
one avatar, video path only. **Supersedes nothing. Blocks nothing. Changes no product behaviour.**

### The gap this closes

Every avatar-side number this project has produced measures the avatar against ITSELF — yaw jitter,
snap count, bone-length constancy, left/right swap count. F-19 reported all four as "AVATAR QUALITY:
PROVEN GOOD". All four are satisfied by an avatar that is smoothly, stably and consistently in the
WRONG pose, which is what the 2026-09-16 recording shows (F-26 §5.1). Nothing anywhere compared the
avatar's pose to the SUBJECT's pose. ADR-060 reached the same wall from the other side: it ruled out
global slerp damping as the difference between avatar and skeleton, and could go no further without
a fidelity number.

### The instrument

`KalidokitControlRigDriver.SampleFidelity(BoneFidelity[])` — 9 bones, read-only, allocation-free,
caller-owned buffer. Per bone it reports the angle between the direction the SUBJECT's segment points
and the direction the RENDERED avatar bone ends up pointing, plus the two source landmarks' minimum
confidence, plus each side's own inclination from the rig's vertical.

It lives on the driver, not in a separate component, because the comparison needs four things that
are private to it and getting any of them wrong produces a plausible, meaningless number: the
landmark buffer AFTER the mirror/swap conditioning, the Kalidokit->rig axis map, the left/right
CROSS-map (the avatar's LEFT arm is solved from source landmarks 12/14/16), and the rig frame.

`AvatarFidelityRecorder` writes one JSON record per frame and samples on `Application.onBeforeRender`
— NOT LateUpdate. The retarget chain ends with UniVRM's `Vrm10Runtime.Process()`; a LateUpdate sample
races it and can capture the pose one stage early, silently measuring a skeleton that was never
drawn. `F19BoneRecorder` records the same reasoning; here it matters more, because a stale sample
would bias the very number the metric exists to establish.

`f26_fidelity_analyze.py` reports the distribution. **22/22 self-test.**

### Four choices, each made to stop a specific failure this project has already had

1. **PER BONE, never a single blended score.** A mean over nine bones hides a trunk failing while the
   arms are fine — exactly the failure the video shows. The analyser REFUSES to emit a combined score.
2. **A DISTRIBUTION (median / p90 / max), not a mean.** The failures are intermittent and
   pose-dependent; a mean over a mostly-standing session reports "good".
3. **Low-confidence frames are EXCLUDED and counted, not averaged in**, at the retarget's own
   `limbConfidenceThreshold` (0.3). A bone whose source is not observed says nothing about the
   retarget. The recorder writes `null`, never `0`, so an unmeasurable bone can never read as perfect.
4. **STALE frames are dropped.** When the pose stream stops, `ReleaseToRest` walks the avatar back to
   rest while the recorded target stays frozen at the last packet — manufacturing a large error out of
   the shutdown. A source value repeating for longer than `poseStaleSeconds` means no packet arrived;
   it cannot mean a still subject, because RTMW3D's own estimation noise changes the value every
   packet. **This was not hypothetical**: the first run reported forearm maxima of 121.6 deg and
   155.4 deg and a ~5 % catastrophic tail on every arm. All of it was the release-to-rest tail. With
   the 742 stale frames dropped the same recording reads max 59.3 / 71.9 deg and 0.0-0.1 % over 30
   deg. Reporting that tail would have been a fourth wrong instrument this month.

### What it measured (Run A: video.webm x4, subject at ~1.4 m, 5929 recorded / 742 stale / 5187 analysed)

| bone | median | p90 | max | >10 deg | follow (rig swing / subject swing) |
|---|---|---|---|---|---|
| leftUpperArm | 0.4 | 1.0 | 4.0 | 0.0 % | 1.00 follows |
| leftLowerArm | 1.0 | 3.8 | 59.3 | 0.7 % | 1.00 follows |
| rightUpperArm | 0.6 | 1.3 | 27.6 | 0.0 % | 0.99 follows |
| rightLowerArm | 1.1 | 3.4 | 71.9 | 1.0 % | 1.00 follows |
| leftUpperLeg | 9.3 | 20.1 | 31.4 | 46.2 % | 1.50 OVER-DRIVEN |
| leftLowerLeg | 14.8 | 28.9 | 42.1 | 70.5 % | 2.32 OVER-DRIVEN |
| rightUpperLeg | 9.2 | 20.6 | 31.6 | 47.7 % | 1.40 |
| rightLowerLeg | 15.1 | 35.8 | 45.5 | 71.0 % | 2.25 OVER-DRIVEN |
| **trunk** | **16.7** | **19.3** | **22.1** | **100.0 %** | **0.27 UNDER-DRIVEN** |

Run B (456.webm x5, subject at ~3 m) is weak evidence and is recorded as such: 80-90 % of its frames
fall below the confidence floor and are excluded. It replicates the trunk result (median 15.2 deg,
follow 0.41) and the leg over-drive (1.08-1.58); its arm numbers are dominated by source noise at
3 m and should not be quoted as a retarget verdict.

### The arms cannot validate the coordinate mapping, so the debug skeleton did

`kalidokitAimArms` is ON: the arm retarget AIMS the bone along the same landmark pair the metric
compares against, so a mapping error would cancel on both sides and near-zero arm error would prove
nothing. The non-aimed channels (trunk, legs) are exactly the ones showing error — an uncomfortable
coincidence that had to be ruled out.

Cross-checked in WORLD space against `PoseDebugSkeleton`, which shares none of the driver's
conditioning (`joints[i].localPosition = landmark.Position * scale`) and which the operator confirms
tracks correctly:

```text
L-ARM   skeleton(12->14)=(-0.095, -0.977,  0.192)   avatar=( 0.111, -0.975,  0.192)
        angle after the known X mirror = 0.9 deg          <- the mapping is right
TRUNK   skeleton         =( 0.086,  0.989, -0.122)   avatar=( 0.000,  0.989,  0.148)
        angle = 16.3 deg, X mirror changes nothing        <- confirms 16.7 deg independently
```

The two paths agree to within 0.4 deg on the trunk. The metric is validated.

### What the trunk number actually is

The bound VRM's REST hips->upperChest chain is itself `(0.0000, 0.9883, 0.1522)`, **8.76 deg off
vertical**. That is the trunk channel's floor: a perfect retarget cannot report 0 deg. Across four
snapshots the avatar's trunk sat **0.00 / 0.23 / 0.40 / 1.20 deg from that rest vector** while the
subject's trunk moved through ±0.15 in both X and Z. The avatar's lateral component was
**identically 0.000** every time.

So the trunk is not merely biased — the LEAN channel does not move. Two mechanisms, both already in
the code and both deliberate:

* **Lateral lean is multiplied by zero.** `spineRoll = pose.Spine.z * torsoRoll`, and
  `kalidokitBodyTorsoRoll = 0` (ADR-059 re-enabled PELVIC roll; trunk roll stayed off). Hence
  X = 0.000 exactly.
* **Sagittal lean is high-passed away.** `rawPitch = Atan2(trunk.z, vert)` is measured against an
  ADAPTIVE baseline with `spineBendBaselineTau = 8 s`, and only the residual is applied. A SUSTAINED
  lean decays to nothing as the baseline follows it; only fast transients survive. This is the
  measured follow ratio of 0.27, and it is also the mechanism behind F-26 §4.2 "the avatar cannot
  sit": a seated posture is precisely a sustained lean.

Neither is a broken line of code. Both are decisions whose cost had never been measured. It is now.

### What this metric does NOT measure — four limits, stated so they are not forgotten

1. **Direction, not position.** Two poses can agree on every bone direction and still put the hands in
   different places, because the avatar's bone lengths are its own (F-26 §3). Endpoint error is a
   different measurement and is deliberately not conflated with this one.
2. **It cannot see trunk YAW.** Axial twist leaves the hip->shoulder line unchanged. The 16.7 deg is
   LEAN only, and says nothing about the torso-yaw blocker of F-16/F-17/F-18. In the sampled frames
   the subject's own shoulder yaw varied only ~8 deg — inside the documented dead zone — so this clip
   cannot test yaw in either direction.
3. **The video path has no stereo.** Every joint's depth is synthesised (F-23). This exercises the
   RETARGET, not the sensor. It has not yet been run on an OAK-D session.
4. **"Frames" are RENDERED frames (~118 Hz), not independent samples.** 5187 rendered frames carry
   ~1300 pose packets at 30 Hz. The percentages are over what was drawn — which is what the viewer
   sees — but they are not 5187 independent observations.

One avatar, one rig, two clips. Nothing here is a product claim.

### Decision

**Accept the instrument. Change nothing in the retarget on the strength of one session.** The
measurement's purpose is to make the next retarget change PROVABLE rather than arguable; spending it
immediately on an unvalidated fix would waste it. The baseline above is the before-number.

**DO NOT**, on this evidence:
- do not quote a single "fidelity score" — the analyser refuses to produce one, on purpose;
- do not quote Run B's arm numbers as a retarget result (80-90 % of it is below the confidence floor);
- do not treat the arms' ~1 deg as proof the retarget is right — for an AIMED channel that is close to
  a tautology, and it is silent about the mapping (see the skeleton cross-check);
- do not read the trunk result as a yaw result;
- do not re-enable `kalidokitBodyTorsoRoll` or lengthen/shorten `spineBendBaselineTau` as a "quick
  fix" — both are ADR-level decisions with their own reasons, and the cost of changing them has not
  been measured either.

---

## ADR-063 — Tracking output is a SUGGESTION: a humanized skeleton layer sits between the filter
## and the retarget, and it is measured to be latency-free on 99.6 % of poses

**Date:** 2026-09-16  **Status:** ACCEPTED, DEFAULT ON. Video path only; never run on stereo.
**Changes nothing** in the tracking, the filtering, P0-1, P1-1/2/3, Arm V2 or Torso V5/V6.

### The gap

The tracker reports 33 positions independently, every frame, with no knowledge that they belong to
one body. Nothing in it prevents a forearm from changing length, an elbow from folding through
itself, a knee from bending forwards, or a dropped joint from being reported AT THE ORIGIN — which in
a hip-centred frame is the mid-hip. Downstream the avatar's bones are rigid and can only be AIMED,
never placed (F-26 §3), so each of those becomes an aim error the retarget is structurally unable to
absorb. P0-1's `LimbGate` holds a limb whose CONFIDENCE is low, and P1-3 refuses to interpolate
across an invalid endpoint, but nothing anywhere asked whether the pose was a possible human shape.

### Decision

`VirtualMirror.Core.Humanize.HumanizedSkeleton`, injected in `AppBootstrap.UpdateTracking()` after
filtering and after the debug skeleton is drawn, before anything retargets. Stage order is
reject → clamp → rebuild structurally → enforce joint limits → blend re-acquisitions; rebuilding
before rejecting would bake a bad sample into the bone lengths.

Four constraints on the design, each protecting something that already works:

1. **No smoothing filter.** The project owns three (the sidecar's One-Euro, P1-1's tracker, P1-3's
   interpolation). A fourth would be latency for its own sake. Every stage here is either a per-frame
   geometric projection — no memory, cannot lag — or fires only on a fault.
2. **Confidence passes through untouched.** A held joint keeps the confidence it arrived with, so
   P0-1 still sees an unobserved limb as unobserved. What changes is the consumers that read
   landmarks with NO gate — the Kalidokit torso solve reads 11/12/23/24 with no validation at all.
   Fabricating confidence here would silently disable a protection that works.
3. **It does not own torso yaw.** V5's composition and V6's wrap guard own yaw POLICY (ADR-037 /
   ADR-045). The twist limit is 90 deg — past any human spine — so it can only refuse the impossible.
4. **It does not touch the root position.** Walk/jump translation has its own smoothing and its own
   open question in F-21; conflating them would tangle two investigations.

### Measured (video path, 2462 poses, both streams from the SAME frames in ONE pass)

| | before | after |
|---|---|---|
| bone-length deviation, median | 0.0568 m | **0.0181 m** (−68 %) |
| bone-length deviation, worst frame | 0.2526 m | 0.0520 m (−79 %) |
| landmark jump, median | 0.0123 m | 0.0119 m (−3 %) |
| landmark jump, p99 | 0.0708 m | 0.0844 m (**+19 % worse**) |
| poses no STATEFUL stage touched | — | **99.6 %** |

The p99 regression is real and is not dismissed: holding a bone at a fixed length moves its child
whenever the tracker's length estimate wanders, and at the tail that costs more motion than it saves.

**Tests:** 32/32 synthetic + fault-injection (C#, headless), 18/18 analyser self-test (Python).

### THERE IS DELIBERATELY NO SHOULDER CONE

A 175 deg cone about the trunk's down axis was implemented first and MEASURED firing on exactly one
pose in a full arm sweep — arms straight overhead — moving the elbow 24 mm. That is the single pose
F-26 §4.1 records as this project's worst avatar failure. A cone is the wrong shape for a joint that
genuinely reaches 180 deg of elevation. The self-test carries arms-overhead as a permanent regression
guard against re-adding one.

Hyperextension is likewise NOT enforced: three landmarks give an interior angle that is
direction-agnostic and capped at 180 deg, so a forearm bent 10 deg the wrong way and one bent 10 deg
the right way are the same three distances. Only the knee case, whose sign against the body's forward
axis is unambiguous, is corrected.

### Two defects the measurement found that 32 passing tests did not

Both were found by the NUMBERS, after the synthetic suite was green, and neither was visible by eye.

* **The body-forward axis was ambiguous and the knee fix fired on 84 % of poses.** Forward was
  `cross(hipLine, up)`, whose handedness depends on which side landmark 23 sits on — and the
  mirror/swap conditioning happens LATER, inside the driver. With the sign inverted a normal leg is
  reflected every frame. Now derived from the FEET (toes are anterior to heels on every human, and the
  vector is ~0.12 m), falling back to the nose, and every front/back test STANDS DOWN when neither is
  observed. Measured: 84.0 % -> 1.8 %.
* **The velocity clamp was measuring its own output and fired on 80 % of poses.** `lastAccepted`
  stored the REBUILT position, so the structural rebuild moved a joint, the next frame's step was
  measured from the moved position, and the clamp fired on the difference it had created itself. The
  clamp is a statement about the TRACKER, so it now compares tracker to tracker. Measured: 80.3 % ->
  0.4 %, and the median landmark jump went from +269 % WORSE to −3 %.

A third, in the instrument: the recorder's bone metric borrowed `BoneLengthCalibrator`, whose mirror
lookup indexes a different bone table, and was averaging the shoulder span against a forearm. Fourth
wrong instrument this month; caught because the number was implausible on the RAW stream too.

### DO NOT, on this evidence

- do not claim this improves the AVATAR. It measures the POSE. Whether a more human pose produces a
  more human avatar is F-26 §11's metric's question, and running it with this layer on and off is the
  obvious next measurement — it has not been done.
- do not treat the video-path result as a sensor result. There is no stereo on that path (F-23), and
  the rejection, hold and recovery stages had NOTHING to do on that clip: 0 holds, 0 NaN, 0
  collapses, 0 impossible angles in either stream. They are demonstrated by fault injection, not by
  this recording.
- do not quote "99.6 % latency-free" as a latency measurement of the PRODUCT. It says this layer's
  stateful stages were idle; it says nothing about the three smoothing stages upstream of it.
- do not tighten the speed ceilings to make the clamp "do more". They are outlier rejectors set above
  what a person in front of a mirror produces, on purpose. A ceiling a real arm can reach is a defect.

### ADR-063 addendum (same day) — the avatar A/B was run, and F-27 does NOT improve avatar fidelity

F-26's fidelity metric, ON vs OFF vs ON-with-bone-lengths-OFF, 4000+ live frames per arm, reference =
the RAW tracked pose in every arm (`SetFidelityReference`, added for this and documented there as a
deliberate handicap):

```text
ON vs OFF                0 bones better, 3 worse, 6 unchanged   (arms +1.0 to +1.3 deg)
ON-no-bone-len vs OFF    0 better, 0 worse, 9 unchanged
```

**The protections are free and the cost is entirely bone-length normalisation** — isolated by
measurement rather than inferred. The ~1 deg on the arms IS the correction: ARM V2 aims the bone at
its input (follow 1.00 in every arm), so against a raw reference the error equals the distance the
layer moved the arm. Whether that is an improvement cannot be decided by this metric, because
deciding it would need a reference better than the raw tracker and none exists.

**F-27 does not fix the leg over-drive** (follow 1.26-2.06 in all three arms). That belongs to the
Kalidokit leg solve, not to the bone lengths fed into it.

**Verdict: on CLEAN tracking the layer is neutral-to-slightly-negative for avatar fidelity.** Its
value rests on the fault cases this clip does not contain. That is not a reason to switch it off - it
buys the protections at no measured cost - but it IS a reason never to claim it improves the avatar.
The next honest test is footage containing real dropouts and occlusion, or a live OAK-D session.

**Also corrected here:** F-26 section 11.5's "the trunk lean channel is dead" was measured against a
RUNTIME `kalidokitBodyTorsoRoll = 0`; the scene ships 1. Same clip, same subject trunk motion, freshly
opened scene: trunk median **16.7 -> 7.3 deg**, follow **0.27 -> 1.29**. The trunk is not dead in the
shipped configuration. This incidentally validates the standing `tasks.md` item "torsoRoll 0 -> 1
applied, NOT validated".

---

## ADR-064 — Unity launches the sidecar itself; the venv and the model are INSTALLED on the target, not copied into the build

**Date:** 2026-09-16
**Status:** Accepted
**Supersedes:** nothing. **Related:** ADR-016 (sidecar out-of-process), F-20B (supervisor).

### Context

Two problems, one decision.

1. The user had to start `sidecar_supervisor.py` in a terminal before pressing Play. That is a manual
   step in front of every demo and it is easy to forget.
2. `python-sidecar~` is invisible to Unity — the trailing `~` is exactly what keeps it from being
   imported — so a built player contained **no sidecar at all** and could not track.

The proposal on the table was to manage the sidecar with **pm2** and copy the whole sidecar tree,
venv included, into `StreamingAssets`. Both halves were rejected, and the reasons are worth keeping
because they are not obvious.

### Decision 1 — spawn `sidecar_supervisor.py` directly from C#; do not add a process manager

`SidecarProcessLauncher` (`Runtime/Tracking/OakD/`) runs the existing supervisor, and
`AppBootstrap.StartSidecar()` calls it from `StartTracking()`.

It uses the project's own **`NativeProcess`** (`Assets/Modules/Utility/StartExternalProcess.cs`), NOT
`System.Diagnostics.Process`, which is stripped under IL2CPP — the configuration this product ships
in. `NativeProcess` wraps `CreateProcessW`/`ShellExecuteEx` directly and already provides argument
passing, stdout/stderr redirection with events, `Id`, `HasExited`, `ExitCode` and `WaitForExit`, so
the launcher needed no capability it does not have. `VirtualMirror.Tracking.asmdef` gains a
reference to the `Utility` assembly.

**Why not pm2:**

* It duplicates the supervisor, which is already a tested watchdog (F-20B, 35/35): restart-on-death,
  crash-loop detection with backoff, environment validation, heartbeat and ready timeouts, and a
  process-tree kill that is deliberately scoped to its own child.
* The "start it only if it is not already running" behaviour already exists, and is implemented
  better than pm2 does it. `acquire_single_instance_lock()` holds a TCP bind on 127.0.0.1:8897 for
  the process lifetime, with the reasoning recorded in the code: *a bound socket cannot be stolen by
  a stale PID file after a crash, which a lock FILE can.* pm2 is PID-file based.
* **The lifecycle is inverted.** pm2 is a daemon; its purpose is surviving parent death and reboots.
  The hard requirement here is the opposite — the sidecar MUST die when Play mode exits, because a
  stranded python holding UDP 8899 blocks the next run. Adding a survival daemon to solve a
  must-die problem means fighting the tool, and `pm2 delete` becomes one more thing that has to fire
  reliably.
* It would add Node.js and a global pm2 install as a third runtime to a Unity + Python product, and
  route sidecar logs into pm2's own store instead of `ILogService`.

**Teardown is a Win32 job object, not just `OnApplicationQuit`.** The child is assigned to a job with
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so the kernel kills the whole tree whenever Unity dies — a
crash or Task Manager included, where no managed teardown path runs at all. `TeardownServices()`
(already idempotent, already called from both `OnApplicationQuit` and `OnDestroy`, so it covers
domain reload) disposes the launcher as its first act.

Measured during teardown: killing the supervisor alone is not enough. The live tree was
supervisor → sender → **a third process**, so `taskkill /F /T` is load-bearing — and doubly so on
`NativeProcess`, whose `Kill()` calls `TerminateProcess` on the single PID and would strand the
actual UDP producer while it still held the camera. The taskkill is scoped to our own PID; a broad
`taskkill /IM python.exe` would kill the user's unrelated interpreters.

Because `NativeProcess` keeps its process handle private, the job object re-opens a handle from the
PID via `OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE)`. Raw kernel32 P/Invoke is unaffected by
IL2CPP stripping — it is the managed `System.Diagnostics` surface that is unavailable, not interop.

**Attach, never double-spawn.** The launcher probes lock port 8897 with a `TcpListener` bind before
spawning. Held means a supervisor is already running — started by hand, or left over — so the
launcher reports `AttachedExternal` and leaves it alone. Two producers on one UDP port interleave
poses from different sessions, which reads as violent jitter rather than as a misconfiguration.

`--allow-port-listener` is passed unconditionally. Unity in Play mode holds UDP 8899, and the
supervisor's port probe binds the destination port to detect a stale producer, so without the flag it
reads our healthy listener as "port in use" and refuses to start.

### Decision 2 — ship the sidecar SOURCE in StreamingAssets; install the venv and model on the target

A post-build step (`Editor/SidecarBuildPostprocessor.cs`) copies the 83 top-level `.py` files
(~1.2 MB), `requirements.lock.txt` and `setup_sidecar.ps1` into `StreamingAssets/Sidecar/`.

**Why not keep those files in `Assets/StreamingAssets` directly:** 83 imported files means 83 `.meta`
files churning on every edit and two copies of every module free to drift. Copying at build time
keeps `python-sidecar~` the single source of truth and guarantees the player carries the same
revision the editor just ran.

**Why the venv is NOT copied — the decisive fact.** A Windows virtualenv is not relocatable. Verified
on this machine:

```text
pyvenv.cfg    home = C:\Program Files\Python310     (absolute, external)
Lib/          site-packages ONLY - no stdlib
Scripts/      python.exe, pythonw.exe - no python310.dll
```

A copied venv therefore works only on a machine that already has Python 3.10.0 at that exact path,
and fails confusingly everywhere else. Shipping it would have produced a build that appeared
self-contained and was not — the same class of silent-wrong-environment failure as the PATH
interpreter trap below. `setup_sidecar.ps1` builds the venv on the target instead.

Also excluded: `rtmw3d-x.onnx` (369 MB, changes roughly never, would cost minutes per build) and
`depthai_blazepose/` (86 MB — the superseded Phase-1 path, named only in a docstring in
`wholebody_udp_sender.py` and never imported).

**The trade-off, stated plainly:** builds stay small and always self-consistent, but the target needs
a one-time setup pass and Python 3.10. This is not a double-click install. PyInstaller would remove
the Python dependency entirely and is the natural v2 step; it was not attempted for v1 because
`onnxruntime-directml`, `depthai` and `opencv` are all awkward to freeze.

### Decision 3 — a missing venv is a HARD failure, never a fallback to `python` on PATH

`SidecarPaths.Validate()` refuses to launch and names the missing file. It must never fall back to
the interpreter on PATH: that resolves a different environment whose onnxruntime cannot load its GPU
provider and drops to CPU **without raising** — 183 ms/frame instead of ~31. Correctness is
unaffected, so the only symptom is that everything is mysteriously slow and every timing number is
wrong. A loud failure is strictly better than a silent 6x slowdown, and
`SidecarPathsTests.Validate_MissingInterpreterExplainsWhyThereIsNoPathFallback` pins the explanation
into the error message so it cannot be quietly dropped.

`setup_sidecar.ps1` checks `ort.get_available_providers()` and fails setup if `DmlExecutionProvider`
is absent — moving that discovery from production to install time.

### Consequence found while doing this

`requirements.txt` had `onnxruntime-directml` **commented out** while the working venv had it
installed. Anyone setting up from that file would have got a sidecar with no inference at all.
`requirements.lock.txt` (exact `pip freeze`, 12 packages) is now the file the setup script installs;
`requirements.txt` remains the human-readable statement of intent.

### Evidence

```text
supervisor accepts the launcher's exact CLI      SIDECAR READY in 14.5 s
second supervisor while first holds the lock     SUPERVISOR ABORT another supervisor already owns lock_port=8897
TcpListener probe on 8897 while held             SocketException -> AttachedExternal, no spawn
taskkill /F /T on the supervisor                 3 processes terminated, 8897 + 8899 released, no orphan
3x start/stop cycle                              3/3 passed, ports free before and after every cycle
SidecarPathsTests                                11/11 passed
```

**Not yet demonstrated:** the in-editor Play → exit → Play cycle, and a real build run outside the
editor. The launch/teardown mechanism above was exercised directly rather than through Unity.

---

## ADR-065 — The sidecar root is the PRODUCTION PATH only; harnesses move to tools/ and tests/

**Date:** 2026-09-16
**Status:** Accepted
**Related:** ADR-064 (packaging), AGENTS.md §9.

### Context

The sidecar root held 83 flat `.py` files: two entry points, nine production modules, and 72
investigation harnesses accumulated across F-16 to F-27. Nothing distinguished the frame loop that
ships from a one-off replay script written for a single afternoon's measurement, and ADR-064's build
step copies the root directory, so every one of those 72 files would have shipped to players.

### Decision

The root keeps the production path and nothing else:

```text
sidecar_supervisor.py  wholebody_udp_sender.py          (entry points)
rtmw3d_pose  oak_depth  f18_portrait  smoothing  joint_tracker
kinematic_recovery  target_ownership  pose_validation  f21_cue_display
```

Those nine are exactly what `wholebody_udp_sender.py` imports. **They were deliberately NOT moved.**
Relocating them would mean editing the import block of the shipping frame loop, which is the one
file this reorganisation had no business destabilising for a cosmetic gain.

Everything else moved into `tests/` (8) and `tools/` (64), grouped by **dependency cluster** rather
than by F-number: `capture/` holds f16+f18+f19 together because they import each other heavily,
alongside `deployment/`, `ownership/`, `validation/`, `video/` and `diagnostics/`.

### The import problem, and why the shim exists

Python puts the *script's own* directory on `sys.path`, not the project root. A harness moved to
`tools/ownership/` therefore cannot `import rtmw3d_pose`, and 39 of the 72 files import at least one
project module. Three cross-group edges also survive the grouping
(`f21_supervisor_integration` → `f20b_deployment_verify`, `f24_jitter_session` → `f21_live_protocol`,
and the capture cluster's internal web).

Each affected file carries a two-line prelude that walks **up** to `_sidecar_path.py` and imports it;
the shim then puts the root and every `tools/` group on `sys.path`. Walking up rather than counting
`dirname()` calls is deliberate — the first version hardcoded three levels, which was right for
`tools/<group>/x.py` and one level too high for `tests/x.py`, and it failed immediately. Searching
for the marker file is correct at any depth and stays correct if a file moves again.

Harnesses are still run directly (`python tools\ownership\f21_walkin_protocol.py`), so the commands
recorded in the F-reports change by **path only, not by form**. That matters: those commands are how
the deleted raw captures get regenerated, and switching to `python -m` would have invalidated every
one of them.

### On merging "similar" scripts

The request that prompted this also asked to merge scripts with similar logic. Measured before
acting, across all 83 files:

```text
whole-file similarity >= 55%     0 pairs
duplicated functions (>= 6 lines) 1  -  load(), 13 lines, in f16_consolidate and f16_edge_check
```

**The similar-looking names are not duplicates.** `f21_video_replay` and `f22_video_replay`,
`f21_perf_ab` and `f22_perf_ab`, `f21_adversarial` and `f22_adversarial` are per-investigation
harnesses over different subsystems — ownership versus pose validation — and merging them would
conflate two independent evidence chains to save nothing.

The one real duplicate was hoisted to `f16_configs.load_rows()`, which both files already had reason
to import. Both keep a three-line `load()` alias so all ten call sites read unchanged.

### Consequence for the build

`SidecarBuildPostprocessor` copies `TopDirectoryOnly`, which now means it ships 12 files instead of
83 — the production path plus the import shim — and no harness or self-test reaches a player. That
was previously accidental; it is now the documented contract, and AGENTS.md §9 records that a file
left at the root ships whether or not a player needs it.

### Evidence

```text
self-tests, before the move   75/75, 37/37, 39/39, 22/22, f26 28, f27 18
self-tests, after the move    75/75, 37/37, 39/39, 22/22, f26 28, f27 18   + test_surface_depth 32/32
production chain imports      all 9 modules + wholebody_udp_sender import clean
compileall (excl. .venv)      exit 0
cross-group harness imports   9 sampled, all resolve
supervisor end-to-end         3/3 start/stop cycles, READY ~13 s, no orphan, ports released
duplicate scan after merge    0 pairs, 0 duplicated functions
```
