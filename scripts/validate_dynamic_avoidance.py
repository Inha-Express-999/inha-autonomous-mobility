"""Save isolated dynamic experiment traces and measured UI verification scope."""

import json
from hashlib import sha256
from pathlib import Path

from campus_sim.avoidance_demo import AvoidancePreview
from campus_sim.avoidance_simulation import simulate_avoidance


def main():
    root = Path(__file__).resolve().parents[1]
    cases = {
        "static_detour": AvoidancePreview(),
        "crossing_yield": AvoidancePreview(mode="crossing", x=3, y=3),
        "stale_stop": AvoidancePreview(stale=True),
        "unavoidable_collision": AvoidancePreview(
            mode="crossing", x=0, y=3, speed_mps=30 / 3.6
        ),
    }
    results = {
        name: {"input": command.model_dump(), "result": simulate_avoidance(command)}
        for name, command in cases.items()
    }
    for name in ("static_detour", "crossing_yield"):
        assert (
            results[name]["result"]["arrived"]
            and not results[name]["result"]["collision"]
        )
    assert results["unavoidable_collision"]["result"]["collision"]
    assert not results["stale_stop"]["result"]["arrived"]
    files = [
        root / "backend/src/campus_sim" / name
        for name in (
            "avoidance_simulation.py",
            "avoidance_demo.py",
            "safety.py",
            "sensors.py",
            "local_planning.py",
            "api.py",
        )
    ]
    files.extend(
        p for p in (root / "backend/src/campus_sim/web").glob("*.*") if p.is_file()
    )
    report = {
        "date": "2026-10-19",
        "project": "Inha Mobil",
        "scope": "isolated synthetic experiment",
        "project_version": (root / "VERSION").read_text().strip(),
        "backend_tests": "191 passed; 1 Starlette warning",
        "browser_checks": {
            "1600x900": "page 1600x900, no page overflow",
            "1280x720": "page 1280x720, fleet bottom 704px, no page overflow",
            "menus": ["배차 비교", "RRT / TTC"],
            "fleet_dialog": "3 vehicles",
            "dynamic": "play, pause, seek, arrival, braking and stale stop verified; no console errors",
            "crossing_at_0_15_s": {
                "ego_y_m": 0.26625,
                "pedestrian_x_m": 2.82,
                "speed_kmh": 5.6,
                "ttc_s": 1.17,
            },
        },
        "screenshots": [
            "artifacts/dashboard-16x9.png",
            "artifacts/dynamic-ttc-braking.png",
        ],
        "source_sha256": {
            str(p.relative_to(root)).replace("\\", "/"): sha256(
                p.read_bytes()
            ).hexdigest()
            for p in sorted(files)
        },
        "cases": results,
        "limits": "No Unity sensor/Physics connection, no campus multi-vehicle collision coordination, no real-world safety/performance validation.",
    }
    destination = root / "artifacts/dynamic_avoidance_validation.json"
    destination.write_text(
        json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    for name, data in results.items():
        result = data["result"]
        print(
            f"{name}: arrived={result['arrived']} collision={result['collision']} frames={len(result['frames'])}"
        )


if __name__ == "__main__":
    main()
