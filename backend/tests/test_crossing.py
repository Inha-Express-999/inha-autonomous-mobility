import math
from dataclasses import replace

import pytest

from campus_sim.crossing import CrossingPolicy, crossing_hazard
from campus_sim.domain import EgoLocalization, MapPosition, SensorObservation
from campus_sim.realtime import make_snapshot
from campus_sim.safety import SafetyPolicy, SensorSample, evaluate_sensor_safety
from campus_sim.service import MobilityService
from campus_sim.tracking import PedestrianMotionTracker

POLICY = CrossingPolicy(0.3, 1.0, 0.1, 0.1, 2.0, "synthetic test bounds, not measured assets")


def frame(t, x, y=0):
    # North-facing ego; x is lateral, so the old forward sector does not see it.
    return SensorObservation.model_validate({
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
        "sessionId": "test", "mapVersion": "synthetic-test", "observedTick": round(t * 100),
        "egoPoseTick": round(t * 100), "valid": True, "observedTimeS": t,
        "sensorPositionM": {"x": 0, "y": 0, "z": 0}, "sensorHeadingRad": 0,
        "detections": [{"rangeM": math.hypot(x, y), "bearingRad": math.atan2(-x, y),
                        "localPositionM": {"x": y, "y": -x, "z": 0},
                        "entityClass": "PEDESTRIAN", "entityId": "p"}],
    })


def evaluate(observation, motions, *, now=1.0, clear=0.0, crossing=POLICY):
    return evaluate_sensor_safety(
        [SensorSample(observation, 1.0, motions)], expected_pose_tick=observation.ego_pose_tick,
        speed_mps=0, now_s=now, clear_since_s=clear, policy=SafetyPolicy.from_default_config(),
        crossing_policy=crossing,
    )


@pytest.mark.parametrize("before,after,expected", [(6.2, 6, "OBSTACLE_STOP"),
                                                  (5.8, 6, "UNKNOWN"),
                                                  (6, 6, "UNKNOWN")])
def test_lateral_vector_approach_separation_and_stationary(before, after, expected):
    tracker = PedestrianMotionTracker()
    tracker.update(frame(0, before))
    current = frame(0.1, after)
    assert evaluate(current, tracker.update(current)).reason.value == expected


def test_missing_motion_holds_and_clear_frames_require_full_recovery():
    current = frame(0.1, 6)
    stopped = evaluate(current, ())
    assert stopped.reason.value == "SENSOR_DATA_STALE"
    assert stopped.clear_since_s is None
    clear = current.model_copy(update={"detections": []})
    recovering = evaluate(clear, (), clear=stopped.clear_since_s)
    assert recovering.reason.value == "SAFETY_RESUME_HOLD"
    result = evaluate_sensor_safety(
        [SensorSample(clear, 2.0)], expected_pose_tick=clear.ego_pose_tick, speed_mps=0,
        now_s=2.0, clear_since_s=recovering.clear_since_s,
        policy=SafetyPolicy.from_default_config(), crossing_policy=POLICY,
    )
    assert result.motion_state is None


def test_crossing_outside_envelope_and_invalid_frame_precedence():
    tracker = PedestrianMotionTracker()
    tracker.update(frame(0, 6.2, y=4))
    current = frame(0.1, 6, y=4)
    motions = tracker.update(current)
    assert evaluate(current, motions).motion_state is None
    invalid = current.model_copy(update={"valid": False})
    assert evaluate(invalid, motions).reason.value == "SENSOR_INVALID"


def test_unknown_identity_never_uses_another_objects_motion():
    tracker = PedestrianMotionTracker()
    tracker.update(frame(0, 6))
    current = frame(0.1, 6)
    motions = tracker.update(current)
    anonymous = current.model_copy(deep=True)
    anonymous.detections[0].entity_id = None
    assert evaluate(anonymous, motions).reason.value == "SENSOR_DATA_STALE"


def test_receipt_age_and_surface_bound_are_conservative():
    tracker = PedestrianMotionTracker()
    tracker.update(frame(0, 6.0))
    current = frame(0.1, 5.8)
    motions = tracker.update(current)
    crossing = replace(POLICY, horizon_s=1.5)
    assert evaluate(current, motions, crossing=crossing).reason.value == "UNKNOWN"
    assert evaluate(current, motions, now=1.2, crossing=crossing).reason.value == "OBSTACLE_STOP"
    assert evaluate(current, motions, now=1.31).reason.value == "SENSOR_DATA_STALE"


def test_unmatched_or_nonfinite_motion_cannot_authorize_driving():
    tracker = PedestrianMotionTracker()
    tracker.update(frame(0, 6))
    current = frame(0.1, 6)
    (motion,) = tracker.update(current)
    for bad in (replace(motion, observed_time_s=0), replace(motion, entity_id="other"),
                replace(motion, relative_velocity_mps=(math.inf, 0))):
        assert crossing_hazard(current, (bad,), receipt_age_s=0,
                               margin_m=1, policy=POLICY) is None


@pytest.mark.parametrize("kwargs", [{"ego_envelope_radius_m": 0},
                                     {"pedestrian_diameter_m": -1},
                                     {"velocity_uncertainty_mps": math.nan},
                                     {"horizon_s": 3}, {"provenance": " "}])
def test_bounds_require_explicit_finite_inputs_and_provenance(kwargs):
    with pytest.raises(ValueError):
        replace(POLICY, **kwargs)


def test_validated_sensor_ingress_revokes_snapshot_authority_for_lateral_crossing():
    service = MobilityService.synthetic_fixture()
    service.crossing_policies["V01"] = POLICY
    runtime = service.vehicle_runtime["V01"]
    for t, x in [(0, 6.2), (0.1, 6)]:
        current = frame(t, x).model_copy(update={"map_version": service.graph.map_version})
        service.record_ego_localization(EgoLocalization(
            vehicle_id="V01", session_id="test", map_version=current.map_version,
            observed_tick=current.ego_pose_tick, position=MapPosition(x=0, y=0, z=0),
            heading_rad=0, speed_mps=0.4,
        ))
        service.record_sensor_observation(current)
    vehicle = make_snapshot(service, "PC_Operator", None, 1)["vehicles"][0]
    assert vehicle["motionState"] == "EMERGENCY_STOP"
    assert vehicle["reason"] == "OBSTACLE_STOP"
    assert runtime.speed_mps == 0.4  # Never fake instantaneous physical braking.
    assert runtime.sensor_clear_since_s is None
