# Observed-surface crossing stop prototype

2026-09-26 · project v0.3.2.0

`MobilityService.crossing_policies[vehicle_id]` enables an additional independent
safety gate for a vehicle with an explicitly supplied `CrossingPolicy`. There is
no default footprint: current visual vehicle wrappers have not established the
required sensor-centered collision envelope. Unconfigured vehicles retain the
existing range/radial safety gates. This is a remaining deployment/configuration
requirement, not a claim that every current vehicle has crossing protection.

The policy requires:

- Ego envelope radius about the sensor origin, containing the entire vehicle
  including sensor mount offset. One configured bound must cover all its sensors.
- Pedestrian maximum **diameter**, since the observed point is on a surface rather
  than at the body center. A center-radius assumption would underbound this case.
- Position and velocity uncertainty bounds and their provenance.
- A positive prediction horizon no greater than two seconds.

The service passes each accepted frame and its matching motion estimates to the
existing safety evaluator. Freshness, invalid-frame and ego-pose handoff checks
still run first. Visible pedestrians without a current two-frame estimate (or
without identity/capture metadata) hold the configured vehicle with
`SENSOR_DATA_STALE`. Constant-relative-velocity circle contact uses all directions,
including lateral returns outside the existing forward sector. Predicted contact
returns `EMERGENCY_STOP / OBSTACLE_STOP`, resets the clear interval, and never
overwrites the measured Physics speed. Clear observations need the same full
one-second recovery interval as the other safety gates. Explicit zone closure
continues to take precedence.

The radius sums ego envelope, pedestrian diameter, existing safety margin,
position uncertainty and velocity uncertainty times lookahead. Receipt age extends
lookahead only up to the two-second capture horizon. Transport latency before
receipt is not calibrated; the two clocks are not assumed synchronized. No hidden
dynamic Transform is queried. Invisible objects are not extrapolated.

Verification: Python tests exercise lateral approach/separation/miss, missing or
anonymous observations, stale and invalid frames, uncertainty/age, recovery,
configuration rejection, and actual service ingress through authoritative snapshot
motion revocation while preserving measured speed. Test dimensions and error
bounds are synthetic assumptions. Calibration of the bounds is still required.
This does not provide swept rectangular-body or
turning/acceleration validation, uncertainty estimation, RRT, verified physical
braking, or full T21/MVP acceptance.

Validation run: `pytest backend/tests AgentScripts/MapData` passed 192 tests;
Ruff and `git diff --check` passed. Existing Starlette/httpx deprecation warning
remains. Unity was not run for this change.

Subsequent integration: `RunUnityPhysicsIntegration.ps1 -Crossing` passed isolated
Unity PlayMode 1/1 with actual LiDAR/Python transport, an approaching lateral
pedestrian, braking/clear hold and mobile-request completion. The stop occurred
outside the old forward sector (1.637 m forward / 4.600 m lateral); 208 sensor ACKs
were accepted and none rejected. Sampled collider clearance reached 0.752 m with
no sampled overlap. This proves the synthetic runtime connection, not continuous
swept safety or a 1 m clearance guarantee. See
`artifacts/validation/2026-09-26-crossing-physics/README.md` for inputs and limits.
