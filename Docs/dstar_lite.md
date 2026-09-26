# D* Lite global replanning — synthetic integration

2026-09-26 · project v0.3.2.0

## Implementation and service use

The own implementation follows the basic D* Lite structure described by
[Koenig and Likhachev (AAAI 2002)](https://idm-lab.org/bib/abstracts/Koen02e.html):
goal-rooted g/rhs values, lexicographic queue keys, start-motion key offset,
and predecessor repair after directed edge changes. No external planner is used
at runtime. Dijkstra and A* serve as independent search oracles in the tests.

`DStarLite` owns a copied fixed graph and goal. `replan` accepts a moved start and
replacement cost/exclusion snapshots. `None` retains the previous snapshot; an
explicit empty dict/set restores base costs/reopens dynamic exclusions. Immutable
graph restrictions (closed source edges, vehicle class/width, service type and
step-free requirements) remain enforced. Exclusions are infinite/unavailable,
never a large but traversable penalty. Directed parallel edges retain their IDs.
Input validation and hard constraints are shared with A*/Dijkstra.

Set `global_algorithm` in `configs/planning.json` to `dstar_lite` to select it for
service route planning. The default remains `astar`. The existing base → bounded
AVOID detour → penalty fallback policy is preserved; CLOSED edges remain excluded.
The service stores separate search states by goal/service/accessibility/policy
phase, caps them at 64 entries, and resets all cached states when the graph content
hash changes (including version/topology/static constraints). Rejected updates do
not partially mutate a valid search. Heap tombstones are compacted after repair.

This is synchronous synthetic global planning. No new Unity run, dynamic-world
perception, measured campus map, full service-load budget or M3 completion is claimed.

## Reproducible comparison

```
python -m campus_sim.cli replan-compare --repetitions 20 --events 30
```

The default 11-node synthetic graph receives seeded penalties, partial closure,
all-edge closure, reopening and moved starts. Both algorithms receive identical
snapshots; Dijkstra checks every route cost and unreachable result outside timing.
Whole-call wall/CPU timing includes validation and cold D* construction. Algorithm
order alternates between repetitions. No warmups or claim of universal speedup.

Recorded 600 event evaluations per algorithm, seed 17:

| Metric | A* full replan | D* Lite |
|---|---:|---:|
| Wall p50 | 0.0432 ms | 0.0958 ms |
| Wall p95 | 0.0587 ms | 0.1815 ms |
| No-route results | 140 | 140 |
| Route changes | 400 | 400 |

All costs/no-route results matched. This small fixture favored A* in wall time.
Raw expanded/updated counts are recorded, but A* expansion counts on failed
searches are unavailable and cannot be compared as complete totals. Windows CPU
clock quantization and host conditions limit interpretation. Timing measures the
planner call, not network event-to-vehicle-response latency.

Evidence: `artifacts/validation/2026-09-26-dstar-lite/`. Tests cover all 121 ordered
node pairs including identity routes under four hard-constraint profiles, 300
seeded change sequences, reopening after disconnection, parallel edges, invalid
updates and service crowd/closure parity with cache invalidation. Real campus
graphs, automatic observed crowd state, larger-scale performance and complete
integrated T24/E3 qualification remain pending.
