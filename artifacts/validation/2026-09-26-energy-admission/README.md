# Energy admission and accounting — 2026-09-26

Uncommitted after v0.4.0.0. See Docs/energy_admission.md.

- Full Python/MapData regression: 455 passed in 21.32s, existing Starlette/httpx warning.
  Command: python -m pytest backend/tests AgentScripts/MapData -q -o cache_dir=tmp/pytest-cache.
- Subsequent focused validation after adding known controller-speed bounds and suppressing
  invalid-map battery display: 22 energy tests passed in 0.73s (one existing dependency warning).
  Command: python -m pytest backend/tests/test_energy.py -q -o cache_dir=tmp/pytest-cache.
- Ruff over backend/MapData/UnityPhysicsIntegration and git diff --check passed.
- Source hashes describe the final focused-test source; the earlier full-suite run preceded
  the controller-speed-bound addition, its extra test, and the battery display validity guard.

Evidence includes exact 35Wh/34.999Wh admission boundary, a 12s synthetic trip consuming
11.2Wh to reach 23.8Wh, a second mission rejected from consumed balance, both actual
multi-vehicle dispatch policies excluding a low-energy vehicle, unreachable chargers,
mid-mission deficit/charger closure, preserved requests/positions, sensor priority,
accepted ego displacement debited once, session-change invalidation, curved polyline
metering, opt-in configuration and health metadata. These are modeled quantities.

No Unity/Player/device execution occurred in this step. Automatic charging travel,
charge queues/times, hubs, real battery/vehicle calibration, actual curved ego path
reconstruction, support recovery, fleet performance/load and overall MVP remain open.
