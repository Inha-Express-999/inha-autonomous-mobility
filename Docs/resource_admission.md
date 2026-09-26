# Synthetic resource admission

2026-09-26 쨌 project v0.3.2.0

`ResourceAdmission` connects route geometry, explicit resource groups and the
ego occupancy tracker to Python control commands. Fleet requests are queued
before arbitration. A corridor can require its exit space in the same atomic
reservation. Missing readiness, latched faults and wait timeouts prevent entry.
Route replacement cancels pending requests and unoccupied reservations.

Without permission, target speed is bounded by reaction distance plus braking
distance to the resource rectangle expanded by body radius, position error and
stop margin. The hold also includes `max_speed_mps * max_sample_gap_s / 2`
from the occupancy policy: a waiting vehicle must remain outside the conservative
between-sample motion envelope. This fixes a body-only stop line that previously
latched an unplanned-entry closure despite obeying the command. At-rest alignment is allowed outside this expanded hold boundary;
rotation is stopped at the boundary. Existing sensor safety takes precedence.
Pre-entry leases must remain valid through the reaction/braking window.

This is optional synthetic configuration, enabled in the isolated integration
harness. The harness has one actual Unity vehicle and an explicitly occupied
exit fixture released by a test endpoint; it is not a two-vehicle Physics test.
Resource wait reasons now also reach PC/mobile snapshots and localized guidance;
see `resource_wait_presentation.md` for verification and alpha compatibility limits.
Campus resource geometry, fault reconciliation, CBS, multi-vehicle Physics and
validated real vehicle braking remain unfinished. The RRT candidate pipeline
still has no live execution authority.

Thirteen focused admission tests cover atomic exit-space admission, speed bounds,
expiry, route replacement, timeout, fault holds, at-rest alignment and consistency
with occupancy under delayed samples, completed group legs and future resource
retention after possible temporary occupancy. Python/MapData 355 tests and Ruff
pass. The single-vehicle isolated Unity PlayMode run passed 4/4, including hold, explicit exit
release, resumed transport and complete reservation release. Latest evidence:
`artifacts/validation/2026-09-26-resource-admission-resume/`. The earlier failed
checkpoint in `2026-09-26-resource-admission/` is retained as historical evidence.
This correction is uncommitted work after v0.3.2.0.

The subsequent opposing two-vehicle Physics scenario and source-bound evidence
are described in `fleet_resource_physics.md`; three-vehicle M5 and CBS acceptance
remain incomplete.
