# Synthetic follower terminal and turn braking

2026-09-26 · Unity 6000.3.21f1 · isolated headless PlayMode.

`AgentScripts/RunUnityFollowerTests.ps1`: **9/9 passed**.

The terminal-waypoint branch previously reduced its speed field without moving
the Rigidbody, hiding residual stopping travel. It now uses the same bounded
kinematic braking integration as revoked authority. A route direction change
outside the alignment tolerance now brakes along the previous travel direction
before rotating in place, instead of instantly redirecting nonzero velocity.

Two new actual-Physics regressions verify residual terminal travel against
v²/(2b) within a timestep allowance, and forward braking/unchanged heading before
turning and resuming on a reverse route. Existing seven follower cases also pass.
XML and source hashes identify the exact executed revision.

`RunUnityPhysicsIntegration.ps1 -PythonPath <repo>/tmp/backend-venv/Scripts/python.exe
-Crossing`: **1/1 passed** with the corrected follower. LiDAR/Python crossing stop,
braking, clear recovery and passenger completion remain connected. 255 sensor
frames accepted, none rejected; stop separation 1.700 m forward / 4.560 m lateral.
Minimum sampled collider clearance 0.840 m with no sampled overlap. These are one
execution's results, not a controlled performance/safety improvement comparison
against the prior run. The new CSV header explicitly labels pedestrian Z.

These fixes preserve honest synthetic movement; they are not a full physical
vehicle model or the target Python unicycle controller. Continuous swept-body
collision, turning clearance/corridor verification and measured dynamics remain
open. Existing Unity allocation diagnostics remain. No original scene, Player,
Android or performance acceptance is claimed.
