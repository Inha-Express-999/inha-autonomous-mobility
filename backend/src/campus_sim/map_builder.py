"""Build an offline OSM-geometry simulation map; never approve unknown access."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import xml.etree.ElementTree as ET
from dataclasses import asdict
from itertools import pairwise
from pathlib import Path

from campus_sim.battery import BatteryPolicy
from campus_sim.domain import Vehicle
from campus_sim.hubs import build_hubs
from campus_sim.map_coordinates import CRS_LOCAL, ORIGIN_LAT, ORIGIN_LON, MapCoordinates, projection
from campus_sim.planning import CostSnapshot, DijkstraPlanner, NoPath, RoadEdge, RoadGraph

SOURCE_FILES = {
    "osm": "Assets/InhaCampus/Source/campus.osm",
    "unity": "Assets/InhaCampus/Source/campus.json",
    "entrances": "Docs/MapResearch/Iterations/EntranceInventory/entrances.csv",
    "policy": "configs/osm_runtime.json",
    "attribution": "Assets/InhaCampus/Source/ATTRIBUTION.txt",
    "builder": "backend/src/campus_sim/map_builder.py",
    "coordinate_adapter": "backend/src/campus_sim/map_coordinates.py",
    "project_version": "VERSION",
    "expanded_osm": "Docs/MapResearch/2026-09-17/expanded_campus.osm",
    "unity_scoped": "Assets/CampusSim/Data/road_connections_scoped.json",
    "unity_restored": "Assets/CampusSim/Data/road_connections_restored.json",
    "unity_extensions": "Assets/CampusSim/Data/road_extensions.json",
    "extension_builder": "backend/src/campus_sim/map_extension.py",
    "osm_reader": "backend/src/campus_sim/osm_roads.py",
    "unity_coverage": "backend/src/campus_sim/unity_coverage.py",
    "hub_reference": "Docs/MapResearch/Iterations/west-field-hub-validation.json",
    "hub_builder": "backend/src/campus_sim/hubs.py",
}
NAMES = {
    "main_gate": "정문",
    "rear_gate": "후문",
    "library": "정석학술정보관",
    "hitech": "하이테크",
    "building_2": "2호관",
    "building_5": "5호관",
    "anniversary_60": "60주년",
    "biryong_plaza": "비룡플라자",
    "dorm_1": "기숙사 1",
    "dorm_2": "기숙사 2",
    "dorm_3": "기숙사 3",
    "inha_station": "인하대역",
    "main_building": "본관",
    "student_center": "학생회관",
}

MAP_MANIFEST_SCHEMA_VERSION = 2
MAP_HASH_FORMAT = "sha256-utf8-lf-v1"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def map_text_digest(path):
    """Hash UTF-8 map inputs with universal newlines, preserving all other content.

    Git may check text out with LF or CRLF. Neither a BOM nor whitespace/content
    changes are discarded, and non-UTF-8 input fails instead of being repaired.
    """
    text = Path(path).read_text(encoding="utf-8")
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def write_json(path, value):
    Path(path).write_text(
        json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def read_policy(path):
    policy = json.loads(Path(path).read_text(encoding="utf-8"))
    if policy["schema_version"] != 3 or policy["routing_mode"] != "osm_geometry_simulation":
        raise ValueError("unsupported map policy; verified mode cannot use assumptions")
    for key in ("default_speed_mps", "max_stop_gap_m"):
        value = policy[key]
        if type(value) not in (int, float) or not math.isfinite(value) or value <= 0:
            raise ValueError(f"invalid {key}")
    if not policy["allowed_highways"] or set(policy["allowed_highways"]) - {
        "service",
        "residential",
        "unclassified",
        "living_street",
        "secondary",
    }:
        raise ValueError("pedestrian/high-speed roads cannot be enabled by this importer")
    if "secondary" in policy["allowed_highways"] and not policy.get("extension_corridors"):
        raise ValueError("secondary roads require explicit pinned extension corridors")
    fleet = tuple(Vehicle(**v) for v in policy["vehicles"])
    if not 1 <= len(fleet) <= 3 or len({v.id for v in fleet}) != len(fleet):
        raise ValueError("configure between one and three uniquely identified vehicles")
    BatteryPolicy(**policy["battery"])
    hub_ids = [hub["id"] for hub in policy["hubs"]]
    if not hub_ids or len(hub_ids) != len(set(hub_ids)):
        raise ValueError("hub ids must be nonempty and unique")
    for hub in policy["hubs"]:
        if not hub["id"] or not hub["name"] or bool(hub.get("stop_id")) == bool(hub.get("node_id")):
            raise ValueError("hub needs id/name and exactly one Stop or road node reference")
        if type(hub.get("charging_ports")) is not int or hub["charging_ports"] != 1:
            raise ValueError("current simulation requires one independent port per home hub")
    bindings = [v["home_hub_id"] for v in policy["vehicles"]]
    if any(hub not in hub_ids for hub in bindings) or len(set(bindings)) != len(bindings):
        raise ValueError("vehicles need distinct existing home hubs")
    return policy


def build_map(project_root, output_dir):
    root, output = Path(project_root), Path(output_dir)
    paths = {key: root / name for key, name in SOURCE_FILES.items()}
    policy = read_policy(paths["policy"])
    unity = json.loads(paths["unity"].read_text(encoding="utf-8-sig"))
    if (unity["originLatitude"], unity["originLongitude"]) != (ORIGIN_LAT, ORIGIN_LON):
        raise ValueError("Unity origin differs from explicit coordinate adapter")
    osm = ET.parse(paths["osm"]).getroot()
    raw_nodes = {
        n.attrib["id"]: (float(n.attrib["lon"]), float(n.attrib["lat"]))
        for n in osm.findall("node")
    }
    ways = {w.attrib["id"]: w for w in osm.findall("way")}
    restricted = set()
    for relation in osm.findall("relation"):
        tags = {t.attrib["k"]: t.attrib["v"] for t in relation.findall("tag")}
        if tags.get("type") == "restriction":
            restricted.update(
                m.attrib["ref"] for m in relation.findall("member") if m.attrib["type"] == "way"
            )
    blocked_nodes = set()
    for node in osm.findall("node"):
        tags = {t.attrib["k"]: t.attrib["v"] for t in node.findall("tag")}
        if tags.get("barrier") in {"bollard", "block", "cycle_barrier", "fence", "wall"} or any(
            tags.get(key) in {"no", "private"} for key in ("access", "vehicle", "motor_vehicle")
        ):
            blocked_nodes.add("osm:" + node.attrib["id"])
    coords = MapCoordinates()
    nodes, segments, excluded = {}, [], []
    alignment_residuals = []
    for feature in unity["features"]:
        if feature["kind"] != "road":
            continue
        feature_id = feature["id"]
        way_id = feature_id.split("_")[0]
        if way_id not in ways:
            raise ValueError(f"Unity road lacks OSM source: {feature_id}")
        way = ways[way_id]
        tags = {t.attrib["k"]: t.attrib["v"] for t in way.findall("tag")}
        reason = None
        if tags.get("highway") not in policy["allowed_highways"]:
            reason = "NOT_VEHICLE_ROAD_CLASS"
        elif any(
            tags.get(key) not in {None, "yes", "permissive", "designated"}
            for key in ("access", "vehicle", "motor_vehicle", "motorcar")
        ):
            reason = "ACCESS_RESTRICTION"
        elif way_id in restricted:
            reason = "TURN_RESTRICTION_UNSUPPORTED"
        elif any(
            key.startswith(("access:", "vehicle:", "motor_vehicle:", "motorcar:", "oneway:"))
            for key in tags
        ):
            reason = "DIRECTIONAL_OR_CONDITIONAL_ACCESS_UNSUPPORTED"
        elif tags.get("oneway") not in {None, "yes", "1", "true", "no", "0", "false", "-1"}:
            reason = "ONEWAY_UNSUPPORTED"
        if reason:
            excluded.append(
                {"unity_feature_id": feature_id, "osm_way_id": way_id, "reason": reason}
            )
            continue
        refs = [n.attrib["ref"] for n in way.findall("nd")]
        if any(ref not in raw_nodes for ref in refs):
            raise ValueError(f"missing OSM node in {way_id}")
        legacy = [coords.wgs84_to_unity(*raw_nodes[ref]) for ref in refs]
        source_points = [(p["x"], p["z"]) for p in feature["points"]]
        for a, b in pairwise(source_points):
            if not any(
                projection(a, c, d)[0] <= 0.003 and projection(b, c, d)[0] <= 0.003
                for c, d in pairwise(legacy)
            ):
                raise ValueError(f"Unity segment skips OSM nodes/geometry: {feature_id}")
        converted = []
        for index, point in enumerate(feature["points"]):
            p = (point["x"], point["z"])
            # A clipped Unity endpoint must lie on its own OSM way, never a nearby road.
            residual = min(projection(p, a, b)[0] for a, b in pairwise(legacy))
            if residual > 0.003:
                raise ValueError(f"Unity/OSM geometry mismatch: {feature_id}, {residual} m")
            alignment_residuals.append(residual)
            nearest = min(range(len(refs)), key=lambda i: math.dist(p, legacy[i]))
            if math.dist(p, legacy[nearest]) <= 0.003:
                node_id = "osm:" + refs[nearest]
                xy = coords.from_wgs84(*raw_nodes[refs[nearest]])
            else:
                node_id = f"clip:{feature_id}:{index}"
                xy = coords.from_unity(*p)
            if node_id in nodes and math.dist(nodes[node_id], xy) > 1e-6:
                raise ValueError("conflicting OSM node coordinates")
            nodes[node_id] = xy
            converted.append(node_id)
        oneway = tags.get("oneway")
        directions = (
            (-1,)
            if oneway == "-1"
            else (1,)
            if oneway in {"yes", "1", "true"}
            or (oneway is None and tags.get("junction") == "roundabout")
            else (1, -1)
        )
        for index, (u, v) in enumerate(pairwise(converted)):
            if u in blocked_nodes or v in blocked_nodes:
                excluded.append(
                    {"unity_feature_id": feature_id, "reason": "NODE_BARRIER", "segment": index}
                )
                continue
            if math.dist(nodes[u], nodes[v]) < 1e-6:
                continue
            segments.append(
                {
                    "u": u,
                    "v": v,
                    "key": f"{feature_id}:{index}",
                    "osm_way_id": way_id,
                    "unity_feature_id": feature_id,
                    "osm_tags": tags,
                    "directions": directions,
                }
            )
    if not segments:
        raise ValueError("no OSM vehicle-road segments; no synthetic fallback")
    extension_report = None
    if policy.get("extension_corridors"):
        from campus_sim.map_extension import integrate_corridors

        nodes, segments, extension_report = integrate_corridors(root, nodes, segments, policy)

    stops, landmarks, cuts = [], [], {}
    with paths["entrances"].open(encoding="utf-8-sig", newline="") as stream:
        entrances = list(csv.DictReader(stream))
    general = [row for row in entrances if row["role"] == "general"]
    if len({row["landmark_id"] for row in general}) != len(general):
        raise ValueError("duplicate general entrance landmark")
    for row in sorted(general, key=lambda r: r["landmark_id"]):
        landmark_id = row["landmark_id"]
        landmark = {"id": landmark_id, "name": NAMES.get(landmark_id, landmark_id), "stop_ids": []}
        anchor = coords.from_unity(float(row["unity_x"]), float(row["unity_z"]))
        override = policy.get("extension_corridors", {}).get("targets", {}).get(landmark_id)
        eligible = [
            (i, s)
            for i, s in enumerate(segments)
            if override is None or s["osm_way_id"] == override["stop_way_id"]
        ]
        if not eligible:
            raise ValueError(f"pinned Stop way absent: {landmark_id}")
        gap, segment_index, ratio, xy = min(
            (
                projection(anchor, nodes[s["u"]], nodes[s["v"]])[0],
                i,
                projection(anchor, nodes[s["u"]], nodes[s["v"]])[1],
                projection(anchor, nodes[s["u"]], nodes[s["v"]])[2],
            )
            for i, s in eligible
        )
        if gap <= policy["max_stop_gap_m"]:
            segment = segments[segment_index]
            if ratio <= 1e-9:
                node_id = segment["u"]
            elif ratio >= 1 - 1e-9:
                node_id = segment["v"]
            else:
                ratio = round(ratio, 12)
                node_id = f"split:{segment['key']}:{ratio}"
                xy = tuple(
                    nodes[segment["u"]][i]
                    + ratio * (nodes[segment["v"]][i] - nodes[segment["u"]][i])
                    for i in (0, 1)
                )
                nodes[node_id] = xy
                cuts.setdefault(segment_index, {})[ratio] = node_id
            stop_id = f"{landmark_id}_road_candidate"
            landmark["stop_ids"].append(stop_id)
            stops.append(
                {
                    "id": stop_id,
                    "landmark_id": landmark_id,
                    "node_id": node_id,
                    "position_xy_m": nodes[node_id],
                    "unity_xz_m": coords.to_unity(*nodes[node_id]),
                    "entrance_id": row["entrance_id"],
                    "entrance_unity_xz_m": [float(row["unity_x"]), float(row["unity_z"])],
                    "entrance_gap_m": gap,
                    "step_free_access": False,
                    "verification_status": "UNVERIFIED_ROAD_PROJECTION_CANDIDATE",
                    "entrance_verification_status": row["verification_status"],
                    "osm_way_id": segment["osm_way_id"],
                    "selection_policy": "pinned_corridor_way" if override else "nearest_core_road",
                }
            )
            landmark["coverage_status"] = "ROAD_STOP_CANDIDATE"
        else:
            landmark.update(coverage_status="NO_STOP_WITHIN_CONFIGURED_GAP", nearest_road_gap_m=gap)
        landmarks.append(landmark)

    edges, provenance = [], []
    for index, segment in enumerate(segments):
        chain = (
            [segment["u"]]
            + [node for _, node in sorted(cuts.get(index, {}).items())]
            + [segment["v"]]
        )
        for part, (a, b) in enumerate(pairwise(chain)):
            for direction in segment["directions"]:
                u, v = (a, b) if direction == 1 else (b, a)
                key = f"{segment['key']}:{part}:{'f' if direction == 1 else 'r'}"
                speed = policy["default_speed_mps"]
                tagged_speed = segment["osm_tags"].get("maxspeed")
                if tagged_speed is not None:
                    # OSM numeric maxspeed is km/h; textual/conditional limits need review.
                    try:
                        limit = float(tagged_speed) / 3.6
                    except ValueError as error:
                        raise ValueError("unsupported OSM maxspeed; review map policy") from error
                    if not math.isfinite(limit) or limit <= 0:
                        raise ValueError("invalid OSM maxspeed")
                    speed = min(speed, limit)
                edge = RoadEdge(
                    u,
                    v,
                    key,
                    (nodes[u], nodes[v]),
                    math.dist(nodes[u], nodes[v]),
                    speed,
                    width_m=None,
                    verified=False,
                )
                edges.append(edge)
                provenance.append(
                    {
                        "edge_id": edge.id,
                        "osm_way_id": segment["osm_way_id"],
                        "unity_feature_id": segment["unity_feature_id"],
                        "osm_tags": segment["osm_tags"],
                        "access_status": "UNVERIFIED_SIMULATION_ONLY",
                        "speed_source": "configured_assumption",
                        **{
                            key: segment[key]
                            for key in (
                                "source_osm_file",
                                "source_segment_index",
                                "source_fraction_interval",
                                "source_pieces",
                                "unity_centerline_sources",
                            )
                            if key in segment
                        },
                    }
                )
    graph = RoadGraph(policy["map_version"], nodes, tuple(edges), synthetic=True)
    costs = CostSnapshot.travel_time(graph)
    home = next((s for s in stops if s["landmark_id"] == policy["home_landmark_id"]), None)
    if home is None:
        raise ValueError("configured home has no road Stop candidate")
    planner = DijkstraPlanner()
    for stop in stops:
        stop["round_trip_reachable"] = all(
            not isinstance(planner.search(graph, a, b, costs), NoPath)
            for a, b in ((home["node_id"], stop["node_id"]), (stop["node_id"], home["node_id"]))
        )
    hubs = build_hubs(policy, graph, stops, paths)

    output.mkdir(parents=True, exist_ok=True)
    write_json(
        output / "graph.json",
        {
            "map_version": graph.map_version,
            "synthetic": True,
            "nodes": dict(graph.nodes),
            "edges": [asdict(e) for e in edges],
            "provenance": provenance,
        },
    )
    write_json(output / "stops.json", stops)
    write_json(output / "landmarks.json", landmarks)
    write_json(output / "policy.json", policy)
    report = {
        "hubs": hubs,
        "vehicle_home_hubs": {v["id"]: v["home_hub_id"] for v in policy["vehicles"]},
        "node_count": len(nodes),
        "directed_edge_count": len(edges),
        "osm_way_count": len({s["osm_way_id"] for s in segments}),
        "stop_candidate_count": len(stops),
        "round_trip_reachable_stop_count": sum(s["round_trip_reachable"] for s in stops),
        "landmark_count": len(landmarks),
        "excluded_features": excluded,
        "max_unity_osm_centerline_residual_m": max(alignment_residuals),
        "unserved_landmarks": [l for l in landmarks if not l["stop_ids"]],
        "disconnected_stop_ids": [s["id"] for s in stops if not s["round_trip_reachable"]],
        "extension_corridors": extension_report,
    }
    write_json(output / "build_report.json", report)
    (output / "ATTRIBUTION.txt").write_text(
        paths["attribution"].read_text(encoding="utf-8")
        + (
            "\nAdditional cached OSM: Docs/MapResearch/2026-09-17/expanded_campus.osm; "
            "acquisition URL/date/hash: Docs/MapResearch/2026-09-17/downloads.json.\n"
            if extension_report
            else ""
        )
        + "\nThis OSM-derived graph retains ODbL attribution. Vehicle access and Stops are unverified.\n",
        encoding="utf-8",
        newline="\n",
    )
    manifest = {
        "schema_version": MAP_MANIFEST_SCHEMA_VERSION,
        "hash_format": MAP_HASH_FORMAT,
        "build_project_version": paths["project_version"].read_text(encoding="utf-8").strip(),
        "map_version": graph.map_version,
        "geometry_source": "cached_osm_and_unity_centerlines",
        "synthetic_geometry": False,
        "routing_mode": policy["routing_mode"],
        "operationally_verified": False,
        "crs": CRS_LOCAL,
        "origin_wgs84_lon_lat": [ORIGIN_LON, ORIGIN_LAT],
        "unity_coordinate_system": "legacy formula from AgentScripts/prepare_campus.py; north-aligned X east/Z north",
        "assumptions": policy["assumptions"],
        "home_stop_id": home["id"],
        "sources": {
            key: {"path": SOURCE_FILES[key], "sha256": map_text_digest(path)}
            for key, path in paths.items()
        },
        "files": {
            name: map_text_digest(output / name)
            for name in (
                "graph.json",
                "stops.json",
                "landmarks.json",
                "policy.json",
                "build_report.json",
                "ATTRIBUTION.txt",
            )
        },
    }
    write_json(output / "manifest.json", manifest)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = args.output or args.project_root / "maps/inha_osm_runtime"
    print(json.dumps(build_map(args.project_root, output), ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
