"""Deterministic FIFO baseline; no route costs or priority optimization."""

from collections.abc import Callable, Iterable, Mapping
from dataclasses import dataclass

from campus_sim.domain import RequestStatus, RequestView, ServiceType, Vehicle


@dataclass(frozen=True)
class Assignment:
    request_id: str
    vehicle_id: str


def has_capacity(vehicle: Vehicle, request: RequestView) -> bool:
    if request.service_type is ServiceType.PASSENGER:
        return (
            vehicle.passenger_capacity >= request.party_size
            and vehicle.wheelchair_slots >= request.service_needs.wheelchair_slots
        )
    return vehicle.cargo_capacity_kg >= request.cargo_kg


class FifoDispatch:
    """Plan without mutating inputs. Infeasible requests stay queued.

    Requests sort by (created_s, id), vehicles by availability sequence then id.
    Without an availability sequence the initial queue uses id order. The predicate
    adds hard constraints (e.g. routes/energy); it never overrides capacity.
    Callers must apply the returned batch atomically before planning again.
    """

    def assign(
        self,
        requests: Iterable[RequestView],
        vehicles: Iterable[Vehicle],
        feasible: Callable[[Vehicle, RequestView], bool] | None = None,
        *,
        vehicle_order: Mapping[str, int] | None = None,
    ) -> tuple[Assignment, ...]:
        request_list, vehicle_list = list(requests), list(vehicles)
        if len({r.id for r in request_list}) != len(request_list):
            raise ValueError("duplicate request id")
        if len({v.id for v in vehicle_list}) != len(vehicle_list):
            raise ValueError("duplicate vehicle id")
        order = vehicle_order or {}
        available = {
            v.id: v
            for v in sorted(vehicle_list, key=lambda v: (order.get(v.id, 0), v.id))
            if v.available
        }
        # Guard against an inconsistent available flag for an occupied vehicle.
        for request in request_list:
            if request.status in {
                RequestStatus.ASSIGNED,
                RequestStatus.PICKUP_SERVICE,
                RequestStatus.IN_TRANSIT,
                RequestStatus.DROPOFF_SERVICE,
            }:
                available.pop(request.vehicle_id, None)
        assignments = []
        queued = sorted(
            (r for r in request_list if r.status is RequestStatus.QUEUED),
            key=lambda r: (r.created_s, r.id),
        )
        for request in queued:
            for vehicle_id, vehicle in available.items():
                if has_capacity(vehicle, request) and (
                    feasible is None or feasible(vehicle, request)
                ):
                    assignments.append(Assignment(request.id, vehicle_id))
                    del available[vehicle_id]
                    break
        return tuple(assignments)
