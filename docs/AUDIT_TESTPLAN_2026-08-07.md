# Audit Fix — Test Plan (2026-08-07)

Verification for the fixes tracked against [`AUDIT_2026-08-07.md`](AUDIT_2026-08-07.md). A fix is "done"
only when its row here passes. Grouped by how it is checked, because much can't be unit-tested (live
Unity + OAK-D). Columns: **How** = the check; **Pass** = the observable success criterion.

## Legend
- **BUILD** — Unity recompiles with 0 errors (editor) / `py_compile` clean (sidecar).
- **BENCH** — a device-free Python/edit-mode assertion.
- **PLAY** — focused Play with the OAK sidecar running; watch the avatar / HUD / logs.
- **CODE** — verify by reading the diff (no runtime path to exercise it cheaply).

## A. Gates (run first)
- **G1 BUILD (Unity):** editor recompiles 0 errors/0 warnings after all C# edits.
- **G2 BUILD (sidecar):** `.venv\Scripts\python -c "import py_compile,glob; [py_compile.compile(f,doraise=True) for f in glob.glob('*.py')]"` in `python-sidecar~/` → clean.
- **G3 BENCH:** existing EditMode tests still pass (PoseSpaceConverter, OneEuroFilter, RotationFromVectors, retargeter, perf) — plus new ones below.

## B. Per-fix verification

| Task | How | Pass criterion |
|---|---|---|
| H1 PerformanceMonitor | PLAY | Diagnostics HUD shows a live, changing FPS + frame-time (not blank). |
| H2 Config single-source | BENCH+PLAY | Change a calibration value, restart → value persists; no fps 60/30 mismatch; one source only (grep shows serialized fields hydrated from `settingsStore.Current` OR schema removed). |
| H3 Retarget rebind on swap | PLAY | Load avatar A, then hot-swap to B → B's face expressions + finger curls track (not frozen/erroring). |
| H4 Glog Release on failure | CODE+BENCH | Catch block calls `MediaPipeGlobalInit.Release`; forcing a bad model path twice does not inflate refcount (add a debug assert or log of refCount). |
| H5 IK toggle live | PLAY | Start FK; tick "IK Arm/Leg Driver" at runtime → arms/legs switch to IK (constraint weight >0); untick → back to FK. |
| H6 FK leg gate | PLAY | Sit / occlude lower body → legs rest straight (not folded/splayed); stand full-body → legs track. |
| H7 Hand confidence gate | BENCH+PLAY | Sidecar bench: with hands hidden, `lh`/`rh` omitted or low-conf → Unity `ReadHand` null; hands hidden → avatar fingers stop twitching. |
| M-CONSOLIDATE mirror/smooth/upright | CODE+PLAY | Exactly one owner each: grep shows Unity `poseFlipX` forced off when OAK active; `jointFilter` bypassed/loosened for OAK; front-facing gate no longer reads shoulder Z. PLAY: no double-flip, no double-lag, upright torso. |
| M1 Lazy toggles | PLAY | Start with face OFF, tick it at runtime → face begins tracking. |
| M4 Session dispose | PLAY | Reload Mirror scene twice → no growing avatar/session count (Profiler/hierarchy); no duplicate AvatarChanged fires. |
| M5 OnDestroy teardown | PLAY | Edit a script mid-Play (domain reload) → next Play logs "OAK-D UDP listening" (no "port 8899 in use"). |
| M7 Webcam open state | PLAY | Point at a busy/denied device → `StartCapture` returns false + a clear "camera failed" log (not silent success). |
| M8 Load race | PLAY | Rapidly click 5 avatars → the LAST clicked wins; no leaked instances (hierarchy has exactly one avatar). |
| M9 OAK buffer race | CODE | Buffer write-target captured under `lock(gate)` (or fields volatile); matches MediaPipe pattern. |
| M10 One-Euro dt | BENCH+PLAY | Filter runs once per NEW frame using timestamp delta (add a counter: filter-calls ≈ datagrams, not ≈ render frames). |
| M11 Hip fallback | BENCH | Feed a frame with one hip depth-hole → sidecar still sends (uses single hip / last good), not skipped. |
| M12 Finger mapping | BENCH+PLAY | On an avatar missing an intermediate finger bone, each finger curls its OWN value (unit test binds a stub skeleton). |
| M13 Roll reference | BENCH | RotationFromVectors unit: rest-dir near referenceUp + target-dir not → no spurious roll (assert delta ≈ expected, no twist). |
| M14 MediaPipe wrist | CODE+PLAY | On MediaPipe-hand fallback, wrist does not spin (tracked=false or weight 0, or neutral gated). |
| M15 Person-box recovery | BENCH+PLAY | Leave frame / occlude → box re-acquires within N frames when you return (log the reset). |
| M16 zrel fallback | BENCH | Force a depth hole on a limb → its Z uses scaled `zrel` (non-zero), not flat 0. |
| M17 Docstring/warn | CODE+PLAY | Docstring names `wholebody_udp_sender.py`; running the OLD blazepose sender logs a one-time "no lh/rh — hands disabled" warning. |
| M18 OAK filter params | PLAY | OAK path uses its own filter params (or the single-owner decision); no residual over-/under-damping. |
| LOW-A..D | CODE (+PLAY where visible) | Each enumerated sub-item verified by diff; visible ones (mirror-flip on MediaPipe hands, camera dropdown hidden in video mode, drag-end, thumb axis) spot-checked in Play. |

## C. Symptom regression check (the recordings)
Record a ~30 s clip after the fixes and confirm, standing ~2 m, hips in frame:
1. **Upright, untwisted** torso when standing (no waist twist/lean).
2. **Stable** when still (no jitter beyond ~1 cm), responsive when moving (no freeze).
3. **Legs** track when visible, rest straight when occluded (no fold/splay).
4. **Fingers** still when hands hidden; curl when visible.
5. **Arms** follow open/close/cross without inversion.
6. HUD shows live FPS + correct provider label.

## Results — session 2 (2026-08-07, Unity MCP on port 6400)

**Automated gates — ALL PASS:**
- **G1 BUILD (Unity):** forced full recompile → **0 errors / 0 warnings** (after fixing the `SharedUp` compile blocker).
- **G2 BUILD (sidecar):** `py_compile` clean on all 7 sidecar `.py` files.
- **G3 BENCH:** EditMode **21/21 pass** (`VirtualMirror.Tests`), re-run green after every edit batch.

**CODE / BENCH rows verified by this agent:**
- **H4** CODE — catch block calls `MediaPipeGlobalInit.Release` (present + compiles).
- **M9** CODE — OAK buffer fields `volatile` (matches MediaPipe pattern).
- **M14** CODE — MediaPipe wrist passes `tracked=false` (PalmRotation no longer computed).
- **M16** BENCH — `bench_m16.py`: depth-hole wrist z = −0.5 from `zrel` (fallback on), 0.0 (off); trunk-flatten + H7 hand gate intact.
- **H7** BENCH — low-confidence hand → `None`; confident hand → 21 points (re-checked in `bench_m16.py`).
- **M-CONSOLIDATE mirror** CODE — resolved by design (sidecar `--mirror` off + Unity owns the live flip); no gate added on purpose.
- **H2** CODE — grep confirms only `Avatar.LastPath` was consumed; dead schema removed; `[SerializeField]` fields are the single source.
- **LOW-A/C/D** CODE — verified by diff (see the audit doc DONE list).

**PLAY rows — REQUIRE THE USER (OAK-D attached + eyes on the avatar).** Not runnable headless: MCP Play-mode freezes when Unity is unfocused and can orphan the `DontDestroyOnLoad` AppBootstrap (see `ai_handoff.md`). These are: H1, H3, H5, H6, M1, M5, M7, M8, M10 (visual), M15, M18, and the §C symptom regression clip. Run the sidecar, tick `useOakUdpTracking`, Play, and walk the §B PLAY column + §C checklist.

## D. Sign-off
Code + automated gates (G1/G2/G3 + the CODE/BENCH rows) are **signed off by this agent (2026-08-07)**.
Final sign-off is the user's **PLAY pass** (§B PLAY rows + §C symptom clip) on the OAK-D. When that passes,
mark the TEST task complete and close the AUDIT items in `ai_handoff.md` / `decisions.md`.
</content>
