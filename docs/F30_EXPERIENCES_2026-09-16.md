# F-30 — Seven experiences, seven scenes

```text
VERDICT: BUILT AND COMPILING. Seven experience scenes on one shared tracking core, each a camera
         and one GameObject.
TESTED:  TrackedStage — the core all seven share — driven over a real socket with recorded
         production packets: 7/7 checks. Plus 3 velocity checks and F-29's 23 EditMode tests.
NOT RUN: nothing here has been rendered on a screen. See §6, and read it before demoing.
```

> **Accuracy is explicitly not the gate.** The brief was to build presentable work and improve it
> from there. So each experience is designed to *degrade honestly* rather than to require precision
> the tracking has not been shown to have: where something needs close range it says so on screen,
> and where it can work at distance it avoids depending on the hands.

---

## 1. The scenes

| scene | what a visitor does | what it demonstrates | needs hands? |
|---|---|---|---|
| **AirGraffiti** | pinch and draw in the air | individual finger tracking | **yes** — close range |
| **BubblePop** | swing at bubbles, 60 s, score + combo | whole-body speed, a game loop | no |
| **PoseMatch** | copy a ghost pose, joints turn green | accuracy a viewer can judge themselves | no |
| **DepthReach** | reach *into* rings at three distances | metric depth — the OAK-D's reason to exist | no |
| **Footprints** | stand still; the floor remembers | F-29 foot contact + the measured floor | no |
| **Fluid** | move; the space swirls and keeps swirling | persistence — the room is *changed* by you | no |
| **ObjectPlay** | bat, kick, head, pinch-and-throw objects | state that outlasts the player | optional |

All seven run the same sidecar on the same port. Nothing about the tracking changed.

## 2. The one architectural decision

**`TrackedStage` owns everything between the socket and a usable body**, and every experience gets
it for free: the UDP provider, the space converter, the F-27 humanized layer *and its once-per-pose
rule*, the world-space pose, both hands, the trust channel, the attract loop, the presence gate and
the floor grounding.

The alternative was ~120 lines copied per scene, and a copy of *this* chain is worse than most,
because the subtle parts are exactly what a copy gets wrong: advance the humanized layer once per
**pose** not per frame (F-27 measured that per-frame advancement makes the stream jump *more*);
never let the attract figure carry telemetry; keep the grounding loop slower than the floor
estimator it feeds back through.

**It is a plain class, not a MonoBehaviour, deliberately.** As a component, each experience would
read it in its own `Update` and Unity's arbitrary execution order would decide whether the pose was
this frame's or last frame's. `ExperienceBase` calls `Stage.Tick(dt)` and *then* calls `Play(dt)` —
the ordering is in the code rather than in a project setting nobody looks at.

`ExperienceBase` adds the shared scaffold: camera, bloom volume, floor grid, HUD, and the keys every
scene shares (`R` reset, `H` humanized, `A` attract, `G` ground). A subclass owes three things: a
title, an instruction, and what to build and play.

### The attract rule

Every experience inherits `ScoringAllowed`, and it exists because of one failure mode: **the attract
figure is synthetic**. It is there so an empty room looks alive. Drawing it is right; *scoring* it is
not — a high score set by the demonstration loop is a lie and is the first thing anyone notices.
Bubble Pop's clock does not run for it, Footprints leaves no marks for it, Pose Match shows it the
target but counts nothing.

Fluid is the deliberate exception: nothing is scored there, and a figure moving through a living
fluid in an empty room *is* the attract state that scene wants.

## 3. What the experiences needed from the tracking layer

**`SkeletonPose.Velocity`** — per-joint world velocity, new here. Speed says a hand is moving but not
*which way*, and both the fluid and the object strikes are directional.

**And the trap that came with it, which is documented on the field and worth repeating:**

```text
|Velocity| IS NOT Speed.
  straight-line motion   they agree exactly          measured 1.000 vs 1.000 m/s
  reversing motion       |Velocity| < Speed           measured mean ratio 0.678 on a dancer
  always                 |Velocity| <= Speed          measured max ratio 0.999
```

Speed smooths the *magnitude* of each frame's displacement; Velocity smooths the *vector*. On
oscillating motion the vector average partly cancels and the magnitude average does not. So: **take
direction from `Velocity` and magnitude from `Speed`.** Using `|Velocity|` as a speed silently
under-reports a hard swing by about a third — which is exactly the vigorous movement an experience
most wants to reward, landing as the weakest hit. The fluid and the object strikes both do it the
right way round.

## 4. Notes worth keeping per experience

**Air Graffiti** draws from the *index fingertip*, not the wrist — the point a person aims with; the
wrist lags a gesture by a hand's length and reads as the system being slow. Pinch is the trigger
because it has a clean edge; anything position-based leaves accidental marks between strokes.

**Bubble Pop** spawns in *body space*, scaled to the player's own shoulder width, so a tall and a
short visitor both get a reachable field. A hit needs **speed as well as proximity** — without the
speed term the whole field clears itself the moment somebody walks through it.

**Pose Match** scores in *torso units*, not metres, so an adult and a child are judged on whether
their **shape** matches. A pose must be *held* ~0.4 s, or flailing wins. Hips and shoulders are
excluded from scoring because they are the frame the pose is measured against and would always
score perfectly.

**Depth Reach** tests lateral and depth error **separately** rather than as one 3D distance, so the
HUD can say *"on target, 23 cm too far back"* — naming the axis a flat camera cannot measure at all.
On a monocular source it says plainly that depth is inferred rather than measured.

**Footprints** is built on `SkeletonPose.Planted` and is the F-29 foot work doing the job it was
measured for. A mark is placed once, where the foot **first** settled, and never moved — because
floor contact was measured reliable to ~21 mm at 1.4 m but only ~81 mm at 2.9 m, and a continuously
tracked mark would wander across the floor for a far-away visitor. It is also the only experience
that does **not** reset per visitor: accumulating across people is the entire point.

**Fluid** is a real velocity field — injection, advection, dissipation — with no pressure solve. That
omission is deliberate: a pressure solve is what makes fluid expensive, and it buys physical accuracy
nobody watching can distinguish from this swirl. The field's *persistence* is what makes the space
feel changed by you rather than merely reactive.

**Object Play** hand-writes its physics rather than using Unity's, for three reasons in order of
weight: the tracked body is not a rigidbody and never will be — it teleports between frames, and a
physics engine resolves a teleporting collider by launching whatever it touches across the room; the
joints' measured velocity is the interesting input and a kinematic collider throws it away; and this
needs no colliders, no fixed timestep and no project settings.

## 5. What changed

```text
Runtime/SkeletonShow/
  TrackedStage.cs                  NEW  socket -> usable body, owned once
  ShowStage.cs                     NEW  camera, bloom, floor grid, primitives
  SkeletonPose.cs                  +    per-joint Velocity (and the |Velocity| != Speed note)

Runtime/Experiences/               NEW assembly, VirtualMirror.Experiences
  ExperienceBase.cs                     scaffold: staging, HUD, keys, the attract rule
  BodyRenderer.cs                       one body, drawn the same way in every scene
  AirGraffitiExperience.cs
  BubblePopExperience.cs
  PoseMatchExperience.cs
  DepthReachExperience.cs
  FootprintsExperience.cs
  FluidFieldExperience.cs
  ObjectPlayExperience.cs

Scenes/                            NEW  AirGraffiti, BubblePop, PoseMatch, DepthReach,
                                        Footprints, Fluid, ObjectPlay
```

Nothing in the mirror app or the sidecar changed. `VirtualMirror.App`, `.Tracking`, `.Core`,
`.SkeletonShow` and `.Tests` all still compile, and F-29's 23 tests still pass.

## 6. What this does NOT prove — read before demoing

1. **Nothing has been rendered on a screen.** Three Unity Editors held the project lock all
   session, so verification is: every assembly compiles; `TrackedStage` is driven over a real socket
   with real recorded packets (7/7); velocity is checked against known motion (3/3); F-29's 23
   EditMode tests still pass. **Every experience's own rendering and feel is unverified** — colours,
   sizes, bloom interaction, whether a bubble is reachable, whether the fluid reads as fluid.
   Expect to tune constants on first run. They are all named constants with reasons attached, which
   is what makes them tunable.
2. **The scene files were generated, not authored in the Editor.** Each is the F-28 scene with its
   component swapped, and the script GUIDs are deterministic (md5 of the asset path). Unity will
   reimport them on first open; if a component shows as missing, the `.meta` GUID and the scene's
   `m_Script` guid have diverged.
3. **No live camera, ever.** All of it is recorded video through the production wire.
4. **Single person only**, unchanged — RTMW3D-x is single-person top-down. Nothing here is
   multi-person and nothing should be deployed where a second person can enter frame.
5. **Hand-dependent experiences need close range.** Air Graffiti needs hands; Object Play's grabbing
   does. At ~2.9 m only 59.9% of hand observations are anatomically plausible. Both say so on screen
   rather than failing silently, but Air Graffiti at 3 m will be frustrating.
6. **`SkeletonShowBootstrap` still has its own copy of the tracking chain.** `TrackedStage` was
   extracted for the experiences but the F-28/F-29 scene was deliberately left alone, because it is
   the one scene carrying verified evidence and a refactor could not be visually checked this
   session. **This is the top follow-up** — two copies of that chain will drift.

## 7. Evidence

```text
docs/evidence/f30/
    acceptance_trackedstage.txt     7/7 checks, real provider, real socket, recorded packets
    F30AcceptanceHarness.cs.txt     the harness source, for reproduction
```

The harness runs with the humanized layer **off**: `HumanizedSkeleton` calls
`Quaternion.LookRotation`, a Unity native ECall that cannot execute outside a player. Its own
correctness is covered by `HumanizedSkeletonSelfTest`; what this run covers is everything around it
in `TrackedStage` — presence, attract, staging, grounding, telemetry and velocity.
