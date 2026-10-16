from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass, field
from functools import wraps
from math import isfinite
from pathlib import Path
from threading import RLock
from time import monotonic
from uuid import uuid4

from campus_sim.battery import BatteryPolicy, BatteryState, ChargeSession
from campus_sim.dispatch import Assignment, FifoDispatch
from campus_sim.domain import (
    CommandAck,
    CreateRequest,
    Landmark,
    ReasonCode,
    RequestStatus,
    RequestView,
    Stop,
    Vehicle,
)
from campus_sim.planning import CostSnapshot, RoadGraph, RoutePlan
from campus_sim.virtual_runtime import VirtualTrip, plan_round_trip, synthetic_road_graph


def synchronized(method):
    @wraps(method)
    def call(self, *args, **kwargs):
        with self._lock:
            return method(self, *args, **kwargs)

    return call


@dataclass
class MobilityService:
    """In-memory synthetic time-only service; Unity Physics is not connected."""

    clock: Callable[[], float] = field(default=monotonic, repr=False)
    started_at: float = field(init=False)
    landmarks: dict[str, Landmark] = field(default_factory=dict)
    stops: dict[str, Stop] = field(default_factory=dict)
    vehicles: dict[str, Vehicle] = field(default_factory=dict)
    requests: dict[str, RequestView] = field(default_factory=dict)
    commands: dict[str, CommandAck] = field(default_factory=dict)
    graph: RoadGraph | None = None
    costs: CostSnapshot | None = None
    stop_nodes: dict[str, str] = field(default_factory=dict)
    home_nodes: dict[str, str] = field(default_factory=dict)
    trips: dict[str, VirtualTrip] = field(default_factory=dict)
    waiting_reasons: dict[str, str] = field(default_factory=dict)
    battery_policy: BatteryPolicy | None = None
    batteries: dict[str, BatteryState] = field(default_factory=dict)
    charges: list[ChargeSession] = field(default_factory=list)
    vehicle_order: dict[str, int] = field(default_factory=dict)
    _ready_sequence: int = field(default=0, init=False)
    map_metadata: dict = field(
        default_factory=lambda: {
            "geometry_source": "synthetic_fixture",
            "synthetic_geometry": True,
            "routing_mode": "synthetic_fixture",
            "operationally_verified": False,
        }
    )
    _time_s: float = field(default=0.0, init=False)
    _lock: RLock = field(default_factory=RLock, init=False, repr=False)

    def __post_init__(self) -> None:
        self.started_at = self.clock()

    @classmethod
    def from_osm_map(
        cls,
        directory=None,
        clock: Callable[[], float] = monotonic,
        *,
        vehicle_count: int | None = None,
        battery_enabled: bool = True,
    ):
        from campus_sim.map_data import default_map_dir, load_map_bundle

        root = Path(__file__).resolve().parents[3]
        bundle = load_map_bundle(
            directory or default_map_dir(), source_root=root if directory is None else None
        )
        service = cls(clock=clock)
        service.graph = bundle.graph
        service.costs = CostSnapshot.travel_time(
            bundle.graph, "configured-speed-" + bundle.metadata["files"]["policy.json"][:12]
        )
        service.landmarks = {landmark.id: landmark for landmark in bundle.landmarks}
        service.stops = {stop.id: stop for stop in bundle.stops}
        service.stop_nodes = bundle.stop_nodes
        count = len(bundle.vehicles) if vehicle_count is None else vehicle_count
        if type(count) is not int or not 1 <= count <= len(bundle.vehicles):
            raise ValueError("vehicle_count outside configured fleet")
        service.vehicles = {v.id: v for v in bundle.vehicles[:count]}
        service.home_nodes = {v.id: bundle.home_node for v in bundle.vehicles[:count]}
        if battery_enabled:
            service.battery_policy = bundle.battery
            service.batteries = {
                v.id: BatteryState(bundle.battery.initial_wh) for v in bundle.vehicles[:count]
            }
        for identifier in service.vehicles:
            service._enqueue_vehicle(identifier)
        service.map_metadata = bundle.metadata
        return service

    @classmethod
    def synthetic_fixture(
        cls,
        clock: Callable[[], float] = monotonic,
        *,
        vehicle_count: int = 1,
    ) -> MobilityService:
        if type(vehicle_count) is not int or not 1 <= vehicle_count <= 3:
            raise ValueError("synthetic vehicle_count must be between 1 and 3")
        service = cls(clock=clock)
        for landmark in (
            Landmark(
                id="main_gate", name="정문", stop_ids=["main_gate_general", "main_gate_step_free"]
            ),
            Landmark(
                id="library",
                name="정석학술정보관",
                stop_ids=["library_general", "library_step_free"],
            ),
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
            # Synthetic capacities; one request at a time, no mixed passenger/cargo load.
            Vehicle(id="V01", passenger_capacity=4, wheelchair_slots=1, cargo_capacity_kg=20),
            Vehicle(id="V02", passenger_capacity=4, wheelchair_slots=0, cargo_capacity_kg=0),
            Vehicle(id="V03", passenger_capacity=0, wheelchair_slots=0, cargo_capacity_kg=20),
        )[:vehicle_count]:
            service.vehicles[vehicle.id] = vehicle
            service.home_nodes[vehicle.id] = "main_gate"
        for index in (4, 5, 6):
            node = f"synthetic_{index}"
            stop_id = f"{node}_stop"
            service.landmarks[node] = Landmark(
                id=node, name=f"합성 거점 {index}", stop_ids=[stop_id]
            )
            service.stops[stop_id] = Stop(id=stop_id, landmark_id=node, step_free_access=True)
        service.graph = synthetic_road_graph()
        service.costs = CostSnapshot.travel_time(service.graph)
        service.stop_nodes = {stop.id: stop.landmark_id for stop in service.stops.values()}
        return service

    def now_s(self) -> float:
        return max(self._time_s, self.clock() - self.started_at)

    @synchronized
    def create_request(self, command: CreateRequest) -> CommandAck:
        self.step()
        cached = self.commands.get(command.command_id)
        if cached is not None:
            return cached

        pickup = self._select_stop(
            command.pickup_landmark_id, command.service_needs.requires_step_free
        )
        dropoff = self._select_stop(
            command.dropoff_landmark_id, command.service_needs.requires_step_free
        )
        request = RequestView(
            # Sequence prefix makes same-tick FIFO ties follow receipt order.
            id=f"req-{len(self.requests) + 1:08d}-{uuid4().hex[:12]}",
            owner_id=command.owner_id,
            service_type=command.service_type,
            status=RequestStatus.QUEUED,
            created_s=self._time_s,
            pickup_landmark_id=command.pickup_landmark_id,
            dropoff_landmark_id=command.dropoff_landmark_id,
            pickup_stop_id=pickup.id,
            dropoff_stop_id=dropoff.id,
            service_needs=command.service_needs,
            party_size=command.party_size,
            cargo_kg=command.cargo_kg,
            latest_arrival_s=command.latest_arrival_s,
        )
        self.requests[request.id] = request
        self._dispatch_at(self._time_s)
        ack = CommandAck(command_id=command.command_id, accepted=True, request=request)
        self.commands[command.command_id] = ack
        return ack

    @synchronized
    def cancel_request(self, request_id: str, command_id: str, owner_id: str) -> CommandAck:
        self.step()
        cached = self.commands.get(command_id)
        if cached is not None:
            return cached
        request = self.requests.get(request_id)
        if request is None or request.owner_id != owner_id:
            raise KeyError("request not found")
        if request.status not in {RequestStatus.QUEUED, RequestStatus.ASSIGNED}:
            raise ValueError("request cannot be cancelled in its current state")
        # A dispatched vehicle remains reserved until its planned return, even
        # when a passenger cancels before pickup. No mid-edge teleportation.
        request.status = RequestStatus.CANCELLED
        request.vehicle_id = None
        request.eta_s = None
        self._dispatch_at(self._time_s)
        ack = CommandAck(command_id=command_id, accepted=True, request=request)
        self.commands[command_id] = ack
        return ack

    @synchronized
    def requests_for_owner(self, owner_id: str) -> list[RequestView]:
        self.step()
        return [request for request in self.requests.values() if request.owner_id == owner_id]

    def _select_stop(self, landmark_id: str, requires_step_free: bool) -> Stop:
        landmark = self.landmarks.get(landmark_id)
        if landmark is None:
            raise ValueError("unknown landmark")
        candidates = [self.stops[stop_id] for stop_id in landmark.stop_ids]
        if requires_step_free:
            candidates = [stop for stop in candidates if stop.step_free_access]
        if not candidates:
            raise ValueError(
                ReasonCode.NO_ACCESSIBLE_ALTERNATIVE.value
                if requires_step_free
                else "NO_MAP_STOP_CANDIDATE"
            )
        return candidates[0]

    def _dispatch_at(self, now_s: float) -> tuple[Assignment, ...]:
        routes: dict[tuple[str, str], tuple[RoutePlan, RoutePlan, RoutePlan]] = {}
        needs_charge: set[str] = set()
        self.waiting_reasons = {
            r.id: "WAITING_FEASIBLE_VEHICLE"
            for r in self.requests.values()
            if r.status is RequestStatus.QUEUED
        }

        def feasible(vehicle: Vehicle, request: RequestView) -> bool:
            if not self._stops_accessible(vehicle, request):
                self.waiting_reasons[request.id] = "STOP_ACCESS_UNAVAILABLE"
                return False
            if self.graph is None or self.costs is None or not self.graph.synthetic:
                return False  # Unverified assumptions allowed only in explicit simulation mode.
            home = self.home_nodes.get(vehicle.id)
            pickup = self.stop_nodes.get(request.pickup_stop_id)
            dropoff = self.stop_nodes.get(request.dropoff_stop_id)
            if home is None or pickup is None or dropoff is None:
                self.waiting_reasons[request.id] = "MISSING_MAP_MAPPING"
                return False
            plan = plan_round_trip(self.graph, self.costs, home, pickup, dropoff)
            if plan is None:
                self.waiting_reasons[request.id] = "NO_ROUND_TRIP_ROUTE"
                return False
            if self.battery_policy is not None:
                policy = self.battery_policy
                required = sum(self._trip_energy(plan, request)) + policy.reserve_wh
                if required > policy.capacity_wh + 1e-8:
                    self.waiting_reasons[request.id] = "BATTERY_CAPACITY_INSUFFICIENT"
                    return False
                if required > self.batteries[vehicle.id].remaining_wh + 1e-8:
                    self.waiting_reasons[request.id] = "WAITING_BATTERY_CHARGE"
                    needs_charge.add(vehicle.id)
                    return False
            routes[vehicle.id, request.id] = plan
            return True

        assignments = FifoDispatch().assign(
            self.requests.values(),
            self.vehicles.values(),
            feasible,
            vehicle_order=self.vehicle_order,
        )
        for assignment in assignments:
            request = self.requests[assignment.request_id]
            self.vehicles[assignment.vehicle_id].available = False
            self.vehicle_order.pop(assignment.vehicle_id, None)
            request.status = RequestStatus.ASSIGNED
            request.vehicle_id = assignment.vehicle_id
            pickup, delivery, returning = routes[assignment.vehicle_id, assignment.request_id]
            trip = VirtualTrip(
                request.id,
                assignment.vehicle_id,
                self.home_nodes[assignment.vehicle_id],
                now_s,
                pickup,
                delivery,
                returning,
            )
            self.trips[request.id] = trip
            trip.energy_by_leg_wh = self._trip_energy((pickup, delivery, returning), request)
            request.eta_s = trip.dropoff_at_s - now_s
            self.waiting_reasons.pop(request.id, None)
        if self.battery_policy is not None:
            for vehicle in self.vehicles.values():
                if vehicle.available and (
                    vehicle.id in needs_charge
                    or self.batteries[vehicle.id].remaining_wh
                    < self.battery_policy.capacity_wh * self.battery_policy.charge_trigger_fraction
                ):
                    self._start_charge(vehicle.id, now_s)
            if any(not charge.completed for charge in self.charges):
                for identifier, reason in self.waiting_reasons.items():
                    if reason == "WAITING_FEASIBLE_VEHICLE":
                        self.waiting_reasons[identifier] = "WAITING_BATTERY_CHARGE"
        return assignments

    def _enqueue_vehicle(self, vehicle_id: str) -> None:
        self._ready_sequence += 1
        self.vehicle_order[vehicle_id] = self._ready_sequence

    def _trip_energy(self, routes, request: RequestView) -> tuple[float, float, float]:
        policy = self.battery_policy
        if policy is None:
            return (0.0, 0.0, 0.0)
        return tuple(
            route.length_m * policy.wh_per_m * (policy.load_factor(request) if i == 1 else 1)
            + policy.auxiliary_power_w * route.cost_s / 3600
            for i, route in enumerate(routes)
        )

    def _start_charge(self, vehicle_id: str, now_s: float) -> None:
        policy = self.battery_policy
        state = self.batteries[vehicle_id]
        duration = (policy.capacity_wh - state.remaining_wh) / policy.charging_power_w * 3600
        if duration <= 1e-8:
            return
        self.vehicles[vehicle_id].available = False
        self.vehicle_order.pop(vehicle_id, None)
        self.charges.append(ChargeSession(vehicle_id, now_s, now_s + duration, state.remaining_wh))

    def _update_batteries(self, now_s: float) -> None:
        if self.battery_policy is None:
            return
        for trip in self.trips.values():
            if trip.returned:
                continue
            consumed = trip.consumed_at(now_s) - trip.consumed_at(self._time_s)
            state = self.batteries[trip.vehicle_id]
            if consumed > state.remaining_wh + 1e-7:
                raise RuntimeError("planned trip exceeded reserved battery energy")
            state.remaining_wh = max(0.0, state.remaining_wh - consumed)
            state.consumed_wh += consumed
        for charge in self.charges:
            if charge.completed:
                continue
            elapsed = max(
                0.0, min(now_s, charge.ends_at_s) - max(self._time_s, charge.started_at_s)
            )
            state = self.batteries[charge.vehicle_id]
            gained = min(
                self.battery_policy.capacity_wh - state.remaining_wh,
                elapsed * self.battery_policy.charging_power_w / 3600,
            )
            state.remaining_wh += gained
            state.charged_wh += gained
            if now_s >= charge.ends_at_s:
                charge.completed = True
                self.vehicles[charge.vehicle_id].available = True
                self._enqueue_vehicle(charge.vehicle_id)

    @synchronized
    def step(self, now_s: float | None = None) -> tuple[Assignment, ...]:
        """Advance virtual time; replay every due return before new assignments.

        Without a time argument use elapsed wall time. Charging and returns are
        replayed chronologically; dynamics and sensor safety are not simulated.
        """
        target = self.now_s() if now_s is None else now_s
        if not isfinite(target) or target < self._time_s:
            raise ValueError("time must be finite and monotonic")
        assignments = []
        while True:
            due = [t.returns_at_s for t in self.trips.values() if not t.returned]
            due.extend(c.ends_at_s for c in self.charges if not c.completed)
            if not due or min(due) > target:
                break
            event_time = min(due)
            self._update_batteries(event_time)
            self._update_trips(event_time)
            self._time_s = event_time
            assignments.extend(self._dispatch_at(event_time))
        self._update_batteries(target)
        self._update_trips(target)
        self._time_s = target
        assignments.extend(self._dispatch_at(target))
        return tuple(assignments)

    def _update_trips(self, now_s: float) -> None:
        for trip in self.trips.values():
            if trip.returned:
                continue
            request = self.requests[trip.request_id]
            if request.status is not RequestStatus.CANCELLED:
                request.eta_s = max(0.0, trip.dropoff_at_s - now_s)
                if now_s >= trip.dropoff_at_s:
                    request.status = RequestStatus.COMPLETED
                    request.eta_s = None
                elif now_s >= trip.pickup_at_s:
                    request.status = RequestStatus.IN_TRANSIT
            if now_s >= trip.returns_at_s:
                trip.returned = True
                self.vehicles[trip.vehicle_id].available = True
                self._enqueue_vehicle(trip.vehicle_id)

    @synchronized
    def virtual_state(self) -> dict:
        """A separate debug response; existing client snapshot schema is unchanged."""
        from dataclasses import asdict

        self.step()
        trips = []
        for trip in self.trips.values():
            item = asdict(trip)
            if not self.map_metadata["synthetic_geometry"]:
                from campus_sim.map_coordinates import MapCoordinates

                coordinates = MapCoordinates()
                for name in ("pickup_route", "delivery_route", "return_route"):
                    item[name]["unity_polyline_xz_m"] = [
                        coordinates.to_unity(*p) for p in item[name]["polyline"]
                    ]
            item.update(
                pickup_at_s=trip.pickup_at_s,
                dropoff_at_s=trip.dropoff_at_s,
                returns_at_s=trip.returns_at_s,
                total_duration_s=trip.returns_at_s - trip.departed_at_s,
                remaining_until_return_s=max(0.0, trip.returns_at_s - self._time_s),
                phase=(
                    "RETURNED"
                    if trip.returned
                    else "RETURNING"
                    if self._time_s >= trip.dropoff_at_s
                    else "TO_DROPOFF"
                    if self._time_s >= trip.pickup_at_s
                    else "TO_PICKUP"
                ),
            )
            trips.append(item)
        return {
            "synthetic": True,
            "synthetic_geometry": self.map_metadata["synthetic_geometry"],
            "routing_mode": self.map_metadata["routing_mode"],
            "map_metadata": self.map_metadata,
            "waiting_reasons": dict(self.waiting_reasons),
            "battery_model": self.battery_policy is not None,
            "battery_policy": self.battery_policy.model_dump() if self.battery_policy else None,
            "batteries": {
                identifier: {
                    **asdict(state),
                    "soc_fraction": state.remaining_wh / self.battery_policy.capacity_wh,
                    "state": "CHARGING"
                    if any(c.vehicle_id == identifier and not c.completed for c in self.charges)
                    else "READY"
                    if self.vehicles[identifier].available
                    else "ON_TRIP",
                }
                for identifier, state in self.batteries.items()
            },
            "charging_sessions": [asdict(c) for c in self.charges],
            "vehicle_fifo": sorted(
                (v.id for v in self.vehicles.values() if v.available),
                key=lambda identifier: (self.vehicle_order.get(identifier, 0), identifier),
            ),
            "simulation_time_s": self._time_s,
            "map_version": self.graph.map_version if self.graph else None,
            "vehicles": [v.model_dump(mode="json") for v in self.vehicles.values()],
            "requests": [r.model_dump(mode="json") for r in self.requests.values()],
            "trips": trips,
        }

    def _stops_accessible(self, vehicle: Vehicle, request: RequestView) -> bool:
        for stop_id in (request.pickup_stop_id, request.dropoff_stop_id):
            stop = self.stops.get(stop_id)
            if stop is None:
                return False
            if request.service_needs.requires_step_free and not stop.step_free_access:
                return False
        return True
