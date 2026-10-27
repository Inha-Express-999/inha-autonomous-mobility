import json
from pathlib import Path
from shutil import copyfile
from urllib.error import URLError

import pytest
from fastapi.testclient import TestClient

from campus_sim.editor_locations import apply_locations, preview_locations
from campus_sim.map_builder import SOURCE_FILES, build_map, write_json
from campus_sim.service import MobilityService
from campus_sim.service_locations import EMPTY, snap_location, validate_locations
from campus_sim.unity_map import unity_map

ROOT = Path(__file__).resolve().parents[2]


def test_snapper_reuses_existing_stop_cut_and_road_endpoint():
    class Identity:
        @staticmethod
        def from_unity(x, z):
            return x, z

    nodes = {"u": (0, 0), "v": (10, 0), "stop": (10 / 3, 0)}
    segments = [{"u": "u", "v": "v", "key": "road:0"}]
    cuts = {0: {1 / 3: "stop"}}
    node, _, _ = snap_location(
        {"id": "hub", "x": 3.333334, "z": 0.1}, Identity(), nodes, segments, cuts
    )
    assert node == "stop" and len(nodes) == 3
    node, _, _ = snap_location(
        {"id": "hub", "x": 0.005, "z": 0.1}, Identity(), nodes, segments, cuts
    )
    assert node == "u" and len(nodes) == 3


EDIT = {
    "schema_version": 1,
    "stops": [{"id": "library_road_candidate", "x": -46.46, "z": -167.31}],
    "hubs": [{"id": "main_gate", "x": -39.8, "z": -321.4}],
}


@pytest.fixture
def project(tmp_path):
    for relative in SOURCE_FILES.values():
        target = tmp_path / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        copyfile(ROOT / relative, target)
    output = tmp_path / "maps/inha_osm_runtime"
    build_map(tmp_path, output, location_overrides=EMPTY)
    snapshot = tmp_path / "Assets/CampusSim/Data/server_map.json"
    snapshot.parent.mkdir(parents=True, exist_ok=True)
    write_json(snapshot, unity_map(MobilityService.from_osm_map(output)))
    return tmp_path


def test_projection_preview_is_read_only(project):
    snapshot = project / "Assets/CampusSim/Data/server_map.json"
    manifest = project / "maps/inha_osm_runtime/manifest.json"
    before = (snapshot.read_bytes(), manifest.read_bytes())
    preview = preview_locations(project, EDIT)
    assert preview["geometry_hash"] != json.loads(before[0])["geometry_hash"]
    assert preview["locations"]["stops"][0]["x"] != EDIT["stops"][0]["x"]
    assert (snapshot.read_bytes(), manifest.read_bytes()) == before
    assert not (project / "configs/service_locations.json").exists()


def test_preview_hash_change_blocks_publication(project, monkeypatch):
    def offline(*args, **kwargs):
        raise URLError(ConnectionRefusedError(10061, "refused"))

    monkeypatch.setattr("campus_sim.editor_locations.request_json", offline)
    snapshot = project / "Assets/CampusSim/Data/server_map.json"
    before = snapshot.read_bytes()
    with pytest.raises(ValueError, match="projection changed"):
        apply_locations(project, EDIT, expected_preview_hash="0" * 64)
    assert snapshot.read_bytes() == before
    assert not (project / "configs/service_locations.json").exists()


def test_validated_preview_matches_published_map(project, monkeypatch):
    def offline(*args, **kwargs):
        raise URLError(ConnectionRefusedError(10061, "refused"))

    monkeypatch.setattr("campus_sim.editor_locations.request_json", offline)
    preview = preview_locations(project, EDIT)
    result = apply_locations(project, EDIT, expected_preview_hash=preview["geometry_hash"])
    assert result["geometry_hash"] == preview["geometry_hash"]


@pytest.mark.parametrize(
    "document",
    [
        {"schema_version": 2, "stops": [], "hubs": []},
        {"schema_version": 1, "stops": [{"id": "x", "x": float("nan"), "z": 0}], "hubs": []},
        {"schema_version": 1, "stops": [{"id": "x", "x": True, "z": 0}], "hubs": []},
        {"schema_version": 1, "stops": [{"id": "x", "x": 0, "z": 0}] * 2, "hubs": []},
    ],
)
def test_invalid_location_contract(document):
    with pytest.raises(ValueError):
        validate_locations(document)


def test_stop_and_hub_move_preserves_ids_and_round_trip_routes(project):
    output = project / "candidate"
    report = build_map(project, output, location_overrides=EDIT)
    service = MobilityService.from_osm_map(output)
    projection = unity_map(service)
    stop = next(s for s in projection["stops"] if s["id"] == "library_road_candidate")
    assert abs(stop["position"]["x"] - (-47.46)) > 0.1
    assert service.home_nodes["V01"] != "split:481241683:4:0:0.617567128481"
    assert report["round_trip_reachable_stop_count"] == 14
    assert set(service.vehicles) == {"V01", "V02", "V03"}
    assert not projection["operationally_verified"]


@pytest.mark.parametrize(
    "row",
    [
        {"id": "library_road_candidate", "x": 100000, "z": 100000},
        {"id": "unknown_stop", "x": -47, "z": -167},
    ],
)
def test_invalid_move_does_not_publish(project, row):
    original = (project / "maps/inha_osm_runtime/manifest.json").read_bytes()
    with pytest.raises(ValueError):
        build_map(
            project,
            project / "rejected",
            location_overrides={"schema_version": 1, "stops": [row], "hubs": []},
        )
    assert (project / "maps/inha_osm_runtime/manifest.json").read_bytes() == original


def test_editor_transaction_and_repeat_are_stable(project, monkeypatch):
    def offline(*args, **kwargs):
        raise URLError(ConnectionRefusedError(10061, "refused"))

    monkeypatch.setattr("campus_sim.editor_locations.request_json", offline)
    result = apply_locations(project, EDIT)
    from campus_sim.lanes import LaneProfiles

    lanes = json.loads(
        (project / "maps/inha_osm_runtime/lane_profiles.json").read_text(encoding="utf-8")
    )
    assert LaneProfiles(lanes, result["geometry_hash"]).by_edge
    again = apply_locations(project, result["locations"])
    assert again["geometry_hash"] == result["geometry_hash"]
    assert json.loads((project / "configs/service_locations.json").read_text(encoding="utf-8"))[
        "stops"
    ]


def test_busy_or_old_server_rolls_back_all_outputs(project, monkeypatch):
    snapshot = project / "Assets/CampusSim/Data/server_map.json"
    original = snapshot.read_bytes()
    old = json.loads(original)["geometry_hash"]
    manifest = (project / "maps/inha_osm_runtime/manifest.json").read_bytes()

    def request(url, body=None):
        if body is None:
            return {"service_id": "inha-mobil-local-demo", "geometry_hash": old}
        raise ValueError("busy")

    monkeypatch.setattr("campus_sim.editor_locations.request_json", request)
    with pytest.raises(ValueError, match="busy"):
        apply_locations(project, EDIT)
    assert snapshot.read_bytes() == original
    assert (project / "maps/inha_osm_runtime/manifest.json").read_bytes() == manifest
    assert not (project / "configs/service_locations.json").exists()
    assert not (project / "configs/.service_locations.lock").exists()


def test_reload_rejects_remote_origin_stale_and_busy():
    from campus_sim.api import create_app

    app = create_app()
    client = TestClient(app, client=("127.0.0.1", 12345))
    old = client.get("/v1/desktop/info").json()["geometry_hash"]
    body = {"expected_geometry_hash": old, "geometry_hash": "0" * 64}
    assert (
        client.post(
            "/v1/desktop/editor-map", json=body, headers={"Origin": "https://example.com"}
        ).status_code
        == 403
    )
    assert TestClient(app).post("/v1/desktop/editor-map", json=body).status_code == 403
    assert (
        client.post(
            "/v1/desktop/editor-map", json=dict(body, expected_geometry_hash="1" * 64)
        ).status_code
        == 409
    )
    app.state.demo.clock.paused = False
    assert client.post("/v1/desktop/editor-map", json=body).status_code == 409
    run = app.state.demo.run_id
    assert (
        client.post("/v1/desktop/editor-map", json=dict(body, geometry_hash=old)).status_code == 200
    )
    assert app.state.demo.run_id == run


def test_reload_activates_new_session_only_after_hash_check(project, monkeypatch):
    import campus_sim.api as module
    from campus_sim.web_demo import DemoSession

    app = module.create_app()
    client = TestClient(app, client=("127.0.0.1", 12345))
    old = client.get("/v1/desktop/info").json()
    moved = project / "new-map"
    build_map(project, moved, location_overrides=EDIT)
    factory = lambda **kwargs: MobilityService.from_osm_map(moved, **kwargs)
    monkeypatch.setattr(module, "DemoSession", lambda: DemoSession(factory=factory))
    candidate = unity_map(factory())["geometry_hash"]
    response = client.post(
        "/v1/desktop/editor-map",
        json={"expected_geometry_hash": old["geometry_hash"], "geometry_hash": candidate},
    )
    assert response.status_code == 200
    assert response.json()["run_id"] != old["run_id"]
    assert client.get("/v1/demo/unity-map").json()["geometry_hash"] == candidate
