import math
from dataclasses import replace

import pytest

from campus_sim.domain import SensorObservation
from campus_sim.observed_trajectory import ObservationBounds, check_observed_trajectory
from campus_sim.safety import SensorSample
from campus_sim.swept_geometry import BoxFootprint, Pose2
from campus_sim.tracking import PedestrianMotionTracker
from campus_sim.trajectory import MotionLimits, time_parameterize

BOUNDS = ObservationBounds(0.1, 0.1, 0, 0, 0, 0.5, 1, 0, "Synthetic equal-rate clocks")
PATH = time_parameterize((Pose2(0, 0, math.pi / 2), Pose2(4, 0, math.pi / 2)),
                         MotionLimits(1, 1, 1, 1, "Synthetic test limits"))


def frame(x, y, time, *, kind="PEDESTRIAN", origin=(0, 0), heading=0):
    dx, dy = x - origin[0], y - origin[1]
    forward = dx * math.sin(heading) + dy * math.cos(heading)
    left = -dx * math.cos(heading) + dy * math.sin(heading)
    return SensorObservation.model_validate({
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D", "sessionId": "s",
        "mapVersion": "synthetic-test", "observedTick": round(time * 10), "egoPoseTick": 1,
        "observedTimeS": time, "valid": True,
        "sensorPositionM": {"x": origin[0], "y": origin[1], "z": 0}, "sensorHeadingRad": heading,
        "detections": [{"rangeM": math.hypot(forward, left), "bearingRad": math.atan2(left, forward),
                        "localPositionM": {"x": forward, "y": left, "z": 0},
                        "entityClass": kind, "entityId": "seen"}],
    })


def moving_sample():
    tracker = PedestrianMotionTracker()
    tracker.update(frame(1.2, 0, 1000))
    latest = frame(1, 0, 1000.1, origin=(0, 0.2), heading=0.1)
    return SensorSample(latest, 10, tracker.update(latest))


def check(samples, *, now=10, bounds=BOUNDS):
    return check_observed_trajectory(PATH, BoxFootprint(0.4, 0.4), samples, now_s=now,
                                     vehicle_id="V01", session_id="s", map_version="synthetic-test",
                                     ego_pose_tick=1, bounds=bounds, margin_m=0)


def test_sensor_motion_to_continuous_collision_without_mixing_unrelated_clocks():
    result = check((moving_sample(),))
    assert result.sweep.collision is True
    assert 0 < result.sweep.checked_duration_s < 0.5
    assert result.oldest_capture_age_bound_s == 0
    assert not result.executable


def test_latency_interval_covers_contact_before_the_nominal_midpoint_prediction():
    sample = moving_sample()
    result = check((sample,), bounds=replace(BOUNDS, max_capture_to_receipt_s=0.4))
    assert result.sweep.collision is True
    assert result.sweep.checked_duration_s == 0
    assert result.oldest_capture_age_bound_s == 0.4


def test_oldest_sensor_caps_prediction_at_two_seconds_from_capture():
    fresh = SensorSample(frame(20, 20, 800, kind="STATIC_OBSTACLE"), 10)
    old = SensorSample(frame(20, 21, 700, kind="STATIC_OBSTACLE"), 9.7)
    result = check((fresh, old), bounds=replace(BOUNDS, max_capture_to_receipt_s=0.2))
    assert result.sweep.collision is False
    assert result.sweep.checked_duration_s == pytest.approx(1.5)
    assert not result.sweep.full_trajectory_checked


def test_clock_rate_maps_velocity_and_caps_horizon_in_capture_seconds():
    sample = SensorSample(frame(20, 20, 800, kind="STATIC_OBSTACLE"), 9.7)
    result = check((sample,), bounds=replace(BOUNDS, max_capture_to_receipt_s=0.2,
                                             capture_seconds_per_server_second=2))
    assert result.sweep.checked_duration_s == pytest.approx(0.6)
    slow = check((moving_sample(),))
    fast = check((moving_sample(),), bounds=replace(BOUNDS, capture_seconds_per_server_second=2))
    assert fast.sweep.checked_duration_s < slow.sweep.checked_duration_s


@pytest.mark.parametrize("change", [
    {"valid": False}, {"session_id": "other"}, {"ego_pose_tick": 2}, {"map_version": "other"},
    {"vehicle_id": "V02"}, {"sensor_position_m": None}, {"observed_time_s": None},
])
def test_invalid_or_mismatched_frames_do_not_clear_path(change):
    sample = moving_sample()
    sample = replace(sample, observation=sample.observation.model_copy(update=change))
    assert check((sample,)).sweep.collision is None


@pytest.mark.parametrize("received", [None, 10.1, 9.4, math.nan])
def test_missing_future_or_expired_receipt_is_unknown(received):
    assert check((replace(moving_sample(), received_at_s=received),)).sweep.collision is None


@pytest.mark.parametrize("kind", ["UNKNOWN", "VEHICLE"])
def test_untracked_classes_are_not_assumed_stationary(kind):
    assert check((SensorSample(frame(5, 0, 1000, kind=kind), 10),)).sweep.collision is None


def test_pedestrian_requires_current_identity_and_matching_observed_surface():
    sample = moving_sample()
    for motions in ((), (replace(sample.motions[0], observed_time_s=1000),),
                    (replace(sample.motions[0], world_position_m=(2, 2)),), sample.motions * 2):
        assert check((replace(sample, motions=motions),)).sweep.collision is None


def test_static_hit_uses_sensor_world_pose_and_full_diameter_bound():
    sample = SensorSample(frame(0.25, 0, 3, kind="STATIC_OBSTACLE",
                               origin=(2, 3), heading=1), 10)
    assert check((sample,)).sweep.collision is True


def test_no_observations_is_unknown_even_if_no_obstacle_list_would_be_clear():
    assert check(()).sweep.collision is None


@pytest.mark.parametrize("change", [{"max_capture_to_receipt_s": -1},
                                   {"max_capture_to_receipt_s": 2}, {"velocity_error_mps": math.nan},
                                   {"pedestrian_diameter_m": 0}, {"provenance": " "},
                                   {"capture_seconds_per_server_second": 0}, {"clock_rate_error": 1}])
def test_required_bounds_reject_invalid_assumptions(change):
    with pytest.raises(ValueError):
        replace(BOUNDS, **change)
