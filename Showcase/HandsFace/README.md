# Hands and Face showcase

Two sample scenes for the hands-only and face-only trackers (H-01, H-02, received by `Runtime/Vision`, H-03). A camera that sees a hand or a face and **no body** is enough.

| Scene | Shows |
|---|---|
| `Scenes/HandsShowcase.unity` | Each hand as a 21-point skeleton at its estimated place in front of the camera, fingertip trails, a gesture label, pinch and finger-curl bars. Three orbs to pick up by pinching over them. |
| `Scenes/FaceShowcase.unity` | A cartoon face driven by the head pose and the 52 blendshapes (blinks, eye look, brows, jaw, smile, pucker), the strongest blendshapes as bars, expression tags, and the 478 landmarks on `L`. |

## Run it

1. Open one of the scenes and press Play. That is all: the scene carries its own `AppBootstrap`, which starts the Python sender it needs (the hands sender for the hands scene, the face sender for the face scene) with your webcam.
2. First time only: `python-sidecar~/.venv/Scripts/python.exe python-sidecar~/tools/model/fetch_hand_models.py` and `fetch_face_models.py` (the models are not in version control).
3. If nothing moves, read the banner at the top of the screen: it says whether the sender is starting, failed, or silent, and why.

Another camera, a phone, a LAN camera link, a video file, the number of hands or faces, every confidence threshold and the rest: **the AppBootstrap "Hands and Face (H-03)" section in the Inspector, or the runtime control panel (F6)**. The scenes have no settings of their own for any of it.

## Keys

| | Hands scene | Face scene |
|---|---|---|
| `F6` | control panel | control panel |
| `R` | put the orbs back | |
| `T` | fingertip trails on/off | |
| `H` | skeleton on/off | |
| `L` | | landmarks on/off |
| `M` | | swap which eye the left blendshapes drive (reflection / photo) |
| `F` | | avatar follows the head position on/off |

## How it is wired

- The scene's `Tracking (AppBootstrap)` object is the same component `Bootstrap.unity` carries, with the body tracking switched off (`useOakUdpTracking` off, no Mirror scene). When a scene is reached through `Bootstrap.unity` instead, the component here destroys itself (AppBootstrap is a singleton) and the real one serves the scene with its own settings.
- The scene scripts never open a socket or a process. `HandsShowcase` / `FaceShowcase` ask `AppBootstrap.Instance.Vision` (a `VisionHost`) for their stream and subscribe to its events. In `Auto` mode that request is what starts the sender, and leaving the scene stops it.
- One webcam or LAN-camera viewer can be read by one program at a time, so the hands and face senders do not run together on one camera: each scene runs only its own. Set both modes to `Always` only with two different cameras.

## Honest limits

- **NOT TESTED in the Unity Editor or against a live camera.** The scripts compile (headless, against Unity's own assemblies) and the host they use is covered by tests with real sockets, but nobody has pressed Play on these scenes yet. The scene files were written by hand to match the existing small scenes; if Unity reports one as unreadable, delete it and recreate it with the same components (a camera with field of view 35, an `AppBootstrap` with body tracking off, and the showcase component).
- The hand and face **positions** and the **head pose** are the sidecar's estimates (an assumed field of view and an average hand or eye width). The orbs are caught on screen position, not on a measured distance.
- Which eye is which on a live face is the first thing to check: close your LEFT eye; if the avatar closes the wrong one, press `M`.
- The scenes are listed in the Launcher in a fourth column, **SHOWCASES** (with Fluid Disruption, which had no row because the one-person column is at the ten-row cap). A Launcher row only loads once the scene is in Build Settings: run **Virtual Mirror > Register All Scenes in Build Settings** (it also registers everything under Showcase/). ESC returns to the Launcher.
