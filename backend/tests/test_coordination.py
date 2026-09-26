import itertools
import json
import math
from pathlib import Path

import pytest

from campus_sim.coordination import (
    AgentTask,
    Claim,
    CoordinationProblem,
    SearchLimits,
    TimedMove,
    conflicts,
    solve_coordination,
)
from campus_sim.road_graph import RoadGraphDocument

ROOT = Path(__file__).resolve().parents[2]


def graph(positions=None, links=None):
    positions = positions or {"a": (0, 0), "b": (1, 0), "c": (1, 1), "d": (0, 1)}
    links = links or [("a", "b"), ("b", "c"), ("c", "d"), ("d", "a")]
    doc = json.loads((ROOT / "maps/fixtures/physics-integration-3.json").read_text())
    doc["nodes"] = [{"id": n, "landmark_id": n, "stop_id": n, "position_m": {"x": xy[0], "y": xy[1]}}
                    for n, xy in positions.items()]
    proto = doc["edges"][0]
    doc["edges"] = [{**proto, "id": u + v, "from_node": u, "to_node": v,
                     "geometry_m": [{"x": positions[n][0], "y": positions[n][1]} for n in (u, v)],
                     "length_m": math.dist(positions[u], positions[v]), "allowed_speed_mps": 1}
                    for a, b in links for u, v in ((a, b), (b, a))]
    return RoadGraphDocument.model_validate(doc)


def problem(tasks, **kwargs):
    return CoordinationProblem(graph(), tasks, quantum_s=1, horizon=6, provenance="synthetic test", **kwargs)


def enumerated_optimum(source, tasks, horizon):
    """Independent exhaustive unit-edge oracle, including permanent goal occupancy.

    Enumerate paths that may pass their goal before choosing a final arrival.
    Collision checking uses node lists and directed transitions, not planner claims.
    """
    adjacency = {node.id: [] for node in source.nodes}
    for edge in source.edges:
        adjacency[edge.from_node].append(edge.to_node)

    def candidates(task):
        found = []

        def walk(path):
            if path[-1] == task.goal:
                found.append((len(path) - 1, tuple(path + [task.goal] * (horizon + 1 - len(path)))))
            if len(path) > horizon:
                return
            for node in [path[-1], *adjacency[path[-1]]]:
                walk([*path, node])

        walk([task.start])
        return sorted(found)

    best = math.inf
    for (cost_a, a), (cost_b, b) in itertools.product(*(candidates(task) for task in tasks)):
        if cost_a + cost_b >= best or any(x == y for x, y in zip(a, b, strict=True)):
            continue
        if any(a[t] == b[t + 1] and b[t] == a[t + 1] for t in range(horizon)):
            continue
        best = cost_a + cost_b
    return best


@pytest.mark.parametrize("starts,goals", [(("a", "b"), ("c", "d")), (("a", "b"), ("b", "a")),
    (("a", "c"), ("c", "a")), (("a", "d"), ("c", "b")), (("a", "b"), ("a", "c"))])
def test_cbs_cost_matches_independent_exhaustive_oracle(starts, goals):
    tasks = [AgentTask(str(i), s, g) for i, (s, g) in enumerate(zip(starts, goals, strict=True))]
    source = graph()
    p = CoordinationProblem(source, tasks, quantum_s=1, horizon=4, provenance="unit-grid oracle")
    result = solve_coordination(p)
    optimum = enumerated_optimum(source, tasks, 4)
    assert result.status == "SUCCESS"
    assert sum(path.arrival for path in result.paths) == optimum
    assert not conflicts(result.paths)
    assert not result.executable


def test_head_on_line_is_infeasible_without_deleting_goal_occupants():
    p = CoordinationProblem(graph({"a": (0, 0), "b": (1, 0)}, [("a", "b")]),
        [AgentTask("V1", "a", "b"), AgentTask("V2", "b", "a")], quantum_s=1, horizon=3, provenance="no bay")
    result = solve_coordination(p)
    assert result.status == "NO_SOLUTION_WITHIN_HORIZON"
    assert not result.paths


def test_three_vehicle_plan_includes_all_agents_and_is_deterministic():
    p = problem([AgentTask("V1", "a", "b"), AgentTask("V2", "b", "c"), AgentTask("V3", "c", "d")])
    first = solve_coordination(p)
    second = solve_coordination(p)
    assert first.status == "SUCCESS" and first.paths == second.paths
    assert first.ct_expanded == second.ct_expanded
    assert not conflicts(first.paths)


def test_shared_intersection_resources_force_delay_even_on_disjoint_edges():
    tasks = [AgentTask("V1", "a", "b"), AgentTask("V2", "d", "c")]
    p = problem(tasks, edge_resources={"ab": ["junction"], "dc": ["junction"]}, clearance_ticks=1)
    result = solve_coordination(p)
    assert result.status == "SUCCESS"
    assert sum(path.arrival for path in result.paths) > 2
    assert not conflicts(result.paths)
    initial_a = p.move_claims(TimedMove("a", "b", 0, 1, "ab"))
    initial_b = p.move_claims(TimedMove("d", "c", 1, 2, "dc"))
    assert Claim(1, "RESOURCE", "junction") in initial_a & initial_b  # Post-travel safety hold.


def test_long_edge_occupies_every_transit_slot_and_opposing_directions_share_key():
    p = problem([AgentTask("V1", "a", "b")])
    outbound = p.move_claims(TimedMove("a", "b", 0, 3, "ab"))
    inbound = p.move_claims(TimedMove("b", "a", 2, 5, "ba"))
    assert any(c.kind == "EDGE" and c.tick == 2 for c in outbound & inbound)
    assert Claim(3, "VERTEX", "b") in outbound


@pytest.mark.parametrize("limits,reason", [(SearchLimits(ct_nodes=1), "constraint_tree"),
    (SearchLimits(low_level_expansions=1), "low_level")])
def test_budget_exhaustion_returns_no_executable_or_partial_plan(limits, reason):
    p = problem([AgentTask("V1", "a", "b"), AgentTask("V2", "b", "a")])
    result = solve_coordination(p, limits=limits)
    assert result.status == "BUDGET_EXCEEDED" and result.reason == reason
    assert not result.paths and not result.executable


def test_priority_uses_same_resource_and_goal_occupancy_rules():
    p = problem([AgentTask("V1", "a", "c"), AgentTask("V2", "b", "d")])
    result = solve_coordination(p, algorithm="priority", priority_order=["V2", "V1"])
    assert result.status == "SUCCESS" and not conflicts(result.paths)
    assert result.paths[0].vehicle_id == "V2"
    with pytest.raises(ValueError):
        solve_coordination(p, algorithm="priority", priority_order=["V1", "V1"])


def test_hard_constraints_remain_in_low_level_and_source_mutation_cannot_change_snapshot():
    source = graph()
    p = CoordinationProblem(source, [AgentTask("V1", "a", "c", vehicle_width_m=20)],
                            quantum_s=1, horizon=6, provenance="width test")
    assert solve_coordination(p).status == "NO_SOLUTION_WITHIN_HORIZON"
    p = CoordinationProblem(source, [AgentTask("V1", "a", "c")], quantum_s=1, horizon=6, provenance="copy")
    before = solve_coordination(p).paths
    source.edges.clear()
    assert solve_coordination(p).paths == before


def test_invalid_overlay_duplicate_tasks_and_horizon_rejected():
    tasks = [AgentTask("V1", "a", "c")]
    with pytest.raises(ValueError):
        problem(tasks, edge_resources={"missing": ["corridor"]})
    with pytest.raises(ValueError):
        problem(tasks * 2)
    with pytest.raises(ValueError):
        CoordinationProblem(graph(), tasks, quantum_s=1, horizon=0, provenance="invalid")


def test_goal_resources_remain_occupied_and_joint_same_goal_is_not_success():
    p = problem([AgentTask("V1", "a", "a"), AgentTask("V2", "c", "a")], node_resources={"a": ["bay"]})
    result = solve_coordination(p)
    assert result.status == "NO_SOLUTION_WITHIN_HORIZON"
