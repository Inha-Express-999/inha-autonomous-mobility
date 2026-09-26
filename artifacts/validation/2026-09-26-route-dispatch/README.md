# Route-derived production dispatch comparison — 2026-09-26

Uncommitted work after v0.4.0.0. Commands actually executed:

- python -m campus_sim.cli dispatch-route-compare --repetitions 20
  comparison.json: 5 scenarios × 2 production algorithms × 20 repetitions.
- python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache
  python-tests.txt: 434 passed in 21.31s, one existing Starlette/httpx warning.
- python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration: passed.
- git diff --check: passed.

Source hashes identify tested runtime, evaluator, CLI, test, config and map inputs.
Full scenario/capability/policy/map snapshots and fingerprints are in comparison.json.
No algorithm switches in the deployed/default config; its default remains Greedy.

Same-request basic case: Greedy/Hungarian costs 245/205 seconds and predicted empty
travel 760/560m, all 3 requests assigned. Capacity/accessibility, aging fairness,
post-queue closure and oversubscribed cases are also included. The oversubscribed
case serves different request sets (unassigned R4 vs R1); its lower total is not
reported as a same-request cost reduction. Failed route pairs are None, not large
soft penalties. Predictions are not actual Unity/campus service measurements.

Timing includes production candidate planning, policy/matching and route install;
it excludes fixture construction, clone and evidence tracing. Each run starts with
identical cold route caches. Small synthetic graphs on a development PC are not a
full performance/load result. Actual completed-service fairness, energy/charging,
hubs and real map/Physics replay remain unverified or unimplemented. No Unity rerun.
