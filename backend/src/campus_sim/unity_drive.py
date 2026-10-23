"""Opt-in Unity Physics route execution in the existing demo session.

Only ego localization, measured vehicle geometry and Raycast observations enter
this controller. Other objects' actual poses/velocities are never accepted.
The existing time-only passenger demo remains a separate baseline.
"""
from __future__ import annotations

import math
import json
from dataclasses import dataclass, field
from time import monotonic
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, ConfigDict, Field, model_validator

from campus_sim.map_coordinates import MapCoordinates, projection
from campus_sim.planning import AStarPlanner, NoPath, RoadGraph, CostSnapshot
from campus_sim.lanes import LaneProfiles, lane_path, shift, unit


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)


class RayDetection(StrictModel):
    range_m: float = Field(ge=0, le=30)
    bearing_rad: float = Field(ge=-math.pi / 2, le=math.pi / 2)
    lateral_m: float | None = Field(default=None, ge=-35, le=35)
    forward_m: float | None = Field(default=None, ge=-10, le=40)
    origin_forward_m: float | None = Field(default=None, ge=0, le=10)

    @model_validator(mode="after")
    def complete_hit_position(self):
        supplied = (self.lateral_m is not None, self.forward_m is not None, self.origin_forward_m is not None)
        if any(supplied) and not all(supplied):
            raise ValueError("Supply all ego-local sensor hit fields together")
        return self


class EgoObservation(StrictModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, json_schema_extra={"allOf": [{
        "if": {"properties": {"schema_version": {"const": 2}}},
        "then": {"required": ["half_width_m"], "properties": {"half_width_m": {"type": "number"}}}
    }]})
    schema_version: Literal[1, 2]
    run_id: str
    geometry_hash: str = Field(min_length=64, max_length=64)
    tick: int = Field(ge=0, strict=True)
    x: float
    z: float
    heading_rad: float
    speed_mps: float = Field(ge=-15, le=30)
    wheelbase_m: float = Field(ge=.3, le=10)
    max_steering_rad: float = Field(gt=0, le=1)
    sensor_valid: bool
    sensor_age_s: float = Field(ge=0, le=10)
    rays: list[RayDetection] = Field(min_length=7, max_length=64)
    half_width_m: float | None = Field(default=None, ge=.1, le=3)

    @model_validator(mode="after")
    def lane_geometry_required(self):
        if self.schema_version == 2 and self.half_width_m is None:
            raise ValueError("Measured body half width required for lane protocol 2")
        return self


class AutoMission(StrictModel):
    run_id: str
    command_id: str = Field(min_length=1, max_length=100)
    stop_id: str


class DriveCommand(StrictModel):
    schema_version: Literal[1, 2]
    run_id: str
    tick: int = Field(ge=0)
    target_speed_kmh: float = Field(ge=0, le=18)
    steering_angle_rad: float = Field(ge=-1, le=1)
    emergency_stop: bool
    valid_for_s: float = Field(gt=0, le=.3)
    status: str
    reason: str
    lane_error_m: float | None = None
    lane_profile_hash: str | None = None


@dataclass
class PhysicalVehicle:
    token: str
    connected: bool = True
    received_s: float = 0
    tick: int = -1
    ego: EgoObservation | None = None
    status: str = "PARKED"
    destination: str | None = None
    route_xy: list = field(default_factory=list)
    route_unity: list = field(default_factory=list)
    progress_m: float = 0
    mission_id: str | None = None
    last_command_id: str | None = None
    reason: str = "No automatic mission"
    sensor_blocked: bool = False
    blocked_release_m: float = 0
    road_speed_mps: float = .8
    lane_width_m: float | None = None
    lane_ids: list = field(default_factory=list)
    lane_error_m: float = 0
    goal_unity: tuple | None = None


class UnityDriveSession:
    """Must be called under DemoSession.lock; freshness uses wall time, never demo speed."""
    max_speed_mps = 5.  # 18km/h simulation tuning, not measured vehicle capability.
    legacy_speed_mps = .8
    lateral_acceleration_mps2 = 1.2
    stale_s = .4

    def __init__(self, session, geometry_hash, clock=monotonic):
        self.session = session
        self.run_id = session.run_id
        self.geometry_hash = geometry_hash
        self.clock = clock
        self.coordinates = MapCoordinates()
        self.vehicles: dict[str, PhysicalVehicle] = {}
        path = session.root / "maps/inha_osm_runtime/lane_profiles.json"
        try:
            self.lanes = LaneProfiles(json.loads(path.read_text(encoding="utf-8")), geometry_hash) if path.exists() else None
        except ValueError:
            self.lanes = None  # Other synthetic baseline maps remain usable; lane missions fail closed.

    def connect(self, vehicle_id):
        service = self.session.service
        if vehicle_id not in service.vehicles:
            raise ValueError("Unknown vehicle")
        previous = self.vehicles.get(vehicle_id)
        if previous and previous.connected and self.clock() - previous.received_s < 2:
            raise ValueError("Vehicle already has a live physics owner")
        if any(t.vehicle_id == vehicle_id and not t.returned for t in service.trips.values()):
            raise ValueError("Vehicle has a virtual trip; reset the demo before attaching Physics")
        token = uuid4().hex
        self.vehicles[vehicle_id] = PhysicalVehicle(token, received_s=self.clock())
        # Exclude the physically owned vehicle from the time-only dispatch baseline.
        service.vehicles[vehicle_id].available = False
        return token

    def disconnect(self, vehicle_id, token):
        vehicle = self.vehicles.get(vehicle_id)
        if vehicle and vehicle.token == token:
            vehicle.connected = False
            vehicle.status = "DISCONNECTED"
            vehicle.reason = "Physics connection lost; vehicle command watchdog brakes"

    def mission(self, vehicle_id, command: AutoMission):
        vehicle = self.vehicles.get(vehicle_id)
        if command.run_id != self.run_id or self.run_id != self.session.run_id:
            raise ValueError("Session changed")
        if not vehicle or not vehicle.connected or vehicle.ego is None or self.clock() - vehicle.received_s > self.stale_s:
            raise ValueError("Fresh Unity localization required")
        if vehicle.last_command_id == command.command_id:
            return self.describe(vehicle_id)
        service = self.session.service
        if command.stop_id not in service.stops:
            raise ValueError("Unknown Stop candidate")
        if abs(vehicle.ego.speed_mps) > .2:
            raise ValueError("Stop before replacing an automatic mission")
        goal = service.stop_nodes[command.stop_id]
        origin = (vehicle.ego.x, vehicle.ego.z)
        using_lanes = vehicle.ego.schema_version == 2
        if using_lanes and self.lanes is None:
            raise ValueError("Pinned lane profiles unavailable; keep vehicle stopped")
        graph = service.graph
        if using_lanes:
            graph = RoadGraph(graph.map_version, graph.nodes,
                              tuple(e for e in graph.edges if self.lanes.feasible(e, vehicle.ego.half_width_m)), graph.synthetic)
        edges_by_id = {e.id: e for e in graph.edges}
        costs = CostSnapshot(service.costs.id + "-lanes", graph.map_version,
                             {identifier: service.costs.costs_s[identifier] for identifier in edges_by_id}) if using_lanes else service.costs
        # Start on an actual directed edge, preserving the remaining polyline.
        candidates = []
        for edge in graph.edges:
            if edge.id not in service.costs.costs_s or edge.stairs or not edge.vehicle_allowed:
                continue
            points = [self.coordinates.to_unity(*xy) for xy in edge.polyline]
            for i, (a, b) in enumerate(zip(points, points[1:])):
                distance, ratio, point = projection(origin, a, b)
                profile = self.lanes.by_edge[edge.id] if using_lanes else None
                if using_lanes:
                    direction = unit(a, b)
                    distance, ratio, _ = projection(origin, shift(a, direction, profile["right_offset_m"]),
                                                    shift(b, direction, profile["right_offset_m"]))
                    point = tuple(a[j] + ratio * (b[j] - a[j]) for j in (0, 1))
                heading = math.atan2(b[0] - a[0], b[1] - a[1])
                if distance > 2 or math.cos(heading - vehicle.ego.heading_rad) < .5:
                    continue
                route = AStarPlanner().search(graph, edge.v, goal, costs)
                if isinstance(route, NoPath):
                    continue
                remainder = [point, *points[i + 1:]]
                lane_edges = [edge, *[edges_by_id[identifier] for identifier in route.edge_ids]]
                route_points = [origin, *remainder, *[self.coordinates.to_unity(*p) for p in route.polyline[1:]]]
                if using_lanes:
                    parts = [(remainder, profile["right_offset_m"])]
                    parts.extend(([self.coordinates.to_unity(*p) for p in e.polyline], self.lanes.by_edge[e.id]["right_offset_m"])
                                 for e in lane_edges[1:])
                    try:
                        route_points = [origin, *lane_path(parts, vehicle.ego.wheelbase_m / math.tan(vehicle.ego.max_steering_rad))]
                    except ValueError:
                        continue
                candidates.append((distance, sum(math.dist(x, y) for x, y in zip(remainder, remainder[1:])) + route.length_m,
                                   route_points, lane_edges))
        if not candidates:
            raise ValueError("No aligned road within 2m, or no directed route; vehicle retained in place")
        _, _, points, lane_edges = min(candidates, key=lambda c: (round(c[0], 3), c[1]))
        vehicle.route_unity = [p for i, p in enumerate(points) if i == 0 or math.dist(p, points[i - 1]) > .001]
        vehicle.route_xy = [self.coordinates.from_unity(*p) for p in vehicle.route_unity]
        vehicle.goal_unity = vehicle.route_unity[-1]
        vehicle.lane_ids = [self.lanes.by_edge[e.id]["lane_id"] for e in lane_edges] if using_lanes else []
        vehicle.lane_width_m = min(self.lanes.by_edge[e.id]["lane_width_m"] for e in lane_edges) if using_lanes else None
        vehicle.road_speed_mps = min(e.speed_mps for e in lane_edges)
        vehicle.sensor_blocked = False
        vehicle.destination = service.stops[command.stop_id].landmark_id
        vehicle.progress_m = 0
        vehicle.mission_id = uuid4().hex
        vehicle.last_command_id = command.command_id
        vehicle.status, vehicle.reason = "DRIVING", "A* route / Unity Physics"
        self.session.clock.read()
        self.session.clock.speed = 1.
        self.session.clock.paused = False
        return self.describe(vehicle_id)

    def observation(self, vehicle_id, token, ego: EgoObservation):
        vehicle = self.vehicles[vehicle_id]
        if self.run_id != self.session.run_id or token != vehicle.token or ego.run_id != self.run_id or ego.geometry_hash != self.geometry_hash:
            raise ValueError("Physics owner, run or map mismatch")
        if vehicle.ego is not None and ego.schema_version != vehicle.ego.schema_version:
            raise ValueError("Reconnect before changing physics protocol")
        if ego.tick <= vehicle.tick:
            raise ValueError("Out-of-order observation")
        vehicle.tick, vehicle.ego, vehicle.received_s = ego.tick, ego, self.clock()
        response = dict(schema_version=ego.schema_version, run_id=self.session.run_id, tick=ego.tick,
                        target_speed_kmh=0., steering_angle_rad=0., emergency_stop=True,
                        valid_for_s=.3, status=vehicle.status, reason=vehicle.reason)

        def stopped(reason, status="YIELDING"):
            vehicle.reason, vehicle.status = reason, status
            response.update(reason=reason, status=status)
            return response

        if not ego.sensor_valid or ego.sensor_age_s > .2:
            return stopped("Invalid/stale Raycast observation")
        if self.session.clock.paused and vehicle.route_unity:
            return stopped("Simulation paused")
        if not vehicle.route_unity or vehicle.status in {"ARRIVED", "OFF_ROUTE"}:
            return response
        position = (ego.x, ego.z)
        goal_distance = math.dist(position, vehicle.route_unity[-1])
        if goal_distance <= .65:
            return stopped("Reached Stop candidate" if abs(ego.speed_mps) < .05 else "Braking at goal",
                           "ARRIVED" if abs(ego.speed_mps) < .05 else "DRIVING")
        length = 0
        nearest = []
        for a, b in zip(vehicle.route_unity, vehicle.route_unity[1:]):
            segment = math.dist(a, b)
            distance, ratio, _ = projection(position, a, b)
            along = length + segment * ratio
            if along + 1 >= vehicle.progress_m:
                nearest.append((distance, along))
            length += segment
        if not nearest:
            return stopped("Route progress unavailable", "OFF_ROUTE")
        cross_track, progress = min(nearest)
        vehicle.lane_error_m = cross_track
        response.update(lane_error_m=cross_track, lane_profile_hash=self.lanes.hash if vehicle.lane_ids else None)
        allowed_error = vehicle.lane_width_m / 2 - ego.half_width_m - .2 if vehicle.lane_width_m is not None else 3
        if cross_track > allowed_error:
            return stopped("Outside lane/vehicle-width corridor; no teleport/recovery", "OFF_ROUTE")
        vehicle.progress_m = max(vehicle.progress_m, progress)
        curve_speed = self.max_speed_mps if ego.schema_version == 2 else self.legacy_speed_mps
        accumulated = 0.
        curved = False
        # Keep the preview floor independent of braking speed to avoid cruise/curve oscillation.
        preview = max(self.max_speed_mps * 2.5, abs(ego.speed_mps) * 2.5)
        for a, b, c in zip(vehicle.route_unity, vehicle.route_unity[1:], vehicle.route_unity[2:]):
            accumulated += math.dist(a, b)
            if accumulated < vehicle.progress_m - 1 or accumulated > vehicle.progress_m + preview:
                continue
            denominator = math.dist(a, b) * math.dist(b, c) * math.dist(a, c)
            if denominator <= 1e-6:
                continue
            curvature = 2 * abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])) / denominator
            if curvature > .03:
                curved = True
                curve_speed = min(curve_speed, math.sqrt(self.lateral_acceleration_mps2 / curvature))
        lookahead = 2.5 if curved else max(3., min(8., 2 + abs(ego.speed_mps) * .9))
        remaining = max(0., length - vehicle.progress_m)
        target_distance = min(length, vehicle.progress_m + lookahead)
        accumulated = 0
        target = vehicle.route_unity[-1]
        for a, b in zip(vehicle.route_unity, vehicle.route_unity[1:]):
            segment = math.dist(a, b)
            if accumulated + segment >= target_distance and segment > 0:
                ratio = (target_distance - accumulated) / segment
                target = tuple(a[j] + ratio * (b[j] - a[j]) for j in (0, 1))
                break
            accumulated += segment
        dx, dz = target[0] - ego.x, target[1] - ego.z
        forward = dx * math.sin(ego.heading_rad) + dz * math.cos(ego.heading_rad)
        lateral = dx * math.cos(ego.heading_rad) - dz * math.sin(ego.heading_rad)
        if forward < -.2:
            return stopped("Route target behind vehicle; reverse manoeuvre unsupported", "OFF_ROUTE")
        steering = math.atan2(2 * ego.wheelbase_m * lateral, max(dx * dx + dz * dz, .1))
        steering = max(-ego.max_steering_rad, min(ego.max_steering_rad, steering))
        # Sensor-only local safety. No object ground truth enters this calculation.
        clearances = []
        for ray in ego.rays:
            if ray.lateral_m is None or not vehicle.lane_ids:
                clearances.append(ray.range_m)  # Conservative legacy observation.
                continue
            hit = (ego.x + ray.lateral_m * math.cos(ego.heading_rad) + ray.forward_m * math.sin(ego.heading_rad),
                   ego.z - ray.lateral_m * math.sin(ego.heading_rad) + ray.forward_m * math.cos(ego.heading_rad))
            walked = 0.
            for a, b in zip(vehicle.route_unity, vehicle.route_unity[1:]):
                segment = math.dist(a, b)
                distance, ratio, _ = projection(hit, a, b)
                along = walked + segment * ratio
                if along >= vehicle.progress_m and distance <= ego.half_width_m + .2:
                    clearances.append(max(0., min(ray.range_m, along - vehicle.progress_m - ray.origin_forward_m)))
                walked += segment
        clearance = min(clearances, default=20.)
        stopping_distance = .5 + abs(ego.speed_mps) * .3 + ego.speed_mps ** 2 / 4
        if clearance <= max(1., stopping_distance) and not vehicle.sensor_blocked:
            vehicle.sensor_blocked = True
            vehicle.blocked_release_m = max(1., stopping_distance, clearance) + .4
        if vehicle.sensor_blocked and clearance <= vehicle.blocked_release_m:
            return stopped("Raycast stopping-distance constraint")
        vehicle.sensor_blocked = False
        speed = min(curve_speed, vehicle.road_speed_mps, math.sqrt(max(0., 2 * (remaining - .5))), max(0., clearance - stopping_distance))
        vehicle.status, vehicle.reason = "DRIVING", "A* / pure pursuit / Raycast safety"
        response.update(target_speed_kmh=speed * 3.6, steering_angle_rad=steering, emergency_stop=False,
                        status=vehicle.status, reason=vehicle.reason)
        return response

    def describe(self, vehicle_id):
        v = self.vehicles[vehicle_id]
        stale = not v.connected or self.clock() - v.received_s > self.stale_s
        return dict(vehicle_id=vehicle_id, status="DISCONNECTED" if stale else v.status,
                    mission_id=v.mission_id, destination=v.destination, reason=v.reason,
                    position=self.coordinates.from_unity(v.ego.x, v.ego.z) if v.ego else None,
                    routes=[dict(phase="TO_DROPOFF", points=v.route_xy)] if v.route_xy else [],
                    motion_source="unity_physics", battery_measured=False,
                    goal_unity=dict(x=v.goal_unity[0], y=0., z=v.goal_unity[1]) if v.goal_unity else None,
                    lane_ids=v.lane_ids, lane_width_m=v.lane_width_m, lane_error_m=v.lane_error_m,
                    lane_profile_hash=self.lanes.hash if v.lane_ids else None,
                    lane_model="right_hand_authored_simulation" if v.lane_ids else "legacy_centerline")
