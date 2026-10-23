from dataclasses import replace
from math import inf, nan
from random import Random

import pytest

from campus_sim.planning import CostSnapshot, DijkstraPlanner, NoPath, RoadEdge, RoadGraph


def graph_fixture():
    nodes = {"A": (0.0, 0.0), "B": (10.0, 0.0), "C": (20.0, 0.0), "D": (0.0, 10.0)}
    edges = (
        RoadEdge("A", "B", "slow", ((0.0, 0.0), (10.0, 0.0)), 10, 1, 3),
        RoadEdge("A", "B", "fast", ((0.0, 0.0), (5.0, 1.0), (10.0, 0.0)), 12, 3, 3),
        RoadEdge("B", "C", "road", ((10.0, 0.0), (20.0, 0.0)), 10, 2, 3),
        RoadEdge("A", "C", "direct", ((0.0, 0.0), (20.0, 0.0)), 20, 1, 3),
    )
    return RoadGraph("test-v1", nodes, edges)


def test_shortest_parallel_edge_and_complete_polyline():
    graph = graph_fixture()
    result = DijkstraPlanner().search(graph, "A", "C", CostSnapshot.travel_time(graph))
    assert result.edge_ids == (("A", "B", "fast"), ("B", "C", "road"))
    assert result.polyline == ((0.0, 0.0), (5.0, 1.0), (10.0, 0.0), (20.0, 0.0))
    assert result.cost_s == 9
    assert result.length_m == 22
    assert result.stats.expanded_nodes == 3
    assert result.stats.generated_nodes >= 3
    assert result.stats.planning_time_s >= 0


def test_directed_unreachable_and_start_equals_goal():
    graph = graph_fixture()
    costs = CostSnapshot.travel_time(graph)
    assert isinstance(DijkstraPlanner().search(graph, "C", "A", costs), NoPath)
    assert isinstance(DijkstraPlanner().search(graph, "A", "D", costs), NoPath)
    same = DijkstraPlanner().search(graph, "B", "B", costs)
    assert same.edge_ids == () and same.cost_s == 0
    assert same.polyline == ((10.0, 0.0),)


@pytest.mark.parametrize(
    "changes",
    [
        {"vehicle_allowed": False},
        {"stairs": True},
        {"width_m": 1.0},
        {"width_m": None},
    ],
)
def test_hard_constraints_exclude_edges(changes):
    graph = graph_fixture()
    graph = replace(graph, edges=tuple(replace(edge, **changes) for edge in graph.edges))
    assert isinstance(
        DijkstraPlanner().search(
            graph,
            "A",
            "C",
            CostSnapshot.travel_time(graph),
            vehicle_width_m=2,
        ),
        NoPath,
    )


def test_closure_and_fixed_penalty_snapshot():
    graph = graph_fixture()
    costs = CostSnapshot.travel_time(graph)
    blocked = DijkstraPlanner().search(
        graph,
        "A",
        "C",
        costs,
        closed_edges=frozenset({("A", "B", "fast")}),
    )
    assert blocked.cost_s == 15
    values = dict(costs.costs_s)
    values["A", "B", "fast"] += 100
    penalized = DijkstraPlanner().search(
        graph, "A", "C", CostSnapshot("crowd", graph.map_version, values)
    )
    assert penalized.cost_s == 15
    assert penalized.cost_snapshot_id == "crowd"


def test_real_map_excludes_unknown_data_and_requires_vehicle_width():
    graph = replace(graph_fixture(), synthetic=False)
    with pytest.raises(ValueError, match="width"):
        DijkstraPlanner().search(graph, "A", "C", CostSnapshot.travel_time(graph))
    assert isinstance(
        DijkstraPlanner().search(
            graph,
            "A",
            "C",
            CostSnapshot.travel_time(graph),
            vehicle_width_m=2,
        ),
        NoPath,
    )
    graph = replace(graph, edges=tuple(replace(e, verified=True) for e in graph.edges))
    assert (
        DijkstraPlanner()
        .search(
            graph,
            "A",
            "C",
            CostSnapshot.travel_time(graph),
            vehicle_width_m=2,
        )
        .cost_s
        == 9
    )


@pytest.mark.parametrize("bad_cost", [-1, inf, nan])
def test_invalid_cost_rejected(bad_cost):
    with pytest.raises(ValueError):
        CostSnapshot("bad", "test", {("A", "B", "key"): bad_cost})


def test_invalid_graph_and_snapshot_rejected():
    graph = graph_fixture()
    with pytest.raises(ValueError, match="duplicate"):
        replace(graph, edges=graph.edges + graph.edges[:1])
    with pytest.raises(ValueError, match="endpoints"):
        replace(graph, nodes={**graph.nodes, "A": (1.0, 0.0)})
    for snapshot in (
        CostSnapshot("wrong-map", "other", {}),
        CostSnapshot("missing", graph.map_version, {}),
    ):
        with pytest.raises(ValueError):
            DijkstraPlanner().search(graph, "A", "C", snapshot)
    with pytest.raises(ValueError, match="unknown"):
        DijkstraPlanner().search(graph, "missing", "C", CostSnapshot.travel_time(graph))


def test_stale_heap_entries_and_zero_cost_cycle():
    graph = graph_fixture()
    graph = replace(
        graph, edges=graph.edges + (RoadEdge("B", "A", "back", ((10.0, 0.0), (0.0, 0.0)), 10, 2),)
    )
    costs = CostSnapshot("zero", graph.map_version, {edge.id: 0 for edge in graph.edges})
    result = DijkstraPlanner().search(graph, "A", "C", costs)
    assert result.cost_s == 0
    # D is unreachable, so the entire heap (including stale entries) is drained.
    result = DijkstraPlanner().search(graph, "A", "D", CostSnapshot.travel_time(graph))
    assert isinstance(result, NoPath)
    assert result.stats.expanded_nodes == 3


def test_100_pairs_against_independent_floyd_warshall_reference():
    rng = Random(19)
    nodes = {str(i): (float(i), 0.0) for i in range(10)}
    edges = []
    expected = [[0.0 if i == j else inf for j in range(10)] for i in range(10)]
    for i in range(10):
        for j in range(10):
            if i != j and rng.random() < 0.25:
                length = abs(i - j)
                speed = rng.uniform(1, 4)
                edges.append(
                    RoadEdge(str(i), str(j), "road", (nodes[str(i)], nodes[str(j)]), length, speed)
                )
                expected[i][j] = length / speed
    for k in range(10):
        for i in range(10):
            for j in range(10):
                expected[i][j] = min(expected[i][j], expected[i][k] + expected[k][j])
    graph = RoadGraph("random-synthetic", nodes, tuple(edges))
    costs = CostSnapshot.travel_time(graph)
    for i in range(10):
        for j in range(10):
            result = DijkstraPlanner().search(graph, str(i), str(j), costs)
            if expected[i][j] == inf:
                assert isinstance(result, NoPath)
            else:
                assert result.cost_s == pytest.approx(expected[i][j])
