"""Offline extension candidates. Does not open roads or edit Unity/runtime maps."""

from __future__ import annotations

import argparse
import csv
import json
import math
from collections import defaultdict
from itertools import pairwise
from pathlib import Path

from campus_sim.map_builder import digest, write_json
from campus_sim.map_coordinates import MapCoordinates, projection
from campus_sim.map_data import load_map_bundle
from campus_sim.osm_roads import read_network
from campus_sim.planning import CostSnapshot, DijkstraPlanner, NoPath, RoadEdge, RoadGraph
from campus_sim.unity_coverage import UnityCoverage

TARGETS = ("hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station")
MODES = {
    "service_only": {"service"},
    "local_roads": {
        "service",
        "residential",
        "unclassified",
        "living_street",
        "tertiary",
        "tertiary_link",
    },
    "including_arterials": {
        "service",
        "residential",
        "unclassified",
        "living_street",
        "tertiary",
        "tertiary_link",
        "secondary",
        "secondary_link",
    },
}
INPUTS = {
    "expanded_osm": "Docs/MapResearch/2026-09-17/expanded_campus.osm",
    "core_unity": "Assets/InhaCampus/Source/campus.json",
    "scoped_unity": "Assets/CampusSim/Data/road_connections_scoped.json",
    "restored_unity": "Assets/CampusSim/Data/road_connections_restored.json",
    "extension_export": "Assets/CampusSim/Data/road_extensions.json",
    "entrances": "Docs/MapResearch/Iterations/EntranceInventory/entrances.csv",
    "dorm3_draft": "Assets/CampusSim/Data/dormitory_three_draft.json",
    "policy": "configs/osm_runtime.json",
    "scene": "Assets/CampusSim/Scenes/CampusTerrain.unity",
    "audit_code": "backend/src/campus_sim/connection_audit.py",
    "planner_code": "backend/src/campus_sim/planning.py",
    "runtime_manifest": "maps/inha_osm_runtime/manifest.json",
    "osm_reader_code": "backend/src/campus_sim/osm_roads.py",
    "coverage_code": "backend/src/campus_sim/unity_coverage.py",
    "coordinate_code": "backend/src/campus_sim/map_coordinates.py",
}


def nearest_ways(point, nodes, segments, limit=6):
    by_way = {}
    for segment in segments:
        gap, ratio, xy = projection(point, nodes[segment.u], nodes[segment.v])
        candidate = (gap, segment.index, ratio, xy, segment)
        if segment.way_id not in by_way or candidate[:2] < by_way[segment.way_id][:2]:
            by_way[segment.way_id] = candidate
    return sorted(by_way.values(), key=lambda item: (item[0], item[4].way_id))[:limit]


def query_graph(nodes, segments, home, candidate, speed_mps):
    cuts = defaultdict(dict)
    points = dict(nodes)
    identifiers = []
    for item in (home, candidate):
        _, _, ratio, xy, segment = item
        if ratio <= 1e-10:
            identifier = segment.u
        elif ratio >= 1 - 1e-10:
            identifier = segment.v
        else:
            ratio = round(ratio, 12)
            identifier = f"split:{segment.way_id}:{segment.index}:{ratio}"
            points[identifier] = xy
            cuts[segment.key][ratio] = identifier
        identifiers.append(identifier)
    edges = []
    for segment in segments:
        chain = [segment.u] + [node for _, node in sorted(cuts[segment.key].items())] + [segment.v]
        speed = (
            min(speed_mps, float(segment.tags["maxspeed"]) / 3.6)
            if "maxspeed" in segment.tags
            else speed_mps
        )
        for part, (a, b) in enumerate(pairwise(chain)):
            for direction in segment.directions:
                u, v = (a, b) if direction == 1 else (b, a)
                edges.append(
                    RoadEdge(
                        u,
                        v,
                        f"{segment.way_id}:{segment.index}:{part}:{direction}",
                        (points[u], points[v]),
                        math.dist(points[u], points[v]),
                        speed,
                    )
                )
    graph = RoadGraph("expanded-osm-diagnostic-v1", points, tuple(edges), synthetic=True)
    costs = CostSnapshot.travel_time(graph)
    planner = DijkstraPlanner()
    return (
        graph,
        planner.search(graph, *identifiers, costs),
        planner.search(graph, *reversed(identifiers), costs),
    )


def route_summary(route):
    if isinstance(route, NoPath):
        return {"reachable": False}
    ways = []
    for edge in route.edge_ids:
        way = edge[2].split(":")[0]
        if not ways or ways[-1] != way:
            ways.append(way)
    return {
        "reachable": True,
        "length_m": route.length_m,
        "travel_time_s": route.cost_s,
        "osm_way_sequence": ways,
    }


def unity_export_alternative(nodes, segments, home, candidate, speed, coverage):
    """Conservatively exclude whole ways with uncovered route samples.

    This finds a reviewable alternative, not a globally optimal mesh-constrained
    route: removing an entire way may exclude other supported portions of it.
    """
    excluded = set()
    for _ in range(8):
        if candidate[4].way_id in excluded or home[4].way_id in excluded:
            return None
        retained = [s for s in segments if s.way_id not in excluded]
        graph, outward, returning = query_graph(nodes, retained, home, candidate, speed)
        if isinstance(outward, NoPath) or isinstance(returning, NoPath):
            return None
        out_coverage = coverage.route(outward, graph)
        return_coverage = coverage.route(returning, graph)
        missing = set(out_coverage["missing_way_ids"] + return_coverage["missing_way_ids"])
        if not missing:
            return {
                "outward": route_summary(outward),
                "return": route_summary(returning),
                "outward_unity_coverage": out_coverage,
                "return_unity_coverage": return_coverage,
                "excluded_uncovered_way_ids": sorted(excluded),
                "limitation": "whole-way exclusion and sampled centerline coverage; not active-mesh or global optimum verification",
            }
        excluded.update(missing)
    return None


def audit_connections(root):
    root = Path(root)
    coordinates = MapCoordinates()
    nodes, all_segments, excluded = read_network(root / INPUTS["expanded_osm"], coordinates)
    current = load_map_bundle(root / "maps/inha_osm_runtime", source_root=root)
    home_xy = current.graph.nodes[current.home_node]
    home_way = next(
        s["osm_way_id"]
        for s in current.metadata["stop_candidates"]
        if s["id"] == current.metadata["home_stop_id"]
    )
    policy = json.loads((root / INPUTS["policy"]).read_text(encoding="utf-8"))
    with (root / INPUTS["entrances"]).open(encoding="utf-8-sig", newline="") as stream:
        anchors = {r["landmark_id"]: r for r in csv.DictReader(stream) if r["role"] == "general"}
    coverage = UnityCoverage(root, coordinates)
    results = []
    for target in TARGETS:
        anchor = anchors[target]
        xy = coordinates.from_unity(float(anchor["unity_x"]), float(anchor["unity_z"]))
        record = {
            "landmark_id": target,
            "entrance_id": anchor["entrance_id"],
            "entrance_status": anchor["verification_status"],
            "operationally_verified": False,
            "nearest_osm_highways": [
                {"gap_m": p[0], "osm_way_id": p[4].way_id, "tags": p[4].tags}
                for p in nearest_ways(xy, nodes, all_segments, 4)
            ],
            "modes": {},
        }
        best = None
        for mode, allowed in MODES.items():
            segments = [s for s in all_segments if s.tags["highway"] in allowed]
            homes = nearest_ways(home_xy, nodes, [s for s in segments if s.way_id == home_way], 1)
            if not homes or homes[0][0] > 0.01:
                raise ValueError("home OSM geometry changed; do not bridge cache versions")
            candidates = []
            for candidate in nearest_ways(xy, nodes, segments):
                graph, outward, returning = query_graph(
                    nodes, segments, homes[0], candidate, policy["default_speed_mps"]
                )
                both = not isinstance(outward, NoPath) and not isinstance(returning, NoPath)
                item = {
                    "osm_way_id": candidate[4].way_id,
                    "gap_m": candidate[0],
                    "tags": candidate[4].tags,
                    "stop_xy_m": candidate[3],
                    "within_stop_gap": candidate[0] <= policy["max_stop_gap_m"],
                    "outward": route_summary(outward),
                    "return": route_summary(returning),
                    "round_trip": both,
                }
                candidates.append(item)
                if best is None and both and item["within_stop_gap"]:
                    route_ways = set(
                        item["outward"]["osm_way_sequence"] + item["return"]["osm_way_sequence"]
                    )
                    tags_by_way = {s.way_id: s.tags for s in segments}
                    best = {
                        **item,
                        "diagnostic_mode": mode,
                        "route_highway_classes": sorted(
                            {tags_by_way[w]["highway"] for w in route_ways}
                        ),
                        "outward_unity_coverage": coverage.route(outward, graph),
                        "return_unity_coverage": coverage.route(returning, graph),
                        "outward_edge_ids": outward.edge_ids,
                        "return_edge_ids": returning.edge_ids,
                    }
                    if (
                        best["outward_unity_coverage"]["missing_way_ids"]
                        or best["return_unity_coverage"]["missing_way_ids"]
                    ):
                        best["unity_export_backed_alternative"] = unity_export_alternative(
                            nodes,
                            segments,
                            homes[0],
                            candidate,
                            policy["default_speed_mps"],
                            coverage,
                        )
            record["modes"][mode] = candidates
        record["best_candidate"] = best
        record["finding"] = (
            "DIRECTED_ROUND_TRIP_CANDIDATE" if best else "NO_SUPPORTED_ROUND_TRIP_WITHIN_GAP"
        )
        results.append(record)
    return {
        "schema_version": 1,
        "kind": "connection_candidates_not_service_approval",
        "runtime_geometry_changed": False,
        "speed_assumption_kmh": round(policy["default_speed_mps"] * 3.6, 6),
        "stop_gap_assumption_m": policy["max_stop_gap_m"],
        "sources": {
            name: {"path": path, "sha256": digest(root / path)} for name, path in INPUTS.items()
        },
        "excluded_ways": excluded,
        "targets": results,
        "limitations": [
            "No invented edges; Stop candidates lie on an original OSM segment.",
            "General entrance anchors remain unverified; no step-free or safe boarding claim.",
            "Road-class modes are diagnostics, not changes to runtime allowed_highways.",
            "Turn-restriction member ways are conservatively excluded, not ignored.",
            "Unity export coverage is not live Editor or mesh/collider/grade validation.",
            "Missing cache connectivity does not prove a real-world road is absent.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = audit_connections(args.project_root)
    output = args.output or args.project_root / "artifacts/osm_connection_audit.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    write_json(output, report)
    for target in report["targets"]:
        best = target["best_candidate"]
        print(
            json.dumps(
                {
                    "target": target["landmark_id"],
                    "finding": target["finding"],
                    "candidate": None
                    if best is None
                    else {
                        key: best[key]
                        for key in (
                            "osm_way_id",
                            "gap_m",
                            "diagnostic_mode",
                            "route_highway_classes",
                        )
                    },
                },
                ensure_ascii=False,
            )
        )


if __name__ == "__main__":
    main()
