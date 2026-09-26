# Opposing fleet resource integration

2026-09-26 · uncommitted work after v0.3.2.0

The dedicated `-Reservations` runner creates two actual 0.4 m Unity Rigidbody
shells, V01 and V02, on `synthetic-physics-reservation-v1`. Their passenger
missions approach the shared corridor from opposite directions, with separate
staging and destination branches. Coordinates, vehicle dimensions and resource
rectangles are explicit test assumptions, not campus data.

The Python service creates both requests through its normal application method
from a loopback-only setup endpoint. Production dispatch, route planning,
WebSocket snapshots, Python control, Unity actuator, ego reporting and LiDAR
ingress run normally. This isolates fleet movement; mobile request transport is
covered separately and is not claimed by this scenario. V03 may appear as an
inactive catalog vehicle but has no runtime or Physics actor in this scenario.

All three resources (corridor and both exits) form one atomic reservation group.
An already completed entrance leg remains satisfied for the original lease's
remaining legs, without reserving the released entrance again. Closed resources
still block permission. Possible occupancy followed by a clear observation is
not treated as route completion while the remaining planned polyline still
crosses that resource. Physical occupancy may be empty while its reservation is
retained. Once the planned crossing and whole-body clearance are both complete,
the resource can be released.

The `-ReservationFault` variant disables only the internal occupant's ego
reporter, leaving the physical actor present. It checks bounded actuator stop
and no entry by the waiting vehicle for 22 seconds, beyond the 20 second initial
lease TTL. It does not implement fault recovery or remove the occupant.

The test-only oracle samples collider penetration and whole-body corridor bounds
each FixedUpdate; these hidden transforms never enter Python planning. This is
sampled verification, not a formal continuous-collision proof. Production
resource decisions use validated ego reports for each vehicle and static route
geometry. Dynamic obstacle safety continues to use sensor observations.

Commands:

```powershell
./AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Reservations
./AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Reservations -ReservationFault
```

M5/T10 remains partial: three active vehicles, multi-corridor deadlock recovery,
CBS comparison, hub/energy behavior, real-map resource approval, original scene,
Player/mobile and load verification remain separate gates. Execution results
must be read from the source-bound artifacts, not inferred from this test design.

## Verified execution

Python/MapData 355 and Ruff pass. Isolated Unity PlayMode normal and fault runs
both pass 1/1. Normal: V02 reservation completion precedes V01 grant; both passenger
missions complete and no claims/faults remain. Fault: one actual occupant remains
stopped beyond lease TTL; no second entry or reassignment, one pending request
and latched resource closures remain. Expected stale sensor rejections are checked
only for the injected fault vehicle; other sensor errors fail the test.
Evidence: `artifacts/validation/2026-09-26-fleet-reservations/`.

## Three active vehicles and mixed service

`-Reservations -FleetThree` selects the separate eight-node map
`synthetic-physics-reservation-three-v1`. It instantiates V01, V02 and V03 as
actual Unity Physics shells at separate staging nodes. Two passenger requests
and one 5 kg cargo request pass through the same service/dispatch/reservation
pipeline. Cargo weight is a synthetic request assumption, not measured capacity
or Physics mass. The two-vehicle fixture and its previous evidence remain separate.

The test waits for accepted sensor input from every vehicle, checks every pair
of physical colliders, counts each corridor entry, and verifies all request IDs
and assigned vehicle IDs are distinct at completion. It requires the cargo
mission to complete on V03 and all resource claims, closures and pending entries
to be cleared. A pre-entry lease may expire while sensor safety prevents motion;
such expiry never clears an entered occupant's reservation.

This is a three-vehicle service integration gate, not complete M5: energy/hub
policies, general deadlock recovery, CBS comparison, real resource geometry and
the required load/performance tests still apply.

Three-vehicle verification: normal PlayMode 1/1 and internal-fault PlayMode 1/1
pass. Normal entry order V02,V01,V03; passenger/passenger/cargo all complete and
all claims release. Faulted V03 remains physically stopped beyond lease TTL with
two waiting vehicles, three retained claims, three closures and one tracker fault.
No sampled body overlap or dual corridor occupancy in either run. Evidence:
`artifacts/validation/2026-09-26-three-vehicle-service/`. These runs do not establish
general scheduling liveness or automatic fault recovery.
