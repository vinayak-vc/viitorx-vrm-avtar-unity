# Virtual Mirror — Tasks

Update this file whenever work starts or finishes. Prefer small checkboxes agents can claim.

---

## Now (M0)

- [ ] Create Runtime/Editor/Tests folder scaffold per `02_ProjectStructure.md`  
- [ ] Add asmdefs (`VirtualMirror.Core`, `.App`, …)  
- [ ] Create `Bootstrap.unity` + `Mirror.unity` with Camera + Directional Light + AvatarRoot  
- [ ] Implement `PathProvider` + `SettingsStore` (schema v1)  
- [ ] Implement `LogService` rolling file  
- [ ] Add URP renderer asset if missing in base project  
- [ ] Pin UniVRM + Animation Rigging + MediaPipe package versions in manifest (record in `19_ThirdParty.md`)  

---

## Next (M1)

- [ ] `IAvatarLoader` + `UniVrmAvatarLoader`  
- [ ] Avatar library UI + file picker  
- [ ] Avatar swap without restart + persist last path  
- [ ] Built-in avatar slots under StreamingAssets  

---

## Next (M2)

- [ ] `ICameraCapture` + webcam service  
- [ ] MediaPipe Pose provider spike (GPU/CPU)  
- [ ] `JointFilterPipeline` + One-Euro  
- [ ] Retarget map + `AnimationRiggingIkDriver`  
- [ ] Fake provider PlayMode test  

---

## Backlog (M3+)

- [ ] Face → BlendShapes  
- [ ] Hands → fingers  
- [ ] Calibration UX  
- [ ] Quality modes  
- [ ] Soak harness  
- [ ] ThirdPartyNotices + EXE packaging  

---

## Blocked

_None_

---

## Completed

- [x] Author agent-oriented SDS docs `00`–`25` + handoff set (recreated 2026-08-05)
