"""Structural checks for a proposed campus map; review evidence is not authenticated."""

from __future__ import annotations

import math
from collections import deque
from itertools import pairwise
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

REQUIRED_FACILITY_KEYS = frozenset({
    "main_gate", "inha_station", "rear_gate", "building_5", "building_2",
    "hitech", "anniversary_60", "biryong_plaza", "dorm_1", "dorm_2", "dorm_3",
})


class ReviewModel(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class EvidenceModel(ReviewModel):
    evidence_refs: list[str] = Field(min_length=1)

    @field_validator("evidence_refs")
    @classmethod
    def require_nonblank_evidence(cls, refs: list[str]) -> list[str]:
        if any(not ref.strip() for ref in refs):
            raise ValueError("evidence reference must be nonblank")
        return refs


class Point(ReviewModel):
    x: float = Field(allow_inf_nan=False)
    y: float = Field(allow_inf_nan=False)


class Node(ReviewModel):
    id: str = Field(min_length=1)
    position_m: Point


class Edge(EvidenceModel):
    id: str = Field(min_length=1)
    from_node: str = Field(min_length=1)
    to_node: str = Field(min_length=1)
    key: str = Field(min_length=1)
    geometry_m: list[Point] = Field(min_length=2)
    length_m: float = Field(gt=0, allow_inf_nan=False)
    width_m: float = Field(gt=0, allow_inf_nan=False)


class VehicleEdge(Edge):
    allowed_speed_mps: float = Field(gt=0, allow_inf_nan=False)
    allowed_vehicle_classes: list[str] = Field(min_length=1)
    access_status: Literal["REVIEWED_ALLOWED"]


class PedestrianEdge(Edge):
    grade_percent: float = Field(allow_inf_nan=False)
    step_free: bool


class Graph(ReviewModel):
    map_version: str = Field(min_length=1)
    nodes: list[Node] = Field(min_length=2)
    edges: list[VehicleEdge] | list[PedestrianEdge] = Field(min_length=1)

    @model_validator(mode="after")
    def check_topology(self) -> Graph:
        nodes = {node.id: node for node in self.nodes}
        if len(nodes) != len(self.nodes):
            raise ValueError("graph node IDs must be unique")
        if len({edge.id for edge in self.edges}) != len(self.edges):
            raise ValueError("graph edge IDs must be unique")
        for edge in self.edges:
            if edge.from_node not in nodes or edge.to_node not in nodes:
                raise ValueError(f"edge {edge.id} references missing node")
            start = nodes[edge.from_node].position_m
            end = nodes[edge.to_node].position_m
            if math.hypot(edge.geometry_m[0].x - start.x, edge.geometry_m[0].y - start.y) > 0.01:
                raise ValueError(f"edge {edge.id} start geometry mismatch")
            if math.hypot(edge.geometry_m[-1].x - end.x, edge.geometry_m[-1].y - end.y) > 0.01:
                raise ValueError(f"edge {edge.id} end geometry mismatch")
            geometry_length = sum(
                math.hypot(right.x - left.x, right.y - left.y)
                for left, right in zip(edge.geometry_m, edge.geometry_m[1:])
            )
            if edge.length_m + 0.01 < geometry_length:
                raise ValueError(f"edge {edge.id} is shorter than its geometry")
        return self


class VehicleGraph(Graph):
    edges: list[VehicleEdge] = Field(min_length=1)


class PedestrianGraph(Graph):
    edges: list[PedestrianEdge] = Field(min_length=1)


class Landmark(EvidenceModel):
    id: str = Field(min_length=1)
    name: str = Field(min_length=1)
    stop_ids: list[str] = Field(min_length=1)
    required_facility_key: str | None = None

    @field_validator("required_facility_key")
    @classmethod
    def known_facility_key(cls, value: str | None) -> str | None:
        if value is not None and value not in REQUIRED_FACILITY_KEYS:
            raise ValueError("unknown required facility key")
        return value


class Stop(EvidenceModel):
    id: str = Field(min_length=1)
    landmark_id: str = Field(min_length=1)
    vehicle_node_id: str = Field(min_length=1)
    pedestrian_stop_node_id: str = Field(min_length=1)
    pedestrian_entrance_node_id: str = Field(min_length=1)
    entrance_id: str = Field(min_length=1)
    step_free_access: bool


class Zone(EvidenceModel):
    id: str = Field(min_length=1)
    polygon_m: list[Point] = Field(min_length=3)
    vehicle_edge_ids: list[str] = Field(min_length=1)
    waiting_stop_ids: list[str] = Field(min_length=1)


class LandmarkDocument(ReviewModel):
    map_version: str = Field(min_length=1)
    items: list[Landmark] = Field(min_length=1)


class StopDocument(ReviewModel):
    map_version: str = Field(min_length=1)
    items: list[Stop] = Field(min_length=1)


class ZoneDocument(ReviewModel):
    map_version: str = Field(min_length=1)
    items: list[Zone]


def validate_review_content(artifacts: dict[str, dict]) -> dict[str, object]:
    """Check references and declared pedestrian paths; never approve the map."""
    vehicle = VehicleGraph.model_validate(artifacts["vehicle_graph"])
    pedestrian = PedestrianGraph.model_validate(artifacts["pedestrian_graph"])
    landmarks = LandmarkDocument.model_validate(artifacts["landmarks"])
    stops = StopDocument.model_validate(artifacts["stops"])
    zones = ZoneDocument.model_validate(artifacts["zones"])
    versions = {item.map_version for item in (vehicle, pedestrian, landmarks, stops, zones)}
    if len(versions) != 1:
        raise ValueError("map artifact versions differ")

    landmark_by_id = {item.id: item for item in landmarks.items}
    stop_by_id = {item.id: item for item in stops.items}
    if len(landmark_by_id) != len(landmarks.items) or len(stop_by_id) != len(stops.items):
        raise ValueError("landmark and Stop IDs must be unique")
    facility_keys = [item.required_facility_key for item in landmarks.items
                     if item.required_facility_key is not None]
    if len(facility_keys) != len(set(facility_keys)):
        raise ValueError("required facility keys must be unique")
    vehicle_nodes = {node.id: node for node in vehicle.nodes}
    pedestrian_nodes = {node.id: node for node in pedestrian.nodes}
    for landmark in landmarks.items:
        if len(landmark.stop_ids) != len(set(landmark.stop_ids)):
            raise ValueError(f"landmark {landmark.id} repeats a Stop")
        for stop_id in landmark.stop_ids:
            stop = stop_by_id.get(stop_id)
            if stop is None or stop.landmark_id != landmark.id:
                raise ValueError(f"landmark {landmark.id} has invalid Stop {stop_id}")
    for stop in stops.items:
        if stop.landmark_id not in landmark_by_id or stop.id not in landmark_by_id[stop.landmark_id].stop_ids:
            raise ValueError(f"Stop {stop.id} has invalid landmark reference")
        if stop.vehicle_node_id not in vehicle_nodes:
            raise ValueError(f"Stop {stop.id} has no vehicle graph node")
        if (stop.pedestrian_stop_node_id not in pedestrian_nodes
                or stop.pedestrian_entrance_node_id not in pedestrian_nodes):
            raise ValueError(f"Stop {stop.id} has missing pedestrian graph node")
        if not (
            _pedestrian_reachable(pedestrian, stop.pedestrian_stop_node_id,
                                  stop.pedestrian_entrance_node_id, stop.step_free_access)
            and _pedestrian_reachable(pedestrian, stop.pedestrian_entrance_node_id,
                                      stop.pedestrian_stop_node_id, stop.step_free_access)
        ):
            raise ValueError(f"Stop {stop.id} has no suitable pedestrian path in both directions")

    edge_ids = {edge.id for edge in vehicle.edges}
    if len({zone.id for zone in zones.items}) != len(zones.items):
        raise ValueError("zone IDs must be unique")
    for zone in zones.items:
        if not _simple_polygon(zone.polygon_m):
            raise ValueError(f"zone {zone.id} has invalid or self-intersecting polygon")
        if not set(zone.vehicle_edge_ids) <= edge_ids:
            raise ValueError(f"zone {zone.id} references unknown vehicle edge")
        for stop_id in zone.waiting_stop_ids:
            stop = stop_by_id.get(stop_id)
            if stop is None:
                raise ValueError(f"zone {zone.id} references unknown waiting Stop")
            position = vehicle_nodes[stop.vehicle_node_id].position_m
            if _inside_polygon(position, zone.polygon_m):
                raise ValueError(f"zone {zone.id} waiting Stop is inside closure polygon")
        intersecting = {
            edge.id for edge in vehicle.edges
            if _polyline_intersects_polygon(edge.geometry_m, zone.polygon_m)
        }
        declared = set(zone.vehicle_edge_ids)
        if intersecting != declared or len(zone.vehicle_edge_ids) != len(declared):
            raise ValueError(
                f"zone {zone.id} closure edge IDs disagree with polygon: "
                f"missing={sorted(intersecting - declared)}, extra={sorted(declared - intersecting)}"
            )

    stop_nodes = {stop.vehicle_node_id for stop in stops.items}
    if not _vehicle_stops_reachable(vehicle, stop_nodes, frozenset()):
        raise ValueError("vehicle Stops are not mutually reachable")

    return {
        "vehicle_node_count": len(vehicle.nodes),
        "vehicle_edge_count": len(vehicle.edges),
        "pedestrian_node_count": len(pedestrian.nodes),
        "pedestrian_edge_count": len(pedestrian.edges),
        "landmark_count": len(landmarks.items),
        "stop_count": len(stops.items),
        "zone_count": len(zones.items),
        "required_facility_count": len(facility_keys),
        "required_facilities_missing": sorted(REQUIRED_FACILITY_KEYS - set(facility_keys)),
        "required_facility_coverage_complete": set(facility_keys) == REQUIRED_FACILITY_KEYS,
    }


def _pedestrian_reachable(
    graph: PedestrianGraph, start: str, goal: str, requires_step_free: bool
) -> bool:
    adjacency: dict[str, list[str]] = {node.id: [] for node in graph.nodes}
    for edge in graph.edges:
        if not requires_step_free or edge.step_free:
            adjacency[edge.from_node].append(edge.to_node)
    queue = deque([start])
    visited = {start}
    while queue:
        node = queue.popleft()
        if node == goal:
            return True
        for next_node in adjacency[node]:
            if next_node not in visited:
                visited.add(next_node)
                queue.append(next_node)
    return False


def _vehicle_stops_reachable(
    graph: VehicleGraph, stop_nodes: set[str], excluded_edges: frozenset[str]
) -> bool:
    adjacency: dict[str, list[str]] = {node.id: [] for node in graph.nodes}
    for edge in graph.edges:
        if edge.id not in excluded_edges:
            adjacency[edge.from_node].append(edge.to_node)
    for start in stop_nodes:
        queue = deque([start])
        visited = {start}
        while queue:
            for next_node in adjacency[queue.popleft()]:
                if next_node not in visited:
                    visited.add(next_node)
                    queue.append(next_node)
        if not stop_nodes <= visited:
            return False
    return True


def _polygon_area(points: list[Point]) -> float:
    return abs(sum(a.x * b.y - b.x * a.y
                   for a, b in zip(points, points[1:] + points[:1]))) / 2


def _simple_polygon(points: list[Point]) -> bool:
    if _polygon_area(points) <= 0.01:
        return False
    if len({(point.x, point.y) for point in points}) != len(points):
        return False
    segments = list(zip(points, points[1:] + points[:1]))
    if any(math.hypot(a.x - b.x, a.y - b.y) <= 1e-9 for a, b in segments):
        return False
    for index, (a, b) in enumerate(segments):
        for other_index, (c, d) in enumerate(segments[index + 1:], index + 1):
            if other_index == index + 1 or (index == 0 and other_index == len(segments) - 1):
                continue
            if _segments_intersect(a, b, c, d):
                return False
    return True


def _inside_polygon(point: Point, polygon: list[Point]) -> bool:
    inside = False
    previous = polygon[-1]
    for current in polygon:
        cross = (current.x - previous.x) * (point.y - previous.y) - (
            current.y - previous.y
        ) * (point.x - previous.x)
        if abs(cross) <= 1e-9 and (
            min(previous.x, current.x) <= point.x <= max(previous.x, current.x)
            and min(previous.y, current.y) <= point.y <= max(previous.y, current.y)
        ):
            return True
        if ((current.y > point.y) != (previous.y > point.y)
                and point.x < (previous.x - current.x) * (point.y - current.y)
                / (previous.y - current.y) + current.x):
            inside = not inside
        previous = current
    return inside


def _polyline_intersects_polygon(polyline: list[Point], polygon: list[Point]) -> bool:
    if any(_inside_polygon(point, polygon) for point in polyline):
        return True
    boundary = list(zip(polygon, polygon[1:] + polygon[:1]))
    return any(
        _segments_intersect(start, end, left, right)
        for start, end in pairwise(polyline)
        for left, right in boundary
    )


def _segments_intersect(a: Point, b: Point, c: Point, d: Point) -> bool:
    def orientation(p: Point, q: Point, r: Point) -> float:
        return (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x)

    def on_segment(p: Point, q: Point, r: Point) -> bool:
        return (min(p.x, r.x) - 1e-9 <= q.x <= max(p.x, r.x) + 1e-9
                and min(p.y, r.y) - 1e-9 <= q.y <= max(p.y, r.y) + 1e-9)

    ab_c = orientation(a, b, c)
    ab_d = orientation(a, b, d)
    cd_a = orientation(c, d, a)
    cd_b = orientation(c, d, b)
    if (ab_c > 0 > ab_d or ab_c < 0 < ab_d) and (cd_a > 0 > cd_b or cd_a < 0 < cd_b):
        return True
    return any((abs(cross) <= 1e-9 and on_segment(p, q, r)) for cross, p, q, r in [
        (ab_c, a, c, b), (ab_d, a, d, b), (cd_a, c, a, d), (cd_b, c, b, d),
    ])
