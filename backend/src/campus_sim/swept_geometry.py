"""Continuous planar footprint checks for linear translation and shortest yaw turns."""
import math
from dataclasses import dataclass

from campus_sim.collision import circle_time_to_collision


@dataclass(frozen=True)
class Pose2:
    x: float
    y: float
    heading_rad: float

    def __post_init__(self):
        if not all(math.isfinite(v) for v in (self.x, self.y, self.heading_rad)):
            raise ValueError("Pose must be finite")


@dataclass(frozen=True)
class BoxFootprint:
    length_m: float
    width_m: float

    def __post_init__(self):
        if not all(math.isfinite(v) and v > 0 for v in (self.length_m, self.width_m)):
            raise ValueError("Footprint dimensions must be finite and positive")

    @property
    def radius_m(self):
        return math.hypot(self.length_m / 2, self.width_m / 2)


def _segment_box(a, b, hx, hy):
    lo, hi = 0.0, 1.0
    for start, end, extent in zip(a, b, (hx, hy), strict=True):
        delta = end - start
        if delta == 0:
            if abs(start) > extent:
                return False
        else:
            near, far = sorted(((-extent - start) / delta, (extent - start) / delta))
            lo, hi = max(lo, near), min(hi, far)
            if lo > hi:
                return False
    return True


def _rounded_box_hit(a, b, hx, hy, radius):
    # Rectangle Minkowski sum with a disc: two strips plus four round corners.
    if _segment_box(a, b, hx + radius, hy) or _segment_box(a, b, hx, hy + radius):
        return True
    velocity = (b[0] - a[0], b[1] - a[1])
    return any(circle_time_to_collision((a[0] - x, a[1] - y), velocity, radius) <= 1
               for x in (-hx, hx) for y in (-hy, hy))


def swept_box_hits_disc(start: Pose2, end: Pose2, footprint: BoxFootprint,
                        obstacle_start: tuple[float, float], obstacle_end: tuple[float, float],
                        radius_m: float, *, margin_m: float = 0,
                        angular_resolution_rad: float = 0.05) -> bool:
    """No endpoint-only gaps. Exact translation; conservative continuous yaw cover.

    Both centers interpolate linearly over the SAME interval. Yaw follows its
    shortest arc. Each yaw slab inflates a midpoint-oriented box by an upper bound
    on every corner's rotation displacement, so rotation cannot slip between
    samples. A true result may be conservative; false means clear for this model.
    Shapes are planar, centered and caller-supplied, with no Ground Truth queries.
    """
    values = (*obstacle_start, *obstacle_end, radius_m, margin_m, angular_resolution_rad)
    if (not all(math.isfinite(v) for v in values) or radius_m < 0 or margin_m < 0
            or not 0.001 <= angular_resolution_rad <= math.pi):
        raise ValueError("Sweep bounds must be finite and valid")
    yaw = math.remainder(end.heading_rad - start.heading_rad, math.tau)
    count = max(1, math.ceil(abs(yaw) / angular_resolution_rad))
    inflation = 2 * footprint.radius_m * math.sin(abs(yaw) / (4 * count))
    radius = radius_m + margin_m + inflation + 1e-9  # contact tolerance, in metres
    relative_a = (obstacle_start[0] - start.x, obstacle_start[1] - start.y)
    relative_b = (obstacle_end[0] - end.x, obstacle_end[1] - end.y)
    if not all(math.isfinite(v) for v in (*relative_a, *relative_b, radius)):
        raise ValueError("Relative sweep geometry is not representable")
    for i in range(count):
        angle = start.heading_rad + yaw * (i + 0.5) / count
        sine, cosine = math.sin(angle), math.cos(angle)

        def local(t, sine=sine, cosine=cosine):
            x, y = (relative_a[k] * (1 - t) + relative_b[k] * t for k in range(2))
            return x * cosine - y * sine, x * sine + y * cosine

        if _rounded_box_hit(local(i / count), local((i + 1) / count),
                            footprint.width_m / 2, footprint.length_m / 2, radius):
            return True
    return False
