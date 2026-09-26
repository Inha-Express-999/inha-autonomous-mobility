# Service-connected coordination worker — 2026-09-26

Uncommitted after v0.4.0.0. Source hashes identify the actual tested working tree.

Command: python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache
Result: 425 passed in 21.06s, one existing Starlette/httpx dependency warning.
Raw combined output: python-tests.txt. Ruff over backend/MapData/UnityPhysicsIntegration
and git diff --check also passed.

The suite includes 26 coordination-runtime cases. Two start actual spawn-process
workers: one runs three-vehicle CBS while the service clock advances and stale
sensors command a stop before the worker finishes; the other attaches the adapter
to three staged service missions with fresh telemetry, receives a three-vehicle
non-executable proposal and invalidates it after a bad sensor frame. The latter
prepares post-pickup mission state; it does not replay physical boarding/transport.

Other cases cover frozen input, matching stationary pose/frame tick updates,
route/geometry/profile/session/pose/request changes, stale/invalid sensing,
movement/off-node rejection, bounded queue coalescing, old results, expiry, future
failure/retry cooldown, closed worker, API lifespan cleanup, preparation deadline,
resource claim/closure/map/unplanned-occupancy changes, existing entered-lease
rejection and graph cost/closure snapshot content. An injected Future exception
is used for failure tests; actual OS process crash/restart was not exercised.

No Unity/Player/Android run, new route execution, live lease order conversion,
mid-edge planning, bounded AVOID composition, hard process-hang watchdog, or
50-client/load performance claim is made. Current valid proposals always remain
executable=false. Prior controller/resource admission retains sole motion gating.
See Docs/coordination_worker.md for opt-in configuration and remaining MVP gates.
