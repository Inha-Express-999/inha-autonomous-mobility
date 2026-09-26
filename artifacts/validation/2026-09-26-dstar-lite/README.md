# D* Lite evidence

2026-09-26 · synthetic planner/service validation.

Commands:

- `python -m campus_sim.cli replan-compare --repetitions 20 --events 30` → `comparison.json`.
- `python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache` → `python-tests.txt`.
- `python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration` → passed.

Full Python/MapData suite: **316 passed**, one existing Starlette/httpx warning.

The recorded comparison uses the 11-node graph, seed 17 and 600 event evaluations
per algorithm. All route costs and unreachable states match Dijkstra. There are
140 unreachable results for each planner. Whole-call wall p95: A* 58,700 ns;
D* Lite 181,500 ns. The experiment provides no speedup evidence on this small graph.
Hardware/OS/Python metadata and graph hash are in the JSON; source hashes accompany it.

Failed-search A* expansion counts are unavailable. Timing includes cold D* creation
but excludes service cache fingerprinting, network delivery and Physics response.
No latest Unity execution, actual campus graph or M3/T24 end-to-end completion claim.
The default planning config remains A*; D* Lite is selectable and service-tested.
