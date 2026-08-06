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

## Capture source note (M2)

Development input is a **video file** (`VideoFileCaptureService`, `sampleVRMFiles/sample video.mp4`) rather than a live webcam, selected via `AppBootstrap.useVideoSource`. `WebcamCaptureService` exists and is swapped in by flipping that flag. Both implement `ICameraCapture`; tracking providers are agnostic to the source.
