"""Seeded stop-turn-translate RRT candidate planner; never grants driving authority."""
import math
import random
from dataclasses import dataclass
from itertools import pairwise

from campus_sim.swept_geometry import BoxFootprint, Pose2, swept_box_hits_disc


@dataclass(frozen=True)
class DiscObstacle:
    x: float
    y: float
    radius_m: float

    def __post_init__(self):
        if not all(math.isfinite(v) for v in (self.x, self.y, self.radius_m)) or self.radius_m < 0:
            raise ValueError("Obstacle must be finite with nonnegative radius")


@dataclass(frozen=True)
class RectCorridor:
    min_x: float
    min_y: float
    max_x: float
    max_y: float

    def __post_init__(self):
        if (not all(math.isfinite(v) for v in (self.min_x, self.min_y, self.max_x, self.max_y))
                or self.min_x >= self.max_x or self.min_y >= self.max_y):
            raise ValueError("Corridor must be finite and nonempty")

    def contains(self, pose, radius):
        return (self.min_x + radius <= pose.x <= self.max_x - radius
                and self.min_y + radius <= pose.y <= self.max_y - radius)


def transition_clear(start, end, footprint, corridor, obstacles, margin_m=0):
    if not math.isfinite(margin_m) or margin_m < 0:
        raise ValueError("Margin must be finite and nonnegative")
    # Convex shrunken corridor bounds the whole center segment and every yaw.
    radius = footprint.radius_m + margin_m
    return (corridor.contains(start, radius) and corridor.contains(end, radius)
            and not any(swept_box_hits_disc(start, end, footprint, (o.x, o.y), (o.x, o.y),
                                           o.radius_m, margin_m=margin_m) for o in obstacles))


def plan_rrt(start: Pose2, goal: Pose2, *, footprint: BoxFootprint, corridor: RectCorridor,
             obstacles: tuple[DiscObstacle, ...], seed: int, iterations: int = 1500,
             step_m: float = 0.5, goal_bias: float = 0.1, margin_m: float = 0.1
             ) -> tuple[Pose2, ...] | None:
    """Return fully swept-checked pose stages, or None; no unchecked fallback.

    Obstacles are static bounds from map/perception, not hidden actors. Dynamic
    prediction, visibility coverage, stale-plan rejection, route rejoin and live
    control arbitration must be handled before this candidate can be executed.
    """
    if (isinstance(iterations, bool) or not isinstance(iterations, int) or iterations < 1
            or not all(math.isfinite(v) for v in (step_m, goal_bias, margin_m))
            or step_m <= 0 or not 0 <= goal_bias <= 1 or margin_m < 0):
        raise ValueError("Invalid RRT limits")
    if not all(transition_clear(p, p, footprint, corridor, obstacles, margin_m)
               for p in (start, goal)):
        return None
    rng = random.Random(seed)
    nodes, parents, stages = [start], [-1], [()]

    def connect(a, b):
        heading = math.atan2(b.x - a.x, b.y - a.y) if (a.x, a.y) != (b.x, b.y) else a.heading_rad
        rotate = Pose2(a.x, a.y, heading)
        move = Pose2(b.x, b.y, heading)
        chain = (a, rotate, move, b)
        if all(transition_clear(p, q, footprint, corridor, obstacles, margin_m)
               for p, q in pairwise(chain)):
            return chain[1:]
        return None

    for _ in range(iterations):
        target = goal if rng.random() < goal_bias else Pose2(
            rng.uniform(corridor.min_x, corridor.max_x),
            rng.uniform(corridor.min_y, corridor.max_y), 0)
        parent = min(range(len(nodes)), key=lambda i: math.hypot(nodes[i].x - target.x,
                                                                nodes[i].y - target.y))
        a = nodes[parent]
        distance = math.hypot(target.x - a.x, target.y - a.y)
        if distance == 0:
            candidate = goal
        else:
            fraction = min(1, step_m / distance)
            candidate = Pose2(a.x + (target.x - a.x) * fraction,
                              a.y + (target.y - a.y) * fraction,
                              math.atan2(target.x - a.x, target.y - a.y))
        chain = connect(a, candidate)
        if chain is None:
            continue
        nodes.append(candidate)
        parents.append(parent)
        stages.append(chain)
        if math.hypot(candidate.x - goal.x, candidate.y - goal.y) > step_m:
            continue
        final = connect(candidate, goal)
        if final is None:
            continue
        chunks = [final]
        index = len(nodes) - 1
        while index > 0:
            chunks.append(stages[index])
            index = parents[index]
        result = [start]
        for chunk in reversed(chunks):
            for pose in chunk:
                if pose != result[-1]:
                    result.append(pose)
        return tuple(result)
    return None
