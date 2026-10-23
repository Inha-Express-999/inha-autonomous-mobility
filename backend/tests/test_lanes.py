import json
import math

import pytest
from pydantic import ValidationError

from campus_sim.lanes import LaneProfiles, lane_path
from campus_sim.unity_drive import AutoMission, EgoObservation
from campus_sim.web_demo import DemoSession


def lane_ego(session, tick=1, **changes):
    values = dict(schema_version=2, run_id=session.run_id, geometry_hash=session.physical.geometry_hash,
                  tick=tick, x=70.0667, z=-159.6858, heading_rad=math.radians(-60), speed_mps=0,
                  wheelbase_m=1.3, max_steering_rad=math.radians(30), half_width_m=.4,
                  sensor_valid=True, sensor_age_s=0,
                  rays=[dict(range_m=20, bearing_rad=math.radians(a)) for a in (-20, -12, -6, 0, 6, 12, 20)])
    values.update(changes)
    return EgoObservation(**values)


def test_opposing_directions_use_opposite_right_lanes():
    north = lane_path([([(0, 0), (0, 20)], 1.75)], 2.25)
    south = lane_path([([(0, 20), (0, 0)], 1.75)], 2.25)
    assert north == [(1.75, 0), (1.75, 20)]
    assert south == [(-1.75, 20), (-1.75, 0)]


@pytest.mark.parametrize("end", [(20, 20), (-20, 20)])
def test_intersection_connector_has_continuous_tangents_and_turn_radius(end):
    path = lane_path([([(0, 0), (0, 20)], 1.75), ([(0, 20), end], 1.75)], 2.25)
    assert len(path) > 15
    maximum = 0
    for a, b, c in zip(path, path[1:], path[2:]):
        denominator = math.dist(a, b) * math.dist(b, c) * math.dist(a, c)
        if denominator > 1e-6:
            cross = abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]))
            maximum = max(maximum, 2 * cross / denominator)
    assert maximum <= 1 / 2.25
    assert all(math.isfinite(x) for p in path for x in p)


def test_short_corners_and_uturns_are_rejected():
    with pytest.raises(ValueError):
        lane_path([([(0, 0), (0, 2)], 1.75), ([(0, 2), (2, 2)], 1.75)], 2.25)
    with pytest.raises(ValueError):
        lane_path([([(0, 0), (0, 20)], 1.75), ([(0, 20), (0, 0)], 1.75)], 2.25)


def test_authored_width_hash_and_lane_goal_are_explicit_and_legacy_protocol_is_preserved():
    session = DemoSession()
    profile = session.physical.lanes.by_edge[("osm:4741759508", "split:481241692:0:0:0.389486496264", "481241692:0:0:1:r")]
    assert profile["authored_road_width_m"] == 7
    assert profile["lane_width_m"] == 3.5 and profile["right_offset_m"] == 1.75
    assert profile["verification"] == "synthetic_authored_width_not_surveyed"
    path = session.root / "maps/inha_osm_runtime/lane_profiles.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    data["profiles"][0]["right_offset_m"] += 1
    with pytest.raises(ValueError):
        LaneProfiles(data, session.physical.geometry_hash)
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, lane_ego(session))
    receipt = session.physical.mission("V01", AutoMission(
        run_id=session.run_id, command_id="lane-test", stop_id="main_building_road_candidate"))
    centre = session.physical.coordinates.to_unity(*session.service.graph.nodes[session.service.stop_nodes["main_building_road_candidate"]])
    assert math.dist((receipt["goal_unity"]["x"], receipt["goal_unity"]["z"]), centre) == pytest.approx(1.75)
    assert receipt["lane_ids"] and receipt["lane_model"] == "right_hand_authored_simulation"
    command = session.physical.observation("V01", token, lane_ego(session, tick=2))
    assert command["schema_version"] == 2 and command["steering_angle_rad"] > 0
    assert command["lane_profile_hash"] == session.physical.lanes.hash
    outside = session.physical.observation("V01", token, lane_ego(session, tick=3, x=80, z=-150))
    assert outside["emergency_stop"] and outside["status"] == "OFF_ROUTE"
    assert outside["lane_error_m"] > .9


def test_lane_protocol_requires_measured_width_and_owner_cannot_switch_protocol():
    session = DemoSession()
    with pytest.raises(ValidationError):
        lane_ego(session, half_width_m=None)
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, lane_ego(session))
    with pytest.raises(ValueError):
        session.physical.observation("V01", token, lane_ego(session, tick=2, schema_version=1))


def test_unknown_road_width_and_oversize_vehicle_are_excluded_instead_of_penalized():
    session = DemoSession()
    profiles = session.physical.lanes
    missing = [e for e in session.service.graph.edges if e.id not in profiles.by_edge]
    assert len(missing) == 2 and not any(profiles.feasible(e, .4) for e in missing)
    current = next(e for e in session.service.graph.edges if e.key == "481241692:0:0:1:r")
    assert profiles.feasible(current, .4) and not profiles.feasible(current, 2)
    token = session.physical.connect("V01")
    session.physical.observation("V01", token, lane_ego(session, half_width_m=2))
    with pytest.raises(ValueError):
        session.physical.mission("V01", AutoMission(run_id=session.run_id, command_id="oversize", stop_id="main_building_road_candidate"))
