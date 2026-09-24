from __future__ import annotations

from dataclasses import dataclass, field
from hashlib import sha256
from pathlib import Path
from time import monotonic
from uuid import uuid4

from campus_sim.domain import (
    CommandAck,
    CreateRequest,
    Landmark,
    ReasonCode,
    RequestStatus,
    RequestView,
    ServiceType,
    Stop,
    Vehicle,
)
from campus_sim.planning import astar, node_for_stop
from campus_sim.road_graph import RoadGraphDocument, load_road_graph


@dataclass
class VehicleRuntime:
    node_id: str
    x: float
    y: float
    route_points: list[tuple[float, float]] = field(default_factory=list)
    route_speeds: list[float] = field(default_factory=list)
    route_id: str | None = None
    mission_state: str = "IDLE"
    request_id: str | None = None
    speed_mps: float = 0.0
    service_remaining_s: float = 0.0


@dataclass
class MobilityService:
    """In-memory M0 service. It owns requests; Unity remains the physics authority."""

    started_at: float = field(default_factory=monotonic)
    landmarks: dict[str, Landmark] = field(default_factory=dict)
    stops: dict[str, Stop] = field(default_factory=dict)
    vehicles: dict[str, Vehicle] = field(default_factory=dict)
    requests: dict[str, RequestView] = field(default_factory=dict)
    commands: dict[str, CommandAck] = field(default_factory=dict)
    graph: RoadGraphDocument | None = None
    vehicle_runtime: dict[str, VehicleRuntime] = field(default_factory=dict)
    last_advance_clock: float = field(default_factory=monotonic)
    route_counter: int = 0

    @classmethod
    def synthetic_fixture(cls) -> MobilityService:
        service = cls()
        graph_path = Path(__file__).resolve().parents[3] / "maps" / "fixtures" / "campus-synthetic-6.json"
        service.graph = load_road_graph(graph_path)
        names = ["정문", "인하대역", "후문", "5호관", "2호관", "하이테크"]
        for node, name in zip(service.graph.nodes, names, strict=True):
            landmark = Landmark(id=node.landmark_id, name=name, stop_ids=[node.stop_id])
            service.landmarks[landmark.id] = landmark
            service.stops[node.stop_id] = Stop(
                id=node.stop_id,
                landmark_id=node.landmark_id,
                step_free_access=True,
            )
        for vehicle in (
            Vehicle(id="V01", passenger_capacity=4, wheelchair_slots=1, cargo_capacity_kg=0),
            Vehicle(id="V02", passenger_capacity=4, wheelchair_slots=0, cargo_capacity_kg=0),
            Vehicle(id="V03", passenger_capacity=0, wheelchair_slots=0, cargo_capacity_kg=20),
        ):
            service.vehicles[vehicle.id] = vehicle
        start = service.graph.nodes[0]
        service.vehicle_runtime["V01"] = VehicleRuntime(
            node_id=start.id,
            x=start.position_m.x,
            y=start.position_m.y,
        )
        return service

    def now_s(self) -> float:
        return round(monotonic() - self.started_at, 3)

    def create_request(self, command: CreateRequest) -> CommandAck:
        cache_key = self._command_cache_key("create", command.owner_id, command.command_id)
        cached = self.commands.get(cache_key)
        if cached is not None:
            return cached

        pickup = self._select_stop(command.pickup_landmark_id, command.service_needs.requires_step_free)
        dropoff = self._select_stop(command.dropoff_landmark_id, command.service_needs.requires_step_free)
        request = RequestView(
            id=f"req-{uuid4().hex[:12]}",
            owner_id=command.owner_id,
            service_type=command.service_type,
            status=RequestStatus.QUEUED,
            created_s=self.now_s(),
            pickup_landmark_id=command.pickup_landmark_id,
            dropoff_landmark_id=command.dropoff_landmark_id,
            pickup_stop_id=pickup.id,
            dropoff_stop_id=dropoff.id,
            service_needs=command.service_needs,
            party_size=command.party_size,
            cargo_kg=command.cargo_kg,
            latest_arrival_s=command.latest_arrival_s,
        )
        if not any(self._vehicle_can_serve(vehicle, command) for vehicle in self.vehicles.values()):
            raise ValueError("no vehicle can satisfy the request capacity requirements")
        if not any(
            vehicle.id in self.vehicle_runtime and self._vehicle_can_serve(vehicle, command)
            for vehicle in self.vehicles.values()
        ):
            raise ValueError("no active synthetic vehicle supports this request type")
        vehicle = self._select_vehicle(command)
        if vehicle is not None:
            vehicle.available = False
            request.status = RequestStatus.ASSIGNED
            request.vehicle_id = vehicle.id
            runtime = self.vehicle_runtime.get(vehicle.id)
            try:
                self._set_route(runtime, request.pickup_stop_id, request, "TO_PICKUP")
            except ValueError:
                vehicle.available = True
                request.status = RequestStatus.FAILED
                request.reason = ReasonCode.UNKNOWN
                request.vehicle_id = None
                raise
            request.eta_s = self._route_duration(runtime)
        self.requests[request.id] = request
        ack = CommandAck(command_id=command.command_id, accepted=True, request=request)
        self.commands[cache_key] = ack.model_copy(deep=True)
        return ack

    def cancel_request(self, request_id: str, command_id: str, owner_id: str) -> CommandAck:
        cache_key = self._command_cache_key("cancel", owner_id, command_id)
        cached = self.commands.get(cache_key)
        if cached is not None:
            return cached
        request = self.requests.get(request_id)
        if request is None or request.owner_id != owner_id:
            raise KeyError("request not found")
        if request.status not in {RequestStatus.QUEUED, RequestStatus.ASSIGNED}:
            raise ValueError("request cannot be cancelled in its current state")
        if request.vehicle_id:
            self.vehicles[request.vehicle_id].available = True
            runtime = self.vehicle_runtime.get(request.vehicle_id)
            if runtime is not None:
                if self.graph is not None:
                    runtime.node_id = self._nearest_node(runtime.x, runtime.y)
                runtime.route_points = []
                runtime.route_speeds = []
                runtime.route_id = None
                runtime.mission_state = "IDLE"
                runtime.request_id = None
                runtime.speed_mps = 0.0
                runtime.service_remaining_s = 0.0
        request.status = RequestStatus.CANCELLED
        request.vehicle_id = None
        request.eta_s = None
        ack = CommandAck(command_id=command_id, accepted=True, request=request)
        self.commands[cache_key] = ack.model_copy(deep=True)
        runtime = self.vehicle_runtime.get("V01")
        if runtime is not None:
            self._assign_next_queued(runtime)
        return ack

    def requests_for_owner(self, owner_id: str) -> list[RequestView]:
        return [request for request in self.requests.values() if request.owner_id == owner_id]

    def _select_stop(self, landmark_id: str, requires_step_free: bool) -> Stop:
        landmark = self.landmarks.get(landmark_id)
        if landmark is None:
            raise ValueError("unknown landmark")
        candidates = [self.stops[stop_id] for stop_id in landmark.stop_ids]
        if requires_step_free:
            candidates = [stop for stop in candidates if stop.step_free_access]
        if not candidates:
            raise ValueError(ReasonCode.NO_ACCESSIBLE_ALTERNATIVE.value)
        return candidates[0]

    def _select_vehicle(self, command: CreateRequest) -> Vehicle | None:
        for vehicle in self.vehicles.values():
            # M1 vertical slice deliberately runs one synthetic passenger vehicle.
            if vehicle.id != "V01":
                continue
            if not vehicle.available:
                continue
            if not self._vehicle_can_serve(vehicle, command):
                continue
            return vehicle
        return None

    @staticmethod
    def _vehicle_can_serve(vehicle: Vehicle, command: CreateRequest) -> bool:
        if command.service_type is ServiceType.PASSENGER:
            return (
                vehicle.passenger_capacity >= command.party_size
                and vehicle.wheelchair_slots >= command.service_needs.wheelchair_slots
            )
        return vehicle.cargo_capacity_kg >= command.cargo_kg

    @staticmethod
    def _command_cache_key(operation: str, owner_id: str, command_id: str) -> str:
        payload = f"{operation}\0{owner_id}\0{command_id}".encode("utf-8")
        return sha256(payload).hexdigest()

    def _set_route(
        self, runtime: VehicleRuntime | None, stop_id: str, request: RequestView, mission: str
    ) -> None:
        if runtime is None or self.graph is None:
            raise ValueError("synthetic route runtime is unavailable")
        start = runtime.node_id
        goal = node_for_stop(self.graph, stop_id)
        result = astar(
            self.graph,
            start,
            goal,
            requires_step_free=request.service_needs.requires_step_free,
        )
        node_by_id = {node.id: node for node in self.graph.nodes}
        start_position = node_by_id[start].position_m
        points: list[tuple[float, float]] = [(runtime.x, runtime.y)]
        speeds: list[float] = []
        if ((runtime.x - start_position.x) ** 2 + (runtime.y - start_position.y) ** 2) ** 0.5 > 0.01:
            # Synthetic recovery connector after cancellation mid-edge; not a validated road segment.
            points.append((start_position.x, start_position.y))
            speeds.append(5.0)
        edge_by_id = {edge.id: edge for edge in self.graph.edges}
        for edge_id in result.edge_ids:
            edge = edge_by_id[edge_id]
            points.extend((point.x, point.y) for point in edge.geometry_m[1:])
            speeds.extend([min(5.0, edge.allowed_speed_mps)] * (len(edge.geometry_m) - 1))
        self.route_counter += 1
        runtime.route_points = points
        runtime.route_speeds = speeds
        runtime.route_id = f"synthetic-route-{self.route_counter}"
        runtime.mission_state = mission
        runtime.request_id = request.id
        runtime.speed_mps = speeds[0] if speeds else 0.0
        runtime.node_id = start
        request.eta_s = self._route_duration(runtime)

    @staticmethod
    def _route_duration(runtime: VehicleRuntime) -> float:
        duration = 0.0
        for index, ((ax, ay), (bx, by)) in enumerate(
            zip(runtime.route_points, runtime.route_points[1:])
        ):
            speed = runtime.route_speeds[index]
            if speed <= 0:
                return 0.0
            duration += ((bx - ax) ** 2 + (by - ay) ** 2) ** 0.5 / speed
        return round(duration, 1)

    def advance(self, delta_s: float | None = None) -> None:
        """Advance the deterministic synthetic vehicle along graph geometry."""
        clock_now = monotonic()
        if delta_s is None:
            delta_s = max(0.0, min(clock_now - self.last_advance_clock, 1.0))
        self.last_advance_clock = clock_now
        if delta_s <= 0:
            return
        remaining = delta_s
        runtime = self.vehicle_runtime.get("V01")
        while runtime is not None and remaining > 1e-9:
            if runtime.service_remaining_s > 0:
                wait = runtime.service_remaining_s
                if wait > remaining:
                    runtime.service_remaining_s -= remaining
                    return
                remaining -= wait
                runtime.service_remaining_s = 0.0
                request = self.requests.get(runtime.request_id or "")
                if request is None:
                    runtime.mission_state = "IDLE"
                    runtime.request_id = None
                    continue
                if request.status is RequestStatus.PICKUP_SERVICE:
                    request.status = RequestStatus.IN_TRANSIT
                    runtime.mission_state = "TO_DROPOFF"
                    self._set_route(runtime, request.dropoff_stop_id or "", request, "TO_DROPOFF")
                    request.eta_s = self._route_duration(runtime) + 2.0
                elif request.status is RequestStatus.DROPOFF_SERVICE:
                    request.status = RequestStatus.COMPLETED
                    request.eta_s = 0.0
                    self.vehicles["V01"].available = True
                    runtime.mission_state = "IDLE"
                    runtime.request_id = None
                    runtime.route_points = []
                    runtime.route_speeds = []
                    runtime.route_id = None
                    runtime.speed_mps = 0.0
                    self._assign_next_queued(runtime)
                continue
            if len(runtime.route_points) < 2:
                request = self.requests.get(runtime.request_id or "")
                if request is not None and request.status is RequestStatus.ASSIGNED:
                    request.status = RequestStatus.PICKUP_SERVICE
                    runtime.mission_state = "PICKUP_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = 2.0
                elif request is not None and request.status is RequestStatus.IN_TRANSIT:
                    request.status = RequestStatus.DROPOFF_SERVICE
                    runtime.mission_state = "DROPOFF_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = 2.0
                else:
                    break
                continue
            if not runtime.route_speeds:
                runtime.mission_state = "IDLE"
                runtime.request_id = None
                runtime.speed_mps = 0.0
                break
            runtime.speed_mps = runtime.route_speeds[0]
            moved, consumed = self._move_along_route(runtime, remaining)
            remaining -= consumed
            if moved and len(runtime.route_points) < 2:
                runtime.node_id = self._nearest_node(runtime.x, runtime.y)
                runtime.route_points = []
                runtime.route_speeds = []
                request = self.requests.get(runtime.request_id or "")
                if request is not None and request.status is RequestStatus.ASSIGNED:
                    request.status = RequestStatus.PICKUP_SERVICE
                    runtime.mission_state = "PICKUP_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = 2.0
                elif request is not None and request.status is RequestStatus.IN_TRANSIT:
                    request.status = RequestStatus.DROPOFF_SERVICE
                    runtime.mission_state = "DROPOFF_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = 2.0
            if not moved:
                break
        for request in self.requests.values():
            if request.vehicle_id == "V01" and request.status not in {
                RequestStatus.COMPLETED, RequestStatus.CANCELLED, RequestStatus.FAILED
            }:
                request.eta_s = self._route_duration(runtime) + (2.0 if request.status in {
                    RequestStatus.ASSIGNED, RequestStatus.IN_TRANSIT
                } else 0.0)

    @staticmethod
    def _move_along_route(runtime: VehicleRuntime, delta_s: float) -> tuple[bool, float]:
        if delta_s <= 0:
            return False, 0.0
        consumed_s = 0.0
        while len(runtime.route_points) >= 2 and runtime.route_speeds:
            ax, ay = runtime.route_points[0]
            bx, by = runtime.route_points[1]
            segment = ((bx - ax) ** 2 + (by - ay) ** 2) ** 0.5
            if segment <= 1e-9:
                runtime.route_points.pop(0)
                runtime.route_speeds.pop(0)
                continue
            speed = runtime.route_speeds[0]
            if speed <= 0:
                runtime.speed_mps = 0.0
                return True, delta_s
            segment_time = segment / speed
            remaining_s = delta_s - consumed_s
            if remaining_s < segment_time:
                ratio = (remaining_s * speed) / segment
                runtime.x = ax + (bx - ax) * ratio
                runtime.y = ay + (by - ay) * ratio
                runtime.route_points[0] = (runtime.x, runtime.y)
                consumed_s = delta_s
                break
            runtime.x, runtime.y = bx, by
            consumed_s += segment_time
            runtime.route_points.pop(0)
            runtime.route_speeds.pop(0)
            if consumed_s >= delta_s:
                break
        runtime.speed_mps = runtime.route_speeds[0] if runtime.route_speeds else 0.0
        return True, consumed_s

    def _nearest_node(self, x: float, y: float) -> str:
        assert self.graph is not None
        return min(
            self.graph.nodes,
            key=lambda node: (node.position_m.x - x) ** 2 + (node.position_m.y - y) ** 2,
        ).id

    def _assign_next_queued(self, runtime: VehicleRuntime) -> None:
        request = min(
            (
                item for item in self.requests.values()
                if item.status is RequestStatus.QUEUED
                and self._request_can_vehicle_serve(self.vehicles["V01"], item)
            ),
            key=lambda item: (item.created_s, item.id),
            default=None,
        )
        if request is None:
            return
        try:
            self._set_route(runtime, request.pickup_stop_id or "", request, "TO_PICKUP")
        except ValueError:
            request.status = RequestStatus.FAILED
            request.reason = ReasonCode.UNKNOWN
            request.vehicle_id = None
            self._assign_next_queued(runtime)
            return
        request.status = RequestStatus.ASSIGNED
        request.vehicle_id = "V01"
        self.vehicles["V01"].available = False

    @staticmethod
    def _request_can_vehicle_serve(vehicle: Vehicle, request: RequestView) -> bool:
        if request.service_type is ServiceType.PASSENGER:
            return (
                vehicle.passenger_capacity >= request.party_size
                and vehicle.wheelchair_slots >= request.service_needs.wheelchair_slots
            )
        return vehicle.cargo_capacity_kg >= request.cargo_kg
