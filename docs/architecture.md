# Virtual Mirror — Architecture (Agent Summary)

Canonical detail lives in SDS `04`–`11`, `16`, `18`. This file is the **agent entry** summary.

---

## Layers

```
UI / Mirror Camera
    ↓ commands
Application (session, settings)
    ↓
Domain: Retarget, IK, Calibration, Avatar session
    ↓ interfaces
Providers: MediaPipe, UniVRM, Webcam, Animation Rigging
    ↓
Infrastructure: threads, files, logs
```

---

## Pipeline

Two paths share the same Core interfaces. The **shipping** path is OAK-D (`useOakUdpTracking=1`,
`useKalidokitBody=1`, `useIkDriver=0`); the MediaPipe/webcam path remains as a fallback.

**Shipping path (OAK-D depth), as of ADR-028..031:**

```
OAK-D RGB + RGB-aligned stereo depth
  → latest-frame queue policy      (ADR-030: newest RGB, depth matched by timestamp)
  → RTMW3D inference (~21 ms)
  → depth sample 5x5/p30 + backproject
  → P0 smoother  (One-Euro + 0.35 m distal caps + bounded hold)     ADR-028
  → P1-1 JointTracker (TRACKED/WEAK/PREDICTED/LOST + plausibility)  ADR-029
  → UDP JSON  { lm[33], xyz, src, seq, t, st, own, lat }   st/own/lat = F-29 trust channel
--------------------------------------------------------------- process boundary
  → OakDUdpPoseProvider (bg thread) → PoseSpaceConverter
  → P1-3 PoseBuffer  (ring of 16; render at now − poseInterpolationDelayMs)  ADR-031
  → KalidokitPoseSolver  (positions → per-bone euler)
  → P0 LimbGate  (confidence-gated hold — the FINAL safety layer)   ADR-028
  → KalidokitControlRigDriver → Vrm10 Runtime.Process → VRM avatar
```

**Fallback path (webcam/video):**

```
Camera → MediaPipe Pose/Face/Hands → JointFilter
      → Retarget (map + vectors) → IK → VRM bones
      → Face BlendShapes + Hand fingers → URP present
```

**Layering rule for the stability stack:** P1 improves the *signal*; P0 remains the *safety
mechanism*. A joint P1 marks LOST has its emit confidence zeroed, which is byte-identical to a real
occlusion, so the LimbGate still makes the final call. Never move safety out of P0.

**Measured budget (real OAK-D, subject present):** camera→UDP 62 ms + 40 ms presentation delay
≈ **102 ms** camera→avatar. Inference dominates the sidecar half.

**Multi-person path (F-32 + F-33, ADR-070/071):** a SECOND sidecar program, not a mode. The
single-person sender above is byte-identical and remains the shipping path for the VRM mirror.

```
OAK-D RGB + depth  (the pipeline above, unchanged)
  + person-detection-retail-0013 on the same VPU   ← free: rgb 29.8 → 29.7 fps
  → PersonTracker: optimal assignment, association in 3-D using metric depth,
                   stable ids that are never reused                          F-32
  → per person, capped by --max-poses (3): RTMW3D crop → depth fuse
       → PersonFilters[track.id]: P0 → mid-hip → P1-1 → F-22                 F-33
  → UDP JSON { persons[], n, ndet, ntrack, … }  PLUS persons[0] republished at
    the ROOT in the single-person shape  ← the whole backward-compatibility story
--------------------------------------------------------------- process boundary
  → OakDUdpPoseProvider.ParseCrowd → CrowdFrame (PersonPose × N)
  → TrackedStage.UpdateCrowd → TrackedBody[]  (SkeletonPose keyed on TRACK ID)
  → CrowdBondsExperience  (Scenes/Bonds.unity)
```

Because the root fields are still populated, the VRM mirror and all nine single-person experience
scenes consume the multi-person sender **unchanged and unaware**. Verified: `root lm ==
persons[0].lm` on every packet, and `Pose` and `Bodies[0]` are the same person across 752 frames.

**The rule that both halves arrived at independently:** anything carrying per-frame temporal state
must be keyed on the **track id**, never on the tracker's emitted index — it re-sorts
most-established-first every frame. `TrackedStage` keys `SkeletonPose` that way; `PersonFilterPool`
keys the filter chains that way. Cost of getting it wrong is one person inheriting another's
history, which reads as every joint teleporting.

**Budget:** RTMW3D is 20.7 ms per person with a **fixed batch of 1** (no batch axis in the ONNX), so
2 people run at ~24 fps, 3 at ~16, 4 at ~12. The F-33 chain adds 0.88 ms per person-frame (~4%).

---

## Key Interfaces

- `IBodyTrackingProvider` / `IFaceTrackingProvider` / `IHandTrackingProvider`  
- `IAvatarLoader`  
- `IIkSolver`  
- `ICameraCapture`  
- `IAvatarLibrary`  

Composition root: `AppBootstrap` + `ServiceRegistry`.

---

## Assemblies

`Core` ← `Tracking` | `Retargeting` | `IK` | `Avatar` | `UI` ← `App`

`Core` ← `Tracking` ← `SkeletonShow` ← `Experiences`

Concretes never flow upward into Core. `SkeletonShow` and `Experiences` are the demonstration
branch: they consume the same tracking contract as the mirror app and load no VRM, no control rig
and no retarget, so the avatar path can never be broken by work on them.

---

## Threading

Inference off main thread when plugin allows; main thread applies immutable frames via double buffer. No VRM file IO on main thread.

---

## Hot Swap

Avatar, camera, smoothing, calibration, modality toggles — all without process restart.

---

## Demonstration branch (F-28 → F-30)

A second consumer of the same tracking contract, with the retarget removed entirely. F-26 established
that the debug skeleton tracks correctly while the VRM avatar does not, because a fixed-proportion
mesh driven from ~16 bone *rotations* can only aim a limb, never place it. Everything here is drawn
from joint POSITIONS, so that whole class of error is absent by construction.

```
OakDUdpPoseProvider → PoseSpaceConverter → P1-3 buffer → [ F-27 humanized ] → SkeletonPose
        │                                                                         ↓
        └─ ParseCrowd → CrowdFrame ──────────────────────────────────────────────┤
                                                                                  ↓
                          TrackedStage  (+ HandPose ×2, TrackingTelemetry, PresenceGate, grounding,
                                          and Bodies[] — one SkeletonPose per TRACK ID)
                                                                                  ↓
                            SkeletonShowBootstrap (5 modes)  |  ExperienceBase (9 scenes)
```

`TrackedStage.Bodies` degrades to a crowd of one when a single-person sender is upstream, so an
experience written for several people still works with one and no scene needs two code paths.
**Landmarks are hip-relative**, so each person is offset by their own measured mid-hip relative to
the primary's — staging them all at one origin draws the whole crowd on top of itself, measured as a
0.00 m mean pair gap across 850 frames before it was fixed.

**`TrackedStage` owns the chain once** (ADR-067). It is a plain class rather than a MonoBehaviour so
the owner calls `Tick(dt)` and *then* reads — the ordering between advancing and reading the tracking
is in the code, not in Unity's script execution order.

Rules this branch keeps:

* **A scene is a camera and one GameObject.** Everything visible is built at runtime, because a scene
  full of hand-placed particle systems is not reviewable in a diff and drifts from the code.
* **Colour means SPEED**, everywhere except the trust HUD, where it means TRUST and the legend says so.
* **The attract figure is never scored.** `ExperienceBase.ScoringAllowed` is false for it; a high
  score set by the synthetic demonstration loop is a lie.
* **Telemetry is read-only.** A mode may change what it REPORTS because of the trust channel, never
  what it DRAWS, or the distinction between the tracking and its diagnosis stops meaning anything.

**Known debt:** `SkeletonShowBootstrap` predates `TrackedStage` and still carries its own copy of the
chain (ADR-067, Consequences).

---

## Extension

New tracker = new provider. New IK = new `IIkSolver`. Do not couple UI to MediaPipe or UniVRM.
New experience = a new `ExperienceBase` subclass plus a scene containing a camera and one GameObject.
A multi-person experience reads `TrackedStage.Bodies` and may key state on `TrackedBody.Id` — the id
is stable while the person is held and never reused, so a score or a colour cannot leak to a later
visitor. Check `IsScorable` before scoring: a LOST person is worth drawing (flickering a body out
because somebody walked in front of them looks far worse than a briefly stale pose) but not scoring,
and the attract figure must never be scored at all.
