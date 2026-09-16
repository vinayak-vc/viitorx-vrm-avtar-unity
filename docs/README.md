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
