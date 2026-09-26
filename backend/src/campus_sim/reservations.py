"""Deterministic resource admission; leases never override reported occupancy.

Resources and occupancy events must come from a qualified map/ego-localization
adapter. This module neither invents resource geometry nor reads hidden actors.
"""
import math
from copy import deepcopy
from dataclasses import dataclass, field


@dataclass(frozen=True)
class ResourceRequest:
    request_id: str
    vehicle_id: str
    resource_ids: frozenset[str]
    priority: float
    difficult_to_yield: bool
    submitted_at_s: float
    lease_s: float
    wait_timeout_s: float


@dataclass
class ResourceLease:
    token: str
    request: ResourceRequest
    expires_at_s: float
    entered: set[str] = field(default_factory=set)
    occupied: set[str] = field(default_factory=set)
    exited: set[str] = field(default_factory=set)


@dataclass(frozen=True)
class ReservationEvent:
    at_s: float
    kind: str
    vehicle_id: str
    request_id: str
    resources: tuple[str, ...]
    token: str | None = None


class ResourceReservations:
    """Single-threaded service-owned state machine with atomic multi-resource grants.

    Include the corridor AND its exit space in one request. Occupancy reports
    concern ego only; a future lease is never proof of actual physical vacancy.
    Waiting vehicles must remain outside entry stop lines in the integration layer.
    """
    def __init__(self, resource_ids, *, map_version: str, aging_interval_s: float = 30):
        self.resources = frozenset(resource_ids)
        if (not self.resources or any(not isinstance(r, str) or not r.strip() for r in self.resources)
                or not isinstance(map_version, str) or not map_version.strip()
                or not math.isfinite(aging_interval_s) or aging_interval_s <= 0):
            raise ValueError("Explicit map resources and positive aging interval required")
        self.map_version = map_version
        self.aging_interval_s = aging_interval_s
        self.now_s = 0.0
        self.pending: dict[str, ResourceRequest] = {}
        self.leases: dict[str, ResourceLease] = {}
        self.claims: dict[str, str] = {}
        self.closed: set[str] = set()
        self.unplanned_occupancy: dict[str, set[str]] = {}
        self.events: list[ReservationEvent] = []
        self.sequence = 0

    def _time(self, now_s):
        if not math.isfinite(now_s) or now_s < self.now_s:
            raise ValueError("Reservation time must be finite and monotonic")
        self.now_s = now_s

    def _event(self, kind, request, token=None):
        self.events.append(ReservationEvent(self.now_s, kind, request.vehicle_id, request.request_id,
                                             tuple(sorted(request.resource_ids)), token))

    def enqueue(self, request_id: str, vehicle_id: str, resource_ids, *, now_s: float,
                priority: float = 0, difficult_to_yield: bool = False,
                lease_s: float = 2, wait_timeout_s: float = 30) -> None:
        resources = frozenset(resource_ids)
        if (not isinstance(request_id, str) or not request_id.strip()
                or not isinstance(vehicle_id, str) or not vehicle_id.strip()
                or not resources or not resources <= self.resources
                or not all(math.isfinite(v) for v in (priority, lease_s, wait_timeout_s))
                or lease_s <= 0 or wait_timeout_s <= 0 or not isinstance(difficult_to_yield, bool)):
            raise ValueError("Invalid resource request")
        if request_id in self.pending or any(l.request.request_id == request_id for l in self.leases.values()):
            raise ValueError("Request already exists")
        if any(r.vehicle_id == vehicle_id for r in self.pending.values()):
            raise ValueError("Vehicle already has a pending request")
        if any(self.leases[t].request.vehicle_id == vehicle_id
               for resource, t in self.claims.items() if resource in resources):
            raise ValueError("Vehicle already claims a requested resource")
        self._time(now_s)
        request = ResourceRequest(request_id, vehicle_id, resources, priority, difficult_to_yield,
                                  now_s, lease_s, wait_timeout_s)
        self.pending[request_id] = request
        self._event("QUEUED", request)

    def advance(self, now_s: float) -> tuple[ResourceLease, ...]:
        self._time(now_s)
        for token, lease in list(self.leases.items()):
            # Once entered, even a currently empty intermediate step cannot expire
            # the still-reserved exit space. Explicit release/cancel is required.
            if not lease.entered and lease.expires_at_s <= now_s:
                self._release(token, "LEASE_EXPIRED")
        deadlocked = {vehicle for cycle in self.wait_cycles() for vehicle in cycle}
        for identity, request in list(self.pending.items()):
            if request.submitted_at_s + request.wait_timeout_s <= now_s:
                self.pending.pop(identity)
                self._event("WAIT_TIMEOUT", request)
                if request.vehicle_id in deadlocked:
                    self._event("DEADLOCK", request)
        ordered = sorted(self.pending.values(), key=lambda r: (
            not r.difficult_to_yield,
            -(r.priority + (now_s - r.submitted_at_s) / self.aging_interval_s),
            r.submitted_at_s, r.vehicle_id, r.request_id))
        granted = []
        for request in ordered:
            if request.resource_ids.intersection(self.closed | self.claims.keys()):
                continue
            self.sequence += 1
            token = f"reservation-{self.sequence}"
            lease = ResourceLease(token, request, now_s + request.lease_s)
            self.leases[token] = lease
            for resource in request.resource_ids:
                self.claims[resource] = token
            self.pending.pop(request.request_id)
            self._event("GRANTED", request, token)
            granted.append(deepcopy(lease))
        return tuple(granted)

    def entry_allowed(self, token: str, vehicle_id: str, resource_id: str, *,
                       now_s: float, map_version: str) -> bool:
        """Reservation permission only; sensor safety and stop-line control still apply."""
        lease = self.leases.get(token)
        return bool(lease is not None and math.isfinite(now_s) and now_s >= self.now_s
                    and map_version == self.map_version and vehicle_id == lease.request.vehicle_id
                    and resource_id in lease.request.resource_ids and resource_id not in lease.exited
                    and resource_id not in self.closed and self.claims.get(resource_id) == token
                    and (lease.entered or now_s < lease.expires_at_s))

    def drain_events(self) -> tuple[ReservationEvent, ...]:
        """Service/replay logger consumes events so a long run can bound memory."""
        result = tuple(self.events)
        self.events.clear()
        return result

    def report_occupancy(self, token: str, vehicle_id: str, occupied_ids, *, now_s: float,
                         map_version: str) -> None:
        """Replace this lease's ego occupancy using a validated boundary observation.

        New entry requires a live grant. Exit is an explicit observed transition,
        never inferred from TTL. Occupied resources cannot be cancelled/reassigned.
        """
        occupied = set(occupied_ids)
        lease = self.leases[token]
        if (vehicle_id != lease.request.vehicle_id or map_version != self.map_version
                or not occupied <= lease.request.resource_ids or occupied.intersection(lease.exited)):
            raise ValueError("Occupancy context mismatch or re-entry after release")
        self._time(now_s)
        unauthorized = ((occupied and not lease.entered and now_s >= lease.expires_at_s)
                        or (occupied - lease.occupied).intersection(self.closed))
        if unauthorized:
            # A physical observation cannot be rejected merely because entry was
            # unauthorized. Close the affected resources and retain the occupant.
            self.closed.update(occupied)
            self._event("UNAUTHORIZED_ENTRY", lease.request, token)
        exited = lease.occupied - occupied
        lease.entered.update(occupied)
        lease.exited.update(exited)
        lease.occupied = occupied
        for resource in exited:
            self.claims.pop(resource)
        self._event("OCCUPANCY", lease.request, token)
        if lease.exited == lease.request.resource_ids:
            self._release(token, "COMPLETED")

    def cancel(self, token: str, vehicle_id: str, *, now_s: float) -> None:
        lease = self.leases[token]
        if lease.request.vehicle_id != vehicle_id or lease.occupied:
            raise ValueError("Cannot cancel another vehicle's or an occupied lease")
        self._time(now_s)
        self._release(token, "CANCELLED")

    def cancel_pending(self, request_id: str, vehicle_id: str, *, now_s: float) -> None:
        request = self.pending[request_id]
        if request.vehicle_id != vehicle_id:
            raise ValueError("Cannot cancel another vehicle's request")
        self._time(now_s)
        self.pending.pop(request_id)
        self._event("QUEUE_CANCELLED", request)

    def _release(self, token, reason):
        lease = self.leases.pop(token)
        for resource in lease.request.resource_ids:
            if self.claims.get(resource) == token:
                self.claims.pop(resource)
        self._event(reason, lease.request, token)

    def close(self, resource_id: str, *, now_s: float) -> None:
        if resource_id not in self.resources:
            raise ValueError("Unknown resource")
        self._time(now_s)
        self.closed.add(resource_id)  # Preserve the actual occupant and its claim.

    def reopen(self, resource_id: str, *, now_s: float) -> None:
        if (resource_id not in self.resources or resource_id in self.claims
                or any(resource_id in occupied for occupied in self.unplanned_occupancy.values())):
            raise ValueError("Reopening requires an explicitly empty, unclaimed resource")
        self._time(now_s)
        self.closed.discard(resource_id)

    def report_unplanned_occupancy(self, vehicle_id: str, occupied_ids, *, now_s: float,
                                   map_version: str) -> None:
        """Record validated ego occupancy with no live token, including late entry.

        Close every affected resource even if another vehicle owns a lease. A
        later empty report records exit but never automatically reopens a fault.
        """
        occupied = set(occupied_ids)
        if not vehicle_id or map_version != self.map_version or not occupied <= self.resources:
            raise ValueError("Unplanned occupancy context mismatch")
        self._time(now_s)
        self.closed.update(occupied)
        if occupied:
            self.unplanned_occupancy[vehicle_id] = occupied
        else:
            self.unplanned_occupancy.pop(vehicle_id, None)
        self.events.append(ReservationEvent(now_s, "UNPLANNED_OCCUPANCY", vehicle_id, "",
                                             tuple(sorted(occupied))))

    def wait_cycles(self) -> tuple[tuple[str, ...], ...]:
        """Report wait-for cycles; never resolve by deleting/moving a vehicle."""
        waits = {}
        for request in self.pending.values():
            waits[request.vehicle_id] = {self.leases[self.claims[r]].request.vehicle_id
                                         for r in request.resource_ids if r in self.claims}
        cycles = set()

        def visit(node, path):
            if node in path:
                cycle = path[path.index(node):]
                cycles.add(min(tuple(cycle[i:] + cycle[:i]) for i in range(len(cycle))))
                return
            for target in sorted(waits.get(node, ())):
                visit(target, [*path, node])

        for node in sorted(waits):
            visit(node, [])
        return tuple(sorted(cycles))
