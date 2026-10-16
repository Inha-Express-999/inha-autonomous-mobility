import pytest

from campus_sim.dispatch import Assignment, FifoDispatch
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
