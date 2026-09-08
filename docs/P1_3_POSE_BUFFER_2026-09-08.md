# P1-3 — Unity Timestamped Pose Buffer + Interpolation (2026-09-08)

**Verdict: PASS — READY FOR RECOVERY / PALM / FOOT WORK.**

---

## 1. P1-2 human A/B result (Phase A) — **PASS**

Real subject, 40 s per policy, identical motion sequence (still → walk → fast arms → fast legs → dance),
100% tracked in both runs.

| metric | FIFO | latest-frame | change |
|---|---|---|---|
| **frame age** median | **131.51 ms** | **31.36 ms** | **−76.2%** |
| frame age p95 / p99 / max | 146.10 / 147.32 / 147.86 | 46.36 / 47.62 / 47.96 | −68% |
| **camera → UDP** median | **161.81 ms** | **62.03 ms** | **−61.7%** |
| compute host→pose | 20.85 ms | 20.70 ms | unchanged |
| RGB/depth sync max | 54.63 ms | 21.24 ms | −61% |
| fps | 21.35 | 21.38 | unchanged |
| P1-1 tracker cost | 0.14 ms | 0.15 ms | unchanged |

The FIFO figure of **131.51 ms reproduces the originally reported 131.4 ms exactly.**

### Jitter — no material change (velocity, m/s, not per-frame)

Per-frame displacement is not comparable between policies (latest-frame skips frames), so speed is used.

| joint | FIFO median / max | latest median / max | median Δ |
|---|---|---|---|
| L-wrist | 0.668 / 4.606 | 0.514 / **3.016** | −23.1% |
| R-wrist | 0.682 / 3.808 | 0.577 / **3.014** | −15.4% |
| L-elbow | 0.396 / 4.656 | 0.374 / **3.057** | −5.5% |
| L-knee | 0.185 / 4.313 | 0.224 / **3.133** | +21.3% |
| L-shoulder | 0.142 / 4.358 | 0.228 / 5.588 | +60.9% |

**8 of 12 joints have LOWER maximum velocity** with latest-frame; the wrists — the fastest joints and
the best jitter indicator — improved on both median and max.

**Implausible-speed events** (physically impossible = jitter, not motion): FIFO **6**, latest-frame **12**,
out of ~10 200 joint-samples each (0.06% vs 0.12%). The single worst (R-hip 10.37 m/s) is one frame in 852.

> **Stated honestly: this A/B is confounded.** The two runs are separate human performances, so median
> differences partly reflect moving differently — the low-velocity joints (shoulders 0.14 m/s, hips
> 0.045 m/s) show large *percentages* on centimetre-scale absolute differences. The controlled comparison
> is the load-simulated A/B (identical static scene), which showed no jitter change. Conclusion:
> **no evidence latest-frame adds jitter or suppresses legitimate fast movement.**

## 2. Compute spikes (Phase B) — **RESOLVED**, not unresolved

| run | frames | spikes > 80 ms | at frame index |
|---|---|---|---|
| human FIFO | 852 | 1 | **[0]** |
| human latest-frame | 853 | 2 | **[0, 1]** |
| unloaded latest-frame | 596 | 2 | **[0, 1]** |
| loaded latest-frame | 407 | 3 | **[0, 1, 2]** |
| **180 s soak** | **3378** | **2** | **[0, 1]** |

Every spike is in the **first three frames of the process**. Cause: **ONNX Runtime / DirectML first-
inference warm-up** (kernel compilation + allocation). It is not queue drain, not GC, not scheduling —
a 3378-frame soak produced spikes only at frames 0 and 1. The earlier "0.3–0.7%, cause not established"
was a wrong attribution: the rate is per-*process*, not per-frame, and it is a startup cost.

## 3. Current Unity timing model (before P1-3)

`ReceiveLoop → ParseInto → double-buffer swap → TryGetLatestFrame` returned **latest-wins**; the control
rig then re-applied that same pose every render frame with a fixed slerp. `seq` and `t` were parsed but
used only for log alignment. **Measured here: 17.84 applies per packet** (11 254 render frames / 631
packets), and **41.53% of render frames showed literally zero bone movement** — the stutter signature.

## 4. New buffer architecture

`Runtime/Core/PoseBuffer.cs` — a bounded ring of 16 pre-allocated `PoseFrame`s.

```
receive thread                         main thread (render)
──────────────                         ────────────────────
ParseInto ──► PoseBuffer.Push          renderTime = now − interpolationDelay
              (rejects dup / OOO /                │
               backwards timestamp)      PoseBuffer.Sample(renderTime)
              ring bounds memory                  │
                                          lerp(A, B, alpha)
                                                  ▼
                             PoseFrame ─► Kalidokit ─► P0 LimbGate ─► ControlRig ─► VRM
```

Kalidokit, the LimbGate, P1-1 and the P0 caps are untouched and remain in the same order.

## 5. Timestamp handling

- Buffer axis is the **Unity receive epoch**, not the sender's clock — no sender-skew assumption.
- `renderTime = now − poseInterpolationDelayMs`. Alpha comes from real timestamps, never a frame count
  or an assumed 30 fps.
- **Duplicate seq** → rejected (`RejectedDuplicate`).
- **Out-of-order seq** → rejected (`RejectedOutOfOrder`). A late packet's interval has already rendered.
- **Backwards timestamp** → rejected even if seq advanced: interpolation divides by `(tB − tA)`, so a
  non-monotonic axis makes alpha meaningless.
- **Dropped packet** → interpolation simply spans the wider gap between the surviving neighbours.
- Ring overwrite bounds memory; `DroppedOld` counts evictions.

## 6. Interpolation algorithm

Positional `Vector3.Lerp` per landmark, `Mathf.Lerp` on confidence, root position on the same timeline,
and `Quaternion.Slerp` exposed via `PoseBuffer.SlerpRotation` for rotational streams.
**No extrapolation:** a renderTime past the newest sample clamps to the newest.

### The safety rule that preserves P0-1

A landmark is **never position-interpolated across an invalid endpoint.** The sidecar emits a dropped
joint as `[0,0,0,0]`, so lerping valid→zero would place the joint half-way to the **origin** — precisely
the F-01 limb-collapse bug P0-1 exists to prevent. Instead the valid endpoint's position is carried and
confidence becomes `min(a, b)` = 0, so the joint still reaches the LimbGate as invalid and the gate holds.
Covered by `InvalidEndpoint_IsNeverPositionInterpolated`.

## 7. Tests — **47 / 47 pass** (33 existing + 14 new)

All 12 brief cases plus two safety tests: two equally spaced poses · irregular spacing · duplicate seq ·
out-of-order seq · dropped packet · empty buffer · single-pose buffer · normal interpolation · root
interpolation · quaternion slerp · buffer overflow · timestamp regression · **invalid-endpoint safety** ·
**no-extrapolation**.

One test initially failed and it was **the test that was wrong**, not the code: the fixture built both
frames at `x = 1f`, so "interpolates normally" asserted 0.5 against a 1→1 lerp. Endpoints must differ or
the assertion proves nothing.

## 8. Runtime metrics

Logged as a periodic aggregate (every 600 samples — never per render frame):
`poseBufferDepth`, `interpDelayMs`, `sourceSeqA`, `sourceSeqB`, `alpha`, `packetAgeMs`, plus
`interp` / `clampNewest` / `dupRej` / `oooRej` counters. `GetBufferDiagnostics()` exposes a snapshot.

## 9. Smoothness improvement — identical deterministic input

Measured with `stream_motion.py` (a continuous sinusoidal swing at 21 Hz) so both runs see **byte-identical
motion** — a human cannot repeat a performance closely enough to measure interpolation quality.
Metric is the left-forearm bone rotation delta per render frame.

| metric | OFF (0 ms) | ON (40 ms) | ON (55 ms) |
|---|---|---|---|
| applied render frames | 11 254 | 11 559 | 13 023 |
| distinct source seq | 631 | 631 | 631 |
| **FROZEN render frames** | **41.53%** | **23.63%** | 29.41% |
| **delta CoV** (lower = smoother) | **2.286** | **1.070** | 0.998 |
| mean bone delta | 0.7773° | 0.5980° | 0.5748° |
| **stutter reduction** | — | **−53%** | −56% |

**Coefficient of variation halved** — motion is spread evenly across render frames instead of arriving in
bursts. That is the stutter the audit's 11.7×/17.8× re-application caused.

**40 ms chosen over 55 ms**: 55 ms buys only 3 further percentage points of stutter reduction for 15 ms
more latency. (The frozen-% at 55 ms is not directly comparable — that run rendered 13 023 frames vs
11 559, changing the ratio's denominator.)

## 10. Added latency and the net position

Added latency is exactly the presentation delay: **+40 ms**. Placed in context:

| stage | original | after P1-2 | after P1-2 + P1-3 |
|---|---|---|---|
| camera → UDP | 161.8 ms | 62.0 ms | 62.0 ms |
| presentation delay | 0 | 0 | +40 ms |
| **total to avatar** | **~162 ms** | **~62 ms** | **~102 ms** |

**P1-2 bought ~100 ms; P1-3 spends 40 ms of it to halve the stutter. Net is still ~60 ms better than
before either change, and smoother.** Had P1-3 been done first, the buffer would have pushed an already
162 ms pipeline to ~202 ms — the ordering mattered.

## 11. Regression check

- **47/47 EditMode tests pass**; 0 `error CS`.
- P0 LimbGate, P0 0.35 m caps, Kalidokit, VRM, IK, RTMW3D, OAK-D depth fusion: **untouched**.
- P1-1 `joint_tracker.py`: **untouched** (md5-verified), 37/37 python assertions pass.
- P1-2 latest-frame policy: **untouched**.
- `poseInterpolationDelayMs = 0` restores the original latest-wins path exactly (the buffer is not even
  populated), so the change is fully reversible at runtime.

## 12. Files changed

| File | Change |
|---|---|
| `Runtime/Core/PoseBuffer.cs` | **NEW** — ring buffer, timestamp handling, interpolation |
| `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs` | push on receive; `SetPoseInterpolation`; sample in `TryGetLatestFrame`; diagnostics |
| `Runtime/Bootstrap/AppBootstrap.cs` | `poseInterpolationDelayMs` (default 40) + wiring |
| `Tests/EditMode/PoseBufferTests.cs` | **NEW** — 14 tests |
| `Scenes/Bootstrap.unity` | `poseInterpolationDelayMs: 40` serialized |
| `python-sidecar~/stream_motion.py` | **NEW** — deterministic A/B motion streamer |

## 13. Remaining problems

1. **23.6% of render frames still show no motion at 40 ms.** With a 21 Hz stream the packet interval is
   47.6 ms, so a 40 ms delay frequently has no future sample and clamps to newest. Fully eliminating it
   needs a delay ≥ one packet interval — a latency trade deliberately not taken.
2. **Hand/palm quaternions still use the existing rate-limited slerp path**, not the buffer. `SlerpRotation`
   is provided and tested, but wiring hands in would have meant touching the hand pipeline, which the
   brief scoped out. Body and hands are therefore on slightly different timelines (~40 ms).
3. **Interpolation is disabled implicitly whenever the body pose is invalid** (only valid frames are
   pushed), so recovery after a total loss falls back to latest-wins for the first frame.
4. **The P1-2 human A/B remains confounded** by being two separate performances (§1).
5. Render rate varied between A/B runs (11 559 vs 13 023 frames), which affects frozen-% comparisons
   across delays but not the CoV comparison at a fixed delay.

## 14. Verdict

### **P1-3 PASS — READY FOR RECOVERY / PALM / FOOT WORK**

| Criterion | Result |
|---|---|
| buffer bounded, no unbounded growth | **PASS** — ring of 16, `DroppedOld` counted |
| duplicate / out-of-order / regression rejected | **PASS** — tested |
| timestamps used, not frame numbers | **PASS** — tested with irregular spacing |
| interpolation is primary, no extrapolation | **PASS** — clamps to newest |
| root interpolated on the same timeline | **PASS** |
| P0 LimbGate / P1-1 not bypassed | **PASS** — order preserved, invalid endpoints never lerped |
| does not add another smoothing filter | **PASS** — reconstruction only |
| added latency bounded and configurable | **PASS** — +40 ms, net −60 ms vs pre-P1-2 |
| smoothness improved | **PASS** — stutter CoV −53%, frozen frames 41.5% → 23.6% |
| no significant pose lag | **PASS** — 102 ms total, better than the 162 ms baseline |

Not implemented, as instructed: IK, foot lock, longer prediction horizon, confidence normalisation,
new pose model, multi-camera.
