# Unity/Python Physics integration

Run from the repository root with Unity 6000.3.21f1 (including Windows build support
for Player mode) and a Python environment containing `backend[dev]`:

```powershell
./AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath ./tmp/backend-venv/Scripts/python.exe
./AgentScripts/RunUnityPhysicsIntegration.ps1 -Player -PythonPath ./tmp/backend-venv/Scripts/python.exe
```

The runner creates a unique `tmp/` Unity project, copies the actual Domain/Networking
code and selected production Physics/presentation components, records input hashes,
and starts a temporary loopback Python server. It never stops an existing Editor or
server. An occupied port is rejected; select `-Port` to use another port.

The fixture is a synthetic 0/4/8 m straight-line graph. The production service,
spawner, Rigidbody follower, localization reporter, Raycast rig and WebSocket source
execute without mocked sensor frames or manually injected vehicle poses. A small
test vehicle shell is used instead of the campus vehicle visual prefab. The existing
graph marker prefabs can visualize this synthetic graph; their transforms are not
the graph authority.

The scenario checks:

1. Mobile role calls a passenger mission from the middle Stop to the last Stop.
2. Unity drives to pickup and reports its measured ego pose; Python advances service.
3. Raycast observes a physical obstacle. Python grants obstacle-stop authority;
   the vehicle brakes and remains before the obstacle.
4. Removing the obstacle produces the server clear-frame hold, recovery, driving,
   dropoff and completion. Mobile and PC observe the same completed request/run.
5. PC creates a cargo return mission which completes through the same Physics path.
   Mobile remains scoped to its own passenger request.

`-Player` builds a Windows test Player, closes the temporary build Editor, then starts
the Player hidden with `-batchmode -nographics`. The Player records NUnit XML directly,
so Editor execution is not mistaken for Player execution. The production project
scene/build profiles, actual vehicle visual prefabs, Android/UI, graphics performance,
multi-vehicle conflicts and real-map service are outside this test's scope.

Outputs include `results.xml`, `source-manifest.json`, `snapshot-trace.csv` and process
logs. The CSV request-status column is the first request in the PC snapshot; after the
passenger completes, cargo completion is asserted separately by its request ID in the
test. Do not interpret that column as the active vehicle mission during the return trip.

## Radar mode

Pass `-Radar` to run the same isolated passenger/cargo/obstacle scenario with the
Raycast Radar range-rate abstraction. Default remains LiDAR. `-Player -Radar` can
build that variant, but do not infer Player verification from a PlayMode run.
`relativeSpeedMps` is signed observed range change per second (negative approach),
not Doppler or a complete relative velocity vector. Existing distance safety gate
remains authoritative; this mode alone does not complete TTC/RRT safety.

## Moving pedestrian variant

Pass `-Pedestrian` to use a Unity-owned kinematic capsule actor instead of deleting
the wall at the recovery step. It is initially paused on the vehicle path, then
walks to a synthetic lateral waypoint and remains active. Completion requires its
motion to finish and passenger/cargo service to complete. Python receives only
sensor detections. This does not validate full pedestrian population dynamics.

## Explicit zone closure variant

Use `-ZoneClosure -TestFilter InhaExpress.Client.Tests.VehicleServiceIntegrationTests`.
The runner selects `physics-zone-integration-3.json`, a separately versioned synthetic
fixture whose edges have the configured test zone ID. Only this scenario enables
POST `/test/closure/{closed}` on the loopback harness app. No closure endpoint is
added to production `campus_sim.api`. The test closes the zone after motion begins,
waits for ZONE_CLOSED and bounded follower braking, checks a held position, opens
the zone and requires sensor clear hold before continuing the full service flow.
# Lateral crossing variant

Use `-Crossing` for the dedicated LiDAR pedestrian crossing scenario. It selects
`CrossingServiceIntegrationTests`, enables a test-shell-only crossing policy on
the Python harness, and rejects combination with Radar/Pedestrian/ZoneClosure
switches. Production API/configuration is unchanged. The test checks a stop
outside the forward range sector, braking, clear hold, passenger completion and
sampled collider clearance. Bounds are synthetic; this is not a full safety gate
or a swept collision/performance certification. Evidence and limits are in
`artifacts/validation/2026-09-26-crossing-physics/`.

## Python command actuator variant

Add `-PythonControl` to `-Crossing` to enable Python route steering and the opt-in
Unity command actuator. The ordinary follower is disabled and the same crossing
scenario verifies the new WebSocket field through actual passenger completion.
The switch currently requires Crossing; original scenes are unchanged. Command
expiry, replay/binding rejection and physical braking have separate component
coverage in `RunUnityFollowerTests.ps1`. See `Docs/python_control_contract.md`.

## Socket interruption variant

Add `-Disconnect` to `-Crossing -PythonControl` to close the PC socket during
motion and reject reconnect for 1.5 seconds. A test-only ASGI harness provides
the fault endpoint; production API is unchanged. The scenario verifies expiry
braking, stationary hold, same-session reconnect, fresh-sensor recovery and
passenger completion. See `artifacts/validation/2026-09-26-socket-recovery/`.
