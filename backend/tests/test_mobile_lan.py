import pytest
from fastapi.testclient import TestClient

from campus_sim.api import create_app
from campus_sim.map_coordinates import MapCoordinates


@pytest.mark.parametrize(
    "arguments,expected", [([], "0.0.0.0"), (["--host", "127.0.0.1"], "127.0.0.1")]
)
def test_pc_owned_server_defaults_to_lan_and_supports_explicit_loopback(
    monkeypatch, arguments, expected
):
    from campus_sim import desktop_server

    configs = []

    class Server:
        def __init__(self, config):
            configs.append(config)

        def run(self):
            pass

    monkeypatch.setenv("CAMPUS_SIM_LISTEN_HOST", "unknown")
    monkeypatch.setattr(desktop_server.sys, "argv", ["desktop-server", *arguments])
    monkeypatch.setattr(desktop_server.uvicorn, "Server", Server)
    desktop_server.main()
    assert configs[0].host == expected


def order(identifier="mobile-one", owner="passenger-one", **changes):
    return dict(
        command_id=identifier,
        owner_id=owner,
        service_type="PASSENGER",
        pickup_landmark_id="library",
        dropoff_landmark_id="hitech",
        **changes,
    )


def test_mobile_client_and_operator_share_requests_and_run_and_isolate_owners():
    # A non-loopback peer uses exactly the same session as the PC operator.
    with TestClient(create_app(), client=("192.168.10.42", 51000)) as mobile:
        run = mobile.get("/v1/desktop/info").json()["run_id"]
        headers = {"X-Campus-Run-Id": run}
        first = mobile.post("/v1/demo/requests", json=order(), headers=headers)
        assert first.status_code == 201
        ack = first.json()
        retry = mobile.post("/v1/demo/requests", json=order(), headers=headers).json()
        assert retry["request"]["id"] == ack["request"]["id"]
        own = mobile.get("/v1/demo/passengers/passenger-one/requests").json()
        assert len(own) == 1 and own[0]["id"] == ack["request"]["id"]
        assert mobile.get("/v1/demo/passengers/passenger-two/requests").json() == []
        state = mobile.get("/v1/demo/state").json()
        assert state["run_id"] == run
        assert [r["id"] for r in state["requests"]] == [ack["request"]["id"]]
        coordinates = MapCoordinates()
        for vehicle in state["vehicles"]:
            expected = coordinates.to_unity(*vehicle["position"])
            assert vehicle["position_unity"] == {"x": expected[0], "y": 0.0, "z": expected[1]}
        cancel = {"owner_id": "passenger-two", "command_id": "cancel-one"}
        endpoint = f"/v1/demo/passengers/requests/{ack['request']['id']}/cancel"
        assert mobile.post(endpoint, json=cancel).status_code == 404
        cancel["owner_id"] = "passenger-one"
        assert mobile.post(endpoint, json=cancel).json()["request"]["status"] == "CANCELLED"
        assert mobile.post(endpoint, json=cancel).json()["request"]["status"] == "CANCELLED"


def test_stale_mobile_command_cannot_enter_restarted_session():
    with TestClient(create_app()) as client:
        run = client.get("/v1/desktop/info").json()["run_id"]
        assert client.post("/v1/demo/reset").status_code == 200
        response = client.post("/v1/demo/requests", json=order(), headers={"X-Campus-Run-Id": run})
        assert response.status_code == 409
        assert client.get("/v1/demo/state").json()["requests"] == []


def test_mobile_service_needs_are_validated_by_same_server():
    with TestClient(create_app()) as client:
        data = order(service_needs={"requires_step_free": False, "wheelchair_slots": 1})
        assert client.post("/v1/demo/requests", json=data).status_code == 422
        assert client.get("/v1/demo/passengers/passenger-one/requests").json() == []
