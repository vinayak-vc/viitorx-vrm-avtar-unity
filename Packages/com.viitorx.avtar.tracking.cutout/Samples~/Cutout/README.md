# Cutout sample

A scene with a camera, a **Vision Bootstrap** (hands and face off, cutout on), a **Cutout View** and a **Cutout Sample**. Press Play: the bootstrap starts the tracking engine's cutout
stream, the engine finds the person in the camera picture and sends the picture with a soft mask, and the view draws it. The buttons in the top-left corner choose what you see
(the camera as it is, the person alone, or the person as one colour) and what is behind the person (nothing, a checker, a colour, a gradient). With the legacy Input Manager the keys 1, 2, 3 do the same.

Needs the **Universal Render Pipeline** (the two shaders include its core library; a project on the built-in pipeline cannot use this package) and the tracking engine with its models
(see the Hands and Face sample for the one-time setup). The two materials of the sample (`Materials/`) use the shaders `VirtualMirror/Cutout` and `VirtualMirror/CutoutBackdrop`; keeping
them referenced by the scene is what includes the shaders in a player build.
