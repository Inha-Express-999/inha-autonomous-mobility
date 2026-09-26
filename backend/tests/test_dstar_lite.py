import math
import random
from dataclasses import replace
from pathlib import Path

import pytest

from campus_sim.domain import ServiceType
from campus_sim.dstar_lite import DStarLite
from campus_sim.planning import NoRouteError, astar, dijkstra
from campus_sim.replan_evaluation import compare_replans
from campus_sim.road_graph import load_road_graph
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]


def graph():
    return load_road_graph(ROOT / "maps/fixtures/campus-synthetic-benchmark-11.json")


def assert_same(planner, source, start, goal, costs, closed, **constraints):
    try:
        reference = dijkstra(source, start, goal, edge_costs_s=costs,
                             excluded_edge_ids=closed, **constraints)
    except NoRouteError:
        with pytest.raises(NoRouteError):
            planner.replan(start, edge_costs_s=costs, excluded_edge_ids=closed)
        return
    actual = planner.replan(start, edge_costs_s=costs, excluded_edge_ids=closed)
    assert actual.path_cost_s == pytest.approx(reference.path_cost_s, abs=1e-8)
    assert actual.node_ids[0] == start and actual.node_ids[-1] == goal
    assert not closed.intersection(actual.edge_ids)
    assert astar(source, start, goal, edge_costs_s=costs, excluded_edge_ids=closed,
                 **constraints).path_cost_s == pytest.approx(actual.path_cost_s)


@pytest.mark.parametrize("constraints", [{}, {"requires_step_free": True},
                                         {"service_type": ServiceType.CARGO}, {"vehicle_width_m": 20}])
def test_all_pairs_match_dijkstra_and_astar_under_identical_hard_constraints(constraints):
    source = graph()
    for goal in source.nodes:
        planner = DStarLite(source, source.nodes[0].id, goal.id, **constraints)
        for start in source.nodes:
            assert_same(planner, source, start.id, goal.id, {}, frozenset(), **constraints)


@pytest.mark.parametrize("seed", [17, 46, 113])
def test_repeated_increases_decreases_closures_reopening_and_moving_start(seed):
    source, rng = graph(), random.Random(seed)
    goal = source.nodes[-1].id
    planner = DStarLite(source, source.nodes[0].id, goal)
    for _ in range(100):
        costs = {e.id: e.cost_s() + rng.uniform(0, 200)
                 for e in rng.sample(source.edges, min(5, len(source.edges)))}
        closed = frozenset(e.id for e in rng.sample(source.edges, rng.randrange(len(source.edges) + 1)))
        start = rng.choice(source.nodes).id
        assert_same(planner, source, start, goal, costs, closed)
        assert len(planner.heap) <= 2 * len(source.nodes)
    assert_same(planner, source, source.nodes[0].id, goal, {}, frozenset())


def test_repeated_unchanged_search_reuses_state_without_expansion():
    source = graph()
    planner = DStarLite(source, source.nodes[0].id, source.nodes[-1].id)
    first = planner.replan()
    second = planner.replan()
    assert second.edge_ids == first.edge_ids
    assert first.expanded_nodes > 0
    assert second.expanded_nodes == planner.updated_nodes == 0


@pytest.mark.parametrize("kwargs", [{"edge_costs_s": {"unknown": 100}},
                                    {"excluded_edge_ids": {"unknown"}},
                                    {"start_node": "unknown"}])
def test_invalid_updates_leave_previous_search_usable(kwargs):
    source = graph()
    planner = DStarLite(source, source.nodes[0].id, source.nodes[-1].id)
    baseline = planner.replan()
    with pytest.raises(ValueError):
        planner.replan(**kwargs)
    assert planner.replan().edge_ids == baseline.edge_ids


@pytest.mark.parametrize("cost", [-1, math.nan, math.inf, True])
def test_invalid_cost_cannot_bypass_common_contract(cost):
    source = graph()
    planner = DStarLite(source, source.nodes[0].id, source.nodes[-1].id)
    with pytest.raises(ValueError):
        planner.replan(edge_costs_s={source.edges[0].id: cost})


def test_parallel_directed_edges_keep_identity_and_reopen_independently():
    source = graph()
    edge = source.edges[0]
    duplicate = edge.model_copy(update={"id": "parallel-test-edge", "expected_wait_s": 10})
    source = source.model_copy(update={"edges": [*source.edges, duplicate]})
    planner = DStarLite(source, edge.from_node, edge.to_node)
    assert_same(planner, source, edge.from_node, edge.to_node, {}, frozenset({edge.id}))
    assert_same(planner, source, edge.from_node, edge.to_node, {}, frozenset())


def test_service_routes_keep_crowd_avoidance_and_closure_policy_with_dstar():
    baseline, incremental = MobilityService.synthetic_fixture(), MobilityService.synthetic_fixture()
    for service in (baseline, incremental):
        service.graph = service.graph.model_copy(update={"edges": [
            edge.model_copy(update={"zone_ids": ["synthetic-test-corridor"]}) if edge.id == "e25" else edge
            for edge in service.graph.edges]})
    incremental.planning_policy = replace(incremental.planning_policy, global_algorithm="dstar_lite")
    for time_s, closed in ((0, False), (7200, False), (7200, True), (7300, False), (0, False)):
        for service in (baseline, incremental):
            service.simulation_time_s = time_s
            service.set_explicit_zone_closure("synthetic-test-corridor", closed)
        a = baseline._plan_route("n1", "n4", ServiceType.PASSENGER, False)
        b = incremental._plan_route("n1", "n4", ServiceType.PASSENGER, False)
        assert a[0].path_cost_s == pytest.approx(b[0].path_cost_s)
        assert a[2] == b[2]
        assert b[0].algorithm == "dstar_lite"
    assert incremental.global_planners
    old = list(incremental.global_planners.values())
    incremental.graph = incremental.graph.model_copy(update={"map_version": "synthetic-replacement"})
    incremental._plan_route("n1", "n4", ServiceType.PASSENGER, False)
    assert all(planner not in old for planner in incremental.global_planners.values())


def test_comparison_preserves_unreachable_events_and_cost_oracle():
    report = compare_replans(graph(), repetitions=2, event_count=10)
    assert report["all_costs_match_dijkstra"]
    assert len(report["rows"]) == 20
    assert report["summary"]["astar"]["no_route_events"] >= 4
    assert report["summary"]["dstar_lite"]["no_route_events"] == report["summary"]["astar"]["no_route_events"]
