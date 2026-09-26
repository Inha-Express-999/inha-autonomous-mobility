import math
from types import SimpleNamespace

import pytest

from campus_sim.controller import ControllerPolicy
from campus_sim.domain import EgoLocalization, MapPosition
from campus_sim.reservations import ResourceReservations
from campus_sim.resource_admission import AdmissionPolicy, ResourceAdmission
from campus_sim.resource_occupancy import OccupancyPolicy, ResourceOccupancyTracker, ResourceRegion
from campus_sim.swept_geometry import BoxFootprint

CONTROL = ControllerPolicy(1, 1, 2, 2, 0.15, 0.14, 0.2, "synthetic actuator")


def setup():
    book = ResourceReservations({"corridor", "exit"}, map_version="synthetic-admission")
    monitor = ResourceOccupancyTracker(book, (
        ResourceRegion("corridor", 3, -1, 5, 1, "synthetic corridor"),
        ResourceRegion("exit", 5.5, -1, 6.5, 1, "synthetic exit space")),
        {"V01": OccupancyPolicy(BoxFootprint(0.4, 0.4), 0, 2, 0.5, 0.2, "synthetic bounds")})
    gate = ResourceAdmission(monitor, {"corridor": frozenset({"corridor", "exit"}),
                                       "exit": frozenset({"exit"})},
                              AdmissionPolicy(0.4, 2, 0.1, 10, 30, "synthetic stop envelope"))
    runtime = SimpleNamespace(route_id="route1", route_points=[(0, 0), (8, 0)],
                              route_progress_m=0, x=0, y=0, speed_mps=0, request_id=None)
    service = SimpleNamespace(simulation_time_s=0, vehicle_runtime={"V01": runtime}, requests={},
                              graph=SimpleNamespace(map_version=book.map_version))
    service.now_s = lambda: service.simulation_time_s
    service.ego_localization_is_stale = lambda vehicle: False
    monitor.observe(EgoLocalization(vehicle_id="V01", session_id="s", observed_tick=0,
                                    map_version=book.map_version, position=MapPosition(x=0, y=0, z=0),
                                    heading_rad=math.pi / 2, speed_mps=0), 0)
    return service, gate


def block_exit(book):
    book.enqueue("blocker", "V02", {"exit"}, now_s=0)
    permit, = book.advance(0)
    book.report_occupancy(permit.token, "V02", {"exit"}, now_s=0, map_version=book.map_version)
    return permit


def test_blocked_exit_prevents_corridor_grant_and_produces_hold_outside_body_boundary():
    service, gate = setup()
    book, runtime = gate.monitor.book, service.vehicle_runtime["V01"]
    blocker = block_exit(book)
    gate.sync(service)
    assert "corridor" not in book.claims  # Atomic corridor + exit admission.
    runtime.x = runtime.route_progress_m = 2.62
    speed, yaw, reason = gate.limit(service, "V01", 1, 0.2, CONTROL)
    assert speed == yaw == 0
    assert reason == "RESOURCE_WAIT"
    assert runtime.x + gate.monitor.policies["V01"].footprint.radius_m < 3
    book.report_occupancy(blocker.token, "V02", set(), now_s=0, map_version=book.map_version)
    gate.sync(service)
    assert gate.limit(service, "V01", 1, 0, CONTROL) == (1, 0, None)


def test_speed_cap_respects_reaction_plus_braking_distance_and_never_accelerates():
    service, gate = setup()
    block_exit(gate.monitor.book)
    gate.sync(service)
    runtime = service.vehicle_runtime["V01"]
    runtime.x = runtime.route_progress_m = 2.3
    speed, _, _ = gate.limit(service, "V01", 1, 0, CONTROL)
    distance = 3 - gate.monitor.policies["V01"].footprint.radius_m - runtime.x - 0.1
    assert 0 < speed < 1
    assert speed * 0.4 + speed**2 / (2 * 2) == pytest.approx(distance)
    assert gate.limit(service, "V01", 0.01, 0, CONTROL)[0] <= 0.01


def test_impending_expiry_revokes_permission_before_braking_window():
    service, gate = setup()
    gate.sync(service)
    runtime = service.vehicle_runtime["V01"]
    runtime.x = runtime.route_progress_m = 2.62
    service.simulation_time_s = 9.9
    assert gate.limit(service, "V01", 1, 0, CONTROL)[0] == 0


def test_waiting_vehicle_can_align_at_rest_only_outside_body_hold_boundary():
    service, gate = setup()
    block_exit(gate.monitor.book)
    gate.sync(service)
    assert gate.limit(service, "V01", 0, 0.5, CONTROL) == (0, 0.5, "RESOURCE_WAIT")
    runtime = service.vehicle_runtime["V01"]
    runtime.x = runtime.route_progress_m = 2.62
    assert gate.limit(service, "V01", 0, 0.5, CONTROL) == (0, 0, "RESOURCE_WAIT")


def test_route_replacement_cancels_old_pending_request_without_releasing_other_occupant():
    service, gate = setup()
    permit = block_exit(gate.monitor.book)
    gate.sync(service)
    old = set(gate.monitor.book.pending)
    runtime = service.vehicle_runtime["V01"]
    runtime.route_id = "new-route"
    runtime.route_points = [(0, 0), (-2, 0)]
    gate.sync(service)
    assert not old.intersection(gate.monitor.book.pending)
    assert gate.monitor.book.leases[permit.token].occupied == {"exit"}


def test_timeout_does_not_silently_requeue_with_reset_wait_age():
    service, gate = setup()
    block_exit(gate.monitor.book)
    gate.sync(service)
    service.simulation_time_s = 30
    gate.sync(service)
    gate.sync(service)
    assert "V01" in gate.timed_out
    assert not gate.monitor.book.pending
    assert gate.limit(service, "V01", 1, 0, CONTROL) == (0, 0, "RESOURCE_STATE_UNAVAILABLE")


def test_fault_cannot_be_overridden_by_an_existing_grant():
    service, gate = setup()
    gate.sync(service)
    gate.monitor.check_freshness(1)
    service.simulation_time_s = 1
    assert gate.limit(service, "V01", 1, 0, CONTROL) == (0, 0, "RESOURCE_STATE_UNAVAILABLE")
