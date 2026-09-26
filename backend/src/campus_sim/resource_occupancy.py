"""Ego-only rectangular resource occupancy, with conservative loss-of-context holds."""
import math
from dataclasses import dataclass

from campus_sim.domain import EgoLocalization
from campus_sim.reservations import ResourceReservations
from campus_sim.swept_geometry import BoxFootprint, Pose2, swept_box_hits_disc


@dataclass(frozen=True)
class ResourceRegion:
    resource_id: str
    min_x: float
    min_y: float
    max_x: float
    max_y: float
    provenance: str

    def __post_init__(self):
        if (not self.resource_id.strip() or not self.provenance.strip()
                or not all(math.isfinite(v) for v in (self.min_x, self.min_y, self.max_x, self.max_y))
                or self.min_x >= self.max_x or self.min_y >= self.max_y):
            raise ValueError("Explicit finite resource rectangle and provenance required")

    @property
    def center(self):
        return ((self.min_x + self.max_x) / 2, (self.min_y + self.max_y) / 2)


@dataclass(frozen=True)
class OccupancyPolicy:
    footprint: BoxFootprint
    position_error_m: float
    max_speed_mps: float
    max_sample_gap_s: float
    outside_hold_s: float
    provenance: str

    def __post_init__(self):
        if (not all(math.isfinite(v) and v >= 0 for v in (self.position_error_m, self.max_speed_mps,
                    self.max_sample_gap_s, self.outside_hold_s)) or self.max_speed_mps <= 0
                or self.max_sample_gap_s <= 0 or self.outside_hold_s <= 0 or not self.provenance.strip()):
            raise ValueError("Explicit footprint, motion/freshness bounds and exit hold required")


def footprint_overlaps_region(pose: Pose2, footprint: BoxFootprint, region: ResourceRegion,
                              position_error_m: float = 0) -> bool:
    """Four separating axes for oriented body versus axis-aligned resource.

    Position uncertainty inflates projected body support. Contact counts as occupied.
    """
    if not math.isfinite(position_error_m) or position_error_m < 0:
        raise ValueError("Invalid position error")
    forward = (math.sin(pose.heading_rad), math.cos(pose.heading_rad))
    lateral = (forward[1], -forward[0])
    cx, cy = region.center
    delta = (pose.x - cx, pose.y - cy)
    rx, ry = (region.max_x - region.min_x) / 2, (region.max_y - region.min_y) / 2
    for ax, ay in ((1, 0), (0, 1), forward, lateral):
        support = (rx * abs(ax) + ry * abs(ay)
                   + footprint.length_m / 2 * abs(forward[0] * ax + forward[1] * ay)
                   + footprint.width_m / 2 * abs(lateral[0] * ax + lateral[1] * ay)
                   + position_error_m + 1e-9)
        if abs(delta[0] * ax + delta[1] * ay) > support:
            return False
    return True


class ResourceOccupancyTracker:
    def __init__(self, book: ResourceReservations, regions: tuple[ResourceRegion, ...],
                 policies: dict[str, OccupancyPolicy]):
        self.regions = {r.resource_id: r for r in regions}
        if len(self.regions) != len(regions) or set(self.regions) != book.resources or not policies:
            raise ValueError("Every declared resource needs one region and vehicles need policies")
        if book.unplanned_occupancy or any(lease.occupied for lease in book.leases.values()):
            raise ValueError("Existing occupancy needs explicit recovery, not a new empty tracker")
        self.book, self.policies = book, dict(policies)
        self.previous: dict[str, tuple[EgoLocalization, float]] = {}
        self.held: dict[str, set[str]] = {}
        self.outside_since: dict[tuple[str, str], float] = {}
        self.faults: dict[str, str] = {}

    def _fault(self, vehicle, reason, now):
        # Lost continuity cannot prove which resource the vehicle occupies. Hold all
        # resources until explicit external reconciliation; no guessed reset/exit.
        self.book.report_unplanned_occupancy(vehicle, self.regions, now_s=now,
                                             map_version=self.book.map_version)
        self.faults[vehicle] = reason
        return False

    def observe(self, observation: EgoLocalization, now_s: float) -> bool:
        vehicle = observation.vehicle_id
        if vehicle not in self.policies:
            return False
        if not math.isfinite(now_s) or now_s < self.book.now_s:
            raise ValueError("Occupancy receipt time must be monotonic")
        if vehicle in self.faults:
            return False
        if observation.map_version != self.book.map_version:
            return self._fault(vehicle, "map_mismatch", now_s)
        policy = self.policies[vehicle]
        pose = Pose2(observation.position.x, observation.position.y, observation.heading_rad)
        current = {name for name, region in self.regions.items()
                   if footprint_overlaps_region(pose, policy.footprint, region, policy.position_error_m)}
        potential = set()
        previous = self.previous.get(vehicle)
        if previous:
            old, received = previous
            if observation.session_id != old.session_id:
                return self._fault(vehicle, "session_changed", now_s)
            if observation.observed_tick <= old.observed_tick:
                return False  # Replays neither clear occupancy nor refresh freshness.
            dt = now_s - received
            distance = math.hypot(pose.x - old.position.x, pose.y - old.position.y)
            if dt > policy.max_sample_gap_s or distance > policy.max_speed_mps * dt + 2 * policy.position_error_m:
                return self._fault(vehicle, "discontinuous_localization", now_s)
            old_pose = Pose2(old.position.x, old.position.y, old.heading_rad)
            for name, region in self.regions.items():
                if name in current or footprint_overlaps_region(old_pose, policy.footprint, region,
                                                                 policy.position_error_m):
                    continue
                # Any bounded-speed path between samples lies inside the ellipse
                # whose foci are the endpoints and major semiaxis is vmax*dt/2.
                # Its enclosing disc plus body radius bounds curved motion and yaw.
                center = ((pose.x + old_pose.x) / 2, (pose.y + old_pose.y) / 2)
                radius = policy.max_speed_mps * dt / 2 + policy.footprint.radius_m + policy.position_error_m
                cx, cy = region.center
                resource_pose = Pose2(cx, cy, 0)
                resource_box = BoxFootprint(region.max_y - region.min_y, region.max_x - region.min_x)
                if swept_box_hits_disc(resource_pose, resource_pose, resource_box, center, center, radius):
                    potential.add(name)
        if observation.speed_mps > policy.max_speed_mps:
            return self._fault(vehicle, "speed_bound_exceeded", now_s)
        held = self.held.setdefault(vehicle, set())
        held.update(current | potential)
        for name in tuple(held):
            key = (vehicle, name)
            if name in current or name in potential:
                self.outside_since.pop(key, None)
            else:
                since = self.outside_since.setdefault(key, now_s)
                if now_s - since >= policy.outside_hold_s:
                    held.remove(name)
                    self.outside_since.pop(key, None)
        owned = set()
        for token, lease in list(self.book.leases.items()):
            if lease.request.vehicle_id != vehicle:
                continue
            active = {r for r in lease.request.resource_ids if self.book.claims.get(r) == token}
            owned.update(active)
            self.book.report_occupancy(token, vehicle, held & active, now_s=now_s,
                                       map_version=self.book.map_version)
        self.book.report_unplanned_occupancy(vehicle, held - owned, now_s=now_s,
                                             map_version=self.book.map_version)
        self.previous[vehicle] = (observation.model_copy(deep=True), now_s)
        return True

    def check_freshness(self, now_s: float) -> None:
        if not math.isfinite(now_s) or now_s < self.book.now_s:
            raise ValueError("Occupancy time must be finite and monotonic")
        for vehicle, (_, received) in tuple(self.previous.items()):
            if vehicle not in self.faults and now_s - received > self.policies[vehicle].max_sample_gap_s:
                self._fault(vehicle, "stale_localization", now_s)
