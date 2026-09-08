# Virtual Mirror — AI Handoff

Last updated: 2026-09-08  
Purpose: next agent can continue without re-deriving context.

---

## 📍 PROGRAM STATUS

| Stage | Status | Evidence |
|---|---|---|
| **P0** — LimbGate + limb caps | **COMPLETE** (accepted after the live human run) | [`P0_ACCEPTANCE_2026-09-07.md`](P0_ACCEPTANCE_2026-09-07.md) |
| **P1-1** — per-joint temporal tracking + plausibility | **COMPLETE** | [`P1_1_TRACKER_2026-09-08.md`](P1_1_TRACKER_2026-09-08.md) |
| **P1-2** — real-time frame freshness / throughput | **COMPLETE** (incl. human A/B) | [`P1_2_FRESHNESS_2026-09-08.md`](P1_2_FRESHNESS_2026-09-08.md) |
| **P1-3** — Unity timestamped pose buffer + interpolation | **COMPLETE** | [`P1_3_POSE_BUFFER_2026-09-08.md`](P1_3_POSE_BUFFER_2026-09-08.md) |
| **Next** | recovery / palm / foot work — NOT STARTED | — |

**Cumulative latency:** camera→avatar was ~162 ms → **~102 ms** (P1-2 removed ~100 ms of queue
staleness; P1-3 spends 40 ms of that on interpolation, halving stutter).
**Tests: 47/47 Unity EditMode + 37/37 Python green.**

---

## 🎞️ P1-3 Unity pose buffer = PASS (2026-09-08, `P1_3_POSE_BUFFER_2026-09-08.md`)

`Runtime/Core/PoseBuffer.cs` — bounded ring of 16 timestamped poses; Unity renders at
`now − poseInterpolationDelayMs` and interpolates between the bracketing pair instead of re-applying
latest-wins (measured **17.8 applies per packet**, **41.5% of render frames frozen**).

- Stutter (delta CoV) **2.286 → 1.070 (−53%)**; frozen render frames **41.5% → 23.6%**.
- Measured with `stream_motion.py` — identical deterministic input in both runs; a human cannot repeat
  a performance closely enough to measure interpolation quality.
- **40 ms chosen over 55 ms**: 55 ms bought only 3 more points of smoothness for 15 ms more latency.
- `poseInterpolationDelayMs = 0` restores the original path exactly (the buffer is not even populated).
- **Safety rule preserving P0-1:** a landmark is NEVER position-interpolated across an invalid endpoint.
  The sidecar sends a dropped joint as `[0,0,0,0]`, so lerping valid→zero would put it half-way to the
  ORIGIN — the exact F-01 collapse. The valid position is carried and confidence becomes `min` = 0, so
  the LimbGate still holds. Tested.
- No extrapolation; duplicate / out-of-order / backwards-timestamp packets rejected.
- **Known gap:** hands/palm quats still use the old rate-limited slerp path (not buffered), so body and
  hands sit on timelines ~40 ms apart.

---

## ⚡ P1-2 frame freshness = PASS (2026-09-08, `P1_2_FRESHNESS_2026-09-08.md`) — CURRENT STATE

**Root cause of the 131 ms camera→host latency, found and fixed.** `DataOutputQueue.get()` returns the
**OLDEST** packet. With inference (~21 ms) slower than the 30 fps sensor, the host queue sat full at
`maxSize=4` → **4 × 33.3 = 133 ms** of pure staleness (measured live: 131.4 ms). Nothing was slow; the
system was working on old frames.

**Fix (host-side only, ~20 lines in `wholebody_udp_sender.py`):** one blocking `get()` for liveness,
then `tryGetAll()` and keep only the NEWEST; depth chosen by **closest timestamp** to the selected RGB
frame. Queue size unchanged. Toggle `--latest-frame` (default ON) / `--no-latest-frame`.

| metric (loaded ~18.8 fps) | FIFO | latest-frame |
|---|---|---|
| frame age median | 130.95 ms | **30.71 ms** |
| camera→UDP median | 183.45 ms | **85.44 ms** |
| RGB/depth sync max | 54.53 ms | **12.09 ms** |
| fps | 18.8 | 18.7 |

- **RGB/depth pairing got BETTER, not riskier** — the old ordinal pairing was mis-associating depth by
  **54.56 ms** (~1.6 frames). That is audit **F-09**, quantified for the first time.
- Compute unchanged (43.29 vs 43.88 ms) → the win was queue wait, not processing.
- 180 s soak: **no accumulation** (median 31.11 → 31.52 ms).
- New permanent diagnostics in `sender_log.jsonl`: `frameAgeMs`, `queueDepth`, `staleDropped`,
  `rgbDepthSyncMs`. Console prints `age=NNms stale=N`.
- `--inject-load-ms` is **TEST-ONLY** (reproduces the subject-present condition without a human);
  never set it in production.

**Still needs a human subject:** real-motion A/B and the per-joint jitter regression (Phases 5–6).
Run `run_p12_ab.bat`. **Methodological warning:** latest-frame skips frames, so *per-frame* displacement
rises without any jitter increase — that comparison must be **velocity-normalised**, not per-frame.

**Do NOT implement the Unity pose buffer yet** — P1-2 only guarantees Unity now receives fresh data.

---

## 🧩 P1-1 per-joint temporal tracking = PASS (2026-09-08, `P1_1_TRACKER_2026-09-08.md`) — CURRENT STATE

Solves the verified P0 finding **CONFIDENT-BUT-WRONG LANDMARKS** (a hidden wrist held a wrong position
for 840 frames at conf ~0.63). New `python-sidecar~/joint_tracker.py`: ONE reusable `JointTracker` per
joint (TRACKED/WEAK/PREDICTED/LOST) + `SkeletonTracker`. Sits AFTER the P0 smoother, BEFORE PoseFrame.
**P0 LimbGate is untouched and remains the final safety layer** — a LOST joint has its emit confidence
zeroed, which looks exactly like a real occlusion to Unity. No Unity C# changed for P1-1.

- **37/37 unit assertions** (`test_joint_tracker.py`), **6/6 adversarial cases detected**
  (`evaluate_p1.py`), incl. the frozen-wrist case → 0.0000 m residual error.
- **Latency 0.085 ms** median / 0.121 ms p99 for 12 joints — 8× under the 1 ms budget.
- Legitimate dancing: **median/p95 unchanged**, peak displacement −18…−27% on 4 joints, worst
  regression +6.1% (5.5 mm). PREDICTED 0.3%, LOST 0.0% → no false rejection.
- Toggle: `--tracker` (default ON) / `--no-tracker` for A/B.

**The check that actually matters is FROZEN detection** — a stuck joint has near-zero residual, speed
and acceleration, so no conventional plausibility test can see it. That was the real observed failure.

**Two bugs the tests caught (both would have shipped silently):** reacquisition was *teleporting* via
the coherence path (aggregate stats looked fine); and the neighbour/segment check was acting as a veto,
rejecting 4.6–13.5% of legitimate motion. Both fixed and re-measured.

**Known weak spot:** a sustained high-confidence teleport longer than the 6-frame prediction window ends
in LOST with slow recovery during fast motion → needs **P1-3/P1-5**.

*(Superseded: P1-2 turned out to be frame freshness, not velocity refinement — see the status table.)*

---

## 🚦 P0 acceptance = CONDITIONAL PASS — 14 criteria still NOT TESTED (2026-09-07, `P0_ACCEPTANCE_2026-09-07.md`) — READ THIS FIRST

P0-1 (`LimbGate` confidence hold) and P0-2 (0.35 m limb cap + legs into the hold set) are implemented.
Full acceptance report: [`P0_ACCEPTANCE_2026-09-07.md`](P0_ACCEPTANCE_2026-09-07.md).

**Machine-verifiable half PASSES:** compile clean (0 `error CS`); **33/33 EditMode tests** incl. all 6
`LimbGateTests`; shipping config confirmed; `LimbGate` traced on the live path (7 hops, source-cited);
OAK-D RGB+depth+intrinsics operating @30 fps; **packet loss 0.00 %** (0/1046 and 0/54 285, 0 out-of-order);
**end-to-end latency MEASURED ≈ 44.9 ms** median camera→avatar (audit had estimated 80–150 ms);
**bone lengths CONSTANT to 0.000000 m** over 12 194 applied frames → *squashing does not occur; the
correct label is LIMB ROTATION INSTABILITY.*

**P0-1 UNITY GATE IS NOW PROVEN (2026-09-08).** Scripted occlusion injection
([`inject_occlusion.py`](../python-sidecar~/inject_occlusion.py) → [`verify_gate.py`](../python-sidecar~/verify_gate.py))
streams the real UDP contract with occluded joints emitted as `[0,0,0,0]` — **with the sidecar not running
at all**, so nothing is inferred from a Python-side hold. Result over 3/5/8/12/20-frame occlusions × 4 limbs:
**20/20 PASS**, gate held for the full duration, **0 zero-rotations across 2765 held frames (no origin
collapse)**, 0 spurious holds on non-occluded limbs, re-acquired every time, bone lengths constant.

**Still NOT TESTED (9 criteria): human motion quality.** Every camera capture recorded `measured_body = 0/33`
(presence probe 2026-09-08: **0/359 frames, max 0/33 joints**) — no human has ever been in frame. Real
confidence-*decay* profiles, spike magnitudes under fast motion, the 0.35 m cap's responsiveness, palm
behaviour, trunk/root post-P0 comparison and avatar visual behaviour all need a person.
→ Run [`python-sidecar~/guided_capture.py`](../python-sidecar~/guided_capture.py) — prompts through blocks
A–J on a countdown and reports every metric **per block**.

**Gotchas found:**
- **Do NOT run the unfiltered EditMode suite** — it aborts the Editor via a MediaPipe native
  `CHECK failed: 1 == ChannelSize()` (`image_frame.cc:362`) on the prebuild domain reload. Scope runs to
  the `VirtualMirror.Tests` assembly. Not a P0 defect.
- ~~`limbConfidenceThreshold` absent from the scene~~ **FIXED** — now serialized at
  `Scenes/Bootstrap.unity:173` (`limbConfidenceThreshold: 0.3`).
- **`model_log` does not start until the VRM avatar binds (~7 s after Play).** Any test injecting data
  before that gets *no observations* — which is NOT a failure. A first gate run scored a misleading
  "16/20" for exactly this reason; warm-up is now 12 s. **Absence of observation ≠ evidence of failure.**
- **L-palm rotation is pinned at the 15°/frame rate limiter** (median == p95 == 14.98°) in the baseline —
  it is slewing at max rate continuously, not tracking. Pre-existing, unexplained, re-check live.
- Diagnostic-only instrumentation added (`DIAG-ONLY` comments, +85 Unity / +20 sidecar lines, additive
  logging only). Baseline logs preserved in `python-sidecar~/pipeline_logs_baseline_audit/`.

**Do NOT start P1** until the capture above is done. Then P1-1 (per-joint temporal state) first —
rationale in §14 of the report.

---

## 🧭 Retarget regression DIAGNOSED + torso-yaw damped (2026-08-11, ADR-027 + `RETARGET_AUDIT_2026-08-11.md`)

The user's screen recording showed the **debug skeleton correct but the avatar arms/legs/torso wrong** (twisted waist, wrong facing, arms dragged/asymmetric, "inhuman" fingers). Full diagnosis in [`RETARGET_AUDIT_2026-08-11.md`](RETARGET_AUDIT_2026-08-11.md). **Root cause:** Kalidokit derives torso facing from the shoulder/hip-line DEPTH separation (2-pt `rollPitchYaw`), which is hypersensitive to OAK depth noise at range (measured: rest yaw −10°, excursions −119°, ±180° flips). **ADR-025's un-flatten exposed it** → the chest over-twists and drags the arms (M1 was stable only because the trunk was flattened = frontal-locked). An interim "×π over-rotation" claim was **WRONG** — Kalidokit's `rigHips` ×π too; the port is faithful there.

**User chose "keep 360° turning but damp it."** Implemented (ADR-027): a **torso-yaw conditioner** (rate-limit 140°/s rejects the ±180° flips + follows real turns; soft dead-zone 8–22° → frontal when small; low-pass) on hips+spine yaw, live-scaled by **`kalidokitTorsoYawScale`** (0 = frontal-lock fallback, 1 = full); plus **restored Kalidokit per-bone dampeners** (Hips 0.7 / Spine 0.45 / Chest 0.25, was 1.0 + 0.5/0.5). Verified offline on the real logs: rest yaw −10°→−1°, flips 2→0, jitter −39%. Compile 0/0.

**FRONTAL-LOCK is now the DEFAULT (2026-08-11, ADR-027 amendment).** The user's post-fix capture confirmed the conditioner worked (hips-yaw max 90°→2.27°, STABLE; arms/bend/position track at ~1.5–1.8 m; both-arms-up now symmetric). The residual "hands go backwards" = the avatar turning to face away, which correlates with **distance** (median torso-yaw 22° at <1.8 m vs 64° at 2.1–2.4 m; raw skeleton folds at 2.66 m). User walks to 2.5 m+ in the M2 room → chose **frontal-lock**. `kalidokitTorsoYawScale` default = **0** (applied live). Arms/bend/walk/fingers unaffected. Re-enable turn = raise the knob while **standing <~1.8 m**; the proper walk-around fix is a **distance-gated yaw** (deferred, see ADR-027 amendment). **R4 RESOLVED — NO arm bug (live injection 2026-08-11):** asymmetric-pose injection on the live driver showed both arms raise correctly when both raised and each side responds independently — the arm solver + `flipQuat` are sound (minor ~25% side magnitude asym only). The "one arm up" was the **torso twist dragging the arms** (R1, fixed by ADR-027). **DO NOT touch `kalidokitBodyFlipQuat`.** Still open: fingers (curl axis/sign + noisy source). **Live re-verify (user):** re-run `run_capture.bat`, stand ~2 m; if the torso still swings on a noisy/far setup, lower `kalidokitTorsoYawScale` (0 = frontal-lock).

---

## 🩹 Milestone-2 waist-bend FIXED — adaptive baseline (2026-08-11, ADR-026)

The user's live M2 OAK run reported: **avatar doesn't bend at the waist (skeleton does), still jitters, and leans forward when standing upright.** Diagnosed from that run's OWN pipeline logs (`python-sidecar~/pipeline_logs/`, 5337 frames) — **the bend signal is in the data** (upright p50 ≈ +5.5° / bend p95 ≈ +19° at <2 m), but the ADR-025 spine-bend used a **single fixed neutral captured on the first frame** — which was the 4.24 m all-zero-Z startup garbage → neutral `0°` → a **constant ~5.5° rest lean**, and the neutral **couldn't track the distance drift** (upright reads +5.5° @2 m, −0.6° @3 m) as the user walks. Noise std ~6° + 35°/frame spikes buried the ~14° bend → "no bend".

**Fix (ADR-026):** replaced the fixed neutral with a **slow ADAPTIVE baseline (high-pass, `tau≈8 s`, median-seeded)** + **rate-limit (250°/s)** + **light low-pass** on the derived pitch. Upright → ~0 at any distance; real bends pass; jitter down. **Verified offline on the user's actual logs:** jitter 0.56→0.36 °/frame (−36 %), per-distance rest median ≈0 (was +5.5° @2 m), bends ±11–12° preserved. Compile 0/0. **Also:** `run_capture.bat` was forcing `--min-cutoff 0.7`, overriding ADR-025's 0.5 still-jitter default → restored to 0.5.

**NEEDS THE USER'S LIVE RE-VERIFY (OAK):** re-run `run_capture.bat`, **stand ~2 m** (last capture was median 2.65 m, 58 % > 2.5 m, ~21 % depth coverage — too far; that alone hurt the bend/turn + jitter). Confirm: upright avatar is straight (no rest lean), waist bends when you bend, less jitter. Live knobs: **`kalidokitSpineBendScale`** (raise for a more visible bend; flip sign if it bends backward), **`kalidokitSpineBendBaselineTau`** (raise to hold a sustained bend longer). Press **C** standing upright to re-seed. Then send the new `compare_logs.py` output + a short clip.

---

## 🎯 POC STATUS + DECISION (2026-08-10, ADR-024) — read this first

Pipeline logging (ADR-023) + two live OAK captures settled the long wrist saga: the remaining instability is **source hand-data quality**, not a Unity bug. **Decision: ship the POC with wrist-orientation OFF (`wristRotationWeight=0`, persisted); fingers still curl.**

| Capability | State | Notes |
|---|---|---|
| Body pose (torso/arms/legs) | ✅ POC-ready | shoulders ~0.8 cm/frame, arms track T/up/out |
| Facing (yaw) | ✅ frontal-stable | hips-yaw p95 0° (frontal-locked flatten, ADR-023) |
| Finger curls | ✅ works | per-finger curl from the OAK hand stream |
| Face expressions | ✅ works | MediaPipe blendshapes (needs a working RGB webcam) |
| Position/root | ✅ works | avatar walks/jumps with the user (measured hip xyz) |
| **Wrist bend** | ⏸️ **deferred** | source palm 7.7–10°/frame + 174° re-acquire snaps at 2–3 m → OFF for POC; needs a close-range hand source |
| Body turn-tracking | ⏸️ deferred | single-front-camera limit |

**Path to re-enable the wrist (post-POC):** a close-range hand source (dedicated webcam MediaPipe Hands, or user much closer, or a hi-res OAK RGB crop) → set `wristRotationWeight` back up; the roll-free apply already works. See ADR-024.

**Sidecar limb-depth smoothing added (2026-08-10, ADR-020 amendment):** the residual jitter is **depth (z) on the limbs** (wrist z jitters ~5× its x,y — invisible in the 2D preview, hence "stable in Python, unstable in Unity"). `smoothing.py` now applies a heavier One-Euro to limb depth (`--depth-min-cutoff 0.3 --depth-beta 0.1`, arms+hands only) + **hold-on-dropout** (`--max-hold-frames 8`, uses the last-good limb value instead of the noisy zrel fallback that caused the 8-12 m spikes; bounded, no freeze). Unit-tested (0.12→0.006 m/frame z-jitter). **Biggest win is still ~2 m framing** (the 19:00 capture was at hip 3.99 m).

**Open question for the user:** in the 2026-08-10 19:00 capture the avatar rendered **grey/untextured** — confirm whether that's intended (a placeholder/scene-view) or a material/render regression on the VRM to investigate separately.

---

## 🩺 Kalidokit body path RE-DIAGNOSED on the user's 2026-08-09 recording — "helicopter"/poor accuracy (2026-08-10, IN PROGRESS)

The user sent a screen recording of the **video test config** (`useKalidokitBody=1`, MediaPipe pose, sample dance video) showing the avatar still helicoptering + arms not tracking. Diagnosed **live in Unity (MCP 6400, in Play)** with a **canonical-pose harness**: inject known poses via reflection on the live `KalidokitControlRigDriver.Apply` + read RAW-skeleton world positions (reliable because `execute_code` runs synchronously on the main thread, so it reads back before the next frame overwrites).

**⚠️ VERIFICATION METHOD LESSON:** screenshots are **UNRELIABLE for injected poses** — the app's Awaitable update loop (ADR-006) re-applies the live pose on the next render even with `component.enabled=false`, and paused screenshots are stale. **Trust the synchronous geometry reads, not screenshots, for injected poses.** ADR-022's "CONVENTION SOLVED + DATA-VERIFIED" was verified only via *bones-are-non-identity-and-changing* — never that the avatar reproduced the pose. **That was a false pass.**

**Findings (geometry-proven, 2026-08-10):**
1. **The euler→normalized-bone mapping is SOUND.** Clean canonical input yields a correct T-pose, and arm UP/FORWARD/SIDE all track (hand goes above head / +Z / out to the side). `kalidokitBodyFlipQuat=2` + `eulerSigns(1,1,1)` is essentially right. **STOP hunting flipQuat/sign combos — the mapping is not the bug.**
2. **`kalidokitBodyMirror=1` (was the live setting) is a real bug.** It reflects the INPUT skeleton (negate-X + swap L/R) before the Kalidokit solve; on an asymmetric pose this produced **~36° spurious forearm roll + 0.5 m hand error** vs a clean reflection (measured). Exactly ADR-018's "do NOT mirror the input skeleton for a rotation-based retarget." **FIX APPLIED + persisted: `kalidokitBodyMirror=0` in `Bootstrap.unity`.** A true L/R flip, if ever wanted, must be OUTPUT-side (swap target bones + sagittal-reflect), not input reflection — pending refactor. See ADR-023.
3. **The residual poor accuracy on THAT recording is INPUT QUALITY**, not the retarget — mono video + distant dancer → weak/noisy MediaPipe Z (the ADR-014 monocular ceiling). The clean-input tests prove the retarget tracks when given good directions. **Real accuracy win = OAK-D depth input / closer framing**, not more mapping tuning.

**Audit done (2026-08-10):** a 9-agent adversarial port audit found the **arm/hips/legs/KMath port FAITHFUL — no solver bugs** (so the helicopter is NOT solver code, confirming the input-quality + mirror diagnosis). It confirmed one real bug in the **finger** driver: **A1 — finger curl used one shared axis+sign for both hands → one curls in, the other hyperextends.** **FIXED** (`KalidokitControlRigDriver.ApplyHandFingers` now takes a per-hand `sideSign`, left −1 / right +1; `kalidokitFingerCurlAxis` still tunes both). Compiles 0 errors. A2 (thumb on the four-finger axis) deferred (low, rig-dependent, needs a close hand source).

**Net fixes this session (all compile-clean; ADR-023):**
1. `kalidokitBodyMirror=0` persisted (removes the input-reflection forearm twist).
2. Per-hand finger `sideSign` (A1, port-audit) — both hands curl inward.
3. **OAK-D hand curve → FIXED + geometry-verified:** the OAK whole-body stream sends body-pose hand points (17-22) as `(0,0,0)`, so Kalidokit's hand-euler was `FindRotation(wrist, origin)` = garbage. `KalidokitControlRigDriver.Apply` now gates the Hand bone on valid hand points → rests straight when absent (zero-pts→Hand.local=(0,0,0); populated→driven).
4. **OAK-D forearm forward-bend + "body rotation gone" → FIXED (sidecar):** `--flatten-trunk` zeroed only the 4 trunk joints' Z (leaving elbows measured → false forward arm-bend; and collapsing shoulder/hip Z-separation → lost yaw). **Fix (corrected 2026-08-10 iter-2):** `wholebody_udp_sender.py` zeroes the shoulders'+hips' Z (upright trunk + stable FRONTAL facing) and shifts each arm by ITS shoulder's Z first, so the arm segment vectors are preserved (no false bend). An earlier "shift the whole upper body, leave hip-Z measured" attempt REGRESSED the facing into a ~90° PROFILE — the Kalidokit hip-yaw is hypersensitive to hip-line Z (proven in Unity: ±0.10 m hip-Z → 40°, ±0.25 m → 64°). Frontal-locked by design now (turning = single-camera limit). See ADR-023. py_compile clean. **The debug line-skeleton confirmed this is the RETARGET side, not tracking** (skeleton frontal + matching the user; avatar in profile).
5. **Live debug line-skeleton (user request):** new `Runtime/Rendering/PoseDebugSkeleton.cs` draws the RAW tracked pose as LineRenderer bones + joint dots (wrists/hand-points highlighted) beside the avatar; driven each frame by `AppBootstrap.RenderDebugSkeleton`, toggled by `showDebugSkeleton` (**on by default now**), placed via `debugSkeletonOffset`/`debugSkeletonScale`. Lets you validate tracking by eye vs the VRM (skeleton-right-but-avatar-wrong ⇒ retarget; skeleton-wrong ⇒ tracking/sidecar). Rendering verified in Play. `VirtualMirror.Rendering.asmdef` now references `VirtualMirror.Core`.
6. **AppBootstrap inspector cleaned:** tunables grouped under `[Header]`s (Tracking Source / Kalidokit Body / Pose Mapping / Features / Debug) with `[Tooltip]`s; plumbing + backend detail hidden via `[HideInInspector]` (still serialized/functional); **removed the superseded arms-only Kalidokit raw-bone path** — `useKalidokitArms` + `kalidokitAxisSigns`/`kalidokitLerp`/`kalidokitDriveHand` + the `KalidokitRetargeter` field/wiring (the whole-body control-rig path replaces it; `KalidokitRetargeter.cs` is now an unused file, recoverable via git). Compile 0 errors.

**OAK-D is the real path and the body now tracks well (T-pose reproduced) — input quality was the ceiling, confirmed.** **User re-run:** restart the sidecar (flatten fix) → re-enter Play → hands straight, arms un-bent, rotation back. **WRIST BEND — now DONE (2026-08-10, ADR-023):** `KalidokitControlRigDriver.ApplyWrist` applies the ADR-021 roll-free swing (palm forward vs captured neutral) to the RAW Hand bones AFTER `ProcessRuntime`, fed by the OAK 21-pt hand stream. Live-weighted by `wristRotationWeight` (0 disables), C key re-captures neutral. Verified in Unity (35° palm → 24.4° hand, roll-free). **Still OPEN:** body turn-tracking (facing is frontal-locked — single-camera limit).

**PIPELINE LOGGING added (2026-08-10) — to diagnose "stable in the Python window, unstable in Unity".** Three logs aligned by a new frame `seq`: `sender_log.jsonl` (sidecar `--log-dir <dir>`), `recv_log.jsonl` (Unity received+converted) + `model_log.jsonl` (applied avatar bones) — Unity writes the latter two when `pipelineLogging` is on (**now ON by default**; `pipelineLogDir` defaults to the sidecar's `pipeline_logs`). `compare_logs.py --dir <dir>` aligns by seq and prints per-stage frame-to-frame **jitter** so instability localizes to SEND / WIRE / RETARGET / WRIST-APPLY (self-tested on synthetic logs: correctly flags a spinning palm and hip-yaw jitter). Files: `wholebody_udp_sender.py` (seq/`t` + `--log-dir`), `OakDUdpPoseProvider.cs` (`recv_log` + `LastSeq`), `AppBootstrap.cs` (`model_log`), `compare_logs.py`. **Capture (one-shot):** with Unity in Play (pipelineLogging on), run **`python-sidecar~/run_capture.bat`** — starts the sidecar with `--log-dir pipeline_logs --show`; press **`q`/ESC** in the preview to stop, and it prints `compare_logs.py` automatically. **Enhanced diagnostics (2026-08-10):** logs now carry elbows, per-frame **distance (`hipZ`)**, **coverage (`cov`)**, and gimbal-free model **forward vectors**; `compare_logs.py` reports **per-axis x/y/z jitter** (depth vs image-plane), a >2.5 m "too far" flag, dropout %, per-stage fps, worst depth-spike seq, and an auto-verdict — this is how "wrist depth = 5× image-plane" + "you're at 4 m" were nailed. **Pending (task 10):** analyze a real capture to fix the user's 4 obs (wrist-spin, jitter, left-edge skeleton, model≠skeleton).

---

## ✅ Full-codebase audit + fix pass — CODE COMPLETE (2026-08-07 session 2); PLAY verify pending

A 5-track read-only audit produced [`docs/AUDIT_2026-08-07.md`](AUDIT_2026-08-07.md) (findings + `file:line`
+ fixes + a **Progress** section) and a verification plan
[`docs/AUDIT_TESTPLAN_2026-08-07.md`](AUDIT_TESTPLAN_2026-08-07.md). **Root cause of the recurring
twist/jitter/leg/mirror/finger bugs: the same responsibility is duplicated across the Unity app and the
Python sidecar** (mirror ×2, smoothing ×2, uprightness ×3, config ×2) + a dead IK path. Core remedy:
single-owner-per-transform.

**✅ ALL 27 fix tasks now CODE-COMPLETE + automated-verified (2026-08-07 session 2, Unity MCP on port 6400).**
Session 1 wrote the Unity C# blind (no MCP bound). Session 2: (1) fixed the one compile blocker —
`RotationFromVectors.cs` called `SharedUp(...)` but only `SafeUp` existed; wrote the missing helper (one
up well-separated from BOTH directions). (2) Finished every REMAINING item: **H2** config single-source
(removed the dead, divergent `AppSettings` schema so the `[SerializeField]` fields are the sole source),
**H5** IK toggle (`EnsureIkSolver` builds+binds on enable), **M1** lazy face/hand toggles
(`EnsureFaceProvider`/`EnsureHandProvider`), **M10** filter-dt gate (filter once per NEW frame via
`TimestampSeconds`), **M16** sidecar `zrel` depth-hole fallback (bench-verified), and all **LOW-A/C/D**
remainders (HUD real fields, shared converter, `PoseSpaceConverter` volatile, Sentis input-dispose +
output-by-index, hands-reset-on-toggle-off, camera dropdown hidden off-webcam, `KeypointSmoother`
default, fps counter, SimCC conf note). **Mirror single-owner = resolved-by-design** (sidecar `--mirror`
off + Unity owns the live flip; the audit's "gate the Unity toggle off" was deliberately NOT done — it
would kill the only live control).

**Verification done (automated):** Unity **compile 0 errors / 0 warnings**; **EditMode 21/21**; sidecar
**`py_compile` clean** (7 files); **`bench_m16.py`** passes (zrel fallback + H7 gate). See the test plan's
**Results** + **Sign-off** sections.

**⏳ What remains = PLAY verification (the user's — needs the OAK-D attached + eyes on the avatar).** Not
runnable headless (MCP Play-mode freezes unfocused + can orphan the `DontDestroyOnLoad` AppBootstrap).
Steps for the user:
1. Run the whole-body sidecar: `cd python-sidecar~ && .venv\Scripts\python wholebody_udp_sender.py --model <path-to>\rtmw3d-x.onnx` (add `--show` for the OAK preview window). Confirm `[wb] frames=… sent=… fps~…` lines with a body in view.
2. In Unity: tick `useOakUdpTracking` on AppBootstrap, Play → HUD "OAK-D 3D (UDP sidecar)", avatar tracks.
3. Walk the test plan §B **PLAY** rows + §C symptom clip (upright/untwisted, stable-when-still, legs, fingers, arms, live FPS).
4. **Live-tune (parameters, not code):** if occluded limbs poke the wrong way in depth, flip `ZREL_SIGN` in `wholebody_udp_sender.py` (or run `--no-zrel-fallback`); confirm mirror-X via the live checkbox; tune `positionScale`/`positionSmoothing`.

---

## 🎥 Post-audit user test-video fixes — waist twist / wrist / jitter (2026-08-07, session 2)

The user recorded an OAK-D whole-body PLAY test after the audit and reported three issues; I watched it
frame-by-frame (sidecar preview real-pose vs avatar) and fixed all three. **Each got an ADR so it does
NOT get re-litigated next session** — read ADR-019/020/021 before touching torso/smoothing/wrist.

1. **Waist twist (real retarget bug) → FIXED, ADR-019.** Standing straight/front-facing, the avatar's
   chest yaw-twisted vs the hips, varying with arm raises. Cause: the Spine basis took its yaw from the
   **shoulder line** (moves when arms move). Fix: both torso bones now take yaw from the horizontally-
   projected **hip line**; the Spine keeps torso-up only for the bend → no yaw-twist relative to the hips.
   Trade-off: chest-vs-pelvis axial twist not reproduced (unreliable anyway). `HumanoidPoseRetargeter`.
2. **Jitter / "smoothing gone" → FIXED (single-owner kept), ADR-020.** The audit's M3/M18 made the OAK
   path bypass Unity's `jointFilter` (single-owner = sidecar); the lone sidecar One-Euro was too light,
   so jitter showed. Fix: **do NOT re-add a Unity stage** — lowered the sidecar default `--min-cutoff`
   1.0 → 0.5 so it's smooth out of the box. Tune `--min-cutoff 0.3` if still jittery / stand ~2 m
   (depth is coarse past ~2 m; the user stood ~2.4–3 m). `wholebody_udp_sender.py`.
3. **Wrist "directional only" → RE-ENABLED, ADR-021 (supersedes ADR-018 disable).** ADR-018 had disabled
   the OAK wrist because the raw palm basis spun at distance. Fix: temporally slerp-smooth the palm
   quaternion in `OakDUdpPoseProvider` (`WristSmoothing=0.35`) and make `wristRotationWeight` **live-
   tunable during Play** (push each frame). Best ~2 m; set weight 0 live to disable — **do not re-disable
   in code**. `OakDUdpPoseProvider.cs`, `AppBootstrap.cs`.

**Verification:** Unity compile 0/0, EditMode 21/21, sidecar `py_compile` clean.

**Round-2 (2026-08-07, after the user's first PLAY test of the above):** three follow-ups —
- **Wrist helicoptered** → the re-enable's full-orientation delta fed the noisy palm ROLL into a continuous
  spin. **Fixed = ROLL-FREE wrist** (`ApplyWrist` now uses `FromToRotation` of the palm forward axis only —
  no roll DOF, structurally can't spin). ADR-021 amendment. Do NOT restore a full-orientation wrist delta.
- **Reaction too slow** → I'd lowered `--min-cutoff` but left `--beta` tiny (laggy on motion). **Fixed =
  raise `--beta` 0.02 → 0.4** (reaction speed), `--min-cutoff` 0.5 → 0.7 (stillness). Both live-tunable per
  sidecar run. ADR-020 amendment.
- **Back-facing twist** → single-camera front/back ambiguity; documented as a KNOWN LIMITATION on ADR-019
  (out of scope for a front-facing mirror; needs multi-view / measured-depth Phase-2). Not chased.

**⚠️ I had to force a recompile while the user was mid-Play → it domain-reloaded and aborted the OAK receive
thread; I then Stopped Play to run EditMode tests. User must STOP Play, RESTART the sidecar (to pick up the
new `--beta`/`--min-cutoff` defaults), and re-enter Play for a clean test.**

**PLAY-verify (user, OAK-D, ~2 m):** (a) front-facing arm raises → no torso twist; (b) hold still → steady,
fast move → snappy (tune `--beta`/`--min-cutoff` live); (c) bend/point a wrist → follows without spinning
(lower `wristRotationWeight` live if too strong).

---

## 🦾 Kalidokit-ported arm solver — the 70%→95% path (2026-08-07, ADR-022)

After a 2nd test still showed the forearm spinning, root-caused decisively: it's **vector-FK's guessed limb
roll**, not the hand (a roll-free hand can't fix a spinning forearm) and **not the engine** (the data is
clean; every engine needs the same landmark→rotation math). User chose **"Port Kalidokit into Unity"** over
leaving Unity. Kalidokit (MIT) derives limb roll from GEOMETRY (bend + plane), so roll is measured not
guessed — the proven approach behind webcam VTuber apps, and our OAK 3D makes it *more* robust.

**Built (arms first, compiles 0/0, EditMode 27/27):**
- `Runtime/Retargeting/Kalidokit/KMath.cs` — faithful port of Kalidokit's Vector math (findRotation,
  angleBetween3DCoords, rollPitchYaw, normalizers) + `KMathTests` (6, locking known values).
- `Runtime/Retargeting/Kalidokit/KalidokitArmSolver.cs` — `calcArms` + `rigArm` verbatim.
- `Runtime/Retargeting/KalidokitRetargeter.cs` — applies UpperArm/LowerArm/Hand as `rest * Remap(euler)`.
- `HumanoidPoseRetargeter.SetArmsExternallyDriven` + `IsArm` → FK yields ONLY the arms (torso+legs stay FK).
- `AppBootstrap` toggle **`useKalidokitArms`** (default OFF, A/B) + live knobs `kalidokitAxisSigns`
  (default (-1,-1,1)), `kalidokitLerp`, `kalidokitDriveHand`.

**⏳ THE live tune (user) — the axis convention.** Kalidokit euler is three-vrm convention (right-handed);
Unity bones are left-handed. `kalidokitAxisSigns` maps between them and is the one thing needing a live
pass (like `poseFlipX`). Steps: tick **`useKalidokitArms`** on AppBootstrap in Play → the arms will move but
likely on wrong axes → try the ±1 sign combinations on `kalidokitAxisSigns` (and `kalidokitLerp` ~0.3–0.6)
until arms track cleanly and the forearm no longer spins. All Inspector-live (no recompile). The solver math
is unit-tested — if it looks wrong, it's the signs, not the math.

**Next after the arm convention is dialled in:** port Kalidokit's `HandSolver` (per-finger + wrist) and
`calcHips`/`calcLegs` (spine/legs) to move the whole body onto the proven solver; retire vector-FK roll.
See ADR-022.

### Whole-body Kalidokit via the normalized control rig (2026-08-07, ADR-022 amendment) — the direction that unsticks the per-bone tuning
Because raw-bone application needs per-bone axis tuning, the whole body now applies to UniVRM's
**normalized control rig** (`Vrm10Runtime.ControlRig`, same VRM1.0 normalized bones as three-vrm) → ONE
global three→Unity convention for every bone. Ported the rest of Kalidokit (`calcHips` rot, `calcLegs`,
spherical/`rollPitchYaw2` in `KMath`, `KalidokitPoseSolver`); new `KalidokitControlRigDriver` drives the
whole skeleton; loader gains `GenerateControlRig` (load-time). Behind **`useKalidokitBody`** — when on,
FK/IK/hand-curl are bypassed; face blendshapes still run. **Compiles 0/0, EditMode 27/27.**

**✅ PROCESS-ORDER FIXED + VERIFIED ON VIDEO (2026-08-07).** The T-pose was UniVRM's auto `Process()`
racing our LateUpdate writes. Fixed: driver sets `vrm.UpdateType = None` on bind, `AppBootstrap` calls
`kalidokitControlRig.ProcessRuntime()` at the end of `UpdateTracking` (after body + face). Headless-verified
on the **video source** (OAK not connected): control rig generated, `UpdateType=None`, control-rig bones
NON-identity and changing frame-to-frame (tracking the video), 0 exceptions, no T-pose. `Bootstrap.unity`
saved to the video test config (`useKalidokitBody=true`, `useVideoSource=true`, `useOakUdpTracking=false`,
`useSentis3dTracking=false`, MediaPipe on).

**✅ CONVENTION SOLVED + VERIFIED ON VIDEO (2026-08-07) — `flipQuat=2`, `mirror=true`, `eulerSigns=(1,1,1)`.**
User feedback (flipQuat 0=inside-out, 2=right-but-mirrored) + a data-driven headless check (stepped the
sim, compared the tracked raised arm vs the avatar's raised arm in the same frame) confirmed: at flipQuat=2
the orientation is correct (hands/head anatomically sane, no inside-out), and the new `kalidokitBodyMirror`
toggle flips copy↔reflection cleanly. Set `kalidokitBodyMirror=true` (copy / un-mirrored per the user's
"fix the mirroring"). Whole-body pose renders as a clean walking stride — no T-pose/inside-out/helicopter.
0 exceptions. **Scene saved on the video test config** (`useVideoSource=true`, MediaPipe, OAK/Sentis off).

**✅ RIGHTWARD-LEAN FIXED (2026-08-07).** Cause: the spine euler was applied to BOTH Spine AND Chest →
doubled torso rotation (~45° yaw + ~20° roll = the constant lean). Fix: distribute the spine across
Spine+Chest (0.5 each) + `kalidokitBodyTorsoRoll` (default 0 = upright, no lean; raise for side-lean).
Verified: roll=0, Spine=Chest=(0,6.1,0) distributed. `KalidokitControlRigDriver` / `AppBootstrap`.

**✅ FINGERS driven on the control rig (curl-based, 2026-08-07).** `KalidokitControlRigDriver` binds the 30
normalized finger bones and curls them from the HandFrame (any hand provider), about a tunable
`kalidokitFingerCurlAxis` (default (0,0,-1)), weight `kalidokitFingerWeight`. Verified end-to-end (synthetic
0.8 curl → control-rig + raw-skeleton finger bone rotated). Chose curl over the full 21-landmark Kalidokit
HandSolver because the dance video's hands are too small for MediaPipe Hand (no valid hand frame on it).

**Remaining / next:**
1. **Mirror preference:** shipped `kalidokitBodyMirror=true` (avatar copies you). Real-mirror reflection = set **false**.
2. **Fingers need a close hand source** (webcam close-up / OAK) for real curl data — on the dance video MediaPipe
   Hand finds nothing so fingers stay open. May also need a `kalidokitFingerCurlAxis` tune for flexion direction.
   Full per-joint Kalidokit HandSolver (21 landmarks → HandFrame surgery) is a future refinement.
3. **Side-lean:** if you WANT torso side-bend, raise `kalidokitBodyTorsoRoll` toward 1 (default 0 = upright).
4. **OAK path later:** `useVideoSource=false`, `useOakUdpTracking=true` (keep `useKalidokitBody=true`, `flipQuat=2`).
   Provider-independent (control rig normalizes it).
5. **⚠️ Don't drive Play repeatedly over MCP** — a MediaPipe Glog double-init abort crashed the editor during
   verification (fixed by relaunching). Use a single clean Play + data reads; live screenshots freeze headless.

Files: `Runtime/Retargeting/Kalidokit/KMath.cs`, `KalidokitPoseSolver.cs`, `Runtime/Retargeting/KalidokitControlRigDriver.cs`,
`Runtime/Avatar/Vrm/UniVrmAvatarLoader.cs` (`GenerateControlRig`), `Runtime/Bootstrap/AppBootstrap.cs`.

---

## ⚠️ Python sidecar relocated to a submodule (2026-08-07)

The OAK-D / model Python sidecar now lives in its **own repo** `vinayak-vc/viitorx-vrm-model-python`,
consumed as a **git submodule** of this repo at `python-sidecar~/` (trailing `~` → Unity ignores the
folder, so the model blobs + `.venv` are never imported; venv is `.venv`, Python 3.10). **The old
`oak_sidecar/` at the Unity-project root is DEPRECATED — use the submodule from now on.** Run:
`cd "Assets/Games/viitorx-vrm-avtar-unity/python-sidecar~/depthai_blazepose" && ..\.venv\Scripts\python udp_pose_sender.py`.
The submodule hosts the new **whole-body + measured-depth path (ADR-018), Stage B+C DONE +
hardware-verified 2026-08-07**: `wholebody_udp_sender.py` runs RTMW3D-x on the RTX 3060
(onnxruntime-directml, ~40 ms) over the OAK RGB, fuses OAK stereo depth per keypoint (`oak_depth.py`
back-projection), and streams 33 body + 2×21 hand landmarks (measured metric, hip-relative) + mid-hip
root over UDP at **~30 fps** (551 datagrams / 0 errors, contract-validated). Body + root already drive
the avatar via the **unchanged** `OakDUdpPoseProvider` — just run the new sender instead of the
Phase-1 one. **Unity finger consumption — CODE DONE 2026-08-07** (needs in-editor verify; MCP not bound
here): `OakDUdpPoseProvider` also parses `lh`/`rh` → `HandFrame` (`TryGetHandFrame`); new
`OakDUdpHandProvider` facade; `AppBootstrap` auto-uses OAK hands when the OAK body provider is active.
Verify: `useOakUdpTracking`+`useHandTracking` on, run `wholebody_udp_sender.py`, watch fingers curl
(left/right + wrist axis may need a live tune). **Still TODO:** MediaPipe face blendshapes (needs a
working RGB webcam); live axis/mirror + depth-scale tuning; guide user to ~2 m (depth coarse at 3.5 m). Model ONNX
at `Assets/SentisModel/rtmw3d-x.onnx` (git-ignored, 369 MB). Validation tools: `validate_rtmw3d.py`,
`validate_depth.py`.

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

### IK arm-placement + head-up fixes (this agent, 2026-08-06) — NOT yet play-verified
Reported from a full-body **dance-video** run (VRM1 `Anime-Boy+V-tuber`, IK Arm/Leg Driver ON): "hands not in correct position" (arms hang at the hips, don't follow the dancer's raised arms) + "head looks up". Root causes + fixes (ADR-011):
- **IK targets collapsed to hip level** — `AnimationRiggingIkDriver.UpdateArm/UpdateLeg` placed `target = hips.position + hipsRestRotation * (tipLandmark * scale)`. `tipLandmark` (wrist/ankle) is **hip-centred**, so scaling the whole vector by `avatarLimb/trackedLimb` (~0.3 on a chibi) also shrank the torso→shoulder span → the hand/foot target sank to the hips. **Fix:** anchor at the avatar's own limb-root bone (`UpperArm`/`UpperLeg`) and scale **only** the limb-local offset: `target = limbRoot.position + hipsRestRotation * ((tip - rootJoint) * scale)` (`rootJoint` = tracked shoulder/hip). This **supersedes** the "hand targets at hips-local z +0.28" numbers from the earlier IK-facing section — anchor point changed, `hipsRestRotation` retained.
- **Head pitched up** — the neck FK segment aimed `midShoulder → Nose`; the nose's forward offset, under the tuned Z convention, tilted the neck backward. **Fix:** aim `midShoulder → midEar` (`HumanoidPoseRetargeter.BuildDefinitions`) — centred, near-vertical, no forward bias → head level/forward (SDS-011 §4 nose–ear plane, SDS-007 §4). Trade-off: neck nod dropped for V1.
- **Modified:** `Runtime/IK/AnimationRiggingIkDriver.cs` (Apply/UpdateArm/UpdateLeg signatures now take the limb-root `HumanBodyBones`; `origin`/hips-anchor removed), `Runtime/Retargeting/HumanoidPoseRetargeter.cs` (neck ToJoints Nose→ears).
- **Style:** explicit types, K&R, no `var`/`new()` — AGENTS.md compliant. No public API broken (private method signatures only).
- **Status:** compiles by inspection (call-sites + signatures matched, no stray `origin`); **NOT play-verified** — needs a local Unity session. Earlier audited 3 IK bugs (FK/IK conflict, scale-normalize, `targetRotationWeight=0`) were already fixed in current code and left untouched.
- **Not code bugs (explained to user):** fingers won't move from a full-body dance video (hands too small for the Hand Landmarker — use upper-body framing); VRM legs only have UpperLeg→LowerLeg→Foot→Toes (Humanoid spec, every VRM).

### Head-forward + folded-legs fixes (this agent, 2026-08-06, iter 2) — NOT yet play-verified — ADR-012
Iter-1 fixes were compiled + confirmed live via a webcam run (DLLs rebuilt 11:14 > edits 11:12; arms now track the raised hands). Remaining from that run + fixes:
- **Head still craned up** — the ear-midpoint neck aim (iter 1 / ADR-011) still carried a small backward pitch under the tuned tracking Z sign. Vector-aiming a near-vertical neck can't do reliable head pitch. **Fix:** removed the Neck segment from `HumanoidPoseRetargeter.BuildDefinitions` — the head now rests forward. Revisit head orientation from the **face landmarker head pose** (SDS-007 §4), not pose landmarks.
- **Legs folded by default** — seated/upper-body webcam → lower body occluded → MediaPipe emits collapsed leg world-landmarks with pass-through confidence that leg IK folded onto. **Fix:** `AnimationRiggingIkDriver.UpdateLeg` now requires the ankle to sit ≥ `MinLegDropMetres` (0.35 m) below the hip along torso-up (`TorsoUp` helper) before driving leg IK; otherwise weight 0 → leg rests straight (SDS-011 §8, no manual toggle).
- **Wrist rotation (implemented, needs tuning)** — ADR-013. Palm basis from Hand world landmarks (`MediaPipeHandProvider.PalmRotation`, run through the shared `PoseSpaceConverter`), carried on `HandFrame` (`Left/RightWristRotation`+`Tracked`), applied **delta-from-neutral** to the Hand bone in `HumanoidHandRetargeter.ApplyWrist` (slerp by `wristRotationWeight`, default 0.7; **C** recalibrates neutral; weight 0 disables → fingers still curl). Files: `HandFrame.cs`, `MediaPipeHandProvider.cs` (ctor now takes converter), `HumanoidHandRetargeter.cs`, `AppBootstrap.cs`. **NOT play-verified** — convention-sensitive; expect a live weight/axis tuning pass like `poseFlipX`. Known limit: world-apply may partly double-count forearm roll far from neutral.
- **Modified:** `Runtime/Retargeting/HumanoidPoseRetargeter.cs` (Neck segment removed), `Runtime/IK/AnimationRiggingIkDriver.cs` (`MinLegDropMetres` const, `TorsoUp` helper, leg plausibility gate).
- **Status:** ✅ **PLAY-VERIFIED on the new machine 2026-08-06** (see next section) — head forward, legs not folded, IK position-only, arms track. Wrist (ADR-013) still needs a live webcam tuning pass.

### Live play-verification — new machine (2026-08-06, this agent)
Unity MCP **is** connected here (bridge port **6400**, instance `viitorx-vrm-avtar-unity-base-project@70066c27`), so ADR-011/012/013 were verified in-editor.
- **Build:** clean recompile, **0 errors / 0 warnings**.
- **Startup (0 runtime errors):** all providers start — `MediaPipePoseProvider` (CPU/VIDEO/full), `MediaPipeFaceProvider`, `MediaPipeHandProvider`; avatar auto-loads + binds; IK rig builds. One benign warning only (H.264 timestamp skew from `vid.mp4`, not our code).
- **ADR-012 D1 — head forward (neck undriven):** live `neck localEuler=(0,0,0)`, head `worldUp≈(−0.09,0.99,−0.08)` (≈ world-up, upright/level, not craned). ✅
- **ADR-011/012 — position-only IK:** all 4 `TwoBoneIKConstraint` report `rotW=0.00 / posW=1.00 / weight=1.00`. ✅
- **ADR-012 D2 — legs not folded:** on the full-body dance video (lower body visible) the leg gate passes and legs track in a plausible standing pose (not collapsed). The gate's OFF/seated case (weight→0, rest straight) is by inspection + the `MinLegDropMetres` logic; not exercised by a full-body clip. ✅ (partial)
- **ADR-011 — arms track raised hands (the reported bug):** demonstrated across the full dynamic range. On the video (arms-down frame) both hands sit at hip level (`y-above-hips ≈ 0.00/−0.03`); driving the `FakeBodyTrackingProvider` waving-arms pose, the left hand rose from `−0.14` below the shoulder to **`+0.81` above the shoulder** (`+2.02` above the hips) as the wave peaked — i.e. the hand follows the input from hip level to well above the head, **not** pinned at the hips. ✅
- **ADR-013 — wrist rotation:** NOT verified here. Needs real MediaPipe **hand** detection with hands close to camera (upper-body webcam framing); the full-body dance video's hands are too small and the fake provider leaves the wrist untracked. **Left for a live webcam tuning pass by the user** (`wristRotationWeight` default 0.7; press **C** upright/neutral to recalibrate; set 0 to disable — fingers still curl).

**MCP play-mode caveat (important for the next agent):** driven purely over MCP the Editor advances the play-mode sim only briefly on entering play, then **freezes** (no repaint while unfocused) and can **exit play abnormally**. The `VideoPlayer` likewise stalls (froze at frame 188). To sample a moving pose deterministically use `UnityEditor.EditorApplication.Step()` (N calls advance N frames; it leaves the editor **paused**, so `manage_camera screenshot include_image` errors with "outside Play mode" — set `EditorApplication.isPaused=false` first). Abnormal exits can leak the `DontDestroyOnLoad` `AppBootstrap` — reload the scene (`EditorSceneManager.OpenScene(path, Single)`) to clear orphans before editing serialized fields.

**⚠️ Regression I introduced then fixed (same session):** to run the fake-arms test I flipped `AppBootstrap.useMediaPipeTracking` to `false` via `SerializedObject` in edit mode; it got **persisted to `Bootstrap.unity`** (disk showed `useMediaPipeTracking: 0`), and my "restore to true" mistakenly edited a leaked `DontDestroyOnLoad` orphan instead of the real scene instance. Symptom the user hit: **"only face tracking"** — face+hands ran on MediaPipe but the **body ran on the fake waving-arms provider** (`Body tracking started` with no `MediaPipe pose provider started` line). **Fixed:** reloaded the scene (cleared the orphan), set `useMediaPipeTracking: 1` on the real instance, saved. On-disk now `useMediaPipeTracking: 1`, `useVideoSource: 0` (webcam). **Lesson:** never leave `useMediaPipeTracking=false` on disk; edit serialized scene fields only on the real scene instance (verify `gameObject.scene.name == "Bootstrap"`), and don't rely on edit-mode reflection surviving a play cycle.

**Machine-specific notes (new machine, do NOT hardcode):** repo root `D:\Unity\viitorx-vrm-avtar-unity-base-project`; session runs from the deep package dir. `settings.json` resolves to `C:\Users\Rushit Vaghela\Documents\MyMirror\Settings\settings.json` — **NOT OneDrive-redirected here** (differs from the old machine). `Bootstrap.unity` is set to **webcam** (`useVideoSource=0`, `Logi C270 HD WebCam`); a dance clip `StreamingAssets/Videos/vid.mp4` exists and is wired as `sampleVideoPath` for video mode (flip `useVideoSource` to test off video). The code default `sampleVideoPath` still points at a stale `C:\...\sample video.mp4` that does not exist here — harmless while the scene overrides it. Unity MCP bridge port **6400**.

### Live webcam accuracy test + Stage-1 cheap wins (2026-08-06, this agent) — ADR-014
User ran a real webcam test (stood **far**, full body in frame, pressed **C**) and reported the avatar replicates only **~20–30%** of their motion ("useless"; asked whether to change approach). Agent watched the screen recording frame-by-frame (`ffmpeg` frames vs the camera-feed inset):
- **Body was on real MediaPipe** (HUD `MediaPipe Pose (CPU)`), so this is the pipeline's real quality — **not** the fake-provider regression from earlier.
- **Failure modes:** arms lag badly + often wrong (crossed / "zombie" forearms); when the user clearly raised both hands, the avatar's arms stayed down. Fingers/**wrist dead** (hands too small at distance for the Hand Landmarker). Legs jitter/cross while the user stands still. Whole body **floats/bobs** (no root/ground lock). Head stays forward (neck undriven, expected).
- **Root causes:** monocular MediaPipe 2D + weak Z (documented ceiling) **and standing far from camera** (dead hands + noisy body), plus over-smoothing (`beta=0.02`) adding lag and confidence-gating clamping extremes.
- **Decision (ADR-014): staged — cheap wins now, GPU 3D-pose provider if insufficient.** User approved the staged plan.
- **Stage-1 applied this session:**
  - Pose model **Full → Heavy** — `pose_landmarker_heavy.bytes` (29 MB, Google official) in `StreamingAssets/MediaPipe/`; `poseModelFileName` + scene updated. (Heavy is slower on CPU → watch tracking framerate; revert to `pose_landmarker_full.bytes` if laggier.)
  - **One-Euro filter now live-tunable** — `OneEuroFilter.SetParameters` + `JointFilterPipeline.SetParameters`; the calibration **smoothing sliders were previously dead** (only set fields, never retuned the running filter) and now actually work. Default `filterBeta` **0.02 → 0.2** (less lag). Panel init order was already safe (sliders set before `AppBootstrap` subscribes, so the initial sync doesn't clobber the default).
  - **Framing guidance** = the biggest free win: user must stand **closer / upper-body** for the hands to track at all.
  - T-pose calibration **deferred** (no `Runtime/Tracking/Calibration/` yet) pending the re-test — the observed errors are distance-noise + lag, not proportion.
- **Files:** `Runtime/Tracking/Filtering/OneEuroFilter.cs`, `JointFilterPipeline.cs`, `Runtime/Bootstrap/AppBootstrap.cs` (defaults + slider retune), `Scenes/Bootstrap.unity` (`poseModelFileName=pose_landmarker_heavy.bytes`, `filterBeta=0.2`, `useMediaPipeTracking=1`, `useVideoSource=0`), `StreamingAssets/MediaPipe/pose_landmarker_heavy.bytes` (new, untracked). Compiles clean (0 errors). **Re-test pending** (user, live webcam, closer framing).
- **Next:** if the re-test is still short of "very accurate", start **Stage 2** — GPU 3D-pose `IBodyTrackingProvider` (Unity Sentis / ONNX-DirectML on the RTX 3060). Also candidates: root/hip grounding (stop the float), velocity outlier-reject (leg jitter), T-pose calibration.

### Stage 2 started — GPU 3D-pose provider (2026-08-06, this agent) — ADR-015
User said "start the GPU 3D provider" and chose the **Sentis + single-stage 3D** stack.
- **Foundation DONE + build-verified:** installed **Unity Sentis** `com.unity.ai.inference` **2.6.1** (rebranded from `com.unity.sentis`; namespace/assembly **`Unity.InferenceEngine`**). 0 compile errors after add. API confirmed: `ModelLoader.Load(ModelAsset)`, `new Worker(model, BackendType.GPUCompute)`, `TextureConverter.ToTensor`, readback. Runs on the RTX 3060 via compute.
- **MODEL RESOLVED = RTMW3D (RTMPose3D family), POC/non-commercial OK.** User clarified it's a **POC, no commercial use** (non-commercial licenses fine) and wants a **modern, accurate** model (ThreeDPose is 2020/dated — runs but superseded). Chosen: **RTMW3D** (OpenMMLab, 2024–2025) — real-time **whole-body 3D, 133 keypoints (body + feet + face + HANDS)** → also attacks the dead-hands problem; Apache-2.0. Ready ONNX: HuggingFace **`Soykaf/RTMW3D-x`** → `rtmw3d-x_8xb64_cocktail14-384x288-b0a0eab7_20240626.onnx` (~369 MB, input 384×288). Download URL + SimCC decode + 133→33 `JointId` mapping notes are in **ADR-015**.
- **✅ Feasibility spike PASSED (2026-08-06) — Sentis path CONFIRMED, ORT fallback NOT needed.** Model at `Assets/SentisModel/rtmw3d-x.onnx` (369 MB); Sentis `com.unity.ai.inference` 2.6.1 installed on THIS machine (it was NOT in `Packages/manifest.json` after pull — that manifest is at the Unity-project root, OUTSIDE the git repo, so package installs don't travel with the repo; re-add per machine). Reimport → `Unity.InferenceEngine.ModelAsset`, **0 errors/0 warnings**. Confirmed I/O (ONNX + `ModelLoader.Load`): input `input` (1,3,384,288); SimCC outputs `output`[1,133,576]=x, `1554`[1,133,768]=y, `1556`[1,133,576]=z. Decode: argmax over last dim ÷2.0 → x∈[0,288)/y∈[0,384)/z depth; map 133→33 `JointId`. Input needs ImageNet mean/std normalization. **NEW RESUME POINT: build `SentisPoseProvider`** (below).
- **✅ `SentisPoseProvider` BUILT + COMPILES (2026-08-06, 0 errors).** `Runtime/Tracking/Sentis/SentisPoseProvider.cs`; Tracking+App asmdefs +`Unity.InferenceEngine`; `AppBootstrap` wired (`useSentis3dTracking` off, `sentisModel`/`sentisImageNetNorm=true`/`sentisMetreScale=1.7`); MediaPipe fallback. Pipeline: camera → GPU `Graphics.Blit` resize to 288×384 → CPU readback → NCHW input buffer (ImageNet mean/std, top-down flip) → `Worker.Schedule(GPUCompute)` → **async readback** (`ReadbackRequest`+poll `IsReadbackRequestDone`, frame-drop while in-flight → no main-thread stall) → SimCC argmax ÷2.0 → 133→33 `JointId` (COCO-WholeBody body+feet subset; face/hands left zero-conf) → hip-centred via `PoseSpaceConverter` × `sentisMetreScale`. **ModelAsset already assigned on `AppBootstrap` in `Bootstrap.unity` (scene saved).**
- **✅ LIVE-VALIDATED (2026-08-06, video source `Assets/StreamingAssets/vid.mp4`).** Controlled decode on a real frame: keypoints anatomically correct both norm modes (nose top→shoulders→wrists→hips→ankles bottom, L/R x-split, conf 0.73–0.92); **ImageNet norm gives higher peaks → `sentisImageNetNorm=true` kept**. Live in-pipeline: `SentisPoseProvider` started (GPUCompute), `valid=True`, produces hip-centred Unity metres — **hips@(0,0,0)**, nose y+0.47, shoulders y+0.32, ankles y−0.50 (Y-up), L=−x/R=+x, **conf 0.80–0.94** (all above the 0.5 gate). Avatar renders **posed** (driven), **0 runtime errors**. **Scene now has `useSentis3dTracking=true` + `useVideoSource=true`** (I set them for the test — revert `useVideoSource` to false for webcam). ⚠️ Frame-to-frame **motion NOT confirmed headless** — VideoPlayer doesn't advance under `EditorApplication.Step()` and a real-time run freezes when Unity is unfocused; **verify motion with a real-time focused Play**. Cosmetic: HUD still prints "MediaPipe Pose (CPU)" while Sentis is active — update the label from the active provider.
- **Run 5 observations (2026-08-06, dance video, REAL-TIME recorded by user):** Sentis path **live-tracks** — avatar moves frame-to-frame (arms down → raised/out across frames), head forward, 0 crashes. BUT **motion range is heavily compressed**: arms make small moves while the dancer's are large, legs stay together vs a wide stance — **less responsive than the MediaPipe run**. Ranked causes to fix: (1) **z-depth mis-scaled** — `DecodeFrame` divides z by `InputHeight` like x/y, but RTMW3D's z SimCC is a *root-relative depth* (not pixels; z bins=576≠y bins=768), so 3D is flattened/wrong → limb reach muted. Needs the proper mmpose RTMW3D z decode (root-relative, its own scale), separate from x/y. (2) **over-smoothing** — One-Euro (calm-webcam tuning) damps fast motion; raise beta / lower min-cutoff for the 3D path. (3) **full-frame input** — top-down model expects a tight person bbox; the dancer doesn't fill the 3:4 frame → weaker/smaller keypoints. Add a centre-crop-to-3:4 (or a cheap person detector). (4) **per-axis signs + `sentisMetreScale`** were inherited from MediaPipe — live-tune `poseFlipX/Y/Z`+scale for this provider. (5) cosmetic: HUD label still "MediaPipe Pose (CPU)".
- **REMAINING TUNING (resume here) — live-tune the Sentis path** (needs webcam; iterative like `poseFlipX`): set `AppBootstrap.useSentis3dTracking=true`, Play, then check via MCP: (1) **normalization** — first hypothesis is ImageNet norm ON; if keypoints are garbage try `sentisImageNetNorm=false` (export may bake norm — op list had Mul/Add/Div, no explicit Sub). Validate by decoding nose/wrist to sane image coords. (2) **coordinate/mirror/z** — tune `poseFlipX/Y/Z` + `sentisMetreScale` so the avatar tracks upright and un-mirrored (RTMW3D z is relative depth — scale is a guess, tune). (3) top-down caveat: full frame is resized to 3:4 (no person-bbox detector) — fine for a centred single user, distorts otherwise; add a centre-crop later. Then: root/hip grounding (stop float) + velocity outlier-reject (leg jitter). ⚠️ Play-mode via MCP freezes when Unity is unfocused and can orphan the `DontDestroyOnLoad` AppBootstrap — use `EditorApplication.Step()` / reload scene to clear; screenshot needs `isPaused=false`.
- **✅ Run-6 fixes APPLIED + COMPILE CLEAN (2026-08-06, 0 errors) — addresses the Run-5 "under-responsive" findings:**
  1. **z-depth decode fixed (the big one)** — pulled the exact mmpose `SimCC3DLabel` decode: `z_metric = (zIndex/(ZBins/2) − 1) × ZRange` with `ZRange=2.1744869` (root-relative, centred at 0), NOT the old pixel treatment (`z/InputHeight`, positive-only → flat). Added const `ZRange`; z magnitude now routed through a **separate `sentisDepthScale`** (default 0.4), independent of `sentisMetreScale`. In `SentisPoseProvider.DecodeFrame`.
  2. **3:4 person crop** — `BuildInput` centre-crops the largest 3:4 region (UV scale/offset on `Graphics.Blit`) when `sentisPersonCrop` (default true) → tight, undistorted person box for the top-down model.
  3. **Looser 3D filter profile** — when the provider is `SentisPoseProvider`, `AppBootstrap` sets One-Euro to `sentisFilterMinCutoff=1.5`/`sentisFilterBeta=0.6` (vs calm-webcam 1.0/0.2) so fast dance isn't damped. Slider-tunable live.
  4. **Axis/scale knobs** — new serialized `sentisDepthScale`, `sentisPersonCrop`, `sentisFilterBeta`, `sentisFilterMinCutoff` on `AppBootstrap` (defaults verified in `Bootstrap.unity`). `poseFlipX/Y/Z`+`sentisMetreScale` unchanged.
  5. **HUD label** — `DescribeActiveTracking()` now shows the real provider ("Sentis RTMW3D (GPU)").
  - **Files:** `Runtime/Tracking/Sentis/SentisPoseProvider.cs`, `Runtime/Bootstrap/AppBootstrap.cs`. Scene verified: `useSentis3dTracking=true`, `sentisModel=rtmw3d-x` assigned, new knobs at defaults.
  - **⏳ NOT motion-verified (resume here):** compiles + scene-ready, but frame-to-frame motion needs a **real-time focused Play by the user** (headless can't advance the VideoPlayer / freezes unfocused). Expected: noticeably bigger arm/leg reach + real depth. Then live-tune `sentisDepthScale` (raise if still muted), `sentisMetreScale`, `poseFlipX/Y/Z` (mirror), smoothing sliders. Still open after that: root/hip grounding (stop float), velocity outlier-reject (leg jitter).
- **✅ Run-7 (2026-08-06) — Sentis result GREATLY IMPROVED (user recording, default params), fixed elbow-bend:** Depth fixes worked — arms now track + reach with real depth. Remaining issue user reported: "static shoulder→wrist, elbow/forearm doesn't bend." Diagnosed from a synchronized zoom (dancer inset vs avatar): the **dancer clearly bends her elbows (forearms crossed at chest) but the avatar's arms stay straight-and-down → NOT the video, it's our pipeline.** Cause: **`sentisDepthScale` too low (0.4)** — the TwoBoneIK elbow only bends when the 3D elbow sits off the shoulder→wrist line, and a natural bend is mostly a toward/away-camera (depth) motion; at 0.4, depth is ~2× compressed vs x/y so the elbow collapses onto the line → rigid straight arm. **Fix: raised default `sentisDepthScale` 0.4 → 0.8** (commensurate with the x/y metre scale) + **made `sentisMetreScale`/`sentisDepthScale` LIVE-tunable** (`SentisPoseProvider.SetTuning`, pushed each frame from `AppBootstrap.UpdateTracking`) so they can be dialled in the Inspector during Play without a restart. Scene saved (`sentisDepthScale=0.8`). Files: `SentisPoseProvider.cs`, `AppBootstrap.cs`. Compiles clean. **⏳ Retest (user):** confirm elbows bend now; if still shallow raise `sentisDepthScale` toward 1.0+; **webcam** (`useVideoSource=false`) with deliberate slow elbow bends is the cleanest test (the dance video has fast, self-occluding poses like crossed forearms that single-camera tracking handles poorly). Still open: root/hip grounding (float), leg-jitter outlier-reject.
- **⚠️ Run-8 (2026-08-06) — WEBCAM was bad; added person-box tracking (top-down bbox stage):** User's live **webcam** run (upper-body, at a desk) tracked far worse than the full-body dance video — arms barely moved (gated to rest), limbs bent behind the body. Diagnosed (dancer-vs-avatar frames): **not a regression** — RTMW3D is a full-body top-down model, and (a) the fixed centre-crop clips raised arms on a landscape webcam, (b) upper-body-only is out of the model's distribution → low confidence → retarget gate holds arms at rest. Both vid.mp4 and webcam are 1280×720; the video worked only because it's a full-body centred subject. User chose "works for both framings → add a person detector." **Implemented as a keypoint-driven person-box TRACKER** (not a second detector NN — lighter, no extra model, no fragile NMS): `SentisPoseProvider` now crops each frame to the previous frame's confident-keypoint bbox (+`BoxMargin` 0.35 to catch entering limbs, smoothed), bootstraps from a centre crop, resets on tracking loss. New methods `ComputeCropRect`/`UpdatePersonBox`; crop→source-UV mapping accounts for the ReadPixels Y-flip. Compiles clean (0 errors). **⏳ Live-validate (user):** webcam, BOTH upper-body and full-body — expect the box to follow you and arms to track at any framing. **Tuning note:** the tight box makes the person fill the input, so motion amplitude is larger → **lower `sentisMetreScale`** if it overshoots (live-tunable). If limbs still bend behind, flip **`poseFlipZ`**. Known limits: single-camera still struggles with fast self-occluding poses (crossed forearms); if you leave frame it resets to centre then re-acquires. A real detector NN is the fallback only if multi-person / hard re-acquisition is needed. Files: `SentisPoseProvider.cs`.
- **✅ Run-9 (2026-08-06) — person-box tracker WORKING + waist-tilt fix.** User webcam run: arms/elbows now track well when framing is decent (verified in frames: arms-up bent-elbow "goalpost" reproduced, hands-to-head reproduced). Person-box tracking is paying off. Three remaining points from the user + status:
  1. **Waist bend tilted the WHOLE model rigidly → FIXED + ✅ USER-CONFIRMED WORKING (2026-08-06).** Cause: `HumanoidPoseRetargeter.ApplyTorso` drove Hips AND Spine from the same `up = midShoulder−midHip`, so a forward bend tilted both together (rigid pivot at the hips). **Fix:** `BasisBone.StableUp` — the **Hips** now use a stable vertical up (`Vector3.up`) so they only track facing/yaw + side-lean and stay upright; the **Spine** keeps the live torso-up and carries the bend → a real waist bend. Files: `HumanoidPoseRetargeter.cs`. Trade-off: hips no longer pitch forward/back (fine for a mirror; pelvis pitch isn't reliably recoverable from webcam landmarks anyway).
  2. **Hands sometimes behind the body (even with `poseFlipZ` toggled)** = the classic **monocular front/back depth ambiguity** — a single camera genuinely can't always tell if a limb is forward or back, so RTMW3D's z occasionally flips. `sentisDepthScale=0.8` amplifies it. NOT a global sign bug (that's why flipping Z didn't fix it). Mitigation: **lower `sentisDepthScale` (0.8 → ~0.5) live** (less depth = less wrong-depth), and/or accept it as a single-camera limit. Truly fixed only by a depth camera (ADR-007) or stronger temporal model. Candidate future code fix: temporal z smoothing / outlier clamp.
  3. **Close framing worse than far** (user confirmed "far is a little better") = top-down full-body model + hip-centring needs the **hips in frame**; when close/upper-body the hips are near/below the frame edge → unstable mid-hip anchor. Guidance: **half-to-full-body framing** (hips visible) tracks best. Person-box helps but can't invent an out-of-frame pelvis.
- **🎯 OAK-D depth camera AVAILABLE — recommended next step for "hands behind" + accuracy (2026-08-06).** User has a **Luxonis OAK-D** and asked if it improves things. **Yes — it's the direct cure for the remaining pain point.** "Hands behind" is monocular front/back depth ambiguity; a stereo-depth camera *measures* real depth, so front/back is no longer guessed. Also removes the `sentisDepthScale`/`metreScale` guesswork (real metric depth) and stabilizes Z. Waist bend + arm/elbow tracking are already good; depth is the last big monocular limitation. Fits the architecture: **ADR-007 explicitly reserved optional depth behind `IBodyTrackingProvider`/`ICameraCapture`.** Integration options (see proposed **ADR-016**):
  - **(A) Incremental — RGB 2D pose + depth-map Z lookup (recommended first):** keep the existing 2D keypoints (MediaPipe or RTMW3D x/y), sample OAK-D's aligned depth map at each keypoint pixel → replace the model's guessed Z with *measured* Z. Smallest change; directly fixes hands-behind + scale. New `OakDDepthProvider : IBodyTrackingProvider` (or a depth-fusion wrapper).
  - **(B) On-device 3D pose (cleaner, bigger):** OAK-D runs pose NN on its Myriad X VPU and fuses stereo depth → outputs metric 3D landmarks directly (DepthAI `spatial` pose), offloading the host. Provider reads DepthAI's 3D landmark stream.
  - **Dependency/cost:** needs the **DepthAI SDK + Unity integration** (Luxonis DepthAI-Unity plugin, or a C++/Python bridge) — a native dependency like MediaPipe was; Windows-supported; USB. Caveats: depth min-range a few cm, edge noise/occlusion holes, person must be within depth range; fingers still need close framing (depth fixes wrist/arm front-back, not finger detail). Real chunk of work but the correct long-term fix.
  - **✅ USER CHOSE Option B (on-device 3D pose), 2026-08-06 — ADR-016 Accepted.** On-device BlazePose = **33 landmarks = our `JointId` topology** + stereo depth → metric 3D from the camera, ~1:1 into `IBodyTrackingProvider`. Integration fork pending confirmation: **B1** `luxonis/depthai-unity` native plugin (all-Unity, precompiled, Pose pipeline returns 3D landmarks; supports "Unity 2021.2+ Windows" but last updated ~2024-01 → **Unity 6.3 compat is the main risk**; pose C# API undocumented → read `src/`) vs **B2** `geaxgx/depthai_blazepose` Python sidecar streaming 33 landmarks to Unity over UDP/socket (robust/decoupled; +Python process). Plan: verify B1 import into Unity 6.3 first, else B2. Then `Runtime/Tracking/OakD/OakDPoseProvider.cs` + `AppBootstrap.useOakDTracking` + fallback. **Requires the OAK-D plugged in + SDK/plugin install + user live-testing — not validatable device-less.**
  - **✅ B1 COMPILE-FEASIBILITY PASSED (2026-08-06, Unity 6.3):** imported the minimal Windows subset (81 MB) to `Assets/Plugins/OAKForUnity/` + `Assets/Plugins/Netly/` → **0 errors/0 warnings**; `DaiBodyPose`/`OAKDevice` loaded (Assembly-CSharp-firstpass), `depthai-unity.dll` imported. Model = MoveNet-17 (real 3D via stereo). Native DLL runtime-load + OAK-D device detection NOT yet tested (needs a play run with the camera).
  - **✅ PROVIDER BUILT + COMPILES CLEAN (2026-08-06, 0 errors).** `Runtime/Tracking/OakD/OakDPoseProvider.cs` — `IBodyTrackingProvider` using its OWN `[DllImport("depthai-unity")]` (`InitBodyPose`/`BodyPoseResults`/`DAICloseDevice`) with `PipelineConfig`/`FrameInfo` structs copied **verbatim** from `PredefinedBase.cs` (ABI-exact; note `useSpatialLocator` is a plain 4-byte bool). Config mirrors `DaiBodyPose` defaults (MoveNet lightning, 192 preview, `useSpatialLocator=true`, RGB-aligned depth, model path `Assets/Plugins/OAKForUnity/Models/movenet_singlepose_lightning_3.blob`). Parses the JSON (`landmarks[]` with flat `location.x/y/z` in **mm**, Newtonsoft) → 17 COCO kps mm→m → hip-centred → `PoseSpaceConverter` → `PoseFrame`. Added `Newtonsoft.Json` to `VirtualMirror.Tracking.asmdef`. Wired in `AppBootstrap.StartTracking` **OAK-first** (priority over Sentis/MediaPipe) with graceful fallback if no device; serialized `useOakDTracking`/`oakModelFileName`/`oakDeviceNum`/`oakLandmarkThreshold`; HUD label "OAK-D 3D (on-device)". **`useOakDTracking=true` saved in `Bootstrap.unity`.**
  - **🐛 First live-test CRASHED Unity (native abort) → root-caused + fixed (2026-08-06).** DepthAI native log (`Editor-prev.log`): the device is an **OV9782-based OAK-D** — it does NOT support 1080p color (silently fell back to 800p), and my `ispScale=2/3` on 800p produced RGB width **854**, not a multiple of 16 → `[StereoDepth] [error] Disparity/depth width must be multiple of 16` → C++ runtime abort → Unity crash. **InitBodyPose itself SUCCEEDED** ("OAK-D pose provider started"); the crash was when the pipeline ran. **Fix:** `BuildConfig` now uses `colorCameraResolution=THE_800_P(4)` + `ispScaleF1=1/ispScaleF2=2` → 640-wide (multiple of 16). Recompiled clean. (Benign warnings remain: NN input "192x192 does not match NN (3x192)" + interleaved-vs-planar — present in the stock demo too; revisit only if pose quality is off. A native abort() can't be caught by managed try/catch, so avoiding the trigger is the only fix — if a different OAK-D model is used, set 800p/720p + a mult-of-16 width.)
  - **🧊 Second live-test FROZE Unity (not a crash) → fixed (2026-08-06).** After the depth-crash fix, init succeeded but the app **froze on Mirror scene load**: `BodyPoseResults` is a BLOCKING native call and was running on the MAIN thread each `Tick` → the first query hung the main thread → Unity unresponsive (had to Task-Manager-kill it). **Fix:** `OakDPoseProvider` now runs `BodyPoseResults` on a **background worker thread** (`WorkerLoop`), double-buffered (`workerFrame`/`mainFrame`) with a lock, exactly like `MediaPipePoseProvider`; `Tick` is a no-op; `TryGetLatestFrame` swaps under the lock; `Dispose` calls `DAICloseDevice` (unblocks the worker) then `Join(1000)`. Main thread never blocks now → no freeze even if the device stalls. Compiles clean; `WorkerLoop` present; interface intact.
  - **Third live-test (2026-08-06): no freeze/crash ✅, but avatar didn't move + no RGB feed.** Live inspection: `OakDPoseProvider` runs (worker thread, timestamps advancing) but every frame is `IsValid=False` with all-zero landmarks → the **on-device MoveNet isn't yielding keypoints**. Candidates: (a) no person in the OAK's view, (b) the NN input-format warnings from the crash log (`Input image 192x192 does not match NN`, `interleaved (HWC) vs planar (CHW)`) → garbage NN input → no detection, (c) JSON-key mismatch in my parse. **Added a TEMP one-shot raw-JSON log** in `WorkerLoop` (`rawDebugRemaining`, logs `OAK-D raw[len]: …`) to disambiguate — REMOVE after diagnosis. Separately, **`WebcamCaptureService` fails** ("Could not connect pins - RenderStream()") + camera dropdown empty: the OAK-D is NOT a UVC webcam and no other webcam is connected → the RGB path (preview + MediaPipe face/hand) has no source. Non-fatal for the OAK BODY path (OAK uses its own color sensor on-device), but face/hand/preview are dead until either a 2nd webcam is plugged in OR the OAK color frame is fed into the RGB path.
  - **Fourth live-test (2026-08-06): NO `OAK-D raw` log + CRASH ON STOP.** BodyPoseResults apparently returned zero (no result → log block skipped, no keypoints), and exiting play **crashed Unity** — the native device-teardown path (`DAICloseDevice` in `Dispose` while the worker may be parked in `BodyPoseResults`). That's the **3rd distinct native failure** from the in-Unity B1 plugin (depth-config crash → main-thread freeze → crash-on-stop), and every one crashes/hangs the whole Unity process.
  - **🔻 B1 VERDICT: too fragile on Unity 6.3.** The precompiled 2024-era `depthai-unity` plugin's native device lifecycle crashes Unity on stop — not reliably fixable from managed C# (can't control the native shutdown), and it produced no keypoints. **SAFETY: `useOakDTracking` set to 0 in `Bootstrap.unity`** (edited on disk while MCP was down) → app is stable again (falls back to Sentis/MediaPipe RGB). B1 code/plugin left in place but OFF.
  - **✅ B2 (Python sidecar) BUILT (2026-08-06) — user chose it.** No native DLL in Unity → DepthAI can't crash the editor. Pieces:
    - **Sidecar env:** `oak_sidecar/venv` (Python 3.10) with **depthai 2.32.0.0** + opencv + numpy; `oak_sidecar/depthai_blazepose/` = cloned `geaxgx/depthai_blazepose`.
    - **Sidecar script:** `oak_sidecar/depthai_blazepose/udp_pose_sender.py` — runs BlazePose on the OAK (edge mode, `xyz=True`), streams 33 `landmarks_world` (metres, hip-relative GHUM) as JSON over UDP to 127.0.0.1:8899. Run: `cd oak_sidecar\depthai_blazepose && ..\venv\Scripts\python udp_pose_sender.py` (`--lm full` for accuracy, Ctrl+C to stop).
    - **Unity provider:** `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs` (`IBodyTrackingProvider`) — binds UDP `oakUdpPort`, background receive thread, parses JSON → 33 BlazePose (1:1 `JointId`) → `PoseSpaceConverter` → double-buffered `PoseFrame`. Wired in `AppBootstrap` behind **`useOakUdpTracking`** (top priority, `AppBootstrap.cs:307`) + `oakUdpPort` (8899); HUD "OAK-D 3D (UDP sidecar)". **✅ COMPILE-VERIFIED 2026-08-07** (forced refresh scope=all + compile via MCP @2f9eb012 → 0 CS errors; code + wiring reviewed AGENTS.md-clean). Still **NOT live-verified** — no OAK-D attached this session, so sidecar run + Play/track/axis-tune remain pending on hardware.
    - **⚠️ Phase-1 quality:** streams GHUM `landmarks_world` (per-limb Z = the GHUM estimate, ≈ MediaPipe) — this proves the STABLE pipeline but does NOT yet fix front/back. **Phase 2 (the real depth win):** sample the OAK depth map per keypoint (`body.landmarks` px → depth[y,x] → back-project) for MEASURED per-keypoint Z, or use `body.xyz` (depth-anchored hip) — then hands-behind is fixed at the source.
    - **B1 leftovers — ✅ REMOVED 2026-08-07:** the `Assets/Plugins/OAKForUnity` + `Assets/Plugins/Netly` native plugins (81 MB) were already gone; this session deleted `OakDPoseProvider.cs` (+ `.meta`) and stripped all B1 wiring from `AppBootstrap` (fields `useOakDTracking`/`oakModelFileName`/`oakDeviceNum`/`oakLandmarkThreshold`, the StartTracking block, and the `DescribeActiveTracking` on-device branch). Recompiled clean (0 CS errors). B2 (`OakDUdpPoseProvider`) is now the only OAK path. **Also hardened the B2 provider (2026-08-07):** `TryGetLatestFrame` now captures its return under the lock (was a benign `hasAnyFrame` read-race), and added `ReceivedCount`/`ParseErrorCount` (Interlocked) + a periodic console line `OAK-D UDP rx=… parseErr=…` so live bring-up shows whether datagrams arrive vs fail to parse. Recompiled clean. **Phase-2 design is now fully spec'd in [`docs/26_OakDDepthPhase2.md`](26_OakDDepthPhase2.md)** (measured per-keypoint depth: host-side depth-map back-projection primary, on-device 33-ROI alternative, GHUM fallback, validation plan) — code it against a live device. **Device-free test tool added: `oak_sidecar/mock_udp_sender.py`** (pure stdlib, no venv/camera) streams a synthetic animated 33-landmark skeleton on the exact B2 wire contract — run it, tick `useOakUdpTracking`, Play → avatar should wave; proves the whole provider path with no hardware. Verified 2026-08-07 against a receiver mimicking `ParseInto`: 60/60 datagrams parsed, 0 errors, 33 landmarks/frame (13 non-zero = defined joints). Combined with the compile-verify + review, the only unverified B2 step left is the in-editor "watch the avatar move" (needs eyes on screen). **RGB "hands behind" is formally CLOSED as won't-fix-on-RGB (see tasks.md)** — inherent monocular ambiguity, superseded by this OAK-D depth path. (`Scenes/Bootstrap.unity` still holds orphaned serialized keys for the removed fields — Unity drops them on next scene save; harmless.) The depthai *runtime* cache `Assets/Games/viitorx-vrm-avtar-unity/.cache/depthai/` was left in place — it's a DepthAI cache the B2 sidecar also uses, not B1 code.
  - **Sidecar bring-up (2026-08-06): pipeline created ✅, then an OV9782 resolution error → FIXED.** First sidecar run: `RuntimeError: ColorCamera(0) - 'video' width or height (1152, 648) bigger than maximum at current sensor resolution (768, 480)` — geaxgx assumes a 16:9 IMX378 (1080p) and requested 1152×648, but the OV9782 caps at ~768×480 (800p). **Fix (Python-only, no Unity touch):** added `--frame_height` to `udp_pose_sender.py`, current default **200** (→ ~355×200, safely inside ≤768×480; bump toward 400 for more resolution, drop to 288 if a larger value overflows). This is the B2 advantage — iterate the sidecar in seconds, no Unity recompile/crash.
  - **⏳ EXACT RESUME (user):**
    1. **Re-run the sidecar:** `cd oak_sidecar\depthai_blazepose && ..\venv\Scripts\python udp_pose_sender.py` — success = repeating `[oak-udp] frames=… sent=… kp0=[…] xyz=[…]` lines (BlazePose streaming from the OAK). If it errors again, paste the traceback (stays in the sidecar, Unity unaffected).
    2. **Unity** (safe — B1 OAK off). B2 code compile is ✅ CONFIRMED (2026-08-07) — no CS-error check needed. Just: attach OAK-D, tick **`useOakUdpTracking`** on `AppBootstrap`; Play; stand in view → HUD "OAK-D 3D (UDP sidecar)", avatar tracks. Tune `poseFlipX/Y/Z` for mirror/axes.
    3. **Judge:** stable (no crash) + tracks smoothly? Depth is GHUM-level (≈MediaPipe) for now.
    4. **Phase 2 (the real front/back fix) — HARDWARE-IN-THE-LOOP, do NOT write blind:** extend the B2 sidecar (`udp_pose_sender.py`) to emit MEASURED per-keypoint depth instead of GHUM `landmarks_world`. The geaxgx *edge* pipeline computes `body.xyz` for only ONE mid-hip ROI via an on-device SpatialLocationCalculator; per-keypoint 3D needs either (a) piping the aligned depth map to the host and back-projecting each `body.landmarks` px through the camera intrinsics, or (b) adding a SpatialLocationCalculator over all 33 keypoint ROIs in the device graph. Both are DepthAI pipeline surgery that MUST be iterated against a live OAK-D — writing it without the camera is untestable and error-prone. Unity side needs no change (still 33 landmarks over UDP; only their Z gets more accurate). When done, the HUD still reads **"OAK-D 3D (UDP sidecar)"** — B1's "on-device" HUD label and provider were removed 2026-08-07.
  - **✅ B2 LIVE BRING-UP — Phase-1 end-to-end PROVEN on hardware (2026-08-07, OAK-D attached this session):**
    - **Sidecar venv was broken** on this D: machine — `oak_sidecar/venv/Scripts` + `pyvenv.cfg` were missing (only `Lib/site-packages` survived the drive copy), so `..\venv\Scripts\python` did not exist. **Fixed** by recreating the venv in place with the matching **Python 3.10** (`C:\Program Files\Python310\python.exe -m venv oak_sidecar\venv`) — regenerates `Scripts/python.exe`+`pyvenv.cfg`, keeps the installed site-packages (depthai 2.32.0.0, cv2 4.11, numpy 1.26.4, all `cp310`). The documented `..\venv\Scripts\python udp_pose_sender.py` works again.
    - **Device detected:** OAK-D MxId `14442C10F143D3D200` (`X_LINK_UNBOOTED` → boots on pipeline start). depthai enumerates it fine.
    - **Sidecar runs:** `..\venv\Scripts\python udp_pose_sender.py` (default `--lm lite`, `--frame_height 200`) → `Pipeline started - USB speed: SUPER`, then streams `[oak-udp] frames=… sent=… kp0=[…] xyz=[…]` **once a body is in view** (no body ⇒ no send, by design). The benign OV9782 warnings (`Unsupported resolution … Defaulting to 800_P`, `Horiz/Vert scaling disabled`) are expected. **`--frame_height 400` ERRORS on this OV9782** (ISP scale ratio exceeds HW limits MAX_N:16/MAX_D:63 — not just the 768×480 cap); **200 is the verified working value** (→352×198). Fixed the misleading "400 safe" comment in `udp_pose_sender.py`. Depth resolution is independent of `--frame_height` (comes from the 400P mono pair), so 200 is fine for Phase-2 depth sampling.
    - **Unity:** set `useOakUdpTracking=true` on the real Bootstrap-scene `AppBootstrap` (SerializedObject, dataPath-guarded) and **saved `Bootstrap.unity`**. Entered Play via MCP (bridge is on **port 6400** this session, instance `viitorx-vrm-avtar-unity-base-project@70066c27` — the runbook's 6401 is stale, ports change): console shows `OAK-D UDP pose provider listening on 127.0.0.1:8899`, `Body tracking started`, avatar auto-loaded (`Anime+Boy+V-tuber.vrm`), **0 errors**. OAK is top-priority body provider; MediaPipe now drives face+hands only.
    - **✅ FULL CHAIN CONFIRMED:** with a body in view the sidecar sent and Unity logged `OAK-D UDP rx=1/2/3 parseErr=0` — datagrams received + **parsed clean**. Phase-1 (GHUM depth ≈ MediaPipe) is proven on real hardware end-to-end. (RGB webcam picked `'Meta Quest 3'` as its device + logged the known benign `Could not connect pins - RenderStream()` — irrelevant to the OAK body path.)
    - **⏳ STILL PENDING (needs user eyes + body in frame):** confirm the avatar visibly tracks and tune `poseFlipX/Y/Z` (mirror + axes). `poseFlip*` build the `PoseSpaceConverter` once at `StartTracking`, so they are **NOT live-tunable** — tuning is a stop → edit serialized field → Play cycle. Current `poseFlipX/Y/Z = true/true/true`.
    - **Phase-2 readiness (source studied 2026-08-07):** `BlazeposeDepthaiEdge.py` already (a) builds stereo depth **aligned to RGB** (`stereo.setDepthAlign(RGB)`) when `xyz=True`, and (b) computes `body.landmarks` (Nx3 int32) **in full video-frame pixel coords with padding already removed** (`lm_postprocess`, lines ~453-458). So Phase-2 Option A is: subclass `BlazeposeDepthai` in the sender (keep the clone clean), override `create_pipeline` to add one `XLinkOut` from the existing StereoDepth node's `.depth`, pull the aligned depth frame host-side, read RGB intrinsics scaled to the depth-frame size once, then per keypoint sample `body.landmarks[i]` px → depth px (`u*Wd/img_w`, `v*Hd/img_h`) → KxK median → back-project → hip-centre → GHUM fallback for holes. The §4 pixel-mapping still MUST be validated with the overlay-on-RGB check against the live camera before trusting Z.
  - **✅ B2 LIVE ITER-2 — FOV crop fixed + retarget/position overhaul (2026-08-07, device attached, MANY user iterations):**
    - **FOV was a CROP, now FIXED (the big one).** Device is an **OAK-D-PRO-W** (wide ~120° lens, color sensor **OV9782 = 1280×800**). geaxgx's `find_isp_scale_params` assumes a 1920×1080 IMX378 → on the OV9782 the derived ISP scale is invalid (`cannot meet HW constraints`, chroma denom >63) → ISP scaling disabled → DepthAI **centre-crops** the sensor → very narrow view (user's phone at the same spot showed full body; OAK Viewer at 640×400 showed the whole room). **Fix:** added OV9782-native color mode to the sidecar — `BlazeposeDepthaiEdge.__init__`/`create_pipeline` now accept `color_res` ("800p"/"720p") + `color_scale` (default 1/2): set `THE_800_P` + `setIspScale(1,2)` → **640×400 full-FOV**, and compute `img_w/img_h/pad_h/frame_size` from the ACTUAL sensor (not 1920×1080). `udp_pose_sender.py` defaults `--color_res 800p --color_scale 1/2`. Verified: log shows `640 x 400`, the OV9782 scaling-disabled/crop errors are GONE, full room captured. (`--color_res none` restores legacy behaviour.)
    - **Sidecar debug preview added:** `--show` opens a cv2 window (OAK RGB + drawn skeleton + hip xyz), so the OAK's own view is visible independent of Unity (Unity's bottom-right preview is the *webcam*, a different camera). Confirmed the skeleton tracks the user faithfully → **capture is good; the retarget was the bottleneck.**
    - **Retarget overhaul (Unity, compiles clean 0 CS errors):**
      - **Legs held straight when occluded** — `HumanoidPoseRetargeter.SetTrackLegs(bool)` (+ `BoundSegment.IsLeg`): when off, leg segments are pinned to their bind (straight) rotation instead of following BlazePose's *hallucinated* occluded-leg landmarks (desk-occluded lower body → the "bent/crouched legs" the user kept hitting, in BOTH IK and FK). Also `AnimationRiggingIkDriver.SetLegTracking(bool)`/`IIkSolver`. Wired behind `AppBootstrap.trackLegs` (**default false** — legs can't be framed here).
      - **Hips upright-only (no tilt, no need to press C for uprightness)** — `ApplyTorso` projects the hip-line to horizontal for the StableUp (Hips) bone → yaw/facing only, can't roll; uprightness no longer depends on the neutral calibration.
      - **Rotational retarget is now the default** (`useIkDriver=false`) — the FK path (`BuildDefinitions` drives all four limbs via `RotationFromVectors`) is the same landmark→bone-rotation approach webcam VTuber tools (Kalidokit) use; it avoids the IK confidence-gate dropout that made arms vanish when the user is small in the wide FOV. IK path left intact (toggle the "IK Arm/Leg Driver" checkbox to compare).
      - **Live mirror toggle** — `PoseSpaceConverter` signs are now mutable (`SetFlipX/Y/Z`); the panel "Mirror Reflection (Flip X)" checkbox flips left/right instantly (was dead before). `converter` is now an `AppBootstrap` field. Scene defaults set `poseFlipX=false, poseFlipY=true, poseFlipZ=false` (Z flip fixed the reported lean-back→forward inversion; X per user "flipped in X" — fine-tune live via the checkbox).
      - **Position tracking (avatar walks/jumps with the user)** — `PoseFrame.RootPositionMetres`/`HasRootPosition`; `OakDUdpPoseProvider` parses the sidecar `xyz` (measured mid-hip mm → converter → metres) onto the frame; `AppBootstrap` drives the **avatar root** position = initial + (currentHip − neutralHip)·`positionScale`, exponentially smoothed (`positionSmoothing`). Neutral-relative; **C** resets it. This is the OAK's signature advantage a mono webcam can't do. Serialized `trackPosition`(true)/`positionScale`(1)/`positionSmoothing`(12).
    - **Files:** sidecar `BlazeposeDepthaiEdge.py`, `udp_pose_sender.py`; Unity `PoseSpaceConverter.cs`, `PoseFrame.cs`, `IIkSolver.cs`, `AnimationRiggingIkDriver.cs`, `HumanoidPoseRetargeter.cs`, `Bootstrap/AppBootstrap.cs`, `Scenes/Bootstrap.unity`.
    - **⏳ STILL PENDING / KNOWN LIMITS (next agent):** (1) **Face + fingers need a working RGB webcam** — the selected `Meta Quest 3` virtual cam fails to open (`Could not connect pins`), so MediaPipe face/hand get no frames; no software fix, needs a real USB webcam (OAK RGB is not fed into Unity in B2). (2) **Legs**: occluded by the user's desk → held straight; only enable `trackLegs` when the full lower body is reliably framed. (3) **Position** `xyz` can spike (saw a −2525 outlier at frame edge) → may want outlier rejection on top of the smoothing; tune `positionScale`/axis signs live. (4) `--lm heavy` runs ~7 fps (choppy); `--lm full` ≈18 fps is a better live-mirror balance. (5) The mirror X sign + `poseFlipZ` are empirical — confirm with the user, then the checkbox/scene value is the source of truth. (6) Retarget quality: if still short, the docs' "direct rotational retarget from 3D" is now the active path — tune smoothing + per-bone; consider deriving wrist/neck from landmarks. **ITER-3 (2026-08-07, same session, user testing):** (a) **Position vertical inversion FIXED** — the OAK `xyz` is **Y-UP** (opposite the GHUM landmarks' Y-down); added `PoseSpaceConverter.ToUnityPosition` (Y passthrough, X/Z keep mirror/forward signs) and used it in `OakDUdpPoseProvider` so jumping RAISES the avatar (was sinking). (b) **Auto-calibration — no C key** — `ApplyTorso` captures the neutral facing only when the user is roughly FRONT-FACING (shoulder line X-dominant vs Z), so a turned start no longer locks a bad neutral (which read as "flipped hands" until C). C still works as a manual redo. (c) **`trackLegs` → TRUE** and **✅ USER-CONFIRMED "legs are fine now"** (wide FOV + rotational retarget; legs track when visible, occluded lower body still can't be — physical limit). Jump-up / walk-position-amplitude / mirror-X were applied but not each independently re-confirmed — treat as good-but-tune (Inspector: `positionScale`, `positionSmoothing`; mirror via the live checkbox). Iter-3 files: `PoseSpaceConverter.cs`, `OakDUdpPoseProvider.cs`, `HumanoidPoseRetargeter.cs`, scene `trackLegs=true`. See ADR-017 for the retarget-architecture decision.
- **⚠️ Second Unity editor on this machine:** a project **`breakbuttler-base-project`** is also open and the MCP resolver routed port 6400 to it while ours was mid-reload (our instance has also moved to port **6401**). ALWAYS pin our instance explicitly (`set_active_instance viitorx-vrm-avtar-unity-base-project@70066c27`) and guard mutating `execute_code` on `Application.dataPath` containing `viitorx-vrm-avtar-unity`.

### Tooling notes (2026-08-06, this agent)
- **ffmpeg installed** via `winget install Gyan.FFmpeg` (v9.0) to inspect the user's screen recording (was absent; Unity 6000.3.9f1 doesn't bundle it). Binaries under `…\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg…\bin\` (PATH updated for new shells). The `watch` skill also needs it; a Whisper key was not set (frames-only is fine for screen recordings).
- **Unity MCP instance name can transiently flip** during a script-recompile domain reload — mid-reload the bridge briefly showed a *different* project name/hash before `viitorx-vrm-avtar-unity-base-project@70066c27` re-registered on port 6400. **Before any mutating `execute_code`, guard on `Application.dataPath` containing `viitorx-vrm-avtar-unity`** so an edit never lands on the wrong project. Re-pin with `set_active_instance 6400` after a reload.

### Tooling — Unity MCP for this working dir (2026-08-06)
The Claude Code session runs from the deep package dir `…\Assets\Games\viitorx-vrm-avtar-unity`, which had **no** `UnityMCP` server (it was only configured on the project-root dir `C:/Unity/viitorx-vrm-avtar-unity-base-project`) → zero Unity MCP tools loaded, so no in-editor compile/play-verify this session. Added `UnityMCP` (`uvx --from mcpforunityserver==10.0.0 mcp-for-unity`) to this dir's local config via `claude mcp add -s local`. **Requires a Claude Code restart** (MCP binds at startup). Unity-side bridge port (6401 on this machine) is set in Unity's *MCP For Unity* window, not in the MCP client config; the server autodiscovers running Unity instances. Alternative: relaunch `claude` from the project root.

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
