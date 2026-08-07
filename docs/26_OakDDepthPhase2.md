# 26 — OAK-D Phase 2: Measured Per-Keypoint Depth

> Status: **SPEC / not yet built** (2026-08-07). Turnkey plan for the "real front/back fix".
> Depends on: ADR-016 (OAK-D B2 sidecar), [25_PosePipeline](25_PosePipeline.md), [16_Threading](16_Threading.md).
> **Hardware-in-the-loop: every step below must be iterated against a live OAK-D. Do not treat as done until validated on the device.**

## 1. Goal

Replace the sidecar's current **Phase-1** landmark source (BlazePose GHUM `landmarks_world`, an *estimated* Z ≈ MediaPipe quality) with **measured** per-keypoint depth from the OAK-D stereo sensor. This removes the monocular front/back ambiguity ("hands behind the body") and the metric-scale guesswork at the source — the whole reason ADR-016 chose a depth camera.

**Unity does not change.** The provider (`OakDUdpPoseProvider`) already consumes 33 hip-relative-metre landmarks over UDP and runs them through `PoseSpaceConverter` → `PoseFrame`. Phase 2 only changes *what numbers the sidecar puts in the `lm` array*; the wire contract, the provider, and the retarget/IK path stay as-is.

## 2. Current state (Phase 1 — baseline)

`oak_sidecar/depthai_blazepose/udp_pose_sender.py`:
- Runs `geaxgx/depthai_blazepose` **edge** mode (`BlazeposeDepthaiEdge`, NN + post-proc on-device), `xyz=True`.
- Streams per frame: `{"lm": [[x,y,z,vis] ×33], "xyz":[hipX,hipY,hipZ]}`.
- `lm` = `body.landmarks_world` (metres, mid-hip origin, GHUM). `xyz` = `body.xyz` = the on-device SpatialLocationCalculator result for **one** ROI (mid-hip region) — the only measured-depth value today.

So exactly **one** point (the hip anchor) is currently measured; all 33 landmark Z values are GHUM estimates.

## 3. Chosen approach

### Option A — host-side back-projection (PRIMARY, do this first)

Stream the aligned **depth map** out to the host, sample it at each landmark pixel, and back-project through the RGB camera intrinsics to metric camera-space XYZ. Reasons: smallest change to the geaxgx edge graph (one extra `XLinkOut`), all the per-keypoint math is plain Python/numpy on the host so it iterates in seconds, and it reuses the exact mm→metre→hip-centre logic the removed B1 provider already proved (`OakDPoseProvider.ParseInto`, see git history / ADR-016).

Pipeline changes (in the geaxgx edge pipeline builder — `BlazeposeDepthaiEdge.py`, or a subclass/patch kept in `udp_pose_sender.py` so the clone stays clean):
1. The stereo `depth` node already exists for `xyz=True`. Add `depth.setDepthAlign(dai.CameraBoardSocket.CAM_A)` (align depth → RGB) if not already aligned, and route it to a new `XLinkOut("depth")`.
2. Host: create an `OutputQueue("depth")`; each frame pull the aligned depth frame (`uint16` mm, shape `[Hd, Wd]`).
3. Fetch intrinsics **once** at startup: `calib = device.readCalibration(); fx,fy,cx,cy = calib.getCameraIntrinsics(dai.CameraBoardSocket.CAM_A, Wd, Hd)` (use the depth-frame resolution, since depth is aligned to RGB the RGB intrinsics scaled to `Wd×Hd` apply).

Per-frame, per keypoint `i` (0..32):
4. Get the landmark pixel in **source-image** space. geaxgx exposes `body.landmarks` (Nx3, pixels in the letter-boxed/rotated NN crop) and the rect used to crop; convert back to the full video-frame pixel `(u_v, v_v)`. `body.landmarks_padded` / `body.rect_points` + the existing `mediapipe_utils` helpers give this mapping — reuse geaxgx's own transform rather than re-deriving. Then scale video-frame px → depth-frame px: `u = u_v * Wd/Wv`, `v = v_v * Hd/Hv`.
5. Sample depth robustly: take a KxK window (K=5) around `(u,v)`, drop zeros (holes/invalid), require ≥ `minValidPx` (e.g. 6) non-zero; `Z = median(valid)` in mm. If too few valid → mark this keypoint **unmeasured** (see §5).
6. Back-project: `Xc = (u - cx) * Z / fx`, `Yc = (v - cy) * Z / fy`, `Zc = Z` (camera space, mm; X right, Y down, Z forward — DepthAI convention).
7. Convert mm→m: divide by 1000.

Hip-centre (matches Phase-1 origin so Unity's converter sees the same frame):
8. Compute measured mid-hip = mean of measured L-hip (idx 23) & R-hip (idx 24) if both measured, else fall back to `body.xyz`/1000. Subtract from every measured keypoint → hip-relative metres.

Emit: fill `lm[i] = [Xc, Yc, Zc, vis]` with the measured hip-relative metres where measured; §5 for the rest.

### Option B — on-device 33-ROI SpatialLocationCalculator (alternative, only if A's host bandwidth/sync is a problem)

Add a `SpatialLocationCalculator` fed 33 ROIs built each frame from the NN landmark output via a `Script` node, so the device returns 33 measured XYZ directly (like `body.xyz` but per keypoint). More device-graph work (dynamic ROI config from a Script node in edge mode is fiddly) and slower to iterate. Prefer A unless depth-map XLink bandwidth at the chosen `--frame_height` proves too costly.

## 4. Landmark-pixel mapping caveat (the likely time-sink)

The single most error-prone step is **§4-step-4**: mapping `body.landmarks` (NN crop space, possibly rotated) back to the aligned-depth pixel grid. Get this wrong and depth is sampled at the wrong body part → garbage Z. Validation aid: draw the sampled pixels back onto the RGB preview (geaxgx renderer) and eyeball that each dot sits on the right joint before trusting the numbers. Use geaxgx's own `mediapipe_utils` inverse-rect transform — do not hand-roll.

## 5. Invalid / occluded keypoints → GHUM fallback

Depth has holes (edges, occlusion, out-of-range < ~20 cm or too far). Per keypoint:
- **Measured** (enough valid depth px): use back-projected hip-relative metres, `vis` = BlazePose visibility.
- **Unmeasured** (hole / out of range): fall back to the Phase-1 GHUM `landmarks_world[i]` (hip-relative already), and **lower its confidence** (e.g. `vis * 0.5`) so the retargeter/One-Euro filter trusts measured joints more. This keeps the avatar whole when depth drops out instead of collapsing a limb to origin.

Optional debug field (Unity ignores unknown keys): add `"src": [0|1 ×33]` (0=GHUM, 1=measured) so a future HUD / log can report measured-coverage %.

## 6. Wire contract (unchanged for Unity)

Still `{"lm": [[x,y,z,vis] ×33], "xyz":[...], optional "src":[...]}`, `lm` = **hip-relative metres**, same axes as Phase 1. `OakDUdpPoseProvider.ParseInto` needs **no change** — it already reads `lm[i] = [x,y,z,vis]` → `PoseSpaceConverter.ToUnity` → `PoseFrame`. HUD stays "OAK-D 3D (UDP sidecar)".

## 7. Validation plan (device attached)

1. Sidecar prints measured-coverage each ~90 frames (extend the existing status line with `measured=NN/33`).
2. Overlay sampled px on RGB preview (§4) — joints land on the body.
3. Unity Play: turn to profile, put a hand **behind** the back — the avatar hand must go behind (the Phase-1 failure). Compare against MediaPipe/Sentis side-by-side.
4. Tune `poseFlipX/Y/Z` for mirror/axis (same as Phase 1; measured Z sign may differ from GHUM Z sign — expect to re-check `poseFlipZ`).
5. Watch FPS: depth XLink + host sampling adds cost; if it drops, lower `--frame_height`, raise the sampling stride, or move to Option B.

## 8. Risks

- **Depth-RGB alignment / resolution mismatch** → wrong sample site (§4). Mitigate with the overlay check.
- **Min-range**: OAK-D depth is unreliable closer than ~20–40 cm; hands near the lens will read as holes → GHUM fallback (§5) covers it but front/back near the camera won't improve.
- **Bandwidth**: streaming the full depth map at high `--frame_height` costs USB bandwidth/latency; keep the frame small (200–400 h) or switch to Option B.
- **Edge-mode rect transform**: geaxgx's landmark→image mapping in edge mode must be reused exactly; a version drift in the clone would break §4.
