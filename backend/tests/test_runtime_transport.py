import json

import pytest
from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from campus_sim.transport.demo import ego_payload, message, observation_payload, run_roundtrip
from campus_sim.transport.gateway import Principal, RunSession, create_runtime_app
from campus_sim.transport.protocol import RuntimeCodec

TOKEN = "local-test-token-12345"
HEADERS = {"Authorization": "Bearer " + TOKEN}
PATH = "/experimental/runtime/ws"


def session_ready():
    session = RunSession("test-run", Principal("SIMULATION", "V01"), clock=lambda: 0.0)
    session.handle_message(json.dumps(message("hello", {"role": "SIMULATION", "vehicle_id": "V01"})))
    return session


def handle(session, kind, payload, **kwargs):
    return session.handle_message(json.dumps(message(
        kind, payload, session.run_id, session_id=session.session_id, **kwargs)))


def test_roundtrip_and_cleanup():
    app = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")})
    with TestClient(app) as client, client.websocket_connect(PATH, headers=HEADERS) as socket:
        result = run_roundtrip(socket.send_json, socket.receive_json)
        assert result["status"] == "ok"
        assert result["source_observation_count"] == 2
        assert result["physical_application_verified"] is False
    assert app.state.sessions == {}
    assert [r["status"] for r in app.state.result_log] == ["VALIDATED", "APPLIED"]


@pytest.mark.parametrize("headers", [{}, {"Authorization": "Bearer wrong"},
                                      {**HEADERS, "Origin": "https://example.com"}])
def test_auth_rejected(headers):
    with (TestClient(create_runtime_app({TOKEN: Principal("SIMULATION", "V01")})) as client,
          pytest.raises(WebSocketDisconnect),
          client.websocket_connect(PATH, headers=headers)):
        pass


@pytest.mark.parametrize("mutation", [
    lambda m: m.update(schema_version=3),
    lambda m: m.update(schema_version=4.0),
    lambda m: m.update(seq=True),
    lambda m: m.update(extra_field=1),
    lambda m: m["payload"].update(speed_mps=float("nan")),
    lambda m: m["payload"].update(speed_mps=-1.0),
    lambda m: m["payload"].update(actual_target_position={}),
])
def test_strict_codec(mutation):
    data = message("ego.state", ego_payload(), "run")
    mutation(data)
    with pytest.raises(ValueError):
        RuntimeCodec.parse_message(json.dumps(data))


@pytest.mark.parametrize("raw", ['{"type":"hello","type":"ego.state"}', "[1,2]",
                                  " " * 65537, "{bad json}"],
                         ids=["duplicate-key", "array", "oversize", "malformed"])
def test_invalid_json(raw):
    with pytest.raises(ValueError):
        RuntimeCodec.parse_message(raw)


def test_mobile_cannot_publish_or_claim_simulation():
    s = RunSession("run", Principal("MOBILE", None))
    with pytest.raises(ValueError, match="PERMISSION"):
        s.handle_message(json.dumps(message("hello", {"role": "SIMULATION", "vehicle_id": "V01"})))
    s.handle_message(json.dumps(message("hello", {"role": "MOBILE", "vehicle_id": None})))
    with pytest.raises(ValueError, match="FORBIDDEN"):
        handle(s, "ego.state", ego_payload())


def test_old_run_other_vehicle_and_duplicate_seq():
    s = session_ready()
    with pytest.raises(ValueError, match="RUN_MISMATCH"):
        s.handle_message(json.dumps(message("ego.state", ego_payload(), "old-run")))
    with pytest.raises(ValueError, match="FORBIDDEN"):
        handle(s, "ego.state", {**ego_payload(), "vehicle_id": "V02"})
    handle(s, "ego.state", ego_payload())
    with pytest.raises(ValueError, match="STALE_SEQUENCE"):
        handle(s, "ego.state", ego_payload())


@pytest.mark.parametrize("changes", [{"ego_pose_version": 199}, {"observed_tick": 79},
                                      {"observed_time_s": 3.9}, {"hits": None},
                                      {"sensor_type": "UNKNOWN"}])
def test_observation_mismatch(changes):
    s = session_ready()
    handle(s, "ego.state", ego_payload())
    with pytest.raises(ValueError):
        handle(s, "sensor.observation", {**observation_payload(), **changes})
    assert s.buffer.scans == {}


def test_missing_and_invalid_observations_never_enable_motion():
    s = session_ready()
    command = handle(s, "ego.state", ego_payload())[1]["payload"]
    assert command["source_observations"] == []
    assert command["reason_code"] == "MISSING_OBSERVATION"
    command = handle(s, "sensor.observation", {
        **observation_payload(), "validity": "INVALID", "hits": []})[1]["payload"]
    assert command["brake_01"] == 1.0 and command["throttle_01"] == 0.0


def test_buffers_bounded_and_stale_observation():
    s = session_ready()
    handle(s, "ego.state", ego_payload())
    for i in range(8):
        handle(s, "sensor.observation", {**observation_payload(), "sensor_id": f"s{i}"})
    with pytest.raises(ValueError, match="SENSOR_LIMIT"):
        handle(s, "sensor.observation", {**observation_payload(), "sensor_id": "s9"})
    assert len(s.buffer.scans) == 8 and len(s.pending) == 1
    s.clock = lambda: 2.0
    with pytest.raises(ValueError, match="STALE_OBSERVATION"):
        handle(s, "sensor.observation", observation_payload() | {"sensor_id": "s0"}, seq=2)


def result_payload(command, status="VALIDATED"):
    return {"vehicle_id": "V01", "command_id": command["command_id"], "status": status,
            "applied_tick": 80 if status == "APPLIED" else None, "rejection_code": None}


def test_validated_is_not_applied_and_result_idempotency():
    s = session_ready()
    cmd = handle(s, "ego.state", ego_payload())[1]["payload"]
    with pytest.raises(ValueError, match="INVALID_APPLICATION"):
        handle(s, "control.result", result_payload(cmd, "APPLIED"))
    handle(s, "control.result", result_payload(cmd))
    assert s.results[-1]["status"] == "VALIDATED"
    with pytest.raises(ValueError, match="DUPLICATE_RESULT"):
        handle(s, "control.result", result_payload(cmd), seq=2)
    handle(s, "control.result", result_payload(cmd, "APPLIED"), seq=2)
    with pytest.raises(ValueError, match="UNKNOWN_OR_FINISHED"):
        handle(s, "control.result", result_payload(cmd, "APPLIED"), seq=3)


@pytest.mark.parametrize("wall_expired", [True, False])
def test_expiry_uses_wall_and_simulation_time(wall_expired):
    s = session_ready()
    cmd = handle(s, "ego.state", ego_payload())[1]["payload"]
    if wall_expired:
        s.clock = lambda: 0.21
    with pytest.raises(ValueError, match="EXPIRED_COMMAND"):
        handle(s, "control.result", result_payload(cmd), time_s=4.0 if wall_expired else 4.21)


def test_reconnect_cannot_apply_previous_command():
    old = session_ready()
    cmd = handle(old, "ego.state", ego_payload())[1]["payload"]
    new = session_ready()
    assert old.session_id != new.session_id
    with pytest.raises(ValueError, match="UNKNOWN_OR_FINISHED"):
        handle(new, "control.result", result_payload(cmd))
    assert new.buffer.ego is None


def test_socket_rejects_duplicate_owner_and_bad_input():
    app = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")})
    with TestClient(app) as client, client.websocket_connect(PATH, headers=HEADERS) as socket:
        with pytest.raises(WebSocketDisconnect), client.websocket_connect(PATH, headers=HEADERS):
            pass
        socket.send_text("not json")
        assert socket.receive_json()["type"] == "error"
        with pytest.raises(WebSocketDisconnect):
            socket.receive_json()


def test_idle_socket_closes():
    app = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")}, timeout_s=0.05)
    with (TestClient(app) as client, client.websocket_connect(PATH, headers=HEADERS) as socket,
          pytest.raises(WebSocketDisconnect)):
        socket.receive_json()
