from itertools import pairwise

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from campus_sim.api import create_app
from campus_sim.battery import BatteryPolicy
from campus_sim.domain import CreateRequest
from campus_sim.service import MobilityService
from campus_sim.virtual_runtime import plan_round_trip


def fleet(**kwargs):
    return MobilityService.from_osm_map(clock=lambda: 0, **kwargs)


def submit(service, identifier, destination="hitech", **kwargs):
    return service.create_request(
        CreateRequest(
            command_id=str(identifier),
            owner_id="battery-test",
            service_type="PASSENGER",
            pickup_landmark_id="main_gate",
            dropoff_landmark_id=destination,
            **kwargs,
        )
    ).request


def test_three_vehicles_assign_first_three_and_queue_fourth():
    service = fleet()
    requests = [submit(service, i) for i in range(4)]
    assert [r.vehicle_id for r in requests] == ["V01", "V02", "V03", None]
    assert requests[-1].status == "QUEUED"
    first = service.trips[requests[0].id]
    service.step(first.dropoff_at_s)
    assert requests[-1].status == "QUEUED"
    service.step(first.returns_at_s)
    assert requests[-1].vehicle_id == "V01"
    assert service.virtual_state()["vehicle_fifo"] == []
    assert all(not service.trips[r.id].returned for r in requests[1:3])
    assert not service.charges  # Their longer trips have not reached their own hubs yet.


def test_idle_vehicle_fifo_survives_lower_id_returning():
    service = fleet()
    first = submit(service, 1, "dorm_1")
    service.step(service.trips[first.id].returns_at_s)
    assert service.virtual_state()["vehicle_fifo"] == ["V02", "V03"]
    service.step(service.charges[0].ends_at_s)
    assert service.virtual_state()["vehicle_fifo"] == ["V02", "V03", "V01"]
    assert [submit(service, i).vehicle_id for i in range(2, 5)] == ["V02", "V03", "V01"]


def test_energy_formula_includes_loaded_delivery_and_empty_return_and_auxiliary():
    service = fleet(vehicle_count=1)
    request = submit(service, 1, party_size=3)
    trip = service.trips[request.id]
    policy = service.battery_policy
    expected = (
        (trip.pickup_route.length_m + trip.return_route.length_m) * 0.35
        + trip.delivery_route.length_m * 0.35 * 1.15
        + (trip.returns_at_s - trip.departed_at_s) * 50 / 3600
    )
    assert sum(trip.energy_by_leg_wh) == pytest.approx(expected)
    service.step(trip.dropoff_at_s)
    state = service.batteries["V01"]
    assert state.consumed_wh == pytest.approx(sum(trip.energy_by_leg_wh[:2]))
    before = state.remaining_wh
    service.virtual_state()
    service.virtual_state()
    assert state.remaining_wh == before
    service.step(trip.returns_at_s)
    assert state.remaining_wh == pytest.approx(policy.initial_wh - expected)
    assert state.remaining_wh >= policy.reserve_wh


def test_battery_shortfall_skips_vehicle_without_rotating_other_ready_vehicles():
    service = fleet()
    service.batteries["V01"].remaining_wh = 350
    request = submit(service, 1)
    assert request.vehicle_id == "V02"
    assert not service.vehicles["V01"].available
    assert service.virtual_state()["batteries"]["V01"]["state"] == "CHARGING"
    session = service.charges[0]
    service.step(session.ends_at_s)
    # At 20 kW V02 has returned but is still replenishing its trip energy.
    assert service.trips[request.id].returned
    assert service.virtual_state()["batteries"]["V02"]["state"] == "CHARGING"
    assert service.virtual_state()["vehicle_fifo"] == ["V03", "V01"]


def test_reserve_boundary_and_not_just_outbound_energy():
    reference = fleet(vehicle_count=1)
    request = submit(reference, 1)
    energy = sum(reference.trips[request.id].energy_by_leg_wh)
    for offset, assigned in ((0.0, True), (-0.001, False)):
        service = fleet(vehicle_count=1)
        # Isolate the energy boundary from opportunistic idle charging.
        service.battery_policy = BatteryPolicy(
            **(service.battery_policy.model_dump() | {"charge_when_idle": False})
        )
        service.batteries["V01"].remaining_wh = energy + service.battery_policy.reserve_wh + offset
        result = submit(service, 1)
        assert (result.vehicle_id is not None) is assigned
        if not assigned:
            assert service.waiting_reasons[result.id] == "WAITING_BATTERY_CHARGE"
            assert not service.trips and len(service.charges) == 1


@pytest.mark.parametrize("algorithm", ["fifo", "greedy", "hungarian"])
def test_charging_dispatches_at_required_energy_instead_of_full_charge(algorithm):
    reference = fleet(vehicle_count=1)
    r = submit(reference, "reference")
    required = sum(reference.trips[r.id].energy_by_leg_wh) + reference.battery_policy.reserve_wh
    service = fleet(vehicle_count=1)
    service.dispatch_algorithm = algorithm
    service.batteries["V01"].remaining_wh = 350
    request = submit(service, 1)
    charge = service.charges[0]
    full_at = charge.ends_at_s
    ready_at = (required - 350) * 3600 / service.battery_policy.charging_power_w
    service.step(ready_at - 0.001)
    assert request.status == "QUEUED" and not charge.completed
    service.step(ready_at)
    assert request.vehicle_id == "V01"
    assert charge.completed and charge.interrupted
    assert charge.ends_at_s == pytest.approx(ready_at) and ready_at < full_at
    assert service.trips[request.id].departed_at_s == pytest.approx(ready_at)
    assert service.batteries["V01"].remaining_wh == pytest.approx(required)
    service.step(service.trips[request.id].returns_at_s)
    assert service.batteries["V01"].remaining_wh == pytest.approx(service.battery_policy.reserve_wh)


@pytest.mark.parametrize("algorithm", ["fifo", "greedy", "hungarian"])
def test_new_request_interrupts_only_selected_charge(algorithm):
    service = fleet()
    service.dispatch_algorithm = algorithm
    for battery in service.batteries.values():
        battery.remaining_wh = 1800
    service.step(0)
    assert len(service.charges) == 3
    request = submit(service, "interrupt")
    assert request.vehicle_id is not None
    assert sum(c.interrupted for c in service.charges) == 1
    assert sum(not c.completed for c in service.charges) == 2
    charge = next(c for c in service.charges if c.vehicle_id == request.vehicle_id)
    assert charge.interrupted and charge.ends_at_s == 0
    if algorithm == "greedy":
        feasible_costs = [
            service.assignment_cost(
                plan_round_trip(
                    service.graph,
                    service.costs,
                    service.home_nodes[v.id],
                    service.stop_nodes[request.pickup_stop_id],
                    service.stop_nodes[request.dropoff_stop_id],
                    service.route_algorithm,
                ),
                request,
                0,
                v,
            )
            for v in service.vehicles.values()
        ]
        assert service.dispatch_log[-1]["assignments"][0]["cost_s"] == pytest.approx(
            min(feasible_costs)
        )


def test_structurally_impossible_energy_does_not_infinite_charge_or_dispatch():
    service = fleet(vehicle_count=1)
    values = service.battery_policy.model_dump()
    values.update(capacity_wh=400, initial_wh=400)
    service.battery_policy = BatteryPolicy(**values)
    service.batteries["V01"].remaining_wh = 400
    request = submit(service, 1)
    service.step(100000)
    assert request.status == "QUEUED" and not service.trips and not service.charges
    assert service.waiting_reasons[request.id] == "BATTERY_CAPACITY_INSUFFICIENT"


def test_cargo_load_factor_applies_only_during_delivery():
    service = fleet(vehicle_count=1)
    request = service.create_request(
        CreateRequest(
            command_id="cargo",
            owner_id="battery-test",
            service_type="CARGO",
            pickup_landmark_id="main_gate",
            dropoff_landmark_id="hitech",
            cargo_kg=20,
            party_size=0,
        )
    ).request
    trip = service.trips[request.id]
    assert trip.energy_by_leg_wh[1] == pytest.approx(
        trip.delivery_route.length_m * 0.35 * 1.2 + trip.delivery_route.cost_s * 50 / 3600
    )
    assert trip.energy_by_leg_wh[2] == pytest.approx(
        trip.return_route.length_m * 0.35 + trip.return_route.cost_s * 50 / 3600
    )


def test_large_jump_matches_small_steps_through_multiple_charges_and_fifo():
    services = [fleet(), fleet()]
    for service in services:
        for i in range(18):
            submit(service, i)
    services[0].step(20000)
    for tick in range(1, 2001):
        services[1].step(tick * 10)
    for service in services:
        assert all(r.status == "COMPLETED" for r in service.requests.values())
        assert all(t.returned for t in service.trips.values())
        assert len(service.charges) >= 3
        assert all(c.completed for c in service.charges)
        for identifier in service.vehicles:
            trips = [t for t in service.trips.values() if t.vehicle_id == identifier]
            assert all(a.returns_at_s <= b.departed_at_s for a, b in pairwise(trips))
            battery = service.batteries[identifier]
            assert battery.remaining_wh >= service.battery_policy.reserve_wh - 1e-7
            assert battery.remaining_wh == pytest.approx(
                2000 + battery.charged_wh - battery.consumed_wh
            )
    for identifier in services[0].vehicles:
        assert services[0].batteries[identifier].remaining_wh == pytest.approx(
            services[1].batteries[identifier].remaining_wh
        )
    first, second = [list(service.trips.values()) for service in services]
    assert [t.vehicle_id for t in first] == [t.vehicle_id for t in second]
    assert [t.departed_at_s for t in first] == pytest.approx([t.departed_at_s for t in second])


@pytest.mark.parametrize(
    "key,value",
    [
        ("capacity_wh", 0),
        ("initial_wh", 2001),
        ("wh_per_m", -1),
        ("charging_power_w", float("nan")),
        ("reserve_fraction", 0.14),
    ],
)
def test_invalid_battery_policy_rejected(key, value):
    policy = fleet().battery_policy.model_dump()
    policy[key] = value
    with pytest.raises(ValidationError):
        BatteryPolicy(**policy)


def test_default_http_exposes_three_vehicle_battery_and_fifo_state():
    with TestClient(create_app()) as client:
        state = client.get("/v1/debug/virtual-state").json()
        assert state["battery_model"] and not state["synthetic_geometry"]
        assert state["vehicle_fifo"] == ["V01", "V02", "V03"]
        assert len(state["batteries"]) == len(state["vehicles"]) == 3
        assert state["battery_policy"]["reserve_fraction"] == 0.15
        vehicles = []
        for index in range(4):
            response = client.post(
                "/v1/requests",
                json={
                    "command_id": str(index),
                    "owner_id": "http-fleet",
                    "service_type": "PASSENGER",
                    "pickup_landmark_id": "main_gate",
                    "dropoff_landmark_id": "inha_station",
                },
            )
            assert response.status_code == 201
            vehicles.append(response.json()["request"]["vehicle_id"])
        assert vehicles == ["V01", "V02", "V03", None]


def test_cancel_before_pickup_retains_trip_and_energy_until_home():
    service = fleet(vehicle_count=1)
    request = service.create_request(
        CreateRequest(
            command_id="cancel-before-pickup",
            owner_id="battery-test",
            service_type="PASSENGER",
            pickup_landmark_id="hitech",
            dropoff_landmark_id="dorm_1",
        )
    ).request
    trip = service.trips[request.id]
    service.cancel_request(request.id, "cancel", "battery-test")
    assert not service.vehicles["V01"].available
    service.step(trip.returns_at_s)
    assert request.status == "CANCELLED" and trip.returned
    assert service.batteries["V01"].consumed_wh == pytest.approx(sum(trip.energy_by_leg_wh))


def test_idle_return_above_thirty_percent_starts_charge_without_another_request():
    service = fleet()
    request = submit(service, "top-up")
    trip = service.trips[request.id]
    service.step(trip.returns_at_s)
    battery = service.batteries["V01"]
    assert battery.remaining_wh > service.battery_policy.capacity_wh * 0.3
    charge = service.charges[0]
    assert charge.vehicle_id == "V01" and charge.started_at_s == trip.returns_at_s
    assert not service.vehicles["V01"].available
    assert "V01" not in service.virtual_state()["vehicle_fifo"]
    before = battery.remaining_wh
    service.step((charge.started_at_s + charge.ends_at_s) / 2)
    assert battery.remaining_wh > before
    service.step(charge.ends_at_s)
    assert battery.remaining_wh == pytest.approx(service.battery_policy.capacity_wh)
    assert service.virtual_state()["vehicle_fifo"] == ["V02", "V03", "V01"]
    assert len(service.charges) == 1  # Full batteries must not loop into charging.


def test_legacy_charge_policy_can_disable_idle_top_up():
    service = fleet(vehicle_count=1)
    values = service.battery_policy.model_dump() | {"charge_when_idle": False}
    service.battery_policy = BatteryPolicy(**values)
    request = submit(service, "legacy")
    service.step(service.trips[request.id].returns_at_s)
    assert service.vehicles["V01"].available and not service.charges
