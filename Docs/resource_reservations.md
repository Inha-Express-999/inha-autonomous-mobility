# Resource reservation baseline — state machine

2026-09-26 · project v0.3.2.0

`ResourceReservations` implements admission/occupancy bookkeeping for named,
map-version-bound resources. A narrow corridor's two directed edges must be mapped
to the same resource by the future qualified map adapter. Intersections and exit
spaces also need explicit resource definitions; no geometry is guessed here.

## Implemented behavior

- Grant all requested resources atomically. A corridor movement should request
  the corridor and its exit space together; an unavailable exit prevents admission.
- Existing holders are never displaced by a higher priority request. Pending
  requests rank by difficulty yielding, priority plus waiting-age credit, submission
  time, vehicle ID, then request ID. Grants are deterministic.
- A grant that has never been entered expires at its TTL. Once any resource is
  entered, TTL alone cannot release actual occupancy or the still-reserved exit.
- Validated ego boundary reports update occupancy. Explicit exit releases the
  exited resource. Other resources in the same movement remain reserved. Completing
  all exits releases the lease. Cancellation is forbidden while occupied.
- Closing a resource retains its occupant. Reopening requires no claim or reported
  unplanned occupant. Late/unauthorized entry is recorded as a fault and closes the
  affected resources; an invalid grant is never evidence that the real body vanished.
- `report_unplanned_occupancy` handles a validated ego with no live token, including
  an expired/pruned lease. An empty report records exit but does not reopen a fault.
- Wait-for cycles are reportable. On timeout, pending requests emit timeout and,
  when applicable, deadlock events. Occupants are not reversed, deleted or released.
- `entry_allowed` answers only reservation permission. It checks map/vehicle/token,
  expiration, resource ownership and closures; it does not override sensor safety.
- A service/replay logger can drain structured events to bound event-buffer memory.

## Integration boundary and remaining work

This is a synchronous service-owned state machine, not a thread-safe public API.
Its state is not yet wired into `MobilityService` command authority. Subsequent
optional ego-localization occupancy integration is implemented and tested in Unity;
see `resource_occupancy.md`. Admission/actuation remains separate.
Callers must validate ego pose/session/tick and derive boundary entry/exit from
verified resource geometry and the entire vehicle footprint. A missing/stale pose
must retain occupancy, not report an exit. Unexpected physical entry must use the
fault path even if normal permission was denied. Occupancy cannot be inferred from
a timer, planned arrival, another actor's hidden Transform, or client UI state.

Stop-line holding, braking-distance anticipation, map overlay loading, actual
multi-vehicle Physics scenarios, schedule/route integration, CBS and the comparative
metrics remain pending. No actual double-occupancy or collision-rate result is
claimed by these unit tests. Full §10/M5 acceptance is still open.

Tests exercise atomic corridor/exit claims, expiration before/after entry, the gap
between corridor exit and entering a reserved bay, existing-holder precedence,
deterministic priority/aging, closure/reopening, unauthorized occupancy, deadlock
timeouts, map/vehicle mismatch and detached grant snapshots. Evidence is retained
in `artifacts/validation/2026-09-26-reservations/`.
