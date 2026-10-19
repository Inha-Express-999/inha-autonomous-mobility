import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app


def test_preview_apis_are_isolated_from_dispatch_state_and_reject_invalid_inputs():
    with TestClient(create_app()) as client:
        original = client.get("/v1/demo/state").json()
        plan = client.post("/v1/demo/planning", json={"pickup": "main_gate", "dropoff": "hitech"})
        assert plan.status_code == 200
        a, d = (plan.json()["results"][key] for key in ("astar", "dijkstra"))
        assert a["cost_s"] == pytest.approx(d["cost_s"])
        assert (
            client.post(
                "/v1/demo/planning", json={"pickup": "fake", "dropoff": "hitech"}
            ).status_code
            == 422
        )
        for payload, expected in (
            ({}, "CANDIDATE"),
            ({"stale": True}, "STALE_FRAME"),
            ({"mode": "crossing", "x": 3, "y": 3}, "WAIT_DYNAMIC_VALIDATION"),
        ):
            response = client.post("/v1/demo/avoidance", json=payload)
            assert response.status_code == 200
            assert response.json()["rrt"]["status"] == expected
            assert not response.json()["rrt"]["executable"]
        assert client.post("/v1/demo/avoidance", json={"x": 100}).status_code == 422
        assert client.post("/v1/demo/control", json={"planner": "unknown"}).status_code == 422
        assert client.get("/demo/avoidance.html").status_code == 200
        state = client.get("/v1/demo/state").json()
        assert state["requests"] == original["requests"] == []
        assert state["run_id"] == original["run_id"]
