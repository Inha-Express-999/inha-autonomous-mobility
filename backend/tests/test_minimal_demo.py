from pathlib import Path

from fastapi.testclient import TestClient

from campus_sim.api import create_app

ROOT = Path(__file__).resolve().parents[2]


def test_local_demo_selects_stops_and_exposes_live_service_route():
    app = create_app(
        map_path=ROOT / "maps/fixtures/physics-integration-3.json",
        minimal_demo=True,
    )
    with TestClient(app) as client:
        page = client.get("/demo")
        assert page.status_code == 200
        assert "시연 시작" in page.text
        before = client.get("/v1/demo/state").json()
        assert len(before["nodes"]) == 3
        assert len(before["snapshot"]["vehicles"]) == 3
        landmark_ids = [item["id"] for item in before["snapshot"]["landmarks"]]
        response = client.post("/v1/requests", json={
            "command_id": "minimal-demo-test",
            "owner_id": "minimal-demo",
            "service_type": "PASSENGER",
            "pickup_landmark_id": landmark_ids[1],
            "dropoff_landmark_id": landmark_ids[2],
            "party_size": 1,
        })
        assert response.status_code == 201
        request_id = response.json()["request"]["id"]
        assigned_vehicle = response.json()["request"]["vehicle_id"]
        initial_x = next(item["position"]["x"] for item in before["snapshot"]["vehicles"]
                         if item["id"] == assigned_vehicle)
        app.state.service.advance(3.5)
        after = client.get("/v1/demo/state").json()["snapshot"]
        assert any(item["id"] == request_id for item in after["requests"])
        assert next(item["position"]["x"] for item in after["vehicles"]
                    if item["id"] == assigned_vehicle) > initial_x
        assert after["routes"]


def test_demo_routes_are_opt_in():
    app = create_app(map_path=ROOT / "maps/fixtures/physics-integration-3.json")
    with TestClient(app) as client:
        assert client.get("/demo").status_code == 404
        assert client.get("/v1/demo/state").status_code == 404


def test_six_stop_demo_exposes_synthetic_map_and_connected_geometry():
    app = create_app(map_path=ROOT / "maps/fixtures/campus-synthetic-6.json", minimal_demo=True)
    with TestClient(app) as client:
        state = client.get("/v1/demo/state").json()
        assert len(state["nodes"]) == 6
        assert state["snapshot"]["mapVersion"] == "synthetic-campus-6stop-v1"
        assert client.get("/health").json()["map_data_status"] == "SYNTHETIC_FIXTURE"
        node_positions = {(node["x"], node["y"]) for node in state["nodes"]}
        assert len(state["edges"]) == 12
        for edge in state["edges"]:
            assert len(edge["points"]) >= 2
            for point in (edge["points"][0], edge["points"][-1]):
                assert (point["x"], point["y"]) in node_positions
