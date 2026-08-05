# Virtual Mirror
## Software Design Specification

Document ID: SDS-019  
Document Name: Third Party  
Version: 1.0  
Status: Active  

---

# 1. Required Dependencies

| Dependency | Purpose | License action |
|------------|---------|----------------|
| Unity Editor / Runtime | Engine | Unity terms |
| URP | Rendering | Unity |
| Input System | Input | Unity |
| Animation Rigging | IK | Unity |
| Test Framework | Tests | Unity |
| **UniVRM (VRM 1.0)** | Avatar load/export | Comply with UniVRM + VRM consortium |
| **MediaPipe** (+ Unity binding) | Pose/Face/Hands | Apache-2.0 / notice files; pin plugin repo |
| MediaPipe model files | Inference | Include model license texts |

---

# 2. Optional Dependencies

| Dependency | Purpose | When |
|------------|---------|------|
| FinalIK | Premium IK | Licensed project |
| OpenCV for Unity | Calibration / undistort | Only if required |
| Standalone File Browser | OS file picker | If not using custom native |
| UniTask | Async | If team standardizes on it |

---

# 3. Camera Hardware (not code deps)

| Device | Role |
|--------|------|
| Logitech C920 / Brio | Baseline |
| Intel RealSense D455 | Depth upgrade |
| OAK-D | On-device AI + depth |
| Azure Kinect | Avoid as primary (discontinued) |

---

# 4. Avatar Tooling (external apps)

| Tool | Role |
|------|------|
| VRoid Studio | End-user avatar creation → VRM 1.0 |
| Blender + UniVRM | Internal preset creation |
| Ready Player Me / Avatar SDK / Meshy | Phase 2 AI avatars |

---

# 5. Pinning Policy

- Record exact versions in `Packages/manifest.json`  
- Update this table when bumping  
- Prefer official releases over random forks  
- Vendor native `.dll` / `.task` with checksums in docs or lockfile note  

---

# 6. Bundled Content

Built-in VRM avatars need redistribution rights documented:

| Asset | Source | Redistributable? |
|-------|--------|------------------|
| builtin_01…N | TBD | Must be Yes before ship |

---

# 7. Notices

Ship `ThirdPartyNotices.txt` next to the EXE with required attributions.

---

# 8. Agent Rules

- Do not add Asset Store packages without updating this doc + ADR  
- Do not commit credentials for AI avatar APIs  
- Prefer existing interface + new adapter over new SDK sprawl
