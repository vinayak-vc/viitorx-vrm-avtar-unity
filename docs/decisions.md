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
