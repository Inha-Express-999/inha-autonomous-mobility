from pathlib import Path

import pytest

from campus_sim.charging import ChargingStation, ChargingStations
from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.energy import EnergyFleet, EnergyPolicy
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]


def setup(balance=20, auto=None, fleet=False):
    service = (MobilityService.synthetic_fleet_fixture() if fleet else
               MobilityService.from_synthetic_graph(ROOT / "maps/fixtures/physics-integration-3.json"))
    policy = EnergyPolicy(capacity_wh=100, wh_per_m=.01, auxiliary_w=360,
        passenger_factor_each=.1, cargo_factor_per_kg=.01, max_speed_mps=5,
        provenance="Synthetic test assumptions")
    service.energy = EnergyFleet(map_version=service.graph.map_version,
        policies={v: policy for v in service.vehicles},
        initial_wh={v: balance for v in service.vehicles}, charger_nodes={"n1"})
    service.charging = ChargingStations(map_version=service.graph.map_version,
        stations=[ChargingStation("dock", "n1", 3600, 1, .8, .1, .3, .3, .5, "synthetic test")],
        auto_start_fraction=auto)
    node = next(n for n in service.graph.nodes if n.id == "n1")
    for runtime in service.vehicle_runtime.values():
        runtime.x, runtime.y = node.position_m.x, node.position_m.y
        runtime.node_id = "n1"
    return service


def tick(service, dt=.1):
    service.simulation_time_s = round(service.simulation_time_s + dt, 9)
    service.charging.tick(service)


def test_time_based_charge_does_not_teleport_or_credit_initial_or_duplicate_tick():
    s = setup()
    c = s.charging
    before = (s.vehicle_runtime["V01"].x, s.vehicle_runtime["V01"].y)
    c.request(s, "V01", "dock")
    tick(s)
    assert s.energy.remaining_wh["V01"] == 20
    tick(s, .2)
    assert s.energy.remaining_wh["V01"] == pytest.approx(20.2)
    c.tick(s)
    assert s.energy.remaining_wh["V01"] == pytest.approx(20.2)
    tick(s, 2)
    assert s.energy.remaining_wh["V01"] == pytest.approx(20.2)
    tick(s)
    assert s.energy.remaining_wh["V01"] == pytest.approx(20.3)
    assert before == (s.vehicle_runtime["V01"].x, s.vehicle_runtime["V01"].y)
    assert s.vehicle_runtime["V01"].route_id is None


def test_fifo_only_owner_receives_power_and_occupied_dock_cannot_be_cancelled():
    s = setup(fleet=True)
    c = s.charging
    c.request(s, "V02", "dock")
    tick(s)
    c.request(s, "V01", "dock")
    c.request(s, "V03", "dock")
    age = c.visits["V02"].queued_s
    c.request(s, "V02", "dock")
    tick(s)
    assert c.visits["V02"].queued_s == age
    assert c.owners == {"dock": "V02"}
    assert s.energy.remaining_wh["V02"] > 20
    assert s.energy.remaining_wh["V01"] == s.energy.remaining_wh["V03"] == 20
    with pytest.raises(ValueError, match="confirmed exit"):
        c.cancel(s, "V02")
    c.cancel(s, "V03")
    assert s.vehicles["V03"].available


def test_completion_survives_context_pause_without_restarting_charge():
    s = setup(balance=79.9)
    c = s.charging
    c.request(s, "V01", "dock")
    tick(s)
    tick(s)
    visit = c.visits["V01"]
    assert visit.completed and s.vehicles["V01"].available
    original = s.energy.map_version
    s.energy.map_version = "synthetic-wrong"
    tick(s)
    assert visit.state == "PAUSED_CONTEXT" and not s.vehicles["V01"].available
    s.energy.map_version = original
    s.energy.remaining_wh["V01"] = 79
    tick(s)
    tick(s)
    assert visit.state == "COMPLETE" and s.vehicles["V01"].available
    assert s.energy.remaining_wh["V01"] == 79
    assert c.owners == {"dock": "V01"}


def test_queued_visits_resume_after_context_recovery():
    s = setup(fleet=True)
    c = s.charging
    c.request(s, "V02", "dock")
    original = s.energy.map_version
    s.energy.map_version = "synthetic-wrong"
    tick(s)
    s.energy.map_version = original
    tick(s)
    assert c.owners == {"dock": "V02"}
    assert c.visits["V02"].state == "CHARGING"


def test_exit_requires_continuous_fresh_samples_and_does_not_release_during_gap():
    s = setup(balance=80, fleet=True)
    c = s.charging
    c.request(s, "V01", "dock")
    tick(s)
    c.request(s, "V02", "dock")
    s.vehicle_runtime["V01"].x += 1
    tick(s)
    tick(s, 3)
    assert c.owners == {"dock": "V01"}
    for _ in range(4):
        tick(s)
    assert "V01" not in c.visits
    tick(s)
    assert c.owners == {"dock": "V02"}


def test_session_replacement_retains_slot_and_stops_power():
    s = setup()
    c = s.charging
    c.request(s, "V01", "dock")
    tick(s)
    s.run_id = "replacement"
    tick(s)
    assert c.owners == {"dock": "V01"}
    assert c.visits["V01"].state == "PAUSED_CONTEXT"
    assert s.energy.remaining_wh["V01"] == 20


def test_service_charges_net_power_then_dispatches_and_releases_after_departure():
    s = setup(balance=15, auto=.2)
    ack = s.create_request(CreateRequest(command_id="charge-trip", owner_id="test", service_type="PASSENGER",
        pickup_landmark_id="fixture_landmark_2", dropoff_landmark_id="fixture_landmark_3"))
    request = s.requests[ack.request.id]
    assert request.status == RequestStatus.QUEUED
    s.advance(.1)
    assert s.vehicle_runtime["V01"].mission_state == "CHARGING"
    s.advance(.1)
    assert s.energy.remaining_wh["V01"] == pytest.approx(15.08)
    for _ in range(1000):
        s.advance(.1)
    assert request.status == RequestStatus.COMPLETED
    assert "V01" not in s.charging.visits
    assert s.vehicle_runtime["V01"].x == pytest.approx(8)
    assert any(e["state"] == "COMPLETE" for e in s.charging.events)
    assert any(e["state"] == "RELEASED" for e in s.charging.events)


def test_moving_or_off_dock_vehicle_receives_no_power():
    s = setup()
    c = s.charging
    s.vehicle_runtime["V01"].x = 2
    c.request(s, "V01", "dock")
    tick(s)
    tick(s)
    assert s.energy.remaining_wh["V01"] == 20
    s.vehicle_runtime["V01"].x = 0
    s.vehicle_runtime["V01"].speed_mps = .2
    tick(s)
    assert s.energy.remaining_wh["V01"] == 20
    s.vehicle_runtime["V01"].speed_mps = 0
    tick(s)
    tick(s)
    assert s.energy.remaining_wh["V01"] == pytest.approx(20.1)


def test_localization_staleness_pauses_without_crediting_unobserved_time():
    from campus_sim.domain import EgoLocalization, MapPosition
    s = setup()
    pose = EgoLocalization(vehicle_id="V01", session_id="charge", observed_tick=1,
        map_version=s.graph.map_version, position=MapPosition(x=0,y=0,z=0), heading_rad=0, speed_mps=0)
    s.record_ego_localization(pose)
    s.charging.request(s, "V01", "dock")
    tick(s)
    tick(s)
    before = s.energy.remaining_wh["V01"]
    tick(s, 2)
    assert s.charging.visits["V01"].state == "PAUSED_CONTEXT"
    assert s.charging.owners == {"dock": "V01"}
    assert s.energy.remaining_wh["V01"] == before
    s.record_ego_localization(pose.model_copy(update={"observed_tick": 2}))
    tick(s)
    assert s.energy.remaining_wh["V01"] == before
    tick(s)
    assert s.energy.remaining_wh["V01"] == pytest.approx(before + .1)


def test_configuration_requires_energy_and_matching_map_and_reports_parked_only():
    from fastapi.testclient import TestClient

    from campus_sim.api import create_app
    config = ROOT / "configs/charging.synthetic.json"
    with pytest.raises(ValueError, match="matching energy"):
        create_app(charging_config_path=config)
    app = create_app(energy_config_path=ROOT / "configs/energy.synthetic.json", charging_config_path=config)
    with TestClient(app) as client:
        assert client.get("/health").json()["charging_status"] == "SYNTHETIC_PARKED_ONLY"
    with pytest.raises(ValueError, match="already configured"):
        create_app(service=app.state.service, charging_config_path=config)
    other = setup()
    other.charging = None
    with pytest.raises(ValueError, match="matching energy"):
        create_app(service=other, charging_config_path=config)


def test_backward_clock_does_not_reuse_credit_or_outside_hold():
    s = setup()
    c = s.charging
    c.request(s, "V01", "dock")
    tick(s)
    tick(s)
    before = s.energy.remaining_wh["V01"]
    s.simulation_time_s = 0
    c.tick(s)
    s.simulation_time_s = .3
    c.tick(s)
    assert s.energy.remaining_wh["V01"] == before
    tick(s)
    assert s.energy.remaining_wh["V01"] == pytest.approx(before + .1)
