import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from campus_sim.physical_lidar import evaluate_lidar
from campus_sim.unity_drive import EgoObservation, LidarHit
from pydantic import ValidationError


def vehicle(route):
    return SimpleNamespace(route_unity=route, progress_m=0., turn=None,
                           lane_width_m=3.5, lidar_environment=None, lidar_blocked=False,
                           lidar_clear_frames=0, lidar_rrt_at_s=0., lidar_rrt_status="NOT_REQUESTED")


def observed(hits=(), speed=3., **changes):
    data = dict(schema_version=5, run_id="test", geometry_hash="0" * 64, tick=1,
                x=0., z=0., heading_rad=0., speed_mps=speed, wheelbase_m=1.3,
                max_steering_rad=.52, half_width_m=.46, half_length_m=1.55,
                rear_axle_offset_m=-.65, sensor_valid=True, sensor_age_s=0.,
                obstacle_detection_enabled=True, rays=[],
                lidar=dict(tick=1, ray_count=2520, max_range_m=15., valid=True, hits=list(hits)))
    data.update(changes)
    return EgoObservation(**data)


def hit(x, z, kind="unknown"):
    return dict(lateral_m=x, forward_m=z, entity_class=kind)


@pytest.mark.parametrize("name", ["v01", "v03", "v03_measured_body", "v03_static_facade"])
def test_recorded_campus_off_route_returns_do_not_brake(name):
    filename = f"{name}_lidar.json" if name in {"v03_measured_body", "v03_static_facade"} else f"{name}_off_corridor_lidar.json"
    data = json.loads((Path(__file__).parent / "fixtures" / filename).read_text())
    ego = EgoObservation(**data["ego"])
    v = vehicle([(p["x"], p["z"]) for p in data["route"]])
    v.progress_m = data.get("progress_m", 0.)
    decision = evaluate_lidar(name, v, ego, 0.)
    assert not decision.stop and decision.speed_cap_mps > 0
    # A real intrusion immediately ahead must still override the curved route.
    intruder = ego.lidar.model_copy(update={"hits": [*ego.lidar.hits,
        LidarHit(**hit(0., 1.2, "pedestrian"))]})
    assert evaluate_lidar(name, v, ego.model_copy(update={"lidar": intruder}), 0.).stop


def test_unclassified_actor_keeps_larger_clearance_than_fixed_map_facade():
    data = json.loads((Path(__file__).parent / "fixtures" / "v03_static_facade_lidar.json").read_text())
    ego = EgoObservation(**data["ego"])
    ego = ego.model_copy(update={"lidar": ego.lidar.model_copy(update={
        "hits": [h.model_copy(update={"entity_class": "unknown"}) for h in ego.lidar.hits]})})
    v = vehicle([(p["x"], p["z"]) for p in data["route"]])
    v.progress_m = data["progress_m"]
    assert evaluate_lidar("unknown", v, ego, 0.).stop


@pytest.mark.parametrize("changes", [dict(body_front_m=.9), dict(body_rear_m=.9),
    dict(body_front_m=2., body_rear_m=.9), dict(body_front_m=.9, body_rear_m=3.),
    dict(body_front_m=-1., body_rear_m=.9)])
def test_partial_or_out_of_envelope_measured_body_is_rejected(changes):
    with pytest.raises(ValidationError):
        observed(**changes)


def test_measured_root_body_preserves_front_and_rear_obstacle_braking():
    for z in (.8, -.8):
        assert evaluate_lidar("test", vehicle([(0., 0.), (0., 15.)]),
            observed([hit(0., z, "static")], body_front_m=.9, body_rear_m=.9), 0.).stop


@pytest.mark.parametrize("kind", ["static", "unknown", "pedestrian", "vehicle"])
def test_curve_obstacle_stops_even_outside_current_forward_strip(kind):
    v = vehicle([(0., 0.), (0., 2.), (-2., 4.), (-10., 4.)])
    assert evaluate_lidar("test", v, observed([hit(-4., 4., kind)]), 0.).stop


def test_tangent_structure_outside_turning_path_is_clear():
    v = vehicle([(0., 0.), (0., 2.), (-2., 4.), (-10., 4.)])
    assert not evaluate_lidar("test", v, observed([hit(0., 10.)], speed=5.), 0.).stop


def test_yielded_pedestrian_must_leave_current_strip_as_well_as_future_curve():
    v = vehicle([(0., 0.), (0., 2.), (-2., 4.), (-10., 4.)])
    v.lidar_blocked = True
    for _ in range(8):
        assert evaluate_lidar("test", v, observed([hit(0., 10., "pedestrian")], speed=0.), 0.).stop
        assert v.lidar_clear_frames == 0


@pytest.mark.parametrize("kind", ["static", "unknown", "pedestrian", "vehicle"])
def test_immediate_body_intrusion_cannot_be_filtered_by_route(kind):
    v = vehicle([(0., 0.), (-10., 0.)])
    assert evaluate_lidar("test", v, observed([hit(.3, .3, kind)]), 0.).stop


def test_current_motion_reaction_envelope_overrides_a_departing_route():
    v = vehicle([(0., 0.), (-10., 0.)])
    assert evaluate_lidar("test", v, observed([hit(0., 2.)], speed=5.), 0.).stop


def test_reverse_rear_obstacle_brakes_without_using_forward_route():
    v = vehicle([(0., 0.), (0., 15.)])
    assert evaluate_lidar("test", v, observed([hit(0., -4.)], speed=-1.), 0.).stop


def test_released_speed_cap_does_not_reenter_ttc_stop_for_unchanged_obstacle():
    v = vehicle([(0., 0.), (0., 15.)])
    assert evaluate_lidar("test", v, observed([hit(0., 6.)], speed=3.), 0.).stop
    for _ in range(4):
        assert evaluate_lidar("test", v, observed([hit(0., 6.)], speed=0.), 0.).stop
    release = evaluate_lidar("test", v, observed([hit(0., 6.)], speed=0.), 0.)
    assert not release.stop and 0 < release.speed_cap_mps < 3
    assert not evaluate_lidar("test", v, observed([hit(0., 6.)], speed=release.speed_cap_mps), 0.).stop


def test_actual_turn_arc_checks_swept_body_instead_of_full_circle():
    from campus_sim.turnaround import TurnController
    from campus_sim.physical_turn import PhysicalTurn
    controller = TurnController(wheelbase_m=1.3, max_steering_rad=.6, lane_offset_m=1.5,
        road_half_width_m=4., forward_space_m=6., rear_space_m=4., minimum_radius_m=2.3,
        half_width_m=.46, half_length_m=1.55)
    v = vehicle([(1.5, 0.), (1.5, 15.)])
    v.turn = PhysicalTurn(controller, (0., 0.), 0.)
    ego = observed(x=1.5, z=.65, speed=.55)
    # Right-hand structure is outside the left-turn body path, but inside old circle.
    assert not evaluate_lidar("test", v, ego.model_copy(update={
        "lidar": ego.lidar.model_copy(update={"hits": [LidarHit(**hit(2., 0.))]})}), 0.).stop
    # Front/rear body returns remain authoritative while manoeuvring.
    danger = observed([hit(0., -2., "vehicle")], x=1.5, z=.65, speed=-.5)
    assert evaluate_lidar("test", v, danger, 0.).stop


@pytest.mark.parametrize("changes", [dict(steering_angle_rad=0.), dict(steering_rate_rad_s=1.),
    dict(steering_angle_rad=.8, steering_rate_rad_s=1.),
    dict(steering_angle_rad=0., steering_rate_rad_s=-1.)])
def test_incomplete_or_impossible_measured_steering_is_rejected(changes):
    with pytest.raises(ValidationError):
        observed(**changes)


def test_measured_steering_forecast_follows_straight_ego_motion():
    from campus_sim.lidar_corridor import motion_sweep
    ego = observed(steering_angle_rad=0., steering_rate_rad_s=2.)
    poses = motion_sweep(vehicle([(0., 0.), (0., 20.)]), ego, 10.)
    assert poses[-1][0] == pytest.approx(0.)
    assert poses[-1][1] == pytest.approx(10.)
    assert all(p[2] == pytest.approx(0.) for p in poses)


@pytest.mark.parametrize("kind", ["static", "unknown", "pedestrian", "vehicle"])
def test_measured_steering_forecast_brakes_for_obstacle_on_actual_turn(kind):
    from campus_sim.lidar_corridor import motion_sweep
    v = vehicle([(0., 0.), (0., 2.), (-2., 4.), (-10., 4.)])
    ego = observed(steering_angle_rad=0., steering_rate_rad_s=2., body_front_m=.9, body_rear_m=.9)
    poses = motion_sweep(v, ego, 10.)
    # Pick a future body centre, outside the immediate reaction strip.
    x, z = next(p[:2] for p in poses if p[4] >= 4.)
    danger = ego.model_copy(update={"lidar": ego.lidar.model_copy(update={"hits": [LidarHit(**hit(x, z, kind))]})})
    assert evaluate_lidar("test", v, danger, 0.).stop
    # Each forecast step advances the vehicle rather than rotating in place.
    assert all(b[4] > a[4] and ((b[0]-a[0])**2 + (b[1]-a[1])**2) > 0
               for a, b in zip(poses, poses[1:]))


def test_measured_steering_forecast_excludes_structure_past_current_tangent():
    v = vehicle([(0., 0.), (0., 2.), (-2., 4.), (-10., 4.)])
    ego = observed([hit(0., 10., "static")], speed=5.,
        steering_angle_rad=0., steering_rate_rad_s=2., body_front_m=.9, body_rear_m=.9)
    assert not evaluate_lidar("test", v, ego, 0.).stop


def test_recorded_stopped_corner_releases_only_after_five_safe_scans_at_safe_speed():
    data = json.loads((Path(__file__).parent / "fixtures" / "v03_restart_lidar.json").read_text())
    ego = EgoObservation(**data["ego"])
    v = vehicle([(p["x"], p["z"]) for p in data["route"]])
    v.progress_m, v.lidar_blocked = data["progress_m"], True
    for _ in range(4):
        assert evaluate_lidar("V03", v, ego, 0.).stop
    release = evaluate_lidar("V03", v, ego, 0.)
    assert not release.stop and 0 < release.speed_cap_mps < .5
    moving = ego.model_copy(update={"speed_mps": release.speed_cap_mps})
    assert not evaluate_lidar("V03", v, moving, 0.).stop
    # A genuine immediate intrusion must reset the release count.
    intrusion = ego.model_copy(update={"lidar": ego.lidar.model_copy(update={
        "hits": [LidarHit(**hit(0., 1., "pedestrian"))]})})
    assert evaluate_lidar("V03", v, intrusion, 0.).stop
    assert v.lidar_clear_frames == 0


def test_unknown_turn_retains_circumscribed_body_corner_protection():
    v = vehicle([(0., 0.), (0., 15.)])
    v.turn = object()  # No executable arc available: conservative circle fallback.
    ego = observed([hit(.9, 1.4, "static")], speed=0., body_front_m=.9, body_rear_m=.9)
    assert evaluate_lidar("test", v, ego, 0.).stop
