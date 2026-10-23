"""Directed multigraph with self-implemented Dijkstra and consistent A*.

Coordinates are local metric (east, north). No Unity dynamic Ground Truth is used.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass, field
from heapq import heappop, heappush
from math import dist, isfinite
from time import perf_counter
from types import MappingProxyType

Point = tuple[float, float]
EdgeId = tuple[str, str, str]


def _nonnegative(value: float, name: str) -> None:
    if not isfinite(value) or value < 0:
        raise ValueError(f"{name} must be finite and nonnegative")


@dataclass(frozen=True)
class RoadEdge:
    u: str
    v: str
    key: str
    polyline: tuple[Point, ...]
    length_m: float
    speed_mps: float
    width_m: float | None = None
    vehicle_allowed: bool = True
    stairs: bool = False
    verified: bool = False

    @property
    def id(self) -> EdgeId:
        return self.u, self.v, self.key

    def __post_init__(self) -> None:
        object.__setattr__(self, "polyline", tuple(tuple(p) for p in self.polyline))
        if not all(isinstance(part, str) and part for part in self.id):
            raise ValueError("edge ids must be nonempty strings")
        _nonnegative(self.length_m, "length_m")
        if not isfinite(self.speed_mps) or self.speed_mps <= 0:
            raise ValueError("speed_mps must be finite and positive")
        if self.width_m is not None and (not isfinite(self.width_m) or self.width_m <= 0):
            raise ValueError("width_m must be finite and positive")
        if len(self.polyline) < 2 or any(
            len(p) != 2 or not all(isfinite(x) for x in p) for p in self.polyline
        ):
            raise ValueError("polyline requires at least two finite XY points")
        if self.length_m + 1e-6 < sum(dist(a, b) for a, b in zip(self.polyline, self.polyline[1:])):
            raise ValueError("length_m cannot be shorter than polyline")
        if any(
            type(value) is not bool for value in (self.vehicle_allowed, self.stairs, self.verified)
        ):
            raise ValueError("edge flags must be explicit booleans")


@dataclass(frozen=True)
class RoadGraph:
    map_version: str
    nodes: Mapping[str, Point]
    edges: tuple[RoadEdge, ...]
    synthetic: bool = True
    _outgoing: Mapping[str, tuple[RoadEdge, ...]] = field(init=False, repr=False)

    def __post_init__(self) -> None:
        if not self.map_version or type(self.synthetic) is not bool:
            raise ValueError("map_version and explicit synthetic flag required")
        nodes = {node: tuple(point) for node, point in self.nodes.items()}
        edges = tuple(self.edges)
        if any(
            not isinstance(node, str) or not node or len(p) != 2 or not all(isfinite(x) for x in p)
            for node, p in nodes.items()
        ):
            raise ValueError("nodes require nonempty ids and finite XY points")
        outgoing: dict[str, list[RoadEdge]] = {node: [] for node in nodes}
        seen = set()
        for edge in edges:
            if edge.id in seen:
                raise ValueError("duplicate (u, v, key)")
            seen.add(edge.id)
            if edge.u not in nodes or edge.v not in nodes:
                raise ValueError("edge references unknown node")
            if edge.polyline[0] != nodes[edge.u] or edge.polyline[-1] != nodes[edge.v]:
                raise ValueError("edge geometry endpoints must match nodes")
            outgoing[edge.u].append(edge)
        object.__setattr__(self, "nodes", MappingProxyType(nodes))
        object.__setattr__(self, "edges", edges)
        object.__setattr__(
            self,
            "_outgoing",
            MappingProxyType(
                {
                    node: tuple(sorted(values, key=lambda e: e.id))
                    for node, values in outgoing.items()
                }
            ),
        )


@dataclass(frozen=True)
class CostSnapshot:
    id: str
    map_version: str
    costs_s: Mapping[EdgeId, float]

    def __post_init__(self) -> None:
        if not self.id or not self.map_version:
            raise ValueError("snapshot id and map_version required")
        costs = dict(self.costs_s)
        for cost in costs.values():
            _nonnegative(cost, "edge cost")
        object.__setattr__(self, "costs_s", MappingProxyType(costs))

    @classmethod
    def travel_time(cls, graph: RoadGraph, snapshot_id: str = "free-flow") -> CostSnapshot:
        return cls(
            snapshot_id,
            graph.map_version,
            {edge.id: edge.length_m / edge.speed_mps for edge in graph.edges},
        )


@dataclass(frozen=True)
class SearchStats:
    expanded_nodes: int
    generated_nodes: int
    peak_open_set_size: int
    planning_time_s: float
    expanded_node_ids: tuple[str, ...] = ()


@dataclass(frozen=True)
class RoutePlan:
    map_version: str
    cost_snapshot_id: str
    edge_ids: tuple[EdgeId, ...]
    polyline: tuple[Point, ...]
    cost_s: float
    length_m: float
    stats: SearchStats


@dataclass(frozen=True)
class NoPath:
    map_version: str
    cost_snapshot_id: str
    stats: SearchStats
    reason: str = "NO_PATH"


class DijkstraPlanner:
    def heuristic_scale(self, graph: RoadGraph, costs: CostSnapshot) -> float:
        return 0.0

    def search(
        self,
        graph: RoadGraph,
        start: str,
        goal: str,
        costs: CostSnapshot,
        *,
        vehicle_width_m: float | None = None,
        closed_edges: frozenset[EdgeId] = frozenset(),
    ) -> RoutePlan | NoPath:
        started = perf_counter()
        if start not in graph.nodes or goal not in graph.nodes:
            raise ValueError("unknown start or goal")
        if costs.map_version != graph.map_version:
            raise ValueError("cost snapshot map_version mismatch")
        edge_ids = {edge.id for edge in graph.edges}
        if set(costs.costs_s) != edge_ids or not closed_edges <= edge_ids:
            raise ValueError("snapshot/closure must reference graph edges exactly")
        if vehicle_width_m is not None and (not isfinite(vehicle_width_m) or vehicle_width_m <= 0):
            raise ValueError("vehicle_width_m must be finite and positive")
        if not graph.synthetic and vehicle_width_m is None:
            raise ValueError("real-map search requires vehicle width")

        best = {start: 0.0}
        parent: dict[str, RoadEdge] = {}
        scale = self.heuristic_scale(graph, costs)

        def heuristic(node: str) -> float:
            return dist(graph.nodes[node], graph.nodes[goal]) * scale

        heap = [(heuristic(start), 0.0, start)]
        expanded, generated, peak = 0, 1, 1
        expanded_ids = []

        def stats() -> SearchStats:
            return SearchStats(
                expanded, generated, peak, perf_counter() - started, tuple(expanded_ids)
            )

        while heap:
            _, cost, node = heappop(heap)
            if cost != best[node]:  # Discard stale entries after a shorter relaxation.
                continue
            expanded += 1
            expanded_ids.append(node)
            if node == goal:
                route = []
                while node != start:
                    edge = parent[node]
                    route.append(edge)
                    node = edge.u
                route.reverse()
                points = [graph.nodes[start]]
                for edge in route:
                    points.extend(edge.polyline[1:])
                return RoutePlan(
                    graph.map_version,
                    costs.id,
                    tuple(e.id for e in route),
                    tuple(points),
                    cost,
                    sum(e.length_m for e in route),
                    stats(),
                )
            for edge in graph._outgoing[node]:
                if edge.id in closed_edges or not edge.vehicle_allowed or edge.stairs:
                    continue
                if not graph.synthetic and (not edge.verified or edge.width_m is None):
                    continue
                if vehicle_width_m is not None and (
                    edge.width_m is None or edge.width_m < vehicle_width_m
                ):
                    continue
                candidate = cost + costs.costs_s[edge.id]
                if not isfinite(candidate):
                    raise ValueError("accumulated path cost overflow")
                if candidate < best.get(edge.v, float("inf")):
                    best[edge.v] = candidate
                    parent[edge.v] = edge
                    heappush(heap, (candidate + heuristic(edge.v), candidate, edge.v))
                    generated += 1
                    peak = max(peak, len(heap))
        return NoPath(graph.map_version, costs.id, stats())


class AStarPlanner(DijkstraPlanner):
    def heuristic_scale(self, graph: RoadGraph, costs: CostSnapshot) -> float:
        """Consistent lower bound for any nonnegative cost snapshot, even zero costs.

        h(n)=Euclidean(n,goal)*min(cost(e)/chord(e)). Triangle inequality
        gives h(u)<=cost(u,v)+h(v). Unknown/penalized edges never overestimate.
        The shared expansion loop applies exactly the baseline hard constraints.
        """
        return min(
            (
                costs.costs_s[e.id] / distance
                for e in graph.edges
                if (distance := dist(graph.nodes[e.u], graph.nodes[e.v])) > 0
            ),
            default=0.0,
        )
