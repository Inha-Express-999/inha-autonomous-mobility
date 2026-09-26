import math

import pytest

from campus_sim.controller import ControllerPolicy, make_control_command, steering_target
from campus_sim.domain import CreateRequest, EgoLocalization, MapPosition, SensorObservation
from campus_sim.realtime import make_snapshot
from campus_sim.service import MobilityService

POLICY = ControllerPolicy(1, math.pi / 2, 2, 2, 0.15, 0.14, 0.2, "synthetic preview limits")


def ready_service():
    service = MobilityService.synthetic_fixture()
    service.control_policies["V01"] = POLICY
    service.create_request(CreateRequest(command_id="control", owner_id="test", service_type="PASSENGER",
        pickup_landmark_id=service.graph.nodes[1].landmark_id,
        dropoff_landmark_id=service.graph.nodes[2].landmark_id))
    runtime = service.vehicle_runtime["V01"]
    target = runtime.route_points[1]
    heading = math.atan2(target[0] - runtime.x, target[1] - runtime.y)
    service.simulation_time_s = 1.1
    service.record_ego_localization(EgoLocalization(
        vehicle_id="V01", session_id="controller-test", observed_tick=1,
        map_version=service.graph.map_version, position=MapPosition(x=runtime.x, y=runtime.y, z=0),
        heading_rad=heading, speed_mps=0,
    ))
    service.record_sensor_observation(SensorObservation.model_validate({
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
        "sessionId": "controller-test", "observedTick": 1, "egoPoseTick": 1,
        "mapVersion": service.graph.map_version, "valid": True, "detections": [],
    }))
    runtime.sensor_clear_since_s = 0
    return service


def test_heading_axis_speed_limits_and_stop_before_turn():
    assert steering_target(0, 0, 0, 0, (0, 10), 0.4, POLICY) == (0.4, 0)
    speed, yaw = steering_target(0, 0, 0, 0, (10, 0), 1, POLICY)
    assert speed == 0 and yaw == POLICY.max_yaw_rate_radps
    assert steering_target(0, 0, 0, 0.5, (10, 0), 1, POLICY) == (0, 0)
    assert steering_target(0, 0, 0, 0, (0, 0.1), 1, POLICY) == (0, 0)


def test_pc_snapshot_carries_sequenced_pose_bound_control_but_mobile_does_not():
    service = ready_service()
    command = make_snapshot(service, "PC_Operator", None, 0)["controlCommands"][0]
    assert command["targetSpeedMps"] > 0
    assert command["sessionId"] == "controller-test" and command["egoPoseTick"] == 1
    assert command["validForS"] == 0.2
    later = make_snapshot(service, "PC_Operator", None, 1)["controlCommands"][0]
    assert later["sequence"] > command["sequence"]
    assert make_snapshot(service, "Mobile_Passenger", "test", 0)["controlCommands"] == []


@pytest.mark.parametrize("failure", ["sensor", "pose", "request", "handoff"])
def test_safety_and_service_holds_override_controller(failure):
    service = ready_service()
    assert make_control_command(service, "V01")["targetSpeedMps"] > 0
    if failure == "sensor":
        frame = service.sensor_observations[("V01", "lidar")].model_copy(
            update={"valid": False, "observed_tick": 2})
        service.record_sensor_observation(frame)
    elif failure == "pose":
        service.simulation_time_s += 0.6
    elif failure == "request":
        service.vehicle_runtime["V01"].request_id = None
    else:
        pose = service.ego_localizations["V01"].model_copy(update={"observed_tick": 2})
        service.record_ego_localization(pose)
    command = make_control_command(service, "V01")
    assert command["targetSpeedMps"] == command["yawRateRadps"] == 0


def test_unconfigured_vehicle_does_not_emit_actuator_intent():
    service = ready_service()
    service.control_policies.clear()
    assert make_control_command(service, "V01") is None


def test_command_lifetime_cannot_extend_sensor_freshness():
    service = ready_service()
    service.simulation_time_s += 0.25
    command = make_control_command(service, "V01")
    assert command["targetSpeedMps"] > 0
    assert command["validForS"] == pytest.approx(0.05)
