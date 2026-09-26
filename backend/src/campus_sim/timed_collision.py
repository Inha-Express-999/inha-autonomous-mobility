"""Short-horizon continuous checks of timed candidates against observed motion bounds."""
import math
from dataclasses import dataclass

from campus_sim.swept_geometry import BoxFootprint, swept_box_hits_disc
from campus_sim.trajectory import TimedTrajectory


@dataclass(frozen=True)
class MovingDiscBound:
    position_m: tuple[float, float]
    velocity_mps: tuple[float, float]
    radius_m: float
    position_uncertainty_m: float
    velocity_uncertainty_mps: float

    def __post_init__(self):
        if (not all(math.isfinite(v) for v in (*self.position_m, *self.velocity_mps,
                    self.radius_m, self.position_uncertainty_m, self.velocity_uncertainty_mps))
                or min(self.radius_m, self.position_uncertainty_m, self.velocity_uncertainty_mps) < 0):
            raise ValueError("Motion bounds must be finite and nonnegative")


@dataclass(frozen=True)
class TrajectorySweepResult:
    checked_duration_s: float
    collision: bool | None
    full_trajectory_checked: bool


def check_timed_prefix(trajectory: TimedTrajectory, footprint: BoxFootprint,
                       obstacles: tuple[MovingDiscBound, ...], *, observation_age_s: float,
                       horizon_s: float = 2.0, margin_m: float = 0.1,
                       max_step_s: float = 0.05) -> TrajectorySweepResult:
    """Constant-velocity predictions never exceed two seconds from observation.

    Acceleration is bounded between analytic ego samples: inflate each sweep by
    a_max*dt²/8 to cover deviation from the linear chord. Yaw uses the continuous
    angular envelope of swept_box_hits_disc. No claim about unseen obstacles or
    sensor coverage is made. A checked prefix does not validate the whole path.
    """
    if (not all(math.isfinite(v) for v in (observation_age_s, horizon_s, margin_m, max_step_s))
            or observation_age_s < 0 or not 0 < horizon_s <= 2 or margin_m < 0
            or not 0.001 <= max_step_s <= 0.2):
        raise ValueError("Invalid timed sweep bounds")
    if observation_age_s >= 2:
        return TrajectorySweepResult(0, None, False)
    until = min(trajectory.duration_s, horizon_s, 2 - observation_age_s)

    def hit(a, b, t0, t1, chord_error):
        for obstacle in obstacles:
            p0 = tuple(obstacle.position_m[k] + obstacle.velocity_mps[k] * (observation_age_s + t0)
                       for k in range(2))
            p1 = tuple(obstacle.position_m[k] + obstacle.velocity_mps[k] * (observation_age_s + t1)
                       for k in range(2))
            radius = (obstacle.radius_m + obstacle.position_uncertainty_m
                      + obstacle.velocity_uncertainty_mps * (observation_age_s + t1))
            if swept_box_hits_disc(a, b, footprint, p0, p1, radius,
                                   margin_m=margin_m + chord_error):
                return True
        return False

    if hit(trajectory.initial_pose, trajectory.initial_pose, 0, 0, 0):
        return TrajectorySweepResult(0, True, False)
    for stage in trajectory.stages:
        if stage.starts_at_s >= until:
            break
        duration = min(stage.duration_s, until - stage.starts_at_s)
        steps = max(1, math.ceil(duration / max_step_s))
        for i in range(steps):
            t0, t1 = duration * i / steps, duration * (i + 1) / steps
            if i == steps - 1:
                t1 = duration
            a, b = stage.sample(t0).pose, stage.sample(t1).pose
            acceleration = max(stage.limits.acceleration_mps2, stage.limits.braking_mps2)
            error = acceleration * (t1 - t0)**2 / 8 if stage.peak_speed_mps else 0
            if hit(a, b, stage.starts_at_s + t0, stage.starts_at_s + t1, error):
                return TrajectorySweepResult(stage.starts_at_s + t1, True, False)
    return TrajectorySweepResult(until, False, until == trajectory.duration_s)
