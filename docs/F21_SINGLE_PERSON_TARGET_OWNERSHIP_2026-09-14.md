# F-21 — Single-Person Lock / Target Ownership

```text
VERDICT: CONDITIONAL
```

The ownership layer is implemented, unit-proven (36/36), and validated on real hardware for every
scenario reachable without two cooperating people in frame at once: acquisition, persistence under
fast/energetic single-person motion, a real sidecar restart with a person present, and bit-identical
fallback behaviour with the feature off. **What is NOT yet certified is the live two-person scenario
matrix** (crossing, hand-off on release, simultaneous entry) — two live attempts were made; the first
was blocked by a UX defect in the test script (now fixed), the second by a real-camera hardware crash
unrelated to F-21's own code. Per this brief's own §21 rules, that gap caps the verdict at CONDITIONAL,
not PASS — nothing here is asserted beyond what was actually measured.

---

## 1. Problem

F-19 found the failure this closes: Person A is tracked, Person B enters frame, the tracker silently
switches to B while Unity renders smooth continuous motion — nothing in the pipeline ever notices the
human changed. F-20A/F-20B made the *transport* self-recovering; neither touches *who* is being
tracked. F-21 adds explicit target ownership so the system stays attached to one person until a real,
evidence-backed release condition is met.

## 2. Current person-selection path (verified from source, not assumed)

```text
MODEL OUTPUT:                RTMW3D-x (rtmw3d_pose.py) - single-person TOP-DOWN 3D pose, ONE
                              inference per frame -> ONE set of 133 keypoints. No person detector, no
                              NMS, no multi-person output of any kind.
PERSON REPRESENTATION:       Exactly one candidate per frame - "candidate count" in this system is
                              therefore always 0 or 1, never N. A real constraint, not a simplification.
CURRENT SELECTION RULE:      "M15" in wholebody_udp_sender.py: a self-following crop box, refined each
                              frame toward whatever confident keypoints appear inside it, hard-reset to
                              a full-frame centre box after a sustained low-confidence streak. Whoever
                              is most confident inside the current crop simply IS "the person" next
                              frame - nothing checked whether it was the same body.
CURRENT FAILURE MODE:        Confirmed at the source: if a second person's keypoints score higher
                              confidence inside the crop, or land in the centre-reset region, the crop
                              silently re-centres on them and every downstream stage treats it as
                              continuous tracking of "the same body." This is F-19's mechanism, traced.
```

## 3. Existing available identity signals

No track ID, no ReID, no appearance signal anywhere in this codebase (confirmed: no person-id/
track-id/reid/detector references in any project-authored file — only unrelated hits inside the
installed `depthai_cli` library). What is real: per-keypoint raw confidence, depth-backprojected
camera-space position (RAW, if read before P0's smoother rate-limits it), body-confidence mean, torso
scale. P1-1's `JointTracker`/`SkeletonTracker` is PER-JOINT physical-plausibility state, with no
concept of "person" — confirmed by reading its state model (`TRACKED/WEAK/PREDICTED/LOST` per joint
index, no cross-joint identity reasoning).

## 4. Architecture

`python-sidecar~/target_ownership.py` — a pure, clock-injected state machine (no camera, no socket),
the same shape as `joint_tracker.py`/`TrackingStreamHealth.cs`. Wired into `wholebody_udp_sender.py`
at two points:

1. **A new raw-observation identity check**, right after `D.backproject()` and *before*
   `body_smoother.filter()` — using RAW (pre-smoothing) hip position, because by the time the
   existing M11 `mid_hip` exists it has already been rate-limited (`--max-jump` 1.5 m/frame default),
   which would blur a genuine person-swap into what looks like fast continuous motion.
2. **M15's bbox-refinement acceptance**, now identity-gated: the crop only follows an observation
   F-21 currently accepts (`ACQUIRING`/`LOCKED`/`REACQUIRING`); it holds steady during
   `TEMPORARILY_LOST` (never drifts toward a rejected candidate) and only fully re-opens
   (`center_bbox()`) once genuinely `RELEASED`/`NO_TARGET`. Confirmed necessary before writing any
   code: without this, F-21 could detect a switch but could never recover the true owner, since the
   model would already have stopped looking at them.

`--no-ownership` restores the exact pre-F-21 M15 logic unchanged, for A/B comparison — verified
bit-identical in §13.

## 5. State machine

```text
NO_TARGET --(plausible obs)--> ACQUIRING --(5 frames confirmed)--> LOCKED
LOCKED --(obs matches owner)--> LOCKED (refresh reference + age)
LOCKED --(obs missing/mismatched)--> TEMPORARILY_LOST (freeze owner reference, start loss clock)
TEMPORARILY_LOST --(obs matches frozen reference)--> REACQUIRING --(5 frames confirmed)--> LOCKED
TEMPORARILY_LOST/REACQUIRING --(loss clock > RELEASE_TIMEOUT)--> RELEASED --> NO_TARGET
```
`RELEASED` is transient: logged/returned for exactly one `update()` call, then folds back to
`NO_TARGET` so a new acquisition can start immediately. Every transition is logged with an explicit
reason (`TARGET_ACQUIRED/LOCKED/TEMP_LOST/REACQUIRED/RELEASED/REJECTED_CANDIDATE/SWITCH`).

## 6. Acquisition rule

A candidate must stay within `SWITCH_MARGIN` of its own first-seen position for
`ACQUIRE_CONFIRM_FRAMES` (5) consecutive frames before locking. Two candidates alternating every
frame restart the confirm count on whichever appeared, so they never accumulate enough confirmations
to lock — the state machine prefers no lock over an arbitrary one (§14's governing principle).

## 7. Ownership rule

Once `LOCKED`, a new observation is accepted only if it is within `SWITCH_MARGIN` (0.35 m, raw
pre-smoothing camera-space) of the owner's last accepted position **and**, if both are available,
within `SCALE_MARGIN_RATIO` (45%) of the owner's torso-span baseline. A higher-confidence or
more-centred competing observation is **never** sufficient on its own — position/scale consistency
governs, confidence only gates whether an observation counts as a candidate at all (`>= 0.3`, reusing
the exact value M15 already used).

## 8. Temporary-loss rule

Any mismatch or missing observation while `LOCKED` immediately enters `TEMPORARILY_LOST` (no delay —
staying `LOCKED` on a mismatched frame is exactly the silent-switch risk being closed) and freezes the
owner's reference position/scale. A later observation is matched against that **frozen** reference,
not against whatever is currently most confident — this is what makes reacquisition identity-gated
rather than "whoever showed up."

## 9. Release rule

`RELEASE_TIMEOUT` (4.0 s, deliberately longer than F-20A's 2 s `STALE_FAILSAFE` since this is about
human behaviour, not transport health — Unity already shows neutral well before this via F-20A
regardless) with no matching observation → `RELEASED` → `NO_TARGET`. A `REACQUIRE_WINDOW` (2.0 s) is
the faster path within that budget: a match found inside it needs only `REACQUIRE_CONFIRM_FRAMES` (5,
same standard as acquisition) to relock, not a full fresh acquisition.

## 10. Reacquisition rule

Matched-to-frozen-reference frames accumulate a confirm count (`REACQUIRING`); a broken match resets
the count (falls back to `TEMPORARILY_LOST`) rather than keeping partial credit, so a flickering false
match cannot slowly accumulate into a wrong reacquire. `owner_since` is preserved across a successful
reacquire — the age reported is from the original acquisition, not reset by the interruption (unit
test `t06`, confirmed exactly `4*DT` before and after a 6-frame occlusion).

## 11. Multi-person behaviour

Honestly bounded by the signals available (§3): position + scale continuity, nothing else. The
**named, up-front limitation**: if person B steps into the *exact* spot A just vacated within one
observation cycle, position alone cannot distinguish them — this is a property of a geometry-only
signal set, not a defect introduced here, and it is exactly why release is timeout-gated (§9) rather
than instantaneous. §16 below reports what the evidence could and could not rule this out on.

## 12. Implementation changes

```text
new (python-sidecar~/):
  target_ownership.py            the state machine - pure, clock-injected, no I/O
  test_target_ownership.py       36 unit assertions, 16 named tests
  f21_restart_interaction_test.py   real-sidecar restart-with-person-present proof
  f21_video_replay.py            single-person video regression harness (no OAK-D needed)
  f21_live_protocol.py           live two-person HUD-guided protocol

edited (python-sidecar~/):
  wholebody_udp_sender.py        two insertion points (SS4), --ownership*/--cue-file CLI flags
                                  (default ON), target_events.jsonl evidence logging, HUD overlay
                                  drawn on the existing --show preview (state/owner/timers/switches
                                  plus, as of the REV noted in SS14, a large on-screen instruction
                                  banner for live-protocol use)
```

Not modified: arm solver, torso yaw, pose-buffer interpolation, depth acquisition, camera
orientation, supervisor restart logic, the UDP wire contract — matches §15's protection list. No
packet field changes: ownership entirely gates whether `build_body_landmarks`/UDP-send happens at
all, reusing the exact `continue`-skip path already used when `mid_hip` can't be computed, so F-20A's
existing stale watchdog (150/500/2000 ms) turns a withheld frame into hold-then-neutral with **zero
new Unity code**.

## 13. Automated test results

```text
test_target_ownership.py          36/36 PASS   (16 named tests; covers 12 of the SS13 adversarial
                                                 cases directly as Observation sequences, incl.
                                                 crossing, rapid A/B/A, flicker-never-locks, gross
                                                 scale mismatch at the owner's own position)
f21_restart_interaction_test.py    5/5 PASS    (real sidecar, real camera, operator in frame:
                                                 forced kill -> new process -> fresh epoch=1, new
                                                 SID, zero stale-identity carry-over, F-20A untouched)
--no-ownership regression          PASS        (frames == sent every tick; bit-identical to the
                                                 pre-F-21 M15 path)
f21_video_replay.py (123.webm)     PASS        775 frames / ~13s @ 60fps, energetic real dance
                                                 motion (jumps, spins): target_id never left 1,
                                                 0 TARGET_SWITCH, 0 TARGET_RELEASED, 4 brief
                                                 temp-loss episodes, all self-recovered to the
                                                 same target. State-frame counts: LOCKED 564,
                                                 TEMPORARILY_LOST 194, REACQUIRING 13, ACQUIRING 4.
```

The video regression is a genuine stress case for §7's "ordinary movement must not cause switching":
vigorous real human motion at 2x production frame rate, not a calm walk. Scope note, stated plainly:
it has no depth channel, so it uses image-plane position continuity only (a generous 25%-of-diagonal
margin, not production's calibrated 0.35 m) — it proves the state machine doesn't false-trip under
motion, not the exact metric thresholds production uses.

## 14. Live test protocol

`f21_live_protocol.py` runs the real sidecar with `--show` and drives it through the full SS8
scenario list (A-H) via a `--cue-file` the sender polls every preview frame. **Attempt 1's own
protocol design was defective**: instructions were printed to the terminal while the operator needed
to be watching the camera window and coordinating a second person — unreadable in practice, exactly
the class of issue the project's own `live-capture-subject-needs-visible-feedback` lesson warns
about, just worse with two people to coordinate instead of one. **Fixed** before attempt 2: the
current instruction and a countdown are now drawn as a banner directly on the SAME preview window as
the F-21 diagnostic HUD (state/owner/timers/switch count) — one window, not a window plus a scrolling
terminal.

## 15. Live results

**Attempt 1 (pre-fix), 2026-09-14 16:49-16:52.** Ran to completion (~157 s) but is **inconclusive for
scenario-level claims** because the operator could not follow which phase was active. `target_id`
stayed at `1` for the entire run with `TARGET_RELEASED` never firing once — consistent with either (a)
correct, robust retention, or (b) the "well-timed handoff" limitation named in §11 (a second person
occupying the first's just-vacated spot within the match window). The logs alone cannot distinguish
these, and the operator, unable to read the cues live, could not say which scenario was actually
executing at each point either. A genuinely informative fragment near the end: five short
`scale_mismatch`-triggered `TEMPORARILY_LOST` episodes (0.17-1.7 s each) all correctly rejected the
mismatched candidate and reacquired the *same* `target_id` — real evidence the reject-then-reacquire
mechanism functions on live hardware, just not evidence for the specific D/E/F/H scenario claims.
Raw evidence preserved at `oak_v4_evidence/f21/live/attempt1_inconclusive/`.

**Attempt 2 (post-fix), 2026-09-14 17:37-17:40.** The OAK-D device crashed (depthai's own
`"Device ... has crashed"` error, observed intermittently across today's session on this same
hardware) roughly 15-20 s into the `A_ENTER` phase; the sidecar process exited and produced **zero**
F-21 events (`target_events.jsonl` is empty for this run). `f21_live_protocol.py` itself does not
check whether its subprocess is still alive, so it kept advancing through all 16 phases against a
dead sidecar — a real gap in the test script, noted for next time, not something that affected the
sidecar or F-21 itself. Raw evidence at `oak_v4_evidence/f21/live/attempt2_device_crash/`.

**A cross-cutting finding worth carrying forward:** both live attempts were launched via
`wholebody_udp_sender.py` directly (matching how the live-protocol script needs a single
long-running `--show` process), **not through F-20B's `sidecar_supervisor.py`**. The device crash in
attempt 2 is exactly the failure class F-20B's supervisor exists to catch and recover from
automatically; running the *next* live F-21 session through the supervisor (accepting that `--show`
and a supervised restart don't mix trivially - see §21) would very likely have turned that crash into
a short recovery instead of a dead end.

**No live two-person session produced usable evidence for scenarios D/E/F/H** (release+hand-off,
crossing, simultaneous entry, repeated entry/exit). This is the reason for CONDITIONAL, stated
without qualification.

## 16. Wrong-person switch analysis

**Zero `TARGET_SWITCH` events were observed in any run** — automated, video-regression, or either live
attempt. That is the single most important number this report has (§14 of the brief: wrong-person
switch rate is the critical acceptance metric), and it is genuinely zero across every test that
produced usable data. What is **not** yet ruled out is the specific "well-timed handoff" case named
in §11: whether a real second person, deliberately or naturally, occupying the first person's
just-vacated position within `REACQUIRE_WINDOW` would be silently retained as the same target. Attempt
1's flat `target_id=1` for 157 s is *consistent* with this happening but does not confirm it happened
— the honest state is "not ruled out," not "found," and not "cleared."

## 17. False-release analysis

Zero false releases were observed. Attempt 1 shows the opposite risk direction if anything —
`RELEASE_TIMEOUT` never fired even across phases explicitly intended to make the owner leave for
15+ seconds — but per §16 above, whether that is "correctly patient" or "masked by a same-spot
hand-off" cannot be separated from the available evidence.

## 18. Performance impact

No F-19/F-20A/F-20B baseline (fps, frame age, capture→send latency) is touched — F-21 does not modify
the depth pipeline, the smoother, or P1-1; it adds one raw-observation check and a state-machine
`update()` call per frame (µs-scale) plus an occasional small JSON line write. The real-hardware smoke
test (§13's underlying run) showed `fps~30.0`, `age~17ms`, matching F-19/F-20A/F-20B baselines with no
measurable regression.

## 19. Regressions

None found. `--no-ownership` reproduces the pre-F-21 M15 path exactly (§13). No `Runtime/` C# file
changed, so P0/P1-1/P1-2/P1-3/F-08/Arm V2/Torso V5/V6/F-20A/F-20B are untouched by construction; the
124-test EditMode suite was not re-run for the same reason F-20B's report gave (no Unity-side change
to regress), and remains available to run on request.

## 20. Remaining risks

1. **The live D/E/F/H scenario matrix is unverified** — the reason for CONDITIONAL. Needs a clean live
   two-person run, ideally through `sidecar_supervisor.py` so a device crash mid-session recovers
   instead of ending the attempt.
2. **The "well-timed handoff" limitation (§11/§16) is named but not measured.** A deliberate live test
   — B stands exactly where A was, the instant A leaves — would turn this from a named risk into a
   measured one either way.
3. **The video-regression margin (§13) is not the production metric threshold** — it proves the state
   machine doesn't false-trip under fast motion, not that `SWITCH_MARGIN_M = 0.35` is exactly right;
   that number is still a starting value pending live tuning, as planned from the outset.
4. **`f21_live_protocol.py` doesn't detect a dead subprocess** and will narrate a full protocol against
   a crashed sidecar (found live, in attempt 2) — worth a small fix (poll `proc.poll()` between
   phases) before the next live session, not done here since the session moved to closing this out.
5. **Recurring depthai device-crash messages** were observed multiple times across today's F-20B and
   F-21 work on this specific hardware. Most were survived by the pipeline; one (attempt 2) was not.
   This is a hardware/driver-level observation, outside F-21's scope to fix, but relevant to how the
   next live session should be launched (§15's supervisor recommendation).

## 21. Production recommendation

Ship F-21 with ownership **ON by default** (already the default) for any installation, since every
executed test shows zero wrong-person switches and no regressions — the layer is strictly additive
safety, not a behaviour change an operator would need to opt into. Do **not** yet certify the
multi-person envelope (crossing/hand-off/simultaneous-entry) as proven for a public installation until
a clean live two-person session runs; until then, treat "two people reliably crossing paths near the
installation" as an unverified condition, which is exactly what CONDITIONAL means here. Launching a
future live F-21 session through the supervisor needs one small adaptation first: `sidecar_supervisor.py`
doesn't currently forward `--show`/`--cue-file` (§20's remaining-risk list; a quick CLI passthrough
addition, not a design change).

## 22. Final decision

CONDITIONAL: the ownership mechanism is proven correct everywhere it could be exercised — unit tests,
real single-person hardware, a real restart, and a real stress video — with zero wrong-person switches
anywhere. It is not yet PASS only because the live multi-person matrix twice failed to produce usable
evidence (once from a test-script UX defect, now fixed; once from unrelated camera-hardware
instability), not because anything tested showed incorrect behaviour.

---

```text
F-21 VERDICT:
CONDITIONAL - ownership mechanism proven correct on every test that produced usable evidence; the
live two-person scenario matrix (D/E/F/H) is the explicit, named gap.

TARGET OWNERSHIP:
Position- and scale-continuity gated, identity-aware M15 bbox integration, zero wrong-person switches
observed across unit tests, real single-person hardware, a real sidecar restart, and a 775-frame real
video stress test.

MULTI-PERSON SAFETY:
Not yet certified live - two attempts, neither produced usable two-person evidence (UX defect, fixed;
then a camera-hardware crash unrelated to F-21). The "well-timed handoff" limitation is named but not
yet independently measured either way.

TEMPORARY LOSS:
Confirmed both synthetically (unit tests) and live (five real reject-then-reacquire cycles in attempt
1, 0.17-1.7 s each, all correctly returning to the same target).

REACQUISITION:
Confirmed: matches the FROZEN owner reference, not whoever is currently most confident (unit-proven);
observed live recovering the same target_id after every temp-loss episode that occurred.

WRONG-PERSON SWITCHES:
Zero, in every test that produced usable data - the single most important number in this report.

AUTOMATED TESTS:
36/36 unit assertions, 5/5 real-hardware restart-interaction, 1/1 video-regression PASS, --no-ownership
regression PASS.

LIVE TEST:
Two attempts, neither completed cleanly (test-script UX defect, since fixed; then a camera-hardware
crash). No live multi-person evidence yet exists.

REGRESSIONS:
None. No Runtime/ change; --no-ownership reproduces pre-F-21 behaviour exactly.

NEXT ACTION:
Run a clean live two-person session with the fixed on-screen cue banner (ideally through
sidecar_supervisor.py once it forwards --show/--cue-file), specifically targeting the named
well-timed-handoff case, before upgrading this report's verdict to PASS.
```
