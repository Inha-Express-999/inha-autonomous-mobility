# Python steering intent contract (synthetic alpha)

2026-09-26 · project v0.3.1.0 · synthetic actuator PlayMode integration verified

`controller.py` computes forward speed/yaw-rate targets from ego localization,
the active service route and its segment speed, and explicit `ControllerPolicy`
limits/provenance. Heading uses north=0 and clockwise positive. Large heading
errors first request braking, then allow rotation when measured speed is at most
0.01 m/s (synthetic near-stop threshold). Speed obeys the local cap, route cap and
braking-distance target. This is route feedback, not timed RRT execution.

Only explicitly configured vehicles in synthetic maps emit `controlCommands` in
PC snapshots. Mobile snapshots contain an empty list. Display `VehicleDto` remains
measured state, separate from `VehicleControlDto` actuator intent. Existing fixtures
without the optional list deserialize to an empty collection; schema version stays
3. C# rejects mobile commands, duplicate vehicles, map mismatches and absent vehicle
references. New capabilities require paired, tested server/client deployment.

Each command binds vehicle/map/telemetry session, monotonic command sequence,
ego pose tick and route ID. It carries targetSpeedMps, yawRateRadps, reason and
validForS (positive, at most 0.3 seconds). Moving-command validity is capped by the
remaining pose/sensor freshness budget at generation. Stop commands remain possible
for stale localization, invalid/missing sensors, pose handoff, zone/service holds
or absent requests. Generation never overwrites measured speed. Safety is refreshed
before control generation; configuration alone cannot bypass the existing gates.

Initial receiver requirements (implemented below for the synthetic opt-in): bind to the local telemetry session/map/route,
reject duplicate/old commands and old/future ego bases, account for transport delay,
apply local speed/yaw/acceleration limits, and physically brake on missing/expired
commands. Ensure a single active actuator per Rigidbody. No Unity actuator consumes
this field in the default scene configuration; the existing follower remains active there. There is no new authority
to execute local RRT candidates and no real-vehicle control claim.

Validation: Python full suite 240 passed before the final freshness-budget change;
then controller tests 8/8 and Ruff passed. Domain snapshot/control DTO compilation
and existing follower PlayMode 9/9 passed in an isolated Unity project. These do not
prove new-field network deserialization or actual command application; both remain
integration gates. Evidence: `artifacts/validation/2026-09-26-control-contract/`.

## Subsequent actuator integration

`VehicleCommandActuator` now consumes commands when the spawner's explicit
`usePythonControl` option is enabled. The route follower is disabled; route geometry
can still be drawn. The actuator rejects wrong binding/route, duplicate sequence,
future/older-than-two-ticks ego basis and already expired delivery. The host retains
eight locally issued pose timestamps per vehicle. Expiry is based on the referenced
pose's local issuance time plus validForS, conservatively including uplink/downlink
delay rather than granting a new lease on receipt. Missing command or withheld
snapshot authority revokes motion. Residual speed physically decelerates.

Local synthetic caps are 1 m/s, 0.5 m/s² acceleration, 2 m/s² braking and π/2 rad/s
yaw. The kinematic body integrates heading and displacement; this is not measured
vehicle dynamics. Mismatched/absent routes revoke the actuator. Sequence state is
now also retained by the transport host's shared `ControlSequenceGuard`, keyed by
server run, map, telemetry session and vehicle. A recreated actor cannot reuse an
already accepted or lower sequence. New server runs have independent sequence
spaces. Expiry and retained host pose history still apply after recreation.
Same-service socket interruption is verified below; actual server restart and
lost-memory recovery remain open.

Isolated Unity tests passed 11/11, and actual Python-controlled crossing integration
passed 1/1 with the follower disabled and passenger completion. Evidence:
`artifacts/validation/2026-09-26-python-actuator/`. Original scenes remain opt-out;
RRT candidates remain non-executable. Player/Android and load gates remain open.

Subsequent lifecycle verification: component tests 12/12 and Python-control
crossing integration 1/1 passed. The integration destroys the completed vehicle,
lets the production spawner recreate it, and verifies that accepted command
sequence continues beyond the old actor while the follower remains disabled.
Direct component tests reject replay across recreation and permit sequence zero
in a different server run. Evidence: `artifacts/validation/2026-09-26-command-lifecycle/`.

Actual socket interruption: `-Crossing -PythonControl -Disconnect` now closes the
moving PC connection and blocks reconnect for 1.5 s. The actuator stops/holds,
then fresh observation recovery and passenger completion succeed with the same
actor/session/run. A sequence-reset defect was reproduced and fixed: snapshot
sequence is allocated by the service across connections and runId is unique per
service instance. Before/after evidence: `artifacts/validation/2026-09-26-socket-recovery/`.
