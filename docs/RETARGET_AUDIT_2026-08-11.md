# Retarget Audit — "so many retarget issues" (2026-08-11)

Triggered by the user's screen recording (`Screen Recording 2026-08-11 121601.mp4`): the green **debug skeleton tracks the user correctly at every timestamp**, but the **avatar's arms/legs/torso/fingers are in wrong, often asymmetric poses** — twisted waist, wrong facing, one arm up when both are raised, "inhuman" fingers, and no waist bend.

This is a diagnosis report. **No retarget code was changed as part of it** (the only edits this session were the waist-bend adaptive baseline, ADR-026, and a sidecar `min-cutoff` alignment). Fixes below are proposals for sign-off.

---

## 0. Correction of an earlier claim (important)

An interim note this session said the torso was over-rotated ~4–7× because our port multiplies hips/spine by **π** while "Kalidokit does not." **That was wrong.** Kalidokit's own `rigHips` **does** `hips.rotation *= Math.PI` and `spine *= Math.PI` (verified against `src/PoseSolver/calcHips.ts`). So `KalidokitPoseSolver.CalcHipsAndSpine` lines 84–85 (`* KMath.Pi`) are **faithful, not a bug.** The real discrepancy is smaller and is described in R2 below.

---

## 1. Method + what is certain

- **The bug is in the retarget, not tracking.** `AppBootstrap.UpdateTracking` feeds the *same* `filtered` PoseFrame to the debug skeleton (`RenderDebugSkeleton(filtered)`, line 312) and to Kalidokit (`kalidokitControlRig.Apply(filtered)`, line 324). Skeleton right + avatar wrong ⇒ the fault is inside `KalidokitPoseSolver.Solve` or `KalidokitControlRigDriver.Apply`/`ThreeEulerToUnity`.
- All numbers below are computed from the run's own logs (`python-sidecar~/pipeline_logs/`, 4980 send/recv frames, the capture that produced the video). Distance avg **2.21 m** (good), depth coverage **~21/33 keypoints (37% holes)**.

---

## 2. Root causes (prioritized)

### R1 — PRIMARY: un-flattening the trunk (ADR-025) re-exposed the torso-yaw hypersensitivity → torso twist drags the arms. **(high confidence)**

Kalidokit derives **Hips yaw from the hip line (23,24)** and **Spine yaw from the shoulder line (11,12)** via the 2-point `rollPitchYaw` (`KalidokitPoseSolver.CalcHipsAndSpine`, using `KMath.RollPitchYaw2`). That estimate is **hypersensitive to the depth (Z) separation of those two points**: with a shoulder X-separation of ~0.3 m, a Z noise of only ±0.2 m rotates the estimated yaw by `atan(0.2/0.3) ≈ 34°`.

Reconstructing our port's torso yaw from the real recorded landmarks:

| Signal (raw spine-yaw euler, pre-dampener) | value |
|---|---|
| median | **−10°** (persistent rest twist) |
| p5 … p95 | **−119° … +22°** |
| min / max | −179° / +104° |
| frame-to-frame jitter | mean 1.4°, p95 4.4°, **max 206°/frame** |

The **−10° median** = the chest sits persistently yawed even at rest (contributes to "hands forward/off to the side" while standing still). The **−119° p5** and the **±180°/206° flips** (from the `−π/π` jump-fix branches in `calcHips` colliding with the hip-line front/back ambiguity) = the *"faces completely the other direction"* and *"twisted from the waist"* moments. Because the arm bones are **children of the chest**, an over-yawed/flipping chest **swings both arms into wrong world positions** without the arm math itself being wrong.

**Why this is a Milestone-2 regression, not an old bug:** In Milestone-1 the sidecar **flattened the trunk** (zeroed shoulder+hip Z, ADR-023), so this yaw signal was ≈ 0 and the torso was frontal-locked and stable — which is exactly why "arms were solved." **ADR-025 un-flattened the trunk** (to enable turning + waist bend), which fed the noisy shoulder/hip Z straight into this hypersensitive estimator. That is the change that produced "so many retarget issues."

### R2 — AMPLIFIER: our port omits Kalidokit's per-bone torso dampeners. **(high confidence, but modest ~1.4×)**

Kalidokit's reference VRM demo applies the torso rotations through **per-bone dampeners** (canonical values: **Hips 0.7, Spine 0.45, Chest 0.25**). Our path instead applies **Hips at full (×1.0)** (`KalidokitControlRigDriver.Apply` line 251) and splits the spine euler **0.5 / 0.5** across Spine and Chest (lines 223–227). Net effect on the real data:

| Bone | ours | Kalidokit | over-rotation |
|---|---|---|---|
| Hips yaw (p95) | 41° | 29° | 1.43× |
| Chest yaw (p95) | 11° | 6° | 2.0× |
| total shoulder-derived twist | 22° | 16° | 1.43× |

So the missing dampeners make the twist ~1.4× (chest 2×) worse than Kalidokit *intends* — real, cheap to fix, but **secondary to R1** (even Kalidokit's damped version would twist ~−83° at the p5 input; the noisy input is the bigger lever).

### R3 — Waist bend: separate axis, already addressed (ADR-026). **(fixed, pending live verify)**
The "doesn't bend at the waist / leans at rest" is the spine *pitch*, replaced this session with an adaptive baseline. Independent of R1/R2.

### R4 — Possible arm-solver L/R asymmetry, independent of the torso. **(needs one live test — medium confidence)**
The *"flex both arms → only one avatar arm goes up"* (00:27) is not fully explained by a symmetric chest yaw (that swings both arms the same way). Milestone-1 only ever verified **symmetric / single-axis** arm poses (T-pose, arm up/forward/side), so an L/R asymmetry in `KalidokitArmSolver.RigArm` (the `invert` handling) or in the global `ThreeEulerToUnity` conversion **could exist and would not have been caught.** The arm-port was audited faithful (ADR-023) and M1 showed arms are correct *when the torso is stable*, so the conversion is **probably** fine — but this must be confirmed by a live **asymmetric-pose injection** before anyone touches `flipQuat`. **Do not change the conversion blind** (it passes the symmetric tests; a blind change risks regressing what works).

### R5 — Fingers "inhuman / bent outside." **(medium confidence)**
Two compounding causes: (a) at 2–4 m the OAK hand-curl source is noisy/ill-posed (ADR-024 deferred wrist for this reason; the curls have the same problem), and (b) the finger curl axis/sign (`kalidokitFingerCurlAxis`, per-hand `sideSign`) may hyperextend on this rig. Lower priority than the body.

### R6 — Environmental: distance + coverage. **(contributing)**
2.21 m avg is acceptable, but **only ~63% of keypoints get measured depth**; the rest are hole-filled/held. Every depth-derived signal (torso yaw, waist pitch, wrist) degrades with this. ~2 m and better lighting reduce all of it.

---

## 3. Proposed fixes (for sign-off — not yet applied)

Ordered by leverage:

- **F1 (biggest, decision needed): tame the torso yaw.** The honest tension is **turn-tracking vs stability**. Options, best-first:
  - **F1a — ship frontal-lock for the POC (recommended).** Re-enable the sidecar `--flatten-trunk` (or a Unity-side "freeze torso yaw" toggle). M1 proved the body+arms are correct with a stable frontal torso. Turning becomes opt-in, documented as a single-camera limitation (ADR-019/024 precedent). This alone should restore most of the video's failures.
  - **F1b — keep turning but damp it:** heavy smoothing + a **dead-zone / hysteresis** on hips+spine yaw (ignore small noisy yaw, only commit to a turn when it's large and sustained), and **reject the ±180° jump-fix flips**. More work; still fights the coarse depth at range.
- **F2 (cheap, principled): restore Kalidokit's per-bone dampeners** (Hips 0.7, Spine 0.45, Chest 0.25) instead of ×1.0 and the 0.5/0.5 split. Confirm exact values against the pinned Kalidokit demo. Removes the ~1.4–2× amplification.
- **F3 (verification, do before touching arms): live asymmetric-pose injection.** With a *stable/frozen* torso, inject "left arm up only" and "right arm up only" into the live `KalidokitControlRigDriver.Apply` and read both raw hand world positions (the ADR-023 synchronous-read harness). If both track ⇒ R4 is disproved and the arm chaos was all torso-drag (F1/F2 suffice). If one is wrong ⇒ localized arm-solver/conversion bug to fix.
- **F4 (fingers): confirm curl axis/sign live; for the POC without a close-hand source, consider relaxing fingers to a soft rest** rather than driving noisy curls (ADR-024 logic).
- **F5: stand ~2 m**, decent lighting.

## 4. What NOT to do
- Do **not** "fix" the ×π (R0 correction — it's faithful).
- Do **not** change `kalidokitBodyFlipQuat` / the arm conversion before F3 confirms an actual arm bug — it passes the symmetric verification and a blind change regresses working arms.
- Do **not** re-add a Unity-side landmark smoothing stage (ADR-020 single-owner) — damp the *derived* torso yaw, not the landmark stream.

## 5. One-line summary
Milestone-2's trunk un-flatten (ADR-025) fed noisy shoulder/hip depth into Kalidokit's hypersensitive torso-yaw estimator, over-twisting the chest and dragging the arms; our missing per-bone dampeners amplify it ~1.4×. Recommend shipping **frontal-lock** for the POC (F1a) + restoring the **dampeners** (F2), then a live **asymmetric-arm test** (F3) before touching the arm conversion.
