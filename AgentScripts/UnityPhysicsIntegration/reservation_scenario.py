"""Loopback-only opposing Physics scenario; no hidden vehicle state inputs."""
from campus_sim.controller import ControllerPolicy
from campus_sim.domain import CreateRequest, ServiceType
from campus_sim.reservations import ResourceReservations
from campus_sim.resource_admission import AdmissionPolicy, ResourceAdmission
from campus_sim.resource_occupancy import (
    OccupancyPolicy,
    ResourceOccupancyTracker,
    ResourceRegion,
)
from campus_sim.swept_geometry import BoxFootprint


def configure(app, service, *, three=False):
    vehicles = ("V01", "V02", "V03") if three else ("V01", "V02")
    service._initialize_vehicle_runtimes(vehicles)
    provenance = "Synthetic 0.4m shells, test corridor and bounded sampling assumptions"
    resources = {"corridor", "west-exit", "east-exit"}
    book = ResourceReservations(resources, map_version=service.graph.map_version)
    service.resource_occupancy = ResourceOccupancyTracker(book, (
        ResourceRegion("corridor", -0.5, -0.6, 4.5, 0.6, provenance),
        ResourceRegion("west-exit", -2, -1.6, -0.5, 1.6, provenance),
        ResourceRegion("east-exit", 4.5, -1.6, 6, 1.6, provenance)),
        {v: OccupancyPolicy(BoxFootprint(0.4, 0.4), 0.02, 2, 0.5, 0.2, provenance)
         for v in vehicles})
    service.resource_admission = ResourceAdmission(service.resource_occupancy,
        {name: frozenset(resources) for name in resources}, AdmissionPolicy(0.4, 2, 0.1, 20, 120, provenance))
    for vehicle in vehicles:
        service.control_policies[vehicle] = ControllerPolicy(1, 1.5707963267948966, 2, 2, 0.15, 0.14, 0.2, provenance)

    @app.post("/test/reservation-start")
    async def start():
        results = []
        for suffix in ("a", "b"):
            results.append(service.create_request(CreateRequest(command_id="reservation-" + suffix,
                owner_id="reservation-" + suffix, service_type=ServiceType.PASSENGER,
                pickup_landmark_id="reservation_" + suffix,
                dropoff_landmark_id="reservation_finish_" + suffix)).accepted)
        if three:
            results.append(service.create_request(CreateRequest(command_id="reservation-c",
                owner_id="reservation-c", service_type=ServiceType.CARGO, party_size=0, cargo_kg=5,
                pickup_landmark_id="reservation_c", dropoff_landmark_id="reservation_finish_c")).accepted)
        return {"accepted": all(results)}

    @app.get("/test/fleet-reservations")
    async def summary():
        monitor = service.resource_occupancy
        return {"completed": sum(e.kind == "COMPLETED" for e in book.events),
                "claims": len(book.claims), "closed": len(book.closed), "faults": len(monitor.faults),
                "pending": len(book.pending),
                "requests": [{"id": r.id, "vehicle": r.vehicle_id, "service": r.service_type.value,
                              "status": r.status.value} for r in service.requests.values()],
                "events": [{"time": e.at_s, "kind": e.kind, "vehicle": e.vehicle_id,
                            "resources": list(e.resources)} for e in book.events if e.kind != "OCCUPANCY"
                           and (e.kind != "UNPLANNED_OCCUPANCY" or e.resources)]}
