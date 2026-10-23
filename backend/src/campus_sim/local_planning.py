"""Seeded geometric RRT candidate planner, never a vehicle control authorization.

Accepts an explicit convex drivable corridor and sensor observations only.
The caller must supply validated road boundaries; OSM centre lines alone do
not establish road width. A successful candidate still needs heading,
time-parameterization, dynamic collision checks and independent safety control.
"""

from dataclasses import dataclass
from math import hypot, isfinite
from random import Random
from time import perf_counter

from campus_sim.sensors import ObservationContext, SensorObservation, observation_error

Point = tuple[float, float]


def segment_distance(point: Point, start: Point, end: Point) -> float:
    dx, dy = end[0] - start[0], end[1] - start[1]
    squared = dx * dx + dy * dy
    t = (
        0
        if squared == 0
        else max(0, min(1, ((point[0] - start[0]) * dx + (point[1] - start[1]) * dy) / squared))
    )
    return hypot(point[0] - start[0] - t * dx, point[1] - start[1] - t * dy)


@dataclass(frozen=True)
class ConvexCorridor:
    """Counterclockwise convex boundary in the same ego-local frame as detections."""

    vertices: tuple[Point, ...]

    def __post_init__(self) -> None:
        if len(self.vertices) < 3 or any(not isfinite(v) for p in self.vertices for v in p):
            raise ValueError("finite convex polygon required")
        edges = tuple(zip(self.vertices, self.vertices[1:] + self.vertices[:1]))
        for a, b in edges:
            if hypot(b[0] - a[0], b[1] - a[1]) <= 1e-9:
                raise ValueError("zero-length corridor edge")
            for p in self.vertices:
                if (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) < -1e-9:
                    raise ValueError("corridor must be convex and counterclockwise")
        area = sum(a[0] * b[1] - b[0] * a[1] for a, b in edges)
        if area <= 1e-9:
            raise ValueError("corridor must have positive area")

    def contains(self, point: Point, clearance: float) -> bool:
        for a, b in zip(self.vertices, self.vertices[1:] + self.vertices[:1]):
            dx, dy = b[0] - a[0], b[1] - a[1]
            if dx * (point[1] - a[1]) - dy * (point[0] - a[0]) < clearance * hypot(dx, dy):
                return False
        return True


@dataclass(frozen=True)
class RrtResult:
    status: str
    path: tuple[Point, ...]
    iterations: int
    tree_edges: tuple[tuple[Point, Point], ...]
    elapsed_s: float
    # Explicitly prevent consumers treating a geometric path as a driving command.
    executable: bool = False


def plan_rrt(
    start: Point,
    goal: Point,
    corridor: ConvexCorridor,
    frames: tuple[SensorObservation, ...],
    context: ObservationContext,
    *,
    ego_radius_m: float,
    margin_m: float = 0.3,
    step_m: float = 0.5,
    goal_bias: float = 0.1,
    max_iterations: int = 1500,
    budget_s: float = 0.05,
    seed: int = 0,
) -> RrtResult:
    """Full segment clearance against inflated hit circles and corridor walls.

    Dynamic or unknown-motion objects cannot be handled safely by this static
    baseline. They return WAIT_DYNAMIC_VALIDATION rather than an executable route.
    """
    if (
        any(
            not isfinite(v)
            for v in (*start, *goal, ego_radius_m, margin_m, step_m, goal_bias, budget_s)
        )
        or ego_radius_m <= 0
        or margin_m < 0
        or step_m <= 0
        or budget_s <= 0
        or not 0 <= goal_bias <= 1
        or type(max_iterations) is not int
        or max_iterations < 1
    ):
        raise ValueError("invalid RRT configuration")
    began = perf_counter()
    nodes, parents, tree = [start], [-1], []

    def result(status: str, path: tuple[Point, ...] = (), iterations: int = 0) -> RrtResult:
        elapsed = perf_counter() - began
        if status == "CANDIDATE" and elapsed >= budget_s:
            status, path = "TIME_BUDGET", ()
        return RrtResult(status, path, iterations, tuple(tree), elapsed)

    error = observation_error(frames, context)
    if error:
        return result(error)
    hits = tuple(hit for frame in frames for hit in frame.detections)
    if any(hit.entity_class != "static" for hit in hits):
        return result("WAIT_DYNAMIC_VALIDATION")
    clearance = ego_radius_m + margin_m

    def free(a: Point, b: Point) -> bool:
        return (
            corridor.contains(a, clearance)
            and corridor.contains(b, clearance)
            and all(
                segment_distance(hit.position_m, a, b) > clearance + hit.radius_m for hit in hits
            )
        )

    if not free(start, start) or not free(goal, goal):
        return result("INVALID_ENDPOINT")
    if free(start, goal):
        return result("CANDIDATE", (start, goal))
    rng = Random(seed)
    min_x, max_x = min(p[0] for p in corridor.vertices), max(p[0] for p in corridor.vertices)
    min_y, max_y = min(p[1] for p in corridor.vertices), max(p[1] for p in corridor.vertices)
    for iteration in range(1, max_iterations + 1):
        if perf_counter() - began >= budget_s:
            return result("TIME_BUDGET", iterations=iteration - 1)
        sample = (
            goal
            if rng.random() < goal_bias
            else (rng.uniform(min_x, max_x), rng.uniform(min_y, max_y))
        )
        nearest = min(
            range(len(nodes)), key=lambda i: hypot(sample[0] - nodes[i][0], sample[1] - nodes[i][1])
        )
        a = nodes[nearest]
        distance = hypot(sample[0] - a[0], sample[1] - a[1])
        if distance < 1e-12:
            continue
        fraction = min(1.0, step_m / distance)
        b = (a[0] + fraction * (sample[0] - a[0]), a[1] + fraction * (sample[1] - a[1]))
        if not free(a, b):
            continue
        nodes.append(b)
        parents.append(nearest)
        tree.append((a, b))
        if free(b, goal):
            path = [goal]
            index = len(nodes) - 1
            while index >= 0:
                path.append(nodes[index])
                index = parents[index]
            if perf_counter() - began >= budget_s:
                return result("TIME_BUDGET", iterations=iteration)
            return result("CANDIDATE", tuple(reversed(path)), iteration)
    return result("NO_PATH_WITHIN_BUDGET", iterations=max_iterations)
