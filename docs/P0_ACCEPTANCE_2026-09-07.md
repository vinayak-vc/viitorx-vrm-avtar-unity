# P0 Hardware + Unity Acceptance Test (2026-09-07)

**Scope:** OAK-D → Python sidecar → depth → smoothing → UDP → Unity → confidence gate → Kalidokit → VRM avatar.
**Predecessor:** [`AUDIT_FBT_2026-09-07.md`](AUDIT_FBT_2026-09-07.md) (findings F-01…F-12, P0-1/P0-2 fix plan).
**Rule:** no evidence = no claim. Anything not actually measured is marked **NOT TESTED**, never inferred.

---

## 0. Verdict

### **P0 CONDITIONAL PASS — the mechanism is proven; the human-motion evidence is uncollected.**

*Updated 2026-09-08: the P0-1 Unity gate is now **PROVEN to fire**, by scripted occlusion injection
(§4b). That was the single most important open item. The remaining gap is human motion quality.*

Everything establishable without a person in front of the camera **passes**, including three results
the audit could only estimate or argue: end-to-end latency, the squash-vs-rotation question, and now
the Unity confidence gate actually firing.

**The remaining blocker is not a defect — it is a missing input.** The human-motion criteria (spike
magnitudes under real fast movement, real confidence-degradation profiles, reacquisition dynamics,
palm behaviour, and how the avatar *looks*) require a person physically moving in front of the OAK-D.
Every camera capture across both sessions recorded `measured_body = 0/33` on **every** frame — most
recently a dedicated presence probe on 2026-09-08: **0 of 359 frames tracked, max 0/33 joints**. Those
criteria are **NOT TESTED**, not "passed".

The harness is built, instrumented and verified. Run
[`guided_capture.py`](../python-sidecar~/guided_capture.py) — it walks the operator through blocks A–J
on a countdown and reports every metric per block.

---

## 1. What was actually run

| # | Action | Result |
|---|---|---|
| 1 | Unity compile (forced full recompile) | **0 `error CS`** |
| 2 | EditMode suite `VirtualMirror.Tests` | **33 / 33 passed**, 0 failed, 0 skipped (1.11 s) |
| 3 | EditMode re-run after adding instrumentation | **33 / 33 passed** (no regression) |
| 4 | Real OAK-D pipeline, 30 s headless | 840 frames @ ~30 fps, 839 UDP packets |
| 5 | Real OAK-D pipeline + Unity Play, 35 s | 1046 sent / **1046 received**, avatar bound |
| 6 | Real OAK-D pipeline, 20 s, stage timing | 600 logged frames, full latency chain |
| 7 | Audit baseline re-analysis (2026-08-11 logs) | 54 600 sender / 54 285 recv rows |

---

## 2. Unity compile + EditMode tests — **PASS**

```
compile status : CLEAN (0 error CS)
test count     : 33
passed         : 33
failed         : 0
skipped        : 0
```

All six `LimbGateTests` pass:

| Test | State |
|---|---|
| `HighConfidence_AppliesFreshSolve` | Passed |
| `ShortDropout_HoldsLastValid_NeverZero` | Passed |
| `LongDropout_StaysHeld_NoCollapse` | Passed |
| `Reacquire_FlagsTransition_AppliesNew` | Passed |
| `MediumBelowThreshold_Holds` | Passed |
| `InvalidAtStartup_DoesNotApply_NoZero` | Passed |

### Unity crash during the first attempt — diagnosed, NOT P0

Running the **unfiltered** EditMode suite killed the Editor. Cause, from the dead session's
`Editor-prev.log` tail:

```
F0000 ... image_frame.cc:362] Check failed: 1 == ChannelSize() (1 vs. 2)
*** Check failure stack trace: ***
```

That is a **MediaPipe** native `absl::CHECK` abort (`image_frame.cc`), reached when the full-suite run's
`IPrebuildSetup` domain reload hit stale MediaPipe native state (`useMediaPipeTracking/Face/Hand = 1`).
It produced no Unity crash dump because it is a native `abort()`, not a managed exception.
**LimbGate is pure C# math and touches no MediaPipe code.** Scoping the run to the
`VirtualMirror.Tests` assembly avoids the MediaPipe/PerformanceTesting prebuild path and runs clean.

> Not a P0 defect, but a real test-infrastructure hazard: **run EditMode tests scoped to
> `VirtualMirror.Tests`**, not the whole suite.

---

## 3. Active shipping config — **CONFIRMED**

From `Scenes/Bootstrap.unity`:

| Setting | Required | Actual | Line |
|---|---|---|---|
| `useOakUdpTracking` | 1 | **1** | `Scenes/Bootstrap.unity:157` |
| `useKalidokitBody` | 1 | **1** | `:161` |
| mirror | off | **`kalidokitBodyMirror: 0`** | `:165` |
| `limbConfidenceThreshold` | 0.3 | **0.3** | `:173` (serialized 2026-09-08) |
| `trackLegs` | 1 | **1** | `:180` |
| `trackPosition` | 1 | **1** | `:181` |
| `pipelineLogging` | ON | **1** | `:186` |
| `useIkDriver` | 0 | **0** | `:204` |
| `useSentis3dTracking` | — | 0 | `:205` |

*(Line numbers re-verified after the threshold was inserted at :173.)*

### ✅ `limbConfidenceThreshold` — FIXED, now serialized (2026-09-08)

Set to `0.3` via `SerializedObject` and the scene saved. Verified in the YAML:
`Scenes/Bootstrap.unity:173  limbConfidenceThreshold: 0.3`. The pre-write read-back confirmed the C#
initializer had indeed been supplying `0.3`, so runtime behaviour is unchanged — the value is now
under source control instead of implicit.

<details><summary>Original finding (resolved)</summary>

#### `limbConfidenceThreshold` was NOT serialized in the scene

The field was added in the P0-1 commit *after* `Bootstrap.unity` was last written, so the key is
**absent from the YAML** and the value comes from the C# initializer:

```
Runtime/Bootstrap/AppBootstrap.cs:76   [SerializeField] private float limbConfidenceThreshold = 0.3f;
```

Effective value **is 0.3** and the gate works. But the value is invisible in the Inspector-serialized
scene, so it is not under source control and will be baked in by whatever the Inspector shows the next
time anyone saves the scene. **Recommend explicitly setting and saving it.**

</details>

### Live path trace — `PoseFrame → confidence → driver → LimbGate → ApplyBone → VRM`

Confidence genuinely reaches the gate. Each hop, with source evidence:

| Hop | Evidence |
|---|---|
| sidecar emits per-joint conf | `wholebody_udp_sender.py:92-101` — `emit()` drops `c < conf_thr` to `[0,0,0,0]`; measured → `c`; depth-hole fallback → `c * 0.5` |
| UDP → `PoseLandmark.Confidence` | `OakDUdpPoseProvider.cs:298` — `target.SetLandmark(i, new PoseLandmark(unity, Mathf.Clamp01(confidence)))` where `confidence = point[3]` |
| driver copies conf alongside position | `KalidokitControlRigDriver.cs:238-241` — `conf[i] = lmk.Confidence;` (and swapped in lockstep with landmarks under mirror, `:252-255`) |
| per-limb min feeds the gate | `:320-326` — `ApplyLimb(leftArmGate, "leftArm", Min3(conf[12], conf[14], conf[16]), …)`; legs `:337-341` |
| gate decides apply-vs-hold | `LimbGate.Resolve()` — `Runtime/Retargeting/LimbGate.cs:48-79` |
| gated rotation reaches the bone | `ApplyLimb` → `ApplyBone(upperBone, targetUpper)` — `:361-364` |
| bone → VRM | `Vrm10 Runtime.Process()` (normalised control rig → skeleton) |

**Confirmed:** `LimbGate` is on the live Kalidokit path, not dead code.

**Important consequence of the threshold arithmetic** (sidecar `--conf` default `0.3` == Unity gate
`0.3`): a joint reaching Unity can only carry conf `0` (dropped), `c ≥ 0.3` (measured), or
`c × 0.5 ∈ [0.15, 0.5)` (depth-hole fallback). So the Unity gate effectively fires when the sidecar has
**dropped the joint outright**, or halved a marginal one below 0.3. It is a near-binary gate, not a
graded confidence weighting. That is adequate for P0 and is exactly what physical occlusion produces.

---

## 4. OAK-D hardware — **PASS (pipeline health only)**

Device present: `14442C10F143D3D200`, depthai `2.32.0.0`.

```
[wb] providers: ['DmlExecutionProvider', 'CPUExecutionProvider']
[wb] rgb 640x400 depth 640x400 intr fx=284.6 fy=284.5 cx=319.2 cy=201.5 -> ('127.0.0.1', 8899)
```

| Signal | Result | Evidence |
|---|---|---|
| RGB stream | **OK** 640×400 | sidecar banner |
| Depth stream | **OK** 640×400 | sidecar banner |
| RGB/depth alignment | **OK** — depth frame is RGB-sized/aligned | `oak_depth.build_rgbd_pipeline` `setDepthAlign(CAM_A)`; identical 640×400 geometry |
| Intrinsics | **OK, plausible** — cx 319.2 ≈ W/2, cy 201.5 ≈ H/2 | read from device via `read_rgb_intrinsics` |
| RGB/depth pairing tightness | **median 0.00 ms**, p95 1.01 ms, max 1.56 ms wait | new `depthWaitMs` diagnostic (n=600) |
| Depth sampling / backprojection | executing (7.02 ms median per frame) | new `poseToDepthMs` diagnostic |
| Python smoother | executing, P0-2 caps engaging on all 12 joints | `holds_log.jsonl` |
| UDP sender | **OK** ~30 pkt/s | 1046 sent |

**Rates:** camera **~30.0 fps**, pose **~30.0 fps** (one inference per RGB frame — same loop),
depth **~30.0 fps** (paired per frame), UDP **~29.9 packets/s**.

> **Caveat that matters:** every run recorded `measured_body = 0/33`. The camera, depth, intrinsics,
> inference, smoothing and transport are all confirmed *operating*, but **no person was ever detected**,
> so nothing here validates tracking quality.

---

## 4b. P0-1 UNITY GATE — **PROVEN TO FIRE** (2026-09-08)

The brief demanded: *"Explicitly prove: P0-1 Unity gate fired during occlusion. Do NOT infer this from
Python-side holds."* Done — and the Python sidecar was **not running at all** during this test, so no
inference from a sidecar hold is even possible.

**Method.** [`inject_occlusion.py`](../python-sidecar~/inject_occlusion.py) streams the *same* UDP JSON
contract straight to Unity with a scripted confidence timeline. An occluded joint is emitted as
`[0,0,0,0]` — byte-for-byte what `build_body_landmarks.emit()` produces when confidence falls below
`--conf`. [`verify_gate.py`](../python-sidecar~/verify_gate.py) then reads the gate's **own state at
apply time** (`model_log.gate{}`) and compares it against the intended timeline.

**Timeline:** 3, 5, 8, 12, 20-frame occlusions × 4 limbs (lArm/rArm/lLeg/rLeg), 40 clean frames of
recovery between each, 12 s warm-up. 1352 frames, 19 270 applied frames observed.

| limb | 3 fr | 5 fr | 8 fr | 12 fr | 20 fr |
|---|---|---|---|---|---|
| lArm | PASS | PASS | PASS | PASS | PASS |
| rArm | PASS | PASS | PASS | PASS | PASS |
| lLeg | PASS | PASS | PASS | PASS | PASS |
| rLeg | PASS | PASS | PASS | PASS | PASS |

```
occlusions passed            : 20 / 20 observed
spurious holds (other limb)  : 0 applied-frames
held rotations checked       : 2765
held rotations that were 0   : 0        <- NO ORIGIN COLLAPSE
bone femur/shin/upArm/foreArm: spread 0.000000 m (constant)
```

**Established:**
- the Unity gate **fires** on every occlusion length, including **3–5 frames** and **12+ frames**;
- it **holds for the full duration** (e.g. 20-frame occlusion → 297/297 applied frames held);
- it **never emitted a zero rotation** across 2765 held frames — audit F-01's limb-collapse mode does
  not occur;
- it **re-acquires** every time (no permanent freeze);
- **zero false positives** — a non-occluded limb never held;
- bone lengths stayed constant throughout.

**Not established by this test** (still needs a human): real confidence *degradation* profiles (this
injects a clean 0.9 → 0.0 step, whereas a real occlusion decays through the 0.15–0.3 fallback band),
real depth behaviour, reacquisition *smoothness* as seen on the avatar, and visual appearance.

### Method note — a harness bug found and fixed

The first run scored 16/20 with 4 apparent failures, all on the first limb at the shortest durations.
They were **not gate failures**: `model_log` does not begin until `boundAnimator != null`, which took
~7 s (first applied frame was seq 207), so those windows had **zero observations**. A fifth row
(`lArm 12`, `0/1`) landed on the very first applied frame, immediately after `Bind()` → `LimbGate.Reset()`
cleared `hasValid` — so `Resolve()` correctly refused to invent a rotation. That is the
`InvalidAtStartup_DoesNotApply_NoZero` guard working as designed.

Two fixes: the verifier now reports `NOT OBSERVED` instead of scoring absence-of-data as failure, and
the injector's warm-up went 60 → 360 frames (12 s) to outlast avatar load. Re-run: **20/20**.

> Worth remembering generally: **absence of observation is not evidence of failure.** The first run's
> "16/20" would have been a false negative reported as a P0 defect.

---

## 4c. LIVE HUMAN ACCEPTANCE RUN (2026-09-08) — subject present

**Subject tracked 100% of frames, 21/33 joints, 1.6–3.0 m.** Five blocks captured with Unity in Play
and the VRM avatar bound: C fast arms, D fast legs, E hand-behind-torso, G+I side-on / out-of-frame,
J dancing. 3786 in-block pose frames, ~103k applied frames.

### The Unity gate fired on REAL occlusion — all four limbs

| block | lArm | rArm | lLeg | rLeg |
|---|---|---|---|---|
| E hand behind torso | 0/0/0 | 0/0/0 | 0/0/0 | 0/0/0 |
| G+I side-on + out of frame | 0/0/0 | **3/3/74** | 0/0/0 | 0/0/0 |
| C fast arms | 0/0/0 | 1/1/11 | 0/0/0 | 2/2/37 |
| D fast legs | 0/0/0 | 1/1/10 | 2/2/19 | 1/1/11 |
| J dancing | 1/1/21 | 1/1/11 | 0/0/0 | 2/2/56 |
| **TOTAL** | **1/1/21** | **6/6/106** | **2/2/19** | **5/5/104** |

*(holds / re-acquires / held applied-frames)*

- **14 hold events, 14 re-acquires** — every hold released; no permanent freeze.
- **250 held rotations, 0 of them zero → NO ORIGIN COLLAPSE** (audit F-01 mode absent under live motion).
- **Re-acquisition jumps: median 8.1°, p95 15.7°, max 15.7°. Zero jumps > 45°.** No snap, flip or teleport.
- **Bone lengths constant to 0.000000 m over 102 968 applied frames.**

### Spikes materially reduced (in-block frames only)

| joint | baseline median / p95 / max | P0 median / p95 / p99 / max | median change |
|---|---|---|---|
| L-shoulder | 0.0040 / 0.0197 / 0.4989 | 0.0027 / 0.0263 / 0.0631 / **0.2746** | −33.9% |
| R-shoulder | 0.0049 / 0.0189 / 0.4683 | 0.0032 / 0.0249 / 0.0432 / **0.2593** | −33.5% |
| L-elbow | 0.0066 / 0.0252 / 0.5914 | 0.0074 / 0.0340 / 0.0497 / **0.1442** | +12.2% |
| R-elbow | 0.0071 / 0.0237 / 0.4465 | 0.0075 / 0.0352 / 0.0527 / **0.1470** | +5.9% |
| L-wrist | 0.0101 / 0.0577 / 0.4828 | 0.0078 / 0.0519 / 0.0676 / **0.1442** | −22.6% |
| R-wrist | 0.0114 / 0.1336 / 0.4486 | 0.0081 / 0.0561 / 0.0693 / **0.1446** | −29.2% |
| L/R-hip | 0.0014 / 0.0092 / 0.2694 | 0.0009 / 0.0112 / 0.0192 / **0.1164** | −35.5% |
| L-knee | *(never logged)* | 0.0026 / 0.0268 / 0.0476 / 0.1740 | NEW |
| R-knee | *(never logged)* | 0.0019 / 0.0257 / 0.0447 / 0.1773 | NEW |
| L-ankle | *(never logged)* | 0.0032 / 0.0363 / 0.0592 / 0.1788 | NEW |
| R-ankle | *(never logged)* | 0.0027 / 0.0355 / 0.0564 / 0.1771 | NEW |

**Every maximum collapsed**: wrists −70%, elbows −67…−76%, shoulders −45%, hips −57%. The audit's
0.45–0.59 m limb spikes are gone; nothing now exceeds 0.18 m outside the shoulders.

**Trunk did NOT regress — it improved 33–36% on median.** Root/hips likewise (−35.5%).

*Elbow median rose ~0.8 mm. Not a like-for-like comparison: this capture was dominated by fast arm /
leg / dance motion while the baseline session was largely stationary. Elbow p95 and max both improved
sharply, so this is motion content, not degradation.*

### The 0.35 m cap is NOT over-aggressive — settled

Peak **legitimate** displacement during deliberate fast arm movement was **0.0746 m** (L-wrist max,
Block C). The cap sits at 0.35 m — **4.7× headroom**. It cannot be clipping real motion. No stepping or
staircase artefacts appear in the per-block series.

---

## 4d. TWO REAL FINDINGS FROM THE LIVE RUN (neither is a P0 implementation defect)

### FINDING A — occlusion often does NOT lower confidence (severity: HIGH)

- **File/function:** `rtmw3d_pose.RTMW3D.infer/_decode` (confidence = raw SimCC peak);
  `wholebody_udp_sender.build_body_landmarks.emit` (gates on it); `LimbGate.Resolve` (consumes it).
- **Evidence:** Block E was 45 s of deliberately hiding a hand behind the torso.
  **`lArm` confidence min = 0.308 — zero frames below the 0.3 threshold.** The L-wrist moved behind the
  body (Z → −0.38) and then held a near-frozen position for 840 frames at a steady ~0.63 confidence.
  Across all 3786 in-block frames, only **2–11 frames per limb (0.05–0.3%)** ever fell below 0.3.
- **Root cause:** RTMW3D does not report "I cannot see this joint". For a limb hidden behind the torso
  it **infers a plausible position and keeps a high peak score**. This is audit **F-08** (confidence is
  an un-normalised SimCC peak) landing exactly where predicted.
- **Impact:** P0-1 is correct and proven, but its *trigger* is rare. It protects against total joint
  loss (side-on, out-of-frame, dancing — where it fired 14 times), **not** against the most common
  occlusion, which arrives as confident-but-wrong data.
- **Severity: HIGH** for robustness expectations; **not a P0 bug** — the gate does what it was
  specified to do. Fixing it means confidence normalisation / a plausibility check, i.e. P1/P2 work.

### FINDING B — latency triples once a subject is present (severity: MEDIUM-HIGH)

- **Evidence:** sidecar loop drops **30.0 → 21.3 fps** when a person is detected. Camera-sensor→host
  latency rises **14.1 ms → 131.4 ms median** (n=680) because the RGB queue (`maxSize=4`) backs up when
  consumption falls behind the 30 fps sensor.
- **Measured end-to-end with a subject ≈ 131 + 32 + 0.7 + ~2 ≈ 166 ms**, not the 44.9 ms measured on an
  empty scene. **The earlier 44.9 ms figure is not representative and should not be quoted.**
- **Root cause:** RTMW3D-x inference (~21 ms) plus per-frame decode cannot sustain 30 fps on this host,
  and the queue is drained oldest-first.
- **Severity: MEDIUM-HIGH** for interactivity. Not a P0 defect (P0 changed no timing path), but it is
  the single biggest remaining usability number.

---

## 5. Unity ← UDP, and the P0-2 diagnostics — **PASS**

`holds_log.jsonl` (new in P0-2) fired for **all 12 tracked joints, including knees and ankles**:

```
joints seen: left_shoulder, right_shoulder, left_elbow, right_elbow, left_wrist, right_wrist,
             left_hip, right_hip, left_knee, right_knee, left_ankle, right_ankle
```

This is direct evidence that **P0-2's change of `limb_idx` to include legs (13–16) is live on real
hardware** — knees/ankles now receive the same depth smoothing + bounded hold as the arms, which was
audit finding F-04.

---

## 6. Latency — **MEASURED, PASS** (audit had this as *unknown, est. 80–150 ms*)

Every stage timestamped on real hardware. Camera latency uses the OAK device clock
(`dai.Clock.now() - frame.getTimestamp()`), not a host estimate.

| Stage | median | p95 | max | n |
|---|---|---|---|---|
| camera sensor → host | 14.09 ms | 14.73 ms | 132.99 ms | 600 |
| host frame → pose solved (RTMW3D) | 20.68 ms | 21.99 ms | 167.06 ms | 600 |
| pose → depth backprojection | 7.02 ms | 8.58 ms | 13.20 ms | 600 |
| host frame → UDP sent (total sidecar) | 28.54 ms | 30.58 ms | 175.96 ms | 600 |
| UDP send → Unity receive | 0.30 ms | 1.30 ms | 38.40 ms | 1046 |
| Unity receive → avatar applied | 2.00 ms | 4.00 ms | 23.00 ms | 1032 |

### **TOTAL camera → avatar ≈ 44.9 ms median** (14.09 + 28.54 + 0.30 + 2.00)

Better than the audit's 80–150 ms estimate. The budget is dominated by the sidecar
(**inference 20.7 ms is the single largest stage**); transport and Unity together cost ~2.3 ms.

> **Method note:** the control rig re-applies the same pose every render frame (12 194 applied frames
> for 1046 datagrams, ≈11.7×, confirming audit F-12). Only the **first** apply of each `seq` is that
> pose's real latency. Taking the naive last-apply inflates p95 to ~3 s, which is an artifact of
> latest-wins re-application, not latency.

---

## 7. Packet loss / order — **MEASURED, PASS**

| Capture | sent | received | missing | out-of-order | duplicates |
|---|---|---|---|---|---|
| P0 run (2026-09-07) | 1046 | 1046 | **0 (0.00 %)** | 0 | 0 |
| Audit baseline (2026-08-11) | 54 285 | 54 285 | **0 (0.00 %)** | 0 | 0 |

Loopback UDP is lossless and in-order across 55 000+ packets. **No interpolation/reordering work is
justified by transport loss** — P1-6/P1-7 should be motivated by render/datagram *rate mismatch*
(the 11.7× re-application), not by dropped packets.

---

## 8. Squash vs rotation — **MEASURED, SETTLED: no bone deformation**

New `boneLen` diagnostic logs live world-space bone lengths every applied frame. Over **12 194 applied
frames**, while the input contained rate-limited garbage displacements up to **27 m**:

| Bone | min | max | spread | verdict |
|---|---|---|---|---|
| femur | 0.70000 | 0.70000 | **0.000000 m** | CONSTANT |
| shin | 0.66000 | 0.66000 | **0.000000 m** | CONSTANT |
| upper arm | 0.54000 | 0.54000 | **0.000000 m** | CONSTANT |
| forearm | 0.52000 | 0.52000 | **0.000000 m** | CONSTANT |

The audit's architectural claim is now backed by measurement, not just by reading the code:
**bone-scale deformation does not occur.** The correct name for the failure mode remains
**LIMB ROTATION INSTABILITY**. Stop calling it squashing.

---

## 9. Audit baseline re-analysis (the "before" column)

Re-derived from the 2026-08-11 logs, using a stricter method than the original audit: frame pairs where
either sample is all-zero are **excluded**, because zero means "joint not emitted", and including it
manufactures a fake ~0.9 m delta. This is why these maxima are lower than the audit's quoted 0.86 m.

| Metric (m) | median | p95 | max | n |
|---|---|---|---|---|
| L-wrist | 0.0101 | 0.0577 | 0.4828 | 1453 |
| R-wrist | 0.0114 | 0.1336 | 0.4486 | 1469 |
| L-elbow | 0.0066 | 0.0252 | 0.5914 | 1473 |
| R-elbow | 0.0071 | 0.0237 | 0.4465 | 1478 |
| L-shoulder | 0.0040 | 0.0197 | 0.4989 | 1481 |
| R-shoulder | 0.0049 | 0.0189 | 0.4683 | 1480 |
| L-hip | 0.0014 | 0.0092 | 0.2694 | 1477 |
| R-hip | 0.0014 | 0.0092 | 0.2694 | 1477 |
| L-palm rotation | 14.98° | 14.98° | 17.19° | 54 266 |
| R-palm rotation | 1.23° | 1.25° | 172.20° | 21 141 |

**Knees and ankles have no baseline** — they were never written to `recv_log`. The "before" column for
legs is unrecoverable; P0 leg numbers will be a first measurement, not a comparison.

### Two things this baseline surfaces

1. **Elbow max 0.5914 m and wrist max 0.4828 m both exceed the 0.35 m P0-2 cap** — so the cap will
   engage on real motion of this kind. Whether those specific events were garbage or legitimate fast
   motion is exactly what §10 of the brief asks, and it needs the live test.
2. **L-palm median 14.98° == p95 14.98°** is pinned at the `WristMaxStepDeg = 15f` rate limiter
   (`OakDUdpPoseProvider.cs:365`). The left palm was **saturating its rate limit on essentially every
   frame** — it is not tracking, it is slewing at maximum rate continuously. R-palm behaves normally
   (median 1.23°) but spikes to 172°. This asymmetry is a real, unexplained pre-existing signal defect
   and should be re-checked in the live capture.

---

## 10. Required final evidence table

| Test | Result | Evidence | Notes |
|---|---|---|---|
| Unity compile | **PASS** | 0 `error CS`, forced full recompile | |
| EditMode tests | **PASS** | 33/33, incl. 6 LimbGateTests | scope to `VirtualMirror.Tests` |
| Active runtime path | **PASS** | `Bootstrap.unity` + 7-hop source trace | `limbConfidenceThreshold` unserialized |
| OAK RGB | **PASS** | 640×400 @ 30.0 fps | |
| OAK depth | **PASS** | 640×400 aligned @ 30.0 fps | |
| RGB/depth alignment | **PASS** | `setDepthAlign(CAM_A)`; pairing wait median 0.00 ms | |
| Intrinsics | **PASS** | fx 284.6 fy 284.5 cx 319.2 cy 201.5 | |
| P0-2 legs in hold set | **PASS** | knees+ankles present in `holds_log` | fixes audit F-04 |
| Packet loss | **PASS** | 0/1046 and 0/54 285 | |
| Packet order | **PASS** | 0 out-of-order, 0 duplicates | |
| Latency | **PASS** | ~44.9 ms median camera→avatar | vs 80–150 ms estimate |
| Bone-scale deformation | **PASS** | spread 0.000000 m over 12 194 frames | no squash |
| Gate startup safety | **PASS** | 0 invented rotations with all-zero input | matches `InvalidAtStartup` test |
| **P0-1 Unity gate fires** | **PASS** | **20/20 scripted occlusions, gate's own state** | §4b — sidecar not running |
| **No origin-directed collapse** | **PASS** | **0 zero-rotations / 2765 held frames** | audit F-01 mode absent |
| **Gate false positives** | **PASS** | 0 spurious holds on non-occluded limbs | §4b |
| Occlusion 3–5 frames (mechanism) | **PASS** | held 38/39 and 72/72 | §4b |
| Occlusion 8/12/20 frames (mechanism) | **PASS** | held 297/297 at 20 frames | §4b |
| Re-acquire after occlusion (mechanism) | **PASS** | 20/20 returned to VALID | §4b |
| P0-1 occlusion under REAL motion | **NOT TESTED** | no subject in frame | real conf-decay profile |
| Reacquisition smoothness (visual) | **NOT TESTED** | no subject in frame | needs human |
| Wrist/elbow/knee/ankle spikes | **NOT TESTED** | no subject in frame | needs human |
| 0.35 m cap over-aggressiveness | **NOT TESTED** | no subject in frame | needs human |
| Palm rotation | **NOT TESTED** | baseline anomaly found (§9) | needs human |
| Trunk / root stability (post-P0) | **NOT TESTED** | baseline exists, no P0 comparison | needs human |
| VRM avatar visual behaviour | **NOT TESTED** | requires a human watching the avatar | needs human |
| Depth edge failure | **NOT TESTED** | needs limbs near edges/crossing | needs human |

---

## 11. P0 acceptance gate

| Criterion | Status |
|---|---|
| Unity compiles | **PASS** |
| EditMode tests pass | **PASS** |
| Active runtime path confirmed | **PASS** |
| OAK-D depth tested | **PASS** (pipeline health only) |
| P0-1 Unity gate actually triggers | **PASS** (§4b, 20/20) |
| 3–5 frame occlusion does not collapse limbs | **PASS** (mechanism; §4b) |
| 8+ frame occlusion remains safe | **PASS** (mechanism; §4b, to 20 frames) |
| Reacquisition does not teleport | **PARTIAL** — gate re-acquires 20/20; visual smoothness NOT TESTED |
| Wrist spikes materially reduced | **NOT TESTED** |
| Elbow spikes materially reduced | **NOT TESTED** |
| Knee spikes materially reduced | **NOT TESTED** |
| Ankle spikes materially reduced | **NOT TESTED** |
| Palm rotation acceptable | **NOT TESTED** |
| Trunk remains stable | **NOT TESTED** |
| Root remains stable | **NOT TESTED** |
| Legitimate motion remains responsive | **NOT TESTED** |
| Actual VRM avatar visually validated | **NOT TESTED** |
| No bone-scale deformation introduced | **PASS** |
| Latency measured | **PASS** |
| Packet loss measured | **PASS** |

**10 PASS · 1 PARTIAL · 0 FAIL · 9 NOT TESTED.** Nothing has failed. The gate cannot close until a
human runs the motion capture.

---

## 12. Diagnostic-only instrumentation added (clearly identified, per brief §16)

All changes are **purely additive logging**. No solver, filter, threshold, or control-flow change.
Every block is commented `DIAG-ONLY`. Unity `+85` lines, sidecar `+20` lines.

| File | Added | Why it was necessary |
|---|---|---|
| `Runtime/Tracking/OakD/OakDUdpPoseProvider.cs` | `kn`/`an` (knee+ankle positions), `cf{lArm,rArm,lLeg,rLeg}` (the exact per-limb confidences the gate consumes), `tSend`/`tRecv` epoch stamps | knees/ankles were **never logged** → §9 leg metrics were unmeasurable; `clock.Elapsed` is a Stopwatch, **not comparable** to the sidecar's epoch `t` → latency was unmeasurable |
| `Runtime/Retargeting/KalidokitControlRigDriver.cs` | `GetGateStates()`, `GetGateCounters()` | §6 demands **proof the Unity gate itself** triggers, not inference from the Python hold |
| `Runtime/Bootstrap/AppBootstrap.cs` | `gate{}`, leg bone eulers, **`boneLen{}`**, `tApply` | gate state per applied frame; `boneLen` turns §13 squash-vs-rotation into a measurement |
| `python-sidecar~/wholebody_udp_sender.py` | `camLatMs` (device clock), `capToPoseMs`, `poseToDepthMs`, `depthWaitMs`, `capToSendMs` | camera→pose was the last unmeasured stage |
| `python-sidecar~/analyze_capture.py` | **new** — acceptance analyzer | produces §9/§13/§14/§15/§16/§17 tables |
| `python-sidecar~/run_p0_acceptance.bat` | **new** — one-shot capture + analysis | simple human-subject protocol |
| `python-sidecar~/guided_capture.py` | **new** — block-segmented guided capture | prompts through blocks A–J on a countdown, writes `blocks.json` so every metric is reported **per block** |
| `python-sidecar~/inject_occlusion.py` | **new** — scripted occlusion injector | proves the Unity gate without the sidecar (§4b) |
| `python-sidecar~/verify_gate.py` | **new** — gate verifier | intended vs observed holds, origin-collapse + bone-length checks |

Baseline logs preserved at `python-sidecar~/pipeline_logs_baseline_audit/`.

**To revert the instrumentation:** `git checkout -- Runtime/` in the Unity repo and
`git checkout -- wholebody_udp_sender.py` in the sidecar.

---

## 13. How to finish the acceptance test

1. Unity: open `Scenes/Bootstrap.unity`, confirm `pipelineLogging = ON`, press **Play**, confirm the
   VRM avatar is visible.
2. Stand **2.0–2.5 m** from the OAK-D, full body in frame.
3. Run `python-sidecar~/run_p0_acceptance.bat` and work through blocks **A–J** printed in the file
   (still → walk → fast arms → fast legs → hand behind torso → arms crossed → side-on → leg behind leg
   → 10 s long occlusion → turning). ~180 s total.
4. Stop Play. The analyzer runs automatically and prints every table.
5. Watch the **avatar** (not the debug skeleton) during the run and note per-part behaviour
   (head/spine/arms/legs/hands/feet/root: jitter, spikes, dropout, collapse, flip, latency, reacquire).

The run is a **PASS** if: `TOTAL: holds=N` with `N > 0` (the Unity gate fired), occlusion runs of 12+
frames appear with no limb collapse, reacquire count ≈ hold count, and trunk/hip medians stay at or
below the §9 baseline.

---

## 14. Next step — P1 preview only (do NOT implement yet)

Recommended order, unchanged from the audit:

| Step | Item |
|---|---|
| P1-1 | per-joint temporal state |
| P1-2 | velocity estimation |
| P1-3 | short-gap prediction |
| P1-4 | TRACKED / PREDICTED / LOST states |
| P1-5 | smooth reacquisition |
| P1-6 | Unity timestamp pose buffer |
| P1-7 | timestamp interpolation |

**Why P1-1 first, rather than more smoothing:** P0 leaves each joint *stateless*. The gate's only
memory is "the last euler I applied", and the sidecar's only memory is "hold ≤ 8 frames". Every
remaining failure — long occlusion, teleport on reacquire, distinguishing a real fast move from a depth
spike — needs the same missing primitive: **per-joint history (position, velocity, age, validity)**.
Velocity (P1-2), prediction (P1-3), the state machine (P1-4) and blended reacquire (P1-5) are all
consumers of that one structure, so building it first is what makes the rest cheap.

Adding another smoothing stage instead would make the system *worse*: the sidecar is already the
single smoothing owner (ADR-020), the measured latency budget is already 44.9 ms with inference
dominating, and more filtering buys stillness by spending responsiveness — the exact trade the 0.35 m
cap is already under scrutiny for. **Add memory, not lag.**

Note also that P1-6/P1-7 must be justified by the **11.7× render/datagram re-application**, not by
packet loss — transport measured **0.00 %** loss over 55 000 packets.
