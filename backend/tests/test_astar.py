from math import dist
from random import Random

import pytest

from campus_sim.planning import (
    AStarPlanner,
    CostSnapshot,
    DijkstraPlanner,
    NoPath,
    RoadEdge,
    RoadGraph,
)
from campus_sim.web_demo import DemoControl, DemoPlan, DemoSession


@pytest.mark.parametrize("seed", range(10))
def test_astar_matches_independent_floyd_warshall_with_arbitrary_costs(seed):
    rng = Random(seed)
    nodes = {str(i): (rng.uniform(-20, 20), rng.uniform(-20, 20)) for i in range(15)}
    edges, costs = [], {}
    for a, point_a in nodes.items():
        for b, point_b in nodes.items():
            if a != b and rng.random() < 0.25:
                for k in range(2):
                    edge = RoadEdge(a, b, str(k), (point_a, point_b), dist(point_a, point_b), 5)
                    edges.append(edge)
                    costs[edge.id] = 0 if seed == 0 else rng.uniform(0.1, 20)
    graph = RoadGraph("random", nodes, tuple(edges))
    snapshot = CostSnapshot("random-cost", "random", costs)
    reference = {(a, b): 0 if a == b else float("inf") for a in nodes for b in nodes}
    for edge in edges:
        reference[edge.u, edge.v] = min(reference[edge.u, edge.v], costs[edge.id])
    for k in nodes:
        for a in nodes:
            for b in nodes:
                reference[a, b] = min(reference[a, b], reference[a, k] + reference[k, b])
    for a, point_a in nodes.items():
        for b, point_b in nodes.items():
            route = AStarPlanner().search(graph, a, b, snapshot)
            if reference[a, b] == float("inf"):
                assert isinstance(route, NoPath)
            else:
                assert route.cost_s == pytest.approx(reference[a, b])
                assert route.polyline[0] == point_a and route.polyline[-1] == point_b


def test_shared_constraints_and_real_map_width_are_preserved():
    nodes = {"a": (0, 0), "b": (1, 0)}
    for kwargs in ({"stairs": True}, {"vehicle_allowed": False}, {"width_m": 0.5}):
        edge = RoadEdge("a", "b", "road", tuple(nodes.values()), 1, 1, **kwargs)
        graph = RoadGraph("test", nodes, (edge,))
        for planner in (AStarPlanner(), DijkstraPlanner()):
            assert isinstance(
                planner.search(graph, "a", "b", CostSnapshot.travel_time(graph), vehicle_width_m=1),
                NoPath,
            )
    graph = RoadGraph("real", nodes, (), synthetic=False)
    with pytest.raises(ValueError, match="vehicle width"):
        AStarPlanner().search(graph, "a", "b", CostSnapshot.travel_time(graph))


def test_campus_all_stop_pairs_equal_cost_and_search_trace():
    session = DemoSession()
    service = session.service
    nodes = tuple(service.stop_nodes.values())
    for start in nodes:
        for goal in nodes:
            a = AStarPlanner().search(service.graph, start, goal, service.costs)
            d = DijkstraPlanner().search(service.graph, start, goal, service.costs)
            assert a.cost_s == pytest.approx(d.cost_s)
            assert a.stats.expanded_nodes <= d.stats.expanded_nodes
            assert a.stats.expanded_node_ids[-1] == goal
    assert len(session.map["nodes"]) == len(service.graph.nodes)


def test_web_planner_switch_retains_active_trips_and_fifo():
    session = DemoSession()
    assert session.state()["planner"] == "astar"
    session.scenario("extensions")
    trips = tuple(session.service.trips.values())
    fifo = dict(session.service.vehicle_order)
    session.control(DemoControl(planner="dijkstra", paused=True))
    assert tuple(session.service.trips.values()) == trips
    assert session.service.vehicle_order == fifo
    comparison = session.compare_planning(DemoPlan(pickup="main_gate", dropoff="hitech"))
    assert comparison["results"]["astar"]["cost_s"] == pytest.approx(
        comparison["results"]["dijkstra"]["cost_s"]
    )
    session.reset()
    assert session.state()["planner"] == "dijkstra"
