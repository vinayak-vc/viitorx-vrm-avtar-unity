# F-34 — Nine crowd experiences, and the rule that decided which ones exist

Date: 2026-09-17
Decision: **ADR-072**
Evidence: [`docs/evidence/f34/`](evidence/f34/)

> **The evidence link may be dead in a clone.** `docs/evidence/` was added to `.gitignore`
> during this work by a change outside it (uncommitted at the time of writing). Files already
> tracked are unaffected, but `evidence/f34/` is NOT tracked and will not arrive with a clone.
> `tasks.md` already carries the open question of whether `evidence/` belongs in a distributed
> SDK at all; this note only records that the answer currently in the working tree is "no",
> and that F-34's evidence was produced before that was decided.
Depends on: F-30 (the experience scaffold), F-31 (sound), F-32 (multi-person on the wire),
F-33 (per-person filters)

---

## 1. What shipped

Nine new scenes, each one `ExperienceBase` subclass plus a `.unity` asset that is a camera and one
GameObject — the same shape as the ten that already exist.

| Scene | Class | What a visitor does |
|---|---|---|
| `CollectiveFluid.unity` | `CollectiveFluidExperience` | Everyone stirs one fluid; the wakes merge and outlive the people who made them |
| `StillnessTug.unity` | `StillnessTugExperience` | A light drifts toward whoever is stillest |
| `CrowdFootprints.unity` | `CrowdFootprintsExperience` | The floor remembers where everybody stood, all day |
| `Eclipse.unity` | `EclipseExperience` | Whoever is nearer the camera takes your light |
| `Chord.unity` | `ChordExperience` | Each person is a note; the metres between you are the interval |
| `Chain.unity` | `ChainExperience` | Hold hands; current flows; the whole room ignites when everyone is linked |
| `Pass.unity` | `PassExperience` | One ball, one rally, counted for the room rather than per player |
| `Podium.unity` | `PodiumExperience` | One live crown: tallest, then stillest, then fastest |
| `MirrorEachOther.unity` | `MirrorEachOtherExperience` | Two people hold the same shape; the screen scores how alike you are |

Plus four prerequisite fixes and five shared classes — §4 and §5.

---

## 2. The one rule that sorted every idea

**An identity switch is not a jump anybody can catch.**

The live two-person session measured an identity migrating between two humans at **0.015 m per
frame** against a **0.35 m** association margin — 97 datagrams, **zero events logged**. There is no
discontinuity in the geometry and therefore no guard to write. The obvious defence — "detect a switch
and freeze the score" — cannot be built, because nothing announces one.

So the defence has to be structural:

> **An experience survives a switch if it is SYMMETRIC IN ITS PARTICIPANTS and reads only the current
> frame.**

That test sorted the whole list. It is why there is no per-player scoreboard anywhere in these nine,
and why the three places identity *is* read are named out loud in §6.

**`Scenes/Bonds.unity` already is the "constellation" idea** — per-person colour, pair lines that
brighten as people close, a flare when hands touch. It was not rebuilt. Its private colour table
moved to `CrowdPalette` so a person keeps the same colour in every crowd scene; nothing else about it
changed.

---

## 3. The budget, settled before anything was designed

**20.43 ms per person, p50 over 400 warm inferences** (p90 20.89, p99 21.69). The shipped 20.7 ms
figure was right; a competing 17.88 ms claim does not reproduce.

| People | Pose cost | Stream lands near |
|---|---|---|
| 1 | 20.4 ms | 21 fps (as shipped) |
| 3 | 61 ms | **16 fps** |
| 4 | 82 ms | 12 fps |

**The real budget is three people at 16 fps.** The detector sees seven; the GPU affords three. Every
scene's rendering cap is 8 because that is what `CrowdFrame` carries on the wire, and every one of
them says so in a comment: it is a *rendering* ceiling, not a claim about what will be posed.

This is also why §4's third fix exists.

---

## 4. Four things fixed before any scene was built

### 4.1 `SkeletonPose` had no way to forget its history

`Speed` and `Velocity` are differences between this frame's world position and the last one's. That
is only meaningful while both belong to the same body measured against the same origin. There was no
way to say "those two are not comparable".

Added `SkeletonPose.ResetHistory()` plus an automatic guard at `TeleportSpeed = 12 m/s` of apparent
mid-hip movement — expressed as a speed and multiplied by the frame's `dt`, so the same guard holds
at 16 fps and at 60.

**It catches two things and misses one, and the miss is the important part:**

* **A grounding snap.** `TrackedStage` snaps its ground offset the first time it has a floor, which
  can move the staging origin by the better part of a metre in one frame. The next frame then
  differences two positions measured against *different origins* and reports a spike on the whole
  body — above every strike gate at once (0.9 m/s in Bubble Pop, 0.55 in Objects) and enough to
  un-plant every planted foot (which needs speed under 0.35). This was a live latent bug, not a
  hypothetical. Pinned by `TeleportGuard_SuppressesTheSpikeFromAGroundingSnap`.
* **A gross identity switch**, where a pose under one id jumps to a body somewhere else in the room.
* **It does NOT catch a slow switch** — 0.015 m/frame is roughly 0.24 m/s, three orders of magnitude
  below the guard and well inside ordinary movement. That is precisely why the nine scenes are shaped
  the way they are. The guard is not the defence; it is only tidy-up for the cases that *are*
  detectable.

The floor is deliberately kept across a reset: it is a property of the room, and re-estimating it
would make every floor effect jump at the exact moment the guard fired.

### 4.2 `ExperienceBase` reset on any presence change

`Stage.Presence` answers "is there **a** person", which is right for one visitor and wrong for
several: with three people playing it never changes, and a fourth walking in wiped everybody.

Added `ResetsOnNewVisitor` (default **true**, so the ten existing scenes are untouched) and
`OnPersonArrived` / `OnPersonLeft`, driven by a new `CrowdRoster` that diffs the crowd **by track
id**. All nine new scenes set it false. `CrowdRoster` exists for *releasing what was allocated to a
person who has gone* — an audio voice, a colour slot — and explicitly not as a licence to accumulate
per-person state: departure is observable, substitution is not.

### 4.3 Three speed constants, scattered and frame-rate coupled

0.9 (Bubble Pop), 0.55 (Objects) and 0.35 (`PlantedMaxSpeed`) were bare literals in three files.
Gathered into `ExperienceTuning`, which also carries the caveat that made them worth gathering:

> They were tuned at ~21 fps and the crowd scenes run at ~16. A speed in m/s is not frame-rate
> coupled by definition, but the way it is *measured* is — `Speed` is a smoothed difference between
> consecutive samples, so coarser sampling cuts the corner off a reversing movement and
> under-reports exactly the vigorous gesture a strike gate is looking for. The effect is one-sided:
> a crowd scene reads **slower**, so these gates get **harder** to pass, not easier.

**They have NOT been re-measured at 16 fps.** Doing that needs the camera, three people and a
capture. Nudging them until they felt right would have been inventing a measurement.
`ThresholdsStillMatchTheValuesTheyWereTunedAt` pins the current values so a future re-measurement is
a deliberate, visible change rather than a drift.

### 4.4 `Render(SkeletonPose)` had no tint

`RenderRaw` had one and `Render` did not, which is the only reason Bonds draws flat bodies — it had
to drop the speed palette, the confidence-in-line-width rule and per-joint heat to get a per-person
colour. Added `Render(pose, tint)` as an overload; the existing signature is unchanged and defaults
to white.

The doc comment carries the warning that goes with it: **colour already means speed**, everywhere,
and two meanings on one channel is unreadable — Time Echo hit this and chose to drop speed entirely
rather than blend them. Each scene picks one.

---

## 5. What is shared, and what was deliberately not

New shared classes under `Runtime/Experiences/`:

| Class | Why it is shared |
|---|---|
| `CrowdPalette` | A visitor who is cyan in one scene and orange in the next has been told, wrongly, that the system lost them |
| `CrowdRoster` | Arrivals and departures by id — §4.2 |
| `ExperienceTuning` | The three thresholds and their caveat — §4.3 |
| `CrowdMath` | The two measures a crowd scene may take, as **pure functions**, so their symmetry is asserted by tests instead of argued for in comments |
| `CrowdVoices` | Polyphonic sustained voices for Chord, tuned from `ExperienceAudio.PentatonicHz` so there is still exactly one scale |
| `FluidField` | Lifted unchanged out of `FluidFieldExperience` — the crowd version is not a different simulation, only a different set of things allowed to push it |
| `FloorMarks` | Lifted unchanged out of `FootprintsExperience` — the crowd version is the same floor with more feet on it |

**Two shipped scenes were migrated** onto `FluidField` and `FloorMarks` rather than having the logic
copied. Same grid, same decay constant, same injection falloff, same 0.22 m merge radius, same
ten-minute fade. Two copies of a fluid drift apart the first time either is tuned, and a viewer
moving between the two scenes would feel the room behave differently for no reason.
**`Fluid.unity` and `Footprints.unity` therefore need a play-mode smoke test** — see §8.

**`ObjectPlayExperience` was NOT migrated.** Pass diverges from it for a measured reason, not an
accidental one: Objects grabs by pinch, and hand landmarks arrive on their own channel **for the
primary person only**, with plausibility falling from 99.1% on a single subject at 1.4 m to 59.9% on
the seven-person clip. A crowd scene cannot be built on a channel most of the crowd does not have. So
Pass bats only, its ball physics has no held state, and sharing the two would have complicated both.

---

## 6. The three places identity is read, named

Every other scene reads only the current frame. These three do not, and each says so in its own
header:

1. **Colour** (`CrowdPalette`, every scene). A switch makes two people trade colours. A wrong colour
   is an embarrassment; a wrong score is a lie. This is the only thing allowed to follow an id
   everywhere.
2. **A pass** (`PassExperience`). A pass is counted when the toucher is a different id from the last.
   A switch can miscount a single pass as a self-hit or the reverse. That error does not accumulate
   against a person and appears on nobody's counter — it is one off-by-one in a shared total.
3. **A crown change cue** (`PodiumExperience`). The crown itself is re-derived every frame from
   geometry; only the *chime* compares ids. The worst a switch can do is fire one chime that nothing
   depended on, because there are no totals.

**What was deliberately not built**, and it is the instructive part:

* **Head-to-head Bubble Pop** (two players, two scores) is the worst idea available. A per-person
  accumulated score is exactly the state a silent switch corrupts, in the most visible way possible:
  a player watches their points appear on somebody else's counter with nothing to explain it.
* **A hold-the-crown timer in Podium.** The obvious next feature, and the one that would ruin it. A
  hold timer is per-person accumulated state and the failure would be maximally visible. Podium keeps
  no totals at all, on purpose, and the HUD says so.
* **A per-pair streak in Mirror Each Other.** The hold timer runs while *any* pair is above threshold
  and resets when none is. A viewer reads "we are holding it", not "pair 1–3 is holding it".

---

## 7. What each scene leans on, and its honest limit

* **Collective** — `FluidField.Inject` reads a pose and nothing else, and writes `+=` into shared
  cells. Injecting A then B leaves the grid exactly as B then A would. *Uniquely* immune: there is no
  identity in a velocity field to corrupt. Asserted by `Fluid_InjectionIsSymmetricInThePeople`.
* **Stillness** — a weighted centroid over the current frame. Asserted by
  `Centroid_DoesNotCareWhoIsListedFirst`. The orb's easing is state, but it belongs to the *light*,
  not to a person. It is also the only scene besides Footprints that rewards **not** moving.
* **Traces** — a mark is a *place*. Nothing about who stood there is stored; colour means **dwell**,
  never person, which is the one decision that would have put identity back into the floor. Each
  person is planted against **their own** measured floor (F-33 gives everyone their own pose
  history); a shared plane would sink one person's marks and float another's.
* **Eclipse** — the only scene here that uses **depth ordering**, which is the one thing a flat
  camera cannot fake. It uses **relative** depth only: F-29 measured the floor to ~21 mm on a single
  subject at 1.4 m, and accuracy beyond ~2 m on one subject has **not** been measured. A comparison
  between two people cancels the common-mode part of the error. With no measured mid-hip on the wire
  there is no ordering, and the HUD says exactly that rather than inventing one from staging offsets.
* **Chord** — every pitch is pentatonic **by construction** (ADR-068), so any number of people in any
  arrangement is consonant. A voice *slot* follows an id (it is a resource); the *note* it plays is
  recomputed every frame from positions, and the mix is 2D so no voice is located anywhere.
* **Chain** — a graph over wrist positions, rebuilt each frame, with hysteresis so a link does not
  flicker. **Its risk is not identity, it is detection**: two people close enough to hold hands is
  the classic case for two detector boxes merging, and that has never been tested. The HUD watches
  for the detected count dropping while links are forming and names it as a known, unmeasured limit
  rather than letting it read as a bug in the game. **Build confidence in this one last.**
* **Pass** — the unit of score is the **rally**, which is symmetric in the people who made it. It is
  also the better game: "keep it up between you" is what you want from strangers sharing a room.
* **Podium** — the crown follows a **body**, not a name. When two labels swap, two positions do not,
  so the crown does not move and nobody watching can tell anything happened.
* **Mirror Each Other** — the strongest reuse available. Pose Match already normalises joint error by
  the player's **own** torso length precisely so a tall adult and a child are judged on shape; swap
  the authored template for the other person's live pose and the scoring is already correct. Asserted
  by `Similarity_IsSymmetricInThePair` and `Similarity_JudgesShapeRatherThanSize`.

---

## 8. What was verified, and what was not

The Unity Editor holds the project lock, so verification is headless: `dotnet build` over the
generated `.csproj` files, and a reflection runner over the built `VirtualMirror.Tests.dll`.

**Compile — 0 errors** across `VirtualMirror.Experiences`, `VirtualMirror.SkeletonShow` and
`VirtualMirror.Tests`. The only Experiences warning is the pre-existing `FindObjectOfType`
deprecation. ([`compile.txt`](evidence/f34/compile.txt))

**Unit tests — 29/29 new, 171 passed overall, 0 skipped in the new fixture.**
([`unit_tests.txt`](evidence/f34/unit_tests.txt))

The 6 remaining failures in the whole-suite run are all `SidecarPathsTests`, which need
`Application.dataPath` — empty outside a player. Untouched by this work and unrelated to it.

**Unity's own compiler was NOT the gate here, and that matters.** A forced refresh reported
`compiling` and the Editor console shows zero errors and zero warnings — but the Editor was in
**play mode** at the time (a live Mirror session with the sidecar attached), and Unity defers script
compilation while playing. The empty console is consistent with a clean build; it is not independent
confirmation of one. **Re-check the Unity console after leaving play mode** before trusting it.

**One existing test's INPUT was changed, not its assertion.**
`SkeletonShowF29Tests.Planted_RequiresBothProximityAndStillness` stepped 0.4 m per 60 Hz frame — 24
m/s, three times a sprinter's hip and not a movement a body can make. The §4.1 guard now recognises
that as the frame of reference moving, so the input is 0.1 m/frame (6 m/s): a genuinely fast foot,
far above the 0.35 m/s planted gate and far below the 12 m/s guard. A new companion test,
`Planted_IsNotFooledByTheHistoryBeingDiscarded`, pins the other side: when the guard *does* fire and
speed goes to zero, a lifted foot must still not read as planted.

**Two small source changes were made purely for testability**, and both are behaviour-neutral:

* `FloorMarks.Create` is now pure, with the visuals in `BuildMarkVisuals`. The .NET runtime refuses
  to JIT a whole method that references a native ECall outside a player, so a single `Random.Range`
  on an untaken branch made the merge and eviction rules untestable however carefully it was guarded.
* `FloorMarks` tracks `hasVisuals` as a bool rather than comparing a `Transform` to null —
  `UnityEngine.Object` overloads `==` with a native call.

### NOT verified — this needs the hardware and a person

Nothing here has been on a screen. Everything above is compilation and pure-logic assertions.

1. **Play-mode smoke test of `Fluid.unity` and `Footprints.unity`** — the two migrated scenes. Highest
   priority, because they were working before this change.
2. **Play-mode smoke test of all nine new scenes** with the sidecar running. Attract state first
   (nobody in frame), then one person, then two.
3. **Chord needs listening to.** It is mostly the sound, and the voice bank has never produced a
   sample. Check for clicks at the loop wrap and that `M` mutes it.
4. **Chain with two people actually holding hands** — the §7 detection risk. Watch the "seen /
   tracked / posed" counts as you close together; the HUD will flag a suspected merge.
5. **Eclipse needs two people at different depths.** If the HUD says "NO MEASURED DEPTH", the sender
   is not publishing a root position and the scene cannot work — that is the honest failure and not a
   bug in the scene.
6. **Re-measure the three thresholds at ~16 fps** (§4.3), with three people.
7. **Re-check the frame rate with three people in `Collective`** — it is the heaviest of the nine
   (one grid plus 1100 particles plus up to 8 skeletons).

---

## 9. Files

```text
Runtime/Experiences/CollectiveFluidExperience.cs      NEW
Runtime/Experiences/StillnessTugExperience.cs         NEW
Runtime/Experiences/CrowdFootprintsExperience.cs      NEW
Runtime/Experiences/EclipseExperience.cs              NEW
Runtime/Experiences/ChordExperience.cs                NEW
Runtime/Experiences/ChainExperience.cs                NEW
Runtime/Experiences/PassExperience.cs                 NEW
Runtime/Experiences/PodiumExperience.cs               NEW
Runtime/Experiences/MirrorEachOtherExperience.cs      NEW
Runtime/Experiences/CrowdPalette.cs                   NEW  shared per-id colour
Runtime/Experiences/CrowdRoster.cs                    NEW  arrivals/departures by id
Runtime/Experiences/CrowdMath.cs                      NEW  the two symmetric measures
Runtime/Experiences/CrowdVoices.cs                    NEW  polyphonic voices for Chord
Runtime/Experiences/ExperienceTuning.cs               NEW  the three thresholds + the caveat
Runtime/Experiences/FluidField.cs                     NEW  lifted from FluidFieldExperience
Runtime/Experiences/FloorMarks.cs                     NEW  lifted from FootprintsExperience
Runtime/SkeletonShow/SkeletonPose.cs                  +    ResetHistory() + the 12 m/s guard
Runtime/Experiences/ExperienceBase.cs                 +    ResetsOnNewVisitor + the two hooks
Runtime/Experiences/BodyRenderer.cs                   +    Render(pose, tint)
Runtime/Experiences/ExperienceAudio.cs                +    PentatonicHz(step)
Runtime/Experiences/FluidFieldExperience.cs           ~    now a thin framing of FluidField
Runtime/Experiences/FootprintsExperience.cs           ~    now a thin framing of FloorMarks
Runtime/Experiences/CrowdBondsExperience.cs           ~    colour table -> CrowdPalette
Runtime/Experiences/BubblePopExperience.cs            ~    HitSpeed -> ExperienceTuning
Runtime/Experiences/ObjectPlayExperience.cs           ~    MinStrikeSpeed -> ExperienceTuning
Tests/EditMode/CrowdExperiencesF34Tests.cs            NEW  29/29
Tests/EditMode/SkeletonShowF29Tests.cs                ~    one input changed, one test added (§8)
Scenes/{CollectiveFluid,StillnessTug,CrowdFootprints,Eclipse,Chord,Chain,Pass,Podium,
        MirrorEachOther}.unity                        NEW
```

The sidecar is **unchanged**. Nothing here needed a new wire field: F-32 put the crowd on the wire
and F-33 filtered it, and these nine are consumers of what is already there.
