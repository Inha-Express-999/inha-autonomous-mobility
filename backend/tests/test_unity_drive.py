import math

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from campus_sim.api import create_app
from campus_sim.map_coordinates import MapCoordinates
from campus_sim.unity_drive import AutoMission, EgoObservation
from campus_sim.web_demo import DemoSession, DemoControl


def observation(session, tick=1, **changes):
    # Existing CampusTerrain V01 initial road position (Unity X/Z), not a road-access assertion.
    value = dict(schema_version=1, run_id=session.run_id,
                 geometry_hash=session.physical.geometry_hash, tick=tick,
                 x=70.07, z=-159.69, heading_rad=math.radians(-60), speed_mps=0,
                 wheelbase_m=1.3, max_steering_rad=math.radians(30),
                 sensor_valid=True, sensor_age_s=0,
                 rays=[dict(range_m=20, bearing_rad=math.radians(angle))
                       for angle in (-20, -12, -6, 0, 6, 12, 20)])
    value.update(changes)
    return EgoObservation(**value)


def start(session):
    token = session.physical.connect("V01")
    parked = session.physical.observation("V01", token, observation(session))
    assert parked["emergency_stop"] and parked["target_speed_kmh"] == 0
    mission = session.physical.mission("V01", AutoMission(
        run_id=session.run_id, command_id="test-auto", stop_id="main_building_road_candidate"))
    return token, mission


def test_automatic_route_uses_current_pose_and_steering_then_stops_at_actual_goal():
    session = DemoSession()
    token, mission = start(session)
    command = session.physical.observation("V01", token, observation(session, tick=2))
    assert not command["emergency_stop"]
    assert 0 < command["target_speed_kmh"] <= .8 * 3.6
    assert command["steering_angle_rad"] < 0  # correct towards the adjacent road centreline
    assert mission["motion_source"] == "unity_physics"
    assert not session.service.vehicles["V01"].available
    goal = session.physical.vehicles["V01"].route_unity[-1]
    arrived = session.physical.observation("V01", token, observation(
        session, tick=3, x=goal[0], z=goal[1]))
    assert arrived["status"] == "ARRIVED" and arrived["emergency_stop"]


@pytest.mark.parametrize("changes", [
    {"sensor_valid": False}, {"sensor_age_s": .3},
    {"rays": [dict(range_m=.2, bearing_rad=0)] * 7},
])
def test_invalid_stale_or_obstructed_sensor_observations_brake(changes):
    session = DemoSession()
    token, _ = start(session)
    result = session.physical.observation("V01", token, observation(session, tick=2, **changes))
    assert result["emergency_stop"] and result["target_speed_kmh"] == 0


def test_owner_sequence_run_map_and_offroad_validation():
    session = DemoSession()
    token, _ = start(session)
    with pytest.raises(ValueError):
        session.physical.connect("V01")
    for invalid in [observation(session), observation(session, tick=2, run_id="wrong"),
                    observation(session, tick=2, geometry_hash="0" * 64)]:
        with pytest.raises(ValueError):
            session.physical.observation("V01", token, invalid)
    result = session.physical.observation("V01", token, observation(session, tick=2, x=1000))
    assert result["emergency_stop"] and result["status"] == "OFF_ROUTE"
    session.physical.disconnect("V01", token)
    assert session.physical.describe("V01")["status"] == "DISCONNECTED"
    assert not session.service.vehicles["V01"].available


def test_obstacle_brake_hysteresis_prevents_low_speed_creep_and_resumes_when_clear():
    session = DemoSession()
    token, _ = start(session)
    for tick, clearance in [(2, .2), (3, .7)]:
        result = session.physical.observation("V01", token, observation(
            session, tick=tick, rays=[dict(range_m=clearance, bearing_rad=0)] * 7))
        assert result["emergency_stop"] and result["target_speed_kmh"] == 0
    result = session.physical.observation("V01", token, observation(session, tick=4))
    assert not result["emergency_stop"] and result["target_speed_kmh"] > 0


def test_dynamic_ground_truth_and_nonfinite_values_are_rejected():
    session = DemoSession()
    with pytest.raises(ValidationError):
        observation(session, other_vehicle_positions=[[0, 0]])
    with pytest.raises(ValidationError):
        observation(session, x=float("nan"))


def test_reset_invalidates_old_owner_even_if_it_sends_the_new_run_id():
    session = DemoSession()
    token, _ = start(session)
    previous = session.physical
    session.reset()
    with pytest.raises(ValueError):
        previous.observation("V01", token, observation(session, tick=2))


def test_physics_pause_speed_and_wall_clock_freshness_are_independent_of_demo_time():
    session = DemoSession()
    now = [10.]
    session.physical.clock = lambda: now[0]
    token, _ = start(session)
    session.control(DemoControl(paused=True, speed=100))
    paused = session.physical.observation("V01", token, observation(session, tick=2))
    assert paused["emergency_stop"] and session.clock.speed == 1
    session.control(DemoControl(paused=False))
    resumed = session.physical.observation("V01", token, observation(session, tick=3))
    assert not resumed["emergency_stop"]
    now[0] += 1
    assert session.physical.describe("V01")["status"] == "DISCONNECTED"
    with pytest.raises(ValueError, match="Fresh Unity"):
        session.physical.mission("V01", AutoMission(run_id=session.run_id, command_id="new", stop_id="main_gate_road_candidate"))


def test_same_session_websocket_to_mission_to_web_physics_pose():
    app = create_app()
    with TestClient(app) as client:
        client.get("/v1/demo/state")
        session = app.state.demo
        with client.websocket_connect("/v1/demo/unity/vehicles/V01/control") as socket:
            ego = observation(session)
            socket.send_json(ego.model_dump())
            assert socket.receive_json()["emergency_stop"]
            result = client.post("/v1/demo/unity/vehicles/V01/mission", json=dict(
                run_id=session.run_id, command_id="http-auto", stop_id="main_building_road_candidate"))
            assert result.status_code == 200
            socket.send_json(observation(session, tick=2).model_dump())
            command = socket.receive_json()
            assert command["run_id"] == session.run_id and command["target_speed_kmh"] > 0
            state = client.get("/v1/demo/state").json()
            vehicle = next(v for v in state["vehicles"] if v["id"] == "V01")
            assert vehicle["position"] == pytest.approx(MapCoordinates().from_unity(ego.x, ego.z))
            assert vehicle["motion_source"] == "unity_physics" and not vehicle["trajectory"]
            assert state["run_id"] == session.run_id
            assert client.post("/v1/demo/unity/vehicles/V01/stop").status_code == 200
            socket.send_json(observation(session, tick=3).model_dump())
            assert socket.receive_json()["emergency_stop"]
