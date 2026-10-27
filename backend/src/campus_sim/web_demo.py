"""Local OSM demonstration: server-owned clock, commands and map presentation.

Uses the existing MobilityService without changing its transport contracts.
No map tiles, CDN, or external routing service are needed at runtime.
"""

from __future__ import annotations

import math
import xml.etree.ElementTree as ET
from collections.abc import Callable
from dataclasses import asdict
from itertools import pairwise
from pathlib import Path
from threading import RLock
from time import monotonic
from typing import Literal
from uuid import uuid4

from pydantic import BaseModel, ConfigDict, Field

from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.map_coordinates import MapCoordinates
from campus_sim.planning import AStarPlanner, DijkstraPlanner, NoPath
from campus_sim.service import MobilityService
from campus_sim.unity_map import unity_map
from campus_sim.unity_drive import UnityDriveSession
from campus_sim.physical_dispatch import PhysicalDispatcher


class DemoControl(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, strict=True)
    speed: float | None = Field(default=None, ge=1, le=300)
    paused: bool | None = None
    planner: Literal["astar", "dijkstra"] | None = None
    dispatch: Literal["fifo", "greedy", "hungarian"] | None = None


class DemoPlan(BaseModel):
    model_config = ConfigDict(extra="forbid")
    pickup: str = Field(min_length=1)
    dropoff: str = Field(min_length=1)


class DemoScenario(BaseModel):
    model_config = ConfigDict(extra="forbid")
    name: str = Field(pattern="^(extensions|battery)$")


class SimulationClock:
    def __init__(self, wall_clock=monotonic):
        self.wall_clock = wall_clock
        self.last_wall = wall_clock()
        self.value = 0.0
        self.speed = 10.0
        self.paused = True

    def read(self):
        wall = self.wall_clock()
        elapsed = max(0.0, wall - self.last_wall)
        self.last_wall = wall
        if not self.paused:
            self.value += elapsed * self.speed
        return self.value


def point_along(polyline, fraction):
    distances = [math.dist(a, b) for a, b in pairwise(polyline)]
    remaining = sum(distances) * max(0, min(1, fraction))
    for (a, b), length in zip(pairwise(polyline), distances):
        if remaining <= length and length > 0:
            f = remaining / length
            return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f]
        remaining -= length
    return list(polyline[-1])


def route_position(route, elapsed, service):
    """Respect individual edge costs, including different speed caps."""
    edges = {edge.id: edge for edge in service.graph.edges}
    for identifier in route.edge_ids:
        duration = service.costs.costs_s[identifier]
        if elapsed < duration:
            return point_along(edges[identifier].polyline, elapsed / duration)
        elapsed -= duration
    return list(route.polyline[-1])


def map_presentation(service, root):
    graph = service.graph
    xs, ys = zip(*graph.nodes.values())
    bounds = [min(xs) - 90, min(ys) - 90, max(xs) + 90, max(ys) + 90]
    coordinates = MapCoordinates()
    osm_path = root / "Docs/MapResearch/2026-09-17/expanded_campus.osm"
    osm = ET.parse(osm_path).getroot()
    nodes = {
        node.attrib["id"]: coordinates.from_wgs84(
            float(node.attrib["lon"]), float(node.attrib["lat"])
        )
        for node in osm.findall("node")
    }
    buildings, context_roads, areas = [], [], []
    for way in osm.findall("way"):
        tags = {tag.attrib["k"]: tag.attrib["v"] for tag in way.findall("tag")}
        points = [nodes[n.attrib["ref"]] for n in way.findall("nd") if n.attrib["ref"] in nodes]
        if len(points) < 2 or not any(
            bounds[0] <= p[0] <= bounds[2] and bounds[1] <= p[1] <= bounds[3] for p in points
        ):
            continue
        item = {"id": way.attrib["id"], "points": points, "name": tags.get("name", "")}
        if "building" in tags and tags["building"] != "no" and points[0] == points[-1]:
            buildings.append(item)
        elif "highway" in tags:
            context_roads.append(item)
        elif points[0] == points[-1] and (
            way.attrib["id"] == "568420971" or tags.get("name") == "대운동장"
        ):
            areas.append(
                {**item, "kind": "parking" if tags.get("amenity") == "parking" else "field"}
            )
    seen, roads = set(), []
    for edge in graph.edges:
        key = tuple(sorted((edge.polyline, tuple(reversed(edge.polyline)))))
        if key not in seen:
            seen.add(key)
            roads.append({"points": edge.polyline, "speed_mps": edge.speed_mps})
    stops = []
    for landmark in service.landmarks.values():
        stop_id = landmark.stop_ids[0]
        point = graph.nodes[service.stop_nodes[stop_id]]
        stops.append({"id": landmark.id, "name": landmark.name, "point": point})
    return {
        "map_version": graph.map_version,
        "bounds": bounds,
        "buildings": buildings,
        "context_roads": context_roads,
        "roads": roads,
        "nodes": [{"id": identifier, "point": point} for identifier, point in graph.nodes.items()],
        "edge_count": len(graph.edges),
        "stops": stops,
        "hubs": [
            {
                **hub,
                "vehicle_ids": [
                    identifier
                    for identifier, home in service.home_hubs.items()
                    if home["id"] == hub["id"]
                ],
            }
            for hub in {home["id"]: home for home in service.home_hubs.values()}.values()
        ],
        "areas": areas,
        "source": "Cached OpenStreetMap geometry + Unity-supported routing corridors",
        "operationally_verified": False,
        "attribution": "© OpenStreetMap contributors · ODbL 1.0",
    }


class DemoSession:
    def __init__(self, factory: Callable = MobilityService.from_osm_map, wall_clock=monotonic):
        self.lock = RLock()
        self.factory = factory
        self.wall_clock = wall_clock
        self.clock = SimulationClock(wall_clock)
        self.service = factory(clock=self.clock.read)
        self.service.route_algorithm = "astar"
        self.run_id = uuid4().hex
        self.root = Path(__file__).resolve().parents[3]
        self.map = map_presentation(self.service, self.root)
        self.physical = UnityDriveSession(self, unity_map(self.service)["geometry_hash"])
        self.map["geometry_hash"] = self.physical.geometry_hash
        self.physical_dispatch = PhysicalDispatcher(self)

    def state(self):
        with self.lock:
            service = self.service
            service.step()
            self.physical_dispatch.step()
            now = service._time_s
            active = {t.vehicle_id: t for t in service.trips.values() if not t.returned}
            vehicles = []
            for vehicle in service.vehicles.values():
                trip = active.get(vehicle.id)
                charge = next(
                    (c for c in service.charges if c.vehicle_id == vehicle.id and not c.completed),
                    None,
                )
                position = list(service.graph.nodes[service.home_nodes[vehicle.id]])
                phase, routes, destination, remaining = "READY", [], None, 0.0
                if charge:
                    phase, remaining = "CHARGING", max(0, charge.ends_at_s - now)
                elif trip:
                    request = service.requests[trip.request_id]
                    destination = request.dropoff_landmark_id
                    route_list = (trip.pickup_route, trip.delivery_route, trip.return_route)
                    elapsed = now - trip.departed_at_s
                    phase = "RETURNING"
                    for candidate, name in zip(
                        route_list, ("TO_PICKUP", "TO_DROPOFF", "RETURNING")
                    ):
                        if elapsed < candidate.cost_s:
                            phase = name
                            position = route_position(candidate, elapsed, service)
                            break
                        elapsed -= candidate.cost_s
                    routes = [
                        {"phase": p, "points": r.polyline}
                        for p, r in zip(("TO_PICKUP", "TO_DROPOFF", "RETURNING"), route_list)
                        if r.edge_ids
                    ]
                    remaining = max(0, trip.returns_at_s - now)
                battery = service.batteries[vehicle.id]
                vehicles.append(
                    {
                        "id": vehicle.id,
                        "home_hub_id": service.home_hubs[vehicle.id]["id"],
                        "home_name": service.home_hubs[vehicle.id]["name"],
                        "position": position,
                        "phase": phase,
                        "remaining_s": remaining,
                        "remaining_wh": battery.remaining_wh,
                        "soc": battery.remaining_wh / service.battery_policy.capacity_wh,
                        "destination": destination,
                        "routes": routes,
                        "request_id": trip.request_id if trip else None,
                        "departed_at_s": trip.departed_at_s if trip else None,
                        "trajectory": [
                            {
                                "points": edge.polyline,
                                "duration_s": service.costs.costs_s[identifier],
                            }
                            for route in (trip.pickup_route, trip.delivery_route, trip.return_route)
                            for identifier in route.edge_ids
                            for edge in [next(e for e in service.graph.edges if e.id == identifier)]
                        ]
                        if trip
                        else [],
                    }
                )
                if vehicle.id in self.physical.vehicles:
                    physics = self.physical.describe(vehicle.id)
                    if physics["position"] is not None:
                        vehicles[-1].update(position=physics["position"], routes=physics["routes"],
                                            phase=("MANEUVER" if physics["status"] == "MANEUVER" else
                                                   "TO_DROPOFF" if physics["status"] == "DRIVING" else "READY"),
                                            destination=physics["destination"], trajectory=[], departed_at_s=None,
                                            mission_id=physics["mission_id"], route_unity=physics["route_unity"],
                                            route_progress_m=physics["route_progress_m"],
                                            physical_status=physics["status"], motion_source="unity_physics",
                                            physical_reason=physics["reason"], battery_measured=False,
                                            driving_profile=physics["driving_profile"],
                                            cruise_speed_kmh=physics["cruise_speed_kmh"],
                                            measured_speed_kmh=physics["measured_speed_kmh"],
                                            observation_fresh=physics["observation_fresh"],
                                            obstacles_ignored=physics["obstacles_ignored"],
                                            lane_model=physics["lane_model"], lane_ids=physics["lane_ids"],
                                            lane_error_m=physics["lane_error_m"], lane_profile_hash=physics["lane_profile_hash"])
                    job=next((j for j in self.physical_dispatch.jobs.values()
                              if j.vehicle_id==vehicle.id and not j.returned),None)
                    if job is not None:
                        vehicles[-1].update(request_id=job.request_id,
                            transport_phase=job.phase,
                            phase="MANEUVER" if physics["status"]=="MANEUVER" else job.phase,
                            execution_source="observed_unity_transport",transport_reason=job.reason)
                    elif vehicle.id in self.physical_dispatch.charging:
                        vehicles[-1].update(phase="CHARGING",remaining_s=max(0.,
                            (service.battery_policy.capacity_wh-battery.remaining_wh)*3600/service.battery_policy.charging_power_w))
            requests = [r.model_dump(mode="json") for r in service.requests.values()]
            queued = [r for r in requests if r["status"] == "QUEUED"]
            return {
                "run_id": self.run_id,
                "geometry_hash": self.physical.geometry_hash,
                "time_s": now,
                "speed": self.clock.speed,
                "paused": self.clock.paused,
                "planner": service.route_algorithm,
                "dispatch": service.dispatch_algorithm,
                "physical_dispatch": service.dispatch_algorithm,
                "physical_dispatch_diagnostics": dict(self.physical_dispatch.diagnostics),
                "map_version": service.graph.map_version,
                "vehicles": vehicles,
                "requests": requests,
                "queue": queued,
                "vehicle_fifo": sorted(
                    (v.id for v in service.vehicles.values() if v.available),
                    key=lambda v: service.vehicle_order.get(v, 0),
                ),
                "waiting_reasons": dict(service.waiting_reasons),
                "completed": sum(r["status"] == "COMPLETED" for r in requests),
                "charging_sessions": [asdict(c) for c in (*service.charges,*self.physical_dispatch.charges)],
                "events": self.events(now),
                "physical_transport_events": list(self.physical_dispatch.events),
                "battery_policy": service.battery_policy.model_dump(),
                "simulation_only": True,
            }

    def advance(self):
        with self.lock:
            self.service.step()
            self.physical_dispatch.step()

    def events(self, now):
        result = []

        def add(at, key, text):
            if at <= now:
                result.append({"id": key, "time_s": at, "text": text})

        for index, request in enumerate(self.service.requests.values(), 1):
            name = self.service.landmarks[request.dropoff_landmark_id].name
            add(request.created_s, f"{request.id}:created", f"요청 {index:02d} 접수 · {name}")
            trip = self.service.trips.get(request.id)
            if trip:
                dispatch_name = {"fifo": "FIFO", "greedy": "Greedy", "hungarian": "Hungarian"}[
                    trip.dispatch_algorithm
                ]
                for at, key, text in (
                    (
                        trip.departed_at_s,
                        "assigned",
                        f"{trip.vehicle_id} → 요청 {index:02d} {dispatch_name} 배차",
                    ),
                    (trip.dropoff_at_s, "delivered", f"{trip.vehicle_id} 목적지 도착 · {name}"),
                    (
                        trip.returns_at_s,
                        "returned",
                        f"{trip.vehicle_id} {self.service.home_hubs[trip.vehicle_id]['name']} 복귀",
                    ),
                ):
                    add(at, f"{request.id}:{key}", text)
            if request.status is RequestStatus.CANCELLED:
                # Cancellation has no stored event time in the existing domain.
                # Its status is displayed in requests; do not invent a timestamp.
                continue
        for index, charge in enumerate(self.service.charges):
            home_name = self.service.home_hubs[charge.vehicle_id]["name"]
            add(
                charge.started_at_s,
                f"charge:{index}:start",
                f"{charge.vehicle_id} {home_name} 충전 시작",
            )
            add(
                charge.ends_at_s,
                f"charge:{index}:end",
                f"{charge.vehicle_id} 충전 중단 · 요청 배차"
                if charge.interrupted
                else f"{charge.vehicle_id} 충전 완료 · 가용 차량 재진입",
            )
        return sorted(result, key=lambda e: e["time_s"])[-40:][::-1]

    def control(self, command: DemoControl):
        with self.lock:
            if command.dispatch is not None:
                self.service.dispatch_algorithm = command.dispatch
                self.physical_dispatch.retry_at = 0.
            if command.planner is not None:
                self.service.route_algorithm = command.planner
                self.physical_dispatch.estimates.clear()
                self.physical_dispatch.retry_at = 0.
            self.service.step()
            if command.speed is not None:
                self.clock.speed = 1. if self.physical.vehicles else command.speed
            if command.paused is not None:
                self.clock.paused = command.paused
            return self.state()

    def compare_planning(self, command: DemoPlan):
        with self.lock:
            service = self.service
            if command.pickup not in service.landmarks or command.dropoff not in service.landmarks:
                raise ValueError("unknown landmark")
            start, goal = (
                service.stop_nodes[service.landmarks[name].stop_ids[0]]
                for name in (command.pickup, command.dropoff)
            )
            results = {}
            for name, planner in (("astar", AStarPlanner()), ("dijkstra", DijkstraPlanner())):
                route = planner.search(service.graph, start, goal, service.costs)
                results[name] = {
                    "found": not isinstance(route, NoPath),
                    "points": route.polyline if not isinstance(route, NoPath) else (),
                    "cost_s": route.cost_s if not isinstance(route, NoPath) else None,
                    "length_m": route.length_m if not isinstance(route, NoPath) else None,
                    "stats": asdict(route.stats),
                }
            return {
                "start_node": start,
                "goal_node": goal,
                "results": results,
                "map_version": service.graph.map_version,
                "cost_snapshot_id": service.costs.id,
            }

    def submit(self, command: CreateRequest):
        with self.lock:
            ack = self.service.create_request(command, external_execution=bool(self.physical.vehicles))
            self.physical_dispatch.step()
            return ack

    def scenario(self, name):
        with self.lock:
            self.clock.read()
            self.clock.paused = True
            destinations = (
                ("hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station")
                if name == "extensions"
                else ("hitech",) * 18
            )
            commands = []
            for destination in destinations:
                commands.append(
                    CreateRequest(
                        command_id=uuid4().hex,
                        owner_id="web-demo",
                        service_type="PASSENGER",
                        pickup_landmark_id="main_gate",
                        dropoff_landmark_id=destination,
                    )
                )
            self.service.create_batch(commands)
            self.clock.read()
            self.clock.speed = 10.0 if name == "extensions" else 100.0
            self.clock.paused = False
            return self.state()

    def reset(self):
        with self.lock:
            planner = self.service.route_algorithm
            dispatch = self.service.dispatch_algorithm
            self.clock = SimulationClock(self.wall_clock)
            self.service = self.factory(clock=self.clock.read)
            self.service.route_algorithm = planner
            self.service.dispatch_algorithm = dispatch
            self.run_id = uuid4().hex
            self.physical = UnityDriveSession(self, unity_map(self.service)["geometry_hash"])
            self.physical_dispatch = PhysicalDispatcher(self)
            return self.state()
