"""Pure deterministic FIFO, Greedy and self-implemented Hungarian dispatch."""

from collections.abc import Callable, Iterable, Mapping
from dataclasses import dataclass
from math import inf, isfinite

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


def hungarian(costs: list[list[float | None]]) -> tuple[int, ...]:
    """Minimum-cost rectangular full row matching. None edges do not exist.

    Rows <= columns and a feasible row matching are required. Partial service
    selection is handled separately by a priority-ordered augmenting frontier.
    No forbidden edge is replaced with a large numeric penalty.
    """
    n = len(costs)
    if not n:
        return ()
    m = len(costs[0])
    if n > m or any(len(row) != m for row in costs):
        raise ValueError("rectangular costs require rows <= columns")
    if any(c is not None and (not isfinite(c) or c < 0) for row in costs for c in row):
        raise ValueError("costs must be finite nonnegative values or None")
    u, v, matched, previous = [0.0] * (n + 1), [0.0] * (m + 1), [0] * (m + 1), [0] * (m + 1)
    for row in range(1, n + 1):
        matched[0] = row
        column = 0
        slack, visited = [inf] * (m + 1), [False] * (m + 1)
        while True:
            visited[column] = True
            current_row, delta, next_column = matched[column], inf, 0
            for candidate in range(1, m + 1):
                if visited[candidate]:
                    continue
                value = costs[current_row - 1][candidate - 1]
                if value is not None:
                    reduced = value - u[current_row] - v[candidate]
                    if reduced < slack[candidate]:
                        slack[candidate], previous[candidate] = reduced, column
                if slack[candidate] < delta:
                    delta, next_column = slack[candidate], candidate
            if not isfinite(delta):
                raise ValueError("no feasible full row matching")
            for candidate in range(m + 1):
                if visited[candidate]:
                    u[matched[candidate]] += delta
                    v[candidate] -= delta
                else:
                    slack[candidate] -= delta
            column = next_column
            if matched[column] == 0:
                break
        while column:
            parent_column = previous[column]
            matched[column] = matched[parent_column]
            column = parent_column
    result = [-1] * n
    for column in range(1, m + 1):
        if matched[column]:
            result[matched[column] - 1] = column - 1
    return tuple(result)


class GreedyDispatch:
    def assign(
        self,
        requests,
        vehicles,
        feasible,
        *,
        cost,
        priority=None,
        vehicle_order=None,
        now_s=None,
        opportunity_count=0,
    ) -> tuple[Assignment, ...]:
        requests, vehicles = list(requests), list(vehicles)
        if len({r.id for r in requests}) != len(requests) or len({v.id for v in vehicles}) != len(
            vehicles
        ):
            raise ValueError("duplicate request or vehicle id")
        occupied = {
            r.vehicle_id
            for r in requests
            if r.status
            in {
                RequestStatus.ASSIGNED,
                RequestStatus.PICKUP_SERVICE,
                RequestStatus.IN_TRANSIT,
                RequestStatus.DROPOFF_SERVICE,
            }
        }
        vehicles = sorted(
            (v for v in vehicles if v.available and v.id not in occupied), key=lambda v: v.id
        )
        queued = [r for r in requests if r.status is RequestStatus.QUEUED]
        matrix = {}
        for request in queued:
            for vehicle in vehicles:
                if has_capacity(vehicle, request) and feasible(vehicle, request):
                    value = cost(vehicle, request)
                    if not isfinite(value) or value < 0:
                        raise ValueError("invalid assignment cost")
                    matrix[request.id, vehicle.id] = value
        queued.sort(key=priority or (lambda r: (r.created_s, r.id)))
        return self._match(queued, vehicles, matrix, now_s, opportunity_count)

    def _match(self, requests, vehicles, matrix, now_s, opportunity_count):
        available = {v.id for v in vehicles}
        result = []
        pending = list(requests)
        while pending:
            eligible = [r for r in pending if any((r.id, v) in matrix for v in available)]
            if not eligible:
                break
            request = eligible[0]
            long_wait = [r for r in eligible if now_s is not None and now_s - r.created_s >= 600]
            if long_wait and (opportunity_count + len(result) + 1) % 3 == 0:
                request = min(long_wait, key=lambda r: (r.created_s, r.id))
            pending.remove(request)
            candidates = [v for v in sorted(available) if (request.id, v) in matrix]
            if candidates:
                vehicle = min(candidates, key=lambda v: (matrix[request.id, v], v))
                available.remove(vehicle)
                result.append(Assignment(request.id, vehicle))
        return tuple(result)


class HungarianDispatch(GreedyDispatch):
    def _match(self, requests, vehicles, matrix, now_s, opportunity_count):
        # A transversal-matroid greedy frontier: keep high-priority requests when
        # an augmenting matching can still serve them. This maximizes cardinality
        # without sacrificing previously accepted priorities, even with None edges.
        selected, owners = [], {}

        def augment(request_id, seen):
            for vehicle in vehicles:
                if vehicle.id in seen or (request_id, vehicle.id) not in matrix:
                    continue
                seen.add(vehicle.id)
                if vehicle.id not in owners or augment(owners[vehicle.id], seen):
                    owners[vehicle.id] = request_id
                    return True
            return False

        for request in requests:
            if augment(request.id, set()):
                selected.append(request)
            if len(selected) == len(vehicles):
                break
        long_wait = [
            r
            for r in requests
            if now_s is not None
            and now_s - r.created_s >= 600
            and any((r.id, v.id) in matrix for v in vehicles)
        ]
        protected = None
        if long_wait and opportunity_count // 3 < (opportunity_count + len(selected)) // 3:
            protected = min(long_wait, key=lambda r: (r.created_s, r.id))
            selected, owners = [], {}
            for request in [protected] + [r for r in requests if r.id != protected.id]:
                if augment(request.id, set()):
                    selected.append(request)
                if len(selected) == len(vehicles):
                    break
        costs = [[matrix.get((r.id, v.id)) for v in vehicles] for r in selected]
        matching = hungarian(costs)
        result = [Assignment(r.id, vehicles[column].id) for r, column in zip(selected, matching)]
        if protected is not None:
            assignment = next(a for a in result if a.request_id == protected.id)
            result.remove(assignment)
            result.insert(2 - opportunity_count % 3, assignment)
        return tuple(result)
