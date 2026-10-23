from itertools import pairwise
from math import dist

import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.avoidance_demo import AvoidancePreview
from campus_sim.avoidance_simulation import simulate_avoidance
from campus_sim.local_planning import segment_distance


def test_static_rrt_vehicle_moves_around_obstacle_and_reaches_goal():
    result = simulate_avoidance(AvoidancePreview())
    assert result["arrived"] and not result["collision"]
    assert result["rrt_status"] == "CANDIDATE"
    assert result["duration_s"] > 3
    assert result["minimum_clearance_m"] > 0
    assert max(abs(f["position"][0]) for f in result["frames"]) > 1
    assert dist(result["frames"][-1]["position"], (0, 12)) < 0.25
    assert result["frames"][-1]["speed_mps"] == pytest.approx(0)
    assert result["frames"][-1]["action"] == "ARRIVED"
    for a, b in zip(result["frames"], result["frames"][1:]):
        dt = b["time_s"] - a["time_s"]
        assert dt == pytest.approx(0.05)
        assert -3 * dt - 1e-10 <= b["speed_mps"] - a["speed_mps"] <= dt + 1e-10
        assert segment_distance((0, 6), a["position"], b["position"]) > 1.2


def test_crossing_ttc_brakes_waits_then_resumes_without_fake_zero_velocity():
    result = simulate_avoidance(AvoidancePreview(mode="crossing", x=3, y=3))
    frames = result["frames"]
    assert result["arrived"] and not result["collision"]
    assert result["rrt_status"] == "WAIT_DYNAMIC_VALIDATION"
    assert any(f["action"] == "BRAKING" and f["speed_mps"] > 0 for f in frames)
    waiting = next(
        i for i, f in enumerate(frames) if f["action"] == "WAITING" and f["speed_mps"] < 0.01
    )
    driving = next(i for i in range(waiting + 1, len(frames)) if frames[i]["action"] == "DRIVING")
    assert frames[driving]["time_s"] - frames[waiting]["time_s"] >= 1
    assert frames[-1]["obstacle"][0] < frames[0]["obstacle"][0]
    finite_ttc = [f["ttc_s"] for f in frames if f["ttc_s"] is not None]
    assert len(finite_ttc) > 1 and max(finite_ttc) != min(finite_ttc)
    for a, b in pairwise(frames):
        dt = b["time_s"] - a["time_s"]
        assert -3 * dt - 1e-10 <= b["speed_mps"] - a["speed_mps"] <= dt + 1e-10
        ra = tuple(a["position"][j] - a["obstacle"][j] for j in (0, 1))
        rb = tuple(b["position"][j] - b["obstacle"][j] for j in (0, 1))
        assert segment_distance((0, 0), ra, rb) > 1.2


def test_stale_sensor_stays_stopped_and_unavoidable_collision_is_reported():
    stale = simulate_avoidance(AvoidancePreview(stale=True))
    assert not stale["arrived"] and not stale["collision"]
    assert all(f["position"] == (0, 0) and not f["sensor_valid"] for f in stale["frames"])
    assert stale["rrt_status"] == "STALE_FRAME"
    collision = simulate_avoidance(AvoidancePreview(mode="crossing", x=0, y=3, speed_mps=30 / 3.6))
    assert collision["collision"] and not collision["arrived"]
    assert collision["frames"][-1]["action"] == "COLLISION"
    assert collision["minimum_clearance_m"] <= 0


def test_dynamic_experiment_api_isolated_and_reproducible():
    with TestClient(create_app()) as client:
        initial = client.get("/v1/demo/state").json()
        response = client.post(
            "/v1/demo/avoidance-simulation", json={"mode": "crossing", "x": 3, "y": 3}
        )
        assert response.status_code == 200
        data = response.json()
        assert data["arrived"] and data["simulation_only"] and not data["unity_connected"]
        second = client.post(
            "/v1/demo/avoidance-simulation", json={"mode": "crossing", "x": 3, "y": 3}
        ).json()
        assert data["frames"] == second["frames"]
        final = client.get("/v1/demo/state").json()
        assert final["run_id"] == initial["run_id"] and final["requests"] == initial["requests"]
        assert (
            client.post("/v1/demo/avoidance-simulation", json={"speed_mps": 100}).status_code == 422
        )
