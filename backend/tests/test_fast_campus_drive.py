import math

import pytest
from pydantic import ValidationError

from campus_sim.unity_drive import AutoMission, DriveCommand
from campus_sim.web_demo import DemoSession
from test_lanes import lane_ego


def fast_vehicle(speed=40):
    session = DemoSession()
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, lane_ego(session, schema_version=3))
    session.physical.mission("V01", AutoMission(
        run_id=session.run_id, command_id="fast", stop_id="library_road_candidate",
        driving_profile="campus_fast_no_obstacles", cruise_speed_kmh=speed))
    return session, token, session.physical.vehicles["V01"]


@pytest.mark.parametrize("speed", [30, 40, 50])
def test_requested_cruise_can_reach_30_to_50_without_sensor_braking(speed):
    session, token, vehicle = fast_vehicle(speed)
    vehicle.route_unity = [(0, 0), (0, 500)]
    vehicle.road_speed_mps = 5  # Synthetic profile explicitly overrides standard road limit.
    result = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=0, z=0, heading_rad=0,
        speed_mps=speed / 3.6, rays=[dict(range_m=.1, bearing_rad=0)] * 7))
    assert not result["emergency_stop"]
    assert result["target_speed_kmh"] == pytest.approx(speed)
    DriveCommand(**result)
    assert session.physical.describe("V01")["obstacles_ignored"]


def test_fast_profile_keeps_freshness_lane_and_goal_checks():
    session, token, vehicle = fast_vehicle()
    vehicle.route_unity = [(0, 0), (0, 500)]
    invalid = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=0, z=0, sensor_valid=False))
    assert invalid["emergency_stop"]
    off_lane = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=3, x=10, z=0))
    assert off_lane["status"] == "OFF_ROUTE"
    vehicle.status = "DRIVING"
    arrived = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=4, x=0, z=500, speed_mps=0))
    assert arrived["status"] == "ARRIVED" and arrived["emergency_stop"]


def test_fast_profile_brakes_for_corner_before_steering():
    session, token, vehicle = fast_vehicle(50)
    vehicle.route_unity = [(0, 0), (0, 40), (10, 50), (60, 50)]
    result = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=0, z=30, heading_rad=0, speed_mps=12))
    assert 0 < result["target_speed_kmh"] < 50
    assert math.isfinite(result["steering_angle_rad"])


def test_fast_corner_limit_increases_without_removing_curvature_bound():
    session, token, vehicle = fast_vehicle(40)
    vehicle.route_unity = [(0, 0), (500, 0)]
    result = session.physical.observation('V01', token, lane_ego(session,
        schema_version=3,tick=2,x=10,z=0,heading_rad=math.pi/3,speed_mps=2))
    curvature=abs(math.tan(result['steering_angle_rad']))/vehicle.ego.wheelbase_m
    old_limit=3.6*math.sqrt(session.physical.lateral_acceleration_mps2/curvature)
    assert result['target_speed_kmh']>old_limit*1.3
    assert result['target_speed_kmh'] == pytest.approx(3.6*math.sqrt(3.6/curvature))
    assert result['target_speed_kmh']<40


def test_corner_exit_keeps_speed_low_until_vehicle_heading_aligns():
    session, token, vehicle = fast_vehicle(50)
    # The geometric bend is already behind the localization point, but the body
    # is still rotating onto the outgoing straight. Do not jump to cruise yet.
    vehicle.route_unity = [(0, 0), (500, 0)]
    result = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=10, z=0,
        heading_rad=math.pi / 3, speed_mps=2))
    assert not result["emergency_stop"]
    assert 0 < result["target_speed_kmh"] < 20
    aligned = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=3, x=11, z=0,
        heading_rad=math.pi / 2, speed_mps=2))
    assert aligned["target_speed_kmh"] == pytest.approx(50)


def test_revisited_intersection_does_not_skip_untravelled_route():
    session, token, vehicle = fast_vehicle(50)
    vehicle.route_unity = [(0, 0), (0, 20), (10, 20), (10, 0), (.1, 0), (.1, 20)]
    vehicle.progress_m = 4
    vehicle.ego = vehicle.ego.model_copy(update={"x": 0., "z": 4.})
    result = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=.08, z=5,
        heading_rad=0, speed_mps=2))
    assert not result["emergency_stop"]
    assert vehicle.progress_m == pytest.approx(5)
    assert result["lane_error_m"] == pytest.approx(.08)


def test_old_clients_cannot_request_fast_profile_and_speed_is_bounded():
    session = DemoSession()
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, lane_ego(session))
    with pytest.raises(ValueError, match="protocol 3"):
        session.physical.mission("V01", AutoMission(
            run_id=session.run_id, command_id="old", stop_id="library_road_candidate",
            driving_profile="campus_fast_no_obstacles"))
    with pytest.raises(ValidationError):
        AutoMission(run_id="x", command_id="x", stop_id="x", cruise_speed_kmh=51)


def test_shared_dashboard_exposes_measured_speed_separately_from_cruise():
    session, token, vehicle = fast_vehicle(50)
    vehicle.route_unity = [(0, 0), (0, 500)]
    session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=0, z=0, speed_mps=10))
    dashboard = next(v for v in session.state()["vehicles"] if v["id"] == "V01")
    assert dashboard["measured_speed_kmh"] == pytest.approx(36)
    assert dashboard["cruise_speed_kmh"] == pytest.approx(50)
    assert dashboard["obstacles_ignored"] and dashboard["observation_fresh"]
    session.physical.disconnect("V01", token)
    dashboard = next(v for v in session.state()["vehicles"] if v["id"] == "V01")
    assert dashboard["measured_speed_kmh"] is None
    assert not dashboard["observation_fresh"]


def test_path_only_mode_accepts_no_obstacle_observations():
    session, token, vehicle = fast_vehicle(40)
    vehicle.route_unity = [(0, 0), (0, 500)]
    result = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=2, x=0, z=0, heading_rad=0,
        obstacle_detection_enabled=False, rays=[]))
    assert not result["emergency_stop"]
    assert result["target_speed_kmh"] == pytest.approx(40)
    vehicle.driving_profile = "standard"
    result = session.physical.observation("V01", token, lane_ego(
        session, schema_version=3, tick=3, x=0, z=0,
        obstacle_detection_enabled=False, rays=[]))
    assert result["emergency_stop"]
    with pytest.raises(ValidationError):
        lane_ego(session, schema_version=3, obstacle_detection_enabled=True, rays=[])


def test_depart_library_toward_hitech_without_immediate_u_turn():
    # Recorded Unity arrival pose. Node-only A* selected a reverse edge here,
    # which failed lane construction despite an available forward circuit.
    session = DemoSession()
    token = session.physical.connect("V01")
    ego = lane_ego(session, schema_version=3, x=-48.70259857, z=-165.94950867,
                   heading_rad=-2.63855958, speed_mps=0,
                   obstacle_detection_enabled=False, rays=[])
    session.physical.observation("V01", token, ego)
    receipt = session.physical.mission("V01", AutoMission(
        run_id=session.run_id, command_id="continue-from-library",
        stop_id="hitech_road_candidate", driving_profile="campus_fast_no_obstacles"))
    route = session.physical.vehicles["V01"].route_unity
    assert receipt["status"] == "DRIVING" and receipt["destination"] == "hitech"
    assert route[0] == (ego.x, ego.z)
    forward = (math.sin(ego.heading_rad), math.cos(ego.heading_rad))
    target = next(p for p in route[1:] if math.dist(p, route[0]) > 1)
    assert sum((target[i] - route[0][i]) * forward[i] for i in (0, 1)) > 0
