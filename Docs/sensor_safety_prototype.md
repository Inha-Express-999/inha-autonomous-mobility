# Sensor-based safety gate prototype

Project version **0.2.3.0** · 2026-09-25 working tree

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
