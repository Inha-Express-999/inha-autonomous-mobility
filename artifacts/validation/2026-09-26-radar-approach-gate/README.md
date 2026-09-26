# Radar approach stop integration

Unity 6000.3.21f1, 2026-09-26. RunUnityPhysicsIntegration.ps1 -Radar: 1/1 passed.
Python/backend and MapData suite 142/142 passed, Ruff passed.
The Radar run asserts the vehicle stops at x in [2,4], earlier than the prior
range-only stop, before removing the wall and completing passenger/cargo missions.
Mobile store continuity and accepted monotonic telemetry after actor recreation
are also checked. XML/trace/source hashes retain exact evidence.

The fixture is synthetic; no full crossing TTC, swept body, production scene,
Player, Android or performance claim follows. Scalar approach time can produce
conservative false stops. Sensor missing/invalid/age gates remain authoritative.
Unity allocation-lifetime and Python dependency deprecation warnings remain.
