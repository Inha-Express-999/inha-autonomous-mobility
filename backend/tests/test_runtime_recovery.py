"""Connection lifecycle checks, not vehicle Physics tests."""
import json

import pytest
from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from campus_sim.transport.demo import ego_payload, message
from campus_sim.transport.gateway import Principal, RunSession, create_runtime_app

TOKEN = "recovery-local-test-token"
PATH = "/experimental/runtime/ws"
HEADERS = {"Authorization": "Bearer " + TOKEN}


def negotiate(socket):
    socket.send_json(message("hello", {"role": "SIMULATION", "vehicle_id": "V01"}))
    return socket.receive_json()


def bound(welcome, kind="ego.state", payload=None, **kwargs):
    return message(kind, ego_payload() if payload is None else payload,
                   welcome["run_id"], session_id=welcome["payload"]["session_id"], **kwargs)


def test_reconnect_rejects_old_session_and_accepts_fresh_input():
    app = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")})
    with TestClient(app) as client:
        with client.websocket_connect(PATH, headers=HEADERS) as first:
            old = negotiate(first)
            first.send_json(bound(old))
            first.receive_json()
            first.receive_json()
        with client.websocket_connect(PATH, headers=HEADERS) as second:
            new = negotiate(second)
            assert old["run_id"] == new["run_id"]
            assert old["payload"]["session_id"] != new["payload"]["session_id"]
            second.send_json(bound(old, seq=100))
            assert second.receive_json()["type"] == "error"
            with pytest.raises(WebSocketDisconnect):
                second.receive_json()
        with client.websocket_connect(PATH, headers=HEADERS) as third:
            current = negotiate(third)
            third.send_json(bound(current))
            assert third.receive_json()["type"] == "ack"
            stop = third.receive_json()["payload"]
            assert stop["source_observations"] == [] and stop["throttle_01"] == 0
    assert not app.state.sessions
    assert len(app.state.connection_log) == 6
    assert app.state.connection_log[3]["reason"] == "INVALID_RUNTIME_INPUT"


def test_new_server_run_rejects_old_run_with_current_session():
    first = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")})
    second = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")})
    assert first.state.run_id != second.state.run_id
    with (TestClient(second) as client,
          client.websocket_connect(PATH, headers=HEADERS) as socket):
        welcome = negotiate(socket)
        packet = bound(welcome)
        packet["run_id"] = first.state.run_id
        socket.send_json(packet)
        assert socket.receive_json()["type"] == "error"


@pytest.mark.parametrize("field", ["session_id", "run_id"])
def test_rejection_has_no_buffer_or_sequence_side_effect(field):
    session = RunSession("run", Principal("SIMULATION", "V01"))
    welcome = session.handle_message(json.dumps(message(
        "hello", {"role": "SIMULATION", "vehicle_id": "V01"})))[0]
    packet = bound(welcome)
    packet[field] = "old"
    with pytest.raises(ValueError):
        session.handle_message(json.dumps(packet))
    assert session.buffer.ego is None and session.sequences == {} and session.pending == {}


@pytest.mark.parametrize("clock,time_s", [(0.2, 4.0), (0.0, 4.2)])
def test_exact_deadline_is_expired(clock, time_s):
    session = RunSession("run", Principal("SIMULATION", "V01"), clock=lambda: 0.0)
    welcome = session.handle_message(json.dumps(message(
        "hello", {"role": "SIMULATION", "vehicle_id": "V01"})))[0]
    cmd = session.handle_message(json.dumps(bound(welcome)))[1]["payload"]
    session.clock = lambda: clock
    result = {"vehicle_id": "V01", "command_id": cmd["command_id"], "status": "VALIDATED",
              "applied_tick": None, "rejection_code": None}
    with pytest.raises(ValueError, match="EXPIRED_COMMAND"):
        session.handle_message(json.dumps(bound(welcome, "control.result", result, time_s=time_s)))


@pytest.mark.parametrize("timeout", [0.0, -1.0, float("nan"), float("inf")])
def test_invalid_timeout_config(timeout):
    with pytest.raises(ValueError):
        create_runtime_app({TOKEN: Principal("SIMULATION", "V01")}, timeout_s=timeout)


def test_idle_disconnect_records_reason_and_releases_owner():
    app = create_runtime_app({TOKEN: Principal("SIMULATION", "V01")}, timeout_s=0.05)
    with TestClient(app) as client:
        with client.websocket_connect(PATH, headers=HEADERS) as socket:
            negotiate(socket)
            with pytest.raises(WebSocketDisconnect):
                socket.receive_json()
        with client.websocket_connect(PATH, headers=HEADERS) as socket:
            assert negotiate(socket)["type"] == "welcome"
    assert any(e["reason"] == "IO_TIMEOUT" for e in app.state.connection_log)
    assert not app.state.sessions
