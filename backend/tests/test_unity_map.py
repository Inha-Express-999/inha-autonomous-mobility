import json
import math
from pathlib import Path

from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.map_coordinates import MapCoordinates
from campus_sim.service import MobilityService
from campus_sim.unity_map import unity_map

ROOT = Path(__file__).resolve().parents[2]
EXPORT = ROOT / "Assets/CampusSim/Data/server_map.json"


def test_unity_projection_and_export_match_all_web_nodes_hubs_and_stops():
    service = MobilityService.from_osm_map(clock=lambda: 0)
    data = unity_map(service)
    assert data == json.loads(EXPORT.read_text(encoding="utf-8"))
    assert len(data["nodes"]) == 187 and len(data["edges"]) == 402
    assert len(data["hubs"]) == 3 and len(data["stops"]) == 14
    coordinates = MapCoordinates()
    for node in data["nodes"]:
        p = node["position"]
        assert p["y"] == 0
        assert (
            math.dist(coordinates.from_unity(p["x"], p["z"]), service.graph.nodes[node["id"]])
            < 0.01
        )
    by_id = {n["id"]: n["position"] for n in data["nodes"]}
    for anchor in data["hubs"] + data["stops"]:
        assert anchor["position"] == by_id[anchor["node_id"]]
    assert {v for hub in data["hubs"] for v in hub["vehicle_ids"]} == {"V01", "V02", "V03"}
    assert not data["operationally_verified"] and not data["vehicle_actuation"]


def test_unity_endpoint_shares_web_session_and_never_changes_its_controls():
    with TestClient(create_app()) as client:
        original = client.get("/v1/demo/state").json()
        data = client.get("/v1/demo/unity-map").json()
        web = client.get("/v1/demo/map").json()
        after = client.get("/v1/demo/state").json()
        assert original == after
        assert data["map_version"] == web["map_version"] == after["map_version"]
        assert {n["id"] for n in data["nodes"]} == {n["id"] for n in web["nodes"]}
        assert {h["id"]: h["node_id"] for h in data["hubs"]} == {
            h["id"]: h["node_id"] for h in web["hubs"]
        }
        assert data == json.loads(EXPORT.read_text(encoding="utf-8"))
        assert client.post("/v1/demo/unity-map", json={}).status_code == 405


def test_geometry_hash_detects_hub_binding_and_node_changes():
    service = MobilityService.from_osm_map(clock=lambda: 0)
    original = unity_map(service)["geometry_hash"]
    service.home_hubs["V01"] = service.home_hubs["V02"]
    assert unity_map(service)["geometry_hash"] != original
