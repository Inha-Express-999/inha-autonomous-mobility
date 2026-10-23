from dataclasses import replace
from itertools import pairwise
from threading import Event

import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.domain import CreateRequest
from campus_sim.planning import CostSnapshot
from campus_sim.service import MobilityService


def submit(service, command_id, pickup="main_gate", dropoff="library"):
    return service.create_request(
        CreateRequest(
            command_id=command_id,
            owner_id="test",
            service_type="PASSENGER",
            pickup_landmark_id=pickup,
            dropoff_landmark_id=dropoff,
        )
    ).request


def test_round_trip_uses_three_independent_searches_and_reserves_until_home():
    service = MobilityService.synthetic_fixture(clock=lambda: 0)
    request = submit(service, "one", "library", "hitech")
    trip = service.trips[request.id]
    assert trip.pickup_route.cost_s == 20
    assert trip.delivery_route.cost_s == 20
    assert trip.return_route.cost_s == pytest.approx(160 / 3)
    assert trip.returns_at_s == pytest.approx(40 + 160 / 3)
    service.step(trip.dropoff_at_s)
    assert request.status == "COMPLETED"
    assert not service.vehicles["V01"].available
    service.step(trip.returns_at_s - 0.001)
    assert not service.vehicles["V01"].available
    service.step(trip.returns_at_s)
    assert service.vehicles["V01"].available and trip.returned


def test_queued_requests_dispatch_at_return_and_large_time_jump_replays_events():
    service = MobilityService.synthetic_fixture(clock=lambda: 0, vehicle_count=2)
    requests = [submit(service, str(i)) for i in range(6)]
    assert [r.status for r in requests] == ["IN_TRANSIT"] * 2 + ["QUEUED"] * 4
    initial_return = service.trips[requests[0].id].returns_at_s
    service.step(500)
    assert all(r.status == "COMPLETED" for r in requests)
    assert all(t.returned for t in service.trips.values())
    assert all(v.available for v in service.vehicles.values())
    next_trips = sorted(
        (t for t in service.trips.values() if t.departed_at_s > 0), key=lambda t: t.departed_at_s
    )
    assert next_trips[0].departed_at_s == initial_return
    for vehicle_id in ("V01", "V02"):
        trips = sorted(
            (t for t in service.trips.values() if t.vehicle_id == vehicle_id),
            key=lambda t: t.departed_at_s,
        )
        assert all(a.returns_at_s <= b.departed_at_s for a, b in pairwise(trips))


def test_missing_return_route_keeps_request_queued():
    service = MobilityService.synthetic_fixture(clock=lambda: 0)
    service.graph = replace(
        service.graph, edges=tuple(e for e in service.graph.edges if e.v != "main_gate")
    )
    service.costs = CostSnapshot.travel_time(service.graph)
    request = submit(service, "no-return")
    assert request.status == "QUEUED" and not service.trips
    assert all(v.available for v in service.vehicles.values())


def test_default_single_vehicle_fifo_until_return():
    service = MobilityService.synthetic_fixture(clock=lambda: 0)
    assert list(service.vehicles) == ["V01"]
    first = submit(service, "first")
    second = submit(service, "second")
    third = submit(service, "third")
    assert second.status == third.status == "QUEUED"
    returns_at = service.trips[first.id].returns_at_s
    service.step(returns_at - 0.01)
    assert second.status == "QUEUED"
    service.step(returns_at)
    assert second.vehicle_id == "V01"
    assert third.status == "QUEUED"
    assert service.trips[second.id].departed_at_s == returns_at
    assert not service.vehicles["V01"].available


def test_future_three_vehicle_configuration_is_explicit():
    assert len(MobilityService.synthetic_fixture(vehicle_count=3).vehicles) == 3
    with pytest.raises(ValueError):
        MobilityService.synthetic_fixture(vehicle_count=0)


def test_waiting_request_revalidated_for_accessibility():
    service = MobilityService.synthetic_fixture(clock=lambda: 0)
    for vehicle in service.vehicles.values():
        vehicle.available = False
    command = CreateRequest(
        command_id="accessible",
        owner_id="test",
        service_type="PASSENGER",
        pickup_landmark_id="main_gate",
        dropoff_landmark_id="library",
        service_needs={"requires_step_free": True, "wheelchair_slots": 1},
    )
    request = service.create_request(command).request
    service.stops[request.dropoff_stop_id].step_free_access = False
    service.vehicles["V01"].available = True
    service.step(1)
    assert request.status == "QUEUED"


def test_fake_clock_auto_return_without_manual_step_and_debug_http(monkeypatch):
    clock = [0.0]
    service = MobilityService.synthetic_fixture(clock=lambda: clock[0])
    with TestClient(create_app(service)) as client:
        body = CreateRequest(
            command_id="http",
            owner_id="test",
            service_type="PASSENGER",
            pickup_landmark_id="main_gate",
            dropoff_landmark_id="library",
        ).model_dump(mode="json")
        first = client.post("/v1/requests", json=body)
        repeated = client.post("/v1/requests", json=body)
        assert first.status_code == 201
        assert repeated.json()["request"]["id"] == first.json()["request"]["id"]
        state = client.get("/v1/debug/virtual-state").json()
        assert state["synthetic"] and not state["battery_model"]
        trip = state["trips"][0]
        assert trip["delivery_route"]["edge_ids"]
        returned = Event()
        original_step = service.step

        def observed_step(*args, **kwargs):
            result = original_step(*args, **kwargs)
            if service.trips and all(t.returned for t in service.trips.values()):
                returned.set()
            return result

        monkeypatch.setattr(service, "step", observed_step)
        clock[0] = trip["returns_at_s"]
        assert returned.wait(2), "background ticker must return vehicle without a GET request"
        assert service.vehicles["V01"].available
        state = client.get("/v1/debug/virtual-state").json()
        assert state["trips"][0]["returned"]
        assert state["requests"][0]["status"] == "COMPLETED"


@pytest.mark.parametrize("bad_time", [-1, float("nan"), float("inf")])
def test_invalid_virtual_time_rejected(bad_time):
    service = MobilityService.synthetic_fixture(clock=lambda: 0)
    with pytest.raises(ValueError, match="time"):
        service.step(bad_time)
