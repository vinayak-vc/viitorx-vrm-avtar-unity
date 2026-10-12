# Showcase.Shared - the tracking code every showcase uses

Assembly `Showcase.Shared`, namespace `ShowcaseShared`. Chromatic Rain and Fluid Disruption are built on it and depend on nothing
else of each other; a new showcase does the same. **Anything that changes how a person is tracked, placed or kept applies to every
showcase because it lives here, not in one of them.**

```
Scripts/  ShowcaseTrackingSource   the live / simulated / auto choice, the truthful status chip, the live CROWD (ICrowdSource)
          ShowcaseCrowd            who is on screen: a slot per sidecar track id, each person placed by their own root, a person
                                   who disappears kept while the experience fades them out (TrackedPerson, PersonSlots,
                                   ICrowdStepper<T>, ICrowdSource)
          ShowcasePoseAdapter      world pose -> view-space body: smoothing, per-joint grace and fade, the F-60 estimated root,
                                   a lost joint kept at its last offset from its parent (F-61)
          ShowcaseBody             the view-space body (positions, weights, fresh flags, hip centre)
          ScreenSpaceMapper        world metres -> view metres -> GUI / viewport
          ITrackingTuning          the tunables the above read; each showcase's settings asset implements it
          SimulatedPoseSource      the deterministic stand-in person
          ShowcaseSharedTypes      Joint, ShowcaseSource, SignalStatus
Tests/    ShowcaseCrowdTests       Showcase > Shared > Run Self Tests (report: Temp/ShowcaseSharedTestResults.txt)
```

## Building a showcase on it (what Fluid Disruption does)

1. Derive a person: `class MyPerson : TrackedPerson` and put whatever the experience keeps PER PERSON in it (a state machine, shapes,
   movement tracks). `TrackedPerson` already owns the `ShowcaseBody` and the `ShowcasePoseAdapter`; implement `ResetExperience()` to
   forget your own state when a slot is handed to someone new or freed.
2. Implement `ICrowdStepper<MyPerson>`: `StepPerson(...)` is one person's frame (adapter update, your state, your tracks) and
   `IsFinished(...)` says when an ABSENT person has faded out and their slot may be freed. Keep it a plain class so it is testable
   without a scene (`DisruptionPersonStepper`).
3. Own a `ShowcaseCrowd<MyPerson>` over an array of `maxPeople` persons, and each frame call
   `crowd.Update(trackingSource, sourceFresh, pose, left, right, telemetry, dt, stepper)`, then drive your visuals from
   `crowd.At(i)` for every slot (a slot that is not in use gets nothing, or gets released).
4. Your settings asset implements `ITrackingTuning` (smoothing, grace, view mapping) and carries a `maxPeople`.

Do not read `TrackedStage.Pose` directly for the person: that is the first person only. Do not place people yourself: the crowd
does it from each person's own measured root, so nobody jumps when the first person leaves.

## Rules

- No showcase-specific type in this assembly (no silhouette, no fluid, no FluXY). If it names an experience it belongs in the experience.
- Change a rule here, add the test in `Tests/`, and run every showcase's tests: the point of this assembly is that they all move together.
