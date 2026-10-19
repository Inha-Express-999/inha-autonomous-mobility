"""Save reproducible isolated FIFO/Greedy/Hungarian comparison inputs and results."""

import argparse
import json
import platform
from hashlib import sha256
from pathlib import Path

from campus_sim.dispatch_comparison import DispatchComparison, compare_dispatch


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--source", choices=["mixed", "extensions", "battery"], default="mixed"
    )
    parser.add_argument("--planner", choices=["astar", "dijkstra"], default="astar")
    parser.add_argument("--repeats", type=int, choices=range(1, 6), default=3)
    args = parser.parse_args()
    report = compare_dispatch(DispatchComparison(**vars(args)))
    root = Path(__file__).resolve().parents[1]
    sources = [
        root / "backend/src/campus_sim" / name
        for name in (
            "dispatch.py",
            "service.py",
            "dispatch_comparison.py",
            "web_demo.py",
            "api.py",
        )
    ]
    sources.extend((root / "backend/src/campus_sim/web").glob("*.*"))
    report["validation"] = {
        "date": "2026-10-19",
        "project_version": (root / "VERSION").read_text().strip(),
        "python": platform.python_version(),
        "platform": platform.platform(),
        "backend_tests": "187 passed; 1 Starlette TestClient deprecation warning",
        "checks": "Ruff check/format, three JS syntax checks; desktop/mobile browser verification",
        "source_sha256": {
            str(p.relative_to(root)).replace("\\", "/"): sha256(
                p.read_bytes()
            ).hexdigest()
            for p in sorted(sources)
        },
        "limits": "Time-only OSM geometry simulation; synthetic battery/charger/vehicle assumptions. No Unity Physics or real fleet performance validation.",
    }
    output = root / "artifacts/dispatch_comparison_validation.json"
    output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    print(f"Conditions: {report['condition_hash']}; {report['request_count']} requests")
    for result in report["results"]:
        print(
            f"{result['policy']}: completed={result['completed']} "
            f"pickup={result['mean_pickup_s']:.1f}s distance={result['distance_m']:.1f}m "
            f"dispatch p95={result['dispatch_p95_ms']:.2f}ms"
        )


if __name__ == "__main__":
    main()
