from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import math
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict, deque
from itertools import pairwise
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
NON_VEHICLE_HIGHWAYS = {
    "bridleway",
    "cycleway",
    "footway",
    "pedestrian",
    "steps",
}
VEHICLE_REVIEW_HIGHWAYS = {
    "living_street",
    "motorway",
    "motorway_link",
    "primary",
    "primary_link",
    "residential",
    "road",
    "secondary",
    "secondary_link",
    "service",
    "tertiary",
    "tertiary_link",
    "track",
    "trunk",
    "trunk_link",
    "unclassified",
}
DENY_VALUES = {"no"}
RESTRICTED_VALUES = {"customers", "destination", "delivery", "private"}
DEFAULT_SOURCE = ROOT / "Assets/InhaCampus/Source/campus.osm"
DEFAULT_SOURCE_URL = "https://www.openstreetmap.org/api/0.6/map?bbox=126.648,37.445,126.659,37.454"
DEFAULT_SOURCE_QUERY_BBOX = {
    "west_lon": 126.648,
    "south_lat": 37.445,
    "east_lon": 126.659,
    "north_lat": 37.454,
}
REVIEW_FIELDS = (
    "observed_vehicle_access",
    "observed_direction",
    "measured_width_m",
    "measured_grade_percent",
    "surface_condition",
    "intersection_checked",
    "evidence_type",
    "evidence_reference",
    "reviewer",
    "reviewed_on",
    "notes",
)


def review_template_csv(dataset: dict[str, Any]) -> str:
    """Create a GIS-joinable field review worksheet without approving any edge."""
    fields = (
        "candidate_segment_id",
        "source_sha256",
        "source_way_osm_id",
        "from_osm_node_id",
        "to_osm_node_id",
        "source_node_index_start",
        "source_node_index_end",
        "highway_tag",
        "oneway_tag",
        "access_tag",
        "vehicle_tag",
        "motor_vehicle_tag",
        "motorcar_tag",
        "bridge_tag",
        "tunnel_tag",
        "layer_tag",
        "surface_tag",
        "width_tag",
        "maxspeed_tag",
        "incline_tag",
        "geometry_wgs84_json",
        "verification_status",
        "routable",
        "review_decision",
        *REVIEW_FIELDS,
    )
    ways_by_id = {way["source_ref"]["id"]: way for way in dataset["ways"]}
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=fields, lineterminator="\n")
    writer.writeheader()
    for segment in dataset["candidate_topology"]["segments"]:
        way = ways_by_id[segment["source_way_ref"]["id"]]
        tags = way["tags"]
        row: dict[str, Any] = {
            "candidate_segment_id": segment["id"],
            "source_sha256": dataset["source"]["sha256"],
            "source_way_osm_id": way["source_ref"]["id"],
            "from_osm_node_id": segment["from_node_id"].removeprefix("osm-node-"),
            "to_osm_node_id": segment["to_node_id"].removeprefix("osm-node-"),
            "source_node_index_start": segment["source_node_index_range"][0],
            "source_node_index_end": segment["source_node_index_range"][1],
            "highway_tag": tags.get("highway", ""),
            "oneway_tag": tags.get("oneway", ""),
            "geometry_wgs84_json": json.dumps(
                [[point["lon"], point["lat"]] for point in segment["geometry_wgs84"]],
                separators=(",", ":"),
            ),
            "verification_status": "UNVERIFIED",
            "routable": "false",
            "review_decision": "",
        }
        for key in ("access", "vehicle", "motor_vehicle", "motorcar", "bridge", "tunnel",
                    "layer", "surface", "width", "maxspeed", "incline"):
            row[f"{key}_tag"] = tags.get(key, "")
        row.update({field: "" for field in REVIEW_FIELDS})
        # OSM is external data. Prefix spreadsheet formula-like text so the CSV
        # remains inert when opened in common spreadsheet applications.
        for key, value in row.items():
            if isinstance(value, str) and value.startswith(("=", "+", "-", "@", "\t", "\r")):
                row[key] = "'" + value
        writer.writerow(row)
    return output.getvalue()


def classify_way(
    tags: dict[str, str], missing_node_refs: list[str], node_ref_count: int
) -> tuple[str, list[str]]:
    highway = tags.get("highway", "")
    reasons: list[str] = []
    access_tags = ("motor_vehicle", "motorcar", "vehicle", "access")
    denied_by = [key for key in access_tags if tags.get(key, "").lower() in DENY_VALUES]
    restricted_by = [key for key in access_tags if tags.get(key, "").lower() in RESTRICTED_VALUES]

    if missing_node_refs:
        reasons.append("one_or_more_referenced_osm_nodes_are_missing")
        return "INCOMPLETE_GEOMETRY", reasons
    if node_ref_count < 2:
        reasons.append("way_has_fewer_than_two_node_refs")
        return "INCOMPLETE_GEOMETRY", reasons
    if denied_by:
        reasons.extend(f"{key}=no" for key in denied_by)
        return "EXPLICIT_ACCESS_DENY", reasons
    if restricted_by:
        reasons.extend(f"{key}={tags[key]}_requires_policy_review" for key in restricted_by)
        return "RESTRICTED_ACCESS_REVIEW", reasons
    if highway in NON_VEHICLE_HIGHWAYS:
        return "NON_VEHICLE_HIGHWAY", [f"highway={highway}"]
    if highway in VEHICLE_REVIEW_HIGHWAYS:
        reasons.append(f"highway={highway}_is_only_a_candidate_classification")
        if not any(key in tags for key in access_tags):
            reasons.append("vehicle_access_tags_missing")
        return "VEHICLE_ACCESS_REVIEW", reasons
    return "OTHER_HIGHWAY_REVIEW", [f"highway={highway or 'missing'}_not_approved"]


def build_dataset(source_path: Path) -> dict[str, Any]:
    source_bytes = source_path.read_bytes()
    has_default_source_metadata = source_path.resolve() == DEFAULT_SOURCE.resolve()
    source_query_bbox = DEFAULT_SOURCE_QUERY_BBOX if has_default_source_metadata else None
    root = ET.fromstring(source_bytes)
    nodes = {
        element.attrib["id"]: {
            "lat": float(element.attrib["lat"]),
            "lon": float(element.attrib["lon"]),
            "tags": {
                tag.attrib["k"]: tag.attrib["v"]
                for tag in element.findall("tag")
            },
        }
        for element in root.findall("node")
    }
    ways: list[dict[str, Any]] = []
    counts: Counter[str] = Counter()

    for way in sorted(root.findall("way"), key=lambda item: int(item.attrib["id"])):
        tags = {
            tag.attrib["k"]: tag.attrib["v"]
            for tag in way.findall("tag")
        }
        if "highway" not in tags:
            continue
        node_refs = [node.attrib["ref"] for node in way.findall("nd")]
        missing_node_refs = [node_ref for node_ref in node_refs if node_ref not in nodes]
        review_status, reasons = classify_way(tags, missing_node_refs, len(node_refs))
        counts[review_status] += 1
        geometry = [
            {"lat": nodes[ref]["lat"], "lon": nodes[ref]["lon"]}
            for ref in node_refs
            if ref in nodes
        ]
        outside_bbox_count = (
            sum(
                not (
                    source_query_bbox["west_lon"] <= point["lon"] <= source_query_bbox["east_lon"]
                    and source_query_bbox["south_lat"] <= point["lat"] <= source_query_bbox["north_lat"]
                )
                for point in geometry
            )
            if source_query_bbox is not None
            else None
        )
        ways.append(
            {
                "id": f"osm-way-{way.attrib['id']}",
                "source_ref": {"type": "way", "id": way.attrib["id"]},
                "tags": dict(sorted(tags.items())),
                "node_refs": node_refs,
                "geometry_wgs84": geometry,
                "vertices_outside_source_query_bbox": outside_bbox_count,
                "missing_node_refs": missing_node_refs,
                "review_status": review_status,
                "review_reasons": reasons,
                "routable": False,
            }
        )

    candidate_ways = [way for way in ways if way["review_status"] == "VEHICLE_ACCESS_REVIEW"]
    referenced_node_refs = {
        node_ref for way in candidate_ways for node_ref in way["node_refs"]
    }
    source_node_tag_evidence = [
        {
            "source_ref": {"type": "node", "id": node_ref},
            "position_wgs84": {"lat": nodes[node_ref]["lat"], "lon": nodes[node_ref]["lon"]},
            "tags": dict(sorted(nodes[node_ref]["tags"].items())),
        }
        for node_ref in sorted(referenced_node_refs & nodes.keys(), key=int)
        if nodes[node_ref]["tags"]
    ]
    way_ids_by_node: dict[str, set[str]] = defaultdict(set)
    for way in candidate_ways:
        way_id = way["source_ref"]["id"]
        for node_ref in set(way["node_refs"]):
            way_ids_by_node[node_ref].add(way_id)
    shared_node_refs = {
        node_ref for node_ref, way_ids in way_ids_by_node.items() if len(way_ids) > 1
    }

    topology_nodes: dict[str, dict[str, Any]] = {}
    topology_edges: list[dict[str, Any]] = []
    for way in candidate_ways:
        refs = way["node_refs"]
        cut_indices = sorted(
            {0, len(refs) - 1}
            | {index for index, ref in enumerate(refs) if ref in shared_node_refs}
        )
        for left_index, right_index in pairwise(cut_indices):
            if right_index <= left_index:
                continue
            from_ref = refs[left_index]
            to_ref = refs[right_index]
            segment_refs = refs[left_index : right_index + 1]
            for node_ref in (from_ref, to_ref):
                node = nodes[node_ref]
                topology_nodes.setdefault(
                    node_ref,
                    {
                        "id": f"osm-node-{node_ref}",
                        "source_ref": {"type": "node", "id": node_ref},
                        "position_wgs84": {"lat": node["lat"], "lon": node["lon"]},
                        "tags": dict(sorted(node["tags"].items())),
                        "candidate_way_count": len(way_ids_by_node[node_ref]),
                        "verification_status": "UNVERIFIED",
                    },
                )

            oneway = way["tags"].get("oneway")
            if oneway == "yes":
                direction_status = "FORWARD_ONLY_CANDIDATE"
            elif oneway == "no":
                direction_status = "BIDIRECTIONAL_CANDIDATE"
            elif oneway == "-1":
                direction_status = "REVERSE_ONLY_CANDIDATE"
            else:
                direction_status = "DIRECTION_UNKNOWN"
            topology_edges.append(
                {
                    "id": f"{way['id']}-segment-{left_index:03d}-{right_index:03d}",
                    "source_way_ref": way["source_ref"],
                    "from_node_id": f"osm-node-{from_ref}",
                    "to_node_id": f"osm-node-{to_ref}",
                    "source_node_index_range": [left_index, right_index],
                    "geometry_wgs84": [
                        {"lat": nodes[ref]["lat"], "lon": nodes[ref]["lon"]}
                        for ref in segment_refs
                    ],
                    "direction_status": direction_status,
                    "oneway_tag": oneway,
                    "verification_status": "UNVERIFIED",
                    "routable": False,
                }
            )

    adjacency: dict[str, set[str]] = defaultdict(set)
    for edge in topology_edges:
        start = edge["from_node_id"]
        end = edge["to_node_id"]
        adjacency[start].add(end)
        adjacency[end].add(start)
    component_sizes: list[int] = []
    visited: set[str] = set()
    for node_id in adjacency:
        if node_id in visited:
            continue
        queue = deque([node_id])
        visited.add(node_id)
        component_size = 0
        while queue:
            current = queue.popleft()
            component_size += 1
            for neighbor in adjacency[current] - visited:
                visited.add(neighbor)
                queue.append(neighbor)
        component_sizes.append(component_size)

    source_hash = hashlib.sha256(source_bytes).hexdigest()
    source = {
        "path": source_path.relative_to(ROOT).as_posix()
        if source_path.is_relative_to(ROOT)
        else str(source_path),
        "sha256": source_hash,
        "license": "OpenStreetMap contributors, ODbL 1.0",
    }
    if has_default_source_metadata:
        source["url"] = DEFAULT_SOURCE_URL
        source["query_bbox_wgs84"] = source_query_bbox
    return {
        "dataset_id": "inha-campus-osm-road-candidates",
        "data_status": "CANDIDATE_ONLY",
        "verification_status": "UNVERIFIED",
        "coordinate_frame": "WGS84_LON_LAT",
        "source": source,
        "extraction": {
            "tool": "AgentScripts/MapData/extract_osm_road_candidates.py",
            "policy": "Preserve OSM highway ways and tags; do not infer vehicle access, direction, width, speed, or service approval.",
        },
        "summary": {
            "highway_way_count": len(ways),
            "routable_edge_count": 0,
            "review_status_counts": dict(sorted(counts.items())),
            "ways_with_vertices_outside_source_query_bbox": sum(
                way["vertices_outside_source_query_bbox"] is not None
                and way["vertices_outside_source_query_bbox"] > 0
                for way in ways
            ) if source_query_bbox is not None else None,
            "vehicle_review_ways_with_vertices_outside_source_query_bbox": sum(
                way["review_status"] == "VEHICLE_ACCESS_REVIEW"
                and way["vertices_outside_source_query_bbox"] is not None
                and way["vertices_outside_source_query_bbox"] > 0
                for way in ways
            ) if source_query_bbox is not None else None,
        },
        "source_node_tag_evidence": source_node_tag_evidence,
        "candidate_topology": {
            "coordinate_frame": "WGS84_LON_LAT",
            "connection_rule": "Split only at shared OSM node IDs used by at least two distinct vehicle-review ways; do not snap by proximity.",
            "shared_candidate_node_count": len(shared_node_refs),
            "candidate_node_count": len(topology_nodes),
            "candidate_segment_count": len(topology_edges),
            "weak_component_sizes_desc": sorted(component_sizes, reverse=True),
            "nodes": [topology_nodes[node_ref] for node_ref in sorted(topology_nodes, key=int)],
            "segments": topology_edges,
            "limitations": [
                "Shared OSM node IDs indicate a topology candidate, not an approved or safe crossing.",
                "Bridge, tunnel, layer, access, width, incline, surface, and vehicle permission have not been adjudicated.",
                "Unspecified oneway values remain unknown; no direction defaults are inferred.",
                "All topology nodes and segments remain unverified and non-routable.",
            ],
        },
        "limitations": [
            "OSM highway classification does not establish campus vehicle permission or safe autonomous-vehicle access.",
            "Missing access, width, speed, surface, incline, and one-way tags remain unknown; no values are inferred.",
            "Only shared OSM node IDs between candidate vehicle ways are split; this artifact is not a topologically validated RoadGraph.",
            "All entries have routable=false and must not be loaded by the runtime planner.",
        ],
        "ways": ways,
    }


def to_feature_collection(features: list[dict[str, Any]], dataset: dict[str, Any]) -> dict[str, Any]:
    collection = {
        "type": "FeatureCollection",
        "name": dataset["dataset_id"],
        "data_status": dataset["data_status"],
        "verification_status": dataset["verification_status"],
        "source_sha256": dataset["source"]["sha256"],
        "coordinate_frame": "WGS84 longitude, latitude",
        "features": features,
    }
    if "query_bbox_wgs84" in dataset["source"]:
        collection["source_query_bbox_wgs84"] = dataset["source"]["query_bbox_wgs84"]
    return collection


def clip_line_to_bbox(
    coordinates: list[list[float]], bbox: dict[str, float]
) -> list[list[list[float]]]:
    """Clip a display-only LineString to the OSM query bbox using Liang-Barsky."""
    west, south = bbox["west_lon"], bbox["south_lat"]
    east, north = bbox["east_lon"], bbox["north_lat"]
    runs: list[list[list[float]]] = []
    current: list[list[float]] = []

    def flush() -> None:
        nonlocal current
        if len(current) >= 2:
            runs.append(current)
        current = []

    for start, end in pairwise(coordinates):
        x1, y1 = start
        x2, y2 = end
        dx, dy = x2 - x1, y2 - y1
        t_min, t_max = 0.0, 1.0
        constraints = (
            (-dx, x1 - west),
            (dx, east - x1),
            (-dy, y1 - south),
            (dy, north - y1),
        )
        visible = True
        for p, q in constraints:
            if p == 0:
                if q < 0:
                    visible = False
                    break
                continue
            ratio = q / p
            if p < 0:
                if ratio > t_max:
                    visible = False
                    break
                t_min = max(t_min, ratio)
            else:
                if ratio < t_min:
                    visible = False
                    break
                t_max = min(t_max, ratio)
        if not visible or t_min > t_max:
            flush()
            continue

        clipped_start = [x1 + t_min * dx, y1 + t_min * dy]
        clipped_end = [x1 + t_max * dx, y1 + t_max * dy]
        if math.dist(clipped_start, clipped_end) <= 1e-12:
            flush()
            continue
        if current and math.dist(current[-1], clipped_start) <= 1e-12:
            if math.dist(current[-1], clipped_end) > 1e-12:
                current.append(clipped_end)
        else:
            flush()
            current = [clipped_start, clipped_end]
    flush()
    return runs


def geojson_outputs(dataset: dict[str, Any]) -> dict[str, dict[str, Any]]:
    way_features = []
    for way in dataset["ways"]:
        coordinates = [[point["lon"], point["lat"]] for point in way["geometry_wgs84"]]
        if len(coordinates) < 2:
            continue
        tags = way["tags"]
        way_features.append(
            {
                "type": "Feature",
                "id": way["id"],
                "geometry": {"type": "LineString", "coordinates": coordinates},
                "properties": {
                    "osm_way_id": way["source_ref"]["id"],
                    "review_status": way["review_status"],
                    "verification_status": "UNVERIFIED",
                    "routable": False,
                    "highway": tags.get("highway"),
                    "oneway": tags.get("oneway"),
                    "access": tags.get("access"),
                    "vehicle": tags.get("vehicle"),
                    "motor_vehicle": tags.get("motor_vehicle"),
                    "vertices_outside_source_query_bbox": way[
                        "vertices_outside_source_query_bbox"
                    ],
                    "tags_json": json.dumps(tags, ensure_ascii=False, sort_keys=True),
                },
            }
        )

    topology = dataset["candidate_topology"]
    node_features = [
        {
            "type": "Feature",
            "id": node["id"],
            "geometry": {
                "type": "Point",
                "coordinates": [
                    node["position_wgs84"]["lon"],
                    node["position_wgs84"]["lat"],
                ],
            },
            "properties": {
                "osm_node_id": node["source_ref"]["id"],
                "candidate_way_count": node["candidate_way_count"],
                "verification_status": node["verification_status"],
                "tags_json": json.dumps(node["tags"], ensure_ascii=False, sort_keys=True),
            },
        }
        for node in topology["nodes"]
    ]
    segment_features = [
        {
            "type": "Feature",
            "id": segment["id"],
            "geometry": {
                "type": "LineString",
                "coordinates": [
                    [point["lon"], point["lat"]]
                    for point in segment["geometry_wgs84"]
                ],
            },
            "properties": {
                "osm_way_id": segment["source_way_ref"]["id"],
                "from_node_id": segment["from_node_id"],
                "to_node_id": segment["to_node_id"],
                "direction_status": segment["direction_status"],
                "oneway": segment["oneway_tag"],
                "verification_status": segment["verification_status"],
                "routable": segment["routable"],
            },
        }
        for segment in topology["segments"]
    ]
    tagged_node_features = [
        {
            "type": "Feature",
            "id": item["source_ref"]["id"],
            "geometry": {
                "type": "Point",
                "coordinates": [
                    item["position_wgs84"]["lon"],
                    item["position_wgs84"]["lat"],
                ],
            },
            "properties": {
                "osm_node_id": item["source_ref"]["id"],
                "verification_status": "UNVERIFIED",
                "tags_json": json.dumps(item["tags"], ensure_ascii=False, sort_keys=True),
            },
        }
        for item in dataset["source_node_tag_evidence"]
    ]
    outputs = {
        "ways": to_feature_collection(way_features, dataset),
        "topology_nodes": to_feature_collection(node_features, dataset),
        "topology_segments": to_feature_collection(segment_features, dataset),
        "tagged_nodes": to_feature_collection(tagged_node_features, dataset),
    }
    query_bbox = dataset["source"].get("query_bbox_wgs84")
    if query_bbox is not None:
        clipped_ways = _clipped_way_features(dataset["ways"], query_bbox)
        clipped_segments = _clipped_segment_features(topology["segments"], query_bbox)
        for layer_name, features in (
            ("ways_query_bbox_display", clipped_ways),
            ("topology_segments_query_bbox_display", clipped_segments),
        ):
            collection = to_feature_collection(features, dataset)
            collection["display_clip_bbox_wgs84"] = query_bbox
            collection["display_only"] = True
            outputs[layer_name] = collection
    return outputs


def _clipped_way_features(
    ways: list[dict[str, Any]], bbox: dict[str, float]
) -> list[dict[str, Any]]:
    features = []
    for way in ways:
        coordinates = [[point["lon"], point["lat"]] for point in way["geometry_wgs84"]]
        runs = clip_line_to_bbox(coordinates, bbox)
        if not runs:
            continue
        geometry = (
            {"type": "LineString", "coordinates": runs[0]}
            if len(runs) == 1
            else {"type": "MultiLineString", "coordinates": runs}
        )
        features.append(
            {
                "type": "Feature",
                "id": way["id"],
                "geometry": geometry,
                "properties": {
                    "osm_way_id": way["source_ref"]["id"],
                    "review_status": way["review_status"],
                    "verification_status": "UNVERIFIED",
                    "routable": False,
                    "geometry_scope": "CLIPPED_TO_SOURCE_QUERY_BBOX_DISPLAY_ONLY",
                },
            }
        )
    return features


def _clipped_segment_features(
    segments: list[dict[str, Any]], bbox: dict[str, float]
) -> list[dict[str, Any]]:
    features = []
    for segment in segments:
        coordinates = [
            [point["lon"], point["lat"]]
            for point in segment["geometry_wgs84"]
        ]
        runs = clip_line_to_bbox(coordinates, bbox)
        if not runs:
            continue
        geometry = (
            {"type": "LineString", "coordinates": runs[0]}
            if len(runs) == 1
            else {"type": "MultiLineString", "coordinates": runs}
        )
        features.append(
            {
                "type": "Feature",
                "id": segment["id"],
                "geometry": geometry,
                "properties": {
                    "osm_way_id": segment["source_way_ref"]["id"],
                    "source_from_node_id": segment["from_node_id"],
                    "source_to_node_id": segment["to_node_id"],
                    "source_node_index_range": segment["source_node_index_range"],
                    "direction_status": segment["direction_status"],
                    "verification_status": "UNVERIFIED",
                    "routable": False,
                    "geometry_scope": "CLIPPED_TO_SOURCE_QUERY_BBOX_DISPLAY_ONLY",
                },
            }
        )
    return features


def main() -> None:
    parser = argparse.ArgumentParser(description="Extract unapproved OSM highway ways for map review.")
    parser.add_argument(
        "--input",
        type=Path,
        default=ROOT / "Assets/InhaCampus/Source/campus.osm",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=ROOT / "maps/candidates/inha-campus-osm-road-candidates.json",
    )
    parser.add_argument(
        "--review-template-output",
        type=Path,
        help="CSV field-review worksheet path (default: alongside --output)",
    )
    args = parser.parse_args()
    dataset = build_dataset(args.input.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(dataset, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    review_path = args.review_template_output or args.output.with_name(
        f"{args.output.stem}-field-review.csv"
    )
    review_path.parent.mkdir(parents=True, exist_ok=True)
    review_path.write_text(review_template_csv(dataset), encoding="utf-8-sig", newline="")
    geojson_paths = {}
    for layer_name, collection in geojson_outputs(dataset).items():
        geojson_path = args.output.with_name(f"{args.output.stem}-{layer_name}.geojson")
        geojson_path.write_text(
            json.dumps(collection, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        geojson_paths[layer_name] = str(geojson_path)
    print(json.dumps(dataset["summary"], ensure_ascii=False, indent=2))
    print(f"Wrote candidate-only review data: {args.output}")
    print(f"Wrote unverified field-review worksheet: {review_path}")
    print(json.dumps({"geojson_layers": geojson_paths}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
