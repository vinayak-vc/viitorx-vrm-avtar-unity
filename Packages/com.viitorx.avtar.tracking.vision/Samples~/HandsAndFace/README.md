# Hands and Face sample

A scene with a camera, a **Vision Bootstrap** and a **Hands And Face Sample**. Press Play: the sample asks the bootstrap for the hands and face streams, the bootstrap starts the
tracking engine for them (ONE process, the camera is read once) and the hands (21 points each) and the face (point cloud and head axes) appear in front of the camera,
with a status box in the top-left corner that says which engine is running and what each stream is doing.

Before it works, once:
1. **Install the engine** (`viitorx-tracking-setup-<version>.exe`, or unzip the engine folder anywhere and put its path in the Vision Bootstrap's *Engine > Engine Path*). The sample says
   "engine: not found" and lists every place it looked at when there is none.
2. **Fetch the models** (`viitorx-tracking fetch-models`, or tick the box in the installer).
3. The camera is `camera:0` (the first webcam). Change *Source* on the Vision Bootstrap for another webcam, a LAN camera link, a stream or a video file.

F6 opens the settings panel at run time (Active Input Handling must include the Input Manager for the key; the panel can also be opened with *Show Panel On Start*).
Hand and face positions are the engine's ESTIMATES (an assumed field of view): a mirror, not a measurement. Nothing here needs a body or an avatar.
