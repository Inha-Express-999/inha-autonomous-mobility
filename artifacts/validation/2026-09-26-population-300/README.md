# Synthetic 300-pedestrian population lifecycle

2026-09-26, Unity 6000.3.21f1 PlayMode: 1/1 passed.
Command: RunUnityPhysicsIntegration.ps1 -TestFilter
InhaExpress.Client.Tests.SyntheticPedestrianPopulationTests -PythonPath <python>.
The filter selects only the population test; the harness starts its server but
this result is not a network/vehicle/crowd integration result.

The test creates 300 active capsule actors in deterministic parallel lanes,
checks first/last placement and Pedestrian layers, observes Rigidbody movement,
pauses all actors, recreates the same layout, and verifies no old colliders remain.
A final Clear leaves zero actors/colliders. Source hashes preserve the tested code.

No seed/randomness is needed for this fixed grid. Count is capped at the planned
1,000-person stress size, but this run validates 300 only. No renderer, animation,
mutual avoidance, sensor density, combined 3-vehicle load, p95/FPS target, original
scene or Android/Player performance is measured. Capsule sizes/routes are synthetic.
