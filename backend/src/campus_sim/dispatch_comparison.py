"""Isolated event-driven, identical-input dispatch experiments for the web demo."""

from hashlib import sha256
from itertools import groupby
from json import dumps
from math import ceil
from statistics import fmean
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.service import MobilityService


class DispatchComparison(BaseModel):
    model_config = ConfigDict(extra="forbid")
    source: Literal["mixed", "extensions", "battery", "current"] = "mixed"
    planner: Literal["astar", "dijkstra"] = "astar"
    repeats: int = Field(default=3, ge=1, le=5, strict=True)


def percentile(values, p):
    return sorted(values)[max(0, ceil(len(values) * p) - 1)] if values else None


def workload(source, current):
    if source == "current":
        requests = [r for r in current if r.status is not RequestStatus.CANCELLED]
        if not requests:
            raise ValueError("현재 접수 요청이 없습니다")
        origin = min(
            [r.created_s for r in requests]
            + [r.latest_arrival_s for r in requests if r.latest_arrival_s is not None]
        )
        return [
            {
                "at_s": r.created_s - origin,
                "command": {
                    "service_type": r.service_type.value,
                    "pickup_landmark_id": r.pickup_landmark_id,
                    "dropoff_landmark_id": r.dropoff_landmark_id,
                    "service_needs": r.service_needs.model_dump(),
                    "party_size": r.party_size,
                    "cargo_kg": r.cargo_kg,
                    "latest_arrival_s": None
                    if r.latest_arrival_s is None
                    else r.latest_arrival_s - origin,
                },
            }
            for r in requests
        ]
    if source in {"extensions", "battery"}:
        destinations = (
            ("hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station")
            if source == "extensions"
            else ("hitech",) * 18
        )
        pairs = [("main_gate", destination) for destination in destinations]
    else:
        pairs = [
            ("main_gate", "hitech"),
            ("rear_gate", "dorm_3"),
            ("library", "inha_station"),
            ("main_gate", "dorm_2"),
            ("rear_gate", "dorm_1"),
            ("anniversary_60", "main_gate"),
            ("building_5", "hitech"),
            ("hitech", "inha_station"),
            ("dorm_2", "rear_gate"),
            ("library", "dorm_3"),
            ("main_gate", "inha_station"),
            ("rear_gate", "library"),
        ]
    result = []
    for i, (pickup, dropoff) in enumerate(pairs):
        at = 0 if source != "mixed" or i < 6 else (180 if i < 9 else 400)
        cargo = source == "mixed" and i in {2, 5, 10}
        result.append(
            {
                "at_s": at,
                "command": {
                    "service_type": "CARGO" if cargo else "PASSENGER",
                    "pickup_landmark_id": pickup,
                    "dropoff_landmark_id": dropoff,
                    "party_size": 0 if cargo else 1,
                    "cargo_kg": 5 if cargo else 0,
                    "latest_arrival_s": at + 300
                    if source == "mixed" and i in {1, 4, 7, 11}
                    else None,
                },
            }
        )
    return result


def run_once(items, policy, planner):
    service = MobilityService.from_osm_map(clock=lambda: 0.0)
    service.route_algorithm, service.dispatch_algorithm = planner, policy
    for at, group in groupby(items, key=lambda indexed: indexed[1]["at_s"]):
        service.step(at)
        service.create_batch(
            [
                CreateRequest(command_id=f"compare-{i}", owner_id="comparison", **item["command"])
                for i, item in group
            ]
        )
    # Only returns and charge completions advance this time-only simulation.
    for _ in range(1000):
        due = [t.returns_at_s for t in service.trips.values() if not t.returned]
        due.extend(c.ends_at_s for c in service.charges if not c.completed)
        if not due:
            break
        service.step(min(due))
    else:
        raise RuntimeError("comparison event budget exceeded")
    waits, pickups, completions, distances, empty = [], [], [], [], []
    assignments = []
    for i, request in enumerate(service.requests.values(), 1):
        trip = service.trips.get(request.id)
        if trip is None:
            assignments.append(
                {"request": i, "vehicle": None, "reason": service.waiting_reasons.get(request.id)}
            )
            continue
        waits.append(trip.departed_at_s - request.created_s)
        pickups.append(trip.pickup_at_s - request.created_s)
        completions.append(trip.dropoff_at_s)
        distances.append(
            sum(r.length_m for r in (trip.pickup_route, trip.delivery_route, trip.return_route))
        )
        empty.append(trip.pickup_route.length_m + trip.return_route.length_m)
        assignments.append(
            {
                "request": i,
                "vehicle": trip.vehicle_id,
                "assigned_s": trip.departed_at_s,
                "pickup_s": trip.pickup_at_s,
                "completed_s": trip.dropoff_at_s,
                "returned_s": trip.returns_at_s,
            }
        )
    deadline_requests = [r for r in service.requests.values() if r.latest_arrival_s is not None]
    on_time = sum(
        r.id in service.trips and service.trips[r.id].dropoff_at_s <= r.latest_arrival_s
        for r in deadline_requests
    )
    metrics = {
        "completed": len(completions),
        "unassigned": len(items) - len(completions),
        "mean_wait_s": fmean(waits) if waits else None,
        "p95_wait_s": percentile(waits, 0.95),
        "mean_pickup_s": fmean(pickups) if pickups else None,
        "p95_pickup_s": percentile(pickups, 0.95),
        "completion_s": max(completions) if completions else None,
        "return_s": max((t.returns_at_s for t in service.trips.values()), default=None),
        "distance_m": sum(distances),
        "empty_distance_m": sum(empty),
        "empty_ratio": sum(empty) / sum(distances) if sum(distances) else None,
        "energy_wh": sum(sum(t.energy_by_leg_wh) for t in service.trips.values()),
        "charge_count": len(service.charges),
        "deadline_count": len(deadline_requests),
        "on_time_rate": on_time / len(deadline_requests) if deadline_requests else None,
        "assignment_cost_s": sum(
            a["cost_s"] for tick in service.dispatch_log for a in tick["assignments"]
        ),
    }
    timings = [tick["elapsed_s"] * 1000 for tick in service.dispatch_log]
    return metrics, assignments, timings, service


def compare_dispatch(command: DispatchComparison, current=()):
    values = workload(command.source, current)
    if not 1 <= len(values) <= 60:
        raise ValueError("비교 요청은 1~60건까지 지원합니다")
    values = sorted(values, key=lambda item: item["at_s"])
    # Pairs retain their original stable request receipt order at equal timestamps.
    items = list(enumerate(values))
    policy_results = {name: [] for name in ("fifo", "greedy", "hungarian")}
    first_metrics, first_assignments = {}, {}
    template = None
    for repeat in range(command.repeats):
        policies = list(policy_results)
        policies = policies[repeat % 3 :] + policies[: repeat % 3]
        for policy in policies:
            metrics, assignments, timings, template = run_once(items, policy, command.planner)
            if policy not in first_metrics:
                first_metrics[policy], first_assignments[policy] = metrics, assignments
            elif first_metrics[policy] != metrics or first_assignments[policy] != assignments:
                raise RuntimeError("non-deterministic comparison outcome")
            policy_results[policy].extend(timings)
    config = {
        "planner": command.planner,
        "map_version": template.graph.map_version,
        "cost_snapshot_id": template.costs.id,
        "map_files": template.map_metadata.get("files", {}),
        "battery": template.battery_policy.model_dump(),
        "charging_dispatch_policy": "interrupt_selected_when_mission_plus_reserve_feasible_v1",
        "vehicles": [v.model_dump() for v in template.vehicles.values()],
        "homes": template.home_nodes,
        "requests": values,
        "cost_policy": {
            "pickup_weight": 1,
            "delivery_weight": 0.5,
            "lateness_weight": 2,
            "energy": "full mission recharge seconds",
            "scarce_vehicle_penalty_s": 120,
            "aging_interval_s": 120,
            "deadline_window_s": 600,
            "deadline_weight": 2,
            "long_wait_s": 600,
            "long_wait_every_n_opportunities": 3,
        },
    }
    # Completed run availability is not an initial-condition input.
    for vehicle in config["vehicles"]:
        vehicle["available"] = True
    return {
        "source": command.source,
        "planner": command.planner,
        "repeats": command.repeats,
        "condition_hash": sha256(dumps(config, sort_keys=True).encode()).hexdigest(),
        "conditions": config,
        "request_count": len(values),
        "simulation_only": True,
        "results": [
            {
                "policy": p,
                **first_metrics[p],
                "assignments": first_assignments[p],
                "dispatch_p50_ms": percentile(policy_results[p], 0.5),
                "dispatch_p95_ms": percentile(policy_results[p], 0.95),
                "dispatch_samples": len(policy_results[p]),
            }
            for p in policy_results
        ],
    }
