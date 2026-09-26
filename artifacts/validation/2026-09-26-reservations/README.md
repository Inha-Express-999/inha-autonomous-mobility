# Reservation state-machine validation

2026-09-26 · Python logic only, no Physics integration.

- Full suite: `python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache` — **331 passed**, one existing Starlette/httpx warning.
- `python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration` — passed.
- Reservation tests contribute 15 cases for atomic entry/exit-space grants,
  expiration with actual occupancy, explicit exits, priority/aging, fault closure,
  late/unplanned entry, deadlock reporting and boundary context guards.

The tests use abstract named resources and explicit occupancy events. They do not
demonstrate actual world double-occupancy prevention or stop-line braking. Verified
map-to-resource/ego-footprint adapters, service authority, Unity scenarios, CBS and
comparison metrics are pending. No hidden actor Transform is consumed.

The full output and hashes of the new module/tests accompany this file. A test
edit initially placed a detached-copy assertion after an occupancy transition;
the assertion was restored to its intended context before the passing full run.
