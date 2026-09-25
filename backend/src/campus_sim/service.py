from __future__ import annotations

import asyncio
import json
import math
from dataclasses import dataclass, field
from hashlib import sha256
from pathlib import Path
from time import monotonic
from uuid import uuid4

from campus_sim.crowd import CrowdModelConfig
from campus_sim.dispatch import minimum_cost_assignment
from campus_sim.domain import (
    CommandAck,
    CreateRequest,
    EgoLocalization,
    Landmark,
    ReasonCode,
    RequestStatus,
    RequestView,
    SensorObservation,
    ServiceType,
    Stop,
    Vehicle,
)
from campus_sim.planning import NoRouteError, astar, node_for_stop
from campus_sim.road_graph import RoadGraphDocument, load_road_graph

EGO_LOCALIZATION_STALE_AFTER_S = 0.5
EGO_ARRIVAL_RADIUS_M = 1.0  # Synthetic fixture threshold; calibrate from verified Stop geometry.
EGO_ROUTE_MATCH_RADIUS_M = 1.0  # Synthetic route-matching threshold; calibrate with verified geometry.
EGO_ARRIVAL_MAX_SPEED_MPS = 0.1
MAX_SENSOR_STREAMS_PER_VEHICLE = 8


@dataclass(frozen=True)
class PlanningPolicy:
    global_replan_interval_s: float
    improvement_ratio: float
    improvement_s: float

    @classmethod
    def from_default_config(cls) -> PlanningPolicy:
        path = Path(__file__).resolve().parents[3] / "configs" / "planning.json"
        try:
            values = json.loads(path.read_text(encoding="utf-8"))

            def number(name: str) -> float:
                value = values[name]
                if isinstance(value, bool) or not isinstance(value, (int, float)):
                    raise TypeError(f"planning config value must be numeric: {name}")
                return float(value)

            policy = cls(
                global_replan_interval_s=number("global_replan_interval_s"),
                improvement_ratio=number("improvement_ratio"),
                improvement_s=number("improvement_s"),
            )
        except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as error:
            raise ValueError(f"invalid planning config: {path}") from error
        if (
            not math.isfinite(policy.global_replan_interval_s)
            or policy.global_replan_interval_s <= 0
            or not math.isfinite(policy.improvement_ratio)
            or not 0 <= policy.improvement_ratio <= 1
            or not math.isfinite(policy.improvement_s)
            or policy.improvement_s < 0
        ):
            raise ValueError(f"planning replan values are outside their valid range: {path}")
        return policy


@dataclass(frozen=True)
class SafetyPolicy:
    sensor_stale_after_s: float
    resume_clear_s: float
    reaction_time_s: float
    emergency_decel_mps2: float
    margin_m: float

    @classmethod
    def from_default_config(cls) -> SafetyPolicy:
        path = Path(__file__).resolve().parents[3] / "configs" / "safety.json"
        try:
            values = json.loads(path.read_text(encoding="utf-8"))

            def number(name: str) -> float:
                value = values[name]
                if isinstance(value, bool) or not isinstance(value, (int, float)):
                    raise TypeError(f"safety config value must be numeric: {name}")
                return float(value)

            policy = cls(
                sensor_stale_after_s=number("sensor_stale_after_s"),
                resume_clear_s=number("resume_clear_s"),
                reaction_time_s=number("reaction_time_s"),
                emergency_decel_mps2=number("emergency_decel_mps2"),
                margin_m=number("margin_m"),
            )
        except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as error:
            raise ValueError(f"invalid safety config: {path}") from error
        values_to_check = (
            policy.sensor_stale_after_s,
            policy.resume_clear_s,
            policy.reaction_time_s,
            policy.emergency_decel_mps2,
            policy.margin_m,
        )
        if any(not math.isfinite(value) or value < 0 for value in values_to_check):
            raise ValueError(f"safety config values must be finite and non-negative: {path}")
        if policy.sensor_stale_after_s == 0 or policy.emergency_decel_mps2 == 0:
            raise ValueError(f"safety freshness and deceleration must be positive: {path}")
        return policy


@dataclass(frozen=True)
class DispatchPriorityPolicy:
    passenger_base: float
    cargo_base: float
    mobility_needs_base: float
    aging_interval_s: float
    deadline_weight: float
    deadline_window_s: float
    fairness_wait_s: float
    fairness_every_n_assignments: int
    pickup_service_s: float
    dropoff_service_s: float
    assignment_algorithm: str

    @classmethod
    def from_default_config(cls) -> DispatchPriorityPolicy:
        path = Path(__file__).resolve().parents[3] / "configs" / "dispatch.json"
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            values = data["request_priority"]
            bases = values["base_priority"]
            fairness = data["fairness"]
            service_times = data["service_times_s"]
            assignment_algorithm = data["assignment_algorithm"]
            fairness_interval = fairness["every_n_assignments"]
            if type(fairness_interval) is not int:
                raise ValueError("fairness.every_n_assignments must be an integer")
            policy = cls(
                passenger_base=float(bases["passenger"]),
                cargo_base=float(bases["cargo"]),
                mobility_needs_base=float(bases["mobility_needs"]),
                aging_interval_s=float(values["aging_interval_s"]),
                deadline_weight=float(values["deadline_weight"]),
                deadline_window_s=float(values["deadline_window_s"]),
                fairness_wait_s=float(fairness["wait_s"]),
                fairness_every_n_assignments=fairness_interval,
                pickup_service_s=float(service_times["pickup"]),
                dropoff_service_s=float(service_times["dropoff"]),
                assignment_algorithm=str(assignment_algorithm),
            )
        except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as error:
            raise ValueError(f"invalid dispatch priority config: {path}") from error
        numeric_values = (
            policy.passenger_base,
            policy.cargo_base,
            policy.mobility_needs_base,
            policy.aging_interval_s,
            policy.deadline_weight,
            policy.deadline_window_s,
            policy.fairness_wait_s,
            policy.pickup_service_s,
            policy.dropoff_service_s,
        )
        if any(not math.isfinite(value) or value < 0 for value in numeric_values):
            raise ValueError(f"dispatch priority values must be finite and non-negative: {path}")
        if (
            policy.aging_interval_s == 0
            or policy.deadline_window_s == 0
            or policy.fairness_wait_s == 0
            or policy.fairness_every_n_assignments < 1
        ):
            raise ValueError(f"dispatch aging, deadline, and fairness values must be positive: {path}")
        if policy.assignment_algorithm not in {"greedy", "hungarian"}:
            raise ValueError(f"dispatch assignment_algorithm must be greedy or hungarian: {path}")
        return policy


@dataclass
class VehicleRuntime:
    node_id: str
    x: float
    y: float
    route_points: list[tuple[float, float]] = field(default_factory=list)
    route_speeds: list[float] = field(default_factory=list)
    route_edge_ids: list[str | None] = field(default_factory=list)
    route_snapshot_points: list[tuple[float, float]] = field(default_factory=list)
    route_snapshot_speeds: list[float] = field(default_factory=list)
    route_snapshot_edge_ids: list[str | None] = field(default_factory=list)
    route_progress_m: float = 0.0
    route_id: str | None = None
    route_reason: str = "UNKNOWN"
    last_replan_check_s: float = 0.0
    mission_state: str = "IDLE"
    request_id: str | None = None
    speed_mps: float = 0.0
    z: float = 0.0
    heading_rad: float = 0.0
    pose_source: str = "synthetic"
    localization_received_at_s: float | None = None
    service_remaining_s: float = 0.0
    safety_motion_state: str | None = None
    safety_reason: str = "UNKNOWN"
    sensor_clear_since_s: float | None = None


@dataclass(frozen=True)
class DispatchCandidate:
    request_priority: float
    pickup_duration_s: float
    created_s: float
    request_id: str
    vehicle_id: str
    request: RequestView
    cost_s: float


@dataclass
class MobilityService:
    """In-memory M0 service. It owns requests; Unity remains the physics authority."""

    landmarks: dict[str, Landmark] = field(default_factory=dict)
    stops: dict[str, Stop] = field(default_factory=dict)
    vehicles: dict[str, Vehicle] = field(default_factory=dict)
    requests: dict[str, RequestView] = field(default_factory=dict)
    commands: dict[str, CommandAck] = field(default_factory=dict)
    command_fingerprints: dict[str, str] = field(default_factory=dict)
    graph: RoadGraphDocument | None = None
    vehicle_runtime: dict[str, VehicleRuntime] = field(default_factory=dict)
    ego_localizations: dict[str, EgoLocalization] = field(default_factory=dict)
    sensor_observations: dict[tuple[str, str], SensorObservation] = field(default_factory=dict)
    sensor_received_at_s: dict[tuple[str, str], float] = field(default_factory=dict)
    last_advance_clock: float = field(default_factory=monotonic)
    simulation_time_s: float = 0.0
    route_counter: int = 0
    assignments_since_fairness: int = 0
    route_duration_cache: dict[tuple[str, str, str, ServiceType, bool, int], float] = field(
        default_factory=dict
    )
    dispatch_priority_policy: DispatchPriorityPolicy = field(
        default_factory=DispatchPriorityPolicy.from_default_config
    )
    safety_policy: SafetyPolicy = field(default_factory=SafetyPolicy.from_default_config)
    crowd_model: CrowdModelConfig = field(default_factory=CrowdModelConfig.from_default_config)
    planning_policy: PlanningPolicy = field(default_factory=PlanningPolicy.from_default_config)

    @classmethod
    def synthetic_fixture(cls) -> MobilityService:
        graph_path = Path(__file__).resolve().parents[3] / "maps" / "fixtures" / "campus-synthetic-6.json"
        return cls.from_synthetic_graph(graph_path)

    @classmethod
    def from_synthetic_graph(
        cls, graph_path: str | Path, *, active_fleet: bool = False
    ) -> MobilityService:
        """Build an in-memory service for a schema-approved synthetic graph fixture."""
        service = cls()
        service.graph = load_road_graph(graph_path)
        fixture_names = {
            "fixture_landmark_1": "정문",
            "fixture_landmark_2": "인하대역",
            "fixture_landmark_3": "후문",
            "fixture_landmark_4": "5호관",
            "fixture_landmark_5": "2호관",
            "fixture_landmark_6": "하이테크",
        }
        for node in service.graph.nodes:
            landmark = Landmark(
                id=node.landmark_id,
                name=fixture_names.get(node.landmark_id, node.landmark_id),
                stop_ids=[node.stop_id],
            )
            service.landmarks[landmark.id] = landmark
            service.stops[node.stop_id] = Stop(
                id=node.stop_id,
                landmark_id=node.landmark_id,
                step_free_access=True,
            )
        for vehicle in (
            # Synthetic M1 vehicle: handles one passenger group or one cargo request per mission.
            Vehicle(id="V01", passenger_capacity=4, wheelchair_slots=1, cargo_capacity_kg=20),
            Vehicle(id="V02", passenger_capacity=4, wheelchair_slots=0, cargo_capacity_kg=0),
            Vehicle(id="V03", passenger_capacity=0, wheelchair_slots=0, cargo_capacity_kg=20),
        ):
            service.vehicles[vehicle.id] = vehicle
        vehicle_ids = tuple(service.vehicles) if active_fleet else ("V01",)
        service._initialize_vehicle_runtimes(vehicle_ids)
        return service

    @classmethod
    def synthetic_fleet_fixture(cls) -> MobilityService:
        """Build the three-vehicle synthetic fleet used for dispatch integration work."""
        graph_path = Path(__file__).resolve().parents[3] / "maps" / "fixtures" / "campus-synthetic-6.json"
        return cls.from_synthetic_graph(graph_path, active_fleet=True)

    def _initialize_vehicle_runtimes(self, vehicle_ids: tuple[str, ...]) -> None:
        if self.graph is None or not vehicle_ids:
            raise ValueError("vehicle runtimes require a graph and at least one vehicle")
        if len(vehicle_ids) > len(self.graph.nodes):
            raise ValueError("synthetic graph has fewer nodes than active vehicles")
        if any(vehicle_id not in self.vehicles for vehicle_id in vehicle_ids):
            raise ValueError("runtime references an unknown vehicle")
        self.vehicle_runtime.clear()
        last_node_index = len(self.graph.nodes) - 1
        denominator = max(1, len(vehicle_ids) - 1)
        for index, vehicle_id in enumerate(vehicle_ids):
            node_index = round(index * last_node_index / denominator)
            node = self.graph.nodes[node_index]
            self.vehicle_runtime[vehicle_id] = VehicleRuntime(
                node_id=node.id,
                x=node.position_m.x,
                y=node.position_m.y,
            )

    def now_s(self) -> float:
        return round(self.simulation_time_s, 3)

    def record_ego_localization(self, observation: EgoLocalization) -> bool:
        """Apply an ego-only pose report to the initialized vehicle runtime."""
        if observation.vehicle_id not in self.vehicles:
            raise ValueError("unknown_vehicle")
        runtime = self.vehicle_runtime.get(observation.vehicle_id)
        if runtime is None:
            raise ValueError("vehicle_runtime_unavailable")
        if self.graph is not None and observation.map_version != self.graph.map_version:
            raise ValueError("map_version_mismatch")
        previous = self.ego_localizations.get(observation.vehicle_id)
        if (
            previous is not None
            and previous.session_id == observation.session_id
            and observation.observed_tick <= previous.observed_tick
        ):
            return False
        if previous is not None and previous.session_id != observation.session_id:
            self.sensor_observations = {
                key: frame
                for key, frame in self.sensor_observations.items()
                if key[0] != observation.vehicle_id
            }
            self.sensor_received_at_s = {
                key: received_at
                for key, received_at in self.sensor_received_at_s.items()
                if key[0] != observation.vehicle_id
            }
        self.ego_localizations[observation.vehicle_id] = observation.model_copy(deep=True)
        runtime.x = observation.position.x
        runtime.y = observation.position.y
        runtime.z = observation.position.z
        runtime.heading_rad = observation.heading_rad
        runtime.speed_mps = observation.speed_mps
        runtime.node_id = self._nearest_node(runtime.x, runtime.y)
        runtime.pose_source = "unity_localization"
        runtime.localization_received_at_s = self.now_s()
        self._update_localized_route_progress(runtime)
        self._refresh_sensor_safety(observation.vehicle_id, runtime)
        return True

    def record_sensor_observation(self, observation: SensorObservation) -> bool:
        """Store a bounded sensor frame only when it references the current fresh ego pose."""
        if observation.vehicle_id not in self.vehicles:
            raise ValueError("unknown_vehicle")
        if self.graph is not None and observation.map_version != self.graph.map_version:
            raise ValueError("map_version_mismatch")
        localization = self.ego_localizations.get(observation.vehicle_id)
        if localization is None:
            raise ValueError("ego_localization_required")
        if self.ego_localization_is_stale(observation.vehicle_id):
            raise ValueError("stale_localization")
        if observation.session_id != localization.session_id:
            raise ValueError("session_mismatch")
        if observation.ego_pose_tick != localization.observed_tick:
            raise ValueError("ego_pose_tick_mismatch")

        key = (observation.vehicle_id, observation.sensor_id)
        previous = self.sensor_observations.get(key)
        if (
            previous is not None
            and previous.session_id == observation.session_id
            and observation.observed_tick <= previous.observed_tick
        ):
            return False
        if previous is None:
            active_streams = sum(
                vehicle_id == observation.vehicle_id and frame.session_id == observation.session_id
                for (vehicle_id, _), frame in self.sensor_observations.items()
            )
            if active_streams >= MAX_SENSOR_STREAMS_PER_VEHICLE:
                raise ValueError("sensor_stream_limit_exceeded")
        self.sensor_observations[key] = observation.model_copy(deep=True)
        self.sensor_received_at_s[key] = self.now_s()
        runtime = self.vehicle_runtime.get(observation.vehicle_id)
        if runtime is not None and runtime.pose_source == "unity_localization":
            self._refresh_sensor_safety(observation.vehicle_id, runtime)
        return True

    def ego_localization_is_stale(self, vehicle_id: str) -> bool:
        runtime = self.vehicle_runtime.get(vehicle_id)
        if runtime is None or runtime.pose_source != "unity_localization":
            return False
        received_at = runtime.localization_received_at_s
        return received_at is None or self.now_s() - received_at > EGO_LOCALIZATION_STALE_AFTER_S

    async def run_clock(self, stop_event: asyncio.Event, fixed_dt_s: float = 0.05) -> None:
        """Advance the authoritative simulation on one fixed-step server clock."""
        if not math.isfinite(fixed_dt_s) or fixed_dt_s <= 0:
            raise ValueError("fixed_dt_s must be finite and positive")
        loop = asyncio.get_running_loop()
        next_tick = loop.time() + fixed_dt_s
        while not stop_event.is_set():
            delay_s = max(0.0, next_tick - loop.time())
            try:
                await asyncio.wait_for(stop_event.wait(), timeout=delay_s)
                break
            except TimeoutError:
                pass
            if stop_event.is_set():
                break
            self.advance(fixed_dt_s)
            next_tick += fixed_dt_s

    def create_request(self, command: CreateRequest) -> CommandAck:
        cache_key = self._command_cache_key("create", command.owner_id, command.command_id)
        fingerprint = self._create_fingerprint(command)
        cached = self._cached_command(cache_key, fingerprint)
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
        self._route_duration_between_stops(
            request.pickup_stop_id or "",
            request.dropoff_stop_id or "",
            service_type=request.service_type,
            requires_step_free=request.service_needs.requires_step_free,
        )
        active_compatible = [
            candidate for candidate in self.vehicles.values()
            if candidate.id in self.vehicle_runtime and self._vehicle_can_serve(candidate, command)
        ]
        stale_available = any(
            candidate.available
            and self.ego_localization_is_stale(candidate.id)
            for candidate in active_compatible
        )
        has_fresh_compatible_vehicle = any(
            not self.ego_localization_is_stale(candidate.id) for candidate in active_compatible
        )
        if stale_available and not has_fresh_compatible_vehicle:
            raise ValueError("stale_localization")
        available_compatible = [vehicle for vehicle in active_compatible if vehicle.available]
        fresh_busy_alternative = any(
            not vehicle.available and not self.ego_localization_is_stale(vehicle.id)
            for vehicle in active_compatible
        )
        if available_compatible and not fresh_busy_alternative and all(
            self._localized_pose_is_off_graph(self.vehicle_runtime[vehicle.id])
            for vehicle in available_compatible
        ):
            raise ValueError("ego_pose_off_graph_node")
        self.requests[request.id] = request
        self._dispatch_queued_requests()
        self._update_request_etas()
        ack = CommandAck(command_id=command.command_id, accepted=True, request=request)
        self._store_command(cache_key, fingerprint, ack)
        return ack

    def cancel_request(self, request_id: str, command_id: str, owner_id: str) -> CommandAck:
        cache_key = self._command_cache_key("cancel", owner_id, command_id)
        fingerprint = sha256(request_id.encode("utf-8")).hexdigest()
        cached = self._cached_command(cache_key, fingerprint)
        if cached is not None:
            return cached
        request = self.requests.get(request_id)
        if request is None or request.owner_id != owner_id:
            raise KeyError("request not found")
        if request.status not in {RequestStatus.QUEUED, RequestStatus.ASSIGNED}:
            raise ValueError("request cannot be cancelled in its current state")
        if request.vehicle_id:
            runtime = self.vehicle_runtime.get(request.vehicle_id)
            if runtime is not None and runtime.pose_source == "unity_localization":
                if self.ego_localization_is_stale(request.vehicle_id):
                    raise ValueError("stale_localization")
                nearest_node = self._nearest_node(runtime.x, runtime.y)
                node_position = next(node.position_m for node in self.graph.nodes if node.id == nearest_node)
                if (
                    runtime.speed_mps > EGO_ARRIVAL_MAX_SPEED_MPS
                    or math.hypot(runtime.x - node_position.x, runtime.y - node_position.y)
                    > EGO_ARRIVAL_RADIUS_M
                ):
                    raise ValueError("vehicle_not_stopped_at_route_node")
            self.vehicles[request.vehicle_id].available = True
            if runtime is not None:
                if self.graph is not None:
                    runtime.node_id = self._nearest_node(runtime.x, runtime.y)
                runtime.route_points = []
                runtime.route_speeds = []
                runtime.route_edge_ids = []
                runtime.route_snapshot_points = []
                runtime.route_snapshot_speeds = []
                runtime.route_snapshot_edge_ids = []
                runtime.route_id = None
                runtime.mission_state = "IDLE"
                runtime.request_id = None
                if runtime.pose_source != "unity_localization":
                    runtime.speed_mps = 0.0
                runtime.service_remaining_s = 0.0
        request.status = RequestStatus.CANCELLED
        request.vehicle_id = None
        request.eta_s = None
        ack = CommandAck(command_id=command_id, accepted=True, request=request)
        self._store_command(cache_key, fingerprint, ack)
        self._dispatch_queued_requests()
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
        payload = f"{operation}\0{owner_id}\0{command_id}".encode()
        return sha256(payload).hexdigest()

    @staticmethod
    def _create_fingerprint(command: CreateRequest) -> str:
        payload = command.model_dump_json(exclude={"command_id", "owner_id"})
        return sha256(payload.encode("utf-8")).hexdigest()

    def _cached_command(self, cache_key: str, fingerprint: str) -> CommandAck | None:
        cached = self.commands.get(cache_key)
        previous_fingerprint = self.command_fingerprints.get(cache_key)
        if previous_fingerprint is not None and previous_fingerprint != fingerprint:
            raise ValueError("command_id_reused_with_different_payload")
        self.command_fingerprints.setdefault(cache_key, fingerprint)
        if cached is None:
            return None
        return cached.model_copy(deep=True)

    def _store_command(self, cache_key: str, fingerprint: str, ack: CommandAck) -> None:
        self.commands[cache_key] = ack.model_copy(deep=True)
        self.command_fingerprints[cache_key] = fingerprint

    def _set_route(
        self, runtime: VehicleRuntime | None, stop_id: str, request: RequestView, mission: str
    ) -> None:
        if runtime is None or self.graph is None:
            raise ValueError("synthetic route runtime is unavailable")
        start = runtime.node_id
        goal = node_for_stop(self.graph, stop_id)
        result, crowd_penalties_s, route_reason = self._plan_route(
            start, goal, request.service_type, request.service_needs.requires_step_free
        )
        node_by_id = {node.id: node for node in self.graph.nodes}
        start_position = node_by_id[start].position_m
        if runtime.pose_source == "unity_localization":
            pose_to_node_m = math.hypot(runtime.x - start_position.x, runtime.y - start_position.y)
            if pose_to_node_m > EGO_ARRIVAL_RADIUS_M:
                raise ValueError("ego_pose_off_graph_node")
            # Snap only within the explicit synthetic graph-node tolerance. Do not
            # invent an unverified straight road connector from a localized pose.
            points: list[tuple[float, float]] = [(start_position.x, start_position.y)]
        else:
            points = [(runtime.x, runtime.y)]
        speeds: list[float] = []
        route_edge_ids: list[str | None] = []
        if runtime.pose_source != "unity_localization" and (
            (runtime.x - start_position.x) ** 2 + (runtime.y - start_position.y) ** 2
        ) ** 0.5 > 0.01:
            # Synthetic recovery connector after cancellation mid-edge; not a validated road segment.
            points.append((start_position.x, start_position.y))
            speeds.append(5.0)
            route_edge_ids.append(None)
        edge_by_id = {edge.id: edge for edge in self.graph.edges}
        for edge_id in result.edge_ids:
            edge = edge_by_id[edge_id]
            points.extend((point.x, point.y) for point in edge.geometry_m[1:])
            base_speed_mps = min(5.0, edge.allowed_speed_mps)
            edge_duration_s = edge.length_m / base_speed_mps + crowd_penalties_s.get(edge_id, 0.0)
            effective_speed_mps = edge.length_m / edge_duration_s
            speeds.extend([effective_speed_mps] * (len(edge.geometry_m) - 1))
            route_edge_ids.extend([edge_id] * (len(edge.geometry_m) - 1))
        self.route_counter += 1
        runtime.route_points = points
        runtime.route_speeds = speeds
        runtime.route_edge_ids = route_edge_ids
        runtime.route_snapshot_points = points.copy()
        runtime.route_snapshot_speeds = speeds.copy()
        runtime.route_snapshot_edge_ids = route_edge_ids.copy()
        runtime.route_progress_m = 0.0
        runtime.route_id = f"synthetic-route-{self.route_counter}"
        runtime.route_reason = route_reason.value
        runtime.last_replan_check_s = float(int(self.simulation_time_s))
        runtime.mission_state = mission
        runtime.request_id = request.id
        if runtime.pose_source != "unity_localization":
            runtime.speed_mps = speeds[0] if speeds else 0.0
        runtime.node_id = start
        request.eta_s = self._route_duration(runtime)

    def _plan_route(self, start: str, goal: str, service_type: ServiceType,
                    requires_step_free: bool):
        """Plan against synthetic crowd costs, preferring a bounded zone detour."""
        if self.graph is None:
            raise ValueError("synthetic route graph is unavailable")
        now_s = float(int(self.simulation_time_s))
        _, crowd_penalties_s = self.crowd_model.edge_cost_snapshot_s(self.graph, now_s)
        edges = {edge.id: edge for edge in self.graph.edges}
        route_penalties_s = {
            edge.id: edge.crowd_penalty_s + edge.zone_penalty_s + edge.expected_wait_s
            + crowd_penalties_s.get(edge.id, 0.0)
            for edge in self.graph.edges
        }
        edge_costs_s = {
            edge.id: edge.length_m / min(5.0, edge.allowed_speed_mps)
            + route_penalties_s[edge.id]
            for edge in self.graph.edges
        }
        base = astar(self.graph, start, goal, service_type=service_type,
                     requires_step_free=requires_step_free, edge_costs_s=edge_costs_s or None)
        avoided = self.crowd_model.avoided_edge_ids(self.graph, now_s)
        if not avoided or not avoided.intersection(base.edge_ids):
            return base, route_penalties_s, ReasonCode.UNKNOWN
        try:
            alternative = astar(self.graph, start, goal, service_type=service_type,
                                requires_step_free=requires_step_free,
                                edge_costs_s=edge_costs_s or None,
                                excluded_edge_ids=avoided)
            zones = self.crowd_model.avoidance_zone_ids(self.graph, avoided)
            limit = self.crowd_model.detour_limit_s(zones, base.path_cost_s)
            if alternative.path_cost_s - base.path_cost_s <= limit:
                return alternative, route_penalties_s, ReasonCode.CROWD_AVOIDANCE
        except NoRouteError:
            pass
        penalties = self.crowd_model.avoidance_penalties_s(self.graph, avoided)
        costs = dict(edge_costs_s)
        for edge_id, penalty in penalties.items():
            costs[edge_id] = costs.get(edge_id, edges[edge_id].cost_s()) + penalty
        result = astar(self.graph, start, goal, service_type=service_type,
                       requires_step_free=requires_step_free, edge_costs_s=costs or None)
        merged = dict(route_penalties_s)
        for edge_id, penalty in penalties.items():
            merged[edge_id] = merged.get(edge_id, 0.0) + penalty
        return result, merged, ReasonCode.CROWD_AVOIDANCE

    @staticmethod
    def _route_duration(runtime: VehicleRuntime) -> float:
        duration = 0.0
        progress_m = runtime.route_progress_m
        traversed_m = 0.0
        for index, ((ax, ay), (bx, by)) in enumerate(
            zip(runtime.route_points, runtime.route_points[1:])
        ):
            speed = runtime.route_speeds[index]
            if speed <= 0:
                return 0.0
            segment_m = math.hypot(bx - ax, by - ay)
            remaining_m = max(0.0, segment_m - max(0.0, progress_m - traversed_m))
            duration += remaining_m / speed
            traversed_m += segment_m
        return round(duration, 1)

    def _refresh_route_speeds(
        self, runtime: VehicleRuntime, edge_penalties_s: dict[str, float]
    ) -> None:
        """Apply current per-edge synthetic travel costs to the active route segments."""
        if self.graph is None:
            return
        edges = {edge.id: edge for edge in self.graph.edges}
        for edge_ids, speeds in (
            (runtime.route_edge_ids, runtime.route_speeds),
            (runtime.route_snapshot_edge_ids, runtime.route_snapshot_speeds),
        ):
            for index, edge_id in enumerate(edge_ids):
                if edge_id is None or edge_id not in edges or index >= len(speeds):
                    continue
                edge = edges[edge_id]
                base_speed_mps = min(5.0, edge.allowed_speed_mps)
                duration_s = edge.length_m / base_speed_mps + edge_penalties_s.get(edge_id, 0.0)
                speeds[index] = edge.length_m / duration_s
        if runtime.route_speeds and runtime.pose_source != "unity_localization":
            runtime.speed_mps = runtime.route_speeds[0]

    def _route_duration_between_stops(
        self,
        start_stop_id: str,
        goal_stop_id: str,
        *,
        service_type: ServiceType,
        requires_step_free: bool,
    ) -> float:
        if self.graph is None:
            raise ValueError("synthetic route graph is unavailable")
        cache_key = (
            self.graph.map_version,
            start_stop_id,
            goal_stop_id,
            service_type,
            requires_step_free,
            int(self.simulation_time_s),
        )
        cached_duration = self.route_duration_cache.get(cache_key)
        if cached_duration is not None:
            return cached_duration
        start_node = node_for_stop(self.graph, start_stop_id)
        goal_node = node_for_stop(self.graph, goal_stop_id)
        if start_node == goal_node:
            return 0.0
        try:
            result, crowd_penalties_s, _ = self._plan_route(
                start_node, goal_node, service_type, requires_step_free
            )
        except NoRouteError as error:
            raise ValueError("dropoff_route_unavailable") from error

        edges_by_id = {edge.id: edge for edge in self.graph.edges}
        duration = 0.0
        for edge_id in result.edge_ids:
            edge = edges_by_id[edge_id]
            speed_mps = min(5.0, edge.allowed_speed_mps)
            duration += edge.length_m / speed_mps + crowd_penalties_s.get(edge_id, 0.0)
        self.route_duration_cache[cache_key] = duration
        return duration

    def _maybe_replan_synthetic_route(self, runtime: VehicleRuntime) -> None:
        """Replan a synthetic vehicle route when a cost change materially improves ETA."""
        if self.graph is None or runtime.pose_source != "synthetic":
            return
        policy = self.planning_policy
        if self.simulation_time_s - runtime.last_replan_check_s < policy.global_replan_interval_s:
            return
        runtime.last_replan_check_s = float(int(self.simulation_time_s))

        request = self.requests.get(runtime.request_id or "")
        if request is None or request.status not in {RequestStatus.ASSIGNED, RequestStatus.IN_TRANSIT}:
            return
        if runtime.service_remaining_s > 0 or len(runtime.route_points) < 2:
            return

        target_stop_id = (
            request.pickup_stop_id
            if request.status is RequestStatus.ASSIGNED
            else request.dropoff_stop_id
        )
        if not target_stop_id:
            return
        start_node = self._nearest_node(runtime.x, runtime.y)
        goal_node = node_for_stop(self.graph, target_stop_id)
        try:
            candidate, crowd_penalties_s, _ = self._plan_route(
                start_node, goal_node, request.service_type,
                request.service_needs.requires_step_free
            )
        except NoRouteError:
            return

        self._refresh_route_speeds(runtime, crowd_penalties_s)
        current_duration_s = self._route_duration(runtime)
        start_position = next(node.position_m for node in self.graph.nodes if node.id == start_node)
        connector_distance_m = math.hypot(
            runtime.x - start_position.x,
            runtime.y - start_position.y,
        )
        candidate_duration_s = connector_distance_m / 5.0
        edge_by_id = {edge.id: edge for edge in self.graph.edges}
        for edge_id in candidate.edge_ids:
            edge = edge_by_id[edge_id]
            base_speed_mps = min(5.0, edge.allowed_speed_mps)
            edge_duration_s = edge.length_m / base_speed_mps + crowd_penalties_s.get(edge_id, 0.0)
            effective_speed_mps = edge.length_m / edge_duration_s
            candidate_duration_s += sum(
                math.hypot(end.x - start.x, end.y - start.y) / effective_speed_mps
                for start, end in zip(edge.geometry_m, edge.geometry_m[1:])
            )

        required_improvement_s = max(
            policy.improvement_s,
            current_duration_s * policy.improvement_ratio,
        )
        if current_duration_s - candidate_duration_s < required_improvement_s:
            return

        runtime.node_id = start_node
        mission = "TO_PICKUP" if request.status is RequestStatus.ASSIGNED else "TO_DROPOFF"
        self._set_route(runtime, target_stop_id, request, mission)

    def _maybe_replan_localized_route(self, vehicle_id: str, runtime: VehicleRuntime) -> None:
        """Replan only from a fresh, stopped ego pose that matches a graph node."""
        if self.graph is None or runtime.pose_source != "unity_localization":
            return
        policy = self.planning_policy
        if self.simulation_time_s - runtime.last_replan_check_s < policy.global_replan_interval_s:
            return
        runtime.last_replan_check_s = float(int(self.simulation_time_s))

        if (
            self.ego_localization_is_stale(vehicle_id)
            or runtime.safety_motion_state is not None
            or runtime.speed_mps > EGO_ARRIVAL_MAX_SPEED_MPS
            or runtime.service_remaining_s > 0
            or len(runtime.route_points) < 2
        ):
            return
        request = self.requests.get(runtime.request_id or "")
        if request is None or request.status not in {RequestStatus.ASSIGNED, RequestStatus.IN_TRANSIT}:
            return
        target_stop_id = (
            request.pickup_stop_id
            if request.status is RequestStatus.ASSIGNED
            else request.dropoff_stop_id
        )
        if not target_stop_id:
            return

        start_node = self._nearest_node(runtime.x, runtime.y)
        node = next(node for node in self.graph.nodes if node.id == start_node)
        if math.hypot(runtime.x - node.position_m.x, runtime.y - node.position_m.y) > EGO_ARRIVAL_RADIUS_M:
            return
        goal_node = node_for_stop(self.graph, target_stop_id)
        try:
            candidate, crowd_penalties_s, _ = self._plan_route(
                start_node, goal_node, request.service_type,
                request.service_needs.requires_step_free
            )
        except NoRouteError:
            return

        self._refresh_route_speeds(runtime, crowd_penalties_s)
        current_duration_s = self._route_duration(runtime)
        edge_by_id = {edge.id: edge for edge in self.graph.edges}
        candidate_duration_s = 0.0
        for edge_id in candidate.edge_ids:
            edge = edge_by_id[edge_id]
            base_speed_mps = min(5.0, edge.allowed_speed_mps)
            edge_duration_s = edge.length_m / base_speed_mps + crowd_penalties_s.get(edge_id, 0.0)
            effective_speed_mps = edge.length_m / edge_duration_s
            candidate_duration_s += sum(
                math.hypot(end.x - start.x, end.y - start.y) / effective_speed_mps
                for start, end in zip(edge.geometry_m, edge.geometry_m[1:])
            )

        required_improvement_s = max(
            policy.improvement_s,
            current_duration_s * policy.improvement_ratio,
        )
        if current_duration_s - candidate_duration_s < required_improvement_s:
            return

        runtime.node_id = start_node
        mission = "TO_PICKUP" if request.status is RequestStatus.ASSIGNED else "TO_DROPOFF"
        self._set_route(runtime, target_stop_id, request, mission)

    @staticmethod
    def _update_localized_route_progress(runtime: VehicleRuntime) -> None:
        """Monotonically match fresh ego pose to the active synthetic route."""
        if len(runtime.route_points) < 2 or not runtime.route_speeds:
            return

        best_distance_sq = math.inf
        best_progress_m = 0.0
        traversed_m = 0.0
        for (start_x, start_y), (end_x, end_y) in zip(
            runtime.route_points, runtime.route_points[1:]
        ):
            delta_x = end_x - start_x
            delta_y = end_y - start_y
            segment_length_sq = delta_x * delta_x + delta_y * delta_y
            segment_m = math.sqrt(segment_length_sq)
            if segment_length_sq <= 1e-12:
                continue
            projection = max(0.0, min(1.0, (
                (runtime.x - start_x) * delta_x + (runtime.y - start_y) * delta_y
            ) / segment_length_sq))
            projected_x = start_x + projection * delta_x
            projected_y = start_y + projection * delta_y
            distance_sq = (runtime.x - projected_x) ** 2 + (runtime.y - projected_y) ** 2
            if distance_sq < best_distance_sq:
                best_distance_sq = distance_sq
                best_progress_m = traversed_m + projection * segment_m
            traversed_m += segment_m

        if best_distance_sq <= EGO_ROUTE_MATCH_RADIUS_M**2:
            runtime.route_progress_m = max(runtime.route_progress_m, best_progress_m)

    def advance(self, delta_s: float | None = None) -> None:
        """Advance each active synthetic or localized vehicle on the shared service tick."""
        clock_now = monotonic()
        if delta_s is None:
            delta_s = max(0.0, min(clock_now - self.last_advance_clock, 1.0))
        self.last_advance_clock = clock_now
        if delta_s <= 0:
            return
        self.simulation_time_s = round(self.simulation_time_s + delta_s, 9)
        for vehicle_id, runtime in tuple(self.vehicle_runtime.items()):
            if runtime.pose_source == "unity_localization":
                self._refresh_sensor_safety(vehicle_id, runtime)
                self._maybe_replan_localized_route(vehicle_id, runtime)
                self._advance_localized_runtime(vehicle_id, runtime, delta_s)
            else:
                self._maybe_replan_synthetic_route(runtime)
                self._advance_synthetic_runtime(vehicle_id, runtime, delta_s)
        self._update_request_etas()
        self._dispatch_queued_requests()

    def _advance_synthetic_runtime(
        self, vehicle_id: str, runtime: VehicleRuntime, delta_s: float
    ) -> None:
        remaining = delta_s
        while remaining > 1e-9:
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
                    request.eta_s = self._route_duration(runtime) + self.dispatch_priority_policy.dropoff_service_s
                elif request.status is RequestStatus.DROPOFF_SERVICE:
                    request.status = RequestStatus.COMPLETED
                    request.eta_s = 0.0
                    self.vehicles[vehicle_id].available = True
                    runtime.mission_state = "IDLE"
                    runtime.request_id = None
                    runtime.route_points = []
                    runtime.route_speeds = []
                    runtime.route_edge_ids = []
                    runtime.route_snapshot_points = []
                    runtime.route_snapshot_speeds = []
                    runtime.route_snapshot_edge_ids = []
                    runtime.route_id = None
                    runtime.speed_mps = 0.0
                continue
            if len(runtime.route_points) < 2:
                request = self.requests.get(runtime.request_id or "")
                if request is not None and request.status is RequestStatus.ASSIGNED:
                    request.status = RequestStatus.PICKUP_SERVICE
                    runtime.mission_state = "PICKUP_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = self.dispatch_priority_policy.pickup_service_s
                elif request is not None and request.status is RequestStatus.IN_TRANSIT:
                    request.status = RequestStatus.DROPOFF_SERVICE
                    runtime.mission_state = "DROPOFF_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = self.dispatch_priority_policy.dropoff_service_s
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
                runtime.route_edge_ids = []
                request = self.requests.get(runtime.request_id or "")
                if request is not None and request.status is RequestStatus.ASSIGNED:
                    request.status = RequestStatus.PICKUP_SERVICE
                    runtime.mission_state = "PICKUP_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = self.dispatch_priority_policy.pickup_service_s
                elif request is not None and request.status is RequestStatus.IN_TRANSIT:
                    request.status = RequestStatus.DROPOFF_SERVICE
                    runtime.mission_state = "DROPOFF_SERVICE"
                    runtime.speed_mps = 0.0
                    runtime.service_remaining_s = self.dispatch_priority_policy.dropoff_service_s
            if not moved:
                break

    def _advance_localized_runtime(
        self, vehicle_id: str, runtime: VehicleRuntime, delta_s: float
    ) -> None:
        """Progress a mission only from fresh Unity pose and verified-stop proximity."""
        if self.ego_localization_is_stale(vehicle_id) or runtime.safety_motion_state is not None:
            return

        request = self.requests.get(runtime.request_id or "")
        if runtime.service_remaining_s > 0:
            runtime.service_remaining_s = max(0.0, runtime.service_remaining_s - delta_s)
            if runtime.service_remaining_s > 0 or request is None:
                return
            if request.status is RequestStatus.PICKUP_SERVICE:
                request.status = RequestStatus.IN_TRANSIT
                runtime.mission_state = "TO_DROPOFF"
                self._set_route(runtime, request.dropoff_stop_id or "", request, "TO_DROPOFF")
                request.eta_s = self._route_duration(runtime) + self.dispatch_priority_policy.dropoff_service_s
            elif request.status is RequestStatus.DROPOFF_SERVICE:
                request.status = RequestStatus.COMPLETED
                request.eta_s = 0.0
                self.vehicles[vehicle_id].available = True
                runtime.mission_state = "IDLE"
                runtime.request_id = None
                runtime.route_points = []
                runtime.route_speeds = []
                runtime.route_edge_ids = []
                runtime.route_snapshot_points = []
                runtime.route_snapshot_speeds = []
                runtime.route_snapshot_edge_ids = []
                runtime.route_id = None
                runtime.speed_mps = 0.0
            return

        if request is None or not runtime.route_points:
            return
        target_x, target_y = runtime.route_points[-1]
        distance = math.hypot(runtime.x - target_x, runtime.y - target_y)
        if distance > EGO_ARRIVAL_RADIUS_M or runtime.speed_mps > EGO_ARRIVAL_MAX_SPEED_MPS:
            return

        runtime.node_id = self._nearest_node(runtime.x, runtime.y)
        runtime.route_points = []
        runtime.route_speeds = []
        runtime.route_edge_ids = []
        runtime.speed_mps = 0.0
        if request.status is RequestStatus.ASSIGNED:
            request.status = RequestStatus.PICKUP_SERVICE
            runtime.mission_state = "PICKUP_SERVICE"
            runtime.service_remaining_s = self.dispatch_priority_policy.pickup_service_s
        elif request.status is RequestStatus.IN_TRANSIT:
            request.status = RequestStatus.DROPOFF_SERVICE
            runtime.mission_state = "DROPOFF_SERVICE"
            runtime.service_remaining_s = self.dispatch_priority_policy.dropoff_service_s

    def _refresh_sensor_safety(self, vehicle_id: str, runtime: VehicleRuntime) -> None:
        """Fail closed on stale/invalid frames and close forward hazards before motion."""
        localization = self.ego_localizations.get(vehicle_id)
        frames = [
            (key, frame)
            for key, frame in self.sensor_observations.items()
            if key[0] == vehicle_id
            and localization is not None
            and frame.session_id == localization.session_id
        ]
        if not frames:
            runtime.safety_motion_state = "REPLANNING"
            runtime.safety_reason = ReasonCode.SENSOR_DATA_STALE.value
            runtime.sensor_clear_since_s = None
            return

        for key, frame in frames:
            received_at = self.sensor_received_at_s.get(key)
            if (
                received_at is None
                or self.now_s() - received_at > self.safety_policy.sensor_stale_after_s
            ):
                runtime.safety_motion_state = "REPLANNING"
                runtime.safety_reason = ReasonCode.SENSOR_DATA_STALE.value
                runtime.sensor_clear_since_s = None
                return
            if not frame.valid:
                runtime.safety_motion_state = "EMERGENCY_STOP"
                runtime.safety_reason = ReasonCode.SENSOR_INVALID.value
                runtime.sensor_clear_since_s = None
                return
            if frame.ego_pose_tick != localization.observed_tick:
                runtime.safety_motion_state = "REPLANNING"
                runtime.safety_reason = ReasonCode.SENSOR_DATA_STALE.value
                return

        speed = max(0.0, runtime.speed_mps)
        stopping_distance_m = (
            speed * self.safety_policy.reaction_time_s
            + speed * speed / (2.0 * self.safety_policy.emergency_decel_mps2)
            + self.safety_policy.margin_m
        )
        for _, frame in frames:
            for detection in frame.detections:
                local = detection.local_position_m
                # Conservative 90-degree forward sector; no path/footprint association yet.
                if local.x > 0.0 and abs(local.y) <= local.x and local.x <= stopping_distance_m:
                    runtime.safety_motion_state = "EMERGENCY_STOP"
                    runtime.safety_reason = ReasonCode.OBSTACLE_STOP.value
                    runtime.sensor_clear_since_s = None
                    return

        if runtime.sensor_clear_since_s is None:
            runtime.sensor_clear_since_s = self.now_s()
        if self.now_s() - runtime.sensor_clear_since_s < self.safety_policy.resume_clear_s:
            runtime.safety_motion_state = "EMERGENCY_STOP"
            runtime.safety_reason = ReasonCode.SAFETY_RESUME_HOLD.value
            return
        runtime.safety_motion_state = None
        runtime.safety_reason = ReasonCode.UNKNOWN.value

    def _update_request_etas(self) -> None:
        for request in self.requests.values():
            if request.vehicle_id is None:
                continue
            runtime = self.vehicle_runtime.get(request.vehicle_id)
            if runtime is None or self.ego_localization_is_stale(request.vehicle_id):
                request.eta_s = None
            elif request.status is RequestStatus.ASSIGNED:
                pickup_duration = self._route_duration(runtime)
                trip_duration = self._route_duration_between_stops(
                    request.pickup_stop_id or "",
                    request.dropoff_stop_id or "",
                    service_type=request.service_type,
                    requires_step_free=request.service_needs.requires_step_free,
                )
                request.eta_s = round(
                    pickup_duration + self.dispatch_priority_policy.pickup_service_s + trip_duration,
                    1,
                )
            elif request.status is RequestStatus.PICKUP_SERVICE:
                trip_duration = self._route_duration_between_stops(
                    request.pickup_stop_id or "",
                    request.dropoff_stop_id or "",
                    service_type=request.service_type,
                    requires_step_free=request.service_needs.requires_step_free,
                )
                request.eta_s = round(runtime.service_remaining_s + trip_duration, 1)
            elif request.status is RequestStatus.IN_TRANSIT:
                request.eta_s = self._route_duration(runtime)
            elif request.status in {RequestStatus.DROPOFF_SERVICE, RequestStatus.COMPLETED}:
                request.eta_s = 0.0

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
                if runtime.route_edge_ids:
                    runtime.route_edge_ids.pop(0)
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
            if runtime.route_edge_ids:
                runtime.route_edge_ids.pop(0)
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

    def _localized_pose_is_off_graph(self, runtime: VehicleRuntime) -> bool:
        if runtime.pose_source != "unity_localization" or self.graph is None:
            return False
        node = next((item for item in self.graph.nodes if item.id == runtime.node_id), None)
        return node is None or math.hypot(
            runtime.x - node.position_m.x,
            runtime.y - node.position_m.y,
        ) > EGO_ARRIVAL_RADIUS_M

    def _dispatch_queued_requests(self) -> None:
        """Greedily match feasible request/vehicle pairs with aging fairness."""
        idle_vehicle_ids = {
            vehicle_id
            for vehicle_id, runtime in self.vehicle_runtime.items()
            if self.vehicles[vehicle_id].available
            and not self.ego_localization_is_stale(vehicle_id)
        }
        policy = self.dispatch_priority_policy
        while idle_vehicle_ids:
            pairs: list[DispatchCandidate] = []
            for request in self.requests.values():
                if request.status is not RequestStatus.QUEUED:
                    continue
                for vehicle_id in idle_vehicle_ids:
                    vehicle = self.vehicles[vehicle_id]
                    runtime = self.vehicle_runtime[vehicle_id]
                    if not self._request_can_vehicle_serve(vehicle, request):
                        continue
                    try:
                        pickup_node = node_for_stop(self.graph, request.pickup_stop_id or "")
                        dropoff_node = node_for_stop(self.graph, request.dropoff_stop_id or "")
                        pickup_duration = self._route_duration_between_nodes(
                            runtime.node_id,
                            pickup_node,
                            service_type=request.service_type,
                            requires_step_free=request.service_needs.requires_step_free,
                        )
                        trip_duration = self._route_duration_between_nodes(
                            pickup_node,
                            dropoff_node,
                            service_type=request.service_type,
                            requires_step_free=request.service_needs.requires_step_free,
                        )
                    except ValueError:
                        continue
                    completion_time_s = (
                        self.now_s()
                        + pickup_duration
                        + self.dispatch_priority_policy.pickup_service_s
                        + trip_duration
                        + self.dispatch_priority_policy.dropoff_service_s
                    )
                    lateness_s = (
                        max(0.0, completion_time_s - request.latest_arrival_s)
                        if request.latest_arrival_s is not None
                        else 0.0
                    )
                    # Battery/energy and rare-vehicle penalties cannot be scored yet:
                    # the current Vehicle contract has no battery or fleet scarcity model.
                    dispatch_cost_s = pickup_duration + 0.5 * trip_duration + 2.0 * lateness_s
                    pairs.append(DispatchCandidate(
                        request_priority=self._queued_request_priority(request, runtime),
                        pickup_duration_s=pickup_duration,
                        created_s=request.created_s,
                        request_id=request.id,
                        vehicle_id=vehicle_id,
                        request=request,
                        cost_s=dispatch_cost_s,
                    ))
            if not pairs:
                return

            fairness_due = (
                self.assignments_since_fairness >= policy.fairness_every_n_assignments - 1
            )
            aged_pairs = [
                pair for pair in pairs
                if self.now_s() - pair.created_s >= policy.fairness_wait_s
            ]
            fairness_dispatch = fairness_due and bool(aged_pairs)
            if fairness_dispatch:
                request_pair = min(
                    aged_pairs,
                    key=lambda pair: (pair.created_s, pair.request_id),
                )
            else:
                if (
                    policy.assignment_algorithm == "hungarian"
                    and len(pairs) >= 2
                    and self._dispatch_hungarian_batch(pairs, idle_vehicle_ids, policy)
                ):
                    continue
                request_pair = min(
                    pairs,
                    key=lambda pair: (-pair.request_priority, pair.created_s, pair.request_id),
                )

            selected = min(
                (pair for pair in pairs if pair.request_id == request_pair.request_id),
                key=lambda pair: (pair.cost_s, pair.vehicle_id),
            )

            vehicle_id = selected.vehicle_id
            request = selected.request
            vehicle = self.vehicles[vehicle_id]
            runtime = self.vehicle_runtime[vehicle_id]
            try:
                self._set_route(runtime, request.pickup_stop_id or "", request, "TO_PICKUP")
            except ValueError:
                idle_vehicle_ids.remove(vehicle_id)
                continue
            request.status = RequestStatus.ASSIGNED
            request.vehicle_id = vehicle_id
            vehicle.available = False
            request.eta_s = self._route_duration(runtime)
            if fairness_dispatch:
                self.assignments_since_fairness = 0
            else:
                self.assignments_since_fairness = min(
                    self.assignments_since_fairness + 1,
                    policy.fairness_every_n_assignments - 1,
                )
            idle_vehicle_ids.remove(vehicle_id)

    def _dispatch_hungarian_batch(
        self,
        pairs: list[DispatchCandidate],
        idle_vehicle_ids: set[str],
        policy: DispatchPriorityPolicy,
    ) -> bool:
        """Assign a same-tick request batch with Hungarian matching when feasible."""
        candidates_by_request: dict[str, list[DispatchCandidate]] = {}
        for pair in pairs:
            candidates_by_request.setdefault(pair.request_id, []).append(pair)
        if len(candidates_by_request) < 2 or len(idle_vehicle_ids) < 2:
            return False

        prioritized_requests = sorted(
            candidates_by_request.values(),
            key=lambda candidates: (
                -max(candidate.request_priority for candidate in candidates),
                candidates[0].created_s,
                candidates[0].request_id,
            ),
        )
        batch_size = min(len(idle_vehicle_ids), len(prioritized_requests))
        aged_request_ids = {
            candidate.request_id
            for candidate in pairs
            if self.now_s() - candidate.created_s >= policy.fairness_wait_s
        }
        if aged_request_ids:
            assignments_until_fairness = (
                policy.fairness_every_n_assignments - 1 - self.assignments_since_fairness
            )
            if assignments_until_fairness <= 0:
                return False
            batch_size = min(batch_size, assignments_until_fairness)
        if batch_size < 2:
            return False

        selected_requests = prioritized_requests[:batch_size]
        request_ids = [candidates[0].request_id for candidates in selected_requests]
        vehicle_ids = sorted(idle_vehicle_ids)
        pair_by_key = {
            (pair.request_id, pair.vehicle_id): pair
            for pair in pairs
        }
        cost_matrix = [
            [
                (candidate.cost_s if (candidate := pair_by_key.get((request_id, vehicle_id))) else None)
                for vehicle_id in vehicle_ids
            ]
            for request_id in request_ids
        ]
        assignments = minimum_cost_assignment(cost_matrix)
        if len(assignments) < 2:
            return False

        assigned_count = 0
        for row_index, column_index in assignments:
            candidate = pair_by_key[(request_ids[row_index], vehicle_ids[column_index])]
            vehicle = self.vehicles[candidate.vehicle_id]
            runtime = self.vehicle_runtime[candidate.vehicle_id]
            try:
                self._set_route(runtime, candidate.request.pickup_stop_id or "", candidate.request, "TO_PICKUP")
            except ValueError:
                continue
            candidate.request.status = RequestStatus.ASSIGNED
            candidate.request.vehicle_id = candidate.vehicle_id
            candidate.request.eta_s = self._route_duration(runtime)
            vehicle.available = False
            idle_vehicle_ids.remove(candidate.vehicle_id)
            self.assignments_since_fairness = min(
                self.assignments_since_fairness + 1,
                policy.fairness_every_n_assignments - 1,
            )
            assigned_count += 1
        return assigned_count > 0

    def _queued_request_priority(self, request: RequestView, runtime: VehicleRuntime) -> float:
        policy = self.dispatch_priority_policy
        requires_mobility_support = (
            request.service_needs.requires_step_free
            or request.service_needs.wheelchair_slots > 0
            or request.service_needs.boarding_assistance
        )
        if requires_mobility_support:
            base = policy.mobility_needs_base
        elif request.service_type is ServiceType.PASSENGER:
            base = policy.passenger_base
        else:
            base = policy.cargo_base

        wait_s = max(0.0, self.now_s() - request.created_s)
        deadline_priority = 0.0
        if request.latest_arrival_s is not None:
            try:
                pickup_node = node_for_stop(self.graph, request.pickup_stop_id or "")
                dropoff_node = node_for_stop(self.graph, request.dropoff_stop_id or "")
                pickup_duration = self._route_duration_between_nodes(
                    runtime.node_id,
                    pickup_node,
                    service_type=request.service_type,
                    requires_step_free=request.service_needs.requires_step_free,
                )
                trip_duration = self._route_duration_between_nodes(
                    pickup_node,
                    dropoff_node,
                    service_type=request.service_type,
                    requires_step_free=request.service_needs.requires_step_free,
                )
            except (NoRouteError, ValueError):
                return -math.inf
            completion_time_s = (
                self.now_s()
                + pickup_duration
                + policy.pickup_service_s
                + trip_duration
                + policy.dropoff_service_s
            )
            slack_s = request.latest_arrival_s - completion_time_s
            urgency = max(0.0, min(2.0, 1.0 - slack_s / policy.deadline_window_s))
            deadline_priority = policy.deadline_weight * urgency
        return base + wait_s / policy.aging_interval_s + deadline_priority

    def _route_duration_between_nodes(
        self,
        start_node: str,
        goal_node: str,
        *,
        service_type: ServiceType,
        requires_step_free: bool,
    ) -> float:
        if self.graph is None:
            raise ValueError("synthetic route graph is unavailable")
        nodes_by_id = {node.id: node for node in self.graph.nodes}
        try:
            start_stop_id = nodes_by_id[start_node].stop_id
            goal_stop_id = nodes_by_id[goal_node].stop_id
        except KeyError as error:
            raise ValueError("route node is unavailable") from error
        return self._route_duration_between_stops(
            start_stop_id,
            goal_stop_id,
            service_type=service_type,
            requires_step_free=requires_step_free,
        )

    @staticmethod
    def _request_can_vehicle_serve(vehicle: Vehicle, request: RequestView) -> bool:
        if request.service_type is ServiceType.PASSENGER:
            return (
                vehicle.passenger_capacity >= request.party_size
                and vehicle.wheelchair_slots >= request.service_needs.wheelchair_slots
            )
        return vehicle.cargo_capacity_kg >= request.cargo_kg
