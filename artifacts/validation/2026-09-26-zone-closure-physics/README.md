# Explicit zone closure Physics integration

Unity 6000.3.21f1 PlayMode, 2026-09-26: 1/1 passed.
Reproduce with RunUnityPhysicsIntegration.ps1 -ZoneClosure -TestFilter
InhaExpress.Client.Tests.VehicleServiceIntegrationTests -PythonPath <python>.

The vehicle first moves under DRIVING authority. A loopback test-only closure
command produces ZONE_CLOSED; the real follower brakes and holds position.
After reopening, sensor recovery hold is observed before the normal wall obstacle,
passenger/cargo completion, mobile continuity and actor recreation checks proceed.
XML includes closure confirmation and ACK counts; CSV and hashes preserve scope.

The separately versioned graph is synthetic and has no actual Biryong polygon.
This does not validate observed-density closure, accessible Stop substitution,
minimum-risk egress, physical vehicle braking calibration, original scene/Player,
Android or load targets. Test endpoint exists only in the harness closure mode.
Allocation-lifetime warnings remain. CSV status is the first passenger request;
cargo is independently checked by ID.
