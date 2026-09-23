from __future__ import annotations

from dataclasses import dataclass, field
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


@dataclass
class MobilityService:
    """In-memory M0 service. It owns requests; Unity remains the physics authority."""

    started_at: float = field(default_factory=monotonic)
    landmarks: dict[str, Landmark] = field(default_factory=dict)
    stops: dict[str, Stop] = field(default_factory=dict)
    vehicles: dict[str, Vehicle] = field(default_factory=dict)
    requests: dict[str, RequestView] = field(default_factory=dict)
    commands: dict[str, CommandAck] = field(default_factory=dict)

    @classmethod
    def synthetic_fixture(cls) -> MobilityService:
        service = cls()
        for landmark in (
            Landmark(id="main_gate", name="정문", stop_ids=["main_gate_general", "main_gate_step_free"]),
            Landmark(id="library", name="정석학술정보관", stop_ids=["library_general", "library_step_free"]),
            Landmark(id="hitech", name="하이테크센터", stop_ids=["hitech_general"]),
        ):
            service.landmarks[landmark.id] = landmark
        for stop in (
            Stop(id="main_gate_general", landmark_id="main_gate", step_free_access=False),
            Stop(id="main_gate_step_free", landmark_id="main_gate", step_free_access=True),
            Stop(id="library_general", landmark_id="library", step_free_access=False),
            Stop(id="library_step_free", landmark_id="library", step_free_access=True),
            Stop(id="hitech_general", landmark_id="hitech", step_free_access=False),
        ):
            service.stops[stop.id] = stop
        for vehicle in (
            Vehicle(id="V01", passenger_capacity=4, wheelchair_slots=1, cargo_capacity_kg=0),
            Vehicle(id="V02", passenger_capacity=4, wheelchair_slots=0, cargo_capacity_kg=0),
            Vehicle(id="V03", passenger_capacity=0, wheelchair_slots=0, cargo_capacity_kg=20),
        ):
            service.vehicles[vehicle.id] = vehicle
        return service

    def now_s(self) -> float:
        return round(monotonic() - self.started_at, 3)

    def create_request(self, command: CreateRequest) -> CommandAck:
        cached = self.commands.get(command.command_id)
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
        vehicle = self._select_vehicle(command)
        if vehicle is not None:
            vehicle.available = False
            request.status = RequestStatus.ASSIGNED
            request.vehicle_id = vehicle.id
            request.eta_s = 180.0  # Synthetic fixture estimate; not a measured arrival time.
        self.requests[request.id] = request
        ack = CommandAck(command_id=command.command_id, accepted=True, request=request)
        self.commands[command.command_id] = ack
        return ack

    def cancel_request(self, request_id: str, command_id: str, owner_id: str) -> CommandAck:
        cached = self.commands.get(command_id)
        if cached is not None:
            return cached
        request = self.requests.get(request_id)
        if request is None or request.owner_id != owner_id:
            raise KeyError("request not found")
        if request.status not in {RequestStatus.QUEUED, RequestStatus.ASSIGNED}:
            raise ValueError("request cannot be cancelled in its current state")
        if request.vehicle_id:
            self.vehicles[request.vehicle_id].available = True
        request.status = RequestStatus.CANCELLED
        request.vehicle_id = None
        request.eta_s = None
        ack = CommandAck(command_id=command_id, accepted=True, request=request)
        self.commands[command_id] = ack
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
            if not vehicle.available:
                continue
            if command.service_type is ServiceType.PASSENGER:
                if vehicle.passenger_capacity < command.party_size:
                    continue
                if vehicle.wheelchair_slots < command.service_needs.wheelchair_slots:
                    continue
            elif vehicle.cargo_capacity_kg < command.cargo_kg:
                continue
            return vehicle
        return None
