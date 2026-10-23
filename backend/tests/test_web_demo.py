import math
from itertools import pairwise

import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.domain import CreateRequest
from campus_sim.web_demo import DemoControl, DemoSession, point_along, route_position


def command(identifier="web", **changes):
    values = {
        "command_id": identifier,
        "owner_id": "web-demo",
        "service_type": "PASSENGER",
        "pickup_landmark_id": "main_gate",
        "dropoff_landmark_id": "hitech",
    }
    return CreateRequest(**(values | changes))


def session():
    wall = [100.0]
    return DemoSession(wall_clock=lambda: wall[0]), wall


def test_clock_pause_speed_and_resume_do_not_jump_or_double_count():
    demo, wall = session()
    wall[0] += 20
    assert demo.state()["time_s"] == 0
    demo.control(DemoControl(paused=False, speed=10))
    wall[0] += 2
    assert demo.state()["time_s"] == 20
    demo.control(DemoControl(speed=100))
    wall[0] += 1
    assert demo.control(DemoControl(paused=True))["time_s"] == 120
    wall[0] += 100
    assert demo.state()["time_s"] == 120
    demo.control(DemoControl(paused=False))
    wall[0] += 1
    assert demo.state()["time_s"] == 220


def test_mid_route_vehicle_uses_dijkstra_geometry_not_straight_line_to_stop():
    demo, wall = session()
    request = demo.submit(command()).request
    trip = demo.service.trips[request.id]
    demo.control(DemoControl(paused=False, speed=1))
    wall[0] += trip.delivery_route.cost_s * 0.4
    state = demo.state()
    vehicle = state["vehicles"][0]
    expected = route_position(trip.delivery_route, state["time_s"], demo.service)
    assert vehicle["phase"] == "TO_DROPOFF"
    assert vehicle["position"] == pytest.approx(expected)
    assert sum(s["duration_s"] for s in vehicle["trajectory"]) == pytest.approx(trip.returns_at_s)
    assert vehicle["soc"] < 1
    assert vehicle["routes"][-1]["phase"] == "RETURNING"
    assert point_along([(0, 0), (0, 10), (10, 10)], 0.75) == [5, 10]


def test_station_detour_trajectory_contains_each_original_return_edge():
    demo, _ = session()
    request = demo.submit(command(dropoff_landmark_id="inha_station")).request
    trip = demo.service.trips[request.id]
    vehicle = demo.state()["vehicles"][0]
    assert len(vehicle["trajectory"]) == sum(
        len(r.edge_ids) for r in (trip.pickup_route, trip.delivery_route, trip.return_route)
    )
    assert trip.return_route.length_m == pytest.approx(407.157469, abs=0.01)
    assert all(a["points"][-1] == b["points"][0] for a, b in pairwise(vehicle["trajectory"]))


def test_extensions_fifo_and_large_jump_completes_all_three_vehicle_trips():
    demo, wall = session()
    state = demo.scenario("extensions")
    assert not state["paused"] and state["speed"] == 10
    assert len(state["requests"]) == 5 and len(state["queue"]) == 2
    assert [r["vehicle_id"] for r in state["requests"][:3]] == ["V01", "V02", "V03"]
    wall[0] += 40
    state = demo.state()
    assert state["completed"] == 5
    assert all(v["phase"] in {"RETURNING", "CHARGING"} for v in state["vehicles"])
    wall[0] += 1000
    state = demo.state()
    assert all(v["phase"] == "READY" for v in state["vehicles"])
    assert not state["queue"]
    assert any("정문 복귀" in e["text"] for e in state["events"])


def test_battery_demo_shows_charging_then_fifo_reentry_and_energy_conservation():
    demo, wall = session()
    demo.scenario("battery")
    wall[0] += 6
    state = demo.state()
    assert all(v["phase"] == "CHARGING" for v in state["vehicles"])
    assert len(state["queue"]) == 18 - state["completed"]
    assert not state["vehicle_fifo"]
    positions = {tuple(v["position"]) for v in state["vehicles"]}
    assert positions == {
        tuple(demo.service.graph.nodes[n]) for n in demo.service.home_nodes.values()
    }
    assert len(positions) == 3
    wall[0] += 194
    state = demo.state()
    assert state["completed"] == 18
    assert len(state["charging_sessions"]) >= 3
    assert all(c["completed"] for c in state["charging_sessions"])
    assert all(v["phase"] == "READY" and v["soc"] == pytest.approx(1) for v in state["vehicles"])
    assert any("가용 차량 재진입" in e["text"] for e in state["events"])
    assert set(state["vehicle_fifo"]) == {"V01", "V02", "V03"}


def test_reset_is_paused_and_clears_missions_energy_and_elapsed_time():
    demo, wall = session()
    before = demo.scenario("battery")
    wall[0] += 10
    demo.advance()
    reset = demo.reset()
    assert reset["run_id"] != before["run_id"]
    assert reset["paused"] and reset["time_s"] == 0 and reset["speed"] == 10
    assert not reset["requests"] and not reset["events"] and not reset["charging_sessions"]
    assert all(v["soc"] == 1 for v in reset["vehicles"])
    assert reset["vehicle_fifo"] == ["V01", "V02", "V03"]


def test_offline_map_contains_source_osm_buildings_roads_and_fourteen_stop_candidates():
    demo, _ = session()
    data = demo.map
    assert len(data["stops"]) == 14 and len(data["roads"]) > 100
    assert data["buildings"] and data["context_roads"]
    assert not data["operationally_verified"]
    assert "OpenStreetMap" in data["attribution"]
    for stop in data["stops"]:
        landmark = demo.service.landmarks[stop["id"]]
        assert (
            stop["point"] == demo.service.graph.nodes[demo.service.stop_nodes[landmark.stop_ids[0]]]
        )
    assert all(math.isfinite(n) for item in data["buildings"] for p in item["points"] for n in p)


def test_http_page_assets_commands_validation_and_isolation():
    demo, wall = session()
    app = create_app()
    app.state.demo = demo
    with TestClient(app) as client:
        assert client.get("/").url.path == "/demo/"
        html = client.get("/demo/")
        assert html.status_code == 200 and 'lang="ko"' in html.text
        for asset in ("app.js", "style.css", "icons.svg"):
            assert client.get(f"/demo/{asset}").status_code == 200
        assert "https://" not in client.get("/demo/app.js").text
        assert len(client.get("/v1/demo/map").json()["stops"]) == 14
        for payload in ({"speed": 0}, {"speed": 301}, {"paused": "false"}, {"typo": True}):
            assert client.post("/v1/demo/control", json=payload).status_code == 422
        assert client.post("/v1/demo/scenario", json={"name": "bad"}).status_code == 422
        invalid = command().model_dump(mode="json") | {"dropoff_landmark_id": "main_gate"}
        assert client.post("/v1/demo/requests", json=invalid).status_code == 422
        for i in range(4):
            assert (
                client.post(
                    "/v1/demo/requests", json=command(str(i)).model_dump(mode="json")
                ).status_code
                == 201
            )
        queued = client.get("/v1/demo/state").json()["queue"]
        assert len(queued) == 1
        assert client.post(f"/v1/demo/requests/{queued[0]['id']}/cancel").status_code == 200
        assert not client.get("/v1/demo/state").json()["queue"]
        assert not app.state.service.requests  # The legacy API's state is preserved.
        client.post("/v1/demo/control", json={"paused": False, "speed": 100})
        wall[0] += 100
        assert client.get("/v1/demo/state").json()["completed"] == 3
        assert client.post("/v1/demo/reset").json()["time_s"] == 0
