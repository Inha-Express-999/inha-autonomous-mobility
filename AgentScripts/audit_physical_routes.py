"""Audit mission planning between lane arrival poses; no Physics pass claim.

Run with backend/src and the project's Python dependencies on PYTHONPATH.
Arrival poses are route endpoint/tangent predictions, not measured localization.
"""
import argparse
import json
import math
from pathlib import Path
from time import perf_counter

from campus_sim.unity_drive import AutoMission, EgoObservation
from campus_sim.web_demo import DemoSession


def audit(protocol=3):
    session = DemoSession()
    physical = session.physical
    token = physical.connect("V01")
    vehicle = physical.vehicles["V01"]
    pose = EgoObservation(schema_version=protocol, run_id=session.run_id,
        geometry_hash=physical.geometry_hash, tick=0, x=70.0667, z=-159.6858,
        heading_rad=math.radians(-60), speed_mps=0, wheelbase_m=1.299999,
        max_steering_rad=.5235988, half_width_m=.46001, sensor_valid=True,
        sensor_age_s=0, obstacle_detection_enabled=False, rays=[],
        **(dict(rear_axle_offset_m=-.65,half_length_m=1.55) if protocol==4 else {}))
    origins = {"spawn": pose}
    rows = []
    stops = sorted(session.service.stops)
    tick = 0
    # First establish predicted arrival poses from the existing scene spawn.
    for origin in ["spawn", *stops]:
        if origin not in origins:
            continue
        for destination in stops:
            if destination == origin:
                continue
            tick += 1
            vehicle.ego = origins[origin].model_copy(update={"tick": tick})
            vehicle.received_s = physical.clock()
            started = perf_counter()
            row = dict(origin=origin, destination=destination)
            try:
                physical.mission("V01", AutoMission(run_id=session.run_id,
                    command_id=f"audit-{tick}", stop_id=destination,
                    driving_profile="campus_fast_no_obstacles", cruise_speed_kmh=50))
                points = vehicle.route_unity
                row.update(accepted=True, length_m=sum(math.dist(a,b) for a,b in zip(points,points[1:])),
                           lane_count=len(vehicle.lane_ids), requires_turn=vehicle.turn is not None)
                if origin == "spawn":
                    a,b = points[-2:]
                    origins[destination] = pose.model_copy(update={
                        "x": b[0], "z": b[1], "heading_rad": math.atan2(b[0]-a[0], b[1]-a[1])})
            except ValueError as error:
                row.update(accepted=False, reason=str(error))
            row["planning_ms"] = (perf_counter()-started)*1000
            rows.append(row)
    return dict(scope="Offline mission planning, predicted lane endpoint poses; not Unity Physics or dispatch validation",
                protocol=protocol,
                vehicle_geometry_source="artifacts/high_speed_drive/turnaround-measure.json (MCP V01 Collider measurement)",
                half_width_m=pose.half_width_m,
                geometry_hash=physical.geometry_hash, lane_profile_hash=physical.lanes.hash,
                missing_origins=[s for s in stops if s not in origins],
                accepted=sum(r["accepted"] for r in rows), total=len(rows), routes=rows)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--protocol",type=int,choices=(3,4),default=3)
    args = parser.parse_args()
    result = audit(args.protocol)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({k:v for k,v in result.items() if k != "routes"},ensure_ascii=False))
