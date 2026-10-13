# Cutout showcase

The person in front of a camera, **cut out of the picture** and shown on their own, in one scene (S-01, ADR-103, report `docs/S01_CUTOUT_2026-10-13.md`). The Python sidecar finds the person and makes the mask; Unity only receives the picture and the mask and decides how to draw them. Nothing here depends on the VRM avatar or on skeleton tracking.

## Run it

1. First time only: `python-sidecar~/.venv/Scripts/python.exe python-sidecar~/tools/model/fetch_cutout_models.py` (the two selfie networks, 0.46 MB each) and, for one crop per person, `tools/model/fetch_yolox_s.py`. The models are not in version control.
2. Open `Scenes/CutoutShowcase.unity` and press Play. The scene carries its own `AppBootstrap`, which starts the cutout sender on `camera:0`. The first frame takes a few seconds (the networks load; the banner says so).
3. **Pick the camera** in the panel on the left: `<` and `>` step `camera:0`, `camera:1`, ..., or type a source (a video file, a LAN camera link) and press Apply. The sender restarts on it a second later. Other cameras, a phone, rotation, resolution and every segmentation setting: the AppBootstrap "Hands and Face (H-03)" section in the Inspector, or the F6 vision panel (its Cutout tab).
4. From the Launcher: **Virtual Mirror > Register All Scenes in Build Settings** once, then the SHOWCASES column, HUMAN CUTOUT. ESC returns.
5. Without Unity: `python-sidecar~/scripts/run_cutout_demo.bat` (the sender with its own window), or `tools/cutout/cutout_udp_receiver.py --view` to see what Unity would get.

## What you see and the keys

| | |
|---|---|
| `1` Original | The camera picture as it is. |
| `2` Person | Only the person, from the picture, on a transparent, checkered, plain, gradient or image backdrop. |
| `3` Silhouette | The person as one colour. Red first. |
| `B` | Backdrop: none (the scene behind shows through), checker, colour, gradient, image (skipped until a picture is assigned to `CutoutDemo`). |
| `C` | Silhouette colour: red, white, cyan, magenta, yellow, green, black. |
| `G` | A glowing outline around the person. |
| `M` | Mirror: Auto (a live camera is shown as a mirror, a file is not), off, on. |
| `F` | Fit: contain the whole picture, or cover the view. |
| `,` `.` | The previous / next camera. |
| `H`, `I` | Hide the panel, hide the notes at the bottom. |
| `F6` | The vision control panel (all settings, sender status and command lines). |

Every switch is a shader parameter. Nothing restarts and the camera is not reopened. The status block (top right) shows the stream state, the frame rate, the picture and mask sizes, how many people, the foreground area, the segmentation time and the sender's latency, and Unity's own upload time.

## How it is wired

- The scene's `Tracking (AppBootstrap)` has the body path off and only the **cutout** stream on (`Auto`): scene scripts never open a socket or start a process; `CutoutDemo` asks `AppBootstrap.Instance.Vision` for the stream and hands its textures to a `CutoutView`.
- The same two textures (`VisionHost.CutoutTextures`: the picture and the mask, row 0 = the top of the picture) are available to anything else. To show the cutout in another scene, add a `CutoutView` and a **Virtual Mirror > Cutout Stream Binder** to it (the scene needs an AppBootstrap); to read the textures for another effect, use the binder without a view.
- A `CutoutView` needs its two materials (`Materials/CutoutPerson.mat`, `CutoutBackdrop.mat`) assigned in a scene you build yourself: they are what pulls the two shaders into a player build (`Shader.Find` works only in the Editor, and the view logs an error if it cannot find them).
- `Editor/CutoutSceneBuilder.cs` builds the scene and its two materials from code (**Showcase > Cutout > Build Showcase Scene**). It never changes Build Settings.
- One webcam can be read by one program at a time: do not run this beside the Hands or Face showcase on the same camera (each stream is `Auto`, so only the scene that is open runs its sender).

## Honest limits

- The model is a small selfie network applied once to a crop of each person. **Dark clothes on a dark background** and **bright floor lights** can be kept or cut wrongly, **thin gaps** between an arm and the body are filled (a pale halo), two people who **touch** are not separated cleanly, and the outline is soft and flickers a little. The notes at the bottom of the screen say so on purpose.
- **Not tested on a person in front of a webcam** (tested on two video files, and on a webcam that saw nobody). Frame rate 18-30 depending on the clip and the GPU.
- **Not built:** particle silhouettes, the camera-to-silhouette transition, disperse and reform, motion echo. They would read the same two textures.
- A built player has no sender (`tools/` is not shipped): start `cutout_udp_sender.py` by hand.
