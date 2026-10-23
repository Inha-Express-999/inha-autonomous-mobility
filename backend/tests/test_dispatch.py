from itertools import permutations
from random import Random

import pytest

from campus_sim.dispatch import (
    Assignment,
    FifoDispatch,
    GreedyDispatch,
    HungarianDispatch,
    hungarian,
)
from campus_sim.domain import RequestStatus, RequestView, ServiceNeeds, ServiceType, Vehicle


def request(identifier, created=0.0, **updates):
    return RequestView(
        id=identifier,
        owner_id="test",
        service_type="PASSENGER",
        status="QUEUED",
        created_s=created,
        pickup_landmark_id="A",
        dropoff_landmark_id="B",
        service_needs=ServiceNeeds(),
        party_size=1,
        cargo_kg=0,
    ).model_copy(update=updates)


def vehicle(identifier, **updates):
    return Vehicle(
        id=identifier, passenger_capacity=4, wheelchair_slots=0, cargo_capacity_kg=0
    ).model_copy(update=updates)


def test_fifo_minimum_case_and_input_order_independence():
    requests = [request("R02"), request("R01")]
    vehicles = [vehicle("V02"), vehicle("V01")]
    assert FifoDispatch().assign(requests, vehicles) == (
        Assignment("R01", "V01"),
        Assignment("R02", "V02"),
    )
    assert all(v.available for v in vehicles)  # Planning is pure.
    assert all(r.status is RequestStatus.QUEUED for r in requests)


def test_creation_time_precedes_id_and_skips_infeasible_head():
    requests = [request("early", -1, party_size=10), request("Z", 1), request("A", 2)]
    assert FifoDispatch().assign(requests, [vehicle("V")]) == (Assignment("Z", "V"),)


def test_capacity_and_external_hard_constraints_are_both_required():
    requests = [
        request(
            "wheelchair",
            service_needs=ServiceNeeds(
                requires_step_free=True,
                wheelchair_slots=1,
            ),
        )
    ]
    assert not FifoDispatch().assign(requests, [vehicle("V")], lambda v, r: True)
    assert not FifoDispatch().assign(
        requests,
        [vehicle("V", wheelchair_slots=1)],
        lambda v, r: False,
    )


def test_busy_occupied_and_cancelled_entries_do_not_get_assigned():
    requests = [
        request("active", status=RequestStatus.IN_TRANSIT, vehicle_id="V01"),
        request("cancelled", status=RequestStatus.CANCELLED),
        request("queued"),
    ]
    assert not FifoDispatch().assign(requests, [vehicle("V01"), vehicle("V02", available=False)])


def test_cargo_only_uses_sufficient_cargo_capacity():
    cargo = request("cargo", service_type=ServiceType.CARGO, party_size=0, cargo_kg=10)
    assert FifoDispatch().assign(
        [cargo], [vehicle("V01"), vehicle("V02", cargo_capacity_kg=10)]
    ) == (Assignment("cargo", "V02"),)


@pytest.mark.parametrize("duplicate_requests", [True, False])
def test_duplicate_ids_rejected(duplicate_requests):
    with pytest.raises(ValueError, match="duplicate"):
        FifoDispatch().assign(
            [request("R")] * (2 if duplicate_requests else 1),
            [vehicle("V")] * (1 if duplicate_requests else 2),
        )


@pytest.mark.parametrize("seed", range(25))
def test_hungarian_matches_brute_force_rectangular_forbidden_edges(seed):
    rng = Random(seed)
    n, m = 1 + seed % 3, 3 + seed % 3
    costs = [
        [rng.randint(0, 100) if i == j or rng.random() > 0.35 else None for j in range(m)]
        for i in range(n)
    ]
    expected = min(
        sum(costs[i][j] for i, j in enumerate(columns))
        for columns in permutations(range(m), n)
        if all(costs[i][j] is not None for i, j in enumerate(columns))
    )
    actual = hungarian(costs)
    assert len(set(actual)) == n
    assert sum(costs[i][j] for i, j in enumerate(actual)) == expected


def test_hungarian_improves_matching_for_same_prioritized_batch():
    requests, vehicles = [request("R1"), request("R2")], [vehicle("V1"), vehicle("V2")]
    costs = {("R1", "V1"): 1, ("R1", "V2"): 2, ("R2", "V1"): 2, ("R2", "V2"): 100}

    def solve(policy):
        return policy.assign(
            requests, vehicles, lambda v, r: True, cost=lambda v, r: costs[r.id, v.id]
        )

    greedy, hung = solve(GreedyDispatch()), solve(HungarianDispatch())
    assert sum(costs[a.request_id, a.vehicle_id] for a in greedy) == 101
    assert sum(costs[a.request_id, a.vehicle_id] for a in hung) == 4


def test_priority_frontier_keeps_high_priority_and_fills_possible_slots():
    requests = [request("R1"), request("R2"), request("R3")]
    vehicles = [vehicle("V1"), vehicle("V2")]
    possible = {("R1", "V1"), ("R2", "V1"), ("R3", "V2")}
    result = HungarianDispatch().assign(
        requests, vehicles, lambda v, r: (r.id, v.id) in possible, cost=lambda v, r: 1
    )
    assert result == (Assignment("R1", "V1"), Assignment("R3", "V2"))
    assert HungarianDispatch().assign(requests, [], lambda v, r: True, cost=lambda v, r: 1) == ()


@pytest.mark.parametrize("policy", [GreedyDispatch, HungarianDispatch])
def test_new_policies_enforce_capacity_occupancy_and_hard_constraints(policy):
    requests = [
        request("active", status=RequestStatus.IN_TRANSIT, vehicle_id="V1"),
        request("large", party_size=10),
        request("normal"),
    ]
    assert not policy().assign(
        requests, [vehicle("V1"), vehicle("V2")], lambda v, r: False, cost=lambda v, r: 0
    )
    result = policy().assign(
        requests, [vehicle("V1"), vehicle("V2")], lambda v, r: True, cost=lambda v, r: 1
    )
    assert result == (Assignment("normal", "V2"),)
    assert all(v.available for v in [vehicle("V1"), vehicle("V2")])
    with pytest.raises(ValueError, match="duplicate"):
        policy().assign([request("a"), request("a")], [], lambda v, r: True, cost=lambda v, r: 1)


@pytest.mark.parametrize(
    "costs", [[[None]], [[1], [2]], [[float("nan")]], [[float("inf")]], [[1, -1]]]
)
def test_invalid_or_unmatchable_hungarian_matrix_is_not_relaxed(costs):
    with pytest.raises(ValueError):
        hungarian(costs)


@pytest.mark.parametrize("policy", [GreedyDispatch, HungarianDispatch])
def test_third_feasible_opportunity_protects_long_waiting_request(policy):
    requests = [request("new", created=700), request("old", created=0)]
    result = policy().assign(
        requests,
        [vehicle("V1")],
        lambda v, r: True,
        cost=lambda v, r: 1,
        priority=lambda r: 0 if r.id == "new" else 1,
        now_s=700,
        opportunity_count=2,
    )
    assert result == (Assignment("old", "V1"),)
    # An incompatible old request cannot take an opportunity from a capable pair.
    result = policy().assign(
        requests,
        [vehicle("V1")],
        lambda v, r: r.id == "new",
        cost=lambda v, r: 1,
        now_s=700,
        opportunity_count=2,
    )
    assert result == (Assignment("new", "V1"),)
