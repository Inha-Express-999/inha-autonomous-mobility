import math

import pytest

from campus_sim.swept_geometry import BoxFootprint, Pose2
from campus_sim.timed_collision import MovingDiscBound, check_timed_prefix
from campus_sim.trajectory import MotionLimits, time_parameterize

LIMITS = MotionLimits(1, 1, 2, 1, "synthetic kinematic assumptions")


def straight(distance, limits=LIMITS):
    return time_parameterize((Pose2(0, 0, math.pi / 2), Pose2(distance, 0, math.pi / 2)), limits)


def test_trapezoidal_profile_has_expected_distance_duration_and_endpoint_rest():
    path = straight(4)
    assert path.duration_s == pytest.approx(4.75)  # accel 1s, cruise 3.25s, brake .5s
    assert path.sample(0).speed_mps == 0
    assert path.sample(1).pose.x == pytest.approx(0.5)
    assert path.sample(4.25).pose.x == pytest.approx(3.75)
    assert path.sample(path.duration_s).pose.x == 4
    assert path.sample(path.duration_s).speed_mps == 0
    previous = path.sample(0)
    for i in range(1, 951):
        current = path.sample(i * 0.005)
        acceleration = (current.speed_mps - previous.speed_mps) / 0.005
        assert -2.00001 <= acceleration <= 1.00001
        assert 0 <= current.speed_mps <= 1
        assert current.pose.x >= previous.pose.x
        previous = current


def test_short_segment_uses_triangular_profile_without_hitting_speed_cap():
    path = straight(0.1)
    peak = math.sqrt(2 * 0.1 / (1 + 0.5))
    assert path.stages[0].peak_speed_mps == pytest.approx(peak)
    assert path.stages[0].cruise_s == pytest.approx(0)
    assert path.duration_s == pytest.approx(peak * 1.5)


def test_rotation_is_stationary_shortest_arc_then_translation_starts_at_rest():
    path = time_parameterize((Pose2(0, 0, 0), Pose2(0, 0, math.pi / 2),
                              Pose2(1, 0, math.pi / 2)), LIMITS)
    state = path.sample(0.5)
    assert (state.pose.x, state.pose.y, state.speed_mps) == (0, 0, 0)
    assert state.yaw_rate_radps == 1
    assert path.sample(math.pi / 2).speed_mps == 0
    wrap = time_parameterize((Pose2(0, 0, math.pi - 0.1), Pose2(0, 0, -math.pi + 0.1)), LIMITS)
    assert wrap.duration_s == pytest.approx(0.2)


@pytest.mark.parametrize("end", [Pose2(1, 0, 0), Pose2(1, 0, 1)])
def test_side_slip_or_simultaneous_turning_translation_is_rejected(end):
    with pytest.raises(ValueError):
        time_parameterize((Pose2(0, 0, 0), end), LIMITS)


def test_motion_timing_changes_crossing_outcome_and_prefix_is_not_full_clearance():
    path = straight(2, MotionLimits(1, 0.5, 1, 1, "synthetic"))
    footprint = BoxFootprint(0.4, 0.4)
    crossing = MovingDiscBound((0.25, 2), (0, -2), 0.1, 0, 0)
    result = check_timed_prefix(path, footprint, (crossing,), observation_age_s=0)
    assert result.collision
    assert 0 < result.checked_duration_s < 1.5
    later = MovingDiscBound((0.25, 6), (0, -2), 0.1, 0, 0)
    clear = check_timed_prefix(path, footprint, (later,), observation_age_s=0)
    assert clear.collision is False
    assert clear.checked_duration_s == 2
    assert not clear.full_trajectory_checked


def test_observation_age_caps_total_prediction_and_expiry_is_unknown():
    path = straight(4)
    result = check_timed_prefix(path, BoxFootprint(1, 1), (), observation_age_s=0.3)
    assert result.checked_duration_s == pytest.approx(1.7)
    assert not result.full_trajectory_checked
    expired = check_timed_prefix(path, BoxFootprint(1, 1), (), observation_age_s=2)
    assert expired.collision is None
    assert expired.checked_duration_s == 0


def test_rotation_collision_and_initial_overlap_are_included():
    path = time_parameterize((Pose2(0, 0, 0), Pose2(0, 0, math.pi / 2)), LIMITS)
    obstacle = MovingDiscBound((1.2, 1.2), (0, 0), 0.1, 0, 0)
    assert check_timed_prefix(path, BoxFootprint(4, 0.4), (obstacle,), observation_age_s=0).collision
    overlap = MovingDiscBound((0, 0), (0, 0), 0.1, 0, 0)
    result = check_timed_prefix(path, BoxFootprint(1, 1), (overlap,), observation_age_s=0)
    assert result.collision and result.checked_duration_s == 0


def test_uncertainty_inflates_predicted_envelope():
    path = straight(1)
    far = MovingDiscBound((0, 1), (0, 0), 0.1, 0, 0)
    uncertain = MovingDiscBound((0, 1), (0, 0), 0.1, 0.8, 0)
    assert check_timed_prefix(path, BoxFootprint(0.4, 0.4), (far,), observation_age_s=0).collision is False
    assert check_timed_prefix(path, BoxFootprint(0.4, 0.4), (uncertain,), observation_age_s=0).collision


@pytest.mark.parametrize("bad", [0, -1, math.inf, math.nan])
def test_invalid_motion_limit_rejected(bad):
    with pytest.raises(ValueError):
        MotionLimits(bad, 1, 1, 1, "test")
