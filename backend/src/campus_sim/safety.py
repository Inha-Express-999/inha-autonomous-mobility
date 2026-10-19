"""Independent advisory safety checks. No actuator or instantaneous speed reset."""

from dataclasses import dataclass
from math import hypot, inf, isfinite, sqrt

from campus_sim.sensors import ObservationContext, SensorObservation, observation_error


def _nonnegative(*values: float) -> None:
    if any(not isfinite(v) or v < 0 for v in values):
        raise ValueError("finite nonnegative values required")


def stopping_distance(speed: float, reaction_s: float, deceleration: float, margin: float) -> float:
    _nonnegative(speed, reaction_s, margin)
    if not isfinite(deceleration) or deceleration <= 0:
        raise ValueError("positive braking deceleration required")
    return speed * reaction_s + speed * speed / (2 * deceleration) + margin


def crossing_ttc(position: tuple, velocity: tuple, radius: float) -> float:
    """Earliest collision of relative constant-velocity circles, including overlap."""
    _nonnegative(radius)
    if any(not isfinite(v) for v in (*position, *velocity)):
        raise ValueError("finite relative position and velocity required")
    x, y = position
    vx, vy = velocity
    c = x * x + y * y - radius * radius
    if c <= 0:
        return 0.0
    a = vx * vx + vy * vy
    b = 2 * (x * vx + y * vy)
    discriminant = b * b - 4 * a * c
    if a == 0 or b >= 0 or discriminant < 0:
        return inf
    return max(0.0, (-b - sqrt(discriminant)) / (2 * a))


@dataclass(frozen=True)
class SafetyAssessment:
    action: str
    reason: str
    stopping_distance_m: float
    minimum_ttc_s: float


def assess_safety(
    frames: tuple[SensorObservation, ...],
    context: ObservationContext,
    *,
    speed_mps: float,
    ego_radius_m: float,
    reaction_s: float = 0.3,
    brake_deceleration_mps2: float = 3.0,
    margin_m: float = 0.5,
    ttc_horizon_s: float = 2.0,
) -> SafetyAssessment:
    _nonnegative(ego_radius_m, ttc_horizon_s)
    if ego_radius_m == 0:
        raise ValueError("positive ego enclosing radius required")
    distance = stopping_distance(speed_mps, reaction_s, brake_deceleration_mps2, margin_m)
    error = observation_error(frames, context)
    if error:
        return SafetyAssessment("STOP", error, distance, inf)
    minimum_ttc, stop, slow = inf, False, False
    for frame in frames:
        for hit in frame.detections:
            x, y = hit.position_m
            radius = ego_radius_m + hit.radius_m + margin_m
            if hypot(x, y) <= radius:
                stop = True
            velocity = hit.relative_velocity_mps
            # Static classification alone does not supply object motion in an ego frame.
            # For a straight-moving ego, a static obstacle has relative velocity (0, -v).
            if velocity is None and hit.entity_class == "static":
                velocity = (0.0, -speed_mps)
            if velocity is not None:
                ttc = crossing_ttc(hit.position_m, velocity, radius)
                minimum_ttc = min(minimum_ttc, ttc)
                if ttc <= ttc_horizon_s:
                    stop = True
            else:
                # Unknown motion inside a local sensor frame cannot authorize motion.
                slow = True
            if abs(x) <= radius and y >= 0:
                clearance = y - sqrt(max(0, radius * radius - x * x))
                if clearance <= distance:
                    stop = True
                elif clearance <= distance * 2:
                    slow = True
    if stop:
        return SafetyAssessment("STOP", "COLLISION_RISK", distance, minimum_ttc)
    if slow:
        return SafetyAssessment(
            "YIELD", "LIMITED_CLEARANCE_OR_UNKNOWN_MOTION", distance, minimum_ttc
        )
    return SafetyAssessment("CLEAR", "OBSERVED_CHECKS_CLEAR", distance, minimum_ttc)
