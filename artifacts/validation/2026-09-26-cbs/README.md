# Offline coordination checkpoint — 2026-09-26

Five repetitions per algorithm/case, via compare_coordination with configs/coordination_benchmark.json. See comparison.json for inputs, metrics and platform; source-hashes.json identifies exact algorithm/test/fixture source.

Both three-vehicle corridor cases: priority 5/5 successful, CBS 5/5 constraint-tree budget exceeded (500). Insufficient horizon: neither algorithm successful. No partial plans or physical execution authority on failure. No actual Physics collision metric is measured here.

Commit preparation: Python/MapData 374 passed (one existing dependency warning), Ruff passed. Unity evidence is recorded separately against each run's copied source.
