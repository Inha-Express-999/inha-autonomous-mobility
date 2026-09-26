"""Timed footprint checks from current sensor returns, without clock-domain mixing."""
import math
from dataclasses import dataclass

from campus_sim.domain import SensorEntityClass
from campus_sim.safety import SensorSample
from campus_sim.swept_geometry import BoxFootprint
from campus_sim.timed_collision import MovingDiscBound, TrajectorySweepResult, check_timed_prefix
from campus_sim.trajectory import TimedTrajectory


@dataclass(frozen=True)
class ObservationBounds:
    static_diameter_m: float
    pedestrian_diameter_m: float
    position_error_m: float
    velocity_error_mps: float
    max_capture_to_receipt_s: float
    freshness_s: float
    capture_seconds_per_server_second: float
    clock_rate_error: float
    provenance: str

    def __post_init__(self):
        values = (self.static_diameter_m, self.pedestrian_diameter_m, self.position_error_m,
                  self.velocity_error_mps, self.max_capture_to_receipt_s, self.freshness_s,
                  self.capture_seconds_per_server_second, self.clock_rate_error)
        if (any(isinstance(v, bool) or not math.isfinite(v) or v < 0 for v in values)
                or min(self.static_diameter_m, self.pedestrian_diameter_m, self.freshness_s) <= 0
                or self.clock_rate_error >= self.capture_seconds_per_server_second
                or not math.isfinite(self.clock_rate_error + self.capture_seconds_per_server_second)
                or self.max_capture_to_receipt_s >= 2 or not self.provenance.strip()):
            raise ValueError("Explicit finite observation envelopes, timing and provenance required")


@dataclass(frozen=True)
class ObservedTrajectoryResult:
    sweep: TrajectorySweepResult
    reason: str
    oldest_capture_age_bound_s: float | None

    @property
    def executable(self) -> bool:
        # Collision-free observed objects do not prove coverage or controller arbitration.
        return False


@dataclass(frozen=True)
class ObservedMotionBounds:
    obstacles: tuple[MovingDiscBound, ...]
    prediction_horizon_s: float
    oldest_capture_age_bound_s: float | None
    reason: str


def capture_motion_bounds(
    samples: tuple[SensorSample, ...], *,
    now_s: float, vehicle_id: str, session_id: str, map_version: str, ego_pose_tick: int,
    bounds: ObservationBounds, horizon_s: float = 2,
) -> ObservedMotionBounds:
    """Check against every visible static return and tracked pedestrian surface.

    Capture timestamps are Unity simulation time; receipt/now are server time.
    Their difference is never used. Receipt age is exact in the server clock;
    unknown transport age lies in [0, configured bound] in capture seconds.
    The explicit clock-rate interval maps server durations to capture durations;
    its validity across pause/rate changes is a caller prerequisite. Propagate by the midpoint
    and inflate by speed * half-width, plus velocity error over the oldest age.
    Motion error bounds must hold for the whole prediction, not just at capture.
    """
    def unknown(reason):
        return ObservedMotionBounds((), 0, None, reason)

    if (not math.isfinite(now_s) or now_s < 0 or not samples
            or not math.isfinite(horizon_s) or not 0 < horizon_s <= 2):
        return unknown("invalid_context")
    obstacles = []
    oldest = 0.0
    rate = bounds.capture_seconds_per_server_second
    rate_upper = rate + bounds.clock_rate_error
    rate_lower = rate - bounds.clock_rate_error
    for sample in samples:
        frame, received = sample.observation, sample.received_at_s
        if (received is None or not math.isfinite(received)
                or not 0 <= now_s - received <= bounds.freshness_s
                or not frame.valid or frame.sensor_position_m is None
                or frame.observed_time_s is None
                or (frame.vehicle_id, frame.session_id, frame.map_version, frame.ego_pose_tick)
                != (vehicle_id, session_id, map_version, ego_pose_tick)):
            return unknown("invalid_or_stale_observation")
        receipt_age = now_s - received
        age_lower = receipt_age * rate_lower
        age_upper = receipt_age * rate_upper + bounds.max_capture_to_receipt_s
        oldest = max(oldest, age_upper)
        if age_upper >= 2:
            return unknown("prediction_expired")
        half_delay = (age_upper - age_lower) / 2
        middle_age = (age_upper + age_lower) / 2
        sine, cosine = math.sin(frame.sensor_heading_rad), math.cos(frame.sensor_heading_rad)
        origin = frame.sensor_position_m
        motions = {m.entity_id: m for m in sample.motions}
        if len(motions) != len(sample.motions):
            return unknown("ambiguous_motion")
        nearest = {}
        for hit in frame.detections:
            if hit.entity_class is SensorEntityClass.STATIC_OBSTACLE:
                position = (origin.x + hit.local_position_m.x * sine - hit.local_position_m.y * cosine,
                            origin.y + hit.local_position_m.x * cosine + hit.local_position_m.y * sine)
                if not all(math.isfinite(v) for v in position):
                    return unknown("unrepresentable_prediction")
                obstacles.append(MovingDiscBound(position, (0, 0), bounds.static_diameter_m,
                                                 bounds.position_error_m, 0))
            elif hit.entity_class is SensorEntityClass.PEDESTRIAN and hit.entity_id:
                old = nearest.get(hit.entity_id)
                if old is None or hit.range_m < old.range_m:
                    nearest[hit.entity_id] = hit
            else:
                # Do not freeze unknowns, untracked vehicles or unidentified pedestrians.
                return unknown("unsupported_or_unidentified_object")
        for identity, hit in nearest.items():
            motion = motions.get(identity)
            if motion is None or motion.observed_time_s != frame.observed_time_s:
                return unknown("missing_current_motion")
            observed = (origin.x + hit.local_position_m.x * sine - hit.local_position_m.y * cosine,
                        origin.y + hit.local_position_m.x * cosine + hit.local_position_m.y * sine)
            if (not all(math.isfinite(v) for v in (*motion.world_position_m, *motion.world_velocity_mps))
                    or math.dist(observed, motion.world_position_m) > 1e-5):
                return unknown("motion_surface_mismatch")
            speed = math.hypot(*motion.world_velocity_mps)
            position = tuple(motion.world_position_m[k] + motion.world_velocity_mps[k] * middle_age
                             for k in range(2))
            error = (bounds.position_error_m + speed * half_delay
                     + bounds.velocity_error_mps * age_upper)
            if not all(math.isfinite(v) for v in (*position, error)):
                return unknown("unrepresentable_prediction")
            velocity = tuple(v * rate for v in motion.world_velocity_mps)
            velocity_error = bounds.velocity_error_mps * rate_upper + speed * bounds.clock_rate_error
            if not all(math.isfinite(v) for v in (*velocity, velocity_error)):
                return unknown("unrepresentable_prediction")
            obstacles.append(MovingDiscBound(position, velocity,
                                             bounds.pedestrian_diameter_m, error,
                                             velocity_error))
    # All discs now share trajectory t=0. Limit horizon by the oldest capture,
    # rather than granting a fresh two seconds at receipt or per sensor.
    return ObservedMotionBounds(tuple(obstacles), min(horizon_s, (2 - oldest) / rate_upper),
                                oldest, "observations_ready")


def check_observed_trajectory(
    trajectory: TimedTrajectory, footprint: BoxFootprint, samples: tuple[SensorSample, ...], *,
    now_s: float, vehicle_id: str, session_id: str, map_version: str, ego_pose_tick: int,
    bounds: ObservationBounds, horizon_s: float = 2, margin_m: float = 0.1,
) -> ObservedTrajectoryResult:
    """Convert current observations and check a bounded, continuous timed prefix."""
    if not math.isfinite(margin_m) or margin_m < 0:
        return ObservedTrajectoryResult(TrajectorySweepResult(0, None, False), "invalid_context", None)
    captured = capture_motion_bounds(samples, now_s=now_s, vehicle_id=vehicle_id,
                                     session_id=session_id, map_version=map_version,
                                     ego_pose_tick=ego_pose_tick, bounds=bounds, horizon_s=horizon_s)
    if captured.prediction_horizon_s <= 0:
        return ObservedTrajectoryResult(TrajectorySweepResult(0, None, False), captured.reason, None)
    result = check_timed_prefix(trajectory, footprint, captured.obstacles, observation_age_s=0,
                                horizon_s=captured.prediction_horizon_s, margin_m=margin_m)
    return ObservedTrajectoryResult(result,
                                    "observed_collision" if result.collision else "observed_prefix_clear",
                                    captured.oldest_capture_age_bound_s)
