"""Real service/transport with one vehicle on a short, explicitly synthetic graph."""
import os
from pathlib import Path

from campus_sim.api import create_app
from campus_sim.controller import ControllerPolicy
from campus_sim.crossing import CrossingPolicy
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]
closure_scenario = os.environ.get("INHA_UNITY_E2E_ZONE_CLOSURE") == "1"
fixture = "physics-zone-integration-3.json" if closure_scenario else "physics-integration-3.json"
service = MobilityService.from_synthetic_graph(ROOT / "maps/fixtures" / fixture)
if os.environ.get("INHA_UNITY_E2E_PYTHON_CONTROL") == "1":
    service.control_policies["V01"] = ControllerPolicy(
        1, 1.5707963267948966, 2, 2, 0.15, 0.14, 0.2, "Synthetic command actuator test limits",
    )
if os.environ.get("INHA_UNITY_E2E_CROSSING") == "1":
    service.crossing_policies["V01"] = CrossingPolicy(
        ego_envelope_radius_m=0.3, pedestrian_diameter_m=1.0,
        position_uncertainty_m=0.1, velocity_uncertainty_mps=0.1, horizon_s=2.0,
        provenance="Synthetic test: centered 0.4m square shell, default 1m capsule; assumed errors",
    )
app = create_app(service)
if os.environ.get("INHA_UNITY_E2E_DISCONNECT") == "1":
    from socket_fault import SocketFaultHarness, SocketFaultState

    socket_fault = SocketFaultState()
    app.add_middleware(SocketFaultHarness, state=socket_fault)

    @app.post("/test/disconnect")
    async def disconnect_test_socket() -> dict:
        return {"closed_pc": socket_fault.disconnect()}

if closure_scenario:
    @app.post("/test/closure/{closed}")
    async def set_test_closure(closed: bool) -> dict:
        # Only this loopback test harness exposes the endpoint, not campus_sim.api.
        service.set_explicit_zone_closure("synthetic-test-corridor", closed)
        return {"closed": closed}
