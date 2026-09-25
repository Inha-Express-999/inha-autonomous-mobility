from __future__ import annotations

from collections.abc import Callable
from itertools import permutations
from math import ceil, isfinite
from statistics import mean
from time import perf_counter_ns
from typing import Any

from campus_sim.dispatch import minimum_cost_assignment
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
    repetitions: int = 1,
    warmups: int = 0,
) -> dict[str, Any]:
    if not pairs:
        raise ValueError("at least one origin-destination pair is required")
    if repetitions < 1:
        raise ValueError("repetitions must be at least 1")
    if warmups < 0:
        raise ValueError("warmups must not be negative")

    options = {
        "service_type": service_type,
        "vehicle_class": vehicle_class,
        "vehicle_width_m": vehicle_width_m,
        "requires_step_free": requires_step_free,
    }
    for _ in range(warmups):
        _compare_routes_once(graph, pairs, **options)
    runs = [_compare_routes_once(graph, pairs, **options) for _ in range(repetitions)]
    report = runs[0]

    durations = {
        algorithm: [
            pair[algorithm]["elapsed_ns"]
            for run in runs
            for pair in run["pairs"]
            if pair[algorithm] is not None
        ]
        for algorithm in ("dijkstra", "astar")
    }
    outcome_matches = [
        run["summary"]["route_outcome_match_count"] == len(pairs)
        and run["summary"]["reachability_mismatch_pair_count"] == 0
        for run in runs
    ]
    report["comparison"]["repetitions"] = repetitions
    report["comparison"]["warmups"] = warmups
    report["comparison"]["timings_note"] = (
        "Per-route planner elapsed_ns samples; p50/p95 use nearest-rank percentiles. "
        "Synthetic fixture timings are not campus performance evidence."
    )
    report["comparison"]["pair_records_note"] = (
        "Per-pair route details and elapsed_ns come from the first measured repetition; "
        "aggregate timing statistics include all measured repetitions."
    )
    report["input_validation"] = {
        "contract": "RoadGraphDocument",
        "topology_and_geometry_validated": True,
        "node_count": len(graph.nodes),
        "edge_count": len(graph.edges),
        "geometry_point_count": sum(len(edge.geometry_m) for edge in graph.edges),
        "edge_endpoint_checks": len(graph.edges),
    }
    report["measurement"] = {
        "pair_count": len(pairs),
        "repetitions": repetitions,
        "warmups": warmups,
        "path_outcomes_match_all_repetitions": all(outcome_matches),
        "matching_repetitions": sum(outcome_matches),
        "duration_ns": {
            algorithm: _timing_summary(samples)
            for algorithm, samples in durations.items()
        },
    }
    return report


def compare_dispatch_assignments(
    scenario: dict[str, Any],
    *,
    repetitions: int = 1,
    warmups: int = 0,
) -> dict[str, Any]:
    """Compare priority-ordered Greedy and Hungarian matching on one cost snapshot."""
    if repetitions < 1:
        raise ValueError("repetitions must be at least 1")
    if warmups < 0:
        raise ValueError("warmups must not be negative")
    vehicles, requests = _validate_dispatch_scenario(scenario)
    options = (vehicles, requests)
    for _ in range(warmups):
        _dispatch_comparison_once(*options)
    runs: list[dict[str, Any]] = []
    durations = {"greedy": [], "hungarian": []}
    for _ in range(repetitions):
        run = _dispatch_comparison_once(*options)
        runs.append(run)
        for algorithm, samples in durations.items():
            samples.append(run[algorithm]["elapsed_ns"])
    first = runs[0]
    request_ids = [request["request_id"] for request in requests]
    greedy = _assignment_summary(first["greedy"]["assignments"], 0, request_ids)
    hungarian = _assignment_summary(first["hungarian"]["assignments"], 0, request_ids)
    greedy["elapsed_ns"] = first["greedy"]["elapsed_ns"]
    hungarian["elapsed_ns"] = first["hungarian"]["elapsed_ns"]
    return {
        "schema_version": 1,
        "scenario_id": scenario["scenario_id"],
        "data_status": "SYNTHETIC_COST_MATRIX",
        "comparison": {
            "priority_order": "descending priority, then created_s, then request_id",
            "cost_snapshot": "identical finite J(v,r) values and hard-constraint None cells",
            "energy_model_included": False,
            "note": "This compares assignment solvers only; it is not campus dispatch evidence.",
        },
        "input_validation": {
            "vehicle_count": len(vehicles),
            "request_count": len(requests),
            "hard_constraint_cells": sum(
                cost is None for request in requests for cost in request["costs"].values()
            ),
        },
        "greedy": greedy,
        "hungarian": hungarian,
        "measurement": {
            "repetitions": repetitions,
            "warmups": warmups,
            "deterministic_assignments": all(
                run["greedy"]["assignments"] == first["greedy"]["assignments"]
                and run["hungarian"]["assignments"] == first["hungarian"]["assignments"]
                for run in runs
            ),
            "duration_ns": {
                algorithm: _timing_summary(samples)
                for algorithm, samples in durations.items()
            },
            "duration_samples_ns": durations,
        },
    }


def _validate_dispatch_scenario(
    scenario: dict[str, Any],
) -> tuple[list[str], list[dict[str, Any]]]:
    if type(scenario.get("schema_version")) is not int or scenario["schema_version"] != 1:
        raise ValueError("dispatch scenario schema_version must be 1")
    scenario_id = scenario.get("scenario_id")
    if not isinstance(scenario_id, str) or not scenario_id.strip():
        raise ValueError("dispatch scenario_id is required")
    if scenario.get("data_status") != "SYNTHETIC_COST_MATRIX":
        raise ValueError("dispatch scenario must declare SYNTHETIC_COST_MATRIX")
    vehicles = scenario.get("vehicles")
    raw_requests = scenario.get("requests")
    if (
        not isinstance(vehicles, list)
        or not vehicles
        or any(not isinstance(vehicle, str) or not vehicle.strip() for vehicle in vehicles)
        or len(set(vehicles)) != len(vehicles)
    ):
        raise ValueError("vehicles must be a non-empty list of unique IDs")
    if not isinstance(raw_requests, list) or not raw_requests:
        raise ValueError("requests must be a non-empty list")

    requests: list[dict[str, Any]] = []
    request_ids: set[str] = set()
    for raw in raw_requests:
        if not isinstance(raw, dict):
            raise TypeError("each request must be an object")
        request_id = raw.get("request_id")
        priority = raw.get("priority")
        created_s = raw.get("created_s")
        costs = raw.get("costs")
        if not isinstance(request_id, str) or not request_id.strip() or request_id in request_ids:
            raise ValueError("request IDs must be non-empty and unique")
        if (
            isinstance(priority, bool)
            or not isinstance(priority, (int, float))
            or not isfinite(priority)
        ):
            raise ValueError("request priority must be finite")
        if (
            isinstance(created_s, bool)
            or not isinstance(created_s, (int, float))
            or not isfinite(created_s)
            or created_s < 0
        ):
            raise ValueError("request created_s must be finite and non-negative")
        if not isinstance(costs, dict) or set(costs) != set(vehicles):
            raise ValueError("each request costs map must name every vehicle exactly once")
        for vehicle_id, cost in costs.items():
            if cost is not None and (
                isinstance(cost, bool)
                or not isinstance(cost, (int, float))
                or not isfinite(cost)
                or cost < 0
            ):
                raise ValueError(f"cost for {request_id}/{vehicle_id} must be finite and non-negative")
        request_ids.add(request_id)
        requests.append({
            "request_id": request_id,
            "priority": float(priority),
            "created_s": float(created_s),
            "costs": costs,
        })
    return vehicles, requests


def _dispatch_comparison_once(
    vehicles: list[str], requests: list[dict[str, Any]]
) -> dict[str, Any]:
    ordered = sorted(
        requests,
        key=lambda request: (-request["priority"], request["created_s"], request["request_id"]),
    )
    available = set(vehicles)
    greedy_assignments: list[dict[str, Any]] = []
    started = perf_counter_ns()
    for request in ordered:
        candidates = [
            (request["costs"][vehicle_id], vehicle_id)
            for vehicle_id in available
            if request["costs"][vehicle_id] is not None
        ]
        if not candidates:
            continue
        cost, vehicle_id = min(candidates)
        available.remove(vehicle_id)
        greedy_assignments.append(_assignment(request, vehicle_id, cost))
    greedy_elapsed = perf_counter_ns() - started

    feasible_requests = [
        request
        for request in ordered
        if any(cost is not None for cost in request["costs"].values())
    ][:len(vehicles)]
    cost_matrix = [
        [request["costs"][vehicle_id] for vehicle_id in vehicles]
        for request in feasible_requests
    ]
    started = perf_counter_ns()
    matching = minimum_cost_assignment(cost_matrix)
    hungarian_elapsed = perf_counter_ns() - started
    hungarian_assignments = [
        _assignment(feasible_requests[row], vehicles[column], cost_matrix[row][column])
        for row, column in matching
    ]
    return {
        "greedy": _assignment_summary(greedy_assignments, greedy_elapsed),
        "hungarian": _assignment_summary(hungarian_assignments, hungarian_elapsed),
    }


def _assignment(request: dict[str, Any], vehicle_id: str, cost: float) -> dict[str, Any]:
    return {
        "request_id": request["request_id"],
        "vehicle_id": vehicle_id,
        "cost_s": round(float(cost), 9),
    }


def _assignment_summary(
    assignments: list[dict[str, Any]],
    elapsed_ns: int,
    request_ids: list[str] | None = None,
) -> dict[str, Any]:
    matched_request_ids = {item["request_id"] for item in assignments}
    return {
        "assignments": assignments,
        "matched_count": len(assignments),
        "total_cost_s": round(sum(item["cost_s"] for item in assignments), 9),
        "unmatched_request_ids": (
            [request_id for request_id in request_ids if request_id not in matched_request_ids]
            if request_ids is not None
            else []
        ),
        "elapsed_ns": elapsed_ns,
    }


def _compare_routes_once(
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


def _timing_summary(samples: list[int]) -> dict[str, int | float | None]:
    ordered = sorted(samples)
    if not ordered:
        return {"sample_count": 0, "mean": None, "min": None, "p50": None, "p95": None, "max": None}
    return {
        "sample_count": len(ordered),
        "mean": mean(ordered),
        "min": ordered[0],
        "p50": ordered[ceil(0.50 * len(ordered)) - 1],
        "p95": ordered[ceil(0.95 * len(ordered)) - 1],
        "max": ordered[-1],
    }
