"""Geometry-only LiDAR ground segmentation for physical protocol 6.

The input is a raw range image (channel x azimuth column) plus the sensor mount
offset and the ego wheel-contact plane. No semantic label, collider tag, terrain
height query or target transform is used: a return is classified only from its
measured position relative to neighbouring returns in the same azimuth column.

Method (scan-line / ray ground filter, cf. Autoware ``ray_ground_filter``):
for every azimuth column, walk the returns from near to far starting at the
ego contact plane. A return continues the ground when its height matches the
ground extrapolated from the previous ground return within a tolerance that
grows with the gap between rings; gentle grade changes (hills) therefore stay
ground, while abrupt vertical structure (walls, people, vehicles) does not.

With sparse rings one raised return is ambiguous (grade change or low object),
so a ground return raised above ``min_obstacle_height_m`` is re-labelled an
obstacle when the same ring reaches clearly farther in a neighbouring column:
terrain is laterally continuous, an object occludes (range-image edge).

All thresholds are synthetic simulation assumptions, not measured road grades,
curb heights or vehicle clearance.
"""
from __future__ import annotations

from dataclasses import dataclass
from math import cos, radians, sin, tan


@dataclass(frozen=True)
class GroundSegmentationConfig:
    # Max change of grade between consecutive ground returns (hill crest/sag).
    max_slope_change_deg: float = 8.
    # Absolute ground grade relative to the leveled ego frame.
    max_ground_slope_deg: float = 15.
    # Fixed height tolerance between consecutive ground returns.
    ground_tolerance_m: float = .1
    # Returns at least this high above the extrapolated ground are obstacles;
    # lower non-ground returns are treated as drivable surface roughness.
    min_obstacle_height_m: float = .15
    # Returns above sensor height + this clearance are overhead (branches, signs).
    overhead_clearance_m: float = .3
    # A raised ground candidate becomes an obstacle when a same-ring neighbour
    # within this many columns reaches farther by max(min, ratio * range).
    edge_window_columns: int = 3
    edge_jump_min_m: float = .5
    edge_jump_ratio: float = .08


DEFAULT_CONFIG = GroundSegmentationConfig()


@dataclass(frozen=True)
class SegmentedPoint:
    """Ego-leveled position relative to the ego reference (vehicle origin)."""
    lateral_m: float
    forward_m: float
    height_m: float  # Above the local extrapolated ground.
    entity_class: str = "unknown"  # Protocol 6 never carries semantic labels.


@dataclass(frozen=True)
class Segmentation:
    obstacles: tuple[SegmentedPoint, ...]
    ground_count: int
    low_count: int
    overhead_count: int
    return_count: int


def segment_scan(scan, config: GroundSegmentationConfig = DEFAULT_CONFIG) -> Segmentation:
    elevations = [radians(e) for e in scan.channel_elevations_deg]
    channels, columns = len(elevations), scan.columns
    change = tan(radians(config.max_slope_change_deg))
    max_grade = tan(radians(config.max_ground_slope_deg))
    sensor_height = scan.sensor_up_m - scan.ground_up_m
    ceiling = sensor_height + config.overhead_clearance_m
    obstacles: list[SegmentedPoint] = []
    raised: list[tuple] = []
    # Horizontal range per (channel, column); a missing return reads as far.
    horizontal_range = [scan.max_range_m * cos(elevations[i // columns]) for i in range(channels * columns)]
    ground = low = overhead = returns = 0
    for column in range(columns):
        azimuth = radians(scan.azimuth_start_deg + scan.azimuth_span_deg * column / columns)
        sa, ca = sin(azimuth), cos(azimuth)
        points = []
        for channel in range(channels):
            range_mm = scan.ranges_mm[channel * columns + column]
            if range_mm <= 0:
                continue
            r = range_mm / 1000.
            e = elevations[channel]
            horizontal = r * cos(e)
            horizontal_range[channel * columns + column] = horizontal
            # rho: horizontal distance from the sensor axis along this column;
            # z: height above the ego wheel-contact plane.
            points.append((horizontal, sensor_height + r * sin(e), horizontal * sa, horizontal * ca, channel))
        returns += len(points)
        points.sort(key=lambda p: p[0])
        # Seed: ego contact plane directly beneath the sensor, inclined like the
        # measured wheel contacts along this azimuth.
        previous_rho, previous_z = 0., 0.
        previous_grade = max(-max_grade, min(max_grade,
            scan.ground_grade_forward * ca + scan.ground_grade_lateral * sa))
        for rho, z, x, y, channel in points:
            gap = max(0., rho - previous_rho)
            expected = previous_z + previous_grade * gap
            residual = z - expected
            if z > ceiling and residual >= config.min_obstacle_height_m:
                overhead += 1
                continue
            if abs(residual) <= config.ground_tolerance_m + change * gap:
                ground += 1
                if residual >= config.min_obstacle_height_m:
                    # Accepted only through the gap allowance: a single sparse
                    # ring cannot tell a grade change from a low object here.
                    raised.append((channel, column, rho, x, y, residual))
                if gap > .3:
                    previous_grade = max(-max_grade, min(max_grade, (z - previous_z) / gap))
                previous_rho, previous_z = rho, z
                continue
            if residual < config.min_obstacle_height_m:
                low += 1  # Small bump or a drop below ground; never authorises a stop here.
                continue
            obstacles.append(SegmentedPoint(scan.sensor_lateral_m + x, scan.sensor_forward_m + y, residual))
    # Second cue across columns: terrain is laterally continuous, while an
    # object occludes the same ring in neighbouring columns (range-image edge).
    for channel, column, rho, x, y, residual in raised:
        jump = max(config.edge_jump_min_m, config.edge_jump_ratio * rho)
        for offset in range(1, config.edge_window_columns + 1):
            if any(horizontal_range[channel * columns + (column + side * offset) % columns] - rho > jump
                   for side in (-1, 1)):
                ground -= 1
                obstacles.append(SegmentedPoint(scan.sensor_lateral_m + x, scan.sensor_forward_m + y, residual))
                break
    return Segmentation(tuple(obstacles), ground, low, overhead, returns)
