"""Validate saved snapshots; does not run Unity or measure braking."""
import json
from pathlib import Path
p = Path(__file__).resolve().parent
def read(name):
    return json.loads((p / name).read_text(encoding="utf-8"))
before = read("latest-before-stop.json")
stopped = read("stopped-checkpoint.json")
after = read("restarted.json")
home = read("home-return.json")
rid = read("request.json")["request"]["id"]
def request(s):
    return next(r for r in s["requests"] if r["id"] == rid)
vehicle_id = stopped["requests"][rid]["vehicle_id"]
def vehicle(s):
    return next(v for v in s["vehicles"] if v["id"] == vehicle_id)
checks = {
    "stopped_in_transit": stopped["requests"][rid]["status"] == "IN_TRANSIT" and stopped["jobs"][rid]["phase"] == "TO_DROPOFF",
    "new_run": before["run_id"] != after["run_id"],
    "same_vehicle": request(after)["vehicle_id"] == vehicle_id,
    "restored_in_transit": request(after)["status"] == "IN_TRANSIT",
    "new_mission": bool(vehicle(after)["mission_id"]) and vehicle(after)["mission_id"] != stopped["jobs"][rid]["mission_id"],
    "fresh_and_moving": vehicle(after)["observation_fresh"] and abs(vehicle(after)["measured_speed_kmh"]) > 3,
    "completed_once": request(home)["status"] == "COMPLETED" and home["completed"] == 1 and len(home["requests"]) == 1,
    "home_released": vehicle(home)["phase"] in ("READY", "CHARGING") and not vehicle(home).get("transport_phase") and not vehicle(home).get("request_id"),
    "home_stopped_fresh": vehicle(home)["observation_fresh"] and abs(vehicle(home)["measured_speed_kmh"]) < .18,
}
print(json.dumps(checks, indent=2))
if not all(checks.values()):
    raise SystemExit("FAIL")
(p / "checks.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
print("PASS")
