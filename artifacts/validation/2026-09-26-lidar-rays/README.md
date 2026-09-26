# LiDAR ray evidence

2026-09-26, Unity 6000.3.21f1, isolated PlayMode production-component project.

Command: `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl -Disconnect`.

Four tests passed:

- Foreground cube occludes rear cube; nearest ray hit=2.5 m and side miss=30 m.
- 70 colliders along a ray saturate the 64-entry buffer: INVALID, no false MISS.
- Sensor inside an external collider invalidates the scan instead of claiming misses.
- Production LiDAR/WebSocket/Python ingress retained 64 rays (2 HIT, 62 MISS) at
  the initial check. Socket interruption stopped the vehicle, reconnect resumed
  after sensor recovery, crossing stopped and recovered, passenger completed,
  and the replacement actor retained sequence continuity.

Integration output: 252 sensor ACKs, zero rejected; sampled minimum clearance
0.790 m, no sampled overlap. This is not continuous swept Physics validation or
a promised clearance. Command sequence 41→60 across interruption; 278→280 across
actor recreation. XML, trace and source hashes are retained alongside this file.

Python full suite: 259 passed; Ruff passed. The subsequent transport bound change
from 16 to 64 KiB was separately exercised by Python's full 64-hit WebSocket test
and oversized-message regression. The Unity source manifest predates that small
server bound change; do not claim Unity reran it. First test harness compile
failed because of an unavailable Newtonsoft test reference; it was replaced
with Unity JsonUtility before the passing executions.

Scope: synthetic fixtures only, no Player/Android/original-scene or performance
qualification. The 2D rays do not prove free area between rays, behind returns,
in excluded layers, or at other heights. RRT candidates remain non-executable.
Existing Python dependency deprecation and Unity allocation diagnostics remain.
