import math

import pytest

from campus_sim.domain import EgoLocalization, MapPosition, SensorEntityClass, SensorObservation
from campus_sim.local_candidate import (
    capture_local_input,
    compute_local_candidate,
    inputs_still_current,
)
from campus_sim.local_rrt import RectCorridor
from campus_sim.service import MobilityService
from campus_sim.swept_geometry import BoxFootprint, Pose2
from campus_sim.trajectory import MotionLimits


def observed_service():
    service = MobilityService.synthetic_fixture()
    service.record_ego_localization(EgoLocalization(
        vehicle_id="V01", session_id="planning-test", observed_tick=1,
        map_version=service.graph.map_version, position=MapPosition(x=0, y=0, z=0),
        heading_rad=math.pi / 2, speed_mps=0,
    ))
    frame = SensorObservation.model_validate({
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
        "sessionId": "planning-test", "mapVersion": service.graph.map_version,
        "observedTick": 1, "egoPoseTick": 1, "valid": True, "observedTimeS": 0,
        "sensorPositionM": {"x": 0, "y": 0, "z": 0}, "sensorHeadingRad": math.pi / 2,
        "detections": [{"rangeM": 3, "bearingRad": 0,
                        "localPositionM": {"x": 3, "y": 0, "z": 0},
                        "entityClass": "STATIC_OBSTACLE"}],
    })
    service.record_sensor_observation(frame)
    return service


def capture(service):
    return capture_local_input(service, "V01", object_diameter_bound_m=0.8,
                               bound_provenance="Synthetic obstacle diameter bound")


def test_actual_ingress_to_projected_obstacle_to_immutable_rrt_candidate():
    service = observed_service()
    inputs = capture(service)
    assert inputs.observed_obstacles[0].x == pytest.approx(3)
    assert inputs.observed_obstacles[0].y == pytest.approx(0)
    assert inputs.observed_obstacles[0].radius_m == 0.8
    candidate = compute_local_candidate(inputs, Pose2(6, 0, math.pi / 2),
                                        footprint=BoxFootprint(0.4, 0.4),
                                        corridor=RectCorridor(-1, -3, 7, 3),
                                        corridor_provenance="Synthetic test rectangle", seed=17)
    assert candidate is not None
    assert inputs_still_current(service, candidate.inputs)
    assert not candidate.executable
    assert any(abs(p.y) > 1 for p in candidate.poses)
    timed = candidate.timed_trajectory(MotionLimits(1, 0.5, 2, 1, "synthetic test limits"))
    assert timed.duration_s > 0
    assert timed.sample(timed.duration_s).pose == candidate.poses[-1]
    assert timed.sample(timed.duration_s).speed_mps == 0
    assert service.vehicle_runtime["V01"].route_id is None  # Candidate never mutates authority.


@pytest.mark.parametrize("change", ["expiry", "clock_rollback", "pose", "frame", "route",
                                     "speed_profile", "mission", "closure", "session", "moving"])
def test_candidate_context_rejects_changed_authoritative_inputs(change):
    service = observed_service()
    service.simulation_time_s = 0.1
    inputs = capture(service)
    runtime = service.vehicle_runtime["V01"]
    if change == "expiry":
        service.simulation_time_s = 0.31
    elif change == "clock_rollback":
        service.simulation_time_s = 0.05
    elif change in {"pose", "session"}:
        pose = service.ego_localizations["V01"].model_copy(update={
            "observed_tick": 2, "session_id": "new" if change == "session" else "planning-test"})
        service.record_ego_localization(pose)
    elif change == "frame":
        frame = service.sensor_observations[("V01", "lidar")].model_copy(
            update={"observed_tick": 2, "observed_time_s": 0.1})
        service.record_sensor_observation(frame)
    elif change == "route":
        runtime.route_id = "new-route"
    elif change == "speed_profile":
        runtime.route_snapshot_speeds = [0.2]
    elif change == "mission":
        runtime.request_id = "new-request"
    elif change == "closure":
        service.closed_zone_ids.add("closed")
    elif change == "moving":
        runtime.speed_mps = 0.1
    assert not inputs_still_current(service, inputs)


def test_identical_frames_in_different_service_instance_cannot_reuse_plan():
    first, second = observed_service(), observed_service()
    assert not inputs_still_current(second, capture(first))


@pytest.mark.parametrize("kind", [SensorEntityClass.PEDESTRIAN, SensorEntityClass.VEHICLE,
                                   SensorEntityClass.UNKNOWN])
def test_static_planner_never_freezes_dynamic_or_unknown_returns(kind):
    service = observed_service()
    frame = service.sensor_observations[("V01", "lidar")].model_copy(deep=True,
        update={"observed_tick": 2, "observed_time_s": 0.1})
    frame.detections[0].entity_class = kind
    service.record_sensor_observation(frame)
    assert capture(service) is None


def test_other_sensor_failure_cannot_be_ignored():
    service = observed_service()
    frame = service.sensor_observations[("V01", "lidar")].model_copy(
        update={"sensor_id": "rear", "valid": False})
    service.record_sensor_observation(frame)
    assert capture(service) is None
