from __future__ import annotations

import json
from itertools import permutations
from pathlib import Path

import pytest

from campus_sim.evaluation import (
    all_ordered_pairs,
    compare_dispatch_assignments,
    compare_routes,
)
from campus_sim.road_graph import load_road_graph

ROOT = Path(__file__).resolve().parents[2]


def test_t03_fixture_has_at_least_100_distinct_ordered_pairs_and_valid_geometry() -> None:
    graph = load_road_graph(ROOT / "maps/fixtures/campus-synthetic-benchmark-11.json")
    pairs = all_ordered_pairs(sorted(node.id for node in graph.nodes))[:100]

    assert len(pairs) == 100
    assert len(set(pairs)) == 100
    assert set(pairs).issubset(set(permutations([node.id for node in graph.nodes], 2)))
    assert len(graph.nodes) == 11
    assert len(graph.edges) == 34
    assert sum(len(edge.geometry_m) for edge in graph.edges) > 2 * len(graph.edges)


def test_route_comparison_records_repeated_costs_and_timing_samples() -> None:
    graph = load_road_graph(ROOT / "maps/fixtures/campus-synthetic-benchmark-11.json")
    pairs = all_ordered_pairs(sorted(node.id for node in graph.nodes))[:100]

    report = compare_routes(graph, pairs, warmups=1, repetitions=3)

    assert report["summary"]["path_cost_match_count"] == 100
    assert report["input_validation"]["topology_and_geometry_validated"] is True
    assert report["input_validation"]["edge_endpoint_checks"] == 34
    assert report["measurement"]["path_outcomes_match_all_repetitions"] is True
    assert report["measurement"]["matching_repetitions"] == 3
    for algorithm in ("dijkstra", "astar"):
        timing = report["measurement"]["duration_ns"][algorithm]
        assert timing["sample_count"] == 300
        assert timing["min"] <= timing["p50"] <= timing["p95"] <= timing["max"]


def test_dispatch_comparison_uses_same_hard_constraints_and_reports_cost_delta() -> None:
    scenario = json.loads((ROOT / "configs/dispatch_benchmark.json").read_text())

    report = compare_dispatch_assignments(scenario, repetitions=5, warmups=1)

    assert report["data_status"] == "SYNTHETIC_COST_MATRIX"
    assert report["comparison"]["energy_model_included"] is False
    assert report["input_validation"]["hard_constraint_cells"] == 3
    assert report["greedy"]["matched_count"] == 3
    assert report["hungarian"]["matched_count"] == 3
    assert report["greedy"]["total_cost_s"] == 104.0
    assert report["hungarian"]["total_cost_s"] == 6.1
    assert report["measurement"]["deterministic_assignments"] is True
    for algorithm in ("greedy", "hungarian"):
        timing = report["measurement"]["duration_ns"][algorithm]
        assert timing["sample_count"] == 5
        assert timing["min"] <= timing["p50"] <= timing["p95"] <= timing["max"]
        assert len(report["measurement"]["duration_samples_ns"][algorithm]) == 5


@pytest.mark.parametrize(
    ("scenario", "message"),
    [
        ({"schema_version": 2}, "schema_version"),
        ({"schema_version": 1, "scenario_id": "x", "data_status": "SYNTHETIC_COST_MATRIX", "vehicles": ["V1"], "requests": []}, "non-empty"),
    ],
)
def test_dispatch_comparison_rejects_invalid_scenario_contract(
    scenario: dict, message: str
) -> None:
    with pytest.raises(ValueError, match=message):
        compare_dispatch_assignments(scenario)


@pytest.mark.parametrize(
    ("pairs", "repetitions", "warmups", "message"),
    [([], 1, 0, "at least one"), ([("a", "b")], 0, 0, "at least 1"), ([("a", "b")], 1, -1, "negative")],
)
def test_route_comparison_rejects_invalid_measurement_settings(
    pairs: list[tuple[str, str]], repetitions: int, warmups: int, message: str
) -> None:
    graph = load_road_graph(ROOT / "maps/fixtures/campus-synthetic-6.json")

    with pytest.raises(ValueError, match=message):
        compare_routes(graph, pairs, repetitions=repetitions, warmups=warmups)
