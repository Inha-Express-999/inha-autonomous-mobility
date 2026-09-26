"""Bounded offline CBS and priority reservation on a quantized RoadGraph snapshot.

Not an actuator/lease authority. Continuous footprint safety and actual occupancy
remain mandatory; success only describes this finite discrete planning model.
"""
from __future__ import annotations

import hashlib
import heapq
import itertools
import json
import math
from dataclasses import asdict, dataclass, field
from time import perf_counter

from campus_sim.domain import ServiceType
from campus_sim.planning import NoRouteError, dijkstra, prepare_routing_snapshot


@dataclass(frozen=True)
class AgentTask:
    vehicle_id: str
    start: str
    goal: str
    service_type: ServiceType = ServiceType.PASSENGER
    vehicle_class: str = "CAMPUS_SHUTTLE"
    vehicle_width_m: float | None = None
    requires_step_free: bool = False

    def options(self):
        return {"service_type": self.service_type, "vehicle_class": self.vehicle_class,
                "vehicle_width_m": self.vehicle_width_m, "requires_step_free": self.requires_step_free}


@dataclass(frozen=True, order=True)
class Claim:
    tick: int
    kind: str
    key: str


@dataclass(frozen=True)
class TimedMove:
    source: str
    target: str
    depart: int
    arrive: int
    edge_id: str | None


@dataclass(frozen=True)
class TimedPath:
    vehicle_id: str
    start: str
    goal: str
    moves: tuple[TimedMove, ...]
    claims: frozenset[Claim]

    @property
    def arrival(self):
        return self.moves[-1].arrive if self.moves else 0

    @property
    def wait_ticks(self):
        return sum(m.arrive - m.depart for m in self.moves if m.edge_id is None)


@dataclass(frozen=True)
class SearchLimits:
    ct_nodes: int = 2000
    low_level_expansions: int = 100000
    wall_time_s: float | None = None

    def __post_init__(self):
        if (any(type(v) is not int or v < 1 for v in (self.ct_nodes, self.low_level_expansions))
                or self.wall_time_s is not None and
                (not math.isfinite(self.wall_time_s) or self.wall_time_s <= 0)):
            raise ValueError("Positive search limits required")


class _LimitReached(Exception):
    pass


@dataclass
class _Budget:
    limits: SearchLimits
    ct_expanded: int = 0
    low_level_expanded: int = 0
    replans: int = 0
    start: float = field(default_factory=perf_counter)

    def step(self, *, high=False):
        if self.limits.wall_time_s is not None and perf_counter() - self.start >= self.limits.wall_time_s:
            raise _LimitReached("wall_time")
        name = "ct_expanded" if high else "low_level_expanded"
        limit = self.limits.ct_nodes if high else self.limits.low_level_expansions
        if getattr(self, name) >= limit:
            raise _LimitReached("constraint_tree" if high else "low_level")
        setattr(self, name, getattr(self, name) + 1)


@dataclass(frozen=True)
class CoordinationResult:
    algorithm: str
    status: str
    reason: str
    fingerprint: str
    paths: tuple[TimedPath, ...]
    ct_expanded: int
    low_level_expanded: int
    replans: int
    elapsed_s: float
    executable: bool = False


class CoordinationProblem:
    def __init__(self, graph, tasks, *, quantum_s: float, horizon: int,
                 edge_resources=None, node_resources=None, clearance_ticks: int = 0, provenance: str):
        if (not graph.map_version.startswith("synthetic-") or isinstance(quantum_s, bool)
                or not isinstance(quantum_s, (int, float)) or not math.isfinite(quantum_s) or quantum_s <= 0
                or type(horizon) is not int or not 1 <= horizon <= 10000
                or type(clearance_ticks) is not int or not 0 <= clearance_ticks <= horizon
                or not provenance.strip()):
            raise ValueError("Explicit bounded synthetic time model and provenance required")
        graph = graph.model_copy(deep=True)
        tasks = tuple(sorted(tasks, key=lambda a: a.vehicle_id))
        if (not 1 <= len(tasks) <= 32 or len({a.vehicle_id for a in tasks}) != len(tasks)
                or any(not isinstance(a.vehicle_id, str) or not a.vehicle_id.strip()
                       or a.service_type not in ServiceType or type(a.requires_step_free) is not bool for a in tasks)):
            raise ValueError("Unique vehicle IDs and valid service constraints required")
        self.tasks = tasks
        self.quantum_s, self.horizon, self.clearance_ticks = quantum_s, horizon, clearance_ticks
        self.map_version = graph.map_version
        self.edge_resources = self._overlay(edge_resources or {}, {e.id for e in graph.edges})
        self.node_resources = self._overlay(node_resources or {}, {n.id for n in graph.nodes})
        self.adjacency, self.heuristic = {}, {}
        for task in tasks:
            _, adjacency = prepare_routing_snapshot(graph, task.start, task.goal, **task.options())
            self.adjacency[task.vehicle_id] = {node: tuple(sorted(edges, key=lambda e: e.id))
                                               for node, edges in adjacency.items()}
            heuristic = {}
            for node in adjacency:
                try:
                    # Existing verified Dijkstra supplies an admissible lower bound
                    # for the new time-expanded A*; quantized edges round upward.
                    cost = dijkstra(graph, node, task.goal, **task.options()).path_cost_s
                    heuristic[node] = math.floor(cost / quantum_s)
                except NoRouteError:
                    heuristic[node] = math.inf
            self.heuristic[task.vehicle_id] = heuristic
        payload = {"graph": graph.model_dump(mode="json"), "tasks": [asdict(a) for a in tasks],
                   "quantum_s": quantum_s, "horizon": horizon, "clearance_ticks": clearance_ticks,
                   "edge_resources": self.edge_resources, "node_resources": self.node_resources,
                   "provenance": provenance}
        self.fingerprint = hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()

    @staticmethod
    def _overlay(mapping, known):
        if not set(mapping) <= known:
            raise ValueError("Unknown resource overlay reference")
        result = {}
        for key, names in mapping.items():
            if isinstance(names, str):
                raise TypeError("Resource names must be a collection")
            names = tuple(names)
            if any(not isinstance(n, str) or not n.strip() for n in names):
                raise ValueError("Resource names must be explicit nonempty strings in a collection")
            result[key] = tuple(sorted(set(names)))
        return result

    def node_claims(self, node, tick):
        claims = {Claim(tick, "VERTEX", node)}
        claims.update(Claim(t, "RESOURCE", name) for name in self.node_resources.get(node, ())
                      for t in range(tick, tick + self.clearance_ticks + 1))
        return claims

    def move_claims(self, move):
        claims = self.node_claims(move.source, move.depart) | self.node_claims(move.target, move.arrive)
        if move.edge_id is not None:
            # Same/reverse/parallel endpoint edges share a conservative conflict key.
            edge_key = json.dumps(sorted((move.source, move.target)))
            claims.update(Claim(t, "EDGE", edge_key)
                          for t in range(move.depart, move.arrive + self.clearance_ticks))
            claims.update(Claim(t, "RESOURCE", name) for name in self.edge_resources.get(move.edge_id, ())
                          for t in range(move.depart, move.arrive + self.clearance_ticks))
        return claims

    def parking_claims(self, goal, arrival):
        return set().union(*(self.node_claims(goal, t) for t in range(arrival, self.horizon + 1)))

    def low_level(self, task, forbidden, budget):
        budget.replans += 1
        initial = (task.start, 0)
        if self.node_claims(task.start, 0) & forbidden or math.isinf(self.heuristic[task.vehicle_id][task.start]):
            return None
        counter = itertools.count()
        heap = [(self.heuristic[task.vehicle_id][task.start], 0, next(counter), initial)]
        parent = {initial: None}
        while heap:
            budget.step()
            _, tick, _, state = heapq.heappop(heap)
            node, _ = state
            if node == task.goal and not self.parking_claims(node, tick) & forbidden:
                moves = []
                while parent[state] is not None:
                    previous, move = parent[state]
                    moves.append(move)
                    state = previous
                moves.reverse()
                claims = self.node_claims(task.start, 0) | self.parking_claims(task.goal, tick)
                for move in moves:
                    claims.update(self.move_claims(move))
                return TimedPath(task.vehicle_id, task.start, task.goal, tuple(moves), frozenset(claims))
            actions = [TimedMove(node, node, tick, tick + 1, None)]
            actions.extend(TimedMove(node, edge.to_node, tick,
                            tick + max(1, math.ceil(edge.cost_s() / self.quantum_s)), edge.id)
                           for edge in self.adjacency[task.vehicle_id][node])
            for move in actions:
                next_state = (move.target, move.arrive)
                h = self.heuristic[task.vehicle_id][move.target]
                if (move.arrive + h > self.horizon or next_state in parent
                        or self.move_claims(move) & forbidden):
                    continue
                parent[next_state] = (state, move)
                heapq.heappush(heap, (move.arrive + h, move.arrive, next(counter), next_state))
        return None


def conflicts(paths):
    """One record per conflicting occupancy token and vehicle pair."""
    result = []
    for first, second in itertools.combinations(sorted(paths, key=lambda p: p.vehicle_id), 2):
        result.extend((claim, first.vehicle_id, second.vehicle_id) for claim in first.claims & second.claims)
    return sorted(result)


def solve_coordination(problem, *, algorithm="cbs", limits=None, priority_order=None):
    if algorithm not in {"cbs", "priority"}:
        raise ValueError("Expected cbs or priority")
    tasks = {a.vehicle_id: a for a in problem.tasks}
    order = tuple(priority_order) if priority_order is not None else tuple(tasks)
    if len(order) != len(tasks) or set(order) != set(tasks):
        raise ValueError("Priority order must contain every vehicle exactly once")
    budget = _Budget(limits or SearchLimits())

    def result(status, reason, paths=()):
        return CoordinationResult(algorithm, status, reason, problem.fingerprint, tuple(paths),
            budget.ct_expanded, budget.low_level_expanded, budget.replans, perf_counter() - budget.start)

    try:
        if algorithm == "priority":
            paths, reserved = [], set()
            for vehicle in order:
                path = problem.low_level(tasks[vehicle], reserved, budget)
                if path is None:
                    return result("PRIORITY_ORDER_FAILED", "No route under earlier reservations within horizon")
                paths.append(path)
                reserved.update(path.claims)
            return result("SUCCESS", "Finite time model only", paths)
        paths = {}
        for vehicle, task in tasks.items():
            path = problem.low_level(task, frozenset(), budget)
            if path is None:
                return result("NO_SOLUTION_WITHIN_HORIZON", "Individual task infeasible")
            paths[vehicle] = path
        counter = itertools.count()
        root = tuple((vehicle, frozenset()) for vehicle in tasks)
        heap = [(sum(p.arrival for p in paths.values()), len(conflicts(paths.values())), next(counter), root, paths)]
        seen = {root}
        while heap:
            budget.step(high=True)
            _, _, _, constraints, paths = heapq.heappop(heap)
            collisions = conflicts(paths.values())
            if not collisions:
                return result("SUCCESS", "Finite time model only", [paths[v] for v in tasks])
            claim, first, second = collisions[0]
            for vehicle in (first, second):
                child = dict(constraints)
                child[vehicle] = child[vehicle] | {claim}
                key = tuple(child.items())
                if key in seen:
                    continue
                seen.add(key)
                path = problem.low_level(tasks[vehicle], child[vehicle], budget)
                if path is None:
                    continue
                replacement = {**paths, vehicle: path}
                heapq.heappush(heap, (sum(p.arrival for p in replacement.values()), len(conflicts(replacement.values())),
                                     next(counter), key, replacement))
        return result("NO_SOLUTION_WITHIN_HORIZON", "Constraint tree exhausted")
    except _LimitReached as error:
        return result("BUDGET_EXCEEDED", str(error))
