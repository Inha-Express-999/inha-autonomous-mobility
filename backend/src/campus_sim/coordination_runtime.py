"""Service-connected, single-flight process worker for non-executable proposals.

Only the service thread may call offer/poll/read. The child receives serialized
snapshots, never the service, lease book, sensors, or any Unity hidden actor state.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import math
import multiprocessing
from collections import deque
from concurrent.futures import ProcessPoolExecutor
from concurrent.futures.process import BrokenProcessPool
from dataclasses import asdict, dataclass
from time import monotonic, perf_counter

from campus_sim.coordination import (
    AgentTask,
    CoordinationProblem,
    CoordinationResult,
    SearchLimits,
    solve_coordination,
)
from campus_sim.domain import RequestStatus, ServiceType
from campus_sim.planning import node_for_stop
from campus_sim.road_graph import RoadGraphDocument


def _json(value):
    return json.dumps(value, sort_keys=True, allow_nan=False)


@dataclass(frozen=True)
class CoordinationJob:
    generation: int
    context: str
    submitted_s: float
    payload: str


@dataclass(frozen=True)
class CoordinationReply:
    generation: int
    context: str
    result: CoordinationResult


def solve_job(job):
    """Picklable process entry point; validation and all graph search happen here."""
    started = perf_counter()
    data = json.loads(job.payload)
    graph = RoadGraphDocument.model_validate(data["graph"])
    tasks = [AgentTask(**{**task, "service_type": ServiceType(task["service_type"])}) for task in data["tasks"]]
    deadline = started + data["limits"]["wall_time_s"]
    try:
        problem = CoordinationProblem(graph, tasks, **data["problem"], preparation_deadline_s=deadline)
        remaining = deadline - perf_counter()
        if remaining <= 0:
            raise TimeoutError("coordination_preparation_deadline")
        limits = SearchLimits(**{**data["limits"], "wall_time_s": remaining})
        result = solve_coordination(problem, algorithm=data["algorithm"], limits=limits)
    except TimeoutError:
        result = CoordinationResult(data["algorithm"], "BUDGET_EXCEEDED", "preprocessing", "",
                                    (), 0, 0, 0, 0, perf_counter() - started)
    return CoordinationReply(job.generation, job.context, result)


def capture_service_job(service, options):
    """Freeze an eligible stopped fleet's graph-node problem and validity context.

    A node tolerance is an explicit synthetic assumption, not a new connector.
    Motion and safety updates invalidate this proposal; no timing is transferred
    to a live route or resource lease. Bounded AVOID policy is not yet composed.
    """
    graph = service.graph
    if graph is None or not graph.map_version.startswith("synthetic-"):
        raise ValueError("synthetic_graph_required")
    if len(graph.nodes) > 256 or len(graph.edges) > 1024:
        raise ValueError("coordination_snapshot_size_limit")
    tolerance = options["node_tolerance_m"]
    if isinstance(tolerance, bool) or not math.isfinite(tolerance) or not 0 < tolerance <= 0.15:
        raise ValueError("explicit_synthetic_node_tolerance_required")
    now = service.now_s()
    if service.crowd_model.avoided_edge_ids(graph, float(int(now))):
        raise ValueError("bounded_crowd_avoidance_not_composed")
    _, penalties = service.crowd_model.edge_cost_snapshot_s(graph, float(int(now)))
    closed = service._closed_edge_ids()
    document = graph.model_dump(mode="json")
    for edge in document["edges"]:
        edge["allowed_speed_mps"] = min(5.0, edge["allowed_speed_mps"])
        edge["crowd_penalty_s"] += penalties.get(edge["id"], 0)
        if edge["id"] in closed:
            edge["is_open"] = False
    monitor = service.resource_occupancy
    tasks, fleet = [], []
    for vehicle, runtime in sorted(service.vehicle_runtime.items()):
        request = service.requests.get(runtime.request_id or "")
        if request is None or request.status not in {RequestStatus.ASSIGNED, RequestStatus.IN_TRANSIT}:
            continue
        pose = service.ego_localizations.get(vehicle)
        if (pose is None or runtime.pose_source != "unity_localization"
                or service.ego_localization_is_stale(vehicle)):
            raise ValueError("fresh_ego_localization_required")
        service._refresh_sensor_safety(vehicle, runtime)
        if runtime.safety_motion_state is not None or runtime.speed_mps > 0.01:
            raise ValueError("stopped_sensor_clear_fleet_required")
        if runtime.route_id is None or runtime.service_remaining_s > 0:
            raise ValueError("active_route_required")
        start = service._nearest_node(runtime.x, runtime.y)
        node = next(n for n in graph.nodes if n.id == start)
        if math.hypot(runtime.x - node.position_m.x, runtime.y - node.position_m.y) > tolerance:
            raise ValueError("stopped_graph_node_required")
        goal_stop = request.pickup_stop_id if request.status == RequestStatus.ASSIGNED else request.dropoff_stop_id
        task = AgentTask(vehicle, start, node_for_stop(graph, goal_stop), service_type=request.service_type,
                         requires_step_free=request.service_needs.requires_step_free)
        if monitor is not None and vehicle in monitor.policies:
            task = AgentTask(**{**asdict(task), "vehicle_width_m": monitor.policies[vehicle].footprint.width_m})
        tasks.append(asdict(task))
        fleet.append({"vehicle": vehicle, "session": pose.session_id, "request": request.id,
                      "status": request.status.value, "needs": request.service_needs.model_dump(mode="json"),
                      "route": runtime.route_id, "points": runtime.route_points,
                      "speeds": runtime.route_speeds, "progress": runtime.route_progress_m,
                      "pose": [runtime.x, runtime.y, runtime.z, runtime.heading_rad, runtime.speed_mps]})
    if not tasks:
        raise ValueError("no_active_fleet")
    reservation = None
    if monitor is not None:
        if monitor.faults or monitor.book.closed or monitor.book.map_version != graph.map_version:
            raise ValueError("resource_state_unavailable")
        if any(monitor.book.unplanned_occupancy.values()) or any(lease.entered for lease in monitor.book.leases.values()):
            raise ValueError("existing_resource_occupancy_not_composed")
        # Occupancy/expiry/priority changes cannot silently inherit an older plan.
        reservation = {"map": monitor.book.map_version, "claims": monitor.book.claims,
                       "pending": [asdict(r) for r in monitor.book.pending.values()],
                       "leases": [asdict(r) for r in monitor.book.leases.values()],
                       "unplanned": monitor.book.unplanned_occupancy,
                       "regions": [asdict(r) for r in monitor.regions.values()],
                       "policies": {v: asdict(p) for v, p in monitor.policies.items()}}
        if service.resource_admission is not None:
            reservation["admission"] = {"policy": asdict(service.resource_admission.policy),
                                        "groups": service.resource_admission.groups}
        # Sets in dataclass lease snapshots are normalized before JSON serialization.
        reservation = json.loads(json.dumps(reservation, default=lambda v: sorted(v)))
    problem = {key: options[key] for key in
               ("quantum_s", "horizon", "clearance_ticks", "edge_resources", "node_resources", "provenance")}
    payload = _json({"graph": document, "tasks": tasks, "problem": problem,
                     "algorithm": options.get("algorithm", "cbs-disjoint"), "limits": options["limits"]})
    context = hashlib.sha256(_json({"run": service.run_id, "fleet": fleet,
        "reservation": reservation, "payload": payload, "options": options}).encode()).hexdigest()
    return context, payload


class CoordinationWorker:
    """One running solve and one coalesced latest request; no unbounded executor queue."""
    def __init__(self, *, max_age_s=5.0, executor=None):
        if isinstance(max_age_s, bool) or not math.isfinite(max_age_s) or not 0 < max_age_s <= 30:
            raise ValueError("bounded_positive_proposal_age_required")
        self.max_age_s = max_age_s
        self._executor = executor
        self._owns_executor = executor is None
        self._future = None
        self._running = self._pending = self._accepted = self._last = None
        self._generation = 0
        self._desired = None
        self._closed = False
        self.events = deque(maxlen=128)

    def _event(self, kind, job=None, detail=None):
        self.events.append({"kind": kind, "generation": job.generation if job else None, "detail": detail})

    def offer(self, context, payload, *, now_s=None):
        if self._closed:
            raise RuntimeError("coordination_worker_closed")
        now = monotonic() if now_s is None else now_s
        if not math.isfinite(now):
            raise ValueError("finite_worker_clock_required")
        if self._desired == context and any(job is not None and job.context == context
                and job.generation == self._generation and 0 <= now - job.submitted_s <= self.max_age_s
                for job in (self._pending, self._running, self._accepted, self._last)):
            return
        self._generation += 1
        self._desired = context
        self._accepted = None
        self._pending = CoordinationJob(self._generation, context, now, payload)
        self._event("QUEUED", self._pending)
        self._launch()

    def _launch(self):
        if self._future is not None or self._pending is None:
            return
        if self._executor is None:
            self._executor = ProcessPoolExecutor(max_workers=1, mp_context=multiprocessing.get_context("spawn"))
        self._running, self._pending = self._pending, None
        try:
            self._future = self._executor.submit(solve_job, self._running)
            self._event("STARTED", self._running)
        except (RuntimeError, OSError) as error:
            self._event("WORKER_FAILED", self._running, type(error).__name__)
            self._last, self._running = self._running, None
            self._reset_broken_pool(error)

    def _reset_broken_pool(self, error):
        if isinstance(error, BrokenProcessPool) and self._owns_executor and self._executor is not None:
            self._executor.shutdown(wait=False, cancel_futures=True)
            self._executor = None

    def invalidate(self, reason):
        if self._desired is not None:
            self._event("INVALIDATED", detail=reason)
        self._desired = None
        self._pending = self._accepted = None
        self._generation += 1

    def poll(self, context, *, now_s=None):
        now = monotonic() if now_s is None else now_s
        if not math.isfinite(now):
            raise ValueError("finite_worker_clock_required")
        if self._desired != context:
            self.invalidate("context_changed")
        if self._future is not None and self._future.done():
            job, future = self._running, self._future
            self._running = self._future = None
            self._last = job
            try:
                reply = future.result()
                if (reply.generation != job.generation or reply.context != job.context
                        or job.generation != self._generation or job.context != self._desired
                        or not 0 <= now - job.submitted_s <= self.max_age_s):
                    self._event("DISCARDED", job, "superseded_or_expired")
                elif reply.result.status != "SUCCESS" or reply.result.executable:
                    self._event("PLAN_FAILED", job, reply.result.status)
                else:
                    self._accepted = job
                    self._reply = reply.result
                    self._event("PROPOSAL_READY", job)
            except Exception as error:  # noqa: BLE001 — isolate arbitrary child failures from safety ticks.
                self._event("WORKER_FAILED", job, type(error).__name__)
                self._reset_broken_pool(error)
        if self._accepted is not None and not 0 <= now - self._accepted.submitted_s <= self.max_age_s:
            self.invalidate("proposal_expired")
        self._launch()
        return self._reply if self._accepted is not None else None

    async def close(self):
        self._closed = True
        self.invalidate("shutdown")
        if self._future is not None:
            self._future.cancel()
        if self._owns_executor and self._executor is not None:
            # Do not block the application's event loop while its bounded solve exits.
            await asyncio.to_thread(self._executor.shutdown, wait=True, cancel_futures=True)
        self._future = self._running = None


class ServiceCoordination:
    """Opt-in proposal adapter; never mutates routes, reservations or controller intent."""
    def __init__(self, options, *, worker=None):
        self.options = json.loads(_json(options))
        limits = SearchLimits(**self.options["limits"])
        if limits.wall_time_s is None or limits.wall_time_s > 5:
            raise ValueError("worker_search_wall_limit_required")
        self.worker = worker if worker is not None else CoordinationWorker()
        self.reason = "NOT_STARTED"

    def tick(self, service):
        try:
            context, payload = capture_service_job(service, self.options)
        except (KeyError, TypeError, ValueError) as error:
            self.reason = str(error)
            self.worker.invalidate(self.reason)
            self.worker.poll(None)
            return None
        self.worker.offer(context, payload)
        proposal = self.worker.poll(context)
        self.reason = "PROPOSAL_READY" if proposal is not None else "PLANNING"
        return proposal

    def read(self, service):
        # Revalidate on every read as inputs can change between service ticks.
        try:
            context, _ = capture_service_job(service, self.options)
        except (KeyError, TypeError, ValueError) as error:
            self.worker.invalidate(str(error))
            return None
        return self.worker.poll(context)

    async def close(self):
        await self.worker.close()
