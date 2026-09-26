"""Real service/transport with one vehicle on a short, explicitly synthetic graph."""
import os
from pathlib import Path

from campus_sim.api import create_app
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]
closure_scenario = os.environ.get("INHA_UNITY_E2E_ZONE_CLOSURE") == "1"
fixture = "physics-zone-integration-3.json" if closure_scenario else "physics-integration-3.json"
service = MobilityService.from_synthetic_graph(ROOT / "maps/fixtures" / fixture)
app = create_app(service)

if closure_scenario:
    @app.post("/test/closure/{closed}")
    async def set_test_closure(closed: bool) -> dict:
        # Only this loopback test harness exposes the endpoint, not campus_sim.api.
        service.set_explicit_zone_closure("synthetic-test-corridor", closed)
        return {"closed": closed}
