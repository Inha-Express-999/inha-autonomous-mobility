"""Compare real service dispatch policies on frozen route-derived synthetic inputs."""
import hashlib
import json
import math
import platform
import statistics
from copy import deepcopy
from dataclasses import asdict, replace
from pathlib import Path
from time import perf_counter_ns

from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.planning import node_for_stop
from campus_sim.road_graph import RoadGraphDocument
from campus_sim.service import MobilityService


def _finite_nonnegative(value):
    return not isinstance(value, bool) and isinstance(value, (int, float)) and math.isfinite(value) and value >= 0


def build_dispatch_case(map_path, case):
    service = MobilityService.from_synthetic_graph(map_path, active_fleet=True)
    if not _finite_nonnegative(case["now_s"]):
        raise ValueError("Finite nonnegative scenario clock required")
    service.simulation_time_s = case["now_s"]
    count = case.get("assignments_since_fairness", 0)
    if type(count) is not int or not 0 <= count < service.dispatch_priority_policy.fairness_every_n_assignments:
        raise ValueError("Invalid fairness counter")
    service.assignments_since_fairness = count
    nodes = {n.id: n for n in service.graph.nodes}
    if set(case["vehicle_nodes"]) != set(service.vehicle_runtime):
        raise ValueError("Scenario must place every fixture vehicle")
    for vehicle, node_id in case["vehicle_nodes"].items():
        node = nodes[node_id]
        runtime = service.vehicle_runtime[vehicle]
        runtime.node_id, runtime.x, runtime.y = node_id, node.position_m.x, node.position_m.y
        service.vehicles[vehicle].available = False  # Queue all requests before the batch tick.
    if not case["requests"]:
        raise ValueError("At least one request required")
    identities = set()
    for raw in case["requests"]:
        identity, created = raw["id"], raw.get("created_s", service.now_s())
        if not isinstance(identity, str) or not identity.strip() or identity in identities:
            raise ValueError("Unique nonempty request IDs required")
        if not _finite_nonnegative(created) or created > service.now_s():
            raise ValueError("Creation time must not exceed scenario time")
        identities.add(identity)
        values = {k: v for k, v in raw.items() if k not in {"id", "created_s", "pickup_node", "dropoff_node"}}
        command = CreateRequest(command_id=identity, owner_id=identity,
            pickup_landmark_id=nodes[raw["pickup_node"]].landmark_id,
            dropoff_landmark_id=nodes[raw["dropoff_node"]].landmark_id, **values)
        ack = service.create_request(command)
        if not ack.accepted or ack.request.status != RequestStatus.QUEUED:
            raise ValueError("Case must contain API-accepted queued requests")
        request = service.requests.pop(ack.request.id)
        request.id, request.created_s = identity, created
        service.requests[identity] = request
    # IDs above are normalized only for deterministic offline evidence. No commands
    # are replayed in this fixture; discard the old UUID acknowledgement cache.
    service.commands.clear()
    service.command_fingerprints.clear()
    for vehicle in service.vehicles.values():
        vehicle.available = True
    closed = set(case.get("closed_edge_ids", []))
    if not closed <= {e.id for e in service.graph.edges}:
        raise ValueError("Unknown closed edge")
    document = service.graph.model_dump(mode="json")
    for edge in document["edges"]:
        if edge["id"] in closed:
            edge["is_open"] = False
    service.graph = RoadGraphDocument.model_validate(document)
    service.route_duration_cache.clear()
    return service


def _route(service, start, goal, request):
    route, penalties, reason = service._plan_route(start, goal, request.service_type,
                                          request.service_needs.requires_step_free)
    edges = {e.id: e for e in service.graph.edges}
    return {"nodes": list(route.node_ids), "edges": list(route.edge_ids),
            "distance_m": sum(edges[e].length_m for e in route.edge_ids), "reason": reason.value,
            "duration_s": sum(edges[e].length_m / min(5.0, edges[e].allowed_speed_mps) + penalties.get(e, 0)
                              for e in route.edge_ids),
            "segments": [{"edge": e, "length_m": edges[e].length_m,
                          "speed_cap_mps": min(5.0, edges[e].allowed_speed_mps), "penalty_s": penalties.get(e, 0)}
                         for e in route.edge_ids]}


def route_dispatch_snapshot(service):
    if service.coordination is not None:
        raise ValueError("Offline evaluation cannot clone a live coordination worker")
    idle = {v for v in service.vehicle_runtime if service.vehicles[v].available
            and not service.ego_localization_is_stale(v)}
    started = perf_counter_ns()
    pairs = {(p.request_id, p.vehicle_id): p for p in service.dispatch_candidates(idle)}
    cells = []
    for request in sorted(service.requests.values(), key=lambda r: r.id):
        if request.status != RequestStatus.QUEUED:
            continue
        for vehicle in sorted(service.vehicle_runtime):
            candidate = pairs.get((request.id, vehicle))
            row = {"request": request.id, "vehicle": vehicle, "feasible": candidate is not None}
            if candidate is None:
                row.update(cost_s=None, reason="UNAVAILABLE" if vehicle not in idle else
                    "CAPACITY_OR_SERVICE_NEEDS" if not service._request_can_vehicle_serve(service.vehicles[vehicle], request)
                    else "ROUTE_UNAVAILABLE")
            else:
                pickup = node_for_stop(service.graph, request.pickup_stop_id)
                dropoff = node_for_stop(service.graph, request.dropoff_stop_id)
                row.update(cost_s=candidate.cost_s, priority=candidate.request_priority,
                    pickup_s=candidate.pickup_duration_s, trip_s=candidate.trip_duration_s,
                    lateness_s=candidate.lateness_s, predicted_completion_s=candidate.completion_time_s,
                    pickup_route=_route(service, service.vehicle_runtime[vehicle].node_id, pickup, request),
                    trip_route=_route(service, pickup, dropoff, request))
                if (not math.isclose(row["pickup_route"]["duration_s"], candidate.pickup_duration_s)
                        or not math.isclose(row["trip_route"]["duration_s"], candidate.trip_duration_s)):
                    raise AssertionError("Cost cache and recorded route evidence disagree")
            if service.energy is not None:
                energy = service.energy.assess(service, vehicle, request)
                row["energy"] = asdict(energy)
                if candidate is None and row.get("reason") == "ROUTE_UNAVAILABLE" and not energy.allowed:
                    row["reason"] = energy.reason
            cells.append(row)
    return cells, perf_counter_ns() - started


def _percentile(values, q):
    return sorted(values)[max(0, math.ceil(q * len(values)) - 1)] if values else None


def compare_route_dispatch(scenario_path, *, repetitions=20):
    if type(repetitions) is not int or repetitions < 1:
        raise ValueError("Positive repetition count required")
    scenario_path = Path(scenario_path)
    config = json.loads(scenario_path.read_text(encoding="utf-8"))
    if (type(config.get("schema_version")) is not int or config.get("schema_version") != 1 or config.get("data_status") != "SYNTHETIC_ROUTE_DISPATCH"
            or not config.get("cases")):
        raise ValueError("Versioned synthetic route-dispatch scenarios required")
    results = []
    for case in config["cases"]:
        baseline = build_dispatch_case(scenario_path.parent / config["map"], case)
        frozen = {"graph": baseline.graph.model_dump(mode="json"), "case": case,
                  "dispatch_policy": asdict(baseline.dispatch_priority_policy),
                  "planning_policy": asdict(baseline.planning_policy),
                  "crowd": baseline.crowd_model.model_dump(mode="json"),
                  "vehicles": {v: state.model_dump(mode="json") for v, state in baseline.vehicles.items()}}
        fingerprint = hashlib.sha256(json.dumps(frozen, sort_keys=True).encode()).hexdigest()
        cells, preprocessing_ns = route_dispatch_snapshot(deepcopy(baseline))
        lookup = {(c["request"], c["vehicle"]): c for c in cells}
        runs = []
        for iteration in range(repetitions):
            for algorithm in ("greedy", "hungarian"):
                # Each run starts with the same empty route-cost caches, same
                # clock, fairness counter, capabilities and queued requests.
                service = deepcopy(baseline)
                service.dispatch_priority_policy = replace(service.dispatch_priority_policy,
                                                            assignment_algorithm=algorithm)
                start = perf_counter_ns()
                service._dispatch_queued_requests()
                elapsed = perf_counter_ns() - start
                assigned = [r for r in service.requests.values() if r.status == RequestStatus.ASSIGNED]
                chosen = [lookup[(r.id, r.vehicle_id)] for r in assigned]
                if not all(c["feasible"] for c in chosen):
                    raise AssertionError("Production dispatch selected an infeasible snapshot cell")
                pickup = [c["pickup_s"] for c in chosen]
                waits = [service.now_s() - r.created_s for r in assigned]
                empty_m = sum(c["pickup_route"]["distance_m"] for c in chosen)
                trip_m = sum(c["trip_route"]["distance_m"] for c in chosen)
                deadlines = [r for r in assigned if r.latest_arrival_s is not None]
                on_time = sum(lookup[(r.id, r.vehicle_id)]["lateness_s"] == 0 for r in deadlines)
                runs.append({"iteration": iteration, "algorithm": algorithm, "elapsed_ns": elapsed,
                    "assignments": sorted((r.id, r.vehicle_id) for r in assigned),
                    "unassigned": sorted(r.id for r in service.requests.values() if r.status == RequestStatus.QUEUED),
                    "total_cost_s": sum(c["cost_s"] for c in chosen), "assigned_count": len(assigned),
                    "mean_pickup_eta_s": statistics.mean(pickup) if pickup else None,
                    "p95_pickup_eta_s": _percentile(pickup, .95),
                    "mean_preassignment_wait_s": statistics.mean(waits) if waits else None,
                    "p95_preassignment_wait_s": _percentile(waits, .95),
                    "empty_distance_m": empty_m, "total_distance_m": empty_m + trip_m,
                    "empty_distance_ratio": empty_m / (empty_m + trip_m) if empty_m + trip_m else None,
                    "predicted_batch_finish_s": max((c["predicted_completion_s"] for c in chosen), default=None),
                    "predicted_on_time_rate": on_time / len(deadlines) if deadlines else None,
                    "assigned_deadline_count": len(deadlines),
                    "unassigned_deadline_count": sum(r.status == RequestStatus.QUEUED and r.latest_arrival_s is not None
                                                     for r in service.requests.values()),
                    "assigned_by_service": {kind: sum(r.service_type.value == kind for r in assigned)
                                            for kind in ("PASSENGER", "CARGO")},
                    "actual_completion_s": None, "actual_wait_s": None})
        summaries = []
        for algorithm in ("greedy", "hungarian"):
            rows = [r for r in runs if r["algorithm"] == algorithm]
            first = rows[0]
            summaries.append({"algorithm": algorithm, "assigned_count": first["assigned_count"],
                "total_cost_s": first["total_cost_s"], "empty_distance_m": first["empty_distance_m"],
                "p50_ms": _percentile([r["elapsed_ns"] / 1e6 for r in rows], .5),
                "p95_ms": _percentile([r["elapsed_ns"] / 1e6 for r in rows], .95),
                "deterministic_assignments": all(r["assignments"] == first["assignments"] for r in rows)})
        greedy, hungarian = runs[:2]
        same_requests = {r for r, _ in greedy["assignments"]} == {r for r, _ in hungarian["assignments"]}
        results.append({"same_served_requests": same_requests,
                        "cost_reduction_s_same_requests": greedy["total_cost_s"] - hungarian["total_cost_s"]
                        if same_requests else None,
                        "id": case["id"], "fingerprint": fingerprint, "input": frozen,
                        "snapshot_preparation_ns": preprocessing_ns, "cells": cells, "runs": runs, "summary": summaries})
    return {"schema_version": 1, "data_status": "SYNTHETIC_ROUTE_DISPATCH", "repetitions": repetitions,
        "python": platform.python_version(), "platform": platform.platform(), "cases": results,
        "cost_formula": "pickup_s + 0.5 * trip_s + 2 * lateness_s",
        "limits": "Actual production priority/fairness and batch selection are included; not a global assignment oracle. "
                  "ETA/distance/completion are planned synthetic quantities, not Physics or real-campus measurements. "
                  "Energy, charging, scarce-vehicle penalty and workload fairness across completed services remain absent. "
                  "Timing covers candidate generation, policy/matching and pickup-route installation, excluding scenario "
                  "construction/deepcopy/evidence route tracing. Both algorithms start from identical cold caches."}
