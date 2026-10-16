# Viitorx Tracking Core

The shared vocabulary of the Viitorx tracking packages: `PoseFrame`, `HandFrame`, `FaceFrame`, `CrowdFrame` and their joint / landmark ids, the provider interfaces
(`IBodyTrackingProvider`, `IHandTrackingProvider`, `IFaceTrackingProvider`), the humanizer (`HumanizedSkeleton`, `OperatingVolume`), `PoseBuffer`, `PoseSpaceConverter`,
and the avatar and camera interfaces. It has no dependency on any other package and no behaviour of its own that needs a scene.

Assembly: `VirtualMirror.Core` (the assembly name is kept from the application this was extracted from, so existing references keep working).

Install (Unity 6000.3+), with the other Viitorx tracking packages: see the `com.viitorx.avtar.tracking` umbrella package.

Status: extracted from the Virtual Mirror application, not yet released. See `CHANGELOG.md`.
