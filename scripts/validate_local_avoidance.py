"""Reproducible synthetic sensor/RRT preview; does not modify a running demo."""

import json
from dataclasses import asdict
from pathlib import Path

from campus_sim.local_planning import ConvexCorridor, plan_rrt
from campus_sim.safety import assess_safety
from campus_sim.sensors import Detection, ObservationContext, SensorObservation


def main() -> None:
    context = ObservationContext(
        vehicle_id="V01",
        sensors={"lidar": "LIDAR", "radar": "RADAR"},
        required_sensor_ids=frozenset({"lidar", "radar"}),
        current_tick=100,
        pose_version=7,
        tick_duration_s=0.02,
    )
    obstacle = Detection(
        position_m=(0, 5), range_m=5, radius_m=0.7, entity_class="static"
    )
    frames = tuple(
        SensorObservation(
            vehicle_id="V01",
            sensor_id=sensor,
            sensor_type=kind,
            observed_tick=100,
            pose_version=7,
            valid=True,
            detections=(obstacle,) if sensor == "lidar" else (),
        )
        for sensor, kind in (("lidar", "LIDAR"), ("radar", "RADAR"))
    )
    corridor = ConvexCorridor(((-4, -2), (4, -2), (4, 12), (-4, 12)))
    # Safety is assessed independently before the geometric search.
    safety = assess_safety(frames, context, speed_mps=2, ego_radius_m=0.5)
    result = plan_rrt(
        (0, 0),
        (0, 10),
        corridor,
        frames,
        context,
        ego_radius_m=0.5,
        budget_s=2,
        seed=12,
    )
    assert safety.action == "STOP" and result.status == "CANDIDATE"
    output = Path(__file__).resolve().parents[1] / "artifacts"
    report = {
        "scenario": "synthetic_static_sensor_rrt_seed12",
        "assumptions": {
            "corridor_width_m": 8,
            "ego_radius_m": 0.5,
            "margin_m": 0.3,
            "obstacle_radius_m": 0.7,
            "geometry_is_campus_road": False,
        },
        "sensor_context": context.model_dump(mode="json"),
        "sensor_frames": [frame.model_dump(mode="json") for frame in frames],
        "safety": asdict(safety),
        "rrt": asdict(result),
        "unity_or_web_connected": False,
        "limitations": "Static geometry candidate only; no heading, controller, dynamic trajectory or sensor physics validation.",
    }
    output.mkdir(exist_ok=True)
    (output / "local_avoidance_validation.json").write_text(
        json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8"
    )

    def xy(point):
        return f"{280 + point[0] * 32:.2f},{470 - point[1] * 32:.2f}"

    tree = "".join(
        f'<polyline points="{xy(a)} {xy(b)}"/>' for a, b in result.tree_edges
    )
    path = " ".join(xy(p) for p in result.path)
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="760" height="610" viewBox="0 0 760 610">
<rect width="760" height="610" fill="#F3F6FC"/>
<g font-family="sans-serif" fill="#10244A"><text x="28" y="36" font-size="22">Sensor-based RRT candidate</text>
<text x="28" y="62" font-size="13">Synthetic corridor / seed 12 / not a campus driving demonstration</text></g>
<rect x="152" y="86" width="256" height="448" fill="white" stroke="#8CA1BA"/>
<rect x="177.6" y="111.6" width="204.8" height="396.8" fill="none" stroke="#B5C7DE" stroke-dasharray="5 4"/>
<g stroke="#C4D8EF" fill="none" stroke-width="1">{tree}</g>
<circle cx="280" cy="310" r="48" fill="#FFE5C8"/>
<circle cx="280" cy="310" r="22.4" fill="#E66B53"/>
<polyline points="280,470 280,150" fill="none" stroke="#A5AFBF" stroke-dasharray="6 5"/>
<polyline points="{path}" fill="none" stroke="#1677FF" stroke-width="3"/>
<g fill="#10244A" font-family="sans-serif" font-size="14">
<text x="430" y="140">Gray: blocked reference</text><text x="430" y="172">Light blue: RRT search tree</text>
<text x="430" y="204">Blue: geometric candidate</text><text x="430" y="236">Orange: inflated obstacle</text>
<text x="430" y="300">Safety: {safety.action}</text><text x="430" y="330">Candidate executable: false</text>
<text x="430" y="380">Start (0, 0) / Goal (0, 10)</text><text x="430" y="410">Ego enclosing radius: 0.5 m</text>
<text x="430" y="440">Clearance margin: 0.3 m</text><text x="28" y="580">A detour candidate never overrides the independent STOP assessment.</text></g></svg>'''
    (output / "local_avoidance_preview.svg").write_text(svg, encoding="utf-8")
    print(f"{result.status}; safety={safety.action}; executable={result.executable}")


if __name__ == "__main__":
    main()
