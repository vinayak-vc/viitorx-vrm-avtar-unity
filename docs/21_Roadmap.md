# Virtual Mirror
## Software Design Specification

Document ID: SDS-021  
Document Name: Roadmap  
Version: 1.0  
Status: Active  

---

# Phase 0 — Foundation

- Unity project / package scaffold + asmdefs  
- Docs frozen for V1 scope  
- URP scene, lighting, empty AvatarRoot  
- Settings + PathProvider + logging  

---

# Phase 1 — Vertical Slice (shippable core)

- Webcam capture  
- MediaPipe Pose → filter → retarget → Animation Rigging IK  
- UniVRM runtime load (library + file picker)  
- Persist last avatar  
- Basic mirror UI  
- 5–10 built-in avatars  

**Outcome:** Body-tracked VRM mirror at ≥ 60 FPS on reference PC.

---

# Phase 1b — Face & Hands

- Face Mesh → BlendShapes  
- Hands → finger drivers  
- Smoothing panels per modality  
- Calibration UX  

---

# Phase 2 — Product Polish & Ecosystem

- Diagnostics overlay polished  
- Quality modes  
- Background modes  
- Installer / EXE packaging  
- Third-party notices  
- Optional FinalIK backend  
- Online avatar gallery / Download More  
- Links or integration to VRoid / AI avatar services  
- In-app clothing / accessory customization (stretch)  

---

# Phase 3 — Advanced Tracking

- Depth camera providers (RealSense / OAK-D)  
- Improved occlusion / scale  
- Gesture recognition  
- Multi-person (architecture allows; product decision)  

---

# Phase 4 — Platform Expansion (optional)

- Recording / mocap export  
- AR/VR modes  
- Cloud accounts  
- Non-Windows (only if MediaPipe story exists)  

---

# Priority Rule

Ship Phase 1 body mirror before investing in marketplace or multi-person.
