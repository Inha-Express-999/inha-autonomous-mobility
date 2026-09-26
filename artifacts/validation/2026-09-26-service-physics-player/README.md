# Recorded Windows Player service integration

- Unity 6000.3.21f1, actual Windows test Player, Mono, `-batchmode -nographics`.
- Combined mobile passenger/PC cargo scenario passed 1/1: Physics movement, actual
  Raycast wall detection, Python stop/clear hold/recovery, pickup/dropoff completion,
  return cargo completion, and mobile request projection.
- Accepted pose ACKs: 322; sensor ACKs: 322; sensor rejections: 0.
- The Player emitted native allocation-lifetime warnings recorded in `player.log`.
  This run proves the listed assertions, not performance or memory quality.
- Raw XML, snapshot trace and source hashes are provided. The CSV requestStatus
  column refers to the first request; cargo completion is asserted separately by ID.
- A fresh T03 run is also included: 110 OD × 20 repetitions, all route costs match;
  2,200 timing samples per algorithm. Timing values are synthetic diagnostics.

Scope: isolated source-copy project, synthetic 3-stop graph and small collider shell.
This is not the original scene/vehicle visual prefab, Android/UI, real-campus graph,
multi-vehicle safety or a production release build.

Reproduction:

```powershell
./AgentScripts/RunUnityPhysicsIntegration.ps1 -Player -PythonPath ./tmp/backend-venv/Scripts/python.exe
./tmp/backend-venv/Scripts/python.exe -m campus_sim.cli route-compare --map maps/fixtures/campus-synthetic-benchmark-11.json --warmups 2 --repetitions 20
```
