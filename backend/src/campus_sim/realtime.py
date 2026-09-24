from __future__ import annotations

import asyncio
import json
import math
from typing import Literal
from uuid import uuid4

from fastapi import WebSocket, WebSocketDisconnect
from pydantic import BaseModel, ConfigDict, Field, ValidationError

from campus_sim.domain import CreateRequest, RequestView, ServiceNeeds
from campus_sim.service import MobilityService

PROJECT_VERSION = "0.2.2.0"
SNAPSHOT_SCHEMA_VERSION = 3
MAP_VERSION = "synthetic-service-v1"
RUN_ID = "server-" + uuid4().hex
MAX_MESSAGE_BYTES = 16 * 1024
ACTIVE_REQUEST_STATUSES = {"ASSIGNED", "PICKUP_SERVICE", "IN_TRANSIT", "DROPOFF_SERVICE"}


def _camel_case(name: str) -> str:
    first, *rest = name.split("_")
    return first + "".join(part.capitalize() for part in rest)


class SubscribeMessage(BaseModel):
    model_config = ConfigDict(alias_generator=_camel_case, populate_by_name=True, extra="forbid")

    type: str = Field(pattern="^subscribe$")
    schema_version: int
    project_version: str = Field(pattern=r"^\d+(\.\d+){3}$")
    role: str = Field(pattern="^(PC_Operator|Mobile_Passenger)$")
    subscriber_id: str | None = Field(default=None, max_length=128)


class ClientCommandBase(BaseModel):
    model_config = ConfigDict(alias_generator=_camel_case, populate_by_name=True, extra="forbid")

    type: str
    message_id: str = Field(min_length=1, max_length=118)


class CreatePassengerRequestMessage(ClientCommandBase):
    type: Literal["create_request"]
    service_type: Literal["PASSENGER"] = "PASSENGER"
    pickup_landmark_id: str = Field(min_length=1)
    dropoff_landmark_id: str = Field(min_length=1)
    service_needs: ServiceNeeds = Field(default_factory=ServiceNeeds)
    party_size: int = Field(default=1, ge=1)
    latest_arrival_s: float | None = Field(default=None, ge=0)


class CancelRequestMessage(ClientCommandBase):
    type: Literal["cancel_request"]
    request_id: str = Field(min_length=1)


async def serve_client_socket(websocket: WebSocket, service: MobilityService) -> None:
    """Prototype snapshot feed. It intentionally exposes synthetic service state only."""
    await websocket.accept()
    try:
        try:
            raw = await asyncio.wait_for(websocket.receive_text(), timeout=5.0)
        except TimeoutError:
            await _send_error(websocket, "subscribe_timeout")
            await websocket.close(code=1008)
            return
        if len(raw.encode("utf-8")) > MAX_MESSAGE_BYTES:
            await _send_error(websocket, "message_too_large")
            await websocket.close(code=1009)
            return
        try:
            subscribe = SubscribeMessage.model_validate_json(raw)
        except ValidationError:
            await _send_error(websocket, "invalid_subscribe")
            await websocket.close(code=1008)
            return
        if subscribe.schema_version != SNAPSHOT_SCHEMA_VERSION:
            await _send_error(websocket, "unsupported_schema_version")
            await websocket.close(code=1008)
            return
        if subscribe.role == "Mobile_Passenger" and not subscribe.subscriber_id:
            await _send_error(websocket, "subscriber_required")
            await websocket.close(code=1008)
            return
        if subscribe.role == "PC_Operator" and subscribe.subscriber_id is not None:
            await _send_error(websocket, "subscriber_not_allowed_for_operator")
            await websocket.close(code=1008)
            return

        await websocket.send_json(
            {
                "type": "connected",
                "schemaVersion": SNAPSHOT_SCHEMA_VERSION,
                "projectVersion": PROJECT_VERSION,
                "runId": RUN_ID,
                "authenticated": False,
            }
        )
        sequence = 0
        while True:
            try:
                incoming = await asyncio.wait_for(websocket.receive_text(), timeout=0.5)
                if len(incoming.encode("utf-8")) > MAX_MESSAGE_BYTES:
                    await _send_error(websocket, "message_too_large")
                    await websocket.close(code=1009)
                    return
                should_close = await handle_client_message(
                    websocket, service, subscribe.role, subscribe.subscriber_id, incoming
                )
                if should_close:
                    return
            except TimeoutError:
                pass

            await websocket.send_json(
                {
                    "type": "snapshot",
                    "snapshot": make_snapshot(
                        service,
                        subscribe.role,
                        subscribe.subscriber_id,
                        sequence,
                    ),
                }
            )
            sequence += 1
    except WebSocketDisconnect:
        return


async def handle_client_message(
    websocket: WebSocket,
    service: MobilityService,
    role: str,
    subscriber_id: str | None,
    raw: str,
) -> bool:
    """Return True when the protocol/session should close after handling the message."""
    try:
        payload = json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError):
        await _send_error(websocket, "invalid_json")
        return True
    if not isinstance(payload, dict):
        await _send_error(websocket, "invalid_message")
        return True
    message_id = payload.get("messageId")
    message_type = payload.get("type")
    command_type = message_type if isinstance(message_type, str) and len(message_type) <= 32 else "unknown"
    if message_type == "ping":
        await websocket.send_json({"type": "pong"})
        return False
    if not isinstance(message_id, str) or not message_id or len(message_id) > 118:
        await _send_error(websocket, "message_id_required")
        return True
    if role != "Mobile_Passenger":
        await _send_command_ack(
            websocket, message_id, command_type, False, error_code="role_not_allowed"
        )
        return False

    try:
        if message_type == "create_request":
            command = CreatePassengerRequestMessage.model_validate(payload)
            service_command = CreateRequest(
                command_id=f"ws:create:{command.message_id}",
                owner_id=subscriber_id,
                service_type=command.service_type,
                pickup_landmark_id=command.pickup_landmark_id,
                dropoff_landmark_id=command.dropoff_landmark_id,
                service_needs=command.service_needs,
                party_size=command.party_size,
                cargo_kg=0,
                latest_arrival_s=command.latest_arrival_s,
            )
            ack = service.create_request(service_command)
            await _send_command_ack(
                websocket,
                command.message_id,
                command.type,
                ack.accepted,
                request=ack.request,
            )
            return False
        if message_type == "cancel_request":
            command = CancelRequestMessage.model_validate(payload)
            ack = service.cancel_request(
                command.request_id,
                f"ws:cancel:{command.message_id}",
                subscriber_id,
            )
            await _send_command_ack(
                websocket,
                command.message_id,
                command.type,
                ack.accepted,
                request=ack.request,
            )
            return False
        await _send_command_ack(
            websocket, message_id, command_type, False, error_code="unsupported_command"
        )
        return False
    except ValidationError:
        await _send_command_ack(
            websocket, message_id, command_type, False, error_code="invalid_request"
        )
    except KeyError:
        await _send_command_ack(
            websocket, message_id, command_type, False, error_code="request_not_found"
        )
    except ValueError as error:
        code = "cancel_not_allowed" if command_type == "cancel_request" else "request_rejected"
        await _send_command_ack(
            websocket,
            message_id,
            command_type,
            False,
            error_code=code,
            error_message=str(error),
        )
    return False


async def _send_command_ack(
    websocket: WebSocket,
    message_id: str,
    command_type: str,
    accepted: bool,
    request: RequestView | None = None,
    error_code: str | None = None,
    error_message: str | None = None,
) -> None:
    await websocket.send_json(
        {
            "type": "command_ack",
            "messageId": message_id,
            "commandType": command_type,
            "accepted": accepted,
            "request": _camelize(request.model_dump(mode="json")) if request else None,
            "errorCode": error_code,
            "errorMessage": error_message,
        }
    )


def make_snapshot(
    service: MobilityService, role: str, subscriber_id: str | None, sequence: int
) -> dict:
    """Build the C# schema-v3 presentation DTO from current in-memory service state."""
    service.advance()
    now_s = service.now_s()
    visible_requests = list(service.requests.values())
    if role == "Mobile_Passenger":
        visible_requests = [item for item in visible_requests if item.owner_id == subscriber_id]

    visible_stop_ids = {
        stop_id
        for request in visible_requests
        for stop_id in (request.pickup_stop_id, request.dropoff_stop_id)
        if stop_id is not None
    }
    operator = role == "PC_Operator"
    landmarks = []
    stops = []
    node_by_landmark = {
        node.landmark_id: node for node in service.graph.nodes
    } if service.graph is not None else {}
    for landmark in service.landmarks.values():
        stop_ids = landmark.stop_ids if operator else [
            stop_id for stop_id in landmark.stop_ids if stop_id in visible_stop_ids
        ]
        landmarks.append(
            {
                "id": landmark.id,
                "name": landmark.name,
                "campusId": "synthetic",
                "verification": "SYNTHETIC",
                "stopIds": stop_ids,
            }
        )
        for stop_id in stop_ids:
            stop = service.stops[stop_id]
            node = node_by_landmark.get(landmark.id)
            x = node.position_m.x if node else 0.0
            y = node.position_m.y if node else 0.0
            stops.append(
                {
                    "id": stop.id,
                    "landmarkId": stop.landmark_id,
                    "entranceId": "synthetic-" + stop.id,
                    "position": {"x": x, "y": y, "z": 0.0},
                    "stepFreeAccess": stop.step_free_access,
                    "verification": "SYNTHETIC",
                }
            )

    visible_vehicle_ids = {
        request.vehicle_id
        for request in visible_requests
        if request.vehicle_id is not None and request.status.value in ACTIVE_REQUEST_STATUSES
    }
    visible_vehicles = [
        vehicle
        for vehicle in service.vehicles.values()
        if operator or vehicle.id in visible_vehicle_ids
    ]
    requests_by_vehicle = {
        request.vehicle_id: request
        for request in visible_requests
        if request.vehicle_id is not None and request.status.value in ACTIVE_REQUEST_STATUSES
    }
    vehicles = []
    for vehicle in visible_vehicles:
        runtime = service.vehicle_runtime.get(vehicle.id)
        request = requests_by_vehicle.get(vehicle.id)
        x = runtime.x if runtime else 0.0
        y = runtime.y if runtime else 0.0
        next_point = runtime.route_points[1] if runtime and len(runtime.route_points) > 1 else None
        heading = (
            math.atan2(next_point[0] - x, next_point[1] - y)
            if next_point else 0.0
        )
        vehicles.append(
            {
                "id": vehicle.id,
                "position": {"x": x, "y": y, "z": 0.0},
                "headingRad": heading,
                "speedMps": runtime.speed_mps if runtime else 0.0,
                "batteryWh": None,
                "missionState": runtime.mission_state if runtime else "IDLE",
                "motionState": "DRIVING" if runtime and runtime.speed_mps > 0 else "WAITING_RESOURCE",
                "reason": "UNKNOWN",
                "requestId": request.id if request else None,
                "routeId": runtime.route_id if runtime else None,
            }
        )

    request_models: list[RequestView] = visible_requests
    return {
        "schemaVersion": SNAPSHOT_SCHEMA_VERSION,
        "projectVersion": PROJECT_VERSION,
        "mapVersion": service.graph.map_version if service.graph else MAP_VERSION,
        "runId": RUN_ID,
        "sequence": sequence,
        "simulationTick": int(now_s * 20),
        "simulationTimeS": now_s,
        "role": role,
        "subscriberId": subscriber_id,
        "vehicles": vehicles,
        "requests": [_camelize(item.model_dump(mode="json")) for item in request_models],
        "landmarks": landmarks,
        "stops": stops,
        "routes": [
            {
                "id": runtime.route_id,
                "mapVersion": service.graph.map_version if service.graph else MAP_VERSION,
                "polyline": [
                    {"x": x, "y": y, "z": 0.0}
                    for x, y in runtime.route_points
                ],
                "reason": "UNKNOWN",
            }
            for vehicle in visible_vehicles
            if (runtime := service.vehicle_runtime.get(vehicle.id)) is not None
            and runtime.route_id is not None
        ],
        "zones": [],
    }


def _camelize(value):
    if isinstance(value, list):
        return [_camelize(item) for item in value]
    if isinstance(value, dict):
        return {_camel_case(key): _camelize(item) for key, item in value.items()}
    return value


async def _send_error(websocket: WebSocket, code: str) -> None:
    await websocket.send_json({"type": "error", "code": code})
