import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.dispatch_comparison import DispatchComparison, compare_dispatch
from campus_sim.domain import CreateRequest
from campus_sim.web_demo import DemoControl, DemoSession


def command(index, **updates):
    return CreateRequest(
        command_id=f"batch-{index}",
        owner_id="test",
        service_type="PASSENGER",
        pickup_landmark_id="main_gate",
        dropoff_landmark_id="hitech",
        **updates,
    )


def test_batch_validation_does_not_partially_queue_and_matching_is_one_batch():
    session = DemoSession()
    service = session.service
    invalid = command(2).model_copy(update={"dropoff_landmark_id": "fake"})
    with pytest.raises(ValueError):
        service.create_batch([command(1), invalid])
    assert not service.requests
    with pytest.raises(ValueError, match="duplicate"):
        service.create_batch([command(1), command(1)])
    session.control(DemoControl(dispatch="hungarian"))
    service.create_batch([command(i) for i in range(5)])
    assigned = [tick for tick in service.dispatch_log if tick["assignments"]]
    assert len(assigned) == 1 and len(assigned[0]["assignments"]) == 3
    trips = tuple(service.trips.values())
    session.control(DemoControl(dispatch="greedy"))
    assert tuple(service.trips.values()) == trips
    assert all(t.dispatch_algorithm == "hungarian" for t in trips)
    session.reset()
    assert session.state()["dispatch"] == "greedy"


def test_comparison_repeats_identical_conditions_and_records_unserviceable_request():
    session = DemoSession()
    # Structural overcapacity must stay unassigned in all policies.
    session.submit(command(1, party_size=100))
    snapshot = tuple(session.service.requests.values())
    report = compare_dispatch(DispatchComparison(source="current", repeats=2), snapshot)
    assert report["request_count"] == 1
    assert all(r["unassigned"] == 1 and r["completed"] == 0 for r in report["results"])
    assert all(r["mean_pickup_s"] is None for r in report["results"])
    assert all(v["available"] for v in report["conditions"]["vehicles"])


def test_mixed_workload_completion_metrics_and_repeat_reproducibility():
    command_spec = DispatchComparison(source="mixed", repeats=2)
    report = compare_dispatch(command_spec)
    again = compare_dispatch(DispatchComparison(source="mixed", repeats=1))
    assert report["condition_hash"] == again["condition_hash"]
    assert [r["assignments"] for r in report["results"]] == [
        r["assignments"] for r in again["results"]
    ]
    for result in report["results"]:
        assert result["completed"] == 12 and result["unassigned"] == 0
        assert result["deadline_count"] == 4
        assert result["dispatch_p95_ms"] >= result["dispatch_p50_ms"] >= 0
        assert result["distance_m"] >= result["empty_distance_m"] > 0
        assert result["mean_pickup_s"] >= result["mean_wait_s"] >= 0
        assert result["return_s"] >= result["completion_s"] > 0
        assert 0 <= result["on_time_rate"] <= 1


def test_comparison_http_isolation_validation_and_assets():
    with TestClient(create_app()) as client:
        initial = client.get("/v1/demo/state").json()
        assert (
            client.post("/v1/demo/dispatch-comparison", json={"source": "current"}).status_code
            == 422
        )
        response = client.post(
            "/v1/demo/dispatch-comparison", json={"source": "extensions", "repeats": 1}
        )
        assert response.status_code == 200
        assert all(r["completed"] == 5 for r in response.json()["results"])
        for payload in ({"repeats": 0}, {"repeats": 6}, {"source": "fake"}):
            assert client.post("/v1/demo/dispatch-comparison", json=payload).status_code == 422
        final = client.get("/v1/demo/state").json()
        assert final["run_id"] == initial["run_id"] and final["requests"] == initial["requests"]
        assert client.get("/demo/comparison.html").status_code == 200
        assert client.get("/demo/inha-mobil-icon.png").headers["content-type"] == "image/png"
        assert client.post("/v1/demo/control", json={"dispatch": "bad"}).status_code == 422
