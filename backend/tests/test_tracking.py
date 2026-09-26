import math

import pytest
from pydantic import ValidationError

from campus_sim.collision import circle_time_to_collision
from campus_sim.domain import EgoLocalization, MapPosition, SensorObservation
from campus_sim.service import MobilityService
from campus_sim.tracking import PedestrianMotionTracker


def capture(t, target=(0, 5), origin=(0, 0), heading=0, *, session="test", visible=True):
    dx, dy = target[0] - origin[0], target[1] - origin[1]
    forward = dx * math.sin(heading) + dy * math.cos(heading)
    left = -dx * math.cos(heading) + dy * math.sin(heading)
    return SensorObservation.model_validate({
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
        "sessionId": session, "mapVersion": "synthetic-test", "observedTick": round(t * 100),
        "egoPoseTick": round(t * 100), "valid": True, "observedTimeS": t,
        "sensorPositionM": {"x": origin[0], "y": origin[1], "z": 0}, "sensorHeadingRad": heading,
        "detections": [{"rangeM": math.hypot(forward, left), "bearingRad": math.atan2(left, forward),
                        "localPositionM": {"x": forward, "y": left, "z": 0},
                        "entityClass": "PEDESTRIAN", "entityId": "person-a"}] if visible else [],
    })


def test_ego_translation_and_rotation_do_not_create_pedestrian_motion():
    tracker = PedestrianMotionTracker()
    assert not tracker.update(capture(0))
    (motion,) = tracker.update(capture(0.1, origin=(0.1, 0), heading=math.pi / 2))
    assert motion.world_velocity_mps == pytest.approx((0, 0))
    assert motion.relative_velocity_mps == pytest.approx((-1, 0))


def test_side_motion_supplies_a_vector_to_circle_ttc_without_radial_guess():
    tracker = PedestrianMotionTracker()
    tracker.update(capture(0, target=(2.2, 0)))
    (motion,) = tracker.update(capture(0.1, target=(2, 0)))
    assert motion.world_velocity_mps == pytest.approx((-2, 0))
    assert circle_time_to_collision(motion.relative_position_m, motion.relative_velocity_mps, 0.5) \
        == pytest.approx(0.75)


@pytest.mark.parametrize("interruption", [capture(0.1, visible=False),
                                         capture(0.1, session="new"), capture(1.0)])
def test_lost_context_or_time_gap_does_not_infer_velocity(interruption):
    tracker = PedestrianMotionTracker()
    tracker.update(capture(0))
    assert not tracker.update(interruption)


def test_partial_capture_metadata_is_rejected():
    data = capture(0).model_dump()
    del data["sensor_position_m"]
    with pytest.raises(ValidationError):
        SensorObservation.model_validate(data)


@pytest.mark.parametrize("late", [
    capture(0.05, target=(100, 0)),
    capture(0.05, target=(100, 0)).model_copy(update={"observed_tick": 15}),
    capture(0.05, target=(100, 0)).model_copy(update={"valid": False}),
])
def test_late_frame_cannot_poison_the_next_velocity_estimate(late):
    tracker = PedestrianMotionTracker()
    tracker.update(capture(0, target=(2.2, 0)))
    tracker.update(capture(0.1, target=(2, 0)))
    assert not tracker.update(late)
    (motion,) = tracker.update(capture(0.2, target=(1.8, 0)))
    assert motion.world_velocity_mps == pytest.approx((-2, 0))


def test_service_tracks_only_valid_ingress_and_expires_diagnostics():
    service = MobilityService.synthetic_fixture()
    for t, target in [(0.0, (2.2, 0)), (0.1, (2, 0))]:
        frame = capture(t, target=target).model_copy(update={"map_version": service.graph.map_version})
        service.record_ego_localization(EgoLocalization(
            vehicle_id="V01", session_id="test", observed_tick=frame.ego_pose_tick,
            map_version=frame.map_version, position=MapPosition(x=0, y=0, z=0),
            heading_rad=0, speed_mps=0,
        ))
        assert service.record_sensor_observation(frame)
    (motion,) = service.current_sensor_motion("V01", "lidar")
    assert motion.world_velocity_mps == pytest.approx((-2, 0))
    late = frame.model_copy(update={"observed_tick": 11, "observed_time_s": 0.05})
    with pytest.raises(ValueError, match="non_monotonic_capture_time"):
        service.record_sensor_observation(late)
    assert service.sensor_observations[("V01", "lidar")].observed_tick == 10
    service.advance(0.31)
    assert not service.current_sensor_motion("V01", "lidar")
