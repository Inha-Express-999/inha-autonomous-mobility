import math

import pytest
from pydantic import ValidationError

from campus_sim.unity_drive import AutoMission, RayDetection
from campus_sim.web_demo import DemoSession
from test_lanes import lane_ego


def driving():
    session = DemoSession()
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, lane_ego(session))
    session.physical.mission("V01", AutoMission(
        run_id=session.run_id, command_id="speed", stop_id="library_road_candidate"))
    return session, token


def test_straight_cruise_respects_18kmh_and_road_limit_and_curve_preview_slows():
    session, token = driving()
    fast = session.physical.observation("V01", token, lane_ego(session, tick=2))
    assert fast["target_speed_kmh"] == pytest.approx(18)
    vehicle = session.physical.vehicles["V01"]
    a, b = vehicle.route_unity[1:3]
    heading = math.atan2(b[0]-a[0], b[1]-a[1])
    distance = math.dist(a, b)
    point = tuple(a[i] + (b[i]-a[i]) * (distance-5) / distance for i in (0, 1))
    curve = session.physical.observation("V01", token, lane_ego(
        session, tick=3, x=point[0], z=point[1], heading_rad=heading, speed_mps=4))
    assert not curve["emergency_stop"] and 0 < curve["target_speed_kmh"] < 10
    vehicle.road_speed_mps = 1
    limited = session.physical.observation("V01", token, lane_ego(
        session, tick=4, x=point[0], z=point[1], heading_rad=heading))
    assert limited["target_speed_kmh"] <= 3.6


def test_high_speed_obstacle_latch_does_not_release_as_braking_distance_shrinks():
    session, token = driving()
    for tick, speed in [(2, 5), (3, 0), (4, .01)]:
        result = session.physical.observation("V01", token, lane_ego(
            session, tick=tick, speed_mps=speed, rays=[dict(range_m=7, bearing_rad=0)]*7))
        assert result["emergency_stop"]
    clear = session.physical.observation("V01", token, lane_ego(session, tick=5))
    assert not clear["emergency_stop"]


def test_adjacent_lane_sensor_returns_do_not_block_ego_route_but_forward_hits_do():
    session, token = driving()
    v = session.physical.vehicles["V01"]
    a,b=v.route_unity[1:3]
    heading=math.atan2(b[0]-a[0], b[1]-a[1])
    side = [dict(range_m=6, bearing_rad=.3, lateral_m=3, forward_m=6, origin_forward_m=1)]*7
    result = session.physical.observation("V01",token,lane_ego(
        session,tick=2,x=a[0],z=a[1],heading_rad=heading,speed_mps=5,rays=side))
    assert not result["emergency_stop"]
    front = [dict(range_m=6,bearing_rad=0,lateral_m=0,forward_m=7,origin_forward_m=1)]*7
    result = session.physical.observation("V01",token,lane_ego(
        session,tick=3,x=a[0],z=a[1],heading_rad=heading,speed_mps=5,rays=front))
    assert result["emergency_stop"]
    with pytest.raises(ValidationError):
        RayDetection(range_m=6,bearing_rad=0,lateral_m=0)
