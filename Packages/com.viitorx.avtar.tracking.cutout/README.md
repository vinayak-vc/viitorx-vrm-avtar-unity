# Viitorx Tracking Cutout

`CutoutView` draws what the cutout stream delivers (the camera picture and a soft person mask) through two shaders, `Cutout` and `CutoutBackdrop`, with `CutoutLook`
(mode, backdrop, mirror, fit). The stream itself (wire, assembler, receiver, textures) and the host are in `com.viitorx.avtar.tracking.vision`.

The shaders include the Universal Render Pipeline's `Core.hlsl`: **a project on the built-in pipeline cannot use this package.** The package depends on
`com.unity.render-pipelines.universal` for that reason.

Sample: **Cutout** (Package Manager > this package > Samples): a Vision Bootstrap, a Cutout View with its two materials and a Cutout Sample with on-screen buttons.

Assembly: `VirtualMirror.Cutout` (namespace `VirtualMirror.Vision`, kept from where the code came from).

Status: extracted from the Virtual Mirror application, not yet released.
