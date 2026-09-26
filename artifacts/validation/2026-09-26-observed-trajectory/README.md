# Observed trajectory adapter validation

2026-09-26, Python synthetic algorithm/service validation.

- Full command: `tmp/backend-venv/Scripts/python.exe -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache` — **286 passed**, one existing Starlette/httpx deprecation warning.
- `tmp/backend-venv/Scripts/python.exe -m ruff check backend AgentScripts/MapData AgentScripts/UnityPhysicsIntegration` — passed.
- Focused observed-trajectory and local-candidate tests: **43 passed**.
- `git diff --check` — passed (line-ending warnings only).

The adapter maps measured surface motion to the continuous timed-footprint checker
using explicit capture-delay and clock-rate bounds. Tests verify immediate contact
inside the timing uncertainty interval, a prediction shortened by the oldest
sensor, unequal clock rates and unrelated clock origins, invalid/current-surface
association rejection, and service candidate epoch invalidation.

`python-tests.txt` is the full run output; `source-manifest.json` binds relevant
code and focused tests. No Unity run was added for this Python-only layer. There
is no live RRT authority, free-area coverage proof, automatic clock calibration,
or validated real vehicle envelope. Existing synthetic assumptions require
qualification before runtime execution. Previous Unity evidence remains limited
to its own recorded source versions.
