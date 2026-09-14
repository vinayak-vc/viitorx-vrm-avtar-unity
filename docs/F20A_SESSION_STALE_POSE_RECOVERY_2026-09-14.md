# F-20A — Session / Stale-Pose Recovery and Reconnect Safety

```text
VERDICT: PASS
```

Implemented and validated live on 2026-09-14 against the OAK-D-PRO-W-97 (`14442C10F143D3D200`) in
portrait, Unity Editor in Play mode, with a subject in frame. Every acceptance criterion in §20 is
met. Two limitations are recorded in §14 rather than hidden; neither blocks the fix.

---

## 1. Root cause

The brief attributes the F-19 failure to P1-3 rejecting a restarted stream. That is half of it. The
live session found **two independent defects**, and fixing only the first would still have left a
public mirror showing a stale human pose.

**Defect 1 — the stream never recovers.** `wholebody_udp_sender.py` restarts `seq` at 1 on every
process start. `PoseBuffer.Push` rejects `seq < newestSeq` as out-of-order — correctly, for a single
session — so after a restart **every** packet is rejected, permanently. Measured in F-19:

```text
RejectedOutOfOrder = 4948      LastSeqA = LastSeqB = 22678
IsRunning = true               ReceivedCount climbing      ParseErrors = 0
packetAgeMs = 208417           (the avatar was rendering a 3.5-minute-old pose)
```

**Defect 2 — the staleness response IS the freeze.** `AppBootstrap.IsPoseStale` already existed and
already fired: `poseStaleSeconds = 0.5`, and once the buffered pose stops advancing it returns true
within half a second. Its response is to **stop calling `kalidokitControlRig.Apply`**. A control rig
that stops being written keeps its last values, so "detecting staleness" and "freezing the avatar on
the last human pose" were the same code path. Even with defect 1 fixed, any real outage would have
left a frozen person on screen indefinitely.

One precision that narrowed the fix usefully: the pose buffer is pushed with **`recvEpoch` — Unity's
own clock** — not the sender's timestamp. The timestamp axis is therefore already monotonic across a
producer restart, so **only** the sequence rule blocked recovery. No timestamp handling needed
changing.

---

## 2. Architecture

Three pieces, deliberately small:

| piece | where | what it does |
|---|---|---|
| `sid` field | sidecar payload | a per-**process** identifier, so a restart is *stated* rather than inferred |
| `TrackingStreamHealth` | new, `Runtime/Tracking/OakD/` | session classification + stale state machine + counters |
| neutral-pose failsafe | `AppBootstrap` + driver | a dead stream releases the avatar to rest instead of freezing it |

`TrackingStreamHealth` takes every clock value as a parameter and touches no Unity API, so it is
deterministic and unit-testable without a socket, a clock or a frame. That is why §19's tests need no
sleeps and cannot be timing-flaky.

**P1-3 was not rewritten.** `PoseBuffer` is unchanged: its duplicate, out-of-order and
backward-timestamp rules stand exactly as they were. The only new interaction is that the provider
calls the existing `Clear()` at a session boundary, before the push.

---

## 3. Session identification

The sidecar generates `SESSION_ID = uuid.uuid4().hex[:12]` once per process and stamps it on every
datagram. It is logged at startup:

```text
[wb]   producer session id = bbcd38bee236  (F-20A: new on every process start)
```

**A `seq` reset is deliberately not used as the identifier.** It is exactly the signature a
duplicated sender, a wrapped counter or a replayed capture would also produce; treating it as proof
of a restart would be equivalent to disabling ordering, which §1 forbids. An explicit per-process id
is proof; a sequence reset is not.

**Compatibility.** Adding a field is backward compatible — consumers that do not read `sid` are
unaffected, and the payload grows ~22 bytes in a ~6 KB datagram. No versioning or handshake was
needed, so none was added.

**Legacy senders.** The deprecated body-only BlazePose sender has no `sid`. For those there is a
bounded last-resort rule: a session break is declared only on **both** a sustained run of ≥8
backwards sequence numbers **and** ≥0.25 s since the last accepted pose. Either alone is insufficient
— a duplicated or reordered packet gives regressions without a quiet period; a stall gives a quiet
period without regressions. Two tests pin both halves.

**Verdicts** are `FirstSession`, `SameSession`, `NewSession` and `OldSession`. The last one exists
because of a bug this task's own §14 test found — see §10.

---

## 4. Stale watchdog

Thresholds are **derived from measured timing**, not chosen for roundness. From F-19's headless
portrait log (22,678 frames): send rate **29.9 fps** (one pose per 33.4 ms), capture→send p50
**31.8 ms** / p95 34.9 / **max 84.1 ms**, camera→host frame age p50 **17.3 ms**, P1-3 presentation
delay **40 ms**.

| threshold | value | why |
|---|--:|---|
| `STALE_WARN` | **150 ms** | ~4.5 inter-pose intervals, and above the worst single-frame pipeline cost actually observed (84.1 ms) plus the 40 ms presentation delay. Below this nothing is reported. |
| `STALE_HOLD` | **500 ms** | ~15 intervals. Set **equal to the existing `poseStaleSeconds`** so the system keeps one definition of "stale enough to stop applying" rather than acquiring a second, conflicting one. |
| `STALE_FAILSAFE` | **2000 ms** | ~60 intervals. Long enough that a USB re-enumeration or GC pause does not flip a visitor to the neutral pose; short enough that nobody watches a frozen human pose longer than that. F-19 observed **208.4 seconds**. |

**States** (§16), all transitions logged:

```text
NoStream -> Connecting -> Live -> StaleHold -> StaleFailsafe -> Reconnecting -> Recovering -> Live
```

`Reconnecting` is distinguished from `StaleHold` by whether datagrams are still arriving — that is
precisely the F-19 shape (packets flowing, nothing accepted) and it deserves its own name.

Verbatim from the Unity console during the USB and injection runs (not reconstructed):

```text
TrackingState NoStream -> Live         (sid=inj219482471 acceptedSeq=25 receivedSeq=25 sinceAccepted=25ms transitions=0)
TrackingState StaleFailsafe -> Recovering (sid=inj668cf11bd acceptedSeq=1 receivedSeq=1 sinceAccepted=4ms transitions=1)
TrackingState Recovering -> Live       (sid=inj668cf11bd acceptedSeq=6 receivedSeq=6 sinceAccepted=20ms transitions=1)
TrackingState Reconnecting -> Recovering (sid=inj668cf11bd acceptedSeq=50000 receivedSeq=50000 sinceAccepted=0ms transitions=1)
TrackingState StaleFailsafe -> Recovering (sid=18f3a0f25933 acceptedSeq=3 receivedSeq=3 sinceAccepted=1ms transitions=3)
TrackingState Recovering -> Live       (sid=7ce3bf8bda42 acceptedSeq=9 receivedSeq=9 sinceAccepted=12ms transitions=4)
```

The `Reconnecting -> Recovering` line is the 50,000-seq forward jump being accepted, and
`StaleFailsafe -> Recovering` with `acceptedSeq=1` is a restarted producer's very first packet being
taken on its own merits — the two transitions this task exists to make possible.

---

## 5. Pose-buffer behaviour

On `NewSession` the provider calls `poseBuffer.Clear()` **before** the push, so a new session's first
pose can never be interpolated against the previous session's. `Clear()` resets `newestSeq`, so the
new stream's `seq 1` is judged on its own merits.

Verified both ways:

* `SessionChange_FlushesBuffer_SoNoInterpolationAcrossTheBoundary` — buffer holds `[A,B,C]`, session
  changes, first new pose arrives; depth is **1**, and sampling returns session B's pose exactly
  (50.0), never a blend of 3 and 50.
* `WithoutSessionFlush_TheOldFailureReproduces` — guards the guard. Without the flush, **0 of 100**
  restarted packets are accepted: 99 rejected out-of-order and exactly 1 as a duplicate (the packet
  whose seq equals the old `newestSeq`). Asserting that split rather than a loose bound keeps the
  test honest about which rule fired.

Live, across two real sidecar restarts: **`rejectedOutOfOrder = 0`** (F-19: 4948).

---

## 6. Reconnect behaviour and the neutral pose

`AppBootstrap` asks the provider for its state each frame. On `StaleFailsafe` it eases the control
rig toward neutral via a new `KalidokitControlRigDriver.ReleaseToRest(k)` and stops consuming poses;
on recovery it resumes. `ResetHoldState()` drops held limb/trunk gate state once on entry, so a
recovered stream cannot resurrect a hold decision taken against a dead session.

Neutral is **identity local rotation on every control-rig bone**. That is exact, not approximate: the
VRM control rig is *normalized*, so its rest pose is identity by definition — there is no captured
rest that could go stale and no per-rig calibration to get wrong.

§6 asked that this be measured rather than assumed. Mean bone angle from rest, per phase:

| phase | bone angle from rest | what it shows |
|---|--:|---|
| tracking | 19–53° | a real human pose |
| `KILL_FORCED` / `KILL_CLEAN` | **19.43° / 52.95°, unchanged** | short stale → **holds the last valid pose** |
| `GAP_FORCED` / `GAP_CLEAN` | **→ 0.00°** | long stale → **controlled neutral** |
| `USB_GAP` (12 s, camera out) | **0.00°, moved/frame 0.0000** | neutral and completely still |

The full §6 policy chain — *fresh → hold last valid → controlled neutral* — is therefore demonstrated
on measured data, not asserted.

---

## 7. Sidecar restart (§7, §8, §12)

`f20a_failure_injection.py` drove the real sidecar through forced and clean kills while Unity
rendered. 23,841 rendered frames.

| phase | n | tracking states | moved/frame | rest at end | max step | snaps > 10° |
|---|--:|---|--:|--:|--:|--:|
| `LIVE_BASELINE` | 3900 | **Live 3900** | 0.104 | 19.5 | 0.165 | **0** |
| `KILL_FORCED` | 135 | Live 113, StaleHold 22 | 0.017 | 19.4 | 0.067 | **0** |
| `GAP_FORCED` | 2145 | StaleHold 384, **StaleFailsafe 1761** | 0.026 | **0.00** | 0.205 | **0** |
| `RESTART_1` | 1500 | StaleFailsafe 904, **Live 536** | 0.082 | 38.2 | 2.266 | **0** |
| `LIVE_AFTER_1` | 3735 | **Live 3735** | 0.043 | 53.0 | 0.022 | **0** |
| `KILL_CLEAN` | 120 | Live 107, StaleHold 13 | 0.018 | 53.0 | 0.000 | **0** |
| `GAP_CLEAN` | 2100 | StaleHold 392, **StaleFailsafe 1708** | 0.042 | **0.00** | 0.000 | **0** |
| `RESTART_2` | 1365 | StaleFailsafe 827, **Live 484** | 0.131 | 42.8 | 1.398 | **0** |
| `LIVE_AFTER_2` | 3630 | **Live 3630** | 0.209 | 38.6 | 0.765 | **0** |

Three distinct session ids across the run (`269bb9cac08f`, `eaae3d413ea9`, `5877ec27feaa`), and:

```text
sessionTransitions = 2      acceptedNewSession = 2      reconnects = 2
rejectedOutOfOrder = 0      rejectedDuplicate  = 0      rejectedOldSession = 0
```

**Recovery latency attributable to F-20A is negligible.** Both transitions accepted the new stream's
`seq 1` at `sinceAccepted = 0.044 s` and `0.063 s`. The ~4 s of `StaleFailsafe` inside each restart
phase is the sidecar's own model load and device initialisation, not the session logic.

**§8 sequence reset**: covered live (above, `seq` 1 accepted after a session that reached ~450) and
deterministically by `SidecarRestart_NewSession_IsAcceptedNotRejectedForever`, which runs session A
to seq 1000, restarts at seq 1, and asserts every packet is accepted with **no new out-of-order
rejections**.

**§12 clean vs forced**: both paths tested — `taskkill /F /T` and `terminate()`. Behaviour identical;
neither required a Unity restart. A Python-exception crash is the same case as a forced kill from the
consumer's point of view (the process stops sending), so it is covered by the forced path rather than
simulated separately.

---

## 8. Unity restart while the sidecar stays alive (§13)

Sidecar left running at `sid=bbcd38bee236`; Play mode stopped and restarted.

```text
t0: state=Live sid=bbcd38bee236 accSeq=692 rxSeq=692 sinceAcc=0.000s transitions=0 ooo=2 dup=0 depth=16
t1: state=Live sid=bbcd38bee236 accSeq=737 rxSeq=737 sinceAcc=0.015s transitions=0 ooo=2 dup=0 depth=16
```

The consumer joined a stream already at `seq 692` — far past 1 — went **Live immediately**, and
correctly recorded **`transitions = 0`**: the producer had not changed, so this is not a session
break. It is not locked out by having missed seq 1–691. `ooo = 2` is two genuinely reordered
datagrams on loopback during bind; bounded and unremarkable against 4948.

---

## 9. Camera disconnect / reconnect (§11)

Tested with a **real USB unplug**, not a simulation. 38,440 rendered frames.

| phase | n | tracking states | moved/frame | rest at end |
|---|--:|---|--:|--:|
| `USB_LIVE` | 3555 | **Live 3555** | 0.053 | 47.8 |
| `USB_UNPLUG` | 2895 | Live 1360, **StaleFailsafe 1177** | 0.083 | **0.00** |
| `USB_GAP` | 3060 | **StaleFailsafe 3060** | **0.0000** | **0.00** |
| `USB_REPLUG` | 3780 | StaleFailsafe 3780 | 0.0000 | 0.00 |
| `USB_RESTART` | 6840 | StaleFailsafe 1906, **Live 4882** | 0.188 | 55.3 |
| `USB_RECOVERED` | 3330 | **Live 3330** | 0.156 | 48.7 |

**The sidecar process dies when the device is pulled** (`rc = 1`) — it does not survive the unplug
and does not self-recover. From the consumer's side that is identical to a crash, and the consumer
handled it correctly: neutral pose, then full recovery on restart, **with no Unity restart and no
scene reload**. §20's camera criterion is met.

What is *not* solved here, and is stated plainly: **nothing automatically restarts the sidecar.** An
unattended installation needs a supervisor (a service wrapper or a watchdog loop) to respawn it.
Building one is outside F-20A's transport scope, and §11 explicitly says not to redesign the OAK-D
pipeline; it is the top item in §14.

---

## 10. Failure injection (§14)

`f20a_packet_injection.py` drives the **shipping receive path** over a real socket with hostile
packets. Counters are checked against what was sent.

| case | sent | expected | observed |
|---|--:|---|---|
| duplicate (same seq ×20) | 20 | rejected duplicate | **`rejectedDuplicate = 20`** ✓ |
| old seq, same session | 20 | rejected out-of-order | 20 ✓ |
| delayed packet (seq just below newest) | 1 | rejected out-of-order | 1 ✓ |
| new session (new sid, seq 1) | 30 | accepted after a session transition | accepted, `transitions = 1` ✓ |
| **packet from the previous session** | 10 | **rejected** | **`rejectedOldSession = 10`** ✓ (after the fix below) |
| large forward seq jump (+50000) | 10 | accepted — burst loss must not wedge | accepted ✓ |
| backward sender timestamp | 10 | must not poison anything | accepted; the buffer uses the receive clock ✓ |
| burst loss (2 s gap) then resume | 30 | recover without a session change | recovered, no transition ✓ |

### 10a. A bug this test found in my own design

The first injection run reported **`rejectedOldSession = 0`**: the ten packets from the *previous*
session were treated as a **new** session and adopted, flushing a healthy buffer. A datagram still in
flight when the old sidecar died would therefore have dragged the consumer backwards.

Fixed by remembering a bounded set of **retired** session ids (8, ring-buffered). A sid we have
already left now returns `SessionVerdict.OldSession` and the provider drops the packet before the
push. Two regression tests pin it — including a three-session sequence where A and B are both
rejected once C is current. Re-run live: **`rejectedOldSession = 10`, exactly the ten sent**, current
sid unchanged, no spurious transition.

This is the case the brief asked for and I had got wrong; the test earned its keep.

---

## 11. Regression tests (§18, §19)

The full EditMode suite, executed in-process:

```text
SUITE: 124 passed, 0 failed   (13 classes)
  ArmAimSolverTests 19   HumanoidHandRetargeterTests 3   KMathTests 6   LimbGateTests 6
  OneEuroFilterTests 4   PerformanceMonitorTests 2       PoseBufferTests 14
  PoseSpaceConverterTests 4   RotationFromVectorsTests 5  TorsoYawGuardTests 18
  TrackingStreamHealthTests 25   TrunkGateTests 15        VrmExpressionRetargeterTests 3
```

`PoseBufferTests` (P1-3, 14), `TorsoYawGuardTests` (V6, 18), `TrunkGateTests` (15) and
`ArmAimSolverTests` (Arm V2, 19) are all green, so none of the protected behaviour regressed.

**How they were run, stated rather than implied:** `run_tests` closes this Editor, so the NUnit test
bodies were invoked directly by reflection in-process. They are the same methods with the same NUnit
asserts the runner would call; what is skipped is the runner's own reporting and isolation. The suite
remains a normal EditMode assembly for CI/batch.

One test of my own was wrong on the first run and was corrected rather than loosened — see §5.

---

## 12. Performance impact

Sidecar, portrait, sub-pixel 1/8, before (F-19, no `sid`) and after:

| run | n | fps | frame age p50 | capture→send p50 |
|---|--:|--:|--:|--:|
| F-19 baseline | 22,678 | 29.9 | 17.3 ms | 31.8 ms |
| F-20A run 1 | 513 | 30.2 | 16.9 ms | 29.9 ms |
| F-20A run 2 | 519 | 30.0 | 17.2 ms | 30.0 ms |
| F-20A run 3 | 519 | 30.1 | 17.0 ms | 30.0 ms |
| **delta** | | **+0.2** | **−0.2 ms** | **−1.8 ms** |

**No measurable regression.** The deltas are within run-to-run variation and the F-20A runs are ~515
frames each against 22,678 for the baseline, so the comparison is indicative rather than tight. The
added work is a string comparison and a few counters per datagram, plus one `Evaluate()` per frame.
P1-2 freshness is untouched — the provider's receive path is unchanged apart from the session check.

---

## 13. Operator diagnostics (§15)

`GetStreamHealth()` returns all of it in one call, so a HUD cannot show a torn mix:

```text
TrackingState   SessionId          LastAcceptedSeq   LastReceivedSeq   SecondsSinceAccepted
SessionTransitions   AcceptedNewSession   RejectedOldSession
RejectedOutOfOrder   RejectedDuplicate    PoseBufferDepth      ReconnectCount
```

Every state transition is logged with the sid, both sequence numbers, the age and the transition
count. **The question an operator can now answer is "are fresh, ordered, current-session poses
actually reaching the avatar right now?"** — `TrackingState`, not `ReceivedCount`. F-19 proved
`IsRunning`, `ReceivedCount` and `ParseErrors` can all look perfect while the avatar is minutes
stale; none of them is the health signal, and the report no longer treats them as one.

---

## 14. Remaining risks

**1. Nothing restarts the sidecar.** The largest remaining gap. The consumer now recovers perfectly,
but only once the producer comes back, and on a USB unplug the producer *dies* (§9). An unattended
installation needs a supervisor to respawn it. Out of scope here by §11 and §17; it is the next
action.

**2. Recovery from neutral necessarily exceeds 10° on the first rendered step.** Measured in the USB
run: the avatar sat at exactly neutral, and the first accepted pose of the new session produced a
13.378° step, then a damped convergence `9.02 → 5.32 → 2.85 → 1.43 → 0.66 → 0.24 → 0.05` — settled in
~8 frames (~36 ms). §20 permits this ("unless the source itself makes a genuine >10° movement": the
source was 32.9° from neutral), and the restart test with the subject nearer neutral produced max
steps of 2.27° and 1.40° with **zero** snaps. But it is structural: releasing to neutral means the
return trip is a real movement. The alternative — leaving a stale human pose on screen — is worse,
and that is the trade being made deliberately.

**3. The receive clock has 1 ms resolution, capping the accepted rate at ~1 kHz.** Found while
chasing three unexplained rejections in the injection run and then confirmed directly: 60 packets
sent back-to-back with strictly increasing `seq` produced **28 out-of-order rejections**, all 60
received, zero duplicates. `PoseBuffer` pushes on `recvEpoch` (millisecond resolution) and rejects
`timestamp <= newest`. This is **pre-existing P1-3 behaviour, not introduced here**, and it never
bites at 30 fps — the real sidecar runs showed `ooo = 0`. §1 forbids modifying P1-3, so it is
reported rather than changed. It would matter only if a future sender exceeded ~1000 packets/s.

**4. The legacy no-`sid` path is tested only synthetically.** The deprecated BlazePose sender was not
run; its recovery rule is covered by three unit tests but has never faced real traffic.

**5. Two concurrent senders on the same port are not handled specially.** They would look like
rapidly alternating sessions; the retired-session set would reject the older one, so the consumer
would lock to whichever sender it saw last rather than flapping. Not tested live, and not a
configuration the installation is meant to have.

---

## 15. Production changes (§23)

```text
git diff -- Runtime/
 Runtime/Bootstrap/AppBootstrap.cs                |  35 +++++-
 Runtime/Retargeting/KalidokitControlRigDriver.cs |  58 ++++++++++
 Runtime/Tracking/OakD/OakDUdpPoseProvider.cs     | 133 ++++++++++++++++++++++-

new:
 Runtime/Tracking/OakD/TrackingStreamHealth.cs        (session + watchdog)
 Tests/EditMode/TrackingStreamHealthTests.cs          (25 tests)
 Runtime/Diagnostics/F19BoneRecorder.cs               (F-19 diagnostic; extended with a state trace)

git diff -- python-sidecar~
 oak_depth.py            | 31 ++   (F-19: the stereo-config constant + truthful banner)
 wholebody_udp_sender.py | 38 ++   (F-19 portrait/sub-pixel flags + F-20A sid)
```

Every change is transport/session or failsafe. **Nothing touches** P1-1, P1-2, P1-3's rules, F-08,
Arm V2, V5 torso composition, V6's thresholds, the portrait transform, or the UDP payload semantics
(one additive field). `ReleaseToRest` and `ResetHoldState` are additive and run **only** in the
failsafe state. No biomechanical constraint, person-lock or avatar tuning is present — those remain
separate tasks, as §17 and §23 require.

---

```text
SESSION RECOVERY DECISION:
Adopt the explicit per-process session id: a restarted sidecar is now recognised from the producer's
own statement rather than inferred from a sequence reset, the pose buffer is flushed at the boundary
so nothing interpolates across it, and two live restarts plus a real USB unplug recovered
automatically with rejectedOutOfOrder = 0 against F-19's 4948, with no Unity restart, no scene reload
and no manual intervention.

STALE-POSE DECISION:
A dead stream now ends in a controlled neutral pose rather than a frozen human one - hold the last
valid pose to 500 ms, release to the normalized rig's exact rest pose by 2 s - measured reaching
0.00 degrees from rest in every outage phase, so the minutes-old avatar F-19 observed cannot recur.

PUBLIC-INSTALLATION IMPACT:
The consumer side is now fail-safe and self-recovering and an operator can tell live tracking from a
dead stream at a glance, but the installation is still not unattended-safe because nothing restarts
the sidecar after it dies - which a USB unplug reliably causes.

NEXT ENGINEERING ACTION:
Add a supervisor that respawns the sidecar on exit with backoff, verify it against the same USB
unplug and forced-kill tests used here, and only then return to F-20's biomechanical constraint layer
against the F-19 failure table.
```
