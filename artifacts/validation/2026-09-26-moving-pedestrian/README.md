# Unity-owned moving pedestrian integration

2026-09-26, Unity 6000.3.21f1 PlayMode: 1/1 passed.
Reproduce: RunUnityPhysicsIntegration.ps1 -Pedestrian -PythonPath <python>.
The real sensor/transport/server/follower flow stops before a paused synthetic
pedestrian. The pedestrian then walks laterally to z=3 using its own Rigidbody,
without being removed or disabled. Server clear hold and vehicle recovery follow;
passenger/cargo completion and actor telemetry recreation also pass.
XML includes accepted frame counts; source hashes and trace preserve scope.

The test uses a default Unity capsule, synthetic path and 1 m/s speed. It does not
validate human dimensions, crowd collision avoidance, full campus walk graph,
boarding lifecycle, real scene assets, 300/1,000-agent load, Player or Android.
CSV status is the first passenger request; cargo completion is checked by ID.
Allocation-lifetime warnings remain; this is correctness evidence, not performance.
