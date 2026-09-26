import math
from pathlib import Path

import pytest
from test_controller import ready_service

from campus_sim.controller import make_control_command
from campus_sim.domain import CreateRequest, EgoLocalization, MapPosition, RequestStatus
from campus_sim.energy import EnergyFleet, EnergyPolicy
from campus_sim.realtime import make_snapshot
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]


def policy(**updates):
    return EnergyPolicy(**{"capacity_wh": 100, "wh_per_m": 1, "auxiliary_w": 360,
        "passenger_factor_each": .5, "cargo_factor_per_kg": .01, "max_speed_mps": 5, "provenance": "synthetic test", **updates})


def queued(balance=100):
    service = MobilityService.from_synthetic_graph(ROOT / "maps/fixtures/physics-integration-3.json")
    service.vehicles["V01"].available = False
    ack = service.create_request(CreateRequest(command_id="energy", owner_id="energy", service_type="PASSENGER",
        pickup_landmark_id="fixture_landmark_2", dropoff_landmark_id="fixture_landmark_3"))
    service.vehicles["V01"].available = True
    service.energy = EnergyFleet(map_version=service.graph.map_version, policies={"V01": policy()},
                                initial_wh={"V01": balance}, charger_nodes={"n1"})
    return service, service.requests[ack.request.id]


@pytest.mark.parametrize("balance,expected", [(35, True), (34.999, False)])
def test_includes_empty_pickup_loaded_trip_service_charger_and_fifteen_percent_reserve(balance, expected):
    service, request = queued(balance)
    result = service.energy.assess(service, "V01", request)
    # 4m empty + 4m loaded*1.5 + 8m return + 16s travel*.1W-hours/s + 4s service*.1 + 15Wh reserve.
    assert result.required_wh == pytest.approx(35)
    assert result.charger_node == "n1" and result.allowed is expected
    service._dispatch_queued_requests()
    assert (request.status == RequestStatus.ASSIGNED) is expected


def test_unreachable_charger_and_wrong_map_or_missing_balance_are_infeasible():
    service, request = queued()
    service.graph.edges[:] = [e for e in service.graph.edges if e.id != "e21"]
    result = service.energy.assess(service, "V01", request)
    assert not result.allowed and result.reason == "ENERGY_ROUTE_UNAVAILABLE"
    service.energy.map_version = "synthetic-other"
    assert service.energy.assess(service, "V01", request).reason == "ENERGY_UNAVAILABLE"
    assert not service.energy.assess(service, "V02", request).allowed


def test_synthetic_trip_consumes_polyline_distance_with_loaded_correction_and_elapsed_auxiliary():
    service, request = queued(35)
    service._dispatch_queued_requests()
    for _ in range(120):
        service.advance(.1)
    assert request.status == RequestStatus.COMPLETED
    # Actual mission: empty 4m + loaded 4m*1.5, and 12s auxiliary (8 travel + 4 service).
    assert service.energy.remaining_wh["V01"] == pytest.approx(23.8)
    assert service.vehicle_runtime["V01"].x == pytest.approx(8)
    assert make_snapshot(service, "PC_Operator", None, 1)["vehicles"][0]["batteryWh"] == pytest.approx(23.8)


def test_energy_shortfall_preserves_active_mission_and_position_without_completion():
    service, request = queued()
    service._dispatch_queued_requests()
    service.advance(1)
    runtime = service.vehicle_runtime["V01"]
    before = (runtime.x, runtime.y, runtime.route_id, request.status)
    service.energy.remaining_wh["V01"] = 15
    service.advance(10)
    assert (runtime.x, runtime.y, runtime.route_id, request.status) == before
    assert runtime.speed_mps == 0 and runtime.safety_reason == "VEHICLE_FAILURE"
    assert request.eta_s is None and not service.vehicles["V01"].available
    assert service.energy.remaining_wh["V01"] == 14  # Auxiliary continues during a hold.


def test_localized_energy_stop_keeps_sensor_priority_and_does_not_overwrite_measured_speed():
    service = ready_service()
    service.energy = EnergyFleet(map_version=service.graph.map_version, policies={"V01": policy()},
                                initial_wh={"V01": 15}, charger_nodes={"n1"})
    service.vehicle_runtime["V01"].speed_mps = .4
    command = make_control_command(service, "V01")
    assert command["targetSpeedMps"] == 0 and command["reason"] == "VEHICLE_FAILURE"
    assert service.vehicle_runtime["V01"].speed_mps == .4
    bad = service.sensor_observations[("V01", "lidar")].model_copy(update={"valid": False, "observed_tick": 2})
    service.record_sensor_observation(bad)
    assert make_control_command(service, "V01")["reason"] == "SENSOR_INVALID"


def test_ego_model_debits_each_accepted_displacement_once_and_faults_on_session_jump():
    service, _ = queued()
    pose = EgoLocalization(vehicle_id="V01", session_id="energy", observed_tick=1,
        map_version=service.graph.map_version, position=MapPosition(x=0,y=0,z=0), heading_rad=math.pi/2, speed_mps=0)
    service.record_ego_localization(pose)
    service.simulation_time_s = .2
    second = pose.model_copy(update={"observed_tick": 2, "position": MapPosition(x=1,y=0,z=0)})
    assert service.record_ego_localization(second)
    assert service.energy.remaining_wh["V01"] == 99
    assert not service.record_ego_localization(second)
    assert service.energy.remaining_wh["V01"] == 99
    changed = second.model_copy(update={"session_id": "new", "observed_tick": 0})
    service.record_ego_localization(changed)
    assert "V01" in service.energy.faults
    assert make_snapshot(service, "PC_Operator", None, 1)["vehicles"][0]["batteryWh"] is None


@pytest.mark.parametrize("updates", [{"reserve_fraction": .1}, {"wh_per_m": -1}, {"auxiliary_w": math.inf}])
def test_unverified_or_invalid_policy_values_do_not_become_valid_energy(updates):
    with pytest.raises(ValueError):
        policy(**updates)



def test_explicit_app_opt_in_validates_map_and_preserves_existing_model():
    from campus_sim.api import create_app

    config = ROOT / "configs/energy.synthetic.json"
    app = create_app(energy_config_path=config)
    assert app.state.service.energy.remaining_wh == {"V01": 600, "V02": 600, "V03": 600}
    with pytest.raises(ValueError, match="already configured"):
        create_app(app.state.service, energy_config_path=config)
    with pytest.raises(ValueError, match="mismatch"):
        create_app(map_path=ROOT / "maps/fixtures/physics-integration-3.json", energy_config_path=config)
    assert create_app().state.service.energy is None


def test_energy_infeasible_is_not_mislabeled_as_an_expensive_route_in_evaluation():
    from campus_sim.dispatch_route_evaluation import route_dispatch_snapshot

    service, _ = queued(34)
    rows, _ = route_dispatch_snapshot(service)
    assert rows[0]["cost_s"] is None
    assert rows[0]["reason"] == "ENERGY_INSUFFICIENT"
    assert rows[0]["energy"]["required_wh"] == pytest.approx(35)


def test_multiple_chargers_choose_feasible_return_without_teleporting_to_it():
    service, request = queued()
    service.energy.charger_nodes = frozenset({"n1", "n3"})
    result = service.energy.assess(service, "V01", request)
    assert result.charger_node == "n3" and result.required_wh == pytest.approx(26.2)
    assert service.vehicle_runtime["V01"].node_id == "n1"


def test_repeated_assignment_uses_consumed_balance_not_initial_capacity():
    service, request = queued(35)
    service._dispatch_queued_requests()
    for _ in range(120):
        service.advance(.1)
    assert request.status == RequestStatus.COMPLETED
    second = service.create_request(CreateRequest(command_id="second", owner_id="energy", service_type="PASSENGER",
        pickup_landmark_id="fixture_landmark_2", dropoff_landmark_id="fixture_landmark_3"))
    assert second.request.status == RequestStatus.QUEUED
    assert service.energy.remaining_wh["V01"] == pytest.approx(23.8)


@pytest.mark.parametrize("algorithm", ["greedy", "hungarian"])
def test_both_algorithms_cannot_assign_below_reserve(algorithm):
    from dataclasses import replace

    service, request = queued(34.99)
    service.dispatch_priority_policy = replace(service.dispatch_priority_policy, assignment_algorithm=algorithm)
    service._dispatch_queued_requests()
    assert request.status == RequestStatus.QUEUED


def test_curved_route_meter_counts_segments_not_endpoint_chord():
    from types import SimpleNamespace

    runtime = SimpleNamespace(route_points=[(0, 0), (3, 0), (3, 4)], route_speeds=[1, 1],
                              route_edge_ids=["a", "b"], x=0, y=0, speed_mps=0)
    distances = []
    service, _ = queued()
    service._move_along_route(runtime, 7, distances.append)
    assert sum(distances) == 7  # The endpoint chord would be only 5m.


def test_charger_closure_mid_service_stops_without_changing_request_or_pose():
    service, request = queued()
    service._dispatch_queued_requests()
    service.advance(1)
    runtime = service.vehicle_runtime["V01"]
    before = (runtime.x, runtime.y, request.status)
    service.graph.edges[:] = [e for e in service.graph.edges if e.id != "e21"]
    service.route_duration_cache.clear()
    service.advance(.1)
    assert (runtime.x, runtime.y, request.status) == before
    assert runtime.safety_reason == "VEHICLE_FAILURE"



@pytest.mark.parametrize("algorithm", ["greedy", "hungarian"])
def test_energy_filter_applies_to_actual_multi_vehicle_batch(algorithm):
    from dataclasses import replace

    from test_dispatch_route_evaluation import MAP, cases

    from campus_sim.dispatch_route_evaluation import build_dispatch_case

    service = build_dispatch_case(MAP, cases()[0])
    service.energy = EnergyFleet.from_config(ROOT / "configs/energy.synthetic.json")
    service.energy.remaining_wh["V01"] = 100  # Below the explicit 150Wh reserve.
    service.dispatch_priority_policy = replace(service.dispatch_priority_policy, assignment_algorithm=algorithm)
    service._dispatch_queued_requests()
    assignments = [r.vehicle_id for r in service.requests.values() if r.status == RequestStatus.ASSIGNED]
    assert set(assignments) == {"V02", "V03"}
    assert service.vehicles["V01"].available
    assert sum(r.status == RequestStatus.QUEUED for r in service.requests.values()) == 1


def test_health_declares_model_and_pc_snapshot_exposes_estimate_without_changing_contract():
    from fastapi.testclient import TestClient

    from campus_sim.api import create_app

    app = create_app(energy_config_path=ROOT / "configs/energy.synthetic.json")
    with TestClient(app) as client:
        assert client.get("/health").json()["energy_model_status"] == "SYNTHETIC_MODEL"
        snapshot = make_snapshot(app.state.service, "PC_Operator", None, 1)
        assert all(590 < v["batteryWh"] <= 600 for v in snapshot["vehicles"])



def test_known_controller_speed_cap_is_included_in_auxiliary_travel_estimate():
    from dataclasses import replace

    from test_controller import POLICY

    service, request = queued()
    service.control_policies["V01"] = replace(POLICY, max_speed_mps=.5)
    # 16m total road travel at .5m/s instead of 1m/s adds 16s * .1Wh/s.
    assert service.energy.assess(service, "V01", request).required_wh == pytest.approx(36.6)
