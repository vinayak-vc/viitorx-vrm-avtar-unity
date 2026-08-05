# Virtual Mirror
## Software Design Specification

Document ID: SDS-012  
Document Name: Render Pipeline  
Version: 1.0  
Status: Active  

---

# 1. Choice

**URP (Universal Render Pipeline)** for Windows standalone mirror rendering.

Target presentation: 1920×1080 avatar view @ 60 FPS.

---

# 2. Mirror Composition

```
+----------------------------------------+
|           Virtual Mirror               |
|             VRM Avatar                 |
|         (full composition)             |
+----------------------------------------+
```

- Avatar is the hero subject — centered, framed mid-thigh to head by default  
- Background is secondary (solid / image / subtle camera blur)  
- UI chrome does not obscure the avatar body  

---

# 3. Cameras

| Camera | Role |
|--------|------|
| `MirrorCamera` | Main avatar view |
| `UI Camera` | Optional if World Space UI |
| `DebugCamera` | Dev only |

MirrorCamera:

- Soft key light + fill (simple studio lighting prefab)  
- No harsh post that breaks VRM toon materials unless toggled  
- Optional horizontal flip for mirror feel (coordinate with tracking mirror flag)

---

# 4. Background Modes

| Mode | Implementation |
|------|----------------|
| Solid color | Clear color / backdrop unlit |
| Image | Textured quad / skybox |
| Camera blur | Low-res webcam texture + blur material (perf-sensitive) |
| Transparent | Future / capture card workflows — not V1 |

`BackgroundController` switches modes without scene reload.

---

# 5. Lighting & Materials

- VRM 1.0 materials via UniVRM / MToon (or URP equivalents)  
- Ensure URP-compatible shaders in project  
- Single Directional + optional rim light  
- Avoid realtime GI  

---

# 6. Post-Processing

V1: minimal (optional mild bloom / color adjust).  
Disable heavy SSAO/SSR by default for laptop GPUs.

---

# 7. Resolution Policy

| Setting | Behavior |
|---------|----------|
| Render scale | 1.0 default; 0.75 Performance mode |
| VSync | On for 60 Hz displays; configurable |
| Fullscreen | Borderless window default |

---

# 8. Performance Rules

- One primary opaque avatar; minimize extra shadow casters  
- No per-frame material instantiation  
- Spring bones: allow disable for low-end  
- Diagnostic overlay uses UI, not extra cameras when possible  

Budget: **VRM rendering 1–3 ms** on recommended GPU.

---

# 9. Capture / Recording (Future)

Not V1. Design `MirrorCamera` so a later recorder can grab its RT without rewiring tracking.
