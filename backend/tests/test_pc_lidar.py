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


def test_reconnected_protocol5_updates_current_pose_and_stays_stopped():
    from campus_sim.map_coordinates import MapCoordinates

    app = create_app()
    with TestClient(app) as client:
        client.get("/v1/demo/state")
        session = app.state.demo
        endpoint = "/v1/demo/unity/vehicles/V01/control"
        with client.websocket_connect(endpoint) as socket:
            socket.send_json(observed(session).model_dump())
            first = socket.receive_json()
            assert first["tick"] == 1 and first["emergency_stop"]
        with client.websocket_connect(endpoint) as socket:
            assert session.physical.vehicles["V01"].ego is None
            fresh = observed(session, tick=2, x=70.57, z=-159.19)
            socket.send_json(fresh.model_dump())
            command = socket.receive_json()
            assert command["schema_version"] == 5
            assert command["tick"] == 2 and command["run_id"] == session.run_id
            assert command["emergency_stop"] and command["target_speed_kmh"] == 0
            assert session.physical.vehicles["V01"].ego.x == fresh.x
            state = client.get("/v1/demo/state").json()
            vehicle = next(v for v in state["vehicles"] if v["id"] == "V01")
            assert vehicle["position"] == pytest.approx(MapCoordinates().from_unity(fresh.x, fresh.z))
