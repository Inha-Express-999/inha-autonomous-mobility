"""Synthetic travel-time simulation, not Unity Physics or verified campus data."""

from dataclasses import dataclass

from campus_sim.planning import (
    CostSnapshot,
    DijkstraPlanner,
    NoPath,
    RoadEdge,
    RoadGraph,
    RoutePlan,
)


@dataclass
class VirtualTrip:
    request_id: str
    vehicle_id: str
    home_node: str
    departed_at_s: float
    pickup_route: RoutePlan
    delivery_route: RoutePlan
    return_route: RoutePlan
    returned: bool = False
    energy_by_leg_wh: tuple[float, float, float] = (0.0, 0.0, 0.0)

    def consumed_at(self, now_s: float) -> float:
        """Uniform progress within each route leg; not a physical energy trace."""
        elapsed = max(0.0, now_s - self.departed_at_s)
        total = 0.0
        for route, energy in zip(
            (self.pickup_route, self.delivery_route, self.return_route), self.energy_by_leg_wh
        ):
            fraction = min(1.0, elapsed / route.cost_s) if route.cost_s > 0 else 1.0
            total += energy * fraction
            elapsed = max(0.0, elapsed - route.cost_s)
        return total

    @property
    def pickup_at_s(self) -> float:
        return self.departed_at_s + self.pickup_route.cost_s

    @property
    def dropoff_at_s(self) -> float:
        return self.pickup_at_s + self.delivery_route.cost_s

    @property
    def returns_at_s(self) -> float:
        return self.dropoff_at_s + self.return_route.cost_s


def plan_round_trip(
    graph: RoadGraph,
    costs: CostSnapshot,
    home: str,
    pickup: str,
    dropoff: str,
) -> tuple[RoutePlan, RoutePlan, RoutePlan] | None:
    planner = DijkstraPlanner()
    routes = []
    for start, goal in ((home, pickup), (pickup, dropoff), (dropoff, home)):
        route = planner.search(graph, start, goal, costs)
        if isinstance(route, NoPath):
            return None
        routes.append(route)
    return tuple(routes)


def synthetic_road_graph() -> RoadGraph:
    """Six invented metric nodes. IDs do not assert actual campus coordinates."""
    nodes = {
        "main_gate": (0.0, 0.0),
        "library": (40.0, 0.0),
        "hitech": (80.0, 0.0),
        "synthetic_4": (80.0, 40.0),
        "synthetic_5": (40.0, 40.0),
        "synthetic_6": (0.0, 40.0),
    }
    ids = list(nodes)
    edges = []
    for u, v in zip(ids, ids[1:] + ids[:1]):
        for a, b, speed in ((u, v, 2.0), (v, u, 1.5)):
            edges.append(RoadEdge(a, b, "road", (nodes[a], nodes[b]), 40.0, speed, 3.0))
    return RoadGraph("synthetic-six-stops-v1", nodes, tuple(edges))
