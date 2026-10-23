"""Resolve configured staging hubs on existing source road nodes; never add links."""

import json
import xml.etree.ElementTree as ET
from itertools import pairwise

from campus_sim.map_coordinates import MapCoordinates
from campus_sim.planning import CostSnapshot, DijkstraPlanner, NoPath


def inside_polygon(point, polygon):
    x, y = point
    inside = False
    for a, b in pairwise(polygon):
        if (a[1] > y) != (b[1] > y) and x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]:
            inside = not inside
    return inside


def build_hubs(policy, graph, stops, source_paths):
    by_stop = {stop["id"]: stop for stop in stops}
    osm = ET.parse(source_paths["osm"]).getroot()
    coordinates = MapCoordinates()
    osm_nodes = {
        n.attrib["id"]: coordinates.from_wgs84(float(n.attrib["lon"]), float(n.attrib["lat"]))
        for n in osm.findall("node")
    }
    ways = {w.attrib["id"]: w for w in osm.findall("way")}
    unity_hub = json.loads(source_paths["hub_reference"].read_text(encoding="utf-8"))
    hubs = {}
    planner = DijkstraPlanner()
    costs = CostSnapshot.travel_time(graph)
    for configured in policy["hubs"]:
        stop_id = configured.get("stop_id")
        if stop_id:
            if stop_id not in by_stop:
                raise ValueError("hub references missing Stop")
            node_id = by_stop[stop_id]["node_id"]
        else:
            node_id = configured["node_id"]
        if node_id not in graph.nodes:
            raise ValueError("hub references missing road node")
        if not any(node_id in (edge.u, edge.v) for edge in graph.edges):
            raise ValueError("hub node has no source road edges")
        parking_way_id = configured.get("source_parking_osm_way_id")
        if parking_way_id:
            way = ways.get(parking_way_id)
            if way is None:
                raise ValueError("hub parking source missing")
            tags = {t.attrib["k"]: t.attrib["v"] for t in way.findall("tag")}
            polygon = [osm_nodes[n.attrib["ref"]] for n in way.findall("nd")]
            if tags.get("amenity") != "parking" or polygon[0] != polygon[-1]:
                raise ValueError("hub source is not a closed OSM parking polygon")
            if not inside_polygon(graph.nodes[node_id], polygon):
                raise ValueError("hub road node is outside source parking polygon")
            if (
                unity_hub["parkingOsmId"] != f"way/{parking_way_id}"
                or unity_hub["hubId"] != configured["source_unity_hub_id"]
            ):
                raise ValueError("Unity hub and OSM parking references disagree")
        unreachable = [
            stop["id"]
            for stop in stops
            if any(
                isinstance(planner.search(graph, a, b, costs), NoPath)
                for a, b in ((node_id, stop["node_id"]), (stop["node_id"], node_id))
            )
        ]
        if unreachable:
            raise ValueError(f"hub has unreachable round trips: {configured['id']}: {unreachable}")
        hubs[configured["id"]] = {
            **configured,
            "node_id": node_id,
            "position_xy_m": list(graph.nodes[node_id]),
            "round_trip_reachable_stop_count": len(stops),
            "verification_status": "UNVERIFIED_SIMULATION_STAGING_NODE",
            "charging_status": "SYNTHETIC_ONE_PORT_PER_HUB",
        }
    return hubs
