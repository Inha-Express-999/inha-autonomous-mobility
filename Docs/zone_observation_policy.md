# Observed zone state policy

2026-09-26 · working tree after v0.2.4.0

`campus_sim.zone_policy` implements the section 8 NORMAL/CAUTION/AVOID/CLOSED
state transitions separately from the time-of-day prior model. ZonePolicy holds
validated synthetic thresholds: 0.2 caution, 0.5 avoid, 1.0 immediate closure,
reopen below 0.7 for 10 seconds. A maximum 1-second observation gap is an explicit
synthetic continuity assumption. Explicit closure takes immediate precedence.

Call update_zone_state once per new density estimate using monotonic simulation
time. Unknown/low-coverage observations are None, never zero. Unknown observations,
a gap beyond the configured bound or density at/above the reopen threshold reset
recovery. Prior can raise route avoidance but does not fabricate an observed
closure. Reopening may return AVOID rather than NORMAL if density remains elevated.
The decision retains observed and prior density separately. Invalid values and
replayed timestamps are rejected without changing the previous immutable decision.

Targeted pytest: 15/15 passed; Ruff passed. Cases cover exact boundaries, continuous
hold, unknown data, renewed closure, observation gaps, peak/prior separation and
invalid/replayed inputs. The prior full service regression remains a separate run.

## Integration still required

This is a tested policy component, not a live closure controller. No service API
accepts fabricated density, and current routing is unchanged. Sensor detection
association/deduplication, validated zone geometry/usable area, coverage estimates,
freshness and uncertainty must supply the observed input. Then closure must be
applied to every planning fallback, active route authority, accessible alternatives
and PC presentation. Closed-zone transit is never allowed merely because a detour
is long. This module alone does not complete T06/M3 or real Biryong validation.

## Explicit closure service integration (2026-09-26)

`MobilityService.set_explicit_zone_closure` now accepts an existing graph zone ID
and a boolean closure flag as an internal scenario/operator boundary. Unknown zones
are rejected. It clears route-duration caches and excludes closed edges from base,
avoidance and penalty-fallback planning. A missing route remains unavailable even
when avoidance costs would otherwise favor crossing the zone.

Active routes containing a closed remaining edge receive EMERGENCY_STOP/ZONE_CLOSED.
If the future pickup-to-dropoff trip becomes unreachable, pickup is held as well.
ETA is unknown while held. Synthetic stepping pauses; localized vehicles retain
their measured speed, with the normal Unity follower responsible for deceleration.
New sensor frames cannot override the closure. Removing an explicit closure returns
localized vehicles to the existing sensor validation/clear-hold gate.

Five service regression cases cover blocked routing/no alternative, active hold and
reopening, unknown-zone rejection, localized speed preservation/sensor precedence,
and unreachable future dropoff without ETA failure. Full Python/MapData suite:
162 passed; Ruff passed (one existing dependency deprecation warning).

This supersedes only the earlier statement that routing is unchanged. The observed
zone state machine is still not wired to sensor-derived density or closure inputs.
There is no new network endpoint or authentication claim. Current active-route
behavior is conservative hold, not automatic safe egress or forced U-turn. Accessible
boundary Stop substitution, original-map geometry, PC zone display, and a dedicated
Unity closure/braking run remain incomplete. No real Biryong zone is invented.

## 2026-09-26 explicit closure in Unity Physics

The dedicated `-ZoneClosure` PlayMode scenario passed using a separately versioned
synthetic graph and test-only loopback closure input. After actual movement, server
ZONE_CLOSED caused the follower to brake and hold; reopening entered sensor recovery
hold, then the scenario completed passenger/cargo missions and telemetry recreation.
Ruff passed. Evidence: `artifacts/validation/2026-09-26-zone-closure-physics/`.

This resolves only the isolated Physics closure round trip. Observed-density input,
real zone geometry, alternate accessible Stops, safe egress, original scene and
performance gates remain incomplete.
