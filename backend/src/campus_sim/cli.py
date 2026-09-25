from __future__ import annotations

import argparse
import json
from pathlib import Path

from campus_sim.domain import ServiceType
from campus_sim.evaluation import (
    all_ordered_pairs,
    compare_dispatch_assignments,
    compare_routes,
)
from campus_sim.planning import node_for_stop
from campus_sim.road_graph import RoadGraphLoadError, load_road_graph


def _positive_integer(value: str) -> int:
    try:
        parsed = int(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError("must be an integer") from error
    if parsed < 1:
        raise argparse.ArgumentTypeError("must be at least 1")
    return parsed


def main() -> None:
    parser = argparse.ArgumentParser(prog="campus-sim")
    subcommands = parser.add_subparsers(dest="command", required=True)
    serve = subcommands.add_parser("serve", help="Run the local development API")
    serve.add_argument("--host", default="127.0.0.1")
    serve.add_argument("--port", type=int, default=8765)
    serve.add_argument("--map", default="maps/fixtures/campus-synthetic-6.json")
    compare = subcommands.add_parser(
        "route-compare", help="Compare Dijkstra and A* on a versioned road graph"
    )
    compare.add_argument("--map", default="maps/fixtures/campus-synthetic-6.json")
    compare.add_argument("--start-stop")
    compare.add_argument("--goal-stop")
    compare.add_argument("--pair-count", type=_positive_integer)
    compare.add_argument("--repetitions", type=_positive_integer, default=1)
    compare.add_argument("--warmups", type=int, default=0)
    compare.add_argument(
        "--service-type", choices=[item.value for item in ServiceType], default="PASSENGER"
    )
    compare.add_argument("--vehicle-class", default="CAMPUS_SHUTTLE")
    compare.add_argument("--vehicle-width-m", type=float)
    compare.add_argument("--require-step-free", action="store_true")
    dispatch_compare = subcommands.add_parser(
        "dispatch-compare", help="Compare Greedy and Hungarian on a synthetic cost snapshot"
    )
    dispatch_compare.add_argument(
        "--scenario", default="configs/dispatch_benchmark.json"
    )
    dispatch_compare.add_argument("--repetitions", type=_positive_integer, default=20)
    dispatch_compare.add_argument("--warmups", type=int, default=2)
    args = parser.parse_args()
    if args.command == "serve":
        import uvicorn

        from campus_sim.api import create_app

        try:
            service_app = create_app(map_path=args.map)
        except (RoadGraphLoadError, ValueError) as error:
            parser.error(str(error))
        uvicorn.run(service_app, host=args.host, port=args.port, reload=False)
    elif args.command == "route-compare":
        if bool(args.start_stop) != bool(args.goal_stop):
            parser.error("--start-stop and --goal-stop must be supplied together")
        if args.start_stop and args.pair_count is not None:
            parser.error("--pair-count cannot be combined with --start-stop/--goal-stop")
        if args.warmups < 0:
            parser.error("--warmups must not be negative")
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
                pairs = all_ordered_pairs(sorted(node.id for node in graph.nodes))
                if args.pair_count is not None:
                    if args.pair_count > len(pairs):
                        parser.error(
                            f"--pair-count {args.pair_count} exceeds the "
                            f"{len(pairs)} available ordered node pairs"
                        )
                    pairs = pairs[:args.pair_count]
            report = compare_routes(
                graph,
                pairs,
                service_type=ServiceType(args.service_type),
                vehicle_class=args.vehicle_class,
                vehicle_width_m=args.vehicle_width_m,
                requires_step_free=args.require_step_free,
                repetitions=args.repetitions,
                warmups=args.warmups,
            )
        except (RoadGraphLoadError, ValueError) as error:
            parser.error(str(error))
        print(json.dumps(report, ensure_ascii=False, indent=2))
    elif args.command == "dispatch-compare":
        if args.warmups < 0:
            parser.error("--warmups must not be negative")
        try:
            scenario = json.loads(Path(args.scenario).read_text(encoding="utf-8"))
            report = compare_dispatch_assignments(
                scenario,
                repetitions=args.repetitions,
                warmups=args.warmups,
            )
        except (OSError, json.JSONDecodeError, TypeError, ValueError) as error:
            parser.error(str(error))
        print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
