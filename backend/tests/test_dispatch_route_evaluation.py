import json
from copy import deepcopy
from pathlib import Path

import pytest

from campus_sim.dispatch_route_evaluation import (
    build_dispatch_case,
    compare_route_dispatch,
    route_dispatch_snapshot,
)
from campus_sim.domain import RequestStatus

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "configs/dispatch_route_benchmark.json"
MAP = ROOT / "maps/fixtures/campus-synthetic-6.json"


def cases():
    return json.loads(CONFIG.read_text(encoding="utf-8"))["cases"]


def test_production_route_cost_cells_have_inspectable_geometry_and_cost_components():
    service = build_dispatch_case(MAP, cases()[0])
    cells, _ = route_dispatch_snapshot(service)
    lookup = {(c["request"], c["vehicle"]): c for c in cells}
    # R1: V01 to n2 is 100m at 5m/s; n2->n5->n6 is 270m/5.
    first = lookup[("R1", "V01")]
    assert first["pickup_route"]["edges"] == ["e12"]
    assert first["pickup_s"] == 20 and first["trip_s"] == 54
    assert first["cost_s"] == 47 and first["predicted_completion_s"] == 698
    assert lookup[("R1", "V03")]["cost_s"] is None
    assert lookup[("R1", "V03")]["reason"] == "CAPACITY_OR_SERVICE_NEEDS"
    assert all(r.status == RequestStatus.QUEUED for r in service.requests.values())
    assert all(v.available for v in service.vehicles.values())
    edges = {e.id: e for e in service.graph.edges}
    for cell in cells:
        if not cell["feasible"]:
            continue
        request = service.requests[cell["request"]]
        for name in ("pickup_route", "trip_route"):
            route = cell[name]
            assert len(route["nodes"]) == len(route["edges"]) + 1
            for index, edge_id in enumerate(route["edges"]):
                edge = edges[edge_id]
                assert (edge.from_node, edge.to_node) == tuple(route["nodes"][index:index + 2])
                assert edge.is_open and request.service_type in edge.allowed_service_types
            assert route["distance_m"] == sum(edges[e].length_m for e in route["edges"])


def test_accessibility_deadline_and_post_queue_closure_are_hard_evidence():
    service = build_dispatch_case(MAP, cases()[1])
    cells, _ = route_dispatch_snapshot(service)
    constrained = [c for c in cells if c["request"] == "R2"]
    assert [c["vehicle"] for c in constrained if c["feasible"]] == ["V01"]
    cell = next(c for c in constrained if c["feasible"])
    assert cell["lateness_s"] > 0
    assert cell["cost_s"] == cell["pickup_s"] + .5 * cell["trip_s"] + 2 * cell["lateness_s"]
    used = cell["pickup_route"]["edges"] + cell["trip_route"]["edges"]
    assert not {"e25", "e52"}.intersection(used)
    closed = build_dispatch_case(MAP, cases()[3])
    rows, _ = route_dispatch_snapshot(closed)
    assert all(not c["feasible"] for c in rows if c["request"] == "R2")
    assert any(c.get("reason") == "ROUTE_UNAVAILABLE" for c in rows)


def test_production_algorithms_share_scenario_and_partial_cost_comparisons_are_flagged():
    report = compare_route_dispatch(CONFIG, repetitions=2)
    first = report["cases"][0]
    assert first["same_served_requests"] and first["cost_reduction_s_same_requests"] == 40
    assert [s["total_cost_s"] for s in first["summary"]] == [245, 205]
    assert all(s["deterministic_assignments"] for case in report["cases"] for s in case["summary"])
    saturated = next(c for c in report["cases"] if c["id"] == "more_requests_than_vehicles")
    assert not saturated["same_served_requests"] and saturated["cost_reduction_s_same_requests"] is None
    for case in report["cases"]:
        cells = {(c["request"], c["vehicle"]): c for c in case["cells"]}
        for row in case["runs"]:
            assigned = row["assignments"]
            assert len({r for r, _ in assigned}) == len(assigned)
            assert len({v for _, v in assigned}) == len(assigned)
            assert all(cells[pair]["feasible"] for pair in assigned)
            assert row["actual_completion_s"] is None and row["actual_wait_s"] is None
            assert not set(row["unassigned"]).intersection(r for r, _ in assigned)


def test_fairness_due_assigns_aged_cargo_and_reports_preexisting_wait():
    report = compare_route_dispatch(CONFIG, repetitions=1)
    case = next(c for c in report["cases"] if c["id"] == "aged_cargo_fairness")
    for run in case["runs"]:
        assert ("R3", "V03") in run["assignments"]
        assert run["p95_preassignment_wait_s"] == 620
        assert run["assigned_by_service"] == {"PASSENGER": 2, "CARGO": 1}


@pytest.mark.parametrize("failure", ["future_request", "nan_clock", "duplicate_id", "unknown_closure", "missing_vehicle"])
def test_invalid_scenarios_are_rejected(failure):
    case = deepcopy(cases()[0])
    if failure == "future_request":
        case["requests"][0]["created_s"] = 621
    elif failure == "nan_clock":
        case["now_s"] = float("nan")
    elif failure == "duplicate_id":
        case["requests"][1]["id"] = "R1"
    elif failure == "unknown_closure":
        case["closed_edge_ids"] = ["missing"]
    else:
        case["vehicle_nodes"].pop("V03")
    with pytest.raises(ValueError):
        build_dispatch_case(MAP, case)
