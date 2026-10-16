# VirtualMirror.Vision - hands, face and the person's cutout from the Python sidecar, in Unity

Receivers for the **hands** wire (UDP 8901), the **face** wire (UDP 8902) and the **cutout** wire (UDP 8903: a camera picture and a soft mask of the person in it, see
"The cutout" at the end) that the sidecar's `hands_udp_sender.py`, `face_udp_sender.py` and `cutout_udp_sender.py` write. A camera that sees
a hand or a face and **no body** is enough: nothing here needs a person. The Python side lives in `python-sidecar~/` (`scripts/run_hands_demo.bat`,
`scripts/run_face_demo.bat`); the wire is documented in `hand_wire.py` and `face_wire.py` and pinned by golden datagrams (`Tests/Editor/VisionFixtures` in this package).

No model, no camera and no native plugin runs in the player: a UDP socket on a background thread and an allocation-free parser.

## Try it in two minutes

1. Start a sender: `python-sidecar~/scripts/run_hands_demo.bat` and/or `run_face_demo.bat` (webcam 0), or `tools/hands/hands_udp_sender.py --video <LAN camera link>`.
2. In any scene, add an empty GameObject with the **Vision > Demo Rig** component and press Play.
   A skeleton per hand, a point cloud and head axes for the face (at their estimated place in front of the main camera), and a status HUD appear.
   The HUD is the first thing to read if nothing moves: `NoStream` = nothing is arriving on that port; `Stale` = it stopped; a rising `rejected` count = the sender and
   this receiver disagree about the wire version.

## Use it from code

```csharp
// one component per stream; it owns the socket and raises events on the MAIN thread
[SerializeField] private HandsReceiverBehaviour hands;
[SerializeField] private FaceReceiverBehaviour face;

private void OnEnable() {
    hands.Updated += OnHands;
    face.Updated += OnFace;
}

private void OnHands(HandsMessage message) {
    int i = 0;
    while (i < message.Count) {
        HandData hand = message.Hand(i);
        // hand.Side, hand.Id, hand.Gesture, hand.Pinch, hand.FingerCurls[0..4], hand.WorldLandmarks[0..20] (Unity metres, hand-centred)
        if (hand.HasPosition) {
            // hand.Position: the hand's centre in camera space, metres, Unity axes (x right, y up, z forward) - an ESTIMATE
        }
        i = i + 1;
    }
}

private void OnFace(FaceMessage message) {
    if (message.Count > 0) {
        FaceData f = message.Face(0);
        float blink = f.Blendshape(ArkitBlendshapes.EyeBlinkLeft);   // the 52 scores, 0..1, by name
        Quaternion head = f.HeadRotation;                             // identity = a frontal face looking at the camera
    }
}
```

Without MonoBehaviours: `new HandsReceiver()` / `new FaceReceiver()`, `Start()`, then `TryGetNew(message)` once per frame with a message you own.

To drive things that already exist in this project: `UdpHandTrackingProvider : IHandTrackingProvider` and `UdpFaceTrackingProvider : IFaceTrackingProvider` give the
existing `HandFrame` / `FaceFrame` (so the humanoid hand retargeter and `VrmExpressionRetargeter` work from a hands-only or face-only camera). To drive ANY character
whose mesh has ARKit-named blendshapes, add **Vision > ARKit Blendshape Driver** (52 scores onto the mesh by name, plus an optional head bone).

## What the numbers mean

| | |
|---|---|
| Spaces | `ImageLandmarks` are the wire's own: x, y fractions of the frame, **y down**, z relative. `WorldLandmarks`, `Position`, `HeadRotation` are Unity's: metres, **y up**, z forward. |
| `Position` | The hand's centre / the point between the eyes in camera space. **An estimate**: from the subject's apparent size and an ASSUMED field of view. Absent (`HasPosition` false, `PositionRefusal` says why) when the sidecar refused the fit. |
| `HeadRotation` | Identity = frontal, upright, looking at the camera. `Yaw`/`Pitch`/`Roll` are degrees as the sidecar defines them (yaw + = nose to the image's right, pitch + = nose down, roll + = clockwise on screen). A geometric estimate, not MediaPipe's own matrix. |
| Blendshapes | 52 scores: index 0 `_neutral`, 1-51 ARKit-compatible, **no tongueOut**. Bind by name (`ArkitBlendshapes`), never by ARKit's own order. Left / Right are the subject's. |
| Mirrored | `message.Mirrored` is true when the sender flipped the picture (a selfie view); positions, yaw and roll are then mirrored too. |
| Held | A `Held` hand or face is the last values, a frame or two stale. Nothing is predicted beyond that; an absent subject is not in the list. |

## Rules of the stream

- `message.Count` is at most the capacity you gave the receiver; `DeclaredCount` says how many the sidecar sent. Extras are counted, not stored.
- A message is reused by its owner: copy what you keep (`CopyFrom`). Arrays on `HandData` / `FaceData` are live: do not modify them.
- A datagram that does not parse is counted by reason (`receiver.CountOf(WireParseResult.X)`), never thrown. An unknown version is refused, not guessed at.
- `receiver.SessionChanges` rises when the sidecar restarts: drop anything you remembered by hand or face id.
- Ids are never reused within a session.

## In this project: AppBootstrap hosts it (H-04)

In this project you do not add the receivers by hand. `AppBootstrap` has a **Hands and Face (H-03)** section (a `VisionSettings`: source, flip, field of view, camera size, provider, per-stream mode and port, every sender threshold, receiver options) and owns a `VisionHost`:

- the two UDP receivers (a stream set to `Off` has none),
- the Python senders, started for you with the settings' command line (`VisionSenderArguments`) and restarted a second after a setting that changes it stops changing,
- the per-frame hand-over: `AppBootstrap.Instance.Vision.HandsUpdated` / `FaceUpdated` (main thread), `HandsMessage` / `FaceMessage`, `StateOf(stream)`, `SenderStatus(stream)`.

A scene asks for a stream with `Vision.AddDemand(VisionStream.Hands)` (and `RemoveDemand` when it ends). In `Auto` mode that is what starts the sender; `Always` runs it whenever the app runs; `Off` closes the socket. A sender somebody else is already running is left alone.

**F6** opens the runtime control panel (`VisionControlPanel`): every setting, the state of both streams (rate, counters, hands or face in view), the command line each sender runs with, restart buttons, Apply and Reset. Panel changes are live but not saved; tick the same setting on AppBootstrap to keep it.

Sample scenes that use all of this: `Showcase/HandsFace` (a hands scene and a face scene).

NOT TESTED in the Editor: the sender launching and the panel. The receivers, the hand-over, the demand rules and the settings-to-flags mapping are covered by `VisionHostTests` and `VisionSenderArgumentsTests`.

## The cutout (S-01): the person in the picture, with a mask

The **cutout** wire (UDP 8903, `cutout_wire.py`) is different in kind: not a small JSON message but a camera PICTURE and a soft person MASK, one frame at a time, cut into datagrams. The sidecar does all the segmenting; this assembly reassembles and shows what it is given.

| Piece | What it is |
|---|---|
| `Wire/CutoutWire`, `CutoutAssembler`, `CutoutFrame` | The 72-byte header reader and the frame reassembly (any order, loss, repeats, restarts, wrap-around; bounded). Buffers are SWAPPED between threads, never copied. |
| `Receiving/UdpDatagramReceiver`, `CutoutReceiver` | The socket and thread (shared with the hands and face receivers) and the cutout receiver: `TryGetNew(CutoutFrame)`, counters (`Accepted`, `Abandoned`, `Overwritten`, `RejectedOf(reason)`). 16 MB receive queue: a 720p frame is ~66 datagrams of 60 KB. |
| `Rendering/CutoutTextures` | The picture and the mask as two GPU textures (`ColorTexture`, `MaskTexture`; `Version`, `Generation`; `People`, `ForegroundFraction`, `InferMs`, `SenderLatencyMs`...). **Row 0 is the TOP of the picture, for both**: sample at `(u, 1 - v)` or use `UvScale` / `UvOffset`. |
| `Behaviours/CutoutView`, `Rendering/CutoutLook`, `Shaders/` | Draws them: Original / Person / Silhouette, glow, mirror, backdrop, fit. Two quads in front of a camera and a URP unlit shader. |

In this project the host serves it: `AppBootstrap.Instance.Vision.CutoutTextures` is one object for the life of the host and `Vision.CutoutUpdated` fires once per uploaded frame. To show it in a scene add a `CutoutView` and a **Cutout Stream Binder** (`Runtime/Bootstrap`); to use the picture and the mask for something else (an emitter, an echo, a transition) use the binder without a view and read the textures. Settings are the "Cutout (S-01)" group of `VisionSettings` (the F6 panel's Cutout tab). The demo scene is `Showcase/Cutout`. Report: `docs/S01_CUTOUT_2026-10-13.md`.

## Using it in another project

Install this package (`com.viitorx.avtar.tracking.vision`; its dependencies `...core` and `...sidecar` come with it: the receivers use `HandFrame`, `FaceFrame`, `ILogService` and the two provider interfaces from core, which has no other dependency). See `docs/INSTALL.md`.
Run the sidecar's senders beside the game. The wire is the whole contract: a receiver in any other engine needs only a UDP socket and a JSON parser (see `tools/hands/hands_udp_receiver.py`
for a 100-line reference).

## Status

**The cutout (S-01)** was compiled, unit-tested in the open Editor (100 new tests) and run in Play mode: `VisionHost` started the sender, the stream went Live and the picture and mask were drawn from two video files. The hands and face parts below were NOT run in Play mode; the host's launching and stopping of a sender through `DirectSenderOnly` was exercised by the cutout run. A person on a live webcam is not yet tested for the cutout either.

Built and unit-tested headless against the Python encoders' own datagrams; the Unity Editor compiled the first revision of this assembly (receivers, behaviours, rigging) with no errors (Editor.log, 2026-10-13); the Host files added after that were compiled headless only. **Not yet run in the Editor's Play mode, against a live camera, or on a rigged character.** The
scene components (receiver behaviours, demo views, HUD, blendshape driver, control panel) are written to the documented API but were NOT exercised in Play mode.
