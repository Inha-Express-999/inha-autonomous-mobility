"""Check observations of a server crash; a verified loss is not service recovery."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent


def read(name):
    return json.loads((root / name).read_text(encoding="utf-8-sig"))


before = read("01-driving-state.json")
after = read("04-after-restart-state.json")
ego = read("latest-observation.json")
request = before["requests"][0]
vehicle = next(v for v in after["vehicles"] if v["id"] == request["vehicle_id"])
events = (root / "events.txt").read_text(encoding="utf-8-sig").splitlines()
stages = (root / "observations.txt").read_text(encoding="utf-8-sig").splitlines()
kill = next(x for x in events if x.startswith("kill_owned_tree "))
restart = next(x for x in events if x.startswith("restart "))
stopped = next(x for x in stages if x.startswith("02-server-down-stopped "))
connected = next(x for x in stages if x.startswith("03-new-server-connected "))
final = next(x for x in stages if x.startswith("04-after-restart "))


def number(line, key):
    return float(re.search(rf"\b{key}=(-?[\d.]+)", line)[1])


checks = {
    "interrupted_in_transit": request["status"] == "IN_TRANSIT" and number(kill, "speed") > 1,
    "stopped_before_restart": abs(number(stopped, "speed")) < .05 and number(stopped, "elapsed") < number(restart, "elapsed"),
    "new_server_run": before["run_id"] != after["run_id"],
    "fresh_observation_after_restart": vehicle["observation_fresh"] and ego["run_id"] == after["run_id"],
    "old_request_absent": not any(r["id"] == request["id"] for r in after["requests"]),
    "no_restored_mission": not vehicle["mission_id"] and not vehicle["transport_phase"] and not vehicle["request_id"],
    "vehicle_still_stopped_at_final_observation": abs(ego["speed_mps"]) < .05,
    "not_falsely_completed": after["completed"] == 0,
}
summary = {
    "evidence_verified": all(checks.values()),
    "service_recovery": "NOT_RESTORED_REQUEST_LOST",
    "source_commit": "69ef457c90a6e99cf2b243643fb260a8decdca26",
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
