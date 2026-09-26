# Synthetic resource admission

2026-09-26 · project v0.3.2.0

`ResourceAdmission` connects route geometry, explicit resource groups and the
ego occupancy tracker to Python control commands. Fleet requests are queued
before arbitration. A corridor can require its exit space in the same atomic
reservation. Missing readiness, latched faults and wait timeouts prevent entry.
Route replacement cancels pending requests and unoccupied reservations.

Without permission, target speed is bounded by reaction distance plus braking
distance to the resource rectangle expanded by body radius, position error and
stop margin. At-rest alignment is allowed outside this expanded hold boundary;
rotation is stopped at the boundary. Existing sensor safety takes precedence.
Pre-entry leases must remain valid through the reaction/braking window.

This is optional synthetic configuration, enabled in the isolated integration
harness. The harness has one actual Unity vehicle and an explicitly occupied
exit fixture released by a test endpoint; it is not a two-vehicle Physics test.
Resource wait reasons are control-command diagnostics, not complete operator UI.
Campus resource geometry, fault reconciliation, CBS, multi-vehicle Physics and
validated real vehicle braking remain unfinished. The RRT candidate pipeline
still has no live execution authority.

Seven focused Python tests cover atomic exit-space admission, speed bounds,
expiry, route replacement, timeout, fault holds and at-rest alignment. Latest
Unity results and source hashes are recorded in
`artifacts/validation/2026-09-26-resource-admission/`.
