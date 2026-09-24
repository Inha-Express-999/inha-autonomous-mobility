from __future__ import annotations

import argparse
import json

from campus_sim.domain import ServiceType
from campus_sim.evaluation import all_ordered_pairs, compare_routes
from campus_sim.planning import node_for_stop
from campus_sim.road_graph import RoadGraphLoadError, load_road_graph


def main() -> None:
    parser = argparse.ArgumentParser(prog="campus-sim")
    subcommands = parser.add_subparsers(dest="command", required=True)
    serve = subcommands.add_parser("serve", help="Run the local development API")
    serve.add_argument("--host", default="127.0.0.1")
    serve.add_argument("--port", type=int, default=8765)
    compare = subcommands.add_parser(
        "route-compare", help="Compare Dijkstra and A* on a versioned road graph"
    )
    compare.add_argument("--map", default="maps/fixtures/campus-synthetic-6.json")
    compare.add_argument("--start-stop")
    compare.add_argument("--goal-stop")
    compare.add_argument(
        "--service-type", choices=[item.value for item in ServiceType], default="PASSENGER"
    )
    compare.add_argument("--vehicle-class", default="CAMPUS_SHUTTLE")
    compare.add_argument("--vehicle-width-m", type=float)
    compare.add_argument("--require-step-free", action="store_true")
    args = parser.parse_args()
    if args.command == "serve":
        import uvicorn

        uvicorn.run("campus_sim.api:app", host=args.host, port=args.port, reload=False)
    elif args.command == "route-compare":
        if bool(args.start_stop) != bool(args.goal_stop):
            parser.error("--start-stop and --goal-stop must be supplied together")
        try:
            graph = load_road_graph(args.map)
            if args.start_stop:
                pairs = [
                    (
                        node_for_stop(graph, args.start_stop),
                        node_for_stop(graph, args.goal_stop),
                    )
                ]
            else:
                pairs = all_ordered_pairs([node.id for node in graph.nodes])
            report = compare_routes(
                graph,
                pairs,
                service_type=ServiceType(args.service_type),
                vehicle_class=args.vehicle_class,
                vehicle_width_m=args.vehicle_width_m,
                requires_step_free=args.require_step_free,
            )
        except (RoadGraphLoadError, ValueError) as error:
            parser.error(str(error))
        print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
