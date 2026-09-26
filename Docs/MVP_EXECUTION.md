# MVP implementation and verification ledger

2026-09-26 · working tree based on project version 0.3.0.0

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

## 2026-09-26 explicit telemetry ownership

Vehicle physics components no longer discover the first scene host. The spawner
injects its PC owner into the localization reporter; the sensor uses the same owner.
A replacement host retires old actors even with an unchanged run ID. Mixed PC/Mobile
host PlayMode service integration passed (352 pose/351 sensor ACK, zero rejections).
See ADR 0002 and artifacts/validation/2026-09-26-telemetry-ownership. Full MVP remains
open; this change is not server authentication or a production scene/device check.

## 2026-09-26 fleet lifecycle and mobile completion

PC full snapshots now retire omitted actors immediately and recreate externally
destroyed actors when still present in the fleet. Ego/sensor tick allocation belongs
to the runtime session, preventing duplicate ticks after actor recreation.
Mobile terminal request projection clears the live vehicle reference while keeping
server/PC assignment history. This fixes a WorldStateStore missing-V01 rejection
and repeated reconnect after completion, which the earlier raw snapshot-event
assertions did not catch. The test now checks the actual mobile store continues
accepting snapshots while the former vehicle serves cargo.

Verification: EditMode 14/14, Physics/Python PlayMode 1/1, Python/MapData 111/111 and
Ruff passed. Final PlayMode accepted 391 pose/390 sensor frames with zero rejections;
no missing-reference reconnect warning appeared. Evidence is in
`artifacts/validation/2026-09-26-fleet-lifecycle/`. Allocation-lifetime warnings and
transient sensor stale holds remain; no performance or full MVP claim is made.

## 2026-09-26 TTC primitive

Added the section 9 constant-relative-velocity circle-contact solver and explicit
maximum two-second prediction horizon in `campus_sim.collision`. Targeted pytest
22/22 and Ruff passed. No new full-suite run was needed for this standalone module;
the prior 111-test service run remains separately recorded. This module is not yet
called by runtime safety. Sensor tracking/frame compensation, collision envelopes,
uncertainty and controller integration remain required before M4 can be achieved.
See `Docs/sensor_safety_prototype.md` for exact scope and assumptions.

## 2026-09-26 Raycast Radar mode

The sensor rig now supports Radar nearest-collider returns and signed range-rate
estimation from consecutive visible hits, plus configurable FOV. Existing LiDAR
prefabs keep their default behavior. Lost/invalid/expired observation baselines
cannot produce a rate. No unobserved actor motion enters the estimate.

Original prefab/Physics EditMode 15/15 and the Radar-mode Python/Unity PlayMode
passenger/cargo/obstacle/recreation scenario 1/1 passed. Source hashes and raw results
are in `artifacts/validation/2026-09-26-raycast-radar/`. This validates the Radar
abstraction and existing range gate, not full TTC, Doppler, vector tracking or
verified physical braking. Radar Player/original scene/performance remain untested.

## 2026-09-26 Radar approach authority

Fresh Radar range-rate now adds a configurable early approach stop to the existing
range safety gate. It uses the existing OBSTACLE_STOP authority and clear hold;
missing rate never invents velocity. Python/MapData 142 tests and Ruff passed.
The real Physics/Python Radar scenario also passed, requiring a stop at x in [2,4]
before obstacle removal, then passenger/cargo completion and actor recreation.
Evidence: `artifacts/validation/2026-09-26-radar-approach-gate/`.
This does not complete crossing TTC/vector tracking or full M4 safety.

## 2026-09-26 grouped actor sensor visibility

Fixed Raycast self-filtering: the actor root is the rig's transform, not the scene's
topmost grouping transform. Only ego descendants are filtered; sibling actors are
observable. Actual EditMode Physics tests cover both pedestrian and vehicle layer
classification under a shared parent, ignoring an ego child collider, and nearest
wall occlusion without leaking hidden entity class. 17/17 tests passed; evidence is
in `artifacts/validation/2026-09-26-sensor-occlusion/`.

This validates tagged collider visibility only. Pedestrian motion/population,
vector tracking, full scene/Player transport and crowd load remain incomplete.

## 2026-09-26 Unity-owned moving pedestrian

Added the independent `InhaExpress.Simulation` assembly and
`SyntheticPedestrianWalker` with copied finite waypoint paths, fixed-step kinematic
movement, pause/resume and endpoint completion. No networking dependency or hidden
actor telemetry is introduced. See ADR 0003 for architecture and limitations.

`RunUnityPhysicsIntegration.ps1 -Pedestrian` passed the real PlayMode service flow:
vehicle stops for a pedestrian, the pedestrian walks laterally clear while staying
active, server clear hold releases the vehicle, and passenger/cargo missions finish.
Evidence: `artifacts/validation/2026-09-26-moving-pedestrian/`.
This is one synthetic moving actor, not full pedestrian service/crowd behavior,
real-map routing, animation or 300/1,000-agent performance validation.

## 2026-09-26 reproducible population fixture

`SyntheticPedestrianPopulation` adds validated deterministic lane placement,
300-person generation, group pause and immediate retirement followed by cleanup.
It remains in the Unity-only simulation assembly. Inputs are scenario assumptions;
no Python perception receives the population's ground-truth positions.

The filtered PlayMode lifecycle test passed: 300 capsules, actual Rigidbody motion,
pause, identical respawn positions, and zero old colliders after cleanup. Evidence:
`artifacts/validation/2026-09-26-population-300/`. This is population lifecycle
verification only, not full 300-person crowd behavior or performance acceptance.
Mutual avoidance, observed density/time-of-day demand, service lifecycle, rendering
and combined load still remain.

## 2026-09-26 observed zone transition component

Implemented a separate immutable observed-density zone policy with immediate
closure, ten-second continuous low-density reopening, unknown/gap reset and
prior/observation separation. 15 targeted tests and Ruff passed. It is not wired
to live routing yet: sensor-derived deduplicated counts, coverage/area geometry,
active-route closure handling and client presentation remain required. See
`Docs/zone_observation_policy.md`. Full MVP status remains incomplete.

## 2026-09-26 explicit zone closure routing and authority

Added internal graph-zone closure control. Closed edges are excluded from every
planning branch including avoidance fallback; route-duration caches are invalidated.
Affected active routes and unreachable future dropoffs hold with ZONE_CLOSED and
unknown ETA. Sensor refresh cannot restore authority while closed, and measured
Unity vehicle speed is preserved. Five added regression cases and the complete
162-test Python/MapData suite passed; Ruff passed.

Observed density/coverage is not fabricated or connected yet. Automatic safe egress,
accessible alternative Stops, original map/UI integration and a dedicated Unity
closure scenario remain open. See `Docs/zone_observation_policy.md`.

## 2026-09-26 explicit closure in Unity Physics

The dedicated `-ZoneClosure` PlayMode scenario passed using a separately versioned
synthetic graph and test-only loopback closure input. After actual movement, server
ZONE_CLOSED caused the follower to brake and hold; reopening entered sensor recovery
hold, then the scenario completed passenger/cargo missions and telemetry recreation.
Ruff passed. Evidence: `artifacts/validation/2026-09-26-zone-closure-physics/`.

This resolves only the isolated Physics closure round trip. Observed-density input,
real zone geometry, alternate accessible Stops, safe egress, original scene and
performance gates remain incomplete.

## 2026-09-26 visible identity and duplicate-return handling

Added optional bounded observed entity IDs in Unity/Python transport and a fresh
pedestrian-return summary that deduplicates known IDs without treating anonymous
ray returns as people. Different session/map contexts cannot be merged. Hidden
objects do not emit IDs. 168 Python/MapData tests, 17 EditMode tests, the moving
pedestrian Physics integration and Ruff passed. Evidence is in
`artifacts/validation/2026-09-26-observed-identity/`.
Zone geometry/coverage/area, EMA, vector tracking and automatic closure integration
remain incomplete; see `Docs/observed_pedestrian_returns.md`.

## 2026-09-26 capture pose and observed motion tracking

Added coherent optional sensor capture time/world-pose metadata, scale-independent
metre conversion and invalidation for tilted planar sensors. Validated ingress now
produces bounded consecutive visible-surface motion estimates with ego translation
and rotation compensation. A fresh diagnostic accessor expires old estimates.
Full Python suite 174 passed before final accessor addition; subsequent tracking
suite 7/7, Ruff, Unity EditMode 17/17 and moving-pedestrian integration passed within
their recorded source scopes. Evidence: artifacts/validation/2026-09-26-capture-motion.
TTC actuation/uncertainty/footprints and full MVP remain incomplete.

## 2026-09-26 out-of-order capture protection

A delayed/replayed frame previously replaced the motion tracker's baseline before
its tick/time check, so a subsequent valid capture could produce a false velocity.
The tracker now leaves the newer baseline intact. Ingress rejects non-increasing
capture times in the same sensor/session with `non_monotonic_capture_time`, without
changing the accepted observation. New-session resets remain allowed.

Tracking/API tests: 28 passed, with the existing dependency deprecation warning;
Ruff passed. Added cases include a late old tick, a higher tick with regressed
capture time and an invalid delayed frame. This is Python regression evidence;
no new Unity run is claimed for this change. Full TTC actuation remains pending.
