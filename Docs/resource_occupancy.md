# Ego-derived resource occupancy

2026-09-26 · project v0.3.2.0

`ResourceOccupancyTracker` is an optional `MobilityService` component. Accepted
ego-localization ingress updates it, and service clock advances check freshness.
It receives no hidden vehicle or pedestrian Ground Truth. Configuration requires
one explicit axis-aligned rectangle per named map resource, per-vehicle centered
footprint, position error, speed/sample-gap bounds, exit-confirmation duration and
provenance. The current integration uses synthetic geometry only.

## Boundary behavior

- Four separating axes compare the entire oriented vehicle rectangle to each
  resource. Position error expands support; boundary contact counts as occupied.
- Possible between-sample traversal is conservatively retained. Under the stated
  speed bound, every possible center path lies inside the ellipse with sample
  endpoints as foci and semimajor axis vmax×dt/2. Its enclosing disc, expanded by
  body radius and position error, bounds curved motion and yaw. This may retain
  a resource even without an actual hit; it does not claim a reconstructed path.
- An exit requires repeated reports showing the whole body and the intervening
  bounded motion clear for the configured duration. Center-point exit is insufficient.
- New/expired/unplanned entry uses the reservation fault path. A missing grant
  does not erase a physically observed occupant.
- Replayed ticks neither clear occupancy nor refresh its timestamp. Stale reports,
  session/map changes, excessive speed and discontinuous jumps latch a fault and
  conservatively close all configured resources. Subsequent poses do not silently
  clear it. External reconciliation/recovery is still required and is not yet wired.
- Creating a new empty tracker over existing occupancy is rejected.

Speed bounds must be valid in the service receipt-clock units, including any
simulation time scaling. Timing and error qualification remain pending. Without
an initial pose there is no occupancy-readiness claim; a future admission adapter
must wait for current observations from every participating vehicle.

## Evidence and remaining integration

Python/MapData 342 tests and Ruff pass. Isolated Unity PlayMode 4/4 passes using:

```
AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl
```

The existing crossing service scenario declares a synthetic rectangle x=[1,2],
y=[-0.6,0.6] and the test's centered 0.4 m square shell. Production Unity ego
reports lead to resource entry, delayed whole-body exit and lease completion.
The server query confirms one completed lease, no claims/closures/tracker faults.
The synthetic pedestrian stop/recovery and passenger completion also pass. This
scenario omits forced socket interruption; stale/session-loss holds are checked
by Python tests. Diagnostics exist only in the isolated test server.

This is occupancy integration, not admission control. The grant is prepared by
the test harness and does not drive actuator permission. Stop-line approach and
braking, resource-to-route geometry loading, multi-vehicle arbitration in Physics,
fault reconciliation, CBS and complete M5 acceptance remain open. No actual campus
coordinates, vehicle dimensions or safety performance are inferred from this test.

Artifacts: `artifacts/validation/2026-09-26-resource-occupancy/`.

## Subsequent admission checkpoint

The evidence above is the historical occupancy-only run. v0.3.2.0 adds optional
admission control; its latest integration still fails to resume after exit
release. See `resource_admission.md` and the current MVP ledger.
