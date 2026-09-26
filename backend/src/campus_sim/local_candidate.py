"""Immutable sensor-derived RRT inputs and stale-candidate rejection.

Current candidates are diagnostic: observation coverage and timed dynamic safety
must still be established before an actuator may consume a path.
"""
import hashlib
import json
import math
from dataclasses import dataclass

from campus_sim.domain import SensorEntityClass
from campus_sim.local_rrt import DiscObstacle, RectCorridor, plan_rrt
from campus_sim.service import EGO_LOCALIZATION_STALE_AFTER_S, MobilityService
from campus_sim.swept_geometry import BoxFootprint, Pose2
from campus_sim.trajectory import MotionLimits, TimedTrajectory, time_parameterize


@dataclass(frozen=True)
class LocalPlanningInput:
    vehicle_id: str
    fingerprint: str
    captured_at_s: float
    expires_at_s: float
    start: Pose2
    observed_obstacles: tuple[DiscObstacle, ...]
    object_diameter_bound_m: float
    bound_provenance: str


@dataclass(frozen=True)
class LocalCandidate:
    inputs: LocalPlanningInput
    poses: tuple[Pose2, ...]
    footprint: BoxFootprint
    corridor: RectCorridor
    corridor_provenance: str
    seed: int

    def timed_trajectory(self, limits: MotionLimits) -> TimedTrajectory:
        return time_parameterize(self.poses, limits)

    @property
    def executable(self) -> bool:
        # Freshness is necessary, but never evidence of visibility/dynamic safety.
        return False


def capture_local_input(service: MobilityService, vehicle_id: str, *, object_diameter_bound_m: float,
                        bound_provenance: str) -> LocalPlanningInput | None:
    if (not math.isfinite(object_diameter_bound_m) or object_diameter_bound_m <= 0
            or not bound_provenance.strip()):
        raise ValueError("Observed object diameter bound and provenance are required")
    now = service.now_s()
    runtime = service.vehicle_runtime.get(vehicle_id)
    pose = service.ego_localizations.get(vehicle_id)
    if (runtime is None or pose is None or service.graph is None
            or not math.isfinite(now) or now < 0 or service.ego_localization_is_stale(vehicle_id)
            or pose.map_version != service.graph.map_version or runtime.speed_mps != 0
            or runtime.safety_motion_state not in {"EMERGENCY_STOP", "REPLANNING"}
            or service.closed_zone_ids):
        return None
    pose_received = runtime.localization_received_at_s
    if (pose_received is None
            or not 0 <= now - pose_received <= EGO_LOCALIZATION_STALE_AFTER_S):
        return None
    frames = sorted((key, frame) for key, frame in service.sensor_observations.items()
                    if key[0] == vehicle_id)
    if not frames:
        return None
    expires = pose_received + EGO_LOCALIZATION_STALE_AFTER_S
    payloads, obstacles = [], []
    for key, frame in frames:
        received = service.sensor_received_at_s.get(key)
        if (received is None or not math.isfinite(received)
                or not 0 <= now - received <= service.safety_policy.sensor_stale_after_s
                or not frame.valid or frame.session_id != pose.session_id
                or frame.map_version != pose.map_version or frame.ego_pose_tick != pose.observed_tick
                or frame.sensor_position_m is None):
            return None
        # No freeze-in-place assumption for pedestrians, vehicles or unknown class.
        if any(hit.entity_class is not SensorEntityClass.STATIC_OBSTACLE for hit in frame.detections):
            return None
        expires = min(expires, received + service.safety_policy.sensor_stale_after_s)
        payloads.append((frame.model_dump(mode="json"), received))
        sine, cosine = math.sin(frame.sensor_heading_rad), math.cos(frame.sensor_heading_rad)
        for hit in frame.detections:
            local, origin = hit.local_position_m, frame.sensor_position_m
            # Full diameter bounds an object about any observed surface point.
            obstacles.append(DiscObstacle(origin.x + local.x * sine - local.y * cosine,
                                          origin.y + local.x * cosine + local.y * sine,
                                          object_diameter_bound_m))
    request = service.requests.get(runtime.request_id or "")
    payload = {
        "epoch": service.local_planning_epoch, "pose": pose.model_dump(mode="json"),
        "pose_received": pose_received, "frames": payloads,
        "route": (runtime.route_id, runtime.route_snapshot_points, runtime.route_snapshot_speeds),
        "mission": (runtime.request_id, runtime.mission_state),
        "request": request.model_dump(mode="json") if request is not None else None,
        "closed_zones": sorted(service.closed_zone_ids),
        "safety": (runtime.safety_motion_state, runtime.safety_reason),
        "bounds": (object_diameter_bound_m, bound_provenance),
    }
    digest = hashlib.sha256(json.dumps(payload, sort_keys=True, allow_nan=False).encode()).hexdigest()
    return LocalPlanningInput(vehicle_id, digest, now, expires,
                              Pose2(pose.position.x, pose.position.y, pose.heading_rad),
                              tuple(obstacles), object_diameter_bound_m, bound_provenance)


def inputs_still_current(service: MobilityService, inputs: LocalPlanningInput) -> bool:
    if not inputs.captured_at_s <= service.now_s() <= inputs.expires_at_s:
        return False
    current = capture_local_input(service, inputs.vehicle_id,
                                  object_diameter_bound_m=inputs.object_diameter_bound_m,
                                  bound_provenance=inputs.bound_provenance)
    return current is not None and current.fingerprint == inputs.fingerprint


def compute_local_candidate(inputs: LocalPlanningInput, goal: Pose2, *, footprint: BoxFootprint,
                            corridor: RectCorridor, corridor_provenance: str,
                            seed: int) -> LocalCandidate | None:
    """Pure bounded computation suitable for a worker; recheck inputs after return."""
    if not corridor_provenance.strip():
        raise ValueError("Corridor provenance is required")
    path = plan_rrt(inputs.start, goal, footprint=footprint, corridor=corridor,
                    obstacles=inputs.observed_obstacles, seed=seed)
    if path is None:
        return None
    return LocalCandidate(inputs, path, footprint, corridor, corridor_provenance, seed)
