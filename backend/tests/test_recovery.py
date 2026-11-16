import sqlite3

import pytest
from test_physical_dispatch import observe, request, setup

from campus_sim.domain import CreateRequest
from campus_sim.recovery import RecoveryStore
from campus_sim.web_demo import DemoSession


def attach(s, path):
    s.recovery = RecoveryStore(path)
    return s


def test_request_ack_survives_restart_before_vehicle_connects(tmp_path):
    path = tmp_path / "state.db"
    s = DemoSession(recovery_path=path)
    command = CreateRequest(
        command_id="once",
        owner_id="test",
        service_type="PASSENGER",
        pickup_landmark_id="library",
        dropoff_landmark_id="hitech",
    )
    ack = s.submit(command)
    restored = DemoSession(recovery_path=path)
    assert restored.run_id != s.run_id
    assert not restored.physical.vehicles
    assert restored.submit(command).request.id == ack.request.id
    assert len(restored.service.requests) == 1
    assert not restored.service.trips


@pytest.mark.parametrize(
    "phase", ["TO_PICKUP", "PICKUP_SERVICE", "TO_DROPOFF", "DROPOFF_SERVICE", "RETURNING"]
)
def test_all_phases_wait_for_fresh_pose_then_replan(tmp_path, phase):
    s, p, token, wall = setup()
    attach(s, tmp_path / "state.db")
    r = request(s)
    job = s.physical_dispatch.jobs[r.id]
    for _ in range(250):
        if job.phase == phase:
            break
        observe(s, p, token, wall, arrive=True)
    assert job.phase == phase
    s.checkpoint()
    old_pose = p.vehicles["V01"].ego
    restored = DemoSession(wall_clock=lambda: wall[0], recovery_path=tmp_path / "state.db")
    restored.physical.clock = lambda: wall[0]
    restored.advance()
    new_job = restored.physical_dispatch.jobs[r.id]
    assert new_job.phase == phase
    assert not restored.physical.vehicles
    assert new_job.reason == "WAITING_FRESH_PHYSICS"
    new_token = restored.physical.connect("V01")
    restored.advance()
    assert restored.physical.vehicles["V01"].mission_id is None
    ego = old_pose.model_copy(update={"run_id": restored.run_id, "tick": 1})
    restored.physical.observation("V01", new_token, ego)
    restored.advance()
    assert restored.physical.vehicles["V01"].mission_id
    assert new_job.mission_id != job.mission_id
    assert len(restored.service.requests) == 1
    assert restored.service.requests[r.id].status == r.status


def test_corrupt_or_incompatible_checkpoint_refuses_empty_start(tmp_path):
    path = tmp_path / "state.db"
    s = DemoSession(recovery_path=path)
    s.checkpoint()
    with sqlite3.connect(path) as db:
        db.execute("UPDATE checkpoint SET payload='{}'")
    with pytest.raises(ValueError, match="RECOVERY_LOAD_FAILED"):
        DemoSession(recovery_path=path)
    with sqlite3.connect(path) as db:
        assert db.execute("SELECT payload FROM checkpoint").fetchone()[0] == "{}"


def test_storage_failure_blocks_later_control(tmp_path, monkeypatch):
    s, p, token, _wall = setup()
    attach(s, tmp_path / "state.db")
    request(s)

    def fail(*args, **kwargs):
        raise OSError("disk unavailable")

    monkeypatch.setattr(sqlite3, "connect", fail)
    with pytest.raises(ValueError, match="RECOVERY_STORAGE_FAILED"):
        s.checkpoint()
    with pytest.raises(ValueError, match="RECOVERY_STORAGE_FAILED"):
        p.observation("V01", token, p.vehicles["V01"].ego.model_copy(update={"tick": 2}))


def test_recovered_trip_finishes_once_and_can_restart_again(tmp_path):
    s, p, token, wall = setup()
    attach(s, tmp_path / "state.db")
    r = request(s)
    job = s.physical_dispatch.jobs[r.id]
    for _ in range(200):
        if job.phase == "TO_DROPOFF":
            break
        observe(s, p, token, wall, arrive=True)
    pose = p.vehicles["V01"].ego
    restored = DemoSession(wall_clock=lambda: wall[0], recovery_path=tmp_path / "state.db")
    restored.physical.clock = lambda: wall[0]
    token = restored.physical.connect("V01")
    restored.physical.observation(
        "V01", token, pose.model_copy(update={"run_id": restored.run_id, "tick": 1})
    )
    restored.advance()
    for _ in range(250):
        if restored.physical_dispatch.jobs[r.id].returned:
            break
        observe(restored, restored.physical, token, wall, arrive=True)
    assert restored.physical_dispatch.jobs[r.id].returned
    assert restored.state()["completed"] == 1
    again = DemoSession(recovery_path=tmp_path / "state.db")
    assert again.state()["completed"] == 1
    assert again.physical_dispatch.jobs[r.id].returned


def test_incompatible_geometry_is_rejected(tmp_path, monkeypatch):
    path = tmp_path / "state.db"
    DemoSession(recovery_path=path).checkpoint()
    original = RecoveryStore.identity
    monkeypatch.setattr(
        RecoveryStore, "identity", staticmethod(lambda s: {**original(s), "geometry": "different"})
    )
    with pytest.raises(ValueError, match="RECOVERY_LOAD_FAILED"):
        DemoSession(recovery_path=path)


def test_committed_request_survives_abrupt_process_exit(tmp_path):
    import subprocess
    import sys

    path = tmp_path / "process.db"
    code = """
import os, sys
from campus_sim.web_demo import DemoSession
from campus_sim.domain import CreateRequest
s=DemoSession(recovery_path=sys.argv[1])
s.submit(CreateRequest(command_id='crash',owner_id='test',service_type='PASSENGER',pickup_landmark_id='library',dropoff_landmark_id='hitech'))
os._exit(17)
"""
    result = subprocess.run([sys.executable, "-c", code, str(path)], timeout=30, check=False)
    assert result.returncode == 17
    restored = DemoSession(recovery_path=path)
    assert len(restored.service.requests) == 1
    assert "crash" in restored.service.commands


def test_api_uses_explicit_recovery_path(tmp_path, monkeypatch):
    from campus_sim.api import create_app

    path = tmp_path / "api.db"
    monkeypatch.setenv("CAMPUS_SIM_RECOVERY_DB", str(path))
    app = create_app()
    command = CreateRequest(
        command_id="api-once",
        owner_id="test",
        service_type="PASSENGER",
        pickup_landmark_id="library",
        dropoff_landmark_id="hitech",
    )
    original = app.state.demo.submit(command)
    other = create_app()
    assert other.state.demo.submit(command).request.id == original.request.id
