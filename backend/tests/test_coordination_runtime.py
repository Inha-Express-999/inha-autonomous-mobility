import asyncio
import json
import math
from concurrent.futures import Future
from pathlib import Path
from time import monotonic

import pytest
from test_controller import ready_service

from campus_sim.api import create_app
from campus_sim.controller import make_control_command
from campus_sim.coordination_runtime import (
    CoordinationJob,
    CoordinationWorker,
    ServiceCoordination,
    capture_service_job,
    solve_job,
)
from campus_sim.domain import (
    CreateRequest,
    EgoLocalization,
    MapPosition,
    RequestStatus,
    SensorObservation,
)
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]


def options():
    return {"quantum_s": 1, "horizon": 256, "clearance_ticks": 1, "edge_resources": {},
            "node_resources": {}, "provenance": "synthetic async adapter", "node_tolerance_m": 0.1,
            "algorithm": "cbs-disjoint", "limits": {"ct_nodes": 2000, "low_level_expansions": 50000,
                                                     "wall_time_s": 3}}


class ManualExecutor:
    def __init__(self):
        self.calls = []

    def submit(self, function, job):
        future = Future()
        self.calls.append((future, function, job))
        return future

    def finish(self, index=0):
        future, function, job = self.calls[index]
        future.set_result(function(job))


def worker_job():
    service = ready_service()
    context, payload = capture_service_job(service, options())
    return service, context, payload


def test_input_is_serialized_and_same_stationary_pose_with_new_tick_remains_eligible():
    service, first, payload = worker_job()
    pose = service.ego_localizations["V01"]
    service.ego_localizations["V01"] = pose.model_copy(update={"observed_tick": pose.observed_tick + 1})
    frame = service.sensor_observations[("V01", "lidar")]
    service.sensor_observations[("V01", "lidar")] = frame.model_copy(
        update={"observed_tick": frame.observed_tick + 1, "ego_pose_tick": pose.observed_tick + 1})
    assert capture_service_job(service, options())[0] == first
    result = solve_job(CoordinationJob(1, first, 0, payload)).result
    assert result.status == "SUCCESS" and len(result.paths) == 1
    assert not result.executable
    service.graph.edges.clear()
    assert json.loads(payload)["graph"]["edges"]  # Frozen worker input is independent.


@pytest.mark.parametrize("mutation", ["route", "geometry", "speed_profile", "session", "position", "request"])
def test_changed_service_context_revokes_ready_proposal(mutation):
    service, context, payload = worker_job()
    executor = ManualExecutor()
    worker = CoordinationWorker(executor=executor)
    adapter = ServiceCoordination(options(), worker=worker)
    worker.offer(context, payload)
    executor.finish()
    assert adapter.read(service) is not None
    runtime = service.vehicle_runtime["V01"]
    if mutation == "route":
        runtime.route_id += "-new"
    elif mutation == "geometry":
        runtime.route_points[-1] = (999, 999)
    elif mutation == "speed_profile":
        runtime.route_speeds[0] *= 0.5
    elif mutation == "session":
        service.ego_localizations["V01"] = service.ego_localizations["V01"].model_copy(update={"session_id": "new"})
    elif mutation == "position":
        runtime.x += 0.01
    else:
        service.requests[runtime.request_id].status = "CANCELLED"
    assert adapter.read(service) is None


@pytest.mark.parametrize("failure", ["stale", "sensor_invalid", "moving", "off_node"])
def test_safety_or_motion_invalidates_pending_result_without_changing_control(failure):
    service, context, payload = worker_job()
    executor = ManualExecutor()
    adapter = ServiceCoordination(options(), worker=CoordinationWorker(executor=executor))
    adapter.worker.offer(context, payload)
    runtime = service.vehicle_runtime["V01"]
    if failure == "stale":
        service.simulation_time_s += 0.6
    elif failure == "sensor_invalid":
        frame = service.sensor_observations[("V01", "lidar")].model_copy(update={"valid": False, "observed_tick": 2})
        service.record_sensor_observation(frame)
    elif failure == "moving":
        runtime.speed_mps = 0.2
    else:
        runtime.x += 0.5
    before = make_control_command(service, "V01")
    adapter.tick(service)
    executor.finish()
    assert adapter.read(service) is None
    after = make_control_command(service, "V01")
    assert before["targetSpeedMps"] == after["targetSpeedMps"]
    assert before["reason"] == after["reason"]
    if failure in {"stale", "sensor_invalid"}:
        assert after["targetSpeedMps"] == 0


def test_one_inflight_and_latest_pending_replace_superseded_work():
    _service, context, payload = worker_job()
    executor = ManualExecutor()
    worker = CoordinationWorker(executor=executor)
    worker.offer(context, payload, now_s=0)
    for i in range(20):
        worker.offer(str(i), payload, now_s=0.1)
    assert len(executor.calls) == 1
    executor.finish()
    assert worker.poll("19", now_s=0.2) is None
    assert len(executor.calls) == 2 and executor.calls[1][2].context == "19"
    executor.finish(1)
    assert worker.poll("19", now_s=0.3) is not None
    assert any(e["kind"] == "DISCARDED" for e in worker.events)


def test_expired_result_and_worker_error_have_no_authority_and_retry_is_throttled():
    _service, context, payload = worker_job()
    executor = ManualExecutor()
    worker = CoordinationWorker(executor=executor, max_age_s=1)
    worker.offer(context, payload, now_s=0)
    executor.finish()
    assert worker.poll(context, now_s=2) is None
    worker.offer(context, payload, now_s=2)
    executor.calls[1][0].set_exception(RuntimeError("test child failure"))
    assert worker.poll(context, now_s=2.1) is None
    for _ in range(100):
        worker.offer(context, payload, now_s=2.2)
    assert len(executor.calls) == 2
    worker.offer(context, payload, now_s=3.1)
    assert len(executor.calls) == 3


def test_expiry_revokes_an_already_ready_proposal():
    _service, context, payload = worker_job()
    executor = ManualExecutor()
    worker = CoordinationWorker(executor=executor, max_age_s=1)
    worker.offer(context, payload, now_s=0)
    executor.finish()
    assert worker.poll(context, now_s=0.1) is not None
    assert worker.poll(context, now_s=1.1) is None


def test_actual_process_search_runs_while_service_clock_and_sensor_stop_continue():
    async def run():
        config = json.loads((ROOT / "configs/coordination_benchmark.json").read_text(encoding="utf-8"))
        payload = json.dumps({"graph": json.loads((ROOT / "maps/fixtures/physics-reservation-8.json").read_text()),
            "tasks": [{"service_type": "PASSENGER", **t} for t in config["cases"][0]["tasks"]],
            "algorithm": "cbs-disjoint", "limits": {**config["limits"], "wall_time_s": 3},
            "problem": {"quantum_s": config["quantum_s"], "horizon": 30,
                        "clearance_ticks": config["clearance_ticks"], "edge_resources": config["edge_resources"],
                        "node_resources": config["node_resources"], "provenance": config["provenance"]}})
        service = ready_service()
        initial = service.now_s()
        worker = CoordinationWorker(max_age_s=10)
        stop = asyncio.Event()
        clock = asyncio.create_task(service.run_clock(stop))
        try:
            worker.offer("three-vehicle", payload)
            callbacks = 0
            observed_stop_while_busy = False
            deadline = monotonic() + 10
            proposal = None
            while proposal is None and monotonic() < deadline:
                await asyncio.sleep(0.01)
                callbacks += 1
                if not worker._future.done() and service.now_s() > initial + 0.6:
                    observed_stop_while_busy |= make_control_command(service, "V01")["targetSpeedMps"] == 0
                proposal = worker.poll("three-vehicle")
                if worker._future is None and proposal is None:
                    pytest.fail(str(list(worker.events)))
            assert proposal is not None and len(proposal.paths) == 3 and not proposal.executable
            assert callbacks > 3 and service.now_s() > initial + 0.6
            assert observed_stop_while_busy
            assert make_control_command(service, "V01")["targetSpeedMps"] == 0
        finally:
            stop.set()
            await clock
            await worker.close()
    asyncio.run(run())


def test_service_clock_hook_and_shutdown_are_optional_and_owned():
    service = ready_service()
    executor = ManualExecutor()
    adapter = ServiceCoordination(options(), worker=CoordinationWorker(executor=executor))
    service.coordination = adapter
    service.advance(0.05)
    assert len(executor.calls) == 1
    asyncio.run(adapter.close())
    with pytest.raises(RuntimeError, match="closed"):
        adapter.worker.offer("x", "{}")


def test_adapter_requires_a_bounded_search_and_graph_node_assumption():
    bad = options()
    bad["limits"].pop("wall_time_s")
    with pytest.raises(ValueError, match="wall_limit"):
        ServiceCoordination(bad)
    service = ready_service()
    for value in [0, math.inf, True, 1]:
        bad = {**options(), "node_tolerance_m": value}
        with pytest.raises(ValueError):
            capture_service_job(service, bad)


@pytest.mark.parametrize("change", ["claims", "closure", "map", "unplanned"])
def test_reservation_changes_invalidate_service_proposals(change):
    from test_resource_presentation import waiting_service

    service, _ = waiting_service()
    executor = ManualExecutor()
    adapter = ServiceCoordination(options(), worker=CoordinationWorker(executor=executor))
    adapter.tick(service)
    executor.finish()
    assert adapter.read(service) is not None
    book = service.resource_occupancy.book
    if change == "claims":
        token = next(iter(book.leases))
        book.cancel(token, "other", now_s=service.now_s())
    elif change == "closure":
        book.closed.add("corridor")
    elif change == "map":
        book.map_version = "synthetic-other"
    else:
        book.unplanned_occupancy["other"] = {"corridor"}
    assert adapter.read(service) is None


def test_graph_cost_and_closed_edge_are_frozen_in_worker_snapshot():
    service, first, original = worker_job()
    edge = service.graph.edges[0]
    service.graph.edges[0] = edge.model_copy(update={"crowd_penalty_s": 5, "is_open": False})
    changed, payload = capture_service_job(service, options())
    assert changed != first
    before = json.loads(original)["graph"]["edges"][0]
    after = json.loads(payload)["graph"]["edges"][0]
    assert after["is_open"] is False and after["crowd_penalty_s"] == before["crowd_penalty_s"] + 5


def test_app_lifespan_closes_enabled_worker_after_stopping_clock():
    async def run():
        service = ready_service()
        executor = ManualExecutor()
        service.coordination = ServiceCoordination(options(), worker=CoordinationWorker(executor=executor))
        app = create_app(service)
        async with app.router.lifespan_context(app):
            service.coordination.tick(service)
            assert len(executor.calls) == 1
        assert app.state.clock_task.done()
        assert executor.calls[0][0].cancelled()
        with pytest.raises(RuntimeError, match="closed"):
            service.coordination.tick(service)
    asyncio.run(run())



def test_three_service_missions_get_process_proposal_and_invalid_sensor_revokes_it():
    async def run():
        service = MobilityService.from_synthetic_graph(ROOT / "maps/fixtures/physics-reservation-8.json", active_fleet=True)
        config = json.loads((ROOT / "configs/coordination_benchmark.json").read_text(encoding="utf-8"))
        for suffix in ("a", "b", "c"):
            cargo = suffix == "c"
            service.create_request(CreateRequest(command_id=suffix, owner_id=suffix,
                service_type="CARGO" if cargo else "PASSENGER", party_size=0 if cargo else 1,
                cargo_kg=5 if cargo else 0, pickup_landmark_id="reservation_" + suffix,
                dropoff_landmark_id="reservation_finish_" + suffix))
        # Stage already-boarded missions. Service pickup/Physics is covered by the
        # separate integration suite; this test concerns asynchronous proposals.
        for runtime in service.vehicle_runtime.values():
            request = service.requests[runtime.request_id]
            request.status = RequestStatus.IN_TRANSIT
            service._set_route(runtime, request.dropoff_stop_id, request, "TO_DROPOFF")
        service.simulation_time_s = 1.1
        route_ids = {v: r.route_id for v, r in service.vehicle_runtime.items()}
        tick = 0

        def telemetry():
            nonlocal tick
            tick += 1
            for vehicle, runtime in service.vehicle_runtime.items():
                service.record_ego_localization(EgoLocalization(vehicle_id=vehicle, session_id="async-fixture",
                    observed_tick=tick, map_version=service.graph.map_version,
                    position=MapPosition(x=runtime.x, y=runtime.y, z=0), heading_rad=0, speed_mps=0))
                service.record_sensor_observation(SensorObservation.model_validate({
                    "vehicleId": vehicle, "sensorId": "lidar", "sensorType": "LIDAR_2D",
                    "sessionId": "async-fixture", "observedTick": tick, "egoPoseTick": tick,
                    "mapVersion": service.graph.map_version, "valid": True, "detections": []}))

        telemetry()
        for runtime in service.vehicle_runtime.values():
            runtime.sensor_clear_since_s = 0
        settings = {**options(), **{k: config[k] for k in
                    ("quantum_s", "clearance_ticks", "edge_resources", "node_resources", "provenance")}, "horizon": 30}
        service.coordination = ServiceCoordination(settings)
        stop = asyncio.Event()
        clock = asyncio.create_task(service.run_clock(stop))
        try:
            deadline = monotonic() + 8
            proposal = None
            while proposal is None and monotonic() < deadline:
                telemetry()
                proposal = service.coordination.read(service)
                await asyncio.sleep(0.05)
            assert proposal is not None, list(service.coordination.worker.events)
            assert len(proposal.paths) == 3 and not proposal.executable
            assert route_ids == {v: r.route_id for v, r in service.vehicle_runtime.items()}
            assert service.resource_admission is None and service.resource_occupancy is None
            bad = service.sensor_observations[("V01", "lidar")].model_copy(update={"valid": False, "observed_tick": tick + 1})
            service.record_sensor_observation(bad)
            assert service.coordination.read(service) is None
            assert service.vehicle_runtime["V01"].safety_reason == "SENSOR_INVALID"
        finally:
            stop.set()
            await clock
            await service.coordination.close()
    asyncio.run(run())



def test_worker_preprocessing_counts_toward_wall_budget():
    _service, context, payload = worker_job()
    data = json.loads(payload)
    data["limits"]["wall_time_s"] = 1e-12
    reply = solve_job(CoordinationJob(1, context, 0, json.dumps(data)))
    assert reply.result.status == "BUDGET_EXCEEDED"
    assert reply.result.reason == "preprocessing"
    assert not reply.result.paths and not reply.result.executable



def test_existing_entered_lease_is_not_treated_as_a_free_planning_resource():
    from test_resource_presentation import waiting_service

    service, lease = waiting_service()
    book = service.resource_occupancy.book
    book.report_occupancy(lease.token, "other", {"corridor"}, now_s=service.now_s(), map_version=book.map_version)
    with pytest.raises(ValueError, match="existing_resource_occupancy"):
        capture_service_job(service, options())
