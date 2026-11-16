"""Verify one recorded Unity process-restart recovery and completed trip."""
import json
import math
import re
from pathlib import Path

root = Path(__file__).resolve().parent


def read(name):
    return json.loads((root / name).read_text(encoding="utf-8-sig"))


before = read("01-driving-state.json")
after = read("04-completed-home-state.json")
ego = read("latest-observation.json")
request = before["requests"][0]
vehicle = next(v for v in after["vehicles"] if v["id"] == request["vehicle_id"])
events = (root / "events.txt").read_text(encoding="utf-8-sig").splitlines()
stages = (root / "observations.txt").read_text(encoding="utf-8-sig").splitlines()
kill = next(x for x in events if x.startswith("kill_owned_tree "))
restart = next(x for x in events if x.startswith("restart "))
stopped = next(x for x in stages if x.startswith("02-server-down-stopped "))
connected = next(x for x in stages if x.startswith("03-new-server-connected "))
final = next(x for x in stages if x.startswith("04-completed-home "))


def number(line, key):
    return float(re.search(rf"\b{key}=(-?[\d.]+)", line)[1])


timeline = (root / "restart-timeline.txt").read_text(encoding="utf-8-sig")
old_vehicle = next(v for v in before["vehicles"] if v["id"] == request["vehicle_id"])
resumed = []
for line in timeline.splitlines():
    fields = dict(re.findall(r"(\w+)=([^\s]+)", line))
    if (fields.get("run") == after["run_id"] and fields.get("request") == request["id"]
            and fields.get("status") == "IN_TRANSIT" and float(fields.get("speed", "0")) > 1
            and fields.get("mission") and fields["mission"] != old_vehicle["mission_id"]):
        resumed.append(fields)
goal = vehicle["route_unity"][-1]
goal_distance = math.hypot(ego["x"]-goal["x"], ego["z"]-goal["z"])
checks = {
    "interrupted_in_transit": request["status"] == "IN_TRANSIT" and number(kill, "speed") > 1,
    "stopped_before_restart": abs(number(stopped, "speed")) < .05 and number(stopped, "elapsed") < number(restart, "elapsed"),
    "new_server_run": before["run_id"] != after["run_id"],
    "same_request_vehicle": len(after["requests"]) == 1 and after["requests"][0]["id"] == request["id"] and after["requests"][0]["vehicle_id"] == request["vehicle_id"],
    "fresh_observation_after_restart": vehicle["observation_fresh"] and ego["run_id"] == after["run_id"],
    "resumed_with_new_mission": bool(resumed),
    "completed_once": after["completed"] == 1 and after["requests"][0]["status"] == "COMPLETED",
    "home_and_reservation_released": vehicle["destination"] == "main_gate" and vehicle["phase"] == "CHARGING" and not vehicle["transport_phase"] and not vehicle["request_id"],
    "within_home_tolerance": goal_distance <= .65,
    "vehicle_stopped_at_final_observation": abs(ego["speed_mps"]) < .05,
    "driver_completed": (root / "result.txt").read_text(encoding="utf-8-sig").startswith("PASS:"),
}

summary = {
    "evidence_verified": all(checks.values()),
    "dirty_worktree": True,
    "final_goal_distance_m": goal_distance,
    "service_recovery": "RESTORED_AND_COMPLETED" if all(checks.values()) else "CHECK_FAILED",
    "source_commit": "69ef457c90a6e99cf2b243643fb260a8decdca26",
    "first_resumed_sample": resumed[0] if resumed else None,
    "repetitions": 1, "vehicle_id": request["vehicle_id"], "request_id": request["id"],
    "old_run": before["run_id"], "new_run": after["run_id"],
    "speed_at_kill_mps": number(kill, "speed"),
    "stop_observed_after_kill_s": round(number(stopped, "elapsed") - number(kill, "elapsed"), 2),
    "new_run_connection_observed_after_restart_s": round(number(connected, "elapsed") - number(restart, "elapsed"), 2),
    "final_observation_after_connection_s": round(number(final, "elapsed") - number(connected, "elapsed"), 2),
    "checks": checks,
}
(root / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(summary, ensure_ascii=False, indent=2))
raise SystemExit(0 if all(checks.values()) else 1)
