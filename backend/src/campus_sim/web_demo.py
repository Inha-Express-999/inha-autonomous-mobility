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
from uuid import uuid4

from pydantic import BaseModel, ConfigDict, Field

from campus_sim.domain import CreateRequest, RequestStatus
from campus_sim.map_coordinates import MapCoordinates
from campus_sim.service import MobilityService


class DemoControl(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, strict=True)
    speed: float | None = Field(default=None, ge=1, le=300)
    paused: bool | None = None


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
    buildings, context_roads = [], []
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
        "stops": stops,
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
        self.run_id = uuid4().hex
        self.root = Path(__file__).resolve().parents[3]
        self.map = map_presentation(self.service, self.root)

    def state(self):
        with self.lock:
            service = self.service
            service.step()
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
            requests = [r.model_dump(mode="json") for r in service.requests.values()]
            queued = [r for r in requests if r["status"] == "QUEUED"]
            return {
                "run_id": self.run_id,
                "time_s": now,
                "speed": self.clock.speed,
                "paused": self.clock.paused,
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
                "charging_sessions": [asdict(c) for c in service.charges],
                "events": self.events(now),
                "battery_policy": service.battery_policy.model_dump(),
                "simulation_only": True,
            }

    def advance(self):
        with self.lock:
            self.service.step()

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
                for at, key, text in (
                    (
                        trip.departed_at_s,
                        "assigned",
                        f"{trip.vehicle_id} → 요청 {index:02d} FIFO 배차",
                    ),
                    (trip.dropoff_at_s, "delivered", f"{trip.vehicle_id} 목적지 도착 · {name}"),
                    (trip.returns_at_s, "returned", f"{trip.vehicle_id} 정문 복귀"),
                ):
                    add(at, f"{request.id}:{key}", text)
            if request.status is RequestStatus.CANCELLED:
                # Cancellation has no stored event time in the existing domain.
                # Its status is displayed in requests; do not invent a timestamp.
                continue
        for index, charge in enumerate(self.service.charges):
            add(charge.started_at_s, f"charge:{index}:start", f"{charge.vehicle_id} 충전 시작")
            add(
                charge.ends_at_s,
                f"charge:{index}:end",
                f"{charge.vehicle_id} 충전 완료 · FIFO 재진입",
            )
        return sorted(result, key=lambda e: e["time_s"])[-40:][::-1]

    def control(self, command: DemoControl):
        with self.lock:
            self.service.step()
            if command.speed is not None:
                self.clock.speed = command.speed
            if command.paused is not None:
                self.clock.paused = command.paused
            return self.state()

    def submit(self, command: CreateRequest):
        with self.lock:
            return self.service.create_request(command)

    def scenario(self, name):
        with self.lock:
            self.clock.read()
            self.clock.paused = True
            destinations = (
                ("hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station")
                if name == "extensions"
                else ("hitech",) * 18
            )
            for destination in destinations:
                self.service.create_request(
                    CreateRequest(
                        command_id=uuid4().hex,
                        owner_id="web-demo",
                        service_type="PASSENGER",
                        pickup_landmark_id="main_gate",
                        dropoff_landmark_id=destination,
                    )
                )
            self.clock.read()
            self.clock.speed = 10.0 if name == "extensions" else 100.0
            self.clock.paused = False
            return self.state()

    def reset(self):
        with self.lock:
            self.clock = SimulationClock(self.wall_clock)
            self.service = self.factory(clock=self.clock.read)
            self.run_id = uuid4().hex
            return self.state()
