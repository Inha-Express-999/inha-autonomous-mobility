# Resource admission resume — 2026-09-26

Uncommitted work after v0.3.2.0. This supersedes the failed admission/restart
claim for the exact source hashes in this directory; prior artifacts remain intact.

Commands:
- `python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache`: 353 passed, existing Starlette/httpx warning.
- `python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration`: passed.
- `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl`: isolated Unity 6000.3.21f1 PlayMode **4/4 passed**.

The old stop envelope covered the instantaneous body only. Valid between-sample
motion bounds could still overlap the corridor, recording unplanned occupancy
and latching closure. The admission hold now includes half the configured maximum
speed times the maximum valid sample gap, matching the occupancy midpoint-disc
bound, in addition to body radius, position error and stop margin. The occupancy
fault policy was not relaxed and existing closures are not automatically cleared.
Python regression cases reproduce the previous failure and check stable holds
and explicit release/resume at 0.1, 0.2 and 0.5 second observation gaps.

Actual Unity V01 held at x=0.091 m before the corridor starting at x=1 m.
Before release: only the fixture exit blocker owned a resource; no closure or
tracker fault. After explicit release: V01 resumed, handled the crossing pedestrian,
completed passenger service and released its reservation. Final summary: completed=1,
claims=0, closed=0, faults=0. Sensor ACKs=241, rejected=0; sampled minimum collider
clearance=0.979 m (a test measurement, not a continuous collision proof).
Actor recreation continued command sequencing. Three standalone LiDAR checks passed.

The exit blocker is abstract test occupancy, not a second Physics vehicle. No
Player/Android, real map, multi-vehicle Physics, performance or full M5 completion
claim. Unity allocation diagnostics and the dependency deprecation warning remain.
