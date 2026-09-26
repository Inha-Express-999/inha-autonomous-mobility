import math

import pytest

from campus_sim.reservations import ResourceReservations


def manager():
    return ResourceReservations({"corridor", "exit", "junction", "bay"}, map_version="synthetic-test")


def occupy(book, grant, resources, at=0):
    book.report_occupancy(grant.token, grant.request.vehicle_id, resources,
                          now_s=at, map_version="synthetic-test")


def test_corridor_and_exit_are_atomic_and_expiry_never_releases_occupant():
    book = manager()
    book.enqueue("r1", "V1", {"corridor", "exit"}, now_s=0, lease_s=1)
    first, = book.advance(0)
    occupy(book, first, {"corridor"})
    book.enqueue("r2", "V2", {"corridor", "exit"}, now_s=0, priority=100)
    assert book.advance(5) == ()
    assert book.claims["corridor"] == first.token
    occupy(book, first, {"exit"}, at=5)
    assert "corridor" not in book.claims
    assert book.advance(5) == ()  # Exit space still occupied.
    occupy(book, first, set(), at=6)
    second, = book.advance(6)
    assert second.request.vehicle_id == "V2"
    assert set(book.claims) == {"corridor", "exit"}


def test_future_exit_reservation_survives_gap_after_entering_corridor():
    book = manager()
    book.enqueue("r1", "V1", {"corridor", "exit"}, now_s=0, lease_s=1)
    first, = book.advance(0)
    occupy(book, first, {"corridor"})
    occupy(book, first, set(), at=0.5)
    book.advance(10)
    assert book.claims["exit"] == first.token
    book.cancel(first.token, "V1", now_s=10)
    assert not book.claims


def test_unentered_lease_expires_without_retaining_claims():
    book = manager()
    book.enqueue("r1", "V1", {"corridor"}, now_s=0, lease_s=1)
    first, = book.advance(0)
    book.enqueue("r2", "V2", {"corridor"}, now_s=0)
    second, = book.advance(1)
    assert second.request.vehicle_id == "V2"
    assert first.token not in book.leases
    assert "LEASE_EXPIRED" in [e.kind for e in book.events]


def test_no_partial_corridor_grant_when_exit_is_reserved():
    book = manager()
    book.enqueue("bay", "V1", {"exit"}, now_s=0)
    book.advance(0)
    book.enqueue("move", "V2", {"corridor", "exit"}, now_s=0)
    assert not book.advance(0)
    assert "corridor" not in book.claims


def test_priority_yield_difficulty_and_deterministic_vehicle_tie_break():
    book = manager()
    book.enqueue("z", "V3", {"junction"}, now_s=0, priority=100)
    book.enqueue("a", "V2", {"junction"}, now_s=0, difficult_to_yield=True)
    book.enqueue("b", "V1", {"junction"}, now_s=0, difficult_to_yield=True)
    grant, = book.advance(0)
    assert grant.request.vehicle_id == "V1"


def test_wait_aging_can_outweigh_a_newer_higher_priority():
    book = manager()
    book.close("junction", now_s=0)
    book.enqueue("old", "V1", {"junction"}, now_s=0, wait_timeout_s=1000)
    book.enqueue("new", "V2", {"junction"}, now_s=90, priority=2)
    book.reopen("junction", now_s=90)
    grant, = book.advance(90)
    assert grant.request.vehicle_id == "V1"


def test_occupied_fault_closes_resource_until_explicit_exit_and_reopening():
    book = manager()
    book.enqueue("r1", "V1", {"corridor"}, now_s=0)
    first, = book.advance(0)
    occupy(book, first, {"corridor"})
    book.close("corridor", now_s=1)
    with pytest.raises(ValueError):
        book.cancel(first.token, "V1", now_s=1)
    with pytest.raises(ValueError):
        book.reopen("corridor", now_s=1)
    occupy(book, first, set(), at=2)
    assert "corridor" in book.closed
    book.reopen("corridor", now_s=2)
    assert "corridor" not in book.closed


def test_deadlock_timeout_reports_cycle_without_releasing_occupants():
    book = manager()
    grants = []
    for vehicle, resource in (("V1", "corridor"), ("V2", "junction")):
        book.enqueue(vehicle, vehicle, {resource}, now_s=0)
        grant, = book.advance(0)
        occupy(book, grant, {resource})
        grants.append(grant)
    book.enqueue("next1", "V1", {"junction"}, now_s=0, wait_timeout_s=5)
    book.enqueue("next2", "V2", {"corridor"}, now_s=0, wait_timeout_s=5)
    assert book.wait_cycles() == (("V1", "V2"),)
    assert not book.advance(5)
    assert len([e for e in book.events if e.kind == "DEADLOCK"]) == 2
    assert all(g.token in book.leases for g in grants)


def test_late_entry_is_recorded_as_fault_not_dropped_as_invalid_observation():
    book = manager()
    book.enqueue("r1", "V1", {"corridor"}, now_s=0, lease_s=1)
    grant, = book.advance(0)
    occupy(book, grant, {"corridor"}, at=2)
    assert "corridor" in book.closed
    assert book.leases[grant.token].occupied == {"corridor"}
    assert not book.advance(10)
    assert "UNAUTHORIZED_ENTRY" in [e.kind for e in book.events]


def test_unplanned_occupant_prevents_reopening_even_after_other_lease_expires():
    book = manager()
    book.enqueue("r1", "V1", {"corridor"}, now_s=0, lease_s=1)
    book.advance(0)
    book.report_unplanned_occupancy("V2", {"corridor"}, now_s=0.5, map_version="synthetic-test")
    book.advance(2)
    with pytest.raises(ValueError):
        book.reopen("corridor", now_s=2)
    book.report_unplanned_occupancy("V2", set(), now_s=2, map_version="synthetic-test")
    book.reopen("corridor", now_s=2)


@pytest.mark.parametrize("time", [-1, math.nan, math.inf])
def test_invalid_or_backward_time_rejected(time):
    with pytest.raises(ValueError):
        manager().advance(time)


def test_wrong_map_and_wrong_vehicle_cannot_change_occupancy():
    book = manager()
    book.enqueue("r1", "V1", {"corridor"}, now_s=0)
    grant, = book.advance(0)
    for vehicle, version in (("V2", "synthetic-test"), ("V1", "different")):
        with pytest.raises(ValueError):
            book.report_occupancy(grant.token, vehicle, {"corridor"}, now_s=0, map_version=version)
    assert not book.leases[grant.token].occupied
    grant.occupied.add("corridor")  # Returned grant is a detached snapshot.
    assert not book.leases[grant.token].occupied


def test_entry_permission_checks_context_expiry_exit_space_and_fault():
    book = manager()
    book.enqueue("r1", "V1", {"corridor", "exit"}, now_s=0, lease_s=1)
    grant, = book.advance(0)
    args = {"now_s": 0, "map_version": "synthetic-test"}
    assert book.entry_allowed(grant.token, "V1", "corridor", **args)
    assert not book.entry_allowed(grant.token, "V2", "corridor", **args)
    assert not book.entry_allowed(grant.token, "V1", "bay", **args)
    assert not book.entry_allowed(grant.token, "V1", "corridor", now_s=1, map_version="synthetic-test")
    occupy(book, grant, {"corridor"})
    assert book.entry_allowed(grant.token, "V1", "exit", now_s=10, map_version="synthetic-test")
    book.close("exit", now_s=10)
    assert not book.entry_allowed(grant.token, "V1", "exit", now_s=10, map_version="synthetic-test")
    assert book.drain_events()
    assert not book.events
