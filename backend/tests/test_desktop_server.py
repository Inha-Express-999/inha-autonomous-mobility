import io
import os
from types import SimpleNamespace

from fastapi.testclient import TestClient

from campus_sim import __version__
from campus_sim.api import create_app
from campus_sim.desktop_server import watch_parent


def test_desktop_identity_is_the_same_web_session_and_map():
    with TestClient(create_app()) as client:
        info = client.get("/v1/desktop/info").json()
        state = client.get("/v1/demo/state").json()
        projection = client.get("/v1/demo/unity-map").json()
        assert info["service_id"] == "inha-mobil-local-demo"
        assert info["schema_version"] == 1 and info["map_schema_version"] == 1
        assert info["run_id"] == state["run_id"]
        assert info["geometry_hash"] == projection["geometry_hash"]
        assert info["process_id"] == os.getpid()
        assert info["project_version"] == __version__
        assert info["simulation_only"] is True
        assert client.get("/v1/demo/state").json() == state


def test_parent_shutdown_command_and_closed_pipe_both_stop_only_owned_server():
    for text in ("shutdown\n", "", "ignored\nshutdown\n"):
        server = SimpleNamespace(should_exit=False)
        watch_parent(io.StringIO(text), server)
        assert server.should_exit is True
