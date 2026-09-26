# Resource wait presentation

2026-09-26 · uncommitted work after v0.3.2.0

PC and mobile snapshots evaluate the same control intent. Only PC snapshots
issue sequenced actuator commands; mobile projection does not advance command
sequence numbers. Existing localization and sensor safety reasons take priority
over resource reasons. Measured vehicle speed is preserved.

`RESOURCE_WAIT` means a route resource or its exit group lacks entry permission.
`RESOURCE_STATE_UNAVAILABLE` means resource readiness, context, fault or timeout
prevents admission. A positive approach speed or at-rest alignment retains
DRIVING so the existing actuator can reach its bounded hold pose. Zero speed/yaw
intent projects YIELDING while measured speed is above 0.01 m/s and
WAITING_RESOURCE after stopping. Presentation never grants a new reservation.

Active request `etaS` is null during resource or safety/localization holds,
because their duration is unknown. This is a transport projection; it does not
overwrite the request's stored synthetic estimate. A cleared hold restores that
estimate, not a newly validated real-world ETA.

PC reason labels explain corridor order, unavailable corridor state and stale
localization. Passenger guidance uses only its request's assigned vehicle,
avoiding another visible vehicle's unrelated safety message. UI wording does
not expose actuator implementation details.

Compatibility: schema_version remains 3 in this alpha; the two additional reason
enum names require the updated server and C# client together. Older clients using
strict enum deserialization cannot consume those names. Capability negotiation
for older clients is not implemented. No map-version change is involved.

Validation:

- Python/MapData **359 passed**, including both-role equality, mobile sequence
  non-issuance, actual-speed preservation, approach/alignment authority, unknown
  ETA, release recovery, role visibility and safety precedence.
- Isolated Unity client EditMode **23/23 passed**, compiling the presentation,
  PC and mobile assemblies and testing localized guidance with an unrelated
  vehicle placed first in the snapshot.
- Real WebSocket PC plus passenger and three actual Unity Physics vehicles:
  PlayMode **1/1 passed**, both roles saw resource wait, passenger ETA was unknown
  during wait, no other owner's state/control commands leaked, and all three
  passenger/passenger/cargo missions completed without sampled overlap.
- Ruff and whitespace checks passed. Existing dependency/Unity allocation
  diagnostics remain. No original UI scene screenshot, Android device, Player
  build, 50-client load or full M5a completion is claimed.

Evidence: `artifacts/validation/2026-09-26-resource-presentation/`.
