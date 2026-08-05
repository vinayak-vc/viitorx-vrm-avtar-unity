# Virtual Mirror
## Software Design Specification

Document ID: SDS-001  
Document Name: Product Requirements  
Version: 1.0  
Status: Active  

---

# 1. Purpose

Define measurable requirements for Virtual Mirror V1 (Windows EXE).  
Every feature implemented must map to a requirement ID below.

---

# 2. Actors

| Actor | Description |
|-------|-------------|
| End User | Person standing in front of a webcam |
| Operator | Same person configuring camera / avatar / smoothing |
| Developer / Agent | Implements and verifies requirements |

---

# 3. Functional Requirements

## 3.1 Avatar

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-AV-01 | Load a `.vrm` (VRM 1.0) file chosen via Windows file picker at runtime | Must |
| FR-AV-02 | Destroy previous avatar and attach tracking to the new instance without restart | Must |
| FR-AV-03 | Ship with 5–10 built-in VRM avatars selectable from an Avatar Library | Must |
| FR-AV-04 | Persist last selected avatar path and restore on next launch | Must |
| FR-AV-05 | Reject invalid / unsupported VRM with a clear user-facing error | Must |
| FR-AV-06 | Apply MediaPipe-driven VRM BlendShapes for face (blink, smile, angry, surprised, mouth open, eye look) | Must |
| FR-AV-07 | Animate VRM hand bones from MediaPipe Hands (21 joints → finger curl / spread) | Must |
| FR-AV-08 | Support optional online avatar gallery / download (Phase 2) | Could |

## 3.2 Camera

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-CAM-01 | Enumerate available USB / webcam devices | Must |
| FR-CAM-02 | Open selected camera at configurable resolution and FPS | Must |
| FR-CAM-03 | Hot-swap camera without restarting the app | Must |
| FR-CAM-04 | Show live preview texture for diagnostics | Should |
| FR-CAM-05 | Support Logitech C920 / Brio class webcams as baseline | Must |
| FR-CAM-06 | Optional depth camera path (RealSense / OAK-D) behind same provider interface | Could |

## 3.3 Tracking

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-TR-01 | Run MediaPipe Pose and publish normalized joint positions + confidence | Must |
| FR-TR-02 | Run MediaPipe Face Mesh and publish landmarks / expression features | Must |
| FR-TR-03 | Run MediaPipe Hands (left + right) and publish 21 joints per hand | Must |
| FR-TR-04 | Apply joint filtering (smoothing + outlier rejection) before retarget | Must |
| FR-TR-05 | Expose tracking confidence per joint and degrade gracefully when low | Must |
| FR-TR-06 | Allow enable/disable of pose, face, hands independently | Should |
| FR-TR-07 | Support alternate providers (MoveNet, BlazePose, depth) via `IBodyTrackingProvider` | Should |

## 3.4 Retargeting & IK

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-RT-01 | Map MediaPipe landmarks to Unity Humanoid / VRM bones | Must |
| FR-RT-02 | Compute bone rotations from joint vectors (not raw landmark copy) | Must |
| FR-RT-03 | Drive limbs through an IK solver (Unity Animation Rigging preferred; FinalIK optional) | Must |
| FR-RT-04 | Support calibration (T-pose / A-pose) to align user scale / offsets | Must |
| FR-RT-05 | Configurable smoothing for body, face, hands independently | Must |

## 3.5 Presentation (Mirror)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-UI-01 | Full-screen or large “Virtual Mirror” view of the avatar | Must |
| FR-UI-02 | Controls: Load Avatar, Camera Settings, Smoothing, Calibration, Background | Must |
| FR-UI-03 | Change background (solid color / image / blur of camera) | Should |
| FR-UI-04 | On-screen diagnostics: FPS, latency estimate, tracking status | Should |
| FR-UI-05 | Horizontal mirror option (user sees avatar as mirror image) | Should |

## 3.6 Settings & Files

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-SET-01 | Persist settings to disk (JSON or PlayerPrefs + JSON hybrid) | Must |
| FR-SET-02 | Store user avatars under Documents/MyMirror/Avatars/ (configurable) | Should |
| FR-SET-03 | Write rotating application logs | Must |

---

# 4. Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-01 | Sustained frame rate | ≥ 60 FPS @ 1080p avatar view on recommended GPU |
| NFR-02 | End-to-end motion latency | ≤ 50 ms when possible; soft fail ≤ 80 ms |
| NFR-03 | Avatar switch time | ≤ 3 s for typical VRM |
| NFR-04 | Cold start to interactive | ≤ 5 s |
| NFR-05 | Working set | < 2 GB typical single-user session |
| NFR-06 | No blocking I/O on main thread | Camera decode / VRM parse / inference offloaded |
| NFR-07 | Platform | Windows 10/11 x64 EXE |
| NFR-08 | Unity version | Unity 6 (preferred) or Unity 2022 LTS |
| NFR-09 | Stability | 2-hour soak with no crash / unbounded memory growth |
| NFR-10 | Extensibility | Tracking and avatar backends behind interfaces |

### Performance budget (single person, modern GPU)

| Stage | Budget |
|-------|--------|
| Camera capture | 1–2 ms |
| Pose (GPU) | 3–6 ms |
| Face | 2–4 ms |
| Hands | 2–4 ms |
| IK + retarget | < 1 ms |
| VRM render | 1–3 ms |

---

# 5. User Stories (V1)

1. As a user, I open the app, pick a built-in avatar, and see it mirror my body within seconds.  
2. As a user, I load my own VRoid-exported `.vrm` and it replaces the current avatar.  
3. As a user, I smile / blink / open mouth and the avatar face responds.  
4. As a user, I move fingers and the avatar hands curl accordingly.  
5. As a user, I change camera and smoothing without restarting.  
6. As a user, I calibrate once and tracking feels aligned to my height.

---

# 6. Acceptance Criteria (V1 Gate)

- [ ] Built-in + file-picker VRM load works  
- [ ] Pose + face + hands drive avatar at ≥ 60 FPS on reference machine  
- [ ] Last avatar restored on relaunch  
- [ ] Calibration + smoothing UI functional  
- [ ] Logs and settings persist  
- [ ] No main-thread stalls > 33 ms during steady tracking  

---

# 7. Out of Scope (V1)

See `00_ProjectVision.md` §6–8. Notably: multiplayer, VR/AR, cloth authoring, marketplace, lip-sync from mic, non-Windows platforms.
