# Virtual Mirror — AI Handoff

Last updated: 2026-08-05  
Purpose: next agent can continue without re-deriving context.

---

## Current state

- **Code:** Not scaffolded yet in this package (docs + `.gitignore` only).  
- **Docs:** Full SDS `00`–`25` written; agent handoff set created (recreated after folder deletion).  
- **Milestone:** Approaching **M0** (bootstrap).  

---

## Understanding (do not re-litigate)

Product = webcam virtual mirror for VRM avatars.  
Stack = Unity + MediaPipe (Pose/Face/Hands) + UniVRM + Animation Rigging.  
Architecture = layered providers, hot-swap, filter → retarget → IK → VRM.  
V1 = single user Windows EXE; no marketplace yet.

---

## Modified / created this session

- `docs/README.md`  
- `docs/00`–`25` SDS (full set)  
- `docs/project-overview.md`  
- `docs/architecture.md`  
- `docs/roadmap.md`  
- `docs/tasks.md`  
- `docs/decisions.md`  
- `docs/ai_handoff.md` (this file)  

---

## Next recommended task

1. Read `AGENTS.md`, `architecture.md`, `tasks.md`  
2. Execute **M0** checklist in `tasks.md`: Runtime scaffold, asmdefs, Bootstrap/Mirror scenes, SettingsStore, PathProvider, LogService  
3. Pin third-party packages; update `19_ThirdParty.md` versions  
4. Resolve **ADR-006** (async style) before deep MediaPipe integration  

---

## Constraints for implementers

- Follow AGENTS.md: no `var`, no `new()`, no expression-bodied members, K&R braces  
- Do not couple UI to MediaPipe  
- Prefer interfaces from day one  
- Update `tasks.md` + this file after each meaningful chunk of work  

---

## Open questions

- Exact MediaPipe Unity plugin package / repo to pin  
- IL2CPP vs Mono for shipping player  
- UniTask vs Unity Awaitable (ADR-006)  
- License sources for the 5–10 built-in VRM avatars
