# Third-party notices: Viitorx Tracking Body

This package is MIT-licensed (see `LICENSE.md`). It contains the following third-party work, and depends on the following software that it does NOT contain.

## Contained

### Kalidokit (yeemachine)

The pose and arm solver mathematics in `Runtime/Retargeting/Kalidokit/` (`KMath.cs`, `KalidokitArmSolver.cs`, `KalidokitPoseSolver.cs`) is a C# port of
[Kalidokit](https://github.com/yeemachine/kalidokit) (the `PoseSolver`, `calcArms` and `rigArm` functions and their helpers). The port is adapted: it solves from the
engine's 33-point body frame onto a VRM normalized control rig, and the arm path was replaced by a quaternion aim solver (`ArmAimSolver.cs`, our own).

```
MIT License

Copyright (c) 2020-2021 Richard Yee

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### One Euro filter (Casiez, Roussel, Vogel)

`Runtime/Tracking/Filtering/OneEuroFilter.cs` implements the algorithm of G. Casiez, N. Roussel and D. Vogel, "1 Euro Filter: A Simple Speed-based Low-pass Filter for Noisy
Input in Interactive Systems", CHI 2012. The code is written for this package from the published algorithm. The authors' reference implementation is distributed under the
BSD 3-Clause licence, copyright (c) 2012 Gery Casiez, Nicolas Roussel, Daniel Vogel; if you consider this file a derivative of it, that licence applies to it.

### Conventions, not code

The 33-point landmark order of the body wire is that of MediaPipe's BlazePose (Apache License 2.0, Google LLC); the hand wire uses MediaPipe's 21-point order. Only the
ordering of the points is used. No MediaPipe code or model is in this package.

### The sample avatar

`Samples~/BodyAvatar/VirtualMirrorRigTemplate.vrm.bytes` (and the test copy under `Tests/Runtime/Data/`) is a plain humanoid rig built by ViitorCloud with Blender
(`tools/blender/build_rig.py` in the repository); it is covered by this package's MIT licence.

## Required, not contained

These are installed separately and keep their own licences:

| Software | Licence | Used by |
|---|---|---|
| UniVRM `com.vrmc.vrm` and `com.vrmc.gltf` (VRM Consortium, DWANGO Co., Ltd.) | MIT | `Retargeting`, `Avatar`, `Body` (not on Unity's registry: see the README) |
| Unity Animation Rigging `com.unity.animation.rigging` | Unity Companion Licence | `VirtualMirror.IK` |
| Newtonsoft Json `com.unity.nuget.newtonsoft-json` | MIT | the wire parsers |

The tracking engine that produces the body stream is a separate program with its own notices (`THIRD_PARTY_NOTICES.txt` in its folder). The body model it needs
(RTMW3D, from OpenMMLab's MMPose) is not distributed with it: check the terms of the weights you obtain.
