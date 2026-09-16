# Virtual Mirror — Tasks

Update this file whenever work starts or finishes. Prefer small checkboxes agents can claim.

---

## ⚠ F-26 AVATAR vs DEBUG SKELETON (2026-09-16) — observation report, no decision taken

Screen recording reviewed (`LatestScreenReording.mp4`, 129 s). **The tracking is not the problem; the
retarget is.** The debug skeleton follows the subject through every pose in the video — including
seated and arms-overhead — while the VRM avatar diverges, monotonically, the further the subject gets
from neutral standing.

- [ ] **Full report: [F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md](F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md)**
      — frame-by-frame table, complete avatar/skeleton comparison, failure mechanisms, four resolution
      options. Evidence in `docs/evidence/F26_avatar_vs_skeleton/`.
- [ ] **⚠ "AVATAR QUALITY — PROVEN GOOD" does not mean pose fidelity.** Every F-19 avatar metric
      (0.012° median yaw, zero snaps, bone lengths to 0.0011 %, zero swaps) measures **internal
      consistency**, not correspondence to the subject. All four are compatible with an avatar that is
      smoothly and stably in the WRONG pose — which is what the video shows. F-19 is not falsified; the
      inference carried into roadmap.md is. See F-26 §5.1.
- [ ] **⚠ No fidelity metric has ever existed.** Nothing compares avatar pose to subject pose. That
      absence is why this went unnoticed, and it is the first thing to fix whatever else is decided.
      Per-joint angular error between the skeleton's bone directions and the avatar's is computable
      today, offline, with no new hardware. F-26 §9.1.
- [ ] **⚠ F-19's requested elbow flexion clamp (~150°) was never built.** The only 150° in the
      retarget is `TorsoYawGuard.WrapGuardDeg`, unrelated. The elbow case was closed instead by F-22
      zeroing confidence in the sidecar — a hold, not a clamp, at a different layer. Possibly the
      better design; never recorded as a substitution. F-26 §5.4.
- [ ] **Top avatar defects, by size:** (1) the avatar cannot leave vertical — nothing drives root
      rotation and the spine-bend high-pass treats a sustained lean as drift, so it cannot sit;
      (2) arm elevation is lost — the hand is used as a direction, never as a target;
      (3) legs splay, no hip-width normalisation; (4) a held limb reads worse than a missing one.
- [ ] **`showDebugSkeleton` defaults to false** — the most informative diagnostic in the project is
      off by default.

## ✅ OFFLINE HAND-OFF-FIX PASS (2026-09-15) — the §27 silent hand-off is closed

```text
F-21   CONDITIONAL - OFFLINE FIX VERIFIED, LIVE ACCEPTANCE PENDING
F-22   OFFLINE ENGINEERING COMPLETE      LIVE HANDS-NEAR-FACE ACCEPTANCE PENDING (L2)
F-20A  PASS
F-20B  PASS, unchanged - the §31 supervisor passthrough is additive (35/35 still)
```

- [x] **F-21 path-consistency gate (ADR-057)** — a reacquire must now be *path*-consistent, not only
      position-consistent: a candidate tracked walking in from outside the margin is refused
      (`reason="path_walked_in"`). Sidecar-only, ~20 lines, no new identity signal, no Runtime/
      change. Reuses `SWITCH_MARGIN_M` as the continuity distance rather than adding a tunable —
      measured separation: largest real single-frame body displacement **126.1 px** vs a 141 px
      margin vs the three real body-to-body jumps of **303.5, 323.0 and 687.5 px** (2.15× clear of
      the smallest of them).
- [x] **The number that matters, on the clip that showed the bug** — `123.webm`, whole production
      chain, production config, against offline ground-truth labels:
      **ownership off 102 → F-21 as shipped 40 → F-21 + gate 2** wrong-person frames, and those
      last 2 are a *verified labeller artefact* during the subject's entry, present identically in
      all three arms. **The hand-off segment itself is 0.** The hand-over that does occur is now
      **declared** — `TARGET_RELEASED` + `TARGET_SWITCH` + a new epoch.
- [x] **The cost, measured rather than assumed** — 96 frames (1.60 s) of the *legitimate returning
      owner* withheld, one contiguous block. A returning owner and an intruder walking in are the
      **same observation stream** from a hip position; the gate does not resolve that ambiguity, it
      resolves it towards *announcing* the change. Brief §7 and ADR-057 both say that is the right
      way round. ~2.5 frames of the right person withheld per frame of the wrong person suppressed —
      a property of this clip, **not** an installation rate.
- [x] **No regression where the gate should never fire** — `video.webm` (single person) and
      `456.webm` (eight dancers, real crossing/occlusion) are **bit-identical** with the gate on and
      off: 435/431/4 and 356/347/9, zero releases, zero switches, zero path rejections in both.
- [x] **Tests** — unit **46/46 → 61/61**; deterministic adversarial **63/63 → 101/101** with the
      brief's P1–P11 added and the original 17 scenarios untouched. **P11 is the control**: the same
      input with the gate off reproduces the silent hand-off exactly (43 wrong-person frames,
      `switches=0`, `epochs=1`), which is what makes the claim falsifiable. It is excluded by name
      from the suite aggregate so a deliberately-disabled run can never read as production behaviour.
- [x] **The measuring instrument was wrong three times, and that is in the report** — median hue →
      largest share → "absence of green means the other person". The last one anchored an epoch to
      the wrong person and reported **429** wrong-person frames. Two further filters were built and
      **removed** because they deleted the man from every two-person frame. Now validated against 41
      eye-checked frames: **36/41, 4 conservative (excluded from scoring, cannot invent a
      wrong-person frame), 1 dangerous and proven unreachable.** See F-21 §30.6.
- [x] **Performance** — isolated A/B over 775 real observations × 200 reps: **+0.204 µs/frame**,
      +14.5 % of the ownership layer, **+0.0006 % of a 30 fps frame budget**. Three scalars of state,
      no buffer, no allocation.
- [x] **⚠ PRODUCTION DEFECT FOUND: `--cue-file` had never worked (§31.4)** —
      `wholebody_udp_sender._read_cue()` calls `io.open` in a module that **never imported `io`**, and
      its own `except Exception: return None` swallowed the `NameError` on every frame. `_read_cue` is
      the only user of `io` in that file, so nothing else surfaced it. **The on-screen cue banner has
      been silently dead since it was written** — the banner REV 2 added specifically because the
      first live attempt was inconclusive when the operator couldn't read console instructions at the
      camera. Tomorrow's session would have repeated that failure with a report saying it was fixed.
      One-line fix, verified by actually rendering the banner (imaged). Found by *testing* the
      feature, not by reading the code — an absent banner looks like operator error, not a bug.
- [x] **Supervisor + protocol integration, real OAK-D, empty room (§15/§31.3)** —
      `f21_supervisor_integration.py` **11/11**: supervisor starts the sidecar, both flags reach the
      running process (read from its argv, not inferred), the preview window is enumerated from
      user32, the cue round-trips through the real reader and renders, the ownership log lands where
      the protocol reads it, `SidecarExit` is detected and restarted, and never more than one sidecar
      tree is alive. Two initial "failures" were *this test* being wrong, not the product — recorded
      in §31.3, because a test wrong in the optimistic direction is the dangerous kind.
- [x] **Dead-producer abort path TESTED, not just written** —
      `f21_live_protocol_abort_test.py` **9/9**: a stub producer is killed mid-phase and the protocol
      returns non-zero, records rc / phase / wall-clock / last ownership state / completed phases, and
      **does not run the phases after the death**.
- [x] **Offline replay soak, twice, and a harness bug found doing it (§30.13)** — 17 min / 21 cycles
      / **31,851 frames / 0 exceptions**, memory ends *below* warm-up, 80 TEMP_LOST all reacquired,
      2,770 holds / 2,770 recoveries / 0 unrecovered. But it logged **0 path-gate rejections** across
      21 replays of the hand-off clip, which did not add up: `f2x_replay_soak.py` fed ownership
      **metres** while setting `switch_margin_m` from a **pixel** diagonal — a ~141 *metre* gate, so
      nothing could ever be rejected. Pre-existing harness defect, not production. **Every soak to
      date therefore exercised F-21's plumbing and never its discrimination**, and its clean "0
      releases" line read as correctness when it was an artefact. Fixed to production constants and
      re-run against `123.webm` with per-cycle reset: **600 `path_walked_in` rejections, 20 declared
      TARGET_SWITCHes, 15,662 frames, 0 exceptions, no memory growth, 749/749 recoveries.**
- [x] **Live tooling fixed before the session (§31)** — `f21_live_protocol.py` now polls `proc.poll()`
      during every wait and aborts with return code / phase / timestamp / last F-21 state instead of
      scoring phases against a dead producer; `sidecar_supervisor.py` forwards `--show` and
      `--cue-file` **explicitly** (not a generic `--extra-args`, which could also forward
      `--no-ownership` and change what is under test). New phases `WALK_IN_IMPOSTOR` /
      `WALK_IN_OWNER` target this pass's finding directly.

## ✅ OFFLINE COMPLETION PASS (2026-09-14) — no physical person available

Everything provable without someone standing at the camera is done. Two live acceptance gates remain
and **only** those; the next hardware session is validation, not development.

```text
F-21   OFFLINE ENGINEERING COMPLETE      LIVE ACCEPTANCE PENDING (two-person matrix)
F-22   OFFLINE ENGINEERING COMPLETE      LIVE HANDS-NEAR-FACE ACCEPTANCE PENDING (L2)
F-20A  PASS, reconnect re-verified end to end against the real C# consumer
F-20B  PASS, restored - a production-blocking empty-room defect was found and fixed (ADR-055)
```

- [x] **F-21 deterministic multi-target suite** — `f21_adversarial.py`, all 17 brief scenarios,
      **63/63**. Ground-truth labels make it measure *silent wrong-person emission*, which the
      `TARGET_SWITCH` counter structurally cannot see. 0 false releases, 0 false reacquisitions,
      0 wrong-person frames outside the deliberately-provoked identical-build crossing.
- [x] **F-21 defect found + fixed (ADR-054)** — a returning owner past `REACQUIRE_WINDOW` was
      rejected 51× as `not_owner`, released, and re-acquired with a spurious `TARGET_SWITCH`.
      Unit suite **36/36 → 46/46** with two regression guards.
- [x] **F-21 real multi-person replay** — `f21_multiperson_replay.py` on 8-dancer footage, margin
      sweep incl. the production-equivalent 141 px: **0 switches, 0 releases at every margin**;
      false rejections begin at ~126 px, i.e. ~11 % headroom.
- [x] **⚠ F-21 silent wrong-person hand-off REPRODUCED AND IMAGED (ADR-056)** — on real two-person
      footage F-21 refused the intruder for 54 frames, then re-locked onto them as the *same*
      `target_id` when they walked into the owner's vacated spot. `TARGET_SWITCH = 0`,
      `TARGET_RELEASED = 0` — invisible to the consumer. **Now FIXED — see the 2026-09-15 block
      below (ADR-057).** ADR-056's premise that only live data could price the fix turned out to be
      wrong: offline ground-truth labelling measured that cost directly.
- [x] **F-22 adversarial suite** — `f22_adversarial.py`, **120/120** across 18 cases derived from
      real frames, each proving raw observation → F-22 → `conf_emit` → the **real**
      `build_body_landmarks` output the consumer receives. No threshold was changed to pass anything.
- [x] **F-22 threshold analysis recomputed in real degrees** — the old §15 table was a mixed-unit
      pixel space. Real vs synthetic evidence reported strictly separately; no combined accuracy
      figure. **0 absolute-angle false rejections on legitimate single-person footage** (435
      frames; 1 single-frame rate-based hold, recovered next frame).
- [x] **F-22 real rejections classified, not counted** — all 12 absolute-angle rejections checked
      against the raw image; cause is multi-person interference (cross-person keypoint fusion /
      occlusion collapse), not single-person noise. Finding: **nothing before F-22 constrains the
      limbs to belong to the same body as the hips.**
- [x] **F-22 performance A/B** — closes §18's own gap: **20.1 µs/frame** (p95 20.2, max 59.8),
      0.060 % of a 33.3 ms frame. End-to-end replay 37.10 → 36.91 fps, reported with its limitation.
- [x] **F-20B empty-room defect found + fixed (ADR-055)** — `frames=` heartbeat was gated behind a
      successful pose emission, so an empty room read as a stuck startup and the supervisor killed a
      healthy sidecar every 45 s → `FAILED_PERMANENT` in ~4 min. Suite **4/6 → 6/6 with no person
      present**.
- [x] **F-20B deployment hardening 35/35** — `f20b_deployment_verify.py`: `run_supervisor.bat`,
      logon-registration procedure (verified statically, deliberately not executed), duplicate guard,
      post-reboot diagnostics freshness, no duplicate sidecars, real session ids.
- [x] **Regression** — Python 176/176 across 5 suites; Unity EditMode **170/170** via a reflection
      runner in the live Editor (the "124-test" figure was an undercount: 124 `[Test]` + 46
      `[TestCase]` = 170).
- [x] **Offline replay soak** — `f2x_replay_soak.py`, labelled `OFFLINE REPLAY SOAK`, explicitly not
      equivalent to a hardware soak. 16 min / 19 clip cycles / **29,595 frames / 0 exceptions**;
      30.83 fps sustained; **memory flat** (+3.5 MB half-mean over the run); 76 `TEMP_LOST` episodes
      all reacquired; **2,581 validator holds, 2,581 recoveries, 0 unrecovered**. Note: the first run
      reported a fake-clean "0.0 MB / +0.0000 MB growth" — an undeclared `ctypes` Win32 call failing
      silently; fixed and re-run before quoting. Detail in F-22 §28b.

---

## ⏳ F-21 — Single-Person Lock / Target Ownership (2026-09-14) — CONDITIONAL

See [`F21_SINGLE_PERSON_TARGET_OWNERSHIP_2026-09-14.md`](F21_SINGLE_PERSON_TARGET_OWNERSHIP_2026-09-14.md)
and ADR-052. `python-sidecar~/target_ownership.py` gates M15's person-box loop by identity (position +
scale continuity — no track id exists in this pipeline, verified from source) so a second person
entering frame cannot silently steal tracking (the F-19 failure).
- [x] State machine + M15 identity-gating integration, `target_events.jsonl` evidence, on-screen HUD
      (state/owner/timers/switch count) on the `--show` preview — `target_ownership.py` +
      `wholebody_udp_sender.py`.
- [x] Unit tests **46/46 PASS** (`test_target_ownership.py`, was 36/36 — two ADR-054 regression
      guards added 2026-09-14) — covers 12/15 of the brief's adversarial cases directly (crossing,
      rapid A/B/A, flicker-never-locks, scale mismatch, boundary timing).
- [x] Real-hardware: clean acquire/lock/emit; **5/5 PASS** sidecar-restart-with-person-present (fresh
      epoch, new SID, zero stale-identity carry-over — `f21_restart_interaction_test.py`);
      `--no-ownership` bit-identical regression check.
- [~] ~~Single-person video regression (`f21_video_replay.py`, 775 frames / ~13s energetic real dance
      motion): **PASS**, zero `TARGET_SWITCH`, zero `TARGET_RELEASED`.~~ **WITHDRAWN 2026-09-14** —
      that clip (`123.webm`) is not single-person; the flat `target_id` across its real person change
      is a silent hand-off, not stability (ADR-056). The single-person claim is now carried by
      `video.webm` (435 frames) and `456.webm`.
- [ ] **Live two-person matrix (blocks PASS)** — two attempts both failed for reasons unrelated to
      correctness: attempt 1 hit a test-script UX defect (instructions unreadable while coordinating
      two people — fixed with an on-screen cue banner); attempt 2 hit a real OAK-D device crash.
      Rerun `python-sidecar~/f21_live_protocol.py` with two people; specifically test the named
      "well-timed handoff" limitation (B stands exactly where A was the instant A leaves).
- [ ] **Follow-up (not blocking):** `f21_live_protocol.py` doesn't detect a dead sidecar subprocess
      and will narrate a full protocol against a crashed process — add a `proc.poll()` check.
- [ ] **Follow-up (not blocking):** `sidecar_supervisor.py` doesn't forward `--show`/`--cue-file` —
      needed to run the next live F-21 session through the supervisor for crash resilience.

---

## ⏳ F-22 — Human Pose Validation / Biomechanical Validation Layer (2026-09-14) — CONDITIONAL

See [`F22_HUMAN_POSE_VALIDATION_2026-09-14.md`](F22_HUMAN_POSE_VALIDATION_2026-09-14.md) and ADR-053.
`python-sidecar~/pose_validation.py` gates elbow/knee bend angle + angular rate (reuses P1-1's
existing `conf_emit=0` "invalid joint" contract, zero `Runtime/`/UDP changes) against F-19's live
evidence (left elbow ~177.5°, right elbow ~179.6° rendered, hands-near-face block).
- [x] Validator (`pose_validation.py`) + sender integration, per-chain localized gating, evidence
      logging (`oak_v4_evidence/f22/`), `--pose-validation`/`--no-pose-validation` (default ON).
- [x] Unit tests **22/22 PASS** (`test_pose_validation.py`) — includes F-19's own measured numbers
      fed directly as fixtures (168° → rejected; knee 176° legitimate-walking max → NOT rejected).
- [~] Offline replay (`f22_video_replay.py`, composes F-21 + F-22 in production order) against
      `video.webm`, 431 owned frames: **0 false absolute-angle rejections**; ~~elbow max
      (155.3°/158.8°) sat within 1–5° of REJECT (160°)~~; 3 momentary rate-based holds (0.7%), each
      recovered within one frame. **The angle figures are SUPERSEDED (2026-09-14)** — that harness
      computes angles in a mixed-unit pixel space. Recomputed in real degrees the same clip's elbow
      maxima are 150.1°/143.5°, i.e. **9.9°/16.5° of headroom**. See `f22_threshold_analysis.py`.
- [ ] **Live L2 session — hands near face (blocks PASS)** — no human was available this session
      (explicit instruction). This is the one test that reproduces F-19's own original defect
      against this validator's raw-geometry threshold. L1/L3–L7 also open, L2 is the named priority.
- [ ] **Follow-up (not blocking):** no CLI threshold-override flags added this pass (unlike
      F-20A/F-20B/F-21's `--*` pattern) — thresholds live only in `pose_validation.py`'s constants.
- [x] ~~**Follow-up (not blocking):** performance impact (fps/latency) not independently re-measured~~
      **DONE 2026-09-14** (`f22_perf_ab.py`): **20.1 µs/frame** added cost (p95 20.2, max 59.8) =
      0.060 % of a 33.3 ms frame; end-to-end replay 37.10 → 36.91 fps, reported with the caveat that
      RTMW3D dominates that second measurement and it does not carry the claim.

---

## ✅ F-20B — Sidecar Supervisor / Watchdog (2026-09-14) — PASS

See [`F20B_SIDECAR_SUPERVISOR_WATCHDOG_2026-09-14.md`](F20B_SIDECAR_SUPERVISOR_WATCHDOG_2026-09-14.md)
and ADR-051. Producer-side complement to F-20A: `python-sidecar~/sidecar_supervisor.py` restarts the
sidecar with backoff/crash-loop protection when it dies (crash, forced kill, camera unplug).
- [x] Supervisor state machine, readiness detection, backoff, crash-loop guard, single-instance lock,
      `supervisor_state.json` diagnostics — `sidecar_supervisor.py` + `run_supervisor.bat`.
- [x] Automated test harness, 6/6 PASS: normal start, forced-kill ×2 (real camera, new SID both
      times), duplicate-supervisor guard, crash-loop → `FAILED_PERMANENT` (throttled, never dies),
      static dependency-failure → clean terminal exit. `f20b_failure_tests.py`.
- [x] **Live session, 2026-09-14** — real USB unplug (sidecar HUNG, not crashed — caught by the
      stdout-heartbeat check, not process-exit detection), camera-absent-at-startup (recovered 36s,
      never hit crash-loop), and a Unity restart while the supervisor/sidecar stayed alive
      (`RestartCount` stayed 0). Avatar recovered live in every case, no Unity restart, no scene
      reload. `python-sidecar~/f20b_usb_test.py --mode live` / `--mode absent`.
- [ ] **Follow-up (not blocking):** consider lowering `--failed-permanent-retry` (60s default) — it is
      now the dominant term in worst-case recovery time once a crash loop trips (report §13).

---

## ✅ Full-body stability program — P0 → P1-3 (2026-09-07 → 2026-09-08) — ALL COMPLETE

Driven by [`AUDIT_FBT_2026-09-07.md`](AUDIT_FBT_2026-09-07.md). ADRs **028–031**. Every stage was
accepted on **measured live evidence**, never on code review alone.

### P0 — confidence-gated stability (ADR-028) — COMPLETE
- [x] **P0-1 `LimbGate`** — per-limb VALID/HELD gate at application time; holds last valid rotation, never substitutes zero. `Runtime/Retargeting/LimbGate.cs`.
- [x] **P0-2** — knees/ankles into the depth-smoothing + hold set; `--arm-max-jump` / `--leg-max-jump` = **0.35 m** for wrist/elbow/knee/ankle (slewed, not dropped).
- [x] **Diagnostic instrumentation** — `recv_log` gained knees/ankles + per-limb confidence + epoch clocks; `model_log` gained gate state, leg bones and **bone lengths**; sidecar gained per-stage timings. All marked `DIAG-ONLY`, additive only.
- [x] **Scripted-occlusion proof** — `inject_occlusion.py` + `verify_gate.py`: **20/20**, 0 zero-rotations across 2765 held frames.
- [x] **Live human acceptance** — gate fired **14×** across all 4 limbs; **0 origin collapse** (250 held rotations); re-acquire jumps **max 15.7°**; wrist/elbow peaks **−67…−76%**; trunk **improved 33–36%**.
- [x] **Squash question settled by measurement** — femur/shin/upArm/foreArm spread **0.000000 m** over 102 968 frames. Correct term is **LIMB ROTATION INSTABILITY**.
- [x] `limbConfidenceThreshold` serialized into `Scenes/Bootstrap.unity` (was relying on the C# initializer).

### P1-1 — per-joint temporal tracking + plausibility (ADR-029) — COMPLETE
- [x] `python-sidecar~/joint_tracker.py` — one reusable `JointTracker` per joint; TRACKED/WEAK/PREDICTED/LOST.
- [x] **FROZEN detector** — the check that actually catches the observed failure (a stuck joint has near-zero residual, speed *and* acceleration).
- [x] 37/37 unit assertions (`test_joint_tracker.py`); **6/6 adversarial cases detected** (`evaluate_p1.py`).
- [x] Cost **0.085 ms** median for 12 joints — 8× under the 1 ms budget.
- [x] Sidecar integration behind `--tracker` / `--no-tracker`.

### P1-2 — frame freshness / throughput (ADR-030) — COMPLETE
- [x] Root cause proven arithmetically: FIFO `get()` + `maxSize=4` at 30 fps = **133 ms** staleness (measured 131.4 ms).
- [x] Latest-frame drain + **timestamp-matched depth** selection.
- [x] Permanent diagnostics: `frameAgeMs`, `queueDepth`, `staleDropped`, `rgbDepthSyncMs`.
- [x] **Live human A/B** — frame age **131.51 → 31.36 ms**; camera→UDP **161.81 → 62.03 ms**; fps and compute unchanged.
- [x] RGB/depth pairing **improved** (audit F-09): max sync error 54.63 → 21.24 ms.
- [x] 180 s soak — **no freshness accumulation**.
- [x] Compute spikes root-caused to **ONNX/DirectML warm-up at frame 0–2**, not queue drain or GC.

### P1-3 — Unity timestamped pose buffer + interpolation (ADR-031) — COMPLETE
- [x] `Runtime/Core/PoseBuffer.cs` — bounded ring of 16; render at `now − poseInterpolationDelayMs`.
- [x] Duplicate / out-of-order / **backwards-timestamp** rejection; **no extrapolation**.
- [x] **Safety rule:** never position-interpolate across an invalid endpoint (would recreate the F-01 origin collapse). Explicitly tested.
- [x] 14 new EditMode tests → **47/47 total**.
- [x] Measured with identical deterministic input (`stream_motion.py`): stutter CoV **2.286 → 1.070 (−53%)**, frozen render frames **41.5% → 23.6%**.
- [x] Delay **40 ms** chosen on evidence (55 ms bought 3 more points for 15 ms more latency).

### Tooling added (reusable)
`inject_occlusion.py`, `verify_gate.py`, `analyze_capture.py`, `guided_capture.py`, `evaluate_p1.py`,
`compare_p12.py`, `stream_motion.py`, `run_p0_acceptance.bat`, `run_p12_ab.bat`.
Audit baseline logs preserved in `python-sidecar~/pipeline_logs_baseline_audit/`.

### Gotchas for the next agent
- **Do NOT run the unfiltered EditMode suite** — it aborts the Editor via a MediaPipe native
  `CHECK failed: 1 == ChannelSize()` (`image_frame.cc:362`) on the prebuild domain reload. Scope runs to
  the `VirtualMirror.Tests` assembly. Not a product defect.
- **`model_log` does not start until the VRM avatar binds (~7 s after Play).** Injecting data before
  that yields *no observations*, which is not a failure. **Absence of observation ≠ evidence of failure.**
- `--inject-load-ms` is a **test-only** instrument; never set it in production.

### Open / next (NOT started)
- [ ] **Recovery: short-gap prediction + blended reacquire** — P1-1's known weak spot (sustained high-confidence teleport → LOST → slow recovery during fast motion).
- [ ] **Palm / wrist rotation** — the L-palm rate limiter saturates (median = p95 = 14.98° vs a 15°/frame cap); hands also sit ~40 ms off the body timeline after P1-3.
- [ ] **Foot / ground constraint** — audit F-10, never implemented.
- [ ] **Confidence normalisation** — audit F-08; P1-1 works around it kinematically rather than fixing the scale.

---

## ⚠️ Full-codebase audit fix backlog (2026-08-07)

A 5-track read-only audit found the recurring bugs stem from **duplicated responsibility across the
Unity app and the Python sidecar** (mirror ×2, smoothing ×2, uprightness ×3, config ×2) plus a dead IK
path. Full report + `file:line` + fixes: [`AUDIT_2026-08-07.md`](AUDIT_2026-08-07.md). Verification
plan: [`AUDIT_TESTPLAN_2026-08-07.md`](AUDIT_TESTPLAN_2026-08-07.md). Tracked as session tasks
(H1–H7, M-CONSOLIDATE, M1/M4/M5/M7–M18, LOW-A..D, TEST).

Fix order: (1) single-owner consolidation of mirror/smoothing/uprightness → (2) H7+M12 finger twitch →
(3) H6 leg gate → (4) H3+M4+M8 hot-swap stability → (5) H1+H2 diagnostics/persistence →
(6) H4/M5/M9/M10 leak/race/teardown → (7) LOW batches via the consolidation refactors.

**Progress:** ✅ **ALL AUDIT CODE FIXES COMPLETE (2026-08-07 session 2, Unity MCP on port 6400).** Every
H/M/LOW item is implemented + **compile-verified 0 errors/0 warnings + EditMode 21/21 + sidecar
`py_compile` clean + M16 bench**. Session-1 wrote the Unity C# blind (no MCP); session-2 fixed the
`SharedUp` compile blocker, then finished the REMAINING items (H2 config single-source via dead-schema
removal, H5 IK toggle, M1 lazy face/hand toggles, M10 filter-dt gate, M16 zrel fallback, and the
LOW-A/C/D remainders), re-verifying compile + tests after each batch. Mirror single-owner is
resolved-by-design (Unity owns; sidecar `--mirror` off). **Only PLAY verification (OAK-D + eyes on the
avatar) remains — the user's, per `AUDIT_TESTPLAN_2026-08-07.md` §B PLAY + §C.**

**Post-audit test-video fixes (2026-08-07):** user PLAY test surfaced 3 issues, all fixed + logged as
ADRs so they don't recur — **waist twist** (spine yaw now from the hip line, ADR-019), **jitter**
(sidecar smoothing strengthened, single-owner kept, ADR-020), **wrist** (re-enabled on OAK with
smoothing + live weight, ADR-021). Round 2: **wrist roll-free** (ADR-021 amend), **beta up for
responsiveness** (ADR-020 amend), back-facing twist = single-camera limit (ADR-019). Compile 0/0 +
EditMode 21/21 + sidecar py_compile clean.

**Kalidokit port — the 70%→95% path (2026-08-07, ADR-022):** the persistent forearm spin is vector-FK's
guessed limb ROLL (not the hand, not the engine). Decision (user-chosen): stay on Unity, replace the
retarget math with a C# port of **Kalidokit (MIT)** which derives roll from limb geometry. **WHOLE BODY ported** (arms + hips/spine +
legs) and applied via UniVRM's **normalized control rig** so ONE global convention works for every bone
(no per-bone guessing). Behind **`useKalidokitBody`** (set before Play; FK/IK/hand-curl bypassed when on).
Compiles 0/0, EditMode 27/27 (6 KMath tests). ⏳ USER live check: set `useKalidokitBody` before Play →
confirm the avatar moves (if T-pose, it's UniVRM Process ordering, fixable) → tune the one convention
(`kalidokitBodyFlipQuat` 0..3 + `kalidokitBodyEulerSigns`, Inspector-live). Next: HandSolver → control-rig
fingers. See ADR-022 + `ai_handoff.md`.

---

## Character rig template + spec + validator (2026-08-10) — DELIVERED

Reverse-engineered the VRM 1.0 rig contract from the pipeline so artists build avatars that track with zero
per-avatar tuning. See [`ai_handoff.md`](ai_handoff.md) "🎭 Canonical VRM 1.0 rig template".

- [x] **Spec doc** [`docs/27_CharacterRigSpec.md`](27_CharacterRigSpec.md) — VRM 1.0 + T-pose + facing rules,
  bone tables (17 core/limb + 30 fingers + 7 optional), required expressions, Blender workflow, validation,
  limits. Indexed in `README.md` (rows 26+27).
- [x] **Blender generator** `tools/blender/build_rig.py` — headless; builds 54-bone T-pose skeleton +
  placeholder skinned mesh + expression shape-key slots + humanoid mapping + expression binds → exports
  VRM 1.0 to `tools/blender/output/`. Installed VRM Add-on for Blender v4.5.0 (MIT) headless into Blender 4.5
  (release CDN TLS-blocked → installed from `git clone` of the source).
- [x] **Import validator** `Editor/VrmRigValidator.cs` (+ `VirtualMirror.Editor.asmdef`) — Menu
  "Virtual Mirror → Validate VRM Rig"; parses the `.vrm` glb JSON (VRMC_vrm) → PASS/FAIL + missing
  bones/expressions. ⏳ needs a Unity compile-check (MCP unbound this session).
- [x] **Verified** generated VRM by glb-JSON parse: spec 1.0, 54 bones (0 missing, all 30 fingers, all
  optionals), 6 required expressions bound. Copied placeholder to `StreamingAssets/Avatars/VirtualMirrorRigTemplate.vrm`.
- [ ] **In-app load-test** — pending Unity MCP (restart to bind) OR manual Play → Tab → pick the template.

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
- [x] **Fix z-depth decode (highest impact)** — ✅ DONE 2026-08-06: got the exact mmpose `SimCC3DLabel` decode (see ADR-015). `DecodeFrame` now decodes z as ROOT-RELATIVE METRIC depth `z_metric = (zIndex/(ZBins/2) − 1) × ZRange` (ZRange=2.1744869), centred at 0 — not a pixel. z magnitude routed via a separate **`sentisDepthScale`** (default 0.4), independent of `sentisMetreScale`.
- [x] **Reduce smoothing for the 3D path** — ✅ DONE: when the body provider is `SentisPoseProvider`, `AppBootstrap` applies a looser One-Euro profile (`sentisFilterMinCutoff` 1.5 / `sentisFilterBeta` 0.6) so fast dance isn't damped. Still slider-tunable live.
- [x] **Person crop to 3:4** — ✅ DONE: `SentisPoseProvider.BuildInput` centre-crops the largest 3:4 region (UV scale/offset on the blit) when `sentisPersonCrop` (default true), so the top-down model gets a tight, undistorted person box instead of a stretched 16:9 frame.
- [~] **Per-axis sign + scale tune** — knobs in place (`poseFlipX/Y/Z` + `sentisMetreScale` + `sentisDepthScale`). Run-7: default `sentisDepthScale` raised **0.4 → 0.8** to fix "elbow doesn't bend" (depth-compressed elbow collapses onto the shoulder→wrist line → rigid straight arm; confirmed via dancer-vs-avatar zoom it's the pipeline, not the video). `sentisMetreScale`/`sentisDepthScale` are now **live-tunable during Play** (`SentisPoseProvider.SetTuning` pushed from `AppBootstrap`). Still needs a live tuning pass (elbow depth 0.8→1.0+ if shallow; mirror via `poseFlipX/Z`).
- [x] **HUD label from active provider** — ✅ DONE: `AppBootstrap.DescribeActiveTracking()` shows "Sentis RTMW3D (GPU)" / "MediaPipe Pose (CPU)" / "Fake Tracking" from the actual `bodyProvider` type.
- [x] **Person-box tracking (Run-8, top-down bbox stage)** — ✅ webcam tracked poorly (full-body model + upper-body framing + arm-clipping centre-crop → gated to rest). Added keypoint-driven person-box tracker in `SentisPoseProvider` (`ComputeCropRect`/`UpdatePersonBox`): crop follows the person from the previous frame's keypoints (+margin 0.35, smoothed), adapts to any framing (upper/full body), no extra detector NN. Compiles clean. ⏳ needs live webcam validation; tune `sentisMetreScale` DOWN (person fills frame now), flip `poseFlipZ` if limbs bend behind. A detector NN is the fallback only for multi-person / hard re-acquisition.
- [x] **`sentisDepthScale` default 0.4→0.8 + live-tunable (Run-7)** — elbow-bend fix; `sentisMetreScale`/`sentisDepthScale` pushed live from `AppBootstrap` (Inspector-tunable during Play).
- [x] **Waist-bend rigid tilt → FIXED (Run-9) + ✅ USER-CONFIRMED WORKING** — Hips + Spine shared `up=midShoulder−midHip` → forward bend tilted both together. `BasisBone.StableUp`: Hips now use vertical up (yaw/side-lean only, stay upright), Spine keeps torso-up (carries bend). `HumanoidPoseRetargeter.cs`. Applies to the FK torso path (both MediaPipe & Sentis).
- [~] **Hands behind body (Sentis/MediaPipe RGB)** — **CLOSED as won't-fix-on-RGB (2026-08-07):** this is the inherent monocular front/back depth ambiguity of a single camera; no RGB-only tweak resolves it (toggling `poseFlipZ` doesn't — it's not a constant sign error), only palliatives (`sentisDepthScale`↓, temporal z-smoothing/outlier clamp). The real fix is the depth camera — that is exactly what the OAK-D B2 path (ADR-016) + Phase-2 measured depth ([`docs/26_OakDDepthPhase2.md`](26_OakDDepthPhase2.md)) address. Superseded by the OAK-D task; do not spend more effort on the RGB paths here.
- [ ] **Close-framing weakness** — top-down model + hip-centring need the hips in frame; guide users to half/full-body framing. Person-box can't recover an out-of-frame pelvis.
- [ ] **⏳ LIVE-VERIFY (blocked on user real-time Play):** the Run-5 fixes compile clean (0 errors) but **motion can't be confirmed headless** (VideoPlayer doesn't advance under `EditorApplication.Step`; real-time freezes when Unity unfocused). Needs a **focused real-time Play** (Sentis on, dance video or webcam): confirm bigger arm/leg reach + depth. Then live-tune `sentisDepthScale` (raise if depth still muted), `sentisMetreScale` (overall amplitude), `poseFlipX/Y/Z` (mirror/handedness), and the smoothing sliders.
- [~] **Live-validate + tune** — ✅ **decode + live pipeline VALIDATED** (2026-08-06, video source): on a real frame both norm modes give anatomically-correct keypoints (ImageNet norm higher-conf → kept); live in-pipeline `SentisPoseProvider` emits valid hip-centred 3D (hips@origin, nose y+0.47, ankles y−0.50, Y-up, L/R x-split, conf 0.80–0.94) and the avatar is driven (posed, not T-pose), 0 errors. ⏳ **Still TODO:** frame-to-frame **motion** couldn't be confirmed headless (VideoPlayer doesn't advance under `EditorApplication.Step`; real-time run freezes when Unity unfocused) → needs a **real-time focused Play** by the user. Then tune: z-depth `sentisMetreScale` (~1.0 m span now, a bit short); mirror/axis via `poseFlipX/Y/Z` for the avatar convention; root/hip grounding (stop float); velocity outlier-reject (leg jitter). Fix stale HUD label ("MediaPipe Pose (CPU)" shown while Sentis is active).
Stack chosen: **Unity Sentis** `com.unity.ai.inference` 2.6.1 (INSTALLED ✅, 0 errors), model **RTMW3D** (RTMPose3D family, whole-body 3D incl. hands, Apache-2.0). POC → non-commercial licenses OK.
- [x] **Install Sentis** (`com.unity.ai.inference` 2.6.1) — namespace/assembly `Unity.InferenceEngine`. Build clean.
- [~] **RTMW3D feasibility spike (NEXT — resume here):** ONNX downloaded + verified (`Soykaf/RTMW3D-x`, 369 MB, 384×288). **Import into Sentis** (regular `Assets/` folder) and read the import log for unsupported operators. Clean → Sentis; rejected → **ONNX Runtime (DirectML)**. See ADR-015 for URL + decode.
- [ ] **Build `SentisPoseProvider : IBodyTrackingProvider`** — camera→`TextureConverter.ToTensor`(384×288)→`Worker.Schedule`(GPUCompute)→readback→**SimCC decode**→133 keypoints→our 33 `JointId` (Unity hips-centred m). Tracking asmdef + `Unity.InferenceEngine` ref. Wire behind `AppBootstrap.useSentis3dTracking` (default off) + MediaPipe fallback. Live-validate/tune (like `poseFlipX`).
- [ ] **Bonus from RTMW3D:** whole-body includes **hands + face** → could replace the separate MediaPipe hand/face providers and fix dead-hands-at-distance.
- [ ] **Also:** root/hip grounding (stop the avatar floating), velocity outlier-reject (leg jitter seen while standing).
- [ ] **Direct rotational retarget from 3D** — feed true 3D limb rotations, dropping most IK reconstruction (fixes head pitch, wrist, depth at the source, not as symptoms).
- [ ] **Per-joint finger + wrist from 3D hand** — replace the single mid-joint curl heuristic (SDS-011 curl) with per-joint angles + real wrist pose; supersedes the ADR-013 delta-from-neutral wrist hack.
- [~] **⭐ OAK-D depth-camera — B1 (in-Unity plugin) ABANDONED (too crash-prone), pivoted to B2 (Python sidecar over UDP) — BUILT, awaiting user test (ADR-016).** B2: `oak_sidecar/venv` (depthai 2.32) + `depthai_blazepose` + `udp_pose_sender.py` (BlazePose on OAK → 33 landmarks over UDP:8899); Unity `OakDUdpPoseProvider` (no native DLL → can't crash Unity) behind `AppBootstrap.useOakUdpTracking`. Phase-1 = GHUM depth (≈MediaPipe, stable); Phase-2 = per-keypoint depth-map sampling (the real front/back fix, hardware-in-the-loop). **✅ 2026-08-07: B2 COMPILE-VERIFIED in Unity 6.3 (0 CS errors) + reviewed AGENTS.md-clean; B1 dead code REMOVED** (deleted `OakDPoseProvider.cs` + all `AppBootstrap` B1 wiring/fields; 81 MB native plugins already gone). **Also 2026-08-07: B2 provider hardened** (lock-safe `TryGetLatestFrame`, `ReceivedCount`/`ParseErrorCount` + periodic `rx=…/parseErr=…` console line for live bring-up) and **Phase-2 fully spec'd → [`docs/26_OakDDepthPhase2.md`](26_OakDDepthPhase2.md)**. **✅ 2026-08-07: PHASE-1 PROVEN LIVE ON HARDWARE (OAK-D attached).** Repaired the broken sidecar venv (missing `Scripts/` → recreated in place with Python 3.10, site-packages intact); device `14442C10F143D3D200` detected; sidecar streams BlazePose over UDP (default `--frame_height 200`; 400 errors on this OV9782 — comment fixed); Unity `useOakUdpTracking=true` saved, Play-verified `OAK-D UDP pose provider listening…` + `rx=1/2/3 parseErr=0` (datagrams received + parsed clean) → full chain OAK→sidecar→UDP→provider→PoseFrame confirmed, 0 errors, HUD "OAK-D 3D (UDP sidecar)". **✅ 2026-08-07 ITER-2 (device attached, many user iterations):** FOV crop FIXED (device = OAK-D-PRO-W / OV9782 1280×800; geaxgx assumed 1920×1080 → invalid ISP scale → centre-crop; sidecar now uses native `--color_res 800p --color_scale 1/2` → 640×400 FULL FOV). Sidecar `--show` cv2 preview added. Retarget overhaul (compiles clean): legs held straight when occluded (`SetTrackLegs`, `trackLegs=false`), hips upright-only (no tilt / no C needed), **rotational FK retarget now default** (`useIkDriver=false`, = webcam-tool/Kalidokit approach), **live mirror toggle** (`PoseSpaceConverter.SetFlipX/Y/Z`), and **position tracking** (avatar walks/jumps with user — `PoseFrame.RootPosition` from the OAK's measured hip `xyz` → avatar root, `trackPosition`/`positionScale`/`positionSmoothing`). Scene defaults saved (`useIkDriver=false, poseFlipX=false, poseFlipZ=false, trackLegs=false, trackPosition=true`). ⏳ NONE user-confirmed yet — needs a focused real-time Play. KNOWN LIMITS: face+fingers need a real USB webcam (Quest 3 virtual cam fails `Could not connect pins`); legs occluded by desk; `xyz` can spike (add outlier reject); `--lm heavy`≈7fps vs `full`≈18fps. Phase-2 (measured per-keypoint depth, doc 26) still open. **✅ ITER-3 (2026-08-07): position vertical inversion fixed (`ToUnityPosition`, OAK xyz is Y-UP); front-facing auto-calibration (no C key); `trackLegs=true` → USER-CONFIRMED "legs are fine now". Retarget-architecture decision recorded in ADR-017 (rotational FK default over IK).** Next: verify jump-up/walk-position live + tune `positionScale`; confirm mirror-X via live checkbox; get a USB webcam for face/fingers; then Phase-2 depth. Old details below (B1, now removed). ✅ 2026-08-06: `luxonis/depthai-unity` minimal Windows subset imported (compiles on Unity 6.3, 0 errors); `OakDPoseProvider` written (native `[DllImport]`, ABI-verbatim structs, MoveNet-17 real-3D → `PoseFrame`), wired OAK-first in `AppBootstrap` (`useOakDTracking=true` saved) + HUD label. ⏳ **NEXT: user live-test with OAK-D plugged in** (Play → HUD "OAK-D 3D (on-device)"); then tune coordinate signs + offload the native call off the main thread if it stalls FPS. Details below + ADR-016. On-device BlazePose (33 landmarks = our `JointId` topology) + stereo depth → metric 3D from the camera; removes the "hands behind" front/back ambiguity + scale guesswork; drops ~1:1 into `IBodyTrackingProvider`. **Integration fork pending:** B1 `luxonis/depthai-unity` native plugin (all-Unity, precompiled, has a Pose pipeline; risk = Unity-6.3 compat of a 2024-era plugin) vs B2 `geaxgx/depthai_blazepose` Python sidecar + UDP/socket to Unity (robust, decoupled; +Python process). Plan: verify B1 import first, else B2. New `Runtime/Tracking/OakD/OakDPoseProvider.cs` + `AppBootstrap.useOakDTracking` + fallback. NEEDS hardware plugged in + SDK/plugin install + user live-testing (can't validate device-less).

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

---

## 2026-09-15 (evening) — F-21 instrument rebuild + F-23 video-driven validation

### DONE
- [x] F-21 live instrument rebuilt; **10 defects fixed** before spending human time (F-21 report §32)
- [x] Cue legibility measured and fixed: 13 px / 4.1–6.2 arcmin → 60 px / 20.5–25.7 arcmin (ADR-058)
- [x] `f21_walkin_protocol.py` + `f21_walkin_score.py` + test (**22/22**); adds the two cases that
      are the REAL false-hold rate (`OWNER_OCCLUDED`, `OWNER_TURN`)
- [x] `cue.json` write race fixed in BOTH protocols — it was killing runs mid-session
- [x] F-23: avatar driven from real video through the production chain, **r = +0.714**
- [x] Pelvic roll re-enabled (ADR-059) — 25.1° p2p was being discarded by a config value
- [x] Slerp A/B measured — **negative result**, damping is not the lever (ADR-060)
- [x] Wire probe + trunk-jitter harness built and self-tested

### BLOCKING — needs people
- [ ] **F-21 LIVE TWO-PERSON SESSION.** Instrument ready, never run. Needs TWO people, ~30 min.
      Clear chair-like objects from the capture volume first (§32.4). Tape a cross at 0.90 m.
- [ ] **Trunk-yaw noise floor.** ~30 s of ONE person standing still at 0.90 m. Last thing gating
      the deadzone decision. The 2026-09-15 attempt is INVALID — no subject was in frame.
- [ ] F-22 L2 hands-near-face live.

### BLOCKING — needs the editor closed
- [ ] Re-run Unity EditMode **170/170** after `kalidokitBodyTorsoRoll: 0 → 1`. The VALUE is now
      validated on live data (F-27 section 8.5: trunk error more than halves), but the EditMode suite
      has still not been re-run against it.

### DECISION, not research
- [ ] Sub-pixel 1/8: torso-yaw quantum **6.80° → 0.85°** at 0.90 m, +2 ms, no FPS cost. This is the
      lever that would let the 8° deadzone come down honestly.

---

## 2026-09-16 — F-26 avatar-vs-skeleton report + the POSE FIDELITY metric

### DONE
- [x] F-26 observation report from `LatestScreenReording.mp4` — full avatar/skeleton comparison,
      failure mechanisms, what prior data does and does not support
      (`docs/F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md`)
- [x] **POSE FIDELITY metric built** — the measurement that had never existed. `BoneFidelity`,
      `KalidokitControlRigDriver.SampleFidelity()` (9 bones, read-only, allocation-free),
      `AvatarFidelityRecorder` (samples on `Application.onBeforeRender`, self-terminating),
      `f26_fidelity_analyze.py` (**22/22 self-test**, refuses to emit a combined score)
- [x] **Validated against live data**, two clips through the production wire (ADR-062, F-26 §11).
      Arms follow to 0.4–1.1° median; legs OVER-DRIVEN 1.4–2.3×; trunk median **16.7°**, follow 0.27
- [x] Cross-checked in world space against `PoseDebugSkeleton` — the aimed arms cannot validate the
      metric's coordinate mapping, so an independent path did. Two paths agree on the trunk to 0.4°
- [x] Trunk failure LOCATED, not just measured: lateral lean is `* torsoRoll` with
      `kalidokitBodyTorsoRoll = 0`; sagittal lean is high-passed by an 8 s adaptive baseline. The
      second is the mechanism behind "the avatar cannot sit" (F-26 §4.2)
- [x] Instrument caught a false finding on itself: forearm maxima of 121.6°/155.4° were entirely the
      release-to-rest tail. Stale-frame drop rule added, with tests

### NEXT — decision, not research
- [ ] **Choose a retarget direction** from F-26 §8 / §11.8. The metric is the before-number; option C
      ("fix the specific failures") now has two named, located causes rather than symptoms.
      **Nothing should change in the retarget until the direction is chosen** — that is what spending
      the measurement on an unvalidated fix would waste.
- [ ] Decide whether SEATED use is in product scope (F-26 §9.4). If it is, the trunk high-pass is the
      top defect. If not, it is not a defect at all. Nobody has stated which.

### NEXT — cheap, and unblocks a real claim
- [ ] Run the fidelity metric on an **OAK-D stereo session**. Both runs so far used the video path,
      which has no stereo and synthesises every joint's depth — it exercises the RETARGET, not the
      sensor (F-26 §11.7.3).
- [ ] **ENDPOINT error metric.** The direction metric is blind to it by construction: two poses can
      agree on every bone direction and still put the hands elsewhere, because the avatar's bone
      lengths are its own. Option D (bone-length normalisation) cannot be judged without it.

### STILL BLOCKING — unchanged, needs people
- [ ] F-21 live two-person session; trunk-yaw noise floor; F-22 L2 hands-near-face live.
- [ ] Re-run Unity EditMode **170/170** with the editor closed.

---

## 2026-09-16 (later) — F-27 humanized skeleton + F-28 skeleton-as-the-product

### DONE — F-27 HUMANIZED SKELETON (ADR-063)
- [x] New layer between the existing filtering and the existing retarget. Tracking output is treated
      as a SUGGESTION: fixed bone lengths, anatomical joint limits, teleport and origin-collapse
      rejection, per-joint hold in the PARENT's frame, 0.15 s re-acquire blend
- [x] **Nothing existing was rewritten** — tracking, filtering, P0-1, P1-1/2/3, Arm V2 and Torso
      V5/V6 are untouched, and confidence passes through so P0-1 still gates exactly as before
- [x] Test mode: `HumanizedSkeletonSelfTest` with synthetic bodies + injected faults (dropped joint,
      confident collapse to origin, NaN, teleport, flicker, impossible elbow fold, knee bending
      forwards, 150 deg torso twist). **32/32**, headless, no Editor or avatar needed
- [x] Before/after recorded from the SAME frames in ONE pass; analyser **18/18** self-test
- [x] **Two defects found by the numbers after the suite was green**: the body-forward axis was
      ambiguous (knee fix fired on 84 % of poses -> 1.8 %), and the velocity clamp was measuring its
      own output (80 % -> 0.4 %). Median jump went from +269 % WORSE to -3 %
- [x] Measured: bone-length deviation **-68 % median / -79 % worst**; **99.6 % of poses touched by no
      stateful stage**, so no added latency

### DONE — F-28 SKELETON SHOW
- [x] `Scenes/SkeletonShow.unity` — three live-switchable modes (1/2/3, TAB, on-screen buttons),
      driven by the SAME production tracking and the F-27 layer, retarget skipped entirely
- [x] Mode 1 glow skeleton, mode 2 energy body (no mesh, no skinning), mode 3 motion effects
- [x] `H` toggles the humanized layer live — the fastest A/B of F-27 there is
- [x] 114-118 fps in the editor; mode switching verified to leave exactly one mode root

### NEXT — cheap and it closes a real question
- [x] **DONE — F-26's fidelity metric run with the layer ON, OFF, and ON-with-bone-lengths-OFF**
      (F-27 section 8, ADR-063 addendum). 4000+ live frames per arm, reference = the RAW pose in every
      arm. Result: the protections cost the avatar NOTHING (9/9 unchanged with bone lengths off); the
      whole ~1 deg arm cost is bone-length normalisation; F-27 does NOT fix the leg over-drive.
      On CLEAN tracking the layer is neutral-to-slightly-negative for avatar fidelity.
- [x] **DONE — `kalidokitBodyTorsoRoll: 0 -> 1` validated**, incidentally: F-26 section 11.5's
      "the trunk lean channel is dead" was measured against a RUNTIME 0 while the scene ships 1.
      Re-measured: trunk median 16.7 -> 7.3 deg, follow 0.27 -> 1.29. F-26 section 11.5 is corrected
      in place.
- [ ] **Run F-27 on an OAK-D stereo session.** Both recordings used the video path, which has no
      stereo — so the rejection, hold and recovery stages had NOTHING to do (0 holds, 0 NaN, 0
      collapses in either stream). They are proven by fault injection, not by live data.
- [ ] Attribute the **p99 jump regression (+19 %)** to a specific bone before deciding if it matters.

### NEXT — product direction, not research
- [ ] **Decide between the avatar and the skeleton.** F-28 exists because F-26 located every avatar
      failure in the retarget; mode 2 sidesteps that entire class of error by building the visual
      from joint positions. That is a product choice, not an engineering one.
- [ ] Show F-28 to somebody. Whether it reads as intentional rather than as a debug view is a
      judgement about an audience, and no audience has seen it.

---

## v1 PACKAGING — sidecar auto-launch and build packaging (2026-09-16, ADR-064)

### DONE
- [x] **`SidecarProcessLauncher`** spawns `sidecar_supervisor.py` from `AppBootstrap.StartTracking()`.
      Output goes to `ILogService`; teardown is first in `TeardownServices()` (already idempotent and
      already wired to both `OnApplicationQuit` and `OnDestroy`, so domain reload is covered).
- [x] **Orphan prevention via Win32 job object** (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`), so the
      kernel kills the tree even when Unity crashes and no managed teardown runs. Measured: the live
      tree is supervisor -> sender -> a third process, so `taskkill /F /T` is load-bearing.
- [x] **Attach, never double-spawn.** Launcher probes lock port 8897 before spawning; held means an
      existing supervisor, so it reports `AttachedExternal` and leaves it alone.
- [x] **`--allow-port-listener` passed unconditionally** — Unity in Play mode holds 8899 and the
      supervisor's probe would otherwise read that healthy listener as a stale producer.
- [x] **Serialized `autoStartSidecar`** on `AppBootstrap` (default on), plus lock port, model
      override, portrait direction and subpixel bits.
- [x] **Build packaging decided and implemented** — `.py` source into `StreamingAssets/Sidecar/` via
      a post-build step; `.venv` (342 MB, NOT relocatable), the 369 MB model and the superseded
      86 MB `depthai_blazepose/` all excluded. Trade-off recorded in ADR-064 and README.md.
- [x] **`setup_sidecar.ps1`** — one-time target setup that verifies `DmlExecutionProvider` and fails
      at install time rather than letting the 5 fps CPU fallback reach production.
- [x] **`requirements.lock.txt`** — exact `pip freeze`. `requirements.txt` had
      `onnxruntime-directml` COMMENTED OUT while the working venv had it installed, so anyone
      setting up from it got a sidecar with no inference at all.
- [x] **Product `README.md`** at the repo root. The previous root copy was a byte-identical
      duplicate of `docs/README.md` whose links only resolve from `docs/`.
- [x] **11 EditMode tests** (`SidecarPathsTests`) covering editor/player path resolution and the
      hard-fail-on-missing-venv contract. 11/11 pass.

### OPEN — required before v1 can be called done
- [ ] **Press Play and demonstrate the cycle three times.** The mechanism is verified directly
      (exact CLI, exact `taskkill /F /T`, 3/3 cycles, no orphan) but NOT through Unity Play mode.
- [ ] **Produce and run a Windows build.** `SidecarBuildPostprocessor` has never executed.
- [ ] **Re-run the full EditMode suite (170 tests) in batch mode** with the editor closed. Still
      outstanding from the `kalidokitBodyTorsoRoll: 0 -> 1` change.
- [ ] **Surface `SidecarProcessLauncher.StatusLine` in `DiagnosticsHudPanel`.** The launcher exposes
      it; nothing displays it yet, so a sidecar failure is currently console-only.
- [ ] Confirm Build Settings scene list and order, and decide whether `SkeletonShow.unity` ships.
- [ ] Cleanup pass (docs/evidence blobs, one-off harnesses) — NOT started; needs the user's call on
      what to delete, since several "test scripts" are the only instrument protection this project
      has.

---

## v1 CLEANUP + SIDECAR REORGANISATION (2026-09-16, ADR-065)

### DONE — cleanup (~2.72 GB freed, nothing tracked was deleted)
- [x] **Citation chain committed FIRST, before any deletion.** `docs/evidence/` was entirely
      untracked, so the evidence behind ADR-062/063 and F-26/27/28 existed on one disk only.
      33 files / 3.5 MB now committed: 9 cited PNGs, RESULTS.txt, FIDELITY_AB.txt, the .gitignore
      rules. Verified 0 .jsonl staged.
- [x] `oak_v4_evidence/` 1,818 MB -> 26.9 MB. Deleted 1,976 untracked files (1,701 PNG, 20 MKV,
      8 MP4, 101 jsonl); kept all 118 tracked summary files. Overlap between the delete list and
      the tracked list was checked to be 0 before executing.
- [x] `pipeline_logs*/` (5 dirs, 759 MB), `__pycache__/`, `docs/evidence/**/*.jsonl` (39 MB),
      `LatestScreenReording.mp4` (130 MB) — all removed.
- [x] **Images KEPT, deliberately.** All 9 PNGs in `docs/evidence/` are cited by
      F26_AVATAR_VS_DEBUG_SKELETON or F28_SKELETON_SHOW_MODES; `t065_arms_overhead_avatar_horizontal.png`
      is the visual record of the failure ADR-063 reasons about. Deleting 3.4 MB would have broken
      the citation chain for no meaningful saving. The real image bulk was the 1,701 untracked
      frame captures inside `oak_v4_evidence`, which are gone.
- [x] **The repo was never bloated by any of this** — it was all untracked, and the f26/f27
      .gitignore rules already excluded the jsonl. This freed disk, not repository size.

### DONE — sidecar reorganisation (ADR-065)
- [x] Root reduced 83 -> 12 .py files: the two entry points, the nine modules
      `wholebody_udp_sender` imports, and the `_sidecar_path` shim. The nine were NOT moved, on
      purpose — relocating them means editing the shipping frame loop's imports.
- [x] 72 files moved into `tests/` (8) and `tools/` (capture 23, ownership 13, diagnostics 11,
      deployment 7, validation 6, video 4), grouped by dependency cluster.
- [x] 39 files given the `_sidecar_path` prelude. It walks UP to find the shim rather than counting
      dirname() calls — a fixed depth was right for tools/<group>/ and wrong for tests/.
- [x] **Dedup measured, not assumed:** 0 whole-file pairs >= 55% similar; exactly 1 duplicated
      function (13-line `load()`), hoisted to `f16_configs.load_rows()`. The similar-looking
      f21_/f22_ names are different subsystems and were correctly left alone.
- [x] `SidecarBuildPostprocessor` now ships 12 files instead of 83; AGENTS.md §9 records that the
      root is the production path and a file left there reaches every player.
- [x] Sidecar README `## Layout` rewritten — it still documented ~12 scripts deleted back in
      `29ec57e`, and the Tests/Device-free sections gave commands that no longer ran.

### Verified
```text
self-tests before move   75/75, 39/39, 37/37, 22/22, f26 28, f27 18
self-tests after  move   identical, + test_surface_depth 32/32
production chain         all 10 modules import clean; compileall exit 0
supervisor end-to-end    3/3 start/stop cycles, READY ~13 s, no orphan, ports released
duplicate scan after     0 pairs, 0 duplicated functions
```

### DONE — root tidy-up (follow-up to ADR-065)
- [x] The first pass sorted only `.py` and left **13 loose non-.py files** at the sidecar root, plus
      **9 .py scripts buried inside `arm_v*_evidence/`** that a root-only scan never saw.
      Root: 96 -> 18 files.
- [x] `scripts/` ← 4 `.bat` + `oak_guided_v4.ps1`. Every `.bat` did `cd /d "%~dp0"` to reach the
      repo root; from `scripts/` that is one level short, so all are re-based to `%~dp0..`.
      Verified the venv interpreter, the model, the supervisor and compare_logs all resolve.
- [x] `docs/` ← `p0_human_report.txt`;  `tools/armaim/` ← the 9 arm-aiming analysis scripts.
- [x] **Three broken launchers found.** `run_p0_acceptance.bat` and `run_p12_ab.bat` have been dead
      since `29ec57e` (they invoke deleted scripts); `run_capture.bat` was broken by the ADR-065
      move itself and is now repointed. The two dead ones are KEPT — P0_ACCEPTANCE and
      P1_2_FRESHNESS cite them by name — but carry a DOES NOT RUN banner and exit 1.
- [x] `oak_v4_evidence/` deliberately left at the root: script defaults hardcode that path.
- [x] Removed `pose_stage_marks.json` (orphan; one unfinished mark, `tEnd 0.0`).

---

## SDK DISTRIBUTION STANDARDS (2026-09-16)

- [x] **MIT `LICENSE`** in both repos. The sidecar README had promised "see repository license"
      while no LICENSE existed. MIT matches the dependency stack (UniVRM, MediaPipe Unity plugin,
      Kalidokit, depthai, vendored depthai_blazepose are all MIT).
- [x] **`package.json`** — UPM manifest, `cloud.viitor.virtual-mirror` `0.1.0`, Unity `6000.3`.
      `0.x` on purpose: the §7 items are still open and the API may move in a minor release.
- [x] **`CHANGELOG.md`** in both repos (Keep a Changelog / semver).
- [x] **`CONTRIBUTING.md`** — repo layout, the DirectML trap, test commands, the evidence rules.
- [x] **`.gitattributes`** in both repos — LF normalisation, CRLF for `.bat`/`.cmd`/`.ps1`,
      binary markers for `.onnx`/`.blob`/media, linguist hints for vendored and generated trees.
      Also ends the constant CRLF warning noise on every commit.
- [x] **`.editorconfig`** — matches the existing C# and Python style (110 cols, same-line braces).
- [x] **`pyproject.toml`** for the sidecar — PEP 621 metadata, console entry points,
      `requires-python = "==3.10.*"` (depthai is cp310-only), ruff/black config. `py-modules` is
      explicit so a harness under `tools/` can never accidentally ship as an importable module.
- [x] **CI** (`.github/workflows/ci.yml`) — validates package metadata and that CHANGELOG documents
      the shipped version; runs the sidecar self-tests on `windows-latest` + Python 3.10; asserts
      `DmlExecutionProvider` is present; guards against absolute machine paths returning.
- [x] **Issue templates** — the bug template requires the ONNX provider list, because "everything is
      slow" is nearly always the silent CPU fallback.
- [x] **`ThirdPartyNotices.txt` extended** with the whole Python stack, which was entirely absent:
      vendored depthai_blazepose (MIT, licence file verified present), RTMW3D/MMPose weights
      (Apache-2.0 code, weights carry their own terms — flagged), depthai, onnxruntime, OpenCV, NumPy.
- [x] **README** — UPM install section. Documents that `com.vrmc.vrm` needs the OpenUPM scoped
      registry and `com.github.homuler.mediapipe` is not on any registry, so neither resolves for a
      consumer automatically; and that UPM does not fetch the `python-sidecar~` submodule.

### Open before a public release
- [ ] Confirm the MIT copyright holder string. `LICENSE` currently reads "ViitorCloud" — inferred
      from the domain, not verified against the legal entity name.
- [ ] Verify the licence attached to the specific `rtmw3d-x.onnx` weights in use. MMPose's CODE is
      Apache-2.0, but released weights can carry dataset-derived restrictions, and the model is a
      hard runtime requirement.
- [ ] Decide whether `evidence/` (~27 MB of tracked measurement data) belongs in a distributed SDK.
      It is the citation chain for the ADRs, but it is development material, not product.
- [ ] Unity EditMode tests are not in CI — that needs a licensed editor (GameCI + secrets).
- [ ] Tag `v0.1.0` once the Play-mode and build acceptance items above are actually run.
