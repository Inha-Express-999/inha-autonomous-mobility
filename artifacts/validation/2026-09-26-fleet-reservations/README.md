# Opposing fleet Physics — 2026-09-26

Uncommitted work after v0.3.2.0. Synthetic two-vehicle test only.

- Python/MapData: **355 passed**, one existing Starlette/httpx deprecation warning.
- Ruff and whitespace checks passed.
- Unity 6000.3.21f1 isolated PlayMode `-Reservations`: **1/1 passed**.
- Same runner `-Reservations -ReservationFault`: **1/1 passed**.
- Both use `-PythonPath tmp/backend-venv/Scripts/python.exe`.

Normal run: actual Physics actors entered in order V02 then V01. Both passenger
services completed, no sampled collider overlap or simultaneous whole-body
corridor occupancy. Sensor ACKs=1000, rejected=0. Reservation events show V02
completed at server time 51.70 s and V01 granted at 51.75 s. Both leases completed;
claims/closures/faults/pending requests all zero.

Fault run: V02 entered, then only its ego reporter was disabled inside the corridor.
The actor remained present, braked within the explicit synthetic command-expiry
plus braking-distance bound, and stayed still for another 22 seconds (beyond the
20 second pre-entry lease TTL). V01 never entered. One pending request remained,
two resource claims were retained, three resources closed and one tracker fault
latched. No lease completion/reassignment. Sensor ACKs=486, expected stale-localization
rejections from V02=199, other rejections=0. Sampled body overlap and dual corridor
occupancy remained false. Stale snapshot speed is not treated as actual speed;
the stop oracle reads the test actor's actual actuator and pose.

The first normal attempt exposed early exit release after possible envelope contact
while approaching a turn. Route-aware claim retention corrected this; regressions
cover completed group legs and future resource retention. The first fault attempt
already held the occupant safely but failed a test assertion that disallowed all
sensor rejections. The corrected test permits only stale_localization for the
explicitly faulted vehicle after injection, and adds physical braking/hold bounds.

Each subdirectory retains its own exact source hashes, XML, telemetry and events.
The normal run preceded the later fault-only assertion enhancements; production
Python and actuator sources are the same. No original scene, Player/Android,
three-vehicle operation, deadlock recovery, CBS, campus resource approval, continuous
collision proof or performance/load claim. Unity allocation diagnostics remain.
