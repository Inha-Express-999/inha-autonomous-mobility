# Charging lifecycle verification — 2026-09-26

- `python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache`: 467 passed, 1 existing Starlette/httpx deprecation warning, 21.99 s.
- `python -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration`: all checks passed after import spacing correction.
- 11 charging cases cover state/energy conservation/queue/recovery and production service charging-to-mission completion.
- No Unity/Player/device run. No automatic route to charger, physical dock exclusion or full M5 claim.
- Source hashes identify this worktree evidence; existing historical artifacts remain unchanged.
