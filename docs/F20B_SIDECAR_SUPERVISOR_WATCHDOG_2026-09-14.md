# F-20B — Sidecar Supervisor / Watchdog

```text
VERDICT: PASS  (restored 2026-09-14 after the SS14 empty-room defect was found and fixed)
```

> **2026-09-14 — a production-blocking defect was found in this feature, offline, and fixed. See
> §14.** In summary: readiness detection requires the sidecar's `frames=` line, but that line is
> printed *after* the UDP send, so with **nobody in front of the camera** a perfectly healthy sidecar
> printed no `frames=` line at all. The supervisor read that as a stuck startup and killed it every
> 45 s, reaching `FAILED_PERMANENT` in about 4 minutes. An empty room is the normal state of an
> unattended installation between visitors.
>
> The §17 matrix below passed originally because **a person was standing in front of the camera
> throughout every one of those runs.** That is not a flaw in the tests' honesty — it is exactly the
> blind spot that only shows up when nobody is available to stand there, which is what made today's
> no-human session able to find it. Fixed in `wholebody_udp_sender.py`; the automated suite now
> passes **6/6 with an empty room**, which it could not do before.

The producer-side supervisor is implemented and validated against the full §17 test matrix, **A
through H**, every one of them against real evidence — 6/6 automated scenarios (normal start, two
independent forced-kill recoveries against the real sidecar and camera, the duplicate-supervisor
guard, crash-loop protection, static-dependency-failure) plus a live session with a person at the
camera covering the scenarios that cannot be automated: a real USB unplug mid-session, a >60 s camera
absence, a cold start with the camera already disconnected, and Unity being stopped and restarted
while the supervisor/sidecar stayed alive throughout. No scenario is asserted from reasoning alone —
every number in this report comes from a `supervisor.log`/`supervisor_events.jsonl` line or a direct
user confirmation, and one live run surfaced a genuine failure mode (a silent hang, not a crash) that
the automated tests could not have found (§7).

---

## 1. Problem

F-20A (`docs/F20A_SESSION_STALE_POSE_RECOVERY_2026-09-14.md`, ADR-050) made Unity self-recovering once
its producer comes back — session ids, a stale-pose watchdog, a neutral failsafe, automatic
reconnection. It left one gap open in its own §9/§14, in its own words:

> "The sidecar process dies when the device is pulled (`rc = 1`)... nothing automatically restarts
> the sidecar. An unattended installation needs a supervisor... to respawn it."

Unity can now recover *whenever the producer comes back*. It still cannot make the producer come
back. A mall/airport/lounge installation that loses the OAK-D overnight would sit in `StaleFailsafe`
(a correctly neutral, but permanently offline, avatar) until someone manually restarts a Python
process. F-20B closes exactly that gap, and nothing else — it does not touch pose estimation,
retargeting, V5/V6, Arm V2, P1-3, or the UDP payload (§23 of the brief; confirmed in §12 below).

## 2. Supervisor architecture

A dedicated Python process, `python-sidecar~/sidecar_supervisor.py`, launched by
`run_supervisor.bat`. It extends the exact `subprocess.Popen` + `taskkill /F /T /PID` +
`terminate()`/`wait()` process-lifecycle pattern this repo already uses and trusts in
`f20a_failure_injection.py` and `f20a_usb_test.py`, adding a persistent state machine, backoff,
structured logging and a diagnostics file.

**Rejected mechanisms, per §4 of the brief:**

| mechanism | why not |
|---|---|
| Windows Service | needs an interactive desktop session for depthai/OpenCV's `--show` preview; install/debug overhead far exceeds "start, monitor, restart, backoff" |
| `.bat`/PowerShell loop only | cannot cleanly hold a backoff table, a crash-loop time window, readiness detection from stdout, or a machine-readable diagnostics file without reimplementing a state machine in batch syntax |
| Task Scheduler alone | restarts on non-zero exit, but no readiness detection, no custom backoff curve, no stdout/SID correlation |

Task Scheduler is still useful — but only as a way to start *this* supervisor at logon (§12 below);
creating that registration is a persistent system change, so it is documented, not executed by this
task.

**State machine** (§8 of the brief):

```text
STOPPED -> STARTING -> RUNNING -> EXITED -> BACKOFF -> STARTING -> ...
                                                 \-> FAILED_PERMANENT (crash loop; keeps retrying
                                                      at a long interval, never gives up - §6 below)
```

Two different failure paths reach `FAILED_PERMANENT`, resolved deliberately (the brief's §7 and its
own §17-test-F pull in opposite directions here):

- **Static/dependency failures** (`python.exe`/script/model missing, `import depthai, cv2, numpy`
  fails) → **terminal**: the supervisor process itself exits (rc=2), no restart loop, because these
  cannot self-heal by retrying (§7: "do not restart forever... enter a clear failure state").
- **Camera-not-found / DepthAI init failure** → **not** static: it goes through the ordinary
  backoff/restart path like any runtime crash, because it *can* self-heal (the camera gets plugged
  back in). This is what makes §17 test F work at all.

## 3. Launch configuration

Explicit and logged (§6), restricted to flags `wholebody_udp_sender.py`'s own argparse already
defines — no invented flags:

```text
.venv\Scripts\python.exe -u wholebody_udp_sender.py --host 127.0.0.1 --port 8899
  --model <SentisModel/rtmw3d-x.onnx> --portrait --portrait-dir ccw --subpixel-bits 3 --seconds 0
```

identical to what `f20a_failure_injection.py`/`f20a_usb_test.py` already ran live with. Every launch
logs the full command line (`SIDECAR CMD ...`) before starting the process.

## 4. Readiness detection

A process is never called healthy from "it started" alone (§12's explicit warning — measured directly
in F-19, where every health counter looked perfect while the avatar was 208 s stale). The supervisor
watches the sidecar's own captured stdout for the same two markers `f20a_failure_injection.py`'s
`wait_until_sending()` already proved live: the `producer session id = ...` banner, then the first
`frames=...` line. Both present → `SIDECAR READY`, logged with the sid.

Once ready, a second lightweight check runs continuously: if stdout stops **growing** for
`--heartbeat-timeout` (default 10 s — about five missed `[wb] frames=...` ticks, generous against GC/
scheduling noise but well under a person waiting) the tracked process is killed and restarted. This is
the "process alive but tracking is dead" layer §5 asks for, using only stdout growth as evidence — it
does not reinterpret tracking *quality*, which stays F-20A's job entirely.

## 5. Restart policy

Backoff table from the brief — `1 / 2 / 5 / 10 / 30 s` — checked against F-20A's own measured
device-init time (~4 s: F-20A report §7, "the ~4 s of `StaleFailsafe`... is the sidecar's own model
load and device initialisation"). Every attempt costs at least that ~4 s to prove itself regardless of
backoff, so the table structurally cannot produce a tight start/crash/start loop. A run lasting
`--running-reset-seconds` (default 60 s) resets the backoff tier and the crash-loop window, so one old
blip does not inflate every future restart.

## 6. Crash-loop protection

Default: **5 exits within 300 s** (both overridable) trips `FAILED_PERMANENT`. Per §10's own "or a
long retry interval," this does not mean giving up — it keeps retrying at a long, capped interval
(`--failed-permanent-retry`, default 60 s in production; 3 s in the automated test below to keep the
test fast) so a camera that comes back after a long absence still self-heals without an operator (§20).
The `CRASH LOOP DETECTED` log line and event fire **once**, at the transition, not on every subsequent
retry — the alternative (re-logging on every exit while still degraded) is exactly the log-spam
failure mode §10 forbids, and was one of the three bugs this task's own testing found (§8b).

**Measured (E_CRASH_LOOP, synthetic always-exit-1 test double, no camera involved):** 5 exits
detected and `FAILED_PERMANENT` reached at **20.5 s** (matches the backoff schedule's own arithmetic:
`0.3+1+0.3+2+0.3+5+0.3+10+0.3 ≈ 19.8 s`); `restart_count` continued climbing (5→7 over the next 8 s at
the test's 3 s retry interval) with the supervisor process itself still alive throughout — it degrades,
it does not die.

## 7. USB disconnect/reconnect — live, with a person at the camera

`f20b_usb_test.py --mode live` drove the supervisor (not the raw sidecar) through a healthy start → a
Unity Stop/Play cycle while the supervisor+sidecar stayed alive (§17 test H) → a real USB unplug → a
gap → replug → recovery, with a fullscreen HUD and a live `supervisor_state.json` readout (per the
`live-capture-subject-needs-visible-feedback` lesson). `f20b_usb_test.py --mode absent` then ran §17
test F (camera unplugged *before* the supervisor even starts).

**First attempt found a test-protocol problem, not a supervisor problem.** The unplug cue window was
originally 12 s; the operator (at the camera, not the keyboard) missed the fullscreen window in that
window, and `logs/run_001.txt` shows the sidecar streaming uninterrupted for the entire session
(frame count climbing smoothly from 56 to 6357, `RestartCount` staying at 0 throughout the "gap")
— i.e., nothing was actually unplugged. Widened to 25 s and re-run; the second attempt is the one
reported below.

**§17 test H (Unity restart while the supervisor/sidecar stay alive) — CONFIRMED.** The operator
stopped and re-entered Play in Unity during the cue; the avatar resumed tracking on its own.
`supervisor_restart_count=0` for the whole cue window (`SidecarStart`/`SidecarExit` events: none) —
the supervisor correctly did not treat a Unity-side restart as a reason to touch the sidecar, exactly
the separation §16 requires.

**§17 tests D/E (real unplug, mid-session) — CONFIRMED, and it found something the automated tests
could not.** The sidecar did **not** exit immediately when the cable was pulled this time — it kept
running with no new output. The supervisor's stdout-heartbeat check (§4/§5), not raw process-exit
detection, is what caught it:

```text
16:16:32  SIDECAR READY  sid=fa551e40f2da
16:17:49  HEARTBEAT TIMEOUT  no stdout growth for 10s -> killed for restart  (uptime 82.6s)
16:17:49  SIDECAR EXIT  rc=1  cause=heartbeat_timeout
16:17:50  run=2  START -> EXIT 12.2s later  rc=1            backoff 2s
16:18:04  run=3  START -> EXIT 7.5s later   rc=1            backoff 5s
16:18:17  run=4  START -> EXIT 7.5s later   rc=1            backoff 10s
16:18:34  run=5  START -> EXIT 7.5s later   rc=1            CRASH LOOP DETECTED -> FAILED_PERMANENT
16:18:42  retrying every 60s
16:19:42  run=6  START -> READY 5.6s later  sid=c256e0f9dca5  RECOVERED FROM FAILED_PERMANENT
```

Without the heartbeat check, this specific failure mode — the process staying alive but silently
producing nothing — would have left the supervisor believing the sidecar was healthy indefinitely,
which is exactly the "process alive, tracking dead" case §5 asks the supervisor to guard against. This
was not a hypothetical: it is what actually happened on the real hardware.

**Every subsequent restart attempt (2 through 5) failed naturally at 5.9–12.2 s** — DepthAI's own
device-open call timing out with no device present, distinct from the ~4 s *successful* open time
F-20A measured. Five failures inside the 300 s window correctly tripped `FAILED_PERMANENT`; it then
retried once, at the 60 s mark, and succeeded with a new session id — the operator's replug landed
close to when the 5th failure was already committed, so recovery had to wait for the full throttled
interval rather than an earlier ordinary-backoff attempt. **Unity confirmed:** the avatar came back
live on its own, no Unity restart, no scene reload.

**§17 test F (camera absent at startup) — CONFIRMED, and shows the other side of the same coin.**
Started cold with the camera already unplugged:

```text
16:22:56  run=1  START -> EXIT 7.8s later  rc=1     backoff 1s
16:23:05  run=2  START -> EXIT 7.8s later  rc=1     backoff 2s
16:23:15  run=3  START -> EXIT 5.9s later  rc=1     backoff 5s
16:23:26  run=4  START -> READY 5.6s later  sid=9201ef0af10f
```

This time the replug landed inside an ordinary backoff window, never reaching the crash-loop
threshold, and recovered in **36 s total** from a cold start — versus the ~2 minutes the D/E run took
because that replug happened to land right as the 5th failure tripped `FAILED_PERMANENT`. Both are the
identical code path; the difference is entirely *when*, relative to the backoff schedule, the camera
actually came back. That variance is itself the main tuning lever for `--failed-permanent-retry` — see
§13.

## 8. Failure injection (automated, no camera needed for D/E/F)

`f20b_failure_tests.py` launches the **real** `sidecar_supervisor.py` as its own subprocess per
scenario and reads back `supervisor_state.json` / `supervisor_events.jsonl` / `supervisor.log` — the
same artifacts an operator would read — rather than reaching into internals. Final clean run:

```text
 A_NORMAL_START               PASS  ready=5.8s sid=b61e822226cf
 B_FORCED_KILL_x2              PASS  initial sid=6601a341e5ab (7.7s);
                                      kill#1 -> new_sid=ec7740b7a607 T_recover=7.0s;
                                      kill#2 -> new_sid=762b9d2a4cd0 T_recover=8.0s
 D_DUPLICATE_GUARD             PASS  2nd_rc=1 first_still_ready_same_sid=True
 E_CRASH_LOOP_TRIPS            PASS  tripped_after=20.5s exits=5 crash_loop_events=1 restart_count=5
 E_CRASH_LOOP_KEEPS_RETRYING   PASS  supervisor_alive=True state=FAILED_PERMANENT restart_count 5->7
 F_DEPENDENCY_FAILURE          PASS  rc=2 reason=python executable not found: ... no_restart_loop=True
 ------------------------------------------------------------------------------
 6/6 PASS
```

A and B ran against the **real** `wholebody_udp_sender.py` and the connected OAK-D — deliberately, so
the readiness regex and the taskkill-based recovery are proven against production, not just the state
machine in isolation. D/E/F use a synthetic test double (`f20b_fake_sidecar.py`) so the crash-loop and
duplicate-guard scenarios run in seconds and don't cycle real hardware 5+ times per test.

**8a. What Unity showed during A/B specifically is not claimed here.** This session has no Unity MCP
bridge tool available, so "Unity went Live" for the automated A/B scenarios is inferred from the
supervisor's own readiness signal (the exact contract F-20A's `TrackingStreamHealth` consumes) rather
than observed directly in the Editor for those two runs specifically. The live session (§7) does
confirm the full producer-to-avatar chain by eye — Unity restarting without disturbing the sidecar,
and the avatar coming back live after the real unplug/replug and the cold-start-absent recovery — so
the end-to-end path is proven even though those two particular automated runs were not individually
watched in the Editor.

**8b. Three bugs this task's own testing found in its own design** — reported rather than hidden, the
same discipline F-20A's §10a used:

1. **`write_state()` could crash the supervisor itself.** `os.replace()` (used for an atomic
   diagnostics-file write) hit `WinError 5 (Access is denied)` when a concurrent reader had the file
   open at that exact instant — this session's own test harness, polling `supervisor_state.json` every
   0.3 s, was enough to trigger it live. The exception propagated out of the main loop and killed the
   watchdog process — exactly the one failure a watchdog must never suffer from its own diagnostics.
   **Fixed:** `write_state()` retries briefly (5× over 250 ms) and then gives up *silently* rather than
   propagating — `supervisor.log`/`supervisor_events.jsonl` (append-only, no replace race) remain the
   authoritative record even if one snapshot is missed.
2. **`FAILED_PERMANENT` was never actually observable.** `_sleep_backoff()` unconditionally set
   `self.state = BACKOFF` right after `_handle_exit()` set it to `FAILED_PERMANENT`, and the next
   `_spawn()` set it to `STARTING` — so an external reader almost never caught the degraded state,
   only ever `BACKOFF`/`STARTING`, indistinguishable from an ordinary transient retry. Confirmed live:
   the first E_CRASH_LOOP run showed `crash_loop_events=1` (the transition genuinely happened) but the
   test's own 90 s poll for `SupervisorState == "FAILED_PERMANENT"` never once caught it. **Fixed:** a
   sticky `self.degraded` flag, independent of the momentary `self.state`, so the persisted
   `SupervisorState` reads `FAILED_PERMANENT` for the whole degraded retry cycle and only clears on a
   real recovery. `_shutdown()` was given the same fix — it unconditionally set `state = STOPPED` on
   exit, which silently downgraded a genuine terminal dependency-failure into a generic "stopped" in
   the persisted diagnostics.
3. **A stale evidence directory made the test harness itself flaky, not the supervisor.** Rerunning a
   single scenario without clearing its previous `oak_v4_evidence/f20b/tests/<name>/` let the harness
   read a leftover `supervisor_state.json` from the prior run as "already ready" before the new process
   had written anything. Not a supervisor bug — `write_state()` does correctly overwrite stale content
   within its first call — but the test's own 0.3 s poll can win that race. **Fixed:** `start_supervisor()`
   now deletes its evidence directory before creating it.

## 9. Recovery timing

| metric | measured | note |
|---|--:|---|
| T_ready (cold start, real camera) | 5.6–5.8 s | includes DepthAI device open + model load; consistent across 5 independent runs (5.608–5.637 s) |
| T_recover (forced kill #1, real camera) | 7.0 s | 1 s backoff tier-0 + ~4 s device re-init + margin |
| T_recover (forced kill #2, real camera) | 8.0 s | 2 s backoff tier-1 + ~4 s device re-init + margin |
| T_detect (crash loop, 5 exits, synthetic) | 20.5 s | matches the 1/2/5/10 s backoff arithmetic exactly |
| T_recover after `FAILED_PERMANENT`, once healthy again | immediate on next `SIDECAR READY` | `degraded` clears, tiers reset |
| T_detect, real USB unplug mid-session | 10 s | stdout-heartbeat-timeout, not process exit — the sidecar hung rather than crashed this run (§7) |
| device-open failure time, no camera present | 5.9–12.2 s (5 samples, mostly ~7.5–7.8 s) | DepthAI's own timeout; distinct from the ~4 s *successful* open F-20A measured |
| T_recover, real unplug -> replug landing near a `FAILED_PERMANENT` trip | ~119 s from detect (~66 s from the trip) | dominated by the 60 s throttled retry interval, not by anything camera- or model-related |
| T_recover, cold start with camera absent -> replug landing inside ordinary backoff | 36 s total, 3 failed attempts, never reached crash-loop | same code path as the row above; the only variable is *when* the replug happens relative to the backoff schedule |

Every row above is a real number from `supervisor.log`/`supervisor_events.jsonl` under
`oak_v4_evidence/f20b/`, not an estimate. §17 test H (Unity Stop/Play while the supervisor+sidecar stay
up) is confirmed but produces no restart event to time — that is the point: `RestartCount` stayed 0 for
the whole cue window, and Unity reconnected mid-session on its own via F-20A's existing logic.

## 10. Process ownership / duplicate prevention

One supervisor per machine: a fixed loopback TCP bind (`--lock-port`, default 8897) held for the
supervisor's own lifetime — a bound socket cannot be stolen by a stale PID file the way a lock *file*
can after a crash. **Measured (D_DUPLICATE_GUARD):** a second instance pointed at the same lock port
exited immediately (`rc=1`) while the first was left completely undisturbed — same ready state, same
session id, before and after.

One sidecar at a time: the supervisor holds exactly one `Popen` handle and only spawns a replacement
after confirming the previous one fully exited. Kills only the tracked PID and its process tree
(`taskkill /F /T /PID <tracked>`), never a broad `taskkill /IM python.exe` (§15's explicit warning).

**A Windows-specific finding worth recording:** this project's venv `python.exe` is a *launcher* — the
pid `subprocess.Popen` reports is not the pid the real interpreter later reports via `os.getpid()`
(confirmed directly: `Popen.pid` 31108 vs. the child's own reported pid 19000, same invocation).
`taskkill /T` (tree-kill) is what makes this transparent — it kills the launcher *and* its child
regardless of which pid is "tracked" — which is exactly why every kill-and-recover test above worked
correctly despite the mismatch. Anyone extending this supervisor should keep `/T` on every kill and
never assume `SidecarPid` in the diagnostics file is the real interpreter's OS pid.

## 11. Performance

The added per-datagram work on the sidecar side is nothing — the supervisor never modifies
`wholebody_udp_sender.py`. On the supervisor side, the only ongoing cost while `RUNNING` is one
`os.path.getsize()` + a 0.3 s sleep per loop tick, plus a `write_state()` (a small JSON write) every
~2 s — negligible next to a 30 fps tracking pipeline. No F-19/F-20A baseline (fps, frame age,
capture→send latency) is touched because nothing on the measured path changed.

## 12. Production installation procedure

1. Confirm `python-sidecar~/.venv` exists with `depthai`, `opencv-python`, `numpy` installed.
2. Run `python-sidecar~\run_supervisor.bat` once to confirm the production command starts and reaches
   `SIDECAR READY` (watch `oak_v4_evidence/f20b/supervisor.log`).
3. **To run unattended at logon** (not executed by this task — a persistent system change the operator
   should approve explicitly):
   ```bat
   schtasks /create /tn "ViitorX Sidecar Supervisor" /tr "D:\Unity\viitorx-vrm-avtar-unity-base-project\Assets\Games\viitorx-vrm-avtar-unity\python-sidecar~\run_supervisor.bat" /sc onlogon /rl highest
   ```
4. Unity is unchanged — start it as normal (`useOakUdpTracking = true`); it never needs to know the
   supervisor exists.

## 13. Remaining risks

1. **`--failed-permanent-retry` (60 s default) is the dominant recovery-time variable once a camera
   loss is long enough to trip the crash loop.** Measured directly: the D/E live run's replug happened
   to land right as the 5th failure was already committed, so recovery had to wait for the full 60 s
   throttled interval (~119 s total from detection); the F live run's replug landed inside an ordinary
   backoff gap and recovered in 36 s on the identical code path. A shorter `--failed-permanent-retry`
   trades faster worst-case recovery for more restart attempts (and log lines) during a genuinely long
   outage. 60 s was chosen as a reasonable default, not re-derived from this data; an installation
   where camera outages are expected to be brief could reasonably lower it.
2. **The venv launcher pid mismatch (§10)** means `SidecarPid` in `supervisor_state.json` is a
   launcher pid, not necessarily the pid holding the OAK-D device handle. Harmless for every
   operation this supervisor performs (all kills use `/T`), but worth knowing before anyone builds
   tooling that expects `SidecarPid` to match, e.g., `tasklist`'s view of who has the camera open.
3. **`--heartbeat-timeout` (10 s) is validated, not just a judgment call** — the live D/E run showed
   the sidecar hang (not crash) on a real unplug, and the heartbeat check is what caught it; see §7.
   10 s was not independently tuned against a false-positive case (a legitimately slow but real frame),
   so a very loaded machine could in principle trigger a spurious restart; none was observed.
4. **The static-dependency-failure path is exercised only with a missing python.exe**, not a genuinely
   broken `depthai` install — both return the same `(False, reason)` from `_validate()` and hit the
   identical terminal code path, so this is believed to generalise, but it was not independently
   reproduced with a corrupted package.

---

## Production changes

```text
new (python-sidecar~/):
  sidecar_supervisor.py     the supervisor: state machine, launch, readiness, backoff, crash-loop
                            guard, single-instance lock, logging, supervisor_state.json diagnostics
  run_supervisor.bat        thin launcher, prints + runs the exact production command
  f20b_fake_sidecar.py      TEST DOUBLE ONLY - never used in production
  f20b_failure_tests.py     automated test harness (A/B/D/E/F, all PASS - SS8)
  f20b_usb_test.py          live HUD-guided protocol; run 2026-09-14 for D/E/F/H - SS7
```

Nothing in `Runtime/` changed. F-20A's `TrackingStreamHealth`/`GetStreamHealth()` already gives an
operator everything needed to see recovery happen from the Unity side, and this task's own diagnostics
file (§21 of the brief) is explicitly scoped as a file, not a dashboard.

> **EditMode suite update (2026-09-14):** this paragraph previously said the 124-test suite "was not
> re-run" because nothing in `Runtime/` changed. It has now been run anyway, via a reflection runner
> inside the live Editor rather than batch mode (which would require closing the Editor): **170/170
> pass, 0 failures.** The "124" figure was itself an undercount — it counted the 124 parameterless
> `[Test]` methods and omitted 46 `[TestCase]` expansions. 124 + 46 = 170 resolved test cases.

---

## 14. The empty-room defect (found 2026-09-14, offline, with no person available)

The one failure mode the entire §17 matrix could not see, because every run in it had a person
standing at the camera.

**Symptom.** With the OAK-D attached, healthy, and nobody in front of it:

```text
[18:46:07] SIDECAR START pid=24460 run=1
[18:46:53] STARTUP STUCK pid=24460 not ready after 45s - killing for restart
[18:46:53] SIDECAR EXIT pid=24460 rc=1 uptime=45.4s cause=startup_stuck
[18:46:53] RESTART scheduled delay=1s
[18:46:54] SIDECAR START pid=39728 run=2      ... and so on, forever
```

The sidecar was completely healthy in every run — it opened the device, read intrinsics, configured
portrait and stereo, and printed its full banner including `F-21 target ownership ON` and `F-22 pose
validation ON`. It was killed anyway, on a 45 s cycle, and would reach `FAILED_PERMANENT` after 5
restarts (~4 minutes), after which it retries only every 60 s and each retry fails the same way.

**Root cause.** Readiness is "the SID banner, then the first `frames=` line" (`SID_MARKER` /
`FRAME_MARKER`). That contract is sound. What broke it is *where* the sidecar prints that line:
`wholebody_udp_sender.py`'s periodic status print sits **after** `build_body_landmarks` and the UDP
send, so every `continue` above it skips the heartbeat entirely — the "no hip depth" path, and, since
F-21 landed, any frame ownership withholds. `frames` itself increments correctly at the top of the
loop; only the *printing* of it was gated behind having a subject.

So the heartbeat was answering "is a person being tracked?" while the supervisor was asking "is the
camera loop alive?". Those are different questions, and conflating them makes an empty room
indistinguishable from a dead pipeline.

**Fix**, in the sidecar rather than the watchdog, because that is where the defect is: an idle
liveness line emitted when the detailed one has been starved for 3 s (longer than its own 2 s
cadence, so it never doubles up during normal tracking):

```text
[wb] frames=76 sent=0 idle - camera loop alive, no pose emitted (no subject / withheld) fps~25.3 age=30ms stale=17
```

It carries `frames=`, so the existing readiness contract is satisfied unchanged, and it is more
informative than the old behaviour ever was: an operator can now tell *camera healthy, nobody there*
from *camera dead* at a glance, which was previously impossible.

**Verification, same conditions, real hardware, empty room:**

```text
[18:50:46] SIDECAR START pid=40328 run=1
[18:50:53] SIDECAR READY sid=1a61dbe970d3 pid=40328 elapsed=6.777s
   ... 72.7 s continuous uptime, RestartCount=0, 23 idle heartbeat lines, no person present
```

And the automated suite, which previously could not pass without a human in frame:

```text
before the fix (empty room)          4/6   A_NORMAL_START FAIL, B_FORCED_KILL FAIL
after the fix  (empty room)          6/6   A ready=7.4s; B kill#1 recovered 9.2s, kill#2 10.1s,
                                            each with a new session id
```

**Why this was invisible until now.** It needs the *absence* of a person to reproduce — the opposite
of what every previous live session had. It is a good argument for keeping a no-human offline pass in
the routine rather than treating hardware sessions as strictly better.

---

---

## 15. Deployment hardening verification (2026-09-14) — 35/35 PASS

`f20b_deployment_verify.py` covers what `f20b_failure_tests.py` does not: the seams between the
supervisor and the machine it has to survive on.

```text
1 run_supervisor.bat        the command it actually runs (not its prose) launches the supervisor;
                            every flag it passes still exists in the supervisor's CLI; its default
                            model path resolves; it cds to %~dp0 so a logon launch has the right cwd
2 logon registration        the documented schtasks command is well-formed, points at the same .bat
                            verified in check 1, and that path exists. NOT registered on this machine
                            and deliberately NOT executed by the script - a persistent system change
                            stays an operator action, which is this report's own SS12 position
3 duplicate guard           second supervisor aborts: "another supervisor already owns lock_port=8907"
4 diagnostics after logon    a seeded previous-boot supervisor_state.json is fully overwritten - no
                            stale sid, no stale RestartCount; all 9 operator fields present;
                            supervisor.log + supervisor_events.jsonl both present for post-mortem
5 no duplicate sidecars     exactly one producer tree after a clean start, after a forced-kill
                            restart, and none at all after the supervisor stops
6 F-20A reconnect           three real producer launches -> three distinct 12-hex session ids
```

**One operational fact worth recording, found while writing check 5.** This venv's
`.venv\Scripts\python.exe` is a **launcher shim** that spawns the real interpreter
(`C:\Program Files\Python310\python.exe`) as its own child, so a single healthy sidecar is always
**two** python processes: the shim the supervisor tracks as `SidecarPid`, plus its child. An operator
running `tasklist | findstr python` will see two and should not read that as a duplicate. It is also
the concrete reason the existing `taskkill /T` is load-bearing rather than defensive: killing the
shim alone would orphan the real interpreter, which would keep holding UDP 8899 and the camera.

The F-20A reconnect claim was additionally closed across the language boundary rather than by
inspection: the three real session ids above were fed to the **real shipping C#**
`TrackingStreamHealth` in the live Editor — `FirstSession` once, `NewSession` on each restart, 117
`SameSession`, and a late straggler from the long-dead first producer correctly classified
`OldSession` rather than re-adopted (which would have flushed a healthy buffer).

---

```text
SUPERVISOR DECISION:
Adopt a dedicated Python supervisor process (not a Windows Service, not a bare .bat loop, not Task
Scheduler alone) that starts the sidecar with the explicit, logged production command, detects both
process exit and a silent stdout-heartbeat stall, and restarts with a measured backoff table that
never produces a tight crash loop.

RECOVERY DECISION:
Two independent forced kills against the real sidecar and camera both recovered with a new session id
in 7-8 seconds; a real USB unplug produced a silent hang rather than a crash, which the heartbeat
check (not process-exit detection) caught and restarted; five consecutive device-open failures with no
camera correctly tripped a throttled FAILED_PERMANENT retry rather than a tight crash loop or a
permanent death, and recovery time after that point is dominated entirely by where the replug happens
relative to the backoff schedule (36 s to 119 s, both observed live on the identical code path).

UNATTENDED-INSTALLATION DECISION:
The full SS17 test matrix passes on real hardware: normal start, forced kill (x2), the duplicate-guard,
crash-loop protection, a real USB unplug mid-session (with the sidecar hanging rather than crashing -
caught anyway), a cold start with the camera already absent, and Unity being stopped and restarted
while the supervisor/sidecar stayed alive throughout, with the avatar recovering live and unattended in
every case - no Unity restart, no scene reload, no operator shell intervention.

EMPTY-ROOM DECISION (2026-09-14):
A liveness heartbeat must report that the CAMERA LOOP is alive, which is not the same question as
whether a SUBJECT is present. The sidecar's frames= line was gated behind a successful pose emission,
so an empty room - the normal state of an unattended installation - read as a stuck startup and the
supervisor killed a healthy sidecar every 45 s, reaching FAILED_PERMANENT in ~4 minutes. Fixed in the
sidecar (idle heartbeat), not in the watchdog, because the watchdog's contract was never wrong. The
automated suite now passes 6/6 with nobody in front of the camera, which it could not do before.

NEXT ENGINEERING ACTION:
Consider lowering --failed-permanent-retry (currently 60s) if this installation expects camera outages
to be brief, since it is now the dominant term in worst-case recovery time (SS13); optionally register
run_supervisor.bat with Task Scheduler for logon-time auto-start (SS12, verified statically in
f20b_deployment_verify.py check 2, still deliberately not automated); then return to F-21
(single-person lock / target ownership).
```
