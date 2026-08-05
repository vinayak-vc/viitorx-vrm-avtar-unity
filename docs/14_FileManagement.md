# Virtual Mirror
## Software Design Specification

Document ID: SDS-014  
Document Name: File Management  
Version: 1.0  
Status: Active  

---

# 1. Paths

All paths via `PathProvider`:

```
Documents/MyMirror/
├── Avatars/                 # user .vrm
├── Settings/settings.json
├── Calibration/default.json
├── Backgrounds/             # optional user images
└── Logs/virtual-mirror-*.log

StreamingAssets/
├── Avatars/                 # built-in .vrm
└── MediaPipe/               # model .task / binaries
```

---

# 2. Avatar File Operations

| Op | Behavior |
|----|----------|
| Pick | Windows file dialog filter `*.vrm` |
| Import copy | Optional: copy into `Documents/MyMirror/Avatars/` |
| Library list | Enumerate StreamingAssets + user folder |
| Last used | Store absolute or rooted-relative path in settings |
| Delete user avatar | Only under user folder; never delete StreamingAssets |

---

# 3. Load Safety

- Max file size gate (e.g. 256 MB configurable)  
- Extension check + UniVRM parse errors  
- Async read (`File.ReadAllBytesAsync` or equivalent)  
- Main thread instantiate only after bytes ready  

---

# 4. Logging

- Rolling daily logs  
- Include app version, Unity version, GPU name at start  
- Do not log PII paths beyond avatar file names if privacy mode on  

---

# 5. Settings & Calibration Files

- UTF-8 JSON  
- Write via temp file + replace (atomic)  
- Schema version field for migrations  

---

# 6. Windows File Picker

Use platform dialog (e.g. `StandaloneFileBrowser` or native wrapper).  
Editor: `EditorUtility.OpenFilePanel` for playmode tests only.

---

# 7. Cloud (Phase 2)

Not V1. Keep `IAvatarLibrary` interface so remote gallery can be added without rewriting Load Avatar UI.
