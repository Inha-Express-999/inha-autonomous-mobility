import copy
import json
import math

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from campus_sim.api import create_app
from campus_sim.domain import EgoLocalization, MapPosition, SensorObservation
from campus_sim.realtime import MAX_MESSAGE_BYTES
from campus_sim.service import MobilityService


def scan():
    return {
        "vehicleId": "V01", "sensorId": "lidar", "sensorType": "LIDAR_2D",
        "sessionId": "scan-test", "mapVersion": "synthetic-test", "observedTick": 1,
        "egoPoseTick": 1, "valid": True, "observedTimeS": 0.1,
        "sensorPositionM": {"x": 0, "y": 0, "z": 0}, "sensorHeadingRad": 0,
        "rayMaxRangeM": 30,
        "rays": [{"bearingRad": -1, "outcome": "MISS", "rangeM": 30},
                 {"bearingRad": 0, "outcome": "HIT", "rangeM": 2}],
        "detections": [{"rangeM": 2, "bearingRad": 0,
                        "localPositionM": {"x": 2, "y": 0, "z": 0},
                        "entityClass": "STATIC_OBSTACLE"}],
    }


def test_roundtrip_preserves_missing_directions_and_hit_boundary():
    frame = SensorObservation.model_validate(scan())
    assert len(frame.rays) == 2  # No interpolated sectors or synthetic rays.
    assert SensorObservation.model_validate(frame.model_dump(by_alias=True)) == frame
    legacy = scan()
    del legacy["rays"], legacy["rayMaxRangeM"]
    assert not SensorObservation.model_validate(legacy).rays


@pytest.mark.parametrize("change", [
    {"valid": True, "rays": [{"bearingRad": 0, "outcome": "INVALID"}]},
    {"rays": []}, {"rayMaxRangeM": None}, {"rayMaxRangeM": float("nan")},
    {"rayMaxRangeM": 1}, {"sensorType": "RADAR"}, {"observedTimeS": None},
    {"detections": []},
    {"rays": [{"bearingRad": 0, "outcome": "MISS", "rangeM": 2}]},
    {"rays": [{"bearingRad": 0, "outcome": "HIT", "rangeM": 3}]},
    {"rays": [{"bearingRad": 0, "outcome": "INVALID", "rangeM": 30}]},
    {"rays": [{"bearingRad": 0, "outcome": "HIT"}]},
    {"rays": [{"bearingRad": 0, "outcome": "MISS", "rangeM": 30}] * 65},
])
def test_inconsistent_scan_is_rejected(change):
    payload = scan() | copy.deepcopy(change)
    with pytest.raises(ValidationError):
        SensorObservation.model_validate(payload)


def test_duplicate_or_reversed_rays_are_rejected():
    for bearings in ((0, 0), (0.1, 0)):
        payload = scan()
        for ray, bearing in zip(payload["rays"], bearings):
            ray["bearingRad"] = bearing
        with pytest.raises(ValidationError):
            SensorObservation.model_validate(payload)


def test_buffer_failure_can_be_reported_but_never_as_valid_scan():
    payload = scan()
    payload["valid"] = False
    payload["rays"].insert(1, {"bearingRad": -0.5, "outcome": "INVALID"})
    frame = SensorObservation.model_validate(payload)
    assert frame.rays[1].range_m is None
    assert not frame.valid


def test_full_64_hit_frame_fits_bounded_transport_and_reaches_service():
    service = MobilityService.synthetic_fixture()
    service.record_ego_localization(EgoLocalization(
        vehicle_id="V01", session_id="scan-test", observed_tick=1,
        map_version=service.graph.map_version, position=MapPosition(x=0, y=0, z=0),
        heading_rad=0, speed_mps=0,
    ))
    payload = scan() | {"mapVersion": service.graph.map_version, "rays": [], "detections": []}
    for index in range(64):
        bearing = -3 + index * 6 / 63
        payload["rays"].append({"bearingRad": bearing, "outcome": "HIT", "rangeM": 2})
        payload["detections"].append({
            "rangeM": 2, "bearingRad": bearing, "entityClass": "STATIC_OBSTACLE",
            "entityId": str(index).zfill(128),
            "localPositionM": {"x": 2 * math.cos(bearing), "y": 2 * math.sin(bearing), "z": 0},
        })
    wire = json.dumps(payload | {"type": "sensor_observation"})
    assert 16 * 1024 < len(wire.encode()) < MAX_MESSAGE_BYTES
    with TestClient(create_app(service)) as client, client.websocket_connect("/v1/client/ws") as ws:
        ws.send_json({"type": "subscribe", "schemaVersion": 3, "projectVersion": "0.3.1.0",
                      "role": "PC_Operator"})
        assert ws.receive_json()["type"] == "connected"
        ws.send_text(wire)
        for _ in range(20):
            response = ws.receive_json()
            if response["type"] == "sensor_observation_ack":
                assert response["accepted"]
                break
        else:
            pytest.fail("No sensor ACK")
    assert len(service.sensor_observations[("V01", "lidar")].rays) == 64
