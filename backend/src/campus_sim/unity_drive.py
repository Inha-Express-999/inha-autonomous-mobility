"""Opt-in Unity Physics route execution in the existing demo session.

Only ego localization, measured vehicle geometry and Raycast observations enter
this controller. Other objects' actual poses/velocities are never accepted.
The existing time-only passenger demo remains a separate baseline.
"""
from __future__ import annotations

import math
import json
from dataclasses import dataclass, field, replace
from time import monotonic
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, ConfigDict, Field, model_validator

from campus_sim.map_coordinates import MapCoordinates, projection
from campus_sim.planning import AStarPlanner, NoPath, RoadGraph, CostSnapshot
from campus_sim.lanes import LaneProfiles, lane_path, directed_lane_route, shift, unit


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
        "if": {"properties": {"schema_version": {"enum": [2, 3, 4]}}},
        "then": {"required": ["half_width_m"], "properties": {"half_width_m": {"type": "number"}}}
    }, {
        "if": {"required": ["obstacle_detection_enabled"], "properties": {"obstacle_detection_enabled": {"const": False}}},
        "then": {"properties": {"schema_version": {"enum": [3, 4]}, "rays": {"maxItems": 0}}},
        "else": {"properties": {"rays": {"minItems": 7}}}
    }, {
        "if": {"properties": {"schema_version": {"const": 4}}},
        "then": {"required": ["rear_axle_offset_m", "half_length_m"], "properties": {
            "rear_axle_offset_m": {"type": "number"}, "half_length_m": {"type": "number"}}}
    }]})
    schema_version: Literal[1, 2, 3, 4]
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
    obstacle_detection_enabled: bool = True
    rays: list[RayDetection] = Field(max_length=64)
    half_width_m: float | None = Field(default=None, ge=.1, le=3)
    rear_axle_offset_m: float | None = Field(default=None, ge=-10, le=10)
    half_length_m: float | None = Field(default=None, ge=.1, le=15)

    @model_validator(mode="after")
    def lane_geometry_required(self):
        if self.obstacle_detection_enabled and len(self.rays) < 7:
            raise ValueError("Enabled obstacle detection requires at least seven rays")
        if not self.obstacle_detection_enabled and (self.schema_version < 3 or self.rays):
            raise ValueError("Disabled obstacle detection requires protocol 3 and no rays")
        if self.schema_version >= 2 and self.half_width_m is None:
            raise ValueError("Measured body half width required for lane protocol 2")
        if self.schema_version == 4 and (self.rear_axle_offset_m is None or self.half_length_m is None):
            raise ValueError("Protocol 4 requires measured rear axle and body length")
        return self


class AutoMission(StrictModel):
    run_id: str
    command_id: str = Field(min_length=1, max_length=100)
    stop_id: str
    driving_profile: Literal["standard", "campus_fast_no_obstacles"] = "standard"
    cruise_speed_kmh: float = Field(default=40, ge=30, le=50)


class DriveCommand(StrictModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, json_schema_extra={"allOf": [{
        "if": {"properties": {"schema_version": {"enum": [1, 2]}}},
        "then": {"properties": {"target_speed_kmh": {"maximum": 18}}}
    }, {
        "if": {"properties": {"target_speed_kmh": {"exclusiveMaximum": 0}}},
        "then": {"properties": {"schema_version": {"const": 4}, "status": {"const": "MANEUVER"}}}
    }]})
    schema_version: Literal[1, 2, 3, 4]
    run_id: str
    tick: int = Field(ge=0)
    target_speed_kmh: float = Field(ge=-5, le=50)
    steering_angle_rad: float = Field(ge=-1, le=1)
    emergency_stop: bool
    valid_for_s: float = Field(gt=0, le=.3)
    status: str
    reason: str
    lane_error_m: float | None = None
    lane_profile_hash: str | None = None
    transport_phase: Literal["", "TO_PICKUP", "PICKUP_SERVICE", "TO_DROPOFF", "DROPOFF_SERVICE", "RETURN_PENDING", "RETURNING"] = ""

    @model_validator(mode="after")
    def legacy_speed_limit(self):
        if self.schema_version < 3 and self.target_speed_kmh > 18:
            raise ValueError("High-speed simulation requires protocol 3")
        if self.target_speed_kmh < 0 and (self.schema_version != 4 or self.status != "MANEUVER"):
            raise ValueError("Reverse requires protocol 4 and an explicit manoeuvre")
        return self


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
    driving_profile: str = "standard"
    cruise_speed_mps: float = 5.
    turn: object | None = None


class UnityDriveSession:
    """Must be called under DemoSession.lock; freshness uses wall time, never demo speed."""
    max_speed_mps = 5.  # 18km/h simulation tuning, not measured vehicle capability.
    legacy_speed_mps = .8
    lateral_acceleration_mps2 = 1.2
    fast_lateral_acceleration_mps2 = 3.6  # Simulation tuning; validate against WheelCollider traces.
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
        service.external_vehicle_ids.add(vehicle_id)
        for charge in service.charges:
            if charge.vehicle_id == vehicle_id and not charge.completed:
                charge.ends_at_s = service._time_s
                charge.completed = charge.interrupted = True
        return token

    def disconnect(self, vehicle_id, token):
        vehicle = self.vehicles.get(vehicle_id)
        if vehicle and vehicle.token == token:
            vehicle.connected = False
            vehicle.status = "DISCONNECTED"
            vehicle.reason = "Physics connection lost; vehicle command watchdog brakes"

    def preview_mission(self, vehicle_id, command: AutoMission, *, origin=None):
        """Plan without moving the vehicle, replacing its mission, or resuming time.

        A supplied origin is a predicted endpoint for multi-leg feasibility only;
        the live ego and ownership state remain untouched under the session lock.
        """
        live = self.vehicles[vehicle_id]
        predicted = replace(live, ego=origin or live.ego, last_command_id=None, turn=None)
        self.vehicles[vehicle_id] = predicted
        try:
            self.mission(vehicle_id, command, _preview=True)
            return predicted
        finally:
            self.vehicles[vehicle_id] = live

    def _resume_clock(self):
        self.session.clock.read()
        self.session.clock.speed = 1.
        self.session.clock.paused = False

    def mission(self, vehicle_id, command: AutoMission, *, _allow_turn=True, _preview=False):
        vehicle = self.vehicles.get(vehicle_id)
        if command.run_id != self.run_id or self.run_id != self.session.run_id:
            raise ValueError("Session changed")
        if not vehicle or not vehicle.connected or vehicle.ego is None or (not _preview and self.clock() - vehicle.received_s > self.stale_s):
            raise ValueError("Fresh Unity localization required")
        if vehicle.last_command_id == command.command_id:
            return self.describe(vehicle_id)
        service = self.session.service
        own_home = command.stop_id == 'home:' + vehicle_id and vehicle_id in service.home_nodes
        if not own_home and command.stop_id not in service.stops:
            raise ValueError("Unknown Stop candidate")
        if abs(vehicle.ego.speed_mps) > .2:
            raise ValueError("Stop before replacing an automatic mission")
        if command.driving_profile != "standard" and vehicle.ego.schema_version < 3:
            raise ValueError("High-speed simulation requires protocol 3")
        goal = service.home_nodes[vehicle_id] if own_home else service.stop_nodes[command.stop_id]
        origin = (vehicle.ego.x, vehicle.ego.z)
        using_lanes = vehicle.ego.schema_version >= 2
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
                if using_lanes:
                    following = directed_lane_route(graph, costs, edge, goal, self.lanes, self.coordinates,
                                                    vehicle.ego.wheelbase_m / math.tan(vehicle.ego.max_steering_rad))
                    if following is None:
                        continue
                    route_length = sum(e.length_m for e in following)
                    continuation = [self.coordinates.to_unity(*p) for e in following for p in e.polyline[1:]]
                else:
                    route = AStarPlanner().search(graph, edge.v, goal, costs)
                    if isinstance(route, NoPath):
                        continue
                    following = [edges_by_id[identifier] for identifier in route.edge_ids]
                    route_length = route.length_m
                    continuation = [self.coordinates.to_unity(*p) for p in route.polyline[1:]]
                remainder = [point, *points[i + 1:]]
                lane_edges = [edge, *following]
                route_points = [origin, *remainder, *continuation]
                if using_lanes:
                    parts = [(remainder, profile["right_offset_m"])]
                    parts.extend(([self.coordinates.to_unity(*p) for p in e.polyline], self.lanes.by_edge[e.id]["right_offset_m"])
                                 for e in lane_edges[1:])
                    try:
                        route_points = [origin, *lane_path(parts, vehicle.ego.wheelbase_m / math.tan(vehicle.ego.max_steering_rad))]
                    except ValueError:
                        continue
                candidates.append((distance, sum(math.dist(x, y) for x, y in zip(remainder, remainder[1:])) + route_length,
                                   route_points, lane_edges))
        if not candidates:
            if _allow_turn:
                from campus_sim.physical_turn import prepare_turn
                if prepare_turn(self, vehicle_id, command):
                    if not _preview:
                        self._resume_clock()
                    return self.describe(vehicle_id)
            raise ValueError("No aligned road within 2m, or no directed route; vehicle retained in place")
        _, _, points, lane_edges = min(candidates, key=lambda c: (round(c[0], 3), c[1]))
        vehicle.route_unity = [p for i, p in enumerate(points) if i == 0 or math.dist(p, points[i - 1]) > .001]
        vehicle.route_xy = [self.coordinates.from_unity(*p) for p in vehicle.route_unity]
        vehicle.goal_unity = vehicle.route_unity[-1]
        vehicle.lane_ids = [self.lanes.by_edge[e.id]["lane_id"] for e in lane_edges] if using_lanes else []
        vehicle.lane_width_m = min(self.lanes.by_edge[e.id]["lane_width_m"] for e in lane_edges) if using_lanes else None
        vehicle.road_speed_mps = min(e.speed_mps for e in lane_edges)
        vehicle.driving_profile = command.driving_profile
        vehicle.cruise_speed_mps = command.cruise_speed_kmh / 3.6 if command.driving_profile != "standard" else self.max_speed_mps
        vehicle.sensor_blocked = False
        vehicle.turn = None
        vehicle.destination = service.home_hubs[vehicle_id]['id'] if own_home else service.stops[command.stop_id].landmark_id
        vehicle.progress_m = 0
        vehicle.mission_id = uuid4().hex
        vehicle.last_command_id = command.command_id
        vehicle.status, vehicle.reason = "DRIVING", "A* route / Unity Physics"
        if _allow_turn:
            from campus_sim.physical_turn import prefer_shorter_turn
            prefer_shorter_turn(self, vehicle_id, command)
        if not _preview:
            self._resume_clock()
        return self.describe(vehicle_id)

    def observation(self, vehicle_id, token, ego: EgoObservation):
        vehicle = self.vehicles[vehicle_id]
        if self.run_id != self.session.run_id or token != vehicle.token or ego.run_id != self.run_id or ego.geometry_hash != self.geometry_hash:
            raise ValueError("Physics owner, run or map mismatch")
        if vehicle.ego is not None and ego.schema_version != vehicle.ego.schema_version:
            raise ValueError("Reconnect before changing physics protocol")
        if ego.tick <= vehicle.tick:
            raise ValueError("Out-of-order observation")
        previous_ego = vehicle.ego
        vehicle.tick, vehicle.ego, vehicle.received_s = ego.tick, ego, self.clock()
        response = dict(schema_version=ego.schema_version, run_id=self.session.run_id, tick=ego.tick,
                        target_speed_kmh=0., steering_angle_rad=0., emergency_stop=True,
                        valid_for_s=.3, status=vehicle.status, reason=vehicle.reason)
        if ego.schema_version == 4:
            dispatcher = getattr(self.session, "physical_dispatch", None)
            response["transport_phase"] = next((job.phase for job in dispatcher.jobs.values()
                if job.vehicle_id == vehicle_id and not job.returned), "") if dispatcher else ""

        def stopped(reason, status="YIELDING"):
            vehicle.reason, vehicle.status = reason, status
            response.update(reason=reason, status=status)
            return response

        if not ego.sensor_valid or ego.sensor_age_s > .2:
            return stopped("Invalid/stale Raycast observation")
        if not ego.obstacle_detection_enabled and vehicle.driving_profile != "campus_fast_no_obstacles" and vehicle.route_unity:
            return stopped("Obstacle detection disabled; select path-only simulation profile")
        if self.session.clock.paused and vehicle.route_unity:
            return stopped("Simulation paused")
        if not vehicle.route_unity or vehicle.status in {"ARRIVED", "OFF_ROUTE"}:
            return response
        if vehicle.turn is not None:
            turn_command = vehicle.turn.update(ego)
            if turn_command['status'] == 'DONE':
                vehicle.turn = None
                return stopped('Turn complete; resume onward route', 'DRIVING')
            response.update(turn_command)
            vehicle.status, vehicle.reason = turn_command['status'], turn_command['reason']
            return response
        position = (ego.x, ego.z)
        goal_distance = math.dist(position, vehicle.route_unity[-1])
        if goal_distance <= .65:
            return stopped("Reached Stop candidate" if abs(ego.speed_mps) < .05 else "Braking at goal",
                           "ARRIVED" if abs(ego.speed_mps) < .05 else "DRIVING")
        length = 0
        nearest = []
        # A route may revisit an intersection. Spatial proximity alone must not
        # jump to a later lap; allow only locally reachable forward progress.
        displacement = math.dist((previous_ego.x, previous_ego.z), position) if previous_ego else 0.
        progress_limit = vehicle.progress_m + max(5., 2 * displacement + 1.)
        for a, b in zip(vehicle.route_unity, vehicle.route_unity[1:]):
            segment = math.dist(a, b)
            distance, ratio, _ = projection(position, a, b)
            along = length + segment * ratio
            if along + 1 >= vehicle.progress_m and along <= progress_limit:
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
        fast_simulation = vehicle.driving_profile == "campus_fast_no_obstacles"
        lateral_limit = self.fast_lateral_acceleration_mps2 if fast_simulation else self.lateral_acceleration_mps2
        cruise = vehicle.cruise_speed_mps if ego.schema_version >= 2 else self.legacy_speed_mps
        curve_speed = cruise
        accumulated = 0.
        curved = False
        # Keep the preview floor independent of braking speed to avoid cruise/curve oscillation.
        preview = max(cruise * 2.5, abs(ego.speed_mps) * 2.5, ego.speed_mps ** 2 / 4 + 5)
        for a, b, c in zip(vehicle.route_unity, vehicle.route_unity[1:], vehicle.route_unity[2:]):
            accumulated += math.dist(a, b)
            if accumulated < vehicle.progress_m - 1 or accumulated > vehicle.progress_m + preview:
                continue
            denominator = math.dist(a, b) * math.dist(b, c) * math.dist(a, c)
            if denominator <= 1e-6:
                continue
            curvature = 2 * abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])) / denominator
            if curvature > (.001 if fast_simulation else .03):
                curved = True
                corner_speed = math.sqrt(lateral_limit / curvature)
                distance_to_corner = max(0., accumulated - vehicle.progress_m - 3.)
                curve_speed = min(curve_speed, math.sqrt(corner_speed ** 2 + 4 * distance_to_corner) if fast_simulation else corner_speed)
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
        if fast_simulation:
            # Geometry can be straight ahead while the vehicle is still turning
            # out of a junction. Apply the same lateral-acceleration bound to
            # the commanded steering, not only to upcoming route vertices.
            steering_curvature = abs(math.tan(steering)) / ego.wheelbase_m
            if steering_curvature > 1e-6:
                curve_speed = min(curve_speed, math.sqrt(
                    lateral_limit / steering_curvature))
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
        if not fast_simulation and clearance <= max(1., stopping_distance) and not vehicle.sensor_blocked:
            vehicle.sensor_blocked = True
            vehicle.blocked_release_m = max(1., stopping_distance, clearance) + .4
        if not fast_simulation and vehicle.sensor_blocked and clearance <= vehicle.blocked_release_m:
            return stopped("Raycast stopping-distance constraint")
        vehicle.sensor_blocked = False
        speed = min(curve_speed, cruise if fast_simulation else vehicle.road_speed_mps,
                    math.sqrt(max(0., 2 * (remaining - .5))),
                    math.inf if fast_simulation else max(0., clearance - stopping_distance))
        vehicle.status, vehicle.reason = "DRIVING", ("A* / simulation cruise / obstacles ignored" if fast_simulation else "A* / pure pursuit / Raycast safety")
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
                    driving_profile=v.driving_profile, cruise_speed_kmh=v.cruise_speed_mps * 3.6,
                    obstacles_ignored=v.driving_profile == "campus_fast_no_obstacles",
                    measured_speed_kmh=v.ego.speed_mps * 3.6 if v.ego and not stale else None,
                    observation_fresh=not stale and v.ego is not None,
                    goal_unity=dict(x=v.goal_unity[0], y=0., z=v.goal_unity[1]) if v.goal_unity else None,
                    lane_ids=v.lane_ids, lane_width_m=v.lane_width_m, lane_error_m=v.lane_error_m,
                    lane_profile_hash=self.lanes.hash if v.lane_ids else None,
                    lane_model="right_hand_authored_simulation" if v.lane_ids else "legacy_centerline")
