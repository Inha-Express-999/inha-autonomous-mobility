# Python control → WebSocket → Unity kinematic actuator

Unity 6000.3.21f1 · 2026-09-26 · isolated PlayMode.

- `RunUnityFollowerTests.ps1`: 11/11 passed. New tests verify actuator-only body
  ownership, physical braking travel after command expiry, duplicate command
  rejection, wrong session/route, old/future pose basis and delayed receipt.
- `RunUnityPhysicsIntegration.ps1 -PythonPath <repo>/tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl`:
  1/1 passed. New command fields deserialize through the actual WebSocket source.
  Python steering drives the shell, the legacy follower is disabled, lateral
  pedestrian detection triggers stop/braking, clear hold permits recovery, and
  the mobile passenger request completes.
- 228 sensor frames accepted, zero rejected. Stop separation 1.742 m forward /
  4.556 m lateral. Minimum sampled collider clearance 0.945 m, no sampled overlap.

Source manifests identify each run. This is one synthetic shell and pedestrian,
not continuous swept collision proof, a one-metre clearance guarantee, benchmark,
real-vehicle dynamics, original scene, Android or Player validation. Existing
Unity allocation diagnostics remain. This closes command application for the
tested route controller; timed RRT candidates are still non-executable.
