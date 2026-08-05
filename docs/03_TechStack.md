# Virtual Mirror
## Software Design Specification

Document ID: SDS-003  
Document Name: Tech Stack  
Version: 1.0  
Status: Active  

---

# 1. Stack Summary

| Layer | Choice | Notes |
|-------|--------|-------|
| Engine | **Unity 6** (fallback: Unity 2022 LTS) | Windows standalone |
| Language | C# (explicit types; AGENTS.md) | .NET / Unity runtime |
| Render pipeline | URP | 1080p mirror view |
| Avatar format | **VRM 1.0** | Runtime load via UniVRM |
| Body tracking | **MediaPipe Pose** (Tasks API) | GPU preferred |
| Face tracking | **MediaPipe Face Mesh / Face Landmarker** | BlendShapes |
| Hand tracking | **MediaPipe Hands / Hand Landmarker** | 21 joints × 2 |
| IK | **Unity Animation Rigging** (primary) | FinalIK optional commercial |
| Camera | WebcamTexture / native capture | Logitech C920/Brio baseline |
| Optional CV | OpenCV for Unity (only if needed) | Calibration / preprocess |
| UI | uGUI or UI Toolkit | Prefer UI Toolkit for settings |
| Packaging | Windows x64 EXE | Il2CPP or Mono (decide via ADR) |

---

# 2. Recommended Versions (pin in manifest)

Exact package versions must be pinned in `Packages/manifest.json` and recorded here when chosen.

| Package | Suggested source | Role |
|---------|------------------|------|
| `com.vrmc.univrm` / UniVRM | Official UniVRM (VRM 1.0) | Import + runtime load |
| MediaPipe Unity plugin | Project-chosen binding (Tasks) | Pose / Face / Hands |
| `com.unity.animation.rigging` | Unity Registry | IK constraints |
| `com.unity.render-pipelines.universal` | Unity Registry | URP |
| `com.unity.inputsystem` | Unity Registry | UI / hotkeys |
| `com.unity.test-framework` | Unity Registry | Tests |
| FinalIK | Asset Store (optional) | Premium IK path |
| OpenCV for Unity | Asset Store (optional) | Preprocess only |

---

# 3. Hardware Matrix

## Minimum

| Component | Spec |
|-----------|------|
| OS | Windows 10 21H2+ / Windows 11 |
| CPU | 4-core modern x64 |
| GPU | DirectX 11 capable; GTX 1060-class or better |
| RAM | 8 GB |
| Camera | 720p USB webcam 30 FPS |

## Recommended

| Component | Spec |
|-----------|------|
| CPU | 8-core modern |
| GPU | RTX 3060+ (RTX 40/50 class ideal) |
| RAM | 16 GB |
| Camera | Logitech Brio / C920 @ 1080p 60 FPS |

## Optional upgrades

| Camera | Benefit |
|--------|---------|
| Intel RealSense D455 | Depth → stabler body scale |
| OAK-D | On-device AI + depth |
| Azure Kinect | Excellent but discontinued — do not design V1 around it |

---

# 4. Why This Stack

- **Mature & cost-effective** for single-user RGB tracking  
- **VRM 1.0** enables VRoid Studio user avatars without custom pipeline  
- **MediaPipe Tasks** covers pose + face + hands with one ecosystem  
- **Unity Animation Rigging** avoids mandatory commercial IK for V1  
- Clear upgrade path: depth cameras, FinalIK, multi-person, clothing preview  

---

# 5. Explicit Non-Choices (V1)

| Option | Reason deferred |
|--------|-----------------|
| Unreal Engine | Team / tooling is Unity |
| MoveNet-only | MediaPipe covers face+hands; MoveNet as alternate provider later |
| Native Kinect SDK as primary | Hardware discontinued |
| Built-in avatar editor | Out of scope; use VRoid / UniVRM offline |
| WebGL build | MediaPipe + webcam + VRM constraints; Windows EXE first |

---

# 6. MediaPipe Integration Notes

- Prefer **MediaPipe Tasks** (Pose Landmarker, Face Landmarker, Hand Landmarker)  
- Run inference on a **worker thread / async job**; publish immutable frames to main thread  
- Model files live in `StreamingAssets/MediaPipe/`  
- GPU delegate when available; CPU fallback with reduced resolution  

---

# 7. UniVRM Integration Notes

- Target **VRM 1.0** (not VRM 0.x only)  
- Runtime: `Vrm10.Vrm10Instance` load from path/bytes  
- Expressions → VRM Expression / BlendShape proxy  
- Humanoid Avatar required for retarget + Animation Rigging  

---

# 8. Build & CI Expectations

| Item | Expectation |
|------|-------------|
| Scripting backend | Document choice in ADR (IL2CPP preferred for ship) |
| API Compatibility | .NET Standard 2.1 / Unity default |
| CI | EditMode tests on every PR; PlayMode smoke nightly |
| Artifacts | Windows x64 player zip / installer |

---

# 9. Third-Party License Checklist

Before shipping, verify and file under `docs/19_ThirdParty.md`:

- UniVRM / VRM Consortium terms  
- MediaPipe / model licenses  
- Any Asset Store EULAs (FinalIK, OpenCV)  
- Bundled avatar licenses (must allow redistribution)
