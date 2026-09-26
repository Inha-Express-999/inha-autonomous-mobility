"""Real service/transport with one vehicle on a short, explicitly synthetic graph."""
import os
from pathlib import Path

from campus_sim.api import create_app
from campus_sim.controller import ControllerPolicy
from campus_sim.crossing import CrossingPolicy
from campus_sim.reservations import ResourceReservations
from campus_sim.resource_admission import AdmissionPolicy, ResourceAdmission
from campus_sim.resource_occupancy import (
    OccupancyPolicy,
    ResourceOccupancyTracker,
    ResourceRegion,
)
from campus_sim.service import MobilityService
from campus_sim.swept_geometry import BoxFootprint

ROOT = Path(__file__).resolve().parents[2]
closure_scenario = os.environ.get("INHA_UNITY_E2E_ZONE_CLOSURE") == "1"
fixture = "physics-zone-integration-3.json" if closure_scenario else "physics-integration-3.json"
if os.environ.get("INHA_UNITY_E2E_RESERVATIONS") == "1":
    fixture = "physics-reservation-6.json"
    if os.environ.get("INHA_UNITY_E2E_FLEET_THREE") == "1":
        fixture = "physics-reservation-8.json"
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
if os.environ.get("INHA_UNITY_E2E_RESERVATIONS") == "1":
    from reservation_scenario import configure

    configure(app, service, three=os.environ.get("INHA_UNITY_E2E_FLEET_THREE") == "1")

if (os.environ.get("INHA_UNITY_E2E_CROSSING") == "1"
        and os.environ.get("INHA_UNITY_E2E_PYTHON_CONTROL") == "1"
        and os.environ.get("INHA_UNITY_E2E_DISCONNECT") != "1"):
    # Synthetic occupied exit fixture, not a second Physics vehicle. Release is
    # explicit in the test; V01 must wait outside the corridor until then.
    reservations = ResourceReservations({"test-corridor", "test-exit"}, map_version=service.graph.map_version)
    reservations.enqueue("exit-blocker", "fixture-blocker", {"test-exit"}, now_s=0, lease_s=120)
    blocker_lease, = reservations.advance(0)
    service.resource_occupancy = ResourceOccupancyTracker(
        reservations,
        (ResourceRegion("test-corridor", 1, -0.6, 2, 0.6, "Synthetic crossing test rectangle"),
         ResourceRegion("test-exit", 2.4, -0.6, 3.4, 0.6, "Synthetic exit-space test rectangle")),
        {"V01": OccupancyPolicy(BoxFootprint(0.4, 0.4), 0.02, 2, 0.5, 0.2,
                                "Synthetic 0.4m shell and conservative timing/error bounds")},
    )
    service.resource_admission = ResourceAdmission(
        service.resource_occupancy,
        {"test-corridor": frozenset({"test-corridor", "test-exit"}), "test-exit": frozenset({"test-exit"})},
        AdmissionPolicy(0.4, 2, 0.1, 10, 90, "Synthetic reaction/braking/stop-margin assumptions"),
    )
    reservations.report_occupancy(blocker_lease.token, "fixture-blocker", {"test-exit"},
                                  now_s=0, map_version=service.graph.map_version)

    @app.post("/test/release-exit")
    async def release_test_exit() -> dict:
        reservations.report_occupancy(blocker_lease.token, "fixture-blocker", set(),
                                      now_s=service.now_s(), map_version=service.graph.map_version)
        return {"released": True}


@app.get("/test/reservation-summary")
async def reservation_summary() -> dict:
    monitor = service.resource_occupancy
    if monitor is None:
        return {"enabled": False}
    return {"enabled": True, "completed": sum(e.kind == "COMPLETED" and e.vehicle_id == "V01"
                                               for e in monitor.book.events),
            "claims": len(monitor.book.claims), "closed": len(monitor.book.closed),
            "faults": len(monitor.faults)}


@app.get("/test/ray-summary")
async def ray_summary() -> dict:
    # Loopback harness only; verifies what production ingress actually retained.
    frames = list(service.sensor_observations.values())
    return {"frames": len(frames), "rays": sum(len(f.rays) for f in frames),
            "hits": sum(r.outcome == "HIT" for f in frames for r in f.rays),
            "misses": sum(r.outcome == "MISS" for f in frames for r in f.rays)}


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
