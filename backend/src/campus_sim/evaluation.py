from __future__ import annotations

from collections.abc import Callable
from itertools import permutations
from statistics import mean
from typing import Any

from campus_sim.domain import ServiceType
from campus_sim.planning import NoRouteError, RouteResult, astar, dijkstra
from campus_sim.road_graph import RoadGraphDocument


def compare_routes(
    graph: RoadGraphDocument,
    pairs: list[tuple[str, str]],
    *,
    service_type: ServiceType = ServiceType.PASSENGER,
    vehicle_class: str = "CAMPUS_SHUTTLE",
    vehicle_width_m: float | None = None,
    requires_step_free: bool = False,
) -> dict[str, Any]:
    pair_results: list[dict[str, Any]] = []
    matched_outcome_pairs = 0
    matched_cost_pairs = 0
    reachable_pairs = 0
    both_unreachable_pairs = 0
    reachability_mismatch_pairs = 0
    astar_lower_expansion_pairs = 0
    dijkstra_expanded: list[int] = []
    astar_expanded: list[int] = []

    for start_node, goal_node in pairs:
        dijkstra_result = _run_or_none(
            dijkstra,
            graph,
            start_node,
            goal_node,
            service_type=service_type,
            vehicle_class=vehicle_class,
            vehicle_width_m=vehicle_width_m,
            requires_step_free=requires_step_free,
        )
        astar_result = _run_or_none(
            astar,
            graph,
            start_node,
            goal_node,
            service_type=service_type,
            vehicle_class=vehicle_class,
            vehicle_width_m=vehicle_width_m,
            requires_step_free=requires_step_free,
        )

        both_unreachable = dijkstra_result is None and astar_result is None
        if both_unreachable:
            both_unreachable_pairs += 1
            matched_outcome_pairs += 1
        elif (dijkstra_result is None) != (astar_result is None):
            reachability_mismatch_pairs += 1

        costs_match = (
            dijkstra_result is not None
            and astar_result is not None
            and abs(dijkstra_result.path_cost_s - astar_result.path_cost_s) <= 1e-9
        )
        if costs_match:
            matched_cost_pairs += 1
            matched_outcome_pairs += 1
        if dijkstra_result is not None and astar_result is not None:
            reachable_pairs += 1
            dijkstra_expanded.append(dijkstra_result.expanded_nodes)
            astar_expanded.append(astar_result.expanded_nodes)
            if astar_result.expanded_nodes < dijkstra_result.expanded_nodes:
                astar_lower_expansion_pairs += 1

        pair_results.append(
            {
                "start_node": start_node,
                "goal_node": goal_node,
                "start_stop_id": _stop_for_node(graph, start_node),
                "goal_stop_id": _stop_for_node(graph, goal_node),
                "route_outcome_match": costs_match or both_unreachable,
                "path_cost_match": costs_match,
                "dijkstra": _serialize_result(dijkstra_result),
                "astar": _serialize_result(astar_result),
            }
        )

    return {
        "schema_version": 1,
        "map_id": graph.map_id,
        "map_version": graph.map_version,
        "data_status": graph.data_status,
        "coordinate_frame": graph.coordinate_frame,
        "verification_status": graph.verification_status,
        "source_description": graph.source_description,
        "source_date": graph.source_date,
        "source_hash": graph.source_hash,
        "origin_wgs84": (
            graph.origin_wgs84.model_dump() if graph.origin_wgs84 is not None else None
        ),
        "comparison": {
            "service_type": service_type.value,
            "vehicle_class": vehicle_class,
            "vehicle_width_m": vehicle_width_m,
            "requires_step_free": requires_step_free,
            "cost_formula": "length_m / allowed_speed_mps + crowd + zone + expected_wait",
            "heuristic": "euclidean_distance / maximum_open_edge_speed",
            "timings_note": "Single-run nanosecond timings are diagnostic only, not performance evidence.",
        },
        "summary": {
            "pair_count": len(pair_results),
            "reachable_pair_count": reachable_pairs,
            "both_unreachable_pair_count": both_unreachable_pairs,
            "reachability_mismatch_pair_count": reachability_mismatch_pairs,
            "route_outcome_match_count": matched_outcome_pairs,
            "path_cost_match_count": matched_cost_pairs,
            "astar_lower_expansion_pair_count": astar_lower_expansion_pairs,
            "mean_expanded_nodes": {
                "dijkstra": mean(dijkstra_expanded) if dijkstra_expanded else None,
                "astar": mean(astar_expanded) if astar_expanded else None,
            },
        },
        "pairs": pair_results,
    }


def all_ordered_pairs(node_ids: list[str]) -> list[tuple[str, str]]:
    return list(permutations(node_ids, 2))


def _run_or_none(
    algorithm: Callable[..., RouteResult],
    graph: RoadGraphDocument,
    start_node: str,
    goal_node: str,
    *,
    service_type: ServiceType,
    vehicle_class: str,
    vehicle_width_m: float | None,
    requires_step_free: bool,
) -> RouteResult | None:
    try:
        return algorithm(
            graph, start_node, goal_node,
            service_type=service_type,
            vehicle_class=vehicle_class,
            vehicle_width_m=vehicle_width_m,
            requires_step_free=requires_step_free,
        )
    except NoRouteError:
        return None


def _serialize_result(result: RouteResult | None) -> dict[str, Any] | None:
    if result is None:
        return None
    return {
        "node_ids": list(result.node_ids),
        "stop_ids": list(result.stop_ids),
        "edge_ids": list(result.edge_ids),
        "path_cost_s": round(result.path_cost_s, 9),
        "path_length_m": round(result.path_length_m, 6),
        "expanded_nodes": result.expanded_nodes,
        "generated_nodes": result.generated_nodes,
        "peak_open_set_size": result.peak_open_set_size,
        "elapsed_ns": result.elapsed_ns,
    }


def _stop_for_node(graph: RoadGraphDocument, node_id: str) -> str:
    for node in graph.nodes:
        if node.id == node_id:
            return node.stop_id
    raise ValueError(f"unknown node id: {node_id}")
