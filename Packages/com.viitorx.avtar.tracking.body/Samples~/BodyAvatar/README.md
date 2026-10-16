# Body Avatar sample

A scene with a camera, a light, a **Body Bootstrap** and a **Body Avatar Sample**. Press Play: the bootstrap starts the tracking engine's body sender for your webcam, loads the VRM that is its *Avatar Asset* (`VirtualMirrorRigTemplate.vrm.bytes`,
a plain humanoid rig that ships with this sample) with its control rig, and the avatar copies you. The box in the top-left corner says what the engine, the body stream and the avatar are doing, and why the avatar is held when you are out of range.

Before it works, once:
1. **Install UniVRM** (`com.vrmc.vrm`; it is not on Unity's registry, see the package README). Without it this sample does not compile.
2. **Install the engine** (`viitorx-tracking-setup-<version>.exe`, or unzip the engine folder anywhere and put its path in the Body Bootstrap's *Settings > Engine Path*), and put the **body model** (`rtmw3d-x-fp16.onnx`) in its `models` folder:
   the engine's `fetch-models` does not download it.
3. The camera is `camera:0` (the first webcam). Change *Settings > Source* for another webcam, a LAN camera link, a stream or a video file. Stand 1.5 to 4 m from it, the whole body in the picture, or at a laptop, head and shoulders (an estimated hip).

The avatar stands at the origin looking toward -Z (the bootstrap turns it 180 degrees when it loads, `Avatar Yaw Degrees`: the retargeting works in the avatar's model frame, and with 0 the arms cross the body); the camera is in front of it at -Z. To use your own avatar, assign a VRM (renamed `.bytes`) to *Avatar Asset*, or a path to *Avatar File*.
C (with the legacy Input Manager) or the button takes the neutral again: stand upright and still when you press it.

The body engine owns the webcam while it runs, so the Vision Bootstrap of the vision package cannot use `camera:0` at the same time. Depth is estimated from the picture (an assumed field of view and height), not measured: a mirror, not a measurement.
