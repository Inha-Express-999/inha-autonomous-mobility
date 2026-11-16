"""Check recorded Unity evidence; this does not run Unity or simulate arrivals."""
import json
import math
import re
from pathlib import Path

folder = Path(__file__).resolve().parent


def read(name):
    return json.loads((folder / name).read_text(encoding="utf-8-sig"))


stages = [read(f"{name}-state.json") for name in
          ("01-driving", "02-held", "03-restored", "04-completed-home")]
final = read("latest-state.json")
ego = read("latest-observation.json")
requests = [state["requests"][0] for state in stages]
request_id = requests[0]["id"]
vehicle_id = requests[0]["vehicle_id"]
vehicles = [next(v for v in state["vehicles"] if v["id"] == vehicle_id)
            for state in stages]
vehicle = next(v for v in final["vehicles"] if v["id"] == vehicle_id)
observations = (folder / "observations.txt").read_text(encoding="utf-8-sig")
timeline = (folder / "timeline.txt").read_text(encoding="utf-8-sig")
result = (folder / "result.txt").read_text(encoding="utf-8-sig")
goal = vehicle["route_unity"][-1]
goal_distance = math.hypot(ego["x"] - goal["x"], ego["z"] - goal["z"])
home = {"V01": "main_gate", "V02": "rear_gate", "V03": "west_field_parking"}[vehicle_id]
# The connection-restored snapshot can precede the next server mission update.
# Require a subsequent moving sample on the same request/dropoff leg, with a
# new mission and connection generation; do not equate socket open with resume.
resumed_samples = []
for line in timeline.splitlines():
    fields = dict(re.findall(r"(\w+)=([^\s]+)", line))
    if (fields.get("request") == request_id and fields.get("vehicle") == vehicle_id
            and fields.get("status") == "IN_TRANSIT"
            and fields.get("phase") == "TO_DROPOFF/TO_DROPOFF"
            and int(fields.get("generation", "0")) >= 2
            and float(fields.get("speed", "0")) > 1):
        resumed_samples.append(fields)
checks = {
    "driver_finished": result.startswith("PASS:"),
    "one_request_at_all_snapshots": all(len(s["requests"]) == 1 for s in stages + [final]),
    "same_run": len({s["run_id"] for s in stages + [final]}) == 1,
    "same_request_and_vehicle": all(r["id"] == request_id and r["vehicle_id"] == vehicle_id
                                    for r in requests + final["requests"]),
    "in_transit_before_during_after_disconnect": all(r["status"] == "IN_TRANSIT" for r in requests[:3]),
    "same_dropoff_leg": all(v["transport_phase"] == "TO_DROPOFF" for v in vehicles[:3]),
    "held_two_connections": bool(re.search(r"02-held .*count=2 / 3", observations)),
    "restored_generation_two": bool(re.search(r"03-restored .*count=3 / 3.*generation=2", observations)),
    "new_mission_after_reconnect": any(s.get("mission") and s["mission"] != vehicles[0]["mission_id"] for s in resumed_samples),
    "resumed_in_transit": bool(resumed_samples),
    "dropoff_observed": "status=DROPOFF_SERVICE" in observations,
    "completed_once": final["completed"] == 1 and final["requests"][0]["status"] == "COMPLETED" and not final["queue"],
    "home_reached_and_released": vehicle["destination"] == home and vehicle["phase"] in ("READY", "CHARGING") and
        not vehicle["transport_phase"] and not vehicle["request_id"] and goal_distance <= .65,
    "stopped_and_fresh": abs(ego["speed_mps"]) < .05 and vehicle["observation_fresh"],
}
summary = {
    "result": "PASS" if all(checks.values()) else "FAIL",
    "scope": "One actual Unity Editor in-transit disconnect/recovery run; offline evidence checks",
    "run_id": final["run_id"], "request_id": request_id, "vehicle_id": vehicle_id,
    "source_commit": "69ef457c90a6e99cf2b243643fb260a8decdca26", "dirty_worktree": True,
    "geometry_hash": ego["geometry_hash"], "repetitions": 1,
    "terminal_phase": vehicle["phase"], "home": home,
    "final_goal_distance_m": goal_distance, "final_speed_mps": ego["speed_mps"],
    "first_resumed_sample": resumed_samples[0] if resumed_samples else None,
    "checks": checks,
}
(folder / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(summary, ensure_ascii=False, indent=2))
raise SystemExit(0 if all(checks.values()) else 1)

