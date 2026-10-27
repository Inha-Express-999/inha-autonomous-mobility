"""Same-way centerline coverage against existing Unity source exports."""

import json
import math
from collections import Counter, defaultdict
from itertools import pairwise

from campus_sim.map_coordinates import projection

INPUTS = {
    "core_unity": "Assets/InhaCampus/Source/campus.json",
    "scoped_unity": "Assets/CampusSim/Data/road_connections_scoped.json",
    "restored_unity": "Assets/CampusSim/Data/road_connections_restored.json",
    "extension_export": "Assets/CampusSim/Data/road_extensions.json",
}


class UnityCoverage:
    def __init__(self, root, coordinates):
        self.lines = {
            name: defaultdict(list)
            for name in ("core_unity", "scoped_unity", "restored_unity", "extension_export")
        }
        for name, ways in self.lines.items():
            data = json.loads((root / INPUTS[name]).read_text(encoding="utf-8"))
            for road in data.get("roads", data.get("features", [])):
                if name == "core_unity" and road["kind"] != "road":
                    continue
                wid = road.get("osmId", road["id"].split("_")[0])
                points = [coordinates.from_unity(p["x"], p["z"]) for p in road["points"]]
                ways[wid].extend(pairwise(points))

    def route(self, route, graph):
        lookup = {edge.id: edge for edge in graph.edges}
        lengths = Counter()
        missing_ways = set()
        for edge_id in route.edge_ids:
            edge = lookup[edge_id]
            way_id = edge.key.split(":")[0]
            a, b = edge.polyline
            steps = max(1, math.ceil(edge.length_m / 2))
            for index in range(steps):
                point = tuple(a[j] + (index + 0.5) / steps * (b[j] - a[j]) for j in (0, 1))
                found = "not_in_unity_exports"
                for name, ways in self.lines.items():
                    if any(projection(point, c, d)[0] <= 0.05 for c, d in ways.get(way_id, ())):
                        found = name
                        break
                lengths[found] += edge.length_m / steps
                if found == "not_in_unity_exports":
                    missing_ways.add(way_id)
        return {
            "length_estimate_m_by_source": dict(lengths),
            "missing_way_ids": sorted(missing_ways),
            "method": "same OSM way only; midpoint samples <=2m, 0.05m centerline tolerance; not mesh/height/access verification",
        }
