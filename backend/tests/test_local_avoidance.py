from math import hypot, inf

import pytest
from pydantic import ValidationError

from campus_sim.local_planning import ConvexCorridor, plan_rrt, segment_distance
from campus_sim.safety import assess_safety, crossing_ttc, stopping_distance
from campus_sim.sensors import Detection, ObservationContext, SensorObservation, observation_error


def context():
    return ObservationContext(
        vehicle_id="V01",
        sensors={"lidar": "LIDAR", "radar": "RADAR"},
        required_sensor_ids=frozenset({"lidar", "radar"}),
        current_tick=100,
        pose_version=7,
        tick_duration_s=0.02,
    )


def frames(*hits):
    return tuple(
        SensorObservation(
            vehicle_id="V01",
            sensor_id=sensor,
            sensor_type=kind,
            observed_tick=100,
            pose_version=7,
            valid=True,
            detections=hits if sensor == "lidar" else (),
        )
        for sensor, kind in (("lidar", "LIDAR"), ("radar", "RADAR"))
    )


def hit(x=0, y=5, radius=0.7, **kwargs):
    return Detection(
        position_m=(x, y),
        range_m=hypot(x, y),
        radius_m=radius,
        entity_class=kwargs.pop("entity_class", "static"),
        **kwargs,
    )


def corridor(width=8):
    return ConvexCorridor(((-width / 2, -2), (width / 2, -2), (width / 2, 12), (-width / 2, 12)))


def plan(observations, **kwargs):
    return plan_rrt(
        (0, 0), (0, 10), corridor(), observations, context(), ego_radius_m=0.5, budget_s=2, **kwargs
    )


@pytest.mark.parametrize(
    "field,value,reason",
    [
        ("observed_tick", 89, "STALE_FRAME"),
        ("observed_tick", 101, "FUTURE_FRAME"),
        ("pose_version", 8, "POSE_MISMATCH"),
        ("vehicle_id", "V02", "WRONG_VEHICLE"),
        ("sensor_id", "fake", "UNKNOWN_SENSOR"),
        ("sensor_type", "RADAR", "UNKNOWN_SENSOR"),
        ("valid", False, "INVALID_SENSOR"),
    ],
)
def test_invalid_frames_stop_and_do_not_plan(field, value, reason):
    original = frames()
    bad = (original[0].model_copy(update={field: value}), original[1])
    assert observation_error(bad, context()) == reason
    assert assess_safety(bad, context(), speed_mps=8.333, ego_radius_m=0.5).action == "STOP"
    result = plan(bad)
    assert result.status == reason and not result.path


def test_missing_duplicate_and_age_boundary():
    original = frames()
    assert observation_error(original[:1], context()) == "MISSING_SENSOR"
    assert observation_error((original[0], original[0]), context()) == "DUPLICATE_SENSOR"
    assert (
        observation_error(
            (original[0].model_copy(update={"observed_tick": 90}), original[1]), context()
        )
        is None
    )


@pytest.mark.parametrize(
    "changes",
    [
        {"position_m": (float("nan"), 1)},
        {"range_m": float("inf")},
        {"range_m": 2},
        {"radius_m": -1},
        {"relative_velocity_mps": (0, float("inf"))},
        {"ground_truth_position": (1, 2)},
    ],
)
def test_detection_rejects_invalid_values_and_ground_truth(changes):
    data = hit().model_dump() | changes
    with pytest.raises(ValidationError):
        Detection.model_validate(data)


def test_ttc_overlap_crossing_receding_and_tangent():
    assert crossing_ttc((0, 0), (1, 0), 1) == 0
    assert crossing_ttc((4, 0), (-2, 0), 1) == pytest.approx(1.5)
    assert crossing_ttc((4, 0), (2, 0), 1) == inf
    assert crossing_ttc((4, 0), (0, 0), 1) == inf
    assert crossing_ttc((1, 3), (0, -1), 1) == pytest.approx(3)
    assert crossing_ttc((2, 3), (0, -1), 1) == inf


def test_thirty_kmh_requires_real_braking_distance_not_fifteen_metre_local_horizon():
    distance = stopping_distance(30 / 3.6, 0.3, 3, 0.5)
    assert distance == pytest.approx(14.574074074)
    assessment = assess_safety(frames(hit(y=14)), context(), speed_mps=30 / 3.6, ego_radius_m=0.5)
    assert assessment.action == "STOP"
    assert assessment.stopping_distance_m == pytest.approx(distance)


def test_crossing_radar_motion_and_unknown_motion():
    observations = frames(hit(x=3, y=0, entity_class="pedestrian", relative_velocity_mps=(-2, 0)))
    assessment = assess_safety(observations, context(), speed_mps=0, ego_radius_m=0.5)
    assert assessment.action == "STOP" and assessment.minimum_ttc_s < 2
    unknown = assess_safety(
        frames(hit(x=5, entity_class="unknown")), context(), speed_mps=0, ego_radius_m=0.5
    )
    assert unknown.action == "YIELD"
    assert plan(observations).status == "WAIT_DYNAMIC_VALIDATION"


def test_rrt_obstacle_detour_seed_and_all_swept_segments():
    obstacle = hit()
    a = plan(frames(obstacle), seed=12)
    b = plan(frames(obstacle), seed=12)
    assert a.status == "CANDIDATE" and a.iterations > 0
    assert a.path == b.path
    assert a.path[0] == (0, 0) and a.path[-1] == (0, 10)
    assert not a.executable
    for start, end in zip(a.path, a.path[1:]):
        # Dense independent check of the entire swept circle, not just endpoints.
        for i in range(101):
            t = i / 100
            x, y = (start[j] + (end[j] - start[j]) * t for j in (0, 1))
            assert -3.2 <= x <= 3.2 and -1.2 <= y <= 11.2
            assert hypot(x, y - 5) > 1.5


def test_segment_midpoint_collision_and_degenerate_segment():
    assert segment_distance((0, 5), (0, 0), (0, 10)) == 0
    assert segment_distance((0, 5), (0, 0), (0, 0)) == 5


def test_blocked_corridor_iteration_failure_and_endpoint_rejection():
    result = plan(frames(hit(radius=3.5)), max_iterations=200)
    assert result.status == "NO_PATH_WITHIN_BUDGET" and not result.path
    assert plan(frames(hit(y=0))).status == "INVALID_ENDPOINT"
    assert plan(frames()).path == ((0, 0), (0, 10))


def test_tiny_time_budget_and_invalid_corridor_fail_closed():
    result = plan_rrt(
        (0, 0), (0, 10), corridor(), frames(hit()), context(), ego_radius_m=0.5, budget_s=1e-12
    )
    assert result.status == "TIME_BUDGET" and not result.path
    direct = plan_rrt(
        (0, 0), (0, 10), corridor(), frames(), context(), ego_radius_m=0.5, budget_s=1e-12
    )
    assert direct.status == "TIME_BUDGET" and not direct.path
    with pytest.raises(ValueError):
        ConvexCorridor(((0, 0), (1, 1), (0, 1), (1, 0)))
    with pytest.raises(ValueError):
        plan(frames(), step_m=-1)


@pytest.mark.parametrize("speed,deceleration", [(-1, 3), (1, 0), (float("nan"), 3)])
def test_invalid_braking_inputs(speed, deceleration):
    with pytest.raises(ValueError):
        stopping_distance(speed, 0.3, deceleration, 0.5)
