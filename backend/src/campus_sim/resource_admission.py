"""Synthetic resource admission gates Python speed intent, below sensor safety."""
import math
from dataclasses import dataclass
from itertools import pairwise

from campus_sim.resource_occupancy import ResourceOccupancyTracker


@dataclass(frozen=True)
class AdmissionPolicy:
    reaction_s: float
    braking_mps2: float
    stop_margin_m: float
    lease_s: float
    wait_timeout_s: float
    provenance: str

    def __post_init__(self):
        if (not all(math.isfinite(v) and v > 0 for v in (self.reaction_s, self.braking_mps2,
                    self.stop_margin_m, self.lease_s, self.wait_timeout_s)) or not self.provenance.strip()):
            raise ValueError("Positive admission timing/braking bounds and provenance required")


def _interval(a, b, region, radius):
    near, far = 0.0, 1.0
    for axis, minimum, maximum in ((0, region.min_x - radius, region.max_x + radius),
                                   (1, region.min_y - radius, region.max_y + radius)):
        delta = b[axis] - a[axis]
        if delta == 0:
            if not minimum <= a[axis] <= maximum:
                return None
        else:
            lo, hi = sorted(((minimum - a[axis]) / delta, (maximum - a[axis]) / delta))
            near, far = max(near, lo), min(far, hi)
            if near > far:
                return None
    return near, far


class ResourceAdmission:
    def __init__(self, monitor: ResourceOccupancyTracker, groups: dict[str, frozenset[str]],
                 policy: AdmissionPolicy, *, difficult_to_yield: frozenset[str] = frozenset()):
        if (set(groups) != monitor.book.resources or any(
                key not in value or not value <= monitor.book.resources for key, value in groups.items())):
            raise ValueError("Every resource needs an explicit group including its exit space")
        if not difficult_to_yield <= monitor.policies.keys():
            raise ValueError("Unknown difficult-to-yield vehicle")
        self.monitor, self.policy = monitor, policy
        self.groups = {key: frozenset(value) for key, value in groups.items()}
        self.bindings: dict[str, tuple[str, str]] = {}
        self.timed_out: set[str] = set()
        self.counter = 0
        self.difficult_to_yield = frozenset(difficult_to_yield)

    def upcoming(self, vehicle, runtime):
        vehicle_policy = self.monitor.policies[vehicle]
        radius = vehicle_policy.footprint.radius_m + vehicle_policy.position_error_m
        required = {}
        offset = 0.0
        for a, b in pairwise(runtime.route_points):
            length = math.dist(a, b)
            for name, region in self.monitor.regions.items():
                interval = _interval(a, b, region, radius)
                if interval is None or offset + interval[1] * length < runtime.route_progress_m:
                    continue
                distance = max(0, offset + interval[0] * length - runtime.route_progress_m)
                # Actual lateral tracking error may place the body nearer than its
                # route projection. Use the smaller geometric distance as well.
                dx = max(region.min_x - radius - runtime.x, 0, runtime.x - region.max_x - radius)
                dy = max(region.min_y - radius - runtime.y, 0, runtime.y - region.max_y - radius)
                required[name] = min(required.get(name, math.inf), distance, math.hypot(dx, dy))
            offset += length
        return required

    def sync(self, service):
        """Queue the entire fleet before arbitration; called once per service tick."""
        book, now = self.monitor.book, service.now_s()
        for request_id, (vehicle, route_id) in list(self.bindings.items()):
            runtime = service.vehicle_runtime.get(vehicle)
            if runtime is not None and runtime.route_id == route_id:
                continue
            if request_id in book.pending:
                book.cancel_pending(request_id, vehicle, now_s=now)
            for token, lease in list(book.leases.items()):
                if lease.request.request_id == request_id and not lease.occupied:
                    book.cancel(token, vehicle, now_s=now)
            self.bindings.pop(request_id)
        for vehicle in sorted(self.monitor.policies):
            runtime = service.vehicle_runtime.get(vehicle)
            if (runtime is None or runtime.route_id is None or vehicle in self.timed_out
                    or vehicle in self.monitor.faults or vehicle not in self.monitor.previous
                    or service.ego_localization_is_stale(vehicle)):
                continue
            upcoming = self.upcoming(vehicle, runtime)
            resources = set().union(*(self.groups[name] for name in upcoming)) if upcoming else set()
            owned = {r for r, t in book.claims.items() if book.leases[t].request.vehicle_id == vehicle}
            # Keep the original atomic group's completed legs satisfied while
            # traversing its remaining resources; do not reserve the cleared
            # entrance behind us again just because an exit references the group.
            completed_legs = set().union(*(lease.exited for lease in book.leases.values()
                if lease.request.vehicle_id == vehicle))
            resources -= completed_legs - upcoming.keys()
            resources -= owned
            if not resources or any(r.vehicle_id == vehicle for r in book.pending.values()):
                continue
            self.counter += 1
            identity = f"admission-{self.counter}"
            request = service.requests.get(runtime.request_id or "")
            priority = service._queued_request_priority(request, runtime) if request is not None else 0
            book.enqueue(identity, vehicle, resources, now_s=now, lease_s=self.policy.lease_s,
                         wait_timeout_s=self.policy.wait_timeout_s, priority=priority,
                         difficult_to_yield=vehicle in self.difficult_to_yield)
            self.bindings[identity] = (vehicle, runtime.route_id)
        event_start = len(book.events)
        book.advance(now)
        for event in book.events[event_start:]:
            if event.kind == "WAIT_TIMEOUT" and event.request_id in self.bindings:
                self.timed_out.add(event.vehicle_id)

    def limit(self, service, vehicle, speed, yaw, controller_policy):
        monitor, now = self.monitor, service.now_s()
        if (vehicle not in monitor.policies or vehicle not in monitor.previous
                or vehicle in monitor.faults or vehicle in self.timed_out
                or service.graph.map_version != monitor.book.map_version):
            return 0.0, 0.0, "RESOURCE_STATE_UNAVAILABLE"
        runtime = service.vehicle_runtime[vehicle]
        capped = speed
        deceleration = min(self.policy.braking_mps2, controller_policy.braking_mps2)
        reaction = max(self.policy.reaction_s, controller_policy.command_valid_for_s)
        occupancy_policy = monitor.policies[vehicle]
        # The occupancy tracker encloses any motion between valid samples by a
        # midpoint disc. Wait outside that same envelope, not merely outside the
        # instantaneous body: otherwise a valid delayed sample can latch an
        # unplanned-entry closure while the vehicle is obeying its hold line.
        sample_margin = occupancy_policy.max_speed_mps * occupancy_policy.max_sample_gap_s / 2
        blocked = False
        rotation_allowed = True
        for name, distance in self.upcoming(vehicle, runtime).items():
            # A pre-entry grant must remain valid through the reaction/braking window.
            future = now + reaction + max(speed, runtime.speed_mps) / deceleration
            lease = monitor.book.leases.get(monitor.book.claims.get(name, ""))
            completed = lease.exited if lease is not None and lease.request.vehicle_id == vehicle else set()
            if all((member != name and member in completed and member not in monitor.book.closed)
                   or monitor.book.entry_allowed(monitor.book.claims.get(member, ""), vehicle, member,
                                              now_s=future, map_version=service.graph.map_version)
                   for member in self.groups[name]):
                continue
            blocked = True
            available = max(0, distance - sample_margin - self.policy.stop_margin_m)
            safe_speed = math.sqrt((deceleration * reaction)**2 + 2 * deceleration * available) - deceleration * reaction
            # Avoid an asymptotic crawl at the hold line.
            if safe_speed < 0.02:
                safe_speed = 0
                rotation_allowed = False
            capped = min(capped, safe_speed)
        if blocked:
            # At-rest alignment is permitted outside the circumscribed-body hold
            # boundary even when steering intentionally requests zero speed.
            return capped, yaw if rotation_allowed else 0.0, "RESOURCE_WAIT"
        return speed, yaw, None
