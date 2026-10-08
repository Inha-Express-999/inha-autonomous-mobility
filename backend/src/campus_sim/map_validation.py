"""Technical inspection of map inputs without granting campus routing authority."""

from __future__ import annotations

import json
from hashlib import sha256
from pathlib import Path
from typing import Any

from campus_sim.map_package import inspect_review_package
from campus_sim.road_graph import RoadGraphLoadError, load_road_graph


def inspect_map(path: str | Path) -> dict[str, Any]:
    """Report what the current loader can validate, preserving a fail-closed MVP gate."""
    map_path = Path(path)
    if map_path.is_dir():
        return inspect_review_package(map_path)
    try:
        source_bytes = map_path.read_bytes()
        raw = json.loads(source_bytes)
    except OSError as error:
        return _unavailable(map_path, "map_unreadable", str(error))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        return _unavailable(map_path, "map_json_invalid", str(error))
    if not isinstance(raw, dict):
        return _unavailable(map_path, "map_root_not_object")

    status = raw.get("data_status")
    if status == "CANDIDATE_ONLY":
        return _unavailable(map_path, "candidate_only_not_routable", data_status=status)
    if status != "SYNTHETIC_FIXTURE":
        return _unavailable(map_path, "unsupported_or_unverified_map_status", data_status=status)
    try:
        graph = load_road_graph(map_path)
    except RoadGraphLoadError as error:
        return _unavailable(map_path, "road_graph_contract_invalid", str(error), status)

    parent = {node.id: node.id for node in graph.nodes}

    def find(node_id: str) -> str:
        while parent[node_id] != node_id:
            parent[node_id] = parent[parent[node_id]]
            node_id = parent[node_id]
        return node_id

    for edge in graph.edges:
        parent[find(edge.to_node)] = find(edge.from_node)
    component_sizes: dict[str, int] = {}
    for node_id in parent:
        root = find(node_id)
        component_sizes[root] = component_sizes.get(root, 0) + 1

    return {
        "report_type": "map_input_inspection",
        "path": str(map_path),
        "map_id": graph.map_id,
        "map_version": graph.map_version,
        "data_status": graph.data_status,
        "graph_sha256": sha256(source_bytes).hexdigest(),
        "road_graph_contract_valid": True,
        "mvp_map_ready": False,
        "reason": "synthetic_fixture_only",
        "node_count": len(graph.nodes),
        "stop_node_count": sum(node.stop_id is not None for node in graph.nodes),
        "junction_node_count": sum(node.stop_id is None for node in graph.nodes),
        "directed_edge_count": len(graph.edges),
        "weak_component_sizes_desc": sorted(component_sizes.values(), reverse=True),
        "limitations": [
            "The current RoadGraph loader accepts synthetic fixtures only.",
            "This check does not verify campus access, pedestrian connectivity, CRS, or Stop approval.",
        ],
    }


def _unavailable(
    path: Path, reason: str, detail: str | None = None, data_status: object = None
) -> dict[str, Any]:
    report = {
        "report_type": "map_input_inspection",
        "path": str(path),
        "data_status": data_status,
        "road_graph_contract_valid": False,
        "mvp_map_ready": False,
        "reason": reason,
    }
    if detail is not None:
        report["detail"] = detail
    return report
