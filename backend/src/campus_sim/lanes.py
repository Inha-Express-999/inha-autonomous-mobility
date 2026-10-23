"""Right-hand simulation lane geometry from pinned Unity-authored road widths.

Widths describe authored meshes, not surveyed usable road widths or permission.
The original road graph/OSM source assets are preserved.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                     ensure_ascii=False, allow_nan=False).encode()).hexdigest()


def build_profiles(graph, geometry_hash, root):
    widths, sources = {}, {}
    files = ["Assets/InhaCampus/Source/campus.json",
             "Assets/CampusSim/Data/road_connections_scoped.json",
             "Assets/CampusSim/Data/road_connections_restored.json"]
    for name in files:
        path = Path(root) / name
        text = path.read_text(encoding="utf-8")
        sources[name] = hashlib.sha256(text.encode()).hexdigest()
        document = json.loads(text)
        for road in document.get("features", document.get("roads", [])):
            if "features" in document and road.get("kind") != "road":
                continue
            if road.get("pedestrian") or road.get("subtype") in {"footway", "path", "steps", "pedestrian"}:
                continue
            width = road.get("width")
            if not isinstance(width, (int, float)) or not math.isfinite(width) or width <= 0:
                continue
            way = str(road.get("osmId", road["id"].split("_")[0])).removeprefix("way/")
            widths.setdefault(way, []).append((width, name))
    pairs = {(e.u, e.v, e.key.split(":")[0]) for e in graph.edges}
    profiles = []
    for edge in sorted(graph.edges, key=lambda e: e.id):
        way = edge.key.split(":")[0]
        if way not in widths or edge.stairs or not edge.vehicle_allowed:
            continue
        width, source = min(widths[way])
        bidirectional = (edge.v, edge.u, way) in pairs
        two_lanes = bidirectional and width >= 5.5
        profiles.append(dict(edge=list(edge.id), lane_id="|".join(edge.id) + ("|right" if two_lanes else "|shared"),
                             authored_road_width_m=width, lane_width_m=width / 2 if two_lanes else width,
                             right_offset_m=width / 4 if two_lanes else 0.,
                             mode="right_hand_two_way" if two_lanes else "shared_single" if bidirectional else "one_way_single",
                             width_source=source, verification="synthetic_authored_width_not_surveyed"))
    payload = dict(schema_version=1, geometry_hash=geometry_hash, sources=sources,
                   traffic_side="right", operationally_verified=False, profiles=profiles)
    return dict(payload, profile_hash=digest(payload))


class LaneProfiles:
    def __init__(self, document, geometry_hash):
        payload = {k: v for k, v in document.items() if k != "profile_hash"}
        if (document.get("schema_version") != 1 or document.get("geometry_hash") != geometry_hash or
                document.get("operationally_verified") is not False or document.get("profile_hash") != digest(payload)):
            raise ValueError("Lane profile version/map/hash mismatch")
        self.hash = document["profile_hash"]
        self.by_edge = {tuple(p["edge"]): p for p in document["profiles"]}

    def feasible(self, edge, half_width):
        profile = self.by_edge.get(edge.id)
        return profile is not None and profile["lane_width_m"] > 2 * (half_width + .2)


def unit(a, b):
    length = math.dist(a, b)
    if length < .001:
        raise ValueError("Degenerate lane segment")
    return ((b[0] - a[0]) / length, (b[1] - a[1]) / length)


def shift(point, direction, offset):
    return (point[0] + direction[1] * offset, point[1] - direction[0] * offset)


def lane_path(parts, minimum_radius):
    """Offset each directed road and smoothly connect lanes inside junctions.

    parts: (centreline Unity-XZ points, right-offset metres). Unsupported short
    corners/U-turns are rejected instead of joining lanes with a sharp chord.
    """
    segments = []
    for points, offset in parts:
        for a, b in zip(points, points[1:]):
            if math.dist(a, b) < .01:
                continue
            direction = unit(a, b)
            if segments and math.dist(segments[-1][1], a) < .02 and abs(segments[-1][2] - offset) < .01:
                previous = unit(segments[-1][0], segments[-1][1])
                if sum(previous[j] * direction[j] for j in (0, 1)) > .9995:
                    segments[-1] = (segments[-1][0], b, offset)
                    continue
            segments.append((a, b, offset))
    if not segments:
        raise ValueError("No lane geometry")
    starts = [shift(a, unit(a, b), offset) for a, b, offset in segments]
    ends = [shift(b, unit(a, b), offset) for a, b, offset in segments]
    curves = {}
    for i in range(len(segments) - 1):
        a, b, offset1 = segments[i]
        c, d, offset2 = segments[i + 1]
        if math.dist(b, c) > .1:
            raise ValueError("Disconnected lane graph")
        incoming, outgoing = unit(a, b), unit(c, d)
        turn = math.atan2(incoming[0] * outgoing[1] - incoming[1] * outgoing[0],
                          sum(incoming[j] * outgoing[j] for j in (0, 1)))
        if abs(turn) > math.radians(135):
            raise ValueError("U-turn lane connector requires a dedicated manoeuvre")
        if abs(turn) < .035 and abs(offset1 - offset2) < .01:
            normal1, normal2 = (incoming[1], -incoming[0]), (outgoing[1], -outgoing[0])
            denominator = 1 + sum(incoming[j] * outgoing[j] for j in (0, 1))
            joint = tuple(b[j] + offset1 * (normal1[j] + normal2[j]) / denominator for j in (0, 1))
            ends[i] = starts[i + 1] = joint
            continue
        radius = max(3., minimum_radius * 1.25, max(offset1, offset2) + 1)
        if abs(turn) < .035:
            trim, handle = 4., 8. / 3
        else:
            offset = (offset1 + offset2) / 2
            trim = (radius + offset if turn < 0 else radius - offset) * math.tan(abs(turn) / 2)
            handle = 4 / 3 * radius * math.tan(abs(turn) / 4)
        if trim >= math.dist(a, b) * .49 or trim >= math.dist(c, d) * .49:
            raise ValueError("Insufficient road length for a feasible lane connector")
        start = tuple(ends[i][j] - incoming[j] * trim for j in (0, 1))
        end = tuple(starts[i + 1][j] + outgoing[j] * trim for j in (0, 1))
        p1 = tuple(start[j] + incoming[j] * handle for j in (0, 1))
        p2 = tuple(end[j] - outgoing[j] * handle for j in (0, 1))
        curve = []
        for step in range(17):
            t = step / 16
            curve.append(tuple((1 - t) ** 3 * start[j] + 3 * (1 - t) ** 2 * t * p1[j] +
                               3 * (1 - t) * t * t * p2[j] + t ** 3 * end[j] for j in (0, 1)))
        ends[i], starts[i + 1], curves[i] = start, end, curve
    result = []
    for i in range(len(segments)):
        if math.dist(starts[i], ends[i]) < .01:
            raise ValueError("Overlapping lane connectors")
        result.extend([starts[i], ends[i]])
        result.extend(curves.get(i, []))
    return [p for i, p in enumerate(result) if i == 0 or math.dist(result[i - 1], p) > .001]


def main():
    from campus_sim.service import MobilityService
    from campus_sim.unity_map import unity_map
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    service = MobilityService.from_osm_map()
    data = build_profiles(service.graph, unity_map(service)["geometry_hash"], args.project_root)
    args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(profiles=len(data["profiles"]), profile_hash=data["profile_hash"])))


if __name__ == "__main__":
    main()
