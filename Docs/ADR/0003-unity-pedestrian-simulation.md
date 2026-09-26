# ADR 0003: Unity-owned pedestrian motion

2026-09-26 · working tree after v0.2.4.0

`InhaExpress.Simulation` is a separate Unity assembly with no client, transport,
Python, or presentation dependency. `SyntheticPedestrianWalker` owns kinematic
Rigidbody movement over an explicitly supplied synthetic waypoint path. It copies
input points, validates finite coordinates and positive speed, supports pause,
uses a fixed-step distance budget and stops at the last point without overshooting.
It requires a CapsuleCollider and assigns the Pedestrian layer to its actor root.

The integration runner's `-Pedestrian` variant replaces the removable wall with
this actor. It starts paused in the vehicle path, then walks laterally clear while
remaining enabled. Only existing Raycast observations reach Python. Test-side
position/completion checks are ground-truth assertions, not planner inputs.

All path coordinates, the 1 m/s test speed and default Unity capsule dimensions
are synthetic assumptions. This is not campus pedestrian routing, human motion
validation, collision-free crowd behavior, boarding/alighting lifecycle, animation,
300/1,000-person spawning, or density inference. The kinematic walker does not yet
avoid bodies along its path. No real campus path is implied or approved.

Vehicle telemetry remains in the existing client integration boundary for now;
new pedestrian dynamics must not acquire a service socket or leak hidden actor
pose/velocity into Python. Observation/perception is a separate responsibility.

## 2026-09-26 reproducible population fixture

`SyntheticPedestrianPopulation` adds validated deterministic lane placement,
300-person generation, group pause and immediate retirement followed by cleanup.
It remains in the Unity-only simulation assembly. Inputs are scenario assumptions;
no Python perception receives the population's ground-truth positions.

The filtered PlayMode lifecycle test passed: 300 capsules, actual Rigidbody motion,
pause, identical respawn positions, and zero old colliders after cleanup. Evidence:
`artifacts/validation/2026-09-26-population-300/`. This is population lifecycle
verification only, not full 300-person crowd behavior or performance acceptance.
Mutual avoidance, observed density/time-of-day demand, service lifecycle, rendering
and combined load still remain.
