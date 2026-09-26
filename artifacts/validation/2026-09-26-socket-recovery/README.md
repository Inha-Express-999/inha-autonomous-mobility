# Actual socket interruption and same-service recovery

Unity 6000.3.21f1 · 2026-09-26 · isolated PlayMode.

Command: `RunUnityPhysicsIntegration.ps1 -PythonPath <repo>/tmp/backend-venv/Scripts/python.exe -Crossing -PythonControl -Disconnect`.

The loopback-only ASGI fault harness closes exactly one subscribed PC socket while
the vehicle is moving, then rejects new WebSocket connections for 1.5 seconds.
Existing mobile connection/service state continue. Production API has no fault
endpoint or middleware.

Initial run failed: snapshots restarted at sequence zero on reconnect while runId
remained unchanged. The existing WorldStateStore correctly rejected them as old,
so recovery hold snapshots were hidden until the new counter caught up. Before
XML/hash/trace are preserved. Fixed by reserving snapshot sequences on the shared
service before each send. IDs now belong to each service instance, so an actual
new instance has a new runId. Sequence gaps between clients are valid.

After fix: **1/1 passed**. The test verifies command expiry and physical stop,
bounded travel (<0.65 m), position held while disconnected, reconnect with the same
session/run/actor, fresh-sensor clear hold and resumed motion. Command sequence
41→63 across interruption; actor recreation later continues 288→290. Passenger
completion follows the crossing. 259 sensor frames accepted, zero rejected.
Sampled minimum clearance 0.589 m, no sampled overlap; not a continuous clearance
or one-metre safety guarantee. The changed approach timing prevents treating this
as a controlled comparison with prior clearance numbers.

Backend/API regression tests and full Python/MapData suite: 242 passed; Ruff passed.
Existing Python deprecation and Unity allocation diagnostics remain. Actual server
process restart/lost memory, moving-actor recreation, Player/Android and production
load are separate unverified gates.
