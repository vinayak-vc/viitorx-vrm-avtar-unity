# F-21 — Single-Person Lock / Target Ownership

```text
VERDICT: CONDITIONAL - OFFLINE FIX VERIFIED, LIVE ACCEPTANCE PENDING
STATUS:  OFFLINE ENGINEERING COMPLETE - LIVE ACCEPTANCE PENDING
```

> **2026-09-16 — THE LIVE SESSION RAN, AND FOUND A SECOND SILENT HAND-OFF. Read §34 first.**
> With two real people on the real camera, F-21 emitted person B under person A's identity for
> ~97 datagrams, inside one epoch, with **zero events logged** — the §27 defect, live. §30's gate is
> not at fault and did not regress: it guards *reacquisition*, and this never left `LOCKED`. The
> cause is that the owner reference **drifts** — `owner_pos` is updated to every accepted frame, so a
> migration slower than 0.35 m *per frame* walks the reference from one human to another without
> failing a single test ([ADR-061](decisions.md)). The headline metric read `false holds 0/4` and is
> blind to it. **Do not ship single-person ownership as a safety property on this evidence.**
>
> **2026-09-15 hand-off-fix pass — read §30–§31 first.** The silent wrong-person hand-off that
> §27 reproduced and deliberately left unfixed is now **fixed** ([ADR-057](decisions.md)): a
> reacquire must be *path*-consistent, not merely position-consistent. On `123.webm` the hand-off
> goes from **38 wrong-person frames to 0**, and the hand-over that does happen is now **declared**
> (`TARGET_RELEASED` + `TARGET_SWITCH` + a new epoch) instead of silent. It costs 96 frames of a
> legitimate returning owner, measured rather than estimated — §30.9. `video.webm` and `456.webm`
> are bit-identical with the gate on and off. **The verdict is still CONDITIONAL**: no live
> two-person evidence exists yet.
>
> **Also fixed, and it matters more than it sounds (§31.4):** `--cue-file` had **never worked** —
> `wholebody_udp_sender._read_cue()` calls `io.open` in a module that never imported `io`, and its
> own `except Exception: return None` swallowed the `NameError` on every frame. The on-screen cue
> banner that REV 2 added — because the first live attempt failed when the operator could not follow
> printed instructions at the camera — has been silently dead since it was written. One-line fix,
> verified by rendering the banner.
>
> **2026-09-14 offline completion pass — read §23–§29 before relying on anything above them.**
> Two of this report's original conclusions did not survive it and are corrected in place:
>
> 1. **§13's `123.webm` result was based on a false premise.** That clip is not a single-person
>    stress video. It contains a second person entering at ~frame 626 and the first person leaving
>    afterwards. Its `target_id never left 1, 0 TARGET_SWITCH` was read here as evidence of stable
>    ownership; it is in fact a **silent wrong-person hand-off**, now confirmed frame by frame with
>    images (§27). The §11/§16 "well-timed handoff" limitation is therefore no longer merely
>    "not ruled out" — it has been reproduced and measured.
> 2. **A real defect was found and fixed in the reacquisition path** (§25): an owner who stepped
>    away for longer than `REACQUIRE_WINDOW` and returned to their exact original position was
>    rejected 51 consecutive times as `not_owner`, released, and re-acquired with a `TARGET_SWITCH`
>    logged against a person who never moved.
>
> The verdict stays CONDITIONAL, for a better-evidenced reason than before.

The ownership layer is implemented, unit-proven (46/46 after §25's regression guards), and validated
on real hardware for every scenario reachable without two cooperating people in frame at once:
acquisition, persistence under fast/energetic single-person motion, a real sidecar restart with a
person present, and bit-identical fallback behaviour with the feature off. **What is NOT yet
certified is the live two-person scenario matrix** (crossing, hand-off on release, simultaneous
entry) — two live attempts were made; the first was blocked by a UX defect in the test script (now
fixed), the second by a real-camera hardware crash unrelated to F-21's own code. Per this brief's own
§21 rules, that gap caps the verdict at CONDITIONAL, not PASS — nothing here is asserted beyond what
was actually measured.

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
f21_video_replay.py (123.webm)     WITHDRAWN    see the correction below - this result was real but
                                                 its interpretation was wrong.
```

> **CORRECTION (2026-09-14, §27).** This line originally read
> `PASS  775 frames / ~13s @ 60fps, energetic real dance motion (jumps, spins): target_id never left
> 1, 0 TARGET_SWITCH, 0 TARGET_RELEASED, 4 brief temp-loss episodes, all self-recovered to the same
> target` — and it was cited as evidence that ownership does not false-trip under fast motion.
>
> `123.webm` is **not a single-person clip.** A second person enters at ~frame 626 and the first
> person leaves shortly after. The flat `target_id = 1` across that change is not stability: the
> emitted skeleton moves from one human to the other inside a single ownership epoch, with no
> `TARGET_RELEASED` and no `TARGET_SWITCH` (§27, with frames). The raw counts above are accurate; the
> word PASS was not. The clip has been re-purposed as this report's strongest multi-person evidence
> rather than dropped.
>
> The single-person false-trip claim it was supporting is now carried by `video.webm` (435 frames,
> genuinely one person) and by `456.webm` (§26), neither of which produced a single rejection at the
> production-equivalent margin.

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

## 23. Offline completion pass (2026-09-14) — scope

No physical person was available for this pass (explicit user instruction), so everything here is
deterministic, replay, or process-level work. Nothing below claims live two-person certification;
what it does is remove every remaining *offline* unknown so the next live session is acceptance
testing rather than development.

```text
new (python-sidecar~/):
  f21_adversarial.py          17-scenario deterministic multi-target suite with ground-truth labels
  f21_multiperson_replay.py   real crowded-footage replay, ownership ON vs OFF, margin sweep
  f21_handoff_forensics.py    surfaces reacquires that are position-plausible but came from
                              elsewhere, and writes the frames so the call is made by looking
edited:
  target_ownership.py         SS25 reacquisition fix + a truthful rejection reason
  test_target_ownership.py    36/36 -> 46/46 (two new regression guards for SS25)
```

## 24. Deterministic multi-target adversarial suite — 63/63 PASS

`f21_adversarial.py` runs the brief's 17 scenarios with **ground-truth labels on every observation**,
which lets it measure the number production logs structurally cannot:

> **silent wrong-person emission** — a frame F-21 let through whose underlying human is not the one
> this ownership epoch locked onto.

That is the actual F-19 defect, and it is *not* the same as the `TARGET_SWITCH` counter, which only
counts a fresh acquisition after a release — a switch F-21 has already declared out loud. A system
can report `TARGET_SWITCH = 0` while silently emitting the wrong person; §27 shows exactly that
happening. Both numbers are reported separately throughout.

Scope limit, stated plainly: RTMW3D-x emits one candidate per frame, so a "second person" here is
represented the only way one can ever actually reach F-21 — **the observation stream switches
source**, which is precisely what the M15 crop hand-off does. This proves the ownership logic. It
does not prove detector behaviour, and is not offered as if it did.

| metric (17 scenarios, 3,697 frames, 1,184 emitted) | result |
|---|---|
| wrong-person switches | **1** — the deliberately-provoked identical-build crossing only |
| wrong-person frames | **12**, all in that one scenario; **0** everywhere else |
| false releases | **0** |
| false reacquisitions | **0** |
| ownership oscillation cycles | 19 (12 of them in the 12× A/B/A/B scenario, i.e. one per interruption) |
| declared `TARGET_SWITCH` events | 3, each a genuine hand-over after a real release |
| acquisition latency | 4 frame intervals (**133 ms** at 30 fps) |
| release latency | **4.067 s** = `RELEASE_TIMEOUT` + 2 frame intervals |
| recovery latency | 4 frame intervals (**133 ms**) |

The crossing scenario is worth separating because it is the §11 limitation, deliberately provoked and
now **quantified** rather than described:

- **identical build, paths crossing** — 12 wrong-person frames leaked (the full crossing window).
  Position is identical at the crossing point and the scale corroborator cannot separate two
  similarly-sized adults. This is a property of a geometry-only signal set, not a coding error.
- **different build (0.40 m vs 0.75 m torso span), same crossing** — **0** wrong-person frames. The
  scale corroborator carries it alone.

Latency figures are reported as frame *intervals*: `ACQUIRE_CONFIRM_FRAMES = 5` means the 5th
consecutive frame is the first emitted one, i.e. 4 intervals of delay. Release latency is
`RELEASE_TIMEOUT + 2` intervals, not `+1`, because the loss clock starts on the first *missing*
frame — one interval after the last seen one — and fires on the first frame strictly past the
timeout. Stated exactly rather than rounded, since these feed F-20A's 150/500/2000 ms budgets.

Scenarios 16 and 17 are process-level rather than state-machine-level:

- **16, sidecar restart while ownership exists** — a restarted process owns nothing: `NO_TARGET`,
  epoch 0, no owner reference, and no persistence path exists in the module at all (asserted, not
  assumed). It re-earns the lock from scratch in 5 frames, and three real interpreter launches
  produce three distinct F-20A session ids.
- **17, Unity restart while ownership exists** — driven over a real loopback socket. 300 datagrams
  (10 s at production rate) were sent into a closed port with **no exception**, which matters because
  `wholebody_udp_sender.py`'s `sendto` is not wrapped in `try/except` and Windows can surface an ICMP
  port-unreachable as `WSAECONNRESET` on a later send; it does not here. A rebound receiver got the
  very next datagram, and ownership state was **bit-identical** across the outage.

## 25. A real defect, found and fixed: the reacquisition dead zone

Scenario 8b found this, and it is the clearest example of why the suite was worth building.

`RELEASE_TIMEOUT` is 4.0 s and `REACQUIRE_WINDOW` is 2.0 s. §9 above describes the window as *"the
faster path within that budget"* — language that presupposes a slower path also inside the budget.
There was none. The code gated the frozen-reference match itself on `loss_elapsed <=
reacquire_window_s`, so past 2.0 s matching was not slowed down, it was **switched off**.

Measured consequence, with the owner standing still in their exact original position at full
confidence after a 2.33 s absence:

```text
f110  owner returns to u = 826 px, the precise spot they left      -> TARGET_REJECTED_CANDIDATE
f111 .. f160   51 consecutive rejections of the actual owner        -> reason "not_owner"
f161  TARGET_RELEASED            (the owner is visible and stationary throughout)
f166  TARGET_ACQUIRED / TARGET_LOCKED / TARGET_SWITCH  <- logged against a person who never moved
```

1.87 s of a present, healthy, stationary owner withheld from the consumer, and a `TARGET_SWITCH` —
this report's single headline safety metric — fired spuriously.

**The fix**: attempt the frozen-reference match for the whole release budget; `REACQUIRE_WINDOW` keeps
its documented meaning as the selector for the confirm count. Both counts default to 5, so the
default numeric behaviour is unchanged — what changes is that the owner can be matched at all.

This was done without live evidence for one specific reason: it is **strictly safer than the
behaviour it replaces**. The alternative the old code forced — release, then fresh acquisition —
accepts *any* body at *any* position with *no* frozen-reference check whatsoever. Requiring the
position+scale gate against the frozen owner is a strictly stronger admission test than the
release-and-reacquire path it avoids. The rejection reason also now names the real discriminator
(`position_jump` / `scale_mismatch`) instead of a flat `not_owner` that hid which gate fired.

Guarded by two new unit tests (`t17`, `t18`): the returning owner is re-locked with no release, no
switch, no rejection, and emission resumes after the confirm window; and a *different* body arriving
in that same widened interval is still rejected on position, still cannot acquire without waiting out
the full release. **46/46** (was 36/36).

## 26. Real multi-person footage — `456.webm`, eight dancers

A different evidence class from the single-person regression: roughly eight real humans in frame at
once, in real imagery, with real occlusion and real crossing, driving the real crop→infer→refine loop
that failed in F-19.

What it can measure without any identity ground truth is the geometric signature of a hand-off — the
image-plane hip displacement **between consecutive emitted frames** — and how it responds to the
ownership margin. The production `SWITCH_MARGIN_M = 0.35` subtends ≈141 px at this clip's geometry
(0.35 m at ~2 m through a ~800 px focal length), so the sweep is anchored on a production-equivalent
value rather than an arbitrary one.

| margin | px | emitted | max jump px | rejections | temp-loss | switches | releases |
|---|--:|--:|--:|--:|--:|--:|--:|
| OFF (pre-F-21 M15) | – | 356/356 | 126.1 | – | – | – | – |
| **ON, production-equivalent** | **141.0** | **352/356** | 126.1 | **0** | **0** | **0** | **0** |
| ON | 220.3 | 352/356 | 126.1 | 0 | 0 | 0 | 0 |
| ON (3× tighter) | 66.1 | 333/356 | 64.9 | 12 | 1 | **0** | **0** |
| ON (7× tighter) | 44.1 | 337/356 | 43.8 | 11 | 1 | **0** | **0** |

Three things this actually establishes:

1. **Zero cost at the production margin on genuinely crowded footage.** The 4-frame difference from
   OFF is exactly the acquisition confirm window, nothing more.
2. **The production margin has ~11 % headroom** on fast group dance — max observed jump 126.1 px
   against a 141 px margin, p99 57.8 px. False rejections begin just below it, at ~126 px. That is
   tighter than is comfortable, and it is a real input to live tuning.
3. **A too-tight margin fails gracefully.** Even 3× too tight produced no release and no switch —
   only brief withholding and self-recovery to the same target. The failure mode of over-tightening
   is a short hold, not a wrong person.

Honest null result, stated as such: the crop never actually migrated to another dancer on this clip,
so this does **not** demonstrate F-21 catching a hand-off. It demonstrates F-21 costing nothing when
there is nothing to catch. The clip that *does* contain a hand-off is the next section.

## 27. The silent wrong-person hand-off, reproduced on real footage

`123.webm` contains a real person change: a woman dancing alone, a man entering at ~frame 626, and
the woman leaving shortly after. Replayed through the real ownership path at the
production-equivalent margin:

```text
f600-f625   LOCKED, emitting          hip_u 623 -> 826 px     (the woman)
f626        the observation jumps to the other body            -> TARGET_TEMP_LOST position_jump
f630-f683   54 frames of TEMPORARILY_LOST, 51 rejections       (correctly refusing the man)
f684-f686   REACQUIRING
f688        LOCKED, emitting resumes  hip_u 728 px             (the MAN)
f690-f730   emitting, hip_u 744 -> 1079, torso span 292 -> 207 (the man, walking out of frame)

frames=775  emitted=501  epochs=1  releases=0  switches=0
```

F-21 did a great deal right: it detected the jump immediately, refused the intruder for 0.9 s, and
never let a single frame of the cross-over through. What it then did wrong is the whole finding —
the man kept walking, arrived within the 141 px margin of the woman's frozen last-known position
(98 px away), passed the frozen-reference match, and was re-locked **as the same `target_id`**.

The consumer sees: `target_id = 1`, `TARGET_SWITCH = 0`, `TARGET_RELEASED = 0`, continuous smooth
motion. Nothing anywhere in the pipeline says the human changed. **This is the F-19 defect, occurring
inside the layer built to prevent it**, via the reacquire path rather than the M15 crop path.

Confirmed by eye, not by threshold — `f21_handoff_forensics.py` deliberately refuses to auto-classify
and writes the frames instead, because no threshold on a hip coordinate can decide whether two sets
of pixels are the same human:

```text
oak_v4_evidence/f21/handoff/123_handoff_f00621_owner_before_loss.png        skeleton on the woman
oak_v4_evidence/f21/handoff/123_handoff_f00628_other_body_at_loss.png       skeleton on the man
oak_v4_evidence/f21/handoff/123_handoff_f00690_reacquired_as_same_target.png  still the man, emitting
```

This is exactly the §11/§16 "well-timed handoff" limitation. It was named there as *"not ruled out"*;
it is now **reproduced, measured, and imaged**. `456.webm` and `video.webm` produced no such
candidate, so the incidence is 1 hand-off in 3 clips / 1,566 frames — but incidence on three arbitrary
dance clips is not a public-installation rate, and is not offered as one.

**Not fixed today, deliberately.** Closing it needs either an identity signal this pipeline does not
have (the brief forbids inventing one without evidence) or new admission logic — development, not
validation. The measurement, the reproduction and the images are the deliverable; the fix is the
first thing the next live session should be designed around. §29 names the specific candidate.

## 28. Regression status after this pass

```text
test_target_ownership.py            46/46 PASS   (36/36 before SS25's two new guards)
f21_adversarial.py                  63/63 PASS   17 scenarios
f21_video_replay.py (video.webm)    PASS         435 frames, genuinely single-person
f21_multiperson_replay.py           PASS         456.webm, 4 margins, 0 switches at every one
Unity EditMode (reflection runner)  170/170 PASS  no Runtime/ change, re-run anyway
OFFLINE REPLAY SOAK                 PASS         16 min / 29,595 frames / 0 exceptions; 76 TEMP_LOST
                                                  episodes all reacquired, 0 releases, 1 epoch, flat
                                                  memory. Full detail in the F-22 report SS28b, since
                                                  the soak runs F-21 and F-22 together.
```

## 29. Updated remaining risks

Replacing §20's list where it has moved:

1. **The silent hand-off of §27 is now measured, not hypothetical.** It is the top item. The specific
   candidate fix, for the next session to arbitrate rather than for this one to guess at: require a
   reacquire to be *path*-consistent as well as position-consistent — a candidate that was observed
   far outside the margin during the loss and walked back in is the exact signature, and it is
   derivable from positions already held, without inventing an identity signal.
2. **The live D/E/F/H matrix is still unverified** — unchanged, and §27 raises its priority sharply,
   since the deliberate same-spot hand-off now has a known-positive offline counterpart to compare
   against.
3. **`SWITCH_MARGIN_M = 0.35` has ~11 % headroom** on fast crowded footage (§26). Still a starting
   value; now with a measured false-rejection onset (~126 px, ≈0.31 m equivalent) instead of none.
4. ~~`f21_live_protocol.py` still doesn't detect a dead subprocess~~ — **FIXED, §31.1.**
5. ~~`sidecar_supervisor.py` still doesn't forward `--show`/`--cue-file`~~ — **FIXED, §31.2.**

> Item 1 of this list — the silent hand-off — is **fixed in §30**. Items 2–3 stand, and §30.12
> replaces this list for everything this pass touched.

## 30. OFFLINE HANDOFF-FIX PASS (2026-09-15)

`§27` reproduced a silent wrong-person hand-off on real footage and deliberately left it unfixed,
because closing it looked like it needed either an identity signal this pipeline does not have or new
admission logic whose false-rejection cost only live data could price. This pass closes it with
neither — using only positions the ownership layer already holds — and then measures that
false-rejection cost offline instead of guessing at it.

### 30.1 The bug

```text
A locked  ->  A lost  ->  B enters, REJECTED (far away)  ->  B walks toward A's frozen position
          ->  B becomes position- and scale-compatible   ->  B REACQUIRED AS A
```

The consumer sees `target_id` unchanged, `TARGET_SWITCH = 0`, `TARGET_RELEASED = 0` and continuous
smooth motion. Nothing anywhere says the human changed.

### 30.2 Evidence, and a metric that can actually see this

`TARGET_SWITCH` counts *declared* hand-overs. It is structurally incapable of counting silent ones,
which is exactly why this report's original headline safety number read zero straight through a real
person change. The metric that can see it needs per-frame identity ground truth, which the pipeline
does not have and must not have:

```text
WRONG_PERSON_FRAMES = emitted frames whose actual human is not the human this epoch locked onto
```

`f21_ground_truth.py` supplies that label **offline, for harnesses only**. It is a measuring
instrument, not a new identity signal: it is unreachable from `wholebody_udp_sender.py`, it needs the
whole clip up front (a background plate from a static median), and it could not run live if anyone
wanted it to. Using appearance to *score* the pipeline is the opposite of using appearance to *drive*
it, which §2 of the brief forbids and which this does not do.

### 30.3 Root cause

The reacquisition test asked one question — *is this candidate standing where the owner was?* —
and had no way to ask the other one: *where did it come from?* `_matches_owner()` compares against a
single frozen point. Nothing in the module retained a candidate's previous position, so a body that
walked in over 54 frames and a body that simply reappeared were the same observation to it.

### 30.4 The fix — path consistency

One idea, implemented at `target_ownership.py:256`, the documented insertion point:

```text
_chain_walked_in is True iff the candidate currently being observed has, at any point in an
unbroken observation chain during this loss episode, been seen OUTSIDE the switch margin.
```

A matched candidate carrying that flag is refused with `reason="path_walked_in"`. Two constants, and
neither is a new tunable invented for this clip:

* **continuity distance = `switch_margin_m`**, the module's existing "a one-frame move further than
  this means a different body" quantity — precisely what the `LOCKED` branch already tests every
  frame. Measured against all three clips it separates cleanly: the largest single-frame
  displacement of a genuinely continuous body is **126.1 px** (456.webm, eight dancers, fast) against
  a 141 px production-equivalent margin, while the three real body-to-body jumps in 123.webm are
  **303.5, 323.0 and 687.5 px**. 12 % headroom above the fastest real motion, and **2.15× below the
  smallest real body swap** - measured against the smallest, not the largest, since that is the one
  that has to stay on the correct side of the threshold.
* **`chain_gap_s = 0.25`**, the longest observation gap across which continuity is still assertable.
  A walking human covers about `SWITCH_MARGIN_M` in that time (0.35 m at ~1.4 m/s), so it is the
  point past which "the same body moved" and "a different body appeared" stop being separable at all.

The deliberate asymmetry, which is the whole safety argument: **a chain break that lands inside the
gate clears the flag.** A body simply appearing at the owner's spot with no observed approach is
genuinely indistinguishable from the owner stepping back out from behind an obstruction, so it stays
admissible. Only the case that *is* distinguishable — the tracked walk-in — is refused.

**What this does not do, stated plainly.** It does not identify anybody. An owner who genuinely walks
out of the margin and walks back is refused too, because from a hip position that is the *same
observation stream* as an impostor walking in. The gate does not resolve the ambiguity; it resolves
it **towards announcing the change rather than hiding it**. The refused case then releases normally at
`RELEASE_TIMEOUT_S` and re-acquires as a new epoch with `TARGET_SWITCH` logged — a hand-over the
consumer is *told about*. That is the trade, and §7 of the brief is explicit about which way it
should go: a false hold is acceptable, a silent wrong-person hand-off is not.

### 30.5 Rejected alternatives

| Considered | Why not |
|---|---|
| Direction-of-arrival vs direction-of-departure | Overfits 123.webm, where both the impostor and the returning owner arrive from the left. Brief §5 forbids exactly this; P4 and P7 exist to keep it forbidden. |
| Owner velocity extrapolation / expected return region | A person about to be occluded has a low-information, noisy velocity, and the two clips' returns are not continuations of the departure. It would add state and a prediction horizon to buy nothing measurable here. |
| Release immediately on a path-inconsistent candidate instead of holding | Better availability, same wrong-person safety — but it destroys the frozen reference the moment any passer-by crosses, so a genuinely occluded owner loses their epoch to a stranger walking past. Holding keeps the reference for the full budget. Brief §7 decides this. |
| A tighter `SWITCH_MARGIN_M` | §26 already measured false rejections beginning at ~126 px against a 141 px margin — ~11 % headroom. Tightening trades a measured, live-verifiable margin for an unmeasured one, and does not address a candidate that reaches the *exact* frozen position anyway. |
| ReID / appearance / face / clothing / a detector | Forbidden by §2, and correctly: none exists in this pipeline. |

### 30.6 The instrument had to be fixed before it could be trusted

Recorded because it is the kind of error that silently inflates a safety number, and because it was
caught three times by checking frames rather than by trusting an agreeing figure.

1. **Median hue.** A dancer's face and bare arms sit inside the torso band, and skin (hue ~10–20)
   lands on top of the *other* dancer's orange shirt. At f111 the median flipped to the man while the
   woman was demonstrably alone in the room, and six frames were being counted as wrong-person
   emissions by the pipeline. They were nothing of the sort.
2. **Largest-share.** Fixed half of it, and missed the real point: **skin is not class-specific.**
   Both dancers have faces, so the orange window contains evidence for *both*. At f111 the woman's own
   skin took 0.570 of her torso band against 0.405 for her shirt. Only the green t-shirt belongs to
   exactly one person, so only it is allowed to decide. Measured: woman's green share p10 0.553 /
   median 0.688 over 31 blobs; man's **maximum 0.001** over 13. Threshold 0.15 — 3.7× below her p10,
   150× above his max.
3. **Absence of evidence.** "No green" was still being read as "the man", so the woman *entering* at
   the left edge — her shirt still outside the frame — anchored epoch 1 to the wrong person and
   reported **429** wrong-person frames. Two independent fixes: an area floor (her entry fragments are
   10 k–34 k px against a 41 k floor), and a per-epoch **modal** anchor instead of a first-frame one.

Two further filters were tried and **removed**, both for the same reason and both caught the same way
— by checking what they did to the two-person frames rather than only to the solo ones:

* a hue-plausibility filter (`hue-0-12 share ≤ 0.6`) that looked well-separated on the solo stretches
  and **deleted the man from every two-person frame**, because his striped shirt goes past 0.6 under
  the lamp;
* a completeness filter (bounding box clear of the frame edges) that was sound reasoning and
  measurably wrong here, because 123.webm is 1080 px wide and portrait and through the whole
  two-person stretch *both* dancers have a box on an edge.

Validated afterwards against 41 eye-checked frames (`validate_labels.py`), with the residual error
rate reported rather than assumed:

```text
36/41 correct (87.8%)
 4 CONSERVATIVE  a present person not labelled -> the frame is EXCLUDED from scoring, so this
                 class of error can never invent a wrong-person frame
 1 DANGEROUS     f594, a lamp-lit wall region labelled 'M'. Verified unreachable: nearest() scores
                 distance-to-bbox, the emitted hip at u=576 is inside the woman's 256 k-px blob at
                 distance 0, and the spurious blob spans x=0-187.
```

### 30.7 New tests

```text
test_target_ownership.py     46 -> 61 assertions   t19 refuse a walk-in, t20 admit a plain
                                                   reappearance, t21 the gate-off control,
                                                   t22 chain_gap_s breaks a stale chain
f21_adversarial.py           63 -> 101 assertions  P1-P11 (brief §9), 17 original scenarios kept
f21_wrongperson_replay.py    NEW   ground-truth wrong-person measurement, whole production chain
f21_ground_truth.py          NEW   the offline labeller
validate_labels.py           NEW   scores the labeller against eye-checked frames
f21_relabel.py               NEW   re-score a recorded run without re-inferring, so a change to the
                                   RULER can never be mistaken for a change to the FIX
f21_perf_ab.py               NEW   isolated ownership-layer A/B
f21_supervisor_integration.py NEW  brief §15
```

`P11` is the control, and the reason the claim is falsifiable: the *same input* with
`path_consistency=False` reproduces the old behaviour exactly — 43 wrong-person frames, `switches=0`,
`releases=0`, `epochs=1`, silent — and with the gate on, 0. It is excluded from the suite aggregate
by name, because folding a deliberately-disabled run into a number that gets read as "what production
does" would be straightforwardly false.

### 30.8 Results — `123.webm`, whole production chain, real metres

Run at the **unmodified production config** (`switch_margin_m=0.35`), not a pixel proxy: every frame
is lifted into metric camera space by the same pinhole back-projection production uses. Stated
limitation — RTMW3D's `zrel` is relative *to the hip*, so the hip has no observable depth here and
sits on a fixed 2.0 m plane. Hip motion is lateral only.

Real stereo depth turns the gate's 0.35 m disc into a 0.35 m sphere and gives it a third axis to
separate bodies on, so it becomes strictly **more** selective. That cuts both ways and both halves
are worth saying: the wrong-person figures below are therefore a **pessimistic bound** — live depth
can only reduce them — but the availability cost in §30.9 is an **optimistic** one, because a
stricter gate refuses *more* returning owners, not fewer, and depth noise adds a fourth way for a
legitimate owner to fall outside the margin. Neither direction is guessed at here; §30.12 lists it as
the thing the live session has to measure.

```text
ARM                emitted   held   WRONG  episodes  releases  switch  path-rej
OWNERSHIP_OFF          775      0     102         2         0       0         0
PATH_OFF (today)       500    275      40         2         0       0         0
PATH_ON  (the fix)     404    371       2         1         1       1        30
PATH_ON, F-22 off      404    371       2         1         1       1        30
```

The wrong-person episodes, which is where the number actually lives:

```text
OWNERSHIP_OFF   f14-f15 (2)   f626-f725 (100 frames, 1.67 s)
PATH_OFF        f14-f15 (2)   f688-f725 ( 38 frames, 0.63 s)   <- the §27 hand-off
PATH_ON         f14-f15 (2)
```

`f14-f15` is present in **every arm**, so it cancels in the comparison — and it is not a pipeline
error at all. Verified by eye: at f14 the woman is entering at the left edge, half out of frame, her
green shirt mostly outside her blob's torso band, and the pipeline is correctly locked on the only
person in the room. It is the residual instrument artefact of §30.6, imaged at
`oak_v4_evidence/f21/wrongperson/frames/123_residual_f00014.png`.

```text
123.WEBM HAND-OFF SEGMENT, WRONG-PERSON FRAMES: 0
```

And the hand-over that does occur is now **declared**:

```text
f438  TARGET_TEMP_LOST         (the room empties)
f515  the woman walks back in  -> refused, path_walked_in, 30 rejections
f678  TARGET_RELEASED          (4.0 s budget expires, exactly on schedule)
f683  TARGET_ACQUIRED / TARGET_LOCKED / TARGET_SWITCH   epoch 2
      epoch 1 owner = W  (majority 0.99, histogram W:332 M:2)
      epoch 2 owner = M  (majority 1.00, histogram M:43)
```


### 30.8b F-21 and F-22 credited separately (brief §13)

F-22 rejects biomechanically impossible geometry, and cross-person keypoint contamination sometimes
*is* biomechanically impossible — so an F-22 suppression could easily be mistaken for F-21 working.
The fourth arm exists to stop that being assumed either way. `PATH_ON_NO_F22` runs the identical
input with the validator switched off entirely:

```text
ARM               wrong-person   F-21 path rejections   F-22 joint suppressions
PATH_ON                      2                     30                      110
PATH_ON_NO_F22               2                     30                        0
```

**Identical.** Every wrong-person frame this pass eliminated was eliminated by F-21's path gate, and
none of it is owed to F-22. Equally, F-22 is not idle — 110 frames carried a suppressed joint — it is
just doing an unrelated job. The two are measured apart and neither is credited with the other's work.

A second reading of the same table, worth stating because it is the more interesting one: F-22
suppressions *fall* as ownership gets stricter. As raw counts that would prove nothing — each arm
emits fewer frames than the last — so per emitted frame:

```text
OWNERSHIP_OFF   366 / 775 emitted = 47.2 %
PATH_OFF        144 / 500 emitted = 28.8 %
PATH_ON         110 / 404 emitted = 27.2 %
```

The rate roughly halves. That is not F-22 improving — it is F-22 having less contaminated input to
reject, because F-21 stopped emitting frames whose limbs and hips came from different bodies. It corroborates §27's finding that nothing before F-22 constrains
the limbs to belong to the same body as the hips — and shows F-21 doing that work upstream.

### 30.9 The cost, measured rather than asserted

The brief accepts a false hold. It is still worth knowing exactly what one costs, and on this clip
the honest answer is *more than the thing it prevents*:

```text
eliminated : 38 frames (0.63 s) of SILENT wrong-person emission
cost       : 96 frames (1.60 s) of the LEGITIMATE OWNER withheld, in one contiguous block
             f530-f625 - she walked back in, and a returning owner is geometrically identical
             to an intruder walking in
             + a 4.0 s release budget spent holding while she was visible
```

Roughly 2.5 frames of the right person withheld per frame of the wrong person suppressed. That ratio
is a property of *this clip* (the owner leaves and returns once, mid-clip) and should not be read as a
general rate. `RELEASE_TIMEOUT_S` is the knob that trades blackout for churn, and it is a live-evidence
decision — not one to guess at today.

### 30.10 Regression status

```text
test_target_ownership.py            61/61 PASS    (46/46 before this pass; +15 new)
f21_adversarial.py                 101/101 PASS   (63/63 before; 17 original scenarios unchanged)
test_pose_validation.py             22/22 PASS    F-22 untouched
test_joint_tracker.py               37/37 PASS
test_kinematic_recovery.py          39/39 PASS
test_surface_depth.py               32/32 PASS
                                   ------------
  Python unit suites                191/191 PASS  (was 176/176; the +15 are this pass's t19-t22)
f22_adversarial.py                 120/120 PASS   F-22 re-run anyway, untouched
f21_restart_interaction_test.py       5/5 PASS
f20b_deployment_verify.py           35/35 PASS    with the §31 supervisor change in place
Unity EditMode (VirtualMirror.Tests) 170/170 PASS, 0 failed
                                                  124 parameterless [Test] + 46 [TestCase] rows.
                                                  Zero Runtime/ or Editor/ change: `git diff
                                                  -- Runtime/ Editor/` is empty.
video.webm  (genuine single person) PATH_OFF and PATH_ON are IDENTICAL: 435 frames, 431 emitted,
                                    4 held (acquisition only), 0 temp-lost, 0 releases, 0 switches,
                                    0 path rejections.
456.webm    (eight real dancers)    PATH_OFF and PATH_ON are IDENTICAL: 356 frames, 347 emitted,
                                    9 held, 1 temp-lost -> 1 reacquire, 0 releases, 0 switches,
                                    0 path rejections. The gate never fires on crowded footage
                                    with real crossing and occlusion either.
```

A note on the Unity count, since it moved: a first reflection run reported 172/170/2. The two
"failures" were `TargetParameterCountException` — **my runner's bug, not the tests'**. Two methods
carry `[Test]` *and* `[TestCase]` rows on a 6-parameter signature, and real NUnit does not generate a
parameterless invocation for a `[Test]` on a parameterised method. Corrected to that semantic, the
count reconciles exactly with the previous baseline.

### 30.11 Performance

Measured in isolation, because in the full pipeline ONNX inference dominates by four orders of
magnitude and an end-to-end number cannot resolve this gate at all. This measurement is pure Python
with no model in the loop, so it is interpreter- and accelerator-independent. 775 real observations
reconstructed exactly from the recorded run, × 200 reps, identical input in both arms:

```text
PATH OFF   median 1.407 us/frame   p95 2.514   max 3.209
PATH ON    median 1.611 us/frame   p95 2.922   max 3.947
DELTA      +0.204 us/frame   = +14.5 % of the ownership layer
                             = +0.00061 % of a 30 fps frame budget
memory     three scalars per instance. No buffer, no history, no per-frame allocation.
```

End-to-end, quoted so it is not omitted and explicitly **not** carrying the claim: 12.63 fps
(PATH OFF) vs 13.69 fps (PATH ON) on the same replay. PATH ON measuring *faster* is the clearest
possible demonstration that this figure resolves nothing about the gate — it emits 404 frames instead
of 500, so it does less downstream work. The harness also runs background subtraction per frame,
which production does not. Use the isolated number above; this one is context, not evidence.

> **Interpreter trap, recorded because it cost real time and would cost anyone else the same.**
> Every harness here must be run with the **sidecar's own venv** — `.venv\Scripts\python.exe` —
> and not with the system Python. They carry different `onnxruntime` builds:
>
> ```text
> .venv\Scripts\python.exe        onnxruntime 1.23.0   ['DmlExecutionProvider', 'CPUExecutionProvider']
> C:\Program Files\Python310      onnxruntime 1.23.2   ['TensorrtExecutionProvider',
>                                                        'CUDAExecutionProvider', 'CPUExecutionProvider']
> ```
>
> `rtmw3d_pose.RTMW3D` asks for `DmlExecutionProvider` and falls back **silently** to CPU when it is
> absent. Under the system Python that is exactly what happens — and the CUDA provider there cannot
> load either (`cublasLt64_12.dll` missing; it needs CUDA 12 + cuDNN 9), so there is no GPU path at
> all and inference runs at **183.7 ms/frame, 5.4 fps**. This is *not* a production defect: the
> sidecar, `sidecar_supervisor.py` and `f21_live_protocol.py` all launch `.venv\Scripts\python.exe`
> explicitly, where DirectML is present. It is a defect in how a harness gets run by hand.
>
> The result that matters is unaffected and was re-verified rather than argued about: the whole
> `123.webm` replay re-run under the venv reproduces **102 / 40 / 2** exactly. Ownership decisions are
> deterministic; only the clock differs.

### 30.12 Remaining live risks

1. **The gate refuses a returning owner.** This is the fix working as designed, not a bug, but its
   *rate* in a real room is unknown and is the single most important thing tomorrow's session should
   price. Offline it cost 1.6 s of a legitimate owner on one clip. The `WALK_IN_OWNER` /
   `WALK_IN_IMPOSTOR` protocol phases exist to measure it directly.
2. **The replay has no hip depth.** The gate is exercised laterally only. Real stereo depth should
   make it more selective, but "should" is not "measured".
3. **`123.webm` is one clip.** One hand-off, one returning owner, two people. It is a real-footage
   existence proof, not an incidence rate, and is not offered as one.
4. `SWITCH_MARGIN_M = 0.35` still carries ~11 % headroom (§26) and is now load-bearing for two
   things rather than one — the gate radius and chain continuity. They move in opposite safety
   directions, which is why reusing it is defensible, but it makes a live re-check of that value
   more valuable, not less.

### 30.13 Offline replay soak — and a harness bug that made every previous soak weaker than it looked

Two runs, because the first one exposed a problem with the instrument rather than with the code.

**Run 1 — stability, all three clips, 17 minutes.**

```text
OFFLINE REPLAY SOAK   17.0 min   21 clip cycles   31,851 frames   31.23 fps
EXCEPTIONS                        0
RSS after warm-up / end / peak    895.9 / 743.9 / 901.8 MB   (no growth; ends BELOW warm-up)
TEMP_LOST episodes                80, all reacquired
validator holds / recoveries      2,770 / 2,770    unrecovered: none
```

Then the number that did not add up: **0 path-gate rejections, and `candidate rejection reasons:
none`** — across 21 replays of a clip that demonstrably contains a person swap. A gate that never
fires on the clip it was built for is either broken or not being reached.

It was not being reached. `f2x_replay_soak.py` lifts every position into **metres** through the
pinhole backprojection, then set

```python
own.cfg.switch_margin_m = diag * 0.064      # a PIXEL figure
```

— a margin of roughly **141 metres**. Nothing could ever fail `_matches_owner`. This is a
pre-existing harness defect, not a production one (production reads `--ownership-switch-margin`,
default 0.35 m, and the replay harnesses use it correctly), but it matters for how every previous
soak should be read: **the soak has only ever exercised F-21's plumbing, never its discrimination**,
and its clean "0 releases, every temp-loss reacquired" line looked like evidence of correctness when
it was an artefact of a gate that could not close. The same applies to §28's earlier soak.

Fixed to the production constants (`switch_margin_m=0.35`, `scale_margin_ratio=0.45` — the lift is
metric, so they apply directly with no conversion to get wrong).

**Run 2 — the gate itself, `123.webm` only, ownership reset per cycle so each cycle is a faithful
repeat of the hand-off rather than inheriting an owner position from a different clip.**

```text
OFFLINE REPLAY SOAK   8.0 min   21 cycles   15,662 frames   32.62 fps   EXCEPTIONS 0
rejections            position_jump 2,649    path_walked_in 600
ownership             41 acquisitions, 60 TEMP_LOST, 20 reacquired, 20 RELEASED, 20 TARGET_SWITCH
memory                peak 897.1 MB, ends below warm-up - no growth across 600 gate firings
validator             749 holds / 749 recoveries, unrecovered: none
```

**600 path-gate rejections and 20 declared hand-overs across 21 repeats**, with zero exceptions, no
unrecovered holds, no memory growth and no wedged state machine. That is the soak evidence for the
change; Run 1 remains the soak evidence for general stability. Reported as two runs rather than one
average, because they measure different things.

What neither run measures is **wrong-person frames** — the soak has no ground-truth labels, and
quoting a soak figure for that would be inventing evidence. That number lives in §30.8.

## 31. Live-test tooling, fixed before the session (brief §14/§15)

### 31.1 Dead producer detection

The second live attempt lost the DepthAI device part-way through and `f21_live_protocol.py` marched
on through every remaining phase, writing a timeline that reads like a completed protocol. Now
`proc.poll()` runs during every wait and between every phase, and a death aborts with the evidence:

```text
LIVE TEST ABORTED: SIDECAR EXITED
  return code      : <rc>
  died during phase: <phase>
  wall clock       : <timestamp>
  last F-21 state  : <last line of the sidecar's own target_events.jsonl>
  phases completed : N of M
  Everything after this point is UNOBSERVED. Do not score it.
```

**Tested, not merely implemented** — `f21_live_protocol_abort_test.py`, **9/9 PASS**. A stub producer
idles until it is killed mid-phase; the test then asserts the protocol returns non-zero, writes a
`LIVE_TEST_ABORTED` record carrying the return code, the phase (`P_TWO`), a wall clock, the last
ownership state *read back from the sidecar's own `target_events.jsonl`*, and the list of phases
genuinely completed — and, the assertion that actually matters, that **the phases after the death
were never run**. No camera and no model: subprocess-death detection is agnostic to what the
subprocess was doing, so this proves nothing about DepthAI failure modes and does not claim to.

### 31.2 Supervisor passthrough

`sidecar_supervisor.py` gained exactly two forwarded flags, `--show` and `--cue-file`, so the next
live session can run under the F-20B watchdog and a device crash becomes a restart instead of the end
of the experiment. Explicitly *not* a generic `--extra-args` passthrough, which was the smaller change
and the wrong one: a supervisor that forwards arbitrary strings can also forward `--no-ownership`, and
then the thing under test is not the thing that was configured. With neither flag set the command line
is byte-identical to before, which is why F-20B still reads 35/35.

`f21_live_protocol.py --supervised` uses it, and the two modes have deliberately **different abort
conditions**: direct mode treats a sidecar exit as fatal; supervised mode expects it and restarts,
treats only a *supervisor* exit as fatal, and reports every intervention at the end — because a phase
that straddled a restart was observed across an ownership epoch reset and must not be read as
continuous.

Two new protocol phases target this pass's finding directly: `WALK_IN_IMPOSTOR` and `WALK_IN_OWNER`.

### 31.3 Integration, verified offline against the real device with an empty room

`f21_supervisor_integration.py`, **11/11 PASS**. Not a camera-recovery test (the sidecar is killed
with `taskkill`, a clean process death, not a device fault) and not a two-person test (the room is
empty on purpose).

```text
1   the supervisor started a sidecar                                     pid observed
2a  the supervisor LOGGED the forwarded flags                            supervisor.log
2b  the RUNNING sidecar process really carries them                      read from Win32_Process argv
3   the preview window exists                                            enumerated from user32,
                                                                         title "whole-body OAK sidecar"
4a  the protocol's cue is parsed by the sidecar's OWN reader             see §31.4
4b  the cue renders a real banner through the real _draw_cue             72 px, imaged
5a  the sidecar writes its ownership log where the protocol looks
5b  the reader handles the empty-room case without raising
6a  the supervisor noticed the sidecar exiting                           SidecarExit
6b  it restarted with a NEW pid                                          7072 -> 13904
7   never more than one sidecar process tree alive                       peak = 1
```

Two of these initially "failed" and neither was a product defect — both were **this test being
wrong**, and they are recorded because a test that is wrong in the optimistic direction is the
dangerous kind:

* check 5 asserted that an ownership event would appear. With an empty room F-21 correctly acquires
  nothing and correctly writes nothing, so the assertion was demanding that an empty room produce a
  target. Replaced with what is actually testable here — the plumbing (the log appears where the
  protocol reads it) and the empty case (the reader returns "nothing yet" instead of raising).
* check 6a looked for an event named `SidecarExited`. The supervisor emits `SidecarExit`. Guessing a
  string instead of reading `sidecar_supervisor.py`'s own `self.event()` calls reported a supervisor
  failure that had not happened.

### 31.4 A production defect the integration test caught: `--cue-file` never worked

Check 4a failed for a real reason, and this one would have cost tomorrow's session.

`wholebody_udp_sender._read_cue()` reads the phase cue with `io.open(...)`. **The module never
imported `io`.** `io.open` therefore raised `NameError` on every call, and the function's own
defensive wrapper —

```python
    except Exception:
        return None
```

— turned that into a silent `None`, every frame, forever. `_read_cue` is the *only* place in
`wholebody_udp_sender.py` that uses `io`, so nothing else ever surfaced it.

The consequence: **the on-screen cue banner has never drawn.** That banner is the entire point of
REV 2 of `f21_live_protocol.py` — added because the first live F-21 attempt was inconclusive when the
operator could not read printed console instructions while physically coordinating two people at the
camera. The fix was written, shipped, and documented as fixed; it has been dead the whole time, and
tomorrow's session would have hit the identical failure mode with a report claiming it was solved.

Fix: `import io`. One line, no behaviour change anywhere else. Verified end-to-end rather than
assumed — the real `write_cue` → real `_read_cue` → real `_draw_cue` chain now returns the cue and
renders a 72–94 px banner, imaged at
`oak_v4_evidence/f21/supervisor_integration/cue_render.png`:

```text
WALK_IN_IMPOSTOR                                    17s
Owner hides. OTHER person walks slowly to
owner's exact spot
```

Two things worth taking from this beyond the one-line fix:

1. **It was found by testing the thing, not by reading the code.** `--cue-file` was exercised in a
   previous live attempt without anyone noticing the banner was absent, because an absent banner
   looks like "the operator missed it" rather than a defect.
2. **A bare `except Exception: return None` converted a programming error into a silent feature
   outage.** The defensive handler is correct in intent — a partial read of a file being atomically
   replaced must not crash the camera loop — but it is indiscriminate enough to hide a `NameError`
   forever. Not changed today (it is the live-critical path and the session is tomorrow), and flagged
   here instead.

---

## 32. THE LIVE INSTRUMENT, REBUILT AND VERIFIED (2026-09-15) — still not run with two people

The session this section was written for did NOT happen: no second person was available. What was
done instead is the thing that had blocked the previous two attempts — the instrument itself. Ten
defects were found and fixed BEFORE any human time was spent, which is the only reason to write this
down: each of them would have damaged or destroyed a session that two people had turned up for.

### 32.1 The ten

| # | Defect | Evidence | Fix |
|---|---|---|---|
| 1 | cue banner ~4x too small at the camera | 13 px cap-height = 4.4-5.6 arcmin @ 2.5 m vs ~5 arcmin acuity limit | cue panel, 60 px = 20.5-25.7 arcmin (ADR-058) |
| 2 | `WALK_IN_OWNER` measured an IMPOSTOR walk-in | 18 s phase vs 4.0 s `RELEASE_TIMEOUT_S` ends with B as owner; the next cue then says "Owner returns" | fixed A/B labels, self-anchoring reps |
| 3 | nothing recorded the preview | no `VideoWriter`/`imwrite` anywhere in the sidecar | external ffmpeg capture |
| 4 | recording unplayable if interrupted | 113 MB, "moov atom not found" | Matroska (survives truncation) |
| 5 | ffmpeg stderr sent to DEVNULL | a 15 s recording with no way to know why | logged beside the recording |
| 6 | libx264 CPU-starved by inference | **70.2 s captured of an 86 s session**, speed 0.16 -> 0.97x | h264_nvenc -> **86.7/86.9 s at 0.999x** |
| 7 | `cue.json` write race KILLED the protocol | `PermissionError [WinError 5]` on `os.replace` mid-run | retry-then-skip, skips counted |
| 8 | composite cost 11 % of a 30 fps budget | 4.29 ms vs 0.62 ms baseline | cached + preallocated -> **0.38 ms** |
| 9 | 1900 px window opened partly off-screen | feed half cut off in the recording | placed, and pinned topmost during capture |
| 10 | composites could be built from the WRONG WINDOW | gdigrab records the desktop; twice it recorded the chat window and rendered happily | game-view guard: verified 0 % refuse / 100 % pass |

`#7` is the one that mattered most. The sidecar re-reads `cue.json` on every preview frame (~30 Hz)
while the protocol writes it at 10 Hz; on Windows a rename over a file another process holds open
fails. Over a two-hour session that is a certainty, and it took the whole run down.

### 32.2 What the protocol now measures, and the case that was missing

`f21_walkin_protocol.py` replaces one-rep-each-at-the-end with a repeated matched-pair experiment.
Subject-facing cues use fixed labels **A** and **B** for the whole session and never the word
"owner" — a ROLE that changes hands mid-protocol, which is what defect 2 was.

```text
OWNER_WALKBACK    A leaves past the margin and returns. ADR-057 says the gate CANNOT distinguish
                  this and refuses it. Its rate is ~1.0 BY CONSTRUCTION and proves nothing; this
                  case is scored for its COST (blackout duration).
IMPOSTOR_WALKIN   B walks to A's spot. Refusal + declared TARGET_SWITCH is correct; a silent
                  reacquire in the same epoch is the S27 failure.
OWNER_OCCLUDED    A stays ON the cross; B passes between A and the camera.      <- NEW
OWNER_TURN        A stays ON the cross; A turns away or crouches.               <- NEW
```

**The last two are the real false-hold rate and no offline clip has ever exercised them.** In an
installation an owner almost never walks fully outside 0.35 m and back — they are briefly occluded,
turn, or are blocked. Those are chain breaks that land INSIDE the margin, and ADR-057 is explicit
that such a break CLEARS the flag. So the gate MUST NOT fire; if it does, that is a false hold in
the damaging sense, against the most common real-world loss pattern.

Every rep re-anchors and VERIFIES the anchor from the sidecar's own log before the manoeuvre. A rep
whose anchor never establishes is marked INVALID and excluded rather than quietly counted.

### 32.3 Scoring

`f21_walkin_score.py` aligns the protocol timeline and `target_events.jsonl` on one wall clock (both
are stamped by `time.time()` by their own writer) and reports holds, hold duration, releases,
declared switches and blackout per rep, with **Wilson 95 % intervals** — because "0 of 6" is not a
measured zero. `f21_walkin_score_test.py` verifies it against synthetic sessions whose answers are
known by construction: **22/22**, including that a `position_jump` rejection is not counted as a
path hold, that events outside the manoeuvre window do not leak in, and that an ANCHOR-phase
rejection is not scored as the manoeuvre.

### 32.4 A live finding that affects how the session must be set up

Reviewing a recorded preview, the tracker was observed **locked onto an empty office chair** at
`conf = 0.38` against a `min_confidence` floor of `0.30`, while a person stood clearly in frame.
**An anchor that lands on furniture marks the rep VALID and makes every classification in it
meaningless.** Clear chair-like objects from the capture volume before running. No threshold was
changed in response; this is recorded, not fixed.

### 32.5 Regression status

```text
test_target_ownership.py       61/61     f21_adversarial.py        101/101
test_pose_validation.py        22/22     f21_live_protocol_abort_test.py  9/9
f20b_deployment_verify.py      35/35     f21_walkin_score_test.py  22/22 (new)
```

Production changes are minimal and opt-in: `--cue-panel` (preview-only, default path
byte-identical), the same flag forwarded by the supervisor, and the `write_cue` race fix in BOTH
protocols. `target_ownership.py` is **untouched**.

### 32.6 STILL NOT DONE

**The two-person session.** The instrument is ready and verified; it has never been run with two
people. F-21 stays **CONDITIONAL** and the false-hold rate remains the open number.

## 33. FIRST LIVE ATTEMPT WITH TWO PEOPLE (2026-09-16) — capture unusable, two scorer defects found

The session ran end to end. Supervisor, cue panel, `h264_nvenc` recorder, phase driver and scorer all
worked; 4/4 reps completed. **The capture is not usable and no F-21 number comes out of it.**

### 33.1 What the scorer said first, and why it was wrong

```text
rep 2  IMPOSTOR_WALKIN   SILENT_WRONG_PERSON      path 0   rel 1   sw 1   reacq 1
```

`SILENT_WRONG_PERSON` is the most serious verdict this suite can return — it is the §27 defect, the
one §30 exists to prevent. It was a **false alarm**, and it was visible on its own line: the rep is
marked *silent* while reporting `sw = 1`, a **declared** switch. Both cannot be true.

The classifier tested only `not fired and reacquires > 0`. It never asked whether a release and a
`TARGET_SWITCH` had already announced the hand-over — which is precisely the thing "silent" is
defined to exclude. A released, switched, correctly-declared hand-over was being reported as the
pipeline's worst failure.

It survived the existing 22/22 because the one test covering a declared switch **also** fired the
path gate, so no test ever isolated *announcement* from *refusal*.

```text
FIXED   declared = releases > 0 and switches > 0 ; a declared hand-over is never SILENT.
        IMPOSTOR_WALKIN -> DECLARED_SWITCH ; OWNER_WALKBACK -> RELEASED_THEN_REACQUIRED (new).
        Announcement is the safety property. Whether the PATH gate specifically refused is a
        separate question and is still reported separately.
GUARDED 5 new tests, including the asymmetry that a release WITHOUT a switch is still SILENT.
```

### 33.2 The second defect: an anchor that establishes and then collapses

With the classifier fixed the capture read clean — and it was not. Underneath, ownership had churned
through **six epochs in 71 s**, with 5 releases and thousands of `position_jump` rejections. Every
rep was still scored, because validity was `anchor is not None`: it asked whether a lock was **ever
seen**, never whether it **survived** to the manoeuvre.

```text
FIXED   an anchor must HOLD. Over the closing window of the ANCHOR phase the machine must emit no
        TARGET_TEMP_LOST, TARGET_RELEASED or TARGET_SWITCH. Uses F-21's OWN verdict - no new
        threshold. Enforced live (f21_walkin_protocol.anchor_held, with operator-facing diagnosis)
        AND re-derived independently in the scorer from the events, so older captures are judged by
        the same standard and the scorer does not depend on the recorder having been correct.
        HOLD_S = 5.0 s is DERIVED: it must exceed RELEASE_TIMEOUT_S (4.0 s) or a complete
        lose-release-reacquire cycle could pass through the window leaving no transition inside it.
GUARDED tests for both directions, including "excluded even though the timeline said valid=True".
        f21_walkin_score_test.py  22/22 -> 32/32.
```

Re-scored with both guards, the same capture now reports what it actually is:

```text
4 reps recorded, 1 scoreable, 3 excluded (0 never anchored, 3 anchor collapsed)
  THE HEADLINE NUMBER   NOT TESTED - no scoreable OWNER_OCCLUDED / OWNER_TURN reps
  SAFETY                NOT TESTED - no scoreable IMPOSTOR_WALKIN reps
```

That is the correct output. The suite went from confidently reporting a 100 % silent-wrong-person
rate to reporting **NOT TESTED**, which is what the evidence supports.

### 33.3 Why the capture failed — measured

```text
hip_z    min 0.88   p10 1.43   median 1.52   p90 2.98   max 3.54 m     spread p10-p90 = 1.55 m
frame-to-frame candidate jump   median 0.010 m   p90 0.092 m   max 1.762 m
consecutive frames jumping further than the 0.35 m margin:  1.7 %
```

The subject's reported depth swings by over 1.5 m while they stand still. At ~30 fps, 1.7 % is
roughly one out-of-margin frame every two seconds, and a single one drops `LOCKED ->
TEMPORARILY_LOST` needing five clean confirms to recover. It never gets them.

From the recording, A is standing close to a **featureless white wall under a bright linear ceiling
light** — the classic stereo failure case. The `p90 = 2.98 m` tail is consistent with wall depth
leaking into the hip sample when the subject's own disparity drops out. No tape cross is visible on
the floor.

**Setup requirements for the next attempt**, added to §32.4's furniture warning:

```text
- subject WELL OFF the back wall (>= 1 m of separation)
- taped cross at 2.0-2.5 m, the documented envelope
- not directly under the light strip where practical
```

### 33.4 What this attempt did NOT show

**Nothing about the §30 gate, in either direction.** It logged **zero** `path_walked_in` firings —
which is ADR-057 behaving exactly as specified, not evidence about it: these were isolated spikes
that break the chain and land back inside the margin, and ADR-057 says such a break clears the flag.
The gate was never reached. The false-hold rate remains **the open number**, unchanged.

## 34. THE LIVE SESSION FOUND A SECOND SILENT HAND-OFF — and §30's gate cannot see it (2026-09-16)

8 reps, 2 per case, on the real OAK-D with two people. **8/8 anchors held, 8/8 scoreable** — the §33
guards did their job. The headline metric came out clean:

```text
false holds (path gate)   0/4 = 0.0%  [95% CI 0.0-49.0%]
silent wrong-person       0/2 = 0.0%  (as scored)
declared hand-overs       2/2 = 100%
```

**All three of those numbers are true, and the session still found a silent wrong-person emission.**
It happened in a case the metric does not inspect.

### 34.1 What actually happened in rep 7 (OWNER_OCCLUDED)

A stood on the cross. B walked between A and the camera. The observation stream migrated onto B and
back, **and the sidecar emitted the whole way**, inside one ownership epoch, with nothing logged.

Read off the recorded preview at 0.5 s intervals (`session_20260915_171612.mkv`, video t=274.5-278.0).
`sent` is the datagram counter and `hip` is `float(mid_hip[2])` — the depth of the pose **actually
emitted**, printed only on the emitting path (the withholding path prints `F21 <state>` instead, which
is how rep 3 below is distinguishable at a glance):

```text
sent=5740  hip 1.61m   F21 LOCKED     <- A
sent=5753  hip 1.61m   F21 LOCKED     <- A
sent=5768  hip 1.34m   F21 LOCKED        migrating
sent=5781  hip 1.16m   F21 LOCKED     <- B          skeleton visibly on B
sent=5796  hip 1.19m   F21 LOCKED     <- B
sent=5809  hip 1.45m   F21 LOCKED        migrating back
sent=5823  hip 1.54m   F21 LOCKED
sent=5837  hip 1.58m   F21 LOCKED     <- A
```

97 datagrams sent across that window. `target_id` stayed **13**, `switches` stayed **12**, and the
ownership log recorded **zero events of any kind** for the entire manoeuvre. Evidence in
`oak_v4_evidence/f21/walkin/s34_evidence/`.

**This is the §27 defect, live.** Not an offline replay, not a synthetic scenario — the production
sidecar, the real camera, two real people, emitting the wrong human under the owner's identity with
nothing in any log to say so.

### 34.2 Why ADR-057's gate did not fire, and why that is not a bug in the gate

ADR-057 guards **reacquisition**. It runs in the `TEMPORARILY_LOST` / `REACQUIRING` branch. In rep 7
the machine **never left `LOCKED`**, so the gate was never reached. It is working exactly as
specified; the specification simply does not cover this path.

The mechanism is in the `LOCKED` branch (`target_ownership.py`):

```python
if ok:
    self.owner_pos = obs.pos          # <- the reference WALKS WITH the observation
```

`SWITCH_MARGIN_M` is tested against the *previous accepted frame*, not against the position the epoch
locked onto. So the reference is only frozen once a loss has already been declared. While `LOCKED`,
a migration that moves less than 0.35 m per frame is accepted step by step, and the owner reference
slides from one human to another without a single frame ever failing the test.

Measured here: 1.61 -> 1.34 -> 1.16 m across one second. **Per-frame that is a few millimetres.** The
margin was designed to catch a JUMP, and a jump is what every previous test gave it — §30's own
offline evidence is built on `123.webm`, where the observation *teleports* 303-687 px between bodies.
A slow drift is a different failure and nothing in the module looks for it.

### 34.3 The same manoeuvre, twice, with opposite outcomes

```text
rep 3   B crosses in front   -> F21 correctly REFUSES B, goes TEMPORARILY_LOST, and pays
                                4.19 s of blackout + a release + a declared switch, while A is
                                standing in plain view on the cross the entire time
rep 7   B crosses in front   -> F21 does not refuse anything. It emits B as A, silently.
```

Both are failures, in opposite directions, from the identical instruction. Neither is captured by
"false holds = 0/4".

### 34.4 A third scorer defect: the metric is blind in the case where the failure occurred

`SILENT_WRONG_PERSON` is only ever evaluated for `IMPOSTOR_WALKIN`. For `OWNER_OCCLUDED` and
`OWNER_TURN` the scorer asks exactly one question — *did the path gate fire?* — and reports
`OK_NO_HOLD` if it did not. So rep 7 scored **the cleanest possible outcome** for the worst possible
event.

That is the same error as §33.1 in a new place: an outcome was being inferred from the absence of one
specific signal rather than from what was emitted. **Wrong-person emission must be checked in every
case, not only where it was expected.**

**FIXED (2026-09-16).** `f21_walkin_score.migration()` now measures, for every rep, how far the
**emitted** mid-hip travelled from the position the epoch anchored on, and whether ownership
announced anything while it did. It reads the absolute hip out of the UDP wire (`xyz`, millimetres)
recorded by `f24_wire_probe`, so it is measured **per frame** instead of read off a preview at 2 Hz.
`f21_walkin_protocol.py` now records that wire by default (`--no-wire` to opt out), inserting the
probe between the sidecar on port 8900 and Unity on 8899 — the forward is byte-identical.

What it claims and does not: it detects **migration, not identity**. It cannot know who a pose
belongs to. Where the owner was told to stand still a silent excursion past `SWITCH_MARGIN_M` is
strong evidence the emitted body changed; in `OWNER_WALKBACK` the owner is *instructed* to leave, so
drift there is reported as `EXPECTED` with no verdict attached. **With no `--wire` the check reports
`NOT MEASURED`, never a pass** — the failure this whole section is about was originally hidden by
exactly that kind of silent default.

Pinned by tests built from the real rep-7 trace (1.61 → 1.16 → 1.58 m):
`f21_walkin_score_test.py` **32/32 → 39/39**.

### 34.4b Confirmed again, with the wire on — and the first measured rate

A third 8-rep run, with `f24_wire_probe` recording the emitted UDP wire. **8/8 anchors held, 8/8
scoreable.** The gate-based metric again reported `false holds 0/4` and `OK_NO_HOLD` for rep 3. The
new per-frame check found the failure in that same rep, independently:

```text
rep 2  IMPOSTOR_WALKIN  drift 0.69 m   33/466 frames beyond margin   DECLARED
rep 3  OWNER_OCCLUDED   drift 0.63 m   71/515 frames beyond margin   SILENT_MIGRATION   <--
rep 4  OWNER_TURN       drift 0.20 m    0/508                        NONE
rep 6  IMPOSTOR_WALKIN  drift 0.66 m   31/577 frames beyond margin   DECLARED
rep 7  OWNER_OCCLUDED   drift 0.56 m   19/395 frames beyond margin   DECLARED
rep 8  OWNER_TURN       drift 0.21 m    0/506                        NONE
rep 1  OWNER_WALKBACK   drift 0.82 m   (owner INSTRUCTED to leave - no verdict)
rep 5  OWNER_WALKBACK   drift 0.77 m   (owner INSTRUCTED to leave - no verdict)

silent migration : 1/6 = 16.7 %  [95 % CI 3.0-56.4 %]
```

Rep 3 verified by eye at its peak-drift frame (`s34_evidence/run3_rep3_peakdrift_t122.png`): **A is
standing on the taped cross, stationary as instructed, and the skeleton is entirely on B**, with
`F21 LOCKED`, `owner=8`, `age=16.5 s`, `hip 1.26 m` against a 1.63 m anchor. Nothing logged.

**`OWNER_OCCLUDED` is now 2 failures out of 2, in opposite directions** — rep 3 silent, rep 7
declared but costing a 4.18 s blackout. Neither is "handled", and the headline `0/4` sees neither.

### 34.4c The drift distribution ADR-061 asked for

The reason to put the drift on the wire was to get a number a budget could be set from. First data:

```text
owner turning in place        0.20, 0.21 m     0 frames beyond the 0.35 m margin
migration onto another body   0.56 - 0.69 m
legitimate walk-out           0.77, 0.82 m     (a loss by design; not a budget case)
```

There is a clean gap between an owner turning on the spot and a migration. **It is n=2 per condition
and it does not cover the case that matters most for a budget — an owner who WALKS while staying the
owner**, which no rep in this protocol produces. Do not set a budget off this table alone; it is the
start of the distribution, not the distribution.

### 34.5 Status

```text
F-21 remains CONDITIONAL. This is now a HARDER no than before the session:
  - §30 closed the reacquisition route to a silent hand-off, and that fix stands - 0/2 on
    IMPOSTOR_WALKIN, 2/2 declared, and the offline 123.webm result is unaffected.
  - The LOCKED-branch drift route is OPEN, measured, and imaged.
  - The occlusion case costs either 4.19 s of blackout or a silent wrong person, depending on
    which way the detector happens to fall.
DO NOT ship single-person ownership as a safety property on this evidence.
```

---

```text
F-21 VERDICT:
CONDITIONAL - OFFLINE FIX VERIFIED, LIVE ACCEPTANCE PENDING. The ownership mechanism is proven
correct across 28 deterministic scenarios (101/101), 61/61 unit assertions and real crowded footage.
Two real defects were found and fixed: the reacquisition dead zone (SS25, ADR-054) and the SILENT
WRONG-PERSON HAND-OFF (SS27 reproduced it, SS30 fixes it, ADR-057). On the real two-person clip that
exhibited it, wrong-person frames go 38 -> 0 and the hand-over becomes DECLARED rather than silent.
This is not PASS because no live two-person evidence exists yet - every result here is replayed
footage with no hip depth, and the fix's own cost (a returning owner is refused, 96 frames on that
clip) has an unknown rate in a real room.

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

WRONG-PERSON FRAMES (the metric that replaced TARGET_SWITCH as the headline):
TARGET_SWITCH counts DECLARED hand-overs only and read zero straight through a real person change,
which is why it is no longer the headline number. Measured against offline ground-truth labels on
123.webm, whole production chain, production config:
    ownership off       102 wrong-person frames
    F-21 as shipped      40
    F-21 + SS30 gate      2   <- and both are a verified LABELLER artefact at f14-f15 during the
                                 subject's entry, present identically in all three arms. The
                                 hand-off segment itself is 0.
Deterministic suite: 0 wrong-person frames outside the deliberately unseparable identical-build
crossing, 0 false releases, 0 false reacquisitions across 4,692 frames.

AUTOMATED TESTS:
61/61 unit assertions, 101/101 deterministic adversarial (28 scenarios incl. P1-P11 path
consistency), 5/5 real-hardware restart-interaction, 170/170 Unity EditMode, 35/35 F-20B deployment,
video.webm and 456.webm bit-identical with the gate on and off, --no-ownership regression PASS.

LIVE TEST:
Two attempts, neither completed cleanly (test-script UX defect, since fixed; then a camera-hardware
crash). No live multi-person evidence yet exists.

REGRESSIONS:
None. No Runtime/ change; --no-ownership reproduces pre-F-21 behaviour exactly.

NEXT ACTION:
Run the live two-person session, under supervision, with the on-screen cue banner:

    python f21_live_protocol.py --supervised

The tooling that blocked the last two attempts is fixed (SS31): a dead producer now aborts instead of
scoring phases against nothing, and the supervisor forwards --show/--cue-file so a device crash is a
restart rather than the end of the experiment. Two protocol phases target this pass's finding
directly - WALK_IN_IMPOSTOR and WALK_IN_OWNER - and what they must price is the SS30 trade: the gate
refuses a returning owner as well as an intruder, and only a real room can say how often that costs
something. Do not upgrade this verdict to PASS before that runs.
```
