from __future__ import annotations

import json
import sys
from hashlib import sha256
from unittest.mock import patch

import pytest
from pyproj import Transformer

from campus_sim.cli import main
from campus_sim.map_validation import inspect_map


def _write_review_package(directory) -> dict:
    source = directory / "source.osm"
    source.write_bytes(b"test-only source bytes")
    def point(x, y):
        return {"x": x, "y": y}

    documents = {
        "vehicle_graph": {
            "nodes": [
                {"id": "v1", "position_m": point(0, 0)},
                {"id": "v2", "position_m": point(10, 0)},
                {"id": "v3", "position_m": point(0, 10)},
            ],
            "edges": [
                {
                    "id": edge_id, "from_node": source, "to_node": target, "key": "0",
                    "geometry_m": [point(*start), point(*end)],
                    "length_m": ((end[0] - start[0]) ** 2 + (end[1] - start[1]) ** 2) ** 0.5,
                    "width_m": 4, "allowed_speed_mps": 5,
                    "allowed_vehicle_classes": ["TEST_SHUTTLE"],
                    "access_status": "REVIEWED_ALLOWED", "evidence_refs": ["test-only"],
                }
                for edge_id, source, target, start, end in [
                    ("v12", "v1", "v2", (0, 0), (10, 0)),
                    ("v21", "v2", "v1", (10, 0), (0, 0)),
                    ("v13", "v1", "v3", (0, 0), (0, 10)),
                    ("v31", "v3", "v1", (0, 10), (0, 0)),
                    ("v32", "v3", "v2", (0, 10), (10, 0)),
                    ("v23", "v2", "v3", (10, 0), (0, 10)),
                ]
            ],
        },
        "pedestrian_graph": {
            "nodes": [
                {"id": "p1", "position_m": point(0, 1)},
                {"id": "p2", "position_m": point(0, 2)},
                {"id": "p3", "position_m": point(10, 1)},
                {"id": "p4", "position_m": point(10, 2)},
            ],
            "edges": [
                {"id": "p12", "from_node": "p1", "to_node": "p2", "key": "0",
                 "geometry_m": [point(0, 1), point(0, 2)], "length_m": 1,
                 "width_m": 2, "grade_percent": 0, "step_free": True,
                 "evidence_refs": ["test-only"]},
                {"id": "p21", "from_node": "p2", "to_node": "p1", "key": "0",
                 "geometry_m": [point(0, 2), point(0, 1)], "length_m": 1,
                 "width_m": 2, "grade_percent": 0, "step_free": True,
                 "evidence_refs": ["test-only"]},
                {"id": "p34", "from_node": "p3", "to_node": "p4", "key": "0",
                 "geometry_m": [point(10, 1), point(10, 2)], "length_m": 1,
                 "width_m": 2, "grade_percent": 0, "step_free": True,
                 "evidence_refs": ["test-only"]},
                {"id": "p43", "from_node": "p4", "to_node": "p3", "key": "0",
                 "geometry_m": [point(10, 2), point(10, 1)], "length_m": 1,
                 "width_m": 2, "grade_percent": 0, "step_free": True,
                 "evidence_refs": ["test-only"]},
            ],
        },
        "landmarks": {"items": [
            {"id": "l1", "name": "Test entrance A", "stop_ids": ["s1"],
             "required_facility_key": "main_gate",
             "evidence_refs": ["test-only"]},
            {"id": "l2", "name": "Test entrance B", "stop_ids": ["s2"],
             "required_facility_key": "rear_gate",
             "evidence_refs": ["test-only"]},
        ]},
        "stops": {"items": [
            {"id": "s1", "landmark_id": "l1", "vehicle_node_id": "v1",
             "pedestrian_stop_node_id": "p1", "pedestrian_entrance_node_id": "p2",
             "entrance_id": "test-door-1", "step_free_access": True,
             "evidence_refs": ["test-only"]},
            {"id": "s2", "landmark_id": "l2", "vehicle_node_id": "v2",
             "pedestrian_stop_node_id": "p3", "pedestrian_entrance_node_id": "p4",
             "entrance_id": "test-door-2", "step_free_access": True,
             "evidence_refs": ["test-only"]},
        ]},
        "zones": {"items": [{
            "id": "biryong_plaza_front",
            "polygon_m": [point(4, -1), point(6, -1), point(6, 1), point(4, 1)],
            "vehicle_edge_ids": ["v12", "v21"], "waiting_stop_ids": ["s1"],
            "evidence_refs": ["test-only"],
        }]},
    }
    artifacts = {}
    for name, document in documents.items():
        path = directory / f"{name}.json"
        path.write_text(json.dumps({"map_version": "test-review-v1", **document}), encoding="utf-8")
        artifacts[name] = {"path": path.name, "sha256": sha256(path.read_bytes()).hexdigest()}
    manifest = {
        "schema_version": 1,
        "map_id": "test-review",
        "map_version": "test-review-v1",
        "data_status": "REVIEW_PACKAGE",
        "coordinate_frame": "LOCAL_METERS",
        "crs": "EPSG:3857",
        "origin_wgs84": {"lon": 126.65, "lat": 37.45},
        "source_files": [{
            "path": source.name,
            "sha256": sha256(source.read_bytes()).hexdigest(),
            "retrieved_on": "2026-09-28",
            "source_description": "test-only bytes",
        }],
        "artifacts": artifacts,
        "review_evidence_refs": ["test-only; no field approval"],
    }
    forward = Transformer.from_crs("EPSG:4326", "EPSG:3857", always_xy=True)
    inverse = Transformer.from_crs("EPSG:3857", "EPSG:4326", always_xy=True)
    origin_x, origin_y = forward.transform(126.65, 37.45)
    manifest["coordinate_controls"] = []
    for node_id, x, y in [("v1", 0, 0), ("v2", 10, 0), ("v3", 0, 10)]:
        lon, lat = inverse.transform(origin_x + x, origin_y + y)
        manifest["coordinate_controls"].append({
            "id": f"control-{node_id}", "vehicle_node_id": node_id,
            "wgs84": {"lon": lon, "lat": lat}, "local_m": point(x, y),
        })
    (directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return manifest


def _rewrite_artifact(directory, manifest: dict, name: str, document: dict) -> None:
    path = directory / f"{name}.json"
    path.write_text(json.dumps(document), encoding="utf-8")
    manifest["artifacts"][name]["sha256"] = sha256(path.read_bytes()).hexdigest()
    (directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")


def test_review_package_integrity_does_not_grant_map_authority(tmp_path) -> None:
    _write_review_package(tmp_path)
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is True
    assert report["review_content_valid"] is True
    assert report["coordinate_contract_valid"] is True
    assert report["road_graph_contract_valid"] is False
    assert report["mvp_map_ready"] is False
    assert report["reason"] == "review_package_unapproved"
    assert report["required_facility_count"] == 2
    assert report["required_facility_coverage_complete"] is False
    assert "biryong_plaza" in report["required_facilities_missing"]
    assert report["artifact_names"] == [
        "landmarks", "pedestrian_graph", "stops", "vehicle_graph", "zones"
    ]


def test_review_package_cli_still_fails_mvp_gate(tmp_path, capsys) -> None:
    _write_review_package(tmp_path)
    with (
        patch.object(sys, "argv", [
            "campus-sim", "validate-map", "--map", str(tmp_path), "--allow-synthetic"
        ]),
        pytest.raises(SystemExit) as exit_result,
    ):
        main()
    assert exit_result.value.code == 2
    assert json.loads(capsys.readouterr().out)["package_integrity_valid"] is True


def test_review_package_rejects_tampered_artifact(tmp_path) -> None:
    _write_review_package(tmp_path)
    (tmp_path / "vehicle_graph.json").write_text(
        json.dumps({"map_version": "test-review-v1", "edge": "tampered"}), encoding="utf-8"
    )
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is False
    assert report["reason"] == "package_file_hash_mismatch"


def test_review_package_rejects_path_escape(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    manifest["artifacts"]["zones"]["path"] = "../outside.json"
    (tmp_path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is False
    assert report["reason"] == "manifest_invalid"


def test_review_package_rejects_version_mismatch_even_with_matching_hash(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    path = tmp_path / "stops.json"
    path.write_text(json.dumps({"map_version": "other-version"}), encoding="utf-8")
    manifest["artifacts"]["stops"]["sha256"] = sha256(path.read_bytes()).hexdigest()
    (tmp_path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is False
    assert report["reason"] == "artifact_map_version_mismatch"


def test_review_package_rejects_missing_vehicle_stop_reference(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    stops = json.loads((tmp_path / "stops.json").read_text(encoding="utf-8"))
    stops["items"][0]["vehicle_node_id"] = "missing-node"
    _rewrite_artifact(tmp_path, manifest, "stops", stops)
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is True
    assert report["review_content_valid"] is False
    assert "no vehicle graph node" in report["detail"]


def test_review_package_rejects_unreachable_step_free_entrance(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    pedestrian = json.loads((tmp_path / "pedestrian_graph.json").read_text(encoding="utf-8"))
    pedestrian["edges"][0]["step_free"] = False
    _rewrite_artifact(tmp_path, manifest, "pedestrian_graph", pedestrian)
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is True
    assert report["review_content_valid"] is False
    assert "no suitable pedestrian path" in report["detail"]


def test_review_package_requires_return_pedestrian_path(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    pedestrian = json.loads((tmp_path / "pedestrian_graph.json").read_text(encoding="utf-8"))
    pedestrian["edges"] = [edge for edge in pedestrian["edges"] if edge["id"] != "p21"]
    _rewrite_artifact(tmp_path, manifest, "pedestrian_graph", pedestrian)
    report = inspect_map(tmp_path)
    assert report["review_content_valid"] is False
    assert "both directions" in report["detail"]


def test_review_package_rejects_waiting_stop_inside_zone(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    zones = json.loads((tmp_path / "zones.json").read_text(encoding="utf-8"))
    zones["items"][0]["polygon_m"] = [
        {"x": -1, "y": -1}, {"x": 1, "y": -1},
        {"x": 1, "y": 1}, {"x": -1, "y": 1},
    ]
    _rewrite_artifact(tmp_path, manifest, "zones", zones)
    report = inspect_map(tmp_path)
    assert report["package_integrity_valid"] is True
    assert report["review_content_valid"] is False
    assert "waiting Stop is inside closure polygon" in report["detail"]


def test_review_package_rejects_waiting_stop_on_zone_boundary(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    zones = json.loads((tmp_path / "zones.json").read_text(encoding="utf-8"))
    zones["items"][0]["polygon_m"] = [
        {"x": 0, "y": -1}, {"x": 2, "y": -1},
        {"x": 2, "y": 1}, {"x": 0, "y": 1},
    ]
    _rewrite_artifact(tmp_path, manifest, "zones", zones)
    report = inspect_map(tmp_path)
    assert report["review_content_valid"] is False
    assert "waiting Stop is inside closure polygon" in report["detail"]


def test_review_package_rejects_control_disagreeing_with_graph(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    manifest["coordinate_controls"][1]["local_m"]["x"] = 11
    (tmp_path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    report = inspect_map(tmp_path)
    assert report["reason"] == "coordinate_contract_invalid"
    assert report["coordinate_contract_valid"] is False


def test_review_package_rejects_non_metre_crs(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    manifest["crs"] = "EPSG:2227"
    (tmp_path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    report = inspect_map(tmp_path)
    assert report["reason"] == "coordinate_contract_invalid"
    assert "metres" in report["detail"]


def test_review_package_rejects_missing_control_node(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    manifest["coordinate_controls"][2]["vehicle_node_id"] = "missing"
    (tmp_path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    report = inspect_map(tmp_path)
    assert report["reason"] == "coordinate_contract_invalid"
    assert "no vehicle node" in report["detail"]


def test_review_package_rejects_one_way_stop_disconnection(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    graph = json.loads((tmp_path / "vehicle_graph.json").read_text(encoding="utf-8"))
    graph["edges"] = [edge for edge in graph["edges"] if edge["to_node"] != "v1"]
    _rewrite_artifact(tmp_path, manifest, "vehicle_graph", graph)
    zones = json.loads((tmp_path / "zones.json").read_text(encoding="utf-8"))
    zones["items"][0]["vehicle_edge_ids"] = ["v12"]
    _rewrite_artifact(tmp_path, manifest, "zones", zones)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_content_invalid"
    assert "not mutually reachable" in report["detail"]


def test_review_package_does_not_require_deferred_zone_bypass(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    graph = json.loads((tmp_path / "vehicle_graph.json").read_text(encoding="utf-8"))
    graph["edges"] = [edge for edge in graph["edges"] if edge["id"] not in {"v32", "v23"}]
    _rewrite_artifact(tmp_path, manifest, "vehicle_graph", graph)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_package_unapproved"
    assert report["mvp_map_ready"] is False


def test_review_package_accepts_no_zones_during_mvp(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    zones = json.loads((tmp_path / "zones.json").read_text(encoding="utf-8"))
    zones["items"] = []
    _rewrite_artifact(tmp_path, manifest, "zones", zones)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_package_unapproved"
    assert report["zone_count"] == 0


@pytest.mark.parametrize("edge_ids, discrepancy", [
    (["v12"], "missing=['v21']"),
    (["v12", "v21", "v13"], "extra=['v13']"),
])
def test_review_package_requires_zone_edges_to_match_polygon(
    tmp_path, edge_ids, discrepancy
) -> None:
    manifest = _write_review_package(tmp_path)
    zones = json.loads((tmp_path / "zones.json").read_text(encoding="utf-8"))
    zones["items"][0]["vehicle_edge_ids"] = edge_ids
    _rewrite_artifact(tmp_path, manifest, "zones", zones)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_content_invalid"
    assert discrepancy in report["detail"]


def test_review_package_detects_polyline_crossing_zone_between_vertices(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    graph = json.loads((tmp_path / "vehicle_graph.json").read_text(encoding="utf-8"))
    edge = next(edge for edge in graph["edges"] if edge["id"] == "v13")
    edge["geometry_m"] = [
        {"x": 0, "y": 0}, {"x": 10, "y": 0}, {"x": 0, "y": 10},
    ]
    edge["length_m"] = 25
    _rewrite_artifact(tmp_path, manifest, "vehicle_graph", graph)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_content_invalid"
    assert "missing=['v13']" in report["detail"]


@pytest.mark.parametrize("polygon", [
    [
        {"x": 4, "y": -1}, {"x": 6, "y": 1}, {"x": 4, "y": 1},
        {"x": 6, "y": -1}, {"x": 7, "y": 0},
    ],
    [
        {"x": 4, "y": -1}, {"x": 6, "y": -1}, {"x": 6, "y": 1},
        {"x": 4, "y": 1}, {"x": 4, "y": -1},
    ],
])
def test_review_package_rejects_invalid_zone_polygon(tmp_path, polygon) -> None:
    manifest = _write_review_package(tmp_path)
    zones = json.loads((tmp_path / "zones.json").read_text(encoding="utf-8"))
    zones["items"][0]["polygon_m"] = polygon
    _rewrite_artifact(tmp_path, manifest, "zones", zones)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_content_invalid"
    assert "invalid or self-intersecting polygon" in report["detail"]


def test_review_package_rejects_duplicate_required_facility_key(tmp_path) -> None:
    manifest = _write_review_package(tmp_path)
    landmarks = json.loads((tmp_path / "landmarks.json").read_text(encoding="utf-8"))
    landmarks["items"][1]["required_facility_key"] = "main_gate"
    _rewrite_artifact(tmp_path, manifest, "landmarks", landmarks)
    report = inspect_map(tmp_path)
    assert report["reason"] == "review_content_invalid"
    assert "required facility keys must be unique" in report["detail"]
