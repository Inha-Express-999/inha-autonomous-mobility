"""Reproducible synthetic full-replan/incremental comparison, including failures."""
import math
import platform
import random
from hashlib import sha256
from time import perf_counter_ns, process_time_ns

from campus_sim.dstar_lite import DStarLite
from campus_sim.planning import NoRouteError, astar, dijkstra


def compare_replans(graph, *, seed=17, repetitions=20, event_count=30):
    if any(isinstance(v, bool) or not isinstance(v, int) or v < 1
           for v in (repetitions, event_count)):
        raise ValueError("Positive repetitions and event count required")
    nodes = sorted(n.id for n in graph.nodes)
    if len(nodes) < 2 or not graph.edges:
        raise ValueError("Comparison needs at least two nodes and one edge")
    rng = random.Random(seed)
    events = []
    for index in range(event_count):
        start = nodes[(index // 5) % (len(nodes) - 1)]
        costs = {} if index % 5 in (0, 4) else {
            e.id: e.cost_s() + rng.uniform(0, 200)
            for e in rng.sample(graph.edges, min(3, len(graph.edges)))
        }
        closed = (frozenset(e.id for e in graph.edges) if index % 5 == 3
                  else frozenset(e.id for e in rng.sample(graph.edges, min(2, len(graph.edges))))
                  if index % 5 == 2 else frozenset())
        events.append((start, costs, closed))
    rows = []
    for repetition in range(repetitions):
        planner = None
        previous = {"astar": None, "dstar_lite": None}
        for index, (start, costs, closed) in enumerate(events):
            try:
                oracle = dijkstra(graph, start, nodes[-1], edge_costs_s=costs, excluded_edge_ids=closed)
            except NoRouteError:
                oracle = None
            results = {}
            # Alternate ordering to reduce a fixed first-run advantage; cold start is included.
            order = ("astar", "dstar_lite") if repetition % 2 == 0 else ("dstar_lite", "astar")
            for algorithm in order:
                wall, cpu = perf_counter_ns(), process_time_ns()
                try:
                    if algorithm == "astar":
                        result = astar(graph, start, nodes[-1], edge_costs_s=costs,
                                       excluded_edge_ids=closed)
                    else:
                        if planner is None:
                            planner = DStarLite(graph, start, nodes[-1])
                        result = planner.replan(start, edge_costs_s=costs, excluded_edge_ids=closed)
                except NoRouteError:
                    result = None
                cpu_ns, wall_ns = process_time_ns() - cpu, perf_counter_ns() - wall
                if ((result is None) != (oracle is None) or result is not None and
                        (not math.isclose(result.path_cost_s, oracle.path_cost_s, abs_tol=1e-8)
                         or closed.intersection(result.edge_ids))):
                    raise AssertionError(f"Invalid {algorithm} result at event {index}")
                path = result.edge_ids if result else None
                results[algorithm] = {"cost_s": result.path_cost_s if result else None,
                    "edge_ids": path, "wall_ns": wall_ns, "cpu_ns": cpu_ns,
                    "expanded_nodes": result.expanded_nodes if result else
                    planner.expanded_nodes if algorithm == "dstar_lite" else None,
                    "updated_nodes": planner.updated_nodes if algorithm == "dstar_lite" else None,
                    "route_changed": index > 0 and path != previous[algorithm]}
                previous[algorithm] = path
            rows.append({"repetition": repetition, "event": index, "start": start,
                         "goal": nodes[-1], "costs_s": costs, "excluded": sorted(closed),
                         "results": results})

    def percentile(values, fraction):
        ordered = sorted(values)
        return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]

    summary = {}
    for algorithm in ("astar", "dstar_lite"):
        values = [row["results"][algorithm] for row in rows]
        summary[algorithm] = {
            "wall_p50_ns": percentile([v["wall_ns"] for v in values], 0.5),
            "wall_p95_ns": percentile([v["wall_ns"] for v in values], 0.95),
            "cpu_total_ns": sum(v["cpu_ns"] for v in values),
            "expanded_nodes_known_total": sum(v["expanded_nodes"] or 0 for v in values),
            "updated_nodes_known_total": sum(v["updated_nodes"] or 0 for v in values),
            "route_changes": sum(v["route_changed"] for v in values),
            "no_route_events": sum(v["cost_s"] is None for v in values),
        }
    return {"scope": "synthetic fixed-topology planner comparison, not Physics or event delivery latency",
            "map_version": graph.map_version, "graph_sha256": sha256(graph.model_dump_json().encode()).hexdigest(),
            "seed": seed, "repetitions": repetitions, "events_per_repetition": event_count,
            "python": platform.python_version(), "platform": platform.platform(),
            "timing": "whole call wall/CPU including validation and cold D* construction; no warmups",
            "metric_limits": "A* no-route expansion counts unavailable; route ties may differ; no speedup assumed",
            "all_costs_match_dijkstra": True, "summary": summary, "rows": rows}
