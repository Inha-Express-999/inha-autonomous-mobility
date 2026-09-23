import pytest

from campus_sim.domain import CreateRequest, ServiceNeeds
from campus_sim.service import MobilityService


def passenger_command(command_id: str = "command-1") -> CreateRequest:
    return CreateRequest(
        command_id=command_id,
        owner_id="fixture-passenger",
        service_type="PASSENGER",
        pickup_landmark_id="main_gate",
        dropoff_landmark_id="library",
        party_size=1,
    )


def test_create_assigns_a_suitable_vehicle_and_is_idempotent() -> None:
    service = MobilityService.synthetic_fixture()
    first = service.create_request(passenger_command())
    repeated = service.create_request(passenger_command())
    assert first.request.status == "ASSIGNED"
    assert first.request.vehicle_id == "V01"
    assert repeated.request.id == first.request.id


def test_step_free_request_selects_step_free_stops() -> None:
    service = MobilityService.synthetic_fixture()
    command = passenger_command()
    command.service_needs = ServiceNeeds(requires_step_free=True, wheelchair_slots=1)
    result = service.create_request(command)
    assert result.request.pickup_stop_id == "main_gate_step_free"
    assert result.request.dropoff_stop_id == "library_step_free"


def test_cancellation_releases_vehicle_and_is_idempotent() -> None:
    service = MobilityService.synthetic_fixture()
    created = service.create_request(passenger_command())
    cancelled = service.cancel_request(created.request.id, "cancel-1", "fixture-passenger")
    repeated = service.cancel_request(created.request.id, "cancel-1", "fixture-passenger")
    assert cancelled.request.status == "CANCELLED"
    assert service.vehicles["V01"].available is True
    assert repeated.request.id == created.request.id


def test_unknown_landmark_is_rejected() -> None:
    service = MobilityService.synthetic_fixture()
    command = passenger_command()
    command.pickup_landmark_id = "missing"
    with pytest.raises(ValueError, match="unknown landmark"):
        service.create_request(command)
