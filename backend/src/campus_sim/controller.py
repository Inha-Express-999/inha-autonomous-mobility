"""Python-authoritative synthetic route steering commands, separate from display state."""
import math
from dataclasses import dataclass

from campus_sim.domain import RequestStatus


@dataclass(frozen=True)
class ControllerPolicy:
    max_speed_mps: float
    max_yaw_rate_radps: float
    heading_gain: float
    braking_mps2: float
    arrival_radius_m: float
    alignment_tolerance_rad: float
    command_valid_for_s: float
    provenance: str

    def __post_init__(self):
        if (not all(math.isfinite(v) and v > 0 for v in (
            self.max_speed_mps, self.max_yaw_rate_radps, self.heading_gain, self.braking_mps2,
            self.arrival_radius_m, self.alignment_tolerance_rad, self.command_valid_for_s))
                or self.alignment_tolerance_rad > math.pi / 4
                or self.command_valid_for_s > 0.3 or not self.provenance.strip()):
            raise ValueError("Controller limits/provenance must be explicit, finite and valid")


def steering_target(x, y, heading, measured_speed, target, segment_speed, policy):
    values = (x, y, heading, measured_speed, *target, segment_speed)
    if not all(math.isfinite(v) for v in values) or measured_speed < 0 or segment_speed < 0:
        raise ValueError("Controller inputs must be finite and nonnegative where applicable")
    dx, dy = target[0] - x, target[1] - y
    distance = math.hypot(dx, dy)
    if distance <= policy.arrival_radius_m:
        return 0.0, 0.0
    error = math.remainder(math.atan2(dx, dy) - heading, math.tau)
    yaw = max(-policy.max_yaw_rate_radps, min(policy.max_yaw_rate_radps, error * policy.heading_gain))
    if abs(error) > policy.alignment_tolerance_rad:
        # Brake first; rotate at rest instead of instantly redirecting velocity.
        return 0.0, yaw if measured_speed <= 0.01 else 0.0
    target_speed = min(policy.max_speed_mps, segment_speed,
                       math.sqrt(2 * policy.braking_mps2 * max(0, distance - policy.arrival_radius_m)))
    return target_speed, yaw


def make_control_command(service, vehicle_id):
    from campus_sim.service import EGO_LOCALIZATION_STALE_AFTER_S

    policy = service.control_policies.get(vehicle_id)
    runtime = service.vehicle_runtime.get(vehicle_id)
    pose = service.ego_localizations.get(vehicle_id)
    if policy is None or runtime is None or pose is None or service.graph is None:
        return None
    if not service.graph.map_version.startswith("synthetic-"):
        return None
    service._refresh_sensor_safety(vehicle_id, runtime)
    request = service.requests.get(runtime.request_id or "")
    speed = yaw = 0.0
    reason = runtime.safety_reason if runtime.safety_motion_state else "NO_ACTIVE_ROUTE"
    stale = service.ego_localization_is_stale(vehicle_id)
    if stale:
        reason = "STALE_LOCALIZATION"
    elif (runtime.safety_motion_state is None and runtime.route_id is not None
          and request is not None and request.status in {RequestStatus.ASSIGNED, RequestStatus.IN_TRANSIT}
          and len(runtime.route_points) > 1):
        traversed = 0.0
        index = len(runtime.route_points) - 2
        for i in range(len(runtime.route_points) - 1):
            a, b = runtime.route_points[i:i + 2]
            traversed += math.hypot(b[0] - a[0], b[1] - a[1])
            index = i
            if traversed > runtime.route_progress_m + policy.arrival_radius_m:
                break
        if index < len(runtime.route_speeds):
            speed, yaw = steering_target(runtime.x, runtime.y, runtime.heading_rad, runtime.speed_mps,
                                         runtime.route_points[index + 1], runtime.route_speeds[index], policy)
            reason = "ROUTE_CONTROL"
    if service.resource_admission is not None and (speed > 0 or yaw != 0):
        speed, yaw, reservation_reason = service.resource_admission.limit(service, vehicle_id, speed, yaw, policy)
        if reservation_reason is not None:
            reason = reservation_reason
    validity = policy.command_valid_for_s
    if speed > 0 or yaw != 0:
        # A freshly issued command must not outlive the observations behind it.
        now = service.now_s()
        receipts = [value for key, value in service.sensor_received_at_s.items()
                    if key[0] == vehicle_id]
        budgets = [runtime.localization_received_at_s + EGO_LOCALIZATION_STALE_AFTER_S - now]
        budgets.extend(received + service.safety_policy.sensor_stale_after_s - now
                       for received in receipts)
        remaining = min(budgets)
        if remaining <= 0:
            speed = yaw = 0.0
            reason = "SENSOR_DATA_STALE"
        else:
            validity = min(validity, remaining)
    service.control_sequences[vehicle_id] = service.control_sequences.get(vehicle_id, -1) + 1
    return {
        "vehicleId": vehicle_id, "mapVersion": service.graph.map_version,
        "sessionId": pose.session_id, "routeId": runtime.route_id,
        "sequence": service.control_sequences[vehicle_id], "egoPoseTick": pose.observed_tick,
        "validForS": validity,
        "targetSpeedMps": speed, "yawRateRadps": yaw, "reason": reason,
    }
