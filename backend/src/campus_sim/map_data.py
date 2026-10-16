"""Read a pinned offline map bundle and fail on missing/changed inputs."""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path

from campus_sim.battery import BatteryPolicy
from campus_sim.domain import Landmark, Stop, Vehicle
from campus_sim.map_builder import digest, read_policy
from campus_sim.planning import RoadEdge, RoadGraph

PAYLOAD_FILES = {
    "graph.json",
    "stops.json",
    "landmarks.json",
    "policy.json",
    "build_report.json",
    "ATTRIBUTION.txt",
}


def default_map_dir():
    configured = os.environ.get("CAMPUS_SIM_MAP_DIR")
    return (
        Path(configured)
        if configured
        else Path(__file__).resolve().parents[3] / "maps/inha_osm_runtime"
    )


@dataclass(frozen=True)
class MapBundle:
    graph: RoadGraph
    landmarks: tuple[Landmark, ...]
    stops: tuple[Stop, ...]
    stop_nodes: dict[str, str]
    home_node: str
    vehicles: tuple[Vehicle, ...]
    battery: BatteryPolicy
    metadata: dict


def load_map_bundle(directory, source_root=None):
    directory = Path(directory)
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    if manifest["schema_version"] != 1 or manifest["routing_mode"] != "osm_geometry_simulation":
        raise ValueError("unsupported runtime map mode")
    if (
        manifest["operationally_verified"] is not False
        or manifest["synthetic_geometry"] is not False
    ):
        raise ValueError("OSM simulation map must keep its unverified geometry provenance")
    if set(manifest["files"]) != PAYLOAD_FILES:
        raise ValueError("missing/unexpected map payload files")
    for name in PAYLOAD_FILES:
        if digest(directory / name) != manifest["files"][name]:
            raise ValueError(f"map payload hash mismatch: {name}")
    if source_root is not None:
        root = Path(source_root).resolve()
        for source in manifest["sources"].values():
            path = (root / source["path"]).resolve()
            if not path.is_relative_to(root) or digest(path) != source["sha256"]:
                raise ValueError("map source hash mismatch; rebuild map bundle")
    policy = read_policy(directory / "policy.json")
    raw = json.loads((directory / "graph.json").read_text(encoding="utf-8"))
    if (
        raw["map_version"] != manifest["map_version"]
        or policy["map_version"] != manifest["map_version"]
    ):
        raise ValueError("map version mismatch")
    if raw["synthetic"] is not True or any(e["verified"] is not False for e in raw["edges"]):
        raise ValueError("unverified OSM geometry requires explicit simulation assumptions")
    graph = RoadGraph(
        raw["map_version"],
        raw["nodes"],
        tuple(RoadEdge(**edge) for edge in raw["edges"]),
        synthetic=True,
    )
    landmarks_raw = json.loads((directory / "landmarks.json").read_text(encoding="utf-8"))
    stops_raw = json.loads((directory / "stops.json").read_text(encoding="utf-8"))
    landmarks = tuple(
        Landmark(id=l["id"], name=l["name"], stop_ids=l["stop_ids"]) for l in landmarks_raw
    )
    stops = tuple(
        Stop(id=s["id"], landmark_id=s["landmark_id"], step_free_access=False) for s in stops_raw
    )
    if len({l.id for l in landmarks}) != len(landmarks) or len({s.id for s in stops}) != len(stops):
        raise ValueError("duplicate map landmark/Stop")
    stop_nodes = {s["id"]: s["node_id"] for s in stops_raw}
    stop_ids = {s.id for s in stops}
    landmark_ids = {l.id for l in landmarks}
    if (
        any(s.landmark_id not in landmark_ids for s in stops)
        or any(stop not in stop_ids for landmark in landmarks for stop in landmark.stop_ids)
        or any(node not in graph.nodes for node in stop_nodes.values())
    ):
        raise ValueError("invalid map Landmark/Stop/node reference")
    by_stop = {s["id"]: s for s in stops_raw}
    for landmark in landmarks:
        if any(by_stop[stop]["landmark_id"] != landmark.id for stop in landmark.stop_ids):
            raise ValueError("Stop belongs to a different landmark")
    if manifest["home_stop_id"] not in stop_nodes:
        raise ValueError("missing home Stop")
    report = json.loads((directory / "build_report.json").read_text(encoding="utf-8"))
    metadata = {
        **manifest,
        "coverage": report,
        "stop_candidates": stops_raw,
        "landmark_coverage": landmarks_raw,
        "source_hashes_checked": source_root is not None,
    }
    vehicles = tuple(Vehicle(**v) for v in policy["vehicles"])
    if not vehicles or len({v.id for v in vehicles}) != len(vehicles):
        raise ValueError("fleet must have unique vehicle ids")
    return MapBundle(
        graph,
        landmarks,
        stops,
        stop_nodes,
        stop_nodes[manifest["home_stop_id"]],
        vehicles,
        BatteryPolicy(**policy["battery"]),
        metadata,
    )
