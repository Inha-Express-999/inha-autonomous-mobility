"""Offline comparison; no result authorizes physical entry or changes live leases."""
import json
import math
import platform
import statistics
from dataclasses import asdict
from pathlib import Path
from time import perf_counter

from campus_sim.coordination import (
    AgentTask,
    CoordinationProblem,
    SearchLimits,
    conflicts,
    solve_coordination,
)
from campus_sim.domain import ServiceType
from campus_sim.road_graph import load_road_graph


def compare_coordination(scenario_path, *, repetitions=5):
    if type(repetitions) is not int or repetitions < 1:
        raise ValueError("Positive repetition count required")
    scenario_path = Path(scenario_path)
    config = json.loads(scenario_path.read_text(encoding="utf-8"))
    if config.get("schema_version") != 1 or config.get("data_status") != "SYNTHETIC_FIXTURE":
        raise ValueError("Versioned synthetic coordination benchmark required")
    source = load_road_graph(scenario_path.parent / config["map"])
    limits = SearchLimits(**config["limits"])
    rows, setup = [], []
    for case in config["cases"]:
        started = perf_counter()
        tasks = [AgentTask(**{**task, "service_type": ServiceType(task.get("service_type", "PASSENGER"))})
                 for task in case["tasks"]]
        problem = CoordinationProblem(source, tasks, quantum_s=config["quantum_s"], horizon=case["horizon"],
            edge_resources=config["edge_resources"], node_resources=config["node_resources"],
            clearance_ticks=config["clearance_ticks"], provenance=config["provenance"])
        setup.append({"case": case["id"], "fingerprint": problem.fingerprint,
                      "preprocessing_s": perf_counter() - started})
        for repetition in range(repetitions):
            for algorithm in ("priority", "cbs", "cbs-disjoint"):
                result = solve_coordination(problem, algorithm=algorithm, limits=limits,
                                            priority_order=case.get("priority_order"))
                success = result.status == "SUCCESS"
                waiting = [p.wait_ticks * problem.quantum_s for p in result.paths]
                rows.append({"case": case["id"], "repetition": repetition, "algorithm": algorithm,
                    "status": result.status, "reason": result.reason, "fingerprint": result.fingerprint,
                    "elapsed_ms": result.elapsed_s * 1000, "ct_expanded": result.ct_expanded,
                    "low_level_expanded": result.low_level_expanded, "low_level_calls": result.replans, "low_level_cache_hits": result.cache_hits,
                    "planned_conflicting_tokens": len(conflicts(result.paths)) if success else None,
                    "makespan_s": max(p.arrival for p in result.paths) * problem.quantum_s if success else None,
                    "sum_arrival_s": sum(p.arrival for p in result.paths) * problem.quantum_s if success else None,
                    "sum_wait_s": sum(waiting) if success else None,
                    "mean_wait_s": statistics.mean(waiting) if success else None,
                    "wait_stddev_s": statistics.pstdev(waiting) if success else None,
                    "physical_conflicts": None, "executable": False,
                    "paths": [{"vehicle": p.vehicle_id, "arrival_tick": p.arrival,
                               "moves": [asdict(m) for m in p.moves]} for p in result.paths]})
    summaries = []
    for case in config["cases"]:
        for algorithm in ("priority", "cbs", "cbs-disjoint"):
            selected = [r for r in rows if r["case"] == case["id"] and r["algorithm"] == algorithm]
            elapsed = sorted(r["elapsed_ms"] for r in selected)
            summaries.append({"case": case["id"], "algorithm": algorithm, "runs": len(selected),
                "successes": sum(r["status"] == "SUCCESS" for r in selected),
                "budget_exceeded": sum(r["status"] == "BUDGET_EXCEEDED" for r in selected),
                "p50_ms": statistics.median(elapsed), "p95_ms": elapsed[math.ceil(0.95 * len(elapsed)) - 1]})
    return {"schema_version": 1, "data_status": "SYNTHETIC_FIXTURE", "map_version": source.map_version,
        "python": platform.python_version(), "platform": platform.platform(), "repetitions": repetitions,
        "input": config, "preprocessing": setup, "summary": summaries, "rows": rows,
        "limits": "Finite quantized plan only; no actuator authority, actual conflicts, live lease expiry, "
                  "continuous footprint, deadlock recovery, sensor delay or performance guarantee. "
                  "Priority is offline fixed-order reservation, not the live ResourceReservations manager. "
                  "Timings cover solve calls; validation/Dijkstra preprocessing is reported separately."}
