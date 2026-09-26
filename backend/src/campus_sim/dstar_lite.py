"""Own D* Lite for fixed directed topology and changing cost/exclusion snapshots.

Algorithm reference: Koenig & Likhachev, AAAI 2002 (basic D* Lite).
https://idm-lab.org/bib/abstracts/papers/aaai02b.pdf
"""
import heapq
import itertools
import math
from time import perf_counter_ns

from campus_sim.domain import ServiceType
from campus_sim.planning import NoRouteError, RouteResult, prepare_routing_snapshot
from campus_sim.road_graph import RoadGraphDocument


class DStarLite:
    def __init__(self, graph: RoadGraphDocument, start_node: str, goal_node: str, *,
                 service_type: ServiceType = ServiceType.PASSENGER,
                 vehicle_class: str = "CAMPUS_SHUTTLE", vehicle_width_m: float | None = None,
                 requires_step_free: bool = False):
        # A fixed private graph prevents external mutation from corrupting reused state.
        self.graph = graph.model_copy(deep=True)
        self.constraints = {"service_type": service_type, "vehicle_class": vehicle_class,
                            "vehicle_width_m": vehicle_width_m, "requires_step_free": requires_step_free}
        self.nodes, self.outgoing = prepare_routing_snapshot(
            self.graph, start_node, goal_node, **self.constraints)
        self.start, self.goal = start_node, goal_node
        self.edges = {e.id: e for e in self.graph.edges}
        self.predecessors = {n: set() for n in self.nodes}
        for edges in self.outgoing.values():
            for edge in edges:
                self.predecessors[edge.to_node].add(edge.from_node)
        self.max_speed = max((e.allowed_speed_mps for e in self.graph.edges), default=1)
        self.costs = {e.id: e.cost_s() for edges in self.outgoing.values() for e in edges}
        self.overrides, self.excluded = {}, frozenset()
        self.g = dict.fromkeys(self.nodes, math.inf)
        self.rhs = dict.fromkeys(self.nodes, math.inf)
        self.rhs[self.goal] = 0
        self.km = 0.0
        self.heap, self.queued = [], {}
        self.counter = itertools.count()
        self.updated_nodes = 0
        self.expanded_nodes = 0
        self.generated_nodes = 0
        self.peak_open_set_size = 0
        self._queue(self.goal)

    def _heuristic(self, a, b):
        a, b = self.nodes[a].position_m, self.nodes[b].position_m
        return math.hypot(a.x - b.x, a.y - b.y) / self.max_speed

    def _key(self, node):
        best = min(self.g[node], self.rhs[node])
        return (best + self._heuristic(self.start, node) + self.km, best)

    def _queue(self, node):
        item = (*self._key(node), next(self.counter), node)
        self.queued[node] = item
        heapq.heappush(self.heap, item)
        self.generated_nodes += 1
        self.peak_open_set_size = max(self.peak_open_set_size, len(self.queued))

    def _top(self):
        while self.heap and self.queued.get(self.heap[0][3]) != self.heap[0]:
            heapq.heappop(self.heap)
        return self.heap[0][:2] if self.heap else (math.inf, math.inf)

    def _update(self, node):
        self.updated_nodes += 1
        if node != self.goal:
            self.rhs[node] = min((self.costs.get(e.id, math.inf) + self.g[e.to_node]
                                  for e in self.outgoing[node]), default=math.inf)
        self.queued.pop(node, None)
        if self.g[node] != self.rhs[node]:
            self._queue(node)

    def replan(self, start_node: str | None = None, *, edge_costs_s=None,
               excluded_edge_ids=None) -> RouteResult:
        """None retains the last snapshot; explicit empty dict/set restores base.

        A new goal, topology or hard-constraint profile requires a new instance.
        Metrics include validation, updates, repair and route extraction per call.
        """
        started = perf_counter_ns()
        start = self.start if start_node is None else start_node
        overrides = self.overrides if edge_costs_s is None else dict(edge_costs_s)
        excluded = self.excluded if excluded_edge_ids is None else frozenset(excluded_edge_ids)
        _, allowed = prepare_routing_snapshot(self.graph, start, self.goal,
                                               edge_costs_s=overrides, excluded_edge_ids=excluded,
                                               **self.constraints)
        new_costs = {e.id: overrides.get(e.id, e.cost_s()) for edges in allowed.values() for e in edges}
        affected = {self.edges[e].from_node for e in self.costs.keys() | new_costs.keys()
                    if self.costs.get(e, math.inf) != new_costs.get(e, math.inf)}
        self.updated_nodes = self.expanded_nodes = self.generated_nodes = 0
        self.peak_open_set_size = len(self.queued)
        self.km += self._heuristic(self.start, start)
        self.start = start
        self.costs, self.overrides, self.excluded = new_costs, dict(overrides), excluded
        for node in sorted(affected):
            self._update(node)
        while self._top() < self._key(self.start) or self.rhs[self.start] != self.g[self.start]:
            if not self.heap:
                break
            item = heapq.heappop(self.heap)
            node, old_key = item[3], item[:2]
            self.queued.pop(node, None)
            if old_key < self._key(node):
                self._queue(node)
            elif self.g[node] > self.rhs[node]:
                self.g[node] = self.rhs[node]
                self.expanded_nodes += 1
                for predecessor in sorted(self.predecessors[node]):
                    self._update(predecessor)
            else:
                self.g[node] = math.inf
                self.expanded_nodes += 1
                self._update(node)
                for predecessor in sorted(self.predecessors[node]):
                    self._update(predecessor)
        # Compact lazy tombstones so repeated events cannot grow storage without bound.
        if len(self.heap) > 2 * max(1, len(self.nodes)):
            self.heap = list(self.queued.values())
            heapq.heapify(self.heap)
        if not math.isfinite(self.g[self.start]):
            raise NoRouteError(f"no D* Lite route from {self.start} to {self.goal}")
        nodes, route = [self.start], []
        while nodes[-1] != self.goal:
            current = nodes[-1]
            candidates = [e for e in self.outgoing[current]
                          if math.isfinite(self.costs.get(e.id, math.inf) + self.g[e.to_node])]
            if not candidates:
                raise NoRouteError("D* Lite route has no finite successor")
            edge = min(candidates, key=lambda e: (self.costs[e.id] + self.g[e.to_node], e.id))
            if edge.to_node in nodes:
                raise NoRouteError("D* Lite extraction encountered a cycle")
            route.append(edge)
            nodes.append(edge.to_node)
        return RouteResult("dstar_lite", tuple(nodes), tuple(self.nodes[n].stop_id for n in nodes),
                           tuple(e.id for e in route), sum(self.costs[e.id] for e in route),
                           sum(e.length_m for e in route), self.expanded_nodes, self.generated_nodes,
                           self.peak_open_set_size, perf_counter_ns() - started)
