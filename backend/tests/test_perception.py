import pytest
from pydantic import ValidationError

from campus_sim.domain import SensorObservation
from campus_sim.perception import summarize_pedestrian_returns
from campus_sim.safety import SensorSample


def frame(ids, *, session="test", valid=True, received_at=1.0, kind="PEDESTRIAN"):
    return SensorSample(SensorObservation.model_validate({
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
        "sessionId": session, "mapVersion": "synthetic-test", "observedTick": 1,
        "egoPoseTick": 1, "valid": valid,
        "detections": [{"rangeM": 5, "bearingRad": 0,
                        "localPositionM": {"x": 5, "y": 0, "z": 0},
                        "entityClass": kind, "entityId": identity} for identity in ids],
    }), received_at)


def test_multiple_rays_and_sensors_do_not_multiply_a_visible_person():
    result = summarize_pedestrian_returns(
        [frame(["a", "a", "b", None]), frame(["a", "b"]), frame(["car"], kind="VEHICLE")],
        now_s=1.0, max_age_s=0.3,
    )
    assert result.entity_ids == {"a", "b"}
    assert result.anonymous_return_count == 1
    assert result.usable_frame_count == 3


def test_missing_invalid_old_and_future_frames_do_not_create_counts():
    result = summarize_pedestrian_returns([
        frame(["a"], valid=False), frame(["b"], received_at=0),
        frame(["c"], received_at=None), frame(["d"], received_at=2),
    ], now_s=1.0, max_age_s=0.3)
    assert not result.entity_ids
    assert result.usable_frame_count == 0


def test_identifiers_from_other_process_sessions_cannot_be_combined():
    with pytest.raises(ValueError, match="sessions/maps"):
        summarize_pedestrian_returns([frame(["a"]), frame(["a"], session="other")],
                                     now_s=1, max_age_s=0.3)


@pytest.mark.parametrize("identity", ["", " ", "x" * 129])
def test_invalid_identity_is_rejected_at_ingress(identity):
    with pytest.raises(ValidationError):
        frame([identity])
