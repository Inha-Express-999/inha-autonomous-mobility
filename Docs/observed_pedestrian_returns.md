# Visible pedestrian return identity and deduplication

2026-09-26 · working tree after v0.2.4.0

`SensorDetection.entity_id` / C# EntityId is optional and bounded. The Raycast rig
assigns opaque process-local IDs only to actually hit Physics objects, shared across
rigs via a weak-key table. Attached Rigidbody owns compound-collider identity;
otherwise the hit collider owns it. Radar preserves identity on its merged return.

`perception.summarize_pedestrian_returns` consumes SensorSamples, ignores invalid,
missing, future or expired receipts, and deduplicates known pedestrian IDs. It
returns anonymous ray count separately from known IDs and usable frame count.
Different simulation session/map contexts cannot be merged. No usable frames is
unknown observation, not proof of zero people. Callers still need validated ingress
session selection and a defined observation window.

This is not zone density: no zone membership, visibility coverage or usable area
is inferred here. Multiple objects without a shared body cannot be asserted to be
one person. Occluded actors have neither detections nor IDs. The ID is an abstraction
for simulation identity, not learned recognition or a real person identifier.
Association/vector tracking, coverage geometry, EMA, zone policy and runtime routing
integration remain separate required steps.

## Consecutive motion estimates

The live ingress now maintains a bounded tracker per admitted vehicle/sensor stream.
Capture time and the sensor's own world pose transform local hits into the common
map frame. Consecutive nearest returns sharing an ID yield observed surface world
velocity and relative velocity after sensor translation is removed. Rotating the
ego alone does not create world motion. Interval bounds are synthetic 0.05–0.3 s.
Invalid/missing-metadata frames, context changes, gaps and absent IDs do not bridge
motion through unobserved periods. Session replacement clears prior trackers.
`current_sensor_motion` exposes only fresh diagnostics, never drive permission.

Tests cover ego translation/rotation, side motion feeding the circle TTC primitive,
lost context/visibility, stale diagnostics and partial metadata rejection. Surface
point changes can still bias velocity; this is not object-center motion, uncertainty
estimation or complete collision avoidance. Runtime safety remains range/radial
approach based until verified collision envelopes and tracking confidence are added.

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
