# AI_WORKLOG - Human Echo (FluXY + cutout effects)

Central, append-only record of the autonomous overnight operation that began 2026-10-13. Everything meaningful is also
in `.ai-work/<task-id>/checkpoint.md`. If a session is lost, read THIS file first, then `git status`, then the checkpoint
of the task you are resuming. Nothing here depends on an agent's memory.

## Mission (verbatim scope from the user, shortened)

Spike first, then ALL THREE features, integrated, verified with the existing videos, documented. Keep the established
defaults. Preserve unrelated user changes. Do not mark a feature complete until it is verified.

Established defaults (from the plan the user approved by "proceed using the current defaults"):
- clip order 27845 -> 14008 -> 11367; compute-shader particles (NO VFX Graph package); 1920x1080 16:9 on this PC (RTX 3060);
  working name "Human Echo"; a SHOWCASES Launcher row; not audio-reactive; NO new model downloads.
- New code lives in a new nested-repo folder `Showcase/HumanEcho/` (Scripts, Editor, Tests, Shaders, Settings, Materials,
  Scenes, README), following Fluid Disruption / Chromatic Rain / Cutout conventions. AGENTS.md style rules apply to C#.

The three features (my reading of "all three planned features"):
- F1 SMOKE: FluXY fluid layer driven by the live cutout mask (dye) and its motion (velocity) + thin neon outline. Clip 27845.
- F2 ECHO: ring of past masks drawn as hue-ramped copies, delayed outline, flash / white-hot grading. Clip 14008.
- F3 PARTICLES: compute-shader particles seeded on the mask, moved by FluXY's velocity, spheres + dots, floor pile. Clip 11367.
- Integration: one scene, presets A/B/C switchable live, Launcher row, quality profiles, docs, tests.

## Repositories

| Repo | Path | Branch | Notes |
|---|---|---|---|
| nested (project code, docs) | `Assets/Games/viitorx-vrm-avtar-unity` | multi-person-detaction | clean at start (HEAD da434bd) |
| sidecar (Python) | `Assets/Games/viitorx-vrm-avtar-unity/python-sidecar~` | (own repo) | no change planned for F1-F3 |
| outer (Unity project) | `D:/Unity/viitorx-vrm-avtar-unity-base-project` | (outer) | had user changes at start: `Packages/manifest.json`, `Packages/packages-lock.json`, several `ProjectSettings/*.asset`, `Assets/Resources/CompileDefines.txt`. DO NOT revert them. |

## Resumability evidence (do not assume; this is what was actually checked)

- Workflow tool (ultracode): documented `resumeFromRunId` (SAME SESSION only), journal at `<transcriptDir>/journal.jsonl`.
  Cross-session resume is NOT supported by it. The durable mechanism is therefore the on-disk checkpoints.
- Every agent prompt requires: write `.ai-work/<task-id>/checkpoint.md` early and keep it current; return a structured summary.
- Run ids and agent ids are recorded in the registry below as soon as they are known.

## Resume procedure after a usage reset or lost session

1. Read this file, then `git status` in the nested repo and the outer repo (preserve user changes).
2. Read `.ai-work/<task-id>/checkpoint.md` for each task not marked DONE.
3. Check the Unity Editor: `manage_scene get_active`, `read_console` (errors first). The Editor is single-instance: only ONE
   actor may drive it at a time (compile / Play / screenshots). Agents never touch the Editor; the orchestrator does.
4. If the same session is alive: `Workflow({scriptPath, resumeFromRunId})` reuses completed agent results. Otherwise rebuild only
   what the checkpoints say is missing.
5. Never run Unity MCP `run_tests` (closes the Editor). Run tests by reflection from `execute_code`.

## Task registry

| Task id | Owner | Status | Checkpoint |
|---|---|---|---|
| spike | orchestrator | DONE: S1-S4 VALIDATED | `.ai-work/spike/checkpoint.md` |
| contracts | orchestrator | DONE (compiled) | `.ai-work/spec/INTERFACES.md` |
| echo (EchoLayer, OutlineLayer, PersonLayer, tests asmdef) | agent `echo-layers` | DONE, collected, integrated | `.ai-work/echo/checkpoint.md`, staged in `.ai-work/stage/echo/` |
| ui (HUD, looks, scene builder, README) | agent `ui-presets-builder` | DONE, collected, integrated | `.ai-work/ui/checkpoint.md`, staged in `.ai-work/stage/ui/` |
| f3-particles (compute + draw) | orchestrator | DONE | `.ai-work/f3-particles/checkpoint.md` |
| integration (controller, looks, Launcher row, tuning, perf, tests) | orchestrator | DONE (verified; see the final log line) | `.ai-work/integration/checkpoint.md` |
| review (3 lenses) | workflow `wf_3a26041c-4da` | DONE, collected, findings fixed | `.ai-work/review/{gpu,csharp,scenarios}.md` |
| docs (README, ADR-104, tasks, ai_handoff, overview, architecture, index, report) | orchestrator | DONE | `docs/HE01_HUMAN_ECHO_2026-10-14.md` |

## Agent / session registry

| When | Kind | Id / runId | Task | Persisted to | Collected |
|---|---|---|---|---|---|
| 2026-10-13 ~23:50 | Workflow (ultracode) | runId `wf_a099326b-28a` (task `wc3hqx1j3`) | echo + ui authoring (2 agents, staged files only, no Editor) | script: `~/.claude/projects/<proj>/<session>/workflows/scripts/human-echo-parallel-author-wf_a099326b-28a.js`; transcripts: `.../subagents/workflows/wf_a099326b-28a/` (journal.jsonl, agent-*.jsonl); files: `.ai-work/stage/{echo,ui}/` | NO |

Second launch (after the limit reset, same runId via resumeFromRunId, task `wer1y2u1q`): agent ids `a57cfbfcae49acc51` (echo-layers) and `a87c5c74eca21d3d6`
(ui-presets-builder); the first launch's ids `afd94850f984acad9` / `a396bf386a3d6d8e0` died at the limit with no output. By 02:15 the ui agent had staged 5 files and both had checkpoints.
COLLECTED 2026-10-14 ~03:35: agent `ui-presets-builder` (a87c5c74eca21d3d6) returned its result (journal.jsonl has it); its 9 staged files were copied into `Showcase/HumanEcho/`,
the project compiled with 0 errors/0 warnings, and its 32 tests passed together with my 13 (45/45 by reflection). Agent `echo-layers` still running at that time.
COLLECTED 2026-10-14 ~04:10: agent `echo-layers` (a57cfbfcae49acc51) finished (journal result + full text in tasks/wer1y2u1q.output); its 12 staged files (EchoSchedule, EchoRing, EchoLayer,
OutlineLayer, PersonLayer, LayerResources, 3 shaders, EchoScheduleTests/EchoColorTests, runner) were copied into `Showcase/HumanEcho/`, diff-checked identical to the staged originals, compiled with
0 errors/0 warnings and no shader messages. HumanEcho.Tests: 93/93 pass by reflection (EchoColor 23, EchoSchedule 25, HumanEchoUI 32, ParticleLayer 7, PersonField 6). Workflow `wf_a099326b-28a` is complete:
nothing is pending in any agent. The echo agent's risks list (shader import, CommandBuffer slice capture, untuned looks) is the integration to-do (seen in the checkpoint).
REVIEW RUN launched 2026-10-14 ~05:30: Workflow runId `wf_3a26041c-4da` (task `w62j5idcl`), three read-only reviewers `review-gpu`, `review-csharp`, `review-scenarios`; each writes
`.ai-work/review/<lens>.md` as it confirms findings and returns a structured list (journal: `.../subagents/workflows/wf_3a26041c-4da/journal.jsonl`; script: `.../workflows/scripts/human-echo-review-wf_3a26041c-4da.js`).
Resume (same session only): `Workflow({scriptPath: <that script>, resumeFromRunId: "wf_3a26041c-4da"})`. If the session is lost: read `.ai-work/review/*.md`, which are the durable findings.
Resume of the authoring run: same session only: `Workflow({scriptPath: <script above>, resumeFromRunId: "wf_a099326b-28a"})`; completed agents return cached results. If the
session is gone: inspect `.ai-work/stage/*` and `.ai-work/{echo,ui}/checkpoint.md`, and re-run only the task whose staged files are incomplete.

## Log (append-only)

- 2026-10-14 (real clock, afternoon of the same session): FINAL STATE. Long comment lines (224) wrapped in `Showcase/HumanEcho/**/*.cs`; 92 code / attribute lines over 110 columns remain (existing code has
  the same share, 3-4 %). Scene REBUILT from code with `HumanEchoSceneBuilder.BuildScene()` (this discarded the in-memory `vision.source = video.webm` edit: the saved scene has `source: camera:0`, three looks
  wired, scene in Build Settings, not dirty). README rewritten (measured results, NOT TESTED list), ADR-104 wording fixed (picture heights) and extended, docs updated (tasks HE-01, ai_handoff HE-01,
  project-overview, architecture, docs/README, new `docs/HE01_HUMAN_ECHO_2026-10-14.md`). FINAL VERIFICATION by reflection in the open Editor (SetUp / TearDown honoured), console cleared first:
  `VirtualMirror.Tests` 608 passed / 0 failed / 2 parameterised skipped; `CutoutShowcase.Tests` 11 / 0; `HumanEcho.Tests` 99 / 0; Console afterwards: 0 entries. No mutation is applied.
  NOT committed (the user did not ask). NOT TESTED: a person on a live webcam, a network camera, a player build, the Escape key by hand, controller Dispose lifecycle, other screen sizes.

- 2026-10-14 07:35 (real clock): after the Editor restart: compile clean, 7 shaders 0 messages, HumanEcho.Tests 99/99 (EchoRingTests 4 new). Mutation check of the ring tests: making `newMask` always true
  was caught by ARepeatedFieldFrameIsNotCapturedAgain (the two-layers test did not fail because the spacing rule alone blocks a same-instant second capture); line restored.

- 2026-10-14 07:10 (real clock): SECOND USAGE-LIMIT RESET. TIME NOTE: the clock times written in this log and in the checkpoints between "~03:" and "~07:" were my estimates and run about 4.5 hours
  AHEAD of reality (file mtimes show the review and the mutation checks happened around 02:20-02:45 local). Use file mtimes, not those labels. The old Unity Editor closed cleanly at 02:45
  (Editor-prev.log ends with a normal shutdown, no crash; the last log content is one "Releasing render texture that is set to be RenderTexture.active" warning from a test, fixed below);
  a new Unity started at 07:07 and was still opening the project at 07:10. State on disk at this point: all review fixes in the working tree (controller Escape/stale/mirror/view settings,
  PersonField.Blank, motion units, HUD drawn rule, bloom guard, dots two-lattice fix, pile fixes, spawn probes) COMPILED AND TESTED up to the compute edits `found`/pile landing, which were saved
  but NOT yet imported; after that more edits (below) are saved and NOT yet compiled. Nothing pending in any agent (both workflows complete). No mutation is applied (checked).
  Edits made after the last compile: PersonField/LayerResources.Release clears an active target; ParticleLayer.SetActive(true) after hidden clears the pool; compute 32 probes; SmokeSettings.maxSteps 4;
  LayerQuad (QueueSmoke removed, orphan mesh cleanup); PresetBuilder reset confirmation; new Tests/EchoRingTests.cs (4 tests, never run).
  NEXT: wait for the Editor, refresh, fix compile errors, run HumanEcho.Tests (expect 95 + 4), visual check of dots/pile/stall, register the scene in Build Settings, wrap long comment lines,
  README + docs (tasks.md, ai_handoff.md, project-overview, architecture, docs/README), final report.

- 2026-10-14 ~07:25: review fixes + their tests. `PersonField.Blank()` mutation check: removing the field clear made BlankEmpties... fail ("a blanked field holds no person", alpha 1.0);
  removing `Reset()` was NOT caught at first (false motion 0.19 < the test's 0.2 threshold), so the assertion was tightened to 0.02 and re-checked: caught (0.19). Both lines restored.

- 2026-10-14 ~06:40: MUTATION CHECK of the UI agent's tests (they were only hand-traced): six mutations (freshness window, live vs file, layer-drawn rule, P key mapping, mirror text, LIVE honesty
  rule) made 13 tests fail, every mutated line was restored and re-grepped. Full runs after restore: see the next lines.
- 2026-10-14 ~06:30: VirtualMirror.Tests (the repo's own assembly, by reflection in the open Editor): 608 passed, 0 failed, 2 parameterised not run (the previous baseline was 607 + 2); the new one is
  `SceneLauncherF35Tests.HumanEchoNeedsNoBodyEitherAndIsListedWithTheShowcases`.

- 2026-10-14 ~02:50: MUTATION CHECK of the first 13 GPU tests (PersonFieldTests 6, ParticleLayerTests 7): round 1 (flow sign, spawn mask test, pile cap, fluid x mapping,
  fluid scale) made 7 tests fail; round 2 (upright flip + mirror in the Field pass) made 4 fail; every mutated line was restored and the run is 13/13 again.
  All five mutations of round 1 and both of round 2 were caught by at least one test.

- 2026-10-13 ~23:55: USAGE LIMIT hit (resets 01:40 IST) 5 s after launching run `wf_a099326b-28a`. Both agents failed with "session limit" having made
  3 tool calls and written NOTHING (checked: no `.ai-work/{echo,ui,stage}` files, journal shows `failed` for both). No work lost. Orchestrator state intact
  (spike code, contract files, spec, worklog; Editor healthy, console clean). Recovery: relaunched the same script with `resumeFromRunId` (nothing was cached).
  Lesson recorded: keep authoring agents few and short-lived, and make the orchestrator's own files the durable core; agents only add staged files.

- 2026-10-13: infrastructure created (this file, `.ai-work/`). Repo state inspected: nested repo clean; outer repo has unrelated user changes.
