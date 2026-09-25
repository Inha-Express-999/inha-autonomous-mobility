from __future__ import annotations

import math
from pathlib import Path

import pytest

from campus_sim.planning import astar, dijkstra
from campus_sim.road_graph import load_road_graph

ROOT = Path(__file__).resolve().parents[2]


def test_cost_snapshot_changes_route_without_mutating_graph_and_keeps_astar_optimal() -> None:
    graph = load_road_graph(ROOT / "maps/fixtures/campus-synthetic-6.json")
    original_costs = {edge.id: edge.cost_s() for edge in graph.edges}

    baseline = astar(graph, "n1", "n4")
    congestion_snapshot = {"e25": 1_024.0}
    dijkstra_result = dijkstra(graph, "n1", "n4", edge_costs_s=congestion_snapshot)
    astar_result = astar(graph, "n1", "n4", edge_costs_s=congestion_snapshot)

    assert "e25" in baseline.edge_ids
    assert "e25" not in astar_result.edge_ids
    assert astar_result.edge_ids == dijkstra_result.edge_ids
    assert astar_result.path_cost_s == pytest.approx(dijkstra_result.path_cost_s)
    assert astar_result.path_cost_s == pytest.approx(150.0)
    assert {edge.id: edge.cost_s() for edge in graph.edges} == original_costs


@pytest.mark.parametrize(
    ("costs", "message"),
    [
        ({"missing-edge": 1.0}, "unknown edges"),
        ({"e12": -1.0}, "at least free-flow"),
        ({"e12": math.inf}, "finite and at least free-flow"),
        ({"e12": True}, "finite and at least free-flow"),
        ({"e12": 1.0}, "at least free-flow"),
    ],
)
def test_cost_snapshot_rejects_unknown_or_invalid_edge_costs(
    costs: dict[str, float], message: str
) -> None:
    graph = load_road_graph(ROOT / "maps/fixtures/campus-synthetic-6.json")

    with pytest.raises(ValueError, match=message):
        astar(graph, "n1", "n4", edge_costs_s=costs)
