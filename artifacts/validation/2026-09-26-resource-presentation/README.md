# Resource presentation — 2026-09-26

Uncommitted work after v0.3.2.0. See Docs/resource_wait_presentation.md.

- `python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache`: 359 passed, one existing deprecation warning.
- `python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration`: passed.
- `AgentScripts/RunUnityClientTests.ps1`: Unity 6000.3.21f1 isolated EditMode 23/23 passed.
- `AgentScripts/RunUnityPhysicsIntegration.ps1 -PythonPath tmp/backend-venv/Scripts/python.exe -Reservations -FleetThree`: isolated PlayMode 1/1 passed.

Physics included an actual Mobile_Passenger socket subscribed to reservation-a,
plus the PC operator and three Physics actors. Assertions prove PC/mobile resource
wait receipt, null passenger ETA during resource wait and no other-owner requests
or actuator commands in the mobile stream. Actual entry order V02,V01,V03; all
three services completed. Sensor ACKs=2260, rejected=0; no sampled collider overlap
or simultaneous corridor occupancy, no residual reservation claims or faults.

The executed test required mobile wait unconditionally for three vehicles. After
its source copy was made, the source assertion was guarded for nondeterministic
first-grant ordering: it now requires that observation when V01 queues behind
another entrant. The recorded run had V02 first, so the guarded condition applies
and the stronger executed assertion passed. Production source hashes are unchanged.

The EditMode checks cover actual guidance selection and text plus all copied
client assembly compilation; no live UI screen rendering or device run is claimed.
The two new enum values require matched updated alpha clients. No compatibility
negotiation, real map, Player/Android or 50-client capacity result is claimed.
