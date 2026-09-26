# MVP implementation and verification ledger

2026-09-26 · working tree based on project version 0.2.4.0

The objective remains a campus passenger/cargo simulation using Unity Physics
and sensor observations with Python service/planning/control. A synthetic demo
is an intermediate verification gate. AGENTS.md sections 14 and 16 define the
full acceptance criteria; this ledger does not reduce that scope.

| Gate | Current evidence | Remaining requirement |
|---|---|---|
| Service regression | 102 baseline tests and 111 tests after safety extraction passed in isolated Python 3.12 venv | Continue behavior checks as modules and features change |
| Responsibility boundaries | Sensor safety policy/evaluation extracted from service; ADR 0001 | Route planning, dispatch and service orchestration still share a large service module |
| Python/C# transport | Current source smoke passed: PC/Mobile, localization, sensor ACK, requests/cancel, reconnect/replay, speed profile | Original scene/UI lifecycle and mobile device round trip |
| M1 vertical flow | Isolated Windows test Player completed mobile passenger and PC cargo missions with actual Raycast stop/recovery; 110 OD × 20 planner comparison passed | Original scene/vehicle visual prefab integration; actual map remains M2 |
| M2 real campus | OSM candidate survey and review validator exist | Verified graph, Stop/landmark/accessibility/facility coverage and Biryong geometry; no evidence may be invented |
| M3 crowd/replanning | Synthetic timed prior, AVOID and A* replan baseline | Observed crowd/hysteresis/CLOSED, D* Lite comparison |
| M4 safety/local planning | 2D raycast source and conservative range gate | General sensor geometry/occlusion, Radar/TTC/RRT, dynamic braking (one physical wall/range-gate case verified) |
| M5 fleet | Synthetic 3-vehicle dispatch with Greedy/Hungarian | Energy/charging/hubs, Reservation/CBS, route-derived comparisons, pedestrian service lifecycle |
| M5a clients | DTOs, role projections and synthetic networking | Authoritative client end-to-end, session isolation, real devices, 50 connections |
| M6 evidence | Limited synthetic solver artifacts | E1–E4 integrated runs, replay, 300/1,000 pedestrians, performance/load raw data and OSS evidence |

## Current reproducible checks

From the repository root, with a Python environment containing `backend[dev]`:

```powershell
& ./tmp/backend-venv/Scripts/python.exe -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache
& ./tmp/backend-venv/Scripts/python.exe -m ruff check backend AgentScripts/MapData
& ./AgentScripts/UnityWebSocketSmoke/run.ps1 -PythonPath (Join-Path (Get-Location) 'tmp/backend-venv/Scripts/python.exe')
& ./AgentScripts/RunUnityFollowerTests.ps1
```

The follower runner copies current source and tests into a unique temporary
Unity project and records source SHA-256 values. It validates only the follower,
its DTO/coordinate dependencies, and real PlayMode Physics. It is not evidence
for full scene import, sensors, network integration, or production Player builds.
No follower run is considered passed unless its test XML reports passing tests.

## 2026-09-26 follower result

The isolated PlayMode run passed 7/7 tests on Unity 6000.3.21f1. Raw XML and
source hashes are in `artifacts/validation/2026-09-26-follower-playmode/`.
The first restricted run could not access licensing IPC; the successful run used
an approved process outside that restriction. The original Editor stayed open.

The first executable test run exposed test setup faults (Transform teleports not
yet synchronized to Rigidbody, and a movement assertion before initial rotation).
Tests now initialize the Rigidbody consistently, start eastbound route tests with
eastward heading, and clean up actors even after failed assertions. Runtime review
also found that server speeds could bypass the local preview cap; the follower now
uses the minimum of both limits and a new high-server-speed regression passed.

Next integration gate: use the real sensor reporter and WebSocket source with
Unity Physics, observe request pickup/dropoff completion, and record both sides'
states. Isolated follower success is not a substitute for this gate.

## 2026-09-26 service and Player gate

The production C# host/spawner/follower/localization/Raycast/transport source now
passes the combined passenger/cargo scenario in both isolated PlayMode and an
actual headless Windows test Player. Mobile calls the passenger request; PC sees
its pickup/dropoff completion and creates a cargo return. Python receives only ego
pose and real ray detections, commands a stop before a physical wall, holds clear
recovery, and completes both missions. Mobile remains scoped to its passenger request.

Player accepted 322 pose and 322 sensor frames (0 sensor rejections). Artifacts are
in `artifacts/validation/2026-09-26-service-physics-player/`; PlayMode evidence is in
`artifacts/validation/2026-09-26-service-physics-playmode/`. The Player emitted native
allocation-lifetime warnings; correctness assertions passed but performance/memory
quality is not verified. No claim is made for the original scenes, vehicle visual
prefabs, graphics/UI, Android or multiple Physics vehicles. Test shell/graph dimensions
are explicitly synthetic. The Player is a validation build, not a production release.

Fresh T03 comparison matched all 110 OD costs over 20 repetitions, with 2,200 timing
samples per algorithm. These timings are diagnostic and not campus performance proof.

Next: integrate the original preview scene/prefabs, expand sensor/authority fault
cases, and continue full map, safety, crowd, fleet, client and experiment gates.

## 2026-09-26 original prefab EditMode gate

Unity 6000.3.21f1 passed 11/11 selected EditMode tests using the original three
vehicle wrappers and their resolved visual dependencies in an isolated project.
Tests cover unit roots, kinematic bodies, renderer/BoxCollider bound agreement,
missing scripts, route authority, and immutable full server route rendering.
ApplyRoute now resolves Rigidbody before Awake when necessary. Held-color checks
allow the LineRenderer's 8-bit channel precision. Evidence and source hashes:
`artifacts/validation/2026-09-26-prefab-editmode/`.

Run with `AgentScripts/RunUnityPrefabTests.ps1 -PythonPath <python-with-backend-dev>`.
This does not verify forward axes, ground contact, vehicle dynamics, original scene
Player behavior, Android, or rendering performance. Earlier Player/follower artifacts
remain evidence for their recorded source hashes; the subsequent Rigidbody lookup
and route display changes were checked by this EditMode run.
