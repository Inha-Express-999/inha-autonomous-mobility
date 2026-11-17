"""Transactional opt-in checkpoints for physical passenger service (not virtual trips)."""

import hashlib
import json
import sqlite3
from dataclasses import asdict
from pathlib import Path

from campus_sim.battery import BatteryState
from campus_sim.domain import CommandAck, RequestView
from campus_sim.physical_dispatch import ObservedTrip


class RecoveryStore:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.failed = False
        self.restored_request_ids = set()
        with sqlite3.connect(self.path) as db:
            db.execute(
                "CREATE TABLE IF NOT EXISTS checkpoint (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL, digest TEXT NOT NULL)"
            )

    def healthy(self):
        if self.failed:
            raise ValueError("RECOVERY_STORAGE_FAILED: restart after repairing storage")

    @staticmethod
    def identity(s):
        service = s.service
        return {
            "schema": 1,
            "geometry": s.physical.geometry_hash,
            "lanes": s.physical.lanes.hash if s.physical.lanes else None,
            "map": service.graph.map_version,
            "costs": hashlib.sha256(
                repr(sorted(service.costs.costs_s.items())).encode()
            ).hexdigest(),
            "homes": service.home_nodes,
            "stops": service.stop_nodes,
            "vehicles": {
                k: v.model_dump(mode="json", exclude={"available"})
                for k, v in service.vehicles.items()
            },
            "battery": service.battery_policy.model_dump() if service.battery_policy else None,
        }

    def save(self, s):
        self.healthy()
        service = s.service
        try:
            if service.trips or set(service.requests) - service.external_request_ids:
                raise ValueError("Recovery supports physical requests only")
            data = {
                "identity": self.identity(s),
                "time": service._time_s,
                "paused": s.clock.paused,
                "planner": service.route_algorithm,
                "dispatch": service.dispatch_algorithm,
                "requests": {k: v.model_dump(mode="json") for k, v in service.requests.items()},
                "commands": {k: v.model_dump(mode="json") for k, v in service.commands.items()},
                "external_vehicles": sorted(service.external_vehicle_ids),
                "jobs": {k: asdict(v) for k, v in s.physical_dispatch.jobs.items()},
                "batteries": {k: asdict(v) for k, v in service.batteries.items()},
                "ready_order": s.physical_dispatch.ready_order,
                "ready_sequence": s.physical_dispatch.ready_sequence,
                "opportunities": s.physical_dispatch.opportunity_count,
                "events": s.physical_dispatch.events,
            }
            payload = json.dumps(data, sort_keys=True, allow_nan=False)
            with sqlite3.connect(self.path) as db:
                db.execute("PRAGMA synchronous=FULL")
                db.execute(
                    "INSERT OR REPLACE INTO checkpoint VALUES (1, ?, ?)",
                    (payload, hashlib.sha256(payload.encode()).hexdigest()),
                )
        except Exception as error:
            self.failed = True
            raise ValueError("RECOVERY_STORAGE_FAILED") from error

    def restore(self, s):
        with sqlite3.connect(self.path) as db:
            row = db.execute("SELECT payload,digest FROM checkpoint WHERE id=1").fetchone()
        if row is None:
            return
        try:
            payload, digest = row
            if hashlib.sha256(payload.encode()).hexdigest() != digest:
                raise ValueError("Checkpoint checksum mismatch")
            data = json.loads(payload)
            if data["identity"] != self.identity(s):
                raise ValueError("Incompatible map, policy or checkpoint schema")
            requests = {k: RequestView.model_validate(v) for k, v in data["requests"].items()}
            commands = {k: CommandAck.model_validate(v) for k, v in data["commands"].items()}
            jobs = {k: ObservedTrip(**v) for k, v in data["jobs"].items()}
            batteries = {k: BatteryState(**v) for k, v in data["batteries"].items()}
            active = set()
            phases = {
                "TO_PICKUP",
                "PICKUP_SERVICE",
                "TO_DROPOFF",
                "DROPOFF_SERVICE",
                "RETURN_PENDING",
                "RETURNING",
                "READY",
            }
            for key, job in jobs.items():
                if job.phase not in phases:
                    raise ValueError("Invalid transport phase")
                if (
                    key != job.request_id
                    or key not in requests
                    or job.vehicle_id not in s.service.vehicles
                ):
                    raise ValueError("Invalid job reference")
                if requests[key].vehicle_id != job.vehicle_id:
                    raise ValueError("Invalid vehicle assignment")
                if not job.returned:
                    if job.vehicle_id in active:
                        raise ValueError("Duplicate vehicle assignment")
                    active.add(job.vehicle_id)
                job.last_wall = job.last_position = None
                job.last_tick = -1
                job.dwell_s = 0
                job.mission_id = "recovery-required"
                job.reason = "WAITING_FRESH_PHYSICS"
            if set(batteries) != set(s.service.batteries):
                raise ValueError("Invalid battery fleet")
            if any(
                k != r.id
                or r.pickup_stop_id not in s.service.stop_nodes
                or r.dropoff_stop_id not in s.service.stop_nodes
                for k, r in requests.items()
            ):
                raise ValueError("Invalid request")
            if any(
                r.vehicle_id
                and r.status.value not in {"CANCELLED", "COMPLETED"}
                and r.id not in jobs
                for r in requests.values()
            ):
                raise ValueError("Assigned request has no transport job")
            s.service.requests = requests
            s.service.commands = commands
            s.service.external_request_ids = set(requests)
            s.service.external_vehicle_ids = set(data["external_vehicles"]) | active
            s.service.batteries = batteries
            s.clock.value = s.service._time_s = data["time"]
            s.clock.last_wall = s.wall_clock()
            s.clock.speed = 1.0
            s.clock.paused = data["paused"]
            s.service.route_algorithm = data["planner"]
            s.service.dispatch_algorithm = data["dispatch"]
            dispatcher = s.physical_dispatch
            dispatcher.jobs = jobs
            dispatcher.ready_order = data["ready_order"]
            dispatcher.ready_sequence = data["ready_sequence"]
            dispatcher.opportunity_count = data["opportunities"]
            dispatcher.events = data["events"]
            self.restored_request_ids = set(requests)
        except Exception as error:
            self.failed = True
            raise ValueError(
                "RECOVERY_LOAD_FAILED: checkpoint preserved; refusing empty service"
            ) from error
