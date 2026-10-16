# Virtual Mirror — Documentation Index

Agent-oriented documentation for the Unity + MediaPipe + VRM virtual mirror platform.

Read in order when onboarding. Update the matching document when a subsystem changes.

---

## Start Here (Agent Handoff)

| Doc | Purpose |
|-----|---------|
| [project-overview.md](project-overview.md) | What the product is and what V1 ships |
| [architecture.md](architecture.md) | Layered architecture and module map |
| [roadmap.md](roadmap.md) | Phased delivery plan |
| [tasks.md](tasks.md) | Current task board (update during work) |
| [decisions.md](decisions.md) | Architecture Decision Records |
| [ai_handoff.md](ai_handoff.md) | Last session state for the next agent |

---

## Demonstration branch (the skeleton as the product)

F-26 established that the tracking is sound and the **retarget** is not, so the avatar is parked and
the debug skeleton is being built into the product. These are the current reports, newest first.

| Doc | Covers |
|-----|--------|
| [HE01_HUMAN_ECHO_2026-10-14.md](HE01_HUMAN_ECHO_2026-10-14.md) | **Human Echo**: the S-01 cutout turned into FluXY smoke, echo copies, a neon outline and GPU particles with a floor pile, three switchable looks (`Showcase/HumanEcho`, ADR-104). 99 + 608 + 11 tests pass; seen working on two video files; NOT tested on a person in front of a webcam |
| [S01_CUTOUT_2026-10-13.md](S01_CUTOUT_2026-10-13.md) | **Real-time human cutout**: the person in a camera picture isolated by the sidecar (MediaPipe selfie network, a crop per person) and drawn by Unity from the picture + a soft mask on a new wire (UDP 8903): Original / Person / Silhouette, glow, backdrops (`Showcase/Cutout`, `CutoutStreamBinder`). Tested; seen working on two video files; NOT tested on a person in front of a webcam |
| `python-sidecar~/docs/WIRE.md` | **The wire specification** (body, hands, face, cutout), canonical in the engine repo: fields, versioning rules, conformance checklist; `Tests/EditMode/VisionFixtures/conformance.json` is the expected result of every golden datagram |
| [PLAN_TRACKING_ENGINE_AND_PACKAGES_2026-10-13.md](PLAN_TRACKING_ENGINE_AND_PACKAGES_2026-10-13.md) | **Plan only**: a compiled Viitorx Tracking Engine (exe folder + Inno Setup installer) and `com.viitorx.avtar.tracking.*` Unity packages; repo reuse analysis |
| [H04_VISION_BOOTSTRAP_AND_SHOWCASE_2026-10-13.md](H04_VISION_BOOTSTRAP_AND_SHOWCASE_2026-10-13.md) | **Hands and Face in AppBootstrap**: one settings section, a host that runs the senders, an F6 control panel, two sample scenes (`Showcase/HandsFace`). Tested headless; not run in Play mode |
| [H03_UNITY_RECEIVERS_2026-10-13.md](H03_UNITY_RECEIVERS_2026-10-13.md) | **Unity receivers for the hands and face wires** (`Runtime/Vision`): allocation-free parser, providers, demo rig, 52-blendshape driver. Tested headless; not run in the Editor or on a live camera |
| [H02_FACE_ONLY_2026-10-13.md](H02_FACE_ONLY_2026-10-13.md) | **Face only, no body**: 52 blendshapes, head pose, position, a sibling sender and wire (port 8902). Not run on a live camera yet |
| [H01_HANDS_ONLY_2026-10-13.md](H01_HANDS_ONLY_2026-10-13.md) | **Hands only, no body**: a separate sender and wire (port 8901), 3D position, finger poses, gestures. Not run on a live camera yet |
| [F36_SIDECAR_BOOT_2026-09-17.md](F36_SIDECAR_BOOT_2026-09-17.md) | One boot scene that starts the multi-person sidecar and opens the launcher |
| [F35_SCENE_LAUNCHER_2026-09-17.md](F35_SCENE_LAUNCHER_2026-09-17.md) | One launcher scene for all twenty scenes, split by how many people each one needs |
| [F34_CROWD_EXPERIENCES_2026-09-17.md](F34_CROWD_EXPERIENCES_2026-09-17.md) | Nine crowd experience scenes, and the rule that decided which multi-person ideas are buildable |
| [F33_PER_PERSON_FILTERS_2026-09-17.md](F33_PER_PERSON_FILTERS_2026-09-17.md) | Per-person P0/P1-1/F-22 filter chains keyed on the track id, and a self-measured sample rate |
| [F32_MULTIPERSON_2026-09-17.md](F32_MULTIPERSON_2026-09-17.md) | Multi-person detection, tracking with stable ids, and the first two-person experience |
| [F30_EXPERIENCES_2026-09-16.md](F30_EXPERIENCES_2026-09-16.md) | Seven interactive experience scenes on one shared tracking core (`TrackedStage`) |
| [F29_TRUST_HANDS_FEET_ATTRACT_2026-09-16.md](F29_TRUST_HANDS_FEET_ATTRACT_2026-09-16.md) | The trust channel, hands and feet drawn, the attract loop, and the measured range limits |
| [F28_SKELETON_SHOW_MODES_2026-09-16.md](F28_SKELETON_SHOW_MODES_2026-09-16.md) | The show scene and its first three modes |
| [F27_HUMANIZED_SKELETON_2026-09-16.md](F27_HUMANIZED_SKELETON_2026-09-16.md) | The humanized skeleton layer |
| [F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md](F26_AVATAR_VS_DEBUG_SKELETON_2026-09-16.md) | Why the avatar diverges while the skeleton does not |

Measurements behind them: `evidence/f29/` and `evidence/f30/`.

---

## Design Specs (SDS)

| # | Document | Covers |
|---|----------|--------|
| 00 | [Project Vision](00_ProjectVision.md) | Goals, non-goals, success metrics |
| 01 | [Product Requirements](01_ProductRequirements.md) | Functional / non-functional requirements |
| 02 | [Project Structure](02_ProjectStructure.md) | Folder scaffold and assembly layout |
| 03 | [Tech Stack](03_TechStack.md) | Engines, packages, versions |
| 04 | [System Architecture](04_SystemArchitecture.md) | High-level system diagram |
| 05 | [Runtime Architecture](05_RuntimeArchitecture.md) | App lifecycle, hot-swap, states |
| 06 | [Unity Architecture](06_UnityArchitecture.md) | Scenes, prefabs, MonoBehaviour roles |
| 07 | [Data Flow](07_DataFlow.md) | Camera → track → retarget → render |
| 08 | [VRM System](08_VRMSystem.md) | Runtime load, blendshapes, spring bones |
| 09 | [MediaPipe System](09_MediaPipeSystem.md) | Pose, face, hands providers |
| 10 | [Retargeting](10_Retargeting.md) | Joint map, rotation from vectors |
| 11 | [IK Pipeline](11_IKPipeline.md) | Animation Rigging / FinalIK |
| 12 | [Render Pipeline](12_RenderPipeline.md) | URP, mirror camera, background |
| 13 | [UI System](13_UISystem.md) | Mirror UI, settings, calibration |
| 14 | [File Management](14_FileManagement.md) | Avatar paths, logs, prefs |
| 15 | [Settings](15_Settings.md) | Persistence, defaults, schema |
| 16 | [Threading](16_Threading.md) | Capture / inference / main thread |
| 17 | [Performance](17_Performance.md) | Budgets, profiling, GC rules |
| 18 | [Extensibility](18_Extensibility.md) | Interfaces, plugins, providers |
| 19 | [Third Party](19_ThirdParty.md) | Packages, licenses, pin versions |
| 20 | [Test Plan](20_TestPlan.md) | Unit, integration, playmode, soak |
| 21 | [Roadmap](21_Roadmap.md) | Product phases |
| 22 | [Milestones](22_Milestones.md) | Ship criteria per milestone |
| 23 | [Coding Standards](23_CodingStandards.md) | C# / Unity rules (see also AGENTS.md) |
| 24 | [Risks](24_Risks.md) | Technical and product risks |
| 25 | [Pose Pipeline](25_PosePipeline.md) | Coordinates, filter, confidence, calib |
| 26 | [OAK-D Depth Phase 2](26_OakDDepthPhase2.md) | Measured per-keypoint depth |
| 27 | [Character Rig Spec](27_CharacterRigSpec.md) | Canonical VRM 1.0 rig contract for artists |

---

## Priority for Agents

1. `AGENTS.md` (repo root / parent project) — coding law
2. `ai_handoff.md` — current state
3. `architecture.md` + relevant SDS for the task
4. `tasks.md` — pick / update work items
5. `decisions.md` — do not contradict accepted ADRs

---

## Document Status Legend

- **Draft** — direction agreed, details may change
- **Active** — implement against this version
- **Frozen** — change only via ADR
