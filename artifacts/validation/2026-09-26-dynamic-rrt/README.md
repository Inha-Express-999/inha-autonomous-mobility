# Mixed observed-object RRT candidate evidence

2026-09-26 · Python synthetic service/algorithm validation, not vehicle execution.

The fixture enters ego localization and two sensor frames through production
`MobilityService` methods. The tracker derives pedestrian surface motion; no
Ground Truth velocity is injected. With explicit synthetic envelopes, delay and
clock-rate bounds, capture creates conservative short-horizon sweep discs.
Own RRT then finds a seed-17 detour in the supplied synthetic corridor.

`candidate.json` records the resulting poses, policy, horizon and timed assessment:
3 planning discs, 1.9 s prediction/checked prefix, about 12.365 s full trajectory,
no observed collision in the prefix. Execution is false and no route is published.
The unchecked remainder and unobserved space cannot be called safe.

Validation commands:

- `tmp/backend-venv/Scripts/python.exe -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache` — **298 passed**, one existing dependency deprecation warning.
- `tmp/backend-venv/Scripts/python.exe -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration` — passed.

Cases include mixed-object detour, blocked narrow corridor, missing/unsupported
motion, changed time or assumptions, static-only compatibility, and continuous
motion-envelope containment with stationary, ordinary and very fast predictions.
Existing footprint tests check every returned turn and translation, including
after bounded path shortcutting; an open corridor reduces to one straight stage.

Reproduce the candidate fixture with Python's `runpy.run_path` on
`backend/tests/test_dynamic_local_candidate.py`, then call its
`observed_service(with_static=True)`, `candidate(service)`, and
`candidate.assess_current_observations(service, LIMITS, BOUNDS)`. All values are
fixture assumptions. The source manifest and full pytest output are retained.

No new Unity/Player run, latency calibration, real-map/corridor approval, dynamic
coordination, performance claim or live RRT control is included. Area coverage,
route rejoin and safety/controller arbitration remain open.
