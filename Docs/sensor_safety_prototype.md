# Sensor-based safety gate prototype

Project version **0.3.1.0** · 2026-09-25 working tree

## Implemented behavior

The Python server applies this gate only after an ego vehicle has entered `unity_localization` mode. Synthetic-server-only route integration keeps its previous behavior. Each localization vehicle must have a current-session sensor frame that is valid and no older than 0.3 simulation seconds; missing, stale, or invalid observations withhold movement authority. Unity's `VehicleActorSpawner` passes that authority to the preview `VehicleRouteFollower` through the snapshot motion state.

For a valid frame, the server checks a conservative 90-degree forward sector (`x > 0`, `abs(y) <= x`). A detection inside the stopping-distance threshold sets `EMERGENCY_STOP/OBSTACLE_STOP`:

```text
d_stop = speed_mps * 0.2 + speed_mps² / (2 * 2.0) + 1.0 m
```

The currently implemented gate values are loaded from `configs/safety.json` at service creation. The loader rejects missing, non-finite, or negative values, and requires positive sensor freshness and emergency deceleration. The file contains only parameters consumed by this prototype; other safety parameters in `AGENTS.md` are not yet runtime controls.

The reaction time, emergency deceleration, and margin are initial assumptions copied from the AGENTS safety settings; they are not measured vehicle values. The distance comparison uses the detection's forward projection. The server requires one continuous simulation second of fresh, valid, clear observations before restoring `DRIVING`. A stop suppresses localized pickup/dropoff state progression as well as route motion authority. PC snapshots expose the state and reason; Mobile guidance has Korean explanations for stale, invalid, obstacle-stop, and resume-hold reasons.

## Limits

- This is a conservative range gate, not TTC, RRT, or a swept-footprint collision check. It does not associate detections with the planned route or vehicle footprint; off-path objects in the sector can cause false stops.
- LiDAR detections are treated as stationary obstacles. RADAR relative speed is not consumed because its sign/frame semantics and tracking are not yet defined.
- It does not predict crossing trajectories, distinguish occlusion from empty space, or handle sensor calibration/false negatives. The Unity LiDAR is a 2D Raycast abstraction and pedestrian actors are not yet available.
- The Unity waypoint preview now performs bounded kinematic deceleration along its last travel direction when motion authority is revoked or its route expires. A PlayMode regression case checks the stopping-distance bound and stationary hold. This is still scripted `MovePosition`, not dynamic Rigidbody braking, and does not validate vehicle emergency-deceleration performance.
- Thresholds and sensor cadence require measurement and configuration before any safety or real-map claim. Real campus movement remains disabled.

## Verification

Python tests cover no-frame, invalid-frame, stale-frame, close forward obstacle, ego-pose/sensor-tick synchronization, 1-second clear recovery, and an end-to-end synthetic request whose vehicle returns to `DRIVING` only after the sensor-clear hold. A PlayMode regression case covers bounded kinematic deceleration and resume, but Unity Test Runner execution is pending. C# source compilation and loopback WebSocket smoke verify the updated reason enum and transport; they do not execute Unity Physics or the follower in PlayMode.

The current deceleration change has not yet been compiled by Unity. A PlayMode batch run was attempted in an isolated project copy, but Unity did not reach the test runner: the Licensing Client channel `LicenseClient-Yeop` was refused and the log reported `com.unity.editor.headless` not found. No PlayMode result file was produced. The original Editor process and project were left running/unchanged.

## 2026-09-26 working-tree update

The decision and SafetyPolicy now live in `campus_sim.safety`; service orchestration
selects current-session observations and applies the immutable decision. All streams
are checked for freshness/validity before pose handoff to prevent insertion order
from retaining a satisfied clear hold across another stream's invalid data.

Current Python/MapData verification passed 111 tests and Ruff. Current C# transport
smoke passed. An isolated Unity 6000.3.21f1 PlayMode run passed 7/7 follower Physics
tests after correcting Rigidbody setup/heading in the tests and enforcing the local
speed cap in the follower. This supersedes the earlier follower execution limitation
only; sensor Physics and end-to-end server/Unity service remain unverified.
See `artifacts/validation/2026-09-26-follower-playmode/` for results and source hashes.

## Subsequent 2026-09-26 Physics service execution

A real Raycast wall-observation/stop/recovery case now passed in isolated PlayMode
and an actual headless Windows test Player connected to the production Python
server. Mobile passenger and PC cargo missions completed through Rigidbody movement
and ego localization; 322 pose and 322 sensor ACKs were accepted in Player with no
sensor rejection. The small synthetic shell and 3-stop map are test assumptions.
This does not validate general occlusion/classification, TTC/RRT, dynamic braking,
campus geometry or the original production scenes. See the service-physics-player
artifacts and `AgentScripts/UnityPhysicsIntegration/README.md` for exact scope.

## 2026-09-26 crossing TTC calculation primitive

`campus_sim.collision.circle_time_to_collision` implements the section 9 equation
`|r + u*t|^2 = R^2`, returning the earliest non-negative contact time, zero for
current contact/overlap and infinity for no future contact. Position and relative
velocity are 2D vectors in a common non-rotating frame; velocity is obstacle minus
ego. R is a caller-supplied combined envelope radius including margin. The solver
scales coordinates and rationalizes the entry root to reduce overflow/cancellation.
Invalid non-finite inputs and negative radii are rejected, not interpreted as clear.

`predict_circle_contact` requires an explicit horizon in (0, 2] seconds, retains
later analytic contact only as diagnostic information and flags whether contact is
inside that horizon. No collision inside the horizon is not a safety certification.
No default vehicle/pedestrian dimensions are invented.

22 tests passed for overlap, approach, recession, stationary cases, side crossing,
tangency/near miss, point envelopes, horizon boundaries, invalid inputs, rotation,
common scaling and checking the returned root on the envelope.

This is a tested calculation primitive, **not an active TTC safety controller**.
The current wire detection has scalar relative speed but no tracked relative
velocity vector. Detection association, ego-motion/frame compensation, uncertainty,
observation freshness, verified footprints and independent integration into the
safety gate are still required. The existing distance gate remains unchanged.
Circles do not validate rectangular swept bodies or physical braking performance.

## 2026-09-26 Raycast Radar range-rate mode

`VehicleRaycastSensorRig` now has an explicit sensor type and configurable FOV.
Existing prefabs retain the LIDAR_2D/360-degree default. Radar mode merges ray hits
into one nearest surface return per observed Collider, then estimates
`relativeSpeedMps = (current range - previous range) / elapsed seconds`.
Negative is approaching; positive is receding. Identity is obtained only from
actual Physics hits, never an unobserved actor Transform/velocity. The integration
runner's `-Radar` switch selects this mode in the isolated test shell.

The first observation has no range-rate estimate. A missed scan, disabled rig,
incomplete/overflow scan, or sample interval outside the synthetic 0.05–0.3 second
window prevents bridging that interval. Current visible ranges become the next
baseline after a complete scan. Histories are bounded by the current scan's hits.
The estimate includes ego motion and changes in the visible surface; it is neither
Doppler nor an object-center velocity vector. Multiple colliders on one object are
not yet fused. No full crossing TTC is inferred from this scalar.

Original-prefab EditMode checks plus a real Physics moving-target/visibility test
passed 15/15. The test checks one return per collider, approaching rate, loss and
reacquisition, and stale interval rejection. Runtime safety still uses the existing
range stop gate; full radar noise/FOV/load characterization and sensor tracking for
vector TTC remain incomplete.

## 2026-09-26 Radar approach gate integration

The pure safety evaluator now applies a second stop condition to valid, fresh,
pose-matched RADAR detections inside the existing forward sector. For negative
range rate, time to the configured margin is `(range - margin) / -range_rate`,
clamped at zero. It stops when this time is within the configured approach horizon
plus server receipt age. `configs/safety.json` sets the synthetic horizon to 2 s
and rejects settings outside (0, 2]. This threshold is an assumption, not a measured
vehicle braking parameter. Receipt age does not measure transport/acquisition delay.

The result uses existing EMERGENCY_STOP/OBSTACLE_STOP authority. Invalid/stale
observations and pose mismatch are checked first; the original stopping-distance
gate still stops close obstacles even if a radar return is receding. Hazards reset
the existing one-second clear hold. Missing range rate is not guessed and falls
back to the distance check. No direct actor state enters Python.

This is an additional conservative radial-approach stop, **not full crossing TTC**.
It can stop for an approaching surface that later misses the vehicle, and it cannot
recover lateral velocity from a scalar range rate. The circle TTC module remains
separate until observation vector tracking and collision footprints are available.
The previous Radar section's distance-only runtime statement describes the earlier
verification; this addition now consumes relativeSpeedMps in runtime safety.

## 2026-09-26 grouped actor sensor visibility

Fixed Raycast self-filtering: the actor root is the rig's transform, not the scene's
topmost grouping transform. Only ego descendants are filtered; sibling actors are
observable. Actual EditMode Physics tests cover both pedestrian and vehicle layer
classification under a shared parent, ignoring an ego child collider, and nearest
wall occlusion without leaking hidden entity class. 17/17 tests passed; evidence is
in `artifacts/validation/2026-09-26-sensor-occlusion/`.

This validates tagged collider visibility only. Pedestrian motion/population,
vector tracking, full scene/Player transport and crowd load remain incomplete.
