# Ego localization → resource occupancy integration

2026-09-26 · isolated Unity 6000.3.21f1 PlayMode and Python service.

- `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl` — **4/4 passed**.
- `python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache` — **342 passed**, existing Starlette/httpx warning.
- Ruff — passed; `git diff --check` — passed.

Production ego input drove one synthetic resource lease through body entry and
confirmed body exit. Server diagnostic: completed=1, claims=0, closed=0, faults=0.
Pedestrian stop/recovery and passenger completion passed; 223 sensor ACKs accepted,
zero rejected; sampled minimum clearance 0.966 m, no sampled collider overlap.
Actor recreation advanced command sequence 248→250.

The lease was prepared by the test harness. Occupancy did not gate actuation.
This is not multi-vehicle admission prevention, continuous Physics safety proof,
actual map validation or performance measurement. Forced disconnect was omitted
in this scenario; Python tests cover conservative loss-of-context occupancy holds.
Source hashes, XML, trajectory trace and full Python output accompany this file.
Existing Unity allocation diagnostics remain; no new Player/Android run was made.
