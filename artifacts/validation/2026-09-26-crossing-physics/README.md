# LiDAR lateral crossing Physics integration

2026-09-26 · Unity 6000.3.21f1 · isolated headless PlayMode 1/1 passed.

Command: `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath <repo>/tmp/backend-venv/Scripts/python.exe -Crossing`.

The production PC source/spawner/reporter/Raycast rig sends only ego and observed
sensor frames. The Python service's explicitly configured crossing gate revokes
motion authority. A visible Unity-owned capsule crosses from z=5 to z=-4 at
2 m/s while a 0.4 m square kinematic shell serves a mobile passenger request.

Observed results:

- `OBSTACLE_STOP` before the pedestrian enters the old forward sector:
  forward separation 1.637 m, lateral separation 4.600 m at stop detection.
- Bounded follower braking, authority withheld, clear recovery hold, and passenger
  request completion after the crossing. Pedestrian remains active in the world.
- 208 accepted sensor frames, zero rejected frames.
- No collider overlap in the test's FixedUpdate samples; minimum sampled clearance
  0.752 m. This is **not** a proof of continuous swept clearance or a 1 m clearance
  guarantee. Test-only Ground Truth checks are not inputs to Python control.

The recorded trace uses the pre-existing header: `x` means ego map X, while `y`
contains pedestrian Unity Z in this crossing test. `requestStatus` is the scenario
label CROSSING; completion is asserted in the XML test, not that CSV column.

Policy radii derive from the synthetic shell/capsule; 0.1 m position and 0.1 m/s
velocity uncertainty are explicit uncalibrated assumptions. Scope excludes real
vehicle envelopes, original campus scenes, Player/Android, crowd load, hidden
pedestrians, swept turning shapes and certified physical braking. Existing Unity
temporary allocation warnings remain. Source hashes bind this result to the
actual execution inputs; later runner header/documentation edits are not a rerun.
