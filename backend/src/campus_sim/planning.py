from __future__ import annotations

import heapq
import itertools
import math
from collections.abc import Mapping
from dataclasses import dataclass
from time import perf_counter_ns

from campus_sim.domain import ServiceType
from campus_sim.road_graph import RoadEdge, RoadGraphDocument


class NoRouteError(ValueError):
    """No route satisfies the requested hard constraints."""


@dataclass(frozen=True)
class RouteResult:
    algorithm: str
    node_ids: tuple[str, ...]
    stop_ids: tuple[str, ...]
    edge_ids: tuple[str, ...]
    path_cost_s: float
    path_length_m: float
    expanded_nodes: int
    generated_nodes: int
    peak_open_set_size: int
    elapsed_ns: int


def dijkstra(
    graph: RoadGraphDocument,
    start_node: str,
    goal_node: str,
    *,
    service_type: ServiceType = ServiceType.PASSENGER,
    vehicle_class: str = "CAMPUS_SHUTTLE",
    vehicle_width_m: float | None = None,
    requires_step_free: bool = False,
    edge_costs_s: Mapping[str, float] | None = None,
    excluded_edge_ids: frozenset[str] | set[str] = frozenset(),
) -> RouteResult:
    return _search(
        graph,
        start_node,
        goal_node,
        algorithm="dijkstra",
        service_type=service_type,
        vehicle_class=vehicle_class,
        vehicle_width_m=vehicle_width_m,
        requires_step_free=requires_step_free,
        edge_costs_s=edge_costs_s,
        excluded_edge_ids=excluded_edge_ids,
    )


def astar(
    graph: RoadGraphDocument,
    start_node: str,
    goal_node: str,
    *,
    service_type: ServiceType = ServiceType.PASSENGER,
    vehicle_class: str = "CAMPUS_SHUTTLE",
    vehicle_width_m: float | None = None,
    requires_step_free: bool = False,
    edge_costs_s: Mapping[str, float] | None = None,
    excluded_edge_ids: frozenset[str] | set[str] = frozenset(),
) -> RouteResult:
    """Find an optimal route under base costs or one immutable edge-cost snapshot.

    Snapshot entries replace the full cost for their edge; omitted edges retain the
    RoadGraph base cost. Overrides may add penalties but cannot beat free-flow time.
    """
    return _search(
        graph,
        start_node,
        goal_node,
        algorithm="astar",
        service_type=service_type,
        vehicle_class=vehicle_class,
        vehicle_width_m=vehicle_width_m,
        requires_step_free=requires_step_free,
        edge_costs_s=edge_costs_s,
        excluded_edge_ids=excluded_edge_ids,
    )


def _search(
    graph: RoadGraphDocument,
    start_node: str,
    goal_node: str,
    *,
    algorithm: str,
    service_type: ServiceType,
    vehicle_class: str,
    vehicle_width_m: float | None,
    requires_step_free: bool,
    edge_costs_s: Mapping[str, float] | None,
    excluded_edge_ids: frozenset[str] | set[str],
) -> RouteResult:
    node_by_id = {node.id: node for node in graph.nodes}
    if start_node not in node_by_id:
        raise ValueError(f"unknown start node: {start_node}")
    if goal_node not in node_by_id:
        raise ValueError(f"unknown goal node: {goal_node}")
    if not vehicle_class.strip():
        raise ValueError("vehicle_class must not be empty")
    if vehicle_width_m is not None and (
        not math.isfinite(vehicle_width_m) or vehicle_width_m <= 0
    ):
        raise ValueError("vehicle_width_m must be a finite positive value")
    known_edge_ids = {edge.id for edge in graph.edges}
    unknown_excluded = excluded_edge_ids.difference(known_edge_ids)
    if unknown_excluded:
        raise ValueError(f"edge exclusions reference unknown edges: {sorted(unknown_excluded)}")
    if edge_costs_s is not None:
        edge_by_id = {edge.id: edge for edge in graph.edges}
        unknown_edges = set(edge_costs_s).difference(edge_by_id)
        if unknown_edges:
            raise ValueError(f"edge cost snapshot references unknown edges: {sorted(unknown_edges)}")
        for edge_id, cost_s in edge_costs_s.items():
            free_flow_s = edge_by_id[edge_id].length_m / edge_by_id[edge_id].allowed_speed_mps
            if (
                isinstance(cost_s, bool)
                or not isinstance(cost_s, (int, float))
                or not math.isfinite(cost_s)
                or cost_s < free_flow_s - 1e-9
            ):
                raise ValueError(
                    f"edge cost for {edge_id!r} must be finite and at least free-flow time"
                )

    adjacency: dict[str, list[RoadEdge]] = {node_id: [] for node_id in node_by_id}
    for edge in graph.edges:
        if edge.id in excluded_edge_ids or not edge.is_open or service_type not in edge.allowed_service_types:
            continue
        if vehicle_class not in edge.allowed_vehicle_classes:
            continue
        if vehicle_width_m is not None and edge.width_m < vehicle_width_m:
            continue
        if requires_step_free and not edge.step_free:
            continue
        adjacency[edge.from_node].append(edge)

    max_speed = max(
        (edge.allowed_speed_mps for edges in adjacency.values() for edge in edges),
        default=1.0,
    )
    goal_position = node_by_id[goal_node].position_m

    def heuristic(node_id: str) -> float:
        if algorithm == "dijkstra":
            return 0.0
        position = node_by_id[node_id].position_m
        return math.hypot(position.x - goal_position.x, position.y - goal_position.y) / max_speed

    started_ns = perf_counter_ns()
    counter = itertools.count()
    open_heap: list[tuple[float, int, float, str]] = [
        (heuristic(start_node), next(counter), 0.0, start_node)
    ]
    g_score = {start_node: 0.0}
    parent: dict[str, tuple[str, RoadEdge]] = {}
    generated_nodes = 1
    expanded_nodes = 0
    peak_open_set_size = 1

    while open_heap:
        _, _, queued_cost, current = heapq.heappop(open_heap)
        if queued_cost > g_score.get(current, math.inf) + 1e-12:
            continue
        if current == goal_node:
            break
        expanded_nodes += 1

        for edge in adjacency[current]:
            edge_cost_s = edge.cost_s() if edge_costs_s is None else edge_costs_s.get(edge.id)
            if edge_cost_s is None:
                edge_cost_s = edge.cost_s()
            candidate_cost = queued_cost + edge_cost_s
            if candidate_cost + 1e-12 >= g_score.get(edge.to_node, math.inf):
                continue
            if edge.to_node not in g_score:
                generated_nodes += 1
            g_score[edge.to_node] = candidate_cost
            parent[edge.to_node] = (current, edge)
            heapq.heappush(
                open_heap,
                (
                    candidate_cost + heuristic(edge.to_node),
                    next(counter),
                    candidate_cost,
                    edge.to_node,
                ),
            )
        peak_open_set_size = max(peak_open_set_size, len(open_heap))

    elapsed_ns = perf_counter_ns() - started_ns
    if goal_node not in g_score:
        raise NoRouteError(
            f"no {service_type.value} route from {start_node} to {goal_node} "
            f"(requires_step_free={requires_step_free})"
        )

    reverse_nodes = [goal_node]
    reverse_edges: list[RoadEdge] = []
    current = goal_node
    while current != start_node:
        previous, edge = parent[current]
        reverse_edges.append(edge)
        reverse_nodes.append(previous)
        current = previous
    node_ids = tuple(reversed(reverse_nodes))
    edge_ids = tuple(edge.id for edge in reversed(reverse_edges))
    stop_by_node = {node.id: node.stop_id for node in graph.nodes}
    route_edges = tuple(reversed(reverse_edges))

    return RouteResult(
        algorithm=algorithm,
        node_ids=node_ids,
        stop_ids=tuple(stop_by_node[node_id] for node_id in node_ids),
        edge_ids=edge_ids,
        path_cost_s=g_score[goal_node],
        path_length_m=sum(edge.length_m for edge in route_edges),
        expanded_nodes=expanded_nodes,
        generated_nodes=generated_nodes,
        peak_open_set_size=peak_open_set_size,
        elapsed_ns=elapsed_ns,
    )


def node_for_stop(graph: RoadGraphDocument, stop_id: str) -> str:
    for node in graph.nodes:
        if node.stop_id == stop_id:
            return node.id
    raise ValueError(f"unknown stop id: {stop_id}")
