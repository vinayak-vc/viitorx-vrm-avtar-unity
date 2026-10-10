# F-46 — Demonstration run of show (solo shoot)

Print this. It is meant to be held, not scrolled.

> **The shoot is also the verification pass.** Everything F-43/44/45 changed is unverified on
> camera: the tilt sign, the ~4.5 m range claim, the FP16 keypoint tail. Act 1 tests the tilt, Act 2
> tests the range, and every act tests FP16. If a take looks wrong, that is a finding, not a retake.

---

## 0 · Before you roll

```bash
python-sidecar~/.venv/Scripts/python.exe tools/capture/f46_preflight.py
```

Twelve checks, one verdict. **Do not start until it says READY.** If the mount is deliberately
angled, re-run with `--tilt-set <degrees>` so a compensated tilt reads as compensated.

Then, by hand:

- [ ] **Light from behind the camera.** Nothing bright in frame, no window behind you. Backlight
      turns you into a silhouette, which loses limbs before it loses the person.
- [ ] **Contrasting clothes.** Dark trousers against a bright floor is the exact condition that
      dropped both legs in SR5.
- [ ] **Camera height and distance** — portrait ≈ 0.82 m high. For crowd scenes, landscape ≈ 1.15 m,
      2.0–2.5 m. **Stand at 1.6–2.0 m, not 0.90 m.** The 0.90 m figure predates F-44 and is now *outside*
      the operating volume (near limit 1.35 m, F-48 / ADR-084): the figure will freeze, then disappear
      under `TOO CLOSE - step back`. SR7 spent minutes at 0.91–1.28 m and that is what broke it.
- [ ] **Phone on a tripod** seeing you *and* the monitor in one frame. This is the master shot.

---

## 1 · The two cuts

| | length | HUD | music | purpose |
|---|---|---|---|---|
| **Proof cut** | 6–8 min | on | none | the engineering record — long takes, failures left in |
| **Showcase cut** | 90–120 s | off | yes | the thing people actually watch |

One shoot, two edits. Shoot for the proof cut; the showcase is a subset of it.

**The most important shot is you and the screen in the same frame.** A screen recording alone cannot
prove the tracking is live or that it is tracking *you*. Run the two-shot continuously as your
master and cut to clean screen capture for detail.

---

## 2 · The director

You cannot perform at 0.9–4 m and operate a keyboard at the same time, and holding a clicker
occupies the hand you are trying to demonstrate. So the sequence runs itself:

| key | does |
|---|---|
| **F10** | start / stop the **LIVE** sequence (acts 1, 2, 3, 6) |
| **F12** | start / stop the **CROWD** sequence (act 4, recorded source) |
| **F9** | captions on / off — **on** for the proof cut, **off** for the showcase |
| **F11** | skip to the next beat now |

It loads each scene, switches SKELETON SHOW modes, counts down on screen, and allows an extra 20 s
whenever a beat crosses between the single-person and crowd columns, because that swap costs 15–20 s
of no tracking (ADR-076). Press F10 and walk to your mark.

---

## 3 · Run of show

### Act 0 — the receipts *(proof cut only, 10 s)*
Film the sidecar's startup banner. Five lines state the whole configuration:
```
model: rtmw3d-x-fp16.onnx (185 MB)
rgb 800x1280   depth 800x1280
stereo: ... subpixel=on(1/8) ... mono=800p(1280x800) rgbIsp=1/1
IR dot projector: on (80%, driver LM3644)
```
For a technical audience this is worth more than a minute of footage.

### Act 1 — it is you *(SKELETON SHOW, glow — 48 s)*
| beat | direction | proves |
|---|---|---|
| 1 | Walk in. Raise your **right** hand, then your left. | the mirror is the right way round |
| 2 | Walk left, right, toward the camera, away. | the body follows you; the floor pool goes with it |

**This is the tilt test.** If you sink into the floor as you approach and rise as you leave, stop —
the sign of `cameraTiltDegrees` is inverted. Negate it and re-shoot.

### Act 2 — it is precise *(64 s)*
| beat | scene / mode | direction | proves |
|---|---|---|---|
| 3 | SKELETON SHOW · HANDS | Open and close both hands. Count on your fingers. | 133 keypoints — every finger |
| 4 | SKELETON SHOW · TRUST HUD | Turn side on. Let one arm fall behind you. | it reports what it cannot see |
| 5 | DEPTH REACH | Reach toward the camera, then far away. | real measured Z, not apparent size |

**Also your range test:** during beat 5, walk out to 4 m and back. That distance has never been shot.

### Act 3 — it is a product *(64 s)*
| beat | scene | direction |
|---|---|---|
| 6 | BUBBLE POP | Pop them — both hands and your feet. |
| 7 | AIR GRAFFITI | Pinch and draw. Release to stop. |
| 8 | FLUID | Sweep both arms slowly, then fast. |
| 9 | TIME ECHO | Move, then hold completely still. |

### Act 4 — many people *(separate take, 64 s)*
Driven from `456.webm` — seven dancers — because you are shooting alone.

**Order matters. Start the harness BEFORE pressing Play**, and shoot this as a separate Unity
session from the live acts:

```bash
.venv/Scripts/python.exe tools/video/f32_multiperson_video.py --video ../../video/456.webm --synth-depth
```

`SidecarBootstrap` listens on UDP 8899 for two pose frames before it spawns anything, and attaches to
an existing producer instead — *"something is already sending poses on UDP 8899; attaching to it
instead of starting a second sidecar."* So with the harness already running, Unity uses it and never
starts the camera sender. Doing it the other way round means killing a supervised process, which the
supervisor will simply restart — that is F-20B working correctly and it will fight you.

Then **F12** in Unity: BONDS → CHAIN → PASS → COLLECTIVE.

> **Label this on screen, and the captions already do.** A video file has one camera, so the harness
> runs with **no stereo depth** — its own header says so. This act demonstrates detection, identity
> tracking and rendering for N people. It does **not** demonstrate measured depth. Saying so costs
> ten seconds and is the difference between a proof and a advert.

### Act 5 — it survives *(one unbroken take, 20 s)*
**Nobody films this and it is your strongest engineering claim.**

On camera, open Task Manager and kill `python.exe`. Tracking stops. Wait. The supervisor restarts it
and tracking returns — F-20B measures recovery at 6.8–9.0 s. **Do not cut.** One continuous shot of
a system repairing itself says more than any experience scene.

### Act 6 — the product *(24 s)*
VIRTUAL MIRROR. Stand square, turn your torso, wave. End here — it is what the project is for.

---

## 4 · Capture

- **OBS** for the two-shot master and anything showing the sidecar preview or a terminal.
- **Unity Recorder** (Package Manager) for clean scene-only captures at a fixed frame rate.
- **Record at 1080p60.** Capturing a 30 fps app at 30 fps produces judder, and smooth motion is the
  entire point.

---

## 5 · What to do if something looks wrong

Do not quietly retake. Each of these is a known open item and the footage is evidence:

| symptom | almost certainly |
|---|---|
| body sinks as you approach | tilt sign inverted — negate `cameraTiltDegrees` |
| a leg missing at startup | the SR5 bug, still not root-caused. **Switch to TRUST HUD and film it.** |
| skeleton frozen, `sent` not rising | detector stall (F-42). `--det-timeout` should exit and restart it |
| a limb snaps once, briefly | possibly the FP16 tail (~1 joint in 1000). Note the timestamp |

The last two have never been observed since they were fixed or introduced. Catching either on camera
is worth more than a clean take.

---

## 6 · F-48 acceptance take (not yet filmed)

F-48 refuses out-of-range poses and teaches the calibration only from stereo-measured depth. It is
implemented and unit/integration-tested; **nothing about it has been seen on a camera.** This short take is
what turns the near/far limits (1.35 m / 4.5 m — derived, not measured) into measured ones. Mark the floor at
**1.0 m, 1.8 m, 3.0 m and 4.0 m** before you roll. Use the SKELETON SHOW, TRUST HUD mode (key **4**): its bottom
block carries the lines to read.

Start the sender the way you started SR7 — **with `--no-ownership`**, so F-21 cannot confound this take.
F-21 is still broken (ADR-083); nothing here tests it.

| stand at | for | you should see | what it proves |
|---|---|---|---|
| **1.0 m** | 15 s | amber `TOO CLOSE - step back`, `VOLUME TOONEAR`, `near` rising, `held` rising; the figure freezes for ~0.75 s then is withheld; **no attract figure**; `calibration` does not gain bones | it reports instead of corrupting |
| **1.6–2.0 m** | 30 s | `VOLUME TRUSTED`, `calibration` climbing to `24/24 bones ready` (some hand/foot bones may lag), `trunk+head` ≈ 0.6–0.9 m, `unmeasured-depth` small | calibration becomes ready, proportions stable |
| **3.0 m, then 4.0 m** | 20 s each | `VOLUME TRUSTED` or `TOOFAR`; read the TRUST HUD `DEPTH n/33 MEASURED` | the real measured-depth coverage, and where `TOOFAR` should start |
| **walk 2.0 → 1.0 → 2.0 m, twice** | — | at the boundary: figure holds, then `TOO CLOSE`; on return `VOLUME SETTLING` for ~0.2 s then `TRUSTED`; **the figure never rushes the lens and the skeleton does not stay stretched** | no camera-rush, no permanent stretch, no silent stale state |
| **HANDS mode (key 5) at 1.0 m, then 1.8 m** | 10 s each | at 1.0 m: `TOO CLOSE` / `REJECTED ... landmark N palms from the wrist`; at 1.8 m both hands `PINCH`/`open` | a bad hand is refused, a good one is not |

Write down, per mark: the `VOLUME` line, the three `rejected` counters, `calibration n/24`, `maxCorrection`,
`maxBoneError`, and the hip depth the sidecar HUD printed. **If the figure freezes at a distance you consider
fine, that is a finding about the limit, not a retake** — move `Near`/`Far` in `AppBootstrap`'s
`Operating Volume (F-48)` and record why.

What this take does NOT cover: crowd bodies, the avatar's finger curls, and F-21.
