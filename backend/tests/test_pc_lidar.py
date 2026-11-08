import math
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient
from jsonschema import Draft202012Validator
from pydantic import ValidationError

from campus_sim.api import create_app
from campus_sim.unity_drive import AutoMission, EgoObservation, DriveCommand
from campus_sim.web_demo import DemoSession
from campus_sim.physical_lidar import evaluate_lidar, rrt_candidate


def observed(session, tick=1, hits=(), **changes):
    value = dict(schema_version=5,run_id=session.run_id,geometry_hash=session.physical.geometry_hash,
        tick=tick,x=70.07,z=-159.69,heading_rad=math.radians(-60),speed_mps=0,
        wheelbase_m=1.3,max_steering_rad=.52,half_width_m=.46,rear_axle_offset_m=-.65,
        half_length_m=1.55,sensor_valid=True,sensor_age_s=0,obstacle_detection_enabled=True,
        rays=[],lidar=dict(tick=tick,ray_count=2520,max_range_m=15,valid=True,hits=list(hits)))
    value.update(changes)
    return EgoObservation(**value)


def hit(x=0,y=2,kind="unknown"):
    return dict(lateral_m=x,forward_m=y,entity_class=kind)


def drive_session():
    session=DemoSession()
    token=session.physical.connect("V01")
    session.physical.observation("V01",token,observed(session))
    session.physical.mission("V01",AutoMission(run_id=session.run_id,command_id="lidar",stop_id="main_building_road_candidate",
        driving_profile="campus_lidar",cruise_speed_kmh=18))
    return session,token


@pytest.mark.parametrize("kind",["unknown","static","pedestrian","vehicle"])
def test_actual_control_brakes_for_each_observed_class_and_recovers_after_clear_scans(kind):
    s,t=drive_session()
    result=s.physical.observation("V01",t,observed(s,2,[hit(kind=kind)],speed_mps=3))
    assert result["emergency_stop"] and result["target_speed_kmh"]==0
    assert "LiDAR" in result["reason"]
    for tick in range(3,7):
        assert s.physical.observation("V01",t,observed(s,tick))["emergency_stop"]
    result=s.physical.observation("V01",t,observed(s,7))
    assert not result["emergency_stop"] and 0<result["target_speed_kmh"]<=18
    assert not s.physical.describe("V01")["obstacles_ignored"]


@pytest.mark.parametrize("changes",[
    dict(sensor_valid=False),dict(sensor_age_s=.3),
    dict(lidar=dict(tick=2,ray_count=2520,max_range_m=15,valid=False,hits=[]))])
def test_invalid_stale_overflowed_scan_never_drives(changes):
    s,t=drive_session()
    result=s.physical.observation("V01",t,observed(s,2,**changes))
    assert result["emergency_stop"] and result["target_speed_kmh"]==0


@pytest.mark.parametrize("changes",[
    dict(lidar=None),dict(obstacle_detection_enabled=False),dict(other_vehicle_positions=[[0,0]]),
    dict(lidar=dict(tick=99,ray_count=2520,max_range_m=15,valid=True,hits=[])),
    dict(lidar=dict(tick=1,ray_count=2519,max_range_m=15,valid=True,hits=[])),
    dict(lidar=dict(tick=1,ray_count=2520,max_range_m=15,valid=True,hits=[hit(y=float("nan"))]))])
def test_missing_mismatched_or_ground_truth_frame_is_rejected(changes):
    with pytest.raises(ValidationError): observed(DemoSession(),**changes)


def test_protocol5_schema_and_reverse_command():
    obs=observed(DemoSession())
    Draft202012Validator(EgoObservation.model_json_schema()).validate(obs.model_dump())
    value=dict(schema_version=5,run_id="run",tick=1,target_speed_kmh=-2,steering_angle_rad=0,
        emergency_stop=False,valid_for_s=.3,status="MANEUVER",reason="observed reverse")
    DriveCommand(**value)
    Draft202012Validator(DriveCommand.model_json_schema()).validate(value)


def test_rear_return_stops_turn_before_turn_controller_runs():
    s,t=drive_session()
    class TurnMustNotRun:
        def update(self,ego): raise AssertionError("unsafe turn reached actuator")
    vehicle=s.physical.vehicles["V01"]
    vehicle.turn=TurnMustNotRun()
    result=s.physical.observation("V01",t,observed(s,2,[hit(y=-2,kind="vehicle")],speed_mps=-1))
    assert result["emergency_stop"]


def test_path_only_profile_cannot_disable_protocol5_safety():
    s,t=drive_session()
    s.physical.vehicles["V01"].driving_profile="campus_fast_no_obstacles"
    result=s.physical.observation("V01",t,observed(s,2,[hit()],speed_mps=3))
    assert result["emergency_stop"]


def test_observed_clear_range_caps_old_high_speed_configuration():
    s,t=drive_session()
    vehicle=s.physical.vehicles["V01"]
    vehicle.driving_profile="campus_fast_no_obstacles";vehicle.cruise_speed_mps=50/3.6
    result=s.physical.observation("V01",t,observed(s,2))
    assert 0<result["target_speed_kmh"]<=18


def test_dynamic_rrt_candidate_never_becomes_executable():
    s,t=drive_session()
    vehicle=s.physical.vehicles["V01"]
    ego=observed(s,2,[hit(kind="pedestrian")])
    assert rrt_candidate("V01",vehicle,ego,ego.lidar.hits)=="WAIT_DYNAMIC_VALIDATION"
    result=evaluate_lidar("V01",vehicle,ego,10.)
    assert result.stop and result.speed_cap_mps==0


def test_static_rrt_preview_runs_but_brakes_remain_authoritative():
    s=DemoSession();ego=observed(s,hits=[hit(y=4,kind="static")])
    vehicle=SimpleNamespace(lane_width_m=8.,route_unity=[(ego.x,ego.z),(ego.x+12*math.sin(ego.heading_rad),ego.z+12*math.cos(ego.heading_rad))])
    status=rrt_candidate("V01",vehicle,ego,ego.lidar.hits)
    assert status.endswith("_NON_EXECUTABLE")


def test_websocket_accepts_same_lidar_contract_and_returns_valid_stop():
    with TestClient(create_app()) as client:
        state=client.get("/v1/demo/state").json()
        # Server map export supplies the exact current geometry hash.
        data=client.get("/v1/demo/unity-map").json()
        with client.websocket_connect("/v1/demo/unity/vehicles/V01/control") as ws:
            payload=observed(DemoSession()).model_dump()
            payload.update(run_id=state["run_id"],geometry_hash=data["geometry_hash"])
            ws.send_json(payload)
            result=ws.receive_json()
            assert DriveCommand(**result).schema_version==5
            assert result["emergency_stop"]


def test_lidar_profile_preserves_map_road_speed_limit():
    s,t=drive_session()
    s.physical.vehicles["V01"].road_speed_mps=.5
    result=s.physical.observation("V01",t,observed(s,2))
    assert not result["emergency_stop"] and 0<result["target_speed_kmh"]<=1.8


def bare_vehicle(route, **changes):
    value = dict(route_unity=route, progress_m=0., turn=None, lidar_blocked=False, lidar_clear_frames=0,
                 lidar_rrt_status="NOT_REQUESTED", lidar_rrt_at_s=-10., lane_width_m=None, lane_error_m=0.)
    value.update(changes)
    return SimpleNamespace(**value)


def left_turn_route(radius=6., steps=24):
    """3 m straight north, then a quarter circle turning left (west)."""
    route = [(0., -.65), (0., 3.)]
    for i in range(1, steps + 1):
        angle = math.pi / 2 * i / steps
        route.append((-radius + radius * math.cos(angle), 3. + radius * math.sin(angle)))
    return route + [(-radius - 10., 3. + radius)]


def heading_north(session, hits, speed=3.):
    return observed(session, 1, hits, x=0., z=0., heading_rad=0., speed_mps=speed)


def test_outside_kerb_of_a_junction_turn_is_not_read_as_straight_ahead():
    # Field case 2026-11-09 (library -> 60th): a 0.2 m kerb 1.85 m outside the
    # left-turn route sat ahead-right of the momentary heading and stalled V01.
    s = DemoSession()
    route, radius = left_turn_route(), 6.
    turned = math.radians(30)  # Vehicle is 30 deg into the left turn.
    x, z = route[8]
    heading = -turned
    progress = 3.65 + radius * turned
    def local(world):
        dx, dz = world[0] - x, world[1] - z
        return dx * math.cos(heading) - dz * math.sin(heading), dx * math.sin(heading) + dz * math.cos(heading)
    kerb = []
    for degrees in (56, 58, 60):  # 1.85 m outside the curve, as measured in the field.
        angle = math.radians(degrees)
        kerb.append(local((-radius + (radius + 1.85) * math.cos(angle), 3. + (radius + 1.85) * math.sin(angle))))
    assert all(abs(lateral) <= .46 + .8 and forward > 2 for lateral, forward in kerb)  # Straight ahead of the heading.
    ego = observed(s, 1, [hit(x=lateral, y=forward) for lateral, forward in kerb], x=x, z=z, heading_rad=heading, speed_mps=3.)
    result = evaluate_lidar("V01", bare_vehicle(route, progress_m=progress), ego, 0.)
    assert not result.stop


def test_return_on_the_curved_route_still_brakes():
    s = DemoSession()
    route = left_turn_route()
    on_route = route[8]  # Inside the curve, ~5 m along.
    result = evaluate_lidar("V01", bare_vehicle(route), heading_north(s, [hit(x=on_route[0], y=on_route[1])]), 0.)
    assert result.stop


def test_return_just_ahead_of_the_bumper_still_brakes_on_a_curved_route():
    s = DemoSession()
    result = evaluate_lidar("V01", bare_vehicle(left_turn_route()), heading_north(s, [hit(x=.3, y=1.2)]), 0.)
    assert result.stop


def test_stopped_vehicle_never_creeps_toward_a_close_route_return():
    s = DemoSession()
    result = evaluate_lidar("V01", bare_vehicle(left_turn_route()), heading_north(s, [hit(x=0., y=2.5)], speed=0.), 0.)
    assert result.stop


def turning_vehicle():
    from campus_sim.physical_turn import PhysicalTurn
    from campus_sim.turnaround import TurnController
    controller = TurnController(wheelbase_m=1.3, max_steering_rad=.52, lane_offset_m=1.5, road_half_width_m=6.,
        forward_space_m=20., rear_space_m=20., minimum_radius_m=2 * 1.3 / math.tan(.52),
        half_width_m=.46, half_length_m=1.55)
    # Local turn frame == world; the rear axle (root z - 0.65) starts at x=1.5, z=0.
    return bare_vehicle([(1.5, .65), (1.5, 20.)], turn=PhysicalTurn(controller, (0., 0.), 0.)), controller


def test_kerb_beside_a_turn_does_not_block_the_planned_sweep():
    # Field case: a rear-gate U-turn never started because any return within
    # 3.5 m (a kerb) blocked it, although the planned sweep stayed in the road.
    s = DemoSession()
    vehicle, _ = turning_vehicle()
    ego = observed(s, 1, [hit(x=1.9, y=-1.7), hit(x=1.9, y=-4.2)], x=1.5, z=.65, heading_rad=0., speed_mps=0.)
    assert not evaluate_lidar("V01", vehicle, ego, 0.).stop


def test_return_on_the_swept_turn_arc_blocks():
    s = DemoSession()
    vehicle, controller = turning_vehicle()
    sample = controller.arcs[0][len(controller.arcs[0]) * 2 // 3]
    ego = observed(s, 1, [hit(x=sample.x - 1.5, y=sample.z - .65)], x=1.5, z=.65, heading_rad=0., speed_mps=0.)
    assert evaluate_lidar("V01", vehicle, ego, 0.).stop
