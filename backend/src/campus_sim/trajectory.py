"""Analytic stop-turn-translate timing for local candidate paths."""
import math
from dataclasses import dataclass
from itertools import pairwise

from campus_sim.swept_geometry import Pose2


@dataclass(frozen=True)
class MotionLimits:
    speed_mps: float
    acceleration_mps2: float
    braking_mps2: float
    yaw_rate_radps: float
    provenance: str

    def __post_init__(self):
        if (not all(math.isfinite(v) and v > 0 for v in (
                self.speed_mps, self.acceleration_mps2, self.braking_mps2, self.yaw_rate_radps))
                or not self.provenance.strip()):
            raise ValueError("Motion limits must be finite, positive and have provenance")


@dataclass(frozen=True)
class TimedState:
    pose: Pose2
    speed_mps: float
    yaw_rate_radps: float


@dataclass(frozen=True)
class TimedStage:
    start: Pose2
    end: Pose2
    starts_at_s: float
    duration_s: float
    peak_speed_mps: float
    acceleration_s: float
    cruise_s: float
    limits: MotionLimits

    def sample(self, local_time_s: float) -> TimedState:
        if not math.isfinite(local_time_s) or not 0 <= local_time_s <= self.duration_s:
            raise ValueError("Time outside stage")
        if local_time_s == self.duration_s:
            return TimedState(self.end, 0, 0)
        if self.peak_speed_mps == 0:
            yaw = math.remainder(self.end.heading_rad - self.start.heading_rad, math.tau)
            return TimedState(Pose2(self.start.x, self.start.y,
                                   self.start.heading_rad + yaw * local_time_s / self.duration_s),
                              0, yaw / self.duration_s)
        a, b, v = self.limits.acceleration_mps2, self.limits.braking_mps2, self.peak_speed_mps
        if local_time_s < self.acceleration_s:
            distance, speed = a * local_time_s**2 / 2, a * local_time_s
        elif local_time_s < self.acceleration_s + self.cruise_s:
            distance = v * self.acceleration_s / 2 + v * (local_time_s - self.acceleration_s)
            speed = v
        else:
            elapsed = local_time_s - self.acceleration_s - self.cruise_s
            distance = v * self.acceleration_s / 2 + v * self.cruise_s + v * elapsed - b * elapsed**2 / 2
            speed = max(0, v - b * elapsed)
        length = math.hypot(self.end.x - self.start.x, self.end.y - self.start.y)
        fraction = min(1, max(0, distance / length))
        return TimedState(Pose2(self.start.x + (self.end.x - self.start.x) * fraction,
                               self.start.y + (self.end.y - self.start.y) * fraction,
                               self.start.heading_rad), speed, 0)


@dataclass(frozen=True)
class TimedTrajectory:
    initial_pose: Pose2
    stages: tuple[TimedStage, ...]

    @property
    def duration_s(self):
        return self.stages[-1].starts_at_s + self.stages[-1].duration_s if self.stages else 0.0

    def sample(self, time_s: float) -> TimedState:
        if not math.isfinite(time_s) or not 0 <= time_s <= self.duration_s:
            raise ValueError("Time outside trajectory")
        for stage in self.stages:
            if time_s < stage.starts_at_s + stage.duration_s:
                return stage.sample(min(stage.duration_s, max(0, time_s - stage.starts_at_s)))
        return TimedState(self.stages[-1].end if self.stages else self.initial_pose, 0, 0)


def time_parameterize(poses: tuple[Pose2, ...], limits: MotionLimits) -> TimedTrajectory:
    """Start/end each stage at rest; reject simultaneous translation and yaw.

    Translation has analytic triangular/trapezoidal speed with asymmetric accel/
    braking bounds. Rotation has bounded constant yaw rate, without an angular
    acceleration model. Inputs are candidate stages, not runtime drive commands.
    """
    if not poses:
        raise ValueError("A trajectory needs an initial pose")
    stages, elapsed = [], 0.0
    for start, end in pairwise(poses):
        length = math.hypot(end.x - start.x, end.y - start.y)
        yaw = math.remainder(end.heading_rad - start.heading_rad, math.tau)
        if length == 0 and yaw == 0:
            continue
        if length == 0:
            duration, peak, accelerate, cruise = abs(yaw) / limits.yaw_rate_radps, 0, 0, 0
        else:
            direction = math.atan2(end.x - start.x, end.y - start.y)
            if abs(yaw) > 1e-8 or abs(math.remainder(direction - start.heading_rad, math.tau)) > 1e-8:
                raise ValueError("Translation must follow fixed forward heading")
            a, b = limits.acceleration_mps2, limits.braking_mps2
            peak = min(limits.speed_mps, math.sqrt(2 * length / (1 / a + 1 / b)))
            if not math.isfinite(peak) or peak <= 0:
                raise ValueError("Trajectory peak speed is not representable")
            accelerate, brake = peak / a, peak / b
            cruise = max(0, (length - peak * (accelerate + brake) / 2) / peak)
            duration = accelerate + cruise + brake
        if not all(math.isfinite(v) for v in (duration, elapsed + duration)) or duration <= 0:
            raise ValueError("Trajectory timing is not representable")
        stages.append(TimedStage(start, end, elapsed, duration, peak, accelerate, cruise, limits))
        elapsed += duration
    return TimedTrajectory(poses[0], tuple(stages))
