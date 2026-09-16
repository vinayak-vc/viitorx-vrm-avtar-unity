# Retarget trace harness

Offline replicas + fixtures used by the two retargeting reports.

| file | what it is |
|---|---|
| `vrmrest.py` | parses a `.vrm` (glb) and prints humanoid rest bone directions in **Unity model space** (applies UniVRM's `Axes.Z` import inversion) |
| `trace.py` | faithful Python replica of `KMath` + `KalidokitArmSolver` + `KalidokitPoseSolver` + the driver's `ApplyBone` |
| `audit.py` | whole-body want-vs-got direction tables (`python audit.py 2`, `python audit.py sweep`) |
| `exp.py` | isolating variants, L/R asymmetry, torso facing |
| `exp2.py` | hybrid, noise robustness, torso-yaw gain, spine-bend decay |
| `armaim.py` | prototype of `ArmAimSolver` (ARM RETARGET V1) |
| `armtest.py` | A1-A11 / symmetry / roll-sweep / noise harness for the arm solver |

The **authoritative** ARM RETARGET V1 numbers come from the shipping C#, not these scripts:

* `Tests/EditMode/ArmAimSolverTests.cs` — run via the Unity Test Runner (EditMode).
* `arm_v1_measure.cs.txt` — the measurement bodies run through Unity's `execute_code`; paste one into any
  editor script/`execute_code` call to reproduce the tables in
  `docs/UNITY_ARM_RETARGET_V1_2026-09-08.md`.

These Python scripts exist because they can run with no Unity Editor attached, and they agree with the C#
to within 0.1 deg on every shared measurement.
