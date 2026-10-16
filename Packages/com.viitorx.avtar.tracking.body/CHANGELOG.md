# Changelog

## 0.1.1 (2026-10-17)
- Bump package version to 0.1.1.

## 0.1.0 (2026-10-17)
- `BodyBootstrap` (engine, wire receiver, VRM with control rig, per-frame drive in one component), `BodyAvatarDriver` (the Kalidokit path of the application's `AppBootstrap`: stream failsafe, operating volume, humanized skeleton, Kalidokit solve, fingers and wrist, root follow), `BodySettings`, `BodyEngineArguments` (the `body` command line, pinned by fixtures the engine's own parser also reads). Sample: Body Avatar.
- `BodySettings.AvatarYawDegrees` (180): the avatar is turned before it is bound, as the application does on load; without it the arms cross the body (found by driving the application's own `AppBootstrap` and the package with the same datagrams: with the turn the bones match to 0.00 degrees).
- Extracted from the application: `OakDUdpPoseProvider`, `TrackingStreamHealth`, the joint filter and the fake providers (`VirtualMirror.Tracking`), `Runtime/Retargeting`, `Runtime/Avatar`, `Runtime/IK`. Code unchanged; assembly names and every GUID unchanged.
- `Retargeting`, `Avatar`, `Body` and their tests compile only when UniVRM (`com.vrmc.vrm`) is installed (`VIITORX_UNIVRM`); an Editor warning says so when it is not.
- The in-process MediaPipe and Sentis providers stay in the application (`VirtualMirror.Tracking.Inference`); this package references neither `Mediapipe.Runtime` nor `Unity.InferenceEngine`.
