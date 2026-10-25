"""Repeatable connection recovery demonstration; uses an in-process fake peer."""
import json
from uuid import uuid4

from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from campus_sim.transport.demo import ego_payload, message
from campus_sim.transport.gateway import Principal, create_runtime_app


def main():
    token = str(uuid4())
    app = create_runtime_app({token: Principal("SIMULATION", "V01")}, timeout_s=0.15)
    headers = {"Authorization": "Bearer " + token}
    path = "/experimental/runtime/ws"
    checks = []

    def hello(socket):
        socket.send_json(message("hello", {"role": "SIMULATION", "vehicle_id": "V01"}))
        return socket.receive_json()

    with TestClient(app) as client:
        with client.websocket_connect(path, headers=headers) as socket:
            old = hello(socket)
            try:
                socket.receive_json()
            except WebSocketDisconnect:
                checks.append("idle_connection_closed")
            else:
                raise RuntimeError("expected idle disconnection")
        with client.websocket_connect(path, headers=headers) as socket:
            current = hello(socket)
            if current["payload"]["session_id"] == old["payload"]["session_id"]:
                raise RuntimeError("session reused")
            socket.send_json(message("ego.state", ego_payload(), old["run_id"], seq=100,
                                     session_id=old["payload"]["session_id"]))
            if socket.receive_json().get("type") != "error":
                raise RuntimeError("old session accepted")
            checks.append("old_session_input_rejected")
        with client.websocket_connect(path, headers=headers) as socket:
            current = hello(socket)
            socket.send_json(message("ego.state", ego_payload(), current["run_id"],
                                     session_id=current["payload"]["session_id"]))
            ack, stop = socket.receive_json(), socket.receive_json()
            if (ack.get("type") != "ack" or stop["payload"]["throttle_01"] != 0
                    or stop["payload"]["source_observations"] != []):
                raise RuntimeError("fresh connection did not start safely")
            checks.append("fresh_session_accepts_new_ego_and_returns_stop")
    if app.state.sessions:
        raise RuntimeError("connection owner leaked")
    checks.append("disconnected_session_resources_released")
    print(json.dumps({"status": "ok", "checks": checks,
        "connection_events": list(app.state.connection_log),
        "evidence": "IN_PROCESS_ASGI_FAKE_PEER",
        "full_service_snapshot_restored": False,
        "physical_application_verified": False}, indent=2))


if __name__ == "__main__":
    main()
