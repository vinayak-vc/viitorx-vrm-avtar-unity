# Third-party notices: Viitorx Tracking Vision

This package is MIT-licensed (see `LICENSE.md`).

It contains no third-party code. The conventions it reads are MediaPipe's: the 21-point hand landmark order and the 52 face blendshape names (which are Apple's ARKit names), in the order MediaPipe's face model gives them (Apache License 2.0, Google LLC; ARKit names are Apple's). Only names and ordering are used: no MediaPipe code or model is in this package.

It needs the Newtonsoft Json package (`com.unity.nuget.newtonsoft-json`, MIT) through the sidecar package.

The hands, face and cutout models belong to the tracking engine, a separate program with its own notices.
