from itertools import permutations

import pytest

from campus_sim.domain import EgoLocalization, MapPosition, ReasonCode, SensorObservation
from campus_sim.safety import SafetyPolicy, SensorSample, evaluate_sensor_safety
from campus_sim.service import MobilityService


def sample(*, tick=10, received_at=1.0, valid=True, distance=None):
    return SensorSample(
        SensorObservation.model_validate({
            "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
            "sessionId": "test-session", "mapVersion": "synthetic-test-v1",
            "observedTick": tick, "egoPoseTick": tick, "valid": valid,
            "detections": [] if distance is None else [{
                "rangeM": distance, "bearingRad": 0.0,
                "localPositionM": {"x": distance, "y": 0.0, "z": 0.0},
                "entityClass": "STATIC_OBSTACLE",
            }],
        }), received_at,
    )


def evaluate(samples, *, now=1.0, clear_since=0.0, tick=10, speed=1.0):
    return evaluate_sensor_safety(
        samples, expected_pose_tick=tick, speed_mps=speed, now_s=now,
        clear_since_s=clear_since, policy=SafetyPolicy.from_default_config(),
    )


@pytest.mark.parametrize("received_at", [None, 0.69, 1.01, float("nan")])
def test_missing_expired_or_future_receipt_revokes_authority_and_resets_hold(received_at):
    decision = evaluate([sample(received_at=received_at)])
    assert decision.motion_state == "REPLANNING"
    assert decision.reason == ReasonCode.SENSOR_DATA_STALE
    assert decision.clear_since_s is None


@pytest.mark.parametrize("bad", [sample(valid=False), sample(received_at=0.0)])
def test_other_stream_failure_cannot_hide_behind_pose_handoff(bad):
    # The old service loop returned at the first tick mismatch, allowing an
    # invalid/expired second stream to retain an already satisfied clear hold.
    for streams in permutations([sample(tick=9), bad]):
        decision = evaluate(streams)
        assert decision.motion_state is not None
        assert decision.clear_since_s is None


def test_fresh_pose_handoff_preserves_hold_but_never_authorizes_motion():
    decision = evaluate([sample(tick=9)])
    assert decision.motion_state == "REPLANNING"
    assert decision.clear_since_s == 0.0
    assert evaluate([sample()], clear_since=decision.clear_since_s).motion_state is None


def test_obstacle_resets_clear_hold_and_recovery_needs_full_interval():
    observation = sample(distance=1.45)  # 1 m/s: 0.2 + 1 / 4 + 1 metres.
    before = observation.observation.model_dump()
    stopped = evaluate([observation])
    assert stopped.reason == ReasonCode.OBSTACLE_STOP
    assert stopped.clear_since_s is None
    recovering = evaluate([sample()], clear_since=stopped.clear_since_s)
    assert recovering.reason == ReasonCode.SAFETY_RESUME_HOLD
    assert evaluate([sample(received_at=1.99)], now=1.99,
                    clear_since=recovering.clear_since_s).motion_state == "EMERGENCY_STOP"
    assert evaluate([sample(received_at=2.0)], now=2.0,
                    clear_since=recovering.clear_since_s).motion_state is None
    assert observation.observation.model_dump() == before


def test_service_applies_reset_when_second_stream_is_invalid_during_pose_handoff():
    service = MobilityService.synthetic_fixture()
    runtime = service.vehicle_runtime["V01"]
    pose = EgoLocalization(
        vehicle_id="V01", session_id="test-session", observed_tick=9,
        map_version=service.graph.map_version,
        position=MapPosition(x=runtime.x, y=runtime.y, z=0.0),
        heading_rad=0.0, speed_mps=0.0,
    )
    service.record_ego_localization(pose)
    frame = sample(tick=9).observation.model_copy(update={"map_version": pose.map_version})
    service.record_sensor_observation(frame)
    service.record_sensor_observation(frame.model_copy(update={"sensor_id": "rear", "valid": False}))
    # An old clear interval must never survive because the front sensor is
    # encountered before the invalid rear sensor when a new pose arrives.
    runtime.sensor_clear_since_s = 0.0
    service.simulation_time_s = 0.1
    service.record_ego_localization(pose.model_copy(update={"observed_tick": 10}))
    assert runtime.safety_reason == "SENSOR_INVALID"
    assert runtime.sensor_clear_since_s is None
    assert runtime.safety_motion_state == "EMERGENCY_STOP"
