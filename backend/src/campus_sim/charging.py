"""Synthetic charging-slot lifecycle; movement and route authority remain separate."""
import json
import math
from collections import deque
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class ChargingStation:
    id: str
    node_id: str
    power_w: float
    efficiency: float
    target_fraction: float
    arrival_radius_m: float
    release_radius_m: float
    release_hold_s: float
    max_credit_step_s: float
    provenance: str

    def __post_init__(self):
        values = (self.power_w, self.efficiency, self.target_fraction, self.arrival_radius_m,
                  self.release_radius_m, self.release_hold_s, self.max_credit_step_s)
        if (not self.id.strip() or not self.node_id.strip() or not self.provenance.strip()
                or any(isinstance(v, bool) or not math.isfinite(v) or v <= 0 for v in values)
                or self.efficiency > 1 or not .15 < self.target_fraction <= 1
                or self.release_radius_m <= self.arrival_radius_m or self.max_credit_step_s > .5):
            raise ValueError("Explicit finite synthetic station power, pose and timing bounds required")


@dataclass
class ChargingVisit:
    vehicle: str
    station: str
    queued_s: float
    session: str
    state: str = "QUEUED"
    entered: bool = False
    completed: bool = False
    previous_credit_s: float | None = None
    outside_since_s: float | None = None
    supplied_wh: float = 0.0


class ChargingStations:
    """One reserved vehicle per station; full vehicles retain the dock until observed exit."""
    def __init__(self, *, map_version, stations, auto_start_fraction=None):
        self.stations = {s.id: s for s in stations}
        if (not map_version.startswith("synthetic-") or not self.stations
                or len(self.stations) != len(stations)
                or len({s.node_id for s in stations}) != len(stations)
                or auto_start_fraction is not None and
                (isinstance(auto_start_fraction, bool) or not math.isfinite(auto_start_fraction)
                 or not 0 < auto_start_fraction < min(s.target_fraction for s in stations))):
            raise ValueError("Unique synthetic station nodes and valid automatic threshold required")
        self.map_version = map_version
        self.auto_start_fraction = auto_start_fraction
        self.visits = {}
        self.owners = {}
        self.events = deque(maxlen=128)
        self.last_tick_s = None

    @classmethod
    def from_config(cls, path):
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        if type(data.get("schema_version")) is not int or data["schema_version"] != 1 or data.get("data_status") != "SYNTHETIC_ASSUMPTIONS":
            raise ValueError("Versioned synthetic charging assumptions required")
        return cls(map_version=data["map_version"], stations=[ChargingStation(**s) for s in data["stations"]],
                   auto_start_fraction=data.get("auto_start_fraction"))

    def validate(self, service):
        if (service.energy is None or service.graph is None or self.map_version != service.graph.map_version
                or self.map_version != service.energy.map_version
                or not {s.node_id for s in self.stations.values()} <= service.energy.charger_nodes
                or not {s.node_id for s in self.stations.values()} <= {n.id for n in service.graph.nodes}):
            raise ValueError("Charging requires matching energy/map and declared charger nodes")
        if any(s.power_w * s.efficiency <= p.auxiliary_w
               or s.target_fraction <= p.reserve_fraction
               for s in self.stations.values() for p in service.energy.policies.values()):
            raise ValueError("Net charge power and target must exceed auxiliary draw and reserve")

    def _session(self, service, vehicle):
        runtime = service.vehicle_runtime[vehicle]
        if runtime.pose_source == "synthetic":
            return "synthetic-" + service.run_id
        pose = service.ego_localizations.get(vehicle)
        return pose.session_id if pose is not None else None

    def _fresh(self, service, visit):
        return (service.energy is not None and service.energy._known(service, visit.vehicle)
                and self._session(service, visit.vehicle) == visit.session
                and not service.ego_localization_is_stale(visit.vehicle))

    def _distance(self, service, vehicle, station):
        runtime = service.vehicle_runtime[vehicle]
        node = next(n for n in service.graph.nodes if n.id == station.node_id)
        return math.hypot(runtime.x - node.position_m.x, runtime.y - node.position_m.y)

    def _state(self, service, visit, state):
        if visit.state != state:
            visit.state = state
            self.events.append({"time_s": service.now_s(), "vehicle": visit.vehicle,
                                "station": visit.station, "state": state})

    def request(self, service, vehicle, station):
        self.validate(service)
        if vehicle not in service.vehicle_runtime or station not in self.stations:
            raise ValueError("Unknown charger or vehicle")
        if vehicle in self.visits:
            if self.visits[vehicle].station == station:
                return  # Idempotent repeat does not reset FIFO age.
            raise ValueError("Vehicle already has a charging visit")
        runtime = service.vehicle_runtime[vehicle]
        if (runtime.request_id is not None or runtime.route_id is not None or runtime.speed_mps > .01
                or not service.vehicles[vehicle].available or not service.energy._known(service, vehicle)
                or service.ego_localization_is_stale(vehicle)):
            raise ValueError("Charging queue requires a known, fresh, stopped idle vehicle")
        self.visits[vehicle] = ChargingVisit(vehicle, station, service.now_s(), self._session(service, vehicle))
        service.vehicles[vehicle].available = False
        runtime.mission_state = "TO_CHARGER"
        self.events.append({"time_s": service.now_s(), "vehicle": vehicle, "station": station, "state": "QUEUED"})

    def cancel(self, service, vehicle):
        visit = self.visits[vehicle]
        if self.owners.get(visit.station) == vehicle:
            # Never free an occupied or unobserved dock because of a UI cancellation.
            if (not self._fresh(service, visit) or visit.entered
                    or self._distance(service, vehicle, self.stations[visit.station]) <= self.stations[visit.station].release_radius_m):
                raise ValueError("Reserved dock needs confirmed exit before cancellation")
            self.owners.pop(visit.station)
        self._state(service, visit, "CANCELLED")
        self.visits.pop(vehicle)
        service.vehicles[vehicle].available = True
        service.vehicle_runtime[vehicle].mission_state = "IDLE"

    def _automatic_parked_requests(self, service):
        if self.auto_start_fraction is None:
            return
        for vehicle, runtime in sorted(service.vehicle_runtime.items()):
            if (vehicle in self.visits or runtime.request_id is not None or runtime.route_id is not None
                    or not service.vehicles[vehicle].available or not service.energy._known(service, vehicle)
                    or service.ego_localization_is_stale(vehicle) or runtime.speed_mps > .01):
                continue
            if service.energy.remaining_wh[vehicle] > service.energy.policies[vehicle].capacity_wh * self.auto_start_fraction:
                continue
            at_dock = [s for s in self.stations.values() if self._distance(service, vehicle, s) <= s.arrival_radius_m]
            if at_dock:
                self.request(service, vehicle, min(at_dock, key=lambda s: s.id).id)

    def tick(self, service):
        now = service.now_s()
        if self.last_tick_s is not None and now <= self.last_tick_s:
            if now < self.last_tick_s:
                for visit in self.visits.values():
                    visit.previous_credit_s = visit.outside_since_s = None
            return  # Snapshot reads/retries cannot credit the same elapsed time twice.
        gap = None if self.last_tick_s is None else now - self.last_tick_s
        self.last_tick_s = now
        for visit in self.visits.values():
            if gap is not None and gap > self.stations[visit.station].max_credit_step_s:
                visit.previous_credit_s = visit.outside_since_s = None
        try:
            self.validate(service)
        except ValueError:
            for visit in self.visits.values():
                visit.previous_credit_s = visit.outside_since_s = None
                service.vehicles[visit.vehicle].available = False
                self._state(service, visit, "PAUSED_CONTEXT")
            return
        self._automatic_parked_requests(service)
        # FIFO reservations are allocated before arrival; they grant no route or motion authority.
        for visit in sorted(self.visits.values(), key=lambda v: (v.queued_s, v.vehicle)):
            if visit.vehicle in self.owners.values():
                continue
            if not self._fresh(service, visit):
                self._state(service, visit, "PAUSED_CONTEXT")
                continue
            self._state(service, visit, "QUEUED")
            if visit.station not in self.owners:
                self.owners[visit.station] = visit.vehicle
                self._state(service, visit, "RESERVED")
        for station_id, vehicle in list(self.owners.items()):
            visit, station = self.visits[vehicle], self.stations[station_id]
            runtime = service.vehicle_runtime[vehicle]
            if not self._fresh(service, visit):
                visit.previous_credit_s = visit.outside_since_s = None
                service.vehicles[vehicle].available = False
                self._state(service, visit, "PAUSED_CONTEXT")
                continue
            distance = self._distance(service, vehicle, station)
            if distance <= station.arrival_radius_m:
                visit.entered = True
            if visit.completed:
                self._state(service, visit, "COMPLETE")
                if runtime.request_id is None and runtime.route_id is None:
                    service.vehicles[vehicle].available = True
                    runtime.mission_state = "IDLE"
            if distance > station.release_radius_m and visit.entered:
                visit.previous_credit_s = None
                if visit.outside_since_s is None:
                    visit.outside_since_s = now
                if now - visit.outside_since_s >= station.release_hold_s:
                    self._state(service, visit, "RELEASED")
                    self.owners.pop(station_id)
                    self.visits.pop(vehicle)
                    if runtime.request_id is None and runtime.route_id is None:
                        service.vehicles[vehicle].available = True
                        runtime.mission_state = "IDLE"
                continue
            visit.outside_since_s = None
            if (distance > station.arrival_radius_m or runtime.speed_mps > .01
                    or runtime.request_id is not None or runtime.route_id is not None):
                visit.previous_credit_s = None
                if not visit.completed:
                    self._state(service, visit, "WAITING_AT_DOCK")
                continue
            visit.entered = True
            target = service.energy.policies[vehicle].capacity_wh * station.target_fraction
            if visit.completed:
                continue  # Do not restart charging automatically while waiting to exit.
            runtime.mission_state = "CHARGING"
            previous = visit.previous_credit_s
            visit.previous_credit_s = now
            self._state(service, visit, "CHARGING")
            if previous is not None and 0 < now - previous <= station.max_credit_step_s:
                amount = station.power_w * station.efficiency * (now - previous) / 3600
                accepted = min(amount, max(0, target - service.energy.remaining_wh[vehicle]))
                service.energy.remaining_wh[vehicle] += accepted
                visit.supplied_wh += accepted
            if service.energy.remaining_wh[vehicle] + 1e-9 >= target:
                visit.completed = True
                self._state(service, visit, "COMPLETE")
                visit.previous_credit_s = None
                runtime.mission_state = "IDLE"
                service.vehicles[vehicle].available = True
