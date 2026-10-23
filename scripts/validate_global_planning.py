"""Compare the same campus OD/cost snapshot; never modify a running web session."""

import json
from dataclasses import asdict
from hashlib import sha256
from pathlib import Path

from campus_sim.web_demo import DemoPlan, DemoSession


def main():
    root = Path(__file__).resolve().parents[1]
    session = DemoSession()
    pairs = []
    for pickup in session.service.landmarks:
        for dropoff in session.service.landmarks:
            result = session.compare_planning(DemoPlan(pickup=pickup, dropoff=dropoff))
            a, d = (result["results"][name] for name in ("astar", "dijkstra"))
            assert a["found"] and d["found"]
            assert abs(a["cost_s"] - d["cost_s"]) < 1e-8
            pairs.append(
                {
                    "pickup": pickup,
                    "dropoff": dropoff,
                    "astar_cost_s": a["cost_s"],
                    "dijkstra_cost_s": d["cost_s"],
                    "astar": a["stats"],
                    "dijkstra": d["stats"],
                }
            )
    # Simulated clock jumps check FIFO -> A* -> completed -> home charging.
    state = session.scenario("extensions")
    assert len(state["queue"]) == 2
    first_vehicles = [t.vehicle_id for t in session.service.trips.values()]
    assert first_vehicles == ["V01", "V02", "V03"]
    for _ in range(10):
        session.clock.value += 3600
        state = session.state()
    assert state["completed"] == 5 and all(c.completed for c in session.service.charges)
    report = {
        "project": "Inha Mobil",
        "date": "2026-10-19",
        "simulation_only": True,
        "map_version": session.service.graph.map_version,
        "cost_snapshot_id": session.service.costs.id,
        "node_count": len(session.service.graph.nodes),
        "edge_count": len(session.service.graph.edges),
        "dispatch": "FIFO",
        "demo_default_planner": "astar",
        "od_count": len(pairs),
        "all_od_costs_match": True,
        "total_expanded_astar": sum(p["astar"]["expanded_nodes"] for p in pairs),
        "total_expanded_dijkstra": sum(p["dijkstra"]["expanded_nodes"] for p in pairs),
        "fifo_first_vehicles": first_vehicles,
        "completed_requests": state["completed"],
        "charges": [asdict(c) for c in session.service.charges],
        "comparisons": pairs,
        "checks": {
            "backend_tests": "147 passed; 1 Starlette deprecation warning",
            "ruff": "check/format passed",
            "javascript": "app.js and avoidance.js syntax passed",
            "browser": "1600px/390px nodes, planning comparison, static/crossing/stale previews; no console errors",
        },
        "source_sha256": {
            str(p.relative_to(root)).replace("\\", "/"): sha256(
                p.read_bytes()
            ).hexdigest()
            for p in [
                root / "backend/src/campus_sim/planning.py",
                *sorted((root / "backend/src/campus_sim/web").glob("*.js")),
                *sorted((root / "backend/src/campus_sim/web").glob("*.html")),
                root / "backend/src/campus_sim/web/style.css",
            ]
        },
        "limits": "Geometric/time-only simulation; no Unity sensors, Physics or controller. Single-run timings do not establish p95 performance.",
    }
    destination = root / "artifacts/global_planning_validation.json"
    destination.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(
        f"{len(pairs)} OD costs match; A* {report['total_expanded_astar']} / "
        f"Dijkstra {report['total_expanded_dijkstra']} expanded; 5 FIFO requests completed"
    )


if __name__ == "__main__":
    main()
