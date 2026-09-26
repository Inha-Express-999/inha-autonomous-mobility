"""Explicit synthetic energy accounting and charger-reachable admission bounds."""
import json
import math
from dataclasses import dataclass, replace
from itertools import pairwise
from pathlib import Path

from campus_sim.domain import RequestStatus
from campus_sim.planning import NoRouteError, node_for_stop


@dataclass(frozen=True)
class EnergyPolicy:
    capacity_wh: float
    wh_per_m: float
    auxiliary_w: float
    passenger_factor_each: float
    cargo_factor_per_kg: float
    max_speed_mps: float
    provenance: str
    reserve_fraction: float = 0.15

    def __post_init__(self):
        values = (self.capacity_wh, self.wh_per_m, self.auxiliary_w, self.passenger_factor_each,
                  self.cargo_factor_per_kg, self.max_speed_mps, self.reserve_fraction)
        if (any(isinstance(v, bool) or not math.isfinite(v) or v < 0 for v in values)
                or min(self.capacity_wh, self.wh_per_m, self.max_speed_mps) <= 0
                or not .15 <= self.reserve_fraction < 1 or not self.provenance.strip()):
            raise ValueError("Explicit finite energy assumptions and at least 15% reserve required")

    def load_factor(self, request):
        return 1 if request is None else 1 + request.party_size * self.passenger_factor_each + request.cargo_kg * self.cargo_factor_per_kg

    def energy(self, distance_m, seconds, factor=1):
        if any(not math.isfinite(v) or v < 0 for v in (distance_m, seconds, factor)) or factor < 1:
            raise ValueError("Finite nonnegative travel and valid load factor required")
        return distance_m * self.wh_per_m * factor + self.auxiliary_w * seconds / 3600


@dataclass(frozen=True)
class EnergyAssessment:
    allowed: bool
    reason: str
    required_wh: float | None = None
    charger_node: str | None = None


class EnergyFleet:
    """Python model balance, not a measured battery or autonomous charging system."""
    def __init__(self, *, map_version, policies, initial_wh, charger_nodes):
        if (not map_version.startswith("synthetic-") or set(policies) != set(initial_wh) or not policies
                or not charger_nodes or any(not isinstance(n, str) or not n.strip() for n in charger_nodes)):
            raise ValueError("Explicit synthetic fleet and charger nodes required")
        if any(isinstance(initial_wh[v], bool) or not math.isfinite(initial_wh[v]) or not 0 <= initial_wh[v] <= p.capacity_wh for v, p in policies.items()):
            raise ValueError("Battery balance must be within capacity")
        self.map_version = map_version
        self.policies = dict(policies)
        self.remaining_wh = dict(initial_wh)
        self.charger_nodes = frozenset(charger_nodes)
        self.faults = {}

    @classmethod
    def from_config(cls, path):
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        if type(data.get("schema_version")) is not int or data["schema_version"] != 1 or data.get("data_status") != "SYNTHETIC_ASSUMPTIONS":
            raise ValueError("Versioned synthetic energy assumptions required")
        return cls(map_version=data["map_version"], charger_nodes=data["charger_nodes"],
                   policies={v: EnergyPolicy(**data["policy"]) for v in data["initial_wh"]}, initial_wh=data["initial_wh"])

    def consume(self, vehicle, *, distance_m=0, seconds=0, request=None):
        if vehicle not in self.policies:
            return
        policy = self.policies[vehicle]
        amount = policy.energy(distance_m, seconds, policy.load_factor(request))
        self.remaining_wh[vehicle] = max(0.0, self.remaining_wh[vehicle] - amount)

    def _known(self, service, vehicle):
        return (vehicle in self.policies and vehicle not in self.faults and service.graph is not None
                and service.graph.map_version == self.map_version
                and self.charger_nodes <= {n.id for n in service.graph.nodes})

    def _leg(self, service, start, goal, request, *, loaded, policy, step_free=None):
        needs = request.service_needs.requires_step_free if step_free is None else step_free
        route, penalties, _ = service._plan_route(start, goal, request.service_type, needs)
        edges = {e.id: e for e in service.graph.edges}
        distance = seconds = 0.0
        for identity in route.edge_ids:
            edge = edges[identity]
            # Account geometry that the follower actually traverses, including bends.
            length = sum(math.hypot(b.x-a.x, b.y-a.y) for a, b in pairwise(edge.geometry_m))
            distance += length
            seconds += length / min(5.0, edge.allowed_speed_mps, policy.max_speed_mps) + penalties.get(identity, 0)
        return policy.energy(distance, seconds, policy.load_factor(request) if loaded else 1)

    def _charger(self, service, dropoff, request, policy):
        feasible = []
        for node in sorted(self.charger_nodes):
            try:
                cost = self._leg(service, dropoff, node, request, loaded=False, policy=policy, step_free=False)
                feasible.append((cost, node))
            except (NoRouteError, ValueError):
                continue
        if not feasible:
            raise ValueError("charger_unreachable")
        return min(feasible)

    def assess(self, service, vehicle, request, *, active=False):
        if not self._known(service, vehicle):
            return EnergyAssessment(False, "ENERGY_UNAVAILABLE")
        policy = self.policies[vehicle]
        control = service.control_policies.get(vehicle)
        if control is not None:
            policy = replace(policy, max_speed_mps=min(policy.max_speed_mps, control.max_speed_mps))
        runtime = service.vehicle_runtime[vehicle]
        try:
            pickup = node_for_stop(service.graph, request.pickup_stop_id)
            dropoff = node_for_stop(service.graph, request.dropoff_stop_id)
            charger_wh, charger = self._charger(service, dropoff, request, policy)
            reserve = policy.capacity_wh * policy.reserve_fraction
            if not active:
                travel = self._leg(service, runtime.node_id, pickup, request, loaded=False, policy=policy)
                travel += self._leg(service, pickup, dropoff, request, loaded=True, policy=policy)
                dwell = service.dispatch_priority_policy.pickup_service_s + service.dispatch_priority_policy.dropoff_service_s
            else:
                travel = 0.0
                skip = runtime.route_progress_m if runtime.pose_source == "unity_localization" else 0.0
                for i, (a, b) in enumerate(pairwise(runtime.route_points)):
                    length = math.dist(a, b)
                    remaining = max(0.0, length - skip)
                    skip = max(0.0, skip - length)
                    if remaining <= 0:
                        continue
                    speed = min(policy.max_speed_mps, runtime.route_speeds[i])
                    if speed <= 0:
                        raise ValueError("unknown_remaining_travel_time")
                    factor = policy.load_factor(request) if request.status == RequestStatus.IN_TRANSIT else 1
                    travel += policy.energy(remaining, remaining / speed, factor)
                dwell = service.dispatch_priority_policy.dropoff_service_s
                if request.status in {RequestStatus.ASSIGNED, RequestStatus.PICKUP_SERVICE}:
                    travel += self._leg(service, pickup, dropoff, request, loaded=True, policy=policy)
                    dwell += (runtime.service_remaining_s if request.status == RequestStatus.PICKUP_SERVICE
                              else service.dispatch_priority_policy.pickup_service_s)
                elif request.status == RequestStatus.DROPOFF_SERVICE:
                    dwell = runtime.service_remaining_s
            required = travel + policy.energy(0, dwell) + charger_wh + reserve
            allowed = self.remaining_wh[vehicle] + 1e-9 >= required  # Absolute Wh roundoff only.
            return EnergyAssessment(allowed,
                                    "OK" if allowed else "ENERGY_INSUFFICIENT",
                                    required, charger)
        except (NoRouteError, ValueError, KeyError, IndexError):
            return EnergyAssessment(False, "ENERGY_ROUTE_UNAVAILABLE")

    def hold(self, service, vehicle):
        if not self._known(service, vehicle):
            return True
        request = service.requests.get(service.vehicle_runtime[vehicle].request_id or "")
        if request is not None and request.status in {
                RequestStatus.ASSIGNED, RequestStatus.PICKUP_SERVICE, RequestStatus.IN_TRANSIT, RequestStatus.DROPOFF_SERVICE}:
            return not self.assess(service, vehicle, request, active=True).allowed
        policy = self.policies[vehicle]
        return self.remaining_wh[vehicle] <= policy.capacity_wh * policy.reserve_fraction

    def observe_ego(self, service, observation):
        vehicle = observation.vehicle_id
        if vehicle not in self.policies:
            return
        runtime = service.vehicle_runtime[vehicle]
        previous = service.ego_localizations.get(vehicle)
        distance = math.hypot(observation.position.x-runtime.x, observation.position.y-runtime.y)
        received = runtime.localization_received_at_s
        age = service.now_s() - (received if received is not None else service.now_s())
        if ((previous is not None and previous.session_id != observation.session_id)
                or (previous is None and distance > .01)
                or previous is not None and distance > self.policies[vehicle].max_speed_mps * max(0, age) + .01):
            self.faults[vehicle] = "untrusted_localization_energy_interval"
            return
        request = service.requests.get(runtime.request_id or "")
        loaded = request if request is not None and request.status in {RequestStatus.IN_TRANSIT, RequestStatus.DROPOFF_SERVICE} else None
        self.consume(vehicle, distance_m=distance, request=loaded)
