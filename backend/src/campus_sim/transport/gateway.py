"""Local STOP-only draft gateway. No service mutation, route planning or driving authority."""

import asyncio
import hmac
import math
import os
import time
from collections import deque
from dataclasses import dataclass
from uuid import uuid4

from fastapi import FastAPI, WebSocket, WebSocketDisconnect

from campus_sim import __version__
from campus_sim.transport.protocol import Ego, Hello, Observation, Result, RuntimeCodec


@dataclass(frozen=True)
class Principal:
    role: str
    vehicle_id: str | None


class ObservationBuffer:
    """One latest ego and at most eight latest scans; never substitutes missing data."""

    def __init__(self):
        self.ego = None
        self.scans = {}
        self.tick = -1
        self.time_s = -1.0

    def put_ego(self, envelope, ego):
        if (self.ego is not None and ego.pose_version <= self.ego.pose_version
                or envelope.simulation_tick <= self.tick
                or envelope.simulation_time_s < self.time_s):
            raise ValueError("STALE_EGO")
        self.ego = ego
        self.tick, self.time_s = envelope.simulation_tick, envelope.simulation_time_s
        self.scans.clear()

    def put_scan(self, envelope, scan):
        if (self.ego is None or scan.vehicle_id != self.ego.vehicle_id
                or scan.ego_pose_version != self.ego.pose_version
                or scan.observed_tick != self.tick
                or scan.observed_time_s != self.time_s
                or envelope.simulation_tick != self.tick
                or envelope.simulation_time_s != self.time_s):
            raise ValueError("POSE_OR_TIME_MISMATCH")
        if scan.sensor_id not in self.scans and len(self.scans) >= 8:
            raise ValueError("SENSOR_LIMIT")
        if scan.sensor_id in self.scans:
            raise ValueError("DUPLICATE_SCAN_FOR_POSE")
        self.scans[scan.sensor_id] = scan


class FakeController:
    """Always brakes. VALID scans do not enable motion or certify sensor safety."""

    @staticmethod
    def step(buffer, now_s):
        scans = list(buffer.scans.values())
        return {
            "vehicle_id": buffer.ego.vehicle_id,
            "command_id": str(uuid4()), "route_id": None,
            "source_observations": [
                {"sensor_id": s.sensor_id, "scan_id": s.scan_id,
                 "observed_tick": s.observed_tick} for s in scans],
            "source_pose_version": buffer.ego.pose_version,
            "issued_time_s": now_s, "valid_until_s": now_s + 0.2,
            "steer_angle_rad": 0.0, "throttle_01": 0.0, "brake_01": 1.0,
            "motion_state": "EMERGENCY_STOP",
            "reason_code": "MISSING_OBSERVATION" if not scans else "TEST_STOP_ONLY",
        }


class RunSession:
    def __init__(self, run_id, principal, *, clock=time.monotonic):
        self.run_id = run_id
        self.principal = principal
        self.session_id = str(uuid4())
        self.clock = clock
        self.ready = False
        self.sequences = {}
        self.buffer = ObservationBuffer()
        self.pending = {}
        self.results = deque(maxlen=64)
        self.output_seq = 0
        self.ego_received_at = None
        self.last_time = -1.0
        self.last_tick = -1

    def response(self, kind, payload, incoming):
        self.output_seq += 1
        return {"type": kind, "schema_version": 4, "project_version": __version__,
                "run_id": self.run_id, "session_id": self.session_id,
                "message_id": str(uuid4()), "seq": self.output_seq,
                "simulation_tick": incoming.simulation_tick,
                "simulation_time_s": incoming.simulation_time_s, "payload": payload}

    def handle_message(self, raw):
        envelope, payload = RuntimeCodec.parse_message(raw)
        if not self.ready:
            if (not isinstance(payload, Hello) or envelope.run_id is not None
                    or envelope.session_id is not None
                    or payload.role != self.principal.role
                    or payload.vehicle_id != self.principal.vehicle_id):
                raise ValueError("HELLO_OR_PERMISSION_MISMATCH")
            self.ready = True
            return [self.response("welcome", {
                "session_id": self.session_id, "role": self.principal.role,
                "vehicle_id": self.principal.vehicle_id, "mode": "FAKE_STOP_ONLY",
                "physical_application_verified": False,
            }, envelope)]
        if isinstance(payload, Hello) or envelope.run_id != self.run_id:
            raise ValueError("RUN_MISMATCH")
        if envelope.session_id != self.session_id:
            raise ValueError("SESSION_MISMATCH")
        if self.principal.role != "SIMULATION" or payload.vehicle_id != self.principal.vehicle_id:
            raise ValueError("FORBIDDEN")
        key = (envelope.type, getattr(payload, "sensor_id", ""))
        if (isinstance(payload, Observation) and key not in self.sequences
                and sum(k[0] == "sensor.observation" for k in self.sequences) >= 8):
            raise ValueError("SENSOR_LIMIT")
        if envelope.seq <= self.sequences.get(key, -1):
            raise ValueError("STALE_SEQUENCE")
        if envelope.simulation_time_s < self.last_time or envelope.simulation_tick < self.last_tick:
            raise ValueError("TIME_REGRESSION")
        now = self.clock()
        if isinstance(payload, Ego):
            self.buffer.put_ego(envelope, payload)
            self.ego_received_at = now
        elif isinstance(payload, Observation):
            if self.ego_received_at is None or now - self.ego_received_at > 1.0:
                raise ValueError("STALE_OBSERVATION")
            self.buffer.put_scan(envelope, payload)
        elif isinstance(payload, Result):
            record = self.pending.get(payload.command_id)
            if record is None:
                raise ValueError("UNKNOWN_OR_FINISHED_COMMAND")
            command, deadline, previous = record
            if (payload.status != "REJECTED"
                    and (now >= deadline or envelope.simulation_time_s >= command["valid_until_s"]
                         or envelope.simulation_time_s < command["issued_time_s"])):
                raise ValueError("EXPIRED_COMMAND")
            if payload.status == "VALIDATED" and previous is not None:
                raise ValueError("DUPLICATE_RESULT")
            if payload.status == "APPLIED" and (
                    previous != "VALIDATED" or payload.applied_tick != envelope.simulation_tick):
                raise ValueError("INVALID_APPLICATION_RESULT")
            self.results.append({"run_id": self.run_id, "session_id": self.session_id,
                                 **payload.model_dump(), "physical_application_verified": False})
            if payload.status in {"APPLIED", "REJECTED"}:
                del self.pending[payload.command_id]
            else:
                self.pending[payload.command_id] = command, deadline, payload.status
        self.sequences[key] = envelope.seq
        self.last_time, self.last_tick = envelope.simulation_time_s, envelope.simulation_tick
        replies = [self.response("ack", {"message_id": envelope.message_id,
                                          "accepted": True}, envelope)]
        if isinstance(payload, (Ego, Observation)):
            # Only the newest stop command is eligible; old commands are never replayed.
            self.pending.clear()
            command = FakeController.step(self.buffer, envelope.simulation_time_s)
            self.pending[command["command_id"]] = command, now + 0.2, None
            replies.append(self.response("control.command", command, envelope))
        return replies


def create_runtime_app(credentials: dict[str, Principal], *, timeout_s=5.0):
    """Explicit credentials are server-side grants, not client-supplied authority."""
    if not math.isfinite(timeout_s) or timeout_s <= 0:
        raise ValueError("timeout must be finite and positive")
    if not credentials or any(len(token) < 16 or not token.isascii() for token in credentials):
        raise ValueError("configure a test token of at least 16 characters")
    for principal in credentials.values():
        if (principal.role not in {"SIMULATION", "PC", "MOBILE"}
                or (principal.role == "SIMULATION") != (principal.vehicle_id is not None)):
            raise ValueError("invalid role/vehicle grant")
    app = FastAPI(title="Isolated schema-v4 STOP transport experiment")
    app.state.run_id = str(uuid4())
    app.state.sessions = {}
    app.state.result_log = deque(maxlen=256)
    app.state.connection_log = deque(maxlen=256)

    @app.websocket("/experimental/runtime/ws")
    async def runtime_socket(socket: WebSocket):
        bearer = socket.headers.get("authorization", "")
        principal = next((p for token, p in credentials.items()
                          if hmac.compare_digest(bearer.encode("utf-8"),
                                                 ("Bearer " + token).encode("ascii"))), None)
        if principal is None or socket.headers.get("origin") is not None:
            await socket.close(code=1008)
            return
        owner_key = principal.vehicle_id or str(uuid4())
        if owner_key in app.state.sessions or len(app.state.sessions) >= 8:
            await socket.close(code=1008)
            return
        session = RunSession(app.state.run_id, principal)
        app.state.sessions[owner_key] = session
        close_reason = "PEER_DISCONNECTED"

        def log_connection(event, reason=None):
            app.state.connection_log.append({"event": event, "reason": reason,
                "run_id": session.run_id, "session_id": session.session_id,
                "vehicle_id": principal.vehicle_id, "wall_time_s": time.monotonic()})

        try:
            await socket.accept()
            log_connection("CONNECTED")
            while True:
                raw = await asyncio.wait_for(socket.receive_text(), timeout=timeout_s)
                replies = session.handle_message(raw)
                while session.results:
                    app.state.result_log.append(session.results.popleft())
                for reply in replies:
                    await asyncio.wait_for(socket.send_text(RuntimeCodec.encode_message(reply)),
                                           timeout=timeout_s)
        except (ValueError, KeyError, RecursionError):
            close_reason = "INVALID_RUNTIME_INPUT"
            # Don't echo payloads, credentials or detailed validator input into the response.
            try:
                await asyncio.wait_for(socket.send_json({"type": "error",
                    "code": "INVALID_RUNTIME_INPUT"}), timeout=timeout_s)
                await socket.close(code=1008)
            except (WebSocketDisconnect, TimeoutError):
                pass
        except TimeoutError:
            close_reason = "IO_TIMEOUT"
            await socket.close(code=1008)
        except WebSocketDisconnect:
            pass
        finally:
            log_connection("DISCONNECTED", close_reason)
            app.state.sessions.pop(owner_key, None)
            session.pending.clear()
            session.buffer = ObservationBuffer()
    return app


def local_app():
    """uvicorn factory, intentionally separate from the v3 service on port 8765."""
    token = os.environ.get("CAMPUS_RUNTIME_TEST_TOKEN", "")
    return create_runtime_app({token: Principal("SIMULATION", "V01")})
