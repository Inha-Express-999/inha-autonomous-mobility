"""FakeSimulationPeer: repeatable STOP-only round trip, never a Physics test."""

import argparse
import json
import os
from uuid import uuid4

from campus_sim import __version__


def message(kind, payload, run_id=None, seq=1, tick=80, time_s=4.0, session_id=None):
    return {"type": kind, "schema_version": 4, "project_version": __version__,
            "run_id": run_id, "session_id": session_id, "message_id": str(uuid4()), "seq": seq,
            "simulation_tick": tick, "simulation_time_s": time_s, "payload": payload}


def ego_payload():
    return {"vehicle_id": "V01", "pose_version": 200,
            "position": {"east": 0.0, "north": 0.0, "height": 0.0},
            "heading_rad": 0.0, "speed_mps": 0.0,
            "velocity": {"east": 0.0, "north": 0.0, "height": 0.0},
            "last_applied_command_id": None}


def observation_payload(sensor_type="LIDAR"):
    hit = {"beam_id": 0, "range_m": 8.0, "bearing_rad": 0.0,
           "local_position": {"right": 0.0, "forward": 8.0, "up": 0.0}}
    if sensor_type == "RADAR":
        hit["radial_relative_speed_mps"] = -2.0
    return {"vehicle_id": "V01", "sensor_id": sensor_type.lower(),
            "sensor_type": sensor_type, "sensor_config_version": "draft-test-1",
            "scan_id": "scan-" + sensor_type.lower(), "observed_tick": 80,
            "observed_time_s": 4.0, "ego_pose_version": 200,
            "validity": "VALID", "hits": [hit]}


def run_roundtrip(send, receive):
    """Callbacks exchange dictionaries, allowing ASGI and real socket tests."""
    send(message("hello", {"role": "SIMULATION", "vehicle_id": "V01"}))
    welcome = receive()
    assert welcome["type"] == "welcome", welcome
    run_id = welcome["run_id"]
    session_id = welcome["payload"]["session_id"]
    command = None
    for kind, payload in [("ego.state", ego_payload()),
                          ("sensor.observation", observation_payload()),
                          ("sensor.observation", observation_payload("RADAR"))]:
        send(message(kind, payload, run_id, session_id=session_id))
        ack, reply = receive(), receive()
        assert ack["type"] == "ack" and ack["payload"]["accepted"], ack
        assert reply["type"] == "control.command", reply
        command = reply["payload"]
        assert command["brake_01"] == 1.0 and command["throttle_01"] == 0.0
    for seq, status in enumerate(("VALIDATED", "APPLIED"), 1):
        send(message("control.result", {
            "vehicle_id": "V01", "command_id": command["command_id"], "status": status,
            "applied_tick": 80 if status == "APPLIED" else None, "rejection_code": None,
        }, run_id, seq=seq, session_id=session_id))
        ack = receive()
        assert ack["type"] == "ack" and ack["payload"]["accepted"], ack
    return {"status": "ok", "run_id": run_id, "session_id": welcome["payload"]["session_id"],
            "vehicle_id": "V01", "command_id": command["command_id"],
            "source_observation_count": len(command["source_observations"]),
            "result": "APPLIED", "evidence": "FAKE_PEER_TRANSPORT_ONLY",
            "physical_application_verified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", help="optional ws://127.0.0.1:8766/experimental/runtime/ws")
    args = parser.parse_args()
    if args.url:
        from urllib.parse import urlparse

        from websockets.sync.client import connect

        url = urlparse(args.url)
        if url.scheme != "ws" or url.hostname not in {"127.0.0.1", "localhost", "::1"}:
            parser.error("this experimental demo only permits local WebSocket URLs")
        token = os.environ.get("CAMPUS_RUNTIME_TEST_TOKEN")
        if not token:
            parser.error("set CAMPUS_RUNTIME_TEST_TOKEN for the local server")
        with connect(args.url, additional_headers={"Authorization": "Bearer " + token},
                     open_timeout=5, max_size=65536) as socket:
            result = run_roundtrip(lambda data: socket.send(json.dumps(data)),
                                   lambda: json.loads(socket.recv(timeout=5)))
    else:
        from fastapi.testclient import TestClient

        from campus_sim.transport.gateway import Principal, create_runtime_app

        token = "in-process-test-" + str(uuid4())
        app = create_runtime_app({token: Principal("SIMULATION", "V01")})
        with (TestClient(app) as client,
              client.websocket_connect("/experimental/runtime/ws", headers={
                  "Authorization": "Bearer " + token}) as socket):
            result = run_roundtrip(socket.send_json, socket.receive_json)
        result["transport"] = "IN_PROCESS_ASGI_WEBSOCKET"
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
